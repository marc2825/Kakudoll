using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VLat.Core.Validation;

namespace VLat.Editor.Authoring
{
    internal enum VlatCloseDecision
    {
        Save,
        Discard,
        Cancel,
    }

    internal enum VlatAuthoringModifierKind
    {
        VertexDelta,
        Visibility,
        Opacity,
        MaterialProperty,
        RenderQueue,
    }

    internal sealed partial class VlatEditorDocumentController : IDisposable
    {
        private readonly VlatRecoveryStore _recoveryStore;
        private IReadOnlyList<VlatDiagnostic> _diagnostics = Array.Empty<VlatDiagnostic>();
        private long _savedRevision;
        private bool _disposed;
        private string _sourcePath;
        private string _savedContent;
        private string _currentContent;
        private string _sourceSignature;
        private bool _sourceConflict;
        private string _recoveryNotice;

        internal VlatEditorDocumentController(VlatRecoveryStore recoveryStore = null)
        {
            _recoveryStore = recoveryStore ?? new VlatRecoveryStore();
        }

        internal VlatAuthoringDocument Document { get; private set; }
        internal VlatEditorSelection Selection { get; } = new VlatEditorSelection();
        internal IReadOnlyList<VlatDiagnostic> Diagnostics => _diagnostics;
        internal string SourcePath => Document == null ? null : _sourcePath;
        internal long SavedRevision => _savedRevision;
        internal bool IsDirty => Document != null &&
            (_sourceConflict || (_savedContent != null ? _savedContent != _currentContent : Document.Revision != _savedRevision));
        internal string ContentFingerprint => _currentContent;
        internal bool SourceConflict => _sourceConflict;
        internal string LastMessage { get; private set; } = string.Empty;

        internal bool TryRestore()
        {
            RequireNotDisposed();
            VlatRecoveryState state;
            if (!_recoveryStore.TryRead(out state))
            {
                _recoveryNotice = _recoveryStore.LastReadError;
                if (!string.IsNullOrEmpty(_recoveryNotice)) LastMessage = _recoveryNotice;
                return false;
            }

            ReleaseDocument();
            Document = state.Document;
            _savedRevision = state.SavedRevision;
            _sourcePath = Document.SourcePath;
            _savedContent = state.SavedContent;
            _sourceSignature = state.SourceSignature;
            _sourceConflict = state.SourceConflict || (!string.IsNullOrEmpty(_sourcePath) &&
                (_sourceSignature == null || _sourceSignature != VlatSessionFingerprint.Source(_sourcePath)));
            Selection.Restore(
                state.SelectionKind,
                state.SelectionIdentity,
                state.SelectionParentIdentity);
            Selection.Reconcile(Document);
            ValidateEditing();
            LastMessage = "前回のVLat Editorセッションを復元しました。";
            PersistRecovery();
            return true;
        }

        internal bool NewDocument(
            VlatCloseDecision dirtyDecision = VlatCloseDecision.Cancel,
            string saveAsPath = null)
        {
            RequireNotDisposed();
            if (!ResolveDirtyDocument(dirtyDecision, saveAsPath))
            {
                return false;
            }

            var document = VlatAuthoringPersistence.CreateNew();
            ReplaceDocument(document, document.Revision);
            LastMessage = "新しいVLatドキュメントを作成しました。";
            return true;
        }

        internal bool Open(
            string path,
            VlatCloseDecision dirtyDecision = VlatCloseDecision.Cancel,
            string saveAsPath = null)
        {
            using var phase = VlatEditorProfiling.Measure(VlatEditorPhase.Open);
            RequireNotDisposed();
            var loadResult = VlatAuthoringPersistence.Load(path);
            if (!loadResult.Success)
            {
                _diagnostics = loadResult.Diagnostics;
                LastMessage = "VLatドキュメントを開けませんでした。現在の編集内容は保持されています。";
                return false;
            }

            if (!ResolveDirtyDocument(dirtyDecision, saveAsPath))
            {
                UnityEngine.Object.DestroyImmediate(loadResult.Document);
                return false;
            }

            // ReplaceDocumentが同じDocument/保存基準/選択/保存元指紋でRecoveryを書き込み済み。
            // diagnostics/LastMessageはRecovery対象外なので、同一snapshotを二重に書かない。
            ReplaceDocument(loadResult.Document, loadResult.Document.Revision);
            _diagnostics = loadResult.Diagnostics;
            LastMessage = "VLatドキュメントを開きました。";
            return true;
        }

        internal bool Save()
        {
            RequireDocument();
            if (string.IsNullOrWhiteSpace(SourcePath))
            {
                LastMessage = "保存先が未指定です。Save Asを使用してください。";
                return false;
            }

            return SaveTo(SourcePath);
        }

        internal bool SaveAs(string path)
        {
            RequireDocument();
            if (string.IsNullOrWhiteSpace(path))
            {
                LastMessage = "保存先が未指定です。";
                return false;
            }

            return SaveTo(path);
        }

        internal bool TryClose(VlatCloseDecision decision, string saveAsPath = null)
        {
            RequireNotDisposed();
            if (!ResolveDirtyDocument(decision, saveAsPath))
            {
                return false;
            }

            _recoveryStore.Clear();
            ReleaseDocument();
            _diagnostics = Array.Empty<VlatDiagnostic>();
            LastMessage = "VLat Editorセッションを閉じました。";
            return true;
        }

        internal void PrepareForDomainReload()
        {
            RequireNotDisposed();
            PersistRecovery();
            ReleaseDocument();
        }

        internal void RefreshAfterUndoRedo()
        {
            using var phase = VlatEditorProfiling.Measure(VlatEditorPhase.UndoRefresh);
            RequireDocument();
            Document.SetSourcePath(_sourcePath);
            Document.EnsureEditorIdentities();
            Selection.Reconcile(Document);
            ValidateEditing();
            LastMessage = IsDirty
                ? "Undo/Redoを適用しました。未保存の変更があります。"
                : "Undo/Redoで保存点に戻りました。";
            PersistRecovery();
        }

        internal void SelectDocument()
        {
            Selection.SelectDocument();
            PersistRecovery();
        }

        internal void SelectBand(int bandIndex)
        {
            RequireDocument();
            Selection.SelectBand(GetBand(bandIndex));
            PersistRecovery();
        }

        internal void SelectLayer(int layerIndex)
        {
            RequireDocument();
            Selection.SelectLayer(GetLayer(layerIndex));
            PersistRecovery();
        }

        internal void SelectModifier(int layerIndex, int modifierIndex)
        {
            RequireDocument();
            var layer = GetLayer(layerIndex);
            Selection.SelectModifier(layer, GetModifier(layer, modifierIndex));
            PersistRecovery();
        }

        internal bool SelectDiagnostic(int diagnosticIndex)
        {
            RequireDocument();
            if (diagnosticIndex < 0 || diagnosticIndex >= _diagnostics.Count)
            {
                return false;
            }

            var selected = Selection.SelectDiagnosticPath(Document, _diagnostics[diagnosticIndex].Path);
            if (selected)
            {
                PersistRecovery();
            }

            return selected;
        }

        internal void SetMetadata(string version, string generator, string avatarName, string vrmVersion)
        {
            RequireDocument();
            VlatAuthoringCommands.SetMetadata(Document, version, generator, avatarName, vrmVersion);
            CommitChange();
        }

        internal int AddBand()
        {
            RequireDocument();
            if (Document.KeyformGroups.Count != 0)
            {
                RejectKeyform("$.keyformGroups", "Keyformと旧Layer/AngleBandの混在は未対応です。");
                return -1;
            }
            var band = new VlatAuthoringBand
            {
                Id = CreateUniqueBandId(),
                Name = "New Band",
                YawMin = -180.0,
                YawMax = 180.0,
                PitchMin = -90.0,
                PitchMax = 90.0,
            };
            VlatAuthoringCommands.AddBand(Document, band);
            Selection.SelectBand(band);
            CommitChange();
            return Document.Bands.Count - 1;
        }

        internal void UpdateBand(
            int bandIndex,
            string id,
            string name,
            double yawMin,
            double yawMax,
            double pitchMin,
            double pitchMax,
            double featherYaw,
            double featherPitch,
            double hysteresis)
        {
            RequireDocument();
            VlatAuthoringCommands.SetBand(
                Document,
                bandIndex,
                id,
                name,
                yawMin,
                yawMax,
                pitchMin,
                pitchMax,
                featherYaw,
                featherPitch,
                hysteresis);
            CommitChange();
        }

        internal void RemoveBand(int bandIndex)
        {
            RequireDocument();
            GetBand(bandIndex);
            VlatAuthoringCommands.RemoveBand(Document, bandIndex);
            if (Document.Bands.Count == 0)
            {
                Selection.SelectDocument();
            }
            else
            {
                Selection.SelectBand(Document.Bands[Math.Min(bandIndex, Document.Bands.Count - 1)]);
            }

            CommitChange();
        }

        internal void MoveBand(int fromIndex, int toIndex)
        {
            RequireDocument();
            VlatAuthoringCommands.MoveBand(Document, fromIndex, toIndex);
            CommitChange();
        }

        internal int AddLayer()
        {
            RequireDocument();
            if (Document.KeyformGroups.Count != 0)
            {
                RejectKeyform("$.keyformGroups", "Keyformと旧Layer/AngleBandの混在は未対応です。");
                return -1;
            }
            var layer = new VlatAuthoringLayer
            {
                Id = CreateUniqueLayerId(),
                Name = "New Layer",
                Tag = "other",
                BandId = Document.Bands.Count == 0 ? string.Empty : Document.Bands[0].Id,
            };
            VlatAuthoringCommands.AddLayer(Document, layer);
            Selection.SelectLayer(layer);
            CommitChange();
            return Document.Layers.Count - 1;
        }

        internal void UpdateLayer(
            int layerIndex,
            string id,
            string name,
            string tag,
            bool enabled,
            int priority,
            string bandId)
        {
            RequireDocument();
            VlatAuthoringCommands.SetLayer(
                Document,
                layerIndex,
                id,
                name,
                tag,
                enabled,
                priority,
                bandId);
            CommitChange();
        }

        internal void RemoveLayer(int layerIndex)
        {
            RequireDocument();
            GetLayer(layerIndex);
            VlatAuthoringCommands.RemoveLayer(Document, layerIndex);
            if (Document.Layers.Count == 0)
            {
                Selection.SelectDocument();
            }
            else
            {
                Selection.SelectLayer(Document.Layers[Math.Min(layerIndex, Document.Layers.Count - 1)]);
            }

            CommitChange();
        }

        internal void MoveLayer(int fromIndex, int toIndex)
        {
            RequireDocument();
            VlatAuthoringCommands.MoveLayer(Document, fromIndex, toIndex);
            CommitChange();
        }

        internal int AddModifier(int layerIndex, VlatAuthoringModifierKind kind)
        {
            RequireDocument();
            var layer = GetLayer(layerIndex);
            var modifier = CreateModifier(kind);
            VlatAuthoringCommands.AddModifier(Document, layerIndex, modifier);
            Selection.SelectModifier(layer, modifier);
            CommitChange();
            return layer.Modifiers.Count - 1;
        }

        internal void RemoveModifier(int layerIndex, int modifierIndex)
        {
            RequireDocument();
            var layer = GetLayer(layerIndex);
            GetModifier(layer, modifierIndex);
            VlatAuthoringCommands.RemoveModifier(Document, layerIndex, modifierIndex);
            if (layer.Modifiers.Count == 0)
            {
                Selection.SelectLayer(layer);
            }
            else
            {
                Selection.SelectModifier(
                    layer,
                    layer.Modifiers[Math.Min(modifierIndex, layer.Modifiers.Count - 1)]);
            }

            CommitChange();
        }

        internal void MoveModifier(int layerIndex, int fromIndex, int toIndex)
        {
            RequireDocument();
            VlatAuthoringCommands.MoveModifier(Document, layerIndex, fromIndex, toIndex);
            CommitChange();
        }

        internal void UpdateVertexDeltaModifier(
            int layerIndex,
            int modifierIndex,
            int meshBinding,
            bool hasNormals)
        {
            VlatAuthoringCommands.SetVertexDeltaModifier(
                RequireDocument(),
                layerIndex,
                modifierIndex,
                meshBinding,
                hasNormals);
            CommitChange();
        }

        internal void UpdateVisibilityModifier(
            int layerIndex,
            int modifierIndex,
            int meshBinding,
            int subMesh,
            bool visibleInBand)
        {
            VlatAuthoringCommands.SetVisibilityModifier(
                RequireDocument(),
                layerIndex,
                modifierIndex,
                meshBinding,
                subMesh,
                visibleInBand);
            CommitChange();
        }

        internal void UpdateOpacityModifier(
            int layerIndex,
            int modifierIndex,
            int meshBinding,
            int materialSlot,
            double value)
        {
            VlatAuthoringCommands.SetOpacityModifier(
                RequireDocument(),
                layerIndex,
                modifierIndex,
                meshBinding,
                materialSlot,
                value);
            CommitChange();
        }

        internal void UpdateMaterialPropertyModifier(
            int layerIndex,
            int modifierIndex,
            int meshBinding,
            int materialSlot,
            string property,
            string valueType,
            bool isScalar,
            double scalar,
            IReadOnlyList<double> components,
            bool interpolate)
        {
            VlatAuthoringCommands.SetMaterialPropertyModifier(
                RequireDocument(),
                layerIndex,
                modifierIndex,
                meshBinding,
                materialSlot,
                property,
                valueType,
                isScalar,
                scalar,
                components,
                interpolate);
            CommitChange();
        }

        internal void UpdateRenderQueueModifier(
            int layerIndex,
            int modifierIndex,
            int meshBinding,
            int materialSlot,
            int offset)
        {
            VlatAuthoringCommands.SetRenderQueueModifier(
                RequireDocument(),
                layerIndex,
                modifierIndex,
                meshBinding,
                materialSlot,
                offset);
            CommitChange();
        }

        /// <summary>
        /// 通常終了するEditor sessionとrecovery dataを破棄します。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _recoveryStore.Clear();
            ReleaseDocument();
            _disposed = true;
        }

        private bool SaveTo(string path)
        {
            using var phase = VlatEditorProfiling.Measure(VlatEditorPhase.Save);
            string fullPath;
            try { fullPath = System.IO.Path.GetFullPath(path); }
            catch (Exception exception)
            {
                _diagnostics = new[] { VlatAuthoringResultUtility.Error("$", "保存先が不正です: " + exception.Message) };
                return false;
            }
            var sameSource = !string.IsNullOrEmpty(_sourcePath) && string.Equals(
                fullPath, _sourcePath,
                System.IO.Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            if (sameSource && (_sourceConflict || _sourceSignature != VlatSessionFingerprint.Source(_sourcePath)))
            {
                _sourceConflict = true;
                _diagnostics = new[] { VlatAuthoringResultUtility.Error("$",
                    "保存元が変更・削除されています。Openで確認するか、別名で保存してください。") };
                LastMessage = "競合があるため保存元へ上書きしていません。";
                PersistRecovery();
                return false;
            }
            Undo.FlushUndoRecordObjects();
            var result = VlatAuthoringPersistence.Save(path, Document);
            _diagnostics = result.Diagnostics;
            if (!result.Success)
            {
                LastMessage = "検証または書き込みに失敗したため保存していません。";
                PersistRecovery();
                return false;
            }

            _sourcePath = Document.SourcePath;
            _sourceSignature = VlatSessionFingerprint.Source(_sourcePath);
            _sourceConflict = false;
            _savedRevision = Document.Revision;
            ValidateEditing();
            _savedContent = _currentContent;
            Document.MarkSaved(_sourcePath);
            LastMessage = "VLatドキュメントを保存しました。";
            PersistRecovery();
            return true;
        }

        private bool ResolveDirtyDocument(VlatCloseDecision decision, string saveAsPath)
        {
            if (Document == null || !IsDirty)
            {
                return true;
            }

            switch (decision)
            {
                case VlatCloseDecision.Save:
                    return string.IsNullOrWhiteSpace(SourcePath)
                        ? SaveAs(saveAsPath)
                        : Save();
                case VlatCloseDecision.Discard:
                    return true;
                default:
                    LastMessage = "操作をキャンセルしました。";
                    return false;
            }
        }

        private void ReplaceDocument(VlatAuthoringDocument document, long savedRevision)
        {
            ReleaseDocument();
            Document = document;
            Document.hideFlags = HideFlags.HideAndDontSave;
            Document.EnsureEditorIdentities();
            _savedRevision = savedRevision;
            _sourcePath = Document.SourcePath;
            _sourceSignature = VlatSessionFingerprint.Source(_sourcePath);
            _sourceConflict = false;
            Selection.SelectDocument();
            ValidateEditing();
            _savedContent = _currentContent;
            Document.MarkSaved(_sourcePath);
            PersistRecovery();
        }

        private void ReleaseDocument()
        {
            if (Document == null)
            {
                return;
            }

            Undo.ClearUndo(Document);
            UnityEngine.Object.DestroyImmediate(Document);
            Document = null;
            Selection.SelectDocument();
        }

        private void CommitChange()
        {
            Document.EnsureEditorIdentities();
            Selection.Reconcile(Document);
            ValidateEditing();
            LastMessage = "編集内容を反映しました。";
            PersistRecovery();
        }

        private void ValidateEditing()
        {
            if (Document == null) { _diagnostics = Array.Empty<VlatDiagnostic>(); return; }
            IReadOnlyList<VlatDiagnostic> diagnostics;
            VlatAuthoringPackResult packed;
            using (VlatEditorProfiling.Measure(VlatEditorPhase.ValidateEditingPack))
                diagnostics = VlatAuthoringPersistence.Validate(Document, VlatAuthoringValidationMode.Editing, out packed);
            using (VlatEditorProfiling.Measure(VlatEditorPhase.ValidateEditingFingerprint))
                _currentContent = VlatSessionFingerprint.Content(Document, packed);
            Document.SetSourcePath(_sourcePath);
            if (IsDirty) Document.MarkDirty(); else Document.MarkSaved(_sourcePath);
            var all = new List<VlatDiagnostic>(diagnostics);
            if (_sourceConflict) all.Add(VlatAuthoringResultUtility.Error("$", "保存元とRecoveryが競合しています。別名保存またはOpenで確認してください。"));
            if (!string.IsNullOrEmpty(_recoveryNotice)) all.Add(new VlatDiagnostic(
                VlatDiagnosticSeverity.Warning, "$", 0, 0, _recoveryNotice));
            _diagnostics = all.AsReadOnly();
        }

        private void PersistRecovery()
        {
            if (Document == null)
            {
                return;
            }

            try
            {
                using (VlatEditorProfiling.Measure(VlatEditorPhase.RecoveryWrite))
                    _recoveryStore.Write(Document, _savedRevision, Selection, _savedContent, _sourceSignature, _sourceConflict);
            }
            catch (Exception exception)
            {
                LastMessage = "Recovery snapshotを書き込めませんでした: " + exception.Message;
            }
        }

        private string CreateUniqueBandId()
        {
            var suffix = Document.Bands.Count + 1;
            while (ContainsBandId("band-" + suffix))
            {
                suffix++;
            }

            return "band-" + suffix;
        }

        private bool ContainsBandId(string id)
        {
            for (var index = 0; index < Document.Bands.Count; index++)
            {
                if (string.Equals(Document.Bands[index].Id, id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private string CreateUniqueLayerId()
        {
            var suffix = Document.Layers.Count + 1;
            while (ContainsLayerId("layer-" + suffix))
            {
                suffix++;
            }

            return "layer-" + suffix;
        }

        private bool ContainsLayerId(string id)
        {
            for (var index = 0; index < Document.Layers.Count; index++)
            {
                if (string.Equals(Document.Layers[index].Id, id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static VlatAuthoringModifier CreateModifier(VlatAuthoringModifierKind kind)
        {
            switch (kind)
            {
                case VlatAuthoringModifierKind.VertexDelta:
                    return new VlatAuthoringVertexDeltaModifier();
                case VlatAuthoringModifierKind.Visibility:
                    return new VlatAuthoringVisibilityModifier { VisibleInBand = true };
                case VlatAuthoringModifierKind.Opacity:
                    return new VlatAuthoringOpacityModifier { Value = 1.0 };
                case VlatAuthoringModifierKind.MaterialProperty:
                    return new VlatAuthoringMaterialPropertyModifier();
                case VlatAuthoringModifierKind.RenderQueue:
                    return new VlatAuthoringRenderQueueModifier();
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private VlatAuthoringBand GetBand(int bandIndex)
        {
            if (bandIndex < 0 || bandIndex >= Document.Bands.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(bandIndex));
            }

            return Document.Bands[bandIndex];
        }

        private VlatAuthoringLayer GetLayer(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= Document.Layers.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(layerIndex));
            }

            return Document.Layers[layerIndex];
        }

        private static VlatAuthoringModifier GetModifier(
            VlatAuthoringLayer layer,
            int modifierIndex)
        {
            if (modifierIndex < 0 || modifierIndex >= layer.Modifiers.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(modifierIndex));
            }

            return layer.Modifiers[modifierIndex];
        }

        private VlatAuthoringDocument RequireDocument()
        {
            RequireNotDisposed();
            if (Document == null)
            {
                throw new InvalidOperationException("VLatドキュメントがありません。");
            }

            return Document;
        }

        private void RequireNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(VlatEditorDocumentController));
            }
        }
    }
}
