# ROADMAP — Keyform v0.2 の実行時描画と VRChat 対応

2026-11-22（Issue #8）。公開時点で未実装のことを、順番と判断の基準つきで残します。
**ここに書いた段階は、どれも公開前の必須作業ではありません。** 現在は Pre-beta で、VRChat では使えません。

## 現在の状態

| 機能 | 状態 | 根拠 |
|---|---|---|
| Yaw ごとの Keyform 編集・Editor プレビュー・保存（v0.2） | 実装済み（Windows / Unity 2022.3.62f3 の自動試験） | README のテスト実績、[KEYFORM_V0_2.md](KEYFORM_V0_2.md) |
| Keyform の CPU 参照評価（`KeyformReferenceEvaluator`） | 実装済み（検証・編集用。毎フレームの Runtime ではない） | Core NUnit |
| v0.1 文書の Runtime 再生（`VlatApplier`、CPU） | 実装済み。単一カメラのみ | PlayMode 試験 |
| Keyform v0.2 の Runtime 再生 | **未実装**（`VlatApplier` は Keyform 文書を拒否する） | KEYFORM_V0_2.md §6 |
| GPU での視点依存変形 | **未実装**。Head Frame 取得と View Context の診断 Shader のみ | [S1](../experiments/VRC_P0_S1_HEAD_FRAME.md)、[S2](../experiments/VRC_P0_S2_VIEW_CONTEXT.md) |
| Unity 内での Head Frame・Stereo・Mirror の模擬 | 限定 PASS（Unity Editor 内。実機ではない） | 同上 |
| VRChat クライアント・HMD での動作 | **実機未検証**（V1〜V10 すべて NOT RUN） | [S2-R1](../experiments/VRC_P0_S2_CLIENT_VALIDATION.md) |
| 商用アバターでの形状画像比較 | 20 FAIL（D3D11 の丸め。モデルの欠陥ではない）を記録として保持 | [実アバター検証の要約](../experiments/REAL_AVATAR_VALIDATION_SUMMARY.md) |
| Pitch・法線/接線・複数メッシュ・LOD | **未実装** | 段階 6 |

表記のきまり: 「実装済み」は自動試験が PASS したもの、「限定 PASS」は Unity 内の模擬で PASS したもの、
「実機未検証」は VRChat クライアントや HMD で確かめていないもの、「未実装」はコードがないもの。
README とこの文書以外で状態を書くときも、この 4 つを使います。

## 段階

各段階は、前の段階の成功指標を満たしてから始めます。撤退条件に当たったら、その段階で止めて記録を残し、
方式を変えるか対応を見送ります。数値の閾値は測定の前に決め、結果を見てから変えません。

### 1. Editor プレビューと CPU 参照評価の照合（出力と座標系の凍結）

- 前提: 現在の Keyform v0.2 形式（KEYFORM_V0_2.md）。
- やること: Editor の 3 視点プレビューの頂点位置を、`KeyformReferenceEvaluator` と、それとは別に書いた
  独立 oracle（手計算した重み・頂点）で照合する。Yaw の符号、±180° 跨ぎ、support 端の扱いを固定する。
- 成功指標: 合成メッシュと Seed-san 相当の公開モデルで、全キー位置と中点の差が FLOAT32 丸め以内（1e-6 m 以下）。
  座標系の規則を文書に固定し、以後の段階はこれを正とする。
- 撤退条件: プレビューと参照評価の差を説明できない（式の取り違え以外の原因が残る）。その場合は先へ進まない。

### 2. Unity Player での最小描画 PoC（単一メッシュ・単一カメラ・Yaw のみ）

- 前提: 段階 1 の規則。Built-in RP、Windows Player。
- やること: 視点依存の変形を GPU（頂点 Shader または Compute）で適用する最小の実装。元の Mesh アセットは書き換えない。
- 成功指標: Player 上の描画結果を readback し、段階 1 の参照評価と位置が 0.1 mm 以内で一致する。
  7 万頂点で 1 フレームあたりの追加コストを測り、記録する（目安: CPU 0.3 ms 以下、GPU 側も測定値を残す）。
- 撤退条件: 元アセットを変更しないと実装できない、または一致条件を満たせない。

### 3. 複数カメラ・同時観察者・Stereo・Mirror

- 前提: 段階 2 の GPU 実装。
- やること: 観察者ごとに違う形を評価できるかを確かめる。左右眼は共通の評価位置を使う（現在の仕様 §7）。
- 成功指標: 2 台以上のカメラ・Stereo・Mirror で、それぞれの描画がその視点の参照評価と一致する。
  **CPU で 1 つの sharedMesh を書き換えるだけの方式は、この段階の解決とみなさない**（全観察者に同じ形が見えるため）。
- 撤退条件: 描画ごとに評価位置を分けられない。その場合は「単一観察者のみ対応」と明記して段階 4 に進むか、見送る。

### 4. VRChat SDK・Shader の制約の実験

- 前提: 段階 3 までの方式。VRChat SDK の版を固定する（現在の記録は 3.10.5。3.10.6 以降は未検証）。
- やること: 全 Pass（Outline・Shadow・Depth 等）、Head Chop、PhysBone、GPU skinning、
  lilToon・NDMF・Avatar Optimizer との組み合わせを、合成アバターで実験する。
- 成功指標: 各組み合わせについて PASS / FAIL / NOT RUN と、FAIL の原因を記録する。
  VRChat の規約に反する手段（SDK の改変・再配布など）を使わない。
- 撤退条件（NO-GO として残すもの）: 必須の Pass で評価位置を取れない、Head Chop で頭部基準が壊れる、
  主要シェーダーと共存できない。NO-GO は理由と再挑戦の条件を書いて残す。

### 5. VRChat クライアント・HMD での実機確認

- 前提: 段階 4 で NO-GO が残っていない組み合わせ。実機の手動操作ができること。
- やること: [S2-R1](../experiments/VRC_P0_S2_CLIENT_VALIDATION.md) で準備した手順（V1〜V10）で、撮影と数値比較を行う。
- 成功指標: 実機の結果を Unity Editor 内の模擬とは別の表に記録し、全項目が PASS。
- 撤退条件: 実機でだけ起きる差を説明できない。**この段階が PASS するまで「VRChat 対応」と書かない。**

### 6. 拡張要件の切り出し

段階 5 までとは別の Issue に分けて扱います。それぞれ、必要になった時点で前提・成功指標・撤退条件を書きます。

- Pitch（上下の向き）での切り替え
- 法線・接線の差分、または再計算
- 複数メッシュ・複数グループの同時編集
- LOD
- 大きなメッシュでのパフォーマンス（現在、13 万頂点で確定・元に戻すに 0.2〜0.3 秒）
- 既存 Layer（v0.1）との合成

## 進め方

- 段階の開始・結果は Issue #8 に追記し、この文書の「現在の状態」を更新します。
- 公開後に段階 6 の項目を Issue として切り出します。
