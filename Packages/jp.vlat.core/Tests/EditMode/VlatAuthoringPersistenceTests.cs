using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VLat.Core.Model;
using VLat.Core.Serialization;
using VLat.Core.Validation;
using VLat.Editor.Authoring;

namespace VLat.Tests.Edit
{
    /// <summary>Phase 2A Authoring Model、保存境界、およびUndo基盤を検証します。</summary>
    public sealed class VlatAuthoringPersistenceTests
    {
        private readonly List<VlatAuthoringDocument> _documents = new List<VlatAuthoringDocument>();

        /// <summary>各テスト前にUnity Undo履歴を初期化します。</summary>
        [SetUp]
        public void SetUp()
        {
            Undo.ClearAll();
        }

        /// <summary>一時Authoring ModelとUndo履歴を破棄します。</summary>
        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
            for (var index = 0; index < _documents.Count; index++)
            {
                if (_documents[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_documents[index]);
                }
            }

            _documents.Clear();
        }

        /// <summary>複数bufferを持つimmutable documentをtyped Authoring Modelへ変換できることを検証します。</summary>
        [Test]
        public void FromImmutable_DecodesMultipleBuffersAndTypedVertexDelta()
        {
            IReadOnlyList<byte[]> buffers;
            var immutable = CreateMultipleBufferDocument(out buffers);

            var result = VlatAuthoringConversion.FromImmutable(immutable, buffers, "source.vlat.json");
            Track(result.Document);

            Assert.That(result.Success, Is.True, JoinDiagnostics(result.Diagnostics));
            Assert.That(result.Document.SourcePath, Is.EqualTo("source.vlat.json"));
            Assert.That(result.Document.Metadata.MeshBindings.Count, Is.EqualTo(1));
            Assert.That(result.Document.Bands.Count, Is.EqualTo(2));
            Assert.That(result.Document.Layers.Count, Is.EqualTo(2));
            Assert.That(result.Document.Textures[0].IsEmbedded, Is.True);
            CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4e }, result.Document.Textures[0].EmbeddedBytes);

            var vertex = result.Document.Layers[0].Modifiers[0] as VlatAuthoringVertexDeltaModifier;
            Assert.That(vertex, Is.Not.Null);
            Assert.That(vertex.HasNormals, Is.True);
            Assert.That(vertex.Deltas.Count, Is.EqualTo(2));
            Assert.That(vertex.Deltas[0].VertexIndex, Is.EqualTo(5));
            Assert.That(vertex.Deltas[1].VertexIndex, Is.EqualTo(1));
            Assert.That(vertex.Deltas[0].PositionDelta, Is.EqualTo(new Vector3(0.5f, 0.25f, -0.5f)));
            Assert.That(vertex.Deltas[1].NormalDelta, Is.EqualTo(new Vector3(0f, 0.2f, 0f)));
        }

        /// <summary>新規Authoring Modelが保存可能な最小immutable documentへ変換できることを検証します。</summary>
        [Test]
        public void CreateNew_ProducesValidCleanMinimalAuthoringDocument()
        {
            var document = VlatAuthoringPersistence.CreateNew("New Avatar");
            Track(document);

            var pack = VlatAuthoringPersistence.Pack(document);

            Assert.That(document.IsDirty, Is.False);
            Assert.That(document.SourcePath, Is.Null);
            Assert.That(document.Metadata.AvatarName, Is.EqualTo("New Avatar"));
            Assert.That(pack.Success, Is.True, JoinDiagnostics(pack.Diagnostics));
            Assert.That(pack.Document.Buffers.Count, Is.Zero);
            Assert.That(pack.Document.Layers.Count, Is.Zero);
        }

        /// <summary>Authoring Modelを全modifier型を保ったimmutable documentへ変換できることを検証します。</summary>
        [Test]
        public void Pack_ConvertsAuthoringToImmutableWithMultipleModifiersAndLayers()
        {
            var authoring = CreateValidAuthoring();

            var result = VlatAuthoringPersistence.Pack(authoring, "packed.vlat.bin");

            Assert.That(result.Success, Is.True, JoinDiagnostics(result.Diagnostics));
            Assert.That(result.Document.Layers.Count, Is.EqualTo(2));
            Assert.That(result.Document.AngleBands.Count, Is.EqualTo(2));
            Assert.That(result.Document.Layers[0].Modifiers.Count, Is.EqualTo(5));
            Assert.That(result.Document.Layers[0].Modifiers[0], Is.TypeOf<VertexDeltaModifier>());
            Assert.That(result.Document.Layers[0].Modifiers[1], Is.TypeOf<VisibilityModifier>());
            Assert.That(result.Document.Layers[0].Modifiers[2], Is.TypeOf<OpacityModifier>());
            Assert.That(result.Document.Layers[0].Modifiers[3], Is.TypeOf<MaterialPropertyModifier>());
            Assert.That(result.Document.Layers[0].Modifiers[4], Is.TypeOf<RenderQueueModifier>());
            Assert.That(result.Document.Buffers.Count, Is.EqualTo(1));
            Assert.That(result.Document.Buffers[0].Uri, Is.EqualTo("packed.vlat.bin"));
        }

        /// <summary>既存fixtureをload、authoring、save、reloadしてsemantic equalityを保つことを検証します。</summary>
        [Test]
        public void ExistingFixture_LoadAuthoringSaveReload_PreservesCanonicalSemantics()
        {
            var fixturePath = Path.GetFullPath(
                "Packages/jp.vlat.core/Tests/EditMode/Fixtures/Valid/valid-minimal.vlat.json");
            var load = VlatAuthoringPersistence.Load(fixturePath);
            Track(load.Document);
            Assert.That(load.Success, Is.True, JoinDiagnostics(load.Diagnostics));

            var directory = CreateTemporaryDirectory();
            try
            {
                var outputPath = Path.Combine(directory, "fixture.vlat.json");
                var save = VlatAuthoringPersistence.Save(outputPath, load.Document);
                Assert.That(save.Success, Is.True, JoinDiagnostics(save.Diagnostics));

                var reload = VlatAuthoringPersistence.Load(outputPath);
                Track(reload.Document);
                Assert.That(reload.Success, Is.True, JoinDiagnostics(reload.Diagnostics));

                var first = VlatAuthoringPersistence.Pack(load.Document, "canonical.vlat.bin");
                var second = VlatAuthoringPersistence.Pack(reload.Document, "canonical.vlat.bin");
                Assert.That(first.Json, Is.EqualTo(second.Json));
                Assert.That(first.Buffers.Count, Is.EqualTo(second.Buffers.Count));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        /// <summary>vertexDeltaと複数bufferを含む入力がcanonical save/reload後も意味を保つことを検証します。</summary>
        [Test]
        public void ComplexLoadSaveReload_PreservesTypedSemanticsAcrossCanonicalRepack()
        {
            IReadOnlyList<byte[]> sourceBuffers;
            var source = CreateMultipleBufferDocument(out sourceBuffers);
            var directory = CreateTemporaryDirectory();
            try
            {
                var inputPath = Path.Combine(directory, "input.vlat.json");
                var outputPath = Path.Combine(directory, "output.vlat.json");
                var initialSave = VlatSerializer.Save(inputPath, source, sourceBuffers);
                Assert.That(initialSave.Success, Is.True, JoinDiagnostics(initialSave.Diagnostics));

                var load = VlatAuthoringPersistence.Load(inputPath);
                Track(load.Document);
                Assert.That(load.Success, Is.True, JoinDiagnostics(load.Diagnostics));
                var save = VlatAuthoringPersistence.Save(outputPath, load.Document);
                Assert.That(save.Success, Is.True, JoinDiagnostics(save.Diagnostics));

                var reload = VlatAuthoringPersistence.Load(outputPath);
                Track(reload.Document);
                Assert.That(reload.Success, Is.True, JoinDiagnostics(reload.Diagnostics));
                var firstCanonical = VlatAuthoringPersistence.Pack(load.Document, "semantic.vlat.bin");
                var secondCanonical = VlatAuthoringPersistence.Pack(reload.Document, "semantic.vlat.bin");
                Assert.That(firstCanonical.Json, Is.EqualTo(secondCanonical.Json));
                CollectionAssert.AreEqual(firstCanonical.Buffers[0], secondCanonical.Buffers[0]);
                Assert.That(source.Buffers.Count, Is.EqualTo(2));
                Assert.That(secondCanonical.Document.Buffers.Count, Is.EqualTo(1));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        /// <summary>同じAuthoring Modelの反復packがbyte-identicalなJSONとbinaryを生成することを検証します。</summary>
        [Test]
        public void Pack_RepeatedCallsAreDeterministicAndDoNotReorderAuthoringData()
        {
            var authoring = CreateValidAuthoring();
            var vertex = (VlatAuthoringVertexDeltaModifier)authoring.Layers[0].Modifiers[0];
            Assert.That(vertex.Deltas[0].VertexIndex, Is.EqualTo(5));

            var first = VlatAuthoringPersistence.Pack(authoring, "deterministic.vlat.bin");
            var second = VlatAuthoringPersistence.Pack(authoring, "deterministic.vlat.bin");

            Assert.That(first.Success, Is.True, JoinDiagnostics(first.Diagnostics));
            Assert.That(second.Success, Is.True, JoinDiagnostics(second.Diagnostics));
            Assert.That(first.Json, Is.EqualTo(second.Json));
            CollectionAssert.AreEqual(first.Buffers[0], second.Buffers[0]);
            Assert.That(vertex.Deltas[0].VertexIndex, Is.EqualTo(5));
            Assert.That(vertex.Deltas[1].VertexIndex, Is.EqualTo(1));

            var directory = CreateTemporaryDirectory();
            try
            {
                var path = Path.Combine(directory, "deterministic.vlat.json");
                Assert.That(VlatAuthoringPersistence.Save(path, authoring).Success, Is.True);
                var firstJson = File.ReadAllBytes(path);
                var firstBinary = File.ReadAllBytes(Path.Combine(directory, "deterministic.vlat.bin"));
                Assert.That(VlatAuthoringPersistence.Save(path, authoring).Success, Is.True);
                CollectionAssert.AreEqual(firstJson, File.ReadAllBytes(path));
                CollectionAssert.AreEqual(
                    firstBinary,
                    File.ReadAllBytes(Path.Combine(directory, "deterministic.vlat.bin")));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        /// <summary>空vertexDeltaは編集中のwarningとして保持でき、Save境界では拒否されることを検証します。</summary>
        [Test]
        public void EmptyVertexDelta_IsEditingWarningAndSaveError()
        {
            var authoring = CreateValidAuthoring();
            ((VlatAuthoringVertexDeltaModifier)authoring.Layers[0].Modifiers[0]).Deltas.Clear();

            var editing = VlatAuthoringPersistence.Validate(authoring, VlatAuthoringValidationMode.Editing);
            var save = VlatAuthoringPersistence.Pack(authoring, "empty.vlat.bin");

            Assert.That(ContainsSeverity(editing, VlatDiagnosticSeverity.Error), Is.False);
            Assert.That(ContainsSeverity(editing, VlatDiagnosticSeverity.Warning), Is.True);
            Assert.That(save.Success, Is.False);
            Assert.That(ContainsPath(save.Diagnostics, ".deltas"), Is.True);
        }

        /// <summary>sparse vertexDeltaが頂点index昇順でcanonical packされることを検証します。</summary>
        [Test]
        public void SparseVertexDelta_IsPackedInAscendingVertexOrder()
        {
            var authoring = CreateValidAuthoring();

            var result = VlatAuthoringPersistence.Pack(authoring, "sparse.vlat.bin");

            Assert.That(result.Success, Is.True, JoinDiagnostics(result.Diagnostics));
            var modifier = (VertexDeltaModifier)result.Document.Layers[0].Modifiers[0];
            var view = result.Document.BufferViews[modifier.Indices.BufferView];
            var binary = result.Buffers[view.Buffer];
            Assert.That(ReadUInt32(binary, checked((int)view.ByteOffset)), Is.EqualTo(1u));
            Assert.That(ReadUInt32(binary, checked((int)view.ByteOffset) + 4), Is.EqualTo(5u));
        }

        /// <summary>埋め込みtexture後を含む全bufferViewが4-byte alignmentを満たすことを検証します。</summary>
        [Test]
        public void Pack_AssignsFourByteAlignedBufferViewOffsets()
        {
            var authoring = CreateValidAuthoring();

            var result = VlatAuthoringPersistence.Pack(authoring, "aligned.vlat.bin");

            Assert.That(result.Success, Is.True, JoinDiagnostics(result.Diagnostics));
            Assert.That(result.Document.BufferViews[0].ByteLength, Is.EqualTo(3));
            for (var index = 0; index < result.Document.BufferViews.Count; index++)
            {
                Assert.That(
                    result.Document.BufferViews[index].ByteOffset % 4,
                    Is.Zero,
                    "bufferView " + index);
            }

            Assert.That(result.Document.Buffers[0].ByteLength % 4, Is.Zero);
        }

        /// <summary>重複indexと非有限deltaを持つmalformed authoring stateをSaveで拒否することを検証します。</summary>
        [Test]
        public void Pack_RejectsMalformedAuthoringState()
        {
            var authoring = CreateValidAuthoring();
            var vertex = (VlatAuthoringVertexDeltaModifier)authoring.Layers[0].Modifiers[0];
            vertex.Deltas[1].VertexIndex = vertex.Deltas[0].VertexIndex;
            vertex.Deltas[1].PositionDelta = new Vector3(float.NaN, 0f, 0f);

            var result = VlatAuthoringPersistence.Pack(authoring, "malformed.vlat.bin");

            Assert.That(result.Success, Is.False);
            Assert.That(ContainsPath(result.Diagnostics, ".vertexIndex"), Is.True);
            Assert.That(ContainsPath(result.Diagnostics, ".positionDelta"), Is.True);
        }

        /// <summary>property editが1回のUndo/Redoで復元されることを検証します。</summary>
        [Test]
        public void Undo_PropertyEdit_RestoresAndReappliesValue()
        {
            var authoring = CreateValidAuthoring();

            VlatAuthoringCommands.SetLayerName(authoring, 0, "Edited Layer");
            Undo.FlushUndoRecordObjects();
            Assert.That(authoring.Layers[0].Name, Is.EqualTo("Edited Layer"));
            Assert.That(authoring.IsDirty, Is.True);

            Undo.PerformUndo();
            Assert.That(authoring.Layers[0].Name, Is.EqualTo("Layer A"));
            Assert.That(authoring.IsDirty, Is.False);

            Undo.PerformRedo();
            Assert.That(authoring.Layers[0].Name, Is.EqualTo("Edited Layer"));
            Assert.That(authoring.IsDirty, Is.True);
        }

        /// <summary>layer/band/modifierのadd/removeが各1回のUndoで復元されることを検証します。</summary>
        [Test]
        public void Undo_AddRemoveCollections_RestoresEachCommand()
        {
            var authoring = CreateValidAuthoring();
            var originalLayerCount = authoring.Layers.Count;
            VlatAuthoringCommands.AddLayer(authoring, CreateLayer("temporary", "band-a"));
            Undo.FlushUndoRecordObjects();
            Assert.That(authoring.Layers.Count, Is.EqualTo(originalLayerCount + 1));
            Undo.PerformUndo();
            Assert.That(authoring.Layers.Count, Is.EqualTo(originalLayerCount));
            Undo.PerformRedo();
            Assert.That(authoring.Layers.Count, Is.EqualTo(originalLayerCount + 1));

            VlatAuthoringCommands.RemoveLayer(authoring, authoring.Layers.Count - 1);
            Undo.FlushUndoRecordObjects();
            Assert.That(authoring.Layers.Count, Is.EqualTo(originalLayerCount));
            Undo.PerformUndo();
            Assert.That(authoring.Layers.Count, Is.EqualTo(originalLayerCount + 1));

            var originalBandCount = authoring.Bands.Count;
            VlatAuthoringCommands.AddBand(authoring, CreateBand("temporary-band"));
            Undo.FlushUndoRecordObjects();
            Assert.That(authoring.Bands.Count, Is.EqualTo(originalBandCount + 1));
            Undo.PerformUndo();
            Assert.That(authoring.Bands.Count, Is.EqualTo(originalBandCount));
            Undo.PerformRedo();
            Assert.That(authoring.Bands.Count, Is.EqualTo(originalBandCount + 1));
            VlatAuthoringCommands.RemoveBand(authoring, authoring.Bands.Count - 1);
            Undo.FlushUndoRecordObjects();
            Assert.That(authoring.Bands.Count, Is.EqualTo(originalBandCount));
            Undo.PerformUndo();
            Assert.That(authoring.Bands.Count, Is.EqualTo(originalBandCount + 1));

            var originalModifierCount = authoring.Layers[0].Modifiers.Count;
            VlatAuthoringCommands.AddModifier(
                authoring,
                0,
                new VlatAuthoringVisibilityModifier { MeshBinding = 0, SubMesh = 1, VisibleInBand = true });
            Undo.FlushUndoRecordObjects();
            Assert.That(authoring.Layers[0].Modifiers.Count, Is.EqualTo(originalModifierCount + 1));
            Undo.PerformUndo();
            Assert.That(authoring.Layers[0].Modifiers.Count, Is.EqualTo(originalModifierCount));
            Undo.PerformRedo();
            Assert.That(authoring.Layers[0].Modifiers.Count, Is.EqualTo(originalModifierCount + 1));
            VlatAuthoringCommands.RemoveModifier(
                authoring,
                0,
                authoring.Layers[0].Modifiers.Count - 1);
            Undo.FlushUndoRecordObjects();
            Assert.That(authoring.Layers[0].Modifiers.Count, Is.EqualTo(originalModifierCount));
            Undo.PerformUndo();
            Assert.That(authoring.Layers[0].Modifiers.Count, Is.EqualTo(originalModifierCount + 1));
        }

        /// <summary>typed vertexDelta editがbinaryを書かずにUndo/Redoされることを検証します。</summary>
        [Test]
        public void Undo_VertexDeltaEdit_RestoresAndReappliesTypedValue()
        {
            var authoring = CreateValidAuthoring();
            var replacement = new VlatAuthoringVertexDelta
            {
                VertexIndex = 5,
                PositionDelta = new Vector3(9f, 8f, 7f),
                NormalDelta = new Vector3(0f, 1f, 0f),
            };

            VlatAuthoringCommands.SetVertexDelta(authoring, 0, 0, 0, replacement);
            Undo.FlushUndoRecordObjects();
            var vertex = (VlatAuthoringVertexDeltaModifier)authoring.Layers[0].Modifiers[0];
            Assert.That(vertex.Deltas[0].PositionDelta, Is.EqualTo(new Vector3(9f, 8f, 7f)));

            Undo.PerformUndo();
            vertex = (VlatAuthoringVertexDeltaModifier)authoring.Layers[0].Modifiers[0];
            Assert.That(vertex.Deltas[0].PositionDelta, Is.EqualTo(new Vector3(0.5f, 0.25f, -0.5f)));

            Undo.PerformRedo();
            vertex = (VlatAuthoringVertexDeltaModifier)authoring.Layers[0].Modifiers[0];
            Assert.That(vertex.Deltas[0].PositionDelta, Is.EqualTo(new Vector3(9f, 8f, 7f)));
        }

        private VlatAuthoringDocument CreateValidAuthoring()
        {
            var document = VlatAuthoringDocument.CreateTemporary();
            Track(document);
            document.Metadata.Version = "0.1.0";
            document.Metadata.Generator = "VLat Phase 2A Tests";
            document.Metadata.AvatarName = "Avatar";
            document.Metadata.VrmVersion = "1.0";
            var binding = new VlatAuthoringMeshBinding
            {
                HierarchyPath = "Face",
                MeshName = "Face",
                VertexCount = 8,
                PositionHash = "xxh64:0123456789abcdef",
            };
            binding.Materials.Add(new VlatAuthoringMaterialSlot
            {
                Slot = 0,
                MaterialName = "Face",
                Shader = "VRM10/MToon10",
            });
            document.Metadata.MeshBindings.Add(binding);
            document.Textures.Add(new VlatAuthoringTexture
            {
                Name = "Embedded",
                IsEmbedded = true,
                MimeType = "image/png",
                EmbeddedBytes = new byte[] { 0x89, 0x50, 0x4e },
            });
            document.Textures.Add(new VlatAuthoringTexture
            {
                Name = "External",
                IsEmbedded = false,
                Uri = "external.png",
            });
            document.Bands.Add(CreateBand("band-a"));
            document.Bands.Add(CreateBand("band-b"));

            var vertex = new VlatAuthoringVertexDeltaModifier
            {
                MeshBinding = 0,
                HasNormals = true,
            };
            vertex.Deltas.Add(new VlatAuthoringVertexDelta
            {
                VertexIndex = 5,
                PositionDelta = new Vector3(0.5f, 0.25f, -0.5f),
                NormalDelta = new Vector3(0.1f, 0f, 0f),
            });
            vertex.Deltas.Add(new VlatAuthoringVertexDelta
            {
                VertexIndex = 1,
                PositionDelta = new Vector3(-0.1f, 0.2f, 0.3f),
                NormalDelta = new Vector3(0f, 0.2f, 0f),
            });
            var firstLayer = CreateLayer("layer-a", "band-a");
            firstLayer.Name = "Layer A";
            firstLayer.Tag = "bangs";
            firstLayer.Priority = 2;
            firstLayer.Modifiers.Add(vertex);
            firstLayer.Modifiers.Add(new VlatAuthoringVisibilityModifier
            {
                MeshBinding = 0,
                SubMesh = 0,
                VisibleInBand = false,
            });
            firstLayer.Modifiers.Add(new VlatAuthoringOpacityModifier
            {
                MeshBinding = 0,
                MaterialSlot = 0,
                Value = 0.5d,
            });
            firstLayer.Modifiers.Add(new VlatAuthoringMaterialPropertyModifier
            {
                MeshBinding = 0,
                MaterialSlot = 0,
                Property = "_Cutoff",
                ValueType = "float",
                IsScalar = true,
                Scalar = 0.25d,
                Interpolate = true,
            });
            firstLayer.Modifiers.Add(new VlatAuthoringRenderQueueModifier
            {
                MeshBinding = 0,
                MaterialSlot = 0,
                Offset = 10,
            });
            document.Layers.Add(firstLayer);

            var secondLayer = CreateLayer("layer-b", "band-b");
            secondLayer.Name = "Layer B";
            secondLayer.Modifiers.Add(new VlatAuthoringVisibilityModifier
            {
                MeshBinding = 0,
                SubMesh = 1,
                VisibleInBand = true,
            });
            document.Layers.Add(secondLayer);
            return document;
        }

        private static VlatAuthoringBand CreateBand(string id)
        {
            return new VlatAuthoringBand
            {
                Id = id,
                Name = id,
                YawMin = -30d,
                YawMax = 30d,
                PitchMin = -20d,
                PitchMax = 20d,
                FeatherYaw = 5d,
                FeatherPitch = 5d,
                Hysteresis = 1d,
            };
        }

        private static VlatAuthoringLayer CreateLayer(string id, string bandId)
        {
            return new VlatAuthoringLayer
            {
                Id = id,
                Name = id,
                Tag = "other",
                Enabled = true,
                Priority = 0,
                BandId = bandId,
            };
        }

        private static VlatDocument CreateMultipleBufferDocument(out IReadOnlyList<byte[]> buffers)
        {
            var vertexBytes = new byte[56];
            WriteUInt32(vertexBytes, 0, 5u);
            WriteUInt32(vertexBytes, 4, 1u);
            WriteVector3(vertexBytes, 8, new Vector3(0.5f, 0.25f, -0.5f));
            WriteVector3(vertexBytes, 20, new Vector3(-0.1f, 0.2f, 0.3f));
            WriteVector3(vertexBytes, 32, new Vector3(0.1f, 0f, 0f));
            WriteVector3(vertexBytes, 44, new Vector3(0f, 0.2f, 0f));
            buffers = new byte[][]
            {
                new byte[] { 0x89, 0x50, 0x4e },
                vertexBytes,
            };

            var material = new VlatMaterialSlot(0, "Face", "VRM10/MToon10");
            var binding = new MeshBinding(
                "Face",
                "Face",
                8,
                "xxh64:0123456789abcdef",
                new[] { material });
            var metadata = new VlatMetadata(
                "0.1.0",
                "Multiple Buffer Fixture",
                new TargetAvatar("Avatar", "1.0", new[] { binding }));
            var bands = new[]
            {
                new AngleBand(
                    "band-a",
                    "Band A",
                    new AngleRange(-30d, 30d),
                    new AngleRange(-20d, 20d),
                    new AngleFeather(5d, 5d),
                    1d),
                new AngleBand(
                    "band-b",
                    "Band B",
                    new AngleRange(40d, 80d),
                    new AngleRange(-20d, 20d),
                    new AngleFeather(5d, 5d)),
            };
            var vertex = new VertexDeltaModifier(
                0,
                new VlatAccessor(1, "UINT32"),
                new VlatAccessor(2, "FLOAT32"),
                new VlatAccessor(3, "FLOAT32"));
            var layers = new[]
            {
                new VlatLayer(
                    "layer-a",
                    "Layer A",
                    "bangs",
                    true,
                    2,
                    "band-a",
                    new VlatModifier[]
                    {
                        vertex,
                        new VisibilityModifier(0, 0, false),
                        new OpacityModifier(0, 0, 0.5d),
                        new MaterialPropertyModifier(
                            0,
                            0,
                            "_Cutoff",
                            "float",
                            new VlatMaterialValue(0.25d),
                            true),
                        new RenderQueueModifier(0, 0, 10),
                    }),
                new VlatLayer(
                    "layer-b",
                    "Layer B",
                    "other",
                    true,
                    0,
                    "band-b",
                    new VlatModifier[] { new VisibilityModifier(0, 1, true) }),
            };
            return new VlatDocument(
                metadata,
                new[]
                {
                    new VlatBuffer("texture.bin", 3),
                    new VlatBuffer("vertex.bin", 56),
                },
                new[]
                {
                    new VlatBufferView(0, 0, 3),
                    new VlatBufferView(1, 0, 8),
                    new VlatBufferView(1, 8, 24),
                    new VlatBufferView(1, 32, 24),
                },
                new[]
                {
                    new VlatTexture("Embedded", null, 0, "image/png"),
                    new VlatTexture("External", "external.png", null, null),
                },
                bands,
                layers);
        }

        private void Track(VlatAuthoringDocument document)
        {
            if (document != null && !_documents.Contains(document))
            {
                _documents.Add(document);
            }
        }

        private static string CreateTemporaryDirectory()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "VlatAuthoringPersistenceTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static bool ContainsSeverity(
            IReadOnlyList<VlatDiagnostic> diagnostics,
            VlatDiagnosticSeverity severity)
        {
            for (var index = 0; index < diagnostics.Count; index++)
            {
                if (diagnostics[index].Severity == severity)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsPath(IReadOnlyList<VlatDiagnostic> diagnostics, string value)
        {
            for (var index = 0; index < diagnostics.Count; index++)
            {
                if (diagnostics[index].Path.IndexOf(value, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string JoinDiagnostics(IReadOnlyList<VlatDiagnostic> diagnostics)
        {
            var result = string.Empty;
            for (var index = 0; index < diagnostics.Count; index++)
            {
                if (index > 0)
                {
                    result += " | ";
                }

                result += diagnostics[index].Path + ": " + diagnostics[index].Message;
            }

            return result;
        }

        private static uint ReadUInt32(byte[] source, int offset)
        {
            return (uint)(source[offset]
                | (source[offset + 1] << 8)
                | (source[offset + 2] << 16)
                | (source[offset + 3] << 24));
        }

        private static void WriteUInt32(byte[] destination, int offset, uint value)
        {
            destination[offset] = (byte)value;
            destination[offset + 1] = (byte)(value >> 8);
            destination[offset + 2] = (byte)(value >> 16);
            destination[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteVector3(byte[] destination, int offset, Vector3 value)
        {
            WriteSingle(destination, offset, value.x);
            WriteSingle(destination, offset + 4, value.y);
            WriteSingle(destination, offset + 8, value.z);
        }

        private static void WriteSingle(byte[] destination, int offset, float value)
        {
            var bits = BitConverter.SingleToInt32Bits(value);
            destination[offset] = (byte)bits;
            destination[offset + 1] = (byte)(bits >> 8);
            destination[offset + 2] = (byte)(bits >> 16);
            destination[offset + 3] = (byte)(bits >> 24);
        }
    }
}
