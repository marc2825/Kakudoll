using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace VLat.HeadFrameExperiment
{
    /// <summary>隔離projectへ複製した実FBXのHead・Renderer・全Skin Weightを監査します。</summary>
    public static class RealAvatarAudit
    {
        /// <summary>新規staging専用batch実行で、複製FBXを読み取り可能なHumanoidとして監査します。</summary>
        public static void RunAudit()
        {
            if (!Application.isBatchMode || !File.Exists("avatar-inputs.json"))
                throw new InvalidOperationException("avatar-inputs.jsonを持つ隔離batch project専用です。");
            string output = OutputPath();
            Directory.CreateDirectory(output);
            JArray models = (JArray)JObject.Parse(File.ReadAllText("avatar-inputs.json"))["models"];
            List<object> records = new List<object>();
            for (int i = 0; i < models.Count; ++i)
            {
                string assetPath = (string)models[i]["assetPath"];
                ConfigureImport(assetPath);
                GameObject root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(assetPath));
                try { records.Add(Inspect((string)models[i]["name"], root, assetPath)); }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            File.WriteAllText(Path.Combine(output, "audit.json"), JsonConvert.SerializeObject(records, Formatting.Indented));
            Debug.Log("Real avatar audit completed: " + records.Count);
        }

        internal static string OutputPath()
        {
            string path = Path.GetFullPath("results-avatars");
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i)
                if (args[i] == "-headFrameOutput") path = Path.GetFullPath(args[i + 1]);
            return path;
        }

        internal static void ConfigureImport(string path)
        {
            if (!path.StartsWith("Assets/Models/", StringComparison.Ordinal)) throw new ArgumentException("複製Models以外のimportは禁止です。");
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("FBX importerを取得できません。");
            importer.isReadable = true;
            importer.importBlendShapes = true;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.optimizeGameObjects = false;
            importer.skinWeights = ModelImporterSkinWeights.Custom;
            importer.maxBonesPerVertex = 255;
            importer.minBoneWeight = 0;
            importer.SaveAndReimport();
        }

        internal static Transform GetHead(GameObject root, out string resolution)
        {
            Animator animator = root.GetComponent<Animator>();
            if (animator != null && animator.avatar != null && animator.avatar.isValid && animator.isHuman)
            {
                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null) { resolution = "Humanoid"; return head; }
            }
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            Transform candidate = null;
            for (int i = 0; i < transforms.Length; ++i)
            {
                if (!string.Equals(transforms[i].name, "Head", StringComparison.OrdinalIgnoreCase)) continue;
                if (candidate != null) throw new InvalidOperationException("Head名が複数あり、自動推定を停止します。");
                candidate = transforms[i];
            }
            if (candidate == null) throw new InvalidOperationException("Headを解決できません。");
            resolution = "UniqueNameFallback_UNVERIFIED";
            return candidate;
        }

        internal static object Inspect(string name, GameObject root, string assetPath)
        {
            Animator animator = root.GetComponent<Animator>();
            Transform head = GetHead(root, out string resolution);
            Transform neck = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Neck) : head.parent;
            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            List<object> meshes = new List<object>();
            int totalVertices = 0;
            for (int i = 0; i < renderers.Length; ++i)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                Mesh mesh = renderer.sharedMesh;
                if (mesh == null) continue;
                totalVertices += mesh.vertexCount;
                List<object> materials = new List<object>();
                Material[] slots = renderer.sharedMaterials;
                for (int slot = 0; slot < slots.Length; ++slot)
                    materials.Add(new { slot, name = slots[slot] == null ? null : slots[slot].name,
                        shader = slots[slot] == null || slots[slot].shader == null ? null : slots[slot].shader.name });
                List<string> shapes = new List<string>();
                for (int shape = 0; shape < mesh.blendShapeCount; ++shape) shapes.Add(mesh.GetBlendShapeName(shape));
                meshes.Add(new { renderer = PathOf(root.transform, renderer.transform), mesh = mesh.name, mesh.vertexCount,
                    mesh.subMeshCount, blendShapeCount = mesh.blendShapeCount, blendShapes = shapes, materials,
                    boneCount = renderer.bones.Length, bindPoseCount = mesh.bindposes.Length,
                    rootBone = renderer.rootBone == null ? null : PathOf(root.transform, renderer.rootBone),
                    rendererScale = Values(renderer.transform.lossyScale), weights = Weights(renderer, head, neck),
                    sourceMeshFingerprint = Fingerprint(mesh) });
            }
            return new { name, assetPath, headResolution = resolution, humanoid = animator != null && animator.isHuman,
                avatarValid = animator != null && animator.avatar != null && animator.avatar.isValid,
                head = PathOf(root.transform, head), neck = neck == null ? null : PathOf(root.transform, neck),
                headLocalRotation = new[] { head.localRotation.x, head.localRotation.y, head.localRotation.z, head.localRotation.w },
                headForwardInAvatar = Values(root.transform.InverseTransformDirection(head.forward)),
                headUpInAvatar = Values(root.transform.InverseTransformDirection(head.up)), headScale = Values(head.lossyScale),
                canonicalBasis = "avatar root rest +Z forward / +Y up; visual face direction requires inspection",
                rendererCount = renderers.Length, totalVertices, meshes,
                physBoneSimulation = "NOT RUN (FBX only, no product Prefab components imported)" };
        }

        internal static object Weights(SkinnedMeshRenderer renderer, Transform head, Transform neck)
        {
            Mesh mesh = renderer.sharedMesh;
            Transform[] bones = renderer.bones;
            int rigidHead = 0, mixedHead = 0, headAffected = 0, headBranchAffected = 0, neckAffected = 0, eyeAffected = 0, jawAffected = 0, hairAffected = 0, maxInfluences = 0;
            double headSum = 0, headBranchSum = 0;
            List<object> boneTotals = new List<object>();
            double[] totals = new double[bones.Length];
            bool[] branch = new bool[bones.Length], eyes = new bool[bones.Length], jaws = new bool[bones.Length], hairs = new bool[bones.Length];
            for (int i = 0; i < bones.Length; ++i)
            {
                string boneName = bones[i] == null ? "" : bones[i].name.ToLowerInvariant();
                branch[i] = bones[i] != null && (bones[i] == head || bones[i].IsChildOf(head));
                eyes[i] = boneName.Contains("eye"); jaws[i] = boneName.Contains("jaw"); hairs[i] = boneName.Contains("hair");
            }
            // Mesh-owned NativeArray views must not be disposed by this reader.
            var counts = mesh.GetBonesPerVertex();
            var weights = mesh.GetAllBoneWeights();
            {
                int cursor = 0;
                for (int vertex = 0; vertex < mesh.vertexCount; ++vertex)
                {
                    int count = counts[vertex]; maxInfluences = Mathf.Max(maxInfluences, count);
                    float h = 0, b = 0, n = 0, eye = 0, jaw = 0, hair = 0;
                    for (int influence = 0; influence < count; ++influence)
                    {
                        var weight = weights[cursor++];
                        if (weight.boneIndex < 0 || weight.boneIndex >= bones.Length) throw new InvalidOperationException("bone indexが範囲外です。");
                        Transform bone = bones[weight.boneIndex]; totals[weight.boneIndex] += weight.weight;
                        if (bone == head) h += weight.weight;
                        if (bone == neck) n += weight.weight;
                        if (branch[weight.boneIndex]) b += weight.weight;
                        if (eyes[weight.boneIndex]) eye += weight.weight;
                        if (jaws[weight.boneIndex]) jaw += weight.weight;
                        if (hairs[weight.boneIndex]) hair += weight.weight;
                    }
                    headSum += h; headBranchSum += b;
                    if (b > 0.0001f) ++headBranchAffected;
                    if (h >= 0.9999f) ++rigidHead;
                    if (h > 0.0001f) { ++headAffected; if (h < 0.9999f) ++mixedHead; }
                    if (n > 0.0001f) ++neckAffected;
                    if (eye > 0.0001f) ++eyeAffected;
                    if (jaw > 0.0001f) ++jawAffected;
                    if (hair > 0.0001f) ++hairAffected;
                }
                if (cursor != weights.Length) throw new InvalidOperationException("skin weight列の長さが不一致です。");
            }
            for (int i = 0; i < bones.Length; ++i)
                if (totals[i] > 0) boneTotals.Add(new { index = i, name = bones[i] == null ? null : bones[i].name, weightSum = totals[i] });
            return new { rigidHead, mixedHead, headAffected, headSum, headBranchAffected, headBranchSum, neckAffected, eyeAffectedByName = eyeAffected,
                jawAffectedByName = jawAffected, hairAffectedByName = hairAffected, maxInfluences, boneTotals };
        }

        internal static string Fingerprint(Mesh mesh)
        {
            // Position/normal/tangent/index/bind/weight/name+frame metadata; raw FBX hash separately protects all morph payload.
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                foreach (Vector3 p in mesh.vertices) { writer.Write(p.x); writer.Write(p.y); writer.Write(p.z); }
                foreach (Vector3 p in mesh.normals) { writer.Write(p.x); writer.Write(p.y); writer.Write(p.z); }
                foreach (Vector4 p in mesh.tangents) { writer.Write(p.x); writer.Write(p.y); writer.Write(p.z); writer.Write(p.w); }
                for (int s = 0; s < mesh.subMeshCount; ++s) foreach (int index in mesh.GetIndices(s)) writer.Write(index);
                foreach (Matrix4x4 m in mesh.bindposes) for (int i = 0; i < 16; ++i) writer.Write(m[i]);
                var counts = mesh.GetBonesPerVertex(); foreach (byte count in counts) writer.Write(count);
                var weights = mesh.GetAllBoneWeights(); foreach (var w in weights) { writer.Write(w.boneIndex); writer.Write(w.weight); }
                for (int s = 0; s < mesh.blendShapeCount; ++s)
                {
                    writer.Write(mesh.GetBlendShapeName(s));
                    for (int f = 0; f < mesh.GetBlendShapeFrameCount(s); ++f) writer.Write(mesh.GetBlendShapeFrameWeight(s, f));
                }
                writer.Flush();
                using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }

        internal static string PathOf(Transform root, Transform target)
        {
            if (target == root) return "";
            string path = target.name;
            while (target.parent != null && target.parent != root) { target = target.parent; path = target.name + "/" + path; }
            return path;
        }

        internal static float[] Values(Vector3 vector) => new[] { vector.x, vector.y, vector.z };
    }
}
