# 開発に参加するには

不具合の報告、提案、Pull Request を歓迎します。大きな変更は、先に Issue で相談してください。

- 不具合・提案: [Issue](https://github.com/marc2825/Kakudoll/issues/new/choose)
- 質問・アイデア: [Discussions](https://github.com/marc2825/Kakudoll/discussions)
- 脆弱性: 公開の場に書かず、[SECURITY.md](SECURITY.md) の方法で
- 今後の予定: [ROADMAP](docs/product/ROADMAP.md)

## 開発環境

- Unity 2022.3.62f3（Built-in Render Pipeline）、UniVRM v0.128.3
- C# 9、UI Toolkit
- テスト用に Python 3.10 以降と .NET SDK 10

## お願い

- 元のアセット（VRM、メッシュ、マテリアル、Prefab）は書き換えず、複製してから加工してください。
- アセンブリの依存の向きを守ってください（[設計](docs/spec/03_ARCHITECTURE.md) §1）。Core は Unity に依存させません。
- 毎フレーム実行される処理で GC アロケーションを出さないでください。重い処理では LINQ、クロージャ、文字列の連結を避けます。
- 公開 API には、日本語の XML ドキュメントコメントを書いてください。
- ファイル形式（v0.1、Keyform v0.2）の互換性を壊さないでください。必要な場合は先に Issue で相談してください。
- テストの結果はそのまま書いてください。実行していないテストは「未実施」と書き、テストを消したり条件を緩めたりして通さないでください。

## テスト

```bash
python3 -m pip install "jsonschema>=4.10,<5"
python3 scripts/run_license_free_checks.py
```

Unity のテストは Test Runner（Window > General > Test Runner）で実行します。詳しくは [docs/TESTING.md](docs/TESTING.md) を見てください。

fork からの Pull Request では、CI で Unity のテストが動きません。手元で実行した結果（コマンド、Unity のバージョン、OS）を Pull Request に書いてください。

変更点は [CHANGELOG](Packages/jp.vlat.core/CHANGELOG.md) の `[Unreleased]` に書き足してください。

## 入れないでほしいもの

- 第三者のアバター、テクスチャ、画像と、そこから取り出したデータ（BlendShape 名の一覧やメッシュの情報など）
- 個人の絶対パス、メールアドレス、認証情報、Unity のライセンスファイル

コミットする前に `python3 scripts/check_public_export.py .` で確認できます。

## ライセンス

Kakudoll は開発中、[PolyForm Strict License 1.0.0](LICENSE) で公開しています。
このライセンスは改変を認めていませんが、Pull Request を送るための変更は LICENSE の Additional Permission で認めています。

完成後に MIT License などへ切り替えられるよう、**いただいた貢献は MIT License で所有者（marc2825）に提供されるもの**とします。
Pull Request を送った時点で、この条件に同意したものとします。
