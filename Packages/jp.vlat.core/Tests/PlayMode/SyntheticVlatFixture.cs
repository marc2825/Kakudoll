using System;
using UnityEngine;
using UnityEngine.Rendering;
using VLat.Core.Model;
using VLat.Runtime;

namespace VLat.Tests.Play
{
    internal sealed class SyntheticVlatFixture
    {
        internal const int PerformanceVertexCount = 70000;
        internal const int PerformanceLayerCount = 20;
        internal const int PerformanceModifierCount = 60;
        internal const int EvaluationsPerBatch = 64;

        private static readonly int[] TriangleIndices = { 0, 1, 2 };

        private SyntheticVlatFixture()
        {
        }

        internal GameObject AvatarRootObject { get; private set; }

        internal Transform AvatarRoot => AvatarRootObject.transform;

        internal Transform Head { get; private set; }

        internal Camera EvaluationCamera { get; private set; }

        internal SkinnedMeshRenderer Renderer { get; private set; }

        internal Mesh OriginalMesh { get; private set; }

        internal Material OriginalMaterial { get; private set; }

        internal VlatApplier Applier { get; private set; }

        internal VlatDocument Document { get; private set; }

        internal byte[][] Buffers { get; private set; }

        internal static SyntheticVlatFixture CreateFunctional()
        {
            return Create(3, 1, true, true, false);
        }

        internal static SyntheticVlatFixture CreatePerformance()
        {
            return Create(
                PerformanceVertexCount,
                PerformanceLayerCount,
                false,
                true,
                true);
        }

        internal void EvaluateBatch()
        {
            for (var index = 0; index < EvaluationsPerBatch; index++)
            {
                Applier.EvaluateNow();
            }
        }

        internal void DestroyApplier()
        {
            if (Applier == null)
            {
                return;
            }

            UnityEngine.Object.DestroyImmediate(Applier);
            Applier = null;
        }

        internal void Dispose()
        {
            DestroyApplier();

            if (EvaluationCamera != null)
            {
                UnityEngine.Object.DestroyImmediate(EvaluationCamera.gameObject);
                EvaluationCamera = null;
            }

            if (AvatarRootObject != null)
            {
                UnityEngine.Object.DestroyImmediate(AvatarRootObject);
                AvatarRootObject = null;
            }

            if (OriginalMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(OriginalMesh);
                OriginalMesh = null;
            }

            if (OriginalMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(OriginalMaterial);
                OriginalMaterial = null;
            }
        }

        private static SyntheticVlatFixture Create(
            int vertexCount,
            int layerCount,
            bool includeExpression,
            bool includeVisibility,
            bool includeOpacity)
        {
            var fixture = new SyntheticVlatFixture();
            fixture.AvatarRootObject = new GameObject("SyntheticAvatar");

            var headObject = new GameObject("Head");
            headObject.transform.SetParent(fixture.AvatarRoot, false);
            fixture.Head = headObject.transform;

            var faceObject = new GameObject("Face");
            faceObject.transform.SetParent(fixture.AvatarRoot, false);
            fixture.Renderer = faceObject.AddComponent<SkinnedMeshRenderer>();

            fixture.OriginalMesh = CreateMesh(vertexCount, includeExpression);
            fixture.Renderer.sharedMesh = fixture.OriginalMesh;

            var shader = FindSupportedShader();
            fixture.OriginalMaterial = new Material(shader)
            {
                name = "SyntheticMaterial",
            };
            fixture.Renderer.sharedMaterials = new[] { fixture.OriginalMaterial };
            if (includeExpression)
            {
                fixture.Renderer.SetBlendShapeWeight(0, 37f);
            }

            var cameraObject = new GameObject("EvaluationCamera");
            fixture.EvaluationCamera = cameraObject.AddComponent<Camera>();
            fixture.EvaluationCamera.transform.position = new Vector3(0f, 0f, 2f);

            fixture.Buffers = new[] { CreateSparseDeltaBuffer() };
            fixture.Document = CreateDocument(
                vertexCount,
                layerCount,
                includeVisibility,
                includeOpacity,
                fixture.OriginalMesh.name,
                fixture.OriginalMaterial.name,
                shader.name,
                PositionHashUtility.Calculate(fixture.OriginalMesh),
                fixture.Buffers[0].Length);

            fixture.Applier = fixture.AvatarRootObject.AddComponent<VlatApplier>();
            fixture.Applier.DebugAngleOverrideEnabled = true;
            fixture.Applier.DebugYaw = 0f;
            fixture.Applier.DebugPitch = 0f;
            return fixture;
        }

        private static Mesh CreateMesh(int vertexCount, bool includeExpression)
        {
            var mesh = new Mesh
            {
                name = "SyntheticMesh",
                indexFormat = vertexCount > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16,
            };
            var vertices = new Vector3[vertexCount];
            vertices[0] = new Vector3(-0.5f, -0.5f, 0f);
            vertices[1] = new Vector3(0.5f, -0.5f, 0f);
            vertices[2] = new Vector3(0f, 0.5f, 0f);
            for (var index = 3; index < vertices.Length; index++)
            {
                vertices[index] = new Vector3(
                    (index & 255) * 0.001f,
                    ((index >> 8) & 255) * 0.001f,
                    (index >> 16) * 0.001f);
            }

            mesh.vertices = vertices;
            mesh.triangles = TriangleIndices;
            mesh.RecalculateBounds();

            if (includeExpression)
            {
                var expressionDelta = new Vector3[vertexCount];
                var zeroNormals = new Vector3[vertexCount];
                var zeroTangents = new Vector3[vertexCount];
                expressionDelta[1] = new Vector3(0f, 0.01f, 0f);
                mesh.AddBlendShapeFrame(
                    "Expression_Smile",
                    100f,
                    expressionDelta,
                    zeroNormals,
                    zeroTangents);
            }

            return mesh;
        }

        private static Shader FindSupportedShader()
        {
            var shader = Shader.Find("VRM10/MToon10");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                throw new InvalidOperationException("テスト用のBuilt-in RP Shaderを取得できません。");
            }

            return shader;
        }

        private static byte[] CreateSparseDeltaBuffer()
        {
            var buffer = new byte[16];
            WriteUInt32LittleEndian(buffer, 0, 0u);
            WriteSingleLittleEndian(buffer, 4, 0.02f);
            WriteSingleLittleEndian(buffer, 8, 0f);
            WriteSingleLittleEndian(buffer, 12, 0f);
            return buffer;
        }

        private static VlatDocument CreateDocument(
            int vertexCount,
            int layerCount,
            bool includeVisibility,
            bool includeOpacity,
            string meshName,
            string materialName,
            string shaderName,
            string positionHash,
            int binaryLength)
        {
            var binding = new MeshBinding(
                "Face",
                meshName,
                vertexCount,
                positionHash,
                new[] { new VlatMaterialSlot(0, materialName, shaderName) });
            var metadata = new VlatMetadata(
                "0.1.0",
                "VLat PlayMode Tests",
                new TargetAvatar("SyntheticAvatar", "1.0", new[] { binding }));
            var bands = new AngleBand[layerCount];
            var layers = new VlatLayer[layerCount];
            for (var layerIndex = 0; layerIndex < layerCount; layerIndex++)
            {
                var bandId = "band-" + layerIndex;
                bands[layerIndex] = new AngleBand(
                    bandId,
                    "Band " + layerIndex,
                    new AngleRange(-10d, 10d),
                    new AngleRange(-10d, 10d),
                    new AngleFeather(0d, 0d));

                var modifierCount = 1;
                if (includeVisibility)
                {
                    modifierCount++;
                }

                if (includeOpacity)
                {
                    modifierCount++;
                }

                var modifiers = new VlatModifier[modifierCount];
                var modifierIndex = 0;
                modifiers[modifierIndex++] = new VertexDeltaModifier(
                    0,
                    new VlatAccessor(0, "UINT32"),
                    new VlatAccessor(1, "FLOAT32"));
                if (includeVisibility)
                {
                    modifiers[modifierIndex++] = new VisibilityModifier(
                        0,
                        0,
                        layerCount == 1 ? false : true);
                }

                if (includeOpacity)
                {
                    modifiers[modifierIndex] = new OpacityModifier(
                        0,
                        0,
                        0.99d - (layerIndex * 0.001d));
                }

                layers[layerIndex] = new VlatLayer(
                    "layer-" + layerIndex,
                    "Layer " + layerIndex,
                    "other",
                    true,
                    0,
                    bandId,
                    modifiers);
            }

            return new VlatDocument(
                metadata,
                new[] { new VlatBuffer("synthetic.vlat.bin", binaryLength) },
                new[]
                {
                    new VlatBufferView(0, 0, 4),
                    new VlatBufferView(0, 4, 12),
                },
                Array.Empty<VlatTexture>(),
                bands,
                layers);
        }

        private static void WriteSingleLittleEndian(byte[] destination, int offset, float value)
        {
            var bits = BitConverter.SingleToInt32Bits(value);
            destination[offset] = (byte)bits;
            destination[offset + 1] = (byte)(bits >> 8);
            destination[offset + 2] = (byte)(bits >> 16);
            destination[offset + 3] = (byte)(bits >> 24);
        }

        private static void WriteUInt32LittleEndian(byte[] destination, int offset, uint value)
        {
            destination[offset] = (byte)value;
            destination[offset + 1] = (byte)(value >> 8);
            destination[offset + 2] = (byte)(value >> 16);
            destination[offset + 3] = (byte)(value >> 24);
        }
    }
}
