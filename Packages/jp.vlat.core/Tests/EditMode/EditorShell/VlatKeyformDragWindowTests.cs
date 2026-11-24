using System;
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
    /// <summary>実EditorWindowのUI Toolkit pointer入力、Esc/Focus/Selection/close境界を検証します。</summary>
    public sealed class VlatKeyformDragWindowTests
    {
        private VlatKeyformPreviewFixture _f;
        private VlatEditorWindow _window;
        private VlatEditorWindow _other;

        /// <summary>テスト所有のWindowを閉じます。</summary>
        [TearDown]
        public void TearDown()
        {
            if (_other != null) { _other.DiscardChanges(); _other.Close(); }
            if (_window != null) { _window.DiscardChanges(); _window.Close(); }
            _f?.Dispose();
            Selection.activeObject = null;
        }

        private IEnumerator Open()
        {
            _f = new VlatKeyformPreviewFixture(true);
            _window = VlatEditorWindow.CreateForRecoveryDirectory(Path.Combine(_f.DirectoryPath, "window"));
            _window.position = new Rect(100, 100, 1200, 1000); _window.Show(); _window.Focus(); yield return null;
            _f.Seed(_window.DocumentController, true); _f.Adopt(_window.PreviewController);
            _window.CreateGUI(); yield return null;
            _window.SelectKey(0);
            _window.rootVisualElement.Q<Toggle>("geometry-selection-toggle").value = true;
            yield return null;
            var p = _window.PreviewController;
            p.SetVertexVisibilityMode(VlatVertexVisibilityMode.SelectThrough);
            var selected = (Vector2)p.VertexSelection.ScreenPositions[7];
            SendPointer(_window, EventType.MouseDown, selected);
            SendPointer(_window, EventType.MouseUp, selected);
            Assert.That(p.VertexSelection.SelectedCount, Is.EqualTo(1));
            _window.rootVisualElement.Q<Toggle>("keyform-drag-toggle").value = true;
        }

        internal static void SendPointer(VlatEditorWindow window, EventType type, Vector2 screen, int button = 0)
        {
            var image = window.rootVisualElement.Q<Image>("preview-main");
            var texture = window.PreviewController.GetTexture(VlatPreviewView.Main);
            var c = image.contentRect; var scale = Mathf.Min(c.width / texture.width, c.height / texture.height);
            var local = new Vector2(c.x + (c.width - texture.width * scale) / 2 + screen.x * scale,
                c.y + (c.height - texture.height * scale) / 2 + (texture.height - screen.y) * scale);
            var e = new Event { type = type, mousePosition = image.LocalToWorld(local), button = button };
            if (type == EventType.MouseDown)
            { using var evt = PointerDownEvent.GetPooled(e); evt.target = image; image.SendEvent(evt); }
            else if (type == EventType.MouseUp)
            { using var evt = PointerUpEvent.GetPooled(e); evt.target = image; image.SendEvent(evt); }
            else
            { using var evt = PointerMoveEvent.GetPooled(e); evt.target = image; image.SendEvent(evt); }
        }

        /// <summary>実pointer down/move/upが一括commitとUnity Undo/Redo、保存・再読込へ接続します。</summary>
        [UnityTest]
        public IEnumerator WindowPointerDrag_CommitsOnceAndPersists()
        {
            yield return Open();
            var s = _window.DocumentController; var p = _window.PreviewController;
            var before = s.ContentFingerprint; var revision = s.Document.Revision;
            Undo.ClearAll();
            SendPointer(_window, EventType.MouseDown, VlatKeyformDragTests.Start);
            Assert.That(p.IsDragging, Is.True, p.DragStatus);
            SendPointer(_window, EventType.MouseDrag, VlatKeyformDragTests.Start + new Vector2(8,0));
            SendPointer(_window, EventType.MouseDrag, VlatKeyformDragTests.Start + new Vector2(16,5));
            Assert.That(s.Document.Revision, Is.EqualTo(revision));
            SendPointer(_window, EventType.MouseUp, VlatKeyformDragTests.Start, 2);
            Assert.That(p.IsDragging, Is.True, "別buttonのMouseUpではcommitしない");
            SendPointer(_window, EventType.MouseUp, VlatKeyformDragTests.Start + new Vector2(16,5));
            Assert.That(p.IsDragging, Is.False); var changed = s.ContentFingerprint;
            Assert.That(changed, Is.Not.EqualTo(before));
            Click(_window.rootVisualElement.Q<Button>("undo-button")); Assert.That(s.ContentFingerprint, Is.EqualTo(before));
            Click(_window.rootVisualElement.Q<Button>("redo-button")); Assert.That(s.ContentFingerprint, Is.EqualTo(changed));
            var path = Path.Combine(_f.DirectoryPath, "ui.vlat.json");
            Assert.That(s.SaveAs(path), Is.True); Assert.That(s.Open(path, VlatCloseDecision.Discard), Is.True);
            Assert.That(s.ContentFingerprint, Is.EqualTo(changed));
            TestContext.WriteLine("ACTUAL_WINDOW_POINTER PASS selection/down/move/up, one Undo/Redo, save/reload");
        }

        /// <summary>実KeyDownイベント、Cancelボタン、Selection通知、別WindowへのFocusが未確定編集を破棄します。</summary>
        [UnityTest]
        public IEnumerator WindowCancellation_EscapeButtonSelectionAndActualFocus()
        {
            yield return Open();
            var s = _window.DocumentController; var p = _window.PreviewController; var before = s.ContentFingerprint;
            for (var i = 0; i < 4; i++)
            {
                _window.Focus(); yield return null;
                SendPointer(_window, EventType.MouseDown, VlatKeyformDragTests.Start);
                SendPointer(_window, EventType.MouseDrag, VlatKeyformDragTests.Start + new Vector2(15,5));
                Assert.That(p.IsDragging, Is.True, "boundary=" + i + " " + p.DragStatus);
                var temp = p.DragMesh;
                if (i == 0)
                {
                    using var evt = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape });
                    var image = _window.rootVisualElement.Q<Image>("preview-main");
                    evt.target = image; image.SendEvent(evt);
                }
                if (i == 1) Click(_window.rootVisualElement.Q<Button>("cancel-keyform-drag-button"));
                if (i == 2) { Selection.activeObject = _f.Source.gameObject; yield return null; }
                if (i == 3)
                {
                    _other = VlatEditorWindow.CreateForRecoveryDirectory(Path.Combine(_f.DirectoryPath, "focus"));
                    _other.Show(); _other.Focus(); yield return null;
                    Assert.That(EditorWindow.focusedWindow, Is.SameAs(_other));
                }
                Assert.That(p.IsDragging, Is.False, "boundary=" + i); Assert.That(temp == null, Is.True);
                Assert.That(s.ContentFingerprint, Is.EqualTo(before));
            }
        }

        /// <summary>Window Closeでは一時Meshを破棄し、確定済みdragだけをRecoveryから再生成します。</summary>
        [UnityTest]
        public IEnumerator WindowCloseAndRecreation_KeepOnlyCommittedDrag()
        {
            yield return Open();
            var p = _window.PreviewController; var s = _window.DocumentController;
            VlatKeyformDragTests.Drag(p, new Vector2(12,0)); var committed = s.ContentFingerprint;
            Assert.That(p.BeginKeyformDrag(VlatKeyformDragTests.Start), Is.True);
            p.UpdateKeyformDrag(VlatKeyformDragTests.Start + Vector2.one * 20); var temp = p.DragMesh;
            // native破棄でOnDisable/OnDestroyを実行。human close dialogの代用とは数えない。
            Object.DestroyImmediate(_window); _window = null; yield return null;
            Assert.That(temp == null, Is.True);
            _window = VlatEditorWindow.CreateForRecoveryDirectory(Path.Combine(_f.DirectoryPath, "window"));
            _window.Show(); yield return null;
            Assert.That(_window.DocumentController.ContentFingerprint, Is.EqualTo(committed));
            Assert.That(_window.PreviewController.IsDragging, Is.False);
        }

        private static void Click(Button button)
        {
            using var evt = NavigationSubmitEvent.GetPooled(); evt.target = button; button.SendEvent(evt);
        }
    }
}
