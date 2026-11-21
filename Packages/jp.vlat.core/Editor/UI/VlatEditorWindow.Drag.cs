using UnityEngine;
using UnityEngine.UIElements;

namespace VLat.Editor.Authoring
{
    internal sealed partial class VlatEditorWindow
    {
        private void ConfigureKeyformDragUi()
        {
            RequireElement<Toggle>("keyform-drag-toggle").RegisterValueChangedCallback(evt =>
            {
                if (_refreshing) return;
                _preview.SetKeyformEditingSelection(_controller);
                _preview.SetKeyformDragEnabled(evt.newValue);
                RenderAndRefreshPreview();
            });
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

        private void OnLostFocus() { CancelWindowDrag(); }
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
            var toggle = rootVisualElement.Q<Toggle>("keyform-drag-toggle");
            if (toggle == null) return;
            toggle.SetValueWithoutNotify(_preview.KeyformDragEnabled);
            toggle.SetEnabled(_preview.GeometrySelectionEnabled && _controller.Selection.FindKeyformIndex(_controller.Document) >= 0);
            RequireElement<Button>("cancel-keyform-drag-button").SetEnabled(_preview.IsDragging);
            RequireElement<Label>("keyform-drag-status").text = _preview.DragStatus;
            if (_preview.KeyformDragEnabled)
                _previewHelpLabel.text = "Main: 選択キーを直接編集 / 左: 変形 / Alt+左: 回転 / 中: 移動 / Esc: 中止";
            var editable = _preview.GeometrySelectionEnabled && !_preview.KeyformDragEnabled;
            _vertexToolDropdown.SetEnabled(editable); _vertexVisibilityDropdown.SetEnabled(editable);
            _vertexSoftRadius.SetEnabled(editable); _vertexFalloffDropdown.SetEnabled(editable);
            _vertexBrushRadius.SetEnabled(editable);
        }
    }
}
