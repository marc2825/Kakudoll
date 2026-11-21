using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using VLat.Core.Model;
using VLat.Core.Serialization;

namespace VLat.Tests.Play
{
    /// <summary>VlatApplierの読み込み、角度評価、Before/After切替を検証します。</summary>
    public sealed class VlatApplierPlayModeTests
    {
        /// <summary>保存済みドキュメントの適用が元アセットと既存表情を変更しないことを検証します。</summary>
        [Test]
        public void LoadInitializeAngleAndBeforeAfter_PreserveOriginalAssetsAndExpression()
        {
            var fixture = SyntheticVlatFixture.CreateFunctional();
            var testDirectory = Path.Combine(
                Application.temporaryCachePath,
                "VLatPlayModeTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDirectory);
            var jsonPath = Path.Combine(testDirectory, "synthetic.vlat.json");
            try
            {
                var originalVertices = fixture.OriginalMesh.vertices;
                var saveResult = VlatSerializer.Save(
                    jsonPath,
                    fixture.Document,
                    fixture.Buffers);
                Assert.That(saveResult.Success, Is.True, DescribeCoreDiagnostics(saveResult.Diagnostics));

                var loadResult = VlatSerializer.Load(jsonPath);
                Assert.That(loadResult.Success, Is.True, DescribeCoreDiagnostics(loadResult.Diagnostics));
                Assert.That(loadResult.Document, Is.Not.Null);
                Assert.That(loadResult.Buffers.Count, Is.EqualTo(1));

                var initializeResult = fixture.Applier.Initialize(
                    fixture.AvatarRoot,
                    fixture.Head,
                    fixture.EvaluationCamera,
                    loadResult.Document,
                    loadResult.Buffers);
                Assert.That(
                    initializeResult.Success,
                    Is.True,
                    DescribeRuntimeDiagnostics(initializeResult.Diagnostics));
                Assert.That(fixture.Applier.IsInitialized, Is.True);
                Assert.That(fixture.Applier.CurrentYaw, Is.EqualTo(0f).Within(0.0001f));
                Assert.That(fixture.Applier.CurrentPitch, Is.EqualTo(0f).Within(0.0001f));
                Assert.That(fixture.Applier.ActiveBandCount, Is.EqualTo(1));

                var generatedMesh = fixture.Renderer.sharedMesh;
                Assert.That(generatedMesh, Is.Not.SameAs(fixture.OriginalMesh));
                Assert.That(fixture.OriginalMesh.blendShapeCount, Is.EqualTo(1));
                Assert.That(generatedMesh.blendShapeCount, Is.EqualTo(2));
                var vlatBlendShape = generatedMesh.GetBlendShapeIndex("VLAT_layer-0__modifier_0");
                Assert.That(vlatBlendShape, Is.EqualTo(1));

                fixture.Renderer.SetBlendShapeWeight(0, 37f);
                Assert.That(fixture.Applier.EvaluateNow(), Is.True);
                Assert.That(fixture.Renderer.GetBlendShapeWeight(0), Is.EqualTo(37f).Within(0.001f));
                Assert.That(
                    fixture.Renderer.GetBlendShapeWeight(vlatBlendShape),
                    Is.EqualTo(100f).Within(0.001f));
                Assert.That(fixture.Renderer.enabled, Is.False);

                fixture.Applier.DebugYaw = 90f;
                Assert.That(fixture.Applier.EvaluateNow(), Is.True);
                Assert.That(fixture.Applier.ActiveBandCount, Is.Zero);
                Assert.That(
                    fixture.Renderer.GetBlendShapeWeight(vlatBlendShape),
                    Is.EqualTo(0f).Within(0.001f));
                Assert.That(fixture.Renderer.GetBlendShapeWeight(0), Is.EqualTo(37f).Within(0.001f));
                Assert.That(fixture.Renderer.enabled, Is.True);

                fixture.Applier.DebugYaw = 0f;
                Assert.That(fixture.Applier.EvaluateNow(), Is.True);
                Assert.That(fixture.Renderer.enabled, Is.False);

                fixture.Applier.ApplicationEnabled = false;
                Assert.That(
                    fixture.Renderer.GetBlendShapeWeight(vlatBlendShape),
                    Is.EqualTo(0f).Within(0.001f));
                Assert.That(fixture.Renderer.GetBlendShapeWeight(0), Is.EqualTo(37f).Within(0.001f));
                Assert.That(fixture.Renderer.enabled, Is.True);
                Assert.That(fixture.Applier.ActiveBandCount, Is.Zero);
                Assert.That(fixture.Applier.AppliedModifierCount, Is.Zero);

                fixture.Applier.ApplicationEnabled = true;
                Assert.That(
                    fixture.Renderer.GetBlendShapeWeight(vlatBlendShape),
                    Is.EqualTo(100f).Within(0.001f));
                Assert.That(fixture.Renderer.enabled, Is.False);

                fixture.Applier.enabled = false;
                Assert.That(
                    fixture.Renderer.GetBlendShapeWeight(vlatBlendShape),
                    Is.EqualTo(0f).Within(0.001f));
                Assert.That(fixture.Renderer.GetBlendShapeWeight(0), Is.EqualTo(37f).Within(0.001f));
                Assert.That(fixture.Renderer.enabled, Is.True);

                fixture.Applier.enabled = true;
                Assert.That(
                    fixture.Renderer.GetBlendShapeWeight(vlatBlendShape),
                    Is.EqualTo(100f).Within(0.001f));
                Assert.That(fixture.Renderer.enabled, Is.False);

                AssertMeshUnchanged(fixture.OriginalMesh, originalVertices);
                Assert.That(fixture.Renderer.sharedMaterials[0], Is.SameAs(fixture.OriginalMaterial));

                fixture.DestroyApplier();
                Assert.That(fixture.Renderer.sharedMesh, Is.SameAs(fixture.OriginalMesh));
                Assert.That(fixture.Renderer.enabled, Is.True);
                Assert.That(fixture.OriginalMesh.blendShapeCount, Is.EqualTo(1));
                AssertMeshUnchanged(fixture.OriginalMesh, originalVertices);
            }
            finally
            {
                fixture.Dispose();
                if (Directory.Exists(testDirectory))
                {
                    Directory.Delete(testDirectory, true);
                }
            }
        }

        /// <summary>未解決参照を含むLayer全体が競合解決前に無効化されることを検証します。</summary>
        [Test]
        public void Initialize_UnresolvedBindingDisablesWholeLayerBeforeConflictResolution()
        {
            var fixture = SyntheticVlatFixture.CreateFunctional();
            try
            {
                var source = fixture.Document;
                var validBinding = source.Metadata.TargetAvatar.MeshBindings[0];
                var missingBinding = new MeshBinding(
                    "Missing/Renderer",
                    "MissingMesh",
                    validBinding.VertexCount,
                    "xxh64:0000000000000000",
                    validBinding.Materials);
                var metadata = new VlatMetadata(
                    source.Metadata.Version,
                    "VLat PlayMode Tests",
                    new TargetAvatar(
                        source.Metadata.TargetAvatar.Name,
                        source.Metadata.TargetAvatar.VrmVersion,
                        new[] { validBinding, missingBinding }));
                var band = source.AngleBands[0];
                var highInvalidLayer = new VlatLayer(
                    "invalid-high",
                    "Invalid high priority",
                    "other",
                    true,
                    10,
                    band.Id,
                    new VlatModifier[]
                    {
                        new VisibilityModifier(0, 0, true),
                        new VertexDeltaModifier(
                            1,
                            new VlatAccessor(0, "UINT32"),
                            new VlatAccessor(1, "FLOAT32")),
                    });
                var validLowLayer = new VlatLayer(
                    "valid-low",
                    "Valid low priority",
                    "other",
                    true,
                    0,
                    band.Id,
                    new VlatModifier[] { new VisibilityModifier(0, 0, false) });
                var document = new VlatDocument(
                    metadata,
                    source.Buffers,
                    source.BufferViews,
                    source.Textures,
                    new[] { band },
                    new[] { highInvalidLayer, validLowLayer });

                var result = fixture.Applier.Initialize(
                    fixture.AvatarRoot,
                    fixture.Head,
                    fixture.EvaluationCamera,
                    document,
                    fixture.Buffers);

                Assert.That(result.Success, Is.True, DescribeRuntimeDiagnostics(result.Diagnostics));
                Assert.That(result.BindingReport.AllSucceeded, Is.False);
                Assert.That(result.BindingReport.SuccessfulCount, Is.EqualTo(1));
                Assert.That(fixture.Renderer.enabled, Is.False);
                Assert.That(fixture.Renderer.sharedMesh, Is.SameAs(fixture.OriginalMesh));
                Assert.That(fixture.Applier.AppliedModifierCount, Is.EqualTo(1));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        /// <summary>同一Layer・Rendererの複数vertexDeltaが一意な動的BlendShapeとして共存することを検証します。</summary>
        [Test]
        public void Initialize_MultipleVertexDeltasInSameLayer_AppliesEveryModifier()
        {
            var fixture = SyntheticVlatFixture.CreateFunctional();
            try
            {
                var source = fixture.Document;
                var sourceLayer = source.Layers[0];
                var firstVertexDelta = new VertexDeltaModifier(
                    0,
                    new VlatAccessor(0, "UINT32"),
                    new VlatAccessor(1, "FLOAT32"));
                var secondVertexDelta = new VertexDeltaModifier(
                    0,
                    new VlatAccessor(0, "UINT32"),
                    new VlatAccessor(1, "FLOAT32"));
                var layer = new VlatLayer(
                    "layer-0",
                    "Layer 0",
                    "other",
                    true,
                    0,
                    source.AngleBands[0].Id,
                    new VlatModifier[]
                    {
                        firstVertexDelta,
                        secondVertexDelta,
                        sourceLayer.Modifiers[1],
                    });
                var document = new VlatDocument(
                    source.Metadata,
                    source.Buffers,
                    source.BufferViews,
                    source.Textures,
                    source.AngleBands,
                    new[] { layer });

                var result = fixture.Applier.Initialize(
                    fixture.AvatarRoot,
                    fixture.Head,
                    fixture.EvaluationCamera,
                    document,
                    fixture.Buffers);

                Assert.That(result.Success, Is.True, DescribeRuntimeDiagnostics(result.Diagnostics));
                var generatedMesh = fixture.Renderer.sharedMesh;
                Assert.That(generatedMesh, Is.Not.SameAs(fixture.OriginalMesh));
                Assert.That(fixture.OriginalMesh.blendShapeCount, Is.EqualTo(1));
                Assert.That(generatedMesh.blendShapeCount, Is.EqualTo(3));
                var firstIndex = generatedMesh.GetBlendShapeIndex("VLAT_layer-0__modifier_0");
                var secondIndex = generatedMesh.GetBlendShapeIndex("VLAT_layer-0__modifier_1");
                Assert.That(firstIndex, Is.EqualTo(1));
                Assert.That(secondIndex, Is.EqualTo(2));
                Assert.That(fixture.Renderer.GetBlendShapeWeight(firstIndex), Is.EqualTo(100f).Within(0.001f));
                Assert.That(fixture.Renderer.GetBlendShapeWeight(secondIndex), Is.EqualTo(100f).Within(0.001f));
                Assert.That(fixture.Renderer.enabled, Is.False);
                Assert.That(fixture.Applier.AppliedModifierCount, Is.EqualTo(3));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        private static void AssertMeshUnchanged(Mesh mesh, Vector3[] expectedVertices)
        {
            var actualVertices = mesh.vertices;
            Assert.That(actualVertices.Length, Is.EqualTo(expectedVertices.Length));
            for (var index = 0; index < actualVertices.Length; index++)
            {
                Assert.That(actualVertices[index], Is.EqualTo(expectedVertices[index]));
            }
        }

        private static string DescribeCoreDiagnostics(
            System.Collections.Generic.IReadOnlyList<VLat.Core.Validation.VlatDiagnostic> diagnostics)
        {
            if (diagnostics == null || diagnostics.Count == 0)
            {
                return string.Empty;
            }

            var diagnostic = diagnostics[0];
            return diagnostic.Path + ": " + diagnostic.Message;
        }

        private static string DescribeRuntimeDiagnostics(
            System.Collections.Generic.IReadOnlyList<VLat.Runtime.VlatRuntimeDiagnostic> diagnostics)
        {
            if (diagnostics == null || diagnostics.Count == 0)
            {
                return string.Empty;
            }

            var diagnostic = diagnostics[0];
            return diagnostic.Path + ": " + diagnostic.Message;
        }
    }
}
