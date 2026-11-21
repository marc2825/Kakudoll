using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VLat.Core.Validation;

namespace VLat.Editor.Authoring
{
    internal sealed partial class VlatEditorWindow : EditorWindow
    {
        private const string UxmlPath = "Packages/jp.vlat.core/Editor/UI/VlatEditorWindow.uxml";
        private const string UssPath = "Packages/jp.vlat.core/Editor/UI/VlatEditorWindow.uss";

        private readonly List<DiagnosticDisplayItem> _diagnosticItems = new List<DiagnosticDisplayItem>();
        private readonly List<VlatAuthoringModifier> _emptyModifiers = new List<VlatAuthoringModifier>();
        private readonly List<string> _expressionChoices = new List<string>();
        private readonly List<string> _vertexTargetChoices = new List<string>();
        private readonly List<string> _vertexToolChoices = new List<string>
        {
            "クリック (Single)",
            "ブラシ (Brush)",
            "矩形 (Rectangle)",
        };
        private readonly List<string> _vertexVisibilityChoices = new List<string>
        {
            "見えている面のみ (Visible Only)",
            "裏側も選択 (Select Through)",
        };
        private readonly List<string> _vertexFalloffChoices = new List<string>
        {
            "なめらか (Smoothstep)",
            "直線 (Linear)",
        };
        private readonly PreviewPointerState[] _previewPointerStates =
        {
            new PreviewPointerState(),
            new PreviewPointerState(),
            new PreviewPointerState(),
        };
        private VlatEditorDocumentController _controller;
        private VlatPreviewController _preview;
        private ListView _bandList;
        private ListView _layerList;
        private ListView _modifierList;
        private ListView _diagnosticList;
        private ScrollView _inspector;
        private Label _dirtyLabel;
        private Label _statusLabel;
        private Image[] _previewImages;
        private Image _vertexSelectionOverlay;
        private Slider _previewYaw;
        private Slider _previewPitch;
        private Toggle _previewAfterToggle;
        private DropdownField _expressionDropdown;
        private Slider _expressionWeight;
        private Label _previewAvatarLabel;
        private Label _previewDiagnosticLabel;
        private Label _previewPerformanceLabel;
        private Label _previewResourceLabel;
        private Toggle _geometrySelectionToggle;
        private DropdownField _vertexTargetDropdown;
        private DropdownField _vertexToolDropdown;
        private DropdownField _vertexVisibilityDropdown;
        private DropdownField _vertexFalloffDropdown;
        private Slider _vertexBrushRadius;
        private Slider _vertexSoftRadius;
        private Label _vertexSelectionStatus;
        private Label _vertexSelectionDiagnostic;
        private Label _vertexSelectionPerformance;
        private Label _previewHelpLabel;
        private int _selectedExpressionIndex = -1;
        private bool _refreshing;
        private bool _assemblyReloading;
        private bool _closeApproved;
        [SerializeField] private string _recoveryDirectory;
        private static string _creatingRecoveryDirectory;

        internal static VlatEditorWindow CreateForRecoveryDirectory(string directory)
        {
            var previous = _creatingRecoveryDirectory;
            _creatingRecoveryDirectory = directory;
            try { return CreateInstance<VlatEditorWindow>(); }
            finally { _creatingRecoveryDirectory = previous; }
        }

        [MenuItem("VLat/Editor")]
        private static void OpenWindow()
        {
            var window = GetWindow<VlatEditorWindow>();
            window.titleContent = new GUIContent("VLat Editor");
            window.minSize = new Vector2(900.0f, 760.0f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("VLat Editor");
            minSize = new Vector2(900.0f, 760.0f);
            saveChangesMessage = "VLatドキュメントに未保存の変更があります。";
            _assemblyReloading = false;
            _closeApproved = false;
            if (string.IsNullOrEmpty(_recoveryDirectory)) _recoveryDirectory = _creatingRecoveryDirectory;
            _controller = new VlatEditorDocumentController(new VlatRecoveryStore(_recoveryDirectory));
            _preview = new VlatPreviewController();
            Selection.selectionChanged -= OnEditorSelectionChanged;
            Selection.selectionChanged += OnEditorSelectionChanged;
            if (!_controller.TryRestore())
            {
                _controller.NewDocument(VlatCloseDecision.Discard);
            }

            Undo.undoRedoPerformed -= OnUndoRedo;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            Undo.undoRedoPerformed += OnUndoRedo;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        /// <summary>
        /// UXMLからPhase 2BのEditor Shellを構築します。
        /// </summary>
        public void CreateGUI()
        {
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (visualTree == null)
            {
                rootVisualElement.Add(new HelpBox(
                    "VlatEditorWindow.uxmlを読み込めません。",
                    HelpBoxMessageType.Error));
                return;
            }

            rootVisualElement.Clear();
            visualTree.CloneTree(rootVisualElement);
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (styleSheet != null)
            {
                rootVisualElement.styleSheets.Add(styleSheet);
            }

            CacheElements();
            ConfigureLayout();
            ConfigureLists();
            WireCommands();
            ConfigureKeyformUi();
            ConfigureKeyformDragUi();
            RefreshAll();
        }

        /// <summary>
        /// Unity標準の未保存確認からSaveが選ばれたとき、安全な保存処理を行います。
        /// </summary>
        public override void SaveChanges()
        {
            if (_controller == null || _controller.Document == null)
            {
                _closeApproved = true;
                base.SaveChanges();
                return;
            }

            var savePath = _controller.SourcePath;
            if (string.IsNullOrWhiteSpace(savePath))
            {
                savePath = ChooseSavePath();
                if (string.IsNullOrWhiteSpace(savePath))
                {
                    return;
                }
            }

            if (_controller.TryClose(VlatCloseDecision.Save, savePath))
            {
                _closeApproved = true;
                hasUnsavedChanges = false;
                base.SaveChanges();
            }
            else
            {
                RefreshAll();
            }
        }

        /// <summary>
        /// Unity標準の未保存確認からDiscardが選ばれたとき、recoveryを破棄して閉じます。
        /// </summary>
        public override void DiscardChanges()
        {
            if (_controller != null)
            {
                _controller.TryClose(VlatCloseDecision.Discard);
            }

            _closeApproved = true;
            hasUnsavedChanges = false;
            base.DiscardChanges();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            Selection.selectionChanged -= OnEditorSelectionChanged;
            CancelWindowDrag();
            _preview?.Dispose();
            _preview = null;
            // Window再生成/Editor終了時も、最後の編集・保存基準・選択をcheckpointする。
            if (!_closeApproved && _controller != null && _controller.Document != null)
                _controller.PrepareForDomainReload();
        }

        private void OnDestroy()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            Selection.selectionChanged -= OnEditorSelectionChanged;
            CancelWindowDrag();
            _preview?.Dispose();
            _preview = null;
            if (_assemblyReloading)
            {
                return;
            }

            if (_closeApproved) _controller?.Dispose();
            else if (_controller != null && _controller.Document != null) _controller.PrepareForDomainReload();
        }

        private void CacheElements()
        {
            _bandList = RequireElement<ListView>("band-list");
            _layerList = RequireElement<ListView>("layer-list");
            _modifierList = RequireElement<ListView>("modifier-list");
            _diagnosticList = RequireElement<ListView>("diagnostic-list");
            _inspector = RequireElement<ScrollView>("inspector-pane");
            _dirtyLabel = RequireElement<Label>("dirty-label");
            _statusLabel = RequireElement<Label>("status-label");
            _previewImages = new[]
            {
                RequireElement<Image>("preview-main"),
                RequireElement<Image>("preview-sub-a"),
                RequireElement<Image>("preview-sub-b"),
            };
            _vertexSelectionOverlay = RequireElement<Image>("selection-overlay");
            _previewYaw = RequireElement<Slider>("preview-yaw");
            _previewPitch = RequireElement<Slider>("preview-pitch");
            _previewAfterToggle = RequireElement<Toggle>("preview-after-toggle");
            _expressionDropdown = RequireElement<DropdownField>("expression-dropdown");
            _expressionWeight = RequireElement<Slider>("expression-weight");
            _previewAvatarLabel = RequireElement<Label>("preview-avatar-label");
            _previewDiagnosticLabel = RequireElement<Label>("preview-diagnostic-label");
            _previewPerformanceLabel = RequireElement<Label>("preview-performance-label");
            _previewResourceLabel = RequireElement<Label>("preview-resource-label");
            _geometrySelectionToggle = RequireElement<Toggle>("geometry-selection-toggle");
            _vertexTargetDropdown = RequireElement<DropdownField>("vertex-target-dropdown");
            _vertexToolDropdown = RequireElement<DropdownField>("vertex-tool-dropdown");
            _vertexVisibilityDropdown =
                RequireElement<DropdownField>("vertex-visibility-dropdown");
            _vertexFalloffDropdown = RequireElement<DropdownField>("vertex-falloff-dropdown");
            _vertexBrushRadius = RequireElement<Slider>("vertex-brush-radius");
            _vertexSoftRadius = RequireElement<Slider>("vertex-soft-radius");
            _vertexSelectionStatus = RequireElement<Label>("vertex-selection-status");
            _vertexSelectionDiagnostic = RequireElement<Label>("vertex-selection-diagnostic");
            _vertexSelectionPerformance = RequireElement<Label>("vertex-selection-performance");
            _previewHelpLabel = RequireElement<Label>("preview-help-label");
        }

        private void ConfigureLists()
        {
            _bandList.makeItem = MakeListLabel;
            _bandList.bindItem = (element, index) =>
            {
                var band = _controller.Document.Bands[index];
                ((Label)element).text = string.IsNullOrWhiteSpace(band.Name)
                    ? band.Id
                    : band.Name + "  (" + band.Id + ")";
            };
            _bandList.selectionChanged += OnBandSelectionChanged;

            _layerList.makeItem = MakeListLabel;
            _layerList.bindItem = (element, index) =>
            {
                var layer = _controller.Document.Layers[index];
                ((Label)element).text = string.IsNullOrWhiteSpace(layer.Name)
                    ? layer.Id
                    : layer.Name + "  (" + layer.Id + ")";
            };
            _layerList.selectionChanged += OnLayerSelectionChanged;

            _modifierList.makeItem = MakeListLabel;
            _modifierList.bindItem = (element, index) =>
            {
                var layerIndex = _controller.Selection.FindLayerIndex(_controller.Document);
                var modifier = _controller.Document.Layers[layerIndex].Modifiers[index];
                ((Label)element).text = GetModifierDisplayName(modifier);
            };
            _modifierList.selectionChanged += OnModifierSelectionChanged;

            _diagnosticList.makeItem = () =>
            {
                var label = new Label();
                label.AddToClassList("vlat-diagnostic-label");
                return label;
            };
            _diagnosticList.bindItem = (element, index) =>
            {
                var diagnostic = _diagnosticItems[index];
                var label = (Label)element;
                label.text = FormatDiagnosticLevel(diagnostic.Level) + "  " + diagnostic.Message +
                    "  (" + diagnostic.Path + ")";
                label.tooltip = "クリックすると該当箇所を選択します。\n" + diagnostic.Path;
                label.EnableInClassList("vlat-diagnostic-error", diagnostic.Level == DiagnosticDisplayLevel.Error);
                label.EnableInClassList("vlat-diagnostic-warning", diagnostic.Level == DiagnosticDisplayLevel.Warning);
                label.EnableInClassList("vlat-diagnostic-info", diagnostic.Level == DiagnosticDisplayLevel.Info);
            };
            _diagnosticList.selectionChanged += OnDiagnosticSelectionChanged;
        }

        private void WireCommands()
        {
            RequireElement<Button>("new-button").clicked += NewDocument;
            RequireElement<Button>("open-button").clicked += OpenDocument;
            RequireElement<Button>("save-button").clicked += SaveDocument;
            RequireElement<Button>("save-as-button").clicked += SaveDocumentAs;
            RequireElement<Button>("undo-button").clicked += Undo.PerformUndo;
            RequireElement<Button>("redo-button").clicked += Undo.PerformRedo;
            RequireElement<Button>("document-button").clicked += () =>
            {
                _controller.SelectDocument();
                RefreshAll();
            };

            RequireElement<Button>("add-band-button").clicked += () =>
            {
                _controller.AddBand();
                RefreshAll();
            };
            RequireElement<Button>("remove-band-button").clicked += RemoveSelectedBand;
            RequireElement<Button>("band-up-button").clicked += () => MoveSelectedBand(-1);
            RequireElement<Button>("band-down-button").clicked += () => MoveSelectedBand(1);

            RequireElement<Button>("add-layer-button").clicked += () =>
            {
                _controller.AddLayer();
                RefreshAll();
            };
            RequireElement<Button>("remove-layer-button").clicked += RemoveSelectedLayer;
            RequireElement<Button>("layer-up-button").clicked += () => MoveSelectedLayer(-1);
            RequireElement<Button>("layer-down-button").clicked += () => MoveSelectedLayer(1);

            RequireElement<Button>("add-modifier-button").clicked += ShowAddModifierMenu;
            RequireElement<Button>("remove-modifier-button").clicked += RemoveSelectedModifier;
            RequireElement<Button>("modifier-up-button").clicked += () => MoveSelectedModifier(-1);
            RequireElement<Button>("modifier-down-button").clicked += () => MoveSelectedModifier(1);

            RequireElement<Button>("select-vrm-button").clicked += SelectVrm;
            RequireElement<Button>("clear-vrm-button").clicked += ClearVrm;
            RequireElement<Button>("reset-main-view-button").clicked += () =>
                ResetPreviewView(VlatPreviewView.Main);
            RequireElement<Button>("reset-sub-a-button").clicked += () =>
                ResetPreviewView(VlatPreviewView.SubA);
            RequireElement<Button>("reset-sub-b-button").clicked += () =>
                ResetPreviewView(VlatPreviewView.SubB);
            RequireElement<Button>("reset-expressions-button").clicked += () =>
            {
                _preview.ResetExpressions();
                RenderAndRefreshPreview();
            };

            _previewYaw.RegisterValueChangedCallback(evt =>
            {
                if (_refreshing)
                {
                    return;
                }

                _preview.SetAngles(VlatPreviewView.Main, evt.newValue, _previewPitch.value);
                RenderAndRefreshPreview();
            });
            _previewPitch.RegisterValueChangedCallback(evt =>
            {
                if (_refreshing)
                {
                    return;
                }

                _preview.SetAngles(VlatPreviewView.Main, _previewYaw.value, evt.newValue);
                RenderAndRefreshPreview();
            });
            _previewAfterToggle.RegisterValueChangedCallback(evt =>
            {
                if (_refreshing)
                {
                    return;
                }

                _preview.SetApplicationEnabled(evt.newValue);
                RenderAndRefreshPreview();
            });
            _expressionDropdown.RegisterValueChangedCallback(_ =>
            {
                if (_refreshing)
                {
                    return;
                }

                _selectedExpressionIndex = _expressionDropdown.index;
                RefreshExpressionWeight();
            });
            _expressionWeight.RegisterValueChangedCallback(evt =>
            {
                if (_refreshing)
                {
                    return;
                }

                _preview.SetExpressionWeight(_selectedExpressionIndex, evt.newValue);
                RenderAndRefreshPreview();
            });

            _geometrySelectionToggle.RegisterValueChangedCallback(evt =>
            {
                if (_refreshing)
                {
                    return;
                }

                _preview.SetGeometrySelectionEnabled(evt.newValue);
                RenderAndRefreshPreview();
            });
            _vertexTargetDropdown.RegisterValueChangedCallback(evt =>
            {
                if (_refreshing)
                {
                    return;
                }

                _preview.SetVertexSelectionTarget(_vertexTargetDropdown.index);
                RenderAndRefreshPreview();
                RefreshVertexSelectionUi(true);
            });
            _vertexToolDropdown.choices = _vertexToolChoices;
            _vertexToolDropdown.RegisterValueChangedCallback(evt =>
            {
                if (_refreshing || _vertexToolDropdown.index < 0)
                {
                    return;
                }

                _preview.SetVertexSelectionTool(
                    (VlatVertexSelectionTool)_vertexToolDropdown.index);
                RefreshVertexSelectionUi();
            });
            _vertexVisibilityDropdown.choices = _vertexVisibilityChoices;
            _vertexVisibilityDropdown.RegisterValueChangedCallback(evt =>
            {
                if (_refreshing || _vertexVisibilityDropdown.index < 0)
                {
                    return;
                }

                _preview.SetVertexVisibilityMode(
                    (VlatVertexVisibilityMode)_vertexVisibilityDropdown.index);
                RefreshVertexSelectionUi();
            });
            _vertexFalloffDropdown.choices = _vertexFalloffChoices;
            _vertexFalloffDropdown.RegisterValueChangedCallback(evt =>
            {
                if (_refreshing || _vertexFalloffDropdown.index < 0)
                {
                    return;
                }

                _preview.SetVertexFalloff(
                    (VlatSoftSelectionFalloff)_vertexFalloffDropdown.index);
                RefreshVertexSelectionUi();
            });
            _vertexBrushRadius.RegisterValueChangedCallback(evt =>
            {
                if (_refreshing)
                {
                    return;
                }

                _preview.SetVertexBrushRadius(evt.newValue);
                RefreshVertexSelectionUi();
            });
            _vertexSoftRadius.RegisterValueChangedCallback(evt =>
            {
                if (_refreshing)
                {
                    return;
                }

                _preview.SetVertexSoftRadius(evt.newValue);
                RefreshVertexSelectionUi();
            });
            RequireElement<Button>("clear-vertex-selection-button").clicked += () =>
            {
                _preview.ClearVertexSelection();
                RefreshVertexSelectionUi();
            };

            for (var index = 0; index < _previewImages.Length; index++)
            {
                var view = (VlatPreviewView)index;
                var image = _previewImages[index];
                image.scaleMode = ScaleMode.ScaleToFit;
                image.RegisterCallback<GeometryChangedEvent>(evt =>
                {
                    _preview.ResizeView(
                        view,
                        evt.newRect.width,
                        evt.newRect.height,
                        EditorGUIUtility.pixelsPerPoint);
                    RenderAndRefreshPreview();
                });
                WirePreviewPointerEvents(image, view, _previewPointerStates[index]);
            }
        }

        private void RefreshAll()
        {
            if (_controller == null || _controller.Document == null || _bandList == null)
            {
                return;
            }

            using var phase = VlatEditorProfiling.Measure(VlatEditorPhase.WindowRefreshAll);
            _refreshing = true;
            try
            {
                RefreshDocumentSections();
                _bandList.itemsSource = _controller.Document.Bands;
                _layerList.itemsSource = _controller.Document.Layers;
                var layerIndex = _controller.Selection.FindLayerIndex(_controller.Document);
                _modifierList.itemsSource = layerIndex < 0
                    ? _emptyModifiers
                    : _controller.Document.Layers[layerIndex].Modifiers;

                RebuildDiagnosticItems();
                _diagnosticList.itemsSource = _diagnosticItems;
                _bandList.Rebuild();
                _layerList.Rebuild();
                _modifierList.Rebuild();
                _diagnosticList.Rebuild();
                RefreshKeyformUi();
                RestoreListSelection();
                BuildInspector();
                UpdateStatus();
                UpdateCommandAvailability();
                _preview.CancelKeyformDrag();
                _preview.SetDocument(_controller.Document);
                _preview.SetKeyformEditingSelection(_controller);
                _preview.SetKeyformGroupSelection(_controller.Selection.FindKeyformGroupIndex(_controller.Document));
                RenderAndRefreshPreview();
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void RestoreListSelection()
        {
            _bandList.selectedIndex = _controller.Selection.FindBandIndex(_controller.Document);
            _layerList.selectedIndex = _controller.Selection.FindLayerIndex(_controller.Document);
            _modifierList.selectedIndex = _controller.Selection.FindModifierIndex(_controller.Document);
        }

        private void UpdateStatus()
        {
            // 表示と同じ集計にする（重複は1件、Keyform v0.2の版注意は情報として数えない）。
            var errorCount = 0;
            var warningCount = 0;
            foreach (var item in _diagnosticItems)
            {
                if (item.Level == DiagnosticDisplayLevel.Error) errorCount++;
                else if (item.Level == DiagnosticDisplayLevel.Warning) warningCount++;
            }

            RefreshStatusPresentation(errorCount, warningCount);
            hasUnsavedChanges = _controller.IsDirty;
        }

        private void UpdateCommandAvailability()
        {
            var keyformActive = _controller.Document.KeyformGroups.Count != 0;
            RequireElement<Button>("add-band-button").SetEnabled(!keyformActive);
            RequireElement<Button>("add-layer-button").SetEnabled(!keyformActive);
            var bandIndex = _controller.Selection.FindBandIndex(_controller.Document);
            var layerIndex = _controller.Selection.FindLayerIndex(_controller.Document);
            var modifierIndex = _controller.Selection.FindModifierIndex(_controller.Document);
            RequireElement<Button>("remove-band-button").SetEnabled(bandIndex >= 0);
            RequireElement<Button>("band-up-button").SetEnabled(bandIndex > 0);
            RequireElement<Button>("band-down-button").SetEnabled(
                bandIndex >= 0 && bandIndex + 1 < _controller.Document.Bands.Count);
            RequireElement<Button>("remove-layer-button").SetEnabled(
                _controller.Selection.Kind == VlatEditorSelectionKind.Layer && layerIndex >= 0);
            RequireElement<Button>("layer-up-button").SetEnabled(
                _controller.Selection.Kind == VlatEditorSelectionKind.Layer && layerIndex > 0);
            RequireElement<Button>("layer-down-button").SetEnabled(
                _controller.Selection.Kind == VlatEditorSelectionKind.Layer &&
                layerIndex >= 0 &&
                layerIndex + 1 < _controller.Document.Layers.Count);
            RequireElement<Button>("add-modifier-button").SetEnabled(layerIndex >= 0);
            RequireElement<Button>("remove-modifier-button").SetEnabled(modifierIndex >= 0);
            RequireElement<Button>("modifier-up-button").SetEnabled(modifierIndex > 0);
            RequireElement<Button>("modifier-down-button").SetEnabled(
                layerIndex >= 0 &&
                modifierIndex >= 0 &&
                modifierIndex + 1 < _controller.Document.Layers[layerIndex].Modifiers.Count);
        }

        private void BuildInspector()
        {
            _inspector.Clear();
            switch (_controller.Selection.Kind)
            {
                case VlatEditorSelectionKind.KeyformGroup:
                case VlatEditorSelectionKind.Keyform:
                    BuildKeyformInspector();
                    break;
                case VlatEditorSelectionKind.Band:
                    BuildBandInspector();
                    break;
                case VlatEditorSelectionKind.Layer:
                    BuildLayerInspector();
                    break;
                case VlatEditorSelectionKind.Modifier:
                    BuildModifierInspector();
                    break;
                default:
                    BuildDocumentInspector();
                    break;
            }
        }

        private void BuildDocumentInspector()
        {
            AddHeading("ドキュメント設定");
            var document = _controller.Document;
            if (document.KeyformGroups.Count == 0 && document.Bands.Count == 0 && document.Layers.Count == 0)
                _inspector.Add(new HelpBox("空のドキュメントです。左の「Yaw Keyform」で「+」を押してグループを作成すると、" +
                    "視点別の形状キーを編集できます。", HelpBoxMessageType.Info));
            AddSection("メタデータ");
            var metadata = document.Metadata;
            AddDelayedText("Version", metadata.Version, value =>
                ApplyChange(() => _controller.SetMetadata(
                    value,
                    metadata.Generator,
                    metadata.AvatarName,
                    metadata.VrmVersion)));
            AddDelayedText("Generator", metadata.Generator, value =>
                ApplyChange(() => _controller.SetMetadata(
                    metadata.Version,
                    value,
                    metadata.AvatarName,
                    metadata.VrmVersion)));
            AddDelayedText("Avatar Name", metadata.AvatarName, value =>
                ApplyChange(() => _controller.SetMetadata(
                    metadata.Version,
                    metadata.Generator,
                    value,
                    metadata.VrmVersion)));
            AddDelayedText("VRM Version", metadata.VrmVersion, value =>
                ApplyChange(() => _controller.SetMetadata(
                    metadata.Version,
                    metadata.Generator,
                    metadata.AvatarName,
                    value)));
            AddSection("Mesh Binding");
            AddNote("Keyformの対象Meshはグループの「対象Mesh」から設定します。texturesは読み取り専用です。");
            AddNote(
                "Mesh bindings: " + metadata.MeshBindings.Count +
                " / Textures: " + _controller.Document.Textures.Count);
            for (var index = 0; index < metadata.MeshBindings.Count; index++)
            {
                var binding = metadata.MeshBindings[index];
                AddNote(
                    "[" + index + "] " + binding.HierarchyPath +
                    " / " + binding.MeshName +
                    " / vertices: " + binding.VertexCount +
                    " / materials: " + binding.Materials.Count);
            }
        }

        private void BuildBandInspector()
        {
            var index = _controller.Selection.FindBandIndex(_controller.Document);
            if (index < 0)
            {
                _controller.SelectDocument();
                BuildDocumentInspector();
                return;
            }

            var band = _controller.Document.Bands[index];
            AddHeading("Angle Band");
            AddDelayedText("ID", band.Id, value => UpdateBand(index, band, id: value));
            AddDelayedText("Name", band.Name, value => UpdateBand(index, band, name: value));
            AddDelayedDouble("Yaw Min", band.YawMin, value => UpdateBand(index, band, yawMin: value));
            AddDelayedDouble("Yaw Max", band.YawMax, value => UpdateBand(index, band, yawMax: value));
            AddDelayedDouble(
                "Pitch Min",
                band.PitchMin,
                value => UpdateBand(index, band, pitchMin: value));
            AddDelayedDouble(
                "Pitch Max",
                band.PitchMax,
                value => UpdateBand(index, band, pitchMax: value));
            AddDelayedDouble(
                "Feather Yaw",
                band.FeatherYaw,
                value => UpdateBand(index, band, featherYaw: value));
            AddDelayedDouble(
                "Feather Pitch",
                band.FeatherPitch,
                value => UpdateBand(index, band, featherPitch: value));
            AddDelayedDouble(
                "Hysteresis",
                band.Hysteresis,
                value => UpdateBand(index, band, hysteresis: value));
        }

        private void BuildLayerInspector()
        {
            var index = _controller.Selection.FindLayerIndex(_controller.Document);
            if (index < 0)
            {
                _controller.SelectDocument();
                BuildDocumentInspector();
                return;
            }

            var layer = _controller.Document.Layers[index];
            AddHeading("Layer");
            AddDelayedText("ID", layer.Id, value => UpdateLayer(index, layer, id: value));
            AddDelayedText("Name", layer.Name, value => UpdateLayer(index, layer, name: value));
            AddDelayedText("Tag", layer.Tag, value => UpdateLayer(index, layer, tag: value));
            AddToggle("Enabled", layer.Enabled, value => UpdateLayer(index, layer, enabled: value));
            AddDelayedInteger(
                "Priority",
                layer.Priority,
                value => UpdateLayer(index, layer, priority: value));
            AddDelayedText("Band ID", layer.BandId, value => UpdateLayer(index, layer, bandId: value));
        }

        private void BuildModifierInspector()
        {
            var layerIndex = _controller.Selection.FindLayerIndex(_controller.Document);
            var modifierIndex = _controller.Selection.FindModifierIndex(_controller.Document);
            if (layerIndex < 0 || modifierIndex < 0)
            {
                _controller.Selection.Reconcile(_controller.Document);
                BuildInspector();
                return;
            }

            var modifier = _controller.Document.Layers[layerIndex].Modifiers[modifierIndex];
            AddHeading(GetModifierDisplayName(modifier));
            var vertex = modifier as VlatAuthoringVertexDeltaModifier;
            if (vertex != null)
            {
                AddDelayedInteger(
                    "Mesh Binding",
                    vertex.MeshBinding,
                    value => ApplyChange(() => _controller.UpdateVertexDeltaModifier(
                        layerIndex,
                        modifierIndex,
                        value,
                        vertex.HasNormals)));
                AddToggle(
                    "Has Normals",
                    vertex.HasNormals,
                    value => ApplyChange(() => _controller.UpdateVertexDeltaModifier(
                        layerIndex,
                        modifierIndex,
                        vertex.MeshBinding,
                        value)));
                AddNote("Vertex deltas: " + vertex.Deltas.Count + "（Phase 2Bでは読み取り専用）");
                return;
            }

            var visibility = modifier as VlatAuthoringVisibilityModifier;
            if (visibility != null)
            {
                AddDelayedInteger(
                    "Mesh Binding",
                    visibility.MeshBinding,
                    value => UpdateVisibility(layerIndex, modifierIndex, visibility, meshBinding: value));
                AddDelayedInteger(
                    "Sub Mesh",
                    visibility.SubMesh,
                    value => UpdateVisibility(layerIndex, modifierIndex, visibility, subMesh: value));
                AddToggle(
                    "Visible In Band",
                    visibility.VisibleInBand,
                    value => UpdateVisibility(layerIndex, modifierIndex, visibility, visible: value));
                return;
            }

            var opacity = modifier as VlatAuthoringOpacityModifier;
            if (opacity != null)
            {
                AddDelayedInteger(
                    "Mesh Binding",
                    opacity.MeshBinding,
                    value => UpdateOpacity(layerIndex, modifierIndex, opacity, meshBinding: value));
                AddDelayedInteger(
                    "Material Slot",
                    opacity.MaterialSlot,
                    value => UpdateOpacity(layerIndex, modifierIndex, opacity, materialSlot: value));
                AddDelayedDouble(
                    "Value",
                    opacity.Value,
                    value => UpdateOpacity(layerIndex, modifierIndex, opacity, opacityValue: value));
                return;
            }

            var material = modifier as VlatAuthoringMaterialPropertyModifier;
            if (material != null)
            {
                BuildMaterialPropertyInspector(layerIndex, modifierIndex, material);
                return;
            }

            var queue = (VlatAuthoringRenderQueueModifier)modifier;
            AddDelayedInteger(
                "Mesh Binding",
                queue.MeshBinding,
                value => UpdateRenderQueue(layerIndex, modifierIndex, queue, meshBinding: value));
            AddDelayedInteger(
                "Material Slot",
                queue.MaterialSlot,
                value => UpdateRenderQueue(layerIndex, modifierIndex, queue, materialSlot: value));
            AddDelayedInteger(
                "Offset",
                queue.Offset,
                value => UpdateRenderQueue(layerIndex, modifierIndex, queue, offset: value));
        }

        private void BuildMaterialPropertyInspector(
            int layerIndex,
            int modifierIndex,
            VlatAuthoringMaterialPropertyModifier modifier)
        {
            AddDelayedInteger(
                "Mesh Binding",
                modifier.MeshBinding,
                value => UpdateMaterial(layerIndex, modifierIndex, modifier, meshBinding: value));
            AddDelayedInteger(
                "Material Slot",
                modifier.MaterialSlot,
                value => UpdateMaterial(layerIndex, modifierIndex, modifier, materialSlot: value));
            AddDelayedText(
                "Property",
                modifier.Property,
                value => UpdateMaterial(layerIndex, modifierIndex, modifier, property: value));
            AddDelayedText(
                "Value Type",
                modifier.ValueType,
                value => UpdateMaterial(layerIndex, modifierIndex, modifier, valueType: value));
            AddToggle(
                "Scalar Value",
                modifier.IsScalar,
                value => UpdateMaterial(layerIndex, modifierIndex, modifier, isScalar: value));
            if (modifier.IsScalar)
            {
                AddDelayedDouble(
                    "Value",
                    modifier.Scalar,
                    value => UpdateMaterial(layerIndex, modifierIndex, modifier, scalar: value));
            }
            else
            {
                AddDelayedText(
                    "Components",
                    JoinComponents(modifier.Components),
                    value =>
                    {
                        List<double> components;
                        if (TryParseComponents(value, out components))
                        {
                            UpdateMaterial(
                                layerIndex,
                                modifierIndex,
                                modifier,
                                components: components);
                        }
                        else
                        {
                            _statusLabel.text = "Componentsはカンマ区切りの有限数で入力してください。";
                        }
                    });
            }

            AddToggle(
                "Interpolate",
                modifier.Interpolate,
                value => UpdateMaterial(layerIndex, modifierIndex, modifier, interpolate: value));
        }

        private void UpdateBand(
            int index,
            VlatAuthoringBand band,
            string id = null,
            string name = null,
            double? yawMin = null,
            double? yawMax = null,
            double? pitchMin = null,
            double? pitchMax = null,
            double? featherYaw = null,
            double? featherPitch = null,
            double? hysteresis = null)
        {
            ApplyChange(() => _controller.UpdateBand(
                index,
                id ?? band.Id,
                name ?? band.Name,
                yawMin ?? band.YawMin,
                yawMax ?? band.YawMax,
                pitchMin ?? band.PitchMin,
                pitchMax ?? band.PitchMax,
                featherYaw ?? band.FeatherYaw,
                featherPitch ?? band.FeatherPitch,
                hysteresis ?? band.Hysteresis));
        }

        private void UpdateLayer(
            int index,
            VlatAuthoringLayer layer,
            string id = null,
            string name = null,
            string tag = null,
            bool? enabled = null,
            int? priority = null,
            string bandId = null)
        {
            ApplyChange(() => _controller.UpdateLayer(
                index,
                id ?? layer.Id,
                name ?? layer.Name,
                tag ?? layer.Tag,
                enabled ?? layer.Enabled,
                priority ?? layer.Priority,
                bandId ?? layer.BandId));
        }

        private void UpdateVisibility(
            int layerIndex,
            int modifierIndex,
            VlatAuthoringVisibilityModifier modifier,
            int? meshBinding = null,
            int? subMesh = null,
            bool? visible = null)
        {
            ApplyChange(() => _controller.UpdateVisibilityModifier(
                layerIndex,
                modifierIndex,
                meshBinding ?? modifier.MeshBinding,
                subMesh ?? modifier.SubMesh,
                visible ?? modifier.VisibleInBand));
        }

        private void UpdateOpacity(
            int layerIndex,
            int modifierIndex,
            VlatAuthoringOpacityModifier modifier,
            int? meshBinding = null,
            int? materialSlot = null,
            double? opacityValue = null)
        {
            ApplyChange(() => _controller.UpdateOpacityModifier(
                layerIndex,
                modifierIndex,
                meshBinding ?? modifier.MeshBinding,
                materialSlot ?? modifier.MaterialSlot,
                opacityValue ?? modifier.Value));
        }

        private void UpdateMaterial(
            int layerIndex,
            int modifierIndex,
            VlatAuthoringMaterialPropertyModifier modifier,
            int? meshBinding = null,
            int? materialSlot = null,
            string property = null,
            string valueType = null,
            bool? isScalar = null,
            double? scalar = null,
            IReadOnlyList<double> components = null,
            bool? interpolate = null)
        {
            ApplyChange(() => _controller.UpdateMaterialPropertyModifier(
                layerIndex,
                modifierIndex,
                meshBinding ?? modifier.MeshBinding,
                materialSlot ?? modifier.MaterialSlot,
                property ?? modifier.Property,
                valueType ?? modifier.ValueType,
                isScalar ?? modifier.IsScalar,
                scalar ?? modifier.Scalar,
                components ?? modifier.Components,
                interpolate ?? modifier.Interpolate));
        }

        private void UpdateRenderQueue(
            int layerIndex,
            int modifierIndex,
            VlatAuthoringRenderQueueModifier modifier,
            int? meshBinding = null,
            int? materialSlot = null,
            int? offset = null)
        {
            ApplyChange(() => _controller.UpdateRenderQueueModifier(
                layerIndex,
                modifierIndex,
                meshBinding ?? modifier.MeshBinding,
                materialSlot ?? modifier.MaterialSlot,
                offset ?? modifier.Offset));
        }

        private void ApplyChange(Action action)
        {
            action();
            RefreshAll();
        }

        private void OnBandSelectionChanged(IEnumerable<object> selected)
        {
            if (_refreshing)
            {
                return;
            }

            foreach (var item in selected)
            {
                _controller.SelectBand(_controller.Document.Bands.IndexOf((VlatAuthoringBand)item));
                RefreshAll();
                return;
            }
        }

        private void OnLayerSelectionChanged(IEnumerable<object> selected)
        {
            if (_refreshing)
            {
                return;
            }

            foreach (var item in selected)
            {
                _controller.SelectLayer(_controller.Document.Layers.IndexOf((VlatAuthoringLayer)item));
                RefreshAll();
                return;
            }
        }

        private void OnModifierSelectionChanged(IEnumerable<object> selected)
        {
            if (_refreshing)
            {
                return;
            }

            var layerIndex = _controller.Selection.FindLayerIndex(_controller.Document);
            if (layerIndex < 0)
            {
                return;
            }

            foreach (var item in selected)
            {
                var modifierIndex = _controller.Document.Layers[layerIndex].Modifiers.IndexOf(
                    (VlatAuthoringModifier)item);
                _controller.SelectModifier(layerIndex, modifierIndex);
                RefreshAll();
                return;
            }
        }

        private void OnDiagnosticSelectionChanged(IEnumerable<object> selected)
        {
            if (_refreshing)
            {
                return;
            }

            foreach (var item in selected)
            {
                // 表示項目から元の診断indexへ戻して選択する（重複統合・表示変換後もずれない）。
                var diagnosticIndex = item is DiagnosticDisplayItem display ? display.SourceIndex : -1;
                if (_controller.SelectDiagnostic(diagnosticIndex))
                {
                    RefreshAll();
                }

                return;
            }
        }

        private void NewDocument()
        {
            VlatCloseDecision decision;
            string savePath;
            if (!ChooseDirtyDecision(out decision, out savePath))
            {
                return;
            }

            _controller.NewDocument(decision, savePath);
            RefreshAll();
        }

        private void OpenDocument()
        {
            var path = OpenDocumentPathProvider != null ? OpenDocumentPathProvider() :
                EditorUtility.OpenFilePanel("Open VLat Document", string.Empty, "json");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            VlatCloseDecision decision;
            string savePath;
            if (!ChooseDirtyDecision(out decision, out savePath))
            {
                return;
            }

            _controller.Open(path, decision, savePath);
            RefreshAll();
        }

        private void SaveDocument()
        {
            if (string.IsNullOrWhiteSpace(_controller.SourcePath))
            {
                SaveDocumentAs();
                return;
            }

            _controller.Save();
            RefreshAll();
        }

        private void SaveDocumentAs()
        {
            var path = ChooseSavePath();
            if (!string.IsNullOrWhiteSpace(path))
            {
                _controller.SaveAs(path);
                RefreshAll();
            }
        }

        private bool ChooseDirtyDecision(out VlatCloseDecision decision, out string savePath)
        {
            decision = VlatCloseDecision.Discard;
            savePath = null;
            if (!_controller.IsDirty)
            {
                return true;
            }

            var result = EditorUtility.DisplayDialogComplex(
                "Unsaved VLat Document",
                "現在の変更を保存しますか？",
                "Save",
                "Cancel",
                "Discard");
            if (result == 1)
            {
                decision = VlatCloseDecision.Cancel;
                return false;
            }

            if (result == 2)
            {
                decision = VlatCloseDecision.Discard;
                return true;
            }

            decision = VlatCloseDecision.Save;
            if (string.IsNullOrWhiteSpace(_controller.SourcePath))
            {
                savePath = ChooseSavePath();
                return !string.IsNullOrWhiteSpace(savePath);
            }

            return true;
        }

        private string ChooseSavePath()
        {
            if (SaveDocumentPathProvider != null) return SaveDocumentPathProvider();
            var path = EditorUtility.SaveFilePanel(
                "Save VLat Document",
                string.Empty,
                "document.vlat.json",
                "json");
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            return path.EndsWith(".vlat.json", StringComparison.OrdinalIgnoreCase)
                ? path
                : path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    ? path.Substring(0, path.Length - ".json".Length) + ".vlat.json"
                    : path + ".vlat.json";
        }

        private void RemoveSelectedBand()
        {
            var index = _controller.Selection.FindBandIndex(_controller.Document);
            if (index >= 0)
            {
                _controller.RemoveBand(index);
                RefreshAll();
            }
        }

        private void MoveSelectedBand(int direction)
        {
            var index = _controller.Selection.FindBandIndex(_controller.Document);
            var target = index + direction;
            if (index >= 0 && target >= 0 && target < _controller.Document.Bands.Count)
            {
                _controller.MoveBand(index, target);
                RefreshAll();
            }
        }

        private void RemoveSelectedLayer()
        {
            if (_controller.Selection.Kind != VlatEditorSelectionKind.Layer)
            {
                return;
            }

            var index = _controller.Selection.FindLayerIndex(_controller.Document);
            if (index >= 0)
            {
                _controller.RemoveLayer(index);
                RefreshAll();
            }
        }

        private void MoveSelectedLayer(int direction)
        {
            if (_controller.Selection.Kind != VlatEditorSelectionKind.Layer)
            {
                return;
            }

            var index = _controller.Selection.FindLayerIndex(_controller.Document);
            var target = index + direction;
            if (index >= 0 && target >= 0 && target < _controller.Document.Layers.Count)
            {
                _controller.MoveLayer(index, target);
                RefreshAll();
            }
        }

        private void ShowAddModifierMenu()
        {
            var layerIndex = _controller.Selection.FindLayerIndex(_controller.Document);
            if (layerIndex < 0)
            {
                return;
            }

            var menu = new GenericMenu();
            AddModifierMenuItem(menu, "Vertex Delta", layerIndex, VlatAuthoringModifierKind.VertexDelta);
            AddModifierMenuItem(menu, "Visibility", layerIndex, VlatAuthoringModifierKind.Visibility);
            AddModifierMenuItem(menu, "Opacity", layerIndex, VlatAuthoringModifierKind.Opacity);
            AddModifierMenuItem(
                menu,
                "Material Property",
                layerIndex,
                VlatAuthoringModifierKind.MaterialProperty);
            AddModifierMenuItem(menu, "Render Queue", layerIndex, VlatAuthoringModifierKind.RenderQueue);
            menu.ShowAsContext();
        }

        private void AddModifierMenuItem(
            GenericMenu menu,
            string label,
            int layerIndex,
            VlatAuthoringModifierKind kind)
        {
            menu.AddItem(new GUIContent(label), false, () =>
            {
                _controller.AddModifier(layerIndex, kind);
                RefreshAll();
            });
        }

        private void RemoveSelectedModifier()
        {
            var layerIndex = _controller.Selection.FindLayerIndex(_controller.Document);
            var modifierIndex = _controller.Selection.FindModifierIndex(_controller.Document);
            if (layerIndex >= 0 && modifierIndex >= 0)
            {
                _controller.RemoveModifier(layerIndex, modifierIndex);
                RefreshAll();
            }
        }

        private void MoveSelectedModifier(int direction)
        {
            var layerIndex = _controller.Selection.FindLayerIndex(_controller.Document);
            var modifierIndex = _controller.Selection.FindModifierIndex(_controller.Document);
            var target = modifierIndex + direction;
            if (layerIndex >= 0 &&
                modifierIndex >= 0 &&
                target >= 0 &&
                target < _controller.Document.Layers[layerIndex].Modifiers.Count)
            {
                _controller.MoveModifier(layerIndex, modifierIndex, target);
                RefreshAll();
            }
        }

        private void OnUndoRedo()
        {
            if (_controller != null && _controller.Document != null)
            {
                _controller.RefreshAfterUndoRedo();
                RefreshAll();
            }
        }

        private void OnBeforeAssemblyReload()
        {
            _assemblyReloading = true;
            CancelWindowDrag();
            _preview?.Dispose();
            _preview = null;
            if (_controller != null && _controller.Document != null)
            {
                _controller.PrepareForDomainReload();
            }
        }

        private async void SelectVrm()
        {
            var path = EditorUtility.OpenFilePanel("Open VRM 1.0 Preview", string.Empty, "vrm");
            if (string.IsNullOrWhiteSpace(path) || _preview == null)
            {
                return;
            }

            RefreshPreviewUi();
            var loaded = await _preview.LoadAvatarAsync(path);
            if (this == null || _preview == null)
            {
                return;
            }

            _selectedExpressionIndex = loaded && _preview.ExpressionNames.Count > 0 ? 0 : -1;
            RenderAndRefreshPreview();
        }

        private void ClearVrm()
        {
            _preview.ClearAvatar();
            _selectedExpressionIndex = -1;
            RefreshPreviewUi();
        }

        private void ResetPreviewView(VlatPreviewView view)
        {
            _preview.ResetView(view);
            RenderAndRefreshPreview();
        }

        private void WirePreviewPointerEvents(
            Image image,
            VlatPreviewView view,
            PreviewPointerState state)
        {
            image.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 && evt.button != 2)
                {
                    return;
                }

                if (_preview.IsDragging) CancelWindowDrag();
                if (_preview.GeometrySelectionEnabled &&
                    view == VlatPreviewView.Main &&
                    evt.button == 0 &&
                    !evt.altKey)
                {
                    if (!TryMapMainPreviewPoint(evt.position, out var screenPosition))
                    {
                        return;
                    }

                    state.LastSelectionPosition = screenPosition;
                    if (_preview.KeyformDragEnabled)
                    {
                        state.Action = PreviewPointerAction.Deform;
                        if (!_preview.BeginKeyformDrag(screenPosition)) { RefreshPreviewUi(); return; }
                        RenderAndRefreshPreview();
                    }
                    else
                    {
                        state.Action = PreviewPointerAction.Selection;
                        _preview.BeginVertexSelection(screenPosition, GetVertexSelectionOperation(evt));
                        RefreshVertexSelectionOverlay();
                    }
                }
                else
                {
                    state.Action = evt.button == 0
                        ? PreviewPointerAction.Orbit
                        : PreviewPointerAction.Pan;
                }

                image.focusable = true;
                image.Focus();
                state.Dragging = true;
                state.PointerId = evt.pointerId;
                state.Button = evt.button;
                state.LastPosition = new Vector2(evt.position.x, evt.position.y);
                image.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            image.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!state.Dragging || state.PointerId != evt.pointerId)
                {
                    return;
                }

                var position = new Vector2(evt.position.x, evt.position.y);
                var delta = position - state.LastPosition;
                state.LastPosition = position;
                if (state.Action == PreviewPointerAction.Selection)
                {
                    if (TryMapMainPreviewPoint(evt.position, out var screenPosition))
                    {
                        state.LastSelectionPosition = screenPosition;
                        _preview.UpdateVertexSelection(screenPosition);
                        RefreshVertexSelectionOverlay();
                    }
                }
                else if (state.Action == PreviewPointerAction.Deform)
                {
                    if (TryMapMainPreviewPoint(evt.position, out var screenPosition, true))
                        _preview.UpdateKeyformDrag(screenPosition);
                    else _preview.CancelKeyformDrag("Pointer座標を変換できません。");
                    _preview.RenderIfNeeded();
                    // 固定gesture中はtextureの内容だけが変わる。対象/表情一覧と診断文字列を
                    // MouseMoveごとに再構築しない。開始/確定/中止境界では従来どおり全UIを更新する。
                    if (_preview.IsDragging) image.MarkDirtyRepaint();
                    else RefreshPreviewUi();
                }
                else if (state.Action == PreviewPointerAction.Orbit)
                {
                    _preview.Orbit(view, delta);
                    RenderAndRefreshPreview();
                }
                else
                {
                    _preview.Pan(view, delta);
                    RenderAndRefreshPreview();
                }
                evt.StopPropagation();
            });
            image.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!state.Dragging || state.PointerId != evt.pointerId || state.Button != evt.button)
                {
                    return;
                }

                state.Dragging = false;
                if (state.Action == PreviewPointerAction.Selection)
                {
                    if (TryMapMainPreviewPoint(evt.position, out var screenPosition))
                    {
                        state.LastSelectionPosition = screenPosition;
                    }

                    _preview.EndVertexSelection(state.LastSelectionPosition);
                    RefreshVertexSelectionUi();
                }

                if (state.Action == PreviewPointerAction.Deform)
                {
                    if (TryMapMainPreviewPoint(evt.position, out var screenPosition, true))
                        _preview.CommitKeyformDrag(screenPosition);
                    else _preview.CancelKeyformDrag("Pointer座標を変換できません。");
                    RefreshAll();
                }
                state.Action = PreviewPointerAction.None;
                if (image.HasPointerCapture(evt.pointerId))
                {
                    image.ReleasePointer(evt.pointerId);
                }

                evt.StopPropagation();
            });
            image.RegisterCallback<PointerCaptureOutEvent>(evt =>
            {
                if (!state.Dragging)
                {
                    return;
                }

                if (state.Action == PreviewPointerAction.Selection)
                {
                    _preview.CancelVertexSelection();
                    RefreshVertexSelectionUi();
                }

                if (state.Action == PreviewPointerAction.Deform)
                {
                    _preview.CancelKeyformDrag("Pointer captureが失われました。");
                    RenderAndRefreshPreview();
                }
                state.Dragging = false;
                state.Action = PreviewPointerAction.None;
            });
            image.RegisterCallback<WheelEvent>(evt =>
            {
                _preview.Zoom(view, evt.delta.y);
                RenderAndRefreshPreview();
                evt.StopPropagation();
            });
        }

        private void RenderAndRefreshPreview()
        {
            if (_preview == null)
            {
                return;
            }

            _preview.RenderIfNeeded();
            RefreshPreviewUi();
        }

        private void RefreshPreviewUi()
        {
            if (_preview == null || _previewImages == null)
            {
                return;
            }

            for (var index = 0; index < _previewImages.Length; index++)
            {
                _previewImages[index].image = _preview.GetTexture((VlatPreviewView)index);
            }

            var main = _preview.GetViewState(VlatPreviewView.Main);
            _previewYaw.SetValueWithoutNotify(main.Yaw);
            _previewPitch.SetValueWithoutNotify(main.Pitch);
            _previewAfterToggle.SetValueWithoutNotify(_preview.ApplicationEnabled);
            _previewAvatarLabel.text = _preview.HasAvatar
                ? Path.GetFileName(_preview.AvatarPath)
                : _preview.IsLoading ? "読込中…" : "VRM未読込";
            _previewAvatarLabel.tooltip = _preview.HasAvatar ? _preview.AvatarPath : string.Empty;
            _previewDiagnosticLabel.text = _preview.DiagnosticSummary;
            _previewPerformanceLabel.text = _preview.PerformanceSummary;
            _previewResourceLabel.text = _preview.ResourceSummary;

            _expressionChoices.Clear();
            for (var index = 0; index < _preview.ExpressionNames.Count; index++)
            {
                _expressionChoices.Add(_preview.ExpressionNames[index]);
            }

            _expressionDropdown.choices = _expressionChoices;
            if (_selectedExpressionIndex >= _expressionChoices.Count)
            {
                _selectedExpressionIndex = _expressionChoices.Count - 1;
            }

            _expressionDropdown.index = _selectedExpressionIndex;
            RefreshExpressionWeight();
            _expressionDropdown.SetEnabled(_expressionChoices.Count > 0);
            _expressionWeight.SetEnabled(
                _selectedExpressionIndex >= 0 && !_preview.GeometrySelectionEnabled);
            _expressionDropdown.SetEnabled(
                _expressionChoices.Count > 0 && !_preview.GeometrySelectionEnabled);
            RequireElement<Button>("reset-expressions-button").SetEnabled(
                !_preview.GeometrySelectionEnabled && _expressionChoices.Count > 0);
            _previewAfterToggle.SetEnabled(!_preview.GeometrySelectionEnabled);
            RequireElement<Button>("clear-vrm-button").SetEnabled(_preview.HasAvatar || _preview.IsLoading);
            RequireElement<Button>("select-vrm-button").SetEnabled(!_preview.IsLoading);
            // VRM読込/clearはInspectorを再構築しないので、既存のキーボタンの状態も同期する。
            for (var view = 0; view < 3; view++)
                _inspector?.Q<Button>("keyform-camera-" + (VlatPreviewView)view)?.SetEnabled(_preview.HasAvatar);
            RefreshVertexSelectionUi(true);
        }

        private void RefreshVertexSelectionUi(bool refreshTargets = false)
        {
            if (_preview == null || _vertexSelectionOverlay == null)
            {
                return;
            }

            _vertexSelectionOverlay.image = _preview.KeyformDragEnabled ? null : _preview.GetVertexSelectionOverlay();
            _geometrySelectionToggle.SetValueWithoutNotify(_preview.GeometrySelectionEnabled);
            if (refreshTargets || _vertexTargetChoices.Count != _preview.VertexTargets.Count)
            {
                _vertexTargetChoices.Clear();
                for (var index = 0; index < _preview.VertexTargets.Count; index++)
                {
                    _vertexTargetChoices.Add(_preview.VertexTargets[index].DisplayName);
                }

                _vertexTargetDropdown.choices = _vertexTargetChoices;
            }

            var targetIndex = _preview.VertexTargetIndex;
            _vertexTargetDropdown.SetValueWithoutNotify(
                targetIndex >= 0 && targetIndex < _vertexTargetChoices.Count
                    ? _vertexTargetChoices[targetIndex]
                    : null);
            _vertexToolDropdown.SetValueWithoutNotify(
                _vertexToolChoices[(int)_preview.VertexSelectionTool]);
            _vertexVisibilityDropdown.SetValueWithoutNotify(
                _vertexVisibilityChoices[(int)_preview.VertexVisibilityMode]);
            _vertexFalloffDropdown.SetValueWithoutNotify(
                _vertexFalloffChoices[(int)_preview.VertexFalloff]);
            _vertexBrushRadius.SetValueWithoutNotify(_preview.VertexBrushRadius);
            _vertexSoftRadius.SetValueWithoutNotify(_preview.VertexSoftRadius);
            _vertexSelectionStatus.text = _preview.VertexSelectionStatus;
            _vertexSelectionDiagnostic.text = _preview.VertexSelectionDiagnostic;
            _vertexSelectionPerformance.text = _preview.VertexSelectionPerformance;
            var controlsEnabled = _preview.GeometrySelectionEnabled && _preview.HasAvatar;
            _vertexTargetDropdown.SetEnabled(controlsEnabled && _vertexTargetChoices.Count > 0);
            _vertexToolDropdown.SetEnabled(controlsEnabled);
            _vertexVisibilityDropdown.SetEnabled(controlsEnabled);
            _vertexFalloffDropdown.SetEnabled(controlsEnabled);
            _vertexBrushRadius.SetEnabled(controlsEnabled);
            _vertexSoftRadius.SetEnabled(controlsEnabled);
            RequireElement<Button>("clear-vertex-selection-button").SetEnabled(controlsEnabled);
            _geometrySelectionToggle.SetEnabled(_preview.HasAvatar);
            _previewHelpLabel.text = _preview.GeometrySelectionEnabled
                ? "左: 選択 / Shift: 追加 / Ctrl・Cmd: 除外 / Alt+左: 回転 / 中: 移動 / ホイール: ズーム"
                : "左ドラッグ: 回転 / 中ドラッグ: 移動 / ホイール: ズーム";
            RefreshKeyformDragUi();
            RefreshPreviewMessages();
            RefreshWorkflowGuide();
        }

        private void RefreshVertexSelectionOverlay()
        {
            if (_preview != null && _vertexSelectionOverlay != null)
            {
                _vertexSelectionOverlay.image = _preview.KeyformDragEnabled ? null : _preview.GetVertexSelectionOverlay();
            }
        }

        private bool TryMapMainPreviewPoint(Vector2 panelPosition, out Vector2 screenPosition, bool allowOutside = false)
        {
            screenPosition = default;
            var texture = _preview.GetTexture(VlatPreviewView.Main);
            if (texture == null)
            {
                return false;
            }

            var image = _previewImages[(int)VlatPreviewView.Main];
            var local = image.WorldToLocal(panelPosition);
            var content = image.contentRect;
            if (content.width <= 0f || content.height <= 0f)
            {
                return false;
            }

            var scale = Mathf.Min(content.width / texture.width, content.height / texture.height);
            var renderedWidth = texture.width * scale;
            var renderedHeight = texture.height * scale;
            var left = content.x + ((content.width - renderedWidth) * 0.5f);
            var top = content.y + ((content.height - renderedHeight) * 0.5f);
            if (!allowOutside && (local.x < left || local.x > left + renderedWidth ||
                local.y < top || local.y > top + renderedHeight))
            {
                return false;
            }

            screenPosition = new Vector2(
                (local.x - left) / scale,
                texture.height - ((local.y - top) / scale));
            return true;
        }

        private static VlatVertexSelectionOperation GetVertexSelectionOperation(
            PointerDownEvent evt)
        {
            if (evt.ctrlKey || evt.commandKey)
            {
                return VlatVertexSelectionOperation.Subtract;
            }

            return evt.shiftKey
                ? VlatVertexSelectionOperation.Add
                : VlatVertexSelectionOperation.Replace;
        }

        private void RefreshExpressionWeight()
        {
            if (_expressionWeight == null || _preview == null)
            {
                return;
            }

            _expressionWeight.SetValueWithoutNotify(
                _preview.GetExpressionWeight(_selectedExpressionIndex));
        }

        private void AddHeading(string text)
        {
            var label = new Label(text);
            label.AddToClassList("vlat-inspector-heading");
            _inspector.Add(label);
        }

        private void AddNote(string text)
        {
            var label = new Label(text);
            label.AddToClassList("vlat-inspector-note");
            _inspector.Add(label);
        }

        private void AddDelayedText(string label, string value, Action<string> changed)
        {
            var field = new TextField(label)
            {
                isDelayed = true,
                value = value ?? string.Empty,
            };
            field.RegisterValueChangedCallback(evt => changed(evt.newValue));
            _inspector.Add(field);
        }

        private void AddDelayedInteger(string label, int value, Action<int> changed)
        {
            var field = new IntegerField(label)
            {
                isDelayed = true,
                value = value,
            };
            field.RegisterValueChangedCallback(evt => changed(evt.newValue));
            _inspector.Add(field);
        }

        private void AddDelayedDouble(string label, double value, Action<double> changed)
        {
            var field = new DoubleField(label)
            {
                isDelayed = true,
                value = value,
            };
            field.RegisterValueChangedCallback(evt => changed(evt.newValue));
            _inspector.Add(field);
        }

        private void AddToggle(string label, bool value, Action<bool> changed)
        {
            var field = new Toggle(label) { value = value };
            field.RegisterValueChangedCallback(evt => changed(evt.newValue));
            _inspector.Add(field);
        }

        private static VisualElement MakeListLabel()
        {
            var label = new Label();
            label.AddToClassList("vlat-list-label");
            return label;
        }

        private static string GetModifierDisplayName(VlatAuthoringModifier modifier)
        {
            if (modifier is VlatAuthoringVertexDeltaModifier)
            {
                return "vertexDelta";
            }

            if (modifier is VlatAuthoringVisibilityModifier)
            {
                return "visibility";
            }

            if (modifier is VlatAuthoringOpacityModifier)
            {
                return "opacity";
            }

            if (modifier is VlatAuthoringMaterialPropertyModifier)
            {
                return "materialProperty";
            }

            return "renderQueue";
        }

        private static string JoinComponents(IReadOnlyList<double> components)
        {
            if (components == null || components.Count == 0)
            {
                return string.Empty;
            }

            var values = new string[components.Count];
            for (var index = 0; index < components.Count; index++)
            {
                values[index] = components[index].ToString("R", CultureInfo.InvariantCulture);
            }

            return string.Join(", ", values);
        }

        private static bool TryParseComponents(string text, out List<double> components)
        {
            components = new List<double>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            var parts = text.Split(',');
            for (var index = 0; index < parts.Length; index++)
            {
                double value;
                if (!double.TryParse(
                        parts[index].Trim(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out value) ||
                    double.IsNaN(value) ||
                    double.IsInfinity(value))
                {
                    components.Clear();
                    return false;
                }

                components.Add(value);
            }

            return true;
        }

        private TElement RequireElement<TElement>(string name)
            where TElement : VisualElement
        {
            var element = rootVisualElement.Q<TElement>(name);
            if (element == null)
            {
                throw new InvalidOperationException("UI要素がありません: " + name);
            }

            return element;
        }

        private sealed class PreviewPointerState
        {
            internal bool Dragging { get; set; }
            internal int PointerId { get; set; }
            internal int Button { get; set; }
            internal Vector2 LastPosition { get; set; }
            internal Vector2 LastSelectionPosition { get; set; }
            internal PreviewPointerAction Action { get; set; }
        }

        private enum PreviewPointerAction
        {
            None,
            Orbit,
            Pan,
            Selection,
            Deform,
        }
    }
}
