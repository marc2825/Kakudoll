# バージョンとリリース

まだ一度もリリースしていません。最初のリリースは `0.1.0`（Pre-beta）の予定です。

## バージョンの付け方

- パッケージのバージョンは `Packages/jp.vlat.core/package.json` の `version` で、[SemVer](https://semver.org/lang/ja/) に従います。
- タグは `v` を付けたバージョン（例: `v0.1.0`）で、`main` のコミットにだけ付けます。
- 0.x のあいだは Pre-beta です。GitHub Release は Pre-release として出します。
  0.1 → 0.2 のように minor を上げるときは、互換性のない変更が入ることがあります。0.1.0 → 0.1.1 のような patch は、互換性を保つ修正だけです。
- 変更点は [CHANGELOG](../../Packages/jp.vlat.core/CHANGELOG.md) の `[Unreleased]` に書きためます。互換性に関わる変更は「互換性」の項に書きます。
- ファイル形式のバージョン（`.vlat.json` の `vlat.version`）は、パッケージのバージョンとは別に決めます（[形式仕様](../spec/02_FORMAT_SPEC.md) §6）。
- 修正は最新版にだけ入れます。

## 互換性

リリースのたびに 1 行足します。

| Kakudoll | 読めるファイル形式 | 書くファイル形式 | Unity | UniVRM | VRChat |
|---|---|---|---|---|---|
| 0.1.0（未リリース） | v0.1.x、Keyform v0.2.0 | v0.1.0（Layer）、v0.2.0（Keyform） | 2022.3.62f3 | v0.128.3 | 非対応 |

## 配布

UPM の Git URL と、clone したフォルダをローカルパッケージとして追加する方法の 2 つです（手順は README）。
`.unitypackage` や OpenUPM では配布していません。GitHub Release にはファイルを添付しません。

## ライセンス

開発中は PolyForm Strict License 1.0.0 です。完成したら MIT License などに切り替えます。
そのときは `LICENSE`、`Packages/jp.vlat.core/LICENSE.md`、`package.json` の `license` を一緒に変え、CHANGELOG に書きます。
外部からの貢献は MIT License で受け取る約束なので（CONTRIBUTING.md）、切り替えるときに貢献した人の同意を取り直す必要はありません。

## リリースの手順

1. Actions → CI → Run workflow で `release_check` にチェックを入れて `main` を実行し、`Release readiness` が READY になるのを確かめる。
   Unity の Secret が必要です（[CI_SETUP.md](../CI_SETUP.md)）。
2. [公開前チェックリスト](../PUBLIC_RELEASE_CHECKLIST.md) の所有者の確認項目が済んでいるか確かめる。
3. CHANGELOG の `[Unreleased]` を `[X.Y.Z] - YYYY-MM-DD` に変え、`package.json` の `version` を合わせ、上の互換性の表に行を足す。
   README の導入例の `#main` を `#vX.Y.Z` に変える。
4. この変更を `main` に入れ、もう一度 1 の CI を通す。
5. タグを付けて push する: `git tag -a vX.Y.Z -m "Kakudoll X.Y.Z" && git push origin vX.Y.Z`
6. GitHub Release を Pre-release として作り、CHANGELOG の該当部分を貼る。
7. Unity Hub の 3D テンプレートで新しいプロジェクトを作り、README の手順で導入・削除できることを確かめて、[TESTING.md](../TESTING.md) に結果を書く。
