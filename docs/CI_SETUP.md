# CI の設定

CI は `.github/workflows/ci.yml` にあり、`main` への push、Pull Request、手動実行で動きます。
テストの内容と実行方法は [TESTING.md](TESTING.md) を見てください。

ライセンスファイルや認証情報は、リポジトリにコミットしないでください。

## ジョブ

| ジョブ | 内容 |
|---|---|
| `license-free` | Unity 不要のテスト（常に実行） |
| `unity-license` | Unity のライセンスの Secret が設定されているかを確かめる。値は出力しない |
| `editmode` | Unity EditMode のテストと、`VLat.Core` の行カバレッジ 80% の確認 |
| `playmode` | Unity PlayMode のテストと性能の確認 |
| `public-history-audit` | 全履歴を取得し、Gitleaks と `check_public_export.py --git-history` で秘密情報や公開しないファイルを探す（常に実行） |
| `release-readiness` | 上の結果を PASS / FAIL / NOT RUN で一覧にする（常に実行） |

Unity の Secret がないとき（fork からの Pull Request を含む）、`editmode` と `playmode` は skipped になります。
skipped は合格ではありません。`release-readiness` は NOT RUN と表示し、「公開準備: NOT READY」とします。

## Unity のテストを CI で動かす

`Settings > Secrets and variables > Actions` で、次のどちらかを Secret に登録します。

| ライセンス | 登録する Secret |
|---|---|
| Personal / Plus / Pro | `UNITY_EMAIL`、`UNITY_PASSWORD`、`UNITY_LICENSE`（`.ulf` の中身をそのまま）または `UNITY_SERIAL` |
| Floating license server | `UNITY_LICENSING_SERVER` |

Personal の場合、`UNITY_LICENSE` には手元で Unity 2022.3.62f3 をアクティベートしたときの `.ulf` ファイルの中身を、書き換えずに入れます
（Windows では `C:\ProgramData\Unity\Unity_lic.ulf` など）。

登録したら Actions → CI → Run workflow で実行し、`Unity license preflight` の Job Summary で各 Secret が `true` になっていること、
`Unity EditMode` と `Unity PlayMode` が動くことを確かめます。手順の詳細は [GameCI のドキュメント](https://game.ci/docs/github/activation) にあります。

Secret の値は、ログや Issue、Pull Request に貼らないでください。

## リポジトリ変数

`Settings > Secrets and variables > Actions > Variables` で設定します。

| 変数 | 用途 | 未設定のとき |
|---|---|---|
| `VLAT_PUBLIC_AUTHOR_EMAILS` | 履歴の監査で、公開してよいコミットの作者メール（カンマ区切り） | メールの検査だけ NOT RUN になる |
| `VLAT_REQUIRE_UNITY_TESTS` | `true` にすると、Unity の Secret がないときに CI を失敗させる | 警告だけ出す |

リポジトリ変数の値はログに表示されます。メールアドレスをログにも出したくない場合は、同じ名前の Secret にして、`ci.yml` の `vars.` を `secrets.` に変えてください。

`VLAT_REQUIRE_UNITY_TESTS` を `true` にすると、fork からの Pull Request も失敗します。外部からの Pull Request を受け付けるあいだは設定しないでください。

## 公開前・リリース前の確認

Actions → CI → Run workflow で `release_check` にチェックを入れて `main` を実行します。
このときは PASS 以外が 1 つでもあると `release-readiness` が失敗します。この実行が通るまでは、公開やリリースの準備ができたとは扱いません。

普段の push と Pull Request では、NOT READY は警告になるだけです。

## 固定しているバージョン

- Unity 2022.3.62f3、UniVRM v0.128.3（VRChat の実験で使った Unity 2022.3.22f1 とは別）
- `game-ci/unity-test-runner` v4.3.2（commit `fa6ced25861c16ef56187828c43f76d00df43a23`）
- `actions/checkout@v7`、`actions/setup-python@v7`、`actions/setup-dotnet@v6`、`actions/cache@v6`、`actions/upload-artifact@v7`
- Gitleaks 8.30.1（ダウンロードしたファイルを SHA-256 で照合してから使う。誤検知の除外は `.gitleaks.toml`）
- runner は `ubuntu-24.04`
- .NET SDK 10.0.x、Python 3.12、`jsonschema>=4.10,<5`

workflow の既定の権限は `contents: read` です。Unity のジョブだけが、テスト結果を表示するために `checks: write` を持ちます。
