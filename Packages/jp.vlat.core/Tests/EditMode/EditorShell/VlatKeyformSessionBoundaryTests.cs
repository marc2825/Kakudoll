using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VLat.Editor.Authoring;

namespace VLat.Tests.Edit
{
    /// <summary>実Unity Undo、保存点、Recovery、Window所有権の境界を検証します。</summary>
    public sealed class VlatKeyformSessionBoundaryTests
    {
        private string _directory;
        private VlatEditorDocumentController _session;
        private GameObject _avatar;
        private Mesh _mesh;
        private Material _material;
        private SkinnedMeshRenderer _a;
        private SkinnedMeshRenderer _b;
        private VlatEditorWindow _window;

        /// <summary>隔離セッションと自作合成Meshを用意します。</summary>
        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VLat1B-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _session = Session();
            _session.NewDocument(VlatCloseDecision.Discard);
            _avatar = new GameObject("OriginalAvatar");
            _mesh = new Mesh { name = "OriginalMesh" };
            _mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            _mesh.triangles = new[] { 0, 1, 2 };
            _mesh.AddBlendShapeFrame("Left", 100, new[] { Vector3.left, Vector3.zero, Vector3.zero }, null, null);
            _mesh.AddBlendShapeFrame("Up", 100, new[] { Vector3.up, Vector3.zero, Vector3.zero }, null, null);
            _material = new Material(Shader.Find("Standard")) { name = "OriginalMaterial" };
            _a = Renderer("A"); _b = Renderer("B");
            Bound(_session);
        }

        /// <summary>テスト所有のWindow・セッション・合成アセットだけを解放します。</summary>
        [TearDown]
        public void TearDown()
        {
            if (_window != null) { _window.DiscardChanges(); _window.Close(); }
            _session?.Dispose();
            UnityEngine.Object.DestroyImmediate(_avatar);
            UnityEngine.Object.DestroyImmediate(_mesh);
            UnityEngine.Object.DestroyImmediate(_material);
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        /// <summary>全編集操作がそれぞれ1 Undoで戻り、1 Redoで同じ内容を再現します。</summary>
        [TestCase("create-group")]
        [TestCase("delete-group")]
        [TestCase("add-key")]
        [TestCase("delete-key")]
        [TestCase("yaw")]
        [TestCase("support")]
        [TestCase("binding")]
        [TestCase("import")]
        [TestCase("clear")]
        public void EachOperation_IsOneUndoRedo(string operation)
        {
            if (operation == "create-group") _session.RemoveKeyformGroup(0);
            if (operation == "delete-key") _session.AddKeyform(0, 45);
            if (operation == "clear" || operation == "delete-group") Import();
            var before = _session.ContentFingerprint;
            switch (operation)
            {
                case "create-group": _session.AddKeyformGroup(); break;
                case "delete-group": _session.RemoveKeyformGroup(0); break;
                case "add-key": _session.AddKeyform(0, 45); break;
                case "delete-key": _session.RemoveKeyform(0, 1); break;
                case "yaw": _session.UpdateKeyformYaw(0, 0, 45); break;
                case "support": _session.UpdateKeyformGroup(0, "face", -90, 90); break;
                case "binding": _session.BindKeyformTarget(0, _avatar, _b); break;
                case "import": Import(); break;
                case "clear": _session.ImportKeyformPositions(0, 0, _avatar, _a, -1); break;
            }
            var after = _session.ContentFingerprint;
            Assert.That(after, Is.Not.EqualTo(before));
            UndoStep(); Assert.That(_session.ContentFingerprint, Is.EqualTo(before));
            RedoStep(); Assert.That(_session.ContentFingerprint, Is.EqualTo(after));
            Originals();
        }

        /// <summary>同frameの連続commandでも意図した操作単位が分離され、選択は履歴を作りません。</summary>
        [Test]
        public void ConsecutiveOperationsAndSelection_DoNotDuplicateOrConsumeHistory()
        {
            var zero = _session.ContentFingerprint;
            _session.AddKeyform(0, 45); var one = _session.ContentFingerprint;
            _session.ImportKeyformPositions(0, 1, _avatar, _a, 0); var two = _session.ContentFingerprint;
            _session.UpdateKeyformYaw(0, 1, 60); var three = _session.ContentFingerprint;
            _session.SelectDocument(); _session.SelectKeyformGroup(0); _session.SelectKeyform(0, 0);
            Assert.That(_session.ContentFingerprint, Is.EqualTo(three));
            UndoStep(); Assert.That(_session.ContentFingerprint, Is.EqualTo(two));
            UndoStep(); Assert.That(_session.ContentFingerprint, Is.EqualTo(one));
            UndoStep(); Assert.That(_session.ContentFingerprint, Is.EqualTo(zero));
            RedoStep(); Assert.That(_session.ContentFingerprint, Is.EqualTo(one));
            RedoStep(); Assert.That(_session.ContentFingerprint, Is.EqualTo(two));
            RedoStep(); Assert.That(_session.ContentFingerprint, Is.EqualTo(three));
        }

        /// <summary>Undo後の拒否操作と選択はRedoを残し、実編集だけがRedo履歴を分岐させます。</summary>
        [Test]
        public void RejectedOperationsPreserveRedo_AndNewEditBranchesIt()
        {
            _session.AddKeyform(0, 45);
            _session.UpdateKeyformYaw(0, 1, 60); var sixty = _session.ContentFingerprint;
            UndoStep(); var fortyFive = _session.ContentFingerprint;
            var revision = _session.Document.Revision;
            Assert.That(_session.UpdateKeyformYaw(0, 1, 0), Is.False);
            Assert.That(_session.ImportKeyformPositions(0, 1, _avatar, _b, 0), Is.False);
            Assert.That(_session.RemoveKeyform(0, 99), Is.False);
            _session.SelectKeyform(0, 1);
            Assert.That(_session.Document.Revision, Is.EqualTo(revision));
            RedoStep(); Assert.That(_session.ContentFingerprint, Is.EqualTo(sixty));
            UndoStep(); Assert.That(_session.ContentFingerprint, Is.EqualTo(fortyFive));
            _session.UpdateKeyformYaw(0, 1, 80); var branch = _session.ContentFingerprint;
            RedoStep(); Assert.That(_session.ContentFingerprint, Is.EqualTo(branch));
            UndoStep(); Assert.That(_session.ContentFingerprint, Is.EqualTo(fortyFive));
        }

        /// <summary>保存前後のUndo/Redoで保存先を巻き戻さず、保存済みJSON/binも書き換えません。</summary>
        [Test]
        public void SavedBoundary_TracksContentAndRetainsSaveAsPath()
        {
            var first = Save("first");
            Import(); var second = Save("second");
            var bytes = File.ReadAllBytes(second); var bin = File.ReadAllBytes(Path.Combine(_directory, "second.vlat.bin"));
            UndoStep(); Assert.That(_session.IsDirty, Is.True);
            Assert.That(_session.SourcePath, Is.EqualTo(second));
            Assert.That(_session.Document.SourcePath, Is.EqualTo(second));
            RedoStep(); Assert.That(_session.IsDirty, Is.False);
            for (var i = 0; i < 3; i++)
            {
                _session.UpdateKeyformYaw(0, 0, 45);
                Assert.That(_session.IsDirty, Is.True);
                UndoStep(); Assert.That(_session.IsDirty, Is.False);
                RedoStep(); Assert.That(_session.IsDirty, Is.True);
                UndoStep(); Assert.That(_session.IsDirty, Is.False);
            }
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(second));
            CollectionAssert.AreEqual(bin, File.ReadAllBytes(Path.Combine(_directory, "second.vlat.bin")));
            Assert.That(File.Exists(first), Is.True);
        }

        /// <summary>内容が保存済み形状へ戻ればrevisionが異なってもCleanで、同値編集は履歴を作りません。</summary>
        [Test]
        public void SemanticDirtyAndNoOp_FollowSavedContent()
        {
            Save("neutral"); var saved = _session.Document.Revision;
            _session.UpdateKeyformYaw(0, 0, 45);
            _session.UpdateKeyformYaw(0, 0, 0);
            Assert.That(_session.Document.Revision, Is.Not.EqualTo(saved));
            Assert.That(_session.IsDirty, Is.False);
            var revision = _session.Document.Revision;
            _session.UpdateKeyformYaw(0, 0, 0);
            _session.ImportKeyformPositions(0, 0, _avatar, _a, -1);
            Assert.That(_session.Document.Revision, Is.EqualTo(revision));
            UndoStep(); Assert.That(_session.Document.KeyformGroups[0].Keys[0].Yaw, Is.EqualTo(45));
        }

        /// <summary>実ファイルI/Oとvalidationの保存失敗はDirty・保存先・元データを保持します。</summary>
        [Test]
        public void SaveFailureAndReload_PreserveBoundaries()
        {
            var path = Save("source"); var bytes = File.ReadAllBytes(path);
            Import(); var content = _session.ContentFingerprint;
            var directoryPath = Path.Combine(_directory, "directory.vlat.json"); Directory.CreateDirectory(directoryPath);
            Assert.That(_session.SaveAs(directoryPath), Is.False);
            Assert.That(_session.SaveAs("\0"), Is.False);
            Assert.That(_session.SourcePath, Is.EqualTo(path));
            Assert.That(_session.IsDirty, Is.True);
            Assert.That(_session.ContentFingerprint, Is.EqualTo(content));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
            Assert.That(_session.Save(), Is.True);
            Assert.That(_session.Open(path, VlatCloseDecision.Discard), Is.True);
            Assert.That(_session.ContentFingerprint, Is.EqualTo(content));
            Assert.That(_session.IsDirty, Is.False);
        }

        /// <summary>保存元JSON/binの外部変更は復元・保存で上書きせず、別名保存で回復できます。</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Recovery_ExternalSourceChangeRequiresSaveAs(bool binary)
        {
            Import(); var path = Save("source");
            _session.UpdateKeyformYaw(0, 0, 45);
            var content = _session.ContentFingerprint;
            _session.PrepareForDomainReload();
            var changed = binary ? Path.Combine(_directory, "source.vlat.bin") : path;
            var changedBytes = File.ReadAllBytes(changed);
            if (binary) changedBytes[4] ^= 1; else changedBytes = System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(path) + "\n ");
            File.WriteAllBytes(changed, changedBytes);
            Assert.That(_session.TryRestore(), Is.True);
            Assert.That(_session.SourceConflict, Is.True);
            Assert.That(_session.IsDirty, Is.True);
            Assert.That(_session.Save(), Is.False);
            CollectionAssert.AreEqual(changedBytes, File.ReadAllBytes(changed));
            Assert.That(_session.SaveAs(Path.Combine(_directory, "recovered.vlat.json")), Is.True);
            Assert.That(_session.ContentFingerprint, Is.EqualTo(content));
            Assert.That(_session.SourceConflict, Is.False);
        }

        /// <summary>壊れたsnapshot・非対応版・中身のないsnapshotは隔離し、新規編集へ安全に戻れます。</summary>
        [TestCase("hash")]
        [TestCase("version")]
        [TestCase("empty")]
        public void Recovery_CorruptDataIsQuarantinedAndDiagnosed(string mode)
        {
            Import(); _session.PrepareForDomainReload();
            var directory = Path.Combine(_directory, "session");
            var manifest = Path.Combine(directory, "editor-session.json");
            if (mode == "version")
            {
                // 現行manifest版(v3)に依存せず、未知版を確実に書き込む。
                var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(manifest));
                Assert.That((int)json["version"], Is.EqualTo(3));
                json["version"] = 99;
                File.WriteAllText(manifest, json.ToString());
            }
            else File.WriteAllText(Directory.GetFiles(directory, "snapshot-*.json")[0], mode == "empty" ? "{}" : "{broken");
            Assert.That(_session.TryRestore(), Is.False);
            Assert.That(_session.LastMessage, Does.Contain("Recovery"));
            Assert.That(Directory.GetFiles(Path.Combine(directory, "Rejected"), "*.json", SearchOption.AllDirectories).Length, Is.GreaterThan(0));
            Assert.That(_session.NewDocument(VlatCloseDecision.Discard), Is.True);
            Bound(_session); Import(); Assert.That(_session.Document.KeyformGroups[0].Keys[0].Deltas.Count, Is.EqualTo(1));
            Originals();
        }

        /// <summary>v1の旧Recoveryを読めますが、保存基準が不明な旧snapshotで元ファイルを上書きしません。</summary>
        [Test]
        public void Recovery_ReadsLegacyManifestConservatively()
        {
            Save("legacy"); _session.PrepareForDomainReload();
            var manifest = Path.Combine(_directory, "session", "editor-session.json");
            var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(manifest));
            json["version"] = 1; json.Remove("savedContent"); json.Remove("sourceSignature"); json.Remove("snapshotHash");
            File.WriteAllText(manifest, json.ToString());
            Assert.That(_session.TryRestore(), Is.True);
            Assert.That(_session.SourceConflict, Is.True);
            Assert.That(_session.Save(), Is.False);
            Assert.That(_session.SaveAs(Path.Combine(_directory, "legacy-copy.vlat.json")), Is.True);
        }

        /// <summary>実ファイルlockでRecovery更新に失敗しても、直前のcheckpointを失いません。</summary>
        [Test]
        public void RecoveryWriteFailure_PreservesLastCheckpointAndRetries()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
                Assert.Ignore("WindowsのFileShare.Noneによる実ファイルlockを検証する項目です。");
            Save("checkpoint");
            var manifest = Path.Combine(_directory, "session", "editor-session.json");
            var checkpoint = File.ReadAllBytes(manifest);
            using (var locked = new FileStream(manifest, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.That(_session.UpdateKeyformYaw(0, 0, 45), Is.True);
            Assert.That(_session.LastMessage, Does.Contain("Recovery"));
            CollectionAssert.AreEqual(checkpoint, File.ReadAllBytes(manifest));
            var content = _session.ContentFingerprint;
            Assert.That(_session.IsDirty, Is.True);
            _session.SelectDocument();
            _session.PrepareForDomainReload();
            Assert.That(_session.TryRestore(), Is.True, _session.LastMessage);
            Assert.That(_session.ContentFingerprint, Is.EqualTo(content));
        }

        /// <summary>未binding draftも実EditorJsonのルート内にある編集内容で比較します。</summary>
        [Test]
        public void UnboundDraftFingerprint_TracksRealSerializedContent()
        {
            _session.RemoveKeyformGroup(0);
            _session.AddKeyformGroup();
            var before = _session.ContentFingerprint;
            _session.UpdateKeyformYaw(0, 0, 45);
            Assert.That(_session.ContentFingerprint, Is.Not.EqualTo(before));
            UndoStep(); Assert.That(_session.ContentFingerprint, Is.EqualTo(before));
        }

        /// <summary>対象変更後は古いRendererを拒否し、Undoで元のBindingへ戻せます。</summary>
        [Test]
        public void BindingSwitchAndMissingReferences_PreserveOriginalAssets()
        {
            Assert.That(_session.BindKeyformTarget(0, _avatar, _b), Is.True);
            Assert.That(_session.ImportKeyformPositions(0, 0, _avatar, _a, 0), Is.False);
            Assert.That(_session.ImportKeyformPositions(0, 0, _avatar, null, 0), Is.False);
            UndoStep();
            Assert.That(_session.ImportKeyformPositions(0, 0, _avatar, _a, 0), Is.True);
            Assert.That(_session.ImportKeyformPositions(0, 0, _avatar, _b, 0), Is.False);
            _session.ImportKeyformPositions(0, 0, _avatar, _a, -1);
            _session.RemoveKeyformGroup(0);
            Originals();
        }

        /// <summary>実Windowを閉じて再作成すると、Clean保存点と選択を復元します。</summary>
        [UnityTest]
        public IEnumerator Window_CloseAndReopen_RestoresCleanSession()
        {
            var dir = Path.Combine(_directory, "window");
            _window = VlatEditorWindow.CreateForRecoveryDirectory(dir); _window.Show(); yield return null;
            var controller = _window.DocumentController; Bound(controller);
            Assert.That(controller.SaveAs(Path.Combine(_directory, "window.vlat.json")), Is.True);
            controller.SelectKeyform(0, 0);
            var content = controller.ContentFingerprint; var identity = controller.Selection.Identity;
            _window.CreateGUI(); _window.Close(); yield return null;
            _window = VlatEditorWindow.CreateForRecoveryDirectory(dir); _window.Show(); yield return null;
            Assert.That(_window.DocumentController.ContentFingerprint, Is.EqualTo(content));
            Assert.That(_window.DocumentController.IsDirty, Is.False);
            Assert.That(_window.DocumentController.Selection.Identity, Is.EqualTo(identity));
        }

        /// <summary>実Windowの未確認破棄とUI再構築でも未保存内容を復元し、commandを重複適用しません。</summary>
        [UnityTest]
        public IEnumerator Window_UnconfirmedDestructionAndGuiRebuild_PreserveDraft()
        {
            var dir = Path.Combine(_directory, "window");
            _window = VlatEditorWindow.CreateForRecoveryDirectory(dir); _window.Show(); yield return null;
            Bound(_window.DocumentController);
            _window.DocumentController.ImportKeyformPositions(0, 0, _avatar, _a, 0);
            var content = _window.DocumentController.ContentFingerprint;
            UnityEngine.Object.DestroyImmediate(_window); yield return null;
            _window = VlatEditorWindow.CreateForRecoveryDirectory(dir); _window.Show(); yield return null;
            Assert.That(_window.DocumentController.ContentFingerprint, Is.EqualTo(content));
            Assert.That(_window.DocumentController.IsDirty, Is.True);
            _window.CreateGUI(); _window.CreateGUI();
            _window.DocumentController.SelectKeyformGroup(0); _window.CreateGUI();
            var button = _window.rootVisualElement.Q<Button>("add-keyform-button");
            using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = button; button.SendEvent(evt); }
            Assert.That(_window.DocumentController.Document.KeyformGroups[0].Keys.Count, Is.EqualTo(2));
            Undo.PerformUndo(); Assert.That(_window.DocumentController.Document.KeyformGroups[0].Keys.Count, Is.EqualTo(1));
        }

        private VlatEditorDocumentController Session() => new VlatEditorDocumentController(new VlatRecoveryStore(Path.Combine(_directory, "session")));
        private void Bound(VlatEditorDocumentController session)
        {
            Assert.That(session.AddKeyformGroup(), Is.True);
            Assert.That(session.BindKeyformTarget(0, _avatar, _a), Is.True);
        }
        private void Import() => Assert.That(_session.ImportKeyformPositions(0, 0, _avatar, _a, 0), Is.True);
        private string Save(string name)
        {
            var path = Path.Combine(_directory, name + ".vlat.json"); Assert.That(_session.SaveAs(path), Is.True); return path;
        }
        private void UndoStep() { Undo.PerformUndo(); _session.RefreshAfterUndoRedo(); }
        private void RedoStep() { Undo.PerformRedo(); _session.RefreshAfterUndoRedo(); }
        private SkinnedMeshRenderer Renderer(string name)
        {
            var go = new GameObject(name); go.transform.SetParent(_avatar.transform, false);
            var renderer = go.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = _mesh;
            renderer.sharedMaterial = _material; renderer.SetBlendShapeWeight(0, 31); return renderer;
        }
        private void Originals()
        {
            foreach (var renderer in new[] { _a, _b })
            {
                Assert.That(renderer.sharedMesh, Is.SameAs(_mesh)); Assert.That(renderer.sharedMaterial, Is.SameAs(_material));
                Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(31));
            }
            CollectionAssert.AreEqual(new[] { Vector3.zero, Vector3.right, Vector3.up }, _mesh.vertices);
            Assert.That(_mesh.blendShapeCount, Is.EqualTo(2));
        }
    }
}
