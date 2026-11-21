using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using VLat.Core.Serialization;

namespace VLat.Core.Model
{
    /// <summary>VLat Modifierの種別名を提供します。</summary>
    public static class VlatModifierTypes
    {
        /// <summary>頂点差分Modifierの種別名です。</summary>
        public const string VertexDelta = "vertexDelta";

        /// <summary>可視性Modifierの種別名です。</summary>
        public const string Visibility = "visibility";

        /// <summary>透明度Modifierの種別名です。</summary>
        public const string Opacity = "opacity";

        /// <summary>マテリアルプロパティModifierの種別名です。</summary>
        public const string MaterialProperty = "materialProperty";

        /// <summary>描画順Modifierの種別名です。</summary>
        public const string RenderQueue = "renderQueue";
    }

    /// <summary>VLatレイヤが対象へ加える補正の基底クラスです。</summary>
    [JsonConverter(typeof(VlatModifierJsonConverter))]
    public abstract class VlatModifier
    {
        /// <summary>Modifierを生成します。</summary>
        /// <param name="type">判別に使用する種別名。</param>
        protected VlatModifier(string type)
        {
            Type = type;
        }

        /// <summary>判別に使用する種別名を取得します。</summary>
        [JsonProperty("type", Order = 0)]
        public string Type { get; }
    }

    /// <summary>bufferViewを参照する数値配列を表します。</summary>
    public sealed class VlatAccessor
    {
        /// <summary>数値配列参照を生成します。</summary>
        /// <param name="bufferView">参照するbufferViewのインデックス。</param>
        /// <param name="componentType">要素の成分型。</param>
        [JsonConstructor]
        public VlatAccessor(int bufferView, string componentType)
        {
            BufferView = bufferView;
            ComponentType = componentType;
        }

        /// <summary>参照するbufferViewのインデックスを取得します。</summary>
        [JsonProperty("bufferView", Order = 0, Required = Required.Always)]
        public int BufferView { get; }

        /// <summary>要素の成分型を取得します。</summary>
        [JsonProperty("componentType", Order = 1, Required = Required.Always)]
        public string ComponentType { get; }
    }

    /// <summary>スパースな頂点差分を表します。</summary>
    public sealed class VertexDeltaModifier : VlatModifier
    {
        /// <summary>頂点差分Modifierを生成します。</summary>
        /// <param name="meshBinding">対象meshBindings配列のインデックス。</param>
        /// <param name="indices">対象頂点インデックス列。</param>
        /// <param name="positions">頂点位置差分列。</param>
        /// <param name="normals">任意の法線差分列。</param>
        [JsonConstructor]
        public VertexDeltaModifier(
            int meshBinding,
            VlatAccessor indices,
            VlatAccessor positions,
            VlatAccessor normals = null)
            : base(VlatModifierTypes.VertexDelta)
        {
            MeshBinding = meshBinding;
            Indices = indices;
            Positions = positions;
            Normals = normals;
        }

        /// <summary>対象meshBindings配列のインデックスを取得します。</summary>
        [JsonProperty("meshBinding", Order = 1, Required = Required.Always)]
        public int MeshBinding { get; }

        /// <summary>対象頂点インデックス列を取得します。</summary>
        [JsonProperty("indices", Order = 2, Required = Required.Always)]
        public VlatAccessor Indices { get; }

        /// <summary>頂点位置差分列を取得します。</summary>
        [JsonProperty("positions", Order = 3, Required = Required.Always)]
        public VlatAccessor Positions { get; }

        /// <summary>任意の法線差分列を取得します。</summary>
        [JsonProperty("normals", Order = 4, NullValueHandling = NullValueHandling.Ignore, Required = Required.DisallowNull)]
        public VlatAccessor Normals { get; }
    }

    /// <summary>メッシュまたはサブメッシュの可視性補正を表します。</summary>
    public sealed class VisibilityModifier : VlatModifier
    {
        /// <summary>可視性Modifierを生成します。</summary>
        /// <param name="meshBinding">対象meshBindings配列のインデックス。</param>
        /// <param name="subMesh">対象サブメッシュのインデックス。</param>
        /// <param name="visibleInBand">角度帯内で表示するかどうか。</param>
        [JsonConstructor]
        public VisibilityModifier(int meshBinding, int subMesh, bool visibleInBand)
            : base(VlatModifierTypes.Visibility)
        {
            MeshBinding = meshBinding;
            SubMesh = subMesh;
            VisibleInBand = visibleInBand;
        }

        /// <summary>対象meshBindings配列のインデックスを取得します。</summary>
        [JsonProperty("meshBinding", Order = 1, Required = Required.Always)]
        public int MeshBinding { get; }

        /// <summary>対象サブメッシュのインデックスを取得します。</summary>
        [JsonProperty("subMesh", Order = 2, Required = Required.Always)]
        public int SubMesh { get; }

        /// <summary>角度帯内で表示するかどうかを取得します。</summary>
        [JsonProperty("visibleInBand", Order = 3, Required = Required.Always)]
        public bool VisibleInBand { get; }
    }

    /// <summary>マテリアルの透明度補正を表します。</summary>
    public sealed class OpacityModifier : VlatModifier
    {
        /// <summary>透明度Modifierを生成します。</summary>
        /// <param name="meshBinding">対象meshBindings配列のインデックス。</param>
        /// <param name="materialSlot">対象メッシュ内のマテリアルスロット番号。</param>
        /// <param name="value">角度帯内の透明度。</param>
        [JsonConstructor]
        public OpacityModifier(int meshBinding, int materialSlot, double value)
            : base(VlatModifierTypes.Opacity)
        {
            MeshBinding = meshBinding;
            MaterialSlot = materialSlot;
            Value = value;
        }

        /// <summary>対象meshBindings配列のインデックスを取得します。</summary>
        [JsonProperty("meshBinding", Order = 1, Required = Required.Always)]
        public int MeshBinding { get; }

        /// <summary>対象メッシュ内のマテリアルスロット番号を取得します。</summary>
        [JsonProperty("materialSlot", Order = 2, Required = Required.Always)]
        public int MaterialSlot { get; }

        /// <summary>角度帯内の透明度を取得します。</summary>
        [JsonProperty("value", Order = 3, Required = Required.Always)]
        public double Value { get; }
    }

    /// <summary>任意のマテリアルプロパティ補正を表します。</summary>
    public sealed class MaterialPropertyModifier : VlatModifier
    {
        /// <summary>マテリアルプロパティModifierを生成します。</summary>
        /// <param name="meshBinding">対象meshBindings配列のインデックス。</param>
        /// <param name="materialSlot">対象メッシュ内のマテリアルスロット番号。</param>
        /// <param name="property">シェーダープロパティ名。</param>
        /// <param name="valueType">値の型名。</param>
        /// <param name="bandValue">角度帯内での値。</param>
        /// <param name="interpolate">連続補間するかどうか。</param>
        [JsonConstructor]
        public MaterialPropertyModifier(
            int meshBinding,
            int materialSlot,
            string property,
            string valueType,
            VlatMaterialValue bandValue,
            bool interpolate)
            : base(VlatModifierTypes.MaterialProperty)
        {
            MeshBinding = meshBinding;
            MaterialSlot = materialSlot;
            Property = property;
            ValueType = valueType;
            BandValue = bandValue;
            Interpolate = interpolate;
        }

        /// <summary>対象meshBindings配列のインデックスを取得します。</summary>
        [JsonProperty("meshBinding", Order = 1, Required = Required.Always)]
        public int MeshBinding { get; }

        /// <summary>対象メッシュ内のマテリアルスロット番号を取得します。</summary>
        [JsonProperty("materialSlot", Order = 2, Required = Required.Always)]
        public int MaterialSlot { get; }

        /// <summary>シェーダープロパティ名を取得します。</summary>
        [JsonProperty("property", Order = 3, Required = Required.Always)]
        public string Property { get; }

        /// <summary>値の型名を取得します。</summary>
        [JsonProperty("valueType", Order = 4, Required = Required.Always)]
        public string ValueType { get; }

        /// <summary>角度帯内での値を取得します。</summary>
        [JsonProperty("bandValue", Order = 5, Required = Required.Always)]
        public VlatMaterialValue BandValue { get; }

        /// <summary>連続補間するかどうかを取得します。</summary>
        [JsonProperty("interpolate", Order = 6, Required = Required.Always)]
        public bool Interpolate { get; }
    }

    /// <summary>マテリアルのrenderQueueオフセットを表します。</summary>
    public sealed class RenderQueueModifier : VlatModifier
    {
        /// <summary>描画順Modifierを生成します。</summary>
        /// <param name="meshBinding">対象meshBindings配列のインデックス。</param>
        /// <param name="materialSlot">対象メッシュ内のマテリアルスロット番号。</param>
        /// <param name="offset">元のrenderQueueに加える値。</param>
        [JsonConstructor]
        public RenderQueueModifier(int meshBinding, int materialSlot, int offset)
            : base(VlatModifierTypes.RenderQueue)
        {
            MeshBinding = meshBinding;
            MaterialSlot = materialSlot;
            Offset = offset;
        }

        /// <summary>対象meshBindings配列のインデックスを取得します。</summary>
        [JsonProperty("meshBinding", Order = 1, Required = Required.Always)]
        public int MeshBinding { get; }

        /// <summary>対象メッシュ内のマテリアルスロット番号を取得します。</summary>
        [JsonProperty("materialSlot", Order = 2, Required = Required.Always)]
        public int MaterialSlot { get; }

        /// <summary>元のrenderQueueに加える値を取得します。</summary>
        [JsonProperty("offset", Order = 3, Required = Required.Always)]
        public int Offset { get; }
    }

    /// <summary>materialProperty Modifierが保持する値を表します。</summary>
    [JsonConverter(typeof(VlatMaterialValueJsonConverter))]
    public sealed class VlatMaterialValue
    {
        private readonly double[] _components;

        /// <summary>スカラー値を生成します。</summary>
        /// <param name="scalar">保持する数値。</param>
        public VlatMaterialValue(double scalar)
        {
            IsScalar = true;
            Scalar = scalar;
            _components = Array.Empty<double>();
        }

        /// <summary>ベクトルまたはカラーの成分値を生成します。</summary>
        /// <param name="components">保持する成分値。</param>
        public VlatMaterialValue(IEnumerable<double> components)
        {
            IsScalar = false;
            Scalar = 0d;

            if (components == null)
            {
                _components = Array.Empty<double>();
                return;
            }

            var collection = components as ICollection<double>;
            if (collection != null)
            {
                _components = new double[collection.Count];
                collection.CopyTo(_components, 0);
                return;
            }

            var values = new List<double>();
            foreach (var component in components)
            {
                values.Add(component);
            }

            _components = values.ToArray();
        }

        /// <summary>値がスカラー形式かどうかを取得します。</summary>
        [JsonIgnore]
        public bool IsScalar { get; }

        /// <summary>スカラー値を取得します。スカラー形式でない場合は0を返します。</summary>
        [JsonIgnore]
        public double Scalar { get; }

        /// <summary>ベクトルまたはカラーの成分数を取得します。</summary>
        [JsonIgnore]
        public int ComponentCount => _components.Length;

        /// <summary>指定位置のベクトルまたはカラー成分を取得します。</summary>
        /// <param name="index">取得する成分のインデックス。</param>
        /// <returns>指定位置の成分値。</returns>
        public double GetComponent(int index)
        {
            return _components[index];
        }
    }
}
