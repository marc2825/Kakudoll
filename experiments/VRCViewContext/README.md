# P0-S2 View Context 検証

S1のB Head Proxy、fixture、GPU readback、rest校正を再利用するEditor-only overlay。
元のVLatProject/62f3、Core/Runtime/Editor、v0.1、S1のsource/結果は変更しない。
このフォルダをUnity projectとして直接開かず、SDK packageだけ新規TEMPへ複製する。

必要: Python 3.9以上、Windows Unity **2022.3.22f1**、D3D11/float RT対応GPU、
base/avatars **3.10.5**入りの読み取り可能なSDK取得元project。
HMDなしでもUnity実カメラと明示的な模擬入力の試験を実行できる。実XR/VRChatの代替にはならない。

```powershell
python .\experiments\VRCViewContext\prepare_view_project.py `
  --sdk-project 'C:\path\to\SDK-project' `
  --output 'C:\Temp\VLat-ViewContext-new'

$unityArgs = @(
  '-batchmode', '-force-d3d11',
  '-projectPath', 'C:\Temp\VLat-ViewContext-new',
  '-executeMethod', 'VLat.HeadFrameExperiment.ViewContextSweep.RunBatch',
  '-headFrameOutput', 'C:\Temp\VLat-ViewContext-new\results',
  '-logFile', 'C:\Temp\VLat-ViewContext-new\sweep.log'
)
$process = Start-Process -FilePath 'C:\Program Files\Unity 2022.3.22f1\Editor\Unity.exe' `
  -ArgumentList $unityArgs -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Unity experiment failed: $($process.ExitCode)" }

python .\scripts\check_view_context_results.py 'C:\Temp\VLat-ViewContext-new\results'
```

パスに空白のないTEMP例を使用する。`-quit` / `-nographics`を付けない。
新規出力先だけを受理する。Edit→Play→Editを経てrunnerが終了するまで、同じprojectを再起動しない。
SDKの初期設定reloadをSessionStateで再開する。元SDKのAssets/設定/Blueprint/アカウントはコピーしない。

- 2 Head軸 × 2 B方式（子Proxy/直接VRCParentConstraint）×4 pose×7 view×26 scenario×2仮想eye×2 phase。
- UnityCamera evidence: 実Camera.RenderのUnity標準bindings。Main/別観測camera、Head同時回転、
  正投影の位置/方向、Stereo情報のない厳格拒否を測る。
- Injected evidence: 専用Shaderの`_VLatTest*`入力で共通関数を測る。
  左右の通常Unity cameraによる仮想eye画像はXR texture-array/実HMD経路ではない。
  Mirror位置は平面反射した点を入力する。実VRCMirrorReflectionの描画ではない。
- 実VRChat Globalsは読み取りのみ。テストで`_VRChat*`やUnity標準globalsをSetGlobalしない。
- `StereoCompile` / `InstancedCompile`はkeywordを指定したcompile-only probe。
  成功しても両眼配列の実bindingやVRChatのPASSにしない。clientへ使用しない。

`ViewContextDiagnostic.shader`がclient候補、`ViewContextInjected.shader`が模擬試験用。
Core/Live/Diagnosticの3 cgincを同じフォルダへ置く。
各MaterialへS1と同じrest Object→canonicalの3行を保存する。pose/viewで更新しない。
浮動小数点の数値判定は64×64 float RTで行い、PNGの8bit色を精密な角度測定に使用しない。

| `_Mode` | 内容 |
| --- | --- |
| 0 | yawの正負が異なる色。右側だけの試験変位も表示可能 |
| 1 | float readback用yaw/pitch/valid |
| 2 | warnings/shared/directional |
| 3 | context/source/statusの数値 |
| 4 | weight/shared/warningの色、無効時magenta |
| 5 / 6 / 7 | 選択world位置 / 実draw位置 / raw mode flags |
| 8 / 9 | skin後world位置 / vertexとfragmentのweight・status |
| 10 | mode palette: mono灰、stereo緑、VR mirror紫、desktop mirror水色、VR photo橙、desktop photo黄、screenshot青、face pink。無効はmagenta |
| 11 | native stereo eye: 左緑/右赤。Stereo配列のないvariantは灰 |

`_AsymmetricProbe=0.04`は正yaw時に頭部右側だけを動かす小さな診断変位。
post-skin world変位であり、v0.1 deltaやS3のskinning整合を実装した意味ではない。
`_RequireStereoCenter=1`はStereo配列のない通常描画を明示的に拒否する。
既定0はMono/不明VR経路として警告を出す。VR multipassを自動識別できたとは扱わない。
`_OrthoDirection=0`は位置、1は視線方向。Mirrorの方向モードは未対応として拒否する。

Face Mirrorだけのflag、未知/競合mode、無効な入力、Mirror位置0、欠損Stereo等はmagenta・statusで診断する。
Mirror位置0は「無効値か、world原点にある有効cameraか」を区別できないため保守的に拒否する。
Photo/Screenの位置は補助照合に使い、現在の描画cameraを無条件に置換しない。
完全な規則・status番号・未規定条件は[実験報告](../../docs/experiments/VRC_P0_S2_VIEW_CONTEXT.md)参照。

S1の未解決FAILを集約する（元データは読み取りのみ）。

```powershell
python .\experiments\VRCViewContext\track_s1_failures.py `
  .\experiments\VRCRealAvatars\results\2026-10-16-unity2022.3.22f1 `
  C:\Temp\s1-carryover-new.json
```

## VRChat実機の準備と証拠（今回未実施）

1. 新規VCC **PC Avatar** project、22f1 / SDK3.10.5を用意する。検証world/環境も版を記録する。
   Stereo確認にはHMDと左右眼の別capture、2人確認には独立した2観測セッションが必要。
2. 利用許諾のあるAvatarを**複製**し、対象Face SMR・Mesh・Materialも複製する。
   保存するPrefabとBlueprintを元Avatarから分離し、SDKで新規Avatarとして扱う。
3. restでHead位置に+Z顔正面/+Y上のProxyを作る。Head子にし、
   `proxy.localRotation = inverse(head.rotation) * avatarRoot.rotation`相当のrest補正を入れる。
   rootBoneをProxyへ変更。別コピーはHead子marker→avatar直下VRCParentConstraint Proxyで比較する。
   source1、weight1、active/locked、world space。元のbones/weight/bindposeは変更しない。
4. S1の実Shader Object行列を読み、`K=F0^-1 M0`を3行propertyへ保存する。
   独立Unity実測でBのunit-scale TRS関係・元形状が保たれることを先に確認する。
   顔正面をraw Head軸から推定せず、特異/非一様/負scaleは対象から除く。
   S1のbounds回避は診断用であり、製品boundsが解決したとは扱わない。
5. live Shaderとcgincだけを複製Materialへ設定。mode0/4/10/11を切り替えて記録する。
   右側だけのprobeを0/0.04 mで比較する。Editor runner・Injected/compile-only Shaderをavatarへ付けない。
6. SDK validation後、**人間が**ログイン・新規private upload等の操作を行う。
   この作業では実行していない。元Blueprintを上書きしない。
7. Desktop、VR左右眼、VR/Desktop Mirror、Handheld/Screenshot、Face Mirror、Head/Neck/Body、
   一人称Head Chop有無、第三者視点を確認する。yawを手で反転して帳尻を合わせない。
   2観測者は正/負yawの異なる位置から同じavatarを同時に見る。
8. `client-evidence-template.csv`へclient/SDK/world/HMD/モード・Proxy階層・Head Chop状態、
   左右眼画像、期待sourceと結果を記録する。`client-results.json`のNOT RUNを証拠なしで変更しない。
   Eye色で左右captureを識別し、その後weight/右側probeの一致を確認する。
   画像の視差をshape weight差と誤認しない。Face Mirrorのflag組合せも別に記録する。

高精度のclient数値確認には、検証world側でfloat RT/公開readback経路等を別途用意し、
Globals・eye・pass・timestampを紐付けて保存する必要がある。今回それを実装・実行していない。
通常PNGの色だけから0.05°精度や左右眼の厳密一致を証明しない。
