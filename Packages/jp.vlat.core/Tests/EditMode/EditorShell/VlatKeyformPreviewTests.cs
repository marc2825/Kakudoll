using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VLat.Editor.Authoring;
using VLat.Runtime;
using Object = UnityEngine.Object;

namespace VLat.Tests.Edit
{
    /// <summary>実Preview Scene/Camera/SkinnedMeshRendererを経由してKeyform v0.2の統合を検証します。</summary>
    public sealed class VlatKeyformPreviewTests
    {
        /// <summary>キー、キー間、support端とsupport外で、独立に計算した重み・頂点に一致します。</summary>
        [TestCase(0, 1, 0)]
        [TestCase(22.5f, .84375, .15625)]
        [TestCase(45, .5, .5)]
        [TestCase(90, 0, 1)]
        [TestCase(-60, .5, 0)]
        [TestCase(120, 0, 0)]
        [TestCase(150, 0, 0)]
        [TestCase(-180, 0, 0)]
        public void RenderedKeyAndInterpolatedGeometry_MatchesIndependentExpectations(float yaw, double left, double right)
        {
            using var f = new VlatKeyformPreviewFixture();
            f.Preview.SetAngles(VlatPreviewView.Main, yaw, 0);
            Assert.That(f.Preview.RenderIfNeeded(), Is.True);
            Assert.That(f.Preview.GetKeyformWeight(VlatPreviewView.Main, 0), Is.EqualTo(left).Within(2e-6));
            Assert.That(f.Preview.GetKeyformWeight(VlatPreviewView.Main, 1), Is.EqualTo(right).Within(2e-6));
            AssertGeometry(f, left, right, 0);
        }

        /// <summary>Cameraの実render callbackで各視点の重みを観測し、最後にMainへ復帰することを検証します。</summary>
        [Test]
        public void ThreeCameras_RenderIndependentWeightsAndRestoreMain()
        {
            using var f = new VlatKeyformPreviewFixture();
            var observed = new Dictionary<string, Vector2>();
            var index = f.Renderer.sharedMesh.GetBlendShapeIndex("VLAT_PreviewKeyform_" +
                f.Session.Document.KeyformGroups[0].EditorIdentity + "__modifier_0");
            void Record(Camera camera)
            {
                if (camera.gameObject.scene == f.Preview.PreviewScene)
                    observed[camera.name] = new Vector2(f.Renderer.GetBlendShapeWeight(index), f.Renderer.GetBlendShapeWeight(index + 1));
            }
            Camera.onPreCull += Record;
            try
            {
                f.Preview.SetAngles(VlatPreviewView.Main, 0, 0);
                Assert.That(f.Preview.RenderIfNeeded(), Is.True);
                Assert.That(observed.Count, Is.EqualTo(3));
                Assert.That(observed["VLat Preview Camera 0"].x, Is.EqualTo(100).Within(.001));
                Assert.That(observed["VLat Preview Camera 1"].x, Is.EqualTo(50).Within(.001));
                Assert.That(observed["VLat Preview Camera 1"].y, Is.EqualTo(50).Within(.001));
                Assert.That(observed["VLat Preview Camera 2"].y, Is.EqualTo(100).Within(.001));
                AssertGeometry(f, 1, 0, 0);
            }
            finally { Camera.onPreCull -= Record; }
        }

        /// <summary>root移動/回転、Head回転、Pitchとpanを含む実Camera位置から既存Head-local規約でYawを得ます。</summary>
        [TestCase(0, 0, 0, 45)]
        [TestCase(90, 0, 0, -45)]
        [TestCase(35, 60, 20, 90)]
        [TestCase(-135, -75, -25, -60)]
        public void HeadFrame_FollowsRootAndHeadWithoutChangingYawConvention(float rootYaw, float headYaw, float pitch, float yaw)
        {
            using var f = new VlatKeyformPreviewFixture();
            f.Clone.transform.position = new Vector3(3, -2, 8);
            f.Clone.transform.rotation = Quaternion.Euler(0, rootYaw, 0);
            f.Head.localRotation = Quaternion.Euler(12, headYaw, 17);
            f.Preview.SetAngles(VlatPreviewView.Main, yaw, pitch);
            Assert.That(f.Preview.RenderIfNeeded(), Is.True);
            Assert.That(f.Preview.GetEvaluatedYaw(VlatPreviewView.Main), Is.EqualTo(yaw).Within(.0001));
            f.Preview.Pan(VlatPreviewView.Main, new Vector2(70, 20));
            Assert.That(f.Preview.RenderIfNeeded(), Is.True);
            var local = f.Head.InverseTransformPoint(f.Preview.GetCamera(VlatPreviewView.Main).transform.position);
            var expected = Math.Atan2(local.x, local.z) * 180 / Math.PI;
            Assert.That(f.Preview.GetEvaluatedYaw(VlatPreviewView.Main), Is.EqualTo(expected).Within(.00001));
            f.Head.Rotate(0, 30, 0);
            Assert.That(f.Preview.RenderIfNeeded(), Is.True, "transform変更だけでも更新する");
        }

        /// <summary>orbit/pan/zoomとキー移動はPreviewだけを更新し、編集RevisionやDirtyを変更しません。</summary>
        [Test]
        public void CameraNavigationAndKeyJump_AreSessionOnlyAndDoNotRebuildMesh()
        {
            using var f = new VlatKeyformPreviewFixture();
            Assert.That(f.Session.SaveAs(Path.Combine(f.DirectoryPath, "camera.vlat.json")), Is.True);
            var revision = f.Session.Document.Revision; var mesh = f.Renderer.sharedMesh;
            f.Preview.Orbit(VlatPreviewView.SubA, new Vector2(-40, 10));
            f.Preview.Pan(VlatPreviewView.SubB, new Vector2(20, 10));
            f.Preview.Zoom(VlatPreviewView.Main, -2);
            Assert.That(f.Preview.RenderIfNeeded(), Is.True);
            Assert.That(f.Preview.MoveCameraToKeyform(VlatPreviewView.SubB, 1), Is.True);
            Assert.That(f.Preview.RenderIfNeeded(), Is.True);
            Assert.That(f.Preview.GetEvaluatedYaw(VlatPreviewView.SubB), Is.EqualTo(90).Within(.0001));
            Assert.That(f.Preview.GetViewState(VlatPreviewView.SubB).Pan, Is.EqualTo(Vector2.zero));
            Assert.That(f.Preview.MoveCameraToKeyform(VlatPreviewView.Main, 99), Is.False);
            f.Preview.ResetView(VlatPreviewView.Main); f.Preview.RenderIfNeeded();
            Assert.That(f.Renderer.sharedMesh, Is.SameAs(mesh));
            Assert.That(f.Session.Document.Revision, Is.EqualTo(revision)); Assert.That(f.Session.IsDirty, Is.False);
            Assert.That(f.Preview.RenderIfNeeded(), Is.False, "未変更ならrenderしない");
        }

        /// <summary>±180度を跨ぐ支持区間をキー移動と実Cameraから評価します。</summary>
        [Test]
        public void WrappedSupport_CameraMinus150EvaluatesStored210Key()
        {
            using var f = new VlatKeyformPreviewFixture();
            var group = f.Session.Document.KeyformGroups[0];
            group.SupportStart = 120; group.SupportEnd = 240; group.Keys[0].Yaw = 150; group.Keys[1].Yaw = 210;
            // 試験の初期設定後、通常commandでrevisionを更新してsnapshotを再構築する。
            VlatAuthoringCommands.SetGenerator(f.Session.Document, "wrapped fixture"); f.Connect();
            Assert.That(f.Preview.MoveCameraToKeyform(VlatPreviewView.Main, 1), Is.True);
            f.Preview.RenderIfNeeded();
            Assert.That(f.Preview.GetEvaluatedYaw(VlatPreviewView.Main), Is.EqualTo(-150).Within(.0001));
            AssertGeometry(f, 0, 1, 0);
        }

        /// <summary>中立/表情とKeyformを加算し、Beforeでも表情のBlendShapeを保ちます。</summary>
        [TestCase(0f)] [TestCase(.5f)] [TestCase(1f)]
        public void ExpressionsAndBeforeAfter_CoexistAcrossThreeViews(float expression)
        {
            using var f = new VlatKeyformPreviewFixture();
            f.Preview.SetAngles(VlatPreviewView.Main, 45, 0);
            f.Preview.SetExpressionWeight(0, expression); f.Preview.RenderIfNeeded();
            AssertGeometry(f, .5, .5, expression);
            f.Preview.SetApplicationEnabled(false); f.Preview.RenderIfNeeded();
            AssertGeometry(f, 0, 0, expression);
            Assert.That(f.Renderer.GetBlendShapeWeight(2), Is.EqualTo(expression * 100).Within(.001));
            f.Preview.SetApplicationEnabled(true); f.Preview.RenderIfNeeded(); AssertGeometry(f, .5, .5, expression);
            f.Preview.ResetExpressions(); f.Preview.RenderIfNeeded(); AssertGeometry(f, .5, .5, 0);
        }

        /// <summary>異なるExpressionへ切り替えてもKeyformの角度補間を維持します。</summary>
        [Test]
        public void ExpressionSwitch_KeepsKeyformAndUpdatesOnlyExpressionContribution()
        {
            using var f = new VlatKeyformPreviewFixture();
            f.Preview.SetAngles(VlatPreviewView.Main, 45, 0);
            f.Preview.SetExpressionWeight(0, 1); f.Preview.RenderIfNeeded(); AssertGeometry(f, .5, .5, 1);
            f.Preview.SetExpressionWeight(0, 0); f.Preview.SetExpressionWeight(1, 1);
            f.Preview.RenderIfNeeded(); AssertGeometry(f, .5, .5, .5);
            f.Preview.SetApplicationEnabled(false); f.Preview.RenderIfNeeded(); AssertGeometry(f, 0, 0, .5);
        }

        /// <summary>編集command、Undo/Redo、削除と選択解除に追従し、過去の複製Meshを解放します。</summary>
        [Test]
        public void DocumentCommandsUndoRedoAndSelection_RefreshWithoutDuplicateApplication()
        {
            using var f = new VlatKeyformPreviewFixture();
            var old = f.Renderer.sharedMesh;
            Assert.That(f.Session.AddKeyform(0, 45), Is.True); f.Connect();
            Assert.That(old == null, Is.True);
            f.Preview.SetAngles(VlatPreviewView.Main, 45, 0); f.Preview.RenderIfNeeded(); AssertGeometry(f, 0, 0, 0);
            Assert.That(f.Session.ImportKeyformPositions(0, 1, f.Source.gameObject, f.SourceRenderer, 0), Is.True);
            f.Connect(); AssertGeometry(f, 1, 0, 0);
            Undo.PerformUndo(); f.Session.RefreshAfterUndoRedo(); f.Connect(); AssertGeometry(f, 0, 0, 0);
            Undo.PerformRedo(); f.Session.RefreshAfterUndoRedo(); f.Connect(); AssertGeometry(f, 1, 0, 0);
            Assert.That(f.Session.UpdateKeyformYaw(0, 1, 30), Is.True); f.Connect();
            f.Preview.MoveCameraToKeyform(VlatPreviewView.Main, 1); f.Preview.RenderIfNeeded(); AssertGeometry(f, 1, 0, 0);
            Assert.That(f.Session.RemoveKeyform(0, 1), Is.True); f.Connect();
            f.Preview.SetKeyformGroupSelection(-1); f.Preview.RenderIfNeeded(); AssertGeometry(f, 0, 0, 0);
            f.Preview.SetKeyformGroupSelection(0); f.Preview.RenderIfNeeded();
            f.Session.RemoveKeyformGroup(0); f.Connect();
            Assert.That(f.Renderer.sharedMesh, Is.SameAs(f.Mesh)); AssertGeometry(f, 0, 0, 0);
        }

        /// <summary>保存・再読込したDocumentでも、頂点と描画が同一になります。</summary>
        [Test]
        public void SaveReload_PreservesGeometryAndRenderedPixels()
        {
            using var f = new VlatKeyformPreviewFixture();
            f.Preview.SetAngles(VlatPreviewView.Main, 22.5f, 0); f.Preview.RenderIfNeeded();
            var pixels = VlatKeyformPreviewFixture.Pixels(f.Preview, VlatPreviewView.Main);
            var path = Path.Combine(f.DirectoryPath, "preview.vlat.json"); Assert.That(f.Session.SaveAs(path), Is.True);
            Assert.That(f.Session.Open(path, VlatCloseDecision.Discard), Is.True); f.Session.SelectKeyformGroup(0); f.Connect();
            AssertGeometry(f, .84375, .15625, 0);
            Assert.That(VlatKeyformPreviewFixture.Pixels(f.Preview, VlatPreviewView.Main), Is.EqualTo(pixels));
            Assert.That(f.Session.IsDirty, Is.False);
        }

        /// <summary>Bindingの各要素と空キーを破損させても基準形状を表示し、失敗Meshを残しません。</summary>
        [TestCase("path")] [TestCase("name")] [TestCase("count")] [TestCase("position")]
        [TestCase("topology")] [TestCase("empty")]
        public void InvalidBindingAndEmptyGroup_AreDiagnosedAndRemainNeutral(string kind)
        {
            using var f = new VlatKeyformPreviewFixture();
            var group = f.Session.Document.KeyformGroups[0]; var binding = f.Session.Document.Metadata.MeshBindings[0];
            switch (kind)
            {
                case "path": binding.HierarchyPath = "Missing"; break;
                case "name": binding.MeshName = "Missing"; break;
                case "count": binding.VertexCount++; break;
                case "position": binding.PositionHash = "xxh64:0000000000000000"; break;
                case "topology": group.TopologyHash = "sha256:" + new string('0', 64); break;
                case "empty": group.Keys.Clear(); break;
            }
            VlatAuthoringCommands.SetGenerator(f.Session.Document, "invalid fixture");
            f.Preview.SetDocument(f.Session.Document); Assert.That(f.Preview.RenderIfNeeded(), Is.True);
            Assert.That(f.Preview.State, Is.EqualTo(VlatPreviewState.VLatUnavailable));
            Assert.That(f.Preview.DiagnosticSummary, Does.Contain("not applicable").Or.Contain("Binding不正"));
            Assert.That(f.Renderer.sharedMesh, Is.SameAs(f.Mesh)); AssertGeometry(f, 0, 0, 0);
        }

        /// <summary>MeshRendererは今回のPreviewで対応したと誤認せず、対象を診断します。</summary>
        [Test]
        public void UnsupportedMeshRenderer_IsDiagnosedWithoutAssetMutation()
        {
            using var f = new VlatKeyformPreviewFixture();
            var face = f.Renderer.gameObject; var material = f.Renderer.sharedMaterial;
            f.Preview.SetDocument(null); // binderを先に正規の破棄経路で外す。
            Object.DestroyImmediate(f.Renderer);
            face.AddComponent<MeshFilter>().sharedMesh = f.Mesh;
            face.AddComponent<MeshRenderer>().sharedMaterial = material;
            f.Preview.SetDocument(f.Session.Document);
            Assert.That(f.Preview.RenderIfNeeded(), Is.True);
            Assert.That(f.Preview.State, Is.EqualTo(VlatPreviewState.VLatUnavailable));
            Assert.That(f.Preview.DiagnosticSummary, Does.Contain("SkinnedMeshRenderer"));
            Assert.That(f.Mesh.blendShapeCount, Is.EqualTo(3));
        }

        /// <summary>保存点を跨ぐUndo/Redoは表示とDirtyだけを戻し、保存ファイルは変更しません。</summary>
        [Test]
        public void SavepointUndoRedo_UpdatesPreviewAndDirtyWithoutWritingSource()
        {
            using var f = new VlatKeyformPreviewFixture();
            var path = Path.Combine(f.DirectoryPath, "undo.vlat.json");
            Assert.That(f.Session.SaveAs(path), Is.True); var source = VlatSessionFingerprint.Source(path);
            Assert.That(f.Session.ImportKeyformPositions(0, 0, f.Source.gameObject, f.SourceRenderer, -1), Is.True);
            f.Connect(); AssertGeometry(f, 0, 0, 0); Assert.That(f.Session.IsDirty, Is.True);
            Undo.PerformUndo(); f.Session.RefreshAfterUndoRedo(); f.Connect();
            AssertGeometry(f, 1, 0, 0); Assert.That(f.Session.IsDirty, Is.False);
            Undo.PerformRedo(); f.Session.RefreshAfterUndoRedo(); f.Connect();
            AssertGeometry(f, 0, 0, 0); Assert.That(f.Session.IsDirty, Is.True);
            Assert.That(VlatSessionFingerprint.Source(path), Is.EqualTo(source));
        }

        /// <summary>renderer消失とmesh差し替えは例外を漏らさず診断し、一時Meshを破棄します。</summary>
        [TestCase(true)] [TestCase(false)]
        public void LostTarget_FailsSafelyAndReleasesGeneratedMesh(bool destroyRenderer)
        {
            using var f = new VlatKeyformPreviewFixture();
            var generated = f.Renderer.sharedMesh;
            if (destroyRenderer) Object.DestroyImmediate(f.Renderer);
            else f.Renderer.sharedMesh = f.Mesh;
            f.Preview.SetAngles(VlatPreviewView.Main, 45, 0);
            Assert.That(f.Preview.RenderIfNeeded(), Is.False);
            Assert.That(f.Preview.State, Is.EqualTo(VlatPreviewState.VLatUnavailable));
            Assert.That(generated == null, Is.True);
            Assert.That(f.Mesh.blendShapeCount, Is.EqualTo(3));
            foreach (var pixel in VlatKeyformPreviewFixture.Pixels(f.Preview, VlatPreviewView.Main))
                Assert.That(pixel.r, Is.LessThan(200), "失敗時は前回の補正画像を表示し続けない");
        }

        /// <summary>原本Mesh/Material/BlendShape weightと利用中Sceneを変更せず、disposeで一時資源を全て解放します。</summary>
        [Test]
        public void SourceAssetsAndScene_AreUntouchedAndOwnedResourcesAreReleased()
        {
            using var f = new VlatKeyformPreviewFixture();
            var scene = SceneManager.GetActiveScene(); var dirty = scene.isDirty; var roots = scene.rootCount;
            f.SourceRenderer.SetBlendShapeWeight(0, 17); f.SourceRenderer.SetBlendShapeWeight(1, 23);
            var meshJson = EditorJsonUtility.ToJson(f.Mesh); var materialJson = EditorJsonUtility.ToJson(f.Material);
            var generated = f.Renderer.sharedMesh; var rt = f.Preview.GetTexture(VlatPreviewView.Main);
            var previewScene = f.Preview.PreviewScene; var clone = f.Clone;
            f.Preview.SetExpressionWeight(0, 1); f.Preview.RenderIfNeeded();
            f.Preview.SetGeometrySelectionEnabled(true); f.Preview.RenderIfNeeded(); AssertGeometry(f, 0, 0, 0);
            f.Preview.SetGeometrySelectionEnabled(false); f.Preview.RenderIfNeeded(); AssertGeometry(f, 1, 0, 1);
            Assert.That(f.SourceRenderer.GetBlendShapeWeight(0), Is.EqualTo(17));
            Assert.That(f.SourceRenderer.GetBlendShapeWeight(1), Is.EqualTo(23));
            Assert.That(f.SourceRenderer.sharedMesh, Is.SameAs(f.Mesh)); Assert.That(f.SourceRenderer.sharedMaterial, Is.SameAs(f.Material));
            Assert.That(EditorJsonUtility.ToJson(f.Mesh), Is.EqualTo(meshJson));
            Assert.That(EditorJsonUtility.ToJson(f.Material), Is.EqualTo(materialJson));
            f.Preview.Dispose(); f.Preview.Dispose();
            Assert.That(generated == null, Is.True); Assert.That(rt == null, Is.True); Assert.That(clone == null, Is.True);
            Assert.That(previewScene.IsValid(), Is.False);
            Assert.That(scene.rootCount, Is.EqualTo(roots)); Assert.That(scene.isDirty, Is.EqualTo(dirty));
        }

        /// <summary>実Windowのキー移動ボタンとAfter toggleを操作し、CloseでMeshとRTを解放します。</summary>
        [UnityTest]
        public IEnumerator WindowControls_MoveCameraToggleCorrectionAndReleaseOnClose()
        {
            using var f = new VlatKeyformPreviewFixture();
            var window = VlatEditorWindow.CreateForRecoveryDirectory(Path.Combine(f.DirectoryPath, "window"));
            try
            {
                window.Show(); yield return null;
                f.Seed(window.DocumentController); window.CreateGUI();
                var revision = window.DocumentController.Document.Revision;
                var keyButton = window.rootVisualElement.Q<Button>("keyform-camera-SubA"); Assert.That(keyButton, Is.Not.Null);
                Assert.That(keyButton.enabledSelf, Is.False);
                f.Adopt(window.PreviewController);
                window.rootVisualElement.Q<Slider>("preview-yaw").value = 15;
                Assert.That(keyButton.enabledSelf, Is.True, "Inspector再生成なしの読込後も操作できる");
                Click(keyButton); Assert.That(window.PreviewController.GetEvaluatedYaw(VlatPreviewView.SubA), Is.EqualTo(90).Within(.0001));
                window.rootVisualElement.Q<Toggle>("preview-after-toggle").value = false;
                Assert.That(window.PreviewController.ApplicationEnabled, Is.False);
                Assert.That(window.DocumentController.Document.Revision, Is.EqualTo(revision));
                var clearedMesh = f.Renderer.sharedMesh;
                Click(window.rootVisualElement.Q<Button>("clear-vrm-button"));
                Assert.That(clearedMesh == null, Is.True); Assert.That(keyButton.enabledSelf, Is.False);
                f.Adopt(window.PreviewController);
                window.rootVisualElement.Q<Slider>("preview-yaw").value = 20;
                Assert.That(keyButton.enabledSelf, Is.True);
                var generated = f.Renderer.sharedMesh; var rt = window.PreviewController.GetTexture(VlatPreviewView.Main);
                window.DiscardChanges(); window.Close(); yield return null;
                Assert.That(generated == null, Is.True); Assert.That(rt == null, Is.True);
            }
            finally { if (window != null) { window.DiscardChanges(); window.Close(); } }
        }

        private static void Click(Button button)
        {
            using var navigation = NavigationSubmitEvent.GetPooled(); navigation.target = button; button.SendEvent(navigation);
        }

        private static void AssertGeometry(VlatKeyformPreviewFixture f, double wide, double deep, double expression)
        {
            var vertices = f.Bake(); var source = f.Mesh.vertices;
            for (var i = 0; i < source.Length; i++)
            {
                var expected = source[i] + new Vector3((i % 2 == 0 ? -.25f : .25f) * (float)wide,
                    ((i & 2) == 0 ? -.15f : .15f) * (float)expression, ((i & 4) == 0 ? -.3f : .3f) * (float)deep);
                Assert.That(Vector3.Distance(vertices[i], expected), Is.LessThan(2e-5f), "vertex " + i);
            }
        }
    }
}
