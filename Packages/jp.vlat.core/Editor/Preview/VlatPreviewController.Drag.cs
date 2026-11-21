using System;
using UnityEngine;
using UnityEditor;

namespace VLat.Editor.Authoring
{
    internal sealed partial class VlatPreviewController
    {
        private VlatKeyformDragGesture _drag;
        private VlatEditorDocumentController _dragSession;
        private int _editingKey = -1;
        private int _editorActiveSelection;
        private int[] _editorSelection;
        private string _editingKeyIdentity;
        internal bool KeyformDragEnabled { get; private set; }
        internal bool IsDragging => _drag != null;
        internal string DragStatus { get; private set; } = string.Empty;
        internal VlatVertexSelectionSession VertexSelection => _vertexSelection;
        internal Mesh DragMesh => _drag?.TemporaryMesh;

        internal void SetKeyformEditingSelection(VlatEditorDocumentController session)
        {
            var key = session.Selection.FindKeyformIndex(session.Document);
            var identity = key < 0 ? null : session.Selection.Identity;
            if (_editingKeyIdentity != identity || _dragSession != session)
            {
                CancelKeyformDrag("編集キーが切り替わりました。");
                _editingKey = key; _editingKeyIdentity = identity;
                _renderDirty = true;
            }
            _editingKey = key;
            if (key < 0) KeyformDragEnabled = false;
            _dragSession = session;
        }

        internal void SetKeyformDragEnabled(bool enabled)
        {
            CancelKeyformDrag();
            KeyformDragEnabled = enabled && _geometrySelectionEnabled;
            DragStatus = KeyformDragEnabled
                ? "Main: 選択キーの確定形状 / Sub A・B: 確定済み補間。左ドラッグで変形。Escで中止。"
                : string.Empty;
            _renderDirty = true;
        }

        internal bool BeginKeyformDrag(Vector2 position)
        {
            CancelKeyformDrag();
            try
            {
                if (!KeyformDragEnabled || !_geometrySelectionEnabled || _dragSession == null ||
                    _document != _dragSession.Document || _editingKey < 0 || _selectedKeyformGroup < 0 ||
                    _keyformPreview == null || !_keyformPreview.IsAvailable || _head == null)
                    throw new InvalidOperationException("有効なKeyformを選択し、Geometry Selectionで頂点を選んでください。");
                var index = _vertexSelection.TargetIndex;
                if (index < 0 || index >= _vertexSelection.Targets.Count)
                    throw new InvalidOperationException("対象Rendererがありません。");
                var target = _vertexSelection.Targets[index];
                var group = _document.KeyformGroups[_selectedKeyformGroup];
                if (!(target.Renderer is SkinnedMeshRenderer skinned) || target.BindingIndex != group.MeshBinding)
                    throw new InvalidOperationException("選択RendererがKeyformのMeshBindingと異なります。単一SkinnedMeshRendererのみ対応します。");
                _editorActiveSelection = Selection.activeInstanceID;
                _editorSelection = Selection.instanceIDs;
                _drag = new VlatKeyformDragGesture(_document, _selectedKeyformGroup, _editingKey,
                    _avatar.transform, _head, skinned, target.SourceMesh, _cameras[0], _vertexSelection, position);
                DragStatus = "ドラッグ中 — Main: 未確定 / Sub A・B: 開始前の確定済み表示（MouseUpで更新）";
                _renderDirty = true;
                return true;
            }
            catch (Exception exception) { _evaluationDirty = true; CancelKeyformDrag(exception.Message); return false; }
        }

        internal bool UpdateKeyformDrag(Vector2 position)
        {
            if (_drag == null) return false;
            try
            {
                RequireDragSelection();
                _drag.Update(position);
                _renderDirty = true;
                return true;
            }
            catch (Exception exception) { _evaluationDirty = true; CancelKeyformDrag(exception.Message); return false; }
        }

        internal bool CommitKeyformDrag(Vector2 position)
        {
            if (!UpdateKeyformDrag(position)) return false;
            try
            {
                var selected = Selection.instanceIDs;
                if (selected.Length != _editorSelection.Length) throw new InvalidOperationException("Editor Selectionが変わりました。");
                for (var i = 0; i < selected.Length; i++)
                    if (selected[i] != _editorSelection[i]) throw new InvalidOperationException("Editor Selectionが変わりました。");
                var positions = _drag.CreateCommit();
                var success = _dragSession.CommitKeyformDrag(_drag.Document, _drag.Revision,
                    _drag.GroupIndex, _drag.KeyIndex, _drag.GroupIdentity, _drag.KeyIdentity, positions);
                CancelKeyformDrag(success ? "ドラッグを確定しました。3視点は確定済み補間へ更新します。" : _dragSession.LastMessage);
                SetDocument(_dragSession.Document);
                return success;
            }
            catch (Exception exception) { _evaluationDirty = true; CancelKeyformDrag(exception.Message); return false; }
        }

        internal void CancelKeyformDrag(string reason = null)
        {
            var active = _drag != null;
            _drag?.Dispose(); _drag = null;
            if (reason != null) DragStatus = reason;
            else if (active) DragStatus = "ドラッグを中止しました。確定済み内容は変更していません。";
            if (active) _renderDirty = true;
        }

        private void RequireDragSelection()
        {
            if (Selection.activeInstanceID != _editorActiveSelection || _dragSession == null || _dragSession.Document != _drag.Document ||
                _dragSession.Selection.Identity != _drag.KeyIdentity ||
                _dragSession.Selection.ParentIdentity != _drag.GroupIdentity)
                throw new InvalidOperationException("編集対象が切り替わったためドラッグを中止しました。");
        }

        private bool RenderDrag()
        {
            try
            {
                RequireDragSelection();
                using var lighting = new VlatPreviewLightingScope(_previewScene);
                _drag.Render(_cameras[0]);
                _renderDirty = false;
                return true;
            }
            catch (Exception exception)
            {
                CancelKeyformDrag(exception.Message);
                // 通常経路で確定状態を描き直す。失敗時は既存のClearTargets境界で安全に消去する。
                return RenderIfNeeded();
            }
        }
    }
}
