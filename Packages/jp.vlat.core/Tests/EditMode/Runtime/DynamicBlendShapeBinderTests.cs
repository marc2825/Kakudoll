using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VLat.Core.Model;
using VLat.Runtime;
using Object = UnityEngine.Object;

namespace VLat.Tests.Edit
{
    public sealed class DynamicBlendShapeBinderTests
    {
        private GameObject _gameObject;
        private SkinnedMeshRenderer _renderer;
        private Mesh _originalMesh;
        private DynamicBlendShapeBinder _binder;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("DynamicBlendShapeBinderTests");
            _renderer = _gameObject.AddComponent<SkinnedMeshRenderer>();
            _originalMesh = new Mesh
            {
                name = "OriginalMesh",
                vertices = new[]
                {
                    Vector3.zero,
                    Vector3.right,
                    Vector3.up
                },
                triangles = new[] { 0, 1, 2 }
            };
            _renderer.sharedMesh = _originalMesh;
            _binder = new DynamicBlendShapeBinder();
        }

        [TearDown]
        public void TearDown()
        {
            _binder?.Dispose();
            if (_gameObject != null)
            {
                Object.DestroyImmediate(_gameObject);
            }

            if (_originalMesh != null)
            {
                Object.DestroyImmediate(_originalMesh);
            }
        }

        [Test]
        public void Bind_RegistersSparsePositionAndNormalDeltasOnClone()
        {
            var fixture = CreateFixture(
                new uint[] { 0, 2 },
                new[] { new Vector3(0.1f, 0.2f, 0.3f), new Vector3(-0.4f, 0.5f, 0.6f) },
                new[] { new Vector3(0f, 0.1f, 0f), new Vector3(0.2f, 0f, -0.2f) });

            var result = _binder.Bind(
                _renderer,
                fixture.Document,
                fixture.Buffers,
                "bangs",
                fixture.Modifier);

            Assert.That(result.Success, Is.True, result.Diagnostic?.Message);
            Assert.That(result.Error, Is.EqualTo(DynamicBlendShapeBindError.None));
            Assert.That(result.Renderer, Is.SameAs(_renderer));
            Assert.That(result.BlendShapeName, Is.EqualTo("VLAT_bangs"));
            Assert.That(result.BlendShapeIndex, Is.EqualTo(0));
            Assert.That(_renderer.sharedMesh, Is.Not.SameAs(_originalMesh));
            Assert.That(_originalMesh.blendShapeCount, Is.Zero);

            var generated = _renderer.sharedMesh;
            Assert.That(generated.blendShapeCount, Is.EqualTo(1));
            Assert.That(generated.GetBlendShapeName(0), Is.EqualTo("VLAT_bangs"));
            Assert.That(generated.GetBlendShapeFrameWeight(0, 0), Is.EqualTo(100f));

            var positions = new Vector3[3];
            var normals = new Vector3[3];
            var tangents = new Vector3[3];
            generated.GetBlendShapeFrameVertices(0, 0, positions, normals, tangents);
            AssertVector(positions[0], 0.1f, 0.2f, 0.3f);
            AssertVector(positions[1], 0f, 0f, 0f);
            AssertVector(positions[2], -0.4f, 0.5f, 0.6f);
            AssertVector(normals[0], 0f, 0.1f, 0f);
            AssertVector(normals[1], 0f, 0f, 0f);
            AssertVector(normals[2], 0.2f, 0f, -0.2f);
            AssertVector(tangents[0], 0f, 0f, 0f);
            AssertVector(tangents[2], 0f, 0f, 0f);
        }

        [Test]
        public void Bind_WhenNormalsAreOmitted_RegistersZeroNormalDeltas()
        {
            var fixture = CreateFixture(
                new uint[] { 1 },
                new[] { new Vector3(0.25f, -0.5f, 0.75f) });

            var result = _binder.Bind(
                _renderer,
                fixture.Document,
                fixture.Buffers,
                "noNormals",
                fixture.Modifier);

            Assert.That(result.Success, Is.True, result.Diagnostic?.Message);
            var positions = new Vector3[3];
            var normals = new Vector3[3];
            var tangents = new Vector3[3];
            _renderer.sharedMesh.GetBlendShapeFrameVertices(
                result.BlendShapeIndex,
                0,
                positions,
                normals,
                tangents);
            AssertVector(positions[1], 0.25f, -0.5f, 0.75f);
            AssertVector(normals[1], 0f, 0f, 0f);
        }

        [Test]
        public void Bind_PreservesOriginalBlendShapes()
        {
            var zeroDeltas = new Vector3[3];
            _originalMesh.AddBlendShapeFrame("Existing", 100f, zeroDeltas, zeroDeltas, zeroDeltas);
            var fixture = CreateFixture(new uint[] { 0 }, new[] { Vector3.right });

            var result = _binder.Bind(
                _renderer,
                fixture.Document,
                fixture.Buffers,
                "added",
                fixture.Modifier);

            Assert.That(result.Success, Is.True, result.Diagnostic?.Message);
            Assert.That(result.BlendShapeIndex, Is.EqualTo(1));
            Assert.That(_originalMesh.blendShapeCount, Is.EqualTo(1));
            Assert.That(_originalMesh.GetBlendShapeName(0), Is.EqualTo("Existing"));
            Assert.That(_renderer.sharedMesh.blendShapeCount, Is.EqualTo(2));
            Assert.That(_renderer.sharedMesh.GetBlendShapeName(0), Is.EqualTo("Existing"));
            Assert.That(_renderer.sharedMesh.GetBlendShapeName(1), Is.EqualTo("VLAT_added"));
        }

        [Test]
        public void Bind_MultipleLayersOnSameRenderer_SharesOneGeneratedMeshAndKeepsIndices()
        {
            var firstFixture = CreateFixture(new uint[] { 0 }, new[] { Vector3.right });
            var secondFixture = CreateFixture(new uint[] { 2 }, new[] { Vector3.up });

            var first = _binder.Bind(
                _renderer,
                firstFixture.Document,
                firstFixture.Buffers,
                "first",
                firstFixture.Modifier);
            var firstGeneratedMesh = _renderer.sharedMesh;
            var second = _binder.Bind(
                _renderer,
                secondFixture.Document,
                secondFixture.Buffers,
                "second",
                secondFixture.Modifier);

            Assert.That(first.Success, Is.True, first.Diagnostic?.Message);
            Assert.That(second.Success, Is.True, second.Diagnostic?.Message);
            Assert.That(first.BlendShapeIndex, Is.EqualTo(0));
            Assert.That(second.BlendShapeIndex, Is.EqualTo(1));
            Assert.That(first.Renderer, Is.SameAs(second.Renderer));
            Assert.That(_renderer.sharedMesh, Is.SameAs(firstGeneratedMesh));
            Assert.That(_renderer.sharedMesh.blendShapeCount, Is.EqualTo(2));
            Assert.That(_renderer.sharedMesh.GetBlendShapeName(first.BlendShapeIndex), Is.EqualTo("VLAT_first"));
            Assert.That(_renderer.sharedMesh.GetBlendShapeName(second.BlendShapeIndex), Is.EqualTo("VLAT_second"));
        }

        [Test]
        public void Bind_MultipleModifiersInSameLayer_UsesModifierIndexToAvoidNameCollision()
        {
            var firstFixture = CreateFixture(new uint[] { 0 }, new[] { Vector3.right });
            var secondFixture = CreateFixture(new uint[] { 2 }, new[] { Vector3.up });

            var first = _binder.Bind(
                _renderer,
                firstFixture.Document,
                firstFixture.Buffers,
                "shared-layer",
                0,
                firstFixture.Modifier);
            var second = _binder.Bind(
                _renderer,
                secondFixture.Document,
                secondFixture.Buffers,
                "shared-layer",
                1,
                secondFixture.Modifier);

            Assert.That(first.Success, Is.True, first.Diagnostic?.Message);
            Assert.That(second.Success, Is.True, second.Diagnostic?.Message);
            Assert.That(first.BlendShapeIndex, Is.EqualTo(0));
            Assert.That(second.BlendShapeIndex, Is.EqualTo(1));
            Assert.That(first.BlendShapeName, Is.EqualTo("VLAT_shared-layer__modifier_0"));
            Assert.That(second.BlendShapeName, Is.EqualTo("VLAT_shared-layer__modifier_1"));
            Assert.That(_renderer.sharedMesh.blendShapeCount, Is.EqualTo(2));
            Assert.That(_originalMesh.blendShapeCount, Is.Zero);
        }

        [Test]
        public void ClearWeights_ResetsOnlyVlatShapesAndKeepsGeneratedMesh()
        {
            var zeroDeltas = new Vector3[3];
            _originalMesh.AddBlendShapeFrame("Existing", 100f, zeroDeltas, zeroDeltas, zeroDeltas);
            var fixture = CreateFixture(new uint[] { 0 }, new[] { Vector3.right });
            var result = _binder.Bind(
                _renderer,
                fixture.Document,
                fixture.Buffers,
                "clear",
                fixture.Modifier);
            Assert.That(result.Success, Is.True, result.Diagnostic?.Message);
            var generated = _renderer.sharedMesh;
            _renderer.SetBlendShapeWeight(0, 42f);
            _renderer.SetBlendShapeWeight(result.BlendShapeIndex, 75f);

            _binder.ClearWeights();

            Assert.That(_renderer.sharedMesh, Is.SameAs(generated));
            Assert.That(_renderer.GetBlendShapeWeight(0), Is.EqualTo(42f));
            Assert.That(_renderer.GetBlendShapeWeight(result.BlendShapeIndex), Is.Zero);
        }

        [Test]
        public void ClearWeights_DoesNotResetBlendShapeAddedByAnotherRuntimeSystem()
        {
            var fixture = CreateFixture(new uint[] { 0 }, new[] { Vector3.right });
            var result = _binder.Bind(
                _renderer,
                fixture.Document,
                fixture.Buffers,
                "owned",
                fixture.Modifier);
            Assert.That(result.Success, Is.True, result.Diagnostic?.Message);

            var generated = _renderer.sharedMesh;
            var zeroDeltas = new Vector3[generated.vertexCount];
            generated.AddBlendShapeFrame("External", 100f, zeroDeltas, zeroDeltas, zeroDeltas);
            var externalIndex = generated.GetBlendShapeIndex("External");
            _renderer.SetBlendShapeWeight(result.BlendShapeIndex, 75f);
            _renderer.SetBlendShapeWeight(externalIndex, 42f);

            _binder.ClearWeights();

            Assert.That(_renderer.GetBlendShapeWeight(result.BlendShapeIndex), Is.Zero);
            Assert.That(_renderer.GetBlendShapeWeight(externalIndex), Is.EqualTo(42f));
        }

        [Test]
        public void Restore_ReturnsOriginalAndDestroysGeneratedMesh_ThenAllowsRebind()
        {
            var fixture = CreateFixture(new uint[] { 0 }, new[] { Vector3.right });
            var first = _binder.Bind(
                _renderer,
                fixture.Document,
                fixture.Buffers,
                "restore",
                fixture.Modifier);
            Assert.That(first.Success, Is.True, first.Diagnostic?.Message);
            var firstGenerated = _renderer.sharedMesh;

            _binder.Restore();

            Assert.That(_renderer.sharedMesh, Is.SameAs(_originalMesh));
            Assert.That(firstGenerated == null, Is.True);
            Assert.That(_originalMesh.blendShapeCount, Is.Zero);

            var second = _binder.Bind(
                _renderer,
                fixture.Document,
                fixture.Buffers,
                "restore",
                fixture.Modifier);
            Assert.That(second.Success, Is.True, second.Diagnostic?.Message);
        }

        [Test]
        public void Dispose_IsIdempotentAndRejectsFurtherBindings()
        {
            var fixture = CreateFixture(new uint[] { 0 }, new[] { Vector3.right });
            var success = _binder.Bind(
                _renderer,
                fixture.Document,
                fixture.Buffers,
                "dispose",
                fixture.Modifier);
            Assert.That(success.Success, Is.True, success.Diagnostic?.Message);
            var generated = _renderer.sharedMesh;

            _binder.Dispose();
            _binder.Dispose();
            var failure = _binder.Bind(
                _renderer,
                fixture.Document,
                fixture.Buffers,
                "afterDispose",
                fixture.Modifier);

            Assert.That(_renderer.sharedMesh, Is.SameAs(_originalMesh));
            Assert.That(generated == null, Is.True);
            Assert.That(failure.Success, Is.False);
            Assert.That(failure.Error, Is.EqualTo(DynamicBlendShapeBindError.Disposed));
        }

        [Test]
        public void Bind_MalformedSecondLayer_DoesNotChangeExistingGeneratedMesh()
        {
            var validFixture = CreateFixture(new uint[] { 0 }, new[] { Vector3.right });
            var invalidFixture = CreateFixture(new uint[] { 1 }, new[] { new Vector3(float.NaN, 0f, 0f) });
            var first = _binder.Bind(
                _renderer,
                validFixture.Document,
                validFixture.Buffers,
                "valid",
                validFixture.Modifier);
            Assert.That(first.Success, Is.True, first.Diagnostic?.Message);
            var generated = _renderer.sharedMesh;

            var failure = _binder.Bind(
                _renderer,
                invalidFixture.Document,
                invalidFixture.Buffers,
                "invalid",
                invalidFixture.Modifier);

            Assert.That(failure.Success, Is.False);
            Assert.That(failure.Error, Is.EqualTo(DynamicBlendShapeBindError.NonFiniteDelta));
            Assert.That(_renderer.sharedMesh, Is.SameAs(generated));
            Assert.That(generated.blendShapeCount, Is.EqualTo(1));
            Assert.That(generated.GetBlendShapeName(0), Is.EqualTo("VLAT_valid"));
        }

        [TestCase("UINT16", "FLOAT32", DynamicBlendShapeBindError.UnsupportedComponentType)]
        [TestCase("UINT32", "FLOAT64", DynamicBlendShapeBindError.UnsupportedComponentType)]
        public void Bind_UnsupportedComponentType_ReturnsDiagnostic(
            string indexComponentType,
            string positionComponentType,
            DynamicBlendShapeBindError expectedError)
        {
            var fixture = CreateFixture(
                new uint[] { 0 },
                new[] { Vector3.right },
                null,
                null,
                indexComponentType,
                positionComponentType);

            var result = _binder.Bind(
                _renderer,
                fixture.Document,
                fixture.Buffers,
                "component",
                fixture.Modifier);

            AssertFailure(result, expectedError);
        }

        [Test]
        public void Bind_MismatchedAccessorCounts_ReturnsDiagnostic()
        {
            var fixture = CreateFixture(
                new uint[] { 0, 1 },
                new[] { Vector3.right });

            var result = _binder.Bind(
                _renderer,
                fixture.Document,
                fixture.Buffers,
                "counts",
                fixture.Modifier);

            AssertFailure(result, DynamicBlendShapeBindError.AccessorCountMismatch);
        }

        [TestCase(3u, DynamicBlendShapeBindError.VertexIndexOutOfRange)]
        [TestCase(4294967295u, DynamicBlendShapeBindError.VertexIndexOutOfRange)]
        public void Bind_OutOfRangeIndex_ReturnsDiagnostic(
            uint vertexIndex,
            DynamicBlendShapeBindError expectedError)
        {
            var fixture = CreateFixture(new[] { vertexIndex }, new[] { Vector3.right });

            var result = _binder.Bind(
                _renderer,
                fixture.Document,
                fixture.Buffers,
                "range",
                fixture.Modifier);

            AssertFailure(result, expectedError);
        }

        [Test]
        public void Bind_DuplicateIndex_ReturnsDiagnostic()
        {
            var fixture = CreateFixture(
                new uint[] { 1, 1 },
                new[] { Vector3.right, Vector3.up });

            var result = _binder.Bind(
                _renderer,
                fixture.Document,
                fixture.Buffers,
                "duplicate",
                fixture.Modifier);

            AssertFailure(result, DynamicBlendShapeBindError.DuplicateVertexIndex);
        }

        [Test]
        public void Bind_VertexCountMismatch_ReturnsDiagnosticWithoutCloning()
        {
            var fixture = CreateFixture(
                new uint[] { 0 },
                new[] { Vector3.right },
                null,
                4);

            var result = _binder.Bind(
                _renderer,
                fixture.Document,
                fixture.Buffers,
                "vertexCount",
                fixture.Modifier);

            AssertFailure(result, DynamicBlendShapeBindError.VertexCountMismatch);
            Assert.That(_renderer.sharedMesh, Is.SameAs(_originalMesh));
            Assert.That(_originalMesh.blendShapeCount, Is.Zero);
        }

        [Test]
        public void Bind_InvalidBufferViewReference_ReturnsDiagnostic()
        {
            var fixture = CreateFixture(new uint[] { 0 }, new[] { Vector3.right });
            var modifier = new VertexDeltaModifier(
                0,
                new VlatAccessor(99, "UINT32"),
                fixture.Modifier.Positions);

            var result = _binder.Bind(
                _renderer,
                fixture.Document,
                fixture.Buffers,
                "view",
                modifier);

            AssertFailure(result, DynamicBlendShapeBindError.InvalidBufferView);
        }

        [Test]
        public void Bind_DeclaredBufferLengthMismatch_ReturnsDiagnostic()
        {
            var fixture = CreateFixture(new uint[] { 0 }, new[] { Vector3.right });
            var truncatedBuffers = new List<byte[]>(fixture.Buffers)
            {
                [0] = new byte[fixture.Buffers[0].Length - 1]
            };

            var result = _binder.Bind(
                _renderer,
                fixture.Document,
                truncatedBuffers,
                "bufferLength",
                fixture.Modifier);

            AssertFailure(result, DynamicBlendShapeBindError.InvalidBufferView);
        }

        [Test]
        public void Bind_InvalidAccessorByteLength_ReturnsDiagnostic()
        {
            var fixture = CreateFixture(new uint[] { 0 }, new[] { Vector3.right });
            var views = new[]
            {
                fixture.Document.BufferViews[0],
                new VlatBufferView(1, fixture.Document.BufferViews[1].ByteOffset, 11)
            };
            var document = new VlatDocument(
                fixture.Document.Metadata,
                fixture.Document.Buffers,
                views,
                fixture.Document.Textures,
                fixture.Document.AngleBands,
                fixture.Document.Layers);

            var result = _binder.Bind(
                _renderer,
                document,
                fixture.Buffers,
                "accessorLength",
                fixture.Modifier);

            AssertFailure(result, DynamicBlendShapeBindError.InvalidAccessorLength);
        }

        [Test]
        public void Bind_DuplicateBlendShapeName_ReturnsDiagnosticWithoutReplacingMesh()
        {
            var fixture = CreateFixture(new uint[] { 0 }, new[] { Vector3.right });
            var first = _binder.Bind(
                _renderer,
                fixture.Document,
                fixture.Buffers,
                "same",
                fixture.Modifier);
            Assert.That(first.Success, Is.True, first.Diagnostic?.Message);
            var generated = _renderer.sharedMesh;

            var second = _binder.Bind(
                _renderer,
                fixture.Document,
                fixture.Buffers,
                "same",
                fixture.Modifier);

            AssertFailure(second, DynamicBlendShapeBindError.DuplicateBlendShapeName);
            Assert.That(_renderer.sharedMesh, Is.SameAs(generated));
            Assert.That(generated.blendShapeCount, Is.EqualTo(1));
        }

        [Test]
        public void Bind_WhenGeneratedMeshWasExternallyReplaced_ReturnsDiagnostic()
        {
            var fixture = CreateFixture(new uint[] { 0 }, new[] { Vector3.right });
            var first = _binder.Bind(
                _renderer,
                fixture.Document,
                fixture.Buffers,
                "first",
                fixture.Modifier);
            Assert.That(first.Success, Is.True, first.Diagnostic?.Message);
            _renderer.sharedMesh = _originalMesh;

            var second = _binder.Bind(
                _renderer,
                fixture.Document,
                fixture.Buffers,
                "second",
                fixture.Modifier);

            AssertFailure(second, DynamicBlendShapeBindError.RendererMeshChanged);
        }

        private static Fixture CreateFixture(
            uint[] indices,
            Vector3[] positions,
            Vector3[] normals = null,
            int? bindingVertexCount = null,
            string indexComponentType = "UINT32",
            string positionComponentType = "FLOAT32")
        {
            var indexBytes = PackIndices(indices, 4);
            var positionBytes = PackVectors(positions, 8);
            var buffers = new List<byte[]> { indexBytes, positionBytes };
            var bufferDefinitions = new List<VlatBuffer>
            {
                new VlatBuffer("indices.bin", indexBytes.LongLength),
                new VlatBuffer("positions.bin", positionBytes.LongLength)
            };
            var bufferViews = new List<VlatBufferView>
            {
                new VlatBufferView(0, 4, indices.LongLength * 4),
                new VlatBufferView(1, 8, positions.LongLength * 12)
            };

            VlatAccessor normalAccessor = null;
            if (normals != null)
            {
                var normalBytes = PackVectors(normals, 4);
                buffers.Add(normalBytes);
                bufferDefinitions.Add(new VlatBuffer("normals.bin", normalBytes.LongLength));
                bufferViews.Add(new VlatBufferView(2, 4, normals.LongLength * 12));
                normalAccessor = new VlatAccessor(2, "FLOAT32");
            }

            var modifier = new VertexDeltaModifier(
                0,
                new VlatAccessor(0, indexComponentType),
                new VlatAccessor(1, positionComponentType),
                normalAccessor);
            var meshBinding = new MeshBinding(
                "Face/Hair",
                "OriginalMesh",
                bindingVertexCount ?? 3,
                "xxh64:test",
                Array.Empty<VlatMaterialSlot>());
            var document = new VlatDocument(
                new VlatMetadata(
                    "0.1.0",
                    "DynamicBlendShapeBinderTests",
                    new TargetAvatar("Test", "1.0", new[] { meshBinding })),
                bufferDefinitions,
                bufferViews,
                Array.Empty<VlatTexture>(),
                Array.Empty<AngleBand>(),
                Array.Empty<VlatLayer>());
            return new Fixture(document, buffers, modifier);
        }

        private static byte[] PackIndices(IReadOnlyList<uint> values, int prefixLength)
        {
            var result = new byte[prefixLength + (values.Count * 4)];
            for (var i = 0; i < values.Count; i++)
            {
                WriteUInt32(result, prefixLength + (i * 4), values[i]);
            }

            return result;
        }

        private static byte[] PackVectors(IReadOnlyList<Vector3> values, int prefixLength)
        {
            var result = new byte[prefixLength + (values.Count * 12)];
            for (var i = 0; i < values.Count; i++)
            {
                var offset = prefixLength + (i * 12);
                WriteSingle(result, offset, values[i].x);
                WriteSingle(result, offset + 4, values[i].y);
                WriteSingle(result, offset + 8, values[i].z);
            }

            return result;
        }

        private static void WriteSingle(byte[] destination, int offset, float value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            Buffer.BlockCopy(bytes, 0, destination, offset, 4);
        }

        private static void WriteUInt32(byte[] destination, int offset, uint value)
        {
            destination[offset] = (byte)value;
            destination[offset + 1] = (byte)(value >> 8);
            destination[offset + 2] = (byte)(value >> 16);
            destination[offset + 3] = (byte)(value >> 24);
        }

        private static void AssertVector(Vector3 actual, float x, float y, float z)
        {
            Assert.That(actual.x, Is.EqualTo(x).Within(0.000001f));
            Assert.That(actual.y, Is.EqualTo(y).Within(0.000001f));
            Assert.That(actual.z, Is.EqualTo(z).Within(0.000001f));
        }

        private static void AssertFailure(
            DynamicBlendShapeBindResult result,
            DynamicBlendShapeBindError expectedError)
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo(expectedError));
            Assert.That(result.Diagnostic, Is.Not.Null);
            Assert.That(result.Diagnostic.Severity, Is.EqualTo(VlatRuntimeDiagnosticSeverity.Error));
            Assert.That(result.Diagnostic.Message, Is.Not.Empty);
            Assert.That(result.Renderer, Is.Null);
            Assert.That(result.BlendShapeIndex, Is.EqualTo(-1));
        }

        private sealed class Fixture
        {
            public Fixture(
                VlatDocument document,
                IReadOnlyList<byte[]> buffers,
                VertexDeltaModifier modifier)
            {
                Document = document;
                Buffers = buffers;
                Modifier = modifier;
            }

            public VlatDocument Document { get; }

            public IReadOnlyList<byte[]> Buffers { get; }

            public VertexDeltaModifier Modifier { get; }
        }
    }
}
