# P0-S1 SDK Constraint追加検証

初回S1でNOT RUNだった**直接VRC ConstraintとSDK変換後の追従**を、Unity 22f1の
Edit Mode / Play Modeで比較する。前回の`VRCHeadFrame`のソース・結果は変更しない。
このフォルダは検証overlayであり、そのままUnity Projectとして開かない。

SDK 3.10.5を導入済みの別projectを取得元にする。Python 3とUnity 2022.3.22f1が必要。
取得元projectのAssets、ProjectSettings、Blueprint、アカウント情報をコピーしない。
SDK packageだけを新規TEMPへ複製する。SDK本体を本リポジトリへ同梱しない。

Windows PowerShellから、存在しない出力先を指定する。

```powershell
python .\experiments\VRCHeadFrameSdk\prepare_sdk_project.py `
    --sdk-project 'C:\path\to\SDK-project' `
    --output 'C:\Temp\VLat-HeadFrame-SDK-new'

& 'C:\Program Files\Unity 2022.3.22f1\Editor\Unity.exe' `
    -batchmode -force-d3d11 `
    -projectPath 'C:\Temp\VLat-HeadFrame-SDK-new' `
    -executeMethod VLat.HeadFrameExperiment.SdkHeadFrameSweep.RunBatch `
    -headFrameOutput 'C:\Temp\VLat-HeadFrame-SDK-new\results-sdk' `
    -logFile 'C:\Temp\VLat-HeadFrame-SDK-new\sdk-sweep.log'

python .\scripts\check_head_frame_sdk_results.py 'C:\Temp\VLat-HeadFrame-SDK-new\results-sdk'
```

`-quit`は付けない。domain reloadを経てPlay Modeへ入り、完了後にrunnerが終了する。
`-nographics`も付けない。GPU行列・角度・world位置をreadbackする。
runnerは隔離batch実行専用で、試験sceneを置換するため元projectで直接実行しない。
同じ結果ディレクトリへの再実行は拒否されるので、新しい出力先を使う。

prepareは両SDKの版を3.10.5へ固定し、取得元lockのUPM実効版を使用する。
SDK EditorソースがNUnitを必要とするため、既存Test FrameworkとNUnitもcompile依存として残す。
IDE package、lilToon、VPM resolverは不要。SDK DLL/変換ソースのSHA-256とUPM版は`inputs.json`に記録する。

比較backendはUnity ParentConstraint、直接VRCParentConstraint、SDKの
`DoConvertUnityConstraints(..., null, false)`による変換、無効化VRC Constraint（負対照）。
変換は新規fixture内だけに行い、AnimationClipを書き換えない。

Head子Proxyを正対照として、Constraint ProxyをrootBoneにするBと、同Proxy基準にrebaseしたCを測る。
各phaseは4 backend × 2 Head軸 × 8 pose × 7 view × 3 method = **1,344ケース**。
初期校正を固定し、pose変更後に標準SDK評価が進むのを待つ。私有APIの呼出や、C#からの
手動Proxy追従、poseごとのShader行列送信は行わない。

結果CSVのFAILには無効Constraintの負対照が含まれる。checkerが正候補のFAILと区別する。
Edit/Play Modeの通過をVRChat client、Standalone Player、Head Chop、Stereo、MirrorのPASSと扱わない。
