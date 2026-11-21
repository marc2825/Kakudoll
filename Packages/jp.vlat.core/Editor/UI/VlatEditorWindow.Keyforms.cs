using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VLat.Core.Validation;

namespace VLat.Editor.Authoring
{
    internal sealed partial class VlatEditorWindow
    {
        private ListView _keyformGroupList;
        private ListView _keyformList;
        private readonly List<VlatAuthoringKeyform> _emptyKeys = new List<VlatAuthoringKeyform>();
        [SerializeField] private GameObject _keyformAvatarRoot;
        [SerializeField] private Renderer _keyformRenderer;
        private int _keyformShapeIndex = -1;

        // OSのfile pickerだけを差し替え、試験も実際のtoolbar/command/persistence経路を通す。
        internal Func<string> OpenDocumentPathProvider { get; set; }
        internal Func<string> SaveDocumentPathProvider { get; set; }
        internal VlatEditorDocumentController DocumentController => _controller;
        internal VlatPreviewController PreviewController => _preview;

        private void ConfigureKeyformUi()
        {
            _keyformGroupList = RequireElement<ListView>("keyform-group-list");
            _keyformList = RequireElement<ListView>("keyform-list");
            _keyformGroupList.makeItem = MakeListLabel;
            _keyformGroupList.bindItem = (element, index) =>
            {
                var group = _controller.Document.KeyformGroups[index];
                ((Label)element).text = group.Id + "  (" + group.Keys.Count + " キー)";
            };
            _keyformList.makeItem = MakeListLabel;
            _keyformList.bindItem = (element, index) =>
            {
                var g = _controller.Selection.FindKeyformGroupIndex(_controller.Document);
                var key = _controller.Document.KeyformGroups[g].Keys[index];
                ((Label)element).text = key.Yaw.ToString("G") + "°   " +
                    (key.Deltas.Count == 0 ? "中立" : key.Deltas.Count + " 頂点");
            };
            _keyformGroupList.selectionChanged += _ =>
            {
                if (_refreshing || _keyformGroupList.selectedIndex < 0) return;
                _controller.SelectKeyformGroup(_keyformGroupList.selectedIndex);
                RefreshAll();
            };
            _keyformList.selectionChanged += _ =>
            {
                if (_refreshing || _keyformList.selectedIndex < 0) return;
                var g = _controller.Selection.FindKeyformGroupIndex(_controller.Document);
                if (g < 0) return;
                _controller.SelectKeyform(g, _keyformList.selectedIndex);
                RefreshAll();
            };
            RequireElement<Button>("add-keyform-group-button").clicked += () =>
                ApplyChange(() => _controller.AddKeyformGroup());
            RequireElement<Button>("remove-keyform-group-button").clicked += () =>
                ApplyChange(() => _controller.RemoveKeyformGroup(_controller.Selection.FindKeyformGroupIndex(_controller.Document)));
            RequireElement<Button>("add-keyform-button").clicked += () =>
            {
                var g = _controller.Selection.FindKeyformGroupIndex(_controller.Document);
                ApplyChange(() => _controller.AddKeyform(g, _controller.SuggestKeyformYaw(g)));
            };
            RequireElement<Button>("remove-keyform-button").clicked += () =>
                ApplyChange(() => _controller.RemoveKeyform(
                    _controller.Selection.FindKeyformGroupIndex(_controller.Document),
                    _controller.Selection.FindKeyformIndex(_controller.Document)));
        }

        private void RefreshKeyformUi()
        {
            var document = _controller.Document;
            var g = _controller.Selection.FindKeyformGroupIndex(document);
            var k = _controller.Selection.FindKeyformIndex(document);
            _keyformGroupList.itemsSource = document.KeyformGroups;
            _keyformList.itemsSource = g < 0 ? _emptyKeys : document.KeyformGroups[g].Keys;
            _keyformGroupList.Rebuild();
            _keyformList.Rebuild();
            _keyformGroupList.SetSelectionWithoutNotify(g < 0 ? Array.Empty<int>() : new[] { g });
            _keyformList.SetSelectionWithoutNotify(k < 0 ? Array.Empty<int>() : new[] { k });
            RequireElement<Button>("add-keyform-group-button").SetEnabled(
                document.KeyformGroups.Count == 0 && document.Bands.Count == 0 &&
                document.Layers.Count == 0 && document.Textures.Count == 0);
            RequireElement<Button>("remove-keyform-group-button").SetEnabled(g >= 0);
            RequireElement<Button>("add-keyform-button").SetEnabled(
                g >= 0 && document.KeyformGroups[g].Keys.Count < VlatValidator.MaximumKeyformKeys);
            RequireElement<Button>("remove-keyform-button").SetEnabled(
                k >= 0 && document.KeyformGroups[g].Keys.Count > 1);
        }

        private void BuildKeyformInspector()
        {
            var g = _controller.Selection.FindKeyformGroupIndex(_controller.Document);
            if (g < 0) { BuildDocumentInspector(); return; }
            var group = _controller.Document.KeyformGroups[g];
            var k = _controller.Selection.FindKeyformIndex(_controller.Document);
            AddHeading(k < 0 ? "Yaw Keyformグループ: " + group.Id : "Yawキー " + group.Keys[k].Yaw.ToString("G") + "°");

            // 1. 選択キー: 最も頻繁に触る項目を先頭に置く。
            if (k >= 0)
            {
                AddSection("選択キー");
                AddKeyformDouble("Yaw (度)", "keyform-yaw", group.Keys[k].Yaw,
                    value => _controller.UpdateKeyformYaw(g, k, value));
                var state = new Label(group.Keys[k].Deltas.Count == 0 ? "中立キー（位置差分なし）" :
                    "位置差分: " + group.Keys[k].Deltas.Count + " 頂点") { name = "keyform-edit-state" };
                state.AddToClassList("vlat-inspector-note");
                _inspector.Add(state);
                var cameraRow = new VisualElement();
                cameraRow.AddToClassList("vlat-inspector-row");
                cameraRow.Add(new Label("視点をこのキー角度へ:"));
                for (var viewIndex = 0; viewIndex < 3; viewIndex++)
                {
                    var view = (VlatPreviewView)viewIndex;
                    var move = new Button(() =>
                    {
                        _preview.MoveCameraToKeyform(view, k);
                        RenderAndRefreshPreview();
                    })
                    {
                        name = "keyform-camera-" + view,
                        text = view == VlatPreviewView.Main ? "Main" : view == VlatPreviewView.SubA ? "Sub A" : "Sub B",
                        tooltip = view + "ビューのYawをこのキー角度へ合わせます（VRM読込後に使用できます）。",
                    };
                    move.SetEnabled(_preview.HasAvatar);
                    cameraRow.Add(move);
                }
                _inspector.Add(cameraRow);
            }

            // 2. 対象Mesh: 未設定だと保存も編集もできないため、状態を最初に示す。
            AddSection("対象Mesh");
            if (group.MeshBinding >= 0 && group.MeshBinding < _controller.Document.Metadata.MeshBindings.Count)
            {
                var binding = _controller.Document.Metadata.MeshBindings[group.MeshBinding];
                var bound = new Label("設定済み: " + binding.HierarchyPath + " / " + binding.MeshName +
                    " / " + binding.VertexCount + " 頂点")
                {
                    tooltip = "Binding [" + group.MeshBinding + "]\n" + binding.PositionHash + "\n" + group.TopologyHash,
                };
                bound.AddToClassList("vlat-inspector-note");
                _inspector.Add(bound);
            }
            else _inspector.Add(new HelpBox("対象が未設定のため保存できません。下の欄で指定してください。", HelpBoxMessageType.Error));
            AddNote("シーン上のアバターRootとRendererを指定して「対象に設定」を押します。" +
                "位置差分のあるグループは別のMeshへ転用できません。");
            var root = new ObjectField("Avatar Root")
            {
                name = "keyform-avatar-root", objectType = typeof(GameObject), allowSceneObjects = true, value = _keyformAvatarRoot,
            };
            root.RegisterValueChangedCallback(evt => { _keyformAvatarRoot = evt.newValue as GameObject; RefreshAll(); });
            _inspector.Add(root);
            var target = new ObjectField("Renderer")
            {
                name = "keyform-renderer", objectType = typeof(Renderer), allowSceneObjects = true, value = _keyformRenderer,
            };
            target.RegisterValueChangedCallback(evt =>
            {
                _keyformRenderer = evt.newValue as Renderer;
                _keyformShapeIndex = -1;
                RefreshAll();
            });
            _inspector.Add(target);
            var mesh = VlatKeyformBindingUtility.GetSourceMesh(_keyformRenderer);
            var meshField = new ObjectField("Source Mesh")
            {
                name = "keyform-mesh", objectType = typeof(Mesh), value = mesh,
            };
            meshField.SetEnabled(false);
            _inspector.Add(meshField);
            var bind = new Button(() => ApplyChange(() => _controller.BindKeyformTarget(g, _keyformAvatarRoot, _keyformRenderer)))
            { name = "bind-keyform-target-button", text = "対象に設定" };
            bind.AddToClassList("vlat-inspector-primary");
            _inspector.Add(bind);

            // 3. BlendShape取込: 既存の表情/補正BlendShapeをキーの出発点にする。
            if (k >= 0)
            {
                AddSection("BlendShapeから位置差分を取込");
                var choices = new List<string> { "中立（差分をクリア）" };
                if (mesh != null)
                    for (var shape = 0; shape < mesh.blendShapeCount; shape++)
                        choices.Add("[" + shape + "] " + mesh.GetBlendShapeName(shape));
                if (_keyformShapeIndex + 1 >= choices.Count) _keyformShapeIndex = -1;
                var shapes = new DropdownField("BlendShape", choices, _keyformShapeIndex + 1) { name = "keyform-shape" };
                shapes.RegisterValueChangedCallback(_ => _keyformShapeIndex = shapes.index - 1);
                _inspector.Add(shapes);
                _inspector.Add(new Button(() => ApplyChange(() => _controller.ImportKeyformPositions(
                    g, k, _keyformAvatarRoot, _keyformRenderer, _keyformShapeIndex)))
                { name = "import-keyform-button", text = "位置差分を取り込む（選択キーを置換）" });
                AddNote("単一100%フレームの位置差分を取り込みます。法線・接線・表情設定は対象外です。");
            }

            // 4. グループ設定: 変更頻度が低い項目は末尾へ。
            AddSection("グループ設定");
            AddKeyformText("Group ID", "keyform-group-id", group.Id,
                value => _controller.UpdateKeyformGroup(g, value, group.SupportStart, group.SupportEnd));
            AddKeyformDouble("支持区間 開始", "keyform-support-start", group.SupportStart,
                value => _controller.UpdateKeyformGroup(g, group.Id, value, group.SupportEnd));
            AddKeyformDouble("支持区間 終了", "keyform-support-end", group.SupportEnd,
                value => _controller.UpdateKeyformGroup(g, group.Id, group.SupportStart, value));
            AddNote("キーのYawは支持区間の内側に置きます。Yaw変更時は昇順に並べ替えます。" +
                "補間: yaw-smoothstep / 差分: bind-mesh-local（位置のみ）。");
            var previewNote = new HelpBox("Previewは読込済みVRMのBindingを検証し、各ビューのHead基準Yawで補間します。" +
                "「補正を表示」をオフにすると表情を保ったまま補正だけを外します。Editor CPU PreviewはGPU/VRChat上の見た目と等価とは限りません。",
                HelpBoxMessageType.Info);
            previewNote.AddToClassList("vlat-keyform-warning");
            _inspector.Add(previewNote);
        }

        private void AddKeyformDouble(string label, string name, double value, Action<double> changed)
        {
            var field = new DoubleField(label) { name = name, isDelayed = true, value = value };
            field.RegisterValueChangedCallback(evt => ApplyChange(() => changed(evt.newValue)));
            _inspector.Add(field);
        }

        private void AddKeyformText(string label, string name, string value, Action<string> changed)
        {
            var field = new TextField(label) { name = name, isDelayed = true, value = value };
            field.RegisterValueChangedCallback(evt => ApplyChange(() => changed(evt.newValue)));
            _inspector.Add(field);
        }
    }
}
