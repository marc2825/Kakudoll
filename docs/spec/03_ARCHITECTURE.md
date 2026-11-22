# 03. VLat システムアーキテクチャ v1.0

## 1. モジュール構成 (asmdef 単位)

```
VLat.Core          … 純C#。データモデル、シリアライズ、バリデーション、
                     角度評価(yaw/pitch/重み/priority解決)。UnityEngine非依存を目指す
                     (Vector3等は自前structかUnityMathematicsで抽象化)
VLat.Runtime       … Unity依存。VLatApplier(MonoBehaviour)、動的ブレンドシェイプ、
                     MaterialPropertyBlock管理、UniVRMアダプタ
VLat.Editor        … Unityエディタ専用。UI Toolkitウィンドウ、編集ツール、
                     Undo統合、PreviewController、再バインドUI
VLat.Bake          … ベイクエクスポータ(Editor専用)
VLat.Tests.Edit    … EditModeテスト (Core中心)
VLat.Tests.Play    … PlayModeテスト (Runtime/ゴールデンイメージ)
```

依存方向: Editor → Runtime → Core、Bake → Runtime。逆依存禁止。
`VLat.Editor`はPhase 2Cのraw VRM 1.0 load境界に限り、固定済みUniVRMの
`VRM10`と`UniGLTF`を直接参照する。VLat評価・binding・modifier適用は
`VLat.Runtime`の`VlatApplier`を経由し、Editor側へ複製しない。

## 2. リポジトリ構成

```
vlat/
├─ Packages/jp.vlat.core/        # UPMパッケージとして配布 (Runtime/, Editor/, Tests/)
├─ VLatProject/                  # 開発用Unityプロジェクト (2022.3.62f3)
│   └─ Assets/Samples/           # サンプルアバター・デモシーン
├─ schema/vlat.schema.json
├─ docs/ (本ドキュメント群 + フォーマット仕様公開版)
├─ .github/workflows/ci.yml      # EditMode/PlayModeテスト + Schema検証
└─ LICENSE (PolyForm Strict 1.0.0)
```

## 3. ランタイム設計

### 3.1 主要クラス
- `VlatDocument` (Core): デシリアライズ済みドキュメント。イミュータブル。
- `AngleEvaluator` (Core): `Evaluate(Vector3 camPosHeadLocal) → BandWeights`。帯重みは
  ステートレスに評価する。visibility のヒステリシス状態は Modifier ごとに
  `VisibilityHysteresis` が保持し、連続値 Modifier と共有しない。
- `ConflictResolver` (Core): Layer×プロパティの占有/ブレンド解決。結果は
  `ResolvedState`（Modifier・実効重み・ターゲットIDの事前確保済みフラット配列と、
  ターゲットごとのベース状態重み）。毎フレームの辞書生成・列挙は行わない。
- `VlatApplier` (Runtime, MonoBehaviour): 毎 `LateUpdate` で
  1. 評価カメラ位置を頭部ローカルへ変換 → yaw/pitch
  2. `AngleEvaluator` → `ConflictResolver`
  3. 差分のみ適用(前フレームの `ResolvedState` と比較し、変化したプロパティだけ書き込む → NFR-1のGC 0B/frame と両立)
- `DynamicBlendShapeBinder` (Runtime): ロード時に vertexDelta を `Mesh.AddBlendShapeFrame` で `VLAT_<layerId>` として追加(元Meshは `Instantiate` した複製に対して行い非破壊を守る)。以降は weight 書き込みのみ。
- `MaterialOverrideBinder` (Runtime): MPB管理。透明モード切替が必要なマテリアルのみロード時にインスタンス複製。

### 3.2 フレーム処理コスト方針
- 毎フレームの処理は「yaw/pitch算出 + 帯重み計算(バンド数分) + 変化プロパティの書込」のみ。O(バンド数+変化Modifier数)。
- メッシュ複製・ブレンドシェイプ追加・マテリアル複製はすべてロード時1回。

## 4. エディタ設計

### 4.1 画面構成 (UI Toolkit)
```
┌─────────────────────────────────────────────────┐
│ Toolbar: New / Open / Save / Save As / Undo / Redo │
├──────────────────┬──────────────────────────────┤
│ Document          │ selected item property editor │
│ Band list + CRUD  │ metadata / Band / Layer /     │
│ Layer list + CRUD │ modifier schema properties    │
│ Modifier + CRUD   │                                │
├──────────────────┴──────────────────────────────┤
│ VRM / angle / Expression controls                 │
│ Main 0°       Sub A 45°       Sub B 90°           │
├─────────────────────────────────────────────────┤
│ validation diagnostics / source・dirty status     │
└─────────────────────────────────────────────────┘
```
- Phase 2B shellへPhase 2CのPreview領域を追加した。VRM lifetime、Camera、RenderTexture、
  runtime評価はWindowではなく`VlatPreviewController`が所有する。Phase 2D-1ではvertex pickingを
  Editor-only selection sessionへ追加したが、gizmo、実変形、2D warp、rebind UIはまだ所有しない。
- Phase 2Cビューポートは`RenderTexture` + `EditorSceneManager.NewPreviewScene`による専用
  Preview Sceneを使う。3 viewは1体のEditor-owned temporary VRM instanceを共有し、
  各Cameraの独立角度を`VlatApplier`へ順次入力してrenderする。

### 4.2 編集データフロー
- on-disk/Runtime側のsingle sourceはimmutable `VlatDocument`とbuffer列、編集中の
  single sourceはEditor-only `VlatAuthoringDocument`とする。Core modelをUIから直接変更しない。
- Load時にbufferViewをtyped authoring dataへdecodeする。vertexDeltaは
  `meshBinding + original vertex index + Vector3 delta`で保持し、編集中のaccessor/offsetや
  `.vlat.bin`直接変更を行わない。
- 編集操作 → command layer → Unity `Undo.RecordObject`の順とし、completed gesture/command
  1回につきtemporary ScriptableObjectへUndo recordを1件作る。binary repackはUndo対象外。
- Save時はauthoring validation → canonical ordering → sizing/alignment → bufferView assignment →
  binary pack → immutable `VlatDocument` → Core validation → JSON+bin atomic saveの共通経路を使う。
- canonical writerは配列順を維持し、各vertexDeltaだけindex昇順へ正規化する。embedded texture、
  layer/modifier順のvertex payloadを4-byte alignmentの単一bufferへpackする。詳細とtrade-offは
  `docs/PHASE2A_FORMAT_AUDIT.md`を参照する。
- Authoring objectは`HideAndDontSave`でProject asset化しない。ownerであるEditor sessionが
  close/replacement/error時に破棄する。通常domain reload前にはEditor-only snapshotを
  `Library/VLat`へ書き、保存済みsourceと未保存差分、または完全な新規未保存documentを復元する。

### 4.3 Editor session / document lifecycle (Phase 2B)
- `VlatEditorWindow`は入力と描画のadapterに限定し、document ownerは
  `VlatEditorDocumentController`とする。Controllerがtemporary Authoring Model、source path、
  save point、selection、diagnostics、recovery lifecycleを所有する。
- dirtyはcommand完了時にAuthoring Modelへ格納する単調revisionと、Controllerだけが保持する
  successful-save revisionの不一致で判定する。毎frameのJSON/binary serializationは行わない。
  Undoで保存点へ戻ればclean、保存点より前へ戻ればdirty、Redoすれば対応revisionから再判定する。
- Band/Layer/ModifierのEditor identityはtemporary Authoring Modelにだけserializeし、format変換と
  on-disk JSONへ出力しない。reorderやUndo/Redoでもselection対象をindexと取り違えない。
- New/Open/Close前のdirty解決はSave/Discard/Cancelを明示する。Open candidateのload失敗、
  validation error、Save Asの書込失敗ではcurrent documentとsource pathを切り替えない。
- recoveryは世代別snapshotを書き終えてflushした後にmanifestをatomic replaceする。
  normal Discard/clean closeでは削除し、source `.vlat.json` / `.vlat.bin`を自動上書きしない。
  詳細は`docs/PHASE2B_EDITOR_SESSION.md`を参照する。

### 4.4 Preview lifecycle / multi-view (Phase 2C)
- `VlatPreviewController`がPreview Scene、temporary VRM、3 Camera、RenderTexture、
  `VlatApplier`、preview用Textureを一括所有する。Window close、assembly reload、avatar
  replace/clear、load失敗、controller dispose/rebuildで対応資源を明示破棄する。
- raw VRMはUniVRM `Vrm10.LoadPathAsync`を`ImmediateCaller`、VRM 0.x migration無効、
  no-op springboneで読み、成功後ただちに`HideAndDontSave`としてPreview Sceneへ移す。
  current Sceneのroot構成とdirty状態を保持し、元VRM/Mesh/Material/Prefabを変更しない。
- Main/Sub A/Sub Bの既定yawは0/45/90度。Camera orbit/pan/zoom/resetとExpression weightは
  preview session stateであり、Authoring revisionや`.vlat` formatへ書かない。
- 現在の未保存`VlatAuthoringDocument`をPhase 2A canonical packerでin-memoryのimmutable
  document/bufferへ変換し、Phase 1 `VlatApplier`へ渡す。一時`.vlat.json/.bin`は作らない。
  3 viewは単一avatarへ各角度を順次適用・renderし、最後にMain角度へ戻す。
- Editor Previewは`VlatApplier`のVRM-agnostic overloadへavatar root、Humanoid head、Cameraを
  明示的に渡す。neutral/restのため`UpdateType=None`にした`Vrm10Instance`をRuntime lifecycle
  overloadへ渡さず、Runtime側の自動更新契約と診断は変更しない。
- Previewのavatar成立とVLat applicabilityを分離する。document未選択・0 Layerは`AvatarOnly`、
  pack不能・binding不一致・有効Layer 0はbaseline avatarを維持した`VLatUnavailable`、初期化成功時
  だけ`AvatarWithVLat`とする。VLat不成立はUIのnon-fatal diagnosticとbinding reportへ保持し、
  VRM load、3 view、camera navigation、Expressionを失敗させない。Save validationは緩和しない。
- refreshはavatar/document revision/Undo・Redo/angle/Expression/Camera/viewport geometryの
  event駆動とし、Editor Updateによる無条件の毎frame評価は行わない。RTはDPI込みsizeが
  変わった場合だけ再生成し、Camera aspectを同期する。
- Preview CameraのGameObject layer maskは全layerを対象にするが、`overrideSceneCullingMask`は
  `EditorSceneManager.GetSceneCullingMask(previewScene)`へ固定する。これによりavatar固有layerを
  hard-codeせず、専用Preview Sceneだけをrenderしてuser/source Sceneを対象外にする。
- neutral基準はimport直後のHumanoid/rest transform、animationなし、Expression weight 0、
  Constraint/LookAt/control rig評価なし、spring simulationなしとする。EditModeでは
  `Vrm10Instance.Runtime`とfull `Vrm10Runtime.Process()`を生成・実行しない。
- Expression keyは`Vrm10Instance.Vrm.Expression.Clips`から列挙する。preview適用はUniVRM公開
  expression validatorとserialized morph/material color/material UV bindingを使うEditor-only
  adapterが担当し、Expression適用後にPhase 1 `VlatApplier`を再構築して既定合成順序を保つ。
  詳細は`docs/PHASE2C_PREVIEW_ARCHITECTURE.md`を参照する。

### 4.5 3D直接編集 (Phase 2D)
- Phase 2D-1の頂点選択は`VlatPreviewController`所有のEditor-only sessionとする。対象は
  SkinnedMeshRendererを主対象とし、identityを`resolved meshBinding + original source vertex index`
  で保持する。selection/tool/soft weightはdocument revision、dirty、Undo、保存形式を変更しない。
- geometry editing中はExpression weightとVLat Before/After stateをstashし、Expression 0かつ
  VLat Beforeのneutral source geometryへ戻し、退出時に復元する。
  animation/control rig/Constraint/LookAt/springを評価せず、Phase 2CのEditMode-safe pathを維持する。
- screen positionはneutral cloneのBakeMesh結果から得るが、BakeMeshのvertex count一致を検証し、
  indexを永続identityへ流用しない。cameraだけの変更ではtopologyを再構築しない。
- Visible Onlyはpreview cloneのtriangleを用いたevent-driven screen-space depth rasterizer、
  Select Throughは同じprojection cacheからvisibility filterだけを外す。法線近似、per-vertex
  Physics raycast、pointer queryごとのGPU同期readbackは用いない。
- Single/Brush/Rectangle、Replace/Add/Subtractをgesture開始時に固定する。soft weightはsource
  triangle adjacency上のdeterministic multi-source geodesic distanceと固定smoothstepで計算する。
- Main viewの可視化は単一のEditor-owned overlay textureへbatchし、per-vertex objectや元material
  変更を行わない。詳細は`docs/PHASE2D_VERTEX_SELECTION.md`を参照する。
- Phase 2D-2でのみ移動gizmo、preview-only変形、確定時1 Undo、Phase 2A共通経路への
  `vertexDelta` commit、任意の近傍normal再計算を追加する。

### 4.6 2Dワープ編集 (Phase 3)
- 現在ビューで対象メッシュ頂点をスクリーン空間へ投影し、深度バッファと比較して可視頂点集合を得る。
- ユーザー操作: キャンバス上にコントロールポイント(ピン+ドラッグハンドル)を配置。
- 変位場: ハンドル変位を RBF(thin-plate) または格子ワープで可視頂点のスクリーン変位に補間。
- 逆投影: 各頂点の元の view-space 深度を固定し、スクリーン変位を同深度平面上の3D変位へ変換。
- 平滑化: 可視/不可視境界の不連続を防ぐため、メッシュグラフ上で Laplacian smoothing を変位ベクトルに数回適用(境界頂点は変位0拘束)。
- 結果は Phase 2 と同一の vertexDelta コミット経路へ流す。**このモジュールが失敗してもPhase 2の編集経路が常に使える**こと。

## 5. ベイク設計 (Phase 4)
- 入力: VlatDocument + 角度リスト(既定: yaw {0, ±45, ±90} × pitch 0)。
- B1: 各角度で ConflictResolver を評価 → vertexDelta 合成 → UniVRM の export 経路で morph target `VLAT_yaw45` 等として追加した VRM を別ファイル出力。
- B2: 各バンドの ResolvedState からマテリアルアセットを生成し `_Baked` サフィックスで出力。
- レポート: ベイクで表現できなかった Modifier(連続遷移・ヒステリシス・renderQueue等)を Markdown で列挙。

## 6. テスト・CI
- EditMode: Core のみで完結(Unityシーン不要)→ 高速。
- PlayMode ゴールデンイメージ: 専用シーンでサンプルアバター+固定角度をレンダリングし、基準PNGと SSIM/画素差比較(閾値はテスト内定数)。基準画像更新はスクリプトで明示的に。
- CI: GitHub Actions + game-ci/unity-test-runner。Unityライセンスは手動アクティベーションのシークレット登録手順を docs に記載。

## 7. リスクと緩和
| リスク | 緩和 |
|--------|------|
| 2D逆投影の品質不足 | Phase分離、3D編集フォールバック、深度固定+Laplacianの保守的仕様 |
| MToon透明切替による描画破綻(ソート) | 透明化はロード時複製+renderQueue明示。ゴールデンイメージで回帰検出 |
| ブレンドシェイプ数上限・メモリ | Layer数上限(既定64)を仕様化。スパース格納 |
| UniVRM API変更 | アダプタ層(`VLat.Runtime.UniVrmAdapter`)に隔離しバージョン固定 |
| エディタのプレビュー複製が重い | 複製はプレビュー用低頻度更新(編集時のみ再評価) |
