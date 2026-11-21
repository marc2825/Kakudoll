using System.Collections.Generic;
using Newtonsoft.Json;

namespace VLat.Core.Model
{
    /// <summary>角度の最小値と最大値を度数で表します。</summary>
    public sealed class AngleRange
    {
        /// <summary>角度範囲を生成します。</summary>
        /// <param name="min">最小角度。</param>
        /// <param name="max">最大角度。</param>
        [JsonConstructor]
        public AngleRange(double min, double max)
        {
            Min = min;
            Max = max;
        }

        /// <summary>最小角度を取得します。</summary>
        [JsonProperty("min", Order = 0, Required = Required.Always)]
        public double Min { get; }

        /// <summary>最大角度を取得します。</summary>
        [JsonProperty("max", Order = 1, Required = Required.Always)]
        public double Max { get; }
    }

    /// <summary>yaw軸とpitch軸の遷移幅を度数で表します。</summary>
    public sealed class AngleFeather
    {
        /// <summary>遷移幅を生成します。</summary>
        /// <param name="yaw">yaw軸の遷移幅。</param>
        /// <param name="pitch">pitch軸の遷移幅。</param>
        [JsonConstructor]
        public AngleFeather(double yaw, double pitch)
        {
            Yaw = yaw;
            Pitch = pitch;
        }

        /// <summary>yaw軸の遷移幅を取得します。</summary>
        [JsonProperty("yaw", Order = 0, Required = Required.Always)]
        public double Yaw { get; }

        /// <summary>pitch軸の遷移幅を取得します。</summary>
        [JsonProperty("pitch", Order = 1, Required = Required.Always)]
        public double Pitch { get; }
    }

    /// <summary>補正が有効になるyaw/pitch空間上の角度帯を表します。</summary>
    public sealed class AngleBand
    {
        /// <summary>角度帯を生成します。</summary>
        /// <param name="id">ドキュメント内で一意なID。</param>
        /// <param name="name">表示名。</param>
        /// <param name="yaw">yaw角度範囲。</param>
        /// <param name="pitch">pitch角度範囲。</param>
        /// <param name="feather">境界外側への遷移幅。</param>
        /// <param name="hysteresis">visibility用のヒステリシス幅。</param>
        [JsonConstructor]
        public AngleBand(
            string id,
            string name,
            AngleRange yaw,
            AngleRange pitch,
            AngleFeather feather,
            double hysteresis = 0d)
        {
            Id = id;
            Name = name;
            Yaw = yaw;
            Pitch = pitch;
            Feather = feather;
            Hysteresis = hysteresis;
        }

        /// <summary>ドキュメント内で一意なIDを取得します。</summary>
        [JsonProperty("id", Order = 0, Required = Required.Always)]
        public string Id { get; }

        /// <summary>表示名を取得します。</summary>
        [JsonProperty("name", Order = 1, Required = Required.Always)]
        public string Name { get; }

        /// <summary>yaw角度範囲を取得します。</summary>
        [JsonProperty("yaw", Order = 2, Required = Required.Always)]
        public AngleRange Yaw { get; }

        /// <summary>pitch角度範囲を取得します。</summary>
        [JsonProperty("pitch", Order = 3, Required = Required.Always)]
        public AngleRange Pitch { get; }

        /// <summary>境界外側への遷移幅を取得します。</summary>
        [JsonProperty("feather", Order = 4, Required = Required.Always)]
        public AngleFeather Feather { get; }

        /// <summary>visibility用のヒステリシス幅を取得します。</summary>
        [JsonProperty("hysteresis", Order = 5, DefaultValueHandling = DefaultValueHandling.Ignore, Required = Required.DisallowNull)]
        public double Hysteresis { get; }
    }

    /// <summary>1つの角度帯に結び付いたModifier群を表します。</summary>
    public sealed class VlatLayer
    {
        /// <summary>差分レイヤを生成します。</summary>
        /// <param name="id">ドキュメント内で一意なID。</param>
        /// <param name="name">表示名。</param>
        /// <param name="tag">部位を表す意味タグ。</param>
        /// <param name="enabled">レイヤが有効かどうか。</param>
        /// <param name="priority">競合解決に使用する優先度。</param>
        /// <param name="bandId">参照するAngleBandのID。</param>
        /// <param name="modifiers">レイヤに含まれるModifier。</param>
        [JsonConstructor]
        public VlatLayer(
            string id,
            string name,
            string tag,
            bool enabled,
            int priority,
            string bandId,
            IEnumerable<VlatModifier> modifiers)
        {
            Id = id;
            Name = name;
            Tag = tag;
            Enabled = enabled;
            Priority = priority;
            BandId = bandId;
            Modifiers = ImmutableModelCollection.Copy(modifiers);
        }

        /// <summary>ドキュメント内で一意なIDを取得します。</summary>
        [JsonProperty("id", Order = 0, Required = Required.Always)]
        public string Id { get; }

        /// <summary>表示名を取得します。</summary>
        [JsonProperty("name", Order = 1, Required = Required.Always)]
        public string Name { get; }

        /// <summary>部位を表す意味タグを取得します。</summary>
        [JsonProperty("tag", Order = 2, Required = Required.Always)]
        public string Tag { get; }

        /// <summary>レイヤが有効かどうかを取得します。</summary>
        [JsonProperty("enabled", Order = 3, Required = Required.Always)]
        public bool Enabled { get; }

        /// <summary>競合解決に使用する優先度を取得します。</summary>
        [JsonProperty("priority", Order = 4, Required = Required.Always)]
        public int Priority { get; }

        /// <summary>参照するAngleBandのIDを取得します。</summary>
        [JsonProperty("bandId", Order = 5, Required = Required.Always)]
        public string BandId { get; }

        /// <summary>レイヤに含まれるModifierを取得します。</summary>
        [JsonProperty("modifiers", Order = 6, Required = Required.Always)]
        public IReadOnlyList<VlatModifier> Modifiers { get; }
    }
}
