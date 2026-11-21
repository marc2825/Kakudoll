using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VLat.Editor.Authoring;

namespace VLat.Tests.Edit
{
    /// <summary>Phase 2Bのdocument session、recovery、CRUD、およびselectionを検証します。</summary>
    public sealed class VlatEditorDocumentControllerTests
    {
        private readonly List<VlatEditorDocumentController> _controllers =
            new List<VlatEditorDocumentController>();
        private readonly List<string> _directories = new List<string>();

        /// <summary>Undo履歴をテストごとに分離します。</summary>
        [SetUp]
        public void SetUp()
        {
            Undo.ClearAll();
        }

        /// <summary>temporary session、recovery、およびUndo履歴を破棄します。</summary>
        [TearDown]
        public void TearDown()
        {
            for (var index = 0; index < _controllers.Count; index++)
            {
                _controllers[index].Dispose();
            }

            _controllers.Clear();
            Undo.ClearAll();
            for (var index = 0; index < _directories.Count; index++)
            {
                if (Directory.Exists(_directories[index]))
                {
                    Directory.Delete(_directories[index], true);
                }
            }

            _directories.Clear();
        }

        /// <summary>Newがsource pathを持たないclean sessionを作ることを検証します。</summary>
        [Test]
        public void New_CreatesCleanSession()
        {
            var controller = CreateController();

            Assert.That(controller.NewDocument(VlatCloseDecision.Discard), Is.True);

            Assert.That(controller.Document, Is.Not.Null);
            Assert.That(controller.SourcePath, Is.Null);
            Assert.That(controller.IsDirty, Is.False);
            Assert.That(controller.Selection.Kind, Is.EqualTo(VlatEditorSelectionKind.Document));
        }

        /// <summary>Open成功時だけcurrent documentとsource pathが切り替わることを検証します。</summary>
        [Test]
        public void Open_SuccessSwitchesDocument()
        {
            var path = CreateSavedDocument("Open Avatar");
            var controller = NewSession();

            Assert.That(controller.Open(path, VlatCloseDecision.Discard), Is.True);

            Assert.That(controller.SourcePath, Is.EqualTo(Path.GetFullPath(path)));
            Assert.That(controller.Document.Metadata.AvatarName, Is.EqualTo("Open Avatar"));
            Assert.That(controller.IsDirty, Is.False);
        }

        /// <summary>Open失敗時に現在のdocument、編集値、source pathを破壊しないことを検証します。</summary>
        [Test]
        public void Open_FailurePreservesCurrentDocument()
        {
            var controller = NewSession();
            controller.SetMetadata("0.1.0", "Generator", "Current Avatar", "1.0");
            var current = controller.Document;
            var missing = Path.Combine(CreateDirectory(), "missing.vlat.json");

            Assert.That(controller.Open(missing, VlatCloseDecision.Discard), Is.False);

            Assert.That(controller.Document, Is.SameAs(current));
            Assert.That(controller.Document.Metadata.AvatarName, Is.EqualTo("Current Avatar"));
            Assert.That(controller.SourcePath, Is.Null);
            Assert.That(controller.IsDirty, Is.True);
        }

        /// <summary>Save成功でsaved revisionとcurrent revisionが一致してcleanになることを検証します。</summary>
        [Test]
        public void Save_SuccessClearsDirty()
        {
            var controller = NewSession();
            controller.AddBand();
            var path = Path.Combine(CreateDirectory(), "saved.vlat.json");

            Assert.That(controller.SaveAs(path), Is.True);

            Assert.That(controller.IsDirty, Is.False);
            Assert.That(controller.SavedRevision, Is.EqualTo(controller.Document.Revision));
            Assert.That(File.Exists(path), Is.True);

            controller.SetMetadata("0.1.0", "Saved Again", "Avatar", "1.0");
            Assert.That(controller.IsDirty, Is.True);
            Assert.That(controller.Save(), Is.True);
            Assert.That(controller.IsDirty, Is.False);
        }

        /// <summary>validationでSaveが失敗した場合にdirtyと元source pathを維持することを検証します。</summary>
        [Test]
        public void Save_FailurePreservesDirtyAndSourcePath()
        {
            var controller = NewSession();
            controller.AddBand();
            var validPath = Path.Combine(CreateDirectory(), "valid.vlat.json");
            Assert.That(controller.SaveAs(validPath), Is.True);
            controller.AddLayer();
            controller.AddModifier(0, VlatAuthoringModifierKind.VertexDelta);
            var failedPath = Path.Combine(CreateDirectory(), "invalid.vlat.json");

            Assert.That(controller.SaveAs(failedPath), Is.False);

            Assert.That(controller.IsDirty, Is.True);
            Assert.That(controller.SourcePath, Is.EqualTo(Path.GetFullPath(validPath)));
            Assert.That(File.Exists(failedPath), Is.False);
        }

        /// <summary>Save As成功後だけsource pathを切り替えることを検証します。</summary>
        [Test]
        public void SaveAs_SwitchesPathOnlyAfterSuccess()
        {
            var controller = NewSession();
            var firstPath = Path.Combine(CreateDirectory(), "first.vlat.json");
            Assert.That(controller.SaveAs(firstPath), Is.True);
            controller.AddBand();
            var impossiblePath = CreateDirectory();

            Assert.That(controller.SaveAs(impossiblePath), Is.False);
            Assert.That(controller.SourcePath, Is.EqualTo(Path.GetFullPath(firstPath)));
            Assert.That(controller.IsDirty, Is.True);

            var secondPath = Path.Combine(CreateDirectory(), "second.vlat.json");
            Assert.That(controller.SaveAs(secondPath), Is.True);
            Assert.That(controller.SourcePath, Is.EqualTo(Path.GetFullPath(secondPath)));
            Assert.That(controller.IsDirty, Is.False);
        }

        /// <summary>保存点へUndoするとclean、Redoするとdirty、保存点より前ならdirtyになることを検証します。</summary>
        [Test]
        public void DirtyTracking_FollowsSavedRevisionAcrossUndoRedo()
        {
            var controller = NewSession();
            controller.AddBand();
            Undo.FlushUndoRecordObjects();
            var path = Path.Combine(CreateDirectory(), "savepoint.vlat.json");
            Assert.That(controller.SaveAs(path), Is.True);
            controller.AddLayer();
            Undo.FlushUndoRecordObjects();
            Assert.That(controller.IsDirty, Is.True);

            Undo.PerformUndo();
            controller.RefreshAfterUndoRedo();
            Assert.That(controller.Document.Layers.Count, Is.Zero);
            Assert.That(controller.IsDirty, Is.False);

            Undo.PerformRedo();
            controller.RefreshAfterUndoRedo();
            Assert.That(controller.Document.Layers.Count, Is.EqualTo(1));
            Assert.That(controller.IsDirty, Is.True);

            Undo.PerformUndo();
            controller.RefreshAfterUndoRedo();
            Undo.PerformUndo();
            controller.RefreshAfterUndoRedo();
            Assert.That(controller.Document.Bands.Count, Is.Zero);
            Assert.That(controller.IsDirty, Is.True);
        }

        /// <summary>CloseのCancel、Discard、Save方針がdataとrecovery lifecycleを守ることを検証します。</summary>
        [Test]
        public void Close_RespectsCancelDiscardAndSavePolicies()
        {
            var cancelDirectory = CreateDirectory();
            var cancel = CreateController(cancelDirectory);
            cancel.NewDocument(VlatCloseDecision.Discard);
            cancel.AddBand();
            Assert.That(cancel.TryClose(VlatCloseDecision.Cancel), Is.False);
            Assert.That(cancel.Document, Is.Not.Null);
            Assert.That(File.Exists(Path.Combine(cancelDirectory, "editor-session.json")), Is.True);

            Assert.That(cancel.TryClose(VlatCloseDecision.Discard), Is.True);
            Assert.That(cancel.Document, Is.Null);
            Assert.That(File.Exists(Path.Combine(cancelDirectory, "editor-session.json")), Is.False);

            var saveDirectory = CreateDirectory();
            var save = CreateController(saveDirectory);
            save.NewDocument(VlatCloseDecision.Discard);
            save.AddBand();
            var path = Path.Combine(CreateDirectory(), "close-save.vlat.json");
            Assert.That(save.TryClose(VlatCloseDecision.Save, path), Is.True);
            Assert.That(save.Document, Is.Null);
            Assert.That(File.Exists(path), Is.True);
            Assert.That(File.Exists(Path.Combine(saveDirectory, "editor-session.json")), Is.False);
        }

        /// <summary>保存済みsourceと未保存変更をRecovery checkpointから復元することを検証します。実Domain Reloadは別試験です。</summary>
        [Test]
        public void Recovery_RestoresSavedDocumentWithUnsavedChanges()
        {
            var recoveryDirectory = CreateDirectory();
            var source = CreateController(recoveryDirectory);
            source.NewDocument(VlatCloseDecision.Discard);
            var bandIndex = source.AddBand();
            var path = Path.Combine(CreateDirectory(), "recovery.vlat.json");
            Assert.That(source.SaveAs(path), Is.True);
            var band = source.Document.Bands[bandIndex];
            source.UpdateBand(
                bandIndex,
                band.Id,
                "Unsaved Name",
                band.YawMin,
                band.YawMax,
                band.PitchMin,
                band.PitchMax,
                band.FeatherYaw,
                band.FeatherPitch,
                band.Hysteresis);
            source.PrepareForDomainReload();

            var restored = CreateController(recoveryDirectory);
            Assert.That(restored.TryRestore(), Is.True);

            Assert.That(restored.SourcePath, Is.EqualTo(Path.GetFullPath(path)));
            Assert.That(restored.Document.Bands[0].Name, Is.EqualTo("Unsaved Name"));
            Assert.That(restored.Selection.Kind, Is.EqualTo(VlatEditorSelectionKind.Band));
            Assert.That(restored.Selection.FindBandIndex(restored.Document), Is.EqualTo(0));
            Assert.That(restored.IsDirty, Is.True);
        }

        /// <summary>sourceを持たない完全な新規未保存documentも復元できることを検証します。</summary>
        [Test]
        public void Recovery_RestoresCompletelyNewUnsavedDocument()
        {
            var recoveryDirectory = CreateDirectory();
            var source = CreateController(recoveryDirectory);
            source.NewDocument(VlatCloseDecision.Discard);
            source.AddBand();
            source.AddLayer();
            source.AddModifier(0, VlatAuthoringModifierKind.Opacity);
            source.PrepareForDomainReload();

            var restored = CreateController(recoveryDirectory);
            Assert.That(restored.TryRestore(), Is.True);

            Assert.That(restored.SourcePath, Is.Null);
            Assert.That(restored.Document.Bands.Count, Is.EqualTo(1));
            Assert.That(restored.Document.Layers.Count, Is.EqualTo(1));
            Assert.That(
                restored.Document.Layers[0].Modifiers[0],
                Is.TypeOf<VlatAuthoringOpacityModifier>());
            Assert.That(restored.Selection.Kind, Is.EqualTo(VlatEditorSelectionKind.Modifier));
            Assert.That(restored.Selection.FindModifierIndex(restored.Document), Is.EqualTo(0));
            Assert.That(restored.IsDirty, Is.True);
        }

        /// <summary>破損recoveryは例外や不完全documentを公開せず、安全に除去されることを検証します。</summary>
        [Test]
        public void Recovery_CorruptionFailsSafely()
        {
            var recoveryDirectory = CreateDirectory();
            File.WriteAllText(Path.Combine(recoveryDirectory, "editor-session.json"), "{broken");
            var controller = CreateController(recoveryDirectory);

            Assert.That(controller.TryRestore(), Is.False);

            Assert.That(controller.Document, Is.Null);
            Assert.That(File.Exists(Path.Combine(recoveryDirectory, "editor-session.json")), Is.False);
        }

        /// <summary>manifestが正常でもsnapshot本体が破損していれば安全に復元を拒否することを検証します。</summary>
        [Test]
        public void Recovery_CorruptedSnapshotFailsSafely()
        {
            var recoveryDirectory = CreateDirectory();
            var source = CreateController(recoveryDirectory);
            source.NewDocument(VlatCloseDecision.Discard);
            source.AddBand();
            source.PrepareForDomainReload();
            var snapshots = Directory.GetFiles(recoveryDirectory, "snapshot-*.json");
            Assert.That(snapshots.Length, Is.EqualTo(1));
            File.WriteAllText(snapshots[0], "{broken");

            var restored = CreateController(recoveryDirectory);
            Assert.That(restored.TryRestore(), Is.False);

            Assert.That(restored.Document, Is.Null);
            Assert.That(File.Exists(Path.Combine(recoveryDirectory, "editor-session.json")), Is.False);
            Assert.That(Directory.GetFiles(recoveryDirectory, "snapshot-*.json"), Is.Empty);
        }

        /// <summary>recovery書き込みが保存済みsource JSON/binを変更しないことを検証します。</summary>
        [Test]
        public void Recovery_DoesNotModifySourceFiles()
        {
            var recoveryDirectory = CreateDirectory();
            var controller = CreateController(recoveryDirectory);
            controller.NewDocument(VlatCloseDecision.Discard);
            controller.AddBand();
            var path = Path.Combine(CreateDirectory(), "source.vlat.json");
            Assert.That(controller.SaveAs(path), Is.True);
            var jsonBefore = File.ReadAllBytes(path);
            controller.SetMetadata("0.1.0", "Changed", "Avatar", "1.0");

            controller.PrepareForDomainReload();

            CollectionAssert.AreEqual(jsonBefore, File.ReadAllBytes(path));
        }

        /// <summary>Band/Layer/Modifierのadd/removeをcommand単位でUndo/Redoできることを検証します。</summary>
        [Test]
        public void Crud_AddRemoveCollectionsSupportsUndoRedo()
        {
            var controller = NewSession();
            controller.AddBand();
            Undo.FlushUndoRecordObjects();
            Assert.That(controller.Document.Bands.Count, Is.EqualTo(1));
            Undo.PerformUndo();
            controller.RefreshAfterUndoRedo();
            Assert.That(controller.Document.Bands.Count, Is.Zero);
            Undo.PerformRedo();
            controller.RefreshAfterUndoRedo();
            Assert.That(controller.Document.Bands.Count, Is.EqualTo(1));

            controller.AddLayer();
            Undo.FlushUndoRecordObjects();
            Assert.That(controller.Document.Layers.Count, Is.EqualTo(1));
            controller.AddModifier(0, VlatAuthoringModifierKind.Visibility);
            Undo.FlushUndoRecordObjects();
            Assert.That(controller.Document.Layers[0].Modifiers.Count, Is.EqualTo(1));
            controller.RemoveModifier(0, 0);
            Undo.FlushUndoRecordObjects();
            Assert.That(controller.Document.Layers[0].Modifiers.Count, Is.Zero);
            Undo.PerformUndo();
            controller.RefreshAfterUndoRedo();
            Assert.That(controller.Document.Layers[0].Modifiers.Count, Is.EqualTo(1));

            controller.RemoveLayer(0);
            Undo.FlushUndoRecordObjects();
            Assert.That(controller.Document.Layers.Count, Is.Zero);
            Undo.PerformUndo();
            controller.RefreshAfterUndoRedo();
            Assert.That(controller.Document.Layers.Count, Is.EqualTo(1));

            controller.RemoveBand(0);
            Undo.FlushUndoRecordObjects();
            Assert.That(controller.Document.Bands.Count, Is.Zero);
            Undo.PerformUndo();
            controller.RefreshAfterUndoRedo();
            Assert.That(controller.Document.Bands.Count, Is.EqualTo(1));
        }

        /// <summary>全modifier型のschema propertyをcommand経由で編集できることを検証します。</summary>
        [Test]
        public void Crud_EditsAllModifierTypesThroughCommands()
        {
            var controller = NewSession();
            controller.AddBand();
            controller.AddLayer();
            controller.AddModifier(0, VlatAuthoringModifierKind.VertexDelta);
            controller.UpdateVertexDeltaModifier(0, 0, 3, true);
            controller.AddModifier(0, VlatAuthoringModifierKind.Visibility);
            controller.UpdateVisibilityModifier(0, 1, 4, 2, false);
            controller.AddModifier(0, VlatAuthoringModifierKind.Opacity);
            controller.UpdateOpacityModifier(0, 2, 5, 3, 0.25);
            controller.AddModifier(0, VlatAuthoringModifierKind.MaterialProperty);
            controller.UpdateMaterialPropertyModifier(
                0,
                3,
                6,
                4,
                "_Color",
                "vec4",
                false,
                0.0,
                new[] { 1.0, 0.5, 0.25, 1.0 },
                true);
            controller.AddModifier(0, VlatAuthoringModifierKind.RenderQueue);
            controller.UpdateRenderQueueModifier(0, 4, 7, 5, 12);

            var modifiers = controller.Document.Layers[0].Modifiers;
            Assert.That(((VlatAuthoringVertexDeltaModifier)modifiers[0]).MeshBinding, Is.EqualTo(3));
            Assert.That(((VlatAuthoringVisibilityModifier)modifiers[1]).SubMesh, Is.EqualTo(2));
            Assert.That(((VlatAuthoringOpacityModifier)modifiers[2]).Value, Is.EqualTo(0.25));
            var material = (VlatAuthoringMaterialPropertyModifier)modifiers[3];
            CollectionAssert.AreEqual(new[] { 1.0, 0.5, 0.25, 1.0 }, material.Components);
            Assert.That(material.Interpolate, Is.True);
            Assert.That(((VlatAuthoringRenderQueueModifier)modifiers[4]).Offset, Is.EqualTo(12));
        }

        /// <summary>property editとreorderが各1 Undo stepで復元されることを検証します。</summary>
        [Test]
        public void Crud_PropertyAndReorderUseCompletedUndoCommands()
        {
            var controller = NewSession();
            controller.AddBand();
            controller.AddBand();
            controller.SelectBand(0);
            var selectedIdentity = controller.Selection.Identity;
            var first = controller.Document.Bands[0];
            controller.UpdateBand(
                0,
                first.Id,
                "Renamed",
                first.YawMin,
                first.YawMax,
                first.PitchMin,
                first.PitchMax,
                first.FeatherYaw,
                first.FeatherPitch,
                first.Hysteresis);
            Undo.FlushUndoRecordObjects();
            Assert.That(controller.Document.Bands[0].Name, Is.EqualTo("Renamed"));
            Undo.PerformUndo();
            controller.RefreshAfterUndoRedo();
            Assert.That(controller.Document.Bands[0].Name, Is.EqualTo("New Band"));
            Assert.That(controller.Selection.Identity, Is.EqualTo(selectedIdentity));

            controller.MoveBand(0, 1);
            Undo.FlushUndoRecordObjects();
            Assert.That(controller.Selection.FindBandIndex(controller.Document), Is.EqualTo(1));
            Undo.PerformUndo();
            controller.RefreshAfterUndoRedo();
            Assert.That(controller.Selection.FindBandIndex(controller.Document), Is.EqualTo(0));
            Undo.PerformRedo();
            controller.RefreshAfterUndoRedo();
            Assert.That(controller.Selection.FindBandIndex(controller.Document), Is.EqualTo(1));
        }

        /// <summary>LayerとModifierのreorderもstable selectionを保ちUndoできることを検証します。</summary>
        [Test]
        public void Crud_LayerAndModifierReorderSupportsUndo()
        {
            var controller = NewSession();
            controller.AddBand();
            controller.AddLayer();
            controller.AddLayer();
            controller.SelectLayer(0);
            var layerIdentity = controller.Selection.Identity;

            controller.MoveLayer(0, 1);
            Undo.FlushUndoRecordObjects();
            Assert.That(controller.Selection.Identity, Is.EqualTo(layerIdentity));
            Assert.That(controller.Selection.FindLayerIndex(controller.Document), Is.EqualTo(1));
            Undo.PerformUndo();
            controller.RefreshAfterUndoRedo();
            Assert.That(controller.Selection.FindLayerIndex(controller.Document), Is.EqualTo(0));

            controller.AddModifier(0, VlatAuthoringModifierKind.Visibility);
            controller.AddModifier(0, VlatAuthoringModifierKind.Opacity);
            controller.SelectModifier(0, 0);
            var modifierIdentity = controller.Selection.Identity;
            controller.MoveModifier(0, 0, 1);
            Undo.FlushUndoRecordObjects();
            Assert.That(controller.Selection.Identity, Is.EqualTo(modifierIdentity));
            Assert.That(controller.Selection.FindModifierIndex(controller.Document), Is.EqualTo(1));
            Undo.PerformUndo();
            controller.RefreshAfterUndoRedo();
            Assert.That(controller.Selection.FindModifierIndex(controller.Document), Is.EqualTo(0));
        }

        /// <summary>reorderと無関係なadd/remove後もstable identityが同じ対象を選択することを検証します。</summary>
        [Test]
        public void Selection_RemainsStableAndFallsBackAfterDeletion()
        {
            var controller = NewSession();
            controller.AddBand();
            controller.AddBand();
            controller.SelectBand(0);
            var selectedIdentity = controller.Selection.Identity;
            controller.AddBand();
            controller.SelectBand(0);
            controller.MoveBand(0, 2);

            Assert.That(controller.Selection.Identity, Is.EqualTo(selectedIdentity));
            Assert.That(controller.Selection.FindBandIndex(controller.Document), Is.EqualTo(2));

            controller.RemoveBand(2);
            Assert.That(controller.Selection.Kind, Is.EqualTo(VlatEditorSelectionKind.Band));
            Assert.That(controller.Selection.FindBandIndex(controller.Document), Is.EqualTo(1));

            controller.AddLayer();
            controller.AddModifier(0, VlatAuthoringModifierKind.Visibility);
            controller.AddModifier(0, VlatAuthoringModifierKind.Opacity);
            controller.SelectModifier(0, 0);
            controller.RemoveModifier(0, 0);
            Assert.That(controller.Selection.Kind, Is.EqualTo(VlatEditorSelectionKind.Modifier));
            Assert.That(controller.Selection.FindModifierIndex(controller.Document), Is.EqualTo(0));
            controller.RemoveModifier(0, 0);
            Assert.That(controller.Selection.Kind, Is.EqualTo(VlatEditorSelectionKind.Layer));
        }

        /// <summary>Save diagnosticsのpathから該当modifierをselectionへ対応付けることを検証します。</summary>
        [Test]
        public void ValidationDiagnostic_SelectsRelatedModifier()
        {
            var controller = NewSession();
            controller.AddBand();
            controller.AddLayer();
            controller.AddModifier(0, VlatAuthoringModifierKind.VertexDelta);
            var path = Path.Combine(CreateDirectory(), "invalid.vlat.json");
            Assert.That(controller.SaveAs(path), Is.False);

            var diagnosticIndex = FindDiagnostic(controller, ".modifiers[0].deltas");
            Assert.That(diagnosticIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(controller.SelectDiagnostic(diagnosticIndex), Is.True);
            Assert.That(controller.Selection.Kind, Is.EqualTo(VlatEditorSelectionKind.Modifier));
            Assert.That(controller.Selection.FindModifierIndex(controller.Document), Is.EqualTo(0));
        }

        /// <summary>session編集をSave/Reloadしてformat上の値と順序を維持することを検証します。</summary>
        [Test]
        public void PersistenceIntegration_EditSaveReloadPreservesSemantics()
        {
            var inputPath = CreateSavedBoundDocument();
            var controller = NewSession();
            Assert.That(controller.Open(inputPath, VlatCloseDecision.Discard), Is.True);
            controller.SetMetadata("0.1.0", "Phase 2B", "Session Avatar", "1.0");
            controller.AddBand();
            controller.AddLayer();
            controller.AddModifier(0, VlatAuthoringModifierKind.Visibility);
            controller.UpdateVisibilityModifier(0, 0, 0, 2, false);
            var path = Path.Combine(CreateDirectory(), "integration.vlat.json");

            Assert.That(controller.SaveAs(path), Is.True);
            var reload = VlatAuthoringPersistence.Load(path);
            try
            {
                Assert.That(reload.Success, Is.True);
                Assert.That(reload.Document.Metadata.AvatarName, Is.EqualTo("Session Avatar"));
                Assert.That(reload.Document.Bands.Count, Is.EqualTo(1));
                Assert.That(reload.Document.Layers.Count, Is.EqualTo(1));
                var modifier = (VlatAuthoringVisibilityModifier)reload.Document.Layers[0].Modifiers[0];
                Assert.That(modifier.SubMesh, Is.EqualTo(2));
                Assert.That(modifier.VisibleInBand, Is.False);
            }
            finally
            {
                if (reload.Document != null)
                {
                    UnityEngine.Object.DestroyImmediate(reload.Document);
                }
            }
        }

        private VlatEditorDocumentController NewSession()
        {
            var controller = CreateController();
            Assert.That(controller.NewDocument(VlatCloseDecision.Discard), Is.True);
            return controller;
        }

        private VlatEditorDocumentController CreateController(string recoveryDirectory = null)
        {
            var directory = recoveryDirectory ?? CreateDirectory();
            var controller = new VlatEditorDocumentController(new VlatRecoveryStore(directory));
            _controllers.Add(controller);
            return controller;
        }

        private string CreateSavedDocument(string avatarName)
        {
            var document = VlatAuthoringPersistence.CreateNew(avatarName);
            var path = Path.Combine(CreateDirectory(), "input.vlat.json");
            try
            {
                var result = VlatAuthoringPersistence.Save(path, document);
                Assert.That(result.Success, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(document);
            }

            return path;
        }

        private string CreateSavedBoundDocument()
        {
            var document = VlatAuthoringPersistence.CreateNew("Bound Avatar");
            document.Metadata.MeshBindings.Add(new VlatAuthoringMeshBinding
            {
                HierarchyPath = "Face",
                MeshName = "Face",
                VertexCount = 8,
                PositionHash = "xxh64:0123456789abcdef",
            });
            var path = Path.Combine(CreateDirectory(), "bound-input.vlat.json");
            try
            {
                var result = VlatAuthoringPersistence.Save(path, document);
                Assert.That(result.Success, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(document);
            }

            return path;
        }

        private string CreateDirectory()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "VlatEditorDocumentControllerTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            _directories.Add(path);
            return path;
        }

        private static int FindDiagnostic(VlatEditorDocumentController controller, string path)
        {
            for (var index = 0; index < controller.Diagnostics.Count; index++)
            {
                if (controller.Diagnostics[index].Path.IndexOf(path, StringComparison.Ordinal) >= 0)
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
