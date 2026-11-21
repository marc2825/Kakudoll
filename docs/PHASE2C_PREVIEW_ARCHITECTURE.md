# Phase 2C Preview Architecture

## 1. Status and scope

Phase 2Cは`VlatEditorWindow`へVRM 1.0 preview、Main/Sub A/Sub B、camera navigation、
angle scrub、VLat runtime評価、Expression previewを追加する。vertex picking、brush/rectangle
selection、soft selection、gizmo、vertexDelta編集、2D warp、rebind UIは対象外である。

2026-08-31のhuman manual smokeでactual VRM 1.0 `Seed-san.vrm`のloadまで到達したが、
EditModeから`Vrm10Instance.Runtime`をlazy生成したため
`Vrm10Runtime expects runtime behaviour.` warningが発生した。runtime-only pathを除去する
修正後のmanual smokeでは、意図的な`Vrm10Instance.UpdateType=None`をRuntime overloadへ渡した
warningと、Seed-san用VLat pairがない状態の有効Layer 0 errorを検出した。Previewをmanual
initializationとAvatar-only fallbackへ修正した後のhuman rerunでは、3 viewportの枠とdiagnosticは
正常だが内部が黒い問題を再現した。§13の調査でPreview Cameraのscene culling mask欠落を修正したが、
2026-08-31の最終human rerunでactual `Seed-san.vrm`の3-view表示、navigation、Expression、fallback、
clear/reload、Console 0を確認した。直前のautomated gateと合わせてPhase 2Cは**COMPLETE**であり、
Phase 2D着手は**GO**とする。本記録更新ではPhase 2D実装へ進まない。

## 2. Components and ownership

```text
VlatEditorWindow (UI input/render adapter)
├─ VlatEditorDocumentController
│  └─ VlatAuthoringDocument (editing single source of truth)
└─ VlatPreviewController
   ├─ dedicated Preview Scene
   ├─ one temporary Vrm10Instance
   ├─ one Editor-only serialized Expression adapter
   ├─ zero or one Phase 1 VlatApplier (applicable VLat only)
   ├─ three Cameras + three RenderTextures
   ├─ temporary decoded VLat textures
   └─ camera / Expression / Before-After session state
```

`VlatEditorWindow`はUI callbackと表示同期だけを行う。VRM、Camera、RenderTexture、
generated Mesh/Material、runtime評価のownerは`VlatPreviewController`である。

| Resource | Owner | Release boundary |
|---|---|---|
| Preview Scene、Camera、Light | PreviewController | Window close、assembly reload、controller dispose |
| temporary VRM instance | PreviewController | replace、clear、failed post-load、controller dispose |
| VlatApplierとgenerated Mesh/Material | PreviewController経由のRuntime | document revision/replacement、avatar replace/clear、dispose |
| decoded VLat Texture | PreviewController | document revision/replacement、avatar replace/clear、dispose |
| RenderTexture | PreviewController | size change、avatar replace/clear、dispose |
| Expression adapterと複製Material | PreviewController | avatar replace/clear、controller dispose |
| camera/Expression weight/Before-After state | PreviewController memory only | avatar clearまたはcontroller dispose。`.vlat`へ保存しない |

temporary Unity objectには`HideAndDontSave`を指定し、scene/prefab/Project assetとして保存される
ことを防ぐ。元VRM、Mesh、Material、Prefabは入力として読むだけで、VLatによるMesh/Material変更は
temporary avatar上でPhase 1 Runtimeが作る複製だけに対して行う。

document load自体が失敗した場合はPhase 2B DocumentControllerがcurrent documentを保持するため、
PreviewControllerへreplacement通知は来ない。document replacement成功時はdocument依存の
VlatApplier/generated resources/decoded textureだけを破棄して再構築し、独立したavatar selectionと
camera/RT sessionは維持する。

`VlatApplier`初期化失敗はtemporary avatarの失敗ではない。Runtime binderが生成途中のMesh/Materialを
disposeして基準状態へ戻した後、PreviewControllerはavatar、Expression adapter、Camera、RTを維持し、
binding reportとdiagnosticだけを将来のPhase 2E rebind UI用に保持する。

## 3. Preview Scene strategy

`PreviewRenderUtility`と`EditorSceneManager.NewPreviewScene`を比較し、後者を採用した。

| Choice | Evaluation |
|---|---|
| PreviewRenderUtility | 単一utility camera中心で、同じavatarを3 Camera/3 RTから管理する場合に追加のowner規則が必要 |
| Dedicated Preview Scene | 1 avatar、3 Camera、Light、RTを通常sceneから分離し、scene単位で最終cleanupできる |

Unity 2022.3はPreview Sceneをactive sceneにできない。一方、UniVRM raw loaderは生成時のactive
sceneへ一時instanceを作る。この境界ではEditor用`ImmediateCaller`で同期的にloadし、成功した
rootを直ちに`HideAndDontSave`としてPreview Sceneへ移す。load前にcleanだったcurrent sceneは、
固定baselineに存在するEditor内部のscene dirtiness clear APIで元のclean状態へ戻す。このAPIの
存在はEditMode testで固定し、元sceneのroot数/dirty状態はactual VRM manualでも再確認する。

PreviewはPlay Modeを必要としない。LightとCameraはPreview Scene内だけにあり、Cameraは対応する
RenderTextureだけへ明示renderする。Cameraの通常の`cullingMask`は全GameObject layerを対象にするが、
`overrideSceneCullingMask`を当該Preview Sceneのmaskへ固定するため、avatar固有layerをhard-codeせず、
user/source Sceneのobjectを描画しない。

## 4. Avatar and baseline state

UIの`Select / Open VRM 1.0`は`.vrm` pathをVLat documentとは独立に選ぶ。UniVRM
`v0.128.3`の`Vrm10.LoadPathAsync`を使用し、VRM 0.x migrationは無効にする。candidateのloadと
VRM 1.0/Humanoid head確認が成功するまで旧avatarを保持し、成功時だけreplaceする。Clearは
avatar、runtime object、RT、temporary textureを破棄する。

再現可能なPhase 2C baselineは次のとおり。

- import直後のHumanoid/rest transform
- animation sourceなし、control rig生成なし
- Expression input weightは初期値0
- loaderには`Vrm10NopSpringboneRuntime`を渡すが、EditModeでは`Vrm10Instance.Runtime`を
  生成せず、spring runtimeの`Process`を呼ばない
- ConstraintとLookAtを評価せず、bone/eye directionはimport直後のrest状態を維持
- VLat Before/Afterはpreview session toggleで切替可能

特定avatarのGameObject、Renderer、Mesh、hierarchy名はhard-codeしない。frame bounds、renderer数、
Expression keyはloadしたinstanceから列挙する。

## 5. Three-view rendering and navigation

Main/Sub A/Sub Bは1体のavatarと1つのin-memory documentを共有する。各viewは独立した
`VlatPreviewViewState`とCameraを持ち、既定yawは0/45/90度、pitch 0度である。3体のavatarは
生成しない。

Expression変更時は、既存VLat evaluationを先にreleaseしてtemporary avatarのbase Material/Meshへ
戻し、§8のEditor-only adapterでExpressionを適用してからPhase 1 `VlatApplier`を再構築する。
VLatがapplicableな場合はこれにより仕様の「VRM Expression → VLat」の順序を保つ。VLatがない、
編集中、またはbinding不一致の場合はExpression適用済みbaseline avatarをそのまま各viewへrenderする。
VLatが初期化済みの場合だけ、各viewについて次を順番に行う。

1. view yaw/pitchをPhase 1 `VlatApplier.DebugYaw/DebugPitch`へ設定する。
2. `VlatApplier.EvaluateNow`で同じRuntime semanticsを適用する。
3. そのviewのCameraを対応RTへrenderする。
4. 3 view終了後、avatarをMain angleのVLat状態へ戻す。

left dragはorbit、middle dragはpan、wheelはzoomで、各viewを操作できる。Main/Sub A/Sub Bに
resetを持つ。camera/Expression操作はAuthoring Modelを変更せず、Undo/dirty対象外である。

## 6. Document conversion and runtime semantics

Previewの入力は現在の`VlatAuthoringDocument`である。document referenceまたは単調revisionが
変化した場合、Phase 2A `VlatAuthoringPersistence.Pack`を呼び、memory上だけでcanonical
immutable `VlatDocument`とbuffer列を作る。一時`.vlat.json`/`.vlat.bin`はwriteしない。

immutable snapshotはPhase 1 `VlatApplier.Initialize(avatarRoot, head, camera, ...)`へ渡す。
`Vrm10Instance` overloadはRuntimeでUniVRM更新順を診断する契約であり、neutral/restのため
`UpdateType=None`にするEditor Previewでは使用しない。angle weighting、interpolation、conflict
resolution、MeshBinding、vertexDelta、visibility/material modifierはすべてRuntime実装を再利用し、
Editor固有evaluatorを持たない。外部/embedded VLat textureだけはEditor ownerがtemporary
`Texture2D`へdecodeし、caller-owned textureとしてRuntimeへ渡す。

documentなし、またはcanonical snapshotのLayer 0は通常の`AvatarOnly` stateとしてRuntime初期化を
試みない。編集中でpackできないdocument、binding不一致、Runtime初期化結果のfailureは
`VLatUnavailable`へ移し、初期化途中のRuntime資源だけを破棄してbaseline avatarを継続する。
Phase 2A/2BのSave時pack/validationは同じstrict pathのままで、Previewだけがfailureを許容する。

## 7. Refresh and resource policy

refreshは次のeventだけで発生し、`EditorApplication.update`による毎frame処理は行わない。

| Event | Document snapshot rebuild | VLat/view evaluation + render |
|---|---:|---:|
| avatar load/replace | yes | yes |
| document edit、Undo/Redo、document replace | yes | yes |
| angle/orbit | no | yes |
| pan/zoom | no | yes（単一avatarを対象view角度へ順次適用するため） |
| Expression/Before-After | no | yes |
| viewport resize/DPI/dock geometry change | no | yes |
| repaint only | no | no |

RT sizeはUI Toolkitのgeometry point size×`EditorGUIUtility.pixelsPerPoint`から計算し、16〜4096へ
clampする。同じpixel sizeでは再生成せず、変更時だけ旧RTをrelease/destroyしてCamera aspectを
更新する。Windowにはrefresh total/average、VLat evaluation、Camera render、document rebuild、
RT recreation/refresh countを表示する。Editor preview全体へRuntimeの300 µs gateは流用せず、
新しいhard thresholdも設けない。

## 8. Diagnostics and Expression state

UIにはcurrent VRM、loaded/not loaded、renderer/Expression数、RT/resource数、Main yaw/pitch、
active angleBand/modifier数、Runtime binding/pack/load/render errorを表示する。Consoleだけを
diagnostic sourceにしない。Preview stateは`NoAvatar`、`AvatarOnly`、`AvatarWithVLat`、
`VLatUnavailable`、`AvatarLoadError`を区別する。

VRM parse、Humanoid head消失、render時unexpected exception等はPreview infrastructureの`ERROR`で
ある。一方、document未選択、0 Layer、編集中のpack error、binding未解決、有効Layer 0は
`VLat not applicable`というUI上のnon-fatal diagnosticに変換する。Runtime初期化結果の元diagnosticと
`MeshBindingResolutionReport`は失わず保持するが、Consoleへerror/warningをlogしない。

Expression dropdownは`Vrm10Instance.Vrm.Expression.Clips`のpreset/custom serialized definitionを
直接列挙し、`Vrm10Instance.Runtime`へアクセスしない。input weightはEditor-only adapterに保持し、
選択weightを0〜1で変更してresetできる。

UniVRM v0.128.3には、既存`Vrm10Instance`へEditModeで複数Expressionを完全なVRM semantics付きで
適用する公開Editor APIがない。このため、binaryおよびblink/lookAt/mouth override解決はUniVRMの
公開`Vrm10RuntimeExpression.ExpressionValidatorFactory`が返す`IExpressionValidator`へ委譲し、
解決済みweightのbinding適用だけをEditor adapterが担当する。adapterはserialized
`MorphTargetBindings`、`MaterialColorBindings`、`MaterialUVBindings`を使い、Materialはtemporary
avatar上で複製してから変更する。Seed-san固有名や独自の簡易BlendShape-only semanticsは持たない。

LookAt inputはneutral/rest contract上0であり、adapterはLookAt bone/expressionの自動生成を行わない。
明示的に選ばれたlookAt preset clipは通常のserialized Expressionとして適用できる。Expression変更で
VlatApplierを再構築するため、VLatのMaterial baselineにも適用済みExpressionが反映される。

## 9. Automated coverage and manual boundary

EditModeではview default/independence/reset、Preview Scene ownership、source scene dirty保持、
RT resize/replacement/clear、反復create/dispose、failed file load、document revision/Undo invalidation、
session操作のnon-dirty、in-memory snapshot/no-file-write、UXML構造を検証する。加えてVRM-like
`Vrm10Instance`を使い、serialized Expression列挙、weight/reset、camera/angle refreshの全経路で
UniVRMのprivate runtime fieldがnullのままであること、binary/override/morph/material color/UV、
source Mesh/Material非破壊、反復adopt/clear/disposeでwarningと所有clone蓄積がないことを検証する。
さらに`UpdateType=None`でmanual VLat initializationを使うこと、documentなし/0 LayerのAvatarOnly、
incomplete Authoringとunresolved bindingのVLatUnavailable fallback、binding report保持、applicable
documentのAvatarWithVLat遷移を検証する。Phase 1の既存PlayMode testsがVlatApplierのRuntime validation、
Mesh/Material非破壊、Expression blend shape保持、性能/GCを担当する。

render sanity testはknown geometryについて、finite/non-zero bounds、Camera frustum、Preview Scene
culling mask、Renderer layer、RT生成とCamera.targetTextureを検証する。graphics deviceが利用可能な
runではreadbackし、clear色以外のpixelが一定数あることも検証する。UI testは各`Image.image`が
Controller-owned RTと同一instanceで、resize後に破棄済み旧RTを保持しないことを固定する。
pixel-perfect比較やSeed-san依存は持たない。

actual `Seed-san.vrm`の最終的な見え、actual Expression列挙/変更、Console 0、明白なduplicate
avatar残留は§10のhuman rerunでPASSした。対応VLat pairを必要とするactual VLat適用項目だけは
Phase 1 manual acceptanceとして分離して残す。

## 10. Actual VRM manual checklist

Test asset: human側のactual VRM 1.0 **`Seed-san.vrm`**。Seed-san対応VLat pairはまだない。
2026-08-31、利用者がscene culling修正版で次のactual-VRM Preview smokeを再実施した。

1. [x] VLat EditorからVRM 1.0を選択・load — PASS
2. [x] avatarがMain Viewへ表示 — PASS
3. [x] avatarがSub View A/Bへ表示 — PASS
4. [x] default 0/45/90° view — PASS
5. [x] orbit — PASS
6. [x] pan — PASS
7. [x] zoom — PASS
8. [x] reset — PASS
9. [x] window resize — PASS
10. [x] Expression一覧表示 — PASS
11. [x] blink weight変更による実際の表情変化 — PASS
12. [x] Expression reset — PASS
13. [x] AvatarOnly / `VLat not applicable` fallback — PASS
14. [x] avatar clear / reload — PASS
15. [x] duplicate avatarの明白な残留なし — PASS
16. [x] Console Error 0 / Warning 0 — PASS

修正前に検出したEditMode `Vrm10Runtime` warning、`UpdateType=None` warning、有効Layer 0の
Console error、Main/Sub全画面blackはいずれも再発しなかった。dock/undock、document replacement、
window/domain reload、source Scene dirty保持、元asset非破壊、直接avatar replacementのlifecycleは
既存Phase 2B manual結果およびPhase 2C automated testsでcoverageを維持する。最終bug-fix rerunの
human acceptance範囲には含めず、未実施項目をmanual PASSとは記録しない。

Phase 1 manual acceptanceはPhase 2C closureから分離する。actual VRM loadとactual Expression previewは
今回PASSした。Seed-san対応`.vlat.json/.bin` pairがないため、actual VLat apply、Before/After、
VLat + Expression併用、actual VLat適用時のoriginal Mesh/Material/source avatar非破壊確認は
**NOT RUN / OPEN**のままとする。対応fixture作成後に`AvatarOnly → AvatarWithVLat`を含めて別途行う。

以上と§9のautomated coverageを合わせ、Phase 2C Definition of Doneは完了した。
**Phase 2C COMPLETE / Phase 2D GO**。Phase 1 actual VLat manualのOPEN項目をPhase 2C PASSへ混入しない。

## 11. UniVRM v0.128.3 runtime contract audit

実コードの契約は次のとおりである。

- `Vrm10Instance.Runtime`は`m_runtime == null`時に`MakeRuntime(m_useControlRig)`を呼ぶlazy property。
- `MakeRuntime`はspring runtimeが未設定ならproviderまたはstandalone実装を作成・初期化し、
  `new Vrm10Runtime(instance, useControlRig, springRuntime)`を返す。
- `Vrm10Runtime` constructorは`Application.isPlaying == false`なら明示的に
  `Vrm10Runtime expects runtime behaviour.`をwarning出力し、Head、ControlRig（任意）、
  Constraint列、LookAt、`Vrm10RuntimeExpression`、SpringBoneを束ねる。
- `Vrm10RuntimeExpression`はinternal constructorでserialized `Vrm.Expression.Clips`からkeyを作り、
  validatorとinternal `ExpressionMerger`を生成する。publicなweight setterは入力を保持するだけで、
  morph/materialへ反映する`Process`はinternalである。
- `Vrm10Runtime.Process()`はExpressionだけのAPIではない。順にVrmAnimation retarget/expression/lookAt、
  ControlRig、全Constraint、指定Transform LookAt入力、LookAt、Expression、SpringBoneを処理する。
  `Vrm10NopSpringboneRuntime`で最後だけをno-opにしても、Constraint/LookAt/Expression等は実行される。

従って旧実装の`CollectExpressions()`で`.Runtime.Expression`へ触れた時点でfull runtimeが生成され、
EditModeであることを知らせるwarningは契約どおり発生した。またrefreshの`.Runtime.Process()`は
neutral/rest previewに不要なConstraint/LookAtまで評価しており、spring simulation disabledだけでは
既存contractと整合しなかった。

UniVRM同梱の`VRM10ExpressionEditorTool`と`Vrm10InstanceEditorExpression`もEditModeでは操作を無効化し、
Play Modeでのみ`Runtime.Expression`を使う。`PreviewSceneManager.Bake`はExpression asset Inspector用の
単一clip previewで、複数weightのvalidator/overrideを提供しない。よって次の候補を比較した。

| Candidate | Result |
|---|---|
| internal runtime APIをreflectionで呼ぶ | version脆弱かつEditor-safe public contractではないため不採用 |
| Phase 2CのExpression previewを制限 | 既存Phase 2C scopeとmanual項目を満たさないため不採用 |
| Preview architecture全体をPlay Mode化 | neutralなEditor Previewとresource ownershipを変えるため不採用 |
| public validator + serialized bindingsのEditor adapter | VRM判断をUniVRMへ委譲し、binding適用だけに二重実装を限定できるため採用 |

これは既存のExpression preview要件と合成順序を実装する技術修正で、format/Runtime/product semanticsを
変更しない。したがって新しい仕様判断待ちは発生せず、`DECISIONS_NEEDED.md`への追記は不要である。

## 12. Preview / VLat applicability boundary audit

2回目のmanual findingのroot causeは2点である。

- Previewはneutral/restのため`Vrm10Instance.UpdateType=None`を意図的に設定していたが、
  `VlatApplier.Initialize(Vrm10Instance, ...)`を呼んでいた。このoverloadはStandalone/Runtimeで
  UniVRM ExpressionをVLatより前に更新する契約を診断するため、手動更新を求めるwarningは正当だった。
  Editor Previewは既存の`Initialize(avatarRoot, head, camera, ...)`へ切り替え、Runtime overloadと
  warningは変更しない。
- Seed-san対応VLat pairがなく、Phase 2BのNew/編集中documentをRuntime applicationへ渡したため、
  Runtime契約どおり有効Layer 0がfailureになった。Previewがそのseverityをavatar failureと同一表示
  していたことがintegration boundaryの誤りだった。

修正後の状態遷移は次のとおりである。

```text
VRM load success
  -> AvatarOnly
  -> documentなし / Layer 0: AvatarOnly + non-fatal UI diagnostic
  -> pack不能 / binding不一致 / valid Layer 0: VLatUnavailable + binding diagnostic
  -> manual VlatApplier initialization success: AvatarWithVLat

VRM parse / Humanoid resolution failure
  -> AvatarLoadError + ERROR diagnostic
preview construction / unexpected render failure
  -> current avatar stateを保持 + ERROR diagnostic
```

`VLatUnavailable`でもtemporary avatar、3 Camera/RT、Expression adapterを所有し続ける。
Runtimeの`再バインド後に有効なLayerがありません`、Save boundaryのstrict validation、Phase 1 testsは
変更しない。これは既存Editor toleranceとRuntime strictnessの接続修正であり、新規仕様判断はない。

## 13. Black-screen render path audit

3 viewportの黒画面について、Windows Unity `2022.3.62f3`、Direct3D 11でactual
`Seed-san.vrm`を一時diagnostic harnessからloadし、Camera.Render直後の320×320 ARGB32 RTを
readbackした。調査用diagnostic/readbackはproduction codeに残していない。

### 13.1 Actual avatar / scene

- rootは`activeSelf=True`、`activeInHierarchy=True`、position/rotation=`(0,0,0)`、
  scale=`(1,1,1)`で専用Preview Sceneに所属した。sceneはvalid/loaded、root数6である。
- Rendererは5、enabled 5、active 5、すべてSkinnedMeshRenderer、すべてGameObject layer 0だった。
- combined boundsはcenter `(-0.41, 1.02, -0.21)`、extents `(1.19, 1.10, 0.63)`、
  size `(2.38, 2.20, 1.27)`で、center/extentsはいずれもfiniteかつnon-zeroだった。
- Directional Lightは2。keyはrotation `(35,135,0)` / intensity `1.1`、fillは
  rotation `(20,315,0)` / intensity `0.55`で、avatar/Cameraと同じPreview Sceneに所属した。

### 13.2 Actual camera / framing

`FrameAvatar()`は上記bounds centerをframe centerとし、extents magnitudeからradius約`1.74`、
FOV 30°とmargin 1.05からdistance約`6.81`を得た。Camera実測値は次のとおりで、全viewについて
`GeometryUtility.TestPlanesAABB`はtrueだった。

| View | Position | Euler / forward |
|---|---|---|
| Main | `(-0.41, 1.02, 6.61)` | `(0,180,0)` / `(0,0,-1)` |
| Sub A | `(4.41, 1.02, 4.61)` | `(0,225,0)` / `(-0.71,0,-0.71)` |
| Sub B | `(6.40, 1.02,-0.21)` | `(0,270,0)` / `(-1,0,0)` |

3台ともnear `0.01`、far `1000`、FOV `30`、GameObject cullingMask `-1`（all layers）、
SolidColor `RGBA(0.12,0.12,0.12,1)`、target 320×320 ARGB32、`IsCreated=True`だった。

### 13.3 Readback and root cause

修正前は3台とも`overrideSceneCullingMask=0`であり、Main/Sub A/Sub Bはいずれも102,400 pixel中
clear 102,400、non-clear 0、RGB min/maxとも31だった。bounds/frustum、GameObject layer、通常の
`cullingMask`、RT、Lightは正常でも、dedicated Preview Sceneのscene culling maskがCameraへ設定
されていなかったため、`Camera.Render()`はclearだけ行いscene内geometry/lightを全て除外していた。

Cameraへ`EditorSceneManager.GetSceneCullingMask(previewScene)`（実測値`1`）を設定する実験では、
同じavatar、framing、native Material、Light、HideFlagsのまま次の非clear pixelを得た。

| View | Clear pixels | Non-clear pixels | RGB min/max |
|---|---:|---:|---|
| Main | 97,330 | 5,070 | 1 / 255 |
| Sub A | 97,372 | 5,028 | 0 / 255 |
| Sub B | 98,877 | 3,523 | 0 / 255 |

従ってroot causeはframing、通常layer mask、lighting/MToon、HideFlags、UI bindingではなくscene
cullingだった。final fixは各CameraをPreview Sceneへ移動後、そのscene maskを
`overrideSceneCullingMask`へ設定する。`cullingMask=~0`でavatarの任意layerを許可しながらscene maskで
専用sceneへ限定するため、Seed-san固有layerのhard-codeもsource Sceneへの描画影響もない。
