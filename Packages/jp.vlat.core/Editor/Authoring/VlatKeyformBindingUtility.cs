using System;
using UnityEngine;
using VLat.Core.Model;
using VLat.Runtime;

namespace VLat.Editor.Authoring
{
    internal static class VlatKeyformBindingUtility
    {
        internal static Mesh GetSourceMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            if (renderer is MeshRenderer)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter != null) return filter.sharedMesh;
            }
            return null;
        }

        internal static VlatAuthoringMeshBinding Capture(GameObject avatarRoot, Renderer renderer, out string topologyHash)
        {
            if (avatarRoot == null || renderer == null)
                throw new ArgumentException("Avatar Rootと対象Rendererを指定してください。");
            string path;
            using (VlatEditorProfiling.Measure(VlatEditorPhase.BindingPaths))
            {
                path = UniVrmAdapter.GetRelativeHierarchyPath(avatarRoot.transform, renderer.transform);
                var matches = 0;
                foreach (var candidate in avatarRoot.GetComponentsInChildren<Renderer>(true))
                    if (UniVrmAdapter.GetRelativeHierarchyPath(avatarRoot.transform, candidate.transform) == path) matches++;
                if (matches != 1) throw new ArgumentException("Rendererの階層パスが一意ではありません。同名の兄弟や複数Rendererを確認してください。");
            }
            var mesh = GetSourceMesh(renderer);
            using (VlatEditorProfiling.Measure(VlatEditorPhase.BindingTopology))
                topologyHash = VlatBlendShapeKeyformImporter.CalculateTopologyHash(mesh);
            string positionHash;
            using (VlatEditorProfiling.Measure(VlatEditorPhase.BindingPositionHash))
                positionHash = PositionHashUtility.Calculate(mesh);
            var binding = new VlatAuthoringMeshBinding
            {
                HierarchyPath = path, MeshName = mesh.name, VertexCount = mesh.vertexCount,
                PositionHash = positionHash,
            };
            var materials = renderer.sharedMaterials;
            for (var i = 0; i < materials.Length; i++)
                if (materials[i] != null && materials[i].shader != null)
                    binding.Materials.Add(new VlatAuthoringMaterialSlot
                    {
                        Slot = i, MaterialName = materials[i].name, Shader = materials[i].shader.name,
                    });
            // 既存Importerで有限基準頂点と読み取り可能性を検査する。元Meshは更新しない。
            bool probeSuccess;
            using (VlatEditorProfiling.Measure(VlatEditorPhase.BindingProbe))
                // 位置/Topology指紋は上で同じMeshから計算済みのため、probe内の同値な再計算だけを省く。
                probeSuccess = VlatBlendShapeKeyformImporter.ImportPositions(mesh, ToImmutable(binding), topologyHash,
                    new[] { -1 }, new[] { 0d }, -120, 120, "keyforms.vlat.bin", sourceHashesVerified: true).Success;
            if (!probeSuccess) throw new ArgumentException("対象MeshをKeyformへ設定できません。");
            return binding;
        }

        internal static void RequireMatch(GameObject avatarRoot, Renderer renderer,
            VlatAuthoringMeshBinding expected, string topologyHash)
        {
            var actual = Capture(avatarRoot, renderer, out var actualTopology);
            if (expected == null || actual.HierarchyPath != expected.HierarchyPath ||
                actual.MeshName != expected.MeshName || actual.VertexCount != expected.VertexCount ||
                !string.Equals(actual.PositionHash, expected.PositionHash, StringComparison.OrdinalIgnoreCase) ||
                actualTopology != topologyHash)
                throw new ArgumentException("Binding不整合: 階層パス・Mesh名・頂点数・頂点位置・Topologyが一致しません。");
        }

        internal static MeshBinding ToImmutable(VlatAuthoringMeshBinding binding)
        {
            var materials = new VlatMaterialSlot[binding.Materials.Count];
            for (var i = 0; i < materials.Length; i++)
            {
                var material = binding.Materials[i];
                materials[i] = new VlatMaterialSlot(material.Slot, material.MaterialName, material.Shader);
            }
            return new MeshBinding(binding.HierarchyPath, binding.MeshName, binding.VertexCount, binding.PositionHash, materials);
        }
    }
}
