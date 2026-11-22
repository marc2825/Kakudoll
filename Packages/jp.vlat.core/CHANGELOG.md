# 変更履歴

`jp.vlat.core`（Kakudoll）の変更履歴です。[Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の書き方と、[SemVer](https://semver.org/lang/ja/) のバージョンに従います。
0.x のあいだは、minor を上げるときに互換性のない変更が入ることがあります。その場合は「互換性」の項に書きます。

## [Unreleased]

最初のリリース（0.1.0）に向けた変更です。

### 追加

- 外部ファイルを読める範囲（`VlatFileAccessPolicy`）と、読み込むサイズの上限（`VlatReadLimits`）。
  `VlatSerializer.Load` / `Save` と `VlatApplier.InitializeFromFile` に、これを渡せるようにしました。
- 画像をデコードする前に、ヘッダーで形式と大きさを確かめる処理（`VlatImageHeader`、`VlatTextureBudget`）。
- 保存が途中で止まったときの検出と復旧（`VlatSerializer.RecoverInterruptedSave`）。Kakudoll Editor は、開くときと保存するときに自動で復旧します。

### 互換性

- 既定では、`.vlat.json` と同じフォルダの外にある buffer や画像を読まなくなりました。`../` で外を指すファイルは読み込みエラーになります。
  信頼できるファイルは、`new VlatFileAccessPolicy(共通の親フォルダ)` か `VlatFileAccessPolicy.TrustedLegacy` を渡せば読めます。
- uri に `\`、`:`、制御文字、Windows の予約名（`CON`、`NUL` など）を使えなくなりました（既定の設定のとき）。
- buffer の `byteLength` が実際のファイルの長さと違うときは、ファイルを読まずにエラーにします。
- Editor が書く bin のファイル名に `%` があるときは、uri で `%25` と書くようにしました。

### ライセンス

- MIT License から [PolyForm Strict License 1.0.0](LICENSE.md) に変えました。開発中のあいだの措置で、完成したら MIT License などに戻す予定です。
  非商用の利用だけを認め、改変したものや複製の配布と、商用の利用は認めません。Pull Request を送るための変更は認めます。
  以前 MIT License で公開した版は、その版については MIT License のままです。

### 修正

- 保存の途中で `File.Replace` が失敗したときに、元のファイルが失われることがあった問題。

## 0.1.0 より前

公開前の開発の記録は、非公開のリポジトリに残しています。
