# VRC G0 + P0-S1 — Head Frame取得の監査・実測

- 日付: **2026-10-16 JST**。実装ベース: `0691976c7d7a6ba626aa40440f95762d7bca7006`。
- 作業ブランチ: `spike/vrc-head-frame-p0-s1`。開始時worktree clean。pushなし。
- 判定: **Unityの限定S1はPASS、独立したP0-S2実験へ条件付きGO**。
  **VRChat製品採用・G0全体完了・旧Phase 2D-2開始はNO-GO**。
- 実FBX3体の追加測定: **Head角度/行列・全頂点GPU位置はPASS**。
  厳密なsurface-image比較は**20 FAIL**、原因は**INCONCLUSIVE**のまま保持（§12）。
- 推奨: **Bの正準Head子ProxyをrootBoneへ設定する最小方式**を次の実機検証候補にする。
  Constraint版を比較対象に残す。CのMesh変換はHead Frame取得だけには不要だった。
  VRChat実機での動作保証を意味しない。

## 1. 目的とスコープ

毎フレームC#からHead行列を渡さず、各描画カメラの位置をShader内で頭部基準yaw/pitchへ
変換できるかを調べる。`rootBone=Head`から結論を仮定せず、Shaderの実際のObject行列、
Renderer APIの行列、Transform、BindPose、skin結果を比較する。

GPU delta、2D Warp、NDMF/compiler製品実装、lilToon統合、v0.2の設計・実装は含めない。
既存仕様の単一評価カメラ・pre-skin vertexDeltaを変更しない。今回の正面軸補正も診断fixtureに
限定し、v0.1 reader/runtimeの意味論へ書き戻さない。この隔離実験に仕様変更は不要であり、
`DECISIONS_NEEDED.md`への新規追記はない。

## 2. 読んだ文書と既存コードの監査（G0）

`AGENTS.md`、`PROGRESS.md`、spec 00/01/02/03、`PHASE2D_VERTEX_SELECTION.md`を通読した。
strategy 3文書は既存remote ref `origin/docs/vrc-first-direction-2026-10-16`のcommit
`1c96fc2dcc9f095c30b0df8f999c704c73dbd30c`から取得し、内容を変更せず本ブランチにも保存した。
PRの提案を確定仕様として扱っていない。

| 領域 | 現行実装・依存 | 今回の扱い |
| --- | --- | --- |
| `VLat.Core` | UnityEngine非依存、Newtonsoft.Json。Model/Serialization/Validation/AngleEvaluator/ConflictResolver | 再利用資産として維持、変更0 |
| `VLat.Runtime` | Core、VRM10、VRM10.MToon10。VlatApplier、DynamicBlendShapeBinder、MaterialOverrideBinder、UniVrmAdapter | 変更0 |
| `VLat.Editor` | Core/Runtime、raw load境界のみVRM10/UniGLTF。Authoring/Undo/Save、Preview、Selection | 変更0 |
| Tests.Edit / Tests.Play | 前者はCore/Runtime/Editor/VRM10、後者はCore/Runtime/VRM10/PerformanceTesting | asmdef・CI変更0 |
| `VLat.Bake` | architecture上の予定。現時点の実装asmdefなし | 追加しない |
| 独立実験 | Editor-only asmdef、参照配列空、autoReferenced=false。Unityだけ | 本体との依存辺0、UniVRM分離リファクタ不要 |

Head取得は`UniVrmAdapter.TryGetHead(Vrm10Instance)` →
`instance.TryGetBoneTransform(HumanBodyBones.Head)`。`VlatApplier`のVRM overloadはここへ結合し、
VRM更新状態を診断して共通Initializeへ渡す。VRM非依存の`avatarRoot/head/camera` overloadもある。
`EvaluateNow`は`_head.InverseTransformPoint(camera.position)` →
`AngleEvaluator.TryCalculateAngles`、`LateUpdate`（execution order 11010）で評価する。
Coreは`atan2(x,z)`、`asin(y)`、yaw範囲(−180,180]を採用する。実験も同じ数式を用いた。

Editor Previewは1体のtemporary avatarを専用Preview Sceneに所有し、3 Cameraへ逐次CPU評価して
描画する。これは各観測者が同時にShader評価する方式の実証ではない。neutral/rest時は
`UpdateType=None`、`Vrm10Instance.Runtime`やfull `Process()`を作らず、Expression専用adapterを使う。
Selectionは元mesh indexとbindingのidentityを維持し、BakeMeshを投影用に使う。

既存の非破壊境界は、MeshをInstantiateして動的BlendShapeを追加し、元Meshへ復帰して生成物を
破棄する方式。MaterialはMPBを優先し、透明化・renderQueue時だけ複製する。
今回この実装・元アセット・Prefab・ユーザーのVRChatプロジェクトを編集していない。

### G0の未完了部分

- 過去の最終ログ: EditMode **154/154**、PlayMode **4/4** PASS、CPU中央値10.2703125 µs、GC 0。
  **今回の再実行結果ではない**。
- 今回62f3 executableは見つからず、既存Unityテスト・性能/GC再測定は**NOT RUN**。
  22f1やUnity 6で既存`VLatProject`を代替実行していない。
- Phase 2D-1の人間21項目は**NOT RUN / NO-GO**のまま。Phase 2Cの人間完了記録は維持する。
- Phase 1 actual avatar + 対応VLat pairのapply/Before-After/Expression併用は**OPEN**のまま。
- 既存Assets内のVRMと対応VLat pairは0件。ローカルの別VRC projectにSDK/lilToonがあることは
  確認したが、既存ユーザーprojectを実験対象として開かなかった。
- 初回は実アバター3体の調査がNOT RUN。追加で**Shinano/Komano/YumekaのFBX構造監査・
  Head Frame測定を実行した（§12）**。製品PrefabのShader/Material/PhysBoneはNOT RUN、
  具体的な需要分類は利用者指示で延期。NDMF/AAOの現行版・互換性調査も未完了。
  このためG0全体の出口はまだ満たさない。

## 3. 公式資料で確認した仕様（2026-10-16閲覧）

| 根拠 | 確認できる範囲 | ここからは保証できないこと |
| --- | --- | --- |
| [VRChat Current Unity Version](https://creators.vrchat.com/sdk/upgrade/current-unity-version/) | 対応Editorは**2022.3.22f1**。同ページは別版でのuploadを避けるよう指示 | 62f3やUnity 6がVRC用に等価とはいえない |
| [Unity Built-in shader variables](https://docs.unity3d.com/2022.3/Documentation/Manual/SL-UnityShaderVariables.html) | ObjectToWorldはcurrent model matrix、WorldToObjectは逆行列。WorldSpaceCameraPosは描画Camera位置 | SMRのmodel matrixがどのTransformと一致するかは、この説明だけでは未確定 |
| [Unity SMR manual](https://docs.unity3d.com/2022.3/Documentation/Manual/class-SkinnedMeshRenderer.html) | rootBoneはskeleton root、boundsはそのTransformへ追従 | rootBone変更だけで常にHead空間や正しいboundsになる保証はない |
| [Renderer.localToWorldMatrix](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Renderer-localToWorldMatrix.html) | 描画計算ではTransform行列との違いに注意するよう記載 | 今回のSMRではこのAPIとも実Shader行列が異なった。API値だけで判定しない |
| [Mesh.bindposes](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Mesh-bindposes.html) | 配列indexはbonesと対応。例では`bone.worldToLocal * renderer.localToWorld` | rootBoneとbones配列の役割は同一ではない |
| [SMR.BakeMesh](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/SkinnedMeshRenderer.BakeMesh.html) | CPU snapshot。GPU Skinning設定にかかわらずCPU計算、TransformとuseScaleに注意 | BakeMesh成功だけではGPU描画の一致を証明できない |
| [VRChat Allowed Avatar Components](https://creators.vrchat.com/avatars/whitelisted-avatar-components/whitelisted-avatar-components/) | SMR、Transform、Constraint等は許可。任意custom scriptsは使用不可 | VLat MonoBehaviourをuploadして動かす経路にはできない |
| [VRChat Constraints](https://creators.vrchat.com/common-components/constraints/) / [Parent Constraint](https://creators.vrchat.com/common-components/constraints/vrc-parent-constraint/) | VRC Constraintを推奨。Unity Constraintはin-game変換される。Parentは位置・回転追従 | Unity ParentConstraintのEditor成功はVRC変換後の実証ではない。ScaleConstraintとは別 |

上記は文書の記述。以降のUnity実測と、そこからの限定的な推奨を区別する。

## 4. 実験環境

| 項目 | 記録 |
| --- | --- |
| 元プロジェクト | Unity 2022.3.62f3 (96770f904ca7)、Built-in、UniVRM v0.128.3 (b045022e...) |
| 元UPM | Newtonsoft 3.2.1、Test Framework 1.3.7、Performance 3.0.3、Coverage 1.2.6、Timeline 1.7.7、Image Conversion 1.0.0。manifest/lock変更0 |
| 実験Editor | **2022.3.22f1 (887be4894c44)**、別Project、新規Windows TEMP staging |
| 実行OS/GPU | Windows 11 (10.0.26200) 64bit、NVIDIA GeForce RTX 3060 Ti、D3D11 level 11.1 |
| 計測 | Gamma project、Linear ARGBFloat RT 64×64、RGBAFloat readback、MSAAなし、Forward |
| 実験依存 | builtin animation 1.0.0、imageconversion 1.0.0のみ。SDK/UniVRM/NDMF/lilToonなし |
| ローカルVRC環境の読取 | 別projectは22f1、VRC base/avatars 3.10.5、lilToon 2.3.4。インストール済み版であり最新版の断定ではない |
| 実行手段 | WSLからWindows Editor起動。通常sandboxではWSL socket error、隔離stagingとEditor実行の承認後に実行成功 |

`PlayerSettings.gpuSkinning`をfalse/trueの双方で試し、finallyで元へ復元した。
これは**設定の比較**であってEditor内skin計算backendのGPU使用証明ではない。
実Player/VRChat内のskin実行経路は**NOT RUN**。Shaderの行列・角度・world座標readbackは実GPUで実行した。

## 5. 方式比較と結果

共通元meshとbone配列を保持し、7候補を別Renderer・別Mesh・別Materialで生成した。
Rendererのrest位置・回転はHeadと意図的に異なる。rootBoneはbones配列に含まれなくても比較する。
Proxyの正準軸は+Z前、+Y上。異軸fixtureではHead restをEuler(90,0,90)とし、子Proxyへ逆回転を入れる。

以下のPASS/FAILは、正の一様スケールの**合成fixture限定**、各方式224ケースの結果。

| 候補 | 変更 | Unity S1 | 最大角度誤差 | 判定の意味 |
| --- | --- | --- | --- | --- |
| Baseline（負対照） | Renderer/meshを維持、rootBone=Body | **FAIL**、168/224失敗 | 57.41257° | Head回転を追えず、測定が単なるrest一致でないことを確認 |
| A HeadRoot | rootBone=Head、mesh/bindposeを維持 | **PASS**、224/224 | 0.003884° | 異軸ではrest校正が必要。raw Head軸を顔正面と同一視しない |
| B ChildProxyRoot | 正準Head子ProxyをrootBoneにする、mesh/bindpose維持 | **PASS**、224/224 | 0.003874° | 最小の推奨候補。Constraint不要 |
| B ConstraintProxyRoot | Head子の正準marker → avatar直下ProxyをParentConstraintで追従、rootBone=Proxy | **PASS**、224/224 | 0.003874° | Unity Constraintのみ確認。VRC Constraint/Head ChopはNOT RUN |
| B RendererOnly（負対照） | RendererをProxy直下へ移すだけ、rootBone=Body、mesh/bindpose維持 | **FAIL**、168/224失敗 | 57.41257° | Renderer Transform移動だけではHead Frameにならない |
| C RebasedChild | Rendererを正準子Proxy基準へ移し、頂点/BindPose/BlendShapeを整合、rootBone=Proxy | **PASS**、224/224 | 0.003874° | 数学的整合と元skinの保持は確認。Frame取得だけならBで足りる |
| C RebasedConstraint | Cの基準をConstraint Proxyとする | **PASS**、224/224 | 0.003874° | 同上、VRC変換後は未検証 |

**全方式ともVRChatクライアントではNOT RUN**。非一様・負スケールは**INCONCLUSIVE**とし、
対応対象へ含めない。今回のCPU基準との差は候補で非一様最大15.75194°、負スケール180°。
このCPU参照との角度一致試験自体は**FAIL**。CSVのINCONCLUSIVEは対応範囲の採否判定であり、
数値的一致を意味しない。現行方式を無補正で流用できないことは観測済みだが、
代替方式まで不可能と判定したわけではない。

## 6. Shader / Renderer / rootBone / BindPoseの関係

### 数学的に必要な不変量

列ベクトルを使い、`B_i(t)`をboneのworld行列、`P_i`をbindpose、`v`を元mesh位置とすると、
線形skinのworld位置は`sum_i w_i B_i(t) P_i v`。表情deltaはこの`v`へpre-skinで加える。
描画用model行列を`M(t)`とすれば、Shaderへ渡るskin後位置は概念上
`M(t)^-1 sum_i w_i B_i(t) P_i v`であり、world描画時に`M(t)`を戻す。
**rootBoneはbones/weightsを書き換える操作ではない**。描画フレームを変えても、Unityがこの
帳尻を合わせればworld skin結果は同じである。今回その形状保持を別途測った。

Cではrest旧Renderer行列を`R0`、新基準を`F0`、`T=F0^-1 R0`として、
`v'=T v`、`P_i'=P_i T^-1`、`delta'=T_linear delta`を使う。
`B_i P_i' v' = B_i P_i v`が全poseで成立する。元Meshを複製し、頂点、normal、tangent、
全BlendShape frameを変換する。実験converterは**正の剛体Tだけを受理**し、一般Mesh変換器として
非一様scale、鏡映、shearへの対応を主張しない。index/triangles/weightsを変えない。

### 今回実際に観測したObject行列

このEditor/D3D11/fixtureでは、全280行列観測で
**`unity_ObjectToWorld = TRS(rootBone.position, rootBone.rotation, Vector3.one)`**だった。
最大成分差0。HeadやProxyのscaleはこのmodel行列に含まれず、skin後頂点側へ反映されていた。
一般的なUnity全版・全backendの仕様保証へ拡張しない。

一方、`Renderer.localToWorldMatrix`はこのfixtureではRenderer Transformの行列を返し、
Head rootの実Shader行列とは異なった。unit restでも最大成分差1.49。
Renderer API、rootBone full matrix、Shader model matrixの3者を同一視してはならない。
rootのunit-scale TRSとの直接比較、full行列との比較を両方CSVへ記録した。

例えばAのunit restのShader原点はworld `(1.1,1.6,-0.7)`。
Head yaw37°後のX列は`(0.7986355,0,-0.6018150)`、Z列は`(0.6018150,0,0.7986355)`となる。
Renderer Transformはその位置・回転に置かれていないが、描画Object行列はHeadへ追従した。
avatar一様scale2では原点Y=3.2、基底列の長さは1のままだった。

### 校正と角度

restで実Shader行列`M0`をreadbackし、正準Head frame`F0`から`K=F0^-1 M0`を1回だけ求める。
Shaderは`cameraObject = WorldToObject * float4(cameraWorld,1)`、
`cameraCanonical = K * float4(cameraObject,1)`、その方向からyaw/pitchを計算する。
**位置はw=1**で変換し、原点を2度引かない。各頂点からカメラへの方向ではなくHead pivotからの方向を使う。
raw yaw/pitchも記録し、軸補正で何が変わったかを分ける。

KはMaterialの3 Vector propertyへ保存する。pose/viewごとにKを更新せず、Shaderに最新Head行列も
CPU角度も送らない。Unity自身がmodel行列とcamera globalsを供給する。
負対照はrestでは校正できてもHead回転で破綻するので、校正だけで結果を成功にできない。

`F(t)^-1 M(t)`のfull成分driftもCSVに残した。一様scale時はdrift最大1になるが、方向の正規化に
より角度は一致する。**full行列が完全に一定という結論ではない**。
非一様・負scaleではこの扱いは成立せず、別の対応条件・意味論が必要。

### BakeMeshのスケール注意

`BakeMesh(false)`結果を`TRS(renderer.position, renderer.rotation, 1)`とfull Transformの両方で
worldへ復元し、上記の解析skin式と比較した。この実験では前者が正の一様scale条件で一致した。
full Transformを掛けるとscaleを二重適用し、最大3.546435 mの誤差になった。
この結果だけで既存Selectionを修正していない。実Avatar構造で別途再現・監査すべき論点である。

## 7. 実装と実行したテスト

[`experiments/VRCHeadFrame`](../../experiments/VRCHeadFrame/README.md)に独立Project、
診断Shader、生成済み`.unity`、専用Mesh/Material、Editor runner、再現PowerShellを置いた。
全C#はEditor-onlyで、アップロードするavatarへ独自MonoBehaviourを付けない。
自動runnerはPreview Sceneを使い、Editor updateを2回進めてConstraint/skin更新後に描画する。
64×64 float readbackで実Shader行列4列、角度、skin後world位置を取得する。
診断readbackやCSV/配列生成は測定用であり、RuntimeのGC 0やGPU性能のベンチマークではない。

条件は`2 PlayerSettings toggles × 2 Head軸 × 10 poses × 7 views × 7 methods = 1,960`。

- views: world yaw −90/−45/0/45/90、pitch0、追加(45,+30)、(−45,−30)。CameraはHead原点を中心に
  **world空間**で移動し、Headの回転で評価角度が変わるようにした。
- poses: neutral、Head yaw37、Head pitch/yaw/roll、Body yaw63、Neck+Head、Expression100、
  scale0.5/2、非一様(1,1.5,0.7)+Head、負scale(−1,1,1)。
  Expression・正の一様scaleの条件もNeck+Head回転と組み合わせる。
- mesh: 6頂点/8三角形、5頂点Head100%、下端1頂点Neck/Head50:50、通常表情の位置delta。
- CPU参照: 正準Head frameのInverseTransformPointと解析角度。11個の既知方向/原点拒否チェック。
- 形状参照: baselineと各候補の実GPU world位置画像を比較。共通pixelの最大world距離と
  coverage差を測る。別に解析skin式とCPU BakeMeshの全6頂点も比較する。
- 合格目標: yaw/pitch各0.05°以下、共通pixel world誤差0.1 mm以下、coverage差2pixel以下、
  CPU bake rigid誤差0.1 mm以下。fixture検証用の暫定閾値で製品SLAではない。

| 実行した項目 | 結果 |
| --- | --- |
| 22f1でfresh import / C#・Shader compile | **PASS**、compiler CS warning/error 0、Shader error 0 |
| 解析角度11チェック | **PASS** |
| 全1,960ケース | **1,232 PASS / 336 FAIL / 392 INCONCLUSIVE**。FAIL336は全て意図した負対照2方式 |
| A/B/Cの正の一様scale条件 | **5候補 × 224/224 PASS** |
| 候補の最大角度誤差 | **0.003883839°** |
| 候補のGPU形状比較 | 最大 **0.000001279766 m**、coverage差 **0** |
| CPU解析skinとBakeMesh(false)+rigid復元 | 最大 **0.0000005364418 m**（全方式・対応範囲） |
| 実Shader行列とrootのunit-scale TRS | **280/280一致**、最大成分差0 |
| source Mesh fingerprint | 軸2種類×設定2通りで全て不変。頂点/normal/index/weight/bindpose/表情payloadをSHA-256比較 |
| 保存済み診断シーン | 生成・別Editorプロセスで再読込・7 Renderer参照・GPU校正の永続化 **PASS** |
| Game viewのXYZ色表示・Inspector操作の人間目視 | **NOT RUN**。Shader実行とシーン再読込の自動検証とは区別 |
| CSV checker | **PASS**。行数/重複/有限値/閾値/負対照/不確定分類/行列を検査 |
| 同梱PowerShellのend-to-end再現 | **PASS**。新規TEMP作成・22f1確認・GPU sweep完走、出力CSV checker PASS |
| 既存v0.1 Schema | 正例 **2/2**、負例 **7/7 PASS**（jsonschema4.10.3） |
| 既存production差分・Git whitespace/meta検査 | Core/Runtime/Editor/元Project/Schema/CI変更0。`git diff --check` PASS、GUID重複0 |

CSVと環境summaryは
[`results/2026-10-16-unity2022.3.22f1`](../../experiments/VRCHeadFrame/results/2026-10-16-unity2022.3.22f1/summary.md)。
同フォルダに`angles.csv`、`matrices.csv`、実行ログ抜粋、SHA256SUMSを保存する。
ログにはライセンスclient signature/token更新エラーが出たが、entitlements解決後にcompileと実測は
完走し終了コード0だった。これを「Console全エラー0」とは記録しない。
初期の診断シーン生成ではUnityの無題加算scene/Editor専用API/Preview Scene保存制約を検出し修正した。
最終generatorはbatchでは独立ProjectのSingle scene、対話実行では既存の保存済みsceneへAdditiveを使う。

## 8. NOT RUNと未解決のリスク

| 項目 | 状態・不足条件 |
| --- | --- |
| 既存62f3 EditMode/PlayMode/CPU性能/GCの再実行 | **NOT RUN**、62f3 Editor不足。過去のPASSを今回のPASSにしない |
| Phase 2D-1人間21項目、Phase 1 pair E2E | **NOT RUN / OPEN**、人間確認と対応fixtureが必要 |
| 3体の実FBX / Humanoid animation・IK・PhysBone | FBX構造とHead Frame/全頂点位置は追加**PASS**（§12）、surface-image **20 FAIL**。animation/IK/PhysBoneは**NOT RUN** |
| PlayerでのCPU/GPU skin backend、別GPU/API、Linear project、極/背面のGPU sweep | **NOT RUN**。CPU参照の極・180°チェックと区別 |
| VRC SDKのConstraint変換・direct VRCParentConstraint | 初回は**NOT RUN**。追加のSDK 3.10.5 Edit/Play Modeで**PASS**（§11）。clientは未実行 |
| Desktop、VR Stereo、Mirror、VRChat Camera、二観測client | **NOT RUN**、VRChat client/hardwareと人間操作が必要 |
| 一人称Head非表示/Head Chop、zero scale | **NOT RUN**。Proxyの階層・solve順・行列特異点への影響が主要ブロッカー |
| 実avatar Bounds/culling、Outline、Shadow/Depth | **NOT RUN**。fixtureの4 m bounds/updateWhenOffscreenは診断用、production解決ではない |
| AAO/MA/NDMF/TTT/VRCFury後のroot/Renderer/material保持 | **NOT RUN**、今回製品パスは実装していない |

重要なリスクは(a) rootBoneの実model matrixがVRC/実Playerでも同じか、(b) Head Chopが
Head子Proxyを潰すか、(c) Constraintのscale・solve順が異なるか、(d)最適化がframeを変えるか、
(e)「見た目の正面」を実Avatarからどう指定するか、である。
非一様・負scaleは実際に大きな角度差を観測しており、当面は**Unsupportedとして明示的に除外**する。
現在のdiagnosticは除外条件を記録するだけで、製品のbuild guardを実装済みとはしない。

## 9. 推奨案と追加検証

**B ChildProxyRootを第一候補**にする。元のRenderer/mesh/bindposeを保ち、顔の正面軸に合わせた
Head子Proxyだけを追加してrootBoneへ指定する。今回B/CでFrame・元skinが共に成立したため、
Frame取得だけにMesh変換を要求する根拠はない。Aは軸補正が必要なモデルとの比較oracleとして残す。

子ProxyがHead Chop等で成立しなければ、avatar直下のConstraint Proxyを第二候補とする。
Unity ConstraintをVRCへ変換して済むと決めず、direct VRC Constraintで位置・回転・scaleと評価順を
測る。Cは将来post-skin delta空間の明確化など別の要求が出た場合に再評価する。
今回のC converterを製品の実Avatarへ自動適用しない。隔離したFBXコピーでの追加比較は§12を参照。

追加採用ゲート:

1. 正の一様scale・canonical前上軸を明示した実Avatar3体でA/B/Cを比較。originalの
   Mesh/Material/Prefab fingerprint、通常表情・首・頭・Humanoid animationを確認する。
   FBXコピーのA/B/C角度・全頂点位置・1 blink・首/頭・原本不変は追加確認済み（§12）。
   surface-imageの20 FAIL、製品Prefab、Humanoid animationは未解決。
2. VRC clientでGPUからmodel行列の基底/原点を表示し、今回のunit-scale root TRS関係を再確認する。
3. Head子ProxyとVRC Constraint Proxyを、一人称/第三者/Head Chop有無で比較する。
4. Boundsと最適化でRenderer/rootBoneが維持される条件を決める。変更がある場合は再測定する。

## 10. P0-S2へ進む条件と具体的な次タスク

**合成fixtureによる独立S2実験着手はGO**。S1で通過したBを使い、現在の単一camera globalsを
置換/拡張する小さなView Context診断に限定する。G0/実機S1が未完了のため、製品採用、GPU deltaの
完成版、Editor拡張を同時に進めるGOではない。

- S2前半: 独立したVCC 22f1 avatar sandboxでSDK版を固定。B子ProxyとVRC Constraint版、
  正の一様scaleのみを用意し、行列/角度/視点種別を表示する。
- S2計測: Desktop、VR左右眼、Desktop/VR Mirror、Cameraを、左右非対称のyaw色で比較。
  yaw符号を手で反転しない。眼ごとのcamera位置と共通眼中心の両方を記録する。
  **今のShaderは左右眼共通weightを実装していない**。stereo macrosだけでS2 PASSとしない。
- 同時にS1実機項目としてHead/Neck/Body、表情、scale0.5/1/2、Head Chop、Proxy追従を確認する。
- 2クライアントが同一avatarを別角度から見て、それぞれ独立のyaw/pitchを得ることを確認する。
- S2出口: 左右眼の共通評価、Mirror/Cameraの定義と符号、Head Frame保持を確認し、
  状態ごとPASS/FAIL/NOT RUN/INCONCLUSIVEを記録する。どれかが未解決なら動的製品採用はNO-GO。

### 人間が行うVRChat再現手順（今回未実施）

1. VCCで**新規**PC Avatar projectを作り、22f1/SDKの版を記録する。既存VLatProjectを移行しない。
2. 許諾のあるavatarを複製し、正面/上軸、Head、対象SMR、元rootBone、weightsを記録する。
   診断対象のMaterialを複製してShaderを設定し、Bの正準ProxyをrootBoneへ指定する。
   元Mesh/Material/Prefabを上書きしない。診断CameraとEditor runnerをavatarへ付けない。
3. SDK validation後、人間がアカウント操作・private uploadまたは利用可能なローカル検証を行う。
   本作業でログイン/アップロード/Blueprint操作は行っていない。
4. 正面/±45/±90、頭/胴/首+頭、表情、scale、Desktop/VR/Mirror/Camera/一人称を確認し、
   画面、条件、軸、期待値、実測値、SDK/client版を保存する。
5. 一人称でHead非表示/Head Chopを切り替え、第三者視点へ戻した際の復帰を確認する。
   不一致時は元rootBoneへ戻した複製を対照にし、Proxy階層/Constraint差を切り分ける。

実アバター試験ではdiagnostic用Materialの色は元のtoon表現とは異なる。形状/skin保持の比較と、
元Materialを保持した製品外観の検証を混同しない。

## 11. 再開後のSDK追加検証（2026-10-16）

初回の成果物59件をchecksum不変で保持し、別のSDK sandboxで直接VRC ConstraintとSDK変換後を
追加測定した。Edit/Play Mode各1,344ケース、合計**2,296 PASS / 392 FAIL**。
FAILは無効Constraintの負対照のみ。正候補の最大角度誤差**0.003873964°**、GPU world位置差
**0.00000120727 m**、coverage差0。有効Proxyの位置・回転追従も一致した。

SDKによるEditor/Play Modeの追従成立までを確認した結果であり、VRChat clientやHead Chopの
PASSへ拡張しない。推奨B方式、S2条件付きGO、製品採用NO-GOは維持する。
追加コード、API根拠、結果CSVと未実行項目は
[SDK Constraint追加報告](VRC_P0_S1_SDK_CONSTRAINT.md)を参照。

## 12. 利用者指定FBX3体の追加検証（2026-10-16）

Shinano / Komano / Yumekaの原本を保持し、隔離SDK projectへFBXだけを複製した。
3体全てでHumanoid Headを解決し、全49 SMRの構造・全非ゼロweight・BlendShape・Material slotを
監査した。各顔RendererでA、B子Proxy、B VRC Proxy、Cの4候補をEdit/Play Modeで比較した。

- Head角度は**1,344/1,344 PASS**、最大誤差**0.003887117°**。
  Shader行列はrootBone unit-scale TRSと最大成分差0。VRC Proxy追従もPASS。
- 全2,016行の結果は**1,492 PASS / 524 FAIL**。FAIL504は意図した負対照、
  **正候補20件はsurface-image位置差が0.1 mmを超えたFAIL**。最大0.563861 mm、coverage差0。
- 別コピーのpoint topologyでvertex IDごとのGPU world位置を読む追加Shaderを作成し、
  **288/288 PASS**、全2,906,112 ID比較の最大位置差**0.000717 mm**。
  vertex一致とpixelのsurface選択は別の試験である。画像差の原因は**INCONCLUSIVE**。
  surface閾値と最初のFAIL CSVは変更せず保存した。
- 元/コピーFBXのSHA-256、source Mesh fingerprintとMaterial JSONは不変。
  製品Prefabのtoon外観、PhysBone、Humanoid animation、VRChat client/Head ChopはNOT RUN。
  近傍のKomano VRM2件はVRM 0と識別し、既存VRM 1.0経路へimportしなかった。
- 具体的需要の調査は利用者指示で延期。Head Frameの限定PASSを需要確認やG0完了へ読み替えない。

推奨B、独立S2へ条件付きGO、製品採用/G0全体NO-GOを維持する。
コード・全方式比較・実測値・失敗の切り分けは
実アバター3体の追加報告 `VRC_G0_REAL_AVATARS.md`（モデル別の構造情報を含むため非公開アーカイブのみ）を参照。
公開用の集計は[実アバター検証の要約](REAL_AVATAR_VALIDATION_SUMMARY.md)。
