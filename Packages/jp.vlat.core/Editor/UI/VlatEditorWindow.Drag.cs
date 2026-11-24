using UnityEngine;
using UnityEngine.UIElements;

namespace VLat.Editor.Authoring
{
    internal sealed partial class VlatEditorWindow
    {
        // 「動かす」ツールの切り替えはTools.cs。ここではドラッグの中止（ボタン、Esc、フォーカスが外れたとき）だけを扱う。
        private void ConfigureKeyformDragUi()
        {
            RequireElement<Button>("cancel-keyform-drag-button").clicked += CancelWindowDrag;
            // CreateGUI再生成時にrootへのcallbackが重複しないよう先に解除する。
            rootVisualElement.UnregisterCallback<KeyDownEvent>(OnDragKeyDown, TrickleDown.TrickleDown);
            rootVisualElement.RegisterCallback<KeyDownEvent>(OnDragKeyDown, TrickleDown.TrickleDown);
        }

        private void OnDragKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape || _preview == null || !_preview.IsDragging) return;
            CancelWindowDrag();
            evt.StopImmediatePropagation();
        }

        private void OnLostFocus() { CancelWindowDrag(); EndSpaceHold(); }
        private void OnEditorSelectionChanged() { CancelWindowDrag(); }

        private void CancelWindowDrag()
        {
            if (_preview == null || !_preview.IsDragging) return;
            _preview.CancelKeyformDrag();
            foreach (var state in _previewPointerStates)
                if (state.Action == PreviewPointerAction.Deform)
                {
                    state.Dragging = false; state.Action = PreviewPointerAction.None;
                    var image = _previewImages == null ? null : _previewImages[0];
                    if (image != null && image.HasPointerCapture(state.PointerId)) image.ReleasePointer(state.PointerId);
                }
            RenderAndRefreshPreview();
        }

        private void RefreshKeyformDragUi()
        {
            if (_previewHelpLabel == null) return;
            RequireElement<Button>("cancel-keyform-drag-button").SetEnabled(_preview.IsDragging);
            if (_preview.KeyformDragEnabled)
                _previewHelpLabel.text = "左ドラッグ: 形を動かす　Alt+左: 回す　中: 視点をずらす　Esc: 中止";
            var editable = _preview.GeometrySelectionEnabled && !_preview.KeyformDragEnabled;
            _vertexToolDropdown.SetEnabled(editable); _vertexVisibilityDropdown.SetEnabled(editable);
            _vertexSoftRadius.SetEnabled(editable); _vertexFalloffDropdown.SetEnabled(editable);
            _vertexBrushRadius.SetEnabled(editable);
        }
    }
}
