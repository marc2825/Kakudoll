using System.Collections.Generic;
using VLat.Core.Validation;

namespace VLat.Editor.Authoring
{
    internal static partial class VlatAuthoringCommands
    {
        internal static void AddKeyformGroup(VlatAuthoringDocument document, VlatAuthoringKeyformGroup group)
        {
            Record(document, "Add Kakudoll Keyform Group");
            document.Metadata.Version = VlatValidator.KeyformVersion;
            document.KeyformGroups.Add(group);
            Finish(document);
        }

        internal static void RemoveKeyformGroup(VlatAuthoringDocument document, int index)
        {
            Record(document, "Remove Kakudoll Keyform Group");
            document.KeyformGroups.RemoveAt(index);
            if (document.KeyformGroups.Count == 0) document.Metadata.Version = VlatValidator.SupportedVersion;
            Finish(document);
        }

        internal static void SetKeyformGroup(VlatAuthoringDocument document, int groupIndex,
            string id, double supportStart, double supportEnd)
        {
            Record(document, "Edit Kakudoll Keyform Group");
            var group = document.KeyformGroups[groupIndex];
            group.Id = id;
            group.SupportStart = supportStart;
            group.SupportEnd = supportEnd;
            Finish(document);
        }

        internal static void BindKeyformGroup(VlatAuthoringDocument document, int groupIndex,
            VlatAuthoringMeshBinding binding, string topologyHash)
        {
            Record(document, "Bind Kakudoll Keyform Target");
            var group = document.KeyformGroups[groupIndex];
            if (group.MeshBinding < 0 || group.MeshBinding >= document.Metadata.MeshBindings.Count)
            {
                group.MeshBinding = document.Metadata.MeshBindings.Count;
                document.Metadata.MeshBindings.Add(binding);
            }
            else document.Metadata.MeshBindings[group.MeshBinding] = binding;
            group.TopologyHash = topologyHash;
            Finish(document);
        }

        internal static void AddKeyform(VlatAuthoringDocument document, int groupIndex, VlatAuthoringKeyform key)
        {
            Record(document, "Add Kakudoll Keyform");
            var keys = document.KeyformGroups[groupIndex].Keys;
            keys.Add(key);
            keys.Sort((a, b) => a.Yaw.CompareTo(b.Yaw));
            Finish(document);
        }

        internal static void RemoveKeyform(VlatAuthoringDocument document, int groupIndex, int keyIndex)
        {
            Record(document, "Remove Kakudoll Keyform");
            document.KeyformGroups[groupIndex].Keys.RemoveAt(keyIndex);
            Finish(document);
        }

        internal static void SetKeyformYaw(VlatAuthoringDocument document, int groupIndex, int keyIndex, double yaw)
        {
            Record(document, "Edit Kakudoll Keyform Yaw");
            var keys = document.KeyformGroups[groupIndex].Keys;
            keys[keyIndex].Yaw = yaw;
            keys.Sort((a, b) => a.Yaw.CompareTo(b.Yaw));
            Finish(document);
        }

        // 1Dの直接編集も、この一括差分commitへ接続する。頂点ごとのUndoは作らない。
        internal static void SetKeyformPositions(VlatAuthoringDocument document, int groupIndex, int keyIndex,
            IReadOnlyList<VlatAuthoringVertexDelta> positions, string undoName = "Import Kakudoll Keyform Positions")
        {
            var copy = new List<VlatAuthoringVertexDelta>(positions.Count);
            for (var i = 0; i < positions.Count; i++) copy.Add(positions[i].Copy());
            // 大量のList要素を入れ替える操作はUnityの差分Undo再生が極端に遅くなる。
            // 同じDocument/Unity履歴の一単位として完全snapshotを記録する。
            Record(document, undoName, complete: true);
            var key = document.KeyformGroups[groupIndex].Keys[keyIndex];
            key.Deltas.Clear();
            key.Deltas.AddRange(copy);
            Finish(document);
        }
    }
}
