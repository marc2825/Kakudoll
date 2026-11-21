using System;
using System.Collections.Generic;
using NUnit.Framework;
using VLat.Core.Model;
using VLat.Core.Validation;

namespace VLat.Tests.Edit
{
    /// <summary>VLatドキュメントの意味的バリデーションを検証します。</summary>
    public sealed class VlatValidatorTests
    {
        /// <summary>major非互換をエラー、minor前方互換を警告として報告することを検証します。</summary>
        [Test]
        public void Validate_VersionCompatibilityDistinguishesMajorErrorAndMinorWarning()
        {
            var majorDiagnostics = VlatValidator.Validate(CreateMinimalDocument("1.0.0"));
            AssertHasDiagnostic(
                majorDiagnostics,
                VlatDiagnosticSeverity.Error,
                "$.vlat.version",
                "major");

            var minorDiagnostics = VlatValidator.Validate(CreateMinimalDocument("0.2.0"));
            AssertNoErrors(minorDiagnostics);
            AssertHasDiagnostic(
                minorDiagnostics,
                VlatDiagnosticSeverity.Warning,
                "$.vlat.version",
                "新しい");
        }

        /// <summary>bufferViewのbuffer参照とbyte範囲の逸脱を検出することを検証します。</summary>
        [Test]
        public void Validate_BufferViewsMustStayInsideReferencedBuffers()
        {
            var document = new VlatDocument(
                CreateMetadata("0.1.0", 3),
                new[] { new VlatBuffer("data.vlat.bin", 16) },
                new[]
                {
                    new VlatBufferView(0, 12, 8),
                    new VlatBufferView(1, 0, 4),
                },
                Array.Empty<VlatTexture>(),
                Array.Empty<AngleBand>(),
                Array.Empty<VlatLayer>());

            var diagnostics = VlatValidator.Validate(document);

            AssertHasDiagnostic(
                diagnostics,
                VlatDiagnosticSeverity.Error,
                "$.bufferViews[0]",
                "範囲");
            AssertHasDiagnostic(
                diagnostics,
                VlatDiagnosticSeverity.Error,
                "$.bufferViews[1].buffer",
                "範囲外");
        }

        /// <summary>vertexDeltaのindex範囲、要素数一致、および任意normal列を検証します。</summary>
        [Test]
        public void Validate_VertexDeltaChecksIndicesCountsAndOptionalNormals()
        {
            byte[] validWithoutNormalsBinary;
            var validWithoutNormals = CreateVertexDocument(
                3,
                new uint[] { 0, 2 },
                2,
                null,
                out validWithoutNormalsBinary);
            AssertNoErrors(VlatValidator.Validate(validWithoutNormals, new[] { validWithoutNormalsBinary }));

            byte[] validWithNormalsBinary;
            var validWithNormals = CreateVertexDocument(
                3,
                new uint[] { 0, 2 },
                2,
                2,
                out validWithNormalsBinary);
            AssertNoErrors(VlatValidator.Validate(validWithNormals, new[] { validWithNormalsBinary }));

            byte[] mismatchedPositionsBinary;
            var mismatchedPositions = CreateVertexDocument(
                3,
                new uint[] { 0, 2 },
                1,
                null,
                out mismatchedPositionsBinary);
            AssertHasDiagnostic(
                VlatValidator.Validate(mismatchedPositions, new[] { mismatchedPositionsBinary }),
                VlatDiagnosticSeverity.Error,
                "$.layers[0].modifiers[0]",
                "要素数");

            byte[] mismatchedNormalsBinary;
            var mismatchedNormals = CreateVertexDocument(
                3,
                new uint[] { 0, 2 },
                2,
                1,
                out mismatchedNormalsBinary);
            AssertHasDiagnostic(
                VlatValidator.Validate(mismatchedNormals, new[] { mismatchedNormalsBinary }),
                VlatDiagnosticSeverity.Error,
                "$.layers[0].modifiers[0].normals",
                "要素数");

            byte[] outOfRangeBinary;
            var outOfRange = CreateVertexDocument(
                3,
                new uint[] { 3 },
                1,
                null,
                out outOfRangeBinary);
            AssertHasDiagnostic(
                VlatValidator.Validate(outOfRange, new[] { outOfRangeBinary }),
                VlatDiagnosticSeverity.Error,
                "$.layers[0].modifiers[0].indices",
                "vertexCount");
        }

        /// <summary>Runtimeで適用不能な重複indexと非有限FLOAT32をCoreでも拒否することを検証します。</summary>
        [Test]
        public void Validate_VertexDeltaRejectsDuplicateIndicesAndNonFiniteVectors()
        {
            byte[] duplicateBinary;
            var duplicate = CreateVertexDocument(
                3,
                new uint[] { 1, 1 },
                2,
                null,
                out duplicateBinary);
            AssertHasDiagnostic(
                VlatValidator.Validate(duplicate, new[] { duplicateBinary }),
                VlatDiagnosticSeverity.Error,
                "$.layers[0].modifiers[0].indices",
                "重複");

            byte[] positionBinary;
            var nonFinitePosition = CreateVertexDocument(
                3,
                new uint[] { 1 },
                1,
                null,
                out positionBinary);
            WriteUInt32LittleEndian(positionBinary, 4, 0x7fc00000u);
            AssertHasDiagnostic(
                VlatValidator.Validate(nonFinitePosition, new[] { positionBinary }),
                VlatDiagnosticSeverity.Error,
                "$.layers[0].modifiers[0].positions",
                "非有限");

            byte[] normalBinary;
            var nonFiniteNormal = CreateVertexDocument(
                3,
                new uint[] { 1 },
                1,
                1,
                out normalBinary);
            WriteUInt32LittleEndian(normalBinary, 16, 0x7f800000u);
            AssertHasDiagnostic(
                VlatValidator.Validate(nonFiniteNormal, new[] { normalBinary }),
                VlatDiagnosticSeverity.Error,
                "$.layers[0].modifiers[0].normals",
                "非有限");
        }

        /// <summary>AngleBandとLayerのID重複をそれぞれ検出することを検証します。</summary>
        [Test]
        public void Validate_RejectsDuplicateBandAndLayerIds()
        {
            var firstBand = CreateBand("duplicate-band");
            var secondBand = CreateBand("duplicate-band");
            var firstLayer = new VlatLayer(
                "duplicate-layer",
                "First",
                "other",
                true,
                0,
                "duplicate-band",
                Array.Empty<VlatModifier>());
            var secondLayer = new VlatLayer(
                "duplicate-layer",
                "Second",
                "other",
                true,
                0,
                "duplicate-band",
                Array.Empty<VlatModifier>());
            var document = new VlatDocument(
                CreateMetadata("0.1.0", 3),
                Array.Empty<VlatBuffer>(),
                Array.Empty<VlatBufferView>(),
                Array.Empty<VlatTexture>(),
                new[] { firstBand, secondBand },
                new[] { firstLayer, secondLayer });

            var diagnostics = VlatValidator.Validate(document);

            AssertHasDiagnostic(
                diagnostics,
                VlatDiagnosticSeverity.Error,
                "$.angleBands[1].id",
                "重複");
            AssertHasDiagnostic(
                diagnostics,
                VlatDiagnosticSeverity.Error,
                "$.layers[1].id",
                "重複");
        }

        /// <summary>band、materialSlot、textureIndexの参照不整合を検出することを検証します。</summary>
        [Test]
        public void Validate_RejectsBrokenBandMaterialAndTextureReferences()
        {
            var band = CreateBand("band");
            var missingBandAndMaterialLayer = new VlatLayer(
                "missing-targets",
                "Missing targets",
                "other",
                true,
                0,
                "missing-band",
                new VlatModifier[] { new OpacityModifier(0, 4, 0.5d) });
            var missingTextureLayer = new VlatLayer(
                "missing-texture",
                "Missing texture",
                "hairHighlight",
                true,
                0,
                "band",
                new VlatModifier[]
                {
                    new MaterialPropertyModifier(
                        0,
                        0,
                        "_MainTex",
                        "textureIndex",
                        new VlatMaterialValue(1d),
                        false),
                });
            var document = new VlatDocument(
                CreateMetadata("0.1.0", 3),
                Array.Empty<VlatBuffer>(),
                Array.Empty<VlatBufferView>(),
                new[] { new VlatTexture("Texture", "texture.png", null, null) },
                new[] { band },
                new[] { missingBandAndMaterialLayer, missingTextureLayer });

            var diagnostics = VlatValidator.Validate(document);

            AssertHasDiagnostic(
                diagnostics,
                VlatDiagnosticSeverity.Error,
                "$.layers[0].bandId",
                "AngleBand");
            AssertHasDiagnostic(
                diagnostics,
                VlatDiagnosticSeverity.Error,
                "$.layers[0].modifiers[0].materialSlot",
                "materialSlot");
            AssertHasDiagnostic(
                diagnostics,
                VlatDiagnosticSeverity.Error,
                "$.layers[1].modifiers[0].bandValue",
                "texture");
        }

        /// <summary>Layer数は64件まで許可し、65件以上を拒否することを検証します。</summary>
        [Test]
        public void Validate_LayerCountAllowsMaximumAndRejectsOverflow()
        {
            AssertNoErrors(VlatValidator.Validate(CreateLayerCountDocument(VlatValidator.MaximumLayerCount)));

            var diagnostics = VlatValidator.Validate(
                CreateLayerCountDocument(VlatValidator.MaximumLayerCount + 1));
            AssertHasDiagnostic(
                diagnostics,
                VlatDiagnosticSeverity.Error,
                "$.layers",
                "64");
        }

        private static VlatDocument CreateMinimalDocument(string version)
        {
            return new VlatDocument(
                CreateMetadata(version, 3),
                Array.Empty<VlatBuffer>(),
                Array.Empty<VlatBufferView>(),
                Array.Empty<VlatTexture>(),
                Array.Empty<AngleBand>(),
                Array.Empty<VlatLayer>());
        }

        private static VlatDocument CreateLayerCountDocument(int layerCount)
        {
            var layers = new VlatLayer[layerCount];
            for (var index = 0; index < layers.Length; index++)
            {
                layers[index] = new VlatLayer(
                    "layer-" + index,
                    "Layer " + index,
                    "other",
                    true,
                    0,
                    "band",
                    Array.Empty<VlatModifier>());
            }

            return new VlatDocument(
                CreateMetadata("0.1.0", 3),
                Array.Empty<VlatBuffer>(),
                Array.Empty<VlatBufferView>(),
                Array.Empty<VlatTexture>(),
                new[] { CreateBand("band") },
                layers);
        }

        private static VlatMetadata CreateMetadata(string version, int vertexCount)
        {
            var material = new VlatMaterialSlot(0, "Material", "VRM10/MToon10");
            var mesh = new MeshBinding(
                "Face",
                "Face",
                vertexCount,
                "xxh64:0000000000000000",
                new[] { material });
            var avatar = new TargetAvatar("Avatar", "1.0", new[] { mesh });
            return new VlatMetadata(version, "VLat Tests", avatar);
        }

        private static AngleBand CreateBand(string id)
        {
            return new AngleBand(
                id,
                id,
                new AngleRange(-20d, 20d),
                new AngleRange(-20d, 20d),
                new AngleFeather(5d, 5d));
        }

        private static VlatDocument CreateVertexDocument(
            int vertexCount,
            uint[] indices,
            int positionCount,
            int? normalCount,
            out byte[] binary)
        {
            var indexByteLength = indices.Length * 4;
            var positionByteLength = positionCount * 12;
            var normalByteLength = normalCount.HasValue ? normalCount.Value * 12 : 0;
            var totalByteLength = indexByteLength + positionByteLength + normalByteLength;
            binary = new byte[totalByteLength];
            for (var index = 0; index < indices.Length; index++)
            {
                WriteUInt32LittleEndian(binary, index * 4, indices[index]);
            }

            var views = new List<VlatBufferView>
            {
                new VlatBufferView(0, 0, indexByteLength),
                new VlatBufferView(0, indexByteLength, positionByteLength),
            };
            VlatAccessor normals = null;
            if (normalCount.HasValue)
            {
                views.Add(new VlatBufferView(
                    0,
                    indexByteLength + positionByteLength,
                    normalByteLength));
                normals = new VlatAccessor(2, "FLOAT32");
            }

            var modifier = new VertexDeltaModifier(
                0,
                new VlatAccessor(0, "UINT32"),
                new VlatAccessor(1, "FLOAT32"),
                normals);
            var band = CreateBand("band");
            var layer = new VlatLayer(
                "layer",
                "Layer",
                "faceLine",
                true,
                0,
                "band",
                new VlatModifier[] { modifier });
            return new VlatDocument(
                CreateMetadata("0.1.0", vertexCount),
                new[] { new VlatBuffer("vertex.vlat.bin", totalByteLength) },
                views,
                Array.Empty<VlatTexture>(),
                new[] { band },
                new[] { layer });
        }

        private static void WriteUInt32LittleEndian(byte[] destination, int offset, uint value)
        {
            destination[offset] = (byte)value;
            destination[offset + 1] = (byte)(value >> 8);
            destination[offset + 2] = (byte)(value >> 16);
            destination[offset + 3] = (byte)(value >> 24);
        }

        private static void AssertNoErrors(IReadOnlyList<VlatDiagnostic> diagnostics)
        {
            for (var index = 0; index < diagnostics.Count; index++)
            {
                Assert.That(
                    diagnostics[index].Severity,
                    Is.Not.EqualTo(VlatDiagnosticSeverity.Error),
                    diagnostics[index].Path + ": " + diagnostics[index].Message);
            }
        }

        private static void AssertHasDiagnostic(
            IReadOnlyList<VlatDiagnostic> diagnostics,
            VlatDiagnosticSeverity severity,
            string expectedPath,
            string expectedMessage)
        {
            for (var index = 0; index < diagnostics.Count; index++)
            {
                var diagnostic = diagnostics[index];
                if (diagnostic.Severity == severity &&
                    diagnostic.Path.IndexOf(expectedPath, StringComparison.Ordinal) >= 0 &&
                    diagnostic.Message.IndexOf(expectedMessage, StringComparison.Ordinal) >= 0)
                {
                    return;
                }
            }

            Assert.Fail(
                "診断がありません: severity={0}, path={1}, message={2}",
                severity,
                expectedPath,
                expectedMessage);
        }
    }
}
