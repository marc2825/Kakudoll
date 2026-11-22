# Core の .NET テスト

Unity に依存しない Core のコードを、Unity なしで .NET SDK 10 で試すためのプロジェクトです。
Unity の EditMode・PlayMode のテストの代わりにはなりません。

- `Core.Tests.csproj`: Core のソースと、Unity を使わないテスト（`Tests/EditMode` の一部）をまとめてビルドし、NUnit で実行する
- `Compatibility/`: Unity と同じ .NET Standard 2.1 の範囲でビルドできるかを確かめる
- `SaveCrash/`: 保存中のプロセスを強制終了し、復旧後のファイルが保存前か保存後のどちらかに一致するかを確かめる

```bash
dotnet test tests/CoreHarness/Core.Tests.csproj --logger "trx;LogFileName=core.trx" --results-directory artifacts/core
dotnet build tests/CoreHarness/Compatibility/Core.Compatibility.csproj
dotnet run -c Release --project tests/CoreHarness/SaveCrash/SaveCrash.csproj -- 100
```

普段は `python3 scripts/run_license_free_checks.py` からまとめて実行されます。
どれも C# 9 でビルドします。NuGet の依存は csproj で固定しており、配布するパッケージの依存には加えていません。

ソケットを禁止する環境では、VSTest のプロセス間通信も止められるため、テストが動きません。
Unity 専用のテスト（`VlatBlendShapeKeyformImporterTests` など）はここには含めていません。
