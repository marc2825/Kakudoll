# 第三者素材について / Third-party notices

Kakudoll のソースコードは [PolyForm Strict License 1.0.0](LICENSE) で公開しています。ここに挙げる第三者の素材や名称には、Kakudoll のライセンスは適用されません。
この文書は 2026-11 時点の公開情報をもとにした整理で、法的な助言ではありません。

## このリポジトリに含めていない素材

### Seed-san

実際の VRM での自動テストに、VRM 公式サンプルの Seed-san を手元で使いました。
モデル、テクスチャ、撮影した画像、テストの出力はリポジトリに含めていません。

- Seed-san model by VirtualCast, Inc.（© VirtualCast, Inc.）
- ライセンス: [VRM Public License 1.0](https://vrm.dev/licenses/1.0/)
- 使用した版: [vrm-c/vrm-specification `samples/Seed-san`（commit `837f156`）](https://github.com/vrm-c/vrm-specification/tree/837f156dbce43ad69183ce1bdab549961ae1c1ee/samples/Seed-san)

### 商用アバター

VRChat に向けた実験では、正規に購入したモデルの複製を手元で測りました。
各モデルの規約でデータの公開が認められているか確かめられないため、モデル本体、画像、メッシュや BlendShape の情報、
モデルごとの測定データは含めていません。集計した結果だけを
[docs/experiments/REAL_AVATAR_VALIDATION_SUMMARY.md](docs/experiments/REAL_AVATAR_VALIDATION_SUMMARY.md) に載せています。

| モデル | 作者 | 配布ページ |
|---|---|---|
| しなの（Shinano） | ポンデロニウム研究所 | <https://ponderogen.booth.pm/items/6106863> |
| 狛乃（Komano） | ひゅうがなつみかん | <https://hyuuganatu.booth.pm/items/5260363> |
| ゆめか（Yumeka） | Arka_X | <https://arkax.booth.pm/items/8672657> |

名称と著作権はそれぞれの作者に帰属します。実験を再現するときは、各自が正規に入手したモデルと各モデルの規約に従ってください。

## 依存ソフトウェア

同梱はしておらず、パッケージマネージャーで取得します。ライセンスは 2026-11-22 に各配布元の文書で確かめました。

| 名前 | 用途 | ライセンス |
|---|---|---|
| [UniVRM](https://github.com/vrm-c/UniVRM) v0.128.3（`com.vrmc.gltf` / `com.vrmc.univrm` / `com.vrmc.vrm`） | VRM の読み込み | MIT License（[LICENSE.txt](https://github.com/vrm-c/UniVRM/blob/v0.128.3/LICENSE.txt)） |
| `com.unity.nuget.newtonsoft-json` 3.2.1（Newtonsoft.Json 13.0.2 を同梱） | JSON の読み書き | パッケージは Unity Companion License、同梱の Newtonsoft.Json は MIT License |
| Unity Test Framework などの `com.unity.*` パッケージ | テストとエディタ | Unity Companion License |
| `com.unity.ext.nunit` 2.0.3（Unity Test Framework の依存） | テスト | Unity Package Distribution License（同梱の NUnit 3.5 は MIT License） |
| NUnit、NUnit3TestAdapter、Microsoft.NET.Test.Sdk | Core の .NET テスト | MIT License |
| VRChat SDK 3.10.5 | `experiments/` の検証用プロジェクトだけで使用。同梱せず、各自が VRChat Creator Companion で導入 | [VRChat Materials License Agreement](https://hello.vrchat.com/legal/sdk)。SDK の再配布・改変を認めていないため、このリポジトリには SDK のファイルを含めていません。Kakudoll のライセンスは SDK には及びません |

### 開発・CI だけで使うツール（配布物には含まない）

| 名前 | ライセンス |
|---|---|
| Python `jsonschema` と依存パッケージ（`attrs`、`referencing`、`rpds-py`、`jsonschema-specifications`） | MIT License |
| GitHub Actions（`actions/checkout`、`actions/setup-python`、`actions/setup-dotnet`、`actions/cache`、`actions/upload-artifact`）、`game-ci/unity-test-runner` | MIT License |
| .NET SDK | MIT License |

## ロゴと紹介画像のフォント

[docs/assets/brand/](docs/assets/brand/) のロゴと紹介画像では、次のフォントの文字を図形（アウトライン）に変換して使っています。フォントのファイルは含めていません。

| フォント | 使った場所 | 著作権表示 | ライセンス |
|---|---|---|---|
| Fredoka SemiBold（Version 2.001） | ロゴ文字「Kakudoll」 | Copyright 2016 The Fredoka Project Authors | [SIL Open Font License 1.1](https://scripts.sil.org/OFL) |
| Noto Sans CJK JP Regular / Bold（Version 2.004） | 紹介画像の日本語 | © 2014-2021 Adobe | [SIL Open Font License 1.1](https://scripts.sil.org/OFL) |

## 商標

Unity は Unity Technologies の、VRChat は VRChat Inc. の商標です。VRM は VRM コンソーシアムの仕様です。
Kakudoll は、ここに挙げた団体・作者と提携しておらず、推奨も受けていません。
