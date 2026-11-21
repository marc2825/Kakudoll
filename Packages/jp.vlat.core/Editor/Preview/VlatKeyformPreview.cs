using System;
using UnityEngine;
using VLat.Core.Evaluation;
using VLat.Runtime;

namespace VLat.Editor.Authoring
{
    // Editorの専用Preview Scene内だけで使用する。製品RuntimeのKeyform対応を意味しない。
    // decode/Mesh複製/復元は既存Runtime binder、補間はCoreに委譲する。
    internal sealed class VlatKeyformPreview : IDisposable
    {
        private readonly DynamicBlendShapeBinder _binder = new DynamicBlendShapeBinder();
        private readonly SkinnedMeshRenderer _renderer;
        private readonly Mesh _mesh;
        private readonly YawKeyformEvaluator _evaluator;
        private readonly int[] _shapeIndices;
        private readonly double[] _weights;
        private readonly Bounds _originalBounds;
        private readonly bool _originalUpdateWhenOffscreen;
        private readonly bool _originalForceMatrices;
        private bool _disposed;

        internal VlatKeyformPreview(VlatAuthoringPackResult pack, Transform root,
            VlatAuthoringKeyformGroup authoringGroup, VlatAuthoringMeshBinding binding,
            MeshBindingResolutionReport report)
        {
            var group = pack.Document.KeyformGroups[0];
            var resolved = report[group.MeshBinding];
            if (!resolved.IsSuccess || !(resolved.Renderer is SkinnedMeshRenderer skinned))
                throw new ArgumentException("Keyform Previewには有効なSkinnedMeshRendererが必要です。");
            if (!HasMatchingBones(skinned))
                throw new ArgumentException("SkinnedMeshRendererのBone参照とMeshのbindposeが一致しません。Avatarを再読込してください。");
            // path一致だけで成功する旧RuntimeのfallbackをKeyformの厳密な契約に広げない。
            VlatKeyformBindingUtility.RequireMatch(root.gameObject, skinned,
                binding,
                group.TopologyHash);
            _renderer = skinned;
            _originalBounds = skinned.localBounds;
            _originalUpdateWhenOffscreen = skinned.updateWhenOffscreen;
            _originalForceMatrices = skinned.forceMatrixRecalculationPerRender;
            _evaluator = new YawKeyformEvaluator(group);
            _weights = new double[group.Keys.Count];
            _shapeIndices = new int[group.Keys.Count];
            try
            {
                for (var k = 0; k < group.Keys.Count; k++)
                {
                    _shapeIndices[k] = -1;
                    if (group.Keys[k].Delta == null) continue;
                    // 入力Meshに同名があればbinderが拒否し、途中成功分もDisposeで復元する。
                    var result = _binder.Bind(skinned, pack.Document, pack.Buffers,
                        "PreviewKeyform_" + authoringGroup.EditorIdentity, k, group.Keys[k].Delta);
                    if (!result.Success) throw new ArgumentException(result.Diagnostic.Message);
                    _shapeIndices[k] = result.BlendShapeIndex;
                }
                _mesh = skinned.sharedMesh;
                // binderの生成物だけを一時資源にする。中立groupではsource Meshに触らない。
                if (_mesh != resolved.Mesh) _mesh.hideFlags = HideFlags.HideAndDontSave;
                var padding = 0f;
                foreach (var key in authoringGroup.Keys)
                    foreach (var delta in key.Deltas)
                        padding = Mathf.Max(padding, delta.PositionDelta.magnitude);
                var bounds = _originalBounds;
                bounds.Expand(padding * 2f);
                skinned.localBounds = bounds;
                skinned.updateWhenOffscreen = true;
                skinned.forceMatrixRecalculationPerRender = true;
            }
            catch { _binder.Dispose(); throw; }
        }

        internal double GetWeight(int index) => _weights[index];
        internal int KeyCount => _weights.Length;
        internal bool IsAvailable => !_disposed && _renderer != null && _mesh != null && _renderer.sharedMesh == _mesh;

        internal void Apply(double yaw, bool enabled)
        {
            if (!IsAvailable) throw new InvalidOperationException("Keyform Previewの対象Renderer/Meshが失われたか変更されました。");
            _evaluator.Evaluate(yaw, _weights);
            for (var k = 0; k < _weights.Length; k++)
                if (_shapeIndices[k] >= 0)
                    _renderer.SetBlendShapeWeight(_shapeIndices[k], enabled ? (float)(_weights[k] * 100d) : 0f);
        }

        internal void ApplyKey(int keyIndex)
        {
            if (!IsAvailable || keyIndex < 0 || keyIndex >= _weights.Length)
                throw new InvalidOperationException("編集キーが失われました。");
            for (var k = 0; k < _weights.Length; k++)
            {
                _weights[k] = k == keyIndex ? 1d : 0d;
                if (_shapeIndices[k] >= 0) _renderer.SetBlendShapeWeight(_shapeIndices[k], k == keyIndex ? 100f : 0f);
            }
        }

        /// <summary>一時BlendShapeと複製Meshを破棄し、Preview Rendererを元のMeshに戻します。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _binder.ClearWeights();
            _binder.Dispose();
            if (_renderer != null)
            {
                // 外部のrig変更後はlocalBounds setter自体がUnity native errorを発生させる。
                // 不正なcloneのbounds復元を省略しても、binderのMesh解放とフラグ復帰は必ず行う。
                if (HasMatchingBones(_renderer)) _renderer.localBounds = _originalBounds;
                _renderer.updateWhenOffscreen = _originalUpdateWhenOffscreen;
                _renderer.forceMatrixRecalculationPerRender = _originalForceMatrices;
            }
        }

        internal static bool HasMatchingBones(SkinnedMeshRenderer renderer)
        {
            if (renderer.sharedMesh == null) return false;
            var bones = renderer.bones;
            if (bones.Length != renderer.sharedMesh.bindposes.Length) return false;
            for (var i = 0; i < bones.Length; i++) if (bones[i] == null) return false;
            return true;
        }
    }
}
