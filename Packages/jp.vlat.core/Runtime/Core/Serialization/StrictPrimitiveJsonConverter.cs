using System;
using System.Globalization;
using Newtonsoft.Json;

namespace VLat.Core.Serialization
{
    internal sealed class StrictPrimitiveJsonConverter : JsonConverter
    {
        public override bool CanWrite => false;

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(string) ||
                   objectType == typeof(int) ||
                   objectType == typeof(int?) ||
                   objectType == typeof(long) ||
                   objectType == typeof(double) ||
                   objectType == typeof(bool);
        }

        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer)
        {
            if (objectType == typeof(string))
            {
                if (reader.TokenType == JsonToken.Null)
                {
                    return null;
                }

                RequireToken(reader, JsonToken.String, "string");
                return (string)reader.Value;
            }

            if (objectType == typeof(int?))
            {
                if (reader.TokenType == JsonToken.Null)
                {
                    return null;
                }

                return ConvertInteger<int>(reader, Convert.ToInt32);
            }

            if (objectType == typeof(int))
            {
                return ConvertInteger<int>(reader, Convert.ToInt32);
            }

            if (objectType == typeof(long))
            {
                return ConvertInteger<long>(reader, Convert.ToInt64);
            }

            if (objectType == typeof(double))
            {
                if (reader.TokenType != JsonToken.Integer && reader.TokenType != JsonToken.Float)
                {
                    throw CreateException(reader, "number");
                }

                try
                {
                    return Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture);
                }
                catch (Exception exception)
                {
                    throw CreateException(reader, "number", exception);
                }
            }

            if (objectType == typeof(bool))
            {
                RequireToken(reader, JsonToken.Boolean, "boolean");
                return (bool)reader.Value;
            }

            throw new JsonSerializationException("未対応のプリミティブ型です: " + objectType.FullName);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotSupportedException("StrictPrimitiveJsonConverterは読み込み専用です。");
        }

        private static T ConvertInteger<T>(
            JsonReader reader,
            Func<object, IFormatProvider, T> converter)
        {
            if (reader.TokenType != JsonToken.Integer && reader.TokenType != JsonToken.Float)
            {
                throw CreateException(reader, "integer");
            }

            try
            {
                if (reader.TokenType == JsonToken.Float)
                {
                    var numericValue = Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture);
                    if (double.IsNaN(numericValue) || double.IsInfinity(numericValue) ||
                        Math.Truncate(numericValue) != numericValue)
                    {
                        throw CreateException(reader, "integer");
                    }
                }

                return converter(reader.Value, CultureInfo.InvariantCulture);
            }
            catch (Exception exception)
            {
                throw CreateException(reader, "integer", exception);
            }
        }

        private static void RequireToken(JsonReader reader, JsonToken expected, string expectedName)
        {
            if (reader.TokenType != expected)
            {
                throw CreateException(reader, expectedName);
            }
        }

        private static JsonSerializationException CreateException(
            JsonReader reader,
            string expectedName,
            Exception innerException = null)
        {
            var lineInfo = reader as IJsonLineInfo;
            return new JsonSerializationException(
                "値は" + expectedName + "である必要があります。",
                reader.Path,
                lineInfo != null && lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0,
                lineInfo != null && lineInfo.HasLineInfo() ? lineInfo.LinePosition : 0,
                innerException);
        }
    }
}
