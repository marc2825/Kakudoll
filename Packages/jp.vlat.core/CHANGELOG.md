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

### 変更

- Kakudoll Editor の画面を作り直しました（[見直しの記録](../../docs/product/EDITOR_UI_REVIEW.md)）。
  - 新しいファイルでは「はじめる画面」が出ます。アバター、直すパーツ、角度を選んで「編集をはじめる」を押すと、
    グループの作成・パーツの指定・キーの作成を 1 回の「元に戻す」で戻せる操作として行います。
    直すパーツはプレビュー用の VRM から選べるので、同じアバターをシーンに置く必要がなくなりました。
  - 編集ビューを大きくし、その下に角度レールを置きました。キー（ひし形）、補正の効く範囲（帯）、編集ビューと比較ビューの角度を
    1 本の軸で見られます。ひし形を押すとそのキーと角度へ移り、横に引くとキーの角度が変わります。キーは今見ている角度に足せます。
  - 左ドラッグの意味を「見る・選ぶ・動かす」の 3 つのツールにしました。キーボードでも操作できます
    （1・2・3 でツール、← → でとなりのキー、Space を押すあいだは補正前、[ ] でブラシの太さ）。
  - 操作の結果はステータス行の 1 か所に出し、次にすることを左に 1 文で出します。検証結果やファイルの情報、
    効く範囲は「詳細」にまとめ、問題があるときは上のバーの印から開けます。
  - 旧形式（v0.1）の Layer 編集は、旧形式のファイルを開いたときと「旧形式（v0.1）で始める」を選んだときだけ出します。
  - 画面のメッセージを、直し方が分かる言葉にしました。
- `VlatEditorDocumentController.BindKeyformTarget` と `ImportKeyformPositions` に、基準にする元のメッシュを渡せる省略可能な引数を足しました
  （プレビューは表示用の一時メッシュに差し替えるため）。

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
