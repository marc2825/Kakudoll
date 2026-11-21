using System;

namespace VLat.Editor.Authoring
{
    internal enum VlatEditorSelectionKind
    {
        Document,
        Band,
        Layer,
        Modifier,
        KeyformGroup,
        Keyform,
    }

    [Serializable]
    internal sealed class VlatEditorSelection
    {
        internal VlatEditorSelectionKind Kind { get; private set; } = VlatEditorSelectionKind.Document;
        internal string Identity { get; private set; } = string.Empty;
        internal string ParentIdentity { get; private set; } = string.Empty;

        internal void SelectDocument()
        {
            Set(VlatEditorSelectionKind.Document, string.Empty, string.Empty);
        }

        internal void SelectBand(VlatAuthoringBand band)
        {
            if (band == null)
            {
                SelectDocument();
                return;
            }

            Set(VlatEditorSelectionKind.Band, band.EditorIdentity, string.Empty);
        }

        internal void SelectLayer(VlatAuthoringLayer layer)
        {
            if (layer == null)
            {
                SelectDocument();
                return;
            }

            Set(VlatEditorSelectionKind.Layer, layer.EditorIdentity, string.Empty);
        }

        internal void SelectModifier(VlatAuthoringLayer layer, VlatAuthoringModifier modifier)
        {
            if (layer == null || modifier == null)
            {
                SelectDocument();
                return;
            }

            Set(VlatEditorSelectionKind.Modifier, modifier.EditorIdentity, layer.EditorIdentity);
        }

        internal void SelectKeyformGroup(VlatAuthoringKeyformGroup group)
        {
            if (group == null) { SelectDocument(); return; }
            Set(VlatEditorSelectionKind.KeyformGroup, group.EditorIdentity, string.Empty);
        }

        internal void SelectKeyform(VlatAuthoringKeyformGroup group, VlatAuthoringKeyform key)
        {
            if (group == null || key == null) { SelectDocument(); return; }
            Set(VlatEditorSelectionKind.Keyform, key.EditorIdentity, group.EditorIdentity);
        }

        internal int FindKeyformGroupIndex(VlatAuthoringDocument document)
        {
            if (document == null || (Kind != VlatEditorSelectionKind.KeyformGroup && Kind != VlatEditorSelectionKind.Keyform)) return -1;
            var identity = Kind == VlatEditorSelectionKind.Keyform ? ParentIdentity : Identity;
            for (var g = 0; g < document.KeyformGroups.Count; g++)
                if (document.KeyformGroups[g] != null && document.KeyformGroups[g].EditorIdentity == identity) return g;
            return -1;
        }

        internal int FindKeyformIndex(VlatAuthoringDocument document)
        {
            if (Kind != VlatEditorSelectionKind.Keyform) return -1;
            var g = FindKeyformGroupIndex(document);
            if (g < 0) return -1;
            var keys = document.KeyformGroups[g].Keys;
            for (var k = 0; k < keys.Count; k++)
                if (keys[k] != null && keys[k].EditorIdentity == Identity) return k;
            return -1;
        }

        internal void Restore(VlatEditorSelectionKind kind, string identity, string parentIdentity)
        {
            Set(kind, identity, parentIdentity);
        }

        internal int FindBandIndex(VlatAuthoringDocument document)
        {
            if (document == null || Kind != VlatEditorSelectionKind.Band)
            {
                return -1;
            }

            for (var index = 0; index < document.Bands.Count; index++)
            {
                var band = document.Bands[index];
                if (band != null && string.Equals(band.EditorIdentity, Identity, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        internal int FindLayerIndex(VlatAuthoringDocument document)
        {
            if (document == null)
            {
                return -1;
            }

            var identity = Kind == VlatEditorSelectionKind.Modifier ? ParentIdentity : Identity;
            if (Kind != VlatEditorSelectionKind.Layer && Kind != VlatEditorSelectionKind.Modifier)
            {
                return -1;
            }

            for (var index = 0; index < document.Layers.Count; index++)
            {
                var layer = document.Layers[index];
                if (layer != null && string.Equals(layer.EditorIdentity, identity, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        internal int FindModifierIndex(VlatAuthoringDocument document)
        {
            if (Kind != VlatEditorSelectionKind.Modifier)
            {
                return -1;
            }

            var layerIndex = FindLayerIndex(document);
            if (layerIndex < 0)
            {
                return -1;
            }

            var modifiers = document.Layers[layerIndex].Modifiers;
            for (var index = 0; index < modifiers.Count; index++)
            {
                var modifier = modifiers[index];
                if (modifier != null &&
                    string.Equals(modifier.EditorIdentity, Identity, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        internal void Reconcile(VlatAuthoringDocument document)
        {
            if (document == null || Kind == VlatEditorSelectionKind.Document)
            {
                SelectDocument();
                return;
            }

            if (Kind == VlatEditorSelectionKind.Band)
            {
                if (FindBandIndex(document) < 0)
                {
                    SelectDocument();
                }

                return;
            }

            if (Kind == VlatEditorSelectionKind.KeyformGroup || Kind == VlatEditorSelectionKind.Keyform)
            {
                var groupIndex = FindKeyformGroupIndex(document);
                if (groupIndex < 0) SelectDocument();
                else if (Kind == VlatEditorSelectionKind.Keyform && FindKeyformIndex(document) < 0)
                    SelectKeyformGroup(document.KeyformGroups[groupIndex]);
                return;
            }

            var layerIndex = FindLayerIndex(document);
            if (layerIndex < 0)
            {
                SelectDocument();
                return;
            }

            if (Kind == VlatEditorSelectionKind.Modifier && FindModifierIndex(document) < 0)
            {
                SelectLayer(document.Layers[layerIndex]);
            }
        }

        internal bool SelectDiagnosticPath(VlatAuthoringDocument document, string path)
        {
            if (document == null || string.IsNullOrEmpty(path))
            {
                return false;
            }

            var groupIndex = ReadIndex(path, "$.keyformGroups[");
            if (groupIndex >= 0 && groupIndex < document.KeyformGroups.Count)
            {
                var group = document.KeyformGroups[groupIndex];
                var keyIndex = ReadIndex(path, ".keys[");
                if (keyIndex >= 0 && keyIndex < group.Keys.Count) SelectKeyform(group, group.Keys[keyIndex]);
                else SelectKeyformGroup(group);
                return true;
            }

            var bandIndex = ReadIndex(path, "$.angleBands[");
            if (bandIndex >= 0 && bandIndex < document.Bands.Count)
            {
                SelectBand(document.Bands[bandIndex]);
                return true;
            }

            var layerIndex = ReadIndex(path, "$.layers[");
            if (layerIndex < 0 || layerIndex >= document.Layers.Count)
            {
                return false;
            }

            var layer = document.Layers[layerIndex];
            var modifierIndex = ReadIndex(path, ".modifiers[");
            if (modifierIndex >= 0 && modifierIndex < layer.Modifiers.Count)
            {
                SelectModifier(layer, layer.Modifiers[modifierIndex]);
            }
            else
            {
                SelectLayer(layer);
            }

            return true;
        }

        private static int ReadIndex(string path, string marker)
        {
            var markerIndex = path.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                return -1;
            }

            var start = markerIndex + marker.Length;
            var end = path.IndexOf(']', start);
            if (end <= start)
            {
                return -1;
            }

            int value;
            return int.TryParse(path.Substring(start, end - start), out value) ? value : -1;
        }

        private void Set(VlatEditorSelectionKind kind, string identity, string parentIdentity)
        {
            Kind = kind;
            Identity = identity ?? string.Empty;
            ParentIdentity = parentIdentity ?? string.Empty;
        }
    }
}
