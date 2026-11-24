using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VLat.Core.Validation;

namespace VLat.Editor.Authoring
{
    // Keyform（角度ごとのキー）の画面。右の列=選んでいるキーと直すパーツ、詳細=効く範囲とシーンでの指定。
    internal sealed partial class VlatEditorWindow
    {
        private readonly List<double> _railKeys = new List<double>();
        private readonly List<bool> _railShapes = new List<bool>();
        private readonly Dictionary<string, bool> _foldoutStates = new Dictionary<string, bool>();
        [SerializeField] private GameObject _keyformAvatarRoot;
        [SerializeField] private Renderer _keyformRenderer;
        private int _keyformShapeIndex = -1;
        private int _bindPreviewPartTarget = -1;
        private Button _addKeyButton;
        private Button _removeKeyButton;
        private VlatAngleRail _rail;

        // OSのfile pickerだけを差し替え、試験も実際のtoolbar/command/persistence経路を通す。
        internal Func<string> OpenDocumentPathProvider { get; set; }
        internal Func<string> SaveDocumentPathProvider { get; set; }
        internal VlatEditorDocumentController DocumentController => _controller;
        internal VlatPreviewController PreviewController => _preview;

        private void ConfigureKeyformUi()
        {
            _rail = new VlatAngleRail
            {
                name = "angle-rail",
                tooltip = "ひし形がキー（塗りつぶしは形を変えたキー）、オレンジの帯が補正の効く範囲、白い印が編集ビューの角度、" +
                    "A・B が比較ビューの角度です。何もない所を引くと編集ビューが回り、ひし形を横に引くとキーの角度が変わり、A・B を引くと比較ビューが回ります。",
            };
            RequireElement<VisualElement>("angle-rail-host").Add(_rail);
            _rail.MainYawChanged = yaw =>
            {
                if (_preview == null) return;
                _preview.SetAngles(VlatPreviewView.Main, yaw, _previewPitch.value);
                RenderAndRefreshPreview();
            };
            // 引き終えた角度がキーと重なったら、そのキーを選ぶ（引いている途中は選択を変えない）。
            _rail.ScrubEnded = key =>
            {
                if (key >= 0) SelectKey(key, false);
            };
            _rail.KeyClicked = key => SelectKey(key, true);
            _rail.KeyMoved = (key, yaw) =>
            {
                var g = DisplayGroupIndex();
                if (g < 0) return;
                ApplyChange(() => _controller.UpdateKeyformYaw(g, key, yaw));
                var selected = _controller.Selection.FindKeyformIndex(_controller.Document);
                if (selected >= 0) MoveMainCameraToKey(selected);
            };
            _rail.CompareYawChanged = (view, yaw) =>
            {
                if (_preview == null) return;
                _preview.SetAngles(view, yaw, _preview.GetViewState(view).Pitch);
                RenderAndRefreshPreview();
            };

            _addKeyButton = RequireElement<Button>("add-keyform-button");
            _addKeyButton.clicked += AddKeyAtCurrentAngle;
            _removeKeyButton = RequireElement<Button>("remove-keyform-button");
            _removeKeyButton.clicked += () =>
            {
                var g = DisplayGroupIndex();
                var k = _controller.Selection.FindKeyformIndex(_controller.Document);
                if (g < 0 || k < 0) return;
                ApplyChange(() => _controller.RemoveKeyform(g, k));
            };
        }

        /// <summary>
        /// レールのひし形を押したときと同じ操作で、キーを選びます（← → も同じ）。moveCameraなら編集ビューをその角度にします。
        /// </summary>
        internal void SelectKey(int keyIndex, bool moveCamera = true)
        {
            var g = DisplayGroupIndex();
            if (g < 0) return;
            var keys = _controller.Document.KeyformGroups[g].Keys;
            if (keyIndex < 0 || keyIndex >= keys.Count) return;
            if (_controller.Selection.FindKeyformGroupIndex(_controller.Document) != g ||
                _controller.Selection.FindKeyformIndex(_controller.Document) != keyIndex)
            {
                _controller.SelectKeyform(g, keyIndex);
                RefreshAll();
            }

            if (moveCamera) MoveMainCameraToKey(keyIndex);
        }

        private void MoveMainCameraToKey(int keyIndex)
        {
            if (_preview == null) return;
            if (_preview.MoveCameraToKeyform(VlatPreviewView.Main, keyIndex)) RenderAndRefreshPreview();
        }

        // グループは1つだけ。ファイル全体を選んでいるときもそのグループを表示に使う。
        private int DisplayGroupIndex()
        {
            var document = _controller?.Document;
            if (document == null || document.KeyformGroups.Count == 0) return -1;
            var g = _controller.Selection.FindKeyformGroupIndex(document);
            return g < 0 ? 0 : g;
        }

        private bool IsGroupBound(int g)
        {
            if (g < 0) return false;
            var document = _controller.Document;
            var binding = document.KeyformGroups[g].MeshBinding;
            return binding >= 0 && binding < document.Metadata.MeshBindings.Count;
        }

        private void AddKeyAtCurrentAngle()
        {
            var g = DisplayGroupIndex();
            if (g < 0 || !TryGetAddYaw(g, out var yaw, out _)) return;
            var added = _controller.AddKeyform(g, yaw);
            RefreshAll();
            if (!added) return;
            var selected = _controller.Selection.FindKeyformIndex(_controller.Document);
            if (selected >= 0) MoveMainCameraToKey(selected);
        }

        // 今の角度に足せるならその角度、足せなければいちばん広く空いている角度を返す。
        private bool TryGetAddYaw(int g, out double yaw, out bool atCurrentAngle)
        {
            yaw = 0;
            atCurrentAngle = false;
            var group = _controller.Document.KeyformGroups[g];
            if (group.Keys.Count >= VlatValidator.MaximumKeyformKeys) return false;
            var current = _preview == null ? 0d : Math.Round(_preview.GetViewState(VlatPreviewView.Main).Yaw);
            var free = current > group.SupportStart && current < group.SupportEnd;
            foreach (var key in group.Keys)
                if (Math.Abs(key.Yaw - current) < 0.5) free = false;
            if (free)
            {
                yaw = current;
                atCurrentAngle = true;
                return true;
            }

            yaw = _controller.SuggestKeyformYaw(g);
            return yaw > group.SupportStart && yaw < group.SupportEnd;
        }

        private void RefreshKeyformUi()
        {
            var document = _controller.Document;
            RequireElement<Button>("add-keyform-group-button").SetEnabled(
                document.KeyformGroups.Count == 0 && document.Bands.Count == 0 &&
                document.Layers.Count == 0 && document.Textures.Count == 0);
            RefreshRail();
        }

        private void RefreshRail()
        {
            if (_rail == null || _controller?.Document == null) return;
            var document = _controller.Document;
            var g = DisplayGroupIndex();
            var k = g < 0 ? -1 : _controller.Selection.FindKeyformIndex(document);
            _railKeys.Clear();
            _railShapes.Clear();
            VlatAuthoringKeyformGroup group = null;
            if (g >= 0)
            {
                group = document.KeyformGroups[g];
                foreach (var key in group.Keys)
                {
                    _railKeys.Add(key.Yaw);
                    _railShapes.Add(key.Deltas.Count != 0);
                }
            }

            var main = _preview == null ? 0f : _preview.GetViewState(VlatPreviewView.Main).Yaw;
            var a = _preview == null ? 45f : _preview.GetViewState(VlatPreviewView.SubA).Yaw;
            var b = _preview == null ? 90f : _preview.GetViewState(VlatPreviewView.SubB).Yaw;
            _rail.SetState(_railKeys, _railShapes, k, group != null,
                group?.SupportStart ?? 0, group?.SupportEnd ?? 0, main, a, b,
                CompareViewsVisible, group != null);

            var keyControls = group != null ? DisplayStyle.Flex : DisplayStyle.None;
            _addKeyButton.style.display = keyControls;
            _removeKeyButton.style.display = keyControls;
            if (group == null) return;

            if (TryGetAddYaw(g, out var yaw, out var atCurrent))
            {
                _addKeyButton.SetEnabled(true);
                _addKeyButton.text = "＋ " + FormatYaw(yaw) + " にキーを足す";
                _addKeyButton.tooltip = atCurrent
                    ? "編集ビューの今の角度にキーを足します。"
                    : "今の角度にはキーがあるか、効く範囲の外なので、いちばん広く空いている角度に足します。";
            }
            else
            {
                _addKeyButton.SetEnabled(false);
                _addKeyButton.text = "＋ キーを足す";
                _addKeyButton.tooltip = "キーは " + VlatValidator.MaximumKeyformKeys + " 個までです。";
            }

            var canRemove = k >= 0 && group.Keys.Count > 1;
            _removeKeyButton.SetEnabled(canRemove);
            _removeKeyButton.tooltip = k < 0
                ? "キーを選ぶと消せます。"
                : canRemove
                    ? "選んでいるキー（" + FormatYaw(group.Keys[k].Yaw) + "）を消します。元に戻すで戻せます。"
                    : "最後のキーは消せません。はじめからやり直すときは「詳細」でグループを消します。";
        }

        // 右の列: 選んでいるキーと、直すパーツ。
        private void BuildKeyformInspector()
        {
            var document = _controller.Document;
            var g = DisplayGroupIndex();
            if (g < 0) return;
            var group = document.KeyformGroups[g];
            var k = _controller.Selection.FindKeyformIndex(document);
            if (k < 0)
            {
                AddHeading("キーを選んでいません");
                AddNote("レールのひし形を押すか、← → でキーを選びます。キーを選ぶと、その角度の形を直せます。");
            }
            else
            {
                BuildKeySection(g, group, k);
            }

            BuildPartSection(g, group);
        }

        private void BuildKeySection(int g, VlatAuthoringKeyformGroup group, int k)
        {
            var key = group.Keys[k];
            AddHeading(FormatYaw(key.Yaw) + " のキー");
            AddKeyformDouble("角度", "keyform-yaw", key.Yaw, value => _controller.UpdateKeyformYaw(g, k, value));
            var state = new Label(key.Deltas.Count == 0
                ? "まだ形を変えていません（元の形のままです）。"
                : key.Deltas.Count.ToString("N0", CultureInfo.InvariantCulture) + " 頂点の位置を変えています。")
            { name = "keyform-edit-state" };
            state.AddToClassList("vlat-note");
            _buildTarget.Add(state);

            var caption = new Label("この角度で見る");
            caption.AddToClassList("vlat-row-caption");
            _buildTarget.Add(caption);
            var cameraRow = new VisualElement();
            cameraRow.AddToClassList("vlat-row");
            cameraRow.AddToClassList("vlat-wrap");
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
                    text = view == VlatPreviewView.Main ? "編集ビュー" : view == VlatPreviewView.SubA ? "比較 A" : "比較 B",
                    tooltip = _preview != null && _preview.HasAvatar
                        ? "このビューを " + FormatYaw(key.Yaw) + " に向けます。"
                        : "VRM を開くと使えます。",
                };
                move.SetEnabled(_preview != null && _preview.HasAvatar);
                cameraRow.Add(move);
            }

            _buildTarget.Add(cameraRow);

            var import = new Foldout { name = "keyform-import-section", text = "BlendShape から形を取り込む" };
            import.AddToClassList("vlat-inspector-foldout");
            import.value = GetFoldoutState(import.name, false);
            import.RegisterValueChangedCallback(evt =>
            {
                if (evt.target == import) _foldoutStates[import.name] = evt.newValue;
            });
            _buildTarget.Add(import);
            var hasSource = TryGetBindingSource(g, out _, out var sourceRenderer, out var sourceMesh);
            var mesh = sourceMesh != null ? sourceMesh : VlatKeyformBindingUtility.GetSourceMesh(sourceRenderer);
            var choices = new List<string> { "なし（このキーの形を空にする）" };
            if (mesh != null)
                for (var shape = 0; shape < mesh.blendShapeCount; shape++)
                    choices.Add("[" + shape + "] " + mesh.GetBlendShapeName(shape));
            if (_keyformShapeIndex + 1 >= choices.Count) _keyformShapeIndex = -1;
            var shapes = new DropdownField("BlendShape", choices, _keyformShapeIndex + 1) { name = "keyform-shape" };
            shapes.RegisterValueChangedCallback(_ => _keyformShapeIndex = shapes.index - 1);
            import.Add(shapes);
            var importButton = new Button(() => ApplyChange(() =>
            {
                if (TryGetBindingSource(g, out var root, out var renderer, out var source))
                    _controller.ImportKeyformPositions(g, k, root, renderer, _keyformShapeIndex, source);
            }))
            {
                name = "import-keyform-button",
                text = "取り込む（このキーの形を置き換える）",
                tooltip = hasSource ? string.Empty : "VRM を開くか、「詳細」でシーンのアバターを指定すると使えます。",
            };
            importButton.SetEnabled(hasSource);
            import.Add(importButton);
            var note = new Label("BlendShape を 100% にしたときの頂点の位置を、このキーの形にします。法線と表情の設定は取り込みません。");
            note.AddToClassList("vlat-note");
            import.Add(note);
        }

        private void BuildPartSection(int g, VlatAuthoringKeyformGroup group)
        {
            AddSection("直すパーツ");
            var document = _controller.Document;
            if (IsGroupBound(g))
            {
                var binding = document.Metadata.MeshBindings[group.MeshBinding];
                var slash = binding.HierarchyPath.LastIndexOf('/');
                var name = slash < 0 ? binding.HierarchyPath : binding.HierarchyPath.Substring(slash + 1);
                var bound = new Label(name + "（" + binding.VertexCount.ToString("N0", CultureInfo.InvariantCulture) + " 頂点）")
                {
                    name = "keyform-bound-part",
                    tooltip = binding.HierarchyPath + "\nメッシュ: " + binding.MeshName + "\n" + binding.PositionHash + "\n" + group.TopologyHash,
                };
                bound.AddToClassList("vlat-emphasis");
                _buildTarget.Add(bound);
                AddNote("形を変えたあとは、別のパーツには付け替えられません。");
                return;
            }

            _buildTarget.Add(new HelpBox("直すパーツが決まっていないため、まだ保存できません。", HelpBoxMessageType.Error));
            if (_preview != null && _preview.HasAvatar)
            {
                var targets = _preview.VertexTargets;
                var choices = new List<string>();
                var indices = new List<int>();
                for (var index = 0; index < targets.Count; index++)
                {
                    if (!(targets[index].Renderer is SkinnedMeshRenderer)) continue;
                    choices.Add(targets[index].Renderer.name + "（" +
                        targets[index].VertexCount.ToString("N0", CultureInfo.InvariantCulture) + " 頂点）");
                    indices.Add(index);
                }

                if (choices.Count != 0)
                {
                    var current = Math.Max(0, indices.IndexOf(_bindPreviewPartTarget));
                    _bindPreviewPartTarget = indices[current];
                    var parts = new DropdownField("パーツ", choices, current) { name = "bind-preview-part-dropdown" };
                    parts.RegisterValueChangedCallback(_ => _bindPreviewPartTarget = parts.index < 0 ? -1 : indices[parts.index]);
                    _buildTarget.Add(parts);
                    var bind = new Button(() =>
                    {
                        if (_bindPreviewPartTarget < 0 || _bindPreviewPartTarget >= _preview.VertexTargets.Count) return;
                        var target = _preview.VertexTargets[_bindPreviewPartTarget];
                        ApplyChange(() => _controller.BindKeyformTarget(g, _preview.AvatarRoot, target.Renderer, target.SourceMesh));
                    })
                    {
                        name = "bind-preview-part-button",
                        text = "このパーツにする",
                    };
                    bind.AddToClassList("vlat-primary");
                    _buildTarget.Add(bind);
                }
            }
            else
            {
                AddNote("VRM を開くと、ここでパーツを選べます。");
            }

            AddNote("シーンに置いたアバターで指定するときは「詳細」を開きます。");
        }

        // 「詳細」: 効く範囲とグループ、シーンのアバターでの指定、ファイルの情報。
        private void BuildDetails()
        {
            var document = _controller.Document;
            var g = DisplayGroupIndex();
            if (g >= 0 && !IsLegacyDocument(document)) BuildKeyformDetails(g, document.KeyformGroups[g]);
            BuildDocumentDetails();
        }

        private void BuildKeyformDetails(int g, VlatAuthoringKeyformGroup group)
        {
            AddDetailsFoldout("details-group", "効く範囲とグループ", true);
            AddKeyformDouble("始まり（°）", "keyform-support-start", group.SupportStart,
                value => _controller.UpdateKeyformGroup(g, group.Id, value, group.SupportEnd));
            AddKeyformDouble("終わり（°）", "keyform-support-end", group.SupportEnd,
                value => _controller.UpdateKeyformGroup(g, group.Id, group.SupportStart, value));
            AddKeyformText("グループ ID", "keyform-group-id", group.Id,
                value => _controller.UpdateKeyformGroup(g, value, group.SupportStart, group.SupportEnd));
            AddNote("補正はこの範囲の中だけで効きます。キーは範囲の内側に置きます。" +
                "キーのあいだはなめらかにつなぎ（smoothstep）、変えるのは頂点の位置だけです。");
            var remove = new Button(() => ApplyChange(() => _controller.RemoveKeyformGroup(g)))
            {
                name = "remove-keyform-group-button",
                text = "グループを消して、はじめからやり直す",
                tooltip = "キーと形をすべて消して「はじめる画面」に戻ります。元に戻すで戻せます。",
            };
            remove.AddToClassList("vlat-danger");
            _buildTarget.Add(remove);

            AddDetailsFoldout("details-scene-binding", "シーンのアバターでパーツを指定", !IsGroupBound(g));
            AddNote("VRM を開かずに、シーンに置いたアバターで直すパーツを決めるときに使います。" +
                "BlendShape の取り込みも、ここで指定したアバターから読みます。");
            var root = new ObjectField("アバターのルート")
            {
                name = "keyform-avatar-root", objectType = typeof(GameObject), allowSceneObjects = true, value = _keyformAvatarRoot,
            };
            root.RegisterValueChangedCallback(evt => { _keyformAvatarRoot = evt.newValue as GameObject; RefreshAll(); });
            _buildTarget.Add(root);
            var target = new ObjectField("パーツ（Renderer）")
            {
                name = "keyform-renderer", objectType = typeof(Renderer), allowSceneObjects = true, value = _keyformRenderer,
            };
            target.RegisterValueChangedCallback(evt =>
            {
                _keyformRenderer = evt.newValue as Renderer;
                _keyformShapeIndex = -1;
                RefreshAll();
            });
            _buildTarget.Add(target);
            var meshField = new ObjectField("メッシュ")
            {
                name = "keyform-mesh", objectType = typeof(Mesh), value = VlatKeyformBindingUtility.GetSourceMesh(_keyformRenderer),
            };
            meshField.SetEnabled(false);
            _buildTarget.Add(meshField);
            var bind = new Button(() => ApplyChange(() => _controller.BindKeyformTarget(g, _keyformAvatarRoot, _keyformRenderer)))
            {
                name = "bind-keyform-target-button",
                text = "このパーツにする",
            };
            _buildTarget.Add(bind);
        }

        // 取り込み元: 「詳細」でシーンのアバターを指定していればそれ、なければプレビューの直すパーツ。
        private bool TryGetBindingSource(int g, out GameObject root, out Renderer renderer, out Mesh sourceMesh)
        {
            root = null;
            renderer = null;
            sourceMesh = null;
            if (_keyformAvatarRoot != null && _keyformRenderer != null)
            {
                root = _keyformAvatarRoot;
                renderer = _keyformRenderer;
                return true;
            }

            if (_preview == null || !_preview.HasAvatar || !IsGroupBound(g)) return false;
            var binding = _controller.Document.KeyformGroups[g].MeshBinding;
            foreach (var target in _preview.VertexTargets)
            {
                if (target.BindingIndex != binding) continue;
                root = _preview.AvatarRoot;
                renderer = target.Renderer;
                sourceMesh = target.SourceMesh;
                return true;
            }

            return false;
        }

        // 直すパーツが決まっていれば、頂点を選ぶパーツも自動でそれにする（3か所で指定しなくてよいように）。
        private void SyncVertexTargetToBinding()
        {
            if (_preview == null || !_preview.HasAvatar || _preview.IsDragging) return;
            var g = DisplayGroupIndex();
            if (!IsGroupBound(g)) return;
            var binding = _controller.Document.KeyformGroups[g].MeshBinding;
            var targets = _preview.VertexTargets;
            var current = _preview.VertexTargetIndex;
            if (current >= 0 && current < targets.Count && targets[current].BindingIndex == binding) return;
            for (var index = 0; index < targets.Count; index++)
            {
                if (targets[index].BindingIndex != binding) continue;
                _preview.SetVertexSelectionTarget(index);
                return;
            }
        }

        private void AddDetailsFoldout(string name, string text, bool defaultOpen)
        {
            var foldout = new Foldout { name = name, text = text };
            foldout.AddToClassList("vlat-details-section");
            foldout.value = GetFoldoutState(name, defaultOpen);
            foldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.target == foldout) _foldoutStates[name] = evt.newValue;
            });
            _detailsDynamic.Add(foldout);
            _buildTarget = foldout;
        }

        private bool GetFoldoutState(string name, bool defaultOpen) =>
            _foldoutStates.TryGetValue(name, out var open) ? open : defaultOpen;

        private void AddKeyformDouble(string label, string name, double value, Func<double, bool> changed)
        {
            var field = new DoubleField(label) { name = name, isDelayed = true, value = value };
            field.RegisterValueChangedCallback(evt => ApplyChange(() => changed(evt.newValue)));
            _buildTarget.Add(field);
        }

        private void AddKeyformText(string label, string name, string value, Func<string, bool> changed)
        {
            var field = new TextField(label) { name = name, isDelayed = true, value = value };
            field.RegisterValueChangedCallback(evt => ApplyChange(() => changed(evt.newValue)));
            _buildTarget.Add(field);
        }

        private static string FormatYaw(double yaw)
        {
            var text = Math.Abs(yaw).ToString("0.#", CultureInfo.InvariantCulture);
            return (yaw < 0 ? "−" : string.Empty) + text + "°";
        }
    }
}
