# Unity非依存Coreの補助試験

Unity用の実Coreソースと、既存Core試験60件・Keyform試験30件をリンクする。
UnityEngine/Editorのstubを使わない。Unity EditMode/PlayModeやVRChatの代替ではない。

リポジトリ内から.NET SDK 10で実行:

```bash
dotnet test tests/CoreHarness/Core.Tests.csproj \
  --logger "trx;LogFileName=core.trx" --results-directory artifacts/core
dotnet build tests/CoreHarness/Compatibility/Core.Compatibility.csproj
python3 scripts/validate_schema.py
python3 scripts/validate_keyform_schema.py
```

いずれもC# 9でコンパイル。Compatibilityはnetstandard2.1のAPI範囲を確認する。
NuGet依存はcsprojに固定した。配布UPMの依存には追加しない。
隔離キャッシュを使う場合は `DOTNET_CLI_HOME` / `NUGET_PACKAGES` を一時ディレクトリに設定する。
ソケットを禁止するsandboxではVSTestのプロセス間通信も拒否されるため、
実行環境でローカルソケットを許可する必要がある。

Unity固有の5試験は `VlatBlendShapeKeyformImporterTests` にあり、この補助ハーネスには含めない。
実UnityのUI操作・実アバター・VRChat実機は別途 `docs/product/TEST_MATRIX.md` に記録する。
