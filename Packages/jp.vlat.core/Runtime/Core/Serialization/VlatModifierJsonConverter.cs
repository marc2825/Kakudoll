using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VLat.Core.Model;

namespace VLat.Core.Serialization
{
    /// <summary>typeプロパティを使ってVLatModifierの派生型を読み書きします。</summary>
    public sealed class VlatModifierJsonConverter : JsonConverter
    {
        /// <summary>指定された型をこのconverterが処理できるかどうかを返します。</summary>
        /// <param name="objectType">判定する型。</param>
        /// <returns>VlatModifierまたはその派生型の場合はtrue。</returns>
        public override bool CanConvert(Type objectType)
        {
            return typeof(VlatModifier).IsAssignableFrom(objectType);
        }

        /// <summary>JSONからVlatModifierの派生型を読み取ります。</summary>
        /// <param name="reader">JSON reader。</param>
        /// <param name="objectType">要求された型。</param>
        /// <param name="existingValue">既存値。</param>
        /// <param name="serializer">親serializer。</param>
        /// <returns>読み取ったModifier。</returns>
        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            JObject modifier;
            try
            {
                modifier = JObject.Load(reader);
            }
            catch (JsonException)
            {
                throw;
            }

            var type = ReadRequiredString(modifier, "type");
            switch (type)
            {
                case VlatModifierTypes.VertexDelta:
                    ValidateAllowedProperties(
                        modifier,
                        "type",
                        "meshBinding",
                        "indices",
                        "positions",
                        "normals");
                    return new VertexDeltaModifier(
                        ReadRequiredInt32(modifier, "meshBinding"),
                        ReadRequiredAccessor(modifier, "indices"),
                        ReadRequiredAccessor(modifier, "positions"),
                        ReadOptionalAccessor(modifier, "normals"));

                case VlatModifierTypes.Visibility:
                    ValidateAllowedProperties(
                        modifier,
                        "type",
                        "meshBinding",
                        "subMesh",
                        "visibleInBand");
                    return new VisibilityModifier(
                        ReadRequiredInt32(modifier, "meshBinding"),
                        ReadRequiredInt32(modifier, "subMesh"),
                        ReadRequiredBoolean(modifier, "visibleInBand"));

                case VlatModifierTypes.Opacity:
                    ValidateAllowedProperties(
                        modifier,
                        "type",
                        "meshBinding",
                        "materialSlot",
                        "value");
                    return new OpacityModifier(
                        ReadRequiredInt32(modifier, "meshBinding"),
                        ReadRequiredInt32(modifier, "materialSlot"),
                        ReadRequiredDouble(modifier, "value"));

                case VlatModifierTypes.MaterialProperty:
                    ValidateAllowedProperties(
                        modifier,
                        "type",
                        "meshBinding",
                        "materialSlot",
                        "property",
                        "valueType",
                        "bandValue",
                        "interpolate");
                    return new MaterialPropertyModifier(
                        ReadRequiredInt32(modifier, "meshBinding"),
                        ReadRequiredInt32(modifier, "materialSlot"),
                        ReadRequiredString(modifier, "property"),
                        ReadRequiredString(modifier, "valueType"),
                        ReadRequiredMaterialValue(modifier, "bandValue", serializer),
                        ReadRequiredBoolean(modifier, "interpolate"));

                case VlatModifierTypes.RenderQueue:
                    ValidateAllowedProperties(
                        modifier,
                        "type",
                        "meshBinding",
                        "materialSlot",
                        "offset");
                    return new RenderQueueModifier(
                        ReadRequiredInt32(modifier, "meshBinding"),
                        ReadRequiredInt32(modifier, "materialSlot"),
                        ReadRequiredInt32(modifier, "offset"));

                default:
                    throw CreateSerializationException(
                        modifier["type"] ?? modifier,
                        "未対応のModifier typeです: " + type,
                        null);
            }
        }

        /// <summary>VlatModifierの派生型をJSONへ書き込みます。</summary>
        /// <param name="writer">JSON writer。</param>
        /// <param name="value">書き込むModifier。</param>
        /// <param name="serializer">親serializer。</param>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteStartObject();
            WriteProperty(writer, "type", ((VlatModifier)value).Type);

            var vertexDelta = value as VertexDeltaModifier;
            if (vertexDelta != null)
            {
                WriteProperty(writer, "meshBinding", vertexDelta.MeshBinding);
                WriteAccessor(writer, "indices", vertexDelta.Indices);
                WriteAccessor(writer, "positions", vertexDelta.Positions);
                if (vertexDelta.Normals != null)
                {
                    WriteAccessor(writer, "normals", vertexDelta.Normals);
                }

                writer.WriteEndObject();
                return;
            }

            var visibility = value as VisibilityModifier;
            if (visibility != null)
            {
                WriteProperty(writer, "meshBinding", visibility.MeshBinding);
                WriteProperty(writer, "subMesh", visibility.SubMesh);
                WriteProperty(writer, "visibleInBand", visibility.VisibleInBand);
                writer.WriteEndObject();
                return;
            }

            var opacity = value as OpacityModifier;
            if (opacity != null)
            {
                WriteProperty(writer, "meshBinding", opacity.MeshBinding);
                WriteProperty(writer, "materialSlot", opacity.MaterialSlot);
                WriteProperty(writer, "value", opacity.Value);
                writer.WriteEndObject();
                return;
            }

            var materialProperty = value as MaterialPropertyModifier;
            if (materialProperty != null)
            {
                WriteProperty(writer, "meshBinding", materialProperty.MeshBinding);
                WriteProperty(writer, "materialSlot", materialProperty.MaterialSlot);
                WriteProperty(writer, "property", materialProperty.Property);
                WriteProperty(writer, "valueType", materialProperty.ValueType);
                writer.WritePropertyName("bandValue");
                serializer.Serialize(writer, materialProperty.BandValue);
                WriteProperty(writer, "interpolate", materialProperty.Interpolate);
                writer.WriteEndObject();
                return;
            }

            var renderQueue = value as RenderQueueModifier;
            if (renderQueue != null)
            {
                WriteProperty(writer, "meshBinding", renderQueue.MeshBinding);
                WriteProperty(writer, "materialSlot", renderQueue.MaterialSlot);
                WriteProperty(writer, "offset", renderQueue.Offset);
                writer.WriteEndObject();
                return;
            }

            throw new JsonSerializationException(
                "未対応のVlatModifier派生型です: " + value.GetType().FullName);
        }

        private static VlatAccessor ReadRequiredAccessor(JObject owner, string propertyName)
        {
            var token = ReadRequiredToken(owner, propertyName);
            if (token.Type != JTokenType.Object)
            {
                throw CreateSerializationException(token, propertyName + "はobjectである必要があります。", null);
            }

            var value = (JObject)token;
            ValidateAllowedProperties(value, "bufferView", "componentType");
            return new VlatAccessor(
                ReadRequiredInt32(value, "bufferView"),
                ReadRequiredString(value, "componentType"));
        }

        private static VlatAccessor ReadOptionalAccessor(JObject owner, string propertyName)
        {
            var token = owner[propertyName];
            if (token == null)
            {
                return null;
            }

            if (token.Type == JTokenType.Null)
            {
                throw CreateSerializationException(
                    token,
                    propertyName + "は省略できますが、明示的なnullにはできません。",
                    null);
            }

            if (token.Type != JTokenType.Object)
            {
                throw CreateSerializationException(token, propertyName + "はobjectである必要があります。", null);
            }

            var value = (JObject)token;
            ValidateAllowedProperties(value, "bufferView", "componentType");
            return new VlatAccessor(
                ReadRequiredInt32(value, "bufferView"),
                ReadRequiredString(value, "componentType"));
        }

        private static VlatMaterialValue ReadRequiredMaterialValue(
            JObject owner,
            string propertyName,
            JsonSerializer serializer)
        {
            var token = ReadRequiredToken(owner, propertyName);
            try
            {
                return token.ToObject<VlatMaterialValue>(serializer);
            }
            catch (JsonSerializationException exception)
            {
                throw CreateSerializationException(token, exception.Message, exception);
            }
        }

        private static string ReadRequiredString(JObject owner, string propertyName)
        {
            var token = ReadRequiredToken(owner, propertyName);
            if (token.Type != JTokenType.String)
            {
                throw CreateSerializationException(token, propertyName + "はstringである必要があります。", null);
            }

            return (string)token;
        }

        private static int ReadRequiredInt32(JObject owner, string propertyName)
        {
            var token = ReadRequiredToken(owner, propertyName);
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            {
                throw CreateSerializationException(token, propertyName + "はintegerである必要があります。", null);
            }

            try
            {
                if (token.Type == JTokenType.Float)
                {
                    var numericValue = token.ToObject<double>();
                    if (double.IsNaN(numericValue) || double.IsInfinity(numericValue) ||
                        Math.Truncate(numericValue) != numericValue ||
                        numericValue < int.MinValue || numericValue > int.MaxValue)
                    {
                        throw CreateSerializationException(token, propertyName + "はintegerである必要があります。", null);
                    }
                }

                return token.ToObject<int>();
            }
            catch (Exception exception)
            {
                throw CreateSerializationException(token, propertyName + "がInt32の範囲外です。", exception);
            }
        }

        private static double ReadRequiredDouble(JObject owner, string propertyName)
        {
            var token = ReadRequiredToken(owner, propertyName);
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            {
                throw CreateSerializationException(token, propertyName + "はnumberである必要があります。", null);
            }

            try
            {
                return token.ToObject<double>();
            }
            catch (Exception exception)
            {
                throw CreateSerializationException(token, propertyName + "を数値として読み取れません。", exception);
            }
        }

        private static bool ReadRequiredBoolean(JObject owner, string propertyName)
        {
            var token = ReadRequiredToken(owner, propertyName);
            if (token.Type != JTokenType.Boolean)
            {
                throw CreateSerializationException(token, propertyName + "はbooleanである必要があります。", null);
            }

            return (bool)token;
        }

        private static JToken ReadRequiredToken(JObject owner, string propertyName)
        {
            var token = owner[propertyName];
            if (token == null || token.Type == JTokenType.Null)
            {
                throw CreateSerializationException(
                    owner,
                    "必須プロパティがありません: " + propertyName,
                    null,
                    AppendPath(owner.Path, propertyName));
            }

            return token;
        }

        private static void ValidateAllowedProperties(JObject value, params string[] allowedProperties)
        {
            foreach (var property in value.Properties())
            {
                var allowed = false;
                for (var i = 0; i < allowedProperties.Length; i++)
                {
                    if (string.Equals(property.Name, allowedProperties[i], StringComparison.Ordinal))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed)
                {
                    throw CreateSerializationException(
                        property,
                        "未定義のプロパティです: " + property.Name,
                        null);
                }
            }
        }

        private static JsonSerializationException CreateSerializationException(
            JToken token,
            string message,
            Exception innerException,
            string pathOverride = null)
        {
            var lineInfo = token as IJsonLineInfo;
            var line = lineInfo != null && lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0;
            var column = lineInfo != null && lineInfo.HasLineInfo() ? lineInfo.LinePosition : 0;
            var path = pathOverride ?? token.Path;
            return new JsonSerializationException(message, path, line, column, innerException);
        }

        private static string AppendPath(string path, string propertyName)
        {
            if (string.IsNullOrEmpty(path))
            {
                return propertyName;
            }

            return path + "." + propertyName;
        }

        private static void WriteAccessor(JsonWriter writer, string name, VlatAccessor accessor)
        {
            writer.WritePropertyName(name);
            if (accessor == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteStartObject();
            WriteProperty(writer, "bufferView", accessor.BufferView);
            WriteProperty(writer, "componentType", accessor.ComponentType);
            writer.WriteEndObject();
        }

        private static void WriteProperty(JsonWriter writer, string name, string value)
        {
            writer.WritePropertyName(name);
            writer.WriteValue(value);
        }

        private static void WriteProperty(JsonWriter writer, string name, int value)
        {
            writer.WritePropertyName(name);
            writer.WriteValue(value);
        }

        private static void WriteProperty(JsonWriter writer, string name, double value)
        {
            writer.WritePropertyName(name);
            writer.WriteValue(value);
        }

        private static void WriteProperty(JsonWriter writer, string name, bool value)
        {
            writer.WritePropertyName(name);
            writer.WriteValue(value);
        }
    }

    /// <summary>materialPropertyのスカラー値または数値配列を読み書きします。</summary>
    public sealed class VlatMaterialValueJsonConverter : JsonConverter
    {
        /// <summary>指定された型をこのconverterが処理できるかどうかを返します。</summary>
        /// <param name="objectType">判定する型。</param>
        /// <returns>VlatMaterialValueの場合はtrue。</returns>
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(VlatMaterialValue);
        }

        /// <summary>JSONのnumberまたはnumber配列を読み取ります。</summary>
        /// <param name="reader">JSON reader。</param>
        /// <param name="objectType">要求された型。</param>
        /// <param name="existingValue">既存値。</param>
        /// <param name="serializer">親serializer。</param>
        /// <returns>読み取ったマテリアル値。</returns>
        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            if (reader.TokenType == JsonToken.Integer || reader.TokenType == JsonToken.Float)
            {
                return new VlatMaterialValue(Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture));
            }

            if (reader.TokenType != JsonToken.StartArray)
            {
                throw new JsonSerializationException(
                    "bandValueはnumberまたはnumber配列である必要があります。",
                    reader.Path,
                    reader is IJsonLineInfo info && info.HasLineInfo() ? info.LineNumber : 0,
                    reader is IJsonLineInfo columnInfo && columnInfo.HasLineInfo() ? columnInfo.LinePosition : 0,
                    null);
            }

            var array = JArray.Load(reader);
            var components = new double[array.Count];
            for (var i = 0; i < array.Count; i++)
            {
                var token = array[i];
                if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                {
                    var lineInfo = token as IJsonLineInfo;
                    throw new JsonSerializationException(
                        "bandValueの全成分はnumberである必要があります。",
                        token.Path,
                        lineInfo != null && lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0,
                        lineInfo != null && lineInfo.HasLineInfo() ? lineInfo.LinePosition : 0,
                        null);
                }

                components[i] = token.ToObject<double>();
            }

            return new VlatMaterialValue(components);
        }

        /// <summary>マテリアル値をJSONのnumberまたはnumber配列として書き込みます。</summary>
        /// <param name="writer">JSON writer。</param>
        /// <param name="value">書き込む値。</param>
        /// <param name="serializer">親serializer。</param>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var materialValue = value as VlatMaterialValue;
            if (materialValue == null)
            {
                writer.WriteNull();
                return;
            }

            if (materialValue.IsScalar)
            {
                writer.WriteValue(materialValue.Scalar);
                return;
            }

            writer.WriteStartArray();
            for (var i = 0; i < materialValue.ComponentCount; i++)
            {
                writer.WriteValue(materialValue.GetComponent(i));
            }

            writer.WriteEndArray();
        }
    }
}
