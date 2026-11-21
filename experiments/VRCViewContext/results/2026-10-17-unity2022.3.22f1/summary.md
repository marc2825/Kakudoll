# P0-S2 実測 — 2026-10-17 JST

Windows Unity2022.3.22f1 / SDK base+avatars3.10.5 / Built-in / D3D11 / RTX3060Ti。
新規TEMPでfresh import後、Stereo分岐のcompile検査を含む最終runnerを完走。終了code0。
XR.enabled=False、deviceなし。実clientやHMDの描画を実行した結果ではない。

- 全**11,648行**、**10,752 PASS / 896 FAIL**。
  有効位置/角度評価7,168、期待どおりの拒否3,584。
  FAILは全て`NegativePerEye` / `NegativeMirrorSign`（各448）の負対照。
- evidence: UnityCamera **2,688 PASS**、Injected **8,064 PASS / 896負対照FAIL**。
- 最大角度誤差**0.003887254°**、記録された最大選択位置差**6.802507e−7 m**。
  CSV丸め後の独立再計算では位置差最大1.414214e−6 m、角度0.003887254095°。
- vertex/fragmentのCPU weight差最大**1.615286e−5**。
- 共有eye **1,344 pair**、yaw/pitch差0、vertex weight差最大**1e−7**、fragment weight差0。
  注入した位置による通常Camera2台の結果で、実XR binding/VR画像ではない。
- Headと評価点の共回転不変性、world視点固定でのHead回転、正投影の位置/方向policyを確認。
- **PNG84枚**：合成6頂点fixtureの色/右側probe/モード表示。
  checkerでPNG CRC/RGB payload・covered pixel・float値との色一致（RGB 1段階以内）を検査。
  PNGの8bit色を高精度な角度測定に使用しない。
- 自作CS error/warning0、4 Shaderのmessageは両phaseとも空。
  SPS/SPIのkeyword compile probeはnative Stereo/instancing分岐をassertしてwarmup PASS。
  constant-buffer/texture-arrayの実bindingや実XR drawは**NOT RUN**。
- checker PASS。破損入力10種類を全て拒否。prepare guard4ケース拒否。
  Schema正例2/負例7 PASS、S1既存3 checker PASS、S1 checksum59/12/27件不変。
- 最初のcompile-only probeはsource defineとUnity自動includeの順序で失敗したため、
  正規keyword指定へ修正。direction初期化warningも修正。中間試行は最終結果へ加算していない。
  ライセンスsignature/token更新エラー等は別扱いでログに記録。

client-results.jsonは全項目NOT RUN。入力分類、metadata、依存lock、source hash、実測logを保存。
画像は実VRChat/Mirror/HMDのスクリーンショットではない。SDK/モデル/私有画像/認証情報は同梱しない。

S1-SURFACE-20は**20 FAIL / 原因INCONCLUSIVE**のまま。s1-carryover.jsonに全条件と
独立測定の再現性を保存。0.1 mm閾値、最大0.563861 mmを変更していない。
実FBXのvertex probeはGPU対GPUであり、実FBX全頂点CPU skin一致を得たわけではない。

**限定S2 PASS、実機S2/製品採用NO-GO、S3本検証への移行NO-GO**。
詳細・mode別status・実機手順・残る条件は
[実験報告](../../../../docs/experiments/VRC_P0_S2_VIEW_CONTEXT.md)と
[再現README](../../README.md)を参照。
