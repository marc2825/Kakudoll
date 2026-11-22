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
    /// <remarks>
    /// ファイルを扱うLoad/Saveは既定で安全モード（<see cref="VlatFileAccessPolicy.Default"/>）です。
    /// 外部ファイルはJSONファイルのフォルダ内に限り、<see cref="VlatReadLimits"/>の上限を超える入力は
    /// 確保前に拒否します。
    /// </remarks>
    public static partial class VlatSerializer
    {
        /// <summary>JSON文字列をVLatドキュメントへ変換し、バイナリを除く意味検証を実行します。</summary>
        /// <param name="json">UTF-8 JSONに相当する文字列。</param>
        /// <returns>ドキュメント、診断、および空のバイナリ一覧を含む結果。</returns>
        public static VlatLoadResult Deserialize(string json)
        {
            return Deserialize(json, null);
        }

        /// <summary>上限を指定してJSON文字列をVLatドキュメントへ変換し、バイナリを除く意味検証を実行します。</summary>
        /// <param name="json">UTF-8 JSONに相当する文字列。</param>
        /// <param name="limits">読み込み上限。nullなら既定。文字列長は <see cref="VlatReadLimits.MaxJsonBytes"/> を文字数として扱います。</param>
        /// <returns>ドキュメント、診断、および空のバイナリ一覧を含む結果。</returns>
        public static VlatLoadResult Deserialize(string json, VlatReadLimits limits)
        {
            limits = limits ?? VlatReadLimits.Default;
            if (json != null && json.Length > limits.MaxJsonBytes)
            {
                return new VlatLoadResult(null, Array.Empty<byte[]>(), new[]
                {
                    Error("$", 0, 0, "JSONが大きすぎます（" + Invariant(json.Length) + " 文字、上限 " +
                                     VlatExternalFiles.FormatBytes(limits.MaxJsonBytes) + "）。"),
                });
            }

            var state = ParseJson(json, limits);
            if (state.Document != null)
            {
                // bufferの宣言長などの資源上限は、実際にファイルを読み書きするLoad/Saveで適用する。
                AddDiagnostics(state.Diagnostics, VlatValidator.Validate(state.Document));
                EnrichLocations(state.Root, state.Diagnostics);
            }

            return new VlatLoadResult(state.Document, Array.Empty<byte[]>(), state.Diagnostics);
        }

        /// <summary>JSONファイルと参照される全バイナリファイルを、安全モードで読み込みます。</summary>
        /// <param name="jsonPath">読み込む.vlat.jsonファイルのパス。</param>
        /// <returns>ドキュメント、バイナリ内容、および診断を含む結果。</returns>
        public static VlatLoadResult Load(string jsonPath)
        {
            return Load(jsonPath, null);
        }

        /// <summary>JSONファイルと参照される全バイナリファイルを、指定したポリシーで読み込みます。</summary>
        /// <param name="jsonPath">読み込む.vlat.jsonファイルのパス。</param>
        /// <param name="policy">外部ファイルのアクセス範囲と読み込み上限。nullなら <see cref="VlatFileAccessPolicy.Default"/>。</param>
        /// <returns>ドキュメント、バイナリ内容、および診断を含む結果。</returns>
        public static VlatLoadResult Load(string jsonPath, VlatFileAccessPolicy policy)
        {
            policy = policy ?? VlatFileAccessPolicy.Default;
            var limits = policy.Limits;
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
                string rootError;
                if (!VlatExternalFiles.TryGetAllowedRoot(fullJsonPath, policy, out _, out rootError))
                {
                    diagnostics.Add(Error("$", 0, 0, rootError));
                    return new VlatLoadResult(null, Array.Empty<byte[]>(), diagnostics);
                }

                byte[] bytes;
                string readError;
                if (!VlatExternalFiles.TryReadAllBytes(fullJsonPath, limits.MaxJsonBytes, out bytes, out readError))
                {
                    diagnostics.Add(Error("$", 0, 0, "VLat JSONを読み込めません: " + readError));
                    return new VlatLoadResult(null, Array.Empty<byte[]>(), diagnostics);
                }

                // File.ReadAllText(path, UTF8) と同じく、BOMがあればそれに従って復号する。
                using (var reader = new StreamReader(new MemoryStream(bytes, false), Encoding.UTF8, true))
                {
                    json = reader.ReadToEnd();
                }
            }
            catch (Exception exception)
            {
                diagnostics.Add(Error("$", 0, 0, "VLat JSONを読み込めません: " + exception.Message));
                return new VlatLoadResult(null, Array.Empty<byte[]>(), diagnostics);
            }

            AddDiagnostics(diagnostics, InspectInterruptedSave(fullJsonPath, policy));
            var state = ParseJson(json, limits);
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

            var limitDiagnostics = CheckDocumentLimits(state.Document, limits);
            if (limitDiagnostics.Count != 0)
            {
                AddDiagnostics(diagnostics, limitDiagnostics);
                EnrichLocations(state.Root, diagnostics);
                return new VlatLoadResult(state.Document, Array.Empty<byte[]>(), diagnostics);
            }

            var buffers = new List<byte[]>(state.Document.Buffers.Count);
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
                if (!VlatExternalFiles.TryResolve(fullJsonPath, definition.Uri, policy, out bufferPath, out pathError))
                {
                    diagnostics.Add(Error(
                        "$.buffers[" + Invariant(i) + "].uri",
                        0,
                        0,
                        "bufferを読み込めません: " + pathError));
                    buffers.Add(null);
                    continue;
                }

                // 宣言長と実ファイル長が違う場合は、内容を確保せずに拒否する（宣言長の偽装・取り違え対策）。
                var actual = new FileInfo(bufferPath);
                if (actual.Exists && actual.Length != definition.ByteLength)
                {
                    diagnostics.Add(Error(
                        "$.buffers[" + Invariant(i) + "].byteLength",
                        0,
                        0,
                        "宣言されたbyteLength（" + definition.ByteLength.ToString(CultureInfo.InvariantCulture) +
                        " byte）と実ファイル長（" + actual.Length.ToString(CultureInfo.InvariantCulture) +
                        " byte）が一致しません。"));
                    buffers.Add(null);
                    continue;
                }

                byte[] contents;
                string readError;
                if (!VlatExternalFiles.TryReadAllBytes(
                        bufferPath,
                        limits.MaxBufferBytes,
                        definition.ByteLength,
                        out contents,
                        out readError))
                {
                    diagnostics.Add(Error(
                        "$.buffers[" + Invariant(i) + "].uri",
                        0,
                        0,
                        "バイナリバッファを読み込めません: " + readError));
                    buffers.Add(null);
                    continue;
                }

                buffers.Add(contents);
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

        /// <summary>VLat JSONとbuffers配列が参照する各バイナリを、安全モードで保存します。</summary>
        /// <param name="jsonPath">保存先.vlat.jsonファイルのパス。</param>
        /// <param name="document">保存するドキュメント。</param>
        /// <param name="binaryBuffers">buffers配列と同じ順序のバイナリ内容。</param>
        /// <returns>保存結果と診断。</returns>
        public static VlatSaveResult Save(
            string jsonPath,
            VlatDocument document,
            IReadOnlyList<byte[]> binaryBuffers)
        {
            return Save(jsonPath, document, binaryBuffers, null);
        }

        /// <summary>
        /// VLat JSONとbuffers配列が参照する各バイナリを、指定したポリシーで保存します。
        /// 保存中の異常終了は保存ジャーナルで検出でき、<see cref="RecoverInterruptedSave(string, VlatFileAccessPolicy)"/>で
        /// 保存前または保存後のどちらか一貫した状態へ戻せます。
        /// </summary>
        /// <param name="jsonPath">保存先.vlat.jsonファイルのパス。</param>
        /// <param name="document">保存するドキュメント。</param>
        /// <param name="binaryBuffers">buffers配列と同じ順序のバイナリ内容。</param>
        /// <param name="policy">書き込みを許可する範囲と、再読込できることを確認する上限。nullなら既定。</param>
        /// <returns>保存結果と診断。</returns>
        public static VlatSaveResult Save(
            string jsonPath,
            VlatDocument document,
            IReadOnlyList<byte[]> binaryBuffers,
            VlatFileAccessPolicy policy)
        {
            policy = policy ?? VlatFileAccessPolicy.Default;
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

            // 読み込み上限を超える文書は、保存しても同じ既定設定では開けないため保存前に拒否する。
            AddDiagnostics(diagnostics, CheckDocumentLimits(document, policy.Limits));
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

            string rootError;
            if (!VlatExternalFiles.TryGetAllowedRoot(fullJsonPath, policy, out _, out rootError))
            {
                diagnostics.Add(Error("$", 0, 0, rootError));
                return new VlatSaveResult(diagnostics);
            }

            var jsonBytes = new UTF8Encoding(false).GetBytes(json);
            if (jsonBytes.LongLength > policy.Limits.MaxJsonBytes)
            {
                diagnostics.Add(Error("$", 0, 0, "JSONが読み込み上限（" +
                                                 VlatExternalFiles.FormatBytes(policy.Limits.MaxJsonBytes) +
                                                 "）を超えるため保存しません。"));
                return new VlatSaveResult(diagnostics);
            }

            var writes = new List<PendingWrite>();
            var targetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            targetPaths.Add(fullJsonPath);
            for (var i = 0; i < document.Buffers.Count; i++)
            {
                string bufferPath;
                string pathError;
                if (!VlatExternalFiles.TryResolve(fullJsonPath, document.Buffers[i].Uri, policy, out bufferPath, out pathError))
                {
                    diagnostics.Add(Error(
                        "$.buffers[" + Invariant(i) + "].uri",
                        0,
                        0,
                        "bufferを保存できません: " + pathError));
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

            // JSONを最後にcommitする。読み手は常にJSONから参照を辿るため、途中で止まっても
            // 保存ジャーナルが残り、次回Loadで「中断された保存」として検出される。
            writes.Add(new PendingWrite(fullJsonPath, jsonBytes));
            string writeError;
            if (!TryWriteTransaction(fullJsonPath, writes, out writeError))
            {
                diagnostics.Add(Error("$", 0, 0, writeError));
            }

            return new VlatSaveResult(diagnostics);
        }

        private static IReadOnlyList<VlatDiagnostic> CheckDocumentLimits(VlatDocument document, VlatReadLimits limits)
        {
            var diagnostics = new List<VlatDiagnostic>();
            if (document == null)
            {
                return diagnostics;
            }

            if (document.Buffers.Count > limits.MaxBufferCount)
            {
                diagnostics.Add(Error("$.buffers", 0, 0, "bufferが多すぎます（" + Invariant(document.Buffers.Count) +
                                                          " 個、上限 " + Invariant(limits.MaxBufferCount) + " 個）。"));
            }

            if (document.Textures.Count > limits.MaxTextureCount)
            {
                diagnostics.Add(Error("$.textures", 0, 0, "textureが多すぎます（" + Invariant(document.Textures.Count) +
                                                           " 個、上限 " + Invariant(limits.MaxTextureCount) + " 個）。"));
            }

            long total = 0;
            for (var i = 0; i < document.Buffers.Count; i++)
            {
                var buffer = document.Buffers[i];
                if (buffer == null)
                {
                    continue;
                }

                if (buffer.ByteLength > limits.MaxBufferBytes)
                {
                    diagnostics.Add(Error(
                        "$.buffers[" + Invariant(i) + "].byteLength",
                        0,
                        0,
                        "bufferが大きすぎます（" + VlatExternalFiles.FormatBytes(buffer.ByteLength) + "、上限 " +
                        VlatExternalFiles.FormatBytes(limits.MaxBufferBytes) + "）。"));
                }

                if (buffer.ByteLength > 0)
                {
                    total = buffer.ByteLength > long.MaxValue - total ? long.MaxValue : total + buffer.ByteLength;
                }
            }

            if (total > limits.MaxTotalBufferBytes)
            {
                diagnostics.Add(Error("$.buffers", 0, 0, "bufferの合計が大きすぎます（" +
                                                          VlatExternalFiles.FormatBytes(total) + "、上限 " +
                                                          VlatExternalFiles.FormatBytes(limits.MaxTotalBufferBytes) + "）。"));
            }

            return diagnostics;
        }

        private static ParseState ParseJson(string json, VlatReadLimits limits)
        {
            var diagnostics = new List<VlatDiagnostic>();
            if (json == null)
            {
                diagnostics.Add(Error("$", 0, 0, "JSON文字列がnullです。"));
                return new ParseState(null, null, diagnostics);
            }

            // DOMを作る前に、深さとトークン数をストリーミングで確認する（巨大・深いJSONによる資源枯渇の防止）。
            if (!PrescanJson(json, limits, diagnostics))
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
                    reader.MaxDepth = limits.MaxJsonDepth;
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

        private static bool PrescanJson(string json, VlatReadLimits limits, List<VlatDiagnostic> diagnostics)
        {
            try
            {
                using (var stringReader = new StringReader(json))
                using (var reader = new JsonTextReader(stringReader))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    // 深さは下で独自に数え、上限超過を分かりやすい診断にする。
                    reader.MaxDepth = null;
                    var tokens = 0;
                    while (reader.Read())
                    {
                        var tokenType = reader.TokenType;
                        if (tokenType == JsonToken.Comment)
                        {
                            diagnostics.Add(Error(
                                NormalizePath(reader.Path),
                                GetLineNumber(reader),
                                GetLinePosition(reader),
                                ".vlat.jsonではコメントを使用できません。"));
                            return false;
                        }

                        if (tokenType == JsonToken.EndObject || tokenType == JsonToken.EndArray)
                        {
                            continue;
                        }

                        if ((tokenType == JsonToken.StartObject || tokenType == JsonToken.StartArray) &&
                            reader.Depth + 1 > limits.MaxJsonDepth)
                        {
                            diagnostics.Add(Error(
                                NormalizePath(reader.Path),
                                GetLineNumber(reader),
                                GetLinePosition(reader),
                                "JSONのネストが深すぎます（上限 " + Invariant(limits.MaxJsonDepth) + "）。"));
                            return false;
                        }

                        if (++tokens > limits.MaxJsonTokens)
                        {
                            diagnostics.Add(Error(
                                NormalizePath(reader.Path),
                                GetLineNumber(reader),
                                GetLinePosition(reader),
                                "JSONの要素が多すぎます（上限 " + Invariant(limits.MaxJsonTokens) + " トークン）。"));
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
    }
}
