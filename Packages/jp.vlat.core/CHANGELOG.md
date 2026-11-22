# Changelog

このパッケージ（`jp.vlat.core`、表示名 Kakudoll）の変更履歴です。
書き方は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/)、版番号は [SemVer](https://semver.org/lang/ja/) に従います。
0.x のあいだは minor でも互換性のない変更がありえます。その場合は「互換性」の項に必ず書きます。
ファイル形式の版（`.vlat.json` の `vlat.version`）はパッケージの版とは別に管理します（[形式仕様](../../docs/spec/02_FORMAT_SPEC.md) §6）。

## [Unreleased]

まだリリースしていません。最初のリリースは `0.1.0` の予定です。

### 追加

- 外部ファイルのアクセス範囲（`VlatFileAccessPolicy`）と読み込み上限（`VlatReadLimits`）。
  `VlatSerializer.Load` / `Save`、`VlatApplier.InitializeFromFile` にポリシーを渡す overload を追加。
- 画像をデコードする前のヘッダー確認（`VlatImageHeader`、`VlatTextureBudget`）。
- 保存ジャーナルによる、中断された保存の検出と復旧（`VlatSerializer.RecoverInterruptedSave`）。
  Kakudoll Editor は開くとき・保存するときに自動で復旧します。

### 互換性

- 既定の読み書きが安全モードになりました。`../` で JSON のフォルダの外を指す buffer・texture は読み込みエラーになります。
  信頼できるファイルは `new VlatFileAccessPolicy(共通の親フォルダ)` か `VlatFileAccessPolicy.TrustedLegacy` で読めます。
- 安全モードでは、uri に `\`・`:`・制御文字・Windows の予約デバイス名を使えません。
- `byteLength` と実ファイル長が違う v0.1 の buffer は、内容を読まずにエラーにします（以前も検証エラーでした）。
- Editor が書く bin のファイル名に `%` が含まれる場合、uri では `%25` にエスケープします。

### ライセンス

- MIT License から [PolyForm Strict License 1.0.0](LICENSE.md) に変更しました（開発中のため。完成後に MIT などへ戻す予定）。
  非商用の利用だけを認め、改変物・複製の配布と商用の利用は認めません。Pull Request のための変更は認めます。
  2026-11-22 までに MIT License で公開した版は、その版については MIT License のままです。

### 修正

- 保存の途中で `File.Replace` が失敗したときに、元のファイルを失うことがあった問題。

## 0.1.0 以前

公開前の開発履歴は、非公開リポジトリに残しています。
