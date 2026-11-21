# G0 / P0-S1 実FBX追加検証

Shinano / Komano / Yumekaの**利用者が所有するFBXのコピー**でHead Frameを比較するEditor-only overlay。
元の`VLatProject`・62f3環境・元FBX/VRM/Material/Prefabは変更しない。SDK、FBX、VRM、テクスチャ、
生成Meshやモデル画像をリポジトリへ含めない。Unity Projectそのものではなく、新規TEMPへ組み立てる。

必要条件: Python 3.9以上、Windows Unity **2022.3.22f1**、D3D11/float RT対応GPU、
base/avatars **3.10.5**のSDK packageが入った読み取り可能な取得元project、3体のFBX。
SDK取得元のAssets/設定/アカウント情報はコピーしない。

```powershell
python .\experiments\VRCRealAvatars\prepare_avatar_project.py `
  --sdk-project 'C:\path\to\SDK-project' `
  --output 'C:\Temp\VLat-RealAvatars-new' `
  --model 'Shinano=C:\path\to\Shinano.fbx' `
  --model 'Komano=C:\path\to\Komano.fbx' `
  --model 'Yumeka=C:\path\to\Yumeka_v1.0.fbx'

& 'C:\Program Files\Unity 2022.3.22f1\Editor\Unity.exe' `
  -batchmode -force-d3d11 -projectPath 'C:\Temp\VLat-RealAvatars-new' `
  -executeMethod VLat.HeadFrameExperiment.RealAvatarSweep.RunBatch `
  -headFrameOutput 'C:\Temp\VLat-RealAvatars-new\results' `
  -logFile 'C:\Temp\VLat-RealAvatars-new\sweep.log'

python .\scripts\check_head_frame_avatar_results.py 'C:\Temp\VLat-RealAvatars-new\results'
```

`-quit` / `-nographics`を付けない。runnerがEdit/Play Modeを順に測定し終了する。
プロセスの終了まで待ってから次のUnityを起動する。結果出力先は新規ディレクトリに限定する。
生成するテストsceneは一時的な通常sceneであり、私有モデルを含む永続assetはrepoへ保存しない。
共通診断Shader・64×64 float readbackは`../VRCHeadFrame`を再利用する。
追加の`RealAvatarVertexProbe.shader`は候補Meshをさらに複製してpoint topologyへ変え、
128×128 float RTへvertex IDごとのworld位置を出力する。全IDの重複・欠落を検査し、
画面上で隠れた頂点も元rootの同じIDと比較する。上限16,384頂点。
triangleのsurface-image比較とは別のdrawであり、双方の結果を独立に保存する。

監査だけなら`-executeMethod VLat.HeadFrameExperiment.RealAvatarAudit.RunAudit -quit`を使う。
FBXの`.meta`はコピーせず、**コピー先だけ**をReadable / Humanoid自動mapping /
非optimized hierarchy / 全非ゼロweight import（上限255）へ設定する。
実製品Prefabのimport設定、Animator、IK、PhysBone、lilToonを再現する操作ではない。
描画の実効weight制限は別途setup JSONに記録する。

実験はblinkを持つRendererのうちHead配下weight合計が最大のものを選択する。
3体とも顔を含む`Body`を選択した。全Rendererのweight/BlendShape/bindpose/Material slotも監査する。
Mesh、Materialは候補ごとに複製し、形状画像を未変更rootの複製Rendererと比較する。
C方式のconverterは正の剛体変換だけを受理する。未対応の変換は`NOT RUN`として理由を保存する。
通常のblinkを100へ設定する試験は顔の選択した1表情だけであり、全表情の保証ではない。

正準frameはrest時のavatar +Z前/+Y上をHead子へ固定する。カメラはworld空間で周回させる。
一度求めた校正をpose/viewで変更せず、Shaderへ毎フレームのHead行列を送らない。
VRCParentConstraintは標準SDK更新に任せ、手動追従や私有初期化APIを使用しない。
Play Modeでは少なくとも3フレームの更新を待つ。

`private-preview/`は無テクスチャのローカル確認画像。リポジトリへの集約対象から除外する。
数値結果を保存する場合も`inputs.json`の元パスを除去し、SDK/モデル本体は含めない。
source Mesh fingerprintは頂点/normal/tangent/index/bindpose/全weight/表情名とframe weightを対象とし、
全表情delta payloadのハッシュではない。元FBXのSHA-256を前後で照合して原本全体を保護する。

VRMの形式だけを読み取り監査するには次を使う（Unity importは実行しない）。

```powershell
python .\experiments\VRCRealAvatars\inspect_vrm_metadata.py 'C:\path\to\Komano.vrm'
```

製品Prefabの外観・PhysBone、Head Chop、VRChat client、Stereo/Mirror/Camera、実Player GPU skinning、
非一様/負scale、既存62f3回帰は今回の実FBX試験では**NOT RUN**。
今回の合格はP0-S2へ進む追加根拠であり、G0全体や製品採用の完了判定ではない。

実測では正候補のsurface-image比較20件が0.1 mm閾値を超えた。閾値とFAILを保持する。
checkerはHead角度/行列、全頂点probe、surface-imageを分けて報告する。
checkerの終了code0はデータの整合とHead Frame/全頂点gateの通過を意味し、
surface-imageのFAILを取り消す意味ではない。詳細は実験報告を参照。
