using System;
using System.Collections.Generic;
using UniGLTF.Extensions.VRMC_vrm;
using UniVRM10;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VLat.Editor.Authoring
{
    /// <summary>
    /// VRM 1.0のserialized Expression定義をEditModeの一時avatarへ適用します。
    /// </summary>
    /// <remarks>
    /// <see cref="Vrm10Instance.Runtime"/>はPlay Mode専用のlifecycleを生成するため使用しません。
    /// binary/overrideの解決はUniVRMの公開Expression validator factoryへ委譲し、
    /// 解決後のmorph/material bindingだけをEditor所有の複製へ適用します。
    /// </remarks>
    internal sealed class VlatEditorExpressionPreview : IDisposable
    {
        private readonly List<ExpressionDefinition> _definitions =
            new List<ExpressionDefinition>();
        private readonly List<ExpressionKey> _keys = new List<ExpressionKey>();
        private readonly List<string> _names = new List<string>();
        private readonly Dictionary<ExpressionKey, float> _inputWeights =
            new Dictionary<ExpressionKey, float>(ExpressionKey.Comparer);
        private readonly Dictionary<ExpressionKey, float> _actualWeights =
            new Dictionary<ExpressionKey, float>(ExpressionKey.Comparer);
        private readonly List<MorphTargetState> _morphTargets =
            new List<MorphTargetState>();
        private readonly Dictionary<MorphTargetKey, int> _morphTargetIndices =
            new Dictionary<MorphTargetKey, int>();
        private readonly List<MaterialState> _materials = new List<MaterialState>();
        private readonly Dictionary<string, int> _materialIndices =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> _boundMaterialNames =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly List<RendererMaterialState> _rendererMaterials =
            new List<RendererMaterialState>();
        private readonly List<Material> _ownedMaterials = new List<Material>();
        private readonly IExpressionValidator _validator;
        private bool _disposed;

        internal VlatEditorExpressionPreview(Vrm10Instance avatar)
        {
            if (avatar == null)
            {
                throw new ArgumentNullException(nameof(avatar));
            }

            var expression = avatar.Vrm == null ? null : avatar.Vrm.Expression;
            if (expression == null)
            {
                return;
            }

            CollectDefinitions(expression);
            CloneBoundMaterials(avatar.transform);
            BuildMaterialStates(avatar.transform);
            BuildBindings(avatar.transform);
            _validator = Vrm10RuntimeExpression.ExpressionValidatorFactory.Create(expression);
        }

        internal IReadOnlyList<string> Names => _names;

        internal int Count => _keys.Count;

        internal int OwnedMaterialCount => _ownedMaterials.Count;

        internal float GetWeight(int index)
        {
            if (index < 0 || index >= _keys.Count)
            {
                return 0f;
            }

            return _inputWeights[_keys[index]];
        }

        internal void SetWeight(int index, float weight)
        {
            if (index < 0 || index >= _keys.Count)
            {
                return;
            }

            _inputWeights[_keys[index]] = Mathf.Clamp01(weight);
        }

        internal void ResetWeights()
        {
            for (var index = 0; index < _keys.Count; index++)
            {
                _inputWeights[_keys[index]] = 0f;
            }
        }

        internal float[] CaptureWeights()
        {
            var weights = new float[_keys.Count];
            for (var index = 0; index < _keys.Count; index++)
            {
                weights[index] = _inputWeights[_keys[index]];
            }

            return weights;
        }

        internal void RestoreWeights(IReadOnlyList<float> weights)
        {
            if (weights == null)
            {
                return;
            }

            var count = Mathf.Min(_keys.Count, weights.Count);
            for (var index = 0; index < count; index++)
            {
                _inputWeights[_keys[index]] = Mathf.Clamp01(weights[index]);
            }

            for (var index = count; index < _keys.Count; index++)
            {
                _inputWeights[_keys[index]] = 0f;
            }
        }

        internal void Apply()
        {
            if (_disposed || _validator == null)
            {
                return;
            }

            _validator.Validate(
                _inputWeights,
                _actualWeights,
                new LookAtEyeDirection(0f, 0f),
                out _,
                out _,
                out _,
                out _);

            for (var index = 0; index < _morphTargets.Count; index++)
            {
                _morphTargets[index].AccumulatedWeight = 0f;
            }

            for (var index = 0; index < _materials.Count; index++)
            {
                _materials[index].ResetAccumulation();
            }

            for (var definitionIndex = 0; definitionIndex < _definitions.Count; definitionIndex++)
            {
                var definition = _definitions[definitionIndex];
                if (!_actualWeights.TryGetValue(definition.Key, out var weight))
                {
                    continue;
                }

                for (var bindingIndex = 0; bindingIndex < definition.MorphBindings.Count; bindingIndex++)
                {
                    var binding = definition.MorphBindings[bindingIndex];
                    _morphTargets[binding.TargetIndex].AccumulatedWeight +=
                        binding.BindingWeight * weight;
                }

                for (var bindingIndex = 0; bindingIndex < definition.ColorBindings.Count; bindingIndex++)
                {
                    var binding = definition.ColorBindings[bindingIndex];
                    _materials[binding.MaterialIndex].AccumulateColor(
                        binding.BindType,
                        binding.TargetValue,
                        weight);
                }

                for (var bindingIndex = 0; bindingIndex < definition.UvBindings.Count; bindingIndex++)
                {
                    var binding = definition.UvBindings[bindingIndex];
                    _materials[binding.MaterialIndex].AccumulateUv(
                        binding.TargetValue,
                        weight);
                }
            }

            for (var index = 0; index < _morphTargets.Count; index++)
            {
                var target = _morphTargets[index];
                if (target.Renderer != null && target.Renderer.sharedMesh != null &&
                    target.BlendShapeIndex >= 0 &&
                    target.BlendShapeIndex < target.Renderer.sharedMesh.blendShapeCount)
                {
                    target.Renderer.SetBlendShapeWeight(
                        target.BlendShapeIndex,
                        target.AccumulatedWeight);
                }
            }

            for (var index = 0; index < _materials.Count; index++)
            {
                _materials[index].Apply();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            for (var index = 0; index < _morphTargets.Count; index++)
            {
                var target = _morphTargets[index];
                if (target.Renderer != null && target.Renderer.sharedMesh != null &&
                    target.BlendShapeIndex >= 0 &&
                    target.BlendShapeIndex < target.Renderer.sharedMesh.blendShapeCount)
                {
                    target.Renderer.SetBlendShapeWeight(
                        target.BlendShapeIndex,
                        target.InitialWeight);
                }
            }

            for (var index = 0; index < _rendererMaterials.Count; index++)
            {
                var state = _rendererMaterials[index];
                if (state.Renderer != null)
                {
                    state.Renderer.sharedMaterials = state.OriginalMaterials;
                }
            }

            for (var index = 0; index < _ownedMaterials.Count; index++)
            {
                DestroyObject(_ownedMaterials[index]);
            }

            _ownedMaterials.Clear();
            _rendererMaterials.Clear();
        }

        private void CollectDefinitions(VRM10ObjectExpression expression)
        {
            foreach (var entry in expression.Clips)
            {
                if (entry.Clip == null)
                {
                    continue;
                }

                var key = expression.CreateKey(entry.Clip);
                _keys.Add(key);
                _names.Add(key.ToString());
                _inputWeights.Add(key, 0f);
                _actualWeights.Add(key, 0f);
                _definitions.Add(new ExpressionDefinition(key, entry.Clip));
                for (var index = 0; index < entry.Clip.MaterialColorBindings.Length; index++)
                {
                    _boundMaterialNames.Add(
                        entry.Clip.MaterialColorBindings[index].MaterialName);
                }

                for (var index = 0; index < entry.Clip.MaterialUVBindings.Length; index++)
                {
                    _boundMaterialNames.Add(
                        entry.Clip.MaterialUVBindings[index].MaterialName);
                }
            }
        }

        private void CloneBoundMaterials(Transform root)
        {
            if (_boundMaterialNames.Count == 0)
            {
                return;
            }

            var cloneMap = new Dictionary<Material, Material>();
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                var original = renderer.sharedMaterials;
                Material[] working = null;
                for (var slot = 0; slot < original.Length; slot++)
                {
                    var source = original[slot];
                    if (source == null || !_boundMaterialNames.Contains(source.name))
                    {
                        continue;
                    }

                    if (!cloneMap.TryGetValue(source, out var clone))
                    {
                        clone = new Material(source)
                        {
                            name = source.name,
                            hideFlags = HideFlags.HideAndDontSave,
                        };
                        cloneMap.Add(source, clone);
                        _ownedMaterials.Add(clone);
                    }

                    if (working == null)
                    {
                        working = (Material[])original.Clone();
                    }

                    working[slot] = clone;
                }

                if (working != null)
                {
                    _rendererMaterials.Add(new RendererMaterialState(renderer, original));
                    renderer.sharedMaterials = working;
                }
            }
        }

        private void BuildMaterialStates(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                var materials = renderers[rendererIndex].sharedMaterials;
                for (var slot = 0; slot < materials.Length; slot++)
                {
                    var material = materials[slot];
                    if (material == null || !_boundMaterialNames.Contains(material.name) ||
                        _materialIndices.ContainsKey(material.name))
                    {
                        continue;
                    }

                    _materialIndices.Add(material.name, _materials.Count);
                    _materials.Add(new MaterialState(material));
                }
            }
        }

        private void BuildBindings(Transform root)
        {
            for (var definitionIndex = 0; definitionIndex < _definitions.Count; definitionIndex++)
            {
                var definition = _definitions[definitionIndex];
                var perClipTargets = new HashSet<MorphTargetKey>();
                var morphBindings = definition.Clip.MorphTargetBindings;
                for (var bindingIndex = 0; bindingIndex < morphBindings.Length; bindingIndex++)
                {
                    var raw = morphBindings[bindingIndex];
                    var targetTransform = root.Find(raw.RelativePath);
                    var renderer = targetTransform == null
                        ? null
                        : targetTransform.GetComponent<SkinnedMeshRenderer>();
                    if (renderer == null || renderer.sharedMesh == null || raw.Index < 0 ||
                        raw.Index >= renderer.sharedMesh.blendShapeCount)
                    {
                        continue;
                    }

                    var key = new MorphTargetKey(renderer, raw.Index);
                    if (!perClipTargets.Add(key))
                    {
                        continue;
                    }

                    if (!_morphTargetIndices.TryGetValue(key, out var targetIndex))
                    {
                        targetIndex = _morphTargets.Count;
                        _morphTargetIndices.Add(key, targetIndex);
                        _morphTargets.Add(new MorphTargetState(
                            renderer,
                            raw.Index,
                            renderer.GetBlendShapeWeight(raw.Index)));
                    }

                    definition.MorphBindings.Add(new BoundMorphTarget(
                        targetIndex,
                        raw.Weight * MorphTargetBinding.VRM_TO_UNITY));
                }

                var colorBindings = definition.Clip.MaterialColorBindings;
                for (var bindingIndex = 0; bindingIndex < colorBindings.Length; bindingIndex++)
                {
                    var raw = colorBindings[bindingIndex];
                    if (_materialIndices.TryGetValue(raw.MaterialName, out var materialIndex) &&
                        _materials[materialIndex].EnsureColorChannel(raw.BindType))
                    {
                        definition.ColorBindings.Add(new BoundMaterialColor(
                            materialIndex,
                            raw.BindType,
                            raw.TargetValue));
                    }
                }

                var uvBindings = definition.Clip.MaterialUVBindings;
                for (var bindingIndex = 0; bindingIndex < uvBindings.Length; bindingIndex++)
                {
                    var raw = uvBindings[bindingIndex];
                    if (_materialIndices.TryGetValue(raw.MaterialName, out var materialIndex))
                    {
                        definition.UvBindings.Add(new BoundMaterialUv(
                            materialIndex,
                            raw.ScalingOffset));
                    }
                }
            }
        }

        private static string GetColorProperty(MaterialColorType bindType)
        {
            switch (bindType)
            {
                case MaterialColorType.color:
                    return "_Color";
                case MaterialColorType.emissionColor:
                    return "_EmissionColor";
                case MaterialColorType.shadeColor:
                    return "_ShadeColor";
                case MaterialColorType.rimColor:
                    return "_RimColor";
                case MaterialColorType.outlineColor:
                    return "_OutlineColor";
                case MaterialColorType.matcapColor:
                    return "_MatcapColor";
                default:
                    throw new ArgumentOutOfRangeException(nameof(bindType), bindType, null);
            }
        }

        private static void DestroyObject(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }

        private sealed class ExpressionDefinition
        {
            internal ExpressionDefinition(ExpressionKey key, VRM10Expression clip)
            {
                Key = key;
                Clip = clip;
            }

            internal ExpressionKey Key { get; }

            internal VRM10Expression Clip { get; }

            internal List<BoundMorphTarget> MorphBindings { get; } =
                new List<BoundMorphTarget>();

            internal List<BoundMaterialColor> ColorBindings { get; } =
                new List<BoundMaterialColor>();

            internal List<BoundMaterialUv> UvBindings { get; } =
                new List<BoundMaterialUv>();
        }

        private sealed class MorphTargetState
        {
            internal MorphTargetState(
                SkinnedMeshRenderer renderer,
                int blendShapeIndex,
                float initialWeight)
            {
                Renderer = renderer;
                BlendShapeIndex = blendShapeIndex;
                InitialWeight = initialWeight;
            }

            internal SkinnedMeshRenderer Renderer { get; }

            internal int BlendShapeIndex { get; }

            internal float InitialWeight { get; }

            internal float AccumulatedWeight { get; set; }
        }

        private sealed class MaterialState
        {
            private readonly Dictionary<MaterialColorType, ColorChannel> _colors =
                new Dictionary<MaterialColorType, ColorChannel>();
            private readonly Vector4 _baseUv;
            private Vector4 _currentUv;

            internal MaterialState(Material material)
            {
                Material = material;
                var scale = material.mainTextureScale;
                var offset = material.mainTextureOffset;
                _baseUv = new Vector4(scale.x, scale.y, offset.x, offset.y);
                _currentUv = _baseUv;
            }

            internal Material Material { get; }

            internal bool EnsureColorChannel(MaterialColorType bindType)
            {
                if (_colors.ContainsKey(bindType))
                {
                    return true;
                }

                var property = GetColorProperty(bindType);
                if (!Material.HasProperty(property))
                {
                    return false;
                }

                var baseline = (Vector4)Material.GetColor(property);
                _colors.Add(bindType, new ColorChannel(property, baseline));
                return true;
            }

            internal void ResetAccumulation()
            {
                foreach (var channel in _colors.Values)
                {
                    channel.CurrentValue = channel.BaseValue;
                }

                _currentUv = _baseUv;
            }

            internal void AccumulateColor(
                MaterialColorType bindType,
                Vector4 targetValue,
                float weight)
            {
                if (_colors.TryGetValue(bindType, out var channel))
                {
                    channel.CurrentValue += (targetValue - channel.BaseValue) * weight;
                }
            }

            internal void AccumulateUv(Vector4 targetValue, float weight)
            {
                _currentUv += (targetValue - _baseUv) * weight;
            }

            internal void Apply()
            {
                if (Material == null)
                {
                    return;
                }

                foreach (var channel in _colors.Values)
                {
                    Material.SetColor(channel.PropertyName, channel.CurrentValue);
                }

                Material.mainTextureScale = new Vector2(_currentUv.x, _currentUv.y);
                Material.mainTextureOffset = new Vector2(_currentUv.z, _currentUv.w);
            }
        }

        private sealed class ColorChannel
        {
            internal ColorChannel(string propertyName, Vector4 baseValue)
            {
                PropertyName = propertyName;
                BaseValue = baseValue;
                CurrentValue = baseValue;
            }

            internal string PropertyName { get; }

            internal Vector4 BaseValue { get; }

            internal Vector4 CurrentValue { get; set; }
        }

        private readonly struct RendererMaterialState
        {
            internal RendererMaterialState(Renderer renderer, Material[] originalMaterials)
            {
                Renderer = renderer;
                OriginalMaterials = originalMaterials;
            }

            internal Renderer Renderer { get; }

            internal Material[] OriginalMaterials { get; }
        }

        private readonly struct BoundMorphTarget
        {
            internal BoundMorphTarget(int targetIndex, float bindingWeight)
            {
                TargetIndex = targetIndex;
                BindingWeight = bindingWeight;
            }

            internal int TargetIndex { get; }

            internal float BindingWeight { get; }
        }

        private readonly struct BoundMaterialColor
        {
            internal BoundMaterialColor(
                int materialIndex,
                MaterialColorType bindType,
                Vector4 targetValue)
            {
                MaterialIndex = materialIndex;
                BindType = bindType;
                TargetValue = targetValue;
            }

            internal int MaterialIndex { get; }

            internal MaterialColorType BindType { get; }

            internal Vector4 TargetValue { get; }
        }

        private readonly struct BoundMaterialUv
        {
            internal BoundMaterialUv(int materialIndex, Vector4 targetValue)
            {
                MaterialIndex = materialIndex;
                TargetValue = targetValue;
            }

            internal int MaterialIndex { get; }

            internal Vector4 TargetValue { get; }
        }

        private readonly struct MorphTargetKey : IEquatable<MorphTargetKey>
        {
            private readonly int _rendererId;
            private readonly int _blendShapeIndex;

            internal MorphTargetKey(SkinnedMeshRenderer renderer, int blendShapeIndex)
            {
                _rendererId = renderer.GetInstanceID();
                _blendShapeIndex = blendShapeIndex;
            }

            public bool Equals(MorphTargetKey other)
            {
                return _rendererId == other._rendererId &&
                    _blendShapeIndex == other._blendShapeIndex;
            }

            public override bool Equals(object obj)
            {
                return obj is MorphTargetKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (_rendererId * 397) ^ _blendShapeIndex;
                }
            }
        }
    }
}
