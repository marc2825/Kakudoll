# Third-party notices / 第三者素材の通知

VLat のソースコードは [MIT License](LICENSE) です。
**MIT License はこのファイルに記載した第三者の素材・名称には適用されません。**
権利の確認は 2026-11-21 時点の公開情報に基づくもので、法的な助言ではありません。

## 1. Seed-san（画像・データは含みません）

| 項目 | 内容 |
|---|---|
| 用途 | Unity Editor の実VRM自動E2E試験（読込→編集→保存→再起動復元）を、利用者のローカル環境で実行するために使用 |
| 著作者 / 著作権 | Seed-san model by VirtualCast, Inc. / © VirtualCast, Inc. |
| ライセンス | [VRM Public License 1.0](https://vrm.dev/licenses/1.0/)（使用した版: [vrm-c/vrm-specification `samples/Seed-san`（commit `837f156`）](https://github.com/vrm-c/vrm-specification/tree/837f156dbce43ad69183ce1bdab549961ae1c1ee/samples/Seed-san)） |
| リポジトリに含まれるもの | 試験コード（`--avatar` で利用者が指定したVRMを読む）と、試験を行ったという記述だけ |
| 含まれないもの | VRM ファイル、テクスチャ、改変した VRM、撮影した画像、試験の出力データ |

VirtualCast, Inc. は VLat に関与・推奨していません。

## 2. 商用アバター（データ・画像は含みません）

VRChat 向けの検証実験（`docs/experiments/`）では、利用者が正規に入手したモデルの**ローカルの複製**を
隔離した Unity プロジェクトで測定しました。各モデルの利用規約（VN3 ライセンス等）で、データの再配布や
派生データの公開を許可しているか確認できないため、このリポジトリには**モデル本体・テクスチャ・
画像・Mesh / BlendShape / weight の構造情報・ファイルの fingerprint・モデル別の生の測定データを含めていません**。
掲載しているのは、モデル名、配布ページへの参照、集計した測定結果（誤差の最大値・件数など）だけです。

| モデル | 作者・配布元 | 配布ページ | 規約 |
|---|---|---|---|
| しなの（Shinano） | ポンデロニウム研究所 | <https://ponderogen.booth.pm/items/6106863> | VN3 ライセンス（配布ページ記載の規約） |
| 狛乃（Komano） | ひゅうがなつみかん | <https://hyuuganatu.booth.pm/items/5260363> | VN3 ライセンス。配布ページに「データの再配布はいかなる場合も禁止」の記載あり |
| ゆめか（Yumeka） | Arka_X | <https://arkax.booth.pm/items/8672657> | VN3 ライセンス（規約はダウンロード後に確認する形式） |

各モデルの名称・著作権はそれぞれの作者に帰属します。VLat は各作者と提携しておらず、推奨も受けていません。
2026-11-21 の確認時点では、上記の配布ページに「現在この商品は非公開中です」と表示されていました。
これらのモデルで実験を再現する場合は、各自が正規に入手したモデルと各規約に従ってください
（手順: `experiments/VRCRealAvatars/README.md`、`experiments/VRCImageDifference/README.md`）。
集計結果: [`docs/experiments/REAL_AVATAR_VALIDATION_SUMMARY.md`](docs/experiments/REAL_AVATAR_VALIDATION_SUMMARY.md)。

## 3. 依存ソフトウェア（リポジトリには同梱せず、パッケージマネージャーで取得）

| 名称 | 用途 | ライセンス |
|---|---|---|
| [UniVRM](https://github.com/vrm-c/UniVRM) `v0.128.3`（`com.vrmc.vrm` / `com.vrmc.gltf` / `com.vrmc.univrm`） | VRM の読込 | MIT License |
| Newtonsoft Json for Unity（`com.unity.nuget.newtonsoft-json` 3.2.1）/ Newtonsoft.Json 13.0.2 | JSON 入出力 | Newtonsoft.Json は MIT License（Unity のパッケージはパッケージ同梱の LICENSE に従う） |
| Unity Test Framework / Performance Testing / Code Coverage / Timeline（`com.unity.*`） | 試験・Editor | Unity Companion License |
| NUnit 3.13.3、NUnit3TestAdapter 4.6.0、Microsoft.NET.Test.Sdk 17.12.0 | Core の .NET 試験（`tests/CoreHarness`） | MIT License |
| VRChat SDK（base / avatars 3.10.5） | `experiments/` の隔離した検証プロジェクトだけで使用。**リポジトリには含めず、各自が VRChat Creator Companion で導入** | VRChat の SDK 利用規約 |
| GitHub Actions（`actions/*`、`game-ci/unity-test-runner`） | CI | 各アクションのライセンス |

Unity は Unity Technologies の、VRChat は VRChat Inc. の商標です。VRM は VRM コンソーシアムの仕様です。
VLat はこれらの組織と提携しておらず、推奨も受けていません。

## 4. 参照した文書

D3D11 Functional Specification、Direct3D / HLSL のドキュメント等は、`docs/experiments/` からリンクで参照しているだけで、
本文の複製は含みません。
