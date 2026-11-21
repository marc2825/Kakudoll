using System.IO;
using UnityEditor;
using UnityEngine.UIElements;
using VLat.Core.Validation;

namespace VLat.Editor.Authoring
{
    // 表示専用の状態同期(作業手順、状態バッジ、メッセージ表示)。Document/Undo/Previewの状態は変更しない。
    internal sealed partial class VlatEditorWindow
    {
        private static readonly string[] WorkflowStepNames =
        {
            "workflow-step-vrm", "workflow-step-group", "workflow-step-target",
            "workflow-step-key", "workflow-step-select", "workflow-step-drag",
        };

        private static readonly string[] WorkflowHints =
        {
            "次: プレビュー上部の「VRMを開く…」でVRM 1.0を読み込みます（元ファイル・元アセットは変更しません）。",
            "次: 左の「Yaw Keyform」の「+」でグループを作成します（空のドキュメントで作成できます）。",
            "次: 右の「対象Mesh」で、シーン上のアバターRootとRendererを指定して「対象に設定」を押します。",
            "次: 左の「Yawキー」から編集するキーを選びます。「+」で空いている角度にキーを追加できます。",
            "次: プレビュー上部の「頂点選択」をオンにし、Mainビューで動かしたい頂点を選びます。",
            "次: 「2Dドラッグ」をオンにし、Mainビューを左ドラッグして形を動かします（Escで中止）。",
        };

        private const string WorkflowDoneHint =
            "Mainビューを左ドラッグして編集できます。Sub A/Bは確定後の補間結果です。1回のドラッグは「元に戻す」1回で戻ります。";

        private const string LegacyWorkflowHint =
            "v0.1 Layer文書を編集中です。左の「Layer編集」でBand/Layer/Modifierを選び、右側で値を編集します。";

        private readonly Label[] _workflowSteps = new Label[6];
        private readonly bool[] _workflowDone = new bool[6];
        private VisualElement _workflowStepRow;
        private Label _workflowHint;
        private Label _documentTitleLabel;
        private Label _validationSummary;
        private Foldout _keyformSection;
        private Foldout _legacySection;
        private VisualElement _selectionControlGroup;
        private Label[] _messageLabels;
        private bool? _legacySectionsShown;

        private void ConfigureLayout()
        {
            // Personal(ライト)テーマでは強調色だけをUSSで上書きする。
            rootVisualElement.EnableInClassList("vlat-light", !EditorGUIUtility.isProSkin);
            for (var index = 0; index < WorkflowStepNames.Length; index++)
                _workflowSteps[index] = RequireElement<Label>(WorkflowStepNames[index]);
            _workflowStepRow = RequireElement<VisualElement>("workflow-steps");
            _workflowHint = RequireElement<Label>("workflow-hint");
            _documentTitleLabel = RequireElement<Label>("document-title-label");
            _validationSummary = RequireElement<Label>("validation-summary");
            _keyformSection = RequireElement<Foldout>("keyform-section");
            _legacySection = RequireElement<Foldout>("legacy-section");
            _selectionControlGroup = RequireElement<VisualElement>("selection-control-group");
            _messageLabels = new[]
            {
                RequireElement<Label>("keyform-drag-status"),
                RequireElement<Label>("vertex-selection-diagnostic"),
                RequireElement<Label>("preview-diagnostic-label"),
            };
            _legacySectionsShown = null;
        }

        // Keyform/Layerの文書種別が変わった時だけ折りたたみを切り替え、利用者の開閉操作を上書きし続けない。
        private void RefreshDocumentSections()
        {
            if (_legacySection == null || _controller?.Document == null) return;
            var document = _controller.Document;
            var legacy = document.Bands.Count != 0 || document.Layers.Count != 0;
            if (_legacySectionsShown == legacy) return;
            _legacySectionsShown = legacy;
            _legacySection.value = legacy;
            _keyformSection.value = !legacy || document.KeyformGroups.Count != 0;
        }

        private void RefreshStatusPresentation(int errorCount, int warningCount)
        {
            if (_documentTitleLabel == null) return;
            var path = _controller.SourcePath;
            var untitled = string.IsNullOrWhiteSpace(path);
            SetTextIfChanged(_documentTitleLabel, untitled ? "無題のドキュメント" : Path.GetFileName(path));
            _documentTitleLabel.tooltip = untitled ? "まだ保存されていません。" : path;
            SetTextIfChanged(_dirtyLabel, _controller.IsDirty ? "● 未保存の変更" : "保存済み");
            _dirtyLabel.EnableInClassList("vlat-dirty-modified", _controller.IsDirty);
            SetTextIfChanged(_validationSummary, errorCount != 0
                ? "エラー " + errorCount + " / 警告 " + warningCount
                : warningCount != 0 ? "警告 " + warningCount : "問題なし");
            _validationSummary.EnableInClassList("vlat-validation-error", errorCount != 0);
            _validationSummary.EnableInClassList("vlat-validation-warning", errorCount == 0 && warningCount != 0);
            SetTextIfChanged(_statusLabel, _controller.LastMessage ?? string.Empty);
            _statusLabel.tooltip = untitled ? "無題のドキュメント" : path;
        }

        private void RefreshWorkflowGuide()
        {
            if (_workflowHint == null || _controller?.Document == null || _preview == null) return;
            var document = _controller.Document;
            var legacy = document.Bands.Count != 0 || document.Layers.Count != 0;
            _workflowStepRow.style.display = legacy ? DisplayStyle.None : DisplayStyle.Flex;
            if (legacy)
            {
                SetTextIfChanged(_workflowHint, LegacyWorkflowHint);
                return;
            }

            var groupIndex = _controller.Selection.FindKeyformGroupIndex(document);
            if (groupIndex < 0 && document.KeyformGroups.Count != 0) groupIndex = 0;
            var group = groupIndex < 0 ? null : document.KeyformGroups[groupIndex];
            _workflowDone[0] = _preview.HasAvatar;
            _workflowDone[1] = group != null;
            _workflowDone[2] = group != null && group.MeshBinding >= 0 &&
                group.MeshBinding < document.Metadata.MeshBindings.Count;
            _workflowDone[3] = _controller.Selection.FindKeyformIndex(document) >= 0;
            _workflowDone[4] = _preview.GeometrySelectionEnabled && _preview.VertexSelection.SelectedCount > 0;
            _workflowDone[5] = _preview.KeyformDragEnabled;
            var current = -1;
            for (var index = 0; index < _workflowSteps.Length; index++)
            {
                var done = _workflowDone[index];
                if (!done && current < 0) current = index;
                _workflowSteps[index].EnableInClassList("vlat-step-done", done);
                _workflowSteps[index].EnableInClassList("vlat-step-current", index == current);
            }

            SetTextIfChanged(_workflowHint, current < 0 ? WorkflowDoneHint : WorkflowHints[current]);
        }

        // 空のメッセージ行は高さを取らないよう非表示にし、Previewの描画面積を確保する。
        private void RefreshPreviewMessages()
        {
            if (_messageLabels == null) return;
            foreach (var label in _messageLabels)
                label.style.display = string.IsNullOrEmpty(label.text) ? DisplayStyle.None : DisplayStyle.Flex;
            if (_selectionControlGroup != null && _preview != null)
                _selectionControlGroup.style.display =
                    _preview.GeometrySelectionEnabled ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void SetTextIfChanged(Label label, string text)
        {
            if (label != null && !string.Equals(label.text, text, System.StringComparison.Ordinal)) label.text = text;
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
            "Keyform v0.2文書です。Keyform対応のVLat Editorで編集できます（Keyform非対応の旧Runtime・旧Editorでは読み込めません）。";

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
            _inspector.Add(label);
        }
    }
}
