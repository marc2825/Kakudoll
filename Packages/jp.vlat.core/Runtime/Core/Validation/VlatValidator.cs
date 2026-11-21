using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using VLat.Core.Model;

namespace VLat.Core.Validation
{
    /// <summary>VLatドキュメントの参照整合性とバイナリ範囲を検証します。</summary>
    public static partial class VlatValidator
    {
        /// <summary>この実装が直接対応するVLatフォーマットバージョンです。</summary>
        public const string SupportedVersion = "0.1.0";

        /// <summary>1ドキュメントに格納できるLayerの最大数です。</summary>
        public const int MaximumLayerCount = 64;

        private static readonly string[] AllowedTags =
        {
            "bangs",
            "sideHair",
            "hairHighlight",
            "eyeHighlight",
            "faceLine",
            "other"
        };

        /// <summary>バイナリ内容を伴わないドキュメントの意味的整合性を検証します。</summary>
        /// <param name="document">検証するドキュメント。</param>
        /// <returns>検出したエラーと警告。</returns>
        public static IReadOnlyList<VlatDiagnostic> Validate(VlatDocument document)
        {
            return Validate(document, null);
        }

        /// <summary>ドキュメントと読み込み済みバイナリの意味的整合性を検証します。</summary>
        /// <param name="document">検証するドキュメント。</param>
        /// <param name="binaryBuffers">buffers配列と同じ順序のバイナリ内容。内容検証を省略する場合はnull。</param>
        /// <returns>検出したエラーと警告。</returns>
        public static IReadOnlyList<VlatDiagnostic> Validate(
            VlatDocument document,
            IReadOnlyList<byte[]> binaryBuffers)
        {
            var diagnostics = new List<VlatDiagnostic>();
            if (document == null)
            {
                AddError(diagnostics, "$", "VLatドキュメントがnullです。");
                return diagnostics.AsReadOnly();
            }

            ValidateMetadata(document.Metadata, diagnostics);
            ValidateBuffers(document, binaryBuffers, diagnostics);
            ValidateTextures(document, diagnostics);
            ValidateAnglesAndLayers(document, binaryBuffers, diagnostics);
            ValidateKeyformGroups(document, binaryBuffers, diagnostics);
            return diagnostics.AsReadOnly();
        }

        private static void ValidateMetadata(VlatMetadata metadata, List<VlatDiagnostic> diagnostics)
        {
            if (metadata == null)
            {
                AddError(diagnostics, "$.vlat", "vlatは必須です。");
                return;
            }

            SemanticVersion version;
            if (!SemanticVersion.TryParse(metadata.Version, out version))
            {
                AddError(diagnostics, "$.vlat.version", "versionはSemVer形式である必要があります。");
            }
            else
            {
                SemanticVersion supported;
                SemanticVersion.TryParse(SupportedVersion, out supported);
                if (!string.Equals(version.Major, supported.Major, StringComparison.Ordinal))
                {
                    AddError(
                        diagnostics,
                        "$.vlat.version",
                        "互換性のないmajorバージョンです。対応majorは" + supported.Major + "です。");
                }
                else if (SemanticVersion.CompareNumeric(version.Minor, supported.Minor) > 0)
                {
                    AddWarning(
                        diagnostics,
                        "$.vlat.version",
                        "この実装より新しいフォーマットバージョンです。現行Schemaにないフィールドがある場合は読み込みに失敗します。");
                }
            }

            if (string.IsNullOrWhiteSpace(metadata.Generator))
            {
                AddError(diagnostics, "$.vlat.generator", "generatorは空にできません。");
            }

            var avatar = metadata.TargetAvatar;
            if (avatar == null)
            {
                AddError(diagnostics, "$.vlat.targetAvatar", "targetAvatarは必須です。");
                return;
            }

            if (string.IsNullOrWhiteSpace(avatar.Name))
            {
                AddError(diagnostics, "$.vlat.targetAvatar.name", "対象アバター名は空にできません。");
            }

            if (!string.Equals(avatar.VrmVersion, "0.x", StringComparison.Ordinal) &&
                !string.Equals(avatar.VrmVersion, "1.0", StringComparison.Ordinal))
            {
                AddError(diagnostics, "$.vlat.targetAvatar.vrmVersion", "vrmVersionは0.xまたは1.0である必要があります。");
            }

            for (var i = 0; i < avatar.MeshBindings.Count; i++)
            {
                ValidateMeshBinding(avatar.MeshBindings[i], i, diagnostics);
            }
        }

        private static void ValidateMeshBinding(
            MeshBinding binding,
            int index,
            List<VlatDiagnostic> diagnostics)
        {
            var path = "$.vlat.targetAvatar.meshBindings[" + Invariant(index) + "]";
            if (binding == null)
            {
                AddError(diagnostics, path, "meshBindingをnullにできません。");
                return;
            }

            if (binding.HierarchyPath == null)
            {
                AddError(diagnostics, path + ".hierarchyPath", "hierarchyPathをnullにできません。");
            }

            if (string.IsNullOrWhiteSpace(binding.MeshName))
            {
                AddError(diagnostics, path + ".meshName", "meshNameは空にできません。");
            }

            if (binding.VertexCount < 0)
            {
                AddError(diagnostics, path + ".vertexCount", "vertexCountは0以上である必要があります。");
            }

            if (!IsValidPositionHash(binding.PositionHash))
            {
                AddError(diagnostics, path + ".positionHash", "positionHashはxxh64:に16桁の16進数を続けた形式である必要があります。");
            }

            var slots = new HashSet<int>();
            for (var materialIndex = 0; materialIndex < binding.Materials.Count; materialIndex++)
            {
                var material = binding.Materials[materialIndex];
                var materialPath = path + ".materials[" + Invariant(materialIndex) + "]";
                if (material == null)
                {
                    AddError(diagnostics, materialPath, "materialをnullにできません。");
                    continue;
                }

                if (material.Slot < 0)
                {
                    AddError(diagnostics, materialPath + ".slot", "slotは0以上である必要があります。");
                }
                else if (!slots.Add(material.Slot))
                {
                    AddError(diagnostics, materialPath + ".slot", "同じmeshBinding内でslotが重複しています。");
                }

                if (string.IsNullOrWhiteSpace(material.MaterialName))
                {
                    AddError(diagnostics, materialPath + ".materialName", "materialNameは空にできません。");
                }

                if (string.IsNullOrWhiteSpace(material.Shader))
                {
                    AddError(diagnostics, materialPath + ".shader", "shaderは空にできません。");
                }
            }
        }

        private static void ValidateBuffers(
            VlatDocument document,
            IReadOnlyList<byte[]> binaryBuffers,
            List<VlatDiagnostic> diagnostics)
        {
            if (binaryBuffers != null && binaryBuffers.Count != document.Buffers.Count)
            {
                AddError(diagnostics, "$.buffers", "バイナリ内容の数がbuffers配列の数と一致しません。");
            }

            for (var i = 0; i < document.Buffers.Count; i++)
            {
                var buffer = document.Buffers[i];
                var path = "$.buffers[" + Invariant(i) + "]";
                if (buffer == null)
                {
                    AddError(diagnostics, path, "bufferをnullにできません。");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(buffer.Uri))
                {
                    AddError(diagnostics, path + ".uri", "bufferのuriは空にできません。");
                }
                else if (!IsRelativeExternalUri(buffer.Uri))
                {
                    AddError(
                        diagnostics,
                        path + ".uri",
                        "bufferのuriはJSONファイルを基準とした相対URIである必要があります。");
                }

                if (buffer.ByteLength < 1)
                {
                    AddError(diagnostics, path + ".byteLength", "byteLengthは1以上である必要があります。");
                }

                if (binaryBuffers != null && i < binaryBuffers.Count)
                {
                    var data = binaryBuffers[i];
                    if (data == null)
                    {
                        AddError(diagnostics, path, "バイナリ内容がnullです。");
                    }
                    else if (data.LongLength != buffer.ByteLength)
                    {
                        AddError(diagnostics, path + ".byteLength", "宣言されたbyteLengthと実ファイル長が一致しません。");
                    }
                }
            }

            for (var i = 0; i < document.BufferViews.Count; i++)
            {
                var view = document.BufferViews[i];
                var path = "$.bufferViews[" + Invariant(i) + "]";
                if (view == null)
                {
                    AddError(diagnostics, path, "bufferViewをnullにできません。");
                    continue;
                }

                if (view.Buffer < 0 || view.Buffer >= document.Buffers.Count)
                {
                    AddError(diagnostics, path + ".buffer", "参照先bufferが範囲外です。");
                    continue;
                }

                if (view.ByteOffset < 0)
                {
                    AddError(diagnostics, path + ".byteOffset", "byteOffsetは0以上である必要があります。");
                }

                if (view.ByteLength < 1)
                {
                    AddError(diagnostics, path + ".byteLength", "byteLengthは1以上である必要があります。");
                }

                var buffer = document.Buffers[view.Buffer];
                if (buffer == null || buffer.ByteLength < 1 || view.ByteOffset < 0 || view.ByteLength < 1)
                {
                    continue;
                }

                if (view.ByteOffset > buffer.ByteLength || view.ByteLength > buffer.ByteLength - view.ByteOffset)
                {
                    AddError(diagnostics, path, "bufferViewが参照先bufferの範囲を超えています。");
                }
            }
        }

        private static void ValidateTextures(VlatDocument document, List<VlatDiagnostic> diagnostics)
        {
            for (var i = 0; i < document.Textures.Count; i++)
            {
                var texture = document.Textures[i];
                var path = "$.textures[" + Invariant(i) + "]";
                if (texture == null)
                {
                    AddError(diagnostics, path, "textureをnullにできません。");
                    continue;
                }

                if (texture.Name != null && texture.Name.Length == 0)
                {
                    AddError(diagnostics, path + ".name", "texture nameは指定する場合1文字以上である必要があります。");
                }

                var hasUri = !string.IsNullOrWhiteSpace(texture.Uri);
                var hasBufferView = texture.BufferView.HasValue;
                if (hasUri == hasBufferView)
                {
                    AddError(diagnostics, path, "textureはuriまたはbufferViewのちょうど一方を持つ必要があります。");
                }

                if (hasUri && !IsRelativeExternalUri(texture.Uri))
                {
                    AddError(
                        diagnostics,
                        path + ".uri",
                        "textureのuriはJSONファイルを基準とした相対URIである必要があります。");
                }

                if (hasBufferView)
                {
                    var bufferView = texture.BufferView.Value;
                    if (bufferView < 0 || bufferView >= document.BufferViews.Count)
                    {
                        AddError(diagnostics, path + ".bufferView", "参照先bufferViewが範囲外です。");
                    }

                    if (string.IsNullOrWhiteSpace(texture.MimeType))
                    {
                        AddError(diagnostics, path + ".mimeType", "bufferView参照時はmimeTypeが必須です。");
                    }
                }

                if (texture.MimeType != null && !IsValidImageMimeType(texture.MimeType))
                {
                    AddError(diagnostics, path + ".mimeType", "mimeTypeはimage/で始まる有効な形式である必要があります。");
                }
            }
        }

        private static void ValidateAnglesAndLayers(
            VlatDocument document,
            IReadOnlyList<byte[]> binaryBuffers,
            List<VlatDiagnostic> diagnostics)
        {
            if (document.Layers.Count > MaximumLayerCount)
            {
                AddError(
                    diagnostics,
                    "$.layers",
                    "layersは最大" + Invariant(MaximumLayerCount) + "件です。");
            }

            var bandIds = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < document.AngleBands.Count; i++)
            {
                var band = document.AngleBands[i];
                var path = "$.angleBands[" + Invariant(i) + "]";
                if (band == null)
                {
                    AddError(diagnostics, path, "angleBandをnullにできません。");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(band.Id))
                {
                    AddError(diagnostics, path + ".id", "AngleBandのidは空にできません。");
                }
                else if (!bandIds.Add(band.Id))
                {
                    AddError(diagnostics, path + ".id", "AngleBandのidが重複しています。");
                }

                if (string.IsNullOrWhiteSpace(band.Name))
                {
                    AddError(diagnostics, path + ".name", "AngleBandのnameは空にできません。");
                }

                ValidateAngleBand(band, path, diagnostics);
            }

            var layerIds = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < document.Layers.Count; i++)
            {
                var layer = document.Layers[i];
                var path = "$.layers[" + Invariant(i) + "]";
                if (layer == null)
                {
                    AddError(diagnostics, path, "layerをnullにできません。");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(layer.Id))
                {
                    AddError(diagnostics, path + ".id", "Layerのidは空にできません。");
                }
                else if (!layerIds.Add(layer.Id))
                {
                    AddError(diagnostics, path + ".id", "Layerのidが重複しています。");
                }

                if (string.IsNullOrWhiteSpace(layer.Name))
                {
                    AddError(diagnostics, path + ".name", "Layerのnameは空にできません。");
                }

                if (!Contains(AllowedTags, layer.Tag))
                {
                    AddError(diagnostics, path + ".tag", "未対応のLayer tagです。");
                }

                if (string.IsNullOrWhiteSpace(layer.BandId) || !bandIds.Contains(layer.BandId))
                {
                    AddError(diagnostics, path + ".bandId", "参照先AngleBandが存在しません。");
                }

                for (var modifierIndex = 0; modifierIndex < layer.Modifiers.Count; modifierIndex++)
                {
                    ValidateModifier(
                        document,
                        binaryBuffers,
                        layer.Modifiers[modifierIndex],
                        path + ".modifiers[" + Invariant(modifierIndex) + "]",
                        diagnostics);
                }
            }
        }

        private static void ValidateAngleBand(
            AngleBand band,
            string path,
            List<VlatDiagnostic> diagnostics)
        {
            if (band.Yaw == null)
            {
                AddError(diagnostics, path + ".yaw", "yaw範囲は必須です。");
            }
            else
            {
                ValidateFiniteRange(band.Yaw, -180d, 180d, false, path + ".yaw", diagnostics);
            }

            if (band.Pitch == null)
            {
                AddError(diagnostics, path + ".pitch", "pitch範囲は必須です。");
            }
            else
            {
                ValidateFiniteRange(band.Pitch, -90d, 90d, true, path + ".pitch", diagnostics);
            }

            if (band.Feather == null)
            {
                AddError(diagnostics, path + ".feather", "featherは必須です。");
            }
            else
            {
                ValidateNonNegativeFinite(band.Feather.Yaw, path + ".feather.yaw", "feather.yaw", diagnostics);
                ValidateNonNegativeFinite(band.Feather.Pitch, path + ".feather.pitch", "feather.pitch", diagnostics);
            }

            ValidateNonNegativeFinite(band.Hysteresis, path + ".hysteresis", "hysteresis", diagnostics);
        }

        private static void ValidateFiniteRange(
            AngleRange range,
            double lower,
            double upper,
            bool requireOrdered,
            string path,
            List<VlatDiagnostic> diagnostics)
        {
            if (!IsFinite(range.Min) || range.Min < lower || range.Min > upper)
            {
                AddError(diagnostics, path + ".min", "角度が許容範囲外です。");
            }

            if (!IsFinite(range.Max) || range.Max < lower || range.Max > upper)
            {
                AddError(diagnostics, path + ".max", "角度が許容範囲外です。");
            }

            if (requireOrdered && IsFinite(range.Min) && IsFinite(range.Max) && range.Min > range.Max)
            {
                AddError(diagnostics, path, "pitchではminをmax以下にする必要があります。");
            }
        }

        private static void ValidateModifier(
            VlatDocument document,
            IReadOnlyList<byte[]> binaryBuffers,
            VlatModifier modifier,
            string path,
            List<VlatDiagnostic> diagnostics)
        {
            if (modifier == null)
            {
                AddError(diagnostics, path, "modifierをnullにできません。");
                return;
            }

            var vertexDelta = modifier as VertexDeltaModifier;
            if (vertexDelta != null)
            {
                MeshBinding binding;
                if (!TryGetMeshBinding(document, vertexDelta.MeshBinding, path, diagnostics, out binding))
                {
                    binding = null;
                }

                ValidateAccessor(document, vertexDelta.Indices, "UINT32", path + ".indices", diagnostics);
                ValidateAccessor(document, vertexDelta.Positions, "FLOAT32", path + ".positions", diagnostics);
                if (vertexDelta.Normals != null)
                {
                    ValidateAccessor(document, vertexDelta.Normals, "FLOAT32", path + ".normals", diagnostics);
                }

                if (binaryBuffers != null && binding != null)
                {
                    ValidateVertexDeltaBinary(document, binaryBuffers, vertexDelta, binding, path, diagnostics);
                }

                return;
            }

            var visibility = modifier as VisibilityModifier;
            if (visibility != null)
            {
                MeshBinding ignored;
                TryGetMeshBinding(document, visibility.MeshBinding, path, diagnostics, out ignored);
                if (visibility.SubMesh < 0)
                {
                    AddError(diagnostics, path + ".subMesh", "subMeshは0以上である必要があります。");
                }

                return;
            }

            var opacity = modifier as OpacityModifier;
            if (opacity != null)
            {
                ValidateMaterialTarget(document, opacity.MeshBinding, opacity.MaterialSlot, path, diagnostics);
                if (!IsFinite(opacity.Value) || opacity.Value < 0d || opacity.Value > 1d)
                {
                    AddError(diagnostics, path + ".value", "opacity valueは0以上1以下である必要があります。");
                }

                return;
            }

            var property = modifier as MaterialPropertyModifier;
            if (property != null)
            {
                ValidateMaterialTarget(document, property.MeshBinding, property.MaterialSlot, path, diagnostics);
                ValidateMaterialProperty(document, property, path, diagnostics);
                return;
            }

            var renderQueue = modifier as RenderQueueModifier;
            if (renderQueue != null)
            {
                ValidateMaterialTarget(document, renderQueue.MeshBinding, renderQueue.MaterialSlot, path, diagnostics);
                return;
            }

            AddError(diagnostics, path + ".type", "未対応のModifier型です。");
        }

        private static bool TryGetMeshBinding(
            VlatDocument document,
            int meshBindingIndex,
            string modifierPath,
            List<VlatDiagnostic> diagnostics,
            out MeshBinding binding)
        {
            binding = null;
            var avatar = document.Metadata == null ? null : document.Metadata.TargetAvatar;
            if (avatar == null || meshBindingIndex < 0 || meshBindingIndex >= avatar.MeshBindings.Count)
            {
                AddError(diagnostics, modifierPath + ".meshBinding", "参照先meshBindingが範囲外です。");
                return false;
            }

            binding = avatar.MeshBindings[meshBindingIndex];
            if (binding == null)
            {
                AddError(diagnostics, modifierPath + ".meshBinding", "参照先meshBindingがnullです。");
                return false;
            }

            return true;
        }

        private static void ValidateMaterialTarget(
            VlatDocument document,
            int meshBindingIndex,
            int materialSlot,
            string modifierPath,
            List<VlatDiagnostic> diagnostics)
        {
            MeshBinding binding;
            if (!TryGetMeshBinding(document, meshBindingIndex, modifierPath, diagnostics, out binding))
            {
                return;
            }

            var found = false;
            for (var i = 0; i < binding.Materials.Count; i++)
            {
                var material = binding.Materials[i];
                if (material != null && material.Slot == materialSlot)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                AddError(diagnostics, modifierPath + ".materialSlot", "対象meshBindingにmaterialSlotが存在しません。");
            }
        }

        private static void ValidateAccessor(
            VlatDocument document,
            VlatAccessor accessor,
            string expectedComponentType,
            string path,
            List<VlatDiagnostic> diagnostics)
        {
            if (accessor == null)
            {
                AddError(diagnostics, path, "accessorは必須です。");
                return;
            }

            if (accessor.BufferView < 0 || accessor.BufferView >= document.BufferViews.Count)
            {
                AddError(diagnostics, path + ".bufferView", "参照先bufferViewが範囲外です。");
            }

            if (!string.Equals(accessor.ComponentType, expectedComponentType, StringComparison.Ordinal))
            {
                AddError(
                    diagnostics,
                    path + ".componentType",
                    "componentTypeは" + expectedComponentType + "である必要があります。");
            }
        }

        private static void ValidateVertexDeltaBinary(
            VlatDocument document,
            IReadOnlyList<byte[]> binaryBuffers,
            VertexDeltaModifier modifier,
            MeshBinding meshBinding,
            string path,
            List<VlatDiagnostic> diagnostics)
        {
            ArraySegment<byte> indices;
            ArraySegment<byte> positions;
            if (!TryGetViewData(document, binaryBuffers, modifier.Indices, out indices) ||
                !TryGetViewData(document, binaryBuffers, modifier.Positions, out positions))
            {
                return;
            }

            if (indices.Count % 4 != 0)
            {
                AddError(diagnostics, path + ".indices", "UINT32 index列のbyteLengthは4の倍数である必要があります。");
                return;
            }

            if (positions.Count % 12 != 0)
            {
                AddError(diagnostics, path + ".positions", "FLOAT32 vec3列のbyteLengthは12の倍数である必要があります。");
                return;
            }

            var indexCount = indices.Count / 4;
            var positionCount = positions.Count / 12;
            if (indexCount != positionCount)
            {
                AddError(diagnostics, path, "indicesとpositionsの要素数が一致しません。");
            }

            if (modifier.Normals != null)
            {
                ArraySegment<byte> normals;
                if (TryGetViewData(document, binaryBuffers, modifier.Normals, out normals))
                {
                    if (normals.Count % 12 != 0)
                    {
                        AddError(diagnostics, path + ".normals", "FLOAT32 vec3列のbyteLengthは12の倍数である必要があります。");
                    }
                    else if (normals.Count / 12 != indexCount)
                    {
                        AddError(diagnostics, path + ".normals", "normalsとindicesの要素数が一致しません。");
                    }
                }
            }

            var bytes = indices.Array;
            if (bytes == null)
            {
                return;
            }

            var offset = indices.Offset;
            var seenIndices = new HashSet<uint>();
            for (var i = 0; i < indexCount; i++)
            {
                var vertexIndex = ReadUInt32LittleEndian(bytes, offset + (i * 4));
                if (vertexIndex >= (uint)meshBinding.VertexCount)
                {
                    AddError(
                        diagnostics,
                        path + ".indices",
                        "頂点indexがvertexCountの範囲外です（要素" + Invariant(i) + "）。");
                    break;
                }

                if (!seenIndices.Add(vertexIndex))
                {
                    AddError(
                        diagnostics,
                        path + ".indices",
                        "頂点indexが重複しています（要素" + Invariant(i) + "）。");
                    break;
                }
            }

            ValidateFiniteVectorData(positions, path + ".positions", diagnostics);
            if (modifier.Normals != null)
            {
                ArraySegment<byte> normalData;
                if (TryGetViewData(document, binaryBuffers, modifier.Normals, out normalData))
                {
                    ValidateFiniteVectorData(normalData, path + ".normals", diagnostics);
                }
            }
        }

        private static bool TryGetViewData(
            VlatDocument document,
            IReadOnlyList<byte[]> binaryBuffers,
            VlatAccessor accessor,
            out ArraySegment<byte> data)
        {
            data = default;
            if (accessor == null || accessor.BufferView < 0 || accessor.BufferView >= document.BufferViews.Count)
            {
                return false;
            }

            var view = document.BufferViews[accessor.BufferView];
            if (view == null || view.Buffer < 0 || view.Buffer >= binaryBuffers.Count)
            {
                return false;
            }

            var buffer = binaryBuffers[view.Buffer];
            if (buffer == null || view.ByteOffset < 0 || view.ByteLength < 0 ||
                view.ByteOffset > buffer.LongLength || view.ByteLength > buffer.LongLength - view.ByteOffset ||
                view.ByteOffset > int.MaxValue || view.ByteLength > int.MaxValue)
            {
                return false;
            }

            data = new ArraySegment<byte>(buffer, (int)view.ByteOffset, (int)view.ByteLength);
            return true;
        }

        private static uint ReadUInt32LittleEndian(byte[] data, int offset)
        {
            return (uint)(data[offset] |
                          (data[offset + 1] << 8) |
                          (data[offset + 2] << 16) |
                          (data[offset + 3] << 24));
        }

        private static void ValidateFiniteVectorData(
            ArraySegment<byte> data,
            string path,
            List<VlatDiagnostic> diagnostics)
        {
            if (data.Array == null || data.Count % 12 != 0)
            {
                return;
            }

            var vectorCount = data.Count / 12;
            for (var vectorIndex = 0; vectorIndex < vectorCount; vectorIndex++)
            {
                var vectorOffset = data.Offset + (vectorIndex * 12);
                for (var componentIndex = 0; componentIndex < 3; componentIndex++)
                {
                    var value = ReadSingleLittleEndian(data.Array, vectorOffset + (componentIndex * 4));
                    if (float.IsNaN(value) || float.IsInfinity(value))
                    {
                        AddError(
                            diagnostics,
                            path,
                            "FLOAT32 vec3に非有限値があります（要素" + Invariant(vectorIndex) + "）。");
                        return;
                    }
                }
            }
        }

        private static float ReadSingleLittleEndian(byte[] data, int offset)
        {
            var bits = data[offset] |
                       (data[offset + 1] << 8) |
                       (data[offset + 2] << 16) |
                       (data[offset + 3] << 24);
            return BitConverter.Int32BitsToSingle(bits);
        }

        private static void ValidateMaterialProperty(
            VlatDocument document,
            MaterialPropertyModifier modifier,
            string path,
            List<VlatDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(modifier.Property))
            {
                AddError(diagnostics, path + ".property", "propertyは空にできません。");
            }

            if (modifier.BandValue == null)
            {
                AddError(diagnostics, path + ".bandValue", "bandValueは必須です。");
                return;
            }

            switch (modifier.ValueType)
            {
                case "float":
                    if (!modifier.BandValue.IsScalar || !IsFinite(modifier.BandValue.Scalar))
                    {
                        AddError(diagnostics, path + ".bandValue", "floatのbandValueは有限のnumberである必要があります。");
                    }

                    break;

                case "color":
                case "vector":
                    if (modifier.BandValue.IsScalar || modifier.BandValue.ComponentCount != 4)
                    {
                        AddError(diagnostics, path + ".bandValue", "color/vectorのbandValueは4成分のnumber配列である必要があります。");
                    }
                    else
                    {
                        for (var i = 0; i < modifier.BandValue.ComponentCount; i++)
                        {
                            if (!IsFinite(modifier.BandValue.GetComponent(i)))
                            {
                                AddError(diagnostics, path + ".bandValue[" + Invariant(i) + "]", "成分値は有限である必要があります。");
                            }
                        }
                    }

                    break;

                case "textureIndex":
                    if (!modifier.BandValue.IsScalar ||
                        !IsFinite(modifier.BandValue.Scalar) ||
                        modifier.BandValue.Scalar < 0d ||
                        modifier.BandValue.Scalar > int.MaxValue ||
                        Math.Truncate(modifier.BandValue.Scalar) != modifier.BandValue.Scalar)
                    {
                        AddError(diagnostics, path + ".bandValue", "textureIndexは0以上のintegerである必要があります。");
                    }
                    else if ((int)modifier.BandValue.Scalar >= document.Textures.Count)
                    {
                        AddError(diagnostics, path + ".bandValue", "参照先textureが範囲外です。");
                    }

                    if (modifier.Interpolate)
                    {
                        AddError(diagnostics, path + ".interpolate", "textureIndexは補間できないためinterpolateをfalseにする必要があります。");
                    }

                    break;

                default:
                    AddError(diagnostics, path + ".valueType", "未対応のmaterial valueTypeです。");
                    break;
            }
        }

        private static void ValidateNonNegativeFinite(
            double value,
            string path,
            string name,
            List<VlatDiagnostic> diagnostics)
        {
            if (!IsFinite(value) || value < 0d)
            {
                AddError(diagnostics, path, name + "は0以上の有限値である必要があります。");
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        internal static bool IsRelativeExternalUri(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            try
            {
                Uri absoluteUri;
                if (Uri.TryCreate(value, UriKind.Absolute, out absoluteUri) ||
                    IsPlatformIndependentRootedPath(value) ||
                    Path.IsPathRooted(value))
                {
                    return false;
                }

                var decoded = Uri.UnescapeDataString(value);
                return !IsPlatformIndependentRootedPath(decoded) && !Path.IsPathRooted(decoded);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsPlatformIndependentRootedPath(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            if (value[0] == '/' || value[0] == '\\')
            {
                return true;
            }

            if (value.Length < 2 || value[1] != ':')
            {
                return false;
            }

            var drive = value[0];
            return (drive >= 'A' && drive <= 'Z') || (drive >= 'a' && drive <= 'z');
        }

        private static bool IsValidPositionHash(string value)
        {
            const string prefix = "xxh64:";
            if (value == null || value.Length != prefix.Length + 16 ||
                !value.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            for (var i = prefix.Length; i < value.Length; i++)
            {
                var character = value[i];
                var isDigit = character >= '0' && character <= '9';
                var isLowerHex = character >= 'a' && character <= 'f';
                var isUpperHex = character >= 'A' && character <= 'F';
                if (!isDigit && !isLowerHex && !isUpperHex)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsValidImageMimeType(string value)
        {
            const string prefix = "image/";
            if (value.Length <= prefix.Length || !value.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            var first = value[prefix.Length];
            var firstIsLetter = (first >= 'a' && first <= 'z') || (first >= 'A' && first <= 'Z');
            var firstIsDigit = first >= '0' && first <= '9';
            if (!firstIsLetter && !firstIsDigit)
            {
                return false;
            }

            for (var i = prefix.Length; i < value.Length; i++)
            {
                var character = value[i];
                var isLetter = (character >= 'a' && character <= 'z') ||
                               (character >= 'A' && character <= 'Z');
                var isDigit = character >= '0' && character <= '9';
                if (!isLetter && !isDigit && character != '.' && character != '+' && character != '-')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Contains(string[] values, string target)
        {
            for (var i = 0; i < values.Length; i++)
            {
                if (string.Equals(values[i], target, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string Invariant(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static void AddError(List<VlatDiagnostic> diagnostics, string path, string message)
        {
            diagnostics.Add(new VlatDiagnostic(VlatDiagnosticSeverity.Error, path, 0, 0, message));
        }

        private static void AddWarning(List<VlatDiagnostic> diagnostics, string path, string message)
        {
            diagnostics.Add(new VlatDiagnostic(VlatDiagnosticSeverity.Warning, path, 0, 0, message));
        }

        private readonly struct SemanticVersion
        {
            public SemanticVersion(string major, string minor, string patch)
            {
                Major = major;
                Minor = minor;
                Patch = patch;
            }

            public string Major { get; }

            public string Minor { get; }

            public string Patch { get; }

            public static int CompareNumeric(string left, string right)
            {
                if (left.Length != right.Length)
                {
                    return left.Length < right.Length ? -1 : 1;
                }

                return string.CompareOrdinal(left, right);
            }

            public static bool TryParse(string value, out SemanticVersion version)
            {
                version = default;
                if (string.IsNullOrEmpty(value))
                {
                    return false;
                }

                var plus = value.IndexOf('+');
                if (plus >= 0 && value.IndexOf('+', plus + 1) >= 0)
                {
                    return false;
                }

                var firstDash = value.IndexOf('-');
                var prerelease = firstDash >= 0 && (plus < 0 || firstDash < plus)
                    ? firstDash
                    : -1;

                var coreEnd = prerelease >= 0 ? prerelease : (plus >= 0 ? plus : value.Length);
                if (coreEnd <= 0)
                {
                    return false;
                }

                if (prerelease >= 0)
                {
                    var prereleaseEnd = plus >= 0 ? plus : value.Length;
                    if (!ValidateIdentifierSequence(
                            value,
                            prerelease + 1,
                            prereleaseEnd - prerelease - 1,
                            true))
                    {
                        return false;
                    }
                }

                if (plus >= 0 && !ValidateIdentifierSequence(value, plus + 1, value.Length - plus - 1, false))
                {
                    return false;
                }

                var firstDot = value.IndexOf('.', 0, coreEnd);
                if (firstDot <= 0)
                {
                    return false;
                }

                var secondDot = value.IndexOf('.', firstDot + 1, coreEnd - firstDot - 1);
                if (secondDot <= firstDot + 1 || secondDot >= coreEnd - 1)
                {
                    return false;
                }

                if (value.IndexOf('.', secondDot + 1, coreEnd - secondDot - 1) >= 0)
                {
                    return false;
                }

                string major;
                string minor;
                string patch;
                if (!TryParseCoreNumber(value, 0, firstDot, out major) ||
                    !TryParseCoreNumber(value, firstDot + 1, secondDot - firstDot - 1, out minor) ||
                    !TryParseCoreNumber(value, secondDot + 1, coreEnd - secondDot - 1, out patch))
                {
                    return false;
                }

                version = new SemanticVersion(major, minor, patch);
                return true;
            }

            private static bool ValidateIdentifierSequence(
                string value,
                int start,
                int length,
                bool rejectNumericLeadingZeros)
            {
                if (length <= 0)
                {
                    return false;
                }

                var identifierLength = 0;
                var identifierStart = start;
                var allDigits = true;
                for (var i = start; i < start + length; i++)
                {
                    var character = value[i];
                    if (character == '.')
                    {
                        if (identifierLength == 0)
                        {
                            return false;
                        }

                        if (rejectNumericLeadingZeros && allDigits && identifierLength > 1 && value[identifierStart] == '0')
                        {
                            return false;
                        }

                        identifierLength = 0;
                        identifierStart = i + 1;
                        allDigits = true;
                        continue;
                    }

                    var isLetter = (character >= 'a' && character <= 'z') ||
                                   (character >= 'A' && character <= 'Z');
                    var isDigit = character >= '0' && character <= '9';
                    if (!isLetter && !isDigit && character != '-')
                    {
                        return false;
                    }

                    if (!isDigit)
                    {
                        allDigits = false;
                    }

                    identifierLength++;
                }

                if (identifierLength == 0)
                {
                    return false;
                }

                return !rejectNumericLeadingZeros || !allDigits || identifierLength == 1 || value[identifierStart] != '0';
            }

            private static bool TryParseCoreNumber(string value, int start, int length, out string number)
            {
                number = null;
                if (length <= 0 || (length > 1 && value[start] == '0'))
                {
                    return false;
                }

                for (var i = start; i < start + length; i++)
                {
                    var character = value[i];
                    if (character < '0' || character > '9')
                    {
                        return false;
                    }
                }

                number = value.Substring(start, length);
                return true;
            }
        }
    }
}
