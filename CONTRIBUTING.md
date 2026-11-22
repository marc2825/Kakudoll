# 貢献のしかた

Kakudoll は開発中です。不具合の報告、提案、Pull Request を歓迎します。
大きな変更は、先に Issue で相談してください。

- 不具合・提案: [Issue](https://github.com/marc2825/Kakudoll/issues/new/choose)（テンプレートがあります）
- 質問・アイデア: [Discussions](https://github.com/marc2825/Kakudoll/discussions)
- 脆弱性: 公開の場には書かず [SECURITY.md](SECURITY.md) の方法で
- 今後の予定: [ROADMAP](docs/product/ROADMAP.md)

## 開発環境

- Unity 2022.3.62f3（Built-in Render Pipeline）、C# 9、UI Toolkit
- UniVRM v0.128.3
- Python 3.10 以上、.NET SDK 10（Core のテスト用）

## 守ってほしいこと

1. 元のアセット（VRM・メッシュ・マテリアル・Prefab）を書き換えない。必ず複製してから加工する。
2. アセンブリの依存方向を守る（[docs/spec/03_ARCHITECTURE.md](docs/spec/03_ARCHITECTURE.md) §1）。Core は Unity に依存しない。
3. 毎フレームの GC アロケーションを出さない。処理の重い経路では LINQ・クロージャ・文字列連結を使わない。
4. 公開 API には日本語の XML ドキュメントコメントを書く。
5. ファイル形式（v0.1 / Keyform v0.2）の互換性を壊さない。仕様と合わない変更が必要なら、先に Issue で相談する。
6. テスト結果は正直に書く。実行していないテストは「未実施」と書き、テストを消したり条件を緩めたりして通さない。

## テスト

```bash
python3 -m pip install "jsonschema>=4.10,<5"
python3 scripts/run_license_free_checks.py
```

Unity のテストは Test Runner（Window > General > Test Runner）から実行してください。
Pull Request には、実行したコマンド、Unity のバージョン、OS、結果を書いてください（テンプレートがあります）。
fork からの Pull Request では Unity のテストが CI で動かない（NOT RUN になる）ため、手元の結果を書いてください。

変更は [CHANGELOG](Packages/jp.vlat.core/CHANGELOG.md) の `[Unreleased]` に追記してください。版の付け方は [RELEASING.md](docs/product/RELEASING.md) にあります。

## 入れないでほしいもの

- 第三者のアバター、テクスチャ、画像と、そこから取り出したデータ（BlendShape 名の一覧やメッシュの情報など）
- 個人の絶対パス、メールアドレス、認証情報、Unity のライセンスファイル

追加する前に `python3 scripts/check_public_export.py .` で確認できます。

## ライセンス

Kakudoll は開発中のあいだ [PolyForm Strict License 1.0.0](LICENSE) で公開しています。
このライセンスは改変を認めていませんが、このリポジトリへ Pull Request を出すための変更は、LICENSE の Additional Permission で認めています。

完成したあとに MIT License などへ変えられるよう、**いただいた貢献は MIT License で所有者（marc2825）に提供されるもの**とします。
Pull Request を出した時点で、この条件に同意したものとみなします。
