using System;
using System.Collections.Generic;
using NUnit.Framework;
using VLat.Core.Evaluation;
using VLat.Core.Model;

namespace VLat.Tests.Edit
{
    /// <summary>ConflictResolverのpriority占有と重み正規化を検証します。</summary>
    public sealed class ConflictResolverTests
    {
        /// <summary>同priorityの重み合計が1以下なら生重みとベース状態が保持されることを検証します。</summary>
        [Test]
        public void Resolve_PreservesWeightsAndBaseWhenSumDoesNotExceedOne()
        {
            var resolver = CreateOpacityResolver(0, 0);
            var state = resolver.CreateState();

            resolver.Resolve(new[] { 0.2d, 0.3d }, state);

            Assert.That(state.Count, Is.EqualTo(2));
            Assert.That(FindWeight(state, "layer-a"), Is.EqualTo(0.2d).Within(1e-9d));
            Assert.That(FindWeight(state, "layer-b"), Is.EqualTo(0.3d).Within(1e-9d));
            Assert.That(state.GetBaseWeight(state[0].GroupId), Is.EqualTo(0.5d).Within(1e-9d));
        }

        /// <summary>同priorityの重み合計が1を超えたときだけ正規化されることを検証します。</summary>
        [Test]
        public void Resolve_NormalizesEqualPriorityWeightsAboveOne()
        {
            var resolver = CreateOpacityResolver(0, 0);
            var state = resolver.CreateState();

            resolver.Resolve(new[] { 0.8d, 0.8d }, state);

            Assert.That(FindWeight(state, "layer-a"), Is.EqualTo(0.5d).Within(1e-9d));
            Assert.That(FindWeight(state, "layer-b"), Is.EqualTo(0.5d).Within(1e-9d));
            Assert.That(state.GetBaseWeight(state[0].GroupId), Is.Zero.Within(1e-9d));
        }

        /// <summary>重みが小さくても最大priorityのLayerのみが連続値を占有することを検証します。</summary>
        [Test]
        public void Resolve_HigherPriorityOccupiesTarget()
        {
            var resolver = CreateOpacityResolver(0, 10);
            var state = resolver.CreateState();

            resolver.Resolve(new[] { 0.9d, 0.1d }, state);

            Assert.That(state.Count, Is.EqualTo(1));
            Assert.That(state[0].Layer.Id, Is.EqualTo("layer-b"));
            Assert.That(state[0].Weight, Is.EqualTo(0.1d).Within(1e-9d));
            Assert.That(state.GetBaseWeight(state[0].GroupId), Is.EqualTo(0.9d).Within(1e-9d));
        }

        /// <summary>最大priority群の連続/スイッチ性だけが解決方式を決めることを検証します。</summary>
        [Test]
        public void Resolve_LowerPrioritySwitchDoesNotChangeContinuousMode()
        {
            var bands = CreateBands();
            var continuous = new MaterialPropertyModifier(
                0,
                0,
                "_Value",
                "float",
                new VlatMaterialValue(0.25d),
                true);
            var switched = new MaterialPropertyModifier(
                0,
                0,
                "_Value",
                "float",
                new VlatMaterialValue(0.75d),
                false);
            var layers = new[]
            {
                CreateLayer("layer-high", "band-a", 10, continuous),
                CreateLayer("layer-low", "band-b", 0, switched),
            };
            var resolver = new ConflictResolver(CreateDocument(bands, layers));
            var state = resolver.CreateState();

            resolver.Resolve(new[] { 0.25d, 0.75d }, state);

            Assert.That(state.Count, Is.EqualTo(1));
            Assert.That(state[0].Layer.Id, Is.EqualTo("layer-high"));
            Assert.That(state[0].IsSwitch, Is.False);
            Assert.That(state[0].Weight, Is.EqualTo(0.25d));
            Assert.That(state.GetBaseWeight(state[0].GroupId), Is.EqualTo(0.75d));
        }

        /// <summary>スイッチ型では同priority内の生重みが最大のLayerのみが選択されることを検証します。</summary>
        [Test]
        public void Resolve_SwitchTargetUsesLargestRawWeight()
        {
            var bands = CreateBands();
            var layers = new[]
            {
                CreateLayer("layer-a", "band-a", 0, new VisibilityModifier(0, 0, false)),
                CreateLayer("layer-b", "band-b", 0, new VisibilityModifier(0, 0, true)),
            };
            var resolver = new ConflictResolver(CreateDocument(bands, layers));
            var state = resolver.CreateState();

            resolver.Resolve(new[] { 0.6d, 0.9d }, state);

            Assert.That(state.Count, Is.EqualTo(1));
            Assert.That(state[0].Layer.Id, Is.EqualTo("layer-b"));
            Assert.That(state[0].IsSwitch, Is.True);
            Assert.That(state.GetBaseWeight(state[0].GroupId), Is.Zero);
        }

        /// <summary>最大重みが0.5未満のスイッチ型はベース状態のままになることを検証します。</summary>
        [Test]
        public void Resolve_SwitchTargetBelowThresholdKeepsBaseState()
        {
            var bands = CreateBands();
            var layers = new[]
            {
                CreateLayer("layer-a", "band-a", 0, new VisibilityModifier(0, 0, false)),
                CreateLayer("layer-b", "band-b", 0, new VisibilityModifier(0, 0, true)),
            };
            var resolver = new ConflictResolver(CreateDocument(bands, layers));
            var state = resolver.CreateState();

            resolver.Resolve(new[] { 0.49d, 0.4d }, state);

            Assert.That(state.Count, Is.Zero);
            Assert.That(state.GetBaseWeight(0), Is.EqualTo(1d));
        }

        /// <summary>visibilityがONになった後は生重みが0.5未満でも退場境界まで保持されることを検証します。</summary>
        [Test]
        public void Resolve_WithAnglesAppliesVisibilityHysteresis()
        {
            var band = new AngleBand(
                "band",
                "Band",
                new AngleRange(-10d, 10d),
                new AngleRange(-10d, 10d),
                new AngleFeather(20d, 20d),
                5d);
            var layer = CreateLayer("layer", "band", 0, new VisibilityModifier(0, 0, false));
            var resolver = new ConflictResolver(CreateDocument(new[] { band }, new[] { layer }));
            var state = resolver.CreateState();

            resolver.Resolve(new ViewAngles(20d, 0d), new[] { 0.5d }, state);
            Assert.That(state.Count, Is.EqualTo(1));

            resolver.Resolve(new ViewAngles(24d, 0d), new[] { 0.16308d }, state);
            Assert.That(state.Count, Is.EqualTo(1));
            Assert.That(state[0].Weight, Is.LessThan(0.5d));

            resolver.Resolve(new ViewAngles(26d, 0d), new[] { 0d }, state);
            Assert.That(state.Count, Is.Zero);
        }

        /// <summary>vertexDeltaがpriorityに関係なく加算用重みとしてすべて残ることを検証します。</summary>
        [Test]
        public void Resolve_VertexDeltaAddsAllPriorities()
        {
            var bands = CreateBands();
            var accessor = new VlatAccessor(0, "UINT32");
            var positionAccessor = new VlatAccessor(1, "FLOAT32");
            var layers = new[]
            {
                CreateLayer("layer-a", "band-a", -10, new VertexDeltaModifier(0, accessor, positionAccessor)),
                CreateLayer("layer-b", "band-b", 10, new VertexDeltaModifier(0, accessor, positionAccessor)),
            };
            var resolver = new ConflictResolver(CreateDocument(bands, layers));
            var state = resolver.CreateState();

            resolver.Resolve(new[] { 0.25d, 0.75d }, state);

            Assert.That(state.Count, Is.EqualTo(2));
            Assert.That(FindWeight(state, "layer-a"), Is.EqualTo(0.25d));
            Assert.That(FindWeight(state, "layer-b"), Is.EqualTo(0.75d));
            Assert.That(state[0].GroupId, Is.EqualTo(-1));
            Assert.That(state[1].GroupId, Is.EqualTo(-1));
        }

        private static ConflictResolver CreateOpacityResolver(int firstPriority, int secondPriority)
        {
            var layers = new[]
            {
                CreateLayer("layer-a", "band-a", firstPriority, new OpacityModifier(0, 0, 0.25d)),
                CreateLayer("layer-b", "band-b", secondPriority, new OpacityModifier(0, 0, 0.75d)),
            };
            return new ConflictResolver(CreateDocument(CreateBands(), layers));
        }

        private static AngleBand[] CreateBands()
        {
            return new[]
            {
                new AngleBand("band-a", "A", new AngleRange(-10d, 10d), new AngleRange(-10d, 10d), new AngleFeather(5d, 5d)),
                new AngleBand("band-b", "B", new AngleRange(20d, 40d), new AngleRange(-10d, 10d), new AngleFeather(5d, 5d)),
            };
        }

        private static VlatLayer CreateLayer(string id, string bandId, int priority, VlatModifier modifier)
        {
            return new VlatLayer(id, id, "other", true, priority, bandId, new[] { modifier });
        }

        private static VlatDocument CreateDocument(
            IReadOnlyList<AngleBand> bands,
            IReadOnlyList<VlatLayer> layers)
        {
            var material = new VlatMaterialSlot(0, "Material", "VRM10/MToon10");
            var mesh = new MeshBinding("Face", "Face", 1, "xxh64:0000000000000000", new[] { material });
            var avatar = new TargetAvatar("Avatar", "1.0", new[] { mesh });
            var metadata = new VlatMetadata("0.1.0", "VLat Tests", avatar);
            return new VlatDocument(
                metadata,
                Array.Empty<VlatBuffer>(),
                Array.Empty<VlatBufferView>(),
                Array.Empty<VlatTexture>(),
                bands,
                layers);
        }

        private static double FindWeight(ResolvedState state, string layerId)
        {
            for (var index = 0; index < state.Count; index++)
            {
                if (string.Equals(state[index].Layer.Id, layerId, StringComparison.Ordinal))
                {
                    return state[index].Weight;
                }
            }

            Assert.Fail("Layer '{0}' was not present in the resolved state.", layerId);
            return 0d;
        }
    }
}
