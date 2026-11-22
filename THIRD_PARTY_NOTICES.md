# 第三者素材について / Third-party notices

Kakudoll のソースコードは [MIT License](LICENSE) です。ここに挙げる第三者の素材・名称には MIT License は適用されません。
（2026-11-21 時点の公開情報にもとづく整理で、法的な助言ではありません。）

## このリポジトリに含めていない素材

### Seed-san

実際の VRM での自動テストに、VRM 公式サンプルの Seed-san をローカルで使いました。
モデル、テクスチャ、撮影した画像、テストの出力は含めていません。

- Seed-san model by VirtualCast, Inc.（© VirtualCast, Inc.）
- ライセンス: [VRM Public License 1.0](https://vrm.dev/licenses/1.0/)
- 使用した版: [vrm-c/vrm-specification `samples/Seed-san`（commit `837f156`）](https://github.com/vrm-c/vrm-specification/tree/837f156dbce43ad69183ce1bdab549961ae1c1ee/samples/Seed-san)

### 商用アバター

VRChat 向けの検証実験では、正規に購入したモデルの複製をローカルで測定しました。
各モデルの規約でデータの公開が許可されているか確認できないため、モデル本体・画像・メッシュや BlendShape の情報・
モデルごとの生の測定データは含めず、集計した結果だけを
[docs/experiments/REAL_AVATAR_VALIDATION_SUMMARY.md](docs/experiments/REAL_AVATAR_VALIDATION_SUMMARY.md) に載せています。

| モデル | 作者 | 配布ページ |
|---|---|---|
| しなの（Shinano） | ポンデロニウム研究所 | <https://ponderogen.booth.pm/items/6106863> |
| 狛乃（Komano） | ひゅうがなつみかん | <https://hyuuganatu.booth.pm/items/5260363> |
| ゆめか（Yumeka） | Arka_X | <https://arkax.booth.pm/items/8672657> |

名称と著作権はそれぞれの作者に帰属します。実験を再現するときは、各自が正規に入手したモデルと各モデルの規約に従ってください。

## 依存ソフトウェア（同梱せず、パッケージマネージャーで取得）

| 名前 | 用途 | ライセンス |
|---|---|---|
| [UniVRM](https://github.com/vrm-c/UniVRM) v0.128.3 | VRM の読み込み | MIT License |
| Newtonsoft.Json 13.0.2（Unity 版 `com.unity.nuget.newtonsoft-json` 3.2.1） | JSON の読み書き | MIT License（Unity のパッケージは同梱の LICENSE に従う） |
| Unity Test Framework などの `com.unity.*` パッケージ | テストとエディタ | Unity Companion License |
| NUnit、NUnit3TestAdapter、Microsoft.NET.Test.Sdk | Core の .NET テスト | MIT License |
| VRChat SDK 3.10.5 | `experiments/` の検証用プロジェクトだけで使用。同梱せず、各自が VRChat Creator Companion で導入 | VRChat の SDK 利用規約 |

## 商標

Unity は Unity Technologies の、VRChat は VRChat Inc. の商標です。VRM は VRM コンソーシアムの仕様です。
Kakudoll は、ここに挙げた団体・作者と提携しておらず、推奨も受けていません。
