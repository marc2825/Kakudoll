# CI セットアップ

2026-11-22更新。KakudollのCI（`.github/workflows/ci.yml`）は次の6ジョブで構成します。
ライセンスファイルや認証情報をリポジトリへコミットしてはいけません。

| ジョブ | Unityライセンス | 内容 |
| --- | --- | --- |
| `license-free` | 不要（常に実行） | v0.1/v0.2 Schema、Core .NET NUnit、netstandard2.1互換build、保存中の強制終了試験（50回）、公開監査ツールの試験、Gate 0証拠ツール、保存済み実験結果checker、実験SHA256SUMS |
| `unity-license` | 判定のみ | Secretが設定済みかを**値を出力せずに**判定し、Job Summaryへ表で記録 |
| `editmode` | 必要 | Unity EditMode + VLat.Core行カバレッジ80%以上 |
| `playmode` | 必要 | Unity PlayMode（7万頂点/20 Layer/60 ModifierのCPU・GC性能ゲートを含む） |
| `public-history-audit` | 不要（常に実行） | 全履歴を取得し、Gitleaks（版とSHA-256を固定）と `check_public_export.py --git-history` で全ref・全blob・作者メールを検査 |
| `release-readiness` | 不要（常に実行） | 上の結果をPASS / FAIL / NOT RUNで一覧にし、全てPASSなら「公開準備: READY」 |

ライセンスSecretが無い、またはfork PRのようにSecretを参照できないイベントでは、
`editmode`/`playmode` は **skipped** になり、`unity-license` ジョブが
`Unity tests NOT RUN` の警告annotationとJob Summaryを出します。
**skippedはPASSではありません。** `release-readiness` もNOT RUNと表示し、「NOT READY」とします。

## 公開準備の判定（Issue #1）

- `main` へのpushとPRでは、NOT READYは警告だけでCIは失敗しません（fork PRはSecretを使えないため）。
- 公開前やリリース前は、Actions → CI → **Run workflow** で `release_check` にチェックを入れて `main` を実行します。
  このときはPASS以外が1つでもあれば `release-readiness` が失敗します。**この実行が緑になるまで「公開準備完了」と扱いません。**
- リポジトリ変数 `VLAT_REQUIRE_UNITY_TESTS=true` を設定すると、通常のpushでもSecret未設定をCI失敗にできます
  （Secretを設定したあとの `main` 保護に推奨。fork PRも失敗するので、外部からのPRを受け付ける場合は設定しない）。

## リポジトリ変数

`Settings > Secrets and variables > Actions > Variables` で設定します。

| 変数 | 用途 | 未設定のとき |
| --- | --- | --- |
| `VLAT_PUBLIC_AUTHOR_EMAILS` | 履歴監査で公開を許可する作者・コミッターのメール（カンマ区切り）。所有者が公開してよいと決めたものだけ | メール検査だけNOT RUN（ほかの検査は実行）。公開準備はNOT READY |
| `VLAT_REQUIRE_UNITY_TESTS` | `true` でUnity Secret未設定をCI失敗にする | 警告だけ |

`VLAT_PUBLIC_AUTHOR_EMAILS` の値はコミットに含まれる公開済みの情報ですが、変数はログでマスクされません。
ログにも出したくない場合は、同じ名前のSecretにして `ci.yml` の `vars.` を `secrets.` に変えてください。

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
- Gitleaks **8.30.1**（Linux x64 のリリースアーカイブを SHA-256 `551f6fc8…` で照合してから使う。誤検知の除外は `.gitleaks.toml`）
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

## ローカルでの実行記録とCIの区別

CIで実行していない検証をローカルで実行した場合は、README の「テスト実績」に
**日付・コミット・OS・Unityの版・実行場所（ローカル / GitHub Actions）** を書きます。
ローカルのPASSを、CIのPASSとして扱いません。2026-11-22時点では、Unity EditMode / PlayMode は
ローカル（Windows 11 / Unity 2022.3.62f3）でだけ実行しており、GitHub Actions では NOT RUN です。

ローカルで行うカバレッジ確認（CIの `editmode` と同じ下限）:

```bash
Unity -batchmode -projectPath VLatProject -runTests -testPlatform EditMode \
  -testResults artifacts/editmode-results.xml -enableCodeCoverage -debugCodeOptimization \
  -coverageResultsPath artifacts/coverage \
  -coverageOptions "generateAdditionalReports;generateAdditionalMetrics;assemblyFilters:+VLat.Core"
python3 scripts/check_coverage.py artifacts/coverage --assembly VLat.Core --minimum 0.80
```

`check_coverage.py` は下限を下回る場合と、対象アセンブリのデータがない場合に終了コード1を返します（2026-11-22に両方を確認）。

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
