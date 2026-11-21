using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VLat.Editor.Authoring;
using Object = UnityEngine.Object;

namespace VLat.Tests.Edit
{
    /// <summary>D-011（pack済みdelta・Recovery manifest v3）とD-012（版注意の表示）の回帰を検証します。</summary>
    public sealed class VlatDecisionRegressionTests
    {
        private static List<VlatAuthoringVertexDelta> SampleDeltas(int count, bool normals)
        {
            var result = new List<VlatAuthoringVertexDelta>(count);
            for (var i = 0; i < count; i++)
                result.Add(new VlatAuthoringVertexDelta
                {
                    VertexIndex = i * 3 + 1,
                    PositionDelta = new Vector3(i * 0.125f, -i * 0.0625f, 0.5f),
                    NormalDelta = normals && i % 2 == 0 ? new Vector3(0, 1, 0) : Vector3.zero,
                });
            return result;
        }

        private static void AssertSameBits(IReadOnlyList<VlatAuthoringVertexDelta> expected, IReadOnlyList<VlatAuthoringVertexDelta> actual)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.That(actual[i].VertexIndex, Is.EqualTo(expected[i].VertexIndex), "index " + i);
                for (var c = 0; c < 3; c++)
                {
                    Assert.That(BitConverter.SingleToInt32Bits(actual[i].PositionDelta[c]),
                        Is.EqualTo(BitConverter.SingleToInt32Bits(expected[i].PositionDelta[c])), "position " + i);
                    Assert.That(BitConverter.SingleToInt32Bits(actual[i].NormalDelta[c]),
                        Is.EqualTo(BitConverter.SingleToInt32Bits(expected[i].NormalDelta[c])), "normal " + i);
                }
            }
        }

        /// <summary>packはbinary32のbit列（負のゼロ・非正規化数・最大値を含む）と法線を厳密に保持します。</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Packing_RoundTripsExactBits(bool normals)
        {
            var deltas = SampleDeltas(257, normals);
            deltas[0].PositionDelta = new Vector3(-0f, float.Epsilon, float.MaxValue);
            deltas[1].PositionDelta = new Vector3(float.MinValue, 1e-38f, -3.25f);
            var packed = VlatAuthoringDeltaPacking.Encode(deltas);
            Assert.That(packed, Is.Not.Empty);
            Assert.That(VlatAuthoringDeltaPacking.TryDecode(packed, out var decoded, out var error), Is.True, error);
            AssertSameBits(deltas, decoded);
            Assert.That(VlatAuthoringDeltaPacking.Encode(new List<VlatAuthoringVertexDelta>()), Is.Empty);
            Assert.That(VlatAuthoringDeltaPacking.TryDecode(string.Empty, out var empty, out _), Is.True);
            Assert.That(empty, Is.Empty);
        }

        /// <summary>壊れたpack済みdeltaは例外ではなく理由付きで拒否され、部分的な差分を返しません。</summary>
        [TestCase("base64")]
        [TestCase("format")]
        [TestCase("truncated")]
        [TestCase("count")]
        public void Packing_RejectsCorruptInput(string mode)
        {
            var bytes = Convert.FromBase64String(VlatAuthoringDeltaPacking.Encode(SampleDeltas(4, false)));
            string packed;
            switch (mode)
            {
                case "base64": packed = "!!not base64!!"; break;
                case "format": bytes[0] = 9; packed = Convert.ToBase64String(bytes); break;
                case "truncated": packed = Convert.ToBase64String(bytes, 0, bytes.Length - 1); break;
                default: bytes[2] = 5; packed = Convert.ToBase64String(bytes); break;
            }
            Assert.That(VlatAuthoringDeltaPacking.TryDecode(packed, out var decoded, out var error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(decoded, Is.Empty);
        }

        /// <summary>DocumentのUnity直列化はpack済み表現になり、要素ごとのobjectを出力せずに往復します。</summary>
        [Test]
        public void DocumentSerialization_UsesPackedDeltasAndRoundTrips()
        {
            var document = VlatAuthoringPersistence.CreateNew("Packed Avatar");
            var copy = VlatAuthoringDocument.CreateTemporary();
            try
            {
                var key = new VlatAuthoringKeyform { Yaw = 15 };
                var expected = SampleDeltas(1000, false);
                key.Deltas.AddRange(expected);
                var group = new VlatAuthoringKeyformGroup();
                group.Keys.Add(new VlatAuthoringKeyform { Yaw = 0 });
                group.Keys.Add(key);
                VlatAuthoringCommands.AddKeyformGroup(document, group);
                var json = EditorJsonUtility.ToJson(document, false);
                Assert.That(json, Does.Contain("\"_packedDeltas\":\""));
                Assert.That(json, Does.Not.Contain("_vertexIndex"));
                Assert.That(key.Deltas, Has.Count.EqualTo(1000), "直列化は実行時の差分列を変更しない");
                EditorJsonUtility.FromJsonOverwrite(json, copy);
                var restored = copy.KeyformGroups[0].Keys[1];
                Assert.That(restored.SerializationError, Is.Null);
                AssertSameBits(expected, restored.Deltas);
                Assert.That(copy.KeyformGroups[0].Keys[0].Deltas, Is.Empty);
                Assert.That(EditorJsonUtility.ToJson(copy, false), Is.EqualTo(json), "表現は決定的");
                // pack文字列を解放しても、次の直列化で同じ表現が再生成される（memory常駐の回避）。
                document.ReleaseSerializationCaches();
                Assert.That(EditorJsonUtility.ToJson(document, false), Is.EqualTo(json));
            }
            finally
            {
                Undo.ClearAll();
                Object.DestroyImmediate(copy);
                Object.DestroyImmediate(document);
            }
        }

        private static (VlatAuthoringDocument document, List<VlatAuthoringVertexDelta> expected) KeyformDocument()
        {
            var document = VlatAuthoringPersistence.CreateNew("Recovery v3 Avatar");
            var group = new VlatAuthoringKeyformGroup();
            var key = new VlatAuthoringKeyform { Yaw = 0 };
            var expected = SampleDeltas(64, false);
            key.Deltas.AddRange(expected);
            group.Keys.Add(key);
            VlatAuthoringCommands.AddKeyformGroup(document, group);
            return (document, expected);
        }

        /// <summary>新規Recoveryはmanifest v3で書かれ、旧v2の要素ごとsnapshotも復元でき、再保存でv3になります。</summary>
        [Test]
        public void Recovery_WritesV3AndRestoresLegacyV2ElementSnapshot()
        {
            var directory = Path.Combine(Path.GetTempPath(), "VLatD011-" + Guid.NewGuid().ToString("N"));
            var (document, expected) = KeyformDocument();
            VlatAuthoringDocument restored = null;
            try
            {
                var store = new VlatRecoveryStore(directory);
                store.Write(document, document.Revision, new VlatEditorSelection(), "sha256:saved");
                var manifestPath = Path.Combine(directory, "editor-session.json");
                var manifest = JObject.Parse(File.ReadAllText(manifestPath));
                Assert.That((int)manifest["version"], Is.EqualTo(3));

                // 旧実装（v2）が書いた要素ごとの表現へ変換する。値は10進表記で厳密に表せるものだけを使う。
                var snapshotPath = Path.Combine(directory, (string)manifest["snapshotFile"]);
                var snapshot = JObject.Parse(File.ReadAllText(snapshotPath));
                var root = snapshot["MonoBehaviour"] as JObject ?? snapshot;
                var keyJson = (JObject)root["_keyformGroups"][0]["_keys"][0];
                var legacy = new JArray();
                foreach (var delta in expected)
                    legacy.Add(new JObject
                    {
                        ["_vertexIndex"] = delta.VertexIndex,
                        ["_positionDelta"] = new JObject { ["x"] = delta.PositionDelta.x, ["y"] = delta.PositionDelta.y, ["z"] = delta.PositionDelta.z },
                        ["_normalDelta"] = new JObject { ["x"] = 0f, ["y"] = 0f, ["z"] = 0f },
                    });
                keyJson["_deltas"] = legacy;
                keyJson.Remove("_packedDeltas");
                var legacyText = snapshot.ToString(Newtonsoft.Json.Formatting.None);
                File.WriteAllText(snapshotPath, legacyText);
                manifest["version"] = 2;
                manifest["snapshotHash"] = VlatSessionFingerprint.Hash(System.Text.Encoding.UTF8.GetBytes(legacyText));
                File.WriteAllText(manifestPath, manifest.ToString());

                Assert.That(store.TryRead(out var state), Is.True, store.LastReadError);
                restored = state.Document;
                AssertSameBits(expected, restored.KeyformGroups[0].Keys[0].Deltas);
                store.Write(restored, restored.Revision, new VlatEditorSelection(), state.SavedContent);
                var rewritten = JObject.Parse(File.ReadAllText(manifestPath));
                Assert.That((int)rewritten["version"], Is.EqualTo(3));
                var rewrittenSnapshot = File.ReadAllText(Path.Combine(directory, (string)rewritten["snapshotFile"]));
                Assert.That(rewrittenSnapshot, Does.Contain("\"_packedDeltas\":\"").And.Not.Contain("_vertexIndex"));
            }
            finally
            {
                if (restored != null) Object.DestroyImmediate(restored);
                Object.DestroyImmediate(document);
                Undo.ClearAll();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        /// <summary>pack済みdeltaが壊れたsnapshot、旧/新表現の混在は、空の差分として黙って復元せず隔離します。</summary>
        [TestCase("corrupt")]
        [TestCase("both")]
        public void Recovery_CorruptPackedDeltasAreQuarantined(string mode)
        {
            var directory = Path.Combine(Path.GetTempPath(), "VLatD011Bad-" + Guid.NewGuid().ToString("N"));
            var (document, _) = KeyformDocument();
            try
            {
                var store = new VlatRecoveryStore(directory);
                store.Write(document, document.Revision, new VlatEditorSelection(), "sha256:saved");
                var manifestPath = Path.Combine(directory, "editor-session.json");
                var manifest = JObject.Parse(File.ReadAllText(manifestPath));
                var snapshotPath = Path.Combine(directory, (string)manifest["snapshotFile"]);
                var snapshot = JObject.Parse(File.ReadAllText(snapshotPath));
                var root = snapshot["MonoBehaviour"] as JObject ?? snapshot;
                var keyJson = (JObject)root["_keyformGroups"][0]["_keys"][0];
                if (mode == "corrupt") keyJson["_packedDeltas"] = "AQAAAA==";
                else keyJson["_deltas"] = new JArray(new JObject { ["_vertexIndex"] = 1 });
                var text = snapshot.ToString(Newtonsoft.Json.Formatting.None);
                File.WriteAllText(snapshotPath, text);
                manifest["snapshotHash"] = VlatSessionFingerprint.Hash(System.Text.Encoding.UTF8.GetBytes(text));
                File.WriteAllText(manifestPath, manifest.ToString());

                Assert.That(store.TryRead(out var state), Is.False);
                Assert.That(state, Is.Null);
                Assert.That(store.LastReadError, Does.Contain("差分"));
                Assert.That(Directory.GetFiles(Path.Combine(directory, "Rejected"), "*.json", SearchOption.AllDirectories), Is.Not.Empty);
            }
            finally
            {
                Object.DestroyImmediate(document);
                Undo.ClearAll();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        /// <summary>復元できなかったキー差分を持つDocumentは保存検証でErrorになり、空の差分として保存されません。</summary>
        [Test]
        public void SaveValidation_RejectsKeyWithSerializationError()
        {
            var (document, _) = KeyformDocument();
            var broken = VlatAuthoringDocument.CreateTemporary();
            try
            {
                var json = JObject.Parse(EditorJsonUtility.ToJson(document, false));
                var root = json["MonoBehaviour"] as JObject ?? json;
                root["_keyformGroups"][0]["_keys"][0]["_packedDeltas"] = "AQAAAA==";
                EditorJsonUtility.FromJsonOverwrite(json.ToString(), broken);
                Assert.That(broken.KeyformGroups[0].Keys[0].SerializationError, Is.Not.Null);
                // 復元できなかったデータは解放・再直列化でも保持され、空の中立キーとして上書きされない。
                broken.ReleaseSerializationCaches();
                Assert.That(EditorJsonUtility.ToJson(broken, false), Does.Contain("AQAAAA=="));
                Assert.That(broken.KeyformGroups[0].Keys[0].SerializationError, Is.Not.Null);
                var diagnostics = VlatAuthoringPersistence.Validate(broken, VlatAuthoringValidationMode.Save);
                Assert.That(diagnostics.Any(d => d.Severity == VLat.Core.Validation.VlatDiagnosticSeverity.Error &&
                    d.Path.EndsWith(".keys[0].delta", StringComparison.Ordinal) && d.Message.Contains("復元できません")), Is.True);
                // 利用者が差分を入れ直すと、通常の表現へ戻りエラーも解消する。
                broken.KeyformGroups[0].Keys[0].Deltas.AddRange(SampleDeltas(2, false));
                var repaired = EditorJsonUtility.ToJson(broken, false);
                Assert.That(repaired, Does.Not.Contain("AQAAAA=="));
                Assert.That(broken.KeyformGroups[0].Keys[0].SerializationError, Is.Null);
            }
            finally
            {
                Undo.ClearAll();
                Object.DestroyImmediate(broken);
                Object.DestroyImmediate(document);
            }
        }

        /// <summary>Keyform v0.2文書の版警告はCoreに残したまま、Windowでは情報1件として表示し、警告数に数えません。</summary>
        [Test]
        public void Window_KeyformVersionWarningShownOnceAsInfo()
        {
            using var f = new VlatKeyformPreviewFixture(true);
            var directory = Path.Combine(f.DirectoryPath, "decision-window");
            var window = VlatEditorWindow.CreateForRecoveryDirectory(directory);
            try
            {
                var session = window.DocumentController;
                Assert.That(session.AddKeyformGroup(), Is.True);
                Assert.That(session.BindKeyformTarget(0, f.Source.gameObject, f.SourceRenderer), Is.True, session.LastMessage);
                Assert.That(session.ImportKeyformPositions(0, 0, f.Source.gameObject, f.SourceRenderer, 0), Is.True, session.LastMessage);
                var coreVersionWarnings = session.Diagnostics.Count(d => d.Path == "$.vlat.version" &&
                    d.Severity == VLat.Core.Validation.VlatDiagnosticSeverity.Warning);
                Assert.That(coreVersionWarnings, Is.GreaterThanOrEqualTo(1), "Coreの判定は変更しない");

                window.CreateGUI();
                var items = window.rootVisualElement.Q<ListView>("diagnostic-list").itemsSource
                    .Cast<VlatEditorWindow.DiagnosticDisplayItem>().ToList();
                var versionItems = items.Where(i => i.Path == "$.vlat.version").ToList();
                Assert.That(versionItems, Has.Count.EqualTo(1));
                Assert.That(versionItems[0].Level, Is.EqualTo(VlatEditorWindow.DiagnosticDisplayLevel.Info));
                Assert.That(versionItems[0].Message, Does.Contain("Keyform v0.2"));
                Assert.That(items.Select(i => (i.Level, i.Path, i.Message)).Distinct().Count(), Is.EqualTo(items.Count), "重複なし");
                var errors = items.Count(i => i.Level == VlatEditorWindow.DiagnosticDisplayLevel.Error);
                var warnings = items.Count(i => i.Level == VlatEditorWindow.DiagnosticDisplayLevel.Warning);
                var summary = window.rootVisualElement.Q<Label>("validation-summary").text;
                if (errors != 0) Assert.That(summary, Does.Contain("エラー " + errors).And.Contain("警告 " + warnings));
                else if (warnings != 0) Assert.That(summary, Is.EqualTo("警告 " + warnings));
                else Assert.That(summary, Is.EqualTo("問題なし"));
                // 表示項目のクリックは元のCore診断へ対応付く（重複統合・表示変換後もindexがずれない）。
                foreach (var item in items)
                    Assert.That(session.Diagnostics[item.SourceIndex].Path, Is.EqualTo(item.Path));
            }
            finally
            {
                window.DiscardChanges();
                Object.DestroyImmediate(window);
                Undo.ClearAll();
            }
        }
    }
}
