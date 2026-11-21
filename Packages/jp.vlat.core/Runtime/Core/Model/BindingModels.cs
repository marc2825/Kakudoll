using System.Collections.Generic;
using Newtonsoft.Json;

namespace VLat.Core.Model
{
    /// <summary>VLatドキュメントを適用するアバターを表します。</summary>
    public sealed class TargetAvatar
    {
        /// <summary>対象アバター情報を生成します。</summary>
        /// <param name="name">アバター名。</param>
        /// <param name="vrmVersion">対象VRMのバージョン。</param>
        /// <param name="meshBindings">対象メッシュのバインディング。</param>
        [JsonConstructor]
        public TargetAvatar(string name, string vrmVersion, IEnumerable<MeshBinding> meshBindings)
        {
            Name = name;
            VrmVersion = vrmVersion;
            MeshBindings = ImmutableModelCollection.Copy(meshBindings);
        }

        /// <summary>アバター名を取得します。</summary>
        [JsonProperty("name", Order = 0, Required = Required.Always)]
        public string Name { get; }

        /// <summary>対象VRMのバージョンを取得します。</summary>
        [JsonProperty("vrmVersion", Order = 1, Required = Required.Always)]
        public string VrmVersion { get; }

        /// <summary>対象メッシュのバインディングを取得します。</summary>
        [JsonProperty("meshBindings", Order = 2, Required = Required.Always)]
        public IReadOnlyList<MeshBinding> MeshBindings { get; }
    }

    /// <summary>アバター内の対象メッシュを再特定するための情報を表します。</summary>
    public sealed class MeshBinding
    {
        /// <summary>メッシュバインディングを生成します。</summary>
        /// <param name="hierarchyPath">アバタールートからのTransformパス。</param>
        /// <param name="meshName">メッシュ名。</param>
        /// <param name="vertexCount">バインド時の頂点数。</param>
        /// <param name="positionHash">バインドポーズ頂点座標のコンテンツハッシュ。</param>
        /// <param name="materials">メッシュのマテリアルスロット。</param>
        [JsonConstructor]
        public MeshBinding(
            string hierarchyPath,
            string meshName,
            int vertexCount,
            string positionHash,
            IEnumerable<VlatMaterialSlot> materials)
        {
            HierarchyPath = hierarchyPath;
            MeshName = meshName;
            VertexCount = vertexCount;
            PositionHash = positionHash;
            Materials = ImmutableModelCollection.Copy(materials);
        }

        /// <summary>アバタールートからのTransformパスを取得します。</summary>
        [JsonProperty("hierarchyPath", Order = 0, Required = Required.Always)]
        public string HierarchyPath { get; }

        /// <summary>メッシュ名を取得します。</summary>
        [JsonProperty("meshName", Order = 1, Required = Required.Always)]
        public string MeshName { get; }

        /// <summary>バインド時の頂点数を取得します。</summary>
        [JsonProperty("vertexCount", Order = 2, Required = Required.Always)]
        public int VertexCount { get; }

        /// <summary>バインドポーズ頂点座標のコンテンツハッシュを取得します。</summary>
        [JsonProperty("positionHash", Order = 3, Required = Required.Always)]
        public string PositionHash { get; }

        /// <summary>メッシュのマテリアルスロットを取得します。</summary>
        [JsonProperty("materials", Order = 4, Required = Required.Always)]
        public IReadOnlyList<VlatMaterialSlot> Materials { get; }
    }

    /// <summary>メッシュに割り当てられたマテリアルスロットを表します。</summary>
    public sealed class VlatMaterialSlot
    {
        /// <summary>マテリアルスロットを生成します。</summary>
        /// <param name="slot">メッシュ内のスロット番号。</param>
        /// <param name="materialName">マテリアル名。</param>
        /// <param name="shader">シェーダー名。</param>
        [JsonConstructor]
        public VlatMaterialSlot(int slot, string materialName, string shader)
        {
            Slot = slot;
            MaterialName = materialName;
            Shader = shader;
        }

        /// <summary>メッシュ内のスロット番号を取得します。</summary>
        [JsonProperty("slot", Order = 0, Required = Required.Always)]
        public int Slot { get; }

        /// <summary>マテリアル名を取得します。</summary>
        [JsonProperty("materialName", Order = 1, Required = Required.Always)]
        public string MaterialName { get; }

        /// <summary>シェーダー名を取得します。</summary>
        [JsonProperty("shader", Order = 2, Required = Required.Always)]
        public string Shader { get; }
    }
}
