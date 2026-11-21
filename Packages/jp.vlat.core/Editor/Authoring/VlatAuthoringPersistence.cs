using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VLat.Core.Model;
using VLat.Core.Serialization;
using VLat.Core.Validation;

namespace VLat.Editor.Authoring
{
    internal enum VlatAuthoringValidationMode
    {
        Editing,
        Save,
    }

    internal static partial class VlatAuthoringPersistence
    {
        private const string DefaultBinaryUri = "document.vlat.bin";

        internal static VlatAuthoringDocument CreateNew(
            string avatarName = "Untitled Avatar",
            string vrmVersion = "1.0",
            string generator = "VLat Editor")
        {
            var document = VlatAuthoringDocument.CreateTemporary();
            document.Metadata.Version = VlatValidator.SupportedVersion;
            document.Metadata.Generator = generator;
            document.Metadata.AvatarName = avatarName;
            document.Metadata.VrmVersion = vrmVersion;
            return document;
        }

        internal static VlatAuthoringLoadResult Load(string jsonPath)
        {
            var loadResult = VlatSerializer.Load(jsonPath);
            if (!loadResult.Success)
            {
                return new VlatAuthoringLoadResult(null, loadResult.Diagnostics);
            }

            return VlatAuthoringConversion.FromImmutable(
                loadResult.Document,
                loadResult.Buffers,
                Path.GetFullPath(jsonPath));
        }

        internal static IReadOnlyList<VlatDiagnostic> Validate(
            VlatAuthoringDocument source,
            VlatAuthoringValidationMode mode)
        {
            return Validate(source, mode, out _);
        }

        internal static IReadOnlyList<VlatDiagnostic> Validate(
            VlatAuthoringDocument source, VlatAuthoringValidationMode mode, out VlatAuthoringPackResult packResult)
        {
            // 検証は常に新しくpackし、同じRevisionの編集境界(Preview/Drag開始/commit事前確認)で再利用する。
            packResult = PackForEditing(source, refresh: true);
            if (mode == VlatAuthoringValidationMode.Save || (source != null && source.KeyformGroups.Count != 0))
            {
                return packResult.Diagnostics;
            }

            var diagnostics = new List<VlatDiagnostic>(packResult.Diagnostics.Count);
            for (var index = 0; index < packResult.Diagnostics.Count; index++)
            {
                var diagnostic = packResult.Diagnostics[index];
                diagnostics.Add(new VlatDiagnostic(
                    diagnostic.Severity == VlatDiagnosticSeverity.Error
                        ? VlatDiagnosticSeverity.Warning
                        : diagnostic.Severity,
                    diagnostic.Path,
                    diagnostic.Line,
                    diagnostic.Column,
                    diagnostic.Message));
            }

            return diagnostics.AsReadOnly();
        }

        /// <summary>
        /// 編集中のin-memory評価用pack。同じDocument instance・Revisionの結果を再利用します。
        /// <paramref name="refresh"/>がtrueなら必ず新しくpackし、結果を再利用対象として保持します。
        /// 保存はこの経路を使わず、保存先URIで<see cref="Pack"/>します。
        /// </summary>
        internal static VlatAuthoringPackResult PackForEditing(VlatAuthoringDocument source, bool refresh = false)
        {
            if (source == null) return Pack(null, DefaultBinaryUri);
            if (!refresh)
            {
                var cached = source.GetCachedEditingPack();
                if (cached != null) return cached;
            }

            var result = Pack(source, DefaultBinaryUri);
            source.SetCachedEditingPack(result);
            return result;
        }

        internal static VlatAuthoringPackResult Pack(
            VlatAuthoringDocument source,
            string binaryUri = DefaultBinaryUri)
        {
            var diagnostics = new List<VlatDiagnostic>();
            using (VlatEditorProfiling.Measure(VlatEditorPhase.PackStructure))
                ValidateStructure(source, diagnostics);
            if (VlatAuthoringResultUtility.ContainsError(diagnostics))
            {
                return new VlatAuthoringPackResult(null, null, null, diagnostics);
            }

            try
            {
                CanonicalPlan plan;
                using (VlatEditorProfiling.Measure(VlatEditorPhase.PackPlan))
                    plan = CreateCanonicalPlan(source, diagnostics);
                if (VlatAuthoringResultUtility.ContainsError(diagnostics))
                {
                    return new VlatAuthoringPackResult(null, null, null, diagnostics);
                }

                if (plan.HasBinary && string.IsNullOrWhiteSpace(binaryUri))
                {
                    diagnostics.Add(VlatAuthoringResultUtility.Error(
                        "$.buffers[0].uri",
                        "バイナリを含む保存ではbinaryUriが必要です。"));
                    return new VlatAuthoringPackResult(null, null, null, diagnostics);
                }

                byte[] binary;
                using (VlatEditorProfiling.Measure(VlatEditorPhase.PackBinary))
                    binary = plan.PackBinary();
                VlatDocument document;
                using (VlatEditorProfiling.Measure(VlatEditorPhase.PackBuild))
                    document = BuildImmutable(source, plan, binaryUri);
                IReadOnlyList<byte[]> buffers = plan.HasBinary
                    ? new[] { binary }
                    : Array.Empty<byte[]>();
                using (VlatEditorProfiling.Measure(VlatEditorPhase.PackCoreValidate))
                    diagnostics.AddRange(VlatValidator.Validate(document, buffers));
                if (VlatAuthoringResultUtility.ContainsError(diagnostics))
                {
                    return new VlatAuthoringPackResult(null, null, null, diagnostics);
                }

                string json;
                using (VlatEditorProfiling.Measure(VlatEditorPhase.PackJson))
                    json = VlatSerializer.Serialize(document, true);
                return new VlatAuthoringPackResult(document, buffers, json, diagnostics);
            }
            catch (Exception exception)
            {
                diagnostics.Add(VlatAuthoringResultUtility.Error(
                    "$",
                    "Authoring Modelをpackできません: " + exception.Message));
                return new VlatAuthoringPackResult(null, null, null, diagnostics);
            }
        }

        internal static VlatAuthoringSaveResult Save(
            string jsonPath,
            VlatAuthoringDocument source)
        {
            if (string.IsNullOrWhiteSpace(jsonPath))
            {
                return new VlatAuthoringSaveResult(new[]
                {
                    VlatAuthoringResultUtility.Error("$", "jsonPathは空にできません。"),
                });
            }

            string fullJsonPath;
            string binaryUri;
            try
            {
                fullJsonPath = Path.GetFullPath(jsonPath);
                binaryUri = GetBinaryUri(fullJsonPath);
            }
            catch (Exception exception)
            {
                return new VlatAuthoringSaveResult(new[]
                {
                    VlatAuthoringResultUtility.Error("$", "保存先パスが不正です: " + exception.Message),
                });
            }

            var packResult = Pack(source, binaryUri);
            if (!packResult.Success)
            {
                return new VlatAuthoringSaveResult(packResult.Diagnostics);
            }

            var saveResult = VlatSerializer.Save(fullJsonPath, packResult.Document, packResult.Buffers);
            if (!saveResult.Success)
            {
                return new VlatAuthoringSaveResult(saveResult.Diagnostics);
            }

            source.MarkSaved(fullJsonPath);
            return new VlatAuthoringSaveResult(saveResult.Diagnostics);
        }

        private static string GetBinaryUri(string jsonPath)
        {
            var fileName = Path.GetFileName(jsonPath);
            if (fileName.EndsWith(".vlat.json", StringComparison.OrdinalIgnoreCase))
            {
                return fileName.Substring(0, fileName.Length - ".json".Length) + ".bin";
            }

            return Path.GetFileNameWithoutExtension(fileName) + ".vlat.bin";
        }

        private static void ValidateStructure(
            VlatAuthoringDocument source,
            List<VlatDiagnostic> diagnostics)
        {
            if (source == null)
            {
                diagnostics.Add(VlatAuthoringResultUtility.Error("$", "Authoring Modelがnullです。"));
                return;
            }

            if (source.Metadata == null)
            {
                diagnostics.Add(VlatAuthoringResultUtility.Error("$.vlat", "metadataがnullです。"));
                return;
            }

            ValidateKeyformStructure(source, diagnostics);

            for (var bindingIndex = 0; bindingIndex < source.Metadata.MeshBindings.Count; bindingIndex++)
            {
                var binding = source.Metadata.MeshBindings[bindingIndex];
                if (binding == null)
                {
                    diagnostics.Add(VlatAuthoringResultUtility.Error(
                        "$.vlat.targetAvatar.meshBindings[" + bindingIndex + "]",
                        "meshBindingがnullです。"));
                    continue;
                }

                for (var materialIndex = 0; materialIndex < binding.Materials.Count; materialIndex++)
                {
                    if (binding.Materials[materialIndex] == null)
                    {
                        diagnostics.Add(VlatAuthoringResultUtility.Error(
                            "$.vlat.targetAvatar.meshBindings[" + bindingIndex + "].materials[" + materialIndex + "]",
                            "materialがnullです。"));
                    }
                }
            }

            for (var textureIndex = 0; textureIndex < source.Textures.Count; textureIndex++)
            {
                var texture = source.Textures[textureIndex];
                var path = "$.textures[" + textureIndex + "]";
                if (texture == null)
                {
                    diagnostics.Add(VlatAuthoringResultUtility.Error(path, "textureがnullです。"));
                }
                else if (texture.IsEmbedded && (texture.EmbeddedBytes == null || texture.EmbeddedBytes.Length == 0))
                {
                    diagnostics.Add(VlatAuthoringResultUtility.Error(
                        path + ".bufferView",
                        "埋め込みtextureのbyte列は空にできません。"));
                }
            }

            for (var bandIndex = 0; bandIndex < source.Bands.Count; bandIndex++)
            {
                if (source.Bands[bandIndex] == null)
                {
                    diagnostics.Add(VlatAuthoringResultUtility.Error(
                        "$.angleBands[" + bandIndex + "]",
                        "angleBandがnullです。"));
                }
            }

            for (var layerIndex = 0; layerIndex < source.Layers.Count; layerIndex++)
            {
                var layer = source.Layers[layerIndex];
                var layerPath = "$.layers[" + layerIndex + "]";
                if (layer == null)
                {
                    diagnostics.Add(VlatAuthoringResultUtility.Error(layerPath, "layerがnullです。"));
                    continue;
                }

                for (var modifierIndex = 0; modifierIndex < layer.Modifiers.Count; modifierIndex++)
                {
                    ValidateModifier(
                        source,
                        layer.Modifiers[modifierIndex],
                        layerPath + ".modifiers[" + modifierIndex + "]",
                        diagnostics);
                }
            }
        }

        private static void ValidateModifier(
            VlatAuthoringDocument source,
            VlatAuthoringModifier modifier,
            string path,
            List<VlatDiagnostic> diagnostics)
        {
            if (modifier == null)
            {
                diagnostics.Add(VlatAuthoringResultUtility.Error(path, "modifierがnullです。"));
                return;
            }

            var vertex = modifier as VlatAuthoringVertexDeltaModifier;
            if (vertex == null)
            {
                if (!(modifier is VlatAuthoringVisibilityModifier) &&
                    !(modifier is VlatAuthoringOpacityModifier) &&
                    !(modifier is VlatAuthoringMaterialPropertyModifier) &&
                    !(modifier is VlatAuthoringRenderQueueModifier))
                {
                    diagnostics.Add(VlatAuthoringResultUtility.Error(path, "未対応のmodifier型です。"));
                }

                return;
            }

            if (vertex.Deltas.Count == 0)
            {
                diagnostics.Add(VlatAuthoringResultUtility.Error(
                    path + ".deltas",
                    "vertexDeltaは1要素以上必要です。編集中は保持できますがSaveできません。"));
                return;
            }

            var vertexCount = -1;
            if (vertex.MeshBinding >= 0 && vertex.MeshBinding < source.Metadata.MeshBindings.Count)
            {
                var binding = source.Metadata.MeshBindings[vertex.MeshBinding];
                if (binding != null)
                {
                    vertexCount = binding.VertexCount;
                }
            }

            // 範囲内indexの重複検出。頂点数が既知なら配列で判定し、未知（binding不正）ならHashSetを使う。
            // 診断のpath・message・順序は従来と同じ。pathは診断を出す時だけ組み立てる（大規模での割当削減）。
            var seen = vertexCount >= 0 && vertexCount <= MaximumSeenArrayVertices ? new bool[vertexCount] : null;
            var indices = seen == null ? new HashSet<int>() : null;
            for (var deltaIndex = 0; deltaIndex < vertex.Deltas.Count; deltaIndex++)
            {
                var delta = vertex.Deltas[deltaIndex];
                if (delta == null)
                {
                    diagnostics.Add(VlatAuthoringResultUtility.Error(DeltaPath(path, deltaIndex), "vertexDelta要素がnullです。"));
                    continue;
                }

                if (delta.VertexIndex < 0 || (vertexCount >= 0 && delta.VertexIndex >= vertexCount))
                {
                    diagnostics.Add(VlatAuthoringResultUtility.Error(
                        DeltaPath(path, deltaIndex) + ".vertexIndex",
                        "vertexIndexがmeshBindingの頂点範囲外です。"));
                }
                else if (seen != null ? seen[delta.VertexIndex] : !indices.Add(delta.VertexIndex))
                {
                    diagnostics.Add(VlatAuthoringResultUtility.Error(
                        DeltaPath(path, deltaIndex) + ".vertexIndex",
                        "同じvertexIndexを重複して保持できません。"));
                }
                else if (seen != null)
                {
                    seen[delta.VertexIndex] = true;
                }

                if (!IsFinite(delta.PositionDelta))
                {
                    diagnostics.Add(VlatAuthoringResultUtility.Error(
                        DeltaPath(path, deltaIndex) + ".positionDelta",
                        "positionDeltaは有限値である必要があります。"));
                }

                if (vertex.HasNormals && !IsFinite(delta.NormalDelta))
                {
                    diagnostics.Add(VlatAuthoringResultUtility.Error(
                        DeltaPath(path, deltaIndex) + ".normalDelta",
                        "normalDeltaは有限値である必要があります。"));
                }
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static CanonicalPlan CreateCanonicalPlan(
            VlatAuthoringDocument source,
            List<VlatDiagnostic> diagnostics)
        {
            var plan = new CanonicalPlan(source.Textures.Count, source.Layers.Count);
            for (var textureIndex = 0; textureIndex < source.Textures.Count; textureIndex++)
            {
                var texture = source.Textures[textureIndex];
                if (texture.IsEmbedded)
                {
                    plan.TextureViewIndices[textureIndex] = plan.AddTexture(texture.EmbeddedBytes);
                }
            }

            for (var layerIndex = 0; layerIndex < source.Layers.Count; layerIndex++)
            {
                var layer = source.Layers[layerIndex];
                var layerPlan = new LayerPlan(layer.Modifiers.Count);
                plan.Layers.Add(layerPlan);
                for (var modifierIndex = 0; modifierIndex < layer.Modifiers.Count; modifierIndex++)
                {
                    var modifier = layer.Modifiers[modifierIndex] as VlatAuthoringVertexDeltaModifier;
                    if (modifier == null)
                    {
                        continue;
                    }

                    var canonical = new List<VlatAuthoringVertexDelta>(modifier.Deltas.Count);
                    for (var deltaIndex = 0; deltaIndex < modifier.Deltas.Count; deltaIndex++)
                    {
                        canonical.Add(modifier.Deltas[deltaIndex].Copy());
                    }

                    canonical.Sort(VertexDeltaComparer.Instance);
                    layerPlan.VertexPlans[modifierIndex] = plan.AddVertexDelta(canonical, modifier.HasNormals);
                }
            }

            AppendKeyformPlan(source, plan);
            return plan;
        }

        private static VlatDocument BuildImmutable(
            VlatAuthoringDocument source,
            CanonicalPlan plan,
            string binaryUri)
        {
            var bindings = new MeshBinding[source.Metadata.MeshBindings.Count];
            for (var bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
            {
                var sourceBinding = source.Metadata.MeshBindings[bindingIndex];
                var materials = new VlatMaterialSlot[sourceBinding.Materials.Count];
                for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    var material = sourceBinding.Materials[materialIndex];
                    materials[materialIndex] = new VlatMaterialSlot(
                        material.Slot,
                        material.MaterialName,
                        material.Shader);
                }

                bindings[bindingIndex] = new MeshBinding(
                    sourceBinding.HierarchyPath,
                    sourceBinding.MeshName,
                    sourceBinding.VertexCount,
                    sourceBinding.PositionHash,
                    materials);
            }

            var metadata = new VlatMetadata(
                source.Metadata.Version,
                source.Metadata.Generator,
                new TargetAvatar(source.Metadata.AvatarName, source.Metadata.VrmVersion, bindings));
            var buffers = plan.HasBinary
                ? new[] { new VlatBuffer(binaryUri, plan.BinaryLength) }
                : Array.Empty<VlatBuffer>();
            var textures = new VlatTexture[source.Textures.Count];
            for (var textureIndex = 0; textureIndex < textures.Length; textureIndex++)
            {
                var texture = source.Textures[textureIndex];
                textures[textureIndex] = texture.IsEmbedded
                    ? new VlatTexture(
                        texture.Name,
                        null,
                        plan.TextureViewIndices[textureIndex],
                        texture.MimeType)
                    : new VlatTexture(texture.Name, texture.Uri, null, null);
            }

            var bands = new AngleBand[source.Bands.Count];
            for (var bandIndex = 0; bandIndex < bands.Length; bandIndex++)
            {
                var band = source.Bands[bandIndex];
                bands[bandIndex] = new AngleBand(
                    band.Id,
                    band.Name,
                    new AngleRange(band.YawMin, band.YawMax),
                    new AngleRange(band.PitchMin, band.PitchMax),
                    new AngleFeather(band.FeatherYaw, band.FeatherPitch),
                    band.Hysteresis);
            }

            var layers = new VlatLayer[source.Layers.Count];
            for (var layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                var layer = source.Layers[layerIndex];
                var modifiers = new VlatModifier[layer.Modifiers.Count];
                for (var modifierIndex = 0; modifierIndex < modifiers.Length; modifierIndex++)
                {
                    modifiers[modifierIndex] = BuildModifier(
                        layer.Modifiers[modifierIndex],
                        plan.Layers[layerIndex].VertexPlans[modifierIndex]);
                }

                layers[layerIndex] = new VlatLayer(
                    layer.Id,
                    layer.Name,
                    layer.Tag,
                    layer.Enabled,
                    layer.Priority,
                    layer.BandId,
                    modifiers);
            }

            return new VlatDocument(metadata, buffers, plan.BufferViews, textures, bands, layers,
                BuildKeyformGroups(source, plan));
        }

        private static VlatModifier BuildModifier(
            VlatAuthoringModifier source,
            VertexPlan vertexPlan)
        {
            var vertex = source as VlatAuthoringVertexDeltaModifier;
            if (vertex != null)
            {
                return new VertexDeltaModifier(
                    vertex.MeshBinding,
                    new VlatAccessor(vertexPlan.IndicesView, "UINT32"),
                    new VlatAccessor(vertexPlan.PositionsView, "FLOAT32"),
                    vertex.HasNormals
                        ? new VlatAccessor(vertexPlan.NormalsView, "FLOAT32")
                        : null);
            }

            var visibility = source as VlatAuthoringVisibilityModifier;
            if (visibility != null)
            {
                return new VisibilityModifier(
                    visibility.MeshBinding,
                    visibility.SubMesh,
                    visibility.VisibleInBand);
            }

            var opacity = source as VlatAuthoringOpacityModifier;
            if (opacity != null)
            {
                return new OpacityModifier(opacity.MeshBinding, opacity.MaterialSlot, opacity.Value);
            }

            var material = source as VlatAuthoringMaterialPropertyModifier;
            if (material != null)
            {
                var value = material.IsScalar
                    ? new VlatMaterialValue(material.Scalar)
                    : new VlatMaterialValue(material.Components);
                return new MaterialPropertyModifier(
                    material.MeshBinding,
                    material.MaterialSlot,
                    material.Property,
                    material.ValueType,
                    value,
                    material.Interpolate);
            }

            var queue = source as VlatAuthoringRenderQueueModifier;
            if (queue != null)
            {
                return new RenderQueueModifier(queue.MeshBinding, queue.MaterialSlot, queue.Offset);
            }

            throw new InvalidOperationException("未対応のModifier型です。");
        }

        // 重複検出に配列を使う頂点数の上限（約4 MB）。これを超える異常なbindingはHashSetで従来どおり判定する。
        private const int MaximumSeenArrayVertices = 4 * 1024 * 1024;

        private static string DeltaPath(string path, int deltaIndex) => path + ".deltas[" + deltaIndex + "]";

        // 既に昇順（重複なし）なら並べ替えを省く。重複はstructure検証で先に拒否されるため結果は同一。
        internal static void SortByVertexIndex(List<VlatAuthoringVertexDelta> deltas)
        {
            for (var i = 1; i < deltas.Count; i++)
                if (deltas[i - 1].VertexIndex >= deltas[i].VertexIndex)
                {
                    deltas.Sort(VertexDeltaComparer.Instance);
                    return;
                }
        }

        private sealed class VertexDeltaComparer : IComparer<VlatAuthoringVertexDelta>
        {
            internal static readonly VertexDeltaComparer Instance = new VertexDeltaComparer();

            public int Compare(VlatAuthoringVertexDelta left, VlatAuthoringVertexDelta right)
            {
                return left.VertexIndex.CompareTo(right.VertexIndex);
            }
        }

        private sealed class LayerPlan
        {
            internal LayerPlan(int modifierCount)
            {
                VertexPlans = new VertexPlan[modifierCount];
            }

            internal VertexPlan[] VertexPlans { get; }
        }

        private sealed class VertexPlan
        {
            internal VertexPlan(
                IReadOnlyList<VlatAuthoringVertexDelta> deltas,
                bool hasNormals,
                int indicesView,
                int positionsView,
                int normalsView)
            {
                Deltas = deltas;
                HasNormals = hasNormals;
                IndicesView = indicesView;
                PositionsView = positionsView;
                NormalsView = normalsView;
            }

            internal IReadOnlyList<VlatAuthoringVertexDelta> Deltas { get; }
            internal bool HasNormals { get; }
            internal int IndicesView { get; }
            internal int PositionsView { get; }
            internal int NormalsView { get; }
        }

        private enum PayloadKind
        {
            Texture,
            VertexIndices,
            VertexPositions,
            VertexNormals,
        }

        private sealed class Payload
        {
            internal Payload(
                PayloadKind kind,
                int offset,
                byte[] bytes,
                IReadOnlyList<VlatAuthoringVertexDelta> deltas)
            {
                Kind = kind;
                Offset = offset;
                Bytes = bytes;
                Deltas = deltas;
            }

            internal PayloadKind Kind { get; }
            internal int Offset { get; }
            internal byte[] Bytes { get; }
            internal IReadOnlyList<VlatAuthoringVertexDelta> Deltas { get; }

            internal void WriteTo(byte[] destination)
            {
                if (Kind == PayloadKind.Texture)
                {
                    System.Buffer.BlockCopy(Bytes, 0, destination, Offset, Bytes.Length);
                    return;
                }

                for (var index = 0; index < Deltas.Count; index++)
                {
                    var delta = Deltas[index];
                    if (Kind == PayloadKind.VertexIndices)
                    {
                        WriteUInt32(destination, Offset + (index * 4), checked((uint)delta.VertexIndex));
                    }
                    else
                    {
                        var value = Kind == PayloadKind.VertexPositions
                            ? delta.PositionDelta
                            : delta.NormalDelta;
                        WriteVector3(destination, Offset + (index * 12), value);
                    }
                }
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

        private sealed class CanonicalPlan
        {
            private readonly List<Payload> _payloads = new List<Payload>();
            private readonly List<VlatBufferView> _bufferViews = new List<VlatBufferView>();
            private int _nextOffset;

            internal CanonicalPlan(int textureCount, int layerCount)
            {
                TextureViewIndices = new int[textureCount];
                for (var index = 0; index < textureCount; index++)
                {
                    TextureViewIndices[index] = -1;
                }

                Layers = new List<LayerPlan>(layerCount);
            }

            internal int[] TextureViewIndices { get; }
            internal List<LayerPlan> Layers { get; }
            internal List<VertexPlan[]> Keyforms { get; } = new List<VertexPlan[]>();
            internal IReadOnlyList<VlatBufferView> BufferViews => _bufferViews;
            internal bool HasBinary => _payloads.Count > 0;
            internal int BinaryLength => Align4(_nextOffset);

            internal int AddTexture(byte[] bytes)
            {
                return AddPayload(PayloadKind.Texture, bytes.Length, bytes, null);
            }

            internal VertexPlan AddVertexDelta(
                IReadOnlyList<VlatAuthoringVertexDelta> deltas,
                bool hasNormals)
            {
                var indices = AddPayload(
                    PayloadKind.VertexIndices,
                    checked(deltas.Count * 4),
                    null,
                    deltas);
                var positions = AddPayload(
                    PayloadKind.VertexPositions,
                    checked(deltas.Count * 12),
                    null,
                    deltas);
                var normals = hasNormals
                    ? AddPayload(
                        PayloadKind.VertexNormals,
                        checked(deltas.Count * 12),
                        null,
                        deltas)
                    : -1;
                return new VertexPlan(deltas, hasNormals, indices, positions, normals);
            }

            internal byte[] PackBinary()
            {
                if (!HasBinary)
                {
                    return Array.Empty<byte>();
                }

                var result = new byte[BinaryLength];
                for (var index = 0; index < _payloads.Count; index++)
                {
                    _payloads[index].WriteTo(result);
                }

                return result;
            }

            private int AddPayload(
                PayloadKind kind,
                int length,
                byte[] bytes,
                IReadOnlyList<VlatAuthoringVertexDelta> deltas)
            {
                var offset = Align4(_nextOffset);
                var viewIndex = _bufferViews.Count;
                _bufferViews.Add(new VlatBufferView(0, offset, length));
                _payloads.Add(new Payload(kind, offset, bytes, deltas));
                _nextOffset = checked(offset + length);
                return viewIndex;
            }

            private static int Align4(int value)
            {
                return checked((value + 3) & ~3);
            }
        }
    }
}
