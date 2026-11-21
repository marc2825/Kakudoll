using NUnit.Framework;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UniGLTF.Extensions.VRMC_vrm;
using UniVRM10;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;
using VLat.Editor.Authoring;
using VLat.Runtime;
using Object = UnityEngine.Object;

namespace VLat.Tests.Edit
{
    /// <summary>Phase 2C previewのcamera state、scene ownership、cleanupを検証します。</summary>
    public sealed class VlatPreviewControllerTests
    {
        /// <summary>Main/Sub A/Sub Bの既定角度と操作状態が独立していることを検証します。</summary>
        [Test]
        public void ViewStates_HaveIndependentDefaultsAndNavigation()
        {
            var main = new VlatPreviewViewState(0f);
            var subA = new VlatPreviewViewState(45f);
            var subB = new VlatPreviewViewState(90f);

            main.Orbit(new Vector2(20f, 10f));
            main.PanBy(new Vector2(5f, -3f));
            main.Zoom(2f);

            Assert.That(main.Yaw, Is.Not.EqualTo(0f));
            Assert.That(main.Pitch, Is.Not.EqualTo(0f));
            Assert.That(main.Pan, Is.Not.EqualTo(Vector2.zero));
            Assert.That(subA.Yaw, Is.EqualTo(45f));
            Assert.That(subB.Yaw, Is.EqualTo(90f));

            main.Reset();
            Assert.That(main.Yaw, Is.EqualTo(0f));
            Assert.That(main.Pitch, Is.EqualTo(0f));
            Assert.That(main.Pan, Is.EqualTo(Vector2.zero));
        }

        /// <summary>Preview scene作成・RT resize・Disposeが利用中sceneを汚さず完結することを検証します。</summary>
        [Test]
        public void Lifecycle_OwnsPreviewSceneAndRenderTexturesWithoutDirtyingSourceScene()
        {
            var sourceScene = SceneManager.GetActiveScene();
            var wasDirty = sourceScene.isDirty;
            var controller = new VlatPreviewController();
            var previewScene = controller.PreviewScene;
            try
            {
                Assert.That(
                    typeof(EditorSceneManager).GetMethod(
                        "ClearSceneDirtiness",
                        BindingFlags.Static | BindingFlags.NonPublic),
                    Is.Not.Null,
                    "Unity 2022.3 scene dirtiness restoration API must remain available.");
                Assert.That(previewScene.IsValid(), Is.True);
                Assert.That(EditorSceneManager.IsPreviewScene(previewScene), Is.True);
                Assert.That(previewScene, Is.Not.EqualTo(sourceScene));

                controller.ResizeView(VlatPreviewView.Main, 320f, 180f, 1f);
                var texture = controller.GetTexture(VlatPreviewView.Main) as RenderTexture;
                Assert.That(texture, Is.Not.Null);
                Assert.That(texture.width, Is.EqualTo(320));
                Assert.That(texture.height, Is.EqualTo(180));
                Assert.That(controller.ResourceSummary, Does.Contain("avatar 0"));

                Assert.DoesNotThrow(() => controller.SetExpressionWeight(0, 1f));
                Assert.DoesNotThrow(controller.ResetExpressions);
                Assert.That(sourceScene.isDirty, Is.EqualTo(wasDirty));

                controller.ClearAvatar();
                Assert.That(controller.GetTexture(VlatPreviewView.Main), Is.Null);
                Assert.That(controller.ResourceSummary, Does.Contain("render textures 0"));
            }
            finally
            {
                controller.Dispose();
            }

            Assert.That(previewScene.IsValid(), Is.False);
            Assert.That(sourceScene.isDirty, Is.EqualTo(wasDirty));
        }

        /// <summary>再resizeは旧RenderTextureを破棄し、新sizeだけを所有することを検証します。</summary>
        [Test]
        public void Resize_ReplacesOwnedRenderTexture()
        {
            var controller = new VlatPreviewController();
            try
            {
                controller.ResizeView(VlatPreviewView.SubA, 200f, 100f, 1f);
                var first = controller.GetTexture(VlatPreviewView.SubA) as RenderTexture;
                controller.ResizeView(VlatPreviewView.SubA, 400f, 240f, 1f);
                var second = controller.GetTexture(VlatPreviewView.SubA) as RenderTexture;

                Assert.That(first, Is.Not.SameAs(second));
                Assert.That(first == null, Is.True);
                Assert.That(second.width, Is.EqualTo(400));
                Assert.That(second.height, Is.EqualTo(240));
            }
            finally
            {
                controller.Dispose();
            }
        }

        /// <summary>PreviewControllerの反復create/disposeでpreview sceneを蓄積しないことを検証します。</summary>
        [Test]
        public void Lifecycle_RepeatedCreateDisposeClosesEveryPreviewScene()
        {
            for (var iteration = 0; iteration < 3; iteration++)
            {
                var controller = new VlatPreviewController();
                var scene = controller.PreviewScene;
                Assert.That(scene.IsValid(), Is.True);
                controller.Dispose();
                Assert.That(scene.IsValid(), Is.False);
            }
        }

        /// <summary>存在しないVRMのload失敗がscene・preview ownershipを変更しないことを検証します。</summary>
        [Test]
        public async Task LoadFailure_PreservesSourceSceneAndReportsDiagnostic()
        {
            var sourceScene = SceneManager.GetActiveScene();
            var wasDirty = sourceScene.isDirty;
            var rootCount = sourceScene.rootCount;
            var controller = new VlatPreviewController();
            try
            {
                var missing = Path.Combine(
                    Path.GetTempPath(),
                    "vlat-missing-" + System.Guid.NewGuid().ToString("N") + ".vrm");
                var result = await controller.LoadAvatarAsync(missing);

                Assert.That(result, Is.False);
                Assert.That(controller.HasAvatar, Is.False);
                Assert.That(controller.DiagnosticSummary, Does.Contain("not found"));
                Assert.That(sourceScene.rootCount, Is.EqualTo(rootCount));
                Assert.That(sourceScene.isDirty, Is.EqualTo(wasDirty));
            }
            finally
            {
                controller.Dispose();
            }
        }

        /// <summary>未保存Authoringを一時fileなしでruntime互換immutable snapshotへpackすることを検証します。</summary>
        [Test]
        public void EvaluationSnapshot_IsInMemoryAndDoesNotWriteTemporaryVlatFiles()
        {
            var document = VlatAuthoringPersistence.CreateNew("Preview Test Avatar");
            var unexpectedPath = Path.GetFullPath("preview.vlat.bin");
            var existedBefore = File.Exists(unexpectedPath);
            // in-memory snapshotは編集検証と同じ既定URIを使う。そのURIのfileも作らない。
            var defaultUriPath = Path.GetFullPath("document.vlat.bin");
            var defaultExistedBefore = File.Exists(defaultUriPath);
            try
            {
                var result = VlatPreviewController.CreateEvaluationSnapshot(document);

                Assert.That(result.Success, Is.True);
                Assert.That(result.Document, Is.Not.Null);
                Assert.That(result.Buffers.Count, Is.Zero);
                Assert.That(File.Exists(unexpectedPath), Is.EqualTo(existedBefore));
                Assert.That(File.Exists(defaultUriPath), Is.EqualTo(defaultExistedBefore));
            }
            finally
            {
                Object.DestroyImmediate(document);
            }
        }

        /// <summary>Authoring revisionとUndo復元をpreview invalidationへ接続し、camera/expression操作ではdirtyにしないことを検証します。</summary>
        [Test]
        public void Invalidation_TracksDocumentRevisionButSessionControlsDoNotDirtyDocument()
        {
            Undo.ClearAll();
            var document = VlatAuthoringPersistence.CreateNew("Invalidation Test");
            var controller = new VlatPreviewController();
            try
            {
                controller.SetDocument(document);
                var firstInvalidation = controller.DocumentInvalidationCount;
                var initialRevision = document.Revision;

                controller.Orbit(VlatPreviewView.Main, new Vector2(4f, 2f));
                controller.SetExpressionWeight(0, 0.5f);
                Assert.That(document.Revision, Is.EqualTo(initialRevision));
                Assert.That(document.IsDirty, Is.False);
                Assert.That(controller.DocumentInvalidationCount, Is.EqualTo(firstInvalidation));

                VlatAuthoringCommands.SetGenerator(document, "Edited Generator");
                Undo.FlushUndoRecordObjects();
                controller.SetDocument(document);
                Assert.That(controller.DocumentInvalidationCount, Is.EqualTo(firstInvalidation + 1));

                Undo.PerformUndo();
                controller.SetDocument(document);
                Assert.That(controller.DocumentInvalidationCount, Is.EqualTo(firstInvalidation + 2));
            }
            finally
            {
                controller.Dispose();
                Object.DestroyImmediate(document);
                Undo.ClearAll();
            }
        }

        /// <summary>
        /// VRM-like instanceの初期化、serialized Expression列挙、camera refreshが
        /// Vrm10Runtimeを生成せず、VRM Expression semanticsを一時複製へだけ適用することを検証します。
        /// </summary>
        [Test]
        public void VrmLikePreview_StaysOnEditorSafePathAndPreservesExpressionSemantics()
        {
            var fixture = SyntheticExpressionAvatar.Create();
            var controller = new VlatPreviewController();
            var runtimeField = GetRuntimeField();
            try
            {
                Assert.That(runtimeField.GetValue(fixture.Instance), Is.Null);

                controller.AdoptAvatar(fixture.Instance, "synthetic-expression.vrm");
                Assert.That(controller.ExpressionNames, Is.EqualTo(new[] { "happy", "aa", "neutral" }));
                Assert.That(runtimeField.GetValue(fixture.Instance), Is.Null,
                    "Expression enumeration must not access Vrm10Instance.Runtime.");

                var previewMaterial = fixture.Renderer.sharedMaterial;
                Assert.That(previewMaterial, Is.Not.SameAs(fixture.SourceMaterial));

                controller.SetExpressionWeight(1, 1f);
                Assert.That(controller.RenderIfNeeded(), Is.True);
                Assert.That(fixture.Renderer.GetBlendShapeWeight(1), Is.EqualTo(100f).Within(0.001f));

                controller.SetExpressionWeight(0, 0.5f);
                Assert.That(controller.RenderIfNeeded(), Is.True);
                Assert.That(fixture.Renderer.GetBlendShapeWeight(0), Is.EqualTo(40f).Within(0.001f));
                Assert.That(fixture.Renderer.GetBlendShapeWeight(1), Is.Zero.Within(0.001f),
                    "happy.OverrideMouth=block must suppress the aa preset.");
                Assert.That(previewMaterial.color, Is.EqualTo(new Color(1f, 0.5f, 0.5f, 1f))
                    .Using(ColorEqualityComparer.Instance));
                Assert.That(previewMaterial.mainTextureScale,
                    Is.EqualTo(new Vector2(1.5f, 1.5f)).Using(Vector2EqualityComparer.Instance));
                Assert.That(previewMaterial.mainTextureOffset,
                    Is.EqualTo(new Vector2(0.125f, 0.25f)).Using(Vector2EqualityComparer.Instance));

                controller.SetAngles(VlatPreviewView.Main, -25f, 12f);
                controller.Orbit(VlatPreviewView.SubA, new Vector2(4f, -3f));
                controller.Pan(VlatPreviewView.SubB, new Vector2(2f, 1f));
                controller.Zoom(VlatPreviewView.Main, -1f);
                Assert.That(controller.RenderIfNeeded(), Is.True);
                Assert.That(runtimeField.GetValue(fixture.Instance), Is.Null,
                    "Angle/camera refresh must not create or process Vrm10Runtime.");

                controller.ResetExpressions();
                Assert.That(controller.RenderIfNeeded(), Is.True);
                Assert.That(fixture.Renderer.GetBlendShapeWeight(0), Is.Zero.Within(0.001f));
                Assert.That(fixture.Renderer.GetBlendShapeWeight(1), Is.Zero.Within(0.001f));
                Assert.That(previewMaterial.color, Is.EqualTo(Color.white)
                    .Using(ColorEqualityComparer.Instance));
                Assert.That(runtimeField.GetValue(fixture.Instance), Is.Null);

                controller.SetExpressionWeight(2, 0.49f);
                Assert.That(controller.RenderIfNeeded(), Is.True);
                Assert.That(fixture.Renderer.GetBlendShapeWeight(0), Is.Zero.Within(0.001f));
                controller.SetExpressionWeight(2, 0.51f);
                Assert.That(controller.RenderIfNeeded(), Is.True);
                Assert.That(fixture.Renderer.GetBlendShapeWeight(0), Is.EqualTo(25f).Within(0.001f),
                    "A binary expression must round its input to the VRM-defined 0/1 result.");
                controller.ResetExpressions();
                Assert.That(controller.RenderIfNeeded(), Is.True);

                Assert.That(fixture.SourceMaterial.color, Is.EqualTo(Color.white)
                    .Using(ColorEqualityComparer.Instance));
                Assert.That(fixture.SourceMaterial.mainTextureScale,
                    Is.EqualTo(Vector2.one).Using(Vector2EqualityComparer.Instance));
                Assert.That(fixture.SourceMaterial.mainTextureOffset,
                    Is.EqualTo(Vector2.zero).Using(Vector2EqualityComparer.Instance));
                Assert.That(fixture.Renderer.sharedMesh, Is.SameAs(fixture.SourceMesh));

                controller.ClearAvatar();
                Assert.That(previewMaterial == null, Is.True,
                    "Editor-owned expression material clone must be destroyed on clear.");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                controller.Dispose();
                fixture.Dispose();
            }
        }

        /// <summary>
        /// Geometry Selection ModeがExpressionとVLat After stateを退避し、neutral表示後に復元することを検証します。
        /// </summary>
        [Test]
        public void GeometrySelectionMode_StashesNeutralizesAndRestoresPreviewState()
        {
            var fixture = SyntheticExpressionAvatar.Create();
            var controller = new VlatPreviewController();
            var runtimeField = GetRuntimeField();
            try
            {
                controller.ResizeView(VlatPreviewView.Main, 200f, 200f, 1f);
                controller.AdoptAvatar(fixture.Instance, "synthetic-selection.vrm");
                controller.SetExpressionWeight(0, 0.75f);
                Assert.That(controller.RenderIfNeeded(), Is.True);
                Assert.That(fixture.Renderer.GetBlendShapeWeight(0), Is.EqualTo(60f).Within(0.001f));
                Assert.That(controller.ApplicationEnabled, Is.True);

                controller.SetGeometrySelectionEnabled(true);
                Assert.That(controller.RenderIfNeeded(), Is.True);
                Assert.That(controller.GeometrySelectionEnabled, Is.True);
                Assert.That(controller.ApplicationEnabled, Is.False);
                Assert.That(controller.GetExpressionWeight(0), Is.Zero);
                Assert.That(fixture.Renderer.GetBlendShapeWeight(0), Is.Zero.Within(0.001f));
                Assert.That(controller.VertexTargets.Count, Is.EqualTo(1));
                Assert.That(controller.GetVertexSelectionOverlay(), Is.Not.Null);

                controller.SetExpressionWeight(0, 1f);
                controller.Orbit(VlatPreviewView.Main, new Vector2(4f, -2f));
                Assert.That(controller.RenderIfNeeded(), Is.True);
                Assert.That(controller.GetExpressionWeight(0), Is.Zero,
                    "geometry editing中のExpression入力はneutral contractを変更しません。");
                Assert.That(runtimeField.GetValue(fixture.Instance), Is.Null);

                controller.SetGeometrySelectionEnabled(false);
                Assert.That(controller.RenderIfNeeded(), Is.True);
                Assert.That(controller.ApplicationEnabled, Is.True);
                Assert.That(controller.GetExpressionWeight(0), Is.EqualTo(0.75f).Within(0.001f));
                Assert.That(fixture.Renderer.GetBlendShapeWeight(0), Is.EqualTo(60f).Within(0.001f));
                Assert.That(controller.GetVertexSelectionOverlay(), Is.Null);
                Assert.That(runtimeField.GetValue(fixture.Instance), Is.Null);

                controller.SetGeometrySelectionEnabled(true);
                Assert.That(controller.RenderIfNeeded(), Is.True);
                var ownedOverlay = controller.GetVertexSelectionOverlay();
                Assert.That(ownedOverlay, Is.Not.Null);
                controller.ClearAvatar();
                Assert.That(ownedOverlay == null, Is.True,
                    "avatar clear must destroy the Editor-owned selection overlay.");
                Assert.That(controller.VertexTargets, Is.Empty);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                controller.Dispose();
                fixture.Dispose();
            }
        }

        /// <summary>
        /// avatarの反復adopt/clearとcontroller disposeでruntime warningや所有資源の蓄積がないことを検証します。
        /// </summary>
        [Test]
        public void VrmLikePreview_RepeatedAdoptClearDisposeHasNoRuntimeWarningOrOwnedLeak()
        {
            var runtimeField = GetRuntimeField();
            var controller = new VlatPreviewController();
            try
            {
                for (var iteration = 0; iteration < 3; iteration++)
                {
                    var fixture = SyntheticExpressionAvatar.Create();
                    try
                    {
                        controller.AdoptAvatar(
                            fixture.Instance,
                            "synthetic-expression-" + iteration + ".vrm");
                        var previewMaterial = fixture.Renderer.sharedMaterial;

                        controller.SetExpressionWeight(0, 1f);
                        controller.SetAngles(VlatPreviewView.Main, iteration * 10f, 0f);
                        Assert.That(controller.RenderIfNeeded(), Is.True);
                        Assert.That(runtimeField.GetValue(fixture.Instance), Is.Null);
                        Assert.That(fixture.SourceMaterial.color, Is.EqualTo(Color.white)
                            .Using(ColorEqualityComparer.Instance));
                        Assert.That(fixture.Renderer.sharedMesh, Is.SameAs(fixture.SourceMesh));

                        controller.ClearAvatar();
                        Assert.That(controller.HasAvatar, Is.False);
                        Assert.That(previewMaterial == null, Is.True);
                    }
                    finally
                    {
                        fixture.Dispose();
                    }
                }

                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                controller.Dispose();
            }
        }

        /// <summary>
        /// VLat未選択または0 LayerのNew documentではmanual Runtime初期化を試さず、
        /// UpdateType=Noneのavatar previewとExpressionを維持することを検証します。
        /// </summary>
        [Test]
        public void AvatarOnly_EmptyAndZeroLayerDocumentsDoNotEmitRuntimeLifecycleDiagnostic()
        {
            var fixture = SyntheticExpressionAvatar.Create();
            var document = VlatAuthoringPersistence.CreateNew("Empty Preview");
            var controller = new VlatPreviewController();
            try
            {
                controller.AdoptAvatar(fixture.Instance, "synthetic-empty.vrm");
                Assert.That(fixture.Instance.UpdateType, Is.EqualTo(Vrm10Instance.UpdateTypes.None));
                Assert.That(controller.State, Is.EqualTo(VlatPreviewState.AvatarOnly));
                Assert.That(controller.DiagnosticSummary, Does.Contain("Avatar loaded"));
                Assert.That(controller.DiagnosticSummary, Does.Not.Contain("UpdateType"));

                controller.SetDocument(document);
                Assert.That(controller.RenderIfNeeded(), Is.True);
                Assert.That(controller.State, Is.EqualTo(VlatPreviewState.AvatarOnly));
                Assert.That(controller.DiagnosticSummary, Does.Contain("document has no layers"));
                Assert.That(controller.DiagnosticSummary, Does.Not.Contain("ERROR"));
                Assert.That(controller.VlatDiagnostics, Is.Empty);

                controller.SetExpressionWeight(0, 1f);
                controller.Orbit(VlatPreviewView.Main, new Vector2(3f, -2f));
                Assert.That(controller.RenderIfNeeded(), Is.True);
                Assert.That(fixture.Renderer.GetBlendShapeWeight(0), Is.EqualTo(80f).Within(0.001f));
                Assert.That(fixture.SourceMaterial.color, Is.EqualTo(Color.white)
                    .Using(ColorEqualityComparer.Instance));
                Assert.That(fixture.Renderer.sharedMesh, Is.SameAs(fixture.SourceMesh));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                controller.Dispose();
                Object.DestroyImmediate(document);
                fixture.Dispose();
            }
        }

        /// <summary>
        /// unresolved bindingをnon-fatalなVLatUnavailableとして保持し、avatar、camera、
        /// Expression previewをbaselineのまま継続することを検証します。
        /// </summary>
        [Test]
        public void UnresolvedBinding_FallsBackToAvatarAndRetainsRebindDiagnostic()
        {
            var fixture = SyntheticExpressionAvatar.Create();
            var document = CreatePreviewDocument(fixture, false);
            var controller = new VlatPreviewController();
            try
            {
                controller.SetDocument(document);
                controller.AdoptAvatar(fixture.Instance, "synthetic-unresolved.vrm");

                Assert.That(controller.State, Is.EqualTo(VlatPreviewState.VLatUnavailable));
                Assert.That(controller.HasAvatar, Is.True);
                Assert.That(controller.BindingReport, Is.Not.Null);
                Assert.That(controller.BindingReport.SuccessfulCount, Is.Zero);
                Assert.That(controller.BindingReport[0].IsSuccess, Is.False);
                Assert.That(controller.VlatDiagnostics, Is.Not.Empty);
                Assert.That(controller.DiagnosticSummary, Does.Contain("rebind required"));
                Assert.That(controller.DiagnosticSummary, Does.Contain("再バインド規則"));
                Assert.That(controller.DiagnosticSummary, Does.Not.Contain("ERROR"));
                Assert.That(controller.DiagnosticSummary, Does.Not.Contain("UpdateType"));

                controller.SetAngles(VlatPreviewView.Main, -30f, 10f);
                controller.Pan(VlatPreviewView.SubA, new Vector2(2f, 1f));
                controller.Zoom(VlatPreviewView.SubB, -1f);
                controller.SetExpressionWeight(1, 1f);
                Assert.That(controller.RenderIfNeeded(), Is.True);
                Assert.That(controller.State, Is.EqualTo(VlatPreviewState.VLatUnavailable));
                Assert.That(fixture.Renderer.GetBlendShapeWeight(1), Is.EqualTo(100f).Within(0.001f));
                Assert.That(fixture.SourceMaterial.color, Is.EqualTo(Color.white)
                    .Using(ColorEqualityComparer.Instance));
                Assert.That(fixture.Renderer.sharedMesh, Is.SameAs(fixture.SourceMesh));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                controller.Dispose();
                Object.DestroyImmediate(document);
                fixture.Dispose();
            }
        }

        /// <summary>
        /// applicable documentではavatar root/head/cameraを渡すmanual overloadだけで
        /// AvatarWithVLatへ遷移し、Vrm10Instance runtime lifecycleを保持しないことを検証します。
        /// </summary>
        [Test]
        public void ApplicableDocument_UsesManualInitializationAndBecomesAvatarWithVLat()
        {
            var fixture = SyntheticExpressionAvatar.Create();
            var document = CreatePreviewDocument(fixture, true);
            var controller = new VlatPreviewController();
            try
            {
                controller.SetDocument(document);
                controller.AdoptAvatar(fixture.Instance, "synthetic-applicable.vrm");

                Assert.That(controller.State, Is.EqualTo(VlatPreviewState.AvatarWithVLat));
                Assert.That(controller.BindingReport, Is.Not.Null);
                Assert.That(controller.BindingReport.AllSucceeded, Is.True);
                Assert.That(controller.DiagnosticSummary, Does.Contain("VLat applied"));
                Assert.That(controller.DiagnosticSummary, Does.Not.Contain("UpdateType"));
                Assert.That(ContainsRuntimeMessage(controller.VlatDiagnostics, "UpdateType"), Is.False);

                var applier = fixture.Instance.GetComponent<VlatApplier>();
                Assert.That(applier, Is.Not.Null);
                var vrmField = typeof(VlatApplier).GetField(
                    "_vrmInstance",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(vrmField, Is.Not.Null);
                Assert.That(vrmField.GetValue(applier), Is.Null,
                    "Editor Preview must use the VRM-agnostic manual initialization overload.");
                Assert.That(fixture.Instance.UpdateType, Is.EqualTo(Vrm10Instance.UpdateTypes.None));
                Assert.That(fixture.SourceMaterial.color, Is.EqualTo(Color.white)
                    .Using(ColorEqualityComparer.Instance));
                Assert.That(fixture.Renderer.sharedMesh, Is.SameAs(fixture.SourceMesh));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                controller.Dispose();
                Object.DestroyImmediate(document);
                fixture.Dispose();
            }
        }

        /// <summary>
        /// binding編集中でSave validationを通らないAuthoring ModelもPreview全体の失敗にせず、
        /// baseline avatarへfallbackすることを検証します。
        /// </summary>
        [Test]
        public void IncompleteAuthoring_FallsBackWithoutWeakeningSaveValidation()
        {
            var fixture = SyntheticExpressionAvatar.Create();
            var document = VlatAuthoringPersistence.CreateNew("Incomplete Preview");
            document.Bands.Add(new VlatAuthoringBand
            {
                Id = "front",
                Name = "Front",
                YawMin = -45d,
                YawMax = 45d,
                PitchMin = -30d,
                PitchMax = 30d,
                FeatherYaw = 5d,
                FeatherPitch = 5d,
                Hysteresis = 0d,
            });
            var layer = new VlatAuthoringLayer
            {
                Id = "incomplete",
                Name = "Incomplete",
                Tag = "other",
                Enabled = true,
                Priority = 0,
                BandId = "front",
            };
            layer.Modifiers.Add(new VlatAuthoringVisibilityModifier
            {
                MeshBinding = 0,
                SubMesh = 0,
                VisibleInBand = true,
            });
            document.Layers.Add(layer);

            var controller = new VlatPreviewController();
            try
            {
                Assert.That(VlatAuthoringPersistence.Pack(document).Success, Is.False,
                    "Save/canonical pack validation must remain strict.");
                controller.SetDocument(document);
                controller.AdoptAvatar(fixture.Instance, "synthetic-incomplete.vrm");

                Assert.That(controller.State, Is.EqualTo(VlatPreviewState.VLatUnavailable));
                Assert.That(controller.HasAvatar, Is.True);
                Assert.That(controller.BindingReport, Is.Null);
                Assert.That(controller.DiagnosticSummary, Does.Contain("VLat not applicable"));
                Assert.That(controller.DiagnosticSummary, Does.Not.Contain("ERROR"));
                controller.SetExpressionWeight(0, 0.5f);
                controller.ResetView(VlatPreviewView.Main);
                Assert.That(controller.RenderIfNeeded(), Is.True);
                Assert.That(fixture.Renderer.GetBlendShapeWeight(0), Is.EqualTo(40f).Within(0.001f));
                Assert.That(fixture.SourceMaterial.color, Is.EqualTo(Color.white)
                    .Using(ColorEqualityComparer.Instance));
                Assert.That(fixture.Renderer.sharedMesh, Is.SameAs(fixture.SourceMesh));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                controller.Dispose();
                Object.DestroyImmediate(document);
                fixture.Dispose();
            }
        }

        /// <summary>
        /// Preview Scene固有のculling mask、framing、RenderTexture割当を固定し、graphics deviceが
        /// 利用可能な環境では既知geometryがclear color以外のpixelを生成することを検証します。
        /// </summary>
        [Test]
        public void RenderSanity_FramesKnownGeometryAndUsesPreviewSceneCullingMask()
        {
            var fixture = SyntheticExpressionAvatar.Create();
            var controller = new VlatPreviewController();
            try
            {
                controller.ResizeView(VlatPreviewView.Main, 192f, 192f, 1f);
                controller.AdoptAvatar(fixture.Instance, "synthetic-render.vrm");

                var scene = controller.PreviewScene;
                var roots = scene.GetRootGameObjects();
                Camera mainCamera = null;
                var lightCount = 0;
                for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                {
                    if (roots[rootIndex].name == "VLat Preview Camera 0")
                    {
                        mainCamera = roots[rootIndex].GetComponent<Camera>();
                    }

                    lightCount += roots[rootIndex].GetComponentsInChildren<Light>(true).Length;
                }

                Assert.That(mainCamera, Is.Not.Null);
                var target = controller.GetTexture(VlatPreviewView.Main) as RenderTexture;
                Assert.That(target, Is.Not.Null);
                Assert.That(target.IsCreated(), Is.True);
                Assert.That(mainCamera.targetTexture, Is.SameAs(target));
                Assert.That(mainCamera.gameObject.scene, Is.EqualTo(scene));
                Assert.That(fixture.Instance.gameObject.scene, Is.EqualTo(scene));
                Assert.That(lightCount, Is.EqualTo(2));
                Assert.That(mainCamera.cullingMask, Is.EqualTo(~0));
                var sceneMask = EditorSceneManager.GetSceneCullingMask(scene);
                Assert.That(sceneMask, Is.Not.Zero);
                Assert.That(mainCamera.overrideSceneCullingMask, Is.EqualTo(sceneMask));
                Assert.That(
                    mainCamera.cullingMask & (1 << fixture.Renderer.gameObject.layer),
                    Is.Not.Zero);

                var bounds = fixture.Renderer.bounds;
                Assert.That(IsFinite(bounds.center), Is.True);
                Assert.That(IsFinite(bounds.extents), Is.True);
                Assert.That(bounds.size.sqrMagnitude, Is.GreaterThan(0f));
                Assert.That(
                    GeometryUtility.TestPlanesAABB(
                        GeometryUtility.CalculateFrustumPlanes(mainCamera),
                        bounds),
                    Is.True);

                if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                {
                    var readback = Readback(target, mainCamera.backgroundColor);
                    Assert.That(readback.NonClearPixels, Is.GreaterThan(32));
                }

                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                controller.Dispose();
                fixture.Dispose();
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static RenderReadback Readback(RenderTexture target, Color clearColor)
        {
            Assert.That(target, Is.Not.Null);
            var previous = RenderTexture.active;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            var expected = (Color32)clearColor;
            try
            {
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
                texture.Apply(false, false);
                var pixels = texture.GetPixels32();
                var clear = 0;
                var nonClear = 0;
                var minimum = byte.MaxValue;
                var maximum = byte.MinValue;
                for (var index = 0; index < pixels.Length; index++)
                {
                    var pixel = pixels[index];
                    minimum = Math.Min(minimum, Math.Min(pixel.r, Math.Min(pixel.g, pixel.b)));
                    maximum = Math.Max(maximum, Math.Max(pixel.r, Math.Max(pixel.g, pixel.b)));
                    if (Math.Abs(pixel.r - expected.r) <= 2 &&
                        Math.Abs(pixel.g - expected.g) <= 2 &&
                        Math.Abs(pixel.b - expected.b) <= 2)
                    {
                        clear++;
                    }
                    else
                    {
                        nonClear++;
                    }
                }

                return new RenderReadback(pixels.Length, clear, nonClear, minimum, maximum);
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(texture);
            }
        }

        private readonly struct RenderReadback
        {
            internal RenderReadback(int totalPixels, int clearPixels, int nonClearPixels, int minimum, int maximum)
            {
                TotalPixels = totalPixels;
                ClearPixels = clearPixels;
                NonClearPixels = nonClearPixels;
                Minimum = minimum;
                Maximum = maximum;
            }

            internal int TotalPixels { get; }
            internal int ClearPixels { get; }
            internal int NonClearPixels { get; }
            internal int Minimum { get; }
            internal int Maximum { get; }
        }

        private static VlatAuthoringDocument CreatePreviewDocument(
            SyntheticExpressionAvatar fixture,
            bool applicable)
        {
            var document = VlatAuthoringPersistence.CreateNew("Preview Binding");
            document.Metadata.MeshBindings.Add(new VlatAuthoringMeshBinding
            {
                HierarchyPath = applicable ? "Face" : "MissingFace",
                MeshName = applicable ? fixture.SourceMesh.name : "MissingMesh",
                VertexCount = fixture.SourceMesh.vertexCount,
                PositionHash = applicable
                    ? PositionHashUtility.Calculate(fixture.SourceMesh)
                    : "xxh64:0000000000000000",
            });
            document.Bands.Add(new VlatAuthoringBand
            {
                Id = "front",
                Name = "Front",
                YawMin = -45d,
                YawMax = 45d,
                PitchMin = -30d,
                PitchMax = 30d,
                FeatherYaw = 5d,
                FeatherPitch = 5d,
                Hysteresis = 0d,
            });
            var layer = new VlatAuthoringLayer
            {
                Id = "visibility",
                Name = "Visibility",
                Tag = "other",
                Enabled = true,
                Priority = 0,
                BandId = "front",
            };
            layer.Modifiers.Add(new VlatAuthoringVisibilityModifier
            {
                MeshBinding = 0,
                SubMesh = 0,
                VisibleInBand = true,
            });
            document.Layers.Add(layer);
            return document;
        }

        private static bool ContainsRuntimeMessage(
            System.Collections.Generic.IReadOnlyList<VlatRuntimeDiagnostic> diagnostics,
            string value)
        {
            for (var index = 0; index < diagnostics.Count; index++)
            {
                if (diagnostics[index] != null && diagnostics[index].Message.Contains(value))
                {
                    return true;
                }
            }

            return false;
        }

        private static FieldInfo GetRuntimeField()
        {
            var field = typeof(Vrm10Instance).GetField(
                "m_runtime",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "UniVRM 0.128.3 Vrm10Instance runtime field contract changed.");
            return field;
        }

        private sealed class SyntheticExpressionAvatar : IDisposable
        {
            private SyntheticExpressionAvatar()
            {
            }

            internal Vrm10Instance Instance { get; private set; }

            internal SkinnedMeshRenderer Renderer { get; private set; }

            internal Mesh SourceMesh { get; private set; }

            internal Material SourceMaterial { get; private set; }

            private VRM10Object Vrm { get; set; }

            private VRM10Expression Happy { get; set; }

            private VRM10Expression Aa { get; set; }

            private VRM10Expression Neutral { get; set; }

            internal static SyntheticExpressionAvatar Create()
            {
                var fixture = new SyntheticExpressionAvatar();
                var root = new GameObject("Synthetic VRM Root");
                fixture.Instance = root.AddComponent<Vrm10Instance>();

                var head = new GameObject("Head");
                head.transform.SetParent(root.transform, false);
                head.transform.localPosition = new Vector3(0f, 1.5f, 0f);
                // 同名の非Component型に依存する文字列検索を避け、固定版UniVRMの公開APIを使用します。
                var humanoid = fixture.Instance.Humanoid;
                Assert.That(humanoid, Is.Not.Null);
                var serializedHumanoid = new SerializedObject(humanoid);
                serializedHumanoid.FindProperty("m_Head").objectReferenceValue = head.transform;
                serializedHumanoid.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(
                    fixture.Instance.TryGetBoneTransform(HumanBodyBones.Head, out var resolvedHead),
                    Is.True);
                Assert.That(resolvedHead, Is.SameAs(head.transform));

                var face = new GameObject("Face");
                face.transform.SetParent(root.transform, false);
                fixture.Renderer = face.AddComponent<SkinnedMeshRenderer>();

                fixture.SourceMesh = CreateMesh();
                fixture.Renderer.sharedMesh = fixture.SourceMesh;
                var shader = Shader.Find("Standard");
                Assert.That(shader, Is.Not.Null);
                fixture.SourceMaterial = new Material(shader)
                {
                    name = "SyntheticFaceMaterial",
                    color = Color.white,
                };
                fixture.SourceMaterial.mainTextureScale = Vector2.one;
                fixture.SourceMaterial.mainTextureOffset = Vector2.zero;
                fixture.Renderer.sharedMaterial = fixture.SourceMaterial;

                fixture.Happy = ScriptableObject.CreateInstance<VRM10Expression>();
                fixture.Happy.name = "happy";
                fixture.Happy.MorphTargetBindings = new[]
                {
                    new MorphTargetBinding("Face", 0, 0.8f),
                };
                fixture.Happy.MaterialColorBindings = new[]
                {
                    new MaterialColorBinding
                    {
                        MaterialName = fixture.SourceMaterial.name,
                        BindType = MaterialColorType.color,
                        TargetValue = Color.red,
                    },
                };
                fixture.Happy.MaterialUVBindings = new[]
                {
                    new MaterialUVBinding
                    {
                        MaterialName = fixture.SourceMaterial.name,
                        Scaling = new Vector2(2f, 2f),
                        Offset = new Vector2(0.25f, 0.5f),
                    },
                };
                fixture.Happy.OverrideMouth = ExpressionOverrideType.block;

                fixture.Aa = ScriptableObject.CreateInstance<VRM10Expression>();
                fixture.Aa.name = "aa";
                fixture.Aa.MorphTargetBindings = new[]
                {
                    new MorphTargetBinding("Face", 1, 1f),
                };

                fixture.Neutral = ScriptableObject.CreateInstance<VRM10Expression>();
                fixture.Neutral.name = "neutral";
                fixture.Neutral.IsBinary = true;
                fixture.Neutral.MorphTargetBindings = new[]
                {
                    new MorphTargetBinding("Face", 0, 0.25f),
                };

                fixture.Vrm = ScriptableObject.CreateInstance<VRM10Object>();
                fixture.Vrm.Expression.Happy = fixture.Happy;
                fixture.Vrm.Expression.Aa = fixture.Aa;
                fixture.Vrm.Expression.Neutral = fixture.Neutral;
                fixture.Instance.Vrm = fixture.Vrm;
                return fixture;
            }

            public void Dispose()
            {
                DestroyImmediate(Instance == null ? null : Instance.gameObject);
                DestroyImmediate(Happy);
                DestroyImmediate(Aa);
                DestroyImmediate(Neutral);
                DestroyImmediate(Vrm);
                DestroyImmediate(SourceMesh);
                DestroyImmediate(SourceMaterial);
            }

            private static Mesh CreateMesh()
            {
                var mesh = new Mesh
                {
                    name = "Synthetic Expression Mesh",
                    vertices = new[]
                    {
                        Vector3.zero,
                        Vector3.right,
                        Vector3.up,
                    },
                    triangles = new[] { 0, 1, 2 },
                };
                var zeros = new[] { Vector3.zero, Vector3.zero, Vector3.zero };
                mesh.AddBlendShapeFrame(
                    "Happy",
                    100f,
                    new[] { Vector3.forward, Vector3.zero, Vector3.zero },
                    zeros,
                    zeros);
                mesh.AddBlendShapeFrame(
                    "Aa",
                    100f,
                    new[] { Vector3.zero, Vector3.forward, Vector3.zero },
                    zeros,
                    zeros);
                mesh.RecalculateBounds();
                return mesh;
            }

            private static void DestroyImmediate(Object target)
            {
                if (target != null)
                {
                    Object.DestroyImmediate(target);
                }
            }
        }
    }
}
