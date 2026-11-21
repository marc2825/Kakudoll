using System;
using System.Collections.Generic;
using UnityEngine;
using VLat.Core.Model;
using VLat.Core.Validation;

namespace VLat.Editor.Authoring
{
    internal static partial class VlatAuthoringPersistence
    {
        private static void ValidateKeyformStructure(VlatAuthoringDocument source, List<VlatDiagnostic> diagnostics)
        {
            if (source.KeyformGroups.Count == 0) return;
            if (source.KeyformGroups.Count != 1 || source.Layers.Count != 0 || source.Bands.Count != 0 ||
                source.Textures.Count != 0 || source.Metadata.Version != VlatValidator.KeyformVersion)
                diagnostics.Add(VlatAuthoringResultUtility.Error("$.keyformGroups",
                    "v0.2は単独1グループのみです。Layer/AngleBand/Textureとの混在は未対応です。"));
            long bytes = 0;
            for (var g = 0; g < source.KeyformGroups.Count; g++)
            {
                var group = source.KeyformGroups[g];
                var path = "$.keyformGroups[" + g + "]";
                if (group == null)
                {
                    diagnostics.Add(VlatAuthoringResultUtility.Error(path, "グループがnullです。"));
                    continue;
                }
                var keys = new YawKeyform[group.Keys.Count];
                for (var k = 0; k < keys.Length; k++)
                    if (group.Keys[k] != null) keys[k] = new YawKeyform(group.Keys[k].Yaw, null);
                var definition = new KeyformGroup(group.Id, group.MeshBinding, group.TopologyHash,
                    group.CoordinateSpace, group.Interpolation, group.SupportStart, group.SupportEnd, keys);
                diagnostics.AddRange(VlatValidator.ValidateKeyformDefinition(definition));
                if (group.MeshBinding < 0 || group.MeshBinding >= source.Metadata.MeshBindings.Count ||
                    source.Metadata.MeshBindings[group.MeshBinding] == null)
                    diagnostics.Add(VlatAuthoringResultUtility.Error(path + ".meshBinding", "対象Renderer/Meshを設定してください。"));
                else if (source.Metadata.MeshBindings[group.MeshBinding].VertexCount < 1 ||
                    source.Metadata.MeshBindings[group.MeshBinding].VertexCount > VlatValidator.MaximumKeyformVertices)
                    diagnostics.Add(VlatAuthoringResultUtility.Error(path + ".meshBinding", "頂点数は1〜200000です。"));
                for (var k = 0; k < group.Keys.Count; k++)
                {
                    var key = group.Keys[k];
                    if (key == null) continue;
                    var keyPath = path + ".keys[" + k + "].delta";
                    if (key.SerializationError != null)
                        diagnostics.Add(VlatAuthoringResultUtility.Error(keyPath,
                            "キーの位置差分データを復元できません（" + key.SerializationError + "）。"));
                    bytes += (long)key.Deltas.Count * 16;
                    if (key.Deltas.Count == 0) continue;
                    var vertex = new VlatAuthoringVertexDeltaModifier { MeshBinding = group.MeshBinding };
                    vertex.Deltas.AddRange(key.Deltas);
                    ValidateModifier(source, vertex, keyPath, diagnostics);
                    for (var v = 0; v < key.Deltas.Count; v++)
                        if (key.Deltas[v] != null && !key.Deltas[v].NormalDelta.Equals(Vector3.zero))
                            diagnostics.Add(VlatAuthoringResultUtility.Error(keyPath, "Keyformの法線差分は未対応です。"));
                }
            }
            if (bytes > VlatValidator.MaximumKeyformBufferBytes)
                diagnostics.Add(VlatAuthoringResultUtility.Error("$.keyformGroups", "差分の合計は16 MiB以下にしてください。"));
        }

        private static void AppendKeyformPlan(VlatAuthoringDocument source, CanonicalPlan plan)
        {
            for (var g = 0; g < source.KeyformGroups.Count; g++)
            {
                var group = source.KeyformGroups[g];
                var plans = new VertexPlan[group.Keys.Count];
                plan.Keyforms.Add(plans);
                for (var k = 0; k < group.Keys.Count; k++)
                {
                    // planはこのPack呼出の中だけで使い、結果（immutable document/buffer）へ参照を残さない。
                    // 同期処理中にAuthoring deltaは変更されないため、要素の複製は行わない。
                    var keyDeltas = group.Keys[k].Deltas;
                    var deltas = new List<VlatAuthoringVertexDelta>(keyDeltas.Count);
                    for (var d = 0; d < keyDeltas.Count; d++)
                        if (!keyDeltas[d].PositionDelta.Equals(Vector3.zero)) deltas.Add(keyDeltas[d]);
                    SortByVertexIndex(deltas);
                    if (deltas.Count != 0) plans[k] = plan.AddVertexDelta(deltas, false);
                }
            }
        }

        private static KeyformGroup[] BuildKeyformGroups(VlatAuthoringDocument source, CanonicalPlan plan)
        {
            var groups = new KeyformGroup[source.KeyformGroups.Count];
            for (var g = 0; g < groups.Length; g++)
            {
                var group = source.KeyformGroups[g];
                var keys = new YawKeyform[group.Keys.Count];
                for (var k = 0; k < keys.Length; k++)
                {
                    var vertex = plan.Keyforms[g][k];
                    keys[k] = new YawKeyform(group.Keys[k].Yaw, vertex == null ? null :
                        new VertexDeltaModifier(group.MeshBinding,
                            new VlatAccessor(vertex.IndicesView, "UINT32"),
                            new VlatAccessor(vertex.PositionsView, "FLOAT32"), null));
                }
                groups[g] = new KeyformGroup(group.Id, group.MeshBinding, group.TopologyHash,
                    group.CoordinateSpace, group.Interpolation, group.SupportStart, group.SupportEnd, keys);
            }
            return groups;
        }
    }
}
