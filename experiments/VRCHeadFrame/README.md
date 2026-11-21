# VRC P0-S1 Head Frame experiment

VLat本体を参照しない **Unity 2022.3.22f1 / Built-in RP** の実験プロジェクト。
`VLatProject`は2022.3.62f3のまま使用する。実験結果・制限・採否は
[`VRC_P0_S1_HEAD_FRAME.md`](../../docs/experiments/VRC_P0_S1_HEAD_FRAME.md)を参照。

## 再現

Windows PowerShellでリポジトリルートから実行する。

```powershell
.\experiments\VRCHeadFrame\run.ps1
# 別のインストール先なら:
.\experiments\VRCHeadFrame\run.ps1 -Unity 'D:\Unity\2022.3.22f1\Editor\Unity.exe'
```

毎回新規TEMPへAssets/Packages/ProjectSettingsだけを複製する。元プロジェクトを開かない。
出力先の`angles.csv`・`matrices.csv`・`summary.md`を確認する。

```text
python scripts/check_head_frame_results.py <表示されたresultsディレクトリ>
```

Unityの直接実行では`-executeMethod VLat.HeadFrameExperiment.HeadFrameSweep.RunBatch`を指定する。
`-quit`は付けない（Editor updateで実験を進め、完了時にrunnerが終了する）。
`-nographics`は付けない（実際のGPU readbackが必要）。
runner終了コード0は測定完了を示す。候補のFAILはCSVとcheckerで判定する。
検証済みCSVは`results/2026-10-16-unity2022.3.22f1/`に同梱した。

## シーン

このプロジェクトを22f1で開き、`Assets/HeadFrame/Generated/HeadFrame.unity`を開く。
6頂点・8三角形、Neck/Head、1つの表情BlendShape、7候補のRendererを含む。
既定では`B_ChildProxyRoot`だけが表示される。比較時は表示するRendererを1つにする。

- `Body`、`Neck`、`Head`を回転してGame viewを観察する。カメラはInspectorで移動する。
- Materialの`_Mode=0`: R=yaw/360+0.5、G=pitch/180+0.5、B=方向有効。
- `_Mode=1`: 画面左／中央／右の区画にObject X／Y／Zのworld方向をRGBで表示。
- `_Mode=2..5`: Object行列のX/Y/Z/原点を生のfloatで出力。負値は画面でclampされるためCSVで読む。
- `_Mode=6`: 校正前のObject yaw/pitch。`_Mode=7`: silhouette。`_Mode=8`: world位置。
- `ExpressionProbe`のweightを0..100へ変更して表情併用を観察する。

`VLat Experiments/Run Head Frame Sweep`で全条件を測定できる。
`Create Head Frame Scene`は新規フォルダへ生成する。対話実行では既存の無題シーンを
先に保存する（Unityの加算シーン作成制約）。隔離batchでの同メソッドはSingle sceneを作る。
保存済みシーンの再読込検証は隔離batchの
`-executeMethod VLat.HeadFrameExperiment.HeadFrameSweep.VerifySavedScene -quit`を使う。

## 境界

SDK、UniVRM、NDMF、lilToon依存、VLat runtime componentはない。
全C#はEditor-only。Shaderは固定のrest校正3行とUnityの描画カメラ／Object行列を使う。
ポーズごとのC#行列送信はしない。batch計測のreadback・配列生成は診断専用で、性能測定ではない。
VRChatアバターへ、この診断用Cameraやfixtureをそのままアップロードしない。
非一様・負スケール、実アバター、実PlayerのGPU skinning backend、Stereo、Mirror、Head Chopは
採用ゲートを通過していない。
