# CI セットアップ

2026-11-19更新。VLatのCI（`.github/workflows/ci.yml`）は次の4ジョブで構成します。
ライセンスファイルや認証情報をリポジトリへコミットしてはいけません。

| ジョブ | Unityライセンス | 内容 |
| --- | --- | --- |
| `license-free` | 不要（常に実行） | v0.1/v0.2 Schema、Core .NET NUnit、netstandard2.1互換build、Gate 0証拠ツール、保存済み実験結果checker、実験SHA256SUMS |
| `unity-license` | 判定のみ | Secretが設定済みかを**値を出力せずに**判定し、Job Summaryへ表で記録 |
| `editmode` | 必要 | Unity EditMode + VLat.Core行カバレッジ80%以上 |
| `playmode` | 必要 | Unity PlayMode（7万頂点/20 Layer/60 ModifierのCPU・GC性能ゲートを含む） |

ライセンスSecretが無い、またはfork PRのようにSecretを参照できないイベントでは、
`editmode`/`playmode` は **skipped** になり、`unity-license` ジョブが
`Unity tests NOT RUN` の警告annotationとJob Summaryを出します。
**skippedはPASSではありません。** 台帳ではNOT RUNとして扱ってください。
リポジトリ変数 `VLAT_REQUIRE_UNITY_TESTS` を `true` にすると、Secret未設定をCI失敗にできます
（Secret設定後の`main`保護に推奨）。

## 過去の失敗の分類（2026-11-19確認）

GitHub Actionsの全9 run（2026-07-20〜2026-10-16）は、Unityテストが起動する前の
**ライセンス未設定**で失敗していました。コードのテスト失敗ではありません。

- 2026-07〜08の`main` push: `Missing Unity License File and no Serial was found.`
  の後、空の`artifactsPath`をupload-artifactへ渡したため
  `Input required and not supplied: path` の二次エラー。
- 2026-10-16のPR: `License activation strategy could not be determined.`
  （upload stepは対象なしの警告）。
- 同期間の`JSON Schema`ジョブは全てPASS。

現在の構成では、テストが起動しなかった場合はstep outputが空になるため、uploadを実行しません。

## 使用バージョン

- Unity `2022.3.62f3`（VRChat検証用2022.3.22f1とは別。混同しない）
- UniVRM `v0.128.3`
- Unity Image Conversion `1.0.0` / Timeline `1.7.7`
- `game-ci/unity-test-runner` **v4.3.2**（commit `fa6ced25861c16ef56187828c43f76d00df43a23` に固定。node24）
- `actions/checkout@v7`, `actions/setup-python@v7`, `actions/setup-dotnet@v6`,
  `actions/cache@v6`, `actions/upload-artifact@v7`（いずれもnode24。Node 20廃止警告への対応）
- runner `ubuntu-24.04` に固定（`ubuntu-latest` のUbuntu 26移行による暗黙変更を避ける）
- Unity Code Coverage `1.2.6` / Unity Performance Testing `3.0.3`
- .NET SDK `10.0.x`、Python `3.12`、`jsonschema>=4.10,<5`

Workflowの既定権限は `contents: read`。Unityジョブだけが、GameCIのcheck run作成のために
`checks: write` を持ちます。checkoutは `persist-credentials: false`。

## GitHub Actions Secrets（Unityジョブを有効にする手順）

リポジトリの `Settings > Secrets and variables > Actions` で登録します。
game-ci v4.3.2 は次のいずれかの組み合わせを受け付けます。

| 方式 | 必要なSecret |
| --- | --- |
| Personal / Plus / Pro シート | `UNITY_EMAIL` + `UNITY_PASSWORD` + `UNITY_LICENSE`（`.ulf`本文）または `UNITY_SERIAL` |
| Floating license server | `UNITY_LICENSING_SERVER` |

1. Unity IDのメールアドレスとパスワードを `UNITY_EMAIL` / `UNITY_PASSWORD` へ登録する。
2. Personalでは、手元でUnity 2022.3.62f3をアクティベートした `.ulf` の**XML本文を改変せず**
   `UNITY_LICENSE` へ登録する（Windows例: `C:\ProgramData\Unity\Unity_lic.ulf`）。
   Pro/Plusでは代わりに `UNITY_SERIAL` を登録する。
3. ActionsタブでCIを `workflow_dispatch` から実行し、`Unity license preflight` のJob Summaryで
   各Secretが `true` と表示され、`Unity EditMode`/`Unity PlayMode` が起動することを確認する。
4. 結果がPASSしたら、必要に応じてリポジトリ変数 `VLAT_REQUIRE_UNITY_TESTS=true` を設定する。

Secretの値をログ・Issue・PRへ貼らないでください。preflightは真偽値だけを出力します。
GameCIの手順: <https://game.ci/docs/github/activation>

## ローカル実行

ライセンス不要の検証はCIと同じ入口で実行できます（Python 3.10以降、.NET SDK 10）。

```bash
python -m pip install "jsonschema>=4.10,<5"
python3 scripts/run_license_free_checks.py
```

`--skip-dotnet` を付けると.NET試験を**NOT RUN**として記録します（PASS扱いしません）。

Unity EditMode/PlayModeは、原プロジェクトではなくリポジトリ外の隔離コピーで実行します
（WSL + Windows Unityの例。stageには `VLatProject/`・`Packages/`・`schema/` を事前に用意）。

```bash
python3 scripts/run_editor_1e.py --stage <isolated-stage> --label <unique> --mode EditMode
python3 scripts/run_editor_1e.py --stage <isolated-stage> --label <unique> --mode PlayMode
```

Unityを直接実行する場合:

```bash
Unity -batchmode -nographics -projectPath VLatProject \
  -runTests -testPlatform EditMode -testResults artifacts/editmode-results.xml
```

CIの結果はGitHubのChecksと `license-free-results`／`editmode-results`／`editmode-coverage`／
`playmode-results` artifactで確認できます。`VLat.Core` の行カバレッジが80%未満、
またはRuntimeの定常中央値が0.3 ms/frameを超える場合もCIは失敗します。
ライセンスに関するログやartifactを外部へ公開する前に、秘密情報が含まれていないことを確認してください。
