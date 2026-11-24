using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VLat.Editor.Authoring;
using Object = UnityEngine.Object;

namespace VLat.Tests.Edit
{
    /// <summary>K-037の画面（はじめる画面、角度レール、ツール、キーボード操作）を実Windowで検証します。</summary>
    public sealed class VlatEditorRedesignTests
    {
        private VlatKeyformPreviewFixture _f;
        private VlatEditorWindow _window;

        /// <summary>テスト所有のWindowとfixtureを閉じます。</summary>
        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                _window.DiscardChanges();
                _window.Close();
                if (_window != null) Object.DestroyImmediate(_window);
            }

            _f?.Dispose();
            _window = null;
            _f = null;
        }

        /// <summary>
        /// プレビュー用VRMのパーツと角度を選んで始めると、元Meshでパーツが決まり、0°を含むキーがそろい、
        /// 1回の元に戻すで全部戻ります。
        /// </summary>
        [UnityTest]
        public IEnumerator StartScreen_BindsPreviewPartWithChosenAnglesInOneUndo()
        {
            _f = new VlatKeyformPreviewFixture(true);
            _window = VlatEditorWindow.CreateForRecoveryDirectory(Path.Combine(_f.DirectoryPath, "window"));
            _window.position = new Rect(80, 80, 1200, 820);
            _window.Show();
            yield return null;
            _window.CreateGUI();
            var root = _window.rootVisualElement;
            var session = _window.DocumentController;
            Assert.That(session.Document.KeyformGroups, Is.Empty);
            Assert.That(root.Q<VisualElement>("start-screen").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(root.Q<Button>("start-part-0"), Is.Null, "VRMを開くまでパーツは並ばない");

            _f.Adopt(_window.PreviewController);
            _window.CreateGUI();
            yield return null;
            var part = root.Q<Button>("start-part-0");
            Assert.That(part, Is.Not.Null);
            Assert.That(root.Q<Button>("start-editing-button").enabledSelf, Is.False, "パーツを選ぶまで始められない");
            Click(part);
            Assert.That(root.Q<Button>("start-editing-button").enabledSelf, Is.True);
            root.Q<Toggle>("start-angle-n45").value = true;
            var meshJson = EditorJsonUtility.ToJson(_f.Mesh);

            Undo.ClearAll();
            Click(root.Q<Button>("start-editing-button"));
            var document = session.Document;
            Assert.That(document.KeyformGroups.Count, Is.EqualTo(1));
            var group = document.KeyformGroups[0];
            Assert.That(group.MeshBinding, Is.Zero, session.LastMessage);
            Assert.That(document.Metadata.MeshBindings[0].HierarchyPath, Is.EqualTo("Head/Face"));
            Assert.That(document.Metadata.MeshBindings[0].VertexCount, Is.EqualTo(_f.Mesh.vertexCount));
            var yaws = new double[group.Keys.Count];
            for (var i = 0; i < yaws.Length; i++) yaws[i] = group.Keys[i].Yaw;
            CollectionAssert.AreEqual(new double[] { -45, 0, 45, 90 }, yaws);
            var selected = session.Selection.FindKeyformIndex(document);
            Assert.That(group.Keys[selected].Yaw, Is.EqualTo(45));
            Assert.That(_window.CurrentTool, Is.EqualTo(VlatEditorTool.Select), "始めたらすぐ頂点を選べる");
            Assert.That(_window.PreviewController.GetViewState(VlatPreviewView.Main).Yaw, Is.EqualTo(45).Within(.0001));
            Assert.That(root.Q<VisualElement>("start-screen").style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(EditorJsonUtility.ToJson(_f.Mesh), Is.EqualTo(meshJson), "元のMeshは変えない");

            Undo.PerformUndo();
            Assert.That(session.Document.KeyformGroups, Is.Empty, "グループ・パーツ・キーは1回で戻る");
            Assert.That(root.Q<VisualElement>("start-screen").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Undo.PerformRedo();
            Assert.That(session.Document.KeyformGroups.Count, Is.EqualTo(1));
            Assert.That(session.Document.KeyformGroups[0].Keys.Count, Is.EqualTo(4));
        }

        /// <summary>今の角度にキーを足し、← → でキーを移り、ツールとビュー上の表示が状態に追従します。</summary>
        [UnityTest]
        public IEnumerator Rail_AddsKeyAtCurrentAngleStepsWithArrowsAndSwitchesTools()
        {
            _f = new VlatKeyformPreviewFixture(true);
            _window = VlatEditorWindow.CreateForRecoveryDirectory(Path.Combine(_f.DirectoryPath, "window"));
            _window.position = new Rect(80, 80, 1200, 820);
            _window.Show();
            yield return null;
            _f.Seed(_window.DocumentController, true);
            _f.Adopt(_window.PreviewController);
            _window.CreateGUI();
            yield return null;
            var root = _window.rootVisualElement;
            var session = _window.DocumentController;
            var rail = root.Q<VlatAngleRail>("angle-rail");
            Assert.That(rail, Is.Not.Null);

            root.Q<FloatField>("preview-yaw").value = 30;
            var add = root.Q<Button>("add-keyform-button");
            Assert.That(add.text, Does.Contain("30°"));
            Click(add);
            var group = session.Document.KeyformGroups[0];
            Assert.That(group.Keys.Count, Is.EqualTo(3));
            Assert.That(group.Keys[1].Yaw, Is.EqualTo(30));
            Assert.That(session.Selection.FindKeyformIndex(session.Document), Is.EqualTo(1));
            Assert.That(rail.SelectedKey, Is.EqualTo(1));
            Assert.That(add.text, Does.Contain("−60°"), "今の角度にキーがあるときは、いちばん広い空きに足す");

            Key(root, KeyCode.RightArrow);
            Assert.That(session.Selection.FindKeyformIndex(session.Document), Is.EqualTo(2));
            Assert.That(_window.PreviewController.GetViewState(VlatPreviewView.Main).Yaw, Is.EqualTo(90).Within(.0001));
            Key(root, KeyCode.LeftArrow);
            Assert.That(session.Selection.FindKeyformIndex(session.Document), Is.EqualTo(1));

            Key(root, KeyCode.Alpha2);
            Assert.That(_window.CurrentTool, Is.EqualTo(VlatEditorTool.Select));
            Assert.That(root.Q<Toggle>("geometry-selection-toggle").value, Is.True);
            Assert.That(root.Q<Label>("view-tag-title").text, Does.Contain("30°"));
            Key(root, KeyCode.Alpha3);
            Assert.That(_window.CurrentTool, Is.EqualTo(VlatEditorTool.Move));
            Assert.That(root.Q<Toggle>("geometry-selection-toggle").value, Is.False);
            Assert.That(root.Q<Toggle>("keyform-drag-toggle").value, Is.True);
            root.Q<Toggle>("tool-view-toggle").value = true;
            Assert.That(_window.CurrentTool, Is.EqualTo(VlatEditorTool.View));
            Assert.That(_window.PreviewController.GeometrySelectionEnabled, Is.False);

            root.Q<FloatField>("preview-yaw").value = 60;
            Assert.That(root.Q<Label>("view-tag-detail").text, Does.Contain("30° と 90° のあいだ"));
            root.Q<FloatField>("preview-yaw").value = 150;
            Assert.That(root.Q<Label>("view-tag-detail").text, Does.Contain("効く範囲の外"));

            var revision = session.Document.Revision;
            Click(root.Q<Button>("remove-keyform-button"));
            Assert.That(session.Document.KeyformGroups[0].Keys.Count, Is.EqualTo(2));
            Assert.That(session.Document.Revision, Is.Not.EqualTo(revision));
            Assert.That(root.Q<Label>("status-label").text, Does.Contain("30°").And.Contain("消しました"));
        }

        private static void Click(Button button)
        {
            Assert.That(button, Is.Not.Null);
            Assert.That(button.enabledInHierarchy, Is.True, button.name);
            using var submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }

        // キー入力はフォーカスのある要素に届くので、ウィンドウの地の部分にフォーカスを置いてから押す。
        private static void Key(VisualElement root, KeyCode key)
        {
            root.Focus();
            using var evt = KeyDownEvent.GetPooled('\0', key, EventModifiers.None);
            root.SendEvent(evt);
        }
    }
}
