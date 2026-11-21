using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VLat.Core.Model;
using VLat.Core.Validation;

namespace VLat.Core.Serialization
{
    /// <summary>VLatのJSONドキュメントと外部バイナリを読み書きします。</summary>
    public static class VlatSerializer
    {
        /// <summary>JSON文字列をVLatドキュメントへ変換し、バイナリを除く意味検証を実行します。</summary>
        /// <param name="json">UTF-8 JSONに相当する文字列。</param>
        /// <returns>ドキュメント、診断、および空のバイナリ一覧を含む結果。</returns>
        public static VlatLoadResult Deserialize(string json)
        {
            var state = ParseJson(json);
            if (state.Document != null)
            {
                AddDiagnostics(state.Diagnostics, VlatValidator.Validate(state.Document));
                EnrichLocations(state.Root, state.Diagnostics);
            }

            return new VlatLoadResult(state.Document, Array.Empty<byte[]>(), state.Diagnostics);
        }

        /// <summary>JSONファイルと参照される全バイナリファイルを読み込みます。</summary>
        /// <param name="jsonPath">読み込む.vlat.jsonファイルのパス。</param>
        /// <returns>ドキュメント、バイナリ内容、および診断を含む結果。</returns>
        public static VlatLoadResult Load(string jsonPath)
        {
            var diagnostics = new List<VlatDiagnostic>();
            if (string.IsNullOrWhiteSpace(jsonPath))
            {
                diagnostics.Add(Error("$", 0, 0, "jsonPathは空にできません。"));
                return new VlatLoadResult(null, Array.Empty<byte[]>(), diagnostics);
            }

            string fullJsonPath;
            string json;
            try
            {
                fullJsonPath = Path.GetFullPath(jsonPath);
                json = File.ReadAllText(fullJsonPath, Encoding.UTF8);
            }
            catch (Exception exception)
            {
                diagnostics.Add(Error("$", 0, 0, "VLat JSONを読み込めません: " + exception.Message));
                return new VlatLoadResult(null, Array.Empty<byte[]>(), diagnostics);
            }

            var state = ParseJson(json);
            AddDiagnostics(diagnostics, state.Diagnostics);
            if (state.Document == null)
            {
                return new VlatLoadResult(null, Array.Empty<byte[]>(), diagnostics);
            }

            if (state.Document.KeyformGroups.Count != 0)
            {
                AddDiagnostics(diagnostics, VlatValidator.Validate(state.Document));
                if (ContainsError(diagnostics))
                    return new VlatLoadResult(state.Document, Array.Empty<byte[]>(), diagnostics);
            }

            var buffers = new List<byte[]>(state.Document.Buffers.Count);
            var baseDirectory = Path.GetDirectoryName(fullJsonPath) ?? string.Empty;
            for (var i = 0; i < state.Document.Buffers.Count; i++)
            {
                var definition = state.Document.Buffers[i];
                if (definition == null)
                {
                    buffers.Add(null);
                    continue;
                }

                string bufferPath;
                string pathError;
                if (!TryResolveExternalPath(baseDirectory, definition.Uri, out bufferPath, out pathError))
                {
                    diagnostics.Add(Error(
                        "$.buffers[" + Invariant(i) + "].uri",
                        0,
                        0,
                        pathError));
                    buffers.Add(null);
                    continue;
                }

                try
                {
                    if (state.Document.KeyformGroups.Count != 0 &&
                        new FileInfo(bufferPath).Length != definition.ByteLength)
                        throw new InvalidDataException("Keyform bufferの実ファイル長が宣言と一致しません。");
                    buffers.Add(File.ReadAllBytes(bufferPath));
                }
                catch (Exception exception)
                {
                    diagnostics.Add(Error(
                        "$.buffers[" + Invariant(i) + "].uri",
                        0,
                        0,
                        "バイナリバッファを読み込めません: " + exception.Message));
                    buffers.Add(null);
                }
            }

            AddDiagnostics(diagnostics, VlatValidator.Validate(state.Document, buffers));
            EnrichLocations(state.Root, diagnostics);
            return new VlatLoadResult(state.Document, buffers, diagnostics);
        }

        /// <summary>VLatドキュメントをJSON文字列へ変換します。</summary>
        /// <param name="document">変換するドキュメント。</param>
        /// <param name="indented">読みやすいインデントを付けるかどうか。</param>
        /// <returns>生成したJSON文字列。</returns>
        /// <exception cref="ArgumentNullException">documentがnullの場合に送出されます。</exception>
        /// <exception cref="JsonException">モデルをJSONへ変換できない場合に送出されます。</exception>
        public static string Serialize(VlatDocument document, bool indented = true)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var serializer = CreateJsonSerializer();
            var builder = new StringBuilder(4096);
            using (var stringWriter = new StringWriter(builder, CultureInfo.InvariantCulture))
            using (var writer = new JsonTextWriter(stringWriter))
            {
                writer.Formatting = indented ? Formatting.Indented : Formatting.None;
                writer.Culture = CultureInfo.InvariantCulture;
                serializer.Serialize(writer, document);
                writer.Flush();
            }

            return builder.ToString();
        }

        /// <summary>VLat JSONとbuffers配列が参照する各バイナリを原子的に保存します。</summary>
        /// <param name="jsonPath">保存先.vlat.jsonファイルのパス。</param>
        /// <param name="document">保存するドキュメント。</param>
        /// <param name="binaryBuffers">buffers配列と同じ順序のバイナリ内容。</param>
        /// <returns>保存結果と診断。</returns>
        public static VlatSaveResult Save(
            string jsonPath,
            VlatDocument document,
            IReadOnlyList<byte[]> binaryBuffers)
        {
            var diagnostics = new List<VlatDiagnostic>();
            if (string.IsNullOrWhiteSpace(jsonPath))
            {
                diagnostics.Add(Error("$", 0, 0, "jsonPathは空にできません。"));
                return new VlatSaveResult(diagnostics);
            }

            if (document != null && document.Buffers.Count > 0 && binaryBuffers == null)
            {
                diagnostics.Add(Error("$.buffers", 0, 0, "保存時は全bufferのバイナリ内容が必要です。"));
                return new VlatSaveResult(diagnostics);
            }

            AddDiagnostics(diagnostics, VlatValidator.Validate(document, binaryBuffers));
            if (ContainsError(diagnostics))
            {
                return new VlatSaveResult(diagnostics);
            }

            string fullJsonPath;
            string json;
            try
            {
                fullJsonPath = Path.GetFullPath(jsonPath);
                json = Serialize(document, true);
            }
            catch (Exception exception)
            {
                diagnostics.Add(Error("$", 0, 0, "JSONを生成できません: " + exception.Message));
                return new VlatSaveResult(diagnostics);
            }

            var writes = new List<PendingWrite>();
            var targetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            targetPaths.Add(fullJsonPath);
            var baseDirectory = Path.GetDirectoryName(fullJsonPath) ?? string.Empty;
            for (var i = 0; i < document.Buffers.Count; i++)
            {
                string bufferPath;
                string pathError;
                if (!TryResolveExternalPath(baseDirectory, document.Buffers[i].Uri, out bufferPath, out pathError))
                {
                    diagnostics.Add(Error(
                        "$.buffers[" + Invariant(i) + "].uri",
                        0,
                        0,
                        pathError));
                    continue;
                }

                if (!targetPaths.Add(bufferPath))
                {
                    diagnostics.Add(Error(
                        "$.buffers[" + Invariant(i) + "].uri",
                        0,
                        0,
                        "保存先パスがJSONまたは別のbufferと重複しています。"));
                    continue;
                }

                writes.Add(new PendingWrite(bufferPath, binaryBuffers[i]));
            }

            if (ContainsError(diagnostics))
            {
                return new VlatSaveResult(diagnostics);
            }

            writes.Add(new PendingWrite(fullJsonPath, new UTF8Encoding(false).GetBytes(json)));
            string writeError;
            if (!TryWriteTransaction(writes, out writeError))
            {
                diagnostics.Add(Error("$", 0, 0, writeError));
            }

            return new VlatSaveResult(diagnostics);
        }

        private static ParseState ParseJson(string json)
        {
            var diagnostics = new List<VlatDiagnostic>();
            if (json == null)
            {
                diagnostics.Add(Error("$", 0, 0, "JSON文字列がnullです。"));
                return new ParseState(null, null, diagnostics);
            }

            if (!RejectComments(json, diagnostics))
            {
                return new ParseState(null, null, diagnostics);
            }

            var numericLexemes = new List<string>();
            if (!RejectNonStandardSyntax(json, diagnostics, numericLexemes))
            {
                return new ParseState(null, null, diagnostics);
            }

            JToken root;
            try
            {
                using (var stringReader = new StringReader(json))
                using (var reader = new JsonTextReader(stringReader))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    reader.FloatParseHandling = FloatParseHandling.Double;
                    var loadSettings = new JsonLoadSettings
                    {
                        CommentHandling = CommentHandling.Ignore,
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        LineInfoHandling = LineInfoHandling.Load
                    };
                    root = JToken.ReadFrom(reader, loadSettings);
                    while (reader.Read())
                    {
                        if (reader.TokenType != JsonToken.Comment)
                        {
                            diagnostics.Add(Error(
                                NormalizePath(reader.Path),
                                GetLineNumber(reader),
                                GetLinePosition(reader),
                                "JSONルートの後に余分な内容があります。"));
                            return new ParseState(null, root, diagnostics);
                        }
                    }
                }
            }
            catch (JsonReaderException exception)
            {
                diagnostics.Add(Error(
                    NormalizePath(exception.Path),
                    exception.LineNumber,
                    exception.LinePosition,
                    exception.Message));
                return new ParseState(null, null, diagnostics);
            }
            catch (JsonException exception)
            {
                diagnostics.Add(Error("$", 0, 0, exception.Message));
                return new ParseState(null, null, diagnostics);
            }

            if (root.Type != JTokenType.Object)
            {
                var rootLine = root as IJsonLineInfo;
                diagnostics.Add(Error(
                    "$",
                    rootLine != null && rootLine.HasLineInfo() ? rootLine.LineNumber : 0,
                    rootLine != null && rootLine.HasLineInfo() ? rootLine.LinePosition : 0,
                    "VLat JSONのルートはobjectである必要があります。"));
                return new ParseState(null, root, diagnostics);
            }

            ValidateCaseSensitivePropertyNames((JObject)root, diagnostics);
            NormalizeExactIntegerTokens(root, numericLexemes, diagnostics);
            if (ContainsError(diagnostics))
            {
                return new ParseState(null, root, diagnostics);
            }

            VlatDocument document;
            try
            {
                document = root.ToObject<VlatDocument>(CreateJsonSerializer());
            }
            catch (JsonSerializationException exception)
            {
                diagnostics.Add(Error(
                    NormalizePath(exception.Path),
                    exception.LineNumber,
                    exception.LinePosition,
                    exception.Message));
                return new ParseState(null, root, diagnostics);
            }
            catch (JsonException exception)
            {
                diagnostics.Add(Error("$", 0, 0, exception.Message));
                return new ParseState(null, root, diagnostics);
            }

            if (document == null)
            {
                diagnostics.Add(Error("$", 0, 0, "VLatドキュメントを生成できませんでした。"));
            }

            return new ParseState(document, root, diagnostics);
        }

        private static bool RejectComments(string json, List<VlatDiagnostic> diagnostics)
        {
            try
            {
                using (var stringReader = new StringReader(json))
                using (var reader = new JsonTextReader(stringReader))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonToken.Comment)
                        {
                            diagnostics.Add(Error(
                                NormalizePath(reader.Path),
                                GetLineNumber(reader),
                                GetLinePosition(reader),
                                ".vlat.jsonではコメントを使用できません。"));
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (JsonReaderException exception)
            {
                diagnostics.Add(Error(
                    NormalizePath(exception.Path),
                    exception.LineNumber,
                    exception.LinePosition,
                    exception.Message));
                return false;
            }
            catch (JsonException exception)
            {
                diagnostics.Add(Error("$", 0, 0, exception.Message));
                return false;
            }
        }

        private static bool RejectNonStandardSyntax(
            string json,
            List<VlatDiagnostic> diagnostics,
            List<string> numericLexemes)
        {
            var inString = false;
            var escaped = false;
            var line = 1;
            var column = 0;
            for (var i = 0; i < json.Length; i++)
            {
                var character = json[i];
                var characterLine = line;
                var characterColumn = column + 1;
                if (character == '\n')
                {
                    line++;
                    column = 0;
                }
                else
                {
                    column++;
                }

                if (inString)
                {
                    if (escaped)
                    {
                        if (character == 'u')
                        {
                            if (i + 4 >= json.Length ||
                                !IsHexDigit(json[i + 1]) ||
                                !IsHexDigit(json[i + 2]) ||
                                !IsHexDigit(json[i + 3]) ||
                                !IsHexDigit(json[i + 4]))
                            {
                                diagnostics.Add(Error("$", characterLine, characterColumn, "\\u escapeには4桁の16進数が必要です。"));
                                return false;
                            }
                        }
                        else if (character != '"' && character != '\\' && character != '/' &&
                                 character != 'b' && character != 'f' && character != 'n' &&
                                 character != 'r' && character != 't')
                        {
                            diagnostics.Add(Error("$", characterLine, characterColumn, "JSON stringで未定義のescapeです。"));
                            return false;
                        }

                        escaped = false;
                    }
                    else if (character == '\\')
                    {
                        escaped = true;
                    }
                    else if (character == '"')
                    {
                        inString = false;
                    }
                    else if (character <= '\u001F')
                    {
                        diagnostics.Add(Error("$", characterLine, characterColumn, "JSON stringにraw制御文字を含めることはできません。"));
                        return false;
                    }

                    continue;
                }

                if (character == '"')
                {
                    inString = true;
                    continue;
                }

                if (character == '\'')
                {
                    diagnostics.Add(Error("$", line, column, "JSON文字列とプロパティ名には二重引用符を使用してください。"));
                    return false;
                }

                if (character == ':')
                {
                    var propertyEnd = i - 1;
                    while (propertyEnd >= 0 && char.IsWhiteSpace(json[propertyEnd]))
                    {
                        propertyEnd--;
                    }

                    if (propertyEnd < 0 || json[propertyEnd] != '"')
                    {
                        diagnostics.Add(Error("$", line, column, "JSONのプロパティ名は二重引用符で囲む必要があります。"));
                        return false;
                    }

                    continue;
                }

                if (character == '}' || character == ']')
                {
                    var previous = i - 1;
                    while (previous >= 0 && char.IsWhiteSpace(json[previous]))
                    {
                        previous--;
                    }

                    if (previous >= 0 && json[previous] == ',')
                    {
                        int commaLine;
                        int commaColumn;
                        GetTextLocation(json, previous, out commaLine, out commaColumn);
                        diagnostics.Add(Error("$", commaLine, commaColumn, "JSONでは末尾カンマを使用できません。"));
                        return false;
                    }

                    continue;
                }

                if (character == ' ' || character == '\t' || character == '\r' || character == '\n' ||
                    (i == 0 && character == '\uFEFF'))
                {
                    continue;
                }

                if (char.IsWhiteSpace(character))
                {
                    diagnostics.Add(Error("$", line, column, "JSONで使用できない空白文字です。"));
                    return false;
                }

                if (character == '{' || character == '[' || character == ',')
                {
                    continue;
                }

                if (character == '-' || (character >= '0' && character <= '9'))
                {
                    var tokenEnd = FindJsonTokenEnd(json, i);
                    if (!IsStrictJsonNumber(json, i, tokenEnd - i))
                    {
                        diagnostics.Add(Error("$", line, column, "JSON numberの形式が不正です。"));
                        return false;
                    }

                    numericLexemes.Add(json.Substring(i, tokenEnd - i));
                    column += tokenEnd - i - 1;
                    i = tokenEnd - 1;
                    continue;
                }

                if ((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z'))
                {
                    var tokenEnd = FindJsonTokenEnd(json, i);
                    var length = tokenEnd - i;
                    var isLiteral = Matches(json, i, length, "true") ||
                                    Matches(json, i, length, "false") ||
                                    Matches(json, i, length, "null");
                    if (!isLiteral)
                    {
                        diagnostics.Add(Error("$", line, column, "JSONで未定義のリテラルです。"));
                        return false;
                    }

                    column += length - 1;
                    i = tokenEnd - 1;
                    continue;
                }

                diagnostics.Add(Error("$", line, column, "JSONで使用できない文字です。"));
                return false;
            }

            return true;
        }

        private static void NormalizeExactIntegerTokens(
            JToken root,
            IReadOnlyList<string> numericLexemes,
            List<VlatDiagnostic> diagnostics)
        {
            var lexemeIndex = 0;
            NormalizeExactIntegerTokens(root, numericLexemes, diagnostics, ref lexemeIndex);
            if (lexemeIndex != numericLexemes.Count)
            {
                diagnostics.Add(Error("$", 0, 0, "JSON numberの内部対応付けに失敗しました。"));
            }
        }

        private static void NormalizeExactIntegerTokens(
            JToken token,
            IReadOnlyList<string> numericLexemes,
            List<VlatDiagnostic> diagnostics,
            ref int lexemeIndex)
        {
            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                if (lexemeIndex >= numericLexemes.Count)
                {
                    diagnostics.Add(Error("$", 0, 0, "JSON numberの内部対応付けに失敗しました。"));
                    return;
                }

                var lexeme = numericLexemes[lexemeIndex];
                lexemeIndex++;
                long minimum;
                long maximum;
                if (!TryGetIntegerBounds(token, out minimum, out maximum))
                {
                    return;
                }

                long exactValue;
                if (!TryParseExactInteger(lexeme, minimum, maximum, out exactValue))
                {
                    var lineInfo = token as IJsonLineInfo;
                    diagnostics.Add(Error(
                        NormalizePath(token.Path),
                        lineInfo != null && lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0,
                        lineInfo != null && lineInfo.HasLineInfo() ? lineInfo.LinePosition : 0,
                        "値は許容範囲内の正確なintegerである必要があります。"));
                    return;
                }

                ((JValue)token).Value = exactValue;
                return;
            }

            var container = token as JContainer;
            if (container == null)
            {
                return;
            }

            foreach (var child in container.Children())
            {
                var property = child as JProperty;
                NormalizeExactIntegerTokens(
                    property == null ? child : property.Value,
                    numericLexemes,
                    diagnostics,
                    ref lexemeIndex);
            }
        }

        private static bool TryGetIntegerBounds(JToken token, out long minimum, out long maximum)
        {
            minimum = 0L;
            maximum = int.MaxValue;
            var property = token.Parent as JProperty;
            if (property == null)
            {
                return false;
            }

            switch (property.Name)
            {
                case "byteLength":
                    minimum = 1L;
                    maximum = long.MaxValue;
                    return true;
                case "byteOffset":
                    maximum = long.MaxValue;
                    return true;
                case "priority":
                case "offset":
                    minimum = int.MinValue;
                    return true;
                case "vertexCount":
                case "slot":
                case "buffer":
                case "bufferView":
                case "meshBinding":
                case "subMesh":
                case "materialSlot":
                    return true;
                case "bandValue":
                    var owner = property.Parent as JObject;
                    var valueType = owner == null ? null : GetExactProperty(owner, "valueType");
                    return valueType != null &&
                           valueType.Value.Type == JTokenType.String &&
                           string.Equals((string)valueType.Value, "textureIndex", StringComparison.Ordinal);
                default:
                    return false;
            }
        }

        private static bool TryParseExactInteger(
            string text,
            long minimum,
            long maximum,
            out long value)
        {
            value = 0L;
            var index = 0;
            var negative = text[index] == '-';
            if (negative)
            {
                index++;
            }

            var exponentMarker = text.IndexOf('e', index);
            if (exponentMarker < 0)
            {
                exponentMarker = text.IndexOf('E', index);
            }

            var mantissaEnd = exponentMarker < 0 ? text.Length : exponentMarker;
            var digits = new StringBuilder(mantissaEnd - index);
            var fractionDigits = 0;
            var afterDecimalPoint = false;
            var allZero = true;
            for (var i = index; i < mantissaEnd; i++)
            {
                var character = text[i];
                if (character == '.')
                {
                    afterDecimalPoint = true;
                    continue;
                }

                digits.Append(character);
                if (afterDecimalPoint)
                {
                    fractionDigits++;
                }

                if (character != '0')
                {
                    allZero = false;
                }
            }

            if (allZero)
            {
                return minimum <= 0L && maximum >= 0L;
            }

            var exponent = exponentMarker < 0
                ? 0L
                : ParseSaturatedExponent(text, exponentMarker + 1);
            var decimalShift = exponent - fractionDigits;
            var integerDigitCount = digits.Length;
            var appendedZeroCount = 0;
            if (decimalShift < 0L)
            {
                var removedDigitCount = -decimalShift;
                if (removedDigitCount >= digits.Length)
                {
                    return false;
                }

                for (var i = digits.Length - (int)removedDigitCount; i < digits.Length; i++)
                {
                    if (digits[i] != '0')
                    {
                        return false;
                    }
                }

                integerDigitCount -= (int)removedDigitCount;
            }
            else
            {
                if (decimalShift > 19L)
                {
                    return false;
                }

                appendedZeroCount = (int)decimalShift;
            }

            var firstSignificantDigit = 0;
            while (firstSignificantDigit < integerDigitCount && digits[firstSignificantDigit] == '0')
            {
                firstSignificantDigit++;
            }

            var magnitudeDigitCount = integerDigitCount - firstSignificantDigit + appendedZeroCount;
            if (magnitudeDigitCount > 19)
            {
                return false;
            }

            ulong magnitude = 0UL;
            for (var i = firstSignificantDigit; i < integerDigitCount; i++)
            {
                magnitude = (magnitude * 10UL) + (ulong)(digits[i] - '0');
            }

            for (var i = 0; i < appendedZeroCount; i++)
            {
                magnitude *= 10UL;
            }

            if (negative)
            {
                const ulong minimumMagnitude = 9223372036854775808UL;
                if (magnitude > minimumMagnitude)
                {
                    return false;
                }

                value = magnitude == minimumMagnitude ? long.MinValue : -(long)magnitude;
            }
            else
            {
                if (magnitude > long.MaxValue)
                {
                    return false;
                }

                value = (long)magnitude;
            }

            return value >= minimum && value <= maximum;
        }

        private static long ParseSaturatedExponent(string text, int start)
        {
            var negative = text[start] == '-';
            if (negative || text[start] == '+')
            {
                start++;
            }

            const long saturation = 1000000000L;
            var result = 0L;
            for (var i = start; i < text.Length; i++)
            {
                var digit = text[i] - '0';
                if (result > (saturation - digit) / 10L)
                {
                    result = saturation;
                    break;
                }

                result = (result * 10L) + digit;
            }

            return negative ? -result : result;
        }

        private static bool IsHexDigit(char character)
        {
            return (character >= '0' && character <= '9') ||
                   (character >= 'a' && character <= 'f') ||
                   (character >= 'A' && character <= 'F');
        }

        private static int FindJsonTokenEnd(string text, int start)
        {
            var index = start;
            while (index < text.Length)
            {
                var character = text[index];
                if (char.IsWhiteSpace(character) || character == ',' || character == ']' ||
                    character == '}' || character == ':' || character == '[' || character == '{')
                {
                    break;
                }

                index++;
            }

            return index;
        }

        private static bool IsStrictJsonNumber(string text, int start, int length)
        {
            if (length <= 0)
            {
                return false;
            }

            var index = start;
            var end = start + length;
            if (text[index] == '-')
            {
                index++;
                if (index >= end)
                {
                    return false;
                }
            }

            if (text[index] == '0')
            {
                index++;
                if (index < end && text[index] >= '0' && text[index] <= '9')
                {
                    return false;
                }
            }
            else
            {
                if (text[index] < '1' || text[index] > '9')
                {
                    return false;
                }

                do
                {
                    index++;
                }
                while (index < end && text[index] >= '0' && text[index] <= '9');
            }

            if (index < end && text[index] == '.')
            {
                index++;
                var fractionStart = index;
                while (index < end && text[index] >= '0' && text[index] <= '9')
                {
                    index++;
                }

                if (index == fractionStart)
                {
                    return false;
                }
            }

            if (index < end && (text[index] == 'e' || text[index] == 'E'))
            {
                index++;
                if (index < end && (text[index] == '+' || text[index] == '-'))
                {
                    index++;
                }

                var exponentStart = index;
                while (index < end && text[index] >= '0' && text[index] <= '9')
                {
                    index++;
                }

                if (index == exponentStart)
                {
                    return false;
                }
            }

            return index == end;
        }

        private static bool Matches(string text, int start, int length, string expected)
        {
            if (length != expected.Length)
            {
                return false;
            }

            for (var i = 0; i < length; i++)
            {
                if (text[start + i] != expected[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static void GetTextLocation(string text, int targetIndex, out int line, out int column)
        {
            line = 1;
            column = 0;
            for (var i = 0; i <= targetIndex && i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    column = 0;
                }
                else
                {
                    column++;
                }
            }
        }

        private static void ValidateCaseSensitivePropertyNames(
            JObject root,
            List<VlatDiagnostic> diagnostics)
        {
            ValidateAllowedJsonProperties(
                root,
                diagnostics,
                "vlat",
                "buffers",
                "bufferViews",
                "textures",
                "angleBands",
                "layers",
                "keyformGroups");

            var metadata = GetExactObject(root, "vlat");
            if (metadata != null)
            {
                ValidateAllowedJsonProperties(metadata, diagnostics, "version", "generator", "targetAvatar");
                var avatar = GetExactObject(metadata, "targetAvatar");
                if (avatar != null)
                {
                    ValidateAllowedJsonProperties(avatar, diagnostics, "name", "vrmVersion", "meshBindings");
                    VisitObjectArray(avatar, "meshBindings", diagnostics, ValidateMeshBindingJson);
                }
            }

            VisitObjectArray(root, "buffers", diagnostics, ValidateBufferJson);
            VisitObjectArray(root, "bufferViews", diagnostics, ValidateBufferViewJson);
            VisitObjectArray(root, "textures", diagnostics, ValidateTextureJson);
            VisitObjectArray(root, "angleBands", diagnostics, ValidateAngleBandJson);
            VisitObjectArray(root, "layers", diagnostics, ValidateLayerJson);
            var groups = GetExactProperty(root, "keyformGroups");
            var version = metadata == null ? null : GetExactProperty(metadata, "version");
            if (groups != null && (version == null || version.Value.Type != JTokenType.String ||
                (string)version.Value != VlatValidator.KeyformVersion))
                diagnostics.Add(Error("$.keyformGroups", 0, 0, "keyformGroupsはversion 0.2.0でのみ使用できます。"));
            if (groups?.Value is JArray groupArray && groupArray.Count == 0)
                diagnostics.Add(Error("$.keyformGroups", 0, 0, "拡張フィールドを指定する場合は1グループが必要です。"));
            VisitObjectArray(root, "keyformGroups", diagnostics, ValidateKeyformGroupJson);
        }

        private static void ValidateKeyformGroupJson(JObject value, List<VlatDiagnostic> diagnostics)
        {
            ValidateAllowedJsonProperties(value, diagnostics, "id", "meshBinding", "topologyHash",
                "coordinateSpace", "interpolation", "supportStart", "supportEnd", "keys");
            VisitObjectArray(value, "keys", diagnostics, ValidateYawKeyformJson);
        }

        private static void ValidateYawKeyformJson(JObject value, List<VlatDiagnostic> diagnostics)
        {
            ValidateAllowedJsonProperties(value, diagnostics, "yaw", "delta");
            var delta = GetExactObject(value, "delta");
            if (delta != null) ValidateModifierJson(delta, diagnostics);
        }

        private static void ValidateMeshBindingJson(JObject value, List<VlatDiagnostic> diagnostics)
        {
            ValidateAllowedJsonProperties(
                value,
                diagnostics,
                "hierarchyPath",
                "meshName",
                "vertexCount",
                "positionHash",
                "materials");
            VisitObjectArray(value, "materials", diagnostics, ValidateMaterialSlotJson);
        }

        private static void ValidateMaterialSlotJson(JObject value, List<VlatDiagnostic> diagnostics)
        {
            ValidateAllowedJsonProperties(value, diagnostics, "slot", "materialName", "shader");
        }

        private static void ValidateBufferJson(JObject value, List<VlatDiagnostic> diagnostics)
        {
            ValidateAllowedJsonProperties(value, diagnostics, "uri", "byteLength");
        }

        private static void ValidateBufferViewJson(JObject value, List<VlatDiagnostic> diagnostics)
        {
            ValidateAllowedJsonProperties(value, diagnostics, "buffer", "byteOffset", "byteLength");
        }

        private static void ValidateTextureJson(JObject value, List<VlatDiagnostic> diagnostics)
        {
            ValidateAllowedJsonProperties(value, diagnostics, "name", "uri", "bufferView", "mimeType");
        }

        private static void ValidateAngleBandJson(JObject value, List<VlatDiagnostic> diagnostics)
        {
            ValidateAllowedJsonProperties(value, diagnostics, "id", "name", "yaw", "pitch", "feather", "hysteresis");
            var yaw = GetExactObject(value, "yaw");
            if (yaw != null)
            {
                ValidateAllowedJsonProperties(yaw, diagnostics, "min", "max");
            }

            var pitch = GetExactObject(value, "pitch");
            if (pitch != null)
            {
                ValidateAllowedJsonProperties(pitch, diagnostics, "min", "max");
            }

            var feather = GetExactObject(value, "feather");
            if (feather != null)
            {
                ValidateAllowedJsonProperties(feather, diagnostics, "yaw", "pitch");
            }
        }

        private static void ValidateLayerJson(JObject value, List<VlatDiagnostic> diagnostics)
        {
            ValidateAllowedJsonProperties(value, diagnostics, "id", "name", "tag", "enabled", "priority", "bandId", "modifiers");
            VisitObjectArray(value, "modifiers", diagnostics, ValidateModifierJson);
        }

        private static void ValidateModifierJson(JObject value, List<VlatDiagnostic> diagnostics)
        {
            var typeProperty = GetExactProperty(value, "type");
            var type = typeProperty == null || typeProperty.Value.Type != JTokenType.String
                ? null
                : (string)typeProperty.Value;
            switch (type)
            {
                case VlatModifierTypes.VertexDelta:
                    ValidateAllowedJsonProperties(value, diagnostics, "type", "meshBinding", "indices", "positions", "normals");
                    ValidateAccessorJson(GetExactObject(value, "indices"), diagnostics);
                    ValidateAccessorJson(GetExactObject(value, "positions"), diagnostics);
                    ValidateAccessorJson(GetExactObject(value, "normals"), diagnostics);
                    break;
                case VlatModifierTypes.Visibility:
                    ValidateAllowedJsonProperties(value, diagnostics, "type", "meshBinding", "subMesh", "visibleInBand");
                    break;
                case VlatModifierTypes.Opacity:
                    ValidateAllowedJsonProperties(value, diagnostics, "type", "meshBinding", "materialSlot", "value");
                    break;
                case VlatModifierTypes.MaterialProperty:
                    ValidateAllowedJsonProperties(
                        value,
                        diagnostics,
                        "type",
                        "meshBinding",
                        "materialSlot",
                        "property",
                        "valueType",
                        "bandValue",
                        "interpolate");
                    break;
                case VlatModifierTypes.RenderQueue:
                    ValidateAllowedJsonProperties(value, diagnostics, "type", "meshBinding", "materialSlot", "offset");
                    break;
                default:
                    ValidateAllowedJsonProperties(
                        value,
                        diagnostics,
                        "type",
                        "meshBinding",
                        "materialSlot",
                        "subMesh",
                        "visibleInBand",
                        "indices",
                        "positions",
                        "normals",
                        "value",
                        "property",
                        "valueType",
                        "bandValue",
                        "interpolate",
                        "offset");
                    ValidateAccessorJson(GetExactObject(value, "indices"), diagnostics);
                    ValidateAccessorJson(GetExactObject(value, "positions"), diagnostics);
                    ValidateAccessorJson(GetExactObject(value, "normals"), diagnostics);
                    break;
            }
        }

        private static void ValidateAccessorJson(JObject value, List<VlatDiagnostic> diagnostics)
        {
            if (value != null)
            {
                ValidateAllowedJsonProperties(value, diagnostics, "bufferView", "componentType");
            }
        }

        private static void VisitObjectArray(
            JObject owner,
            string propertyName,
            List<VlatDiagnostic> diagnostics,
            Action<JObject, List<VlatDiagnostic>> visitor)
        {
            var property = GetExactProperty(owner, propertyName);
            var array = property == null ? null : property.Value as JArray;
            if (array == null)
            {
                return;
            }

            for (var i = 0; i < array.Count; i++)
            {
                var item = array[i] as JObject;
                if (item != null)
                {
                    visitor(item, diagnostics);
                }
            }
        }

        private static JObject GetExactObject(JObject owner, string propertyName)
        {
            var property = GetExactProperty(owner, propertyName);
            return property == null ? null : property.Value as JObject;
        }

        private static JProperty GetExactProperty(JObject owner, string propertyName)
        {
            foreach (var property in owner.Properties())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.Ordinal))
                {
                    return property;
                }
            }

            return null;
        }

        private static void ValidateAllowedJsonProperties(
            JObject value,
            List<VlatDiagnostic> diagnostics,
            params string[] allowedNames)
        {
            foreach (var property in value.Properties())
            {
                var allowed = false;
                for (var i = 0; i < allowedNames.Length; i++)
                {
                    if (string.Equals(property.Name, allowedNames[i], StringComparison.Ordinal))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (allowed)
                {
                    continue;
                }

                var lineInfo = property as IJsonLineInfo;
                diagnostics.Add(Error(
                    NormalizePath(property.Path),
                    lineInfo != null && lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0,
                    lineInfo != null && lineInfo.HasLineInfo() ? lineInfo.LinePosition : 0,
                    "Schemaに定義されていない、または大文字小文字が一致しないプロパティです: " + property.Name));
            }
        }

        private static JsonSerializer CreateJsonSerializer()
        {
            var settings = new JsonSerializerSettings
            {
                Culture = CultureInfo.InvariantCulture,
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double,
                MissingMemberHandling = MissingMemberHandling.Error,
                NullValueHandling = NullValueHandling.Ignore
            };
            settings.Converters.Add(new StrictPrimitiveJsonConverter());
            return JsonSerializer.Create(settings);
        }

        private static bool TryResolveExternalPath(
            string baseDirectory,
            string uri,
            out string fullPath,
            out string error)
        {
            fullPath = null;
            error = null;
            if (string.IsNullOrWhiteSpace(uri))
            {
                error = "bufferのuriは空にできません。";
                return false;
            }

            try
            {
                if (!VlatValidator.IsRelativeExternalUri(uri))
                {
                    error = "bufferのuriはJSONファイルを基準とした相対URIである必要があります。";
                    return false;
                }

                var decoded = Uri.UnescapeDataString(uri).Replace('/', Path.DirectorySeparatorChar);
                var fullBaseDirectory = Path.GetFullPath(baseDirectory);
                fullPath = Path.GetFullPath(Path.Combine(fullBaseDirectory, decoded));
                return true;
            }
            catch (Exception exception)
            {
                error = "bufferのuriをファイルパスへ変換できません: " + exception.Message;
                return false;
            }
        }

        private static bool TryWriteTransaction(IReadOnlyList<PendingWrite> writes, out string error)
        {
            error = null;
            try
            {
                for (var i = 0; i < writes.Count; i++)
                {
                    writes[i].Stage();
                }

                for (var i = 0; i < writes.Count; i++)
                {
                    writes[i].Commit();
                }
            }
            catch (Exception exception)
            {
                var rollbackErrors = new StringBuilder();
                for (var i = writes.Count - 1; i >= 0; i--)
                {
                    string rollbackError;
                    if (!writes[i].Rollback(out rollbackError) && !string.IsNullOrEmpty(rollbackError))
                    {
                        if (rollbackErrors.Length > 0)
                        {
                            rollbackErrors.Append(" / ");
                        }

                        rollbackErrors.Append(rollbackError);
                    }
                }

                error = "VLatファイルを保存できません: " + exception.Message;
                if (rollbackErrors.Length > 0)
                {
                    error += "（復元にも失敗しました: " + rollbackErrors + "）";
                }

                return false;
            }

            // 全commit後は保存が成立済みです。backup削除失敗をrollbackへ流すと、
            // 先に削除済みのbackupを復元できないため、cleanupはbest-effortで行います。
            for (var i = 0; i < writes.Count; i++)
            {
                writes[i].Complete();
            }

            return true;
        }

        private static void EnrichLocations(JToken root, List<VlatDiagnostic> diagnostics)
        {
            if (root == null)
            {
                return;
            }

            for (var i = 0; i < diagnostics.Count; i++)
            {
                var diagnostic = diagnostics[i];
                if (diagnostic == null || diagnostic.Line > 0)
                {
                    continue;
                }

                var token = FindNearestToken(root, diagnostic.Path);
                var lineInfo = token as IJsonLineInfo;
                if (lineInfo == null || !lineInfo.HasLineInfo())
                {
                    continue;
                }

                diagnostics[i] = new VlatDiagnostic(
                    diagnostic.Severity,
                    diagnostic.Path,
                    lineInfo.LineNumber,
                    lineInfo.LinePosition,
                    diagnostic.Message);
            }
        }

        private static JToken FindNearestToken(JToken root, string path)
        {
            var candidate = string.IsNullOrEmpty(path) ? "$" : path;
            while (!string.IsNullOrEmpty(candidate))
            {
                try
                {
                    var token = root.SelectToken(candidate, false);
                    if (token != null)
                    {
                        return token;
                    }
                }
                catch (JsonException)
                {
                    return root;
                }

                if (candidate == "$")
                {
                    break;
                }

                var dot = candidate.LastIndexOf('.');
                var bracket = candidate.LastIndexOf('[');
                var cut = dot > bracket ? dot : bracket;
                if (cut <= 0)
                {
                    candidate = "$";
                }
                else
                {
                    candidate = candidate.Substring(0, cut);
                }
            }

            return root;
        }

        private static void AddDiagnostics(
            List<VlatDiagnostic> destination,
            IReadOnlyList<VlatDiagnostic> source)
        {
            for (var i = 0; i < source.Count; i++)
            {
                destination.Add(source[i]);
            }
        }

        private static bool ContainsError(IReadOnlyList<VlatDiagnostic> diagnostics)
        {
            for (var i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].Severity == VlatDiagnosticSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }

        private static VlatDiagnostic Error(string path, int line, int column, string message)
        {
            return new VlatDiagnostic(VlatDiagnosticSeverity.Error, path, line, column, message);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "$";
            }

            if (path[0] == '$')
            {
                return path;
            }

            return path[0] == '[' ? "$" + path : "$." + path;
        }

        private static int GetLineNumber(JsonReader reader)
        {
            var info = reader as IJsonLineInfo;
            return info != null && info.HasLineInfo() ? info.LineNumber : 0;
        }

        private static int GetLinePosition(JsonReader reader)
        {
            var info = reader as IJsonLineInfo;
            return info != null && info.HasLineInfo() ? info.LinePosition : 0;
        }

        private static string Invariant(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private sealed class ParseState
        {
            public ParseState(VlatDocument document, JToken root, List<VlatDiagnostic> diagnostics)
            {
                Document = document;
                Root = root;
                Diagnostics = diagnostics;
            }

            public VlatDocument Document { get; }

            public JToken Root { get; }

            public List<VlatDiagnostic> Diagnostics { get; }
        }

        private sealed class PendingWrite
        {
            private readonly byte[] _contents;
            private string _temporaryPath;
            private string _backupPath;
            private bool _hadOriginal;
            private bool _committed;

            public PendingWrite(string targetPath, byte[] contents)
            {
                TargetPath = targetPath;
                _contents = contents;
            }

            public string TargetPath { get; }

            public void Stage()
            {
                var directory = Path.GetDirectoryName(TargetPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                _temporaryPath = CreateSidecarPath(TargetPath, ".tmp");
                using (var stream = new FileStream(
                           _temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    stream.Write(_contents, 0, _contents.Length);
                    stream.Flush(true);
                }
            }

            public void Commit()
            {
                _hadOriginal = File.Exists(TargetPath);
                if (_hadOriginal)
                {
                    _backupPath = CreateSidecarPath(TargetPath, ".bak");
                    File.Replace(_temporaryPath, TargetPath, _backupPath);
                }
                else
                {
                    File.Move(_temporaryPath, TargetPath);
                }

                _committed = true;
                _temporaryPath = null;
            }

            public void Complete()
            {
                try
                {
                    SafeDelete(_backupPath);
                }
                catch (Exception)
                {
                    // 保存自体は完了しているため、一意名のバックアップを残して処理を継続します。
                }

                _backupPath = null;
            }

            public bool Rollback(out string error)
            {
                error = null;
                try
                {
                    if (_committed)
                    {
                        if (_hadOriginal && !string.IsNullOrEmpty(_backupPath) && File.Exists(_backupPath))
                        {
                            if (File.Exists(TargetPath))
                            {
                                File.Replace(_backupPath, TargetPath, null);
                            }
                            else
                            {
                                File.Move(_backupPath, TargetPath);
                            }
                        }
                        else if (!_hadOriginal)
                        {
                            SafeDelete(TargetPath);
                        }
                    }

                    SafeDelete(_temporaryPath);
                    SafeDelete(_backupPath);
                    return true;
                }
                catch (Exception exception)
                {
                    error = TargetPath + ": " + exception.Message;
                    return false;
                }
            }

            private static string CreateSidecarPath(string targetPath, string suffix)
            {
                var directory = Path.GetDirectoryName(targetPath) ?? string.Empty;
                var fileName = Path.GetFileName(targetPath);
                return Path.Combine(
                    directory,
                    "." + fileName + "." + Guid.NewGuid().ToString("N") + suffix);
            }

            private static void SafeDelete(string path)
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
