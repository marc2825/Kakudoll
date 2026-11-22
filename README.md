# Kakudoll

見る角度によって、アバターの形を描き分けるための Unity エディタ拡張です。

> **VRChat ではまだ使えません。** 編集とプレビューは Unity Editor の中だけで動きます。
> 開発中（Pre-beta）で、ファイル形式や API は今後変わることがあります。

アニメの作画では、正面と横顔で前髪の流れや顔の輪郭をわざと描き変えます。
Kakudoll は、その「角度ごとの見え方」を 3D アバターでも作れるようにすることを目指しています。
元の VRM・メッシュ・マテリアルは書き換えません。

[English](#english)

## 目次

- [できること・まだできないこと](#できることまだできないこと)
- [動作環境](#動作環境)
- [インストール](#インストール)（[更新](#更新) / [削除](#削除)）
- [クイックスタート](#クイックスタート)
- [安全な編集と保存](#安全な編集と保存)
- [困ったとき](#困ったとき)
- [開発](#開発)（[テスト実績](#テスト実績)）

## できること・まだできないこと

できること:

- 左右の向き（Yaw）ごとにキーを作り、その角度での形を編集する
- 頂点を選んで、Unity の画面上でドラッグして形を変える（ソフト選択つき）
- 3 つのプレビューで、キーとキーのあいだの変化を確かめる
- 既存の BlendShape から形を取り込む
- 元に戻す・やり直し、保存、Unity が異常終了したときの復元

まだできないこと:

- VRChat 上での再生（実機での確認もまだです）
- 編集した形をゲーム実行中に再生すること（古い v0.1 形式だけは `VlatApplier` で再生できます）
- 複数メッシュの同時編集、法線の再計算、上下の向き（Pitch）での切り替え
- 大きなメッシュ（13 万頂点ほど）では、確定や元に戻すのに 0.2〜0.3 秒かかります

今後の予定と、それぞれを「できた」と言える条件は [ROADMAP](docs/product/ROADMAP.md) にあります。

## 動作環境

| | |
|---|---|
| Unity | 2022.3.62f3（Built-in Render Pipeline）。2022.3 のほかの版、Unity 6 は未確認 |
| UniVRM | v0.128.3（`com.vrmc.gltf` と `com.vrmc.vrm`）。VRM 1.0 を編集できます。VRM 0.x は読み込みのみ |
| OS | Windows 11 で確認しています。macOS・Linux の Unity Editor は未確認 |
| プロジェクト | Unity Hub の「3D（Built-in Render Pipeline）」テンプレートで作ったもの。`com.unity.test-framework` が必要です（UniVRM が使います。テンプレートには入っています） |
| そのほか | UniVRM を Git から取得するため、Git（Windows は Git for Windows）とネット接続 |

既存のアバターへの影響: Kakudoll はアバターのプレビュー用の複製を作って編集します。
元の VRM・メッシュ・マテリアル・Prefab は変更せず、保存するのは `.vlat.json` と `.vlat.bin` だけです。

## インストール

作業の前に、Unity プロジェクトのバックアップを取ってください。

### 既存のプロジェクトに追加する（Git URL）

`Packages/manifest.json` の `dependencies` に 3 行を加えます。

```json
"com.vrmc.gltf": "https://github.com/vrm-c/UniVRM.git?path=/Assets/UniGLTF#v0.128.3",
"com.vrmc.vrm": "https://github.com/vrm-c/UniVRM.git?path=/Assets/VRM10#v0.128.3",
"jp.vlat.core": "https://github.com/marc2825/Kakudoll.git?path=/Packages/jp.vlat.core#v0.1.0"
```

Unity に戻ると取り込みが始まり、メニューに **Kakudoll** が出ます。

> `#v0.1.0` は最初のリリースのタグで、まだ付けていません。リポジトリを公開して v0.1.0 を出したあとに使える手順です。
> 同じ書き方（`?path=` とタグの指定）は、2026-11-22 にローカルの Git リポジトリを使って、
> 新しいプロジェクトへの導入・メニュー表示・サンプルの読み書き・削除まで確認しました（[テスト実績](#テスト実績)）。

### clone したフォルダから追加する

このリポジトリを clone し、`Packages/manifest.json` に UniVRM の 2 行と、clone した場所を指す行を加えます。

```json
"jp.vlat.core": "file:C:/path/to/Kakudoll/Packages/jp.vlat.core"
```

`manifest.json` からの相対パス（例: `file:../../Kakudoll/Packages/jp.vlat.core`）でも書けます。

### 開発用のプロジェクトを開く

clone した中の `VLatProject` フォルダを Unity Hub で開くと、Kakudoll と UniVRM が設定済みの状態で使えます。

### 更新

1. [CHANGELOG](Packages/jp.vlat.core/CHANGELOG.md) の「互換性」を読みます。
2. `.vlat.json` と `.vlat.bin` を複製しておきます。
3. `manifest.json` の `#v0.1.0` を新しいタグに書き換えます。

### 削除

1. `manifest.json` から `jp.vlat.core` の行を消します。ほかで UniVRM を使っていなければ、UniVRM の 2 行も消します。
2. プロジェクトの `Library/VLat/` フォルダを消します（まだ保存していない編集の復元用データです）。

保存した `.vlat.json` と `.vlat.bin` は、保存した場所にそのまま残ります。

## クイックスタート

1. メニューの **Kakudoll > Editor** を開く
2. 「VRMを開く…」でアバターを読み込む（編集はプレビュー用の複製に対して行われます）
3. 編集したいメッシュ（顔など）を選び、グループを作る
4. 0°・45°・90° などの角度にキーを追加する
5. 頂点を選び、メインビューでドラッグして形を変える
6. サブビューで角度による変化を確かめ、「保存」する（`.vlat.json` と `.vlat.bin` ができます）

アバターを用意せずに動作だけ確かめるなら、「開く…」でパッケージに入っている合成サンプル
`Packages/jp.vlat.core/Tests/EditMode/Fixtures/Keyform/three-keys.vlat.json` を開けます
（実在のアバターとは結びついていないので、プレビューには形が出ません）。

## 安全な編集と保存

- **前の版には戻せません。** 大事なファイルは、保存する前に複製するか Git で管理してください。
- 保存中に Unity が落ちても、次に Kakudoll Editor で開けば、保存前か保存後のどちらかの内容に戻ります。
  JSON と bin が食い違ったまま使われることはありません。詳しくは [保存の仕組みと、データを守れる範囲](docs/product/SAVE_AND_RECOVERY.md)。
- まだ保存していない編集は `Library/VLat/` に自動で残り、Unity が異常終了したあとに復元できます。
- 他の人から受け取った `.vlat.json` は、同じフォルダの中のファイルだけを読みます（大きすぎるファイル・画像も読みません）。
  規則は [形式仕様 §8](docs/spec/02_FORMAT_SPEC.md#8-外部ファイルの扱い参照実装のポリシー)。

## 困ったとき

| 症状 | 対処 |
|---|---|
| `UnityEditor.TestTools` や `NUnit` が見つからないというコンパイルエラー（UniGLTF のファイル） | `manifest.json` に `"com.unity.test-framework": "1.1.33"` を加える |
| UniVRM を取得できない | Git が入っていて PATH が通っているか、ネットにつながるかを確認する |
| 「uriが許可されたフォルダの外を指しています」 | `.vlat.bin` や画像を `.vlat.json` と同じフォルダ（またはその下）に置く |
| 「前回の保存が途中で中断され…」 | Kakudoll Editor でそのファイルを開くと、保存前の内容へ戻る |
| 「別の処理がこのファイルを保存中です」 | 同じファイルを別の Unity で開いていないか確認する |

不具合は [Issue](https://github.com/marc2825/Kakudoll/issues/new/choose) で教えてください。
脆弱性は公開の Issue には書かず、[SECURITY.md](SECURITY.md) の方法で報告してください。

## 開発

貢献のしかたは [CONTRIBUTING.md](CONTRIBUTING.md) にあります。Unity のライセンスが要らない検証は次のとおりです。

```bash
python3 -m pip install "jsonschema>=4.10,<5"
python3 scripts/run_license_free_checks.py
```

Unity のテストは Test Runner（Window > General > Test Runner）から実行できます。
CI の設定と、Unity のテストを CI で動かす方法は [docs/CI_SETUP.md](docs/CI_SETUP.md) にあります。

### テスト実績

「NOT RUN」は実行していないという意味で、PASS ではありません。

| 対象 | 実行した場所 | 結果 | 日付・コミット・環境 |
|---|---|---|---|
| ライセンス不要の検証（Schema、Core NUnit 140 件、netstandard2.1 build、公開監査ツールの試験、保存中の強制終了 50 回、保存済み実験結果） | ローカル | PASS（非配布ファイルを使う 5 件は NOT RUN） | 2026-11-22、`20a9449`、WSL2 Ubuntu 24.04 / .NET SDK 10.0.115 / Python 3.12 |
| 同上 | GitHub Actions | PASS（push・PR ごとに実行） | 2026-11-22、`2ee4989`、ubuntu-24.04。以降は各 run の Job Summary |
| Unity EditMode | ローカル | 413/413 PASS。`VLat.Core` の行カバレッジ 90.82%（下限 80%） | 2026-11-22、`20a9449`、Windows 11 Pro（26200）/ Unity 2022.3.62f3 / D3D11 |
| Unity PlayMode | ローカル | 4/4 PASS。7 万頂点・20 Layer の定常評価は中央値 11.71 µs/frame（上限 300 µs）、GC 割り当て 0 | 同上 |
| Unity EditMode / PlayMode | GitHub Actions | **NOT RUN**（Unity ライセンスの Secret が未設定） | 2026-11-22、`2ee4989` |
| 履歴の監査（Gitleaks・全 ref の禁止ファイル検査） | GitHub Actions | PASS（作者メールの検査は許可リスト未設定のため NOT RUN） | 2026-11-22、`2ee4989` |
| 新しいプロジェクトへの導入（Git URL・ローカルパス）・メニュー表示・サンプルの読み書き・削除 | ローカル | PASS | 2026-11-22、`20a9449`、Unity 2022.3.62f3 / UniVRM v0.128.3 / Windows 11 |
| VRChat クライアント・HMD | — | **NOT RUN** | [ROADMAP](docs/product/ROADMAP.md) 段階 5 |

CI では最後の `Release readiness` ジョブが、コミットごとに各検証の PASS / FAIL / NOT RUN を一覧にします。

### ドキュメント

- ファイル形式: [docs/spec/02_FORMAT_SPEC.md](docs/spec/02_FORMAT_SPEC.md)、[docs/product/KEYFORM_V0_2.md](docs/product/KEYFORM_V0_2.md)
- 設計: [docs/spec/03_ARCHITECTURE.md](docs/spec/03_ARCHITECTURE.md)、[docs/product/DECISIONS.md](docs/product/DECISIONS.md)
- 版の付け方とリリース手順: [docs/product/RELEASING.md](docs/product/RELEASING.md)
- VRChat に向けた検証実験: [docs/experiments/](docs/experiments/)
- このリポジトリに含めていない資料: [docs/PUBLIC_DISTRIBUTION.md](docs/PUBLIC_DISTRIBUTION.md)

## 名前について

Kakudoll（カクドール）は「角度（kakudo）」と「doll」を合わせた名前です。
開発初期の名前「VLat」は、ファイル形式（`.vlat.json`）、パッケージ名（`jp.vlat.core`）、コード内の名前にそのまま残っています。
互換性のため、これらは変えません。

## ライセンス

開発中のあいだは [PolyForm Strict License 1.0.0](LICENSE) です。
個人の趣味・学習・研究など**非商用の目的で使う**ことはできますが、**改変したものや複製を配布すること、商用で使うことはできません**。
改善の提案（Pull Request）のための変更は認めています（[CONTRIBUTING.md](CONTRIBUTING.md)）。
完成したら、MIT License など、より自由なライセンスに変える予定です。

2026-11-22 までに MIT License で公開していた版は、その版を受け取った人にとっては MIT License のままです。

第三者のアバターや、その画像・データは含んでいません（[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)）。

---

## English

Kakudoll is a Unity editor extension for shaping how an anime-style VRM avatar looks from different viewing angles,
without modifying the original assets. It is **pre-beta** and **does not work in VRChat yet**: editing and preview run
inside the Unity Editor only.

**What works:** per-yaw keyforms on one mesh, vertex dragging with soft selection, three-view preview, importing
existing BlendShapes, undo/redo, saving to `.vlat.json` + `.vlat.bin`, and recovery after an Editor crash.
**Not yet:** playback at runtime (only the older v0.1 format plays via `VlatApplier`), VRChat, multiple meshes,
normals, pitch. See the [roadmap](docs/product/ROADMAP.md).

**Requirements:** Unity 2022.3.62f3 (Built-in RP) and UniVRM v0.128.3, tested on Windows 11. The project needs
`com.unity.test-framework` (included in Unity Hub's 3D template) because UniVRM uses it. Git must be on PATH.

**Install:** back up your project, then add to `Packages/manifest.json`:

```json
"com.vrmc.gltf": "https://github.com/vrm-c/UniVRM.git?path=/Assets/UniGLTF#v0.128.3",
"com.vrmc.vrm": "https://github.com/vrm-c/UniVRM.git?path=/Assets/VRM10#v0.128.3",
"jp.vlat.core": "https://github.com/marc2825/Kakudoll.git?path=/Packages/jp.vlat.core#v0.1.0"
```

The `v0.1.0` tag will exist after the first release; the same syntax was verified against a local Git repository.
To remove Kakudoll, delete the `jp.vlat.core` line and the `Library/VLat/` folder. Your saved files stay where you saved them.

**Quick start:** open **Kakudoll > Editor**, load a VRM 1.0 avatar with "VRMを開く…", create a group on a mesh, add keys
at angles such as 0°/45°/90°, drag vertices in the main view, then save.

**Your data:** Kakudoll never edits the original VRM, meshes or materials. A save interrupted by a crash is rolled back or
completed the next time you open the file in Kakudoll, but there is no version history, so keep backups.
Files received from others may only reference files inside their own folder, and oversized inputs are rejected
([format spec §8](docs/spec/02_FORMAT_SPEC.md#8-外部ファイルの扱い参照実装のポリシー)).

**Tests:** `python3 scripts/run_license_free_checks.py` runs the checks that need no Unity license. As of 2026-11-22
(commit `20a9449`, Windows 11, Unity 2022.3.62f3), Unity EditMode 413/413 and PlayMode 4/4 pass locally;
Unity tests on GitHub Actions are **NOT RUN** until a Unity license secret is configured.

**Reporting:** bugs via [Issues](https://github.com/marc2825/Kakudoll/issues/new/choose); security issues privately as
described in [SECURITY.md](SECURITY.md).

**License:** while in development, Kakudoll is under the [PolyForm Strict License 1.0.0](LICENSE): you may use it for
noncommercial purposes, but you may not distribute it or modified versions, or use it commercially. Changes made only to
propose a pull request are permitted. The plan is to move to a permissive license such as MIT once it is complete.
Versions published under MIT before 2026-11-22 stay MIT for those who received them.
See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for third-party terms.
