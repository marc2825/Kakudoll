# P0-S2-R1 — VRChatクライアント検証の準備と未実施条件

2026-10-17。**実機V1〜V10はすべてNOT RUN、実機S2・製品採用・S3本検証はNO-GO。**
ユーザー回答「今は実機の手動操作はできない」を受け、認証・Avatar選択・アップロードには進んでいない。
Unity内の模擬ケースの追加ではなく、実機で使う合成Avatar・live Shader・証拠回収・操作手順を準備した。
以下のUnity回帰とpreflightは実機結果から区別する。

## 1. Git保全と環境

開始時は`spike/vrc-s1-image-fail-investigation`にS1-R1の未コミット成果があった。
既存S2は`5022f58`に保存済みだった。S1-R1の差分・checker・36 SHAを確認し、39ファイルをローカルコミット
`6023a9e7d0194d6ad9771334462643cd123def15`（`test: explain S1 raster image differences`）で保全した。
clean確認後、このコミット/ブランチをベースに`spike/vrc-s2-client-validation`を作成した。
main変更・push・PR・mergeなし。今回の新規成果はこの検証ブランチ上にある。

|項目|確認した状態|
|---|---|
|既存VLat|Unity 2022.3.62f3の設定/Assetsを変更していない|
|隔離SDK project|Unity 2022.3.22f1 / Built-in / GPU skinning / D3D11|
|SDK|com.vrchat.base / avatars 3.10.5。既存SDK projectのPackagesだけ複製|
|新規TEMP|`C:\Users\<user>\AppData\Local\Temp\VLatClientValidation-P0S2R1-20261017a`|
|GPU|NVIDIA GeForce RTX 3060 Ti、Windows driver 32.0.15.9595|
|VRChat|Steam版インストールあり、app438100 / buildid25841461|
|exeのFileVersion|6000.0.67.12655311。Unity Playerバイナリの版でありVRChatの実リリース番号として扱わない|
|実行状態|確認時VRChat/SteamVR/OVR未起動、Windows interactive sessionあり|
|HMD|関連PnP名で接続機器を検出できず、利用可能性未確認。無線機器等の不存在は断定しない|
|アカウント/実クライアント版|未確認。資格情報を読まず、ログイン/自動認証なし|

[公式Editor要件](https://creators.vrchat.com/sdk/upgrade/current-unity-version/)を確認し、既存S1/S2と同じ22f1で分離した。
SDKのバージョンと実クライアントの内部Unity版の違いは、互換性実測の代わりにならない。
クライアント起動時に実バージョンと描画設定を別途記録する。

## 2. 診断AvatarとShader

`experiments/VRCClientValidation/`を追加。既存実験ソースは変更していない。
`prepare_client_project.py`は既存S2/S1の準備関数を再利用し、新規TEMPへSDKだけを持ち込む。
Editor-only生成コードが合成Humanoid、Mesh、Material、Avatar、Menu/FX、sceneを作成する。
私有FBX/VRM・製品Prefab・元Materialは使用しない。

- HeadProbe/Card1: Headの正準markerをSDK3.10.5のVRCParentConstraintで追うProxy BをrootBoneに使用。
- Card0: Head子Proxyの対照。Card2: 配列を意図的に使わない厳格負対照。
- 赤の長い+X突起、緑の−X突起、青の+Z目印で左右非対称を明示。
- Probe ON: 正yawで+X側のみ最大20mmの小さなpost-skin変位。OFF初期値。
- Keep Head: VRCHeadChopのHead scale=1 overrideを切替。通常OFF、Head Chop既定動作との比較用。
- OFF参照Prefab: 同じ生成MeshのHeadをHips-root/単色Unlitで描画。参照と診断を別Avatarとして手動Build & Testできる。
- 必要なruntime componentはUnity標準とVRCのものだけ。任意MonoBehaviourの毎フレーム実行/行列送信なし。

これは視点と符号の診断用。v0.1 delta、S3のSkinning変形、lilToon/NDMF統合は実装していない。
Boundsは診断用に大きく確保しており、製品のculling/性能の証明にはならない。
生成したPrefab/sceneは隔離projectの`Assets/VLatClientGenerated/`に保存される。

## 3. View Contextと実際のShader Globals観測

live Shaderは凍結済みS2の`ViewContextCore.cginc`と`ViewContextLive.cginc`を再利用する。
通常は描画カメラ、Stereo配列ありなら左右眼中心、MirrorはMirror位置を評価する。
Photo/Screen位置は補助診断で、無条件で選択位置に置換しない。Mirrorのyaw反転なし。
Face Mirror単独は未確定status=4、通常Mirrorとの重複は別context/warning=16で保留する。
正投影は位置policyでwarning=8。方向policyとの違いは既存S2報告のまま。

[公式Shader Globals](https://creators.vrchat.com/worlds/udon/vrc-graphics/vrchat-shader-globals/)上の定義と、
avatar各passへのbinding実測は別物。**VRChatクライアントでのglobal観測は全項目NOT RUN**。
Unity Editorで得たmode=0はVRChat bindingの証拠ではない。

数値パネルにはdraw/selected/mirror/screen/photo/左右眼位置、3種のVRChat mode、stereoAvailable/native eye、
status/warnings、yaw/pitch、VS/PS weight、Object frameを記録する。
未知mode、競合mode、非有限値、空Stereo、ゼロMirror、縮退方向、Stereo必須だが配列なしを診断する。
ただし未定義globalが0として見える場合と仕様上の0をShaderだけで区別できない。
特に全flag=0/mono warning=1を「各globalが正しく供給された」と解釈しない。

## 4. 実行したUnity preflight

保存証拠: `experiments/VRCClientValidation/results/2026-10-17-unity2022.3.22f1/`。
`verification.txt`にchecker出力を保存し、追加実験コード/生成assets/結果186件を`SHA256SUMS`で照合した。
この節は全て**UnityEditorPreflight**。SDKのAvatar validation、Build & Test、VRChat Clientとは異なる。

|検査|結果|範囲|
|---|---|---|
|合成Humanoid|PASS|isHuman/isValid、元FBX不要|
|Renderer/Material/Mesh/Proxy参照|PASS|5 SMR、Missing scripts 0、Blueprint未設定|
|runtime component検査|PASS|Unity/VRC componentのみ、独自runtime C#なし|
|3 Shader|PASS|D3D11で描画、Shader messageなし|
|Windows向けAssetBundle|PASS|Unity BuildPipelineで作成。SDKビルドではない|
|OFF参照|PASS|256×256=65,536画素、RGBA相違成分0。合成Avatar/固定cameraのみ|
|パネル復号|PASS|3/3 PNG checksum一致。通常2枚status=0、厳格負対照status=5|
|VS/PS weight|PASS|3枚とも差0。実機両眼の比較ではない|
|報告FrameからのCPU角度再計算|PASS|有効2枚の最大誤差約0.00218°。同一Object binding内の数学確認|
|証拠ツール負対照|PASS|10 tests: CRC/bit改ざん、鏡反転、四隅変換、非有限raw値保持、模擬結果混入、証拠不足、local-only V8拒否等|

CPU再計算はShader自身が報告したObject行列を使う。実Headボーンの独立した位置/回転参照ではない。
追加したclient専用guardは特異/非有限Frameをstatus=10、非有限角度/weightをstatus=6にする。
実際のHead Chop時にこのguardが必要か/どの状態になるかはV7で未検証。既存S2 Coreは変更していない。
頭/胴体/Scaleのクライアント追従はV10で別途確認する。
配列を使わないCard2は制御された診断分岐であり、実際のVR multipass/bindingの検証ではない。

初回コンパイルではSDK ControlPanel公開APIへのasmdef参照不足を修正した。
最初のOFF比較は同期Camera.Render間でrootBoneを変更した結果、参照画像が空になり20,790 RGBA成分差を記録した。
比較を別々に初期化したPrefab同士へ変更し、0差で再確認した。閾値変更なし。
これは今回の比較ハーネスの初期化手順の問題として扱い、rootBoneを描画途中で変更して即比較する手順は採用しない。
Unity内部の更新タイミングの一般的保証までは立証していない。初回記録も保存している。

## 5. S1/S2回帰

既存のcheckerと249/249 SHAを再実行し、凍結ソース/結果の保持を確認した。
既存S1 synthetic / SDK Constraint / 実FBX3体の保存結果checkerは全て成功した。
負対照のFAILを正常条件PASSへ昇格していない。

既存S2 runnerを変更せず新規隔離projectで**Edit/Play ModeをGPU再実行**:
10,752正常PASS、896意図的FAIL、最大角度誤差0.003887254095°、両眼weight差1.0000000006e-7。
既存結果と一致した。これは回帰実行であり実機の追加模擬ケースではない。

S1-R1は保存済み隔離projectの凍結overlayを照合して再実行した。新出力先を使用し、元の画像差を再調査していない。
結果は旧raw20 FAIL/最大0.563860638mm、新対応点20 PASS/最大0.000479933mm、原因pixel28件で保存済み結果と一致した。
旧raw20 FAILと新しい対応点20 PASSは異なる測定量として維持する。
CPU/GPU頂点一致・対応点一致・実クライアント外観一致の3基準を混同しない。

## 6. V1〜V10の実機結果

|ID|対象|実機判定|不足している証拠|
|---|---|---|---|
|V1|Desktop通常描画|NOT RUN|通常描画card、頭/全身動作、OFF外観|
|V2|Desktop Mirror|NOT RUN|実mirror mode/position、通常との独立性、左右符号|
|V3|VR通常/左右眼|NOT RUN|HMD、同一フレーム両眼、共有中心/VS weight|
|V4|VR Mirror|NOT RUN|HMD両眼、実Mirror中心、通常描画との違い|
|V5|Handheld/Screenshot|NOT RUN|Desktop/VR/撮影パスごとのmode/位置と通常画面の比較|
|V6|Face Mirror|NOT RUN|face/mirror flagの実組合せ、適切な評価policy|
|V7|Head Chop|NOT RUN|通常一人称とoverride、Proxy更新、復帰時OFF外観|
|V8|2観測者|NOT RUN|共通に見えるprivate Avatar、同期した異なる2視点|
|V9|実配列なし経路|NOT RUN|実Shader経路の同定、明示警告。強制負対照で代用不可|
|V10|Head/全身/Scale|NOT RUN|固定観測点との角度関係、Proxy追従、Scale前後|

実機PASS 0 / FAIL 0 / NOT RUN 10 / INCONCLUSIVE 0。
実機PNG/動画/log/global値はまだ存在しない。templateを証拠として数えない。
Unity preflight画像と実機画像を同じevidence sourceに保存しない。

## 7. Build & Testと残る手動操作

**SDK Build & Test自体もNOT RUN**。Unity AssetBundleができたことだけでSDK通過を主張しない。
SDK sourceではControlPanelの通常初期化が`ApiCredentials.Load()`/current-user fetchへ進み得るため、自動で開かなかった。
`ClientBuildMenu`は、人がSDKパネルを開いた後に公開`IVRCSdkAvatarBuilderApi.BuildAndTest`を呼ぶ。
認証やUploadコードは追加していない。[SDK公開API](https://creators.vrchat.com/sdk/public-sdk-api/)

次の操作は[実験README](../../experiments/VRCClientValidation/README.md)に具体化した:
隔離sceneを開く → 人がSDK validation/必要な認証を実施 → Offline Build & Test →
DesktopクライアントでSDK Test Avatarを選択 → V1/V2/V5からPNG/動画を採取 → HMD条件へ進む。
通常画面captureとVRChat Screenshotは別描画パスになり得るため区別する。
自分視点cameraはHeadと共に動くため、固定観測者のHead回転テストにはMirror/固定撮影Cameraを使う。
ステレオの単眼desktop outputや別時刻の眼画像だけならINCONCLUSIVE。

Build & TestのAvatarは本人にのみ表示されるためV8を満たせない。
2観測者にはprivate uploadの明示承認が必要。手順は準備したが、**Uploadは未承認/未実施**。
[公式ローカルAvatarの制約](https://creators.vrchat.com/avatars/#local-avatar-testing)

## 8. 証拠回収と未解決リスク

PNGの32bit×56word cardはCRC/checksum、回転/鏡反転、四隅homographyで復号する。
暗黙のyaw反転はせず、反転は画像の読み取り方向にだけ適用する。
出力は必ずUNASSESSED。実測provenanceと下位受入条件を人が記録してから判定する。
checkerは不足証拠/ファイルhash不一致/模擬source/片眼/両眼差/local-only V8を拒否するが、出所の真実性や人の申告を自動証明しない。

残るリスク:

- avatar passへShader Globalsが実際に供給されるか、Camera modeと位置の意味、Face Mirror policy。
- actual XR constant-buffer/eye binding、配列が使えない経路で共通眼中心を取得できるか。
- Head Chop/Scale/Constraint更新順序、デバッグBoundsに隠れるculling問題。
- FX/メニュー/Head Chop toggleがSDK処理後・クライアントで有効か。今回のSDK validationは未実施。
- OFF外観は合成AvatarのUnity Unlit比較に限定。実機・製品Material/lighting/outline/Prefab整合は別途必要。
- 大きな数値cardとchecksumは診断コストを伴う。製品性能や実Avatarの見た目を評価していない。

## 9. S2判定とS3条件

**Unity限定PASSを維持。実機S2/製品採用NO-GO、S3本検証NO-GO。**
今回実機の部分PASSも得ていない。Avatar/Shader/手順が準備できたことと実機ゲート通過は別。
S3へ進むには、重要な通常描画・両眼Stereo・Mirror・Cameraで実機証拠を揃え、
Proxy追従/Head Chop/OFF外観/Scaleを確認し、Face Mirrorと配列なし経路の扱いを確定する必要がある。
2観測者もlocal testの制約を満たす方式で検証する。
S1の旧20 FAILは履歴を保持し、新対応点20 PASSとは別に報告する。
準備段階でS3/lilToon/NDMFの本実装に着手しない。

## 10. 次セッション

入口は専用引継ぎ `docs/handoff/VRC_CLIENT_VALIDATION.md`（非公開アーカイブのみ。公開版では [`experiments/VRCClientValidation/README.md`](../../experiments/VRCClientValidation/README.md) が手順の入口）。branch/base、ローカルproject、実行コマンド、
証拠の場所、許可範囲、次の手動操作を集約した。
今回追加したファイルは本報告、引継ぎ、PROGRESS追記、`experiments/VRCClientValidation/`のみ。
