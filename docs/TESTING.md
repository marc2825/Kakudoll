# テスト

## テストの種類

| 種類 | 内容 | 合格の条件 |
|---|---|---|
| Unity 不要のテスト | JSON Schema、Core の単体テスト（.NET）、.NET Standard 2.1 でのビルド、保存中にプロセスを強制終了する試験、公開前の監査ツールのテスト、保存済みの実験結果の再検査 | すべて合格 |
| Unity EditMode | エディタの機能を含む全体のテスト | すべて合格し、`VLat.Core` の行カバレッジが 80% 以上 |
| Unity PlayMode | 実行時の再生（v0.1） | すべて合格し、7 万頂点・20 Layer の評価が 1 フレームあたり中央値 0.3 ms 以下、GC 割り当て 0 |

スクリプトや CI が「NOT RUN」と出したものは、実行していないという意味です。合格には数えません。
配布していないファイル（商用アバターの測定データなど）が必要な検査は、公開版では NOT RUN になります。

## 実行のしかた

Unity 不要のテスト（Python 3.10 以降と .NET SDK 10 が必要）:

```bash
python3 -m pip install "jsonschema>=4.10,<5"
python3 scripts/run_license_free_checks.py
```

.NET SDK がない環境では `--skip-dotnet` を付けます。その場合、.NET のテストは NOT RUN として記録されます。

Unity のテストは、Test Runner（Window > General > Test Runner）か、コマンドラインで実行します。

```bash
Unity -batchmode -projectPath VLatProject -runTests -testPlatform EditMode \
  -testResults artifacts/editmode-results.xml -enableCodeCoverage -debugCodeOptimization \
  -coverageResultsPath artifacts/coverage \
  -coverageOptions "generateAdditionalReports;generateAdditionalMetrics;assemblyFilters:+VLat.Core"
python3 scripts/check_coverage.py artifacts/coverage --assembly VLat.Core --minimum 0.80
```

PlayMode は `-testPlatform PlayMode` にします。

WSL から Windows の Unity を使う場合は、リポジトリとは別の場所にプロジェクトを複製して実行するスクリプトがあります。
複製先には、あらかじめ `VLatProject/` を置いておきます。

```bash
python3 scripts/run_editor_1e.py --stage <複製先> --label <結果の名前> --mode EditMode
```

保存中の強制終了の試験は、回数を指定して単独でも実行できます（100 回で 20 秒ほど）。

```bash
dotnet run -c Release --project tests/CoreHarness/SaveCrash/SaveCrash.csproj -- 100
```

## 最新の結果

| テスト | 実行した場所 | 結果 | 日付・コミット・環境 |
|---|---|---|---|
| Unity 不要のテスト | 手元 | 合格（配布していないファイルが必要な 5 件は NOT RUN） | 2026-11-22、`20a9449`、WSL2 Ubuntu 24.04、.NET SDK 10.0.115、Python 3.12 |
| Unity 不要のテスト | GitHub Actions | 合格 | push と Pull Request のたびに実行 |
| Unity EditMode | 手元 | 413 件すべて合格。`VLat.Core` の行カバレッジ 90.82% | 2026-11-22、`20a9449`、Windows 11 Pro、Unity 2022.3.62f3、D3D11 |
| Unity PlayMode | 手元 | 4 件すべて合格。評価は中央値 11.71 µs/フレーム、GC 割り当て 0 | 同上 |
| Unity EditMode・PlayMode | GitHub Actions | NOT RUN（Unity のライセンスを設定していない） | — |
| 履歴の監査（Gitleaks など） | GitHub Actions | 合格 | push と Pull Request のたびに実行 |
| 新しいプロジェクトへの導入と削除 | 手元 | 合格（GitHub の URL、ローカルの Git リポジトリ、ローカルパスの 3 通り） | 2026-11-22、`d8c1daa`・`20a9449`、Windows 11、Unity 2022.3.62f3、UniVRM v0.128.3 |
| VRChat クライアント・HMD | — | NOT RUN | [ROADMAP](product/ROADMAP.md) の段階 5 |

GitHub Actions の結果は、各 run の Job Summary にある「Release readiness」の表で確かめられます。

結果を書き足すときは、日付・コミット・OS・Unity のバージョン・実行した場所を書いてください。
手元で合格したテストを、CI で合格したように書かないでください。
