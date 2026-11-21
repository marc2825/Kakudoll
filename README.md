# VLat

VLat（View-dependent Layer for avatar toolkits）は、アニメ調3Dアバターに**視点（Yaw）ごとの形状差分**を
付け、元のアセットを変更せずに保存・再生するためのUnity向け制作基盤です。
正面と横顔で輪郭や前髪の見え方を作り分けるような、「見る角度によって変わる形」を作者が直接編集できることを目指しています。
短期目標は、PC版VRChatのアバター制作者が使えるUnityアドオンです。

> **開発中（Beta前）です。** 互換性を保証するリリースはまだありません。
> VRChatクライアント・HMD上での動作は**未検証（NOT RUN）**です。VRChat向けの再生機能は未実装です。

## 現在の状態

| 機能 | 状態 |
|---|---|
| フォーマット v0.1（AngleBand / Layer / Modifier）の読み書き・検証 | 実装済み・自動試験済み |
| Keyform v0.2（単一グループ・Yawのみ・位置差分のみ）の読み書き・検証 | 実装済み・自動試験済み |
| CPU Runtime（`VlatApplier`。動的BlendShape、MaterialPropertyBlock / 複製Materialでの補正）による v0.1 再生 | 実装済み・自動試験済み |
| Keyform v0.2 の Runtime 再生 | **未実装**（`VlatApplier` は v0.2 文書を明示的に拒否します） |
| Unity Editor: Keyformグループ / Yawキー編集、BlendShapeからの取込、Undo/Redo、保存、Recovery | 実装済み・自動試験済み |
| Editor: 3視点Preview（Main / Sub A / Sub B）とYaw補間表示 | 実装済み・自動試験済み |
| Editor: 頂点選択（Soft Selection）と Mainビューでの2Dドラッグ直接編集 | 実装済み・自動試験済み |
| 実VRM（Seed-san）での自動制作E2E（読込→編集→保存→再起動復元） | 自動検証済み（2026-10-25） |
| 大規模Mesh（131k頂点 × 6 Renderer）の編集応答 | 改善中。確定/Undo 約0.28秒・ドラッグ開始 約0.19秒で、目安（250 / 100 ms）に未達 |
| 人による制作評価（操作感・見た目の作者承認） | 未実施 |
| VRChatクライアント / HMD / Mirror / Camera 上の動作 | **未実施（NOT RUN）** |
| VRChat向けGPU再生、lilToon全Pass対応、NDMF統合、Bake出力 | 未実装 |
| VPM配布・Unity Package のリリース版 | 未実装 |

「自動試験済み」は、リポジトリ内のEditMode / PlayMode / Core試験で確認したという意味です。
2026-11-21 に、この公開版と同じコードで **EditMode 357/357、PlayMode 4/4、Core（.NET）90/90** が成功しました
（Windows 11・Unity 2022.3.62f3、新規のUnityプロジェクトへ配置してパッケージ解決から実行）。
利用環境で再実行する手順は下の「動作確認」を参照してください。

## 対応環境

| 項目 | バージョン |
|---|---|
| Unity | `2022.3.62f3` LTS（Built-in Render Pipeline）。他のバージョンは未検証 |
| UniVRM | `v0.128.3`（VRM 1.0 を主対象。VRM 0.x は読込のみ） |
| 言語・UI | C# 9 / UI Toolkit |
| OS | Windows で検証。CIの Core / Schema 試験は Linux |
| VRChat | 対象外（未検証）。実験用の診断環境は Unity `2022.3.22f1` / VRChat SDK `3.10.5` で別に管理 |

`Packages/jp.vlat.core/package.json` の `version`（パッケージの版）と、文書内の `vlat.version`
（`0.1.0` / Keyform `0.2.0`、ファイル形式の版）は別の概念です。

## 導入（開発版）

リリース版のパッケージはまだありません。開発版はリポジトリをcloneして使います。

1. Unity Hub に Unity `2022.3.62f3` をインストールする。
2. このリポジトリをcloneし、Unity Hub から `VLatProject` フォルダを開く。
   Package Manager が `Packages/jp.vlat.core` と、Git URLで固定した UniVRM `v0.128.3` を解決します
   （初回はネットワークと Git が必要です）。
3. メニュー **VLat > Editor** で編集ウィンドウを開く。

既存のUnityプロジェクトで試す場合は、`VLatProject/Packages/manifest.json` と同じ UniVRM の依存を追加し、
`Packages/jp.vlat.core` をローカルパッケージ（`file:`）として参照してください。
VLatは元のVRM・Mesh・Material・Prefabを変更しませんが、作業前にプロジェクトのバックアップを取ってください。

## 使い方の例

利用権を確認したVRM 1.0アバターを用意して、次の順に操作します。

1. **VLat > Editor** を開き、「VRMを開く」でアバターを読み込む（原本ではなくPreview用の複製に表示されます）。
2. 編集対象のRenderer（顔など）を指定し、Keyformグループを作る。
3. 0°・45°・90° などのYawキーを追加する。既存のBlendShapeから位置差分を取り込むこともできます。
4. 頂点選択モードで対象の頂点を選び、Mainビューでドラッグして形を変える（1回のドラッグが1回のUndo単位）。
5. Sub A / Sub B のPreviewで、キー間のYaw補間が意図どおりかを確認する。
6. 保存すると `.vlat.json`（とバイナリ差分）が書き出されます。元アセットは変更されません。

現時点で保存したKeyform（v0.2）はEditor上のPreviewで確認するためのもので、Runtime・VRChat上で再生する機能はまだありません。
v0.1形式の文書は、アバターのルートへ `VlatApplier` コンポーネントを追加して再生できます
（手順: [`docs/PHASE1_MANUAL_CHECK.md`](docs/PHASE1_MANUAL_CHECK.md)）。
フォーマットの詳細は [`docs/spec/02_FORMAT_SPEC.md`](docs/spec/02_FORMAT_SPEC.md) と
[`docs/product/KEYFORM_V0_2.md`](docs/product/KEYFORM_V0_2.md) を参照してください。

## 動作確認

```bash
# Unityライセンス不要の検証（Schema / Core .NET / 保存済み実験結果のchecker / SHA-256照合）
python3 -m pip install "jsonschema>=4.10,<5"
python3 scripts/run_license_free_checks.py          # .NET SDK がない場合は --skip-dotnet（NOT RUN と表示）

# Unity EditMode / PlayMode（Windows + WSL。リポジトリ外に作った隔離コピーで実行する）
python3 scripts/run_editor_1e.py --stage <isolated-copy> --label <unique-label> --mode EditMode
python3 scripts/run_editor_1e.py --stage <isolated-copy> --label <unique-label> --mode PlayMode
```

Unity Editor の場所は環境変数 `UNITY_EXECUTABLE` で指定できます。
Unity Test Runner（Window > General > Test Runner）から `VLatProject` の EditMode / PlayMode 試験を実行することもできます。
CI（GitHub Actions）は Unity ライセンス不要の検証を常に実行し、Unity試験はライセンスSecretがある場合だけ実行します。
Secretがない場合は **NOT RUN** と表示し、PASSとは扱いません。設定は [`docs/CI_SETUP.md`](docs/CI_SETUP.md)。

## 既知の制限

- 編集できるのは単一のSkinnedMeshRendererの、bind-mesh-local の位置差分だけです。法線・接線の再生成、複数Rendererの同時編集は未実装です。
- Keyform v0.2 は単一グループ・Yawのみ・位置のみです。Pitch、複数グループ、非線形の変形は対象外です。
- 大規模Mesh（131k頂点 × 6 Renderer）では確定・Undo・ドラッグ開始に0.2〜0.3秒程度かかります。数値ドラッグ中の更新は約15 msで、GCアロケーションは発生しません。
- 逆行列を求められない（特異な）Skinningは診断して編集の開始を拒否します。非一様Scaleと複数boneの混合は合成Meshの数値回帰でだけ確認しています。全てのアバター構成での動作は保証しません。
- VRChat上の動作（Desktop / Mirror / Camera / HMD）は未検証です。VRChat向けGPU再生は、実機での成立性の証拠が揃うまで製品に組み込みません。
- 人による制作評価（30分以上の実制作、操作感の確認）は未実施です。

## 今後の予定

順番は変わることがあります。

1. 大規模Meshでの確定・Undo・ドラッグ開始の短縮
2. CIでのUnity試験の常時実行
3. VRChatクライアント（Desktop / Mirror / Camera）での実機検証（[`experiments/VRCClientValidation/README.md`](experiments/VRCClientValidation/README.md)）
4. Keyform v0.2 の Runtime 再生
5. 実機での成立性が確認できた後に、VRChat向けの再生方式とVPM配布

## ドキュメント

- v0.1 の要件・フォーマット・構成: [`docs/spec/01_REQUIREMENTS.md`](docs/spec/01_REQUIREMENTS.md)、[`02_FORMAT_SPEC.md`](docs/spec/02_FORMAT_SPEC.md)、[`03_ARCHITECTURE.md`](docs/spec/03_ARCHITECTURE.md)
- Keyform v0.2: [`docs/product/KEYFORM_V0_2.md`](docs/product/KEYFORM_V0_2.md) / 互換性: [`docs/product/COMPATIBILITY.md`](docs/product/COMPATIBILITY.md) / 設計判断: [`docs/product/DECISIONS.md`](docs/product/DECISIONS.md)
- Editor の設計ノート: [`docs/PHASE2A_FORMAT_AUDIT.md`](docs/PHASE2A_FORMAT_AUDIT.md)、[`PHASE2B_EDITOR_SESSION.md`](docs/PHASE2B_EDITOR_SESSION.md)、[`PHASE2C_PREVIEW_ARCHITECTURE.md`](docs/PHASE2C_PREVIEW_ARCHITECTURE.md)、[`PHASE2D_VERTEX_SELECTION.md`](docs/PHASE2D_VERTEX_SELECTION.md)
- VRChat向けの検証実験（Unity上の模擬試験の報告）: [`docs/experiments/`](docs/experiments/)
- CI の設定: [`docs/CI_SETUP.md`](docs/CI_SETUP.md)
- このリポジトリに含めていない資料と理由: [`docs/PUBLIC_DISTRIBUTION.md`](docs/PUBLIC_DISTRIBUTION.md)
- 貢献の方法: [`CONTRIBUTING.md`](CONTRIBUTING.md) / 脆弱性の報告: [`SECURITY.md`](SECURITY.md)

## License と第三者素材

ソースコードは [MIT License](LICENSE) です。

リポジトリには第三者のアバターのモデル本体・テクスチャ・画像は含まれていません。
実VRMでの自動E2Eには VRM 公式サンプルの Seed-san（Seed-san model by VirtualCast, Inc.、VRM Public License 1.0）を
ローカルで使いましたが、その画像・データは含めていません。商用アバター（しなの / 狛乃 / ゆめか）を使った検証は、
集計した測定結果だけを掲載しています。詳細は [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) を参照してください。

---

## English summary

VLat is an in-development Unity toolkit for authoring and replaying non-destructive, view-dependent
(yaw-dependent) shape differences on anime-style avatars, aimed first at PC VRChat avatar creators.
The Unity Editor workflow (yaw keyforms, three-view preview, direct 2D drag editing, undo/redo, save and
crash recovery) is implemented and covered by automated tests on Unity 2022.3.62f3 with UniVRM v0.128.3.
Runtime playback exists only for the v0.1 format; keyform (v0.2) playback and any VRChat playback are
not implemented, and behavior in the VRChat client or on an HMD has **not** been tested.

Source code is MIT-licensed. No third-party avatar models, textures, images or model-derived data are
included; see [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).
