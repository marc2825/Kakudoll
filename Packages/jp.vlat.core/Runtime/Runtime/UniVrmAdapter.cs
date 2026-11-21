using System;
using System.Collections.Generic;
using UniVRM10;
using UnityEngine;
using VLat.Core.Model;

namespace VLat.Runtime
{
    /// <summary>MeshBindingが解決された段階を表します。</summary>
    public enum MeshBindingMatchStage
    {
        /// <summary>アバタールートからのヒエラルキーパスが完全一致しました。</summary>
        ExactHierarchyPath,

        /// <summary>Mesh名と頂点数の組み合わせで一致しました。</summary>
        MeshNameAndVertexCount,

        /// <summary>頂点数と頂点位置ハッシュの組み合わせで一致しました。</summary>
        VertexCountAndPositionHash,

        /// <summary>一意な対象を解決できませんでした。</summary>
        Failed,
    }

    /// <summary>1つのMeshBindingに対するUnity Renderer解決結果を表します。</summary>
    public sealed class MeshBindingResolution
    {
        internal MeshBindingResolution(
            int bindingIndex,
            MeshBinding binding,
            Renderer renderer,
            Mesh mesh,
            MeshBindingMatchStage stage,
            string message)
        {
            BindingIndex = bindingIndex;
            Binding = binding;
            Renderer = renderer;
            Mesh = mesh;
            Stage = stage;
            Message = message ?? string.Empty;
        }

        /// <summary>VLat meshBindings配列内のインデックスを取得します。</summary>
        public int BindingIndex { get; }

        /// <summary>解決対象のMeshBindingを取得します。</summary>
        public MeshBinding Binding { get; }

        /// <summary>解決されたRendererを取得します。失敗時はnullです。</summary>
        public Renderer Renderer { get; }

        /// <summary>解決されたMeshを取得します。失敗時はnullです。</summary>
        public Mesh Mesh { get; }

        /// <summary>解決に使用した段階を取得します。</summary>
        public MeshBindingMatchStage Stage { get; }

        /// <summary>解決結果の説明を取得します。</summary>
        public string Message { get; }

        /// <summary>一意なRendererを解決できたかどうかを取得します。</summary>
        public bool IsSuccess => Renderer != null && Mesh != null && Stage != MeshBindingMatchStage.Failed;
    }

    /// <summary>ドキュメント内の全MeshBindingに対する解決レポートを表します。</summary>
    public sealed class MeshBindingResolutionReport
    {
        private readonly MeshBindingResolution[] _items;

        internal MeshBindingResolutionReport(MeshBindingResolution[] items, int successfulCount)
        {
            _items = items;
            SuccessfulCount = successfulCount;
        }

        /// <summary>MeshBinding数を取得します。</summary>
        public int Count => _items.Length;

        /// <summary>解決に成功したMeshBinding数を取得します。</summary>
        public int SuccessfulCount { get; }

        /// <summary>すべてのMeshBindingを解決できたかどうかを取得します。</summary>
        public bool AllSucceeded => SuccessfulCount == _items.Length;

        /// <summary>指定位置の解決結果を取得します。</summary>
        /// <param name="index">meshBindings配列内のインデックス。</param>
        /// <returns>指定されたMeshBindingの解決結果。</returns>
        public MeshBindingResolution this[int index] => _items[index];
    }

    /// <summary>UniVRM 1.0インスタンスとVLat MeshBindingの境界を提供します。</summary>
    public static class UniVrmAdapter
    {
        /// <summary>VRM 1.0の元Humanoid head Transformを取得します。</summary>
        /// <param name="instance">対象のVrm10Instance。</param>
        /// <param name="head">取得した元Humanoid head Transform。</param>
        /// <returns>headを取得できたときは<c>true</c>。</returns>
        public static bool TryGetHead(Vrm10Instance instance, out Transform head)
        {
            if (instance == null)
            {
                head = null;
                return false;
            }

            return instance.TryGetBoneTransform(HumanBodyBones.Head, out head) && head != null;
        }

        /// <summary>Vrm10Instance配下のRendererから全MeshBindingを順序どおり解決します。</summary>
        /// <param name="instance">対象のVrm10Instance。</param>
        /// <param name="document">解決するVLatドキュメント。</param>
        /// <returns>全MeshBindingの解決レポート。</returns>
        /// <exception cref="ArgumentNullException">引数がnullの場合に送出されます。</exception>
        public static MeshBindingResolutionReport Resolve(Vrm10Instance instance, VlatDocument document)
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            return Resolve(instance.transform, document);
        }

        /// <summary>任意のアバタールート配下のRendererから全MeshBindingを順序どおり解決します。</summary>
        /// <param name="avatarRoot">ヒエラルキーパスの基準となるアバタールート。</param>
        /// <param name="document">解決するVLatドキュメント。</param>
        /// <returns>全MeshBindingの解決レポート。</returns>
        /// <exception cref="ArgumentNullException">引数またはtargetAvatarがnullの場合に送出されます。</exception>
        public static MeshBindingResolutionReport Resolve(Transform avatarRoot, VlatDocument document)
        {
            if (avatarRoot == null)
            {
                throw new ArgumentNullException(nameof(avatarRoot));
            }

            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (document.Metadata == null || document.Metadata.TargetAvatar == null)
            {
                throw new ArgumentException("VLatドキュメントにtargetAvatarがありません。", nameof(document));
            }

            var candidates = CollectCandidates(avatarRoot);
            var bindings = document.Metadata.TargetAvatar.MeshBindings;
            var resolutions = new MeshBindingResolution[bindings.Count];
            var successfulCount = 0;
            for (var index = 0; index < bindings.Count; index++)
            {
                resolutions[index] = ResolveOne(index, bindings[index], candidates);
                if (resolutions[index].IsSuccess)
                {
                    successfulCount++;
                }
            }

            return new MeshBindingResolutionReport(resolutions, successfulCount);
        }

        /// <summary>アバタールートから対象Transformまでの相対ヒエラルキーパスを返します。</summary>
        /// <param name="avatarRoot">パスの基準となるTransform。</param>
        /// <param name="target">avatarRoot自身またはその子孫Transform。</param>
        /// <returns>区切り文字<c>/</c>を使用した相対パス。root自身は空文字列です。</returns>
        /// <exception cref="ArgumentNullException">引数がnullの場合に送出されます。</exception>
        /// <exception cref="ArgumentException">targetがavatarRootの子孫でない場合に送出されます。</exception>
        public static string GetRelativeHierarchyPath(Transform avatarRoot, Transform target)
        {
            if (avatarRoot == null)
            {
                throw new ArgumentNullException(nameof(avatarRoot));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var path = GetHierarchyPath(avatarRoot, target);
            if (path == null)
            {
                throw new ArgumentException("targetはavatarRootの子孫である必要があります。", nameof(target));
            }

            return path;
        }

        private static MeshBindingResolution ResolveOne(
            int bindingIndex,
            MeshBinding binding,
            Candidate[] candidates)
        {
            if (binding == null)
            {
                return Failed(bindingIndex, null, "MeshBindingがnullです。");
            }

            var match = FindUniqueByPath(candidates, binding.HierarchyPath);
            if (match != null)
            {
                return Success(bindingIndex, binding, match, MeshBindingMatchStage.ExactHierarchyPath);
            }

            match = FindUniqueByNameAndVertexCount(candidates, binding.MeshName, binding.VertexCount);
            if (match != null)
            {
                return Success(bindingIndex, binding, match, MeshBindingMatchStage.MeshNameAndVertexCount);
            }

            var hashMatches = 0;
            Candidate hashMatch = null;
            for (var index = 0; index < candidates.Length; index++)
            {
                var candidate = candidates[index];
                if (candidate.Mesh.vertexCount != binding.VertexCount)
                {
                    continue;
                }

                string positionHash;
                try
                {
                    positionHash = candidate.GetPositionHash();
                }
                catch (Exception)
                {
                    continue;
                }

                if (string.Equals(positionHash, binding.PositionHash, StringComparison.OrdinalIgnoreCase))
                {
                    hashMatches++;
                    hashMatch = candidate;
                }
            }

            if (hashMatches == 1)
            {
                return Success(
                    bindingIndex,
                    binding,
                    hashMatch,
                    MeshBindingMatchStage.VertexCountAndPositionHash);
            }

            if (hashMatches > 1)
            {
                return Failed(bindingIndex, binding, "頂点数とpositionHashが一致するRendererが複数あります。");
            }

            return Failed(bindingIndex, binding, "再バインド規則に一致するRendererがありません。");
        }

        private static Candidate FindUniqueByPath(Candidate[] candidates, string path)
        {
            Candidate match = null;
            var count = 0;
            for (var index = 0; index < candidates.Length; index++)
            {
                if (string.Equals(candidates[index].HierarchyPath, path, StringComparison.Ordinal))
                {
                    count++;
                    match = candidates[index];
                }
            }

            return count == 1 ? match : null;
        }

        private static Candidate FindUniqueByNameAndVertexCount(
            Candidate[] candidates,
            string meshName,
            int vertexCount)
        {
            Candidate match = null;
            var count = 0;
            for (var index = 0; index < candidates.Length; index++)
            {
                var candidate = candidates[index];
                if (candidate.Mesh.vertexCount == vertexCount
                    && string.Equals(candidate.Mesh.name, meshName, StringComparison.Ordinal))
                {
                    count++;
                    match = candidate;
                }
            }

            return count == 1 ? match : null;
        }

        private static MeshBindingResolution Success(
            int index,
            MeshBinding binding,
            Candidate candidate,
            MeshBindingMatchStage stage)
        {
            return new MeshBindingResolution(
                index,
                binding,
                candidate.Renderer,
                candidate.Mesh,
                stage,
                "MeshBindingを" + GetStageLabel(stage) + "で解決しました。");
        }

        private static MeshBindingResolution Failed(int index, MeshBinding binding, string message)
        {
            return new MeshBindingResolution(
                index,
                binding,
                null,
                null,
                MeshBindingMatchStage.Failed,
                message);
        }

        private static string GetStageLabel(MeshBindingMatchStage stage)
        {
            switch (stage)
            {
                case MeshBindingMatchStage.ExactHierarchyPath:
                    return "hierarchyPath完全一致";
                case MeshBindingMatchStage.MeshNameAndVertexCount:
                    return "meshNameとvertexCount";
                case MeshBindingMatchStage.VertexCountAndPositionHash:
                    return "vertexCountとpositionHash";
                default:
                    return "不明な段階";
            }
        }

        private static Candidate[] CollectCandidates(Transform avatarRoot)
        {
            var renderers = avatarRoot.GetComponentsInChildren<Renderer>(true);
            var candidates = new List<Candidate>(renderers.Length);
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                var mesh = GetSharedMesh(renderer);
                if (mesh != null)
                {
                    candidates.Add(new Candidate(
                        renderer,
                        mesh,
                        GetHierarchyPath(avatarRoot, renderer.transform)));
                }
            }

            return candidates.ToArray();
        }

        private static Mesh GetSharedMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                return skinned.sharedMesh;
            }

            if (renderer is MeshRenderer)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                return filter == null ? null : filter.sharedMesh;
            }

            return null;
        }

        private static string GetHierarchyPath(Transform root, Transform target)
        {
            if (target == root)
            {
                return string.Empty;
            }

            var names = new List<string>();
            var current = target;
            while (current != null && current != root)
            {
                names.Add(current.name);
                current = current.parent;
            }

            if (current != root)
            {
                return null;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        private sealed class Candidate
        {
            private string _positionHash;

            public Candidate(Renderer renderer, Mesh mesh, string hierarchyPath)
            {
                Renderer = renderer;
                Mesh = mesh;
                HierarchyPath = hierarchyPath;
            }

            public Renderer Renderer { get; }

            public Mesh Mesh { get; }

            public string HierarchyPath { get; }

            public string GetPositionHash()
            {
                if (_positionHash == null)
                {
                    _positionHash = PositionHashUtility.Calculate(Mesh);
                }

                return _positionHash;
            }
        }
    }
}
