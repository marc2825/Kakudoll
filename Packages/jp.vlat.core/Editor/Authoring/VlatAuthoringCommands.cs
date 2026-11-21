using System;
using System.Collections.Generic;
using UnityEditor;

namespace VLat.Editor.Authoring
{
    internal static partial class VlatAuthoringCommands
    {
        internal static void SetMetadata(
            VlatAuthoringDocument document,
            string version,
            string generator,
            string avatarName,
            string vrmVersion)
        {
            RequireDocument(document);
            Record(document, "Edit VLat Metadata");
            document.Metadata.Version = version;
            document.Metadata.Generator = generator;
            document.Metadata.AvatarName = avatarName;
            document.Metadata.VrmVersion = vrmVersion;
            Finish(document);
        }

        internal static void SetGenerator(VlatAuthoringDocument document, string value)
        {
            RequireDocument(document);
            Record(document, "Edit VLat Generator");
            document.Metadata.Generator = value;
            Finish(document);
        }

        internal static void SetBand(
            VlatAuthoringDocument document,
            int bandIndex,
            string id,
            string name,
            double yawMin,
            double yawMax,
            double pitchMin,
            double pitchMax,
            double featherYaw,
            double featherPitch,
            double hysteresis)
        {
            var band = GetBand(document, bandIndex);
            Record(document, "Edit VLat Band");
            band.Id = id;
            band.Name = name;
            band.YawMin = yawMin;
            band.YawMax = yawMax;
            band.PitchMin = pitchMin;
            band.PitchMax = pitchMax;
            band.FeatherYaw = featherYaw;
            band.FeatherPitch = featherPitch;
            band.Hysteresis = hysteresis;
            Finish(document);
        }

        internal static void SetLayer(
            VlatAuthoringDocument document,
            int layerIndex,
            string id,
            string name,
            string tag,
            bool enabled,
            int priority,
            string bandId)
        {
            var layer = GetLayer(document, layerIndex);
            Record(document, "Edit VLat Layer");
            layer.Id = id;
            layer.Name = name;
            layer.Tag = tag;
            layer.Enabled = enabled;
            layer.Priority = priority;
            layer.BandId = bandId;
            Finish(document);
        }

        internal static void SetLayerName(VlatAuthoringDocument document, int layerIndex, string value)
        {
            var layer = GetLayer(document, layerIndex);
            Record(document, "Edit VLat Layer");
            layer.Name = value;
            Finish(document);
        }

        internal static void AddLayer(VlatAuthoringDocument document, VlatAuthoringLayer layer)
        {
            RequireDocument(document);
            if (layer == null)
            {
                throw new ArgumentNullException(nameof(layer));
            }

            _ = layer.EditorIdentity;
            Record(document, "Add VLat Layer");
            document.Layers.Add(layer);
            Finish(document);
        }

        internal static void RemoveLayer(VlatAuthoringDocument document, int layerIndex)
        {
            GetLayer(document, layerIndex);
            Record(document, "Remove VLat Layer");
            document.Layers.RemoveAt(layerIndex);
            Finish(document);
        }

        internal static void MoveLayer(VlatAuthoringDocument document, int fromIndex, int toIndex)
        {
            RequireDocument(document);
            Move(document, document.Layers, fromIndex, toIndex, "Reorder VLat Layer");
        }

        internal static void AddBand(VlatAuthoringDocument document, VlatAuthoringBand band)
        {
            RequireDocument(document);
            if (band == null)
            {
                throw new ArgumentNullException(nameof(band));
            }

            _ = band.EditorIdentity;
            Record(document, "Add VLat Band");
            document.Bands.Add(band);
            Finish(document);
        }

        internal static void RemoveBand(VlatAuthoringDocument document, int bandIndex)
        {
            GetBand(document, bandIndex);
            Record(document, "Remove VLat Band");
            document.Bands.RemoveAt(bandIndex);
            Finish(document);
        }

        internal static void MoveBand(VlatAuthoringDocument document, int fromIndex, int toIndex)
        {
            RequireDocument(document);
            Move(document, document.Bands, fromIndex, toIndex, "Reorder VLat Band");
        }

        internal static void AddModifier(
            VlatAuthoringDocument document,
            int layerIndex,
            VlatAuthoringModifier modifier)
        {
            var layer = GetLayer(document, layerIndex);
            if (modifier == null)
            {
                throw new ArgumentNullException(nameof(modifier));
            }

            _ = modifier.EditorIdentity;
            Record(document, "Add VLat Modifier");
            layer.Modifiers.Add(modifier);
            Finish(document);
        }

        internal static void RemoveModifier(
            VlatAuthoringDocument document,
            int layerIndex,
            int modifierIndex)
        {
            var layer = GetLayer(document, layerIndex);
            GetModifier(layer, modifierIndex);
            Record(document, "Remove VLat Modifier");
            layer.Modifiers.RemoveAt(modifierIndex);
            Finish(document);
        }

        internal static void MoveModifier(
            VlatAuthoringDocument document,
            int layerIndex,
            int fromIndex,
            int toIndex)
        {
            var layer = GetLayer(document, layerIndex);
            Move(document, layer.Modifiers, fromIndex, toIndex, "Reorder VLat Modifier");
        }

        internal static void SetVertexDeltaModifier(
            VlatAuthoringDocument document,
            int layerIndex,
            int modifierIndex,
            int meshBinding,
            bool hasNormals)
        {
            var modifier = GetModifier<VlatAuthoringVertexDeltaModifier>(document, layerIndex, modifierIndex);
            Record(document, "Edit VLat Vertex Delta Modifier");
            modifier.MeshBinding = meshBinding;
            modifier.HasNormals = hasNormals;
            Finish(document);
        }

        internal static void SetVisibilityModifier(
            VlatAuthoringDocument document,
            int layerIndex,
            int modifierIndex,
            int meshBinding,
            int subMesh,
            bool visibleInBand)
        {
            var modifier = GetModifier<VlatAuthoringVisibilityModifier>(document, layerIndex, modifierIndex);
            Record(document, "Edit VLat Visibility Modifier");
            modifier.MeshBinding = meshBinding;
            modifier.SubMesh = subMesh;
            modifier.VisibleInBand = visibleInBand;
            Finish(document);
        }

        internal static void SetOpacityModifier(
            VlatAuthoringDocument document,
            int layerIndex,
            int modifierIndex,
            int meshBinding,
            int materialSlot,
            double value)
        {
            var modifier = GetModifier<VlatAuthoringOpacityModifier>(document, layerIndex, modifierIndex);
            Record(document, "Edit VLat Opacity Modifier");
            modifier.MeshBinding = meshBinding;
            modifier.MaterialSlot = materialSlot;
            modifier.Value = value;
            Finish(document);
        }

        internal static void SetMaterialPropertyModifier(
            VlatAuthoringDocument document,
            int layerIndex,
            int modifierIndex,
            int meshBinding,
            int materialSlot,
            string property,
            string valueType,
            bool isScalar,
            double scalar,
            IReadOnlyList<double> components,
            bool interpolate)
        {
            var modifier = GetModifier<VlatAuthoringMaterialPropertyModifier>(
                document,
                layerIndex,
                modifierIndex);
            Record(document, "Edit VLat Material Property Modifier");
            modifier.MeshBinding = meshBinding;
            modifier.MaterialSlot = materialSlot;
            modifier.Property = property;
            modifier.ValueType = valueType;
            modifier.IsScalar = isScalar;
            modifier.Scalar = scalar;
            modifier.Components.Clear();
            if (components != null)
            {
                for (var index = 0; index < components.Count; index++)
                {
                    modifier.Components.Add(components[index]);
                }
            }

            modifier.Interpolate = interpolate;
            Finish(document);
        }

        internal static void SetRenderQueueModifier(
            VlatAuthoringDocument document,
            int layerIndex,
            int modifierIndex,
            int meshBinding,
            int materialSlot,
            int offset)
        {
            var modifier = GetModifier<VlatAuthoringRenderQueueModifier>(document, layerIndex, modifierIndex);
            Record(document, "Edit VLat Render Queue Modifier");
            modifier.MeshBinding = meshBinding;
            modifier.MaterialSlot = materialSlot;
            modifier.Offset = offset;
            Finish(document);
        }

        internal static void SetVertexDelta(
            VlatAuthoringDocument document,
            int layerIndex,
            int modifierIndex,
            int deltaIndex,
            VlatAuthoringVertexDelta value)
        {
            var modifier = GetModifier<VlatAuthoringVertexDeltaModifier>(document, layerIndex, modifierIndex);
            if (deltaIndex < 0 || deltaIndex >= modifier.Deltas.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaIndex));
            }

            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            Record(document, "Edit VLat Vertex Delta");
            modifier.Deltas[deltaIndex] = value.Copy();
            Finish(document);
        }

        internal static void AddVertexDelta(
            VlatAuthoringDocument document,
            int layerIndex,
            int modifierIndex,
            VlatAuthoringVertexDelta value)
        {
            var modifier = GetModifier<VlatAuthoringVertexDeltaModifier>(document, layerIndex, modifierIndex);
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            Record(document, "Add VLat Vertex Delta");
            modifier.Deltas.Add(value.Copy());
            Finish(document);
        }

        internal static void RemoveVertexDelta(
            VlatAuthoringDocument document,
            int layerIndex,
            int modifierIndex,
            int deltaIndex)
        {
            var modifier = GetModifier<VlatAuthoringVertexDeltaModifier>(document, layerIndex, modifierIndex);
            if (deltaIndex < 0 || deltaIndex >= modifier.Deltas.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaIndex));
            }

            Record(document, "Remove VLat Vertex Delta");
            modifier.Deltas.RemoveAt(deltaIndex);
            Finish(document);
        }

        private static VlatAuthoringBand GetBand(VlatAuthoringDocument document, int bandIndex)
        {
            RequireDocument(document);
            if (bandIndex < 0 || bandIndex >= document.Bands.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(bandIndex));
            }

            return document.Bands[bandIndex];
        }

        private static VlatAuthoringLayer GetLayer(VlatAuthoringDocument document, int layerIndex)
        {
            RequireDocument(document);
            if (layerIndex < 0 || layerIndex >= document.Layers.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(layerIndex));
            }

            return document.Layers[layerIndex];
        }

        private static VlatAuthoringModifier GetModifier(VlatAuthoringLayer layer, int modifierIndex)
        {
            if (modifierIndex < 0 || modifierIndex >= layer.Modifiers.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(modifierIndex));
            }

            return layer.Modifiers[modifierIndex];
        }

        private static TModifier GetModifier<TModifier>(
            VlatAuthoringDocument document,
            int layerIndex,
            int modifierIndex)
            where TModifier : VlatAuthoringModifier
        {
            var layer = GetLayer(document, layerIndex);
            var modifier = GetModifier(layer, modifierIndex) as TModifier;
            if (modifier == null)
            {
                throw new ArgumentException(
                    "指定されたModifier型が一致しません。",
                    nameof(modifierIndex));
            }

            return modifier;
        }

        private static void Move<T>(
            VlatAuthoringDocument document,
            List<T> items,
            int fromIndex,
            int toIndex,
            string undoName)
        {
            if (fromIndex < 0 || fromIndex >= items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(fromIndex));
            }

            if (toIndex < 0 || toIndex >= items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(toIndex));
            }

            if (fromIndex == toIndex)
            {
                return;
            }

            Record(document, undoName);
            var item = items[fromIndex];
            items.RemoveAt(fromIndex);
            items.Insert(toIndex, item);
            Finish(document);
        }

        private static void RequireDocument(VlatAuthoringDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }
        }

        private static void Record(VlatAuthoringDocument document, string name, bool complete = false)
        {
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
            if (complete) Undo.RegisterCompleteObjectUndo(document, name);
            else Undo.RecordObject(document, name);
        }

        private static void Finish(VlatAuthoringDocument document)
        {
            document.AdvanceRevision();
            document.MarkDirty();
            EditorUtility.SetDirty(document);
            Undo.FlushUndoRecordObjects();
            // Undo記録の直列化は完了済み。pack済み差分文字列を常駐させない（次の直列化で再生成）。
            document.ReleaseSerializationCaches();
        }
    }
}
