# 商用アバターでの検証 — 公開用の集計

2026-10-16〜17 に、利用者が正規に入手した商用アバター3体（しなの / 狛乃 / ゆめか）の FBX の**ローカル複製**を使い、
VRChat 向けの Head Frame（頭部基準の座標系）が実際のアバターでも成り立つかを Unity 上で測りました。
この文書は公開用の集計です。モデル本体・画像・Mesh / BlendShape / weight の構造情報・ファイルの fingerprint・
モデル別の生の測定データは、各モデルの規約で公開が許可されているか確認できないため掲載していません
（[`THIRD_PARTY_NOTICES.md`](../../THIRD_PARTY_NOTICES.md)、[公開版に含まれないファイル](../PUBLIC_DISTRIBUTION.md)）。

**これは Unity Editor 上の模擬試験です。VRChat クライアント・HMD での実機試験ではありません。**

## 環境と方法

- Windows 11 / Unity 2022.3.22f1 / VRChat SDK base・avatars 3.10.5 / Built-in / D3D11 / GPU: GeForce RTX 3060 Ti
- 元の FBX・VRM・Material・Prefab は変更せず、新規の一時プロジェクトへ FBX だけを複製した。複製前後と測定後に元 FBX の SHA-256 が一致することを確認した。
- 3体の全 SkinnedMeshRenderer（計49）を監査し、各モデルの顔を含む Renderer（約0.9万〜1.1万頂点）で、
  Head Frame の実装候補4方式（A: Head直下、B: 子Proxy、B: VRC Proxy、C: 剛体rebase）を Edit / Play Mode で比較した。
- 判定の閾値（位置 0.1 mm、coverage 不一致 2 pixel 以下）は測定前に決め、結果を見て変更していない。
- 手順とコード: [`experiments/VRCRealAvatars/README.md`](../../experiments/VRCRealAvatars/README.md)、
  [`experiments/VRCImageDifference/README.md`](../../experiments/VRCImageDifference/README.md)（各自が利用権を持つモデルで再現できる）。
  合成アバターだけを使う同種の実験は [`VRC_P0_S1_HEAD_FRAME.md`](VRC_P0_S1_HEAD_FRAME.md)。

## 結果（G0 / P0-S1 追加検証、2026-10-16）

| 試験 | 結果 |
|---|---|
| Head 角度・Shader 行列（4方式） | **1,344 / 1,344 PASS**。最大角度誤差 0.003887117° |
| VRC Proxy の追従 | PASS。最大位置差 4.165001e−9 m、回転差 0° |
| 三角形 surface-image（画面上の補間表面点の位置比較） | Edit / Play 計2,016行で **1,492 PASS / 524 FAIL**。うち504件は意図した負対照、**正しい候補の20件が FAIL**（最大 0.5638606 mm、coverage 差 0） |
| 全頂点 GPU probe（頂点 ID ごとの位置比較） | **288 / 288 PASS**。2,906,112 件の比較で最大 7.170482e−7 m |
| 原本の保護 | 3体とも元 FBX と複製の SHA-256 が不変、元 Mesh / Material の指紋も不変 |
| 結果 checker の破損入力 | 7 / 7 種類を拒否 |

20件の FAIL の内訳: モデル別では 2 体に発生（16件 / 4件）、1体は 0件。条件別では頭部の pitch・roll、表情（瞬き）、拡大縮小、C方式など。

判定（当時）: **Head Frame・全頂点位置は Unity 上で限定 PASS、surface-image は FAIL。**
独立した S2 検証へは条件付きで進め、製品への採用・G0 全体の完了は NO-GO。

## 20件の FAIL の原因調査（P0-S1-R1、2026-10-17）

| 測定 | 結果 |
|---|---|
| 元の試験の再実行 | 全2,016行の各値が元の結果と一致（FAIL 20件も再現） |
| 原因の特定 | **20 / 20 件を特定、未特定 0** |
| 原因 | D3D11 の rasterization で、viewport 変換後の座標が 1/256 pixel に丸められ、同じ三角形の中で補間係数（重心座標）が変わる。頂点が動いたわけではない |
| 大きな差の画素 | 28 / 28 画素が同じ三角形 ID。単独描画でも差を再現。丸めを再現した予測と実測の差は最大 0.176888 µm |
| 同じ重心座標での対応点の比較 | **20 / 20 PASS**（共通画素 7,842、最大 0.479933 µm） |
| CPU の LBS と全頂点 GPU の比較 | 最大 0.727806 µm（0.1 mm 閾値以内） |
| 1 mm の Mesh 破損（負対照） | 20 / 20 検出 |

**元の raw surface-image の 20 FAIL は FAIL のまま記録しています。**
原因調査で行った「同じ重心座標での対応点の比較」は**別の測定量**で、その 20 PASS は元の FAIL を取り消すものではありません。
閾値は変えていません。解像度を上げると元の比較でも閾値内に戻りますが、それを合格の根拠にはしていません。

## 実行していないもの（NOT RUN）

VRChat クライアント、HMD・Stereo、Mirror、Camera、Head Chop、製品 Prefab の外観（toon shader の各 Pass）、PhysBone、
Humanoid のアニメーション / IK、実 Player の GPU skinning、別の GPU / グラフィックス API、非一様 / 負の scale。

詳細な報告（S1-R1）: [`VRC_P0_S1_IMAGE_DIFFERENCE_INVESTIGATION.md`](VRC_P0_S1_IMAGE_DIFFERENCE_INVESTIGATION.md)。
