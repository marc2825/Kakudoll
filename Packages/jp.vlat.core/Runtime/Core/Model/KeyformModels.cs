using System.Collections.Generic;
using Newtonsoft.Json;

namespace VLat.Core.Model
{
    /// <summary>同一基準メッシュに対する、局所Yaw補間のキー集合を表します。</summary>
    public sealed class KeyformGroup
    {
        /// <summary>キー集合を生成します。保存・評価前にドキュメント検証が必要です。</summary>
        [JsonConstructor]
        public KeyformGroup(string id, int meshBinding, string topologyHash,
            string coordinateSpace, string interpolation, double supportStart,
            double supportEnd, IEnumerable<YawKeyform> keys)
        {
            Id = id;
            MeshBinding = meshBinding;
            TopologyHash = topologyHash;
            CoordinateSpace = coordinateSpace;
            Interpolation = interpolation;
            SupportStart = supportStart;
            SupportEnd = supportEnd;
            Keys = ImmutableModelCollection.Copy(keys);
        }

        /// <summary>ドキュメント内で一意なグループ識別子を取得します。</summary>
        [JsonProperty("id", Order = 0, Required = Required.Always)]
        public string Id { get; }

        /// <summary>共通基準メッシュのバインディング番号を取得します。</summary>
        [JsonProperty("meshBinding", Order = 1, Required = Required.Always)]
        public int MeshBinding { get; }

        /// <summary>サブメッシュ順・三角形頂点列のSHA-256を取得します。</summary>
        [JsonProperty("topologyHash", Order = 2, Required = Required.Always)]
        public string TopologyHash { get; }

        /// <summary>差分座標系を取得します。初期実装はbind-mesh-localのみです。</summary>
        [JsonProperty("coordinateSpace", Order = 3, Required = Required.Always)]
        public string CoordinateSpace { get; }

        /// <summary>補間方式を取得します。初期実装はyaw-smoothstepのみです。</summary>
        [JsonProperty("interpolation", Order = 4, Required = Required.Always)]
        public string Interpolation { get; }

        /// <summary>ゼロ差分へ戻る支持区間の開始角度を度単位で取得します。</summary>
        [JsonProperty("supportStart", Order = 5, Required = Required.Always)]
        public double SupportStart { get; }

        /// <summary>開始角度から正方向に展開した支持区間の終了角度を取得します。</summary>
        [JsonProperty("supportEnd", Order = 6, Required = Required.Always)]
        public double SupportEnd { get; }

        /// <summary>支持区間内でYaw昇順に並んだキーを取得します。</summary>
        [JsonProperty("keys", Order = 7, Required = Required.Always)]
        public IReadOnlyList<YawKeyform> Keys { get; }
    }

    /// <summary>一つのYaw方向と、その方向で厳密に再現する頂点差分を表します。</summary>
    public sealed class YawKeyform
    {
        /// <summary>キーを生成します。deltaがnullの場合は明示的な中立キーです。</summary>
        [JsonConstructor]
        public YawKeyform(double yaw, VertexDeltaModifier delta)
        {
            Yaw = yaw;
            Delta = delta;
        }

        /// <summary>支持区間と同じ展開済みYaw角度を度単位で取得します。</summary>
        [JsonProperty("yaw", Order = 0, Required = Required.Always)]
        public double Yaw { get; }

        /// <summary>共通基準メッシュに対する差分を取得します。nullは中立形状です。</summary>
        [JsonProperty("delta", Order = 1, Required = Required.AllowNull, NullValueHandling = NullValueHandling.Include)]
        public VertexDeltaModifier Delta { get; }
    }
}
