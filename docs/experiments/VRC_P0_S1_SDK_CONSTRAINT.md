# P0-S1追加検証 — VRC ConstraintとPlay Mode

- 2026-10-16 JST。前回の[Head Frame報告](VRC_P0_S1_HEAD_FRAME.md)でNOT RUNだったSDKの
  直接Constraintと変換後Constraintを追加検証した。P0-S2の視点処理はまだ実装していない。
- **SDK 3.10.5 / Unity 2022.3.22f1の合成fixtureでPASS**。
  **VRChat clientでの採用はNO-GO**。Head Chop / Stereo / Mirror / Cameraは未実行。
- ブランチは`spike/vrc-head-frame-p0-s1`。前回の未コミット変更を保持し、pushなし。

## 目的と環境

Unity ParentConstraintで成功した正準Head Frameの追従が、直接作成したVRCParentConstraintと
SDK変換後でも成立するかを確認する。Edit Modeの静的評価に加え、Play Modeのフレーム進行後に
実Shader行列とyaw/pitch、skin後world位置を読む。

Unity **2022.3.22f1 (887be4894c44)**、Windows 11、NVIDIA RTX 3060 Ti、D3D11。
VRC base / avatarsはともに**3.10.5**。使用したDLLとSDK変換ソースのSHA-256、解決されたUPM版は
結果の`inputs.json`に記録した。これはインストール済み版であり、最新版と断定しない。
SDK packageとUPM依存の版は取得元lockに固定した。SDK EditorソースがNUnitを使用するため、
Test Framework **1.1.29**とNUnit **1.0.6**もcompile依存として残した。
元VLatのTest Frameworkやpackage manifestを変更していない。

初回S1の独立Projectを新規Windows TEMPへコピーし、SDK package 2つと検証overlayだけを追加した。
SDK取得元projectのAssets・ProjectSettings・Blueprint・アカウント情報はコピーしない。
元SDKを含むユーザーprojectは開かない。SDK本体のDLL・ソースをリポジトリへ同梱しない。

## 使用APIと根拠

[公式Constraints API](https://creators.vrchat.com/common-components/constraints/constraints-api/)の
公開APIだけで設定した。直接方式は`Sources`、`GlobalWeight`、`IsActive`、`Locked`、
`SolveInLocalSpace`を設定し、`ApplyConfigurationChanges()`で反映する。
全て単一source、weight1、world solve、offset0、locked。

変換方式は同資料の`AvatarDynamicsSetup.DoConvertUnityConstraints`を用い、
SDK 3.10.5のinline documentationも確認した。返り値falseは「通知すべき問題なし」を表す。
`avatarDescriptor=null`、`convertReferencedAnimationClips=false`で、新規fixtureのUnity Constraint
だけを置換し、AnimationClipへアクセスしない。元Unity componentの消去とVRC component生成、
source数・設定値をCSVで別途確認する。

SDKの私有RuntimeInit等を呼ばない。C#でProxyを毎フレームコピーしたり、poseごとにShaderへ
Head行列やCPU角度を渡したりしない。SDK標準のEditor/Play Mode初期化と更新を使う。

## 実装・測定条件

[`experiments/VRCHeadFrameSdk`](../../experiments/VRCHeadFrameSdk/README.md)にSDK用のprepare scriptと
Editor-only overlayを追加した。初回S1のfixture、Shader、captureをstaging内でそのまま再利用する。
元の`experiments/VRCHeadFrame`のソース・保存済みscene・測定結果は変更しない。
本体のCore/Runtime/Editor、元62f3 Project、v0.1仕様・Schemaも変更0。

backendは以下の4種類。

1. Unity ParentConstraint（対照）
2. VRCParentConstraintの直接作成
3. SDK公開APIによるUnity → VRC変換
4. `IsActive=false`のVRCParentConstraint（負対照）

各backendで、正準Head子ProxyをrootBoneにするBを正対照として残し、Constraint Proxy rootのB、
同ProxyにRenderer/Mesh/BindPoseを整合したCを比較する。

`2 phases × 4 backend × 2 Head軸 × 8 pose × 7 view × 3 method = 2,688ケース`。
pose/viewは前回の正の一様scale範囲と同じ。Head、Body、Neck+Head、表情、scale0.5/2の複合条件を含む。
非一様・負scaleを今回の合格範囲へ追加していない。

- phase開始時のrest校正を固定し、Shader側に保存する。pose/viewごとに校正し直さない。
- Play Modeでは`Time.frameCount`が3フレーム進むのを待つ。Editor updateが呼ばれただけでは評価しない。
- proxy position/rotationと正準Head frameの差を記録する。
- GPU行列をrootのunit-scale TRSと比較し、GPU yaw/pitchをCPU参照と比較する。
- 同じpose/viewのbaselineと候補のworld位置画像・coverageを比較する。
- 合格閾値は角度各0.05°、Proxy回転0.05°、Proxy位置0.1 mm、GPU形状0.1 mm、coverage差2 pixel、
  行列成分差1e-5。CSV checkerが出力statusを数値から再計算し、負対照を分けて検査する。

SDK初回起動はAPI compatibility/define変更による追加assembly reloadを起こした。
当初runnerの静的IEnumeratorが失われたため、SessionStateにphaseを保持し、未完了のEdit Modeを
再開する処理を追加した。通常のEdit → Play domain reloadもSessionStateとstate callbackで処理する。
`-quit`を指定せず、runnerが完了後にEditorを終了する。
修正後は**新規stagingからのfresh import・SDK設定reload・Edit/Play Mode測定・正常終了を通してPASS**
と確認した。結果checkerもこのfresh runを検査している。

## 実測結果

| backend / method | Edit Mode | Play Mode |
| --- | --- | --- |
| Unity / B ChildProxy | 112/112 PASS | 112/112 PASS |
| Unity / B ConstraintProxy | 112/112 PASS | 112/112 PASS |
| Unity / C RebasedConstraint | 112/112 PASS | 112/112 PASS |
| VRC direct / B ChildProxy | 112/112 PASS | 112/112 PASS |
| VRC direct / B ConstraintProxy | 112/112 PASS | 112/112 PASS |
| VRC direct / C RebasedConstraint | 112/112 PASS | 112/112 PASS |
| SDK converted / B ChildProxy | 112/112 PASS | 112/112 PASS |
| SDK converted / B ConstraintProxy | 112/112 PASS | 112/112 PASS |
| SDK converted / C RebasedConstraint | 112/112 PASS | 112/112 PASS |
| VRC disabled / B ChildProxy | 112/112 PASS | 112/112 PASS |
| VRC disabled / B ConstraintProxy | 14 PASS / 98 FAIL | 14 PASS / 98 FAIL |
| VRC disabled / C RebasedConstraint | 14 PASS / 98 FAIL | 14 PASS / 98 FAIL |

合計 **2,296 PASS / 392 FAIL**。FAIL392は全て無効Constraintの負対照。
各phaseの正候補・child対照は全て通過し、無効化したConstraintだけがHead/Neck/Body回転へ追従できない。
restでは負対照も一致するので、初期校正だけで実験を成功扱いにできない。

- 最大角度誤差: **0.003873964°**（正候補）
- 最大GPU world位置差: **0.00000120727 m**、coverage差 **0**（正候補）
- 有効Proxyのposition/rotation誤差: **0 m / 0°**（今回観測したfloat値）
- Shader行列とroot unit-scale TRSの成分差: **0**
- 無効Proxyの最大position差 **0.1025948 m**、rotation差 **63.00001°**、yaw差 **63.00065°**
- SDK converterは問題フラグfalse、Unity Constraint残留0、VRC component数1、source数1。
- CSV checker **PASS**。初回S1の59 source/artifact checksumも全て不変。
- 行不足・重複・偽PASS・正候補の角度誤差・変換問題・SDK版違い・Proxy追従誤差を作った
  **7種類の破損結果**をcheckerが全て拒否した。保存した実測CSVは改変していない。
- SDK取得元とstagingのConstraint DLL、Dynamics DLL、converterソースもSHA-256不変。
- prepare scriptの既存出力・source内外の危険な重なり・repo内SDK出力・SDK版違いの拒否6ケース、
  正常コピー・source不変・元Assets除外・UPM固定・overlay追加を検査してPASS。

測定CSV、設定CSV、環境・依存・ログ抜粋は
[`results/2026-10-16-sdk3.10.5`](../../experiments/VRCHeadFrameSdk/results/2026-10-16-sdk3.10.5/summary.md)。
SDKの初回compileではNUnit不足を検出し、元SDK環境の固定依存を追加して解消した。
初回importにはSDK Mobile Shaderのfallback警告とlicense client更新エラーがあり、
「Console全Error/Warning 0」とは扱わない。独自C#と診断Shaderのcompile結果とは分けて記録する。

## 判定と残るゲート

**SDK 3.10.5のEdit/Play Modeで、正準子Proxyと直接/変換Constraint ProxyによるHead Frame取得はPASS**。
推奨は引き続きB ChildProxyRoot。Constraint ProxyはHead Chop等への代替候補として根拠が増えた。
CのMesh変換をHead Frame取得だけの必須条件へ変更しない。

以下は**NOT RUN**のまま。

- 実アバター3体、Humanoid/Animator/IK/PhysBone、元Prefab/Materialsとの実アバターE2E。
- Standalone Playerでのskin backend、VRChat client、Desktop/VR Stereo/Mirror/Camera、2 client。
- 一人称Head非表示と実VRCHeadChop。公式[Head Chop資料](https://creators.vrchat.com/avatars/avatar-components/vrc-headchop/)
  ではHeadをnear-zero scaleにし、親設定が子へ及ぶことが説明されている。今回それを実行したわけではない。
- 非一様・負scale、Bounds/Outline/Shadow/Depth、最適化後のframe保持。
- 既存62f3テスト再実行、Phase 2D-1の人間21項目、Phase 1 actual VLat pair。

Play Modeの成功をStandalone PlayerのGPU skinningやVRChat成功へ読み替えない。
S2独立実験は条件付きGO、製品採用・G0全体完了・旧Phase 2D-2は引き続きNO-GO。
次はB ChildProxyとVRC Constraint Proxyを同じ実アバター複製へ設定し、Head Chopの有無と
Desktop/VR/Mirror/Cameraの視点定義を実機で比較する。アカウント・upload操作は人間が行う。
