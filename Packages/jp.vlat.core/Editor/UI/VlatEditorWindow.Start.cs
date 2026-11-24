using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace VLat.Editor.Authoring
{
    // 「はじめる画面」。グループのない新しいファイルで、アバター・直すパーツ・角度の3つを選ぶだけで編集を始める。
    internal sealed partial class VlatEditorWindow
    {
        private static readonly int[] StartAngleChoices = { -90, -45, 0, 45, 90 };
        private readonly List<Button> _startPartButtons = new List<Button>();
        private readonly List<int> _startPartTargets = new List<int>();
        private readonly List<Toggle> _startAngleToggles = new List<Toggle>();
        [SerializeField] private List<int> _startAngles = new List<int> { 45, 90 };
        private VisualElement _startScreen;
        private VisualElement _editorStage;
        private VisualElement _startParts;
        private VisualElement _startStepAvatar;
        private VisualElement _startStepPart;
        private VisualElement _startStepAngles;
        private Label _startAvatarLabel;
        private Label _startPartNote;
        private Label _startMessage;
        private Button _startOpenVrmButton;
        private Button _startEditingButton;
        private Object _startPartsAvatar;
        private int _startPartsCount = -1;
        private int _startPartTarget = -1;
        private string _startError = string.Empty;

        private void ConfigureStartScreen()
        {
            _startScreen = RequireElement<VisualElement>("start-screen");
            _editorStage = RequireElement<VisualElement>("editor-stage");
            _startParts = RequireElement<VisualElement>("start-parts");
            _startStepAvatar = RequireElement<VisualElement>("start-step-avatar");
            _startStepPart = RequireElement<VisualElement>("start-step-part");
            _startStepAngles = RequireElement<VisualElement>("start-step-angles");
            _startAvatarLabel = RequireElement<Label>("start-avatar-label");
            _startPartNote = RequireElement<Label>("start-part-note");
            _startMessage = RequireElement<Label>("start-message");
            _startOpenVrmButton = RequireElement<Button>("start-open-vrm-button");
            _startEditingButton = RequireElement<Button>("start-editing-button");
            _startOpenVrmButton.clicked += SelectVrm;
            _startEditingButton.clicked += StartEditing;
            RequireElement<Button>("start-open-file-button").clicked += OpenDocument;
            RequireElement<Button>("add-keyform-group-button").clicked += StartWithSceneAvatar;
            RequireElement<Button>("start-legacy-button").clicked += () =>
            {
                _controller.AddBand();
                RefreshAll();
            };

            var angles = RequireElement<VisualElement>("start-angles");
            angles.Clear();
            _startAngleToggles.Clear();
            foreach (var angle in StartAngleChoices)
            {
                var value = angle;
                var toggle = new Toggle
                {
                    name = "start-angle-" + (angle < 0 ? "n" + (-angle) : angle.ToString(CultureInfo.InvariantCulture)),
                    text = FormatYaw(angle) + "\n" + DescribeAngle(angle),
                    value = angle == 0 || _startAngles.Contains(angle),
                    tooltip = angle == 0 ? "正面（0°）は、形を変えない基準として必ず作ります。" : FormatYaw(angle) + " にキーを作ります。",
                };
                toggle.AddToClassList("vlat-choice-toggle");
                toggle.SetEnabled(angle != 0);
                toggle.RegisterValueChangedCallback(evt =>
                {
                    _startAngles.Remove(value);
                    if (evt.newValue) _startAngles.Add(value);
                    RefreshStartScreen();
                });
                angles.Add(toggle);
                _startAngleToggles.Add(toggle);
            }

            _startPartsAvatar = null;
            _startPartsCount = -1;
        }

        private static string DescribeAngle(int angle)
        {
            switch (angle)
            {
                case 0: return "正面";
                case 45: return "ななめ";
                case 90: return "横";
                case -45: return "反対のななめ";
                case -90: return "反対の横";
                default: return string.Empty;
            }
        }

        private bool IsStartScreenVisible()
        {
            var document = _controller?.Document;
            return document != null && !IsLegacyDocument(document) && document.KeyformGroups.Count == 0;
        }

        private void RefreshStartScreen()
        {
            if (_startScreen == null || _preview == null) return;
            var visible = IsStartScreenVisible();
            _startScreen.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _editorStage.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
            RequireElement<VisualElement>("side-pane").style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
            RequireElement<VisualElement>("tool-strip").style.display =
                visible || IsLegacyDocument(_controller.Document) ? DisplayStyle.None : DisplayStyle.Flex;
            if (!visible)
            {
                _startError = string.Empty;
                SetTextIfChanged(_startMessage, string.Empty);
                _startMessage.style.display = DisplayStyle.None;
                return;
            }

            var hasAvatar = _preview.HasAvatar;
            SetTextIfChanged(_startAvatarLabel, _preview.IsLoading
                ? "読み込んでいます…"
                : hasAvatar
                    ? Path.GetFileName(_preview.AvatarPath) + "（プレビュー用の複製です）"
                    : _preview.State == VlatPreviewState.AvatarLoadError
                        ? "VRM を読み込めませんでした。VRM 1.0 のファイルか確かめてください。"
                        : "まだ開いていません。元のファイルは書き換えません。");
            _startOpenVrmButton.text = hasAvatar ? "ほかの VRM を開く…" : "VRM を開く…";
            _startOpenVrmButton.SetEnabled(!_preview.IsLoading);
            _startOpenVrmButton.EnableInClassList("vlat-primary", !hasAvatar);

            if (!ReferenceEquals(_startPartsAvatar, _preview.AvatarRoot) || _startPartsCount != _preview.VertexTargets.Count)
                RebuildStartParts();
            for (var index = 0; index < _startPartButtons.Count; index++)
                _startPartButtons[index].EnableInClassList("vlat-choice-selected", _startPartTargets[index] == _startPartTarget);
            SetTextIfChanged(_startPartNote, !hasAvatar
                ? "VRM を開くと、ここにパーツが並びます。"
                : _startPartButtons.Count == 0
                    ? "動かせるパーツ（SkinnedMeshRenderer）が見つかりません。"
                    : "形を変えたいメッシュを 1 つ選びます。顔と前髪が別のメッシュなら、まず前髪から試すと分かりやすいです。");

            var partChosen = _startPartTarget >= 0;
            _startStepAvatar.EnableInClassList("vlat-start-step-done", hasAvatar);
            _startStepPart.EnableInClassList("vlat-start-step-done", hasAvatar && partChosen);
            _startStepPart.SetEnabled(hasAvatar);
            _startStepAngles.SetEnabled(hasAvatar && partChosen);
            foreach (var toggle in _startAngleToggles) toggle.EnableInClassList("vlat-choice-selected", toggle.value);
            var ready = hasAvatar && partChosen && !_preview.IsLoading;
            _startEditingButton.SetEnabled(ready);
            _startEditingButton.tooltip = ready
                ? "グループを作り、選んだパーツと角度でキーを用意します。元に戻すで 1 回で戻せます。"
                : !hasAvatar ? "先に VRM を開きます。" : "先に直すパーツを選びます。";
            SetTextIfChanged(_startMessage, _startError);
            _startMessage.style.display = _startError.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void RebuildStartParts()
        {
            _startParts.Clear();
            _startPartButtons.Clear();
            _startPartTargets.Clear();
            _startPartsAvatar = _preview.AvatarRoot;
            _startPartsCount = _preview.VertexTargets.Count;
            if (!_preview.HasAvatar)
            {
                _startPartTarget = -1;
                return;
            }

            var targets = _preview.VertexTargets;
            for (var index = 0; index < targets.Count; index++)
            {
                var target = targets[index];
                if (!(target.Renderer is SkinnedMeshRenderer)) continue;
                var targetIndex = index;
                var button = new Button(() =>
                {
                    _startPartTarget = targetIndex;
                    _startError = string.Empty;
                    RefreshStartScreen();
                    RefreshWorkflowGuide();
                })
                {
                    name = "start-part-" + _startPartButtons.Count,
                    tooltip = target.HierarchyPath,
                };
                button.AddToClassList("vlat-choice");
                var title = new Label(target.Renderer.name) { pickingMode = PickingMode.Ignore };
                title.AddToClassList("vlat-choice-title");
                var meshName = target.SourceMesh == null ? string.Empty : target.SourceMesh.name;
                var detail = new Label(target.VertexCount.ToString("N0", CultureInfo.InvariantCulture) + " 頂点" +
                    (meshName.Length == 0 || meshName == target.Renderer.name ? string.Empty : "　メッシュ " + meshName))
                { pickingMode = PickingMode.Ignore };
                detail.AddToClassList("vlat-choice-detail");
                button.Add(title);
                button.Add(detail);
                _startParts.Add(button);
                _startPartButtons.Add(button);
                _startPartTargets.Add(index);
            }

            if (!_startPartTargets.Contains(_startPartTarget)) _startPartTarget = -1;
        }

        /// <summary>
        /// 「編集をはじめる」。グループの作成、直すパーツの指定、選んだ角度のキーを1回の元に戻すにまとめ、
        /// ななめのキーを選んで「選ぶ」ツールにします。
        /// </summary>
        internal void StartEditing()
        {
            _startError = string.Empty;
            if (_preview == null || !_preview.HasAvatar || !IsStartScreenVisible()) return;
            var targets = _preview.VertexTargets;
            if (_startPartTarget < 0 || _startPartTarget >= targets.Count) return;
            var target = targets[_startPartTarget];
            var root = _preview.AvatarRoot;
            try
            {
                // 失敗するパーツでグループだけ残さないよう、先に同じ検査をする。
                VlatKeyformBindingUtility.Capture(root, target.Renderer, target.SourceMesh, out _);
            }
            catch (ArgumentException exception)
            {
                _startError = "このパーツは使えません。" + exception.Message;
                RefreshStartScreen();
                return;
            }

            var undoGroup = Undo.GetCurrentGroup();
            if (!_controller.AddKeyformGroup())
            {
                _startError = _controller.LastMessage;
                RefreshAll();
                return;
            }

            var bound = _controller.BindKeyformTarget(0, root, target.Renderer, target.SourceMesh);
            var group = _controller.Document.KeyformGroups[0];
            var angles = new List<int>(_startAngles);
            angles.Sort();
            foreach (var angle in angles)
            {
                if (angle == 0 || angle <= group.SupportStart || angle >= group.SupportEnd) continue;
                _controller.AddKeyform(0, angle);
            }

            // Recordが操作ごとにUndo groupを進めるので、最初の操作のgroupへまとめる。
            Undo.CollapseUndoOperations(undoGroup + 1);
            var key = PreferredStartKey(group);
            _controller.SelectKeyform(0, key);
            _startPartTarget = -1;
            RefreshAll();
            MoveMainCameraToKey(key);
            if (bound) SetTool(VlatEditorTool.Select);
            _statusMessage = bound
                ? "編集をはじめました。" + FormatYaw(group.Keys[key].Yaw) + " のキーで、形を変えたい頂点を選びます。"
                : _controller.LastMessage;
            RefreshStatusLine();
        }

        // はじめに開くキー: 45°があればそれ、なければ0°以外でいちばん0°に近いキー。
        private static int PreferredStartKey(VlatAuthoringKeyformGroup group)
        {
            var best = 0;
            var bestScore = double.MaxValue;
            for (var index = 0; index < group.Keys.Count; index++)
            {
                var yaw = group.Keys[index].Yaw;
                if (Math.Abs(yaw) < 0.5) continue;
                var score = Math.Abs(yaw - 45);
                if (score < bestScore)
                {
                    best = index;
                    bestScore = score;
                }
            }

            return best;
        }

        // VRMを開かずに始める。直すパーツはシーンのアバターで指定するので、詳細を開く。
        private void StartWithSceneAvatar()
        {
            ApplyChange(() => _controller.AddKeyformGroup());
            _foldoutStates["details-scene-binding"] = true;
            _detailsOpen = true;
            RefreshAll();
        }
    }
}
