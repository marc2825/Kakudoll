# Contributing / 貢献の方法

VLat は開発中です。Issue での提案・不具合報告、Pull Request を歓迎します。
大きな変更は、先に Issue で方針を相談してください。

## 開発環境

- Unity `2022.3.62f3` LTS / Built-in Render Pipeline / C# 9 / UI Toolkit
- UniVRM `v0.128.3`（`VLatProject/Packages/manifest.json` で固定）
- Python 3.10 以上（検証スクリプト）、.NET SDK（Core 試験。CI は .NET 10）

依存のバージョンを変える場合は、理由と試験結果を Pull Request に書いてください。

## 守ること

1. **元のアセットを変更しない。** 元の VRM・Mesh・Material・Prefab を直接書き換えるコードは受け入れません。複製してから加工してください。
2. **asmdef の依存方向を守る。** 構成は [`docs/spec/03_ARCHITECTURE.md`](docs/spec/03_ARCHITECTURE.md) §1 のとおりです。逆向きの依存を追加しないでください（Core は Unity に依存しません）。
3. **毎フレームの GC アロケーションを発生させない。** ホットパスで LINQ・クロージャ・文字列連結を使わないでください。
4. **公開 API には日本語の XML ドキュメントコメントを付ける。**
5. **フォーマットの互換性を保つ。** v0.1 / Keyform v0.2 の読み書きを壊さないでください。仕様（`docs/spec/`、`docs/product/KEYFORM_V0_2.md`）と矛盾する実装が必要な場合は、仕様を黙って変えずに Issue で相談してください。
6. **試験結果を正直に書く。** 実行していない試験（VRChat 実機、HMD、人による評価、Unity ライセンスがない CI など）は NOT RUN と書き、PASS と読み替えないでください。既存の試験を削除したり期待値を緩めたりして PASS にしないでください。過去の FAIL の記録は消さないでください。

## 試験

```bash
python3 -m pip install "jsonschema>=4.10,<5"
python3 scripts/run_license_free_checks.py
```

Unity の EditMode / PlayMode 試験は、Unity Test Runner か `scripts/run_editor_1e.py`（リポジトリ外の隔離コピーで実行）で実行してください。
Pull Request には、実行したコマンド、Unity のバージョン、OS、結果（件数）を書いてください。
Unity のログや test XML をリポジトリに追加する必要はありません（個人の絶対パスを含みやすいため）。

## 含めてはいけないもの

- 第三者のアバター・モデル・テクスチャ・画像、およびそれらから抽出したデータ（Mesh、BlendShape 名の一覧、weight、fingerprint など）。
  利用規約で公開が明示的に許可され、帰属表示を [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) に追加したものだけを例外とします。
- 個人の絶対パス（`C:\Users\<名前>\...`、`/home/<名前>/...`）、メールアドレス、認証情報、Unity のライセンスファイル（`.ulf`）。
  ログや test XML を追加する前に、`python3 scripts/check_public_export.py .` で確認してください。

## ライセンス

送られた貢献は、このリポジトリと同じ [MIT License](LICENSE) で提供されるものとします。
