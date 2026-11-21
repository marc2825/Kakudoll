# 調査した依存版とAPI

2026-10-20。対応「予定」と検証済みを混同しない。

| 対象 | 実ファイルで確認した版 | 今回の利用・制限 |
|---|---|---|
| VLat Unity baseline | 2022.3.62f3 / 96770f904ca7 | ProjectVersion/CI固定。作業中にユーザーが導入し、exe `-version` も一致 |
| Built-in RP / C# | Built-in / C# 9 | asmdefの逆依存追加なし |
| UniVRM | 0.128.3 / b045022e3e76977b7bb3e343669b447b7b77e606 | manifest/lock固定。今回ImporterはUniVRM APIを増やさない |
| Unity Newtonsoft | com.unity.nuget.newtonsoft-json 3.2.1 | 既存依存。別のJSONライブラリを追加しない |
| Unity Test Framework | 1.3.7 / Performance 3.0.3 | VLat開発プロジェクト |
| .NET補助試験 | SDK 10.0.112、NUnit 3.13.3、Newtonsoft.Json 13.0.2 | Coreソースのみ。Unity試験とは別の証拠 |
| VRChat SDK base/avatars | 3.10.5 | 保存済み診断TEMPの実package.jsonとAPI sourceを確認。今回導入・ビルド・uploadなし |
| VRChat診断Unity | 2022.3.22f1 | 既存S1/S2証拠の版。今回VLat baselineをこれへ変更しない |
| lilToon | 2.3.4 | ローカルsourceおよび調査対象VCCプロジェクトのpackage.json。VLat製品統合なし |
| NDMF / Modular Avatar / Avatar Optimizer | 調査対象VCCプロジェクトに未導入 | 実版・実API互換性NOT RUN。将来導入時に固定・再監査 |

API照合:

- Unity `Mesh.GetBlendShapeFrameCount`, `GetBlendShapeFrameWeight`,
  `GetBlendShapeFrameVertices(shapeIndex, frameIndex, positions, null, null)` を使用。
  位置差分だけを読むことと、配列が元vertex countに対応することを確認。
  [Unity 2022.3 API](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Mesh.GetBlendShapeFrameVertices.html)
- Topology fingerprintでは `Mesh.GetIndices(subMesh, applyBaseVertex: true)` を使用。
  [Unity 2022.3 API](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Mesh.GetIndices.html)
- SDK 3.10.5の `IVRCSdkAvatarBuilderApi` と実装に
  `Task BuildAndTest(GameObject target)` があることを読取確認。呼び出していない。
- ローカルlilToon 2.3.4の `lil_common_vert.hlsl` で
  `LIL_CUSTOM_VERTEX_OS` / `LIL_CUSTOM_VERTEX_WS` を確認。
  これだけでOutline/Shadow/Depth/Stereoの対応を推定しない。
  [公式拡張資料](https://lilxyzw.github.io/lilToon/ja_JP/dev/custom_shader.html)
- NDMFは[公式Execution Model](https://ndmf.nadena.dev/execution-model.html)を参照した。
  ローカルに選定版がなく、phase選択・他プラグインとの順序は未決定。
- 固定UniVRMの `Vrm10Instance.Humanoid` と、その型の実asmdef `UniHumanoid` を確認した。
  既存Preview試験の文字列lookupによる失敗を修正するため、test asmdefだけに直接参照を追加。
  Runtime/Core/Editorの依存方向は変更しない。

最終実Unityのimport/compile、EditMode189件、PlayMode4件は全PASS。
初回失敗と修正の記録を含め、詳細は `TEST_MATRIX.md` を参照。

Unity検証にはVLatパッケージと開発プロジェクトだけを新しいWindows TEMPへコピーする。
既存のアバター制作プロジェクト、私有FBX、VRChat認証情報はコピー・変更しない。
現行VRChat全般や最新SDK全般の互換性を、この表から主張しない。

## 2026-10-21 — 1Aで再確認した実行環境

Unity 2022.3.62f3/96770f904ca7、固定manifest/lockは変更なし。
UI Toolkitの `ObjectField`, `DoubleField`, `DropdownField`, `ListView` と
`NavigationSubmitEvent` による実Window自動操作をこの実版でcompile・実行した。
[Unity 2022.3 NavigationSubmitEvent](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/UIElements.NavigationSubmitEvent.html)。
OS file pickerは通常のEditorUtilityを維持し、試験で選択結果のみを注入する。

.NET補助試験の今回の実SDKは **10.0.113**（前回10.0.112）。
固定NuGet版、C#9、netstandard2.1の範囲は変更なし。
Editor接続のための新規依存・asmdef参照は追加していない。
SDK/NDMF/lilToonの新しい互換実証は今回行っていない。

## 2026-10-21 — 1Bの実APIと検証範囲

依存・asmdef・ProjectVersionは変更なし。2022.3.62f3の実Editorで、
EditorJsonUtilityのMonoBehaviourルート、Undo.FlushUndoRecordObjects、Window OnDisable、
AssemblyReloadEventsとEditorUtility.RequestScriptReloadをcompile・実行確認した。
導入済みcom.unity.test-framework 1.3.7のWaitForDomainReload実装とPreserveTestState sampleを
読取確認し、実Reloadを期待するyieldとSerializeFieldによる試験状態保持を利用した。
Source: 検証copyのLibrary/PackageCache/com.unity.test-framework@1.3.7/
UnityEditor.TestRunner/TestRunner/Messages/WaitForDomainReload.cs。

Windows11/D3D11の実Domain Reloadと別プロセス再起動はPASS。
NTFS/FileShare.Noneの書込失敗試験はWindows専用。他OS・異常終了は別検証。
Newtonsoft.Jsonは既存Unity依存を使い、新しい配布依存を追加していない。

## 2026-10-21 — 1Cの実APIと描画

依存/asmdef/ProjectVersionは不変。Unity2022.3.62f3/96770f904ca7、Windows11/D3D11で
SkinnedMeshRenderer.forceMatrixRecalculationPerRender / updateWhenOffscreen / BakeMesh、
Camera.Render、head.InverseTransformPoint、RenderTexture readbackをcompile・実行した。
元値の復元と専用cloneへの限定、実画像の独立oracle一致を試験した。
[Unity SkinnedMeshRenderer API](https://docs.unity.cn/ScriptReference/SkinnedMeshRenderer.html)。
Raw VRM読込とserialized ExpressionのAPIは既存UniVRM0.128.3のまま。
実Test Framework1.3.7のWaitForDomainReloadで1Cと1Bの実Reloadを確認した。
新しいSDK/NDMF/lilToon互換性は検証していない。Scene/VRChat FBX読込UIは未追加。

## 2026-11-19 — 監査時の再確認

| 対象 | 確認した版 | 扱い |
|---|---|---|
| VLat Unity | 2022.3.62f3 / 96770f904ca7 | 不変。EditMode/PlayModeを再実行 |
| VRChat診断Unity | 2022.3.22f1（インストール存在のみ確認） | 起動していない |
| VRChat SDK | 実験の凍結版は3.10.5。最新は **3.10.6（2026-10-30）** | 3.10.6は未導入・未検証。差分と必要な試験は `docs/handoff/VRC_CLIENT_VALIDATION.md`（非公開アーカイブのみ） |
| .NET SDK（補助試験） | 10.0.115 | Core 90/90、netstandard2.1 build PASS |
| GitHub Actions | checkout v7 / setup-python v7 / setup-dotnet v6 / cache v6 / upload-artifact v7（node24） | release noteの破壊的変更は本workflowの使用範囲に該当しないことを確認 |
| game-ci/unity-test-runner | v4.3.2（`fa6ced25861c16ef56187828c43f76d00df43a23`、node24） | 旧来の安定構成のbackport。`@v4`（v4.4.0のthin wrapper）ではなくcommit固定 |

使用した新しいUnity API: `Unity.Profiling.ProfilerMarker`（Editor計測）、
`Matrix4x4.GetColumn`（逆投影の線形分解）、UI Toolkitの `TwoPaneSplitView` 入れ子・`Foldout`・
`Slider.showInputField`・USS `var()`（fallback付き）。いずれも2022.3.62f3で実compile・EditMode実行済み。
試験probeの `GUIView.GrabPixels` は内部APIで、画像確認用に限定し製品コードでは使わない。
