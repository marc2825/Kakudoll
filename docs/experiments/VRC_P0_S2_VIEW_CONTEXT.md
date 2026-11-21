# P0-S2 — View Context / Stereo / Mirror

2026-10-17 JST。開始HEAD **4eec47d**、S1成果物は全てコミット済み、worktree clean。
指定ブランチ`spike/vrc-view-context-p0-s2`を作成した。リモート操作・push・mergeなし。
S1のsource/artifact checksum **59 + 12 + 27件**を維持する。

**判定: Unity実カメラと明示的模擬入力によるS2は限定PASS。VRChat実機S2・製品採用はNO-GO。**
**P0-S3本検証への移行はNO-GO**。実機Stereo/Mirror/複数観測者/Head ChopとS1画像FAILが残る。
Core/Runtime/Editor、元62f3 project、v0.1、元FBX/Mesh/Material/Prefabは変更していない。

## 1. 目的と既存資産

各描画が独立した評価位置を選び、同じVR観測者の左右眼は共通位置を使えるかを検証する。
S1の正準Head Proxy **B**、合成rig、rest校正、float readbackを再利用した。
2 Head軸と子Proxy / 直接VRCParentConstraint Proxyを比較する。
CSVの`B_ConstraintProxyRoot`はS1の名称を継承しているが、今回のcomponentは直接VRC Constraint。

S1はHead Frame限定PASSであり、3体の画像比較20 FAILは未解決。
今回の右側だけの小さな診断変位は、符号と共通weightを確認するprobeである。
pre-skin delta、混合weight整合、v0.1互換GPU変形、NDMF/lilToon製品統合は実装していない。

## 2. 公式仕様・Unity/SDK実装の監査

### Unity標準とVRChat独自Globals

[VRChat Shader Globals](https://creators.vrchat.com/worlds/udon/vrc-graphics/vrchat-shader-globals/)は
保護された`_VRChat`名前空間を定める。独自変数は作らず、文書化済み変数を宣言して読む。
模擬試験は別Shaderの`_VLatTest*`へ入力し、Unity/VRChat globalsを上書きしない。

| 変数・提供元 | 文書/実装で確認した意味 | 有効性・未確認点 |
| --- | --- | --- |
| `_WorldSpaceCameraPos` / Unity | 描画cameraのworld位置。Stereoではeye別位置へのaliasになる | Monoでもworld原点0は有効。Shadow時はlight位置とは限らない |
| `unity_StereoWorldSpaceCameraPos[2]` / Unity | Stereo左右位置の配列。world空間 | `USING_STEREO_MATRICES`が成立するvariantに限定。Mono/multipassで読めると仮定しない |
| `_VRChatMirrorMode` / VRC | 0通常、1 VR mirror、2 Desktop mirror | 未知値・他modeとの組合せは別診断 |
| `_VRChatMirrorCameraPos` / VRC | Mirror cameraのworld位置。VRでは眼に依存しない中心。Mirror外は0 | active mirrorでも0ならsentinelと有効な原点を区別できない |
| `_VRChatCameraMode` / VRC | 0通常、1 VR handheld、2 Desktop handheld、3 screenshot | flag単独でStereoやcamera poseの有効性を保証しない |
| `_VRChatFaceMirrorMode` / VRC | face mirror時1、それ以外0。VR/Desktopは異なるcamera types | MirrorModeとの組合せ、pose供給関係は文書だけでは未規定 |
| `_VRChatScreenCameraPos` / VRC | main screen cameraのworld位置、inactive時0 | 現在の描画が撮影camera等なら別位置。VR共通眼中心という保証は明記されていない |
| `_VRChatPhotoCameraPos` / VRC | handheld photo cameraのworld位置、inactive時0 | activeでも原点0との区別不可。Screenshotの評価位置と同一とは規定されていない |

上のVRCの値・sentinelは公式文書の記述。**実avatarの各passでの実測保証ではない**。
Screen/Photoの非ゼロ値も、現在描画との一致やstaleでないことを独立に証明するものではない。

Unity側は[標準Shader変数](https://docs.unity3d.com/2022.3/Documentation/Manual/SL-UnityShaderVariables.html)、
[Stereo instancingのmacro手順](https://docs.unity3d.com/2022.3/Documentation/Manual/SinglePassInstancing.html)と
インストール済み22f1の`CGIncludes/UnityShaderVariables.cginc`を照合した。
`UNITY_SINGLE_PASS_STEREO` / `UNITY_STEREO_INSTANCING_ENABLED` / `UNITY_STEREO_MULTIVIEW_ENABLED`から
`USING_STEREO_MATRICES`が決まり、camera位置が`unity_StereoEyeIndex`へ依存する。
vertexでinstance/eyeを初期化し、fragmentでeye indexを復元する。平均位置の選択は両stageで同じ関数を使う。

### SDKで実際に読んだ範囲

取得元SDK base/avatarsは**3.10.5**。公開ソースを検索し、baseの
`Runtime/VRCSDK/Sample Assets/Shaders/Mobile/ToonStandard/CG/Helpers.cginc`の`GetStereoViewDir`を確認した。
同関数もMirror位置を優先し、Stereo行列がある場合は左右位置の平均、それ以外は通常camera位置を使う。
これはSDKのToon照明用view directionの実装であり、VLatのHead起点角度・各pass・実clientの保証とは別。

検索したbase/avatarsの公開C#/HLSL/Shaderソースでは、残りのcamera globalsのclient書込処理は確認できなかった。
閉じたclient側実装やSDK DLL内部まで確認したという意味ではない。SDK readerの存在から
Face Mirror、Screenshot、Head Chop、全avatar passの挙動を断定しない。
資料・SDK sample・Unity includeのfingerprintを保存結果に記録する。

## 3. View Contextの選択方針

1. mode値の範囲・整数性を確認する。未知modeやMirror+撮影modeの同時指定は拒否する。
2. MirrorModeが1/2なら**Mirror位置を優先**する。Stereo配列・Screen・Photoで置換しない。
   yaw符号の反転は加えない。Face flagもある場合はface contextと未確認warningを付ける。
3. Face flagだけがありMirrorModeが0なら、position供給を証明できないため拒否する。
4. その他は、その描画のStereo配列があるなら**左右位置の算術平均**。
   ない場合は描画camera位置を使い、**Mono/multipass未判別warning**を必ず出す。
   `_RequireStereoCenter=1`では配列なしを拒否する。Desktopも拒否されるため、これは厳格診断用の設定。
5. Handheld/Screenshotのcontextを分類する。評価元は現在のdraw/その両眼中心。
   Screen/Photo globalsは照合情報であり、現在描画を無条件に置き換えない。
   Photo inactiveや位置不一致をwarningへ出す。この補助位置を評価元として失敗後にfallbackする方式ではない。
6. selected pointをworld→Object→固定rest校正でHead Frameへ変換する。
   `yaw=atan2(x,z)`、`pitch=asin(y/length)`。原点と一致・非有限値は拒否する。

校正はS1と同じ`K=F0^-1 M0`を3 Vector propertyへ**1回だけ**設定する。
毎frameのHead行列・CPU角度・観測者情報をC#から送らない。観測者ごとのnetwork同期も使わない。
Mirror用viewを別符号として特例処理せず、提供world位置を同じHead Frameで評価する。

### Stereoの未解決条件

配列がないvariantでは、通常DesktopとVR multipassを信頼できる方法で区別できていない。
標準draw位置を左右別に使う負対照は、今回実際に異なる角度/weightになった。
警告を出すだけではVR共通shapeの保証にならないので、**そのVR経路は未対応/NO-GO**。
Screen位置を共通眼中心と仮定する代替や、projection行列だけによるVR判定は導入していない。

### 正投影

既定のposition policyは「Head→camera位置」を評価し、parallel cameraでも横移動で角度が変わる。
これは透視cameraのルールと揃うが、同じ投影方向の見た目と一致するとは限らない。
方向policyでは`-camera.forward`をworld→Headへ**w=0**で変換し、parallel移動で評価が変わらない。
Unity view行列のZ反転を考慮して第3行の方向を使う。
Stereo時の方向候補は両eyeのview方向の和を正規化する。
Mirrorの正投影方向policyは今回拒否する。実face mirrorをこの候補に割り当てたとは扱わない。

### 診断状態

| status | 意味 |
| --- | --- |
| 0 | 定義したpolicyで評価可能。別途warnings/sharedを確認する |
| 1 / 2 | 未知mode / 競合mode |
| 3 / 4 | Mirror位置0で有効性不明 / Face Mirrorのframe供給未定義 |
| 5 | 厳格Stereo設定だが配列なし |
| 6 / 7 | 非有限値 / Head起点と一致等で方向未定義 |
| 8 / 9 | Mirror正投影方向の未対応 / 空のStereo位置入力 |

warningのbit値は1: Mono/multipass未判別、2: Photo inactive、4: 補助位置不一致、
8: 正投影position policy、16: Face/Mirror組合せのclient未検証。
無効入力はmagentaで表示し、試験変位のweightを0にする。
context/source/status、警告、選択位置、raw mode、eye色を別modeで診断できる。

## 4. 環境と試験設計

- [公式対応Unity](https://creators.vrchat.com/sdk/upgrade/current-unity-version/) **2022.3.22f1 (887be4894c44)**。
  Windows 11、RTX 3060 Ti、D3D11、Built-in、Gamma project、64×64 Linear ARGBFloat RT、MSAAなし。
- SDK base/avatars **3.10.5**を既存projectから読み取り複製。取得元Assets・設定・アカウントを持ち込まない。
  ローカル導入版を固定したもので、SDK最新版という主張ではない。
- `XR.enabled=False`、loaded deviceなし。**実XR constant-buffer / render target array bindingはNOT RUN**。
  Editor/Play Modeを実Player GPU skinningの証明にしない。
- 2 Head軸、B子Proxyと直接VRC Proxy、Rest/HeadYaw/BodyYaw/NeckHead、7 view、26 scenario、2仮想eye、
  Edit/Play Modeで**11,648ケース**。pose後は標準SDK更新を3frame以上待つ。
- `UnityCamera`は実Camera.Renderによる標準bindings。
  `Injected`は専用Shaderの明示入力であり、通常Unity Camera2台による仮想eye projection。
  実HMD/SPI描画やVRCMirrorReflectionの代替として数えない。
- Mirrorの模擬位置はworld平面にcamera位置を反射させたもの。
  reflection texture、oblique clipping、Mirror renderer/Face Mirror実装は実行していない。
- 右側頂点だけを正yawのweightで最大0.04 m動かすprobeを画像に使う。
  左右非対称で、頂点とfragmentのweightを別途readbackする。
  数値測定ではprobe変位0。全skin/shapeの互換性試験ではない。

CPU参照は別に記述した契約caseと倍精度の角度・weight計算。
さらにPython checkerが保存Head quaternionとworld位置から角度を独立に再計算する。
暫定閾値は角度0.05°、選択位置10 µm、weight 0.0001、共有eyeの差1e−5。
色PNGは8bitの診断画像であり、精密判定にはfloat readbackを使う。
同期readback、CSV/配列生成はEditor計測用。GPU costやRuntime GC 0のベンチマークではない。
Head共回転試験は評価点の位置を共回転させるもので、cameraのrollやHMD光学projectionを一致させる試験ではない。

## 5. 自動試験の結果

新規stagingのfresh importでcompile・Edit/Playを完走し、Stereo分岐のcompile assertionを追加して
最終版も終了code0まで再実行した。**11,648行中10,752 PASS / 896 FAIL**。
PASSの内訳は有効位置/角度評価7,168と期待どおりの拒否診断3,584。
FAIL896は全て眼別評価・Mirror誤反転の負対照。実clientのPASSは0件。

| 数値・整合検証 | 最終結果 |
| --- | --- |
| Edit / Play | 各5,824行、各5,376 PASS / 448負対照FAIL |
| evidence別 | 実UnityCamera **2,688 PASS**、Injected **8,064 PASS / 896負対照FAIL** |
| 最大yaw / pitch誤差 | **0.000671387° / 0.003887254°**（CSVの記録値） |
| 選択world位置の最大誤差 | **6.802507e−7 m**（float readbackの記録値）。CSV丸め後の独立再計算最大1.414214e−6 m |
| vertex/fragment weightのCPU差 | 最大**1.615286e−5**、閾値0.0001以下 |
| 共有両眼 | **1,344 pair**、yaw/pitch差0、vertex weight差最大**1e−7**、fragment weight差0 |
| Head+camera同時回転 | 2軸・2方式・7view・両eyeで4poseの角度不変性 **PASS** |
| 正投影 | position / direction両policyの実Unity camera試験 **PASS** |
| PNG | **84枚 PASS**、CRC/payload/coverage/実測RGBとの一致を検証（RGB 1段階以内） |
| Shader / C# | 自作CS error/warning 0、4 Shaderのmessageは両phaseとも空 |
| Stereo keyword compile-only | SPS / SPIを指定し、native Stereo行列/instancing macro分岐をassertしてwarmup **PASS**。bindingはNOT RUN |
| 結果checker | grid/Head quaternion/位置選択/角度/weight/pair/拒否/画像/分類を再検査 **PASS** |
| 破損入力 | 行不足/重複/偽負対照PASS/client誤分類/CPU角度/両眼差/PNG/compile message/SDK版/S1 FAIL破棄を**10/10拒否** |

vertex weightはvarying経由のreadbackであり、interpolation/丸めも含む。差1e−7を実vertex演算の眼別差と断定しない。

初回のcompile-only probeではsource内defineがUnityの自動includeより遅く、Stereo変数の宣言不整合を検出した。
正規のkeyword variant指定へ修正した。directionの初期化warningも修正し、最終のcompile messagesは空。
ライセンスclientのsignature/token更新エラー等はログに残るため、全Console無警告を意味しない。
中間の未完成測定を最終PASSへ数えていない。

保存結果: [summary / CSV / 画像](../../experiments/VRCViewContext/results/2026-10-17-unity2022.3.22f1/summary.md)。
SDK/モデル/私有画像は同梱せず、PNGは全て合成6頂点fixtureの診断画像。
判定は実camera / 模擬入力 / compile-onlyを分ける。

| 対象 | Unity側の試験 | 実VRChat |
| --- | --- | --- |
| 通常Desktop相当 | **PASS**：実Unity camera位置とHead角度を比較 | **NOT RUN** |
| VR両眼共通中心 | **PASS（模擬）**：注入平均、両stageのweightと両眼pair | **NOT RUN** |
| VR/Desktop Mirror | **PASS（模擬）**：Mirror中心優先、符号反転なし。誤反転はFAIL負対照 | **NOT RUN** |
| Handheld / Screenshot | **PASS（模擬）**：現在draw/共通中心、Photo/Screen decoy | **NOT RUN** |
| Face Mirror | 診断policy **PASS**、実対応は**INCONCLUSIVE**。Face単独を拒否 | **NOT RUN** |
| 複数観測者 | **PASS（Unity）**：独立Camera2台/位置。network実証とは別 | **NOT RUN**（network/2 client未実施） |
| Head回転 | **PASS（Unity）**：固定viewの変化、Head+camera同時回転の不変性 | **NOT RUN** |
| 正投影 | **PASS（Unity）**：position/direction policyを比較 | **NOT RUN**（VRC各mode） |
| Head Chop | **NOT RUN** | **NOT RUN** |

未知mode・Face単独・Mirror位置0・Stereo欠損・NaN・Head起点の各caseは、期待どおり診断すると試験PASS。
そのcamera modeを利用可能としてPASSした意味ではない。
負対照`NegativePerEye` / `NegativeMirrorSign`は各**448 FAIL**。
例えばrest/world yaw45°では、通常camera約45°に対して反射点を使うMirrorは約119.93°。
単純なyaw反転ではこの位置関係を表せない。
同じ条件の正投影はposition policy約36.47°、direction policy約45°になり、横移動の定義差を確認した。

## 6. VRChat実機の結果と必要な証拠

**全項目NOT RUN**。診断avatarのupload、認証、実client session、HMDの左右capture、2観測者は操作していない。
`client-results.json`と記入用CSVを用意し、模擬データで実機の欄を埋めていない。
具体的準備・private upload前の人間手順は
[README](../../experiments/VRCViewContext/README.md)の「VRChat実機の準備と証拠」を参照。

必要な証拠は、client/Unity/SDK/world/HMD版、B子/Constraintの階層、Head Chop on/off、
mode/source/status/共有weight、左右eyeを区別できるcapture、異なる観測者の同時画像。
Mirrorの画像左右反転をyaw符号反転と混同しない。撮影cameraを動かすときmain viewと独立して変わることを確認する。
PNGだけでは0.05°精度を証明できない。高精度検証にはworld側float RT/公開readback等の別計測を要する。
これは今回実装・実行していない。

[Head Chop公式説明](https://creators.vrchat.com/avatars/avatar-components/vrc-headchop/)では
Headのnear-zero scaleと子への影響が示されている。子Proxyのframeやgeometry/cullingへどう作用するかは
今回の通常回転試験から結論できない。Constraint版も自動的に回避できると仮定しない。

## 7. S1から残る画像FAIL：S1-SURFACE-20

**20 FAILと0.1 mm閾値を維持。原因INCONCLUSIVE。** 読み取り専用trackerで条件を集約した。
最初のsurface測定と追加vertex probe後の再測定で、同じ20条件・最大誤差が再現している。
各行のphase/model/camera/methodは`s1-carryover.json`に保存する。

| model / pose / camera world(yaw,pitch) | method | 各phaseの件数 | world位置画像差 |
| --- | --- | ---: | ---: |
| Shinano / HeadPitchRoll / (−45,0) | A、B子、B VRC、C | 4 | 約0.18464〜0.18473 mm |
| Shinano / Blink（CSV: BlendShape） / (45,0) | B子 | 1 | 0.281930 mm |
| Shinano / HalfScale / (45,0) | B子 | 1 | 0.140965 mm |
| Shinano / DoubleScale / (45,0) | B子 | 1 | **0.563861 mm** |
| Shinano / DoubleScale / (45,30) | A | 1 | 0.353006 mm |
| Komano / Rest / (45,30) | C | 1 | 0.118891 mm |
| Komano / BodyYaw / (0,0) | C | 1 | 0.107839 mm |

Edit/Play各10件、合計20。Yumekaは該当0件。
0.564 mmは変位量や画像RGB差ではなく、**同じpixelで読んだbaseline/candidateの補間world位置の距離の最大値**。
S1は64×64 float RT、共通coverage pixelで比較し、最も大きい3D距離をworld_error_mへ記録した。
そのpixelが同じ三角形・同じsurfaceに属した証拠は未取得。

| 差の段階 | 現在分かること / 原因候補 |
| --- | --- |
| Material / Lighting | Mode8はunlitのworld位置出力。色・texture・lightingの差を直接測っていないため、通常の照明差による説明は弱い |
| Normal / Tangent | 同診断Shaderは位置のみを入力にし、fragmentでも使わない。直接の陰影差とは別。一般toonのnormal保持は未検証 |
| Skinning / matrix | 別point-topology drawの全頂点GPU差は最大0.000717 mm。root変換の微小float差はある。point/triangleで同一backendである証明はない |
| Camera / timing | 同じCamera/pose/viewで比較し、2回の独立測定で再現。三角形/深度の対応や厳密な時点差までは証明していない |
| Rasterization / depth | 近接・重なり面、境界pixel、depth量子化等で別surfaceが選択される可能性。primitive ID/深度未測定のため未確定 |

**実FBX全頂点のCPU skin一致を得たわけではない**。
S1実FBXの全頂点probeはGPU baseline対GPU候補（別topology）、CPUは角度参照。
合成6頂点のCPU skin/Bake一致と区別する。S2もこの未検証範囲を成功へ昇格しない。

次の切り分けは、失敗pixel座標、baseline/candidateのworld・深度・primitive IDを同時取得し、
勝ったsurfaceを比較すること。triangle topologyを維持した位置probeと実FBXのCPU LBSも追加候補。
短時間で根本原因を証明できなかったため、今回のS2 view選択から独立した未解決課題として残す。

## 8. Go/No-GoとS3移行条件

未検証の前提・リスク:

- Stereo配列が現在のphoto/screenshot cameraへ正しくbindingされるか、staleなscreen eye情報が残らないかは実機未確認。
  宣言やcompile成功だけでは保証しない。Photoとの位置差はwarningとして見える。
- Face Mirrorのmode組合せ・正投影・pose供給、Head Chopによる子Proxy/Constraintの差は未確認。
- 今回はPC D3D11の単一unlit pass。実XR、GL/Multiview、Outline/Shadow/Depth、world固有camera、
  最適化後のframe、製品bounds/culling、GPU costを検証していない。

- **限定PASS**: 定義したpolicy、Unity通常camera bindings、模擬Stereo/Mirror/Camera、Head回転、
  共通weight、診断拒否、左右非対称probe。compile-onlyは別枠。
- **NO-GO**: 実VRChatのS2出口、動的製品採用、S3の実アバター統合へ移行したという判定。
  合成fixtureの数学実験を別途行う余地はあるが、S2実機完了ではない。

S3本検証への条件:

1. 実Desktop / VR左右眼 / VR・Desktop Mirror / Camera・Screenshotでsourceと角度を確認する。
   VRの左右眼は同一weight。配列なしVR経路を解決するか、未対応として明示的に止める。
2. Face Mirrorの実flag/pose供給を確認し、対象範囲を決める。未知の組合せをsilent fallbackしない。
3. 2人の別視点とHead Chop/一人称/第三者でHead Frameの保持を確認する。
4. S1-SURFACE-20の原因と無効時形状保持を証明する。閾値の引上げだけで解消しない。
   別の比較方式を採る場合も根拠と元FAILを残し、採否を改めて判断する。
5. その後、Head100%領域の1キーdeltaでCPU参照とGPUを比較する。
   Eye/Neck/Tongue等の混合weight、normal/tangent、Bounds/各passは別ゲート。

旧Phase 2D-1人間試験、62f3回帰、Phase 1 actual pair、延期中の需要調査も未完了のまま。
S2の限定PASSでこれらを完了にしない。

## 9. 変更ファイル

- `experiments/VRCViewContext/Overlay/ViewContextCore.cginc` / `ViewContextLive.cginc` / `ViewContextDiagnostic.cginc`
- 同overlayのlive / Injected / StereoCompile / InstancedCompile Shader4件
- `Overlay/Editor/ViewContextCases.cs` / `ViewContextSweep.cs`
- `prepare_view_project.py` / `track_s1_failures.py` / `README.md`
- `client-results.json` / `client-evidence-template.csv`
- `results/2026-10-17-unity2022.3.22f1/`（実測CSV/画像/依存/ログ/checksum/未解決FAIL集約）
- `scripts/check_view_context_results.py`、本報告、`PROGRESS.md`

S1の保存済み実験・数値・判定は変更していない。
