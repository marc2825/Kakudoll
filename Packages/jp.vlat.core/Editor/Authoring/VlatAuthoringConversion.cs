using System;
using System.Collections.Generic;
using UnityEngine;
using VLat.Core.Model;
using VLat.Core.Validation;

namespace VLat.Editor.Authoring
{
    internal static class VlatAuthoringConversion
    {
        internal static VlatAuthoringLoadResult FromImmutable(
            VlatDocument source,
            IReadOnlyList<byte[]> buffers,
            string sourcePath = null)
        {
            var diagnostics = new List<VlatDiagnostic>(VlatValidator.Validate(source, buffers));
            if (VlatAuthoringResultUtility.ContainsError(diagnostics))
            {
                return new VlatAuthoringLoadResult(null, diagnostics);
            }

            VlatAuthoringDocument destination = null;
            try
            {
                destination = VlatAuthoringDocument.CreateTemporary();
                destination.SetSourcePath(sourcePath);
                CopyMetadata(source.Metadata, destination.Metadata);
                CopyTextures(source, buffers, destination.Textures);
                CopyBands(source.AngleBands, destination.Bands);
                CopyLayers(source, buffers, destination.Layers);
                for (var g = 0; g < source.KeyformGroups.Count; g++)
                {
                    var original = source.KeyformGroups[g];
                    var group = new VlatAuthoringKeyformGroup
                    {
                        Id = original.Id, MeshBinding = original.MeshBinding,
                        TopologyHash = original.TopologyHash, CoordinateSpace = original.CoordinateSpace,
                        Interpolation = original.Interpolation, SupportStart = original.SupportStart,
                        SupportEnd = original.SupportEnd,
                    };
                    for (var k = 0; k < original.Keys.Count; k++)
                    {
                        var key = new VlatAuthoringKeyform { Yaw = original.Keys[k].Yaw };
                        if (original.Keys[k].Delta != null)
                            key.Deltas.AddRange(CopyVertexDelta(source, buffers, original.Keys[k].Delta).Deltas);
                        group.Keys.Add(key);
                    }
                    destination.KeyformGroups.Add(group);
                }
                return new VlatAuthoringLoadResult(destination, diagnostics);
            }
            catch (Exception exception)
            {
                if (destination != null)
                {
                    UnityEngine.Object.DestroyImmediate(destination);
                }

                diagnostics.Add(VlatAuthoringResultUtility.Error(
                    "$",
                    "Authoring Modelへ変換できません: " + exception.Message));
                return new VlatAuthoringLoadResult(null, diagnostics);
            }
        }

        private static void CopyMetadata(VlatMetadata source, VlatAuthoringMetadata destination)
        {
            destination.Version = source.Version;
            destination.Generator = source.Generator;
            destination.AvatarName = source.TargetAvatar.Name;
            destination.VrmVersion = source.TargetAvatar.VrmVersion;
            for (var bindingIndex = 0; bindingIndex < source.TargetAvatar.MeshBindings.Count; bindingIndex++)
            {
                var sourceBinding = source.TargetAvatar.MeshBindings[bindingIndex];
                var destinationBinding = new VlatAuthoringMeshBinding
                {
                    HierarchyPath = sourceBinding.HierarchyPath,
                    MeshName = sourceBinding.MeshName,
                    VertexCount = sourceBinding.VertexCount,
                    PositionHash = sourceBinding.PositionHash,
                };
                for (var materialIndex = 0; materialIndex < sourceBinding.Materials.Count; materialIndex++)
                {
                    var sourceMaterial = sourceBinding.Materials[materialIndex];
                    destinationBinding.Materials.Add(new VlatAuthoringMaterialSlot
                    {
                        Slot = sourceMaterial.Slot,
                        MaterialName = sourceMaterial.MaterialName,
                        Shader = sourceMaterial.Shader,
                    });
                }

                destination.MeshBindings.Add(destinationBinding);
            }
        }

        private static void CopyTextures(
            VlatDocument source,
            IReadOnlyList<byte[]> buffers,
            List<VlatAuthoringTexture> destination)
        {
            for (var textureIndex = 0; textureIndex < source.Textures.Count; textureIndex++)
            {
                var texture = source.Textures[textureIndex];
                var authoring = new VlatAuthoringTexture
                {
                    Name = texture.Name,
                    IsEmbedded = texture.BufferView.HasValue,
                    Uri = texture.Uri,
                    MimeType = texture.MimeType,
                };
                if (texture.BufferView.HasValue)
                {
                    authoring.EmbeddedBytes = ReadView(source, buffers, texture.BufferView.Value);
                }

                destination.Add(authoring);
            }
        }

        private static void CopyBands(
            IReadOnlyList<AngleBand> source,
            List<VlatAuthoringBand> destination)
        {
            for (var index = 0; index < source.Count; index++)
            {
                var band = source[index];
                destination.Add(new VlatAuthoringBand
                {
                    Id = band.Id,
                    Name = band.Name,
                    YawMin = band.Yaw.Min,
                    YawMax = band.Yaw.Max,
                    PitchMin = band.Pitch.Min,
                    PitchMax = band.Pitch.Max,
                    FeatherYaw = band.Feather.Yaw,
                    FeatherPitch = band.Feather.Pitch,
                    Hysteresis = band.Hysteresis,
                });
            }
        }

        private static void CopyLayers(
            VlatDocument source,
            IReadOnlyList<byte[]> buffers,
            List<VlatAuthoringLayer> destination)
        {
            for (var layerIndex = 0; layerIndex < source.Layers.Count; layerIndex++)
            {
                var layer = source.Layers[layerIndex];
                var authoring = new VlatAuthoringLayer
                {
                    Id = layer.Id,
                    Name = layer.Name,
                    Tag = layer.Tag,
                    Enabled = layer.Enabled,
                    Priority = layer.Priority,
                    BandId = layer.BandId,
                };
                for (var modifierIndex = 0; modifierIndex < layer.Modifiers.Count; modifierIndex++)
                {
                    authoring.Modifiers.Add(CopyModifier(source, buffers, layer.Modifiers[modifierIndex]));
                }

                destination.Add(authoring);
            }
        }

        private static VlatAuthoringModifier CopyModifier(
            VlatDocument document,
            IReadOnlyList<byte[]> buffers,
            VlatModifier source)
        {
            var vertex = source as VertexDeltaModifier;
            if (vertex != null)
            {
                return CopyVertexDelta(document, buffers, vertex);
            }

            var visibility = source as VisibilityModifier;
            if (visibility != null)
            {
                return new VlatAuthoringVisibilityModifier
                {
                    MeshBinding = visibility.MeshBinding,
                    SubMesh = visibility.SubMesh,
                    VisibleInBand = visibility.VisibleInBand,
                };
            }

            var opacity = source as OpacityModifier;
            if (opacity != null)
            {
                return new VlatAuthoringOpacityModifier
                {
                    MeshBinding = opacity.MeshBinding,
                    MaterialSlot = opacity.MaterialSlot,
                    Value = opacity.Value,
                };
            }

            var material = source as MaterialPropertyModifier;
            if (material != null)
            {
                var result = new VlatAuthoringMaterialPropertyModifier
                {
                    MeshBinding = material.MeshBinding,
                    MaterialSlot = material.MaterialSlot,
                    Property = material.Property,
                    ValueType = material.ValueType,
                    IsScalar = material.BandValue.IsScalar,
                    Scalar = material.BandValue.Scalar,
                    Interpolate = material.Interpolate,
                };
                if (!material.BandValue.IsScalar)
                {
                    for (var index = 0; index < material.BandValue.ComponentCount; index++)
                    {
                        result.Components.Add(material.BandValue.GetComponent(index));
                    }
                }

                return result;
            }

            var queue = source as RenderQueueModifier;
            if (queue != null)
            {
                return new VlatAuthoringRenderQueueModifier
                {
                    MeshBinding = queue.MeshBinding,
                    MaterialSlot = queue.MaterialSlot,
                    Offset = queue.Offset,
                };
            }

            throw new InvalidOperationException("未対応のModifier型です: " + source.GetType().FullName);
        }

        internal static VlatAuthoringVertexDeltaModifier CopyVertexDelta(
            VlatDocument document,
            IReadOnlyList<byte[]> buffers,
            VertexDeltaModifier source)
        {
            var indices = ReadView(document, buffers, source.Indices.BufferView);
            var positions = ReadView(document, buffers, source.Positions.BufferView);
            var normals = source.Normals == null
                ? null
                : ReadView(document, buffers, source.Normals.BufferView);
            var count = indices.Length / 4;
            var result = new VlatAuthoringVertexDeltaModifier
            {
                MeshBinding = source.MeshBinding,
                HasNormals = normals != null,
            };
            for (var index = 0; index < count; index++)
            {
                var positionOffset = index * 12;
                var delta = new VlatAuthoringVertexDelta
                {
                    VertexIndex = checked((int)ReadUInt32(indices, index * 4)),
                    PositionDelta = ReadVector3(positions, positionOffset),
                };
                if (normals != null)
                {
                    delta.NormalDelta = ReadVector3(normals, positionOffset);
                }

                result.Deltas.Add(delta);
            }

            return result;
        }

        private static byte[] ReadView(
            VlatDocument document,
            IReadOnlyList<byte[]> buffers,
            int viewIndex)
        {
            var view = document.BufferViews[viewIndex];
            var source = buffers[view.Buffer];
            var length = checked((int)view.ByteLength);
            var result = new byte[length];
            System.Buffer.BlockCopy(source, checked((int)view.ByteOffset), result, 0, length);
            return result;
        }

        private static uint ReadUInt32(byte[] source, int offset)
        {
            return source[offset]
                | ((uint)source[offset + 1] << 8)
                | ((uint)source[offset + 2] << 16)
                | ((uint)source[offset + 3] << 24);
        }

        private static Vector3 ReadVector3(byte[] source, int offset)
        {
            return new Vector3(
                ReadSingle(source, offset),
                ReadSingle(source, offset + 4),
                ReadSingle(source, offset + 8));
        }

        private static float ReadSingle(byte[] source, int offset)
        {
            var bits = source[offset]
                | (source[offset + 1] << 8)
                | (source[offset + 2] << 16)
                | (source[offset + 3] << 24);
            return BitConverter.Int32BitsToSingle(bits);
        }
    }
}
