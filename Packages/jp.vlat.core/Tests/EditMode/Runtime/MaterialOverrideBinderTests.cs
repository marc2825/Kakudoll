using System;
using NUnit.Framework;
using UnityEngine;
using VLat.Core.Evaluation;
using VLat.Core.Model;
using VLat.Runtime;
using Object = UnityEngine.Object;

namespace VLat.Tests.Edit
{
    public sealed class MaterialOverrideBinderTests
    {
        [Test]
        public void Apply_FloatAndColorShareOneMpbAndOnlyWriteDifferences()
        {
            var shader = RequireShader("Standard");
            var material = new Material(shader) { name = "Source" };
            var fixture = new RendererFixture(1, material);
            var binder = new MaterialOverrideBinder();
            try
            {
                var glossId = Shader.PropertyToID("_Glossiness");
                var metallicId = Shader.PropertyToID("_Metallic");
                var colorId = Shader.PropertyToID("_Color");
                var originalColor = new Color(0.2f, 0.4f, 0.6f, 0.8f);
                var originalBlock = new MaterialPropertyBlock();
                originalBlock.SetFloat(glossId, 0.3f);
                originalBlock.SetFloat(metallicId, 0.7f);
                originalBlock.SetColor(colorId, originalColor);
                fixture.Renderer.SetPropertyBlock(originalBlock, 0);

                var document = CreateDocument(
                    1,
                    Array.Empty<VlatTexture>(),
                    new MaterialPropertyModifier(
                        0,
                        0,
                        "_Glossiness",
                        "float",
                        new VlatMaterialValue(0.9d),
                        true),
                    new MaterialPropertyModifier(
                        0,
                        0,
                        "_Color",
                        "color",
                        new VlatMaterialValue(new[] { 1d, 0d, 0d, 0.4d }),
                        true));
                var result = binder.Initialize(document, new Renderer[] { fixture.Renderer }, Array.Empty<Texture>());
                Assert.That(result.Success, Is.True);
                Assert.That(result.ClonedMaterialCount, Is.Zero);
                Assert.That(fixture.Renderer.sharedMaterials[0], Is.SameAs(material));

                var state = Resolve(document, 0.5d);
                Assert.That(binder.Apply(state), Is.EqualTo(1));

                var applied = new MaterialPropertyBlock();
                fixture.Renderer.GetPropertyBlock(applied, 0);
                Assert.That(applied.GetFloat(glossId), Is.EqualTo(0.6f).Within(0.00001f));
                AssertColor(applied.GetColor(colorId), 0.6f, 0.2f, 0.3f, 0.6f);
                Assert.That(applied.GetFloat(metallicId), Is.EqualTo(0.7f).Within(0.00001f));
                Assert.That(binder.Apply(state), Is.Zero);

                binder.ResetToBase();
                var restored = new MaterialPropertyBlock();
                fixture.Renderer.GetPropertyBlock(restored, 0);
                Assert.That(restored.GetFloat(glossId), Is.EqualTo(0.3f).Within(0.00001f));
                Assert.That(restored.GetFloat(metallicId), Is.EqualTo(0.7f).Within(0.00001f));
                AssertColor(restored.GetColor(colorId), 0.2f, 0.4f, 0.6f, 0.8f);
                Assert.That(fixture.Renderer.sharedMaterials[0], Is.SameAs(material));
                Assert.That(binder.Apply(state), Is.EqualTo(1));
            }
            finally
            {
                binder.Dispose();
                fixture.Dispose();
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void Apply_VectorUsesMpbWithoutMaterialClone()
        {
            var shader = RequireShader("Standard");
            var material = new Material(shader) { name = "Source" };
            var fixture = new RendererFixture(1, material);
            var binder = new MaterialOverrideBinder();
            try
            {
                var propertyId = Shader.PropertyToID("_EmissionColor");
                material.SetVector(propertyId, new Vector4(0.1f, 0.2f, 0.3f, 0.4f));
                var document = CreateDocument(
                    1,
                    Array.Empty<VlatTexture>(),
                    new MaterialPropertyModifier(
                        0,
                        0,
                        "_EmissionColor",
                        "vector",
                        new VlatMaterialValue(new[] { 0.9d, 0.8d, 0.7d, 0.6d }),
                        true));

                var result = binder.Initialize(document, new Renderer[] { fixture.Renderer }, Array.Empty<Texture>());
                Assert.That(result.Success, Is.True);
                Assert.That(result.ClonedMaterialCount, Is.Zero);
                Assert.That(binder.Apply(Resolve(document, 1d)), Is.EqualTo(1));

                var block = new MaterialPropertyBlock();
                fixture.Renderer.GetPropertyBlock(block, 0);
                AssertVector(block.GetVector(propertyId), 0.9f, 0.8f, 0.7f, 0.6f);
                AssertVector(material.GetVector(propertyId), 0.1f, 0.2f, 0.3f, 0.4f);
            }
            finally
            {
                binder.Dispose();
                fixture.Dispose();
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void Apply_TextureSwitchUsesMpbAndDoesNotCloneMaterial()
        {
            var shader = RequireShader("Standard");
            var material = new Material(shader) { name = "Source" };
            var baseTexture = new Texture2D(2, 2);
            var bandTexture = new Texture2D(2, 2);
            var fixture = new RendererFixture(1, material);
            var binder = new MaterialOverrideBinder();
            try
            {
                var propertyId = Shader.PropertyToID("_MainTex");
                material.SetTexture(propertyId, baseTexture);
                var document = CreateDocument(
                    1,
                    new[] { new VlatTexture("Band", "band.png", null, null) },
                    new MaterialPropertyModifier(
                        0,
                        0,
                        "_MainTex",
                        "textureIndex",
                        new VlatMaterialValue(0d),
                        false));

                var result = binder.Initialize(document, new Renderer[] { fixture.Renderer }, new Texture[] { bandTexture });
                Assert.That(result.Success, Is.True);
                Assert.That(result.ClonedMaterialCount, Is.Zero);
                Assert.That(fixture.Renderer.sharedMaterials[0], Is.SameAs(material));
                Assert.That(binder.Apply(Resolve(document, 1d)), Is.EqualTo(1));

                var block = new MaterialPropertyBlock();
                fixture.Renderer.GetPropertyBlock(block, 0);
                Assert.That(block.GetTexture(propertyId), Is.SameAs(bandTexture));
                Assert.That(material.GetTexture(propertyId), Is.SameAs(baseTexture));
                binder.ResetToBase();
                fixture.Renderer.GetPropertyBlock(block, 0);
                Assert.That(block.isEmpty, Is.True);
            }
            finally
            {
                binder.Dispose();
                fixture.Dispose();
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(baseTexture);
                Object.DestroyImmediate(bandTexture);
            }
        }

        [Test]
        public void Apply_RenderQueueClonesOnceAndResetReusesClone()
        {
            var shader = RequireShader("Standard");
            var material = new Material(shader) { name = "Source", renderQueue = 2000 };
            var fixture = new RendererFixture(1, material);
            var binder = new MaterialOverrideBinder();
            try
            {
                var document = CreateDocument(
                    1,
                    Array.Empty<VlatTexture>(),
                    new RenderQueueModifier(0, 0, 25));
                var result = binder.Initialize(document, new Renderer[] { fixture.Renderer }, Array.Empty<Texture>());
                Assert.That(result.Success, Is.True);
                Assert.That(result.ClonedMaterialCount, Is.EqualTo(1));
                var generated = fixture.Renderer.sharedMaterials[0];
                Assert.That(generated, Is.Not.SameAs(material));
                Assert.That(material.renderQueue, Is.EqualTo(2000));

                var state = Resolve(document, 1d);
                Assert.That(binder.Apply(state), Is.EqualTo(1));
                Assert.That(generated.renderQueue, Is.EqualTo(2025));
                Assert.That(binder.Apply(state), Is.Zero);

                binder.ResetToBase();
                Assert.That(fixture.Renderer.sharedMaterials[0], Is.SameAs(material));
                Assert.That(generated.renderQueue, Is.EqualTo(2000));
                Assert.That(binder.Apply(state), Is.EqualTo(2));
                Assert.That(fixture.Renderer.sharedMaterials[0], Is.SameAs(generated));
                Assert.That(generated.renderQueue, Is.EqualTo(2025));

                binder.Dispose();
                Assert.That(fixture.Renderer.sharedMaterials[0], Is.SameAs(material));
                Assert.That(material.renderQueue, Is.EqualTo(2000));
            }
            finally
            {
                binder.Dispose();
                fixture.Dispose();
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void Initialize_OpaqueMToonOpacityConfiguresOnlyClone()
        {
            var shader = RequireShader("VRM10/MToon10");
            var material = new Material(shader) { name = "MToon Source" };
            var fixture = new RendererFixture(1, material);
            var binder = new MaterialOverrideBinder();
            try
            {
                material.SetInt("_AlphaMode", 0);
                material.SetInt("_TransparentWithZWrite", 0);
                material.SetColor("_Color", Color.white);
                var document = CreateDocument(
                    1,
                    Array.Empty<VlatTexture>(),
                    new OpacityModifier(0, 0, 0.25d));

                var result = binder.Initialize(document, new Renderer[] { fixture.Renderer }, Array.Empty<Texture>());
                Assert.That(result.Success, Is.True);
                Assert.That(result.ClonedMaterialCount, Is.EqualTo(1));
                var generated = fixture.Renderer.sharedMaterials[0];
                Assert.That(generated, Is.Not.SameAs(material));
                Assert.That(generated.GetInt("_AlphaMode"), Is.EqualTo(2));
                Assert.That(generated.GetInt("_TransparentWithZWrite"), Is.EqualTo(1));
                Assert.That(generated.GetInt("_M_ZWrite"), Is.EqualTo(1));
                Assert.That(material.GetInt("_AlphaMode"), Is.Zero);
                Assert.That(material.GetInt("_TransparentWithZWrite"), Is.Zero);

                Assert.That(binder.Apply(Resolve(document, 1d)), Is.EqualTo(1));
                var block = new MaterialPropertyBlock();
                fixture.Renderer.GetPropertyBlock(block, 0);
                Assert.That(block.GetColor("_Color").a, Is.EqualTo(0.25f).Within(0.00001f));
                Assert.That(material.GetColor("_Color").a, Is.EqualTo(1f));
                Assert.That(binder.Apply(Resolve(document, 1d)), Is.Zero);

                binder.ResetToBase();
                Assert.That(fixture.Renderer.sharedMaterials[0], Is.SameAs(material));
                fixture.Renderer.GetPropertyBlock(block, 0);
                Assert.That(block.isEmpty, Is.True);
            }
            finally
            {
                binder.Dispose();
                fixture.Dispose();
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void Initialize_UnsupportedOpaqueShaderReturnsWarningWithoutMutatingSource()
        {
            var shader = RequireShader("Standard");
            var material = new Material(shader) { name = "Source", renderQueue = 2000 };
            var fixture = new RendererFixture(1, material);
            var binder = new MaterialOverrideBinder();
            try
            {
                var document = CreateDocument(
                    1,
                    Array.Empty<VlatTexture>(),
                    new OpacityModifier(0, 0, 0.5d));
                var result = binder.Initialize(document, new Renderer[] { fixture.Renderer }, Array.Empty<Texture>());

                Assert.That(result.Success, Is.True);
                Assert.That(result.ClonedMaterialCount, Is.EqualTo(1));
                Assert.That(ContainsWarning(result, "MToon10"), Is.True);
                Assert.That(fixture.Renderer.sharedMaterials[0], Is.Not.SameAs(material));
                Assert.That(material.renderQueue, Is.EqualTo(2000));
            }
            finally
            {
                binder.Dispose();
                fixture.Dispose();
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void Apply_SingleSubMeshVisibilityUsesRendererEnabledAndRestoresIt()
        {
            var shader = RequireShader("Standard");
            var material = new Material(shader);
            var fixture = new RendererFixture(1, material);
            var binder = new MaterialOverrideBinder();
            try
            {
                fixture.Renderer.enabled = true;
                var document = CreateDocument(
                    1,
                    Array.Empty<VlatTexture>(),
                    new VisibilityModifier(0, 0, false));
                var result = binder.Initialize(document, new Renderer[] { fixture.Renderer }, Array.Empty<Texture>());
                Assert.That(result.Success, Is.True);
                Assert.That(result.ClonedMaterialCount, Is.Zero);

                Assert.That(binder.Apply(Resolve(document, 1d)), Is.EqualTo(1));
                Assert.That(fixture.Renderer.enabled, Is.False);
                Assert.That(binder.Apply(Resolve(document, 1d)), Is.Zero);
                Assert.That(binder.Apply(Resolve(document, 0d)), Is.EqualTo(1));
                Assert.That(fixture.Renderer.enabled, Is.True);

                fixture.Renderer.enabled = false;
                binder.ResetToBase();
                Assert.That(fixture.Renderer.enabled, Is.True);
            }
            finally
            {
                binder.Dispose();
                fixture.Dispose();
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void Apply_MultiSubMeshVisibilityUsesTargetSlotAlphaFallback()
        {
            var shader = RequireShader("VRM10/MToon10");
            var first = new Material(shader) { name = "First" };
            var second = new Material(shader) { name = "Second" };
            var fixture = new RendererFixture(2, first, second);
            var binder = new MaterialOverrideBinder();
            try
            {
                first.SetColor("_Color", Color.white);
                second.SetColor("_Color", Color.white);
                first.SetInt("_AlphaMode", 0);
                second.SetInt("_AlphaMode", 0);
                var document = CreateDocument(
                    2,
                    Array.Empty<VlatTexture>(),
                    new VisibilityModifier(0, 1, false));
                var result = binder.Initialize(document, new Renderer[] { fixture.Renderer }, Array.Empty<Texture>());

                Assert.That(result.Success, Is.True);
                Assert.That(result.ClonedMaterialCount, Is.EqualTo(1));
                Assert.That(ContainsWarning(result, "alpha"), Is.True);
                Assert.That(fixture.Renderer.sharedMaterials[0], Is.SameAs(first));
                Assert.That(fixture.Renderer.sharedMaterials[1], Is.Not.SameAs(second));
                Assert.That(second.GetInt("_AlphaMode"), Is.Zero);

                Assert.That(binder.Apply(Resolve(document, 1d)), Is.EqualTo(1));
                Assert.That(fixture.Renderer.enabled, Is.True);
                var block = new MaterialPropertyBlock();
                fixture.Renderer.GetPropertyBlock(block, 1);
                Assert.That(block.GetColor("_Color").a, Is.Zero);

                binder.ResetToBase();
                Assert.That(fixture.Renderer.sharedMaterials[0], Is.SameAs(first));
                Assert.That(fixture.Renderer.sharedMaterials[1], Is.SameAs(second));
                fixture.Renderer.GetPropertyBlock(block, 1);
                Assert.That(block.isEmpty, Is.True);
            }
            finally
            {
                binder.Dispose();
                fixture.Dispose();
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        private static VlatDocument CreateDocument(
            int materialCount,
            VlatTexture[] textures,
            params VlatModifier[] modifiers)
        {
            var slots = new VlatMaterialSlot[materialCount];
            for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                slots[slotIndex] = new VlatMaterialSlot(slotIndex, "Material " + slotIndex, "Test/Shader");
            }

            var binding = new MeshBinding("", "TestMesh", 4, "xxh64:test", slots);
            var avatar = new TargetAvatar("Test", "1.0", new[] { binding });
            var metadata = new VlatMetadata("0.1.0", "Tests", avatar);
            var band = new AngleBand(
                "band",
                "Band",
                new AngleRange(-180d, 180d),
                new AngleRange(-90d, 90d),
                new AngleFeather(0d, 0d));
            var layer = new VlatLayer("layer", "Layer", "other", true, 0, "band", modifiers);
            return new VlatDocument(
                metadata,
                Array.Empty<VlatBuffer>(),
                Array.Empty<VlatBufferView>(),
                textures,
                new[] { band },
                new[] { layer });
        }

        private static ResolvedState Resolve(VlatDocument document, double weight)
        {
            var resolver = new ConflictResolver(document);
            var state = resolver.CreateState();
            resolver.Resolve(new[] { weight }, state);
            return state;
        }

        private static Shader RequireShader(string name)
        {
            var shader = Shader.Find(name);
            Assert.That(shader, Is.Not.Null, "テストに必要なShaderが見つかりません: " + name);
            return shader;
        }

        private static bool ContainsWarning(MaterialOverrideInitializationResult result, string text)
        {
            for (var index = 0; index < result.Diagnostics.Count; index++)
            {
                var diagnostic = result.Diagnostics[index];
                if (diagnostic.Severity == VlatRuntimeDiagnosticSeverity.Warning &&
                    diagnostic.Message.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertColor(Color actual, float r, float g, float b, float a)
        {
            Assert.That(actual.r, Is.EqualTo(r).Within(0.00001f));
            Assert.That(actual.g, Is.EqualTo(g).Within(0.00001f));
            Assert.That(actual.b, Is.EqualTo(b).Within(0.00001f));
            Assert.That(actual.a, Is.EqualTo(a).Within(0.00001f));
        }

        private static void AssertVector(Vector4 actual, float x, float y, float z, float w)
        {
            Assert.That(actual.x, Is.EqualTo(x).Within(0.00001f));
            Assert.That(actual.y, Is.EqualTo(y).Within(0.00001f));
            Assert.That(actual.z, Is.EqualTo(z).Within(0.00001f));
            Assert.That(actual.w, Is.EqualTo(w).Within(0.00001f));
        }

        private sealed class RendererFixture : IDisposable
        {
            public RendererFixture(int subMeshCount, params Material[] materials)
            {
                GameObject = new GameObject("MaterialOverrideBinderTests");
                var filter = GameObject.AddComponent<MeshFilter>();
                Renderer = GameObject.AddComponent<MeshRenderer>();
                Mesh = new Mesh
                {
                    name = "TestMesh",
                    vertices = new[]
                    {
                        new Vector3(-1f, -1f, 0f),
                        new Vector3(1f, -1f, 0f),
                        new Vector3(-1f, 1f, 0f),
                        new Vector3(1f, 1f, 0f),
                    },
                    subMeshCount = subMeshCount,
                };
                if (subMeshCount == 1)
                {
                    Mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
                }
                else
                {
                    Mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
                    Mesh.SetTriangles(new[] { 2, 1, 3 }, 1);
                }

                filter.sharedMesh = Mesh;
                Renderer.sharedMaterials = materials;
                Renderer.enabled = true;
            }

            public GameObject GameObject { get; }
            public MeshRenderer Renderer { get; }
            public Mesh Mesh { get; }

            public void Dispose()
            {
                Object.DestroyImmediate(GameObject);
                Object.DestroyImmediate(Mesh);
            }
        }
    }
}
