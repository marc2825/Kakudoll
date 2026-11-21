# P0-S1-R1 — 形状画像差20件の原因調査

2026-10-17。開始HEAD `5022f58`、worktree cleanを確認し、S2成果をコミットで保全した状態から
`spike/vrc-s1-image-fail-investigation`を作成。push/PR/merge/認証/uploadなし。

## 1. 元の20 FAILと再現

元S1/S2記録と0.1 mm閾値は変更しない。今回の元ハーネス再実行はEdit/Playの全2,016行で
保存済みCSVと各fieldの数値が一致した。Head角度1,344/1,344、全頂点probe288/288はPASS。

| Phase | Avatar | Pose | Method | World yaw/pitch | 元/再現 最大差 mm |
| --- | --- | --- | --- | --- | ---: |
| EditMode | Shinano | HeadPitchRoll | A_HeadRoot | -45/0 | 0.1846413 |
| EditMode | Shinano | HeadPitchRoll | B_ChildProxyRoot | -45/0 | 0.1847267 |
| EditMode | Shinano | HeadPitchRoll | B_VrcProxyRoot | -45/0 | 0.1846433 |
| EditMode | Shinano | HeadPitchRoll | C_RebasedChild | -45/0 | 0.1846638 |
| EditMode | Shinano | BlendShape | B_ChildProxyRoot | 45/0 | 0.2819303 |
| EditMode | Shinano | HalfScale | B_ChildProxyRoot | 45/0 | 0.1409652 |
| EditMode | Shinano | DoubleScale | B_ChildProxyRoot | 45/0 | 0.5638606 |
| EditMode | Shinano | DoubleScale | A_HeadRoot | 45/30 | 0.3530061 |
| EditMode | Komano | Rest | C_RebasedChild | 45/30 | 0.1188905 |
| EditMode | Komano | BodyYaw | C_RebasedChild | 0/0 | 0.1078390 |
| PlayMode | Shinano | HeadPitchRoll | A_HeadRoot | -45/0 | 0.1846413 |
| PlayMode | Shinano | HeadPitchRoll | B_ChildProxyRoot | -45/0 | 0.1847267 |
| PlayMode | Shinano | HeadPitchRoll | B_VrcProxyRoot | -45/0 | 0.1846433 |
| PlayMode | Shinano | HeadPitchRoll | C_RebasedChild | -45/0 | 0.1846638 |
| PlayMode | Shinano | BlendShape | B_ChildProxyRoot | 45/0 | 0.2819303 |
| PlayMode | Shinano | HalfScale | B_ChildProxyRoot | 45/0 | 0.1409652 |
| PlayMode | Shinano | DoubleScale | B_ChildProxyRoot | 45/0 | 0.5638606 |
| PlayMode | Shinano | DoubleScale | A_HeadRoot | 45/30 | 0.3530061 |
| PlayMode | Komano | Rest | C_RebasedChild | 45/30 | 0.1188905 |
| PlayMode | Komano | BodyYaw | C_RebasedChild | 0/0 | 0.1078390 |

Yumekaの正候補には元FAILがない。Edit/Play各10件、方式A4/B子8/B VRC2/C6件。

## 2. 測定量の監査

元`HeadFrameDiagnostic.shader`のmode8は、VSで求めたworld位置をTEXCOORDへ出し、
fragmentで`float4(i.world,1)`を返す。64×64 Linear ARGBFloat/depth24/MSAA1、Forward/Cull Off。
`HeadFrameCapture.CompareGeometry`は両画像のalpha>0.5の共通画素に対して
`max_pixel ||world_baseline(pixel) - world_candidate(pixel)||_2`を求める。
単位m、許容0.0001 m、coverage不一致は2画素以下。全20件のcoverage不一致は0。

これは画面上の補間表面点の比較であり、色差、頂点変位、同じ3D点の追跡ではない。
三角形IDは元ハーネスでは未計測。同じIDでもrayとの交差点とfragment補間値が一致するかは別に検証が必要。

追加`RealAvatarVertexProbe`は別コピーのtopologyをPointsへ変え、全vertex IDへRT画素を割り当てる。
元S1の実FBX結果はGPU baseline対GPU候補であり、全頂点CPU skin参照の実測ではなかった。
今回、全weightと選択blink frameを用いた独立CPU LBSを追加した。

## 3. 調査環境と隔離

Windows/Unity2022.3.22f1/SDK base・avatars3.10.5/Built-in/D3D11/RTX3060Ti。
元62f3 project、Core/Runtime/Editor、v0.1、元FBX/Mesh/Material/Prefabは変更しない。
元ハーネス再現用TEMPと調査overlay用TEMPを新規作成し、元SDK/FBXは読取専用で複製。
実VRChat、実Player、HMD、別GPU/API、toon製品PrefabはNOT RUN。

## 4. 根拠と実験方法

[Direct3Dのrasterization規則](https://learn.microsoft.com/en-us/windows/win32/direct3d11/d3d10-graphics-programming-guide-rasterizer-stage-rules)
はcoverageと頂点属性の補間を区別している。
[HLSL semantics](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-semantics)
のSV_PrimitiveIDを使い、元の三角形index/submeshを維持したままIDを出力する。
文書から今回の誤差原因を断定せず、実測で照合する。

world/ID/depth/normal/単色を同一Cameraの別drawで取得。大差画素の三角形をfragment discardで
単独描画し、同じ画素が残るかと深度を確認する。point probeの当該頂点からdouble ray-plane参照も求める。
絶対world補間値、surface対応、vertex位置の3つを分ける。

診断variantはBaseline再描画、元Shaderとの一致、world→clip式、Cull Back、ZTest Always、
BakeMeshの静的描画、128/256解像度、blink OFF、Head100%weights。VLat変形は全てゼロ。

最終runnerのEdit/Play測定とShader auditは終了code0。中断・未実行の起動は受入結果へ数えない。

## 5. 根本原因

**今回の20件は、D3Dのサブピクセル座標丸めにより、同一三角形内の補間係数が変わる現象。**
分類は**レンダリング仕様による差 + ハーネスの測定量の意味論**。
VLatの変形バグ、誤ったBindPose、別の三角形の深度勝敗、照明差として説明する証拠はない。
旧ハーネスの数値計算自体は正しく、実在するrasterized attribute差を検出した。
これを同一点の幾何変位と解釈することが不適切だった。

[D3D11.3 Functional Specification §3.4.1](https://microsoft.github.io/DirectX-Specs/d3d/archive/D3D11_3_FunctionalSpec.htm)
は、viewport変換後のXYを小数8bitの固定小数点へ丸め、属性補間にもその座標を使うと規定する。
float演算の微小差でも、丸め境界を跨ぐと**1/256 pixel = 0.00390625 pixel**の段差となる。
細い投影三角形では、その段差によりperspective-correctな重心係数が変化し、
表面に沿った補間world位置の差が頂点位置差より大きくなる。

実測で確認した因果関係:

1. 全20ケースのCamera View/Projection matrix差0、baseline再描画差0。
2. 元mode8と新probeのworld画像はbaseline/candidateとも全画素で一致。
   target4.5への変更やnormal varying追加で発生した別の差ではない。
3. 大差は合計28画素。全て同じslot・同じprimitive IDで、単独三角形描画にも同じ差が残る。
   「別surfaceを拾った」とする当初仮説は今回の28画素では棄却する。
4. Shaderからclip XYZWを別probeで読み、viewport変換・1/256丸め・perspective補間をdoubleで再現。
   RTのY方向は2規約を照合し、全28画素で同じflip規約が成立した。画素ごとに丸め幅等を調整していない。
   全28画素でbaseline/candidateの丸め先が一段変わる。
   この予測は実fragment world位置へ1 µm未満で一致し、丸めなしの計算より十分よく説明する。
5. 同じ三角形の**baselineの重心係数をcandidateへ固定適用**すると幾何学的な差は1 µm未満。
   独立CPU LBSと全vertex IDのGPU位置比較も元0.1 mm閾値を満たす。
6. 解像度128/256ではこの20条件が旧surface閾値内へ戻るが、64条件のFAILは維持する。
   解像度変更を修正・合格の根拠にしない。

例えば最大差のShinano/DoubleScale/B子/(45,0)は画素(46,49)。
三角形の1頂点の丸めYが`49.4453125 → 49.44140625`へ変わる。
raw surface差0.563861 mmを丸め後の補間計算が説明し、同じ重心座標での差は約0.000451 mm。
表情・HalfScaleの同じ画面座標にも同じ丸め段差があり、world単位の差はscaleへ比例する。

これはGPU位置がPASSだったという消去法だけではない。
**同じ三角形の選択、単独描画、clip丸め先、補間予測、固定重心係数**を直接照合した根拠である。
数値予測の1 µmは追加のモデル再構成精度の検査で、旧0.1 mm閾値を変えるものではない。

## 6. 仮説の切り分け

以下のPASSは「その仮説が原因」という意味ではなく、記述した照合・対照の成功を表す。

| 順 | 仮説 | 観測と判断 | 検証状態 |
| --- | --- | --- | --- |
| 1 | Camera projection/view不一致 | 同じCamera、両matrix差0、baseline repeat差0。原因として棄却 | PASS |
| 2 | SMR/BindPose/Transform不整合 | A/B/C、全頂点GPUと独立CPU LBSが閾値内。Head100%でも旧差が残る。大きな幾何不整合では説明できない | PASS（測定範囲） |
| 3 | GPU頂点変換 | 微小float差は実在し、丸め境界跨ぎの入力となる。world頂点が0.564 mm動いたわけではない | PASS（実測・原因連鎖） |
| 4 | Normal/Tangent | 元mode8は入力にも使わないため直接原因ではない。normalの補間値には最大約0.00975のベクトル差があり、toon陰影保持は別ゲート | PASS（直接原因の除外）、製品整合NOT RUN |
| 5 | Material/Shader | 専用unlit、元probeと新probeは完全一致、単色RGB差0。元製品Materialの検証は含まない | PASS / 製品NOT RUN |
| 6 | Lighting/Shadow/Outline | 元mode8は照明計算も追加passもない。実toon各passは未実行 | 直接原因から除外 / NOT RUN |
| 7 | Depth/Z-fighting/Culling | 28画素全て同じprimitive、単独描画でも差、Cull Backでも20 FAIL。ZAlwaysは別の可視surfaceになるので単独の成功を修正扱いしない | PASS（切り分け） |
| 8 | Resolution/MSAA/Rasterization | MSAAは元から1で無効。128/256で20条件PASS、1/256丸め予測が28画素を再現 | PASS。MSAA4等はNOT RUN |
| 9 | 計測アルゴリズム | 画素属性差を同一点の変位へ読み替えた意味論が問題。二種類の量を分離して記録 | 修正・再検証 |

blink OFFでも対象B子の旧差が残り、Head単一boneでも20条件に旧FAILが残った。
静的BakeMeshでは16条件PASS/4条件FAILとなり、skinningの有無だけでは不連続を説明できない。
A/B/Cごとの件数差も、丸め境界を跨ぐfloat値の違いとして各画素の予測で説明できた。

## 7. 最小修正と前後比較

製品コード、元S1/S2のShader/runner/結果、既存閾値は変更していない。
新しい実験ハーネスに以下を追加した。

- `raw surface`を描画属性の安定性として保持する。
- `matched barycentric`を同じ三角形上の対応点の幾何差として別に測る。
  各共通画素でbaselineのperspective補間係数をcandidateへそのまま適用する。
  primitive不一致・coverage欠落を無視せず、別途FAILにする。
- CPU LBS、clip readback、サブピクセル予測、1 mm破損負対照で根拠を検証する。
- 前後の差分画像も別名で保存し、**測定量の変更であって描画の修正ではない**と明記する。

`original-difference.png`は旧画素world距離/0.1 mm、`correspondence-difference.png`は
同一重心係数のworld距離/0.1 mm。両方Rをclamp表示し、精度判定にはfloatとJSONを使用する。
表示の黒化を見た目の完全保持やold FAIL消滅と主張しない。

| 比較量 | 最終結果 | 判定 |
| --- | ---: | --- |
| 元raw surface（修正前と同じ指標） | 最大0.563860638 mm、20/20 FAIL | **FAIL維持** |
| 同一重心係数の幾何対応（追加指標） | 7,842/7,842共通画素、最大0.000479933 mm | **20/20 PASS** |
| 全頂点GPU baseline対candidate（当該20条件） | 最大0.000526625 mm | PASS |
| 独立CPU LBS対GPU全vertex ID（当該20条件） | 最大0.000727806 mm | PASS |
| 1/256丸めモデル対fragment | 28画素、最大0.000176888 mm | PASS（再構成精度1 µm以下） |
| 元Shader対新probe / Baseline再描画 | 全画素差0 | PASS |
| 1 mm Mesh破損負対照 | GPU頂点差最小0.500007 mm、全20件検出 | 20/20期待FAIL |
| 私有診断PNG | 240枚、CRC/payload/floatからの全画素色一致 | PASS |

対応点指標への変更は測定量の分離であり、0.564 mmが描画修正で0.000480 mmへ減ったという主張ではない。
旧raw gateとその判定は保存し、製品外観の確認へ持ち越す。

調査コード内の静的BakeMesh試験では、一度二重scale復元により共通画素なしを検出した。
元S1の注意事項に従い、Bake snapshotへRendererの位置・回転だけを戻すよう修正した。
これは**新設した補助ハーネスのバグ**であり、元20件の根本原因ではない。
最終全画素比較追加時のC#変数名競合も修正し、未完了試行を受入へ数えていない。

## 8. 未解決事項とゲート

20件の原因未特定というブロッカーは、今回のD3D11条件では解消した。
**旧の厳密surface比較20 FAILはそのまま**。通常のraster補間がbit単位で不変という保証もしていない。
normal補間も影響されるため、製品toon/shadow/outlineの外観安定性は未解決・NOT RUN。
renderer/root変更の可否を、幾何対応のPASSだけで製品向けに決めない。

- **S1: Head Frame・幾何対応のUnity限定PASS**。画像差の原因は特定済み。
  旧raw surface gateは20 FAILのまま別項目として残す。製品採用NO-GO。
- **S2実機検証: 条件付きGO**。既存のprivate診断手順でB子/VRC Proxy、Head Chop、
  Stereo/Mirror/撮影camera/二観測者を確かめることは進められる。認証/uploadは人間が行う。
- **S3本検証への移行: NO-GO**。S2実機出口、Face Mirror/VR multipass方針、Head Chopと各pass、
  raster属性差を含む製品外観の受入条件が残る。新しい幾何指標を旧gateへ自動置換しない。

実FBX全表情、Humanoid animation/IK、製品Prefab/PhysBone、非一様/負scale、別GPU/API、
実Player、62f3既存回帰、旧human gate、延期した需要分類は今回も完了扱いにしない。

## 9. 保存結果・テスト・変更ファイル

最終の数値と検証記録は `experiments/VRCImageDifference/results/2026-10-17-unity2022.3.22f1/summary.md`
（商用アバター由来の測定データを含むため非公開アーカイブのみ。公開用の集計は
[実アバター検証の要約](REAL_AVATAR_VALIDATION_SUMMARY.md)）。
再現は[README](../../experiments/VRCImageDifference/README.md)を参照。
私有モデル画像/float生データは隔離TEMPだけに残し、repoへは数値・hash・依存lockを保存する。

変更は本報告、`PROGRESS.md`、`experiments/VRCImageDifference/`、
`scripts/check_image_difference_results.py`に限定。main変更・push/PR作成なし。

実行・非退行の確認:

- 元S1実FBX sweep: 2,016行/288頂点probe、保存済みと全field一致。
- R1最終sweep: 20ケース/28大差画素/7,842共通画素/222診断variant、終了code0。
- R1 Shader audit: HeadFrameDiagnostic/SurfaceProbe/ClipVertexProbeのmessage空。
- 未変更S2をR1と同じassembly内で実行: 11,648行、10,752 PASS/896意図的FAIL、
  CSV全fieldと合成PNG84枚が保存S2へ一致。`s2-regression.json`にhashを記録。
- S1合成/SDK/実FBXの既存checker、R1 checker、v0.1 Schema正例2/負例7を再実行。
- 集約scriptは既存出力/入力project/SDK子/元FBX子の4 guardを全拒否。原本への書込みなし。
- R1 checker破損入力12/12を拒否。行欠落/重複、偽旧PASS、CPU異常、GPU座標改変、
  丸め先/予測改変、対応点最大値の偽装、coverage欠落、負対照削除/偽PASSを含む。
- FBX原本とコピー、主要SDK3 fingerprint、旧S1/S2の213 checksum、最終overlay source一致。
  fingerprintの範囲は保存JSONと既存S1報告に従い、SDK全バイナリの網羅監査とは扱わない。

最終私有画像の場所（ローカルのみ）:
`%TEMP%\VLatImageInvestigation-P0S1R1-20261017b\results-final-complete\private-images`。
各phase/条件に元差分・対応点差分・primitive・depth・normal・単色画像とRGBA32F生データがある。
非公開のまま証拠hashを保存し、元FBXを必要とする再現手順をREADMEに示した。
