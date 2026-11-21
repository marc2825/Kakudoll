# P0-S1-R1: 表面画像差の原因調査

S1/S2を凍結したまま、同じ実FBXを**複製**して比較するEditor-only overlay。
Core/Runtime/Editor、元アセット、62f3 project、v0.1を変更しない。
SDK/モデル/生成Mesh/モデル画像はrepoへ含めない。私有画像とfloat生データはTEMPに保存する。

## 再現

Windows Unity2022.3.22f1、SDK base/avatars3.10.5、D3D11 float RT対応GPU、所有する3体のFBXが必要。
prepareが終了してからEditorを起動する。存在する出力先は拒否する。

```powershell
python .\experiments\VRCImageDifference\prepare_image_project.py `
  --sdk-project 'C:\path\to\SDK-project' `
  --output 'C:\Temp\VLat-ImageR1-new' `
  --model 'Shinano=C:\path\to\Shinano.fbx' `
  --model 'Komano=C:\path\to\Komano.fbx' `
  --model 'Yumeka=C:\path\to\Yumeka_v1.0.fbx'

# 先に変更のないS1ハーネスで全2,016行/288全頂点probeを再測定する。
& 'C:\Program Files\Unity 2022.3.22f1\Editor\Unity.exe' `
  -batchmode -force-d3d11 -projectPath 'C:\Temp\VLat-ImageR1-new' `
  -executeMethod VLat.HeadFrameExperiment.RealAvatarSweep.RunBatch `
  -headFrameOutput 'C:\Temp\VLat-ImageR1-new\original' `
  -logFile 'C:\Temp\VLat-ImageR1-new\original.log'

# 上のEditorプロセス終了を待つ。三角形・深度の調査は別の新規結果ディレクトリへ。
& 'C:\Program Files\Unity 2022.3.22f1\Editor\Unity.exe' `
  -batchmode -force-d3d11 -projectPath 'C:\Temp\VLat-ImageR1-new' `
  -executeMethod VLat.HeadFrameExperiment.ImageDifferenceSweep.RunBatch `
  -imageOutput 'C:\Temp\VLat-ImageR1-new\investigation' `
  -logFile 'C:\Temp\VLat-ImageR1-new\investigation.log'

python .\scripts\check_head_frame_avatar_results.py 'C:\Temp\VLat-ImageR1-new\original'
python .\scripts\check_image_difference_results.py 'C:\Temp\VLat-ImageR1-new\investigation'

# 終了後にShaderのmessageと環境を別途監査する（この呼出だけ-quitを付ける）。
& 'C:\Program Files\Unity 2022.3.22f1\Editor\Unity.exe' `
  -batchmode -force-d3d11 -quit -projectPath 'C:\Temp\VLat-ImageR1-new' `
  -executeMethod VLat.HeadFrameExperiment.ImageDifferenceAudit.RunBatch `
  -imageAuditOutput 'C:\Temp\VLat-ImageR1-new\investigation\shader-audit.json' `
  -logFile 'C:\Temp\VLat-ImageR1-new\shader-audit.log'

# 私有画像はコピーせず、数値・hash・検査結果だけを存在しない集約先へ保存する。
python .\experiments\VRCImageDifference\collect_evidence.py `
  'C:\Temp\VLat-ImageR1-new' 'C:\Temp\VLat-ImageR1-new\investigation' `
  'C:\Temp\VLat-ImageR1-collected-new' --sdk-project 'C:\path\to\SDK-project'
```

`-quit`/`-nographics`を付けない。SDKの設定reloadをSessionStateで復帰し、Edit/Play終了後にrunnerが終了する。
全試験はVLat変形ゼロ。元S1 fixtureのA/B子/B VRC/Cと同じbone操作、表情、カメラを使う。
表示boundsを大きくする診断条件であり、製品boundsの受入試験ではない。

## 何を比較するか

- 元S1: 64×64 Linear ARGBFloat / depth24 / MSAA1 / Cull Off。同じcovered画素の補間world位置の距離の最大値。
- 追加probe: 元のtriangle index/submeshを維持。`SV_PrimitiveID`とslotで三角形を識別する。
  world / primitive / device+view depth / normal / 固定単色を別drawで読む。同じIDであることを確認せず位置差を同一点変位と呼ばない。
- 大差画素: baselineとcandidateで勝った二枚の三角形を、fragment discardでそれぞれ単独描画する。
  元Meshのtopology/頂点/skinを変更しない。coverageと深度を4通り記録する。
- CPU参照: 全非ゼロweightを用いるLBS。選択blinkの単一100%frameのみを明示対応。
  `(bone.localToWorld * bindpose) * (position + morph)`をdouble積和で計算し、全IDのGPU point probeと比較する。
  UnityがEditor内でどのskin backendを使ったかや実Playerを保証する試験ではない。
- clip probe: clip XYZWを全vertex IDで読み、64×64のviewport座標を1/256 pixelへ丸めて
  perspective補間をdoubleで再現する。RTのY方向候補を照合し、今回の28大差画素は全て同じflip規約。
- 対応点: 同じprimitiveのbaseline補間係数をcandidateの3頂点へ固定適用する。
  20条件の全7,842共通画素を検査する。元raw位置距離とは別の測定量として記録する。
- 診断variant: Baseline再描画、probeと元Shader照合、world→clip式、Cull Back、ZTest Always、
  BakeMesh後の静的描画、128/256解像度、blink ON/OFF、Head100%weight、1 mm Mesh破損負対照。
  各variantは異なる意味を持つので独立表示する。

`strictSurfaceStatus`の閾値は元の**0.1 mm / coverage2画素**のまま。
checkerの終了code0は整合検査の成功であり、元20 FAILをPASSへ変える意味ではない。

## 私有画像

`private-images/<phase>/<avatar-pose-method-yaw-pitch>/`に以下を保存する。

- `original-baseline` / `original-candidate`: float world位置。PNGはclamp表示、数値判定には使わない。
- `original-difference`: R=位置距離/0.1mmをclamp、G=coverage不一致。赤点が元閾値超過。
- `correspondence-difference`: R=同じ重心座標の幾何差/0.1mm。描画修正後の画像という意味ではない。
- `primitive-*`: 三角形IDの仮色。ID自体はfloat生データ/JSONを読む。
- `depth-*`: R=device depth、G=view距離。PNGの微差を目視で判定しない。
- `normal-*`: world normalを[-1,1]→[0,1]表示。
- `solid-*`: unlit固定単色。silhouette/coverageを分離できる。

各項目にPNGとlittle-endian `.rgba32f`（bottom-up行、RGBA float32）がある。
モデルの許諾なく公開しない。集約時は数値結果、依存lock、ログ抜粋、画像hash/寸法だけをrepoへ保存する。

実VRChat、HMD、Head Chop、製品toon外観、別GPU/API、62f3回帰は別試験であり、このrunnerから成功扱いにしない。

保存済みsource/数値/文書のchecksumは、`experiments/VRCImageDifference`を作業ディレクトリとして
`sha256sum --check results/2026-10-17-unity2022.3.22f1/SHA256SUMS`で照合する。
旧S1/S2のmanifestは書き換えない。今回のnative S2回帰と旧結果の同値hashは`s2-regression.json`を参照。
