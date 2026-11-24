using UnityEngine;
using UnityEngine.UIElements;

namespace VLat.Editor.Authoring
{
    /// <summary>編集ビューでの左ドラッグの意味。見る=回す、選ぶ=頂点を選ぶ、動かす=選んだ頂点で形を変える。</summary>
    internal enum VlatEditorTool
    {
        View,
        Select,
        Move,
    }

    // ツールの切り替えとキーボード操作。Previewの頂点選択(GeometrySelection)とドラッグ編集(KeyformDrag)の組み合わせを
    // 3つのツールとして見せる。Documentは変更しない。
    internal sealed partial class VlatEditorWindow
    {
        private const float BrushStep = 1.25f;
        private Toggle _toolView;
        private Toggle _toolSelect;
        private Toggle _toolMove;
        private bool _spaceHeld;
        private bool _applicationBeforeSpace;

        internal VlatEditorTool CurrentTool =>
            _preview == null ? VlatEditorTool.View :
            _preview.KeyformDragEnabled ? VlatEditorTool.Move :
            _preview.GeometrySelectionEnabled ? VlatEditorTool.Select : VlatEditorTool.View;

        private void ConfigureTools()
        {
            _toolView = RequireElement<Toggle>("tool-view-toggle");
            _toolSelect = RequireElement<Toggle>("geometry-selection-toggle");
            _toolMove = RequireElement<Toggle>("keyform-drag-toggle");
            AddShortcutBadge(_toolView, "1");
            AddShortcutBadge(_toolSelect, "2");
            AddShortcutBadge(_toolMove, "3");
            // 3つは1つだけが選ばれる。選んでいるツールをもう一度押しても外れない。
            _toolView.RegisterValueChangedCallback(evt =>
            {
                if (_refreshing) return;
                if (evt.newValue) SetTool(VlatEditorTool.View);
                else RefreshToolUi();
            });
            _toolSelect.RegisterValueChangedCallback(evt =>
            {
                if (_refreshing) return;
                SetTool(evt.newValue ? VlatEditorTool.Select : VlatEditorTool.View);
            });
            _toolMove.RegisterValueChangedCallback(evt =>
            {
                if (_refreshing) return;
                SetTool(evt.newValue ? VlatEditorTool.Move : VlatEditorTool.Select);
            });

            // CreateGUI再生成時にrootへのcallbackが重複しないよう先に解除する。
            rootVisualElement.UnregisterCallback<KeyDownEvent>(OnShortcutKeyDown);
            rootVisualElement.UnregisterCallback<KeyUpEvent>(OnShortcutKeyUp);
            rootVisualElement.RegisterCallback<KeyDownEvent>(OnShortcutKeyDown);
            rootVisualElement.RegisterCallback<KeyUpEvent>(OnShortcutKeyUp);
        }

        // キー入力はフォーカスのある要素に届く。何もフォーカスしていないとショートカットが効かないので、
        // ウィンドウが選ばれたら、ウィンドウの地の部分にフォーカスを置く。
        private void OnFocus()
        {
            var focused = rootVisualElement?.focusController?.focusedElement;
            if (focused == null && rootVisualElement != null && rootVisualElement.focusable) rootVisualElement.Focus();
        }

        private static void AddShortcutBadge(Toggle toggle, string key)
        {
            var badge = new Label(key) { pickingMode = PickingMode.Ignore };
            badge.AddToClassList("vlat-tool-key");
            var input = toggle.Q(className: Toggle.inputUssClassName);
            (input ?? toggle).Add(badge);
        }

        /// <summary>ツールを切り替えます。使えないツール（VRMがない、キーを選んでいない）は選ばず、表示だけ戻します。</summary>
        internal void SetTool(VlatEditorTool tool)
        {
            if (_preview == null || _controller?.Document == null) return;
            if (!CanUseTool(tool, out _))
            {
                RefreshToolUi();
                return;
            }

            EndSpaceHold();
            switch (tool)
            {
                case VlatEditorTool.View:
                    _preview.SetGeometrySelectionEnabled(false);
                    break;
                case VlatEditorTool.Select:
                    _preview.SetGeometrySelectionEnabled(true);
                    if (_preview.KeyformDragEnabled) _preview.SetKeyformDragEnabled(false);
                    break;
                case VlatEditorTool.Move:
                    _preview.SetGeometrySelectionEnabled(true);
                    _preview.SetKeyformEditingSelection(_controller);
                    _preview.SetKeyformDragEnabled(true);
                    break;
            }

            RenderAndRefreshPreview();
        }

        private bool CanUseTool(VlatEditorTool tool, out string reason)
        {
            reason = string.Empty;
            if (tool == VlatEditorTool.View) return true;
            if (IsLegacyDocument(_controller.Document))
            {
                reason = "旧形式のファイルでは使えません。";
                return false;
            }

            if (IsStartScreenVisible())
            {
                reason = "「編集をはじめる」を押すと使えます。";
                return false;
            }

            if (_preview == null || !_preview.HasAvatar)
            {
                reason = "VRM を開くと使えます。";
                return false;
            }

            if (tool == VlatEditorTool.Select) return true;
            var g = DisplayGroupIndex();
            if (!IsGroupBound(g))
            {
                reason = "直すパーツを決めると使えます。";
                return false;
            }

            if (_controller.Selection.FindKeyformIndex(_controller.Document) < 0)
            {
                reason = "キーを選ぶと使えます。レールのひし形を押すか、← → で選びます。";
                return false;
            }

            return true;
        }

        private void RefreshToolUi()
        {
            if (_toolView == null || _preview == null || _controller?.Document == null) return;
            var tool = CurrentTool;
            SetToolState(_toolView, tool == VlatEditorTool.View, VlatEditorTool.View, "回して見ます（1）。");
            SetToolState(_toolSelect, tool == VlatEditorTool.Select, VlatEditorTool.Select, "形を変えたい頂点を選びます（2）。");
            SetToolState(_toolMove, tool == VlatEditorTool.Move, VlatEditorTool.Move, "選んだ頂点をドラッグして、キーの形を変えます（3）。");
        }

        private void SetToolState(Toggle toggle, bool active, VlatEditorTool tool, string description)
        {
            toggle.SetValueWithoutNotify(active);
            toggle.EnableInClassList("vlat-tool-active", active);
            var usable = CanUseTool(tool, out var reason);
            toggle.SetEnabled(usable);
            toggle.tooltip = usable ? description : description + "\n" + reason;
        }

        private void OnShortcutKeyDown(KeyDownEvent evt)
        {
            if (_controller?.Document == null || _preview == null || evt.actionKey || evt.altKey) return;
            if (IsTextInput(evt.target as VisualElement)) return;
            var canvas = IsCanvasTarget(evt.target as VisualElement);
            switch (evt.keyCode)
            {
                case KeyCode.Alpha1:
                case KeyCode.Keypad1:
                    SetTool(VlatEditorTool.View);
                    break;
                case KeyCode.Alpha2:
                case KeyCode.Keypad2:
                    SetTool(VlatEditorTool.Select);
                    break;
                case KeyCode.Alpha3:
                case KeyCode.Keypad3:
                    SetTool(VlatEditorTool.Move);
                    break;
                case KeyCode.LeftBracket:
                    AdjustBrush(1f / BrushStep);
                    break;
                case KeyCode.RightBracket:
                    AdjustBrush(BrushStep);
                    break;
                case KeyCode.LeftArrow when canvas:
                    StepKey(-1);
                    break;
                case KeyCode.RightArrow when canvas:
                    StepKey(1);
                    break;
                case KeyCode.Space when canvas:
                    BeginSpaceHold();
                    break;
                default:
                    return;
            }

            evt.StopPropagation();
        }

        private void OnShortcutKeyUp(KeyUpEvent evt)
        {
            if (evt.keyCode != KeyCode.Space || !_spaceHeld) return;
            EndSpaceHold();
            evt.StopPropagation();
        }

        // Spaceを押しているあいだだけ補正を外して、補正前と見比べる。
        private void BeginSpaceHold()
        {
            if (_spaceHeld || CurrentTool != VlatEditorTool.View || !_preview.HasAvatar) return;
            _spaceHeld = true;
            _applicationBeforeSpace = _preview.ApplicationEnabled;
            if (_applicationBeforeSpace) _preview.SetApplicationEnabled(false);
            RenderAndRefreshPreview();
        }

        private void EndSpaceHold()
        {
            if (!_spaceHeld) return;
            _spaceHeld = false;
            if (_preview == null) return;
            if (_applicationBeforeSpace) _preview.SetApplicationEnabled(true);
            RenderAndRefreshPreview();
        }

        /// <summary>となりのキーを選び、編集ビューをその角度にします。キーを選んでいなければ、今の角度から見たとなりのキー。</summary>
        internal void StepKey(int direction)
        {
            var g = DisplayGroupIndex();
            if (g < 0) return;
            var keys = _controller.Document.KeyformGroups[g].Keys;
            if (keys.Count == 0) return;
            var current = _controller.Selection.FindKeyformIndex(_controller.Document);
            int next;
            if (current >= 0)
            {
                next = Mathf.Clamp(current + direction, 0, keys.Count - 1);
            }
            else
            {
                var yaw = _preview.GetViewState(VlatPreviewView.Main).Yaw;
                next = direction > 0 ? keys.Count - 1 : 0;
                for (var index = 0; index < keys.Count; index++)
                {
                    var i = direction > 0 ? index : keys.Count - 1 - index;
                    if (direction > 0 ? keys[i].Yaw > yaw : keys[i].Yaw < yaw)
                    {
                        next = i;
                        break;
                    }
                }
            }

            SelectKey(next, true);
        }

        private void AdjustBrush(float factor)
        {
            if (CurrentTool != VlatEditorTool.Select) return;
            _preview.SetVertexBrushRadius(Mathf.Clamp(_preview.VertexBrushRadius * factor, _vertexBrushRadius.lowValue, _vertexBrushRadius.highValue));
            RefreshVertexSelectionUi();
        }

        // 文字を打っている欄では、キー操作を横取りしない。
        private static bool IsTextInput(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
                if (current.ClassListContains("unity-base-text-field") || current.ClassListContains("unity-text-field")) return true;
            return false;
        }

        // ← → と Space は、ビュー・レール・ウィンドウの地の部分にフォーカスがあるときだけ使う（ボタンや欄の操作と取り合わない）。
        private bool IsCanvasTarget(VisualElement element)
        {
            if (element == null || element == rootVisualElement) return true;
            if (element is Image || element is VlatAngleRail) return true;
            for (var current = element; current != null; current = current.parent)
            {
                if (current.ClassListContains("unity-base-field") || current is Button || current is Foldout) return false;
                if (current == rootVisualElement) return true;
            }

            return true;
        }
    }
}
