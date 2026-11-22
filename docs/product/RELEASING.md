# 版の付け方とリリース手順

2026-11-22（Issue #7）。まだ一度もリリースしていません。最初のリリースは `0.1.0`（Pre-beta）の予定です。

## 版の付け方

- パッケージの版は `Packages/jp.vlat.core/package.json` の `version` で、[SemVer](https://semver.org/lang/ja/) に従います。
- Git のタグは `v` + 版（例: `v0.1.0`）。タグは `main` のコミットにだけ付けます。
- 0.x のあいだは Pre-beta です。安定版とは名乗らず、GitHub Release は「Pre-release」にします。
  minor を上げるとき（0.1 → 0.2）は互換性のない変更がありえます。patch（0.1.0 → 0.1.1）は互換性を保つ修正だけです。
- 変更は [CHANGELOG](../../Packages/jp.vlat.core/CHANGELOG.md) の `[Unreleased]` に書きためます。
  互換性に関わる変更は必ず「互換性」の項に書きます。
- ファイル形式の版（`.vlat.json` の `vlat.version`）は、パッケージの版とは別に管理します（[形式仕様](../spec/02_FORMAT_SPEC.md) §6、
  [KEYFORM_V0_2.md](KEYFORM_V0_2.md) §1）。
- 修正は最新の版だけに行います。古い版へのバックポートはしません。

## 互換性表

新しい版を出すたびに 1 行足します。

| Kakudoll | 読めるファイル形式 | 書くファイル形式 | Unity | UniVRM | VRChat |
|---|---|---|---|---|---|
| 0.1.0（未リリース） | v0.1.x、Keyform v0.2.0 | v0.1.0（Layer）、v0.2.0（Keyform） | 2022.3.62f3 | v0.128.3 | 非対応 |

## 配布方法

- UPM の Git URL（`?path=/Packages/jp.vlat.core#vX.Y.Z`）と、clone してローカルパッケージとして追加する方法の 2 つです。
  手順は README の「インストール」にあります。
- `.unitypackage` や OpenUPM での配布はしていません。
- GitHub Release にはバイナリを添付しません（ソースとタグだけ）。

## リリースの手順

1. `main` で CI を手動実行する（Actions → CI → Run workflow で `release_check` にチェック）。
   `Release readiness` が **READY** になることを確認する。Unity の Secret と作者メールの許可リストが必要です（[CI_SETUP.md](../CI_SETUP.md)）。
2. [公開前チェックリスト](../PUBLIC_RELEASE_CHECKLIST.md) の所有者の確認項目が済んでいることを確かめる。
3. CHANGELOG の `[Unreleased]` を `[X.Y.Z] - YYYY-MM-DD` にし、`package.json` の `version` を合わせ、互換性表に行を足す。
   README の `#v0.1.0` などの例も新しい版にする。
4. 3 のコミットを `main` に入れ、もう一度 1 の CI を通す。
5. タグを付けて push する: `git tag -a vX.Y.Z -m "Kakudoll X.Y.Z" && git push origin vX.Y.Z`
6. GitHub Release を Pre-release として作り、CHANGELOG の該当部分を本文に貼る。
7. Unity Hub の 3D テンプレートで新しいプロジェクトを作り、README の Git URL の手順で導入できること、
   **Kakudoll > Editor** が開くこと、削除できることを確かめ、結果を README のテスト実績に書く。
