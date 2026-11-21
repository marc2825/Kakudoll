using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VLat.Editor.Authoring;
using Object = UnityEngine.Object;

namespace VLat.Tests.Edit
{
    /// <summary>2026-11-19監査の性能修正とUI整理が、保存・Undo・Recoveryの意味を変えないことを検証します。</summary>
    public sealed class VlatAuditRegressionTests
    {
        /// <summary>編集用packは同じRevisionだけで再利用され、command・Undo・Redoでは新しい内容になります。</summary>
        [Test]
        public void EditingPack_ReusedOnlyForSameRevisionAcrossUndoRedo()
        {
            var document = VlatAuthoringPersistence.CreateNew("Pack Cache Avatar");
            try
            {
                Undo.ClearAll();
                var first = VlatAuthoringPersistence.PackForEditing(document);
                Assert.That(first.Success, Is.True);
                Assert.That(VlatAuthoringPersistence.PackForEditing(document), Is.SameAs(first));
                var firstFingerprint = VlatSessionFingerprint.Content(document, first);
                Assert.That(VlatSessionFingerprint.Content(document, first), Is.SameAs(firstFingerprint));

                VlatAuthoringCommands.SetGenerator(document, "Changed Generator");
                var changed = VlatAuthoringPersistence.PackForEditing(document);
                Assert.That(changed, Is.Not.SameAs(first));
                Assert.That(changed.Json, Does.Contain("Changed Generator"));

                Undo.PerformUndo();
                var undone = VlatAuthoringPersistence.PackForEditing(document);
                Assert.That(undone, Is.Not.SameAs(changed));
                Assert.That(undone.Json, Does.Not.Contain("Changed Generator"));
                Assert.That(undone.Json, Is.EqualTo(first.Json));
                Assert.That(VlatSessionFingerprint.Content(document, undone), Is.EqualTo(firstFingerprint));

                Undo.PerformRedo();
                Assert.That(VlatAuthoringPersistence.PackForEditing(document).Json, Does.Contain("Changed Generator"));
            }
            finally
            {
                Undo.ClearAll();
                Object.DestroyImmediate(document);
            }
        }

        /// <summary>検証は常に新しくpackし、その結果を同じRevisionの編集境界へ渡します。</summary>
        [Test]
        public void Validate_AlwaysRefreshesAndPublishesEditingPack()
        {
            var document = VlatAuthoringPersistence.CreateNew("Validate Avatar");
            try
            {
                var cached = VlatAuthoringPersistence.PackForEditing(document);
                VlatAuthoringPersistence.Validate(document, VlatAuthoringValidationMode.Editing, out var validated);
                Assert.That(validated, Is.Not.SameAs(cached));
                Assert.That(validated.Json, Is.EqualTo(cached.Json));
                Assert.That(VlatAuthoringPersistence.PackForEditing(document), Is.SameAs(validated));
                Assert.That(VlatAuthoringPersistence.PackForEditing(document, refresh: true), Is.Not.SameAs(validated));
            }
            finally { Object.DestroyImmediate(document); }
        }

        /// <summary>1 MiB単位の書込みでもsnapshotは従来と同じBOMなしUTF-8 bytesで、manifest hashと復元が一致します。</summary>
        [Test]
        public void RecoverySnapshot_ChunkedWriteKeepsBytesHashAndRestore()
        {
            var directory = Path.Combine(Path.GetTempPath(), "VLatAuditRecovery-" + Guid.NewGuid().ToString("N"));
            var document = VlatAuthoringPersistence.CreateNew("Recovery Chunk Avatar");
            VlatAuthoringDocument restored = null;
            try
            {
                var group = new VlatAuthoringKeyformGroup();
                var key = new VlatAuthoringKeyform { Yaw = 0 };
                // 1 MiBを複数回跨ぐ大きさにし、chunk境界の連結を検証する。
                // D-011のpack済み表現（16 byte/要素→base64）でも3 MiBを超える件数にする。
                for (var i = 0; i < 200000; i++)
                    key.Deltas.Add(new VlatAuthoringVertexDelta { VertexIndex = i, PositionDelta = new Vector3(i * 1e-4f, -i * 2e-4f, 0.125f) });
                group.Keys.Add(key);
                VlatAuthoringCommands.AddKeyformGroup(document, group);
                var store = new VlatRecoveryStore(directory);
                var selection = new VlatEditorSelection();
                store.Write(document, document.Revision, selection, "sha256:saved", null, false);

                var expected = new UTF8Encoding(false, true).GetBytes(EditorJsonUtility.ToJson(document, false));
                Assert.That(expected.Length, Is.GreaterThan(3 << 20));
                var snapshots = Directory.GetFiles(directory, "snapshot-*.json");
                Assert.That(snapshots.Length, Is.EqualTo(1));
                var written = File.ReadAllBytes(snapshots[0]);
                Assert.That(written, Is.EqualTo(expected));
                var manifest = File.ReadAllText(Path.Combine(directory, "editor-session.json"));
                Assert.That(manifest, Does.Contain(VlatSessionFingerprint.Hash(expected)));

                Assert.That(store.TryRead(out var state), Is.True, store.LastReadError);
                restored = state.Document;
                Assert.That(restored.KeyformGroups[0].Keys[0].Deltas.Count, Is.EqualTo(200000));
                Assert.That(restored.KeyformGroups[0].Keys[0].Deltas[199999].PositionDelta,
                    Is.EqualTo(key.Deltas[199999].PositionDelta));
                Assert.That(state.SavedContent, Is.EqualTo("sha256:saved"));
            }
            finally
            {
                if (restored != null) Object.DestroyImmediate(restored);
                Object.DestroyImmediate(document);
                Undo.ClearAll();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static int _recoveryWrites;
        private static void CountRecovery(VlatEditorPhase phase, long ticks)
        {
            if (phase == VlatEditorPhase.RecoveryWrite) _recoveryWrites++;
        }

        /// <summary>Openは同一状態のRecoveryを一度だけ書き、復元時の内容・保存基準は従来どおりです。</summary>
        [Test]
        public void Open_WritesRecoveryOnceAndRestoresSavedState()
        {
            var directory = Path.Combine(Path.GetTempPath(), "VLatAuditOpen-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var controller = new VlatEditorDocumentController(new VlatRecoveryStore(Path.Combine(directory, "recovery")));
            VlatEditorDocumentController reopened = null;
            try
            {
                Assert.That(controller.NewDocument(VlatCloseDecision.Discard), Is.True);
                controller.SetMetadata("0.1.0", "Open Once", "Open Avatar", "1.0");
                controller.AddBand(); // 既存のSave試験と同じく、保存可能な最小内容にする。
                var path = Path.Combine(directory, "open.vlat.json");
                Assert.That(controller.SaveAs(path), Is.True, controller.LastMessage);
                var fingerprint = controller.ContentFingerprint;

                _recoveryWrites = 0;
                VlatEditorProfiling.Observer = CountRecovery;
                try { Assert.That(controller.Open(path, VlatCloseDecision.Discard), Is.True, controller.LastMessage); }
                finally { VlatEditorProfiling.Observer = null; }
                Assert.That(_recoveryWrites, Is.EqualTo(1));
                Assert.That(controller.IsDirty, Is.False);
                Assert.That(controller.ContentFingerprint, Is.EqualTo(fingerprint));

                reopened = new VlatEditorDocumentController(new VlatRecoveryStore(Path.Combine(directory, "recovery")));
                Assert.That(reopened.TryRestore(), Is.True, reopened.LastMessage);
                Assert.That(reopened.ContentFingerprint, Is.EqualTo(fingerprint));
                Assert.That(reopened.IsDirty, Is.False);
                Assert.That(reopened.Document.Metadata.Generator, Is.EqualTo("Open Once"));
            }
            finally
            {
                VlatEditorProfiling.Observer = null;
                reopened?.Dispose();
                controller.Dispose();
                Undo.ClearAll();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        /// <summary>作業手順ガイド・状態バッジ・旧Layer折りたたみが文書状態に追従し、Documentを変更しません。</summary>
        [Test]
        public void Window_WorkflowGuideAndStatusTrackDocumentState()
        {
            var directory = Path.Combine(Path.GetTempPath(), "VLatAuditWindow-" + Guid.NewGuid().ToString("N"));
            var window = VlatEditorWindow.CreateForRecoveryDirectory(directory);
            try
            {
                window.CreateGUI();
                var root = window.rootVisualElement;
                var session = window.DocumentController;
                Assert.That(root.Q<Label>("workflow-step-vrm").ClassListContains("vlat-step-current"), Is.True);
                Assert.That(root.Q<Label>("workflow-hint").text, Does.Contain("VRM"));
                Assert.That(root.Q<Label>("dirty-label").ClassListContains("vlat-dirty-modified"), Is.False);
                Assert.That(root.Q<Foldout>("legacy-section").value, Is.False);
                Assert.That(root.Q<Foldout>("keyform-section").value, Is.True);
                var revision = session.Document.Revision;
                window.CreateGUI();
                Assert.That(session.Document.Revision, Is.EqualTo(revision), "表示更新はDocumentを変更しない");

                Assert.That(session.AddKeyformGroup(), Is.True);
                window.CreateGUI();
                Assert.That(root.Q<Label>("workflow-step-group").ClassListContains("vlat-step-done"), Is.True);
                Assert.That(root.Q<Label>("workflow-step-target").ClassListContains("vlat-step-done"), Is.False);
                Assert.That(root.Q<Label>("dirty-label").ClassListContains("vlat-dirty-modified"), Is.True);
                Assert.That(root.Q<Label>("validation-summary").text, Does.Contain("エラー"));

                Assert.That(session.NewDocument(VlatCloseDecision.Discard), Is.True);
                session.AddBand(); session.AddLayer();
                window.CreateGUI();
                Assert.That(root.Q<Foldout>("legacy-section").value, Is.True);
                Assert.That(root.Q<VisualElement>("workflow-steps").style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(root.Q<Label>("workflow-hint").text, Does.Contain("v0.1"));
            }
            finally
            {
                // 表示していないWindowは既存のAsset試験と同じくDestroyImmediateで破棄する。
                window.DiscardChanges();
                Object.DestroyImmediate(window);
                Undo.ClearAll();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
