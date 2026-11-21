using System;
using System.Collections.Generic;
using VLat.Core.Model;

namespace VLat.Core.Evaluation
{
    /// <summary>競合解決後の1つのModifierと実効重みを表します。</summary>
    public readonly struct ResolvedModifier
    {
        /// <summary>解決済みModifierを生成します。</summary>
        /// <param name="layer">所属レイヤ。</param>
        /// <param name="modifier">適用対象のModifier。</param>
        /// <param name="weight">実効重み。</param>
        /// <param name="groupId">競合ターゲットの内部ID。</param>
        /// <param name="isSwitch">スイッチ型Modifierかどうか。</param>
        public ResolvedModifier(VlatLayer layer, VlatModifier modifier, double weight, int groupId, bool isSwitch)
        {
            Layer = layer;
            Modifier = modifier;
            Weight = weight;
            GroupId = groupId;
            IsSwitch = isSwitch;
        }

        /// <summary>所属レイヤを取得します。</summary>
        public VlatLayer Layer { get; }

        /// <summary>適用対象のModifierを取得します。</summary>
        public VlatModifier Modifier { get; }

        /// <summary>実効重みを取得します。</summary>
        public double Weight { get; }

        /// <summary>競合ターゲットのIDを取得します。vertexDeltaでは-1です。</summary>
        public int GroupId { get; }

        /// <summary>スイッチ型Modifierかどうかを取得します。</summary>
        public bool IsSwitch { get; }
    }

    /// <summary>競合解決結果を再利用可能なバッファとして保持します。</summary>
    public sealed class ResolvedState
    {
        private readonly ConflictResolver _owner;
        private readonly ResolvedModifier[] _items;
        private readonly double[] _groupWeights;

        internal ResolvedState(ConflictResolver owner, int capacity, int groupCount)
        {
            _owner = owner;
            _items = new ResolvedModifier[capacity];
            _groupWeights = new double[groupCount];
        }

        /// <summary>現在の解決済みModifier数を取得します。</summary>
        public int Count { get; private set; }

        /// <summary>競合ターゲット数を取得します。</summary>
        public int GroupCount => _groupWeights.Length;

        /// <summary>指定位置の解決済みModifierを取得します。</summary>
        /// <param name="index">解決結果のインデックス。</param>
        /// <returns>解決済みModifier。</returns>
        public ResolvedModifier this[int index] => index >= 0 && index < Count
            ? _items[index]
            : throw new ArgumentOutOfRangeException(nameof(index));

        /// <summary>指定競合ターゲットに対するベース状態の混合重みを取得します。</summary>
        /// <param name="groupId">競合ターゲットID。</param>
        /// <returns>0から1のベース状態重み。</returns>
        public double GetBaseWeight(int groupId)
        {
            if (groupId < 0 || groupId >= _groupWeights.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(groupId));
            }

            return 1d - _groupWeights[groupId];
        }

        internal bool IsOwnedBy(ConflictResolver owner)
        {
            return ReferenceEquals(_owner, owner);
        }

        internal void Clear()
        {
            Count = 0;
            Array.Clear(_groupWeights, 0, _groupWeights.Length);
        }

        internal void Add(ResolvedModifier item)
        {
            _items[Count] = item;
            Count++;
        }

        internal void SetGroupWeight(int groupId, double weight)
        {
            _groupWeights[groupId] = weight;
        }
    }

    /// <summary>LayerとModifierのpriority占有・正規化ブレンドを解決します。</summary>
    public sealed class ConflictResolver
    {
        private readonly Entry[] _entries;
        private readonly AngleBand[] _bands;
        private readonly int[] _additiveEntryIndices;
        private readonly int[][] _groupEntryIndices;
        private readonly double[] _rawWeights;
        private readonly bool[] _switchStates;
        private readonly int _groupCount;

        /// <summary>対象VLatドキュメントを指定して競合解決器を生成します。</summary>
        /// <param name="document">対象ドキュメント。</param>
        public ConflictResolver(VlatDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            _bands = new AngleBand[document.AngleBands.Count];
            var bandIndices = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var bandIndex = 0; bandIndex < document.AngleBands.Count; bandIndex++)
            {
                var band = document.AngleBands[bandIndex];
                _bands[bandIndex] = band;
                if (band != null && band.Id != null && !bandIndices.ContainsKey(band.Id))
                {
                    bandIndices.Add(band.Id, bandIndex);
                }
            }

            var entries = new List<Entry>();
            var groups = new Dictionary<GroupKey, int>();
            for (var layerIndex = 0; layerIndex < document.Layers.Count; layerIndex++)
            {
                var layer = document.Layers[layerIndex];
                if (layer == null)
                {
                    continue;
                }

                var bandIndex = -1;
                if (layer.BandId != null && !bandIndices.TryGetValue(layer.BandId, out bandIndex))
                {
                    bandIndex = -1;
                }

                for (var modifierIndex = 0; modifierIndex < layer.Modifiers.Count; modifierIndex++)
                {
                    var modifier = layer.Modifiers[modifierIndex];
                    if (modifier == null)
                    {
                        continue;
                    }

                    var additive = modifier is VertexDeltaModifier;
                    var isSwitch = IsSwitchModifier(modifier);
                    var groupId = -1;
                    if (!additive)
                    {
                        var key = GroupKey.Create(modifier);
                        if (!groups.TryGetValue(key, out groupId))
                        {
                            groupId = groups.Count;
                            groups.Add(key, groupId);
                        }
                    }

                    entries.Add(new Entry(
                        layer,
                        modifier,
                        bandIndex,
                        groupId,
                        additive,
                        isSwitch,
                        modifier is VisibilityModifier ? new VisibilityHysteresis() : null));
                }
            }

            _entries = entries.ToArray();
            _groupCount = groups.Count;
            _rawWeights = new double[_entries.Length];
            _switchStates = new bool[_entries.Length];

            var additiveIndices = new List<int>();
            var groupedIndices = new List<int>[_groupCount];
            for (var groupIndex = 0; groupIndex < _groupCount; groupIndex++)
            {
                groupedIndices[groupIndex] = new List<int>();
            }

            for (var entryIndex = 0; entryIndex < _entries.Length; entryIndex++)
            {
                var entry = _entries[entryIndex];
                if (entry.Additive)
                {
                    additiveIndices.Add(entryIndex);
                }
                else
                {
                    groupedIndices[entry.GroupId].Add(entryIndex);
                }
            }

            _additiveEntryIndices = additiveIndices.ToArray();
            _groupEntryIndices = new int[_groupCount][];
            for (var groupIndex = 0; groupIndex < _groupCount; groupIndex++)
            {
                _groupEntryIndices[groupIndex] = groupedIndices[groupIndex].ToArray();
            }
        }

        /// <summary>競合解決時に再利用する出力バッファを生成します。</summary>
        /// <returns>この解決器専用の出力バッファ。</returns>
        public ResolvedState CreateState()
        {
            return new ResolvedState(this, _entries.Length, _groupCount);
        }

        /// <summary>帯重みとpriorityからModifierの実効重みを解決します。</summary>
        /// <param name="bandWeights">VlatDocument.angleBandsと同じ順序の帯重み。</param>
        /// <param name="destination">この解決器が生成した書き込み先。</param>
        public void Resolve(IReadOnlyList<double> bandWeights, ResolvedState destination)
        {
            ResolveInternal(default, false, bandWeights, destination);
        }

        /// <summary>visibilityのModifier別ヒステリシスを含めて実効重みを解決します。</summary>
        /// <param name="angles">現在の視点角度。</param>
        /// <param name="bandWeights">VlatDocument.angleBandsと同じ順序の帯重み。</param>
        /// <param name="destination">この解決器が生成した書き込み先。</param>
        public void Resolve(ViewAngles angles, IReadOnlyList<double> bandWeights, ResolvedState destination)
        {
            ResolveInternal(angles, true, bandWeights, destination);
        }

        /// <summary>すべてのvisibilityヒステリシス状態をOFFへ戻します。</summary>
        public void ResetHysteresis()
        {
            for (var index = 0; index < _entries.Length; index++)
            {
                var state = _entries[index].VisibilityState;
                if (state != null)
                {
                    state.Reset();
                }
            }
        }

        private void ResolveInternal(
            ViewAngles angles,
            bool useHysteresis,
            IReadOnlyList<double> bandWeights,
            ResolvedState destination)
        {
            if (bandWeights == null)
            {
                throw new ArgumentNullException(nameof(bandWeights));
            }

            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (!destination.IsOwnedBy(this))
            {
                throw new ArgumentException("別のConflictResolverが生成した出力バッファは使用できません。", nameof(destination));
            }

            destination.Clear();
            PrepareEntryStates(angles, useHysteresis, bandWeights);
            ResolveAdditiveEntries(destination);
            for (var groupId = 0; groupId < _groupCount; groupId++)
            {
                ResolveGroup(groupId, destination);
            }
        }

        private static bool IsSwitchModifier(VlatModifier modifier)
        {
            if (modifier is VisibilityModifier || modifier is RenderQueueModifier)
            {
                return true;
            }

            var material = modifier as MaterialPropertyModifier;
            return material != null
                && (!material.Interpolate || string.Equals(material.ValueType, "textureIndex", StringComparison.Ordinal));
        }

        private void PrepareEntryStates(
            ViewAngles angles,
            bool useHysteresis,
            IReadOnlyList<double> bandWeights)
        {
            for (var index = 0; index < _entries.Length; index++)
            {
                var entry = _entries[index];
                var weight = GetRawWeight(entry, bandWeights);
                _rawWeights[index] = weight;
                if (!entry.IsSwitch)
                {
                    _switchStates[index] = false;
                    continue;
                }

                var visibilityState = entry.VisibilityState;
                if (!useHysteresis || visibilityState == null)
                {
                    _switchStates[index] = weight >= 0.5d;
                    continue;
                }

                if (!entry.Layer.Enabled || entry.BandIndex < 0 || entry.BandIndex >= _bands.Length)
                {
                    visibilityState.Reset();
                    _switchStates[index] = false;
                    continue;
                }

                _switchStates[index] = visibilityState.Evaluate(
                    _bands[entry.BandIndex],
                    angles.Yaw,
                    angles.Pitch,
                    weight);
            }
        }

        private void ResolveAdditiveEntries(ResolvedState destination)
        {
            for (var listIndex = 0; listIndex < _additiveEntryIndices.Length; listIndex++)
            {
                var entryIndex = _additiveEntryIndices[listIndex];
                var weight = _rawWeights[entryIndex];
                if (weight > 0d)
                {
                    var entry = _entries[entryIndex];
                    destination.Add(new ResolvedModifier(entry.Layer, entry.Modifier, weight, -1, false));
                }
            }
        }

        private void ResolveGroup(int groupId, ResolvedState destination)
        {
            var indices = _groupEntryIndices[groupId];
            var maximumPriority = int.MinValue;
            for (var listIndex = 0; listIndex < indices.Length; listIndex++)
            {
                var entryIndex = indices[listIndex];
                var entry = _entries[entryIndex];
                if (_rawWeights[entryIndex] <= 0d && !_switchStates[entryIndex])
                {
                    continue;
                }

                if (entry.Layer.Priority > maximumPriority)
                {
                    maximumPriority = entry.Layer.Priority;
                }

            }

            if (maximumPriority == int.MinValue)
            {
                return;
            }

            var isSwitch = false;
            for (var listIndex = 0; listIndex < indices.Length; listIndex++)
            {
                var entryIndex = indices[listIndex];
                var entry = _entries[entryIndex];
                if (entry.Layer.Priority == maximumPriority
                    && (_rawWeights[entryIndex] > 0d || _switchStates[entryIndex])
                    && entry.IsSwitch)
                {
                    isSwitch = true;
                    break;
                }
            }

            if (isSwitch)
            {
                ResolveSwitchGroup(groupId, maximumPriority, indices, destination);
                return;
            }

            var sum = 0d;
            for (var listIndex = 0; listIndex < indices.Length; listIndex++)
            {
                var entryIndex = indices[listIndex];
                var entry = _entries[entryIndex];
                if (entry.Layer.Priority == maximumPriority)
                {
                    sum += _rawWeights[entryIndex];
                }
            }

            if (sum <= 0d)
            {
                return;
            }

            var scale = sum > 1d ? 1d / sum : 1d;
            for (var listIndex = 0; listIndex < indices.Length; listIndex++)
            {
                var entryIndex = indices[listIndex];
                var entry = _entries[entryIndex];
                if (entry.Layer.Priority != maximumPriority)
                {
                    continue;
                }

                var weight = _rawWeights[entryIndex] * scale;
                if (weight > 0d)
                {
                    destination.Add(new ResolvedModifier(entry.Layer, entry.Modifier, weight, groupId, false));
                }
            }

            destination.SetGroupWeight(groupId, Math.Min(1d, sum));
        }

        private void ResolveSwitchGroup(
            int groupId,
            int maximumPriority,
            int[] indices,
            ResolvedState destination)
        {
            var winnerIndex = -1;
            var winnerWeight = double.NegativeInfinity;
            for (var listIndex = 0; listIndex < indices.Length; listIndex++)
            {
                var entryIndex = indices[listIndex];
                var entry = _entries[entryIndex];
                if (entry.Layer.Priority != maximumPriority || !entry.IsSwitch || !_switchStates[entryIndex])
                {
                    continue;
                }

                var weight = _rawWeights[entryIndex];
                if (weight > winnerWeight)
                {
                    winnerIndex = entryIndex;
                    winnerWeight = weight;
                }
            }

            if (winnerIndex < 0)
            {
                return;
            }

            var winner = _entries[winnerIndex];
            destination.Add(new ResolvedModifier(winner.Layer, winner.Modifier, winnerWeight, groupId, true));
            destination.SetGroupWeight(groupId, 1d);
        }

        private static double GetRawWeight(Entry entry, IReadOnlyList<double> bandWeights)
        {
            if (!entry.Layer.Enabled || entry.BandIndex < 0 || entry.BandIndex >= bandWeights.Count)
            {
                return 0d;
            }

            var weight = bandWeights[entry.BandIndex];
            if (double.IsNaN(weight) || weight <= 0d)
            {
                return 0d;
            }

            return weight >= 1d ? 1d : weight;
        }

        private readonly struct Entry
        {
            public Entry(
                VlatLayer layer,
                VlatModifier modifier,
                int bandIndex,
                int groupId,
                bool additive,
                bool isSwitch,
                VisibilityHysteresis visibilityState)
            {
                Layer = layer;
                Modifier = modifier;
                BandIndex = bandIndex;
                GroupId = groupId;
                Additive = additive;
                IsSwitch = isSwitch;
                VisibilityState = visibilityState;
            }

            public VlatLayer Layer { get; }
            public VlatModifier Modifier { get; }
            public int BandIndex { get; }
            public int GroupId { get; }
            public bool Additive { get; }
            public bool IsSwitch { get; }
            public VisibilityHysteresis VisibilityState { get; }
        }

        private readonly struct GroupKey : IEquatable<GroupKey>
        {
            private GroupKey(int kind, int meshBinding, int targetIndex, string property)
            {
                Kind = kind;
                MeshBinding = meshBinding;
                TargetIndex = targetIndex;
                Property = property;
            }

            private int Kind { get; }
            private int MeshBinding { get; }
            private int TargetIndex { get; }
            private string Property { get; }

            public static GroupKey Create(VlatModifier modifier)
            {
                if (modifier is VisibilityModifier visibility)
                {
                    return new GroupKey(1, visibility.MeshBinding, visibility.SubMesh, null);
                }

                if (modifier is OpacityModifier opacity)
                {
                    return new GroupKey(2, opacity.MeshBinding, opacity.MaterialSlot, null);
                }

                if (modifier is MaterialPropertyModifier material)
                {
                    return new GroupKey(3, material.MeshBinding, material.MaterialSlot, material.Property);
                }

                if (modifier is RenderQueueModifier renderQueue)
                {
                    return new GroupKey(4, renderQueue.MeshBinding, renderQueue.MaterialSlot, null);
                }

                return new GroupKey(0, -1, -1, modifier.Type);
            }

            public bool Equals(GroupKey other)
            {
                return Kind == other.Kind
                    && MeshBinding == other.MeshBinding
                    && TargetIndex == other.TargetIndex
                    && string.Equals(Property, other.Property, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is GroupKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = Kind;
                    hash = (hash * 397) ^ MeshBinding;
                    hash = (hash * 397) ^ TargetIndex;
                    return (hash * 397) ^ (Property == null ? 0 : StringComparer.Ordinal.GetHashCode(Property));
                }
            }
        }
    }
}
