using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using VLat.Core.Model;
using VLat.Core.Serialization;
using VLat.Core.Validation;

namespace VLat.Tests.Edit
{
    /// <summary>VLat JSONとバイナリのシリアライズ往復を検証します。</summary>
    public sealed class VlatSerializationTests
    {
        /// <summary>最小ドキュメントがDeserialize、Serialize、Deserializeで意味を保持することを検証します。</summary>
        [Test]
        public void DeserializeSerializeDeserialize_ValidMinimalPreservesMeaning()
        {
            var result = AssertSemanticRoundTrip(CreateMinimalJson());

            Assert.That(result.Document.Metadata.Version, Is.EqualTo("0.1.0"));
            Assert.That(result.Document.Metadata.TargetAvatar.MeshBindings.Count, Is.Zero);
            Assert.That(result.Document.AngleBands.Count, Is.Zero);
            Assert.That(result.Document.Layers.Count, Is.Zero);
        }

        /// <summary>全Modifier派生型を含むドキュメントが往復し、派生型を保持することを検証します。</summary>
        [Test]
        public void DeserializeSerializeDeserialize_ValidAllModifiersPreservesMeaningAndTypes()
        {
            var json = VlatSerializer.Serialize(CreateAllModifiersDocument(), true);
            var result = AssertSemanticRoundTrip(json);
            var modifiers = result.Document.Layers[0].Modifiers;

            Assert.That(modifiers.Count, Is.EqualTo(5));
            Assert.That(modifiers[0], Is.TypeOf<VertexDeltaModifier>());
            Assert.That(modifiers[1], Is.TypeOf<VisibilityModifier>());
            Assert.That(modifiers[2], Is.TypeOf<OpacityModifier>());
            Assert.That(modifiers[3], Is.TypeOf<MaterialPropertyModifier>());
            Assert.That(modifiers[4], Is.TypeOf<RenderQueueModifier>());
        }

        /// <summary>未定義プロパティを行番号付きエラーとして拒否することを検証します。</summary>
        [Test]
        public void Deserialize_UnknownPropertyReturnsLocatedError()
        {
            var json = CreateJsonWithModifier(
                "        {\n" +
                "          \"type\": \"opacity\",\n" +
                "          \"meshBinding\": 0,\n" +
                "          \"materialSlot\": 0,\n" +
                "          \"value\": 0.5,\n" +
                "          \"unexpected\": true\n" +
                "        }");

            var result = VlatSerializer.Deserialize(json);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Document, Is.Null);
            AssertLocatedError(result.Diagnostics, "unexpected");
        }

        /// <summary>重複プロパティを行番号付きエラーとして拒否することを検証します。</summary>
        [Test]
        public void Deserialize_DuplicatePropertyReturnsLocatedError()
        {
            var json = CreateMinimalJson().Replace(
                "    \"version\": \"0.1.0\",",
                "    \"version\": \"0.1.0\",\n    \"version\": \"0.1.0\",");

            var result = VlatSerializer.Deserialize(json);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Document, Is.Null);
            AssertLocatedError(result.Diagnostics, "version");
        }

        /// <summary>未知のModifier typeを行番号付きエラーとして拒否することを検証します。</summary>
        [Test]
        public void Deserialize_UnknownModifierTypeReturnsLocatedError()
        {
            var json = CreateJsonWithModifier(
                "        {\n" +
                "          \"type\": \"unknownModifier\"\n" +
                "        }");

            var result = VlatSerializer.Deserialize(json);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Document, Is.Null);
            AssertLocatedError(result.Diagnostics, "type");
        }

        /// <summary>ModifierプロパティのJSON型不正を行番号付きエラーとして拒否することを検証します。</summary>
        [Test]
        public void Deserialize_WrongPropertyTypeReturnsLocatedError()
        {
            var json = CreateJsonWithModifier(
                "        {\n" +
                "          \"type\": \"opacity\",\n" +
                "          \"meshBinding\": \"zero\",\n" +
                "          \"materialSlot\": 0,\n" +
                "          \"value\": 0.5\n" +
                "        }");

            var result = VlatSerializer.Deserialize(json);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Document, Is.Null);
            AssertLocatedError(result.Diagnostics, "meshBinding");
        }

        /// <summary>SaveとLoadでJSONとbinが往復し、入力byte配列を変更しないことを検証します。</summary>
        [Test]
        public void SaveLoad_RoundTripsJsonAndBinaryWithoutMutatingInput()
        {
            var temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "VLatSerializationTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);

            try
            {
                byte[] binary;
                var document = CreateBinaryDocument(out binary);
                var originalBytes = (byte[])binary.Clone();
                var jsonPath = Path.Combine(temporaryDirectory, "roundtrip.vlat.json");

                var saveResult = VlatSerializer.Save(jsonPath, document, new[] { binary });

                Assert.That(saveResult.Success, Is.True);
                CollectionAssert.AreEqual(originalBytes, binary);
                Assert.That(File.Exists(jsonPath), Is.True);
                Assert.That(File.Exists(Path.Combine(temporaryDirectory, "roundtrip.vlat.bin")), Is.True);

                var loadResult = VlatSerializer.Load(jsonPath);

                Assert.That(loadResult.Success, Is.True);
                Assert.That(loadResult.Buffers.Count, Is.EqualTo(1));
                CollectionAssert.AreEqual(originalBytes, loadResult.Buffers[0]);
                Assert.That(
                    VlatSerializer.Serialize(loadResult.Document, false),
                    Is.EqualTo(VlatSerializer.Serialize(document, false)));

                loadResult.Buffers[0][0] ^= 0xFF;
                CollectionAssert.AreEqual(originalBytes, binary);
            }
            finally
            {
                if (Directory.Exists(temporaryDirectory))
                {
                    Directory.Delete(temporaryDirectory, true);
                }
            }
        }

        private static VlatLoadResult AssertSemanticRoundTrip(string json)
        {
            var first = VlatSerializer.Deserialize(json);
            Assert.That(first.Success, Is.True);
            Assert.That(first.Document, Is.Not.Null);

            var canonicalJson = VlatSerializer.Serialize(first.Document, false);
            var second = VlatSerializer.Deserialize(canonicalJson);
            Assert.That(second.Success, Is.True);
            Assert.That(second.Document, Is.Not.Null);
            Assert.That(VlatSerializer.Serialize(second.Document, false), Is.EqualTo(canonicalJson));
            return second;
        }

        private static void AssertLocatedError(
            IReadOnlyList<VlatDiagnostic> diagnostics,
            string expectedText)
        {
            for (var index = 0; index < diagnostics.Count; index++)
            {
                var diagnostic = diagnostics[index];
                if (diagnostic.Severity == VlatDiagnosticSeverity.Error &&
                    diagnostic.Line > 0 &&
                    diagnostic.Column > 0 &&
                    ((diagnostic.Path != null && diagnostic.Path.IndexOf(expectedText, StringComparison.Ordinal) >= 0) ||
                     (diagnostic.Message != null && diagnostic.Message.IndexOf(expectedText, StringComparison.Ordinal) >= 0)))
                {
                    return;
                }
            }

            Assert.Fail("'{0}' に対する行番号付きエラーがありません。", expectedText);
        }

        private static string CreateMinimalJson()
        {
            return
                "{\n" +
                "  \"vlat\": {\n" +
                "    \"version\": \"0.1.0\",\n" +
                "    \"generator\": \"VLat Tests\",\n" +
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

        private static string CreateJsonWithModifier(string modifierJson)
        {
            return
                "{\n" +
                "  \"vlat\": {\n" +
                "    \"version\": \"0.1.0\",\n" +
                "    \"generator\": \"VLat Tests\",\n" +
                "    \"targetAvatar\": {\n" +
                "      \"name\": \"Avatar\",\n" +
                "      \"vrmVersion\": \"1.0\",\n" +
                "      \"meshBindings\": [\n" +
                "        {\n" +
                "          \"hierarchyPath\": \"Face\",\n" +
                "          \"meshName\": \"Face\",\n" +
                "          \"vertexCount\": 3,\n" +
                "          \"positionHash\": \"xxh64:0000000000000000\",\n" +
                "          \"materials\": [\n" +
                "            { \"slot\": 0, \"materialName\": \"Face\", \"shader\": \"VRM10/MToon10\" }\n" +
                "          ]\n" +
                "        }\n" +
                "      ]\n" +
                "    }\n" +
                "  },\n" +
                "  \"angleBands\": [\n" +
                "    {\n" +
                "      \"id\": \"band\",\n" +
                "      \"name\": \"Band\",\n" +
                "      \"yaw\": { \"min\": -10, \"max\": 10 },\n" +
                "      \"pitch\": { \"min\": -10, \"max\": 10 },\n" +
                "      \"feather\": { \"yaw\": 5, \"pitch\": 5 }\n" +
                "    }\n" +
                "  ],\n" +
                "  \"layers\": [\n" +
                "    {\n" +
                "      \"id\": \"layer\",\n" +
                "      \"name\": \"Layer\",\n" +
                "      \"tag\": \"other\",\n" +
                "      \"enabled\": true,\n" +
                "      \"priority\": 0,\n" +
                "      \"bandId\": \"band\",\n" +
                "      \"modifiers\": [\n" +
                modifierJson + "\n" +
                "      ]\n" +
                "    }\n" +
                "  ]\n" +
                "}";
        }

        private static VlatDocument CreateAllModifiersDocument()
        {
            var material = new VlatMaterialSlot(0, "Hair", "VRM10/MToon10");
            var mesh = new MeshBinding(
                "Face/Hair",
                "Hair",
                2,
                "xxh64:0123456789abcdef",
                new[] { material });
            var avatar = new TargetAvatar("AllModifiersAvatar", "1.0", new[] { mesh });
            var metadata = new VlatMetadata("0.1.0", "VLat Tests", avatar);
            var band = new AngleBand(
                "band",
                "Band",
                new AngleRange(170d, -170d),
                new AngleRange(-30d, 30d),
                new AngleFeather(10d, 5d),
                2d);
            var modifiers = new VlatModifier[]
            {
                new VertexDeltaModifier(
                    0,
                    new VlatAccessor(0, "UINT32"),
                    new VlatAccessor(1, "FLOAT32"),
                    new VlatAccessor(2, "FLOAT32")),
                new VisibilityModifier(0, 0, false),
                new OpacityModifier(0, 0, 0.35d),
                new MaterialPropertyModifier(
                    0,
                    0,
                    "_MainTex",
                    "textureIndex",
                    new VlatMaterialValue(0d),
                    false),
                new RenderQueueModifier(0, 0, 10),
            };
            var layer = new VlatLayer("layer", "Layer", "bangs", true, 3, "band", modifiers);

            return new VlatDocument(
                metadata,
                new[] { new VlatBuffer("all-modifiers.vlat.bin", 28) },
                new[]
                {
                    new VlatBufferView(0, 0, 4),
                    new VlatBufferView(0, 4, 12),
                    new VlatBufferView(0, 16, 12),
                },
                new[] { new VlatTexture("Highlight", "highlight.png", null, null) },
                new[] { band },
                new[] { layer });
        }

        private static VlatDocument CreateBinaryDocument(out byte[] binary)
        {
            var material = new VlatMaterialSlot(0, "Face", "VRM10/MToon10");
            var mesh = new MeshBinding(
                "Face",
                "Face",
                3,
                "xxh64:0000000000000000",
                new[] { material });
            var metadata = new VlatMetadata(
                "0.1.0",
                "VLat Tests",
                new TargetAvatar("Avatar", "1.0", new[] { mesh }));
            var band = new AngleBand(
                "band",
                "Band",
                new AngleRange(-20d, 20d),
                new AngleRange(-20d, 20d),
                new AngleFeather(5d, 5d));
            var modifier = new VertexDeltaModifier(
                0,
                new VlatAccessor(0, "UINT32"),
                new VlatAccessor(1, "FLOAT32"),
                new VlatAccessor(2, "FLOAT32"));
            var layer = new VlatLayer(
                "layer",
                "Layer",
                "faceLine",
                true,
                0,
                "band",
                new VlatModifier[] { modifier });

            binary = new byte[28];
            binary[0] = 2;
            return new VlatDocument(
                metadata,
                new[] { new VlatBuffer("roundtrip.vlat.bin", binary.Length) },
                new[]
                {
                    new VlatBufferView(0, 0, 4),
                    new VlatBufferView(0, 4, 12),
                    new VlatBufferView(0, 16, 12),
                },
                Array.Empty<VlatTexture>(),
                new[] { band },
                new[] { layer });
        }
    }
}
