using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VLat.Core.Evaluation;
using VLat.Core.Model;
using VLat.Core.Serialization;
using VLat.Core.Validation;
using VLat.Editor.Authoring;
using VLat.Runtime;

namespace VLat.Tests.Edit
{
    /// <summary>既存EditorへのKeyform接続、UI操作、非破壊性、UndoとRecoveryの回帰を検証します。</summary>
    public sealed class VlatKeyformEditorTests
    {
        private string _directory;
        private GameObject _avatar;
        private SkinnedMeshRenderer _renderer;
        private Mesh _mesh;
        private Material _material;
        private VlatEditorDocumentController _controller;
        private VlatEditorWindow _window;

        /// <summary>私有アセットを使わず、位置差分が既知のMeshと隔離セッションを作成します。</summary>
        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VlatKeyformEditor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _avatar = new GameObject("Avatar");
            var face = new GameObject("Face");
            face.transform.SetParent(_avatar.transform, false);
            _renderer = face.AddComponent<SkinnedMeshRenderer>();
            _mesh = new Mesh { name = "FaceMesh" };
            _mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            _mesh.triangles = new[] { 0, 1, 2 };
            _mesh.AddBlendShapeFrame("Left", 100, new[] { Vector3.left, Vector3.zero, Vector3.zero }, null, null);
            _mesh.AddBlendShapeFrame("Right", 100, new[] { Vector3.zero, Vector3.zero, Vector3.forward }, null, null);
            _renderer.sharedMesh = _mesh;
            _renderer.SetBlendShapeWeight(0, 37);
            _material = new Material(Shader.Find("Standard")) { name = "OriginalMaterial" };
            _renderer.sharedMaterial = _material;
            _controller = new VlatEditorDocumentController(new VlatRecoveryStore(Path.Combine(_directory, "recovery")));
            Assert.That(_controller.NewDocument(VlatCloseDecision.Discard), Is.True);
        }

        /// <summary>一時ウィンドウ、Undo、セッション、生成アセットを解放します。</summary>
        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                _window.DiscardChanges();
                _window.Close();
            }
            _controller?.Dispose();
            UnityEngine.Object.DestroyImmediate(_avatar);
            UnityEngine.Object.DestroyImmediate(_mesh);
            UnityEngine.Object.DestroyImmediate(_material);
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        /// <summary>新規groupは単独で作られ、旧Layerとの混在や2個目を拒否します。</summary>
        [Test]
        public void GroupCreation_UsesExistingDocumentAndRejectsMixedProfiles()
        {
            Assert.That(_controller.AddKeyformGroup(), Is.True);
            Assert.That(_controller.Document.Metadata.Version, Is.EqualTo("0.2.0"));
            Assert.That(_controller.Selection.Kind, Is.EqualTo(VlatEditorSelectionKind.KeyformGroup));
            var revision = _controller.Document.Revision;
            Assert.That(_controller.AddKeyformGroup(), Is.False);
            Assert.That(_controller.AddBand(), Is.EqualTo(-1));
            Assert.That(_controller.AddLayer(), Is.EqualTo(-1));
            Assert.That(_controller.Document.Revision, Is.EqualTo(revision));
            _controller.RemoveKeyformGroup(0);
            _controller.AddBand();
            revision = _controller.Document.Revision;
            Assert.That(_controller.AddKeyformGroup(), Is.False);
            Assert.That(_controller.Document.Revision, Is.EqualTo(revision));
            Assert.That(_controller.Document.Bands.Count, Is.EqualTo(1));
        }

        /// <summary>未bindingのdraftはRecovery可能ですが、保存先を変更せずエラーを表示します。</summary>
        [Test]
        public void UnboundDraft_BlocksSaveAndKeepsPreviousFile()
        {
            var path = Path.Combine(_directory, "previous.vlat.json");
            Assert.That(_controller.SaveAs(path), Is.True);
            var original = File.ReadAllBytes(path);
            Assert.That(_controller.AddKeyformGroup(), Is.True);
            Assert.That(_controller.Save(), Is.False);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            AssertDiagnostic("meshBinding");
            Assert.That(_controller.IsDirty, Is.True);
            _controller.PrepareForDomainReload();
            Assert.That(_controller.TryRestore(), Is.True);
            Assert.That(_controller.Document.KeyformGroups.Count, Is.EqualTo(1));
            Assert.That(_controller.Document.KeyformGroups[0].MeshBinding, Is.EqualTo(-1));
        }

        /// <summary>三つのキーを取込・Yaw編集・保存・再読込して差分と決定的binを保持します。</summary>
        [Test]
        public void Import_EditSaveReload_PreservesPositionsAndOriginalAssets()
        {
            CreateBoundGroup();
            Assert.That(_controller.AddKeyform(0, -45), Is.True);
            Assert.That(_controller.ImportKeyformPositions(0, 0, _avatar, _renderer, 0), Is.True);
            Assert.That(_controller.AddKeyform(0, 45), Is.True);
            Assert.That(_controller.ImportKeyformPositions(0, 2, _avatar, _renderer, 1), Is.True);
            var identity = _controller.Selection.Identity;
            Assert.That(_controller.UpdateKeyformYaw(0, 2, -60), Is.True);
            Assert.That(_controller.Selection.Identity, Is.EqualTo(identity));
            Assert.That(_controller.Selection.FindKeyformIndex(_controller.Document), Is.EqualTo(0));
            var path = Save();
            var bin = File.ReadAllBytes(Path.Combine(_directory, "output.vlat.bin"));
            Assert.That(_controller.Open(path, VlatCloseDecision.Discard), Is.True);
            var group = _controller.Document.KeyformGroups[0];
            Assert.That(group.Keys[0].Yaw, Is.EqualTo(-60));
            Assert.That(group.Keys[1].Yaw, Is.EqualTo(-45));
            Assert.That(group.Keys[2].Deltas, Is.Empty);
            Assert.That(group.Keys[0].Deltas[0].VertexIndex, Is.EqualTo(2));
            Assert.That(group.Keys[0].Deltas[0].PositionDelta, Is.EqualTo(Vector3.forward));
            Assert.That(_controller.Save(), Is.True);
            CollectionAssert.AreEqual(bin, File.ReadAllBytes(Path.Combine(_directory, "output.vlat.bin")));
            var loaded = VlatSerializer.Load(path);
            var evaluator = new KeyformReferenceEvaluator(loaded.Document, loaded.Buffers);
            var deltas = new Float3[3];
            evaluator.Evaluate(-60, deltas);
            Assert.That(deltas[2], Is.EqualTo(new Float3(0, 0, 1)));
            AssertOriginalAssets();
        }

        /// <summary>キー削除は隣の選択へ移り、最後のキー削除はrevisionを変えず拒否します。</summary>
        [Test]
        public void DeleteKey_RepairsSelectionAndProtectsLastKey()
        {
            CreateBoundGroup();
            _controller.AddKeyform(0, 45);
            Assert.That(_controller.RemoveKeyform(0, 1), Is.True);
            Assert.That(_controller.Selection.FindKeyformIndex(_controller.Document), Is.Zero);
            var revision = _controller.Document.Revision;
            Assert.That(_controller.RemoveKeyform(0, 0), Is.False);
            Assert.That(_controller.Document.Revision, Is.EqualTo(revision));
            AssertDiagnostic("keys");
        }

        /// <summary>重複、区間端、非有限Yawを拒否し、既存キーとrevisionを保ちます。</summary>
        [TestCase(0d)]
        [TestCase(-120d)]
        [TestCase(120d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void InvalidYaw_IsRejectedWithoutDocumentMutation(double yaw)
        {
            CreateBoundGroup();
            _controller.AddKeyform(0, 45);
            var revision = _controller.Document.Revision;
            Assert.That(_controller.UpdateKeyformYaw(0, 1, yaw), Is.False);
            Assert.That(_controller.Document.KeyformGroups[0].Keys[1].Yaw, Is.EqualTo(45));
            Assert.That(_controller.Document.Revision, Is.EqualTo(revision));
            AssertDiagnostic("keys");
            Assert.That(_controller.SelectDiagnostic(0), Is.True);
            Assert.That(_controller.Selection.FindKeyformIndex(_controller.Document), Is.EqualTo(1));
        }

        /// <summary>支持区間の不正変更と空IDを拒否し、範囲内の編集は許可します。</summary>
        [Test]
        public void GroupProperties_ValidateBeforeMutation()
        {
            CreateBoundGroup();
            var revision = _controller.Document.Revision;
            Assert.That(_controller.UpdateKeyformGroup(0, "", -90, 90), Is.False);
            Assert.That(_controller.UpdateKeyformGroup(0, "keys", 10, 120), Is.False);
            Assert.That(_controller.UpdateKeyformGroup(0, "keys", -180, 180), Is.False);
            Assert.That(_controller.Document.Revision, Is.EqualTo(revision));
            Assert.That(_controller.UpdateKeyformGroup(0, "face", -90, 90), Is.True);
            Assert.That(_controller.Document.KeyformGroups[0].Id, Is.EqualTo("face"));
        }

        /// <summary>同数MeshでもBindingの各要素や階層の一意性が変わると取込を拒否します。</summary>
        [TestCase("positions")]
        [TestCase("topology")]
        [TestCase("name")]
        [TestCase("path")]
        [TestCase("outside")]
        [TestCase("ambiguous")]
        public void Import_RejectsBindingMismatch(string mismatch)
        {
            CreateBoundGroup();
            var revision = _controller.Document.Revision;
            switch (mismatch)
            {
                case "positions": _mesh.vertices = new[] { Vector3.right, Vector3.zero, Vector3.up }; break;
                case "topology": _mesh.triangles = new[] { 0, 2, 1 }; break;
                case "name": _mesh.name = "Other"; break;
                case "path": _renderer.name = "Other"; break;
                case "outside": _renderer.transform.SetParent(null); break;
                case "ambiguous":
                    var twin = new GameObject("Face");
                    twin.transform.SetParent(_avatar.transform, false);
                    twin.AddComponent<SkinnedMeshRenderer>().sharedMesh = _mesh;
                    break;
            }
            try
            {
                Assert.That(_controller.ImportKeyformPositions(0, 0, _avatar, _renderer, 0), Is.False);
                Assert.That(_controller.Document.Revision, Is.EqualTo(revision));
                Assert.That(_controller.Document.KeyformGroups[0].Keys[0].Deltas, Is.Empty);
                AssertDiagnostic("delta");
            }
            finally { if (_renderer.transform.parent == null) _renderer.transform.SetParent(_avatar.transform, false); }
        }

        /// <summary>差分を保持したまま別基準へ再bindingする操作を拒否します。</summary>
        [Test]
        public void Rebind_WithExistingPositions_RequiresSameBase()
        {
            CreateBoundGroup();
            Assert.That(_controller.ImportKeyformPositions(0, 0, _avatar, _renderer, 0), Is.True);
            var revision = _controller.Document.Revision;
            _mesh.triangles = new[] { 0, 2, 1 };
            Assert.That(_controller.BindKeyformTarget(0, _avatar, _renderer), Is.False);
            Assert.That(_controller.Document.Revision, Is.EqualTo(revision));
            Assert.That(_controller.Document.KeyformGroups[0].Keys[0].Deltas.Count, Is.EqualTo(1));
        }

        /// <summary>multi-frameと100%以外の取込は既存の位置差分を上書きしません。</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Import_UnsupportedFrames_PreservesCurrentKey(bool multiFrame)
        {
            CreateBoundGroup();
            Assert.That(_controller.ImportKeyformPositions(0, 0, _avatar, _renderer, 0), Is.True);
            _mesh.ClearBlendShapes();
            _mesh.AddBlendShapeFrame("Unsupported", 50, new Vector3[3], null, null);
            if (multiFrame) _mesh.AddBlendShapeFrame("Unsupported", 100, new Vector3[3], null, null);
            var revision = _controller.Document.Revision;
            Assert.That(_controller.ImportKeyformPositions(0, 0, _avatar, _renderer, 0), Is.False);
            Assert.That(_controller.Document.Revision, Is.EqualTo(revision));
            Assert.That(_controller.Document.KeyformGroups[0].Keys[0].Deltas[0].PositionDelta, Is.EqualTo(Vector3.left));
            AssertDiagnostic("delta");
        }

        /// <summary>取込1操作のUndo/Redoと保存点への復帰が、差分・revisionとも一致します。</summary>
        [Test]
        public void ImportUndoRedo_RestoresPositionsAndSavedRevision()
        {
            CreateBoundGroup();
            Save();
            var saved = _controller.Document.Revision;
            _controller.ImportKeyformPositions(0, 0, _avatar, _renderer, 0);
            var imported = _controller.Document.Revision;
            Undo.FlushUndoRecordObjects();
            Assert.That(_controller.IsDirty, Is.True);
            Undo.PerformUndo();
            _controller.RefreshAfterUndoRedo();
            Assert.That(_controller.Document.KeyformGroups[0].Keys[0].Deltas, Is.Empty);
            Assert.That(_controller.Document.Revision, Is.EqualTo(saved));
            Assert.That(_controller.IsDirty, Is.False);
            Undo.PerformRedo();
            _controller.RefreshAfterUndoRedo();
            Assert.That(_controller.Document.Revision, Is.EqualTo(imported));
            Assert.That(_controller.Document.KeyformGroups[0].Keys[0].Deltas[0].PositionDelta, Is.EqualTo(Vector3.left));
            Assert.That(_controller.IsDirty, Is.True);
            AssertOriginalAssets();
        }

        /// <summary>group削除のUndo/Redoはmetadataと全キーを復元し、旧Layer編集も再開できます。</summary>
        [Test]
        public void DeleteGroupUndoRedo_RestoresWholeGroupAndProfile()
        {
            CreateBoundGroup();
            _controller.ImportKeyformPositions(0, 0, _avatar, _renderer, 0);
            var revision = _controller.Document.Revision;
            _controller.RemoveKeyformGroup(0);
            Undo.FlushUndoRecordObjects();
            Assert.That(_controller.Document.KeyformGroups, Is.Empty);
            Assert.That(_controller.Document.Metadata.Version, Is.EqualTo("0.1.0"));
            Undo.PerformUndo();
            _controller.RefreshAfterUndoRedo();
            Assert.That(_controller.Document.Revision, Is.EqualTo(revision));
            Assert.That(_controller.Document.KeyformGroups[0].Keys[0].Deltas.Count, Is.EqualTo(1));
            Assert.That(_controller.Document.Metadata.Version, Is.EqualTo("0.2.0"));
            Undo.PerformRedo();
            _controller.RefreshAfterUndoRedo();
            Assert.That(_controller.Document.KeyformGroups, Is.Empty);
            Assert.That(_controller.AddBand(), Is.Zero);
        }

        /// <summary>Yaw移動とキー削除はstable selectionを保ち、基本Undo/Redoが成立します。</summary>
        [Test]
        public void KeyEditAndDeleteUndoRedo_PreserveStableIdentity()
        {
            CreateBoundGroup();
            _controller.AddKeyform(0, 45);
            var identity = _controller.Selection.Identity;
            _controller.UpdateKeyformYaw(0, 1, -45);
            Undo.FlushUndoRecordObjects();
            Assert.That(_controller.Selection.FindKeyformIndex(_controller.Document), Is.Zero);
            Undo.PerformUndo();
            _controller.RefreshAfterUndoRedo();
            Assert.That(_controller.Selection.Identity, Is.EqualTo(identity));
            Assert.That(_controller.Selection.FindKeyformIndex(_controller.Document), Is.EqualTo(1));
            Undo.PerformRedo();
            _controller.RefreshAfterUndoRedo();
            Assert.That(_controller.Selection.FindKeyformIndex(_controller.Document), Is.Zero);
            _controller.RemoveKeyform(0, 0);
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            _controller.RefreshAfterUndoRedo();
            Assert.That(_controller.Document.KeyformGroups[0].Keys.Count, Is.EqualTo(2));
            Assert.That(_controller.Document.KeyformGroups[0].Keys[0].EditorIdentity, Is.EqualTo(identity));
        }

        /// <summary>Recoveryは選択identity・差分・dirty状態を復元し、保存済みJSON/binを変更しません。</summary>
        [Test]
        public void Recovery_RoundTripsKeyformSessionWithoutChangingSavedFiles()
        {
            CreateBoundGroup();
            var path = Save();
            var original = File.ReadAllBytes(path);
            _controller.AddKeyform(0, 45);
            _controller.ImportKeyformPositions(0, 1, _avatar, _renderer, 1);
            var identity = _controller.Selection.Identity;
            var revision = _controller.Document.Revision;
            _controller.PrepareForDomainReload();
            Assert.That(_controller.TryRestore(), Is.True);
            Assert.That(_controller.Selection.Identity, Is.EqualTo(identity));
            Assert.That(_controller.Selection.FindKeyformIndex(_controller.Document), Is.EqualTo(1));
            Assert.That(_controller.Document.Revision, Is.EqualTo(revision));
            Assert.That(_controller.Document.KeyformGroups[0].Keys[1].Deltas[0].PositionDelta, Is.EqualTo(Vector3.forward));
            Assert.That(_controller.IsDirty, Is.True);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            Assert.That(_controller.Save(), Is.True);
        }

        /// <summary>1e-12の位置差分を中立へ丸めず保存し、法線の未対応入力は拒否します。</summary>
        [Test]
        public void Pack_PreservesTinyPositionsAndRejectsNormals()
        {
            CreateBoundGroup();
            var key = _controller.Document.KeyformGroups[0].Keys[0];
            key.Deltas.Add(new VlatAuthoringVertexDelta { VertexIndex = 1, PositionDelta = new Vector3(1e-12f, 0, 0) });
            var packed = VlatAuthoringPersistence.Pack(_controller.Document);
            Assert.That(packed.Success, Is.True);
            Assert.That(packed.Document.KeyformGroups[0].Keys[0].Delta, Is.Not.Null);
            var evaluator = new KeyformReferenceEvaluator(packed.Document, packed.Buffers);
            var output = new Float3[3];
            evaluator.Evaluate(0, output);
            Assert.That(output[1].X, Is.EqualTo((double)1e-12f));
            key.Deltas[0].NormalDelta = new Vector3(1e-12f, 0, 0);
            Assert.That(VlatAuthoringPersistence.Pack(_controller.Document).Success, Is.False);
        }

        /// <summary>非対応ファイルや壊れたBindingをOpenしても編集中Documentを置換しません。</summary>
        [Test]
        public void Open_RejectsUnsupportedAndBrokenBindingWithoutLosingSession()
        {
            CreateBoundGroup();
            var path = Save();
            var original = File.ReadAllText(path);
            var document = _controller.Document;
            var invalid = Path.Combine(_directory, "invalid.vlat.json");
            File.WriteAllText(invalid, original.Replace("bind-mesh-local", "head-local"));
            Assert.That(_controller.Open(invalid, VlatCloseDecision.Discard), Is.False);
            Assert.That(_controller.Document, Is.SameAs(document));
            File.WriteAllText(invalid, original.Replace("\"meshBinding\": 0", "\"meshBinding\": 99"));
            Assert.That(_controller.Open(invalid, VlatCloseDecision.Discard), Is.False);
            Assert.That(_controller.Document, Is.SameAs(document));
        }

        /// <summary>UI Toolkitの実Windowを操作し、作成・対象選択・取込・保存・再読込・削除・Undoを検証します。</summary>
        [UnityTest]
        public IEnumerator WindowControls_CreateImportSaveReloadAndUndo()
        {
            _window = ScriptableObject.CreateInstance<VlatEditorWindow>();
            _window.DocumentController.NewDocument(VlatCloseDecision.Discard);
            _window.Show();
            yield return null;
            if (_window.rootVisualElement.Q<Button>("new-button") == null) _window.CreateGUI();
            yield return null;
            Assert.That(_window.rootVisualElement.panel, Is.Not.Null);
            var session = _window.DocumentController;
            Click("new-button");
            Click("add-keyform-group-button");
            Assert.That(session.Document.KeyformGroups.Count, Is.EqualTo(1));
            _window.rootVisualElement.Q<ObjectField>("keyform-avatar-root").value = _avatar;
            _window.rootVisualElement.Q<ObjectField>("keyform-renderer").value = _renderer;
            Click("bind-keyform-target-button");
            Assert.That(session.Document.KeyformGroups[0].MeshBinding, Is.Zero);
            _window.SelectKey(0);
            _window.rootVisualElement.Q<DoubleField>("keyform-yaw").value = -45;
            _window.rootVisualElement.Q<DropdownField>("keyform-shape").index = 1;
            Click("import-keyform-button");
            Assert.That(session.Document.KeyformGroups[0].Keys[0].Deltas[0].PositionDelta, Is.EqualTo(Vector3.left));
            Click("add-keyform-button");
            _window.rootVisualElement.Q<DoubleField>("keyform-yaw").value = 45;
            _window.rootVisualElement.Q<DropdownField>("keyform-shape").index = 2;
            Click("import-keyform-button");
            var group = session.Document.KeyformGroups[0];
            var revision = session.Document.Revision;
            _window.rootVisualElement.Q<DoubleField>("keyform-yaw").value = -45;
            Assert.That(session.Document.Revision, Is.EqualTo(revision));
            Assert.That(session.Diagnostics[0].Severity, Is.EqualTo(VlatDiagnosticSeverity.Error));
            _window.rootVisualElement.Q<DoubleField>("keyform-yaw").value = 60;
            var path = Path.Combine(_directory, "ui.vlat.json");
            _window.SaveDocumentPathProvider = () => path;
            _window.OpenDocumentPathProvider = () => path;
            Click("save-as-button");
            Assert.That(File.Exists(path), Is.True);
            Assert.That(session.IsDirty, Is.False);
            var before = VlatAuthoringPersistence.Pack(session.Document);
            Click("open-button");
            var after = VlatAuthoringPersistence.Pack(session.Document);
            Assert.That(after.Json, Is.EqualTo(before.Json));
            CollectionAssert.AreEqual(before.Buffers[0], after.Buffers[0]);
            _window.SelectKey(1);
            Click("remove-keyform-button");
            Undo.FlushUndoRecordObjects();
            Assert.That(session.Document.KeyformGroups[0].Keys.Count, Is.EqualTo(1));
            Click("undo-button");
            Assert.That(session.Document.KeyformGroups[0].Keys.Count, Is.EqualTo(2));
            Click("redo-button");
            Assert.That(session.Document.KeyformGroups[0].Keys.Count, Is.EqualTo(1));
            Click("remove-keyform-group-button");
            Assert.That(session.Document.KeyformGroups, Is.Empty);
            AssertOriginalAssets();
            TestContext.WriteLine("Real EditorWindow/UI Toolkit E2E: create/select/bind/yaw/import/reject duplicate/save/open/delete/Undo/Redo PASS; source Mesh/Material/weights unchanged.");
        }

        private void Click(string name)
        {
            var button = _window.rootVisualElement.Q<Button>(name);
            Assert.That(button, Is.Not.Null, name);
            Assert.That(button.enabledInHierarchy, Is.True, name);
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
        }

        private void CreateBoundGroup()
        {
            Assert.That(_controller.AddKeyformGroup(), Is.True);
            Assert.That(_controller.BindKeyformTarget(0, _avatar, _renderer), Is.True);
        }

        private string Save()
        {
            var path = Path.Combine(_directory, "output.vlat.json");
            Assert.That(_controller.SaveAs(path), Is.True);
            return path;
        }

        private void AssertDiagnostic(string path)
        {
            var found = false;
            foreach (var diagnostic in _controller.Diagnostics)
                if (diagnostic.Severity == VlatDiagnosticSeverity.Error && diagnostic.Path.Contains(path)) found = true;
            Assert.That(found, Is.True, path);
        }

        private void AssertOriginalAssets()
        {
            Assert.That(_renderer.sharedMesh, Is.SameAs(_mesh));
            Assert.That(_renderer.sharedMaterial, Is.SameAs(_material));
            Assert.That(_renderer.GetBlendShapeWeight(0), Is.EqualTo(37));
            Assert.That(_mesh.blendShapeCount, Is.EqualTo(2));
            CollectionAssert.AreEqual(new[] { Vector3.zero, Vector3.right, Vector3.up }, _mesh.vertices);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, _mesh.triangles);
            var delta = new Vector3[3];
            _mesh.GetBlendShapeFrameVertices(0, 0, delta, null, null);
            Assert.That(delta[0], Is.EqualTo(Vector3.left));
        }
    }
}
