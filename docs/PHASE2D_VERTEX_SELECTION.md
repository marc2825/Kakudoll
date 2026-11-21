# Phase 2D-1 — Vertex Picking / Selection / Soft Selection Foundation

## 1. Scope

Phase 2D-1は、Phase 2Cのneutral/rest Preview上で編集対象meshを選び、元mesh頂点を
screen-spaceから選択し、soft-selection weightを可視化するEditor-only foundationである。
この段階ではmove gizmo、preview deformation、`vertexDelta` commit、Save、drag Undo、
2D Warpを実装しない。

選択、tool、半径、falloff、Visible Only / Select Throughは一時Editor session stateであり、
`VlatAuthoringDocument.Revision`、dirty、document Undo、`.vlat.json/.bin`を変更しない。

## 2. Ownership and data flow

```text
VlatEditorWindow (UI/input adapter)
        |
        v
VlatPreviewController (Preview lifetime / neutral editing mode)
        |
        v
VlatVertexSelectionSession (target cache / picking / soft weights / overlay)
        |
        +-- preview-clone Renderer / source Mesh (read only)
        +-- Main Camera projection (read only)
        +-- Editor-owned BakeMesh / arrays / overlay Texture2D
```

`VlatVertexSelectionSession`はPreviewControllerが所有し、avatar replace/clear、target change、
window close、assembly reloadで破棄する。Windowは座標・modifier・tool commandだけを渡し、
topology、projection、visibility、selection algorithmを持たない。

## 3. Target and persistent vertex identity

対象候補はtemporary avatar配下の`SkinnedMeshRenderer`を優先し、`MeshRenderer + MeshFilter`も
補助対象とする。UIにはrenderer hierarchy path、mesh名、頂点数、VLat meshBinding解決状態を
表示する。名前のhard-codeは行わない。

選択identityは常に次の組で表す。

```text
(resolved VLat meshBinding index, original source mesh vertex index)
```

VLat bindingがないAvatarOnly状態でも選択foundationを確認できるよう、targetはrenderer identityを
保持し、binding indexを`-1`（unbound）として明示する。この状態の選択を将来commitしてはならない。
bindingが解決できる場合はPhase 1 `UniVrmAdapter.Resolve`の結果を再利用し、独自rebind規則を
追加しない。

`SkinnedMeshRenderer.BakeMesh`はneutral/rest状態の画面上の頂点位置だけに使用する。
BakeMeshが返すvertex配列の要素順はsource `sharedMesh`のindexと1:1であることを毎回
`vertexCount`一致で検証し、異なる場合は対象を編集不能として扱う。選択identityをbaked meshの
一時indexやtriangle corner indexへ変換しない。

## 4. Neutral editing pose and Expression state

Geometry Selection Modeへ入るとき、現在のExpression input weightsを一時配列へstashし、
全weightを0へして適用する。mode中はExpression controlを無効にする。modeを抜けるときはstashを
復元して再評価する。avatar replace/clearではstashを破棄する。

既存VLat `After` stateも同時にstashし、mode中はapplicationを`Before`へ固定して、camera角度に
よって既存vertexDeltaが変化しないsource neutral geometryを表示・選択する。退出時は元の
Before/After stateを復元する。これによりcamera orbit/pan/zoomはprojectionだけをinvalidateし、
geometry/index cacheを再構築しない。

Phase 2C contractを維持し、animation、control rig、Constraint、LookAt、spring simulationを
評価しない。`Vrm10Instance.UpdateType=None`を維持し、`Vrm10Instance.Runtime`および
`Vrm10Runtime.Process()`を使わない。VLat angle evaluationのBefore/Afterは既存Preview contractを
維持するが、頂点選択のscreen positionはその時点のneutral preview clone geometryから得る。

## 5. Geometry and projection caches

target変更またはneutral geometry変更時だけ、Editor-owned baked meshへskin/blendshape結果を取得し、
次をcacheする。

- original vertex index順のtarget-local position;
- triangle index列;
- source mesh topologyから構築した重複なしadjacency;
- world positionとMain Camera screen position;
- screen-space depth bufferと各頂点のvisibility;
- selected flagとsoft-selection weight。

Camera orbit/pan/zoom/resizeではsource verticesとadjacencyを再構築せず、world-to-screen projection、
visibility、overlayだけを更新する。pointer move中に`mesh.vertices`、LINQ、per-vertex GameObject、
文字列生成を行わない。計測値としてgeometry build、projection/visibility、selection query、
soft-selectionを別々に保持する。Brush drag中はhard-selectionとbatched overlayだけを更新し、
geodesic再計算はgesture終了時に1回行う。

## 6. Visibility strategy

Phase 2D-1の`Visible Only`は、preview cloneの実triangle geometryをMain Cameraへ投影し、
CPUでscreen-space z-bufferをrasterizeした結果と頂点depthを比較する。targetだけでなくavatar内の
有効なMesh/SkinnedMesh Rendererもoccluder cacheへ含める。これは法線向きによる近似や
per-vertex Physics raycastではなく、surface depthによる遮蔽判定である。

候補を比較した結果は次の通り。

| Candidate | Decision | Reason |
|---|---|---|
| GPU ID/depth pass + synchronous readback | 不採用 | pointer queryごとの同期readbackがEditor操作をstallさせる |
| GPU ID/depth pass + async readback | 将来候補 | render/material/callback lifetimeがD1の規模を越え、gesture開始時の世代管理が必要 |
| Physics raycast | 不採用 | collider生成、skin追従、per-vertex rayが重く、render surfaceと一致しない |
| CPU triangle z-buffer cache | 採用 | event駆動で決定的、実triangle遮蔽を扱い、テスト可能でsource assetを変更しない |

depth bufferはMain viewのpixel sizeに追従する。ただし過大viewportでのコストを抑えるため長辺を
最大1024 pixelに縮小し、projection/query双方を同一scaleへ写像する。triangle内部はperspective
correctな逆depth補間を用いる。両面geometryをoccluderとし、透明/cutout materialのalpha判定は
Phase 2D-1では行わない。この制約はUI diagnosticではなく本設計上の既知制約とする。

`Select Through`はvisibility filterだけを外し、同じprojected vertex cacheを用いる。
同一pixel近傍に複数頂点があるsingle pickは、(1) Visible Onlyならvisible、(2) camera depthが近い、
(3) pointer距離が短い、(4) original indexが小さい、の固定順で決定する。

## 7. Selection semantics

toolはSingle、Brush、Rectangleを持つ。操作開始時にtool、Replace/Add/Subtract、Visible Only /
Select Through、brush radiusをgesture snapshotへ固定し、drag途中のkey releaseやUI変更では
意味を変えない。

- 通常click/drag: Replace;
- Shift: Add;
- CtrlまたはCmd: Subtract;
- Single: 一定pixel半径内の決定的best vertex 1点;
- Brush: pointer pathを一定間隔でsampleし、円内の対象を集合演算;
- Rectangle: down/upで定まる矩形内の対象を集合演算;
- 空所Replace: clear、空所Add/Subtract: no-op。

selectionは`bool[]`でoriginal vertex index順に保持する。入力順、triangle順、HashSet列挙順に
依存しない。

## 8. Soft selection

source mesh triangleからundirected adjacencyを一度構築する。edge costはneutral target-localの
edge lengthとし、選択済み各頂点を距離0とするmulti-source Dijkstraでgeodesic距離を求める。
半径外はweight 0、選択頂点は1とする。falloffは既定の
`smoothstep(1 - distance / radius)`（`t*t*(3-2*t)`）とLinearをEditor session optionとして持つ。
同距離のqueue tieはoriginal vertex index順にし、結果を決定的にする。radius/falloff変更では
topologyを再構築しない。

## 9. Visualization and input coexistence

Main viewの上にEditor-owned transparent `Texture2D`を1枚重ね、screen-space pointをまとめて
描画する。hard-selectedはorange、soft weightはblueからyellowへの連続色、rectangle gestureは
枠で示す。per-vertex GameObject、Label、VisualElementを生成せず、avatar materialも変更しない。

Geometry Selection Mode外はPhase 2Cと同じLeft orbit / Middle pan / Wheel zoom。mode中はLeftを
選択、Alt+Leftをorbit、Middleをpan、Wheelをzoomとする。camera操作はselectionを変更せず、
camera変更後にprojection/visibility/overlayだけをinvalidateする。

## 10. Failure and lifecycle policy

targetにreadable meshがない、BakeMesh vertex countが一致しない、bindingがない場合はEditor UIの
non-fatal diagnosticとし、Avatar Preview/camera/Expressionを維持する。例外、cache破損、resource
生成失敗だけをpreview infrastructure failureとして扱う。

owned baked meshes、temporary occluder meshes、overlay texture、arraysはtarget/avatar/session終了時に
解放する。元VRM、元Mesh、元Material、Prefab、user sceneは変更しない。

## 11. Phase boundary

Phase 2D-1の出力はoriginal vertex index順のhard selectionとsoft weightsまでである。
Phase 2D-2だけが、このsession snapshotを受け取り、coordinate conversion、temporary deformation、
move gizmo、completed gesture 1 Undo、Phase 2A vertexDelta commandへのcommitを行う。
Phase 2D-1からAuthoring Modelへ書き戻す経路は持たせない。

## 12. Seed-san human manual checklist

Automated gate完了時点では、このworkspaceに`Seed-san.vrm`がないため以下は**NOT RUN**。
人間がWindows Unity `2022.3.62f3`で結果を報告するまでPhase 2D-1 human gateをPASSにせず、
Phase 2D-2は開始しない。

- [ ] `Seed-san.vrm`をloadできる。
- [ ] editable renderer一覧にrenderer名、mesh名、vertex count、binding statusが表示される。
- [ ] head、hair等のactual rendererを名前のhard-codeなしでUI選択できる。
- [ ] Geometry Selection中はExpression neutral / VLat Beforeになり、退出後にstateが復元する。
- [ ] Main viewにunselected/core/soft vertex overlayが表示される。
- [ ] Single clickがthreshold内のvertexを選択する。
- [ ] Brush selectionとradius変更が動作する。
- [ ] Rectangle selectionが動作する。
- [ ] Shift gestureがAddになる。
- [ ] Ctrl/Cmd gestureがSubtractになる。
- [ ] Visible Onlyで裏面・遮蔽vertexを選ばない。
- [ ] Select Throughで同じ範囲の裏面vertexも選択できる。
- [ ] soft-selectionが同一surfaceのtopologyに沿って減衰する。
- [ ] Alt+Left orbit後もoverlay/picking位置が一致する。
- [ ] Middle pan後もoverlay/picking位置が一致する。
- [ ] wheel zoom後もoverlay/picking位置が一致する。
- [ ] 0° Main / 45° Sub A / 90° Sub B表示を維持する（編集overlayはMainのみ）。
- [ ] target renderer変更時に旧selectionが安全にclearされる。
- [ ] selection、radius、visibility、camera操作でdocumentがdirtyにならない。
- [ ] avatar clear/reloadでoverlay/duplicate avatarの残留がない。
- [ ] Console Error 0 / Warning 0。

Seed-san対応`.vlat.json/.bin` pairがない場合、binding statusがunbound/rebind requiredでも
AvatarOnlyのvertex selection foundationは確認できる。actual VLat apply / Before-After / VLat +
Expression併用はPhase 1 manual acceptanceのOPEN項目として分離し、Phase 2D-2でactual
`vertexDelta`を生成できる段階のmanual acceptanceと統合する。
