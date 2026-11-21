# P0-S2-R1 実機診断Avatar

現状: **VRChatクライアントV1〜V10はNOT RUN**。操作担当者は現在手動操作できない。
このフォルダは合成Humanoidの生成コード、実機用live Shader、証拠回収ツールを提供する。
Unityの画像・AssetBundle作成をVRChatのBuild & Test成功や実機成功に読み替えない。

## 隔離projectの作成

SDK取得元の`Packages`だけを読む。元Assets/設定/アカウント/FBXはコピーしない。
取得元SDKはbase/avatarsとも3.10.5に固定。出力先はrepository外の**存在しない**TEMPを指定する。
SDKの更新を自動実行しない。既存の62f3プロジェクトをこのEditorで開かない。

```powershell
python .\experiments\VRCClientValidation\prepare_client_project.py `
  --sdk-project 'C:\path\to\SDK-3.10.5-project' `
  --output 'C:\Temp\VLat-Client-new'

& 'C:\Program Files\Unity 2022.3.22f1\Editor\Unity.exe' `
  -batchmode -force-d3d11 -projectPath 'C:\Temp\VLat-Client-new' `
  -executeMethod VLat.HeadFrameExperiment.ClientValidationSetup.RunBatch `
  -logFile 'C:\Temp\VLat-Client-new\client-preflight.log'
```

準備スクリプトの終了を待ってからUnityを起動する。`-nographics`/`-quit`は付けない。
生成済みAssets/結果の上書きを拒否する。再現時は新規TEMPを使う。

生成物は`Assets/VLatClientGenerated/`:

- `ClientValidation.unity`: 診断Avatarを1体配置したscene。
- `ClientDiagnostic.prefab`: VRCParentConstraintでHead追従する正準Proxy BをHeadProbeとCard1のrootBoneに使用。
- `ClientOffReference.prefab`: 同じ生成MeshでHeadProbeをHips-root/単色Shaderにした参照。
- Humanoid Avatar、Mesh、Material、字形texture、FX、Expressions Menu/Parameters。すべて自作の合成データ。

任意MonoBehaviourのruntime処理はゼロ。Editor asmdefだけがSDK Editorへ依存する。
頭の**赤い長い突起がAvatar +X**、緑の小突起が−X、青が正面+Z。
`Probe ON (+X)`は正のyawだけで赤側を最大20mm動かす。OFFが初期値。
これは描画視点/符号の診断用post-skin変位であり、v0.1 deltaやS3変形実装ではない。
`Keep Head (test)`はVRCHeadChopのHead scale=1 overrideを切り替える。OFFが通常の自分視点Head Chop。
Head Chopが非対応・stripされた場合も実測するまで成功扱いにしない。

## 表示と数値パネル

Card0（青系header）: Head子Proxyの対照。Card1（茶系）: VRC Constraint Proxy B。
Card2（紫）: Stereo配列を**意図的に使わず**共通Stereo基準を必須にした負対照。
Card2の警告はV9の実際のShader経路を検証した証拠にはならない。
各headerは`Y`yaw°、`P`pitch°、`W`weight%、`S`status、`C`context、`E`native eye（`-`は配列経路外）、末尾Card番号。
エラーは紫。Mirrorのyaw符号反転は追加していない。

選択/evaluationは既存S2の`ViewContextCore.cginc`/`ViewContextLive.cginc`をそのまま再利用:

|条件|評価位置/扱い|
|---|---|
|通常Desktop|描画中の`_WorldSpaceCameraPos`、mono/multipass未証明warning=1|
|Stereo配列あり|左右位置の平均、両眼共通|
|Mirror|`_VRChatMirrorCameraPos`、通常視点と別評価|
|Handheld/Screenshot|描画中の位置、配列ありならその中心。Screen/Photo globalは補助観測|
|Face Mirror|Face flagを別記録。単独flagはstatus=4、Mirror重複はwarning=16、採用判断保留|
|配列なし厳格モード|status=5で停止。無言のmono fallbackをしない|
|正投影|本診断は位置policy、warning=8。方向policyとの一致を仮定しない|

その他のstatusは既存Coreのコメント参照。モードの範囲外/競合、NaN/Inf、ゼロMirror、空Stereo、方向縮退を診断する。
client専用guardのstatus=10は特異/非有限Frame。非有限角度/weightはstatus=6で変位を停止する。
非有限のraw globalは復号JSONで文字列と元32bit wordを保持し、勝手に0へ置換しない。
**未定義globalが0に見える場合と本来の0はShaderだけでは識別できない**。全flag=0を「VRChatのbinding確認済み」としない。

## 手動のBuild & Test（未実施）

1. 上記隔離projectを22f1で開き、生成sceneを明示的に開く。Hierarchyの診断Avatarルートを選択。
2. SDKパネルを**人が**開き、必要なアカウント操作を人が行う。Builderのvalidationを確認し、warning/errorを保存。
3. Offline Testingの**Build & Test**を選ぶ。補助メニュー`VLat > Client Validation > Build and Test selected avatar (local only)`も同じ公開APIを呼ぶ。パネル自動起動・ログイン・Uploadは実装していない。
4. ローカルbundleの作成成功ログを保存。VRChatをDesktopで起動し、AvatarメニューのSDK Test Avatarsから**人が**診断Avatarを選ぶ。
5. クライアントの実バージョン・撮影時刻・world名/設定・Desktop/VR・Mirror品質・眼/取得方法・Probe/KeepHead値を記録する。ログのアカウント情報は共有前に除く。
6. V1→V2→V5を優先し、通常画面/鏡/撮影画面それぞれのPNGと操作動画を保存。その後V3/V4/V6/V7/V9/V10。
7. OFF参照はsceneの診断instanceを外して`ClientOffReference.prefab`だけを配置し、別ローカルAvatarとしてBuild & Test。同じカメラ/距離/Scale/Head Chop/メニュー条件で比較する。元の保存scene/prefabを上書きする必要はない。

[公式Local Avatar Testing](https://creators.vrchat.com/avatars/#local-avatar-testing)によると、Build & TestのAvatarは**装着した本人にしか見えない**。
V8はこれでは成立しない。必要なら後述のprivate uploadを別途承認してから行う。
`--watch-avatars`による自動切替は今回使用しない。

## V1〜V10の操作と必要な証拠

|ID|操作/確認|保存する証拠|
|---|---|---|
|V1|Desktop通常描画で胸cardを見下ろす。OFF/ON、childとVRC Proxyを比較。自己cameraはHeadと共に動く点に注意|通常描画PNG、頭/視線/全身の操作動画、card0/1の数値。鏡で撮ったPNGを通常描画の証拠にしない|
|V2|Desktop MirrorをON。鏡への斜め左右45°/90°、Head回転、OFF/ON。通常画面と鏡を同時記録|mode=2、source=3、mirror position、非対称赤突起、通常との違いが見える画面と動画|
|V3|HMDで通常描画。可能ならSteamVR等で同一フレームの左右眼を取得|native eye=0/1、選択中心・VS weight一致、元の両眼画像。desktop用単眼出力だけでは判定不可|
|V4|VR Mirrorで同じ両眼取得、通常視点とMirror視点を分離|mirror mode=1/source=3、両眼中心/VS weight、通常との比較|
|V5|HandheldをAvatarの左右へ動かす。DesktopとVR、実際のScreenshotを別々に実行|撮影preview/撮影PNG、通常画面との同時動画、camera mode=1/2/3、draw/selected/screen/photoの数値|
|V6|Face Mirror単独、通常Mirrorと併用、OFFを比較|face flagとmirror flag、S/statusと警告、各画面。status=4やwarning=16を無条件PASSにしない|
|V7|通常一人称でKeepHead OFF/ON、Mirrorを対照。頭非表示と胸card/Proxy状態の変化|切替動画、Object行列/状態、Headは意図どおり非表示か、復帰時OFF外観|
|V8|private Avatarを2人が共通に見て左右異なる角度から同時観察|両観測者の同期動画/PNG、同一Avatarと時刻、異なる角度/weight。local AvatarではNOT RUN|
|V9|実際にVRで配列が利用できない経路が発生した場合に調べる|実経路のbinding証拠とCard0/1のstereoAvailable、厳格負対照Card2の警告。Card2だけではINCONCLUSIVE|
|V10|撮影Cameraまたは鏡を固定しHeadのみ/全身を回転、Avatar Scaleを変更|固定視点を示す動画、前後の行列/yaw/pitch、OFF比較。負/非一様Scaleは未対応の別条件として記録|

自分視点のカメラは頭に追従し得る。Head回転の期待値を「固定観測者」と同じにしない。
VR左右眼は静止した別時刻の画像で代用しない。眼番号、同一描画フレーム、元画像を保持する。
通常Screenshotは別の撮影パスになり得るため、V1/V2の証拠はdesktop/眼bufferの画面captureで確保し、V5のScreenshotとは分ける。
数値cardは最小272×512画素程度を確保し、JPEG/動画再圧縮/強い斜角/遮蔽を避ける。動画だけでbitが読めなければINCONCLUSIVE。

## PNGの復号と記録

```powershell
# card全体を正面から切り出した場合。元スクリーンショットも保持する。
python .\experiments\VRCClientValidation\decode_card.py card.png `
  --source VRChatClient --output card-decoded.json
# 切り出さず透視変形したcardの四隅を指定（左上・右上・右下・左下）
python .\experiments\VRCClientValidation\decode_card.py screenshot.png `
  --corners 100 200 400 210 390 750 110 740 `
  --source VRChatClient --output card-decoded.json
python .\experiments\VRCClientValidation\check_client_evidence.py evidence\client-results.json
```

`client-results-template.json`を新しい証拠folderへコピーする。V1〜V10のNOT RUN、各下位条件、provenance、相対path/SHA256を埋める。
PASSは全下位条件で実測証拠が揃った場合だけ。片方のCamera/眼だけなら当該IDはINCONCLUSIVEとし、notesに部分結果を記載する。
Stereo PASSでは同一フレームのleft/right復号JSONもevidence配列へhash付きで登録し、`stereo_pair`に
`same_render_frame:true,left_decoded:"left.json",right_decoded:"right.json"`を記載する。
checkerは両眼VS weight差≤1e-6、選択位置差≤1e-5m、native eye=0/1、status=0、sharedStereo=1を検査する。
これらは**新しい実機証拠の診断基準**であり、既存S1/S2の閾値を変更しない。
human_operator_attestationには操作/観測した事実を短く記載する（個人情報は不要）。
provenanceのsourceは`VRChatClient`、client_versionは実画面/実ログから取得した版にする。
V8のPASSは`avatar_distribution:"private_uploaded_with_explicit_approval"`も必要。
checkerは記録の整合性を検査するもので、画像の出所や人の申告を自動証明するものではない。

復号器はPNG CRC、56word payloadのchecksum、8通りの回転/鏡反転を照合する。
四隅指定には平面homographyを使う。独自機械学習/OCR、SDK、追加Python packageは不要。
単なる復号成功は`UNASSESSED`。`--source VRChatClient`を指定しただけで実機PASSにはならない。
報告されたObject行列と評価位置からのCPU再計算は同一binding内の数学確認であり、実Headボーンの独立参照ではない。

protocol v1: 34×64cell。横1〜32cellが32bit LSB-first、上8行はheader/境界、行8〜63に56word。
0 magic VLC1、1 version、2 CardId、3 stereoAvailable、4 native eye、5〜7 VRChat modes、8〜12 evaluation metadata、
13〜17 weight/angles/VS weight/VS status、18〜38 selected/draw/mirror/screen/photo/left/right位置、39 orthographic、40 Effect、
41 requireStereo、42〜53 Object frame origin/XYZ、54 shader time、55 rolling uint32 checksum。
Shader同梱の`DataWord`と復号JSONの`words`で全原値を追跡できる。固定の正準校正行列identity専用。

## 2観測者に必要なprivate upload（未承認・未実施）

ローカル検証後、ユーザーが明示的に承認した場合だけ実施する:
生成AvatarのSDK validation/非公開内容/新規Blueprintを確認 → 人がログイン → private指定で新規Upload →
本人が装着 → 同じinstanceで2人が安全設定/Shader表示を許可して同時撮影。
公開化、元AvatarのBlueprintへの上書き、他人のアカウント操作は必要ない。
この手順の提示はUpload承認を意味しない。

## 回帰と引継ぎ

```sh
python3 -m unittest discover -s experiments/VRCClientValidation -p test_evidence_tools.py -v
python3 experiments/VRCClientValidation/check_client_evidence.py experiments/VRCClientValidation/client-results-template.json
```

実行済み証拠は`results/2026-10-17-unity2022.3.22f1/`。
詳細な判定は[実機検証報告](../../docs/experiments/VRC_P0_S2_CLIENT_VALIDATION.md)、次セッションの入口は
[引継ぎ](../../docs/handoff/VRC_CLIENT_VALIDATION.md)。
