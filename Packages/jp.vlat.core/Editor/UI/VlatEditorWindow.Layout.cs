using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VLat.Core.Validation;

namespace VLat.Editor.Authoring
{
    // 表示だけの状態同期（画面の切り替え、ビュー上の表示、状態の印、ステータス行）。Document/Undo/Previewは変更しない。
    internal sealed partial class VlatEditorWindow
    {
        private const string LegacyHint =
            "旧形式（v0.1）のファイルを編集しています。右の一覧で Band・Layer・Modifier を選び、その下で値を変えます。";

        private Label _workflowHint;
        private Label _documentTitleLabel;
        private Label _validationSummary;
        private VisualElement _legacySection;
        private VisualElement _toolOptions;
        private VisualElement _viewControlGroup;
        private VisualElement _selectionControlGroup;
        private VisualElement _moveControlGroup;
        private VisualElement _detailsPane;
        private VisualElement _detailsDynamic;
        private Foldout _detailsValidation;
        private Button _detailsButton;
        private VisualElement _viewTag;
        private Label _viewTagTitle;
        private Label _viewTagDetail;
        private VisualElement _compareViews;
        private Label _compareALabel;
        private Label _compareBLabel;
        private VisualElement _viewportPlaceholder;
        private Label _viewportPlaceholderLabel;
        private Label _viewportMessage;
        private Label _moveSelectionStatus;
        private bool _viewportTiny;
        private Label[] _messageLabels;
        private bool? _legacySectionsShown;
        [SerializeField] private bool _detailsOpen;
        private int _shownMessageSequence = -1;
        private int _shownDragSequence = -1;
        private string _statusMessage = string.Empty;

        private void ConfigureLayout()
        {
            // Personal(ライト)テーマでは、強調色だけをUSSで上書きする。
            rootVisualElement.EnableInClassList("vlat-light", !EditorGUIUtility.isProSkin);
            rootVisualElement.focusable = true;
            _workflowHint = RequireElement<Label>("workflow-hint");
            _documentTitleLabel = RequireElement<Label>("document-title-label");
            _validationSummary = RequireElement<Label>("validation-summary");
            _legacySection = RequireElement<VisualElement>("legacy-section");
            _toolOptions = RequireElement<VisualElement>("tool-options");
            _viewControlGroup = RequireElement<VisualElement>("view-control-group");
            _selectionControlGroup = RequireElement<VisualElement>("selection-control-group");
            _moveControlGroup = RequireElement<VisualElement>("move-control-group");
            _detailsPane = RequireElement<VisualElement>("details-pane");
            _detailsDynamic = RequireElement<VisualElement>("details-dynamic");
            _detailsValidation = RequireElement<Foldout>("details-validation");
            _detailsButton = RequireElement<Button>("details-button");
            _viewTag = RequireElement<VisualElement>("view-tag");
            _viewTagTitle = RequireElement<Label>("view-tag-title");
            _viewTagDetail = RequireElement<Label>("view-tag-detail");
            _compareViews = RequireElement<VisualElement>("compare-views");
            _compareALabel = RequireElement<Label>("compare-a-label");
            _compareBLabel = RequireElement<Label>("compare-b-label");
            _viewportPlaceholder = RequireElement<VisualElement>("viewport-placeholder");
            _viewportPlaceholderLabel = RequireElement<Label>("viewport-placeholder-label");
            _viewportMessage = RequireElement<Label>("viewport-message");
            _moveSelectionStatus = RequireElement<Label>("move-selection-status");
            _messageLabels = new[]
            {
                RequireElement<Label>("vertex-selection-diagnostic"),
                RequireElement<Label>("viewport-message"),
                RequireElement<Label>("start-message"),
            };
            _buildTarget = RequireElement<ScrollView>("inspector-pane");
            // 編集ビューが狭いときは比較ビューを小さくし、さらに狭ければ隠して、編集ビューを優先する。
            var viewport = RequireElement<VisualElement>("viewport");
            viewport.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                var width = evt.newRect.width;
                viewport.EnableInClassList("vlat-viewport-narrow", width < 600f);
                var tiny = width < 420f;
                if (tiny == _viewportTiny) return;
                _viewportTiny = tiny;
                RefreshViewportUi();
                RefreshRail();
            });
            _detailsButton.clicked += () =>
            {
                _detailsOpen = !_detailsOpen;
                RefreshDetailsVisibility();
            };
            // 検証結果の印を押すと、詳細の検証結果を開く。
            _validationSummary.RegisterCallback<ClickEvent>(_ =>
            {
                _detailsOpen = true;
                _detailsValidation.value = true;
                RefreshDetailsVisibility();
            });
            _legacySectionsShown = null;
            _shownMessageSequence = -1;
            _shownDragSequence = -1;
        }

        // 比較ビューは、VRMがあり、編集ビューが狭すぎないときだけ出す（レールのA・Bの印も同じ）。
        private bool CompareViewsVisible => _preview != null && _preview.HasAvatar && !_viewportTiny;

        private static bool IsLegacyDocument(VlatAuthoringDocument document) =>
            document != null && (document.Bands.Count != 0 || document.Layers.Count != 0);

        // 旧形式のファイルのときだけ、旧形式の一覧を出す。キーの道具は隠す。
        private void RefreshDocumentSections()
        {
            if (_legacySection == null || _controller?.Document == null) return;
            var legacy = IsLegacyDocument(_controller.Document);
            if (_legacySectionsShown == legacy) return;
            _legacySectionsShown = legacy;
            _legacySection.style.display = legacy ? DisplayStyle.Flex : DisplayStyle.None;
            RequireElement<VisualElement>("tool-strip").style.display = legacy ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void RefreshDetailsVisibility()
        {
            if (_detailsPane == null) return;
            _detailsPane.style.display = _detailsOpen ? DisplayStyle.Flex : DisplayStyle.None;
            _detailsButton.EnableInClassList("vlat-toggle-on", _detailsOpen);
        }

        private void RefreshStatusPresentation(int errorCount, int warningCount)
        {
            if (_documentTitleLabel == null) return;
            var path = _controller.SourcePath;
            var untitled = string.IsNullOrWhiteSpace(path);
            SetTextIfChanged(_documentTitleLabel, untitled ? "無題のファイル" : Path.GetFileName(path));
            _documentTitleLabel.tooltip = untitled ? "まだ保存していません。" : path;
            SetTextIfChanged(_dirtyLabel, _controller.IsDirty ? "未保存の変更" : "保存済み");
            _dirtyLabel.EnableInClassList("vlat-dirty-modified", _controller.IsDirty);
            SetTextIfChanged(_validationSummary, errorCount != 0
                ? "エラー " + errorCount + " / 警告 " + warningCount
                : warningCount != 0 ? "警告 " + warningCount : "問題なし");
            _validationSummary.EnableInClassList("vlat-validation-error", errorCount != 0);
            _validationSummary.EnableInClassList("vlat-validation-warning", errorCount == 0 && warningCount != 0);
            if (_controller.MessageSequence != _shownMessageSequence)
            {
                _shownMessageSequence = _controller.MessageSequence;
                _statusMessage = _controller.LastMessage ?? string.Empty;
            }

            RefreshStatusLine();
        }

        // ステータス行の右側。最後に起きたこと（ファイルの操作、編集、ドラッグ、VRMの読み込み）を1つだけ出す。
        private void RefreshStatusLine()
        {
            if (_statusLabel == null) return;
            if (_preview != null && _preview.DragStatusSequence != _shownDragSequence)
            {
                _shownDragSequence = _preview.DragStatusSequence;
                if (!string.IsNullOrEmpty(_preview.DragStatus)) _statusMessage = _preview.DragStatus;
            }

            SetTextIfChanged(_statusLabel, _statusMessage);
            _statusLabel.tooltip = _statusMessage;
        }

        // ステータス行の左側。今の状態から、次にすることを1文で出す（手順の「済み」は持たない）。
        private void RefreshWorkflowGuide()
        {
            if (_workflowHint == null || _controller?.Document == null || _preview == null) return;
            SetTextIfChanged(_workflowHint, BuildGuide());
        }

        private string BuildGuide()
        {
            var document = _controller.Document;
            if (IsLegacyDocument(document)) return LegacyHint;
            if (document.KeyformGroups.Count == 0)
            {
                if (_preview.IsLoading) return "VRM を読み込んでいます…";
                if (!_preview.HasAvatar) return "はじめに VRM を開きます。元のファイルは書き換えません。";
                if (_startPartTarget < 0) return "形を変えたいパーツを選びます。";
                return "形を変えたい角度を選んで、「編集をはじめる」を押します。";
            }

            var g = DisplayGroupIndex();
            if (!IsGroupBound(g)) return "直すパーツが決まっていません。右の「直すパーツ」か「詳細」で指定します。";
            if (!_preview.HasAvatar) return "VRM を開くと、形を見ながら直せます。";
            var k = _controller.Selection.FindKeyformIndex(document);
            if (k < 0) return "レールのひし形を押すか、← → でキーを選びます。";
            var yaw = FormatYaw(document.KeyformGroups[g].Keys[k].Yaw);
            switch (CurrentTool)
            {
                case VlatEditorTool.Select:
                    var selected = _preview.VertexSelection.SelectedCount;
                    return selected == 0
                        ? yaw + " のキー: 形を変えたい頂点を、クリックかブラシで選びます。"
                        : yaw + " のキー: " + selected.ToString("N0", CultureInfo.InvariantCulture) + " 頂点を選んでいます。3 を押すと、ドラッグで動かせます。";
                case VlatEditorTool.Move:
                    if (_preview.IsDragging) return "ドラッグ中です。手を離すと確定し、Esc で中止します。";
                    return _preview.VertexSelection.SelectedCount == 0
                        ? yaw + " のキー: 先に 2 を押して、動かす頂点を選びます。"
                        : yaw + " のキー: 選んだ頂点をドラッグして形を変えます。";
                default:
                    return yaw + " のキーを選んでいます。2 で頂点を選び、3 で動かします。← → でとなりのキーへ移ります。";
            }
        }

        // 編集ビューの上の表示（何の形を見ているか）、比較ビューの見出し、VRMがないときの案内。
        private void RefreshViewportUi()
        {
            if (_viewTag == null || _preview == null || _controller?.Document == null) return;
            var hasAvatar = _preview.HasAvatar;
            _viewportPlaceholder.style.display = hasAvatar ? DisplayStyle.None : DisplayStyle.Flex;
            SetTextIfChanged(_viewportPlaceholderLabel, _preview.IsLoading
                ? "VRM を読み込んでいます…"
                : _preview.State == VlatPreviewState.AvatarLoadError
                    ? "VRM を読み込めませんでした。VRM 1.0 のファイルか確かめて、もう一度開いてください。"
                    : "VRM を開くと、ここにアバターが表示されます。元のファイルは書き換えません。");
            RequireElement<Button>("viewport-open-vrm-button").SetEnabled(!_preview.IsLoading);
            _compareViews.style.display = CompareViewsVisible ? DisplayStyle.Flex : DisplayStyle.None;
            RequireElement<Button>("reset-main-view-button").style.display = hasAvatar ? DisplayStyle.Flex : DisplayStyle.None;

            var stale = _preview.IsDragging;
            SetCompareLabel(_compareALabel, "比較 A", VlatPreviewView.SubA, stale);
            SetCompareLabel(_compareBLabel, "比較 B", VlatPreviewView.SubB, stale);

            var document = _controller.Document;
            var g = DisplayGroupIndex();
            var showTag = hasAvatar && g >= 0;
            _viewTag.style.display = showTag ? DisplayStyle.Flex : DisplayStyle.None;
            if (showTag) BuildViewTag(document.KeyformGroups[g]);

            var message = string.Empty;
            if (hasAvatar && _preview.State == VlatPreviewState.VLatUnavailable)
                message = "補正を表示できません。「詳細」の検証結果とプレビューの詳細を確かめてください。";
            SetTextIfChanged(_viewportMessage, message);
            _viewportMessage.style.display = message.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void SetCompareLabel(Label label, string name, VlatPreviewView view, bool stale)
        {
            var text = name + "  " + FormatYaw(Math.Round(_preview.GetViewState(view).Yaw));
            SetTextIfChanged(label, stale ? text + "（手を離すと更新）" : text);
            label.EnableInClassList("vlat-compare-stale", stale);
        }

        private void BuildViewTag(VlatAuthoringKeyformGroup group)
        {
            var tool = CurrentTool;
            var k = _controller.Selection.FindKeyformIndex(_controller.Document);
            if (tool != VlatEditorTool.View && k >= 0)
            {
                SetTextIfChanged(_viewTagTitle, FormatYaw(group.Keys[k].Yaw) + " のキー");
                SetTextIfChanged(_viewTagDetail, tool == VlatEditorTool.Select
                    ? "頂点を選ぶ（補正前の形で表示）"
                    : "形を動かす（このキーの形で表示）");
                return;
            }

            var yaw = (double)_preview.GetViewState(VlatPreviewView.Main).Yaw;
            var rounded = Math.Round(yaw);
            SetTextIfChanged(_viewTagTitle, FormatYaw(rounded));
            string detail;
            if (!_preview.ApplicationEnabled)
                detail = _spaceHeld ? "補正前の形（Space を離すと戻ります）" : "補正前の形（「補正を表示」がオフ）";
            else if (yaw <= group.SupportStart || yaw >= group.SupportEnd)
                detail = "効く範囲の外なので、補正はかかりません";
            else
                detail = DescribeBetweenKeys(group, yaw);
            SetTextIfChanged(_viewTagDetail, detail);
        }

        private static string DescribeBetweenKeys(VlatAuthoringKeyformGroup group, double yaw)
        {
            VlatAuthoringKeyform before = null;
            VlatAuthoringKeyform after = null;
            foreach (var key in group.Keys)
            {
                if (Math.Abs(key.Yaw - yaw) < 0.5) return FormatYaw(key.Yaw) + " のキーの形";
                if (key.Yaw < yaw) before = key;
                else if (after == null) after = key;
            }

            if (before != null && after != null)
                return FormatYaw(before.Yaw) + " と " + FormatYaw(after.Yaw) + " のあいだの形（見るだけ）";
            var nearest = before ?? after;
            return nearest == null ? "補正なし" : FormatYaw(nearest.Yaw) + " のキーの形（いちばん近いキー）";
        }

        // 空のメッセージ行は高さを取らないように隠す。ツールの設定は、選んでいるツールの分だけ出す。
        private void RefreshPreviewMessages()
        {
            if (_messageLabels == null) return;
            foreach (var label in _messageLabels)
                label.style.display = string.IsNullOrEmpty(label.text) ? DisplayStyle.None : DisplayStyle.Flex;
            if (_preview == null || _controller?.Document == null) return;
            var legacy = IsLegacyDocument(_controller.Document);
            var tool = CurrentTool;
            _viewControlGroup.style.display = legacy || tool == VlatEditorTool.View ? DisplayStyle.Flex : DisplayStyle.None;
            _selectionControlGroup.style.display = !legacy && tool == VlatEditorTool.Select ? DisplayStyle.Flex : DisplayStyle.None;
            _moveControlGroup.style.display = !legacy && tool == VlatEditorTool.Move ? DisplayStyle.Flex : DisplayStyle.None;
            var selected = _preview.VertexSelection.SelectedCount;
            SetTextIfChanged(_moveSelectionStatus, selected == 0
                ? "まだ頂点を選んでいません。2 を押して選びます。"
                : selected.ToString("N0", CultureInfo.InvariantCulture) + " 頂点を選んでいます。");
            RefreshStatusLine();
        }

        private static void SetTextIfChanged(Label label, string text)
        {
            if (label != null && !string.Equals(label.text, text, StringComparison.Ordinal)) label.text = text;
        }

        // 検証結果の表示用項目(D-012)。Coreの診断は変更せず、表示だけを整理する。
        internal enum DiagnosticDisplayLevel { Error, Warning, Info }

        internal sealed class DiagnosticDisplayItem
        {
            internal DiagnosticDisplayLevel Level;
            internal string Path;
            internal string Message;
            internal int SourceIndex;
        }

        private const string KeyformVersionNote =
            "Keyform v0.2 のファイルです。Keyform に対応した Kakudoll で編集できます（対応していない古い Runtime・Editor では読み込めません）。";

        // 同じ内容の重複を1件にまとめ、Keyform v0.2文書のnewer-minor警告（仕様上の旧backend向け注意）は情報表示にする。
        private void RebuildDiagnosticItems()
        {
            _diagnosticItems.Clear();
            var document = _controller.Document;
            var keyformDocument = document != null && document.KeyformGroups.Count != 0 &&
                document.Metadata.Version == VlatValidator.KeyformVersion;
            for (var index = 0; index < _controller.Diagnostics.Count; index++)
            {
                var source = _controller.Diagnostics[index];
                var item = new DiagnosticDisplayItem
                {
                    Level = source.Severity == VlatDiagnosticSeverity.Error ? DiagnosticDisplayLevel.Error : DiagnosticDisplayLevel.Warning,
                    Path = source.Path,
                    Message = source.Message,
                    SourceIndex = index,
                };
                if (keyformDocument && source.Severity == VlatDiagnosticSeverity.Warning && source.Path == "$.vlat.version")
                {
                    item.Level = DiagnosticDisplayLevel.Info;
                    item.Message = KeyformVersionNote;
                }

                var duplicate = false;
                foreach (var existing in _diagnosticItems)
                    if (existing.Level == item.Level && existing.Path == item.Path && existing.Message == item.Message) { duplicate = true; break; }
                if (!duplicate) _diagnosticItems.Add(item);
            }
        }

        private static string FormatDiagnosticLevel(DiagnosticDisplayLevel level)
        {
            switch (level)
            {
                case DiagnosticDisplayLevel.Error: return "エラー";
                case DiagnosticDisplayLevel.Warning: return "警告";
                default: return "情報";
            }
        }

        private void AddSection(string text)
        {
            var label = new Label(text);
            label.AddToClassList("vlat-inspector-section");
            _buildTarget.Add(label);
        }
    }
}
