using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using VLat.Core.Evaluation;
using VLat.Core.Model;
using VLat.Core.Serialization;
using VLat.Core.Validation;

namespace VLat.Tests.Edit
{
    /// <summary>VLatの入出力、意味検証、および競合解決の異常系境界を検証します。</summary>
    public sealed class VlatIoAndValidationEdgeTests
    {
        private const string ValidHash = "xxh64:0123456789abcdef";

        /// <summary>nullのドキュメント、メタデータ、および対象アバターが診断へ変換されることを検証します。</summary>
        [Test]
        public void Validate_NullDocumentMetadataAndAvatarReportErrors()
        {
            AssertHasError(VlatValidator.Validate(null), "$");

            var missingMetadata = new DocumentBuilder
            {
                Metadata = null,
            }.Build();
            AssertHasError(VlatValidator.Validate(missingMetadata), "$.vlat");

            var missingAvatar = new DocumentBuilder
            {
                Metadata = new VlatMetadata("invalid", " ", null),
            }.Build();
            var diagnostics = VlatValidator.Validate(missingAvatar);

            AssertHasError(diagnostics, "$.vlat.version");
            AssertHasError(diagnostics, "$.vlat.generator");
            AssertHasError(diagnostics, "$.vlat.targetAvatar");
        }

        /// <summary>対象アバター、MeshBinding、およびmaterial slotの各不正値を検出することを検証します。</summary>
        [Test]
        public void Validate_AvatarMeshAndMaterialFieldsReportErrors()
        {
            var invalidBinding = new MeshBinding(
                null,
                " ",
                -1,
                "xxh64:0123456789abcg",
                new VlatMaterialSlot[]
                {
                    null,
                    new VlatMaterialSlot(-1, "", " "),
                    new VlatMaterialSlot(0, "MaterialA", "ShaderA"),
                    new VlatMaterialSlot(0, "MaterialB", "ShaderB"),
                });
            var metadata = new VlatMetadata(
                "0.1.0",
                "Tests",
                new TargetAvatar("", "2.0", new MeshBinding[] { null, invalidBinding }));
            var diagnostics = VlatValidator.Validate(new DocumentBuilder { Metadata = metadata }.Build());

            AssertHasError(diagnostics, "targetAvatar.name");
            AssertHasError(diagnostics, "targetAvatar.vrmVersion");
            AssertHasError(diagnostics, "meshBindings[0]");
            AssertHasError(diagnostics, "meshBindings[1].hierarchyPath");
            AssertHasError(diagnostics, "meshBindings[1].meshName");
            AssertHasError(diagnostics, "meshBindings[1].vertexCount");
            AssertHasError(diagnostics, "meshBindings[1].positionHash");
            AssertHasError(diagnostics, "materials[0]");
            AssertHasError(diagnostics, "materials[1].slot");
            AssertHasError(diagnostics, "materials[1].materialName");
            AssertHasError(diagnostics, "materials[1].shader");
            AssertHasError(diagnostics, "materials[3].slot");
        }

        /// <summary>bufferとbufferViewのnull、URI、長さ、参照、および範囲不正をまとめて検出することを検証します。</summary>
        [Test]
        public void Validate_BufferAndBufferViewEdgesReportErrors()
        {
            var document = new DocumentBuilder
            {
                Buffers = new VlatBuffer[]
                {
                    null,
                    new VlatBuffer("", 0),
                    new VlatBuffer("data.bin", 4),
                    new VlatBuffer("unused.bin", 4),
                },
                BufferViews = new VlatBufferView[]
                {
                    null,
                    new VlatBufferView(-1, 0, 1),
                    new VlatBufferView(99, 0, 1),
                    new VlatBufferView(2, -1, 0),
                    new VlatBufferView(2, 4, 1),
                    new VlatBufferView(2, 3, 2),
                    new VlatBufferView(0, 0, 1),
                },
            }.Build();
            var diagnostics = VlatValidator.Validate(
                document,
                new byte[][] { Array.Empty<byte>(), null, new byte[3] });

            AssertHasError(diagnostics, "$.buffers");
            AssertHasError(diagnostics, "$.buffers[0]");
            AssertHasError(diagnostics, "$.buffers[1].uri");
            AssertHasError(diagnostics, "$.buffers[1].byteLength");
            AssertHasError(diagnostics, "$.buffers[1]");
            AssertHasError(diagnostics, "$.buffers[2].byteLength");
            AssertHasError(diagnostics, "$.bufferViews[0]");
            AssertHasError(diagnostics, "$.bufferViews[1].buffer");
            AssertHasError(diagnostics, "$.bufferViews[2].buffer");
            AssertHasError(diagnostics, "$.bufferViews[3].byteOffset");
            AssertHasError(diagnostics, "$.bufferViews[3].byteLength");
            AssertHasError(diagnostics, "$.bufferViews[4]");
            AssertHasError(diagnostics, "$.bufferViews[5]");
            AssertHasError(
                VlatValidator.Validate(CreateDocumentWithBuffer("https://example.com/data.bin", 1)),
                "$.buffers[0].uri");
            AssertHasError(
                VlatValidator.Validate(CreateDocumentWithBuffer("%2Ftmp%2Fencoded.bin", 1)),
                "$.buffers[0].uri");
            Assert.That(
                VlatSerializer.Deserialize(VlatSerializer.Serialize(
                    CreateDocumentWithBuffer("%2Ftmp%2Fencoded.bin", 1))).Success,
                Is.False);
        }

        /// <summary>textureの排他source、表示名、bufferView、およびMIME形式を検証することを確認します。</summary>
        [Test]
        public void Validate_TextureUnionAndMimeEdgesReportErrors()
        {
            var document = new DocumentBuilder
            {
                Buffers = new[] { new VlatBuffer("data.bin", 4) },
                BufferViews = new[] { new VlatBufferView(0, 0, 4) },
                Textures = new VlatTexture[]
                {
                    null,
                    new VlatTexture("", null, null, null),
                    new VlatTexture("Both", "both.png", 0, "image/png"),
                    new VlatTexture("MissingMime", null, 4, null),
                    new VlatTexture("WrongPrefix", "wrong.png", null, "text/plain"),
                    new VlatTexture("EmptySubtype", "empty.png", null, "image/"),
                    new VlatTexture("WrongFirst", "first.png", null, "image/_png"),
                    new VlatTexture("WrongCharacter", "char.png", null, "image/pn?g"),
                    new VlatTexture("Valid", "valid.png", null, "image/vnd.test+png"),
                    new VlatTexture("Absolute", "https://example.com/texture.png", null, null),
                    new VlatTexture("EncodedAbsolute", "%2Ftmp%2Ftexture.png", null, null),
                },
            }.Build();
            var diagnostics = VlatValidator.Validate(document);

            AssertHasError(diagnostics, "$.textures[0]");
            AssertHasError(diagnostics, "$.textures[1].name");
            AssertHasError(diagnostics, "$.textures[1]");
            AssertHasError(diagnostics, "$.textures[2]");
            AssertHasError(diagnostics, "$.textures[3].bufferView");
            AssertHasError(diagnostics, "$.textures[3].mimeType");
            AssertHasError(diagnostics, "$.textures[4].mimeType");
            AssertHasError(diagnostics, "$.textures[5].mimeType");
            AssertHasError(diagnostics, "$.textures[6].mimeType");
            AssertHasError(diagnostics, "$.textures[7].mimeType");
            AssertNoErrorAt(diagnostics, "$.textures[8].mimeType");
            AssertHasError(diagnostics, "$.textures[9].uri");
            AssertHasError(diagnostics, "$.textures[10].uri");
        }

        /// <summary>AngleBandとLayerのnull、必須値、角度、tag、および未知Modifierを検出することを検証します。</summary>
        [Test]
        public void Validate_AngleLayerAndUnknownModifierEdgesReportErrors()
        {
            var missingPartsBand = new AngleBand("", " ", null, null, null, -1d);
            var invalidNumbersBand = new AngleBand(
                "numbers",
                "Numbers",
                new AngleRange(double.NaN, double.PositiveInfinity),
                new AngleRange(80d, -80d),
                new AngleFeather(-1d, double.NegativeInfinity),
                double.NaN);
            var invalidLayer = new VlatLayer(
                "",
                " ",
                "unsupportedTag",
                true,
                0,
                "missing-band",
                new VlatModifier[] { null, new UnsupportedModifier() });
            var document = new DocumentBuilder
            {
                AngleBands = new AngleBand[] { null, missingPartsBand, invalidNumbersBand },
                Layers = new VlatLayer[] { null, invalidLayer },
            }.Build();
            var diagnostics = VlatValidator.Validate(document);

            AssertHasError(diagnostics, "$.angleBands[0]");
            AssertHasError(diagnostics, "$.angleBands[1].id");
            AssertHasError(diagnostics, "$.angleBands[1].name");
            AssertHasError(diagnostics, "$.angleBands[1].yaw");
            AssertHasError(diagnostics, "$.angleBands[1].pitch");
            AssertHasError(diagnostics, "$.angleBands[1].feather");
            AssertHasError(diagnostics, "$.angleBands[1].hysteresis");
            AssertHasError(diagnostics, "$.angleBands[2].yaw.min");
            AssertHasError(diagnostics, "$.angleBands[2].yaw.max");
            AssertHasError(diagnostics, "$.angleBands[2].pitch");
            AssertHasError(diagnostics, "$.angleBands[2].feather.yaw");
            AssertHasError(diagnostics, "$.angleBands[2].feather.pitch");
            AssertHasError(diagnostics, "$.layers[0]");
            AssertHasError(diagnostics, "$.layers[1].id");
            AssertHasError(diagnostics, "$.layers[1].name");
            AssertHasError(diagnostics, "$.layers[1].tag");
            AssertHasError(diagnostics, "$.layers[1].bandId");
            AssertHasError(diagnostics, "$.layers[1].modifiers[0]");
            AssertHasError(diagnostics, "$.layers[1].modifiers[1].type");
        }

        /// <summary>Modifierの対象参照、subMesh、opacity、およびmaterial slotの不正を検出することを検証します。</summary>
        [Test]
        public void Validate_ModifierTargetsAndScalarEdgesReportErrors()
        {
            var modifiers = new VlatModifier[]
            {
                new VisibilityModifier(-1, -1, true),
                new OpacityModifier(0, 9, -0.1d),
                new OpacityModifier(0, 0, double.NaN),
                new OpacityModifier(0, 0, 1.1d),
                new RenderQueueModifier(4, 0, 1),
                new RenderQueueModifier(0, 8, -1),
            };
            var document = CreateDocumentWithModifiers(modifiers);
            var diagnostics = VlatValidator.Validate(document);

            AssertHasError(diagnostics, "modifiers[0].meshBinding");
            AssertHasError(diagnostics, "modifiers[0].subMesh");
            AssertHasError(diagnostics, "modifiers[1].materialSlot");
            AssertHasError(diagnostics, "modifiers[1].value");
            AssertHasError(diagnostics, "modifiers[2].value");
            AssertHasError(diagnostics, "modifiers[3].value");
            AssertHasError(diagnostics, "modifiers[4].meshBinding");
            AssertHasError(diagnostics, "modifiers[5].materialSlot");

            var nullBindingMetadata = new VlatMetadata(
                "0.1.0",
                "Tests",
                new TargetAvatar("Avatar", "1.0", new MeshBinding[] { null }));
            var nullBindingDocument = new DocumentBuilder
            {
                Metadata = nullBindingMetadata,
                AngleBands = new[] { CreateValidBand() },
                Layers = new[]
                {
                    CreateLayer(new VlatModifier[] { new OpacityModifier(0, 0, 0.5d) }),
                },
            }.Build();
            AssertHasError(VlatValidator.Validate(nullBindingDocument), "modifiers[0].meshBinding");
        }

        /// <summary>materialPropertyのfloat、color、vector、textureIndex、および未知valueTypeを検証します。</summary>
        [Test]
        public void Validate_MaterialPropertyValueTypeEdgesReportErrors()
        {
            var modifiers = new VlatModifier[]
            {
                new MaterialPropertyModifier(0, 0, " ", "float", null, true),
                new MaterialPropertyModifier(0, 0, "_FloatVector", "float", new VlatMaterialValue(new[] { 1d, 2d }), true),
                new MaterialPropertyModifier(0, 0, "_FloatNaN", "float", new VlatMaterialValue(double.NaN), true),
                new MaterialPropertyModifier(0, 0, "_ColorScalar", "color", new VlatMaterialValue(1d), true),
                new MaterialPropertyModifier(0, 0, "_VectorShort", "vector", new VlatMaterialValue(new[] { 1d, 2d, 3d }), true),
                new MaterialPropertyModifier(0, 0, "_ColorNaN", "color", new VlatMaterialValue(new[] { 1d, double.NaN, 3d, 4d }), true),
                new MaterialPropertyModifier(0, 0, "_VectorValid", "vector", new VlatMaterialValue(YieldComponents()), true),
                new MaterialPropertyModifier(0, 0, "_TextureVector", "textureIndex", new VlatMaterialValue(new[] { 0d }), false),
                new MaterialPropertyModifier(0, 0, "_TextureNegative", "textureIndex", new VlatMaterialValue(-1d), false),
                new MaterialPropertyModifier(0, 0, "_TextureFraction", "textureIndex", new VlatMaterialValue(0.5d), false),
                new MaterialPropertyModifier(0, 0, "_TextureHuge", "textureIndex", new VlatMaterialValue((double)int.MaxValue + 1d), false),
                new MaterialPropertyModifier(0, 0, "_TextureMissing", "textureIndex", new VlatMaterialValue(1d), true),
                new MaterialPropertyModifier(0, 0, "_TextureValid", "textureIndex", new VlatMaterialValue(0d), false),
                new MaterialPropertyModifier(0, 0, "_Unknown", "matrix", new VlatMaterialValue(0d), false),
                new MaterialPropertyModifier(0, 0, "_EmptyVector", "vector", new VlatMaterialValue((IEnumerable<double>)null), true),
            };
            var builder = CreateBuilderWithModifiers(modifiers);
            builder.Textures = new[] { new VlatTexture("Texture", "texture.png", null, null) };
            var diagnostics = VlatValidator.Validate(builder.Build());

            AssertHasError(diagnostics, "modifiers[0].property");
            AssertHasError(diagnostics, "modifiers[0].bandValue");
            AssertHasError(diagnostics, "modifiers[1].bandValue");
            AssertHasError(diagnostics, "modifiers[2].bandValue");
            AssertHasError(diagnostics, "modifiers[3].bandValue");
            AssertHasError(diagnostics, "modifiers[4].bandValue");
            AssertHasError(diagnostics, "modifiers[5].bandValue[1]");
            AssertNoErrorAt(diagnostics, "modifiers[6].bandValue");
            AssertHasError(diagnostics, "modifiers[7].bandValue");
            AssertHasError(diagnostics, "modifiers[8].bandValue");
            AssertHasError(diagnostics, "modifiers[9].bandValue");
            AssertHasError(diagnostics, "modifiers[10].bandValue");
            AssertHasError(diagnostics, "modifiers[11].bandValue");
            AssertHasError(diagnostics, "modifiers[11].interpolate");
            AssertNoErrorAt(diagnostics, "modifiers[12].bandValue");
            AssertHasError(diagnostics, "modifiers[13].valueType");
            AssertHasError(diagnostics, "modifiers[14].bandValue");
        }

        /// <summary>vertexDelta accessorとバイナリ列の不正な型、長さ、および参照を検出することを検証します。</summary>
        [Test]
        public void Validate_VertexAccessorAndMalformedBinaryEdgesReportErrors()
        {
            var binary = new byte[80];
            var views = new VlatBufferView[]
            {
                new VlatBufferView(0, 0, 3),
                new VlatBufferView(0, 3, 12),
                new VlatBufferView(0, 15, 4),
                new VlatBufferView(0, 19, 11),
                new VlatBufferView(0, 30, 12),
                new VlatBufferView(0, 42, 11),
                new VlatBufferView(0, 53, 24),
                null,
                new VlatBufferView(2, 0, 4),
                new VlatBufferView(0, 100, 4),
            };
            var modifiers = new VlatModifier[]
            {
                new VertexDeltaModifier(-1, null, new VlatAccessor(99, "UINT32"), new VlatAccessor(-1, "UINT32")),
                new VertexDeltaModifier(0, new VlatAccessor(0, "UINT32"), new VlatAccessor(1, "FLOAT32")),
                new VertexDeltaModifier(0, new VlatAccessor(2, "UINT32"), new VlatAccessor(3, "FLOAT32")),
                new VertexDeltaModifier(0, new VlatAccessor(2, "UINT32"), new VlatAccessor(4, "FLOAT32"), new VlatAccessor(5, "FLOAT32")),
                new VertexDeltaModifier(0, new VlatAccessor(2, "UINT32"), new VlatAccessor(4, "FLOAT32"), new VlatAccessor(6, "FLOAT32")),
                new VertexDeltaModifier(0, new VlatAccessor(7, "UINT32"), new VlatAccessor(4, "FLOAT32")),
                new VertexDeltaModifier(0, new VlatAccessor(8, "UINT32"), new VlatAccessor(4, "FLOAT32")),
                new VertexDeltaModifier(0, new VlatAccessor(9, "UINT32"), new VlatAccessor(4, "FLOAT32")),
            };
            var builder = CreateBuilderWithModifiers(modifiers);
            builder.Buffers = new[] { new VlatBuffer("vertices.bin", binary.Length) };
            builder.BufferViews = views;
            var diagnostics = VlatValidator.Validate(builder.Build(), new[] { binary });

            AssertHasError(diagnostics, "modifiers[0].meshBinding");
            AssertHasError(diagnostics, "modifiers[0].indices");
            AssertHasError(diagnostics, "modifiers[0].positions.bufferView");
            AssertHasError(diagnostics, "modifiers[0].positions.componentType");
            AssertHasError(diagnostics, "modifiers[0].normals.bufferView");
            AssertHasError(diagnostics, "modifiers[0].normals.componentType");
            AssertHasError(diagnostics, "modifiers[1].indices");
            AssertHasError(diagnostics, "modifiers[2].positions");
            AssertHasError(diagnostics, "modifiers[3].normals");
            AssertHasError(diagnostics, "modifiers[4].normals");
            AssertHasError(diagnostics, "$.bufferViews[7]");
            AssertHasError(diagnostics, "$.bufferViews[8].buffer");
            AssertHasError(diagnostics, "$.bufferViews[9]");

            var nullBinaryDiagnostics = VlatValidator.Validate(builder.Build(), new byte[][] { null });
            AssertHasError(nullBinaryDiagnostics, "$.buffers[0]");
        }

        /// <summary>SemVerの識別子、桁、区切り、および互換性診断を境界値で検証します。</summary>
        [Test]
        public void Validate_SemVerFormsAndCompatibilityReportExpectedDiagnostics()
        {
            var validVersions = new[]
            {
                "0.1.0",
                "0.1.0-alpha",
                "0.1.0-alpha.1+build.5",
                "0.1.0-0+meta.01",
                "0.1.0+build-meta",
                "0.1.0-alpha-beta+build-meta.01",
                "0.1.999999999999999999999999999999",
            };
            for (var i = 0; i < validVersions.Length; i++)
            {
                AssertNoErrorAt(
                    VlatValidator.Validate(CreateDocumentForVersion(validVersions[i])),
                    "$.vlat.version");
            }

            var invalidVersions = new string[]
            {
                null,
                "",
                "1",
                "1.0",
                "1..0",
                "1.0.",
                "1.0.0.1",
                "01.0.0",
                "1.01.0",
                "1.0.01",
                "1.a.0",
                "1.0.0-",
                "1.0.0+",
                "1.0.0+a+b",
                "1.0.0-alpha..1",
                "1.0.0-alpha.",
                "1.0.0-alpha_1",
                "1.0.0-01",
                "1.0.0-alpha.01",
            };
            for (var i = 0; i < invalidVersions.Length; i++)
            {
                AssertHasError(
                    VlatValidator.Validate(CreateDocumentForVersion(invalidVersions[i])),
                    "$.vlat.version");
            }

            var forwardMinor = VlatValidator.Validate(CreateDocumentForVersion("0.2.0"));
            AssertHasDiagnostic(forwardMinor, VlatDiagnosticSeverity.Warning, "$.vlat.version");
            var largeForwardMinor = VlatValidator.Validate(
                CreateDocumentForVersion("0.999999999999999999999999999999.0"));
            AssertNoErrorAt(largeForwardMinor, "$.vlat.version");
            AssertHasDiagnostic(largeForwardMinor, VlatDiagnosticSeverity.Warning, "$.vlat.version");
            var incompatibleMajor = VlatValidator.Validate(CreateDocumentForVersion("1.0.0"));
            AssertHasError(incompatibleMajor, "$.vlat.version");
        }

        /// <summary>コメント、単一引用符、末尾カンマ、escape、空白、数値、およびリテラルの非標準JSONを拒否します。</summary>
        [Test]
        public void Deserialize_NonStandardJsonSyntaxIsRejected()
        {
            AssertDeserializeFailure(null);
            var minimal = CreateMinimalJson();
            var invalidJson = new[]
            {
                "",
                "[]",
                minimal + " true",
                "/* comment */" + minimal,
                "{'vlat':null}",
                "{vlat:null}",
                minimal.Replace("\"layers\":[]", "\"layers\":[],\n"),
                minimal.Replace("Tests", "Te\\x73ts"),
                minimal.Replace("Tests", "Te\\'sts"),
                minimal.Replace("Tests", "Te\\u12G4sts"),
                minimal.Replace("Tests", "Te\tsts"),
                "{\u00A0}",
                "{\"x\":undefined}",
                "{\"x\":@}",
                "{\"x\":01}",
                "{\"x\":-}",
                "{\"x\":1.}",
                "{\"x\":1e}",
                "{\"x\":+1}",
                "{\"x\":NaN}",
                "{\"x\":Infinity}",
                "{\"unterminated\":\"value}",
            };
            for (var i = 0; i < invalidJson.Length; i++)
            {
                AssertDeserializeFailure(invalidJson[i]);
            }

            var escaped = minimal.Replace("Tests", "\\u0054ests");
            Assert.That(VlatSerializer.Deserialize(escaped).Success, Is.True);
            AssertDeserializeFailure("{\"x\":-0.25e+2}");
        }

        /// <summary>必須プロパティとプリミティブJSON型の不一致をすべて失敗結果として返すことを検証します。</summary>
        [Test]
        public void Deserialize_PrimitiveAndRequiredPropertyErrorsAreRejected()
        {
            var minimal = CreateMinimalJson();
            var layer = CreateJsonWithModifier(null);
            var invalidDocuments = new[]
            {
                "{}",
                "{\"vlat\":null,\"angleBands\":[],\"layers\":[]}",
                minimal.Replace("\"version\":\"0.1.0\"", "\"version\":1"),
                minimal.Replace("\"generator\":\"Tests\"", "\"generator\":1"),
                minimal.Replace("\"targetAvatar\":", "\"targetAvatar\":null,\"ignored\":"),
                minimal.Replace("\"meshBindings\":[]", "\"meshBindings\":null"),
                minimal.Replace("\"angleBands\":[]", "\"angleBands\":null"),
                minimal.Replace("\"layers\":[]", "\"layers\":{}"),
                CreateJsonWithLayer("0.5", "true", "[]"),
                CreateJsonWithLayer("0", "\"yes\"", "[]"),
                CreateJsonWithLayer("2147483648", "true", "[]"),
                CreateJsonWithLayer("\"zero\"", "true", "[]"),
                CreateMinimalJsonWithBuffer("\"one\""),
                CreateMinimalJsonWithBuffer("1.5"),
                CreateMinimalJsonWithBuffer("9223372036854775808"),
                CreateJsonWithAngleMinimum("\"zero\""),
                layer.Replace("\"tag\":\"other\"", "\"tag\":null"),
            };
            for (var i = 0; i < invalidDocuments.Length; i++)
            {
                AssertDeserializeFailure(invalidDocuments[i]);
            }

            Assert.That(VlatSerializer.Deserialize(CreateJsonWithLayer("1.0", "true", "[]")).Success, Is.True);
            Assert.That(VlatSerializer.Deserialize(CreateMinimalJsonWithBuffer("1.0")).Success, Is.True);
        }

        /// <summary>各Modifier converterの必須値、型、accessor、およびbandValue異常を拒否することを検証します。</summary>
        [Test]
        public void Deserialize_MalformedModifiersAndMaterialValuesAreRejected()
        {
            var invalidModifiers = new[]
            {
                "null",
                "{}",
                "{\"type\":1}",
                "{\"type\":\"unknown\"}",
                "{\"type\":\"vertexDelta\",\"meshBinding\":0,\"indices\":[],\"positions\":{\"bufferView\":0,\"componentType\":\"FLOAT32\"}}",
                "{\"type\":\"vertexDelta\",\"meshBinding\":0,\"indices\":{\"bufferView\":0,\"componentType\":\"UINT32\",\"extra\":1},\"positions\":{\"bufferView\":0,\"componentType\":\"FLOAT32\"}}",
                "{\"type\":\"vertexDelta\",\"meshBinding\":0,\"indices\":{\"bufferView\":0,\"componentType\":\"UINT32\"},\"positions\":{\"bufferView\":0,\"componentType\":\"FLOAT32\"},\"normals\":null}",
                "{\"type\":\"vertexDelta\",\"meshBinding\":0.5,\"indices\":{\"bufferView\":0,\"componentType\":\"UINT32\"},\"positions\":{\"bufferView\":0,\"componentType\":\"FLOAT32\"}}",
                "{\"type\":\"visibility\",\"meshBinding\":0,\"subMesh\":0,\"visibleInBand\":1}",
                "{\"type\":\"opacity\",\"meshBinding\":0,\"materialSlot\":0,\"value\":\"half\"}",
                "{\"type\":\"materialProperty\",\"meshBinding\":0,\"materialSlot\":0,\"property\":\"_Value\",\"valueType\":\"float\",\"bandValue\":{},\"interpolate\":true}",
                "{\"type\":\"materialProperty\",\"meshBinding\":0,\"materialSlot\":0,\"property\":\"_Value\",\"valueType\":\"vector\",\"bandValue\":[1,\"two\",3,4],\"interpolate\":true}",
                "{\"type\":\"materialProperty\",\"meshBinding\":0,\"materialSlot\":0,\"property\":\"_Value\",\"valueType\":\"float\",\"bandValue\":null,\"interpolate\":true}",
                "{\"type\":\"renderQueue\",\"meshBinding\":0,\"materialSlot\":0,\"offset\":2147483648}",
                "{\"type\":\"opacity\",\"meshBinding\":0,\"materialSlot\":0,\"value\":0.5,\"extra\":true}",
            };
            for (var i = 0; i < invalidModifiers.Length; i++)
            {
                AssertDeserializeFailure(CreateJsonWithModifier(invalidModifiers[i]));
            }
        }

        /// <summary>Loadが空パス、不在JSON、不正JSON、不在buffer、長さ不一致、および絶対URIを安全に報告することを検証します。</summary>
        [Test]
        public void Load_InvalidPathsJsonAndBuffersReturnFailureResults()
        {
            Assert.That(VlatSerializer.Load(null).Success, Is.False);
            Assert.That(VlatSerializer.Load(" ").Success, Is.False);

            var directory = CreateTemporaryDirectory();
            try
            {
                Assert.That(VlatSerializer.Load(Path.Combine(directory, "missing.vlat.json")).Success, Is.False);

                var invalidJsonPath = Path.Combine(directory, "invalid.vlat.json");
                File.WriteAllText(invalidJsonPath, "{ invalid }");
                Assert.That(VlatSerializer.Load(invalidJsonPath).Success, Is.False);

                var missingBufferDocument = CreateDocumentWithBuffer("missing.bin", 4);
                var missingBufferPath = Path.Combine(directory, "missing-buffer.vlat.json");
                File.WriteAllText(missingBufferPath, VlatSerializer.Serialize(missingBufferDocument));
                var missingBuffer = VlatSerializer.Load(missingBufferPath);
                Assert.That(missingBuffer.Success, Is.False);
                Assert.That(missingBuffer.Buffers.Count, Is.EqualTo(1));
                Assert.That(missingBuffer.Buffers[0], Is.Null);
                AssertHasLocatedError(missingBuffer.Diagnostics, "$.buffers[0].uri");

                var mismatchDocument = CreateDocumentWithBuffer("mismatch.bin", 3);
                var mismatchPath = Path.Combine(directory, "mismatch.vlat.json");
                File.WriteAllText(mismatchPath, VlatSerializer.Serialize(mismatchDocument));
                File.WriteAllBytes(Path.Combine(directory, "mismatch.bin"), new byte[] { 1, 2 });
                AssertHasError(VlatSerializer.Load(mismatchPath).Diagnostics, "$.buffers[0].byteLength");

                var absoluteDocument = CreateDocumentWithBuffer("https://example.com/data.bin", 1);
                var absolutePath = Path.Combine(directory, "absolute.vlat.json");
                File.WriteAllText(absolutePath, VlatSerializer.Serialize(absoluteDocument));
                var absolute = VlatSerializer.Load(absolutePath);
                Assert.That(absolute.Success, Is.False);
                AssertHasLocatedError(absolute.Diagnostics, "$.buffers[0].uri");

                var encodedAbsoluteDocument = CreateDocumentWithBuffer("%2Ftmp%2Fvlat-encoded-load.bin", 1);
                var encodedAbsolutePath = Path.Combine(directory, "encoded-absolute.vlat.json");
                File.WriteAllText(encodedAbsolutePath, VlatSerializer.Serialize(encodedAbsoluteDocument));
                var encodedAbsolute = VlatSerializer.Load(encodedAbsolutePath);
                Assert.That(encodedAbsolute.Success, Is.False);
                AssertHasLocatedError(encodedAbsolute.Diagnostics, "$.buffers[0].uri");
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        /// <summary>Saveがnull、buffer不足、絶対URI、および保存先重複をファイル作成前に拒否することを検証します。</summary>
        [Test]
        public void Save_InvalidArgumentsAndDuplicateTargetsReturnFailureResults()
        {
            var directory = CreateTemporaryDirectory();
            try
            {
                var minimal = new DocumentBuilder().Build();
                Assert.That(VlatSerializer.Save(null, minimal, Array.Empty<byte[]>()).Success, Is.False);
                Assert.That(VlatSerializer.Save(" ", minimal, Array.Empty<byte[]>()).Success, Is.False);
                Assert.That(
                    VlatSerializer.Save(Path.Combine(directory, "null.vlat.json"), null, Array.Empty<byte[]>()).Success,
                    Is.False);

                var oneBuffer = CreateDocumentWithBuffer("one.bin", 1);
                Assert.That(
                    VlatSerializer.Save(Path.Combine(directory, "missing-bytes.vlat.json"), oneBuffer, null).Success,
                    Is.False);
                Assert.That(
                    VlatSerializer.Save(
                        Path.Combine(directory, "count.vlat.json"),
                        oneBuffer,
                        Array.Empty<byte[]>()).Success,
                    Is.False);

                var absolute = CreateDocumentWithBuffer("https://example.com/data.bin", 1);
                Assert.That(
                    VlatSerializer.Save(
                        Path.Combine(directory, "absolute.vlat.json"),
                        absolute,
                        new[] { new byte[] { 1 } }).Success,
                    Is.False);

                var encodedAbsolute = CreateDocumentWithBuffer("%2Ftmp%2Fvlat-encoded-save.bin", 1);
                Assert.That(
                    VlatSerializer.Save(
                        Path.Combine(directory, "encoded-absolute.vlat.json"),
                        encodedAbsolute,
                        new[] { new byte[] { 1 } }).Success,
                    Is.False);

                var duplicate = new DocumentBuilder
                {
                    Buffers = new[] { new VlatBuffer("same.bin", 1), new VlatBuffer("same.bin", 1) },
                }.Build();
                Assert.That(
                    VlatSerializer.Save(
                        Path.Combine(directory, "duplicate.vlat.json"),
                        duplicate,
                        new[] { new byte[] { 1 }, new byte[] { 2 } }).Success,
                    Is.False);

                var jsonCollision = CreateDocumentWithBuffer("collision.vlat.json", 1);
                Assert.That(
                    VlatSerializer.Save(
                        Path.Combine(directory, "collision.vlat.json"),
                        jsonCollision,
                        new[] { new byte[] { 1 } }).Success,
                    Is.False);
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        /// <summary>親相対URIと上書き保存が成功し、入力bufferと保存済みbufferが相互に独立することを検証します。</summary>
        [Test]
        public void SaveLoad_ParentRelativeOverwriteAndCopiesRemainIndependent()
        {
            var directory = CreateTemporaryDirectory();
            try
            {
                var jsonPath = Path.Combine(directory, "nested", "document.vlat.json");
                var document = CreateDocumentWithBuffer("../shared/data.bin", 4);
                var first = new byte[] { 1, 2, 3, 4 };
                var firstCopy = (byte[])first.Clone();

                Assert.That(VlatSerializer.Save(jsonPath, document, new[] { first }).Success, Is.True);
                CollectionAssert.AreEqual(firstCopy, first);
                var loadedFirst = VlatSerializer.Load(jsonPath);
                Assert.That(loadedFirst.Success, Is.True);
                CollectionAssert.AreEqual(firstCopy, loadedFirst.Buffers[0]);

                loadedFirst.Buffers[0][0] = 99;
                CollectionAssert.AreEqual(firstCopy, File.ReadAllBytes(Path.Combine(directory, "shared", "data.bin")));

                var second = new byte[] { 5, 6, 7, 8 };
                var secondCopy = (byte[])second.Clone();
                Assert.That(VlatSerializer.Save(jsonPath, document, new[] { second }).Success, Is.True);
                CollectionAssert.AreEqual(secondCopy, second);
                CollectionAssert.AreEqual(secondCopy, VlatSerializer.Load(jsonPath).Buffers[0]);
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        /// <summary>JSON commit失敗時に先に置換した既存bufferを元へrollbackすることを検証します。</summary>
        [Test]
        public void Save_CommitFailureRestoresExistingBuffer()
        {
            var directory = CreateTemporaryDirectory();
            try
            {
                var blockedJsonPath = Path.Combine(directory, "blocked.vlat.json");
                Directory.CreateDirectory(blockedJsonPath);
                var bufferPath = Path.Combine(directory, "rollback.bin");
                var original = new byte[] { 1, 2, 3, 4 };
                File.WriteAllBytes(bufferPath, original);
                var document = CreateDocumentWithBuffer("rollback.bin", 4);

                var result = VlatSerializer.Save(
                    blockedJsonPath,
                    document,
                    new[] { new byte[] { 9, 9, 9, 9 } });

                Assert.That(result.Success, Is.False);
                AssertHasError(result.Diagnostics, "$");
                CollectionAssert.AreEqual(original, File.ReadAllBytes(bufferPath));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        /// <summary>結果型と公開converterが入力を防御コピーし、null・scalar・vector・未知型を処理することを検証します。</summary>
        [Test]
        public void ResultTypesAndPublicConverters_HandleEdgeValues()
        {
            var source = new byte[] { 1, 2, 3 };
            var warning = new VlatDiagnostic(VlatDiagnosticSeverity.Warning, null, 0, 0, null);
            var load = new VlatLoadResult(
                new DocumentBuilder().Build(),
                new byte[][] { source, null },
                new VlatDiagnostic[] { null, warning });
            source[0] = 9;

            Assert.That(load.Success, Is.True);
            Assert.That(load.Buffers.Count, Is.EqualTo(2));
            Assert.That(load.Buffers[0][0], Is.EqualTo(1));
            Assert.That(load.Buffers[1], Is.Null);
            Assert.That(load.Diagnostics.Count, Is.EqualTo(2));
            Assert.That(new VlatLoadResult(null, null, null).Success, Is.False);
            Assert.That(new VlatSaveResult(null).Success, Is.True);
            Assert.That(
                new VlatSaveResult(
                    new[] { new VlatDiagnostic(VlatDiagnosticSeverity.Error, "$", 0, 0, "error") }).Success,
                Is.False);

            var modifierConverter = new VlatModifierJsonConverter();
            var materialValueConverter = new VlatMaterialValueJsonConverter();
            Assert.That(modifierConverter.CanConvert(typeof(VlatModifier)), Is.True);
            Assert.That(modifierConverter.CanConvert(typeof(string)), Is.False);
            Assert.That(materialValueConverter.CanConvert(typeof(VlatMaterialValue)), Is.True);
            Assert.That(materialValueConverter.CanConvert(typeof(double)), Is.False);

            Assert.That(WriteWithModifierConverter(modifierConverter, null), Is.EqualTo("null"));
            Assert.That(WriteWithMaterialValueConverter(materialValueConverter, null), Is.EqualTo("null"));
            Assert.That(
                WriteWithMaterialValueConverter(materialValueConverter, new VlatMaterialValue(0.25d)),
                Is.EqualTo("0.25"));
            Assert.That(
                WriteWithMaterialValueConverter(materialValueConverter, new VlatMaterialValue(new[] { 1d, 2d })),
                Is.EqualTo("[1.0,2.0]"));

            Assert.Throws<ArgumentNullException>(() => VlatSerializer.Serialize(null));
            var unsupported = CreateDocumentWithModifiers(new VlatModifier[] { new UnsupportedModifier() });
            Assert.Throws<JsonSerializationException>(() => VlatSerializer.Serialize(unsupported));
        }

        /// <summary>ConflictResolverが不正引数を拒否し、disabled・不明band・reset後の状態を正しく扱うことを検証します。</summary>
        [Test]
        public void ConflictResolver_ArgumentsInactiveEntriesAndResetAreHandled()
        {
            Assert.Throws<ArgumentNullException>(() => new ConflictResolver(null));

            var activeDocument = CreateDocumentWithModifiers(
                new VlatModifier[] { new VisibilityModifier(0, 0, false) });
            var resolver = new ConflictResolver(activeDocument);
            var state = resolver.CreateState();
            Assert.Throws<ArgumentNullException>(() => resolver.Resolve(null, state));
            Assert.Throws<ArgumentNullException>(() => resolver.Resolve(new[] { 1d }, null));

            var otherResolver = new ConflictResolver(activeDocument);
            Assert.Throws<ArgumentException>(() => resolver.Resolve(new[] { 1d }, otherResolver.CreateState()));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                var ignored = state[-1];
            });
            Assert.Throws<ArgumentOutOfRangeException>(() => state.GetBaseWeight(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => state.GetBaseWeight(state.GroupCount));

            resolver.Resolve(new ViewAngles(0d, 0d), new[] { 1d }, state);
            Assert.That(state.Count, Is.EqualTo(1));
            resolver.ResetHysteresis();
            resolver.Resolve(new ViewAngles(0d, 0d), new[] { 0.4d }, state);
            Assert.That(state.Count, Is.Zero);

            var inactiveDocument = new DocumentBuilder
            {
                AngleBands = new AngleBand[] { null, CreateValidBand() },
                Layers = new VlatLayer[]
                {
                    null,
                    new VlatLayer("disabled", "Disabled", "other", false, 0, "band", new VlatModifier[]
                    {
                        new VisibilityModifier(0, 0, false),
                        null,
                    }),
                    new VlatLayer("missing", "Missing", "other", true, 0, "missing-band", new VlatModifier[]
                    {
                        new VisibilityModifier(0, 0, false),
                    }),
                },
            }.Build();
            var inactiveResolver = new ConflictResolver(inactiveDocument);
            var inactiveState = inactiveResolver.CreateState();
            inactiveResolver.Resolve(new ViewAngles(0d, 0d), new[] { double.NaN }, inactiveState);
            Assert.That(inactiveState.Count, Is.Zero);
            inactiveResolver.Resolve(Array.Empty<double>(), inactiveState);
            Assert.That(inactiveState.Count, Is.Zero);
            inactiveResolver.ResetHysteresis();
        }

        private static DocumentBuilder CreateBuilderWithModifiers(IEnumerable<VlatModifier> modifiers)
        {
            return new DocumentBuilder
            {
                AngleBands = new[] { CreateValidBand() },
                Layers = new[] { CreateLayer(modifiers) },
            };
        }

        private static VlatDocument CreateDocumentWithModifiers(IEnumerable<VlatModifier> modifiers)
        {
            return CreateBuilderWithModifiers(modifiers).Build();
        }

        private static VlatDocument CreateDocumentWithBuffer(string uri, long byteLength)
        {
            return new DocumentBuilder
            {
                Buffers = new[] { new VlatBuffer(uri, byteLength) },
            }.Build();
        }

        private static VlatDocument CreateDocumentForVersion(string version)
        {
            return new DocumentBuilder
            {
                Metadata = CreateValidMetadata(version),
            }.Build();
        }

        private static VlatMetadata CreateValidMetadata(string version = "0.1.0")
        {
            var material = new VlatMaterialSlot(0, "Material", "VRM10/MToon10");
            var mesh = new MeshBinding("Face", "Face", 4, ValidHash, new[] { material });
            return new VlatMetadata(
                version,
                "Tests",
                new TargetAvatar("Avatar", "1.0", new[] { mesh }));
        }

        private static AngleBand CreateValidBand()
        {
            return new AngleBand(
                "band",
                "Band",
                new AngleRange(-20d, 20d),
                new AngleRange(-20d, 20d),
                new AngleFeather(5d, 5d),
                2d);
        }

        private static VlatLayer CreateLayer(IEnumerable<VlatModifier> modifiers)
        {
            return new VlatLayer("layer", "Layer", "other", true, 0, "band", modifiers);
        }

        private static IEnumerable<double> YieldComponents()
        {
            yield return 1d;
            yield return 2d;
            yield return 3d;
            yield return 4d;
        }

        private static string CreateMinimalJson()
        {
            return
                "{\"vlat\":{\"version\":\"0.1.0\",\"generator\":\"Tests\",\"targetAvatar\":" +
                "{\"name\":\"Avatar\",\"vrmVersion\":\"1.0\",\"meshBindings\":[]}}," +
                "\"angleBands\":[],\"layers\":[]}";
        }

        private static string CreateMinimalJsonWithBuffer(string byteLengthJson)
        {
            return CreateMinimalJson().Replace(
                "\"angleBands\":[]",
                "\"buffers\":[{\"uri\":\"data.bin\",\"byteLength\":" + byteLengthJson + "}],\"angleBands\":[]");
        }

        private static string CreateJsonWithAngleMinimum(string minimumJson)
        {
            return CreateMinimalJson().Replace(
                "\"angleBands\":[]",
                "\"angleBands\":[{\"id\":\"band\",\"name\":\"Band\",\"yaw\":{\"min\":" + minimumJson +
                ",\"max\":20},\"pitch\":{\"min\":-20,\"max\":20},\"feather\":{\"yaw\":5,\"pitch\":5}}]");
        }

        private static string CreateJsonWithLayer(
            string priorityJson,
            string enabledJson,
            string modifiersJson)
        {
            return
                "{\"vlat\":{\"version\":\"0.1.0\",\"generator\":\"Tests\",\"targetAvatar\":" +
                "{\"name\":\"Avatar\",\"vrmVersion\":\"1.0\",\"meshBindings\":[]}}," +
                "\"angleBands\":[{\"id\":\"band\",\"name\":\"Band\",\"yaw\":{\"min\":-20,\"max\":20}," +
                "\"pitch\":{\"min\":-20,\"max\":20},\"feather\":{\"yaw\":5,\"pitch\":5}}]," +
                "\"layers\":[{\"id\":\"layer\",\"name\":\"Layer\",\"tag\":\"other\",\"enabled\":" + enabledJson +
                ",\"priority\":" + priorityJson + ",\"bandId\":\"band\",\"modifiers\":" + modifiersJson + "}]}";
        }

        private static string CreateJsonWithModifier(string modifierJson)
        {
            var modifier = modifierJson ?? string.Empty;
            var modifiers = modifier.Length == 0 ? "[]" : "[" + modifier + "]";
            var json = CreateJsonWithLayer("0", "true", modifiers);
            return json.Replace(
                "\"meshBindings\":[]",
                "\"meshBindings\":[{\"hierarchyPath\":\"Face\",\"meshName\":\"Face\",\"vertexCount\":4," +
                "\"positionHash\":\"" + ValidHash + "\",\"materials\":[{\"slot\":0,\"materialName\":\"Material\"," +
                "\"shader\":\"VRM10/MToon10\"}]}]");
        }

        private static void AssertDeserializeFailure(string json)
        {
            var result = VlatSerializer.Deserialize(json);
            Assert.That(result.Success, Is.False, "JSON should be rejected: {0}", json);
            Assert.That(result.Diagnostics.Count, Is.GreaterThan(0));
        }

        private static void AssertHasError(
            IReadOnlyList<VlatDiagnostic> diagnostics,
            string expectedPathPart)
        {
            AssertHasDiagnostic(diagnostics, VlatDiagnosticSeverity.Error, expectedPathPart);
        }

        private static void AssertHasLocatedError(
            IReadOnlyList<VlatDiagnostic> diagnostics,
            string expectedPathPart)
        {
            for (var i = 0; i < diagnostics.Count; i++)
            {
                var diagnostic = diagnostics[i];
                if (diagnostic != null &&
                    diagnostic.Severity == VlatDiagnosticSeverity.Error &&
                    diagnostic.Path.IndexOf(expectedPathPart, StringComparison.Ordinal) >= 0 &&
                    diagnostic.Line > 0 &&
                    diagnostic.Column > 0)
                {
                    return;
                }
            }

            Assert.Fail("行番号付きエラーがありません: {0}", expectedPathPart);
        }

        private static void AssertHasDiagnostic(
            IReadOnlyList<VlatDiagnostic> diagnostics,
            VlatDiagnosticSeverity severity,
            string expectedPathPart)
        {
            for (var i = 0; i < diagnostics.Count; i++)
            {
                var diagnostic = diagnostics[i];
                if (diagnostic != null &&
                    diagnostic.Severity == severity &&
                    diagnostic.Path.IndexOf(expectedPathPart, StringComparison.Ordinal) >= 0)
                {
                    return;
                }
            }

            Assert.Fail("期待した診断がありません: {0} ({1})", expectedPathPart, severity);
        }

        private static void AssertNoErrorAt(
            IReadOnlyList<VlatDiagnostic> diagnostics,
            string expectedPathPart)
        {
            for (var i = 0; i < diagnostics.Count; i++)
            {
                var diagnostic = diagnostics[i];
                if (diagnostic != null &&
                    diagnostic.Severity == VlatDiagnosticSeverity.Error &&
                    diagnostic.Path.IndexOf(expectedPathPart, StringComparison.Ordinal) >= 0)
                {
                    Assert.Fail("想定外のエラーがあります: {0}: {1}", diagnostic.Path, diagnostic.Message);
                }
            }
        }

        private static string WriteWithModifierConverter(VlatModifierJsonConverter converter, object value)
        {
            using (var output = new StringWriter())
            using (var writer = new JsonTextWriter(output))
            {
                converter.WriteJson(writer, value, new JsonSerializer());
                writer.Flush();
                return output.ToString();
            }
        }

        private static string WriteWithMaterialValueConverter(
            VlatMaterialValueJsonConverter converter,
            object value)
        {
            using (var output = new StringWriter())
            using (var writer = new JsonTextWriter(output))
            {
                converter.WriteJson(writer, value, new JsonSerializer());
                writer.Flush();
                return output.ToString();
            }
        }

        private static string CreateTemporaryDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "VlatEdgeTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteTemporaryDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        private sealed class UnsupportedModifier : VlatModifier
        {
            public UnsupportedModifier()
                : base("unsupported")
            {
            }
        }

        private sealed class DocumentBuilder
        {
            public VlatMetadata Metadata = CreateValidMetadata();
            public VlatBuffer[] Buffers = Array.Empty<VlatBuffer>();
            public VlatBufferView[] BufferViews = Array.Empty<VlatBufferView>();
            public VlatTexture[] Textures = Array.Empty<VlatTexture>();
            public AngleBand[] AngleBands = Array.Empty<AngleBand>();
            public VlatLayer[] Layers = Array.Empty<VlatLayer>();

            public VlatDocument Build()
            {
                return new VlatDocument(Metadata, Buffers, BufferViews, Textures, AngleBands, Layers);
            }
        }
    }
}
