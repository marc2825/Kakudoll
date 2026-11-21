using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using VLat.Core.Model;
using VLat.Core.Serialization;
using VLat.Core.Validation;

namespace VLat.Tests.Edit
{
    /// <summary>標準JSON、Schema必須条件、およびModifier判別共用体の厳密性を検証します。</summary>
    public sealed class VlatStrictJsonTests
    {
        /// <summary>null、空入力、およびobject以外のルートを安全に拒否することを検証します。</summary>
        [Test]
        public void Deserialize_RejectsNullEmptyAndNonObjectRoots()
        {
            AssertRejected(VlatSerializer.Deserialize(null), "null input", false);
            AssertRejected(VlatSerializer.Deserialize(string.Empty), "empty input", false);
            AssertRejected(VlatSerializer.Deserialize("   \r\n\t"), "whitespace input", false);
            AssertRejected(VlatSerializer.Deserialize("[]"), "array root", true);
            AssertRejected(VlatSerializer.Deserialize("\"text\""), "string root", true);
            AssertRejected(VlatSerializer.Deserialize("1"), "number root", true);
            AssertRejected(VlatSerializer.Deserialize("null"), "null root", true);
        }

        /// <summary>Newtonsoft.Jsonが既定で許容する非標準JSON構文をすべて拒否することを検証します。</summary>
        [Test]
        public void Deserialize_RejectsNonStandardJsonLexemes()
        {
            var minimal = CreateMinimalJson();
            var full = CreateFullJson();
            var cases = new[]
            {
                new JsonCase("trailing content", minimal + "\n{}"),
                new JsonCase("line comment", minimal.Replace("{\n", "{\n// comment\n")),
                new JsonCase("block comment", minimal.Replace("{\n", "{\n/* comment */\n")),
                new JsonCase("single quote", minimal.Replace('"', '\'')),
                new JsonCase("trailing comma", minimal.Replace("  \"layers\": []\n}", "  \"layers\": [],\n}")),
                new JsonCase("unquoted key", minimal.Replace("\"vlat\"", "vlat")),
                new JsonCase("invalid literal", full.Replace("\"enabled\": true", "\"enabled\": truth")),
                new JsonCase("leading zero", full.Replace("\"vertexCount\": 4", "\"vertexCount\": 04")),
                new JsonCase("hex number", full.Replace("\"vertexCount\": 4", "\"vertexCount\": 0x4")),
                new JsonCase("bad exponent", full.Replace("\"vertexCount\": 4", "\"vertexCount\": 4e")),
                new JsonCase("non-standard escape", minimal.Replace("VLat Strict Tests", "VLat\\'Strict Tests")),
                new JsonCase("bad unicode escape", minimal.Replace("VLat Strict Tests", "VLat\\u12G4 Tests")),
                new JsonCase("raw tab", minimal.Replace("VLat Strict Tests", "VLat\tStrict Tests")),
                new JsonCase("duplicate property", minimal.Replace(
                    "    \"version\": \"0.1.0\",",
                    "    \"version\": \"0.1.0\",\n    \"version\": \"0.1.0\",")),
                new JsonCase("case mismatch", minimal.Replace("\"vlat\"", "\"Vlat\"")),
                new JsonCase("unknown nested property", AddProperty(full, "$.vlat.targetAvatar", "unexpected", true)),
            };

            for (var index = 0; index < cases.Length; index++)
            {
                AssertRejected(
                    VlatSerializer.Deserialize(cases[index].Json),
                    cases[index].Name,
                    true);
            }
        }

        /// <summary>通常モデルの全必須プロパティが欠落またはnullの場合に拒否することを検証します。</summary>
        [Test]
        public void Deserialize_RejectsEveryMissingOrNullRequiredModelProperty()
        {
            var full = CreateFullJson();
            var requiredPaths = new[]
            {
                "$.vlat",
                "$.vlat.version",
                "$.vlat.generator",
                "$.vlat.targetAvatar",
                "$.vlat.targetAvatar.name",
                "$.vlat.targetAvatar.vrmVersion",
                "$.vlat.targetAvatar.meshBindings",
                "$.vlat.targetAvatar.meshBindings[0].hierarchyPath",
                "$.vlat.targetAvatar.meshBindings[0].meshName",
                "$.vlat.targetAvatar.meshBindings[0].vertexCount",
                "$.vlat.targetAvatar.meshBindings[0].positionHash",
                "$.vlat.targetAvatar.meshBindings[0].materials",
                "$.vlat.targetAvatar.meshBindings[0].materials[0].slot",
                "$.vlat.targetAvatar.meshBindings[0].materials[0].materialName",
                "$.vlat.targetAvatar.meshBindings[0].materials[0].shader",
                "$.buffers[0].uri",
                "$.buffers[0].byteLength",
                "$.bufferViews[0].buffer",
                "$.bufferViews[0].byteOffset",
                "$.bufferViews[0].byteLength",
                "$.textures[0].uri",
                "$.textures[1].bufferView",
                "$.textures[1].mimeType",
                "$.angleBands",
                "$.angleBands[0].id",
                "$.angleBands[0].name",
                "$.angleBands[0].yaw",
                "$.angleBands[0].yaw.min",
                "$.angleBands[0].yaw.max",
                "$.angleBands[0].pitch",
                "$.angleBands[0].pitch.min",
                "$.angleBands[0].pitch.max",
                "$.angleBands[0].feather",
                "$.angleBands[0].feather.yaw",
                "$.angleBands[0].feather.pitch",
                "$.layers",
                "$.layers[0].id",
                "$.layers[0].name",
                "$.layers[0].tag",
                "$.layers[0].enabled",
                "$.layers[0].priority",
                "$.layers[0].bandId",
                "$.layers[0].modifiers",
            };

            for (var index = 0; index < requiredPaths.Length; index++)
            {
                var path = requiredPaths[index];
                AssertRejected(
                    VlatSerializer.Deserialize(RemoveProperty(full, path)),
                    "missing " + path,
                    true);
                AssertRejected(
                    VlatSerializer.Deserialize(SetProperty(full, path, JValue.CreateNull())),
                    "null " + path,
                    true);
            }
        }

        /// <summary>string/int/nullable int/long/double/boolの型不正と数値範囲外を拒否することを検証します。</summary>
        [Test]
        public void Deserialize_RejectsWrongPrimitiveTypesAndRanges()
        {
            var full = CreateFullJson();
            var cases = new[]
            {
                new MutationCase("string from integer", "$.vlat.generator", new JValue(1)),
                new MutationCase("int from string", "$.vlat.targetAvatar.meshBindings[0].vertexCount", new JValue("4")),
                new MutationCase("nullable int from string", "$.textures[1].bufferView", new JValue("3")),
                new MutationCase("long from string", "$.buffers[0].byteLength", new JValue("32")),
                new MutationCase("double from string", "$.angleBands[0].yaw.min", new JValue("-20")),
                new MutationCase("bool from integer", "$.layers[0].enabled", new JValue(1)),
                new MutationCase("int overflow", "$.vlat.targetAvatar.meshBindings[0].vertexCount", new JValue(2147483648L)),
                new MutationCase("long overflow", "$.buffers[0].byteLength", new JValue(9223372036854775808m)),
                new MutationCase("fractional int", "$.layers[0].priority", new JValue(1.5d)),
                new MutationCase("negative vertex count", "$.vlat.targetAvatar.meshBindings[0].vertexCount", new JValue(-1)),
                new MutationCase("zero buffer length", "$.buffers[0].byteLength", new JValue(0)),
                new MutationCase("zero view length", "$.bufferViews[0].byteLength", new JValue(0)),
                new MutationCase("yaw out of range", "$.angleBands[0].yaw.min", new JValue(-181d)),
                new MutationCase("opacity out of range", "$.layers[0].modifiers[2].value", new JValue(1.1d)),
            };

            for (var index = 0; index < cases.Length; index++)
            {
                var item = cases[index];
                AssertRejected(
                    VlatSerializer.Deserialize(SetProperty(full, item.Path, item.Value)),
                    item.Name,
                    true);
            }
        }

        /// <summary>Schemaのinteger型が整数値の1.0表現を通常モデルとModifierの双方で受理することを検証します。</summary>
        [Test]
        public void Deserialize_AcceptsIntegralFloatingPointTokensForEveryIntegerShape()
        {
            var root = JObject.Parse(CreateFullJson());
            var paths = new[]
            {
                "$.vlat.targetAvatar.meshBindings[0].vertexCount",
                "$.vlat.targetAvatar.meshBindings[0].materials[0].slot",
                "$.buffers[0].byteLength",
                "$.bufferViews[0].buffer",
                "$.bufferViews[0].byteOffset",
                "$.bufferViews[0].byteLength",
                "$.textures[1].bufferView",
                "$.layers[0].priority",
                "$.layers[0].modifiers[0].meshBinding",
                "$.layers[0].modifiers[0].indices.bufferView",
                "$.layers[0].modifiers[1].subMesh",
                "$.layers[0].modifiers[2].materialSlot",
                "$.layers[0].modifiers[7].offset",
            };

            for (var index = 0; index < paths.Length; index++)
            {
                var token = RequireToken(root, paths[index]);
                token.Replace(new JValue(Convert.ToDouble(((JValue)token).Value)));
            }

            var result = VlatSerializer.Deserialize(root.ToString(Formatting.None));

            Assert.IsTrue(result.Success, DumpDiagnostics(result.Diagnostics));

            var exactBeyondDouble = VlatSerializer.Deserialize(CreateFullJson().Replace(
                "\"byteLength\": 32",
                "\"byteLength\": 9007199254740993.0"));
            Assert.IsTrue(exactBeyondDouble.Success, DumpDiagnostics(exactBeyondDouble.Diagnostics));
            Assert.AreEqual(9007199254740993L, exactBeyondDouble.Document.Buffers[0].ByteLength);

            var maximumInt64 = VlatSerializer.Deserialize(CreateFullJson().Replace(
                "\"byteLength\": 32",
                "\"byteLength\": 9223372036854775807.0"));
            Assert.IsTrue(maximumInt64.Success, DumpDiagnostics(maximumInt64.Diagnostics));
            Assert.AreEqual(long.MaxValue, maximumInt64.Document.Buffers[0].ByteLength);

            var integralExponent = VlatSerializer.Deserialize(CreateFullJson().Replace(
                "\"byteLength\": 32",
                "\"byteLength\": 3.2e1"));
            Assert.IsTrue(integralExponent.Success, DumpDiagnostics(integralExponent.Diagnostics));
            Assert.AreEqual(32L, integralExponent.Document.Buffers[0].ByteLength);

            AssertRejected(
                VlatSerializer.Deserialize(CreateFullJson().Replace(
                    "\"byteLength\": 32",
                    "\"byteLength\": 9223372036854775808.0")),
                "long overflow represented as float",
                true);
            AssertRejected(
                VlatSerializer.Deserialize(CreateFullJson().Replace(
                    "\"byteLength\": 32",
                    "\"byteLength\": 32.0000000000000000001")),
                "long fractional precision loss",
                true);
            AssertRejected(
                VlatSerializer.Deserialize(CreateFullJson().Replace(
                    "\"priority\": 0",
                    "\"priority\": 1e-400")),
                "integer exponent underflow",
                true);
        }

        /// <summary>全Modifierの必須プロパティ欠落/nullと余計なプロパティを拒否することを検証します。</summary>
        [Test]
        public void Deserialize_RejectsMissingNullAndExtraModifierProperties()
        {
            var full = CreateFullJson();
            var requiredPaths = new[]
            {
                "$.layers[0].modifiers[0].type",
                "$.layers[0].modifiers[0].meshBinding",
                "$.layers[0].modifiers[0].indices",
                "$.layers[0].modifiers[0].indices.bufferView",
                "$.layers[0].modifiers[0].indices.componentType",
                "$.layers[0].modifiers[0].positions",
                "$.layers[0].modifiers[0].positions.bufferView",
                "$.layers[0].modifiers[0].positions.componentType",
                "$.layers[0].modifiers[1].type",
                "$.layers[0].modifiers[1].meshBinding",
                "$.layers[0].modifiers[1].subMesh",
                "$.layers[0].modifiers[1].visibleInBand",
                "$.layers[0].modifiers[2].type",
                "$.layers[0].modifiers[2].meshBinding",
                "$.layers[0].modifiers[2].materialSlot",
                "$.layers[0].modifiers[2].value",
                "$.layers[0].modifiers[3].type",
                "$.layers[0].modifiers[3].meshBinding",
                "$.layers[0].modifiers[3].materialSlot",
                "$.layers[0].modifiers[3].property",
                "$.layers[0].modifiers[3].valueType",
                "$.layers[0].modifiers[3].bandValue",
                "$.layers[0].modifiers[3].interpolate",
                "$.layers[0].modifiers[7].type",
                "$.layers[0].modifiers[7].meshBinding",
                "$.layers[0].modifiers[7].materialSlot",
                "$.layers[0].modifiers[7].offset",
            };

            for (var index = 0; index < requiredPaths.Length; index++)
            {
                var path = requiredPaths[index];
                AssertRejected(VlatSerializer.Deserialize(RemoveProperty(full, path)), "missing " + path, true);
                AssertRejected(VlatSerializer.Deserialize(SetProperty(full, path, JValue.CreateNull())), "null " + path, true);
            }

            for (var modifierIndex = 0; modifierIndex < 8; modifierIndex++)
            {
                var modifierPath = "$.layers[0].modifiers[" + modifierIndex + "]";
                AssertRejected(
                    VlatSerializer.Deserialize(AddProperty(full, modifierPath, "unexpected", true)),
                    "extra modifier property " + modifierIndex,
                    true);
            }

            AssertRejected(
                VlatSerializer.Deserialize(AddProperty(full, "$.layers[0].modifiers[0].indices", "unexpected", true)),
                "extra accessor property",
                true);
            AssertRejected(
                VlatSerializer.Deserialize(SetProperty(full, "$.layers[0].modifiers[0].normals", JValue.CreateNull())),
                "explicit null optional normals",
                true);
        }

        /// <summary>Accessorの形状・型・componentType不正と未知Modifier typeを拒否することを検証します。</summary>
        [Test]
        public void Deserialize_RejectsInvalidAccessorsAndUnknownModifierType()
        {
            var full = CreateFullJson();
            var cases = new[]
            {
                new MutationCase("scalar accessor", "$.layers[0].modifiers[0].indices", new JValue(0)),
                new MutationCase("string accessor index", "$.layers[0].modifiers[0].indices.bufferView", new JValue("0")),
                new MutationCase("numeric component type", "$.layers[0].modifiers[0].indices.componentType", new JValue(1)),
                new MutationCase("wrong index component", "$.layers[0].modifiers[0].indices.componentType", new JValue("FLOAT32")),
                new MutationCase("wrong position component", "$.layers[0].modifiers[0].positions.componentType", new JValue("UINT32")),
                new MutationCase("negative accessor index", "$.layers[0].modifiers[0].indices.bufferView", new JValue(-1)),
                new MutationCase("fractional modifier int", "$.layers[0].modifiers[1].subMesh", new JValue(0.5d)),
                new MutationCase("modifier int overflow", "$.layers[0].modifiers[2].meshBinding", new JValue(2147483648L)),
                new MutationCase("unknown modifier", "$.layers[0].modifiers[0].type", new JValue("unknownModifier")),
            };

            for (var index = 0; index < cases.Length; index++)
            {
                var item = cases[index];
                AssertRejected(
                    VlatSerializer.Deserialize(SetProperty(full, item.Path, item.Value)),
                    item.Name,
                    true);
            }
        }

        /// <summary>materialPropertyのscalar/vector/textureIndex値の型・要素・参照不正を拒否することを検証します。</summary>
        [Test]
        public void Deserialize_RejectsInvalidMaterialBandValues()
        {
            var full = CreateFullJson();
            var cases = new[]
            {
                new MutationCase("float vector", "$.layers[0].modifiers[3].bandValue", new JArray(1d, 2d, 3d, 4d)),
                new MutationCase("color scalar", "$.layers[0].modifiers[4].bandValue", new JValue(1d)),
                new MutationCase("short color", "$.layers[0].modifiers[4].bandValue", new JArray(1d, 1d, 1d)),
                new MutationCase("vector scalar", "$.layers[0].modifiers[5].bandValue", new JValue(1d)),
                new MutationCase("bandValue string", "$.layers[0].modifiers[3].bandValue", new JValue("bad")),
                new MutationCase("vector string component", "$.layers[0].modifiers[5].bandValue", new JArray(1d, 1d, "bad", 1d)),
                new MutationCase("texture vector", "$.layers[0].modifiers[6].bandValue", new JArray(1d)),
                new MutationCase("texture fraction", "$.layers[0].modifiers[6].bandValue", new JValue(0.5d)),
                new MutationCase("texture negative", "$.layers[0].modifiers[6].bandValue", new JValue(-1)),
                new MutationCase("texture out of range", "$.layers[0].modifiers[6].bandValue", new JValue(2)),
                new MutationCase("texture interpolation", "$.layers[0].modifiers[6].interpolate", new JValue(true)),
            };

            for (var index = 0; index < cases.Length; index++)
            {
                var item = cases[index];
                AssertRejected(
                    VlatSerializer.Deserialize(SetProperty(full, item.Path, item.Value)),
                    item.Name,
                    true);
            }
        }

        /// <summary>Serializeがnullを拒否し、全Modifier派生型を正しいtype判別付きで書き込むことを検証します。</summary>
        [Test]
        public void Serialize_RejectsNullAndWritesEveryModifierSubtype()
        {
            Assert.Throws<ArgumentNullException>(() => VlatSerializer.Serialize(null));

            var json = VlatSerializer.Serialize(CreateFullDocument(), true);
            StringAssert.Contains("\"type\": \"vertexDelta\"", json);
            StringAssert.Contains("\"type\": \"visibility\"", json);
            StringAssert.Contains("\"type\": \"opacity\"", json);
            StringAssert.Contains("\"type\": \"materialProperty\"", json);
            StringAssert.Contains("\"type\": \"renderQueue\"", json);

            var result = VlatSerializer.Deserialize(json);
            Assert.IsTrue(result.Success, DumpDiagnostics(result.Diagnostics));
            var modifiers = result.Document.Layers[0].Modifiers;
            Assert.AreEqual(8, modifiers.Count);
            Assert.IsInstanceOf<VertexDeltaModifier>(modifiers[0]);
            Assert.IsInstanceOf<VisibilityModifier>(modifiers[1]);
            Assert.IsInstanceOf<OpacityModifier>(modifiers[2]);
            Assert.IsInstanceOf<MaterialPropertyModifier>(modifiers[3]);
            Assert.IsInstanceOf<MaterialPropertyModifier>(modifiers[4]);
            Assert.IsInstanceOf<MaterialPropertyModifier>(modifiers[5]);
            Assert.IsInstanceOf<MaterialPropertyModifier>(modifiers[6]);
            Assert.IsInstanceOf<RenderQueueModifier>(modifiers[7]);

            Assert.Throws<JsonSerializationException>(
                () => VlatSerializer.Serialize(CreateDocumentWithModifier(new UnsupportedModifier())));
        }

        /// <summary>converterのnull入力とスカラー・ベクトル単独往復を検証します。</summary>
        [Test]
        public void MaterialValueConverter_RoundTripsNullScalarAndVector()
        {
            Assert.IsNull(JsonConvert.DeserializeObject<VlatMaterialValue>("null"));

            var scalarJson = JsonConvert.SerializeObject(new VlatMaterialValue(0.25d));
            var scalar = JsonConvert.DeserializeObject<VlatMaterialValue>(scalarJson);
            Assert.IsTrue(scalar.IsScalar);
            Assert.AreEqual(0.25d, scalar.Scalar);

            var vectorJson = JsonConvert.SerializeObject(new VlatMaterialValue(new[] { 1d, 2d, 3d, 4d }));
            var vector = JsonConvert.DeserializeObject<VlatMaterialValue>(vectorJson);
            Assert.IsFalse(vector.IsScalar);
            Assert.AreEqual(4, vector.ComponentCount);
            Assert.AreEqual(3d, vector.GetComponent(2));

            Assert.Throws<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<VlatMaterialValue>("[1,\"bad\",3,4]"));
        }

        /// <summary>Load/Saveの不正引数と不正buffer URIが診断結果へ変換されることを検証します。</summary>
        [Test]
        public void LoadSave_InvalidArgumentsReturnDiagnosticsWithoutThrowing()
        {
            Assert.IsFalse(VlatSerializer.Load(null).Success);
            Assert.IsFalse(VlatSerializer.Load(string.Empty).Success);
            Assert.IsFalse(VlatSerializer.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.vlat.json")).Success);

            var document = CreateFullDocument();
            Assert.IsFalse(VlatSerializer.Save(string.Empty, document, new[] { new byte[32] }).Success);
            Assert.IsFalse(VlatSerializer.Save("unused.vlat.json", null, null).Success);
            Assert.IsFalse(VlatSerializer.Save("unused.vlat.json", document, null).Success);
            Assert.IsFalse(VlatSerializer.Save("unused.vlat.json", document, Array.Empty<byte[]>()).Success);

            var absoluteUriDocument = CreateDocumentWithBufferUri(Path.GetFullPath("absolute.vlat.bin"));
            var temporaryPath = Path.Combine(Path.GetTempPath(), "VlatStrict-" + Guid.NewGuid().ToString("N"), "document.vlat.json");
            Assert.IsFalse(VlatSerializer.Save(temporaryPath, absoluteUriDocument, new[] { new byte[32] }).Success);

            var duplicateUriDocument = CreateDocumentWithDuplicateBufferUri();
            Assert.IsFalse(VlatSerializer.Save(
                temporaryPath,
                duplicateUriDocument,
                new[] { new byte[32], new byte[32] }).Success);
        }

        private static void AssertRejected(VlatLoadResult result, string name, bool requireLocation)
        {
            Assert.IsFalse(result.Success, name + " was accepted.");
            var foundError = false;
            var foundLocatedError = false;
            for (var index = 0; index < result.Diagnostics.Count; index++)
            {
                var diagnostic = result.Diagnostics[index];
                if (diagnostic.Severity != VlatDiagnosticSeverity.Error)
                {
                    continue;
                }

                foundError = true;
                if (diagnostic.Line > 0 && diagnostic.Column >= 0)
                {
                    foundLocatedError = true;
                }
            }

            Assert.IsTrue(foundError, name + " did not return an error diagnostic.");
            if (requireLocation)
            {
                Assert.IsTrue(foundLocatedError, name + " did not return a located error: " + DumpDiagnostics(result.Diagnostics));
            }
        }

        private static string CreateMinimalJson()
        {
            return
                "{\n" +
                "  \"vlat\": {\n" +
                "    \"version\": \"0.1.0\",\n" +
                "    \"generator\": \"VLat Strict Tests\",\n" +
                "    \"targetAvatar\": {\n" +
                "      \"name\": \"MinimalAvatar\",\n" +
                "      \"vrmVersion\": \"1.0\",\n" +
                "      \"meshBindings\": []\n" +
                "    }\n" +
                "  },\n" +
                "  \"angleBands\": [],\n" +
                "  \"layers\": []\n" +
                "}";
        }

        private static string CreateFullJson()
        {
            return VlatSerializer.Serialize(CreateFullDocument(), true);
        }

        private static VlatDocument CreateFullDocument()
        {
            var materials = new[]
            {
                new VlatMaterialSlot(0, "Hair", "VRM10/MToon10"),
                new VlatMaterialSlot(1, "Highlight", "VRM10/MToon10"),
            };
            var mesh = new MeshBinding(
                "Face/Hair",
                "Hair",
                4,
                "xxh64:0123456789abcdef",
                materials);
            var metadata = new VlatMetadata(
                "0.1.0",
                "VLat Strict Tests",
                new TargetAvatar("StrictAvatar", "1.0", new[] { mesh }));
            var buffers = new[] { new VlatBuffer("strict.vlat.bin", 32) };
            var views = new[]
            {
                new VlatBufferView(0, 0, 4),
                new VlatBufferView(0, 4, 12),
                new VlatBufferView(0, 16, 12),
                new VlatBufferView(0, 28, 4),
            };
            var textures = new[]
            {
                new VlatTexture("External", "external.png", null, null),
                new VlatTexture("Embedded", null, 3, "image/png"),
            };
            var band = new AngleBand(
                "band",
                "Band",
                new AngleRange(-20d, 20d),
                new AngleRange(-15d, 15d),
                new AngleFeather(5d, 5d),
                1d);
            var modifiers = new VlatModifier[]
            {
                new VertexDeltaModifier(
                    0,
                    new VlatAccessor(0, "UINT32"),
                    new VlatAccessor(1, "FLOAT32"),
                    new VlatAccessor(2, "FLOAT32")),
                new VisibilityModifier(0, 0, false),
                new OpacityModifier(0, 0, 0.5d),
                new MaterialPropertyModifier(0, 0, "_Cutoff", "float", new VlatMaterialValue(0.4d), true),
                new MaterialPropertyModifier(
                    0,
                    0,
                    "_Color",
                    "color",
                    new VlatMaterialValue(new[] { 1d, 0.5d, 0.25d, 1d }),
                    true),
                new MaterialPropertyModifier(
                    0,
                    0,
                    "_Vector",
                    "vector",
                    new VlatMaterialValue(new[] { 1d, 2d, 3d, 4d }),
                    false),
                new MaterialPropertyModifier(0, 1, "_MainTex", "textureIndex", new VlatMaterialValue(1d), false),
                new RenderQueueModifier(0, 0, 10),
            };
            var layer = new VlatLayer("layer", "Layer", "bangs", true, 0, "band", modifiers);
            return new VlatDocument(metadata, buffers, views, textures, new[] { band }, new[] { layer });
        }

        private static VlatDocument CreateDocumentWithModifier(VlatModifier modifier)
        {
            var source = CreateFullDocument();
            var layer = new VlatLayer("layer", "Layer", "other", true, 0, "band", new[] { modifier });
            return new VlatDocument(
                source.Metadata,
                source.Buffers,
                source.BufferViews,
                source.Textures,
                source.AngleBands,
                new[] { layer });
        }

        private static VlatDocument CreateDocumentWithBufferUri(string uri)
        {
            var source = CreateFullDocument();
            return new VlatDocument(
                source.Metadata,
                new[] { new VlatBuffer(uri, 32) },
                source.BufferViews,
                source.Textures,
                source.AngleBands,
                source.Layers);
        }

        private static VlatDocument CreateDocumentWithDuplicateBufferUri()
        {
            var source = CreateFullDocument();
            return new VlatDocument(
                source.Metadata,
                new[]
                {
                    new VlatBuffer("duplicate.vlat.bin", 32),
                    new VlatBuffer("duplicate.vlat.bin", 32),
                },
                source.BufferViews,
                source.Textures,
                source.AngleBands,
                source.Layers);
        }

        private static string RemoveProperty(string json, string path)
        {
            var root = JObject.Parse(json);
            var property = RequireToken(root, path).Parent as JProperty;
            Assert.IsNotNull(property, "Path is not a property: " + path);
            property.Remove();
            return root.ToString(Formatting.Indented);
        }

        private static string SetProperty(string json, string path, JToken replacement)
        {
            var root = JObject.Parse(json);
            RequireToken(root, path).Replace(replacement.DeepClone());
            return root.ToString(Formatting.Indented);
        }

        private static string AddProperty(string json, string objectPath, string name, object value)
        {
            var root = JObject.Parse(json);
            var target = RequireToken(root, objectPath) as JObject;
            Assert.IsNotNull(target, "Path is not an object: " + objectPath);
            target.Add(name, JToken.FromObject(value));
            return root.ToString(Formatting.Indented);
        }

        private static JToken RequireToken(JObject root, string path)
        {
            var token = root.SelectToken(path, false);
            Assert.IsNotNull(token, "Missing test path: " + path);
            return token;
        }

        private static string DumpDiagnostics(IReadOnlyList<VlatDiagnostic> diagnostics)
        {
            var message = string.Empty;
            for (var index = 0; index < diagnostics.Count; index++)
            {
                var diagnostic = diagnostics[index];
                if (index > 0)
                {
                    message += " | ";
                }

                message += diagnostic.Path + " (" + diagnostic.Line + "," + diagnostic.Column + "): " + diagnostic.Message;
            }

            return message;
        }

        private sealed class UnsupportedModifier : VlatModifier
        {
            public UnsupportedModifier()
                : base("unsupported")
            {
            }
        }

        private readonly struct JsonCase
        {
            public JsonCase(string name, string json)
            {
                Name = name;
                Json = json;
            }

            public string Name { get; }

            public string Json { get; }
        }

        private readonly struct MutationCase
        {
            public MutationCase(string name, string path, JToken value)
            {
                Name = name;
                Path = path;
                Value = value;
            }

            public string Name { get; }

            public string Path { get; }

            public JToken Value { get; }
        }
    }
}
