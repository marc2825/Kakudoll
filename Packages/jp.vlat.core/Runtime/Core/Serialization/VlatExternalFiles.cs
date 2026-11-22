using System;
using System.Globalization;
using System.IO;
using VLat.Core.Validation;

namespace VLat.Core.Serialization
{
    /// <summary>
    /// .vlat.jsonが参照する外部ファイルのパス解決と、上限付きの読み込みを行います。
    /// buffer・textureのどちらも、Core・Runtime・Editorでこの規則を共用します。
    /// </summary>
    public static class VlatExternalFiles
    {
        private static readonly bool WindowsPaths = Path.DirectorySeparatorChar == '\\';

        private static readonly StringComparison PathComparison =
            WindowsPaths ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        private static readonly string[] ReservedDeviceNames =
        {
            "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        /// <summary>JSONファイルの位置とポリシーから、外部ファイルを置いてよいフォルダを求めます。</summary>
        /// <param name="jsonPath">.vlat.jsonのパス。</param>
        /// <param name="policy">アクセスポリシー。nullなら既定。</param>
        /// <param name="rootDirectory">許可されたフォルダの完全パス。範囲を制限しない場合もJSONのフォルダを返します。</param>
        /// <param name="error">失敗時の理由。</param>
        /// <returns>JSONファイルが許可範囲内にあり、フォルダを決定できたときは<c>true</c>。</returns>
        public static bool TryGetAllowedRoot(
            string jsonPath,
            VlatFileAccessPolicy policy,
            out string rootDirectory,
            out string error)
        {
            rootDirectory = null;
            error = null;
            policy = policy ?? VlatFileAccessPolicy.Default;
            try
            {
                var fullJsonPath = Path.GetFullPath(jsonPath);
                var jsonDirectory = Path.GetDirectoryName(fullJsonPath) ?? string.Empty;
                if (policy.AllowOutsideRoot || policy.AllowedRootDirectory == null)
                {
                    rootDirectory = TrimSeparator(Path.GetFullPath(jsonDirectory));
                    return true;
                }

                rootDirectory = TrimSeparator(Path.GetFullPath(policy.AllowedRootDirectory));
                if (!IsUnder(fullJsonPath, rootDirectory))
                {
                    error = "JSONファイルが、許可されたフォルダ（AllowedRootDirectory）の外にあります。";
                    rootDirectory = null;
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = "許可するフォルダを決定できません: " + exception.Message;
                rootDirectory = null;
                return false;
            }
        }

        /// <summary>JSONファイル基準の相対URIを、ポリシーに従ってファイルの完全パスへ解決します。</summary>
        /// <param name="jsonPath">.vlat.jsonのパス。</param>
        /// <param name="uri">buffers[].uri または textures[].uri の値。</param>
        /// <param name="policy">アクセスポリシー。nullなら既定（安全モード）。</param>
        /// <param name="fullPath">解決したファイルの完全パス。</param>
        /// <param name="error">拒否・失敗時の理由（利用者向けの日本語）。</param>
        /// <returns>アクセスしてよいパスへ解決できたときは<c>true</c>。</returns>
        public static bool TryResolve(
            string jsonPath,
            string uri,
            VlatFileAccessPolicy policy,
            out string fullPath,
            out string error)
        {
            fullPath = null;
            error = null;
            policy = policy ?? VlatFileAccessPolicy.Default;
            if (string.IsNullOrWhiteSpace(jsonPath))
            {
                error = "外部ファイルの基準になるJSONファイルのパスがありません。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(uri))
            {
                error = "uriは空にできません。";
                return false;
            }

            string root;
            if (!TryGetAllowedRoot(jsonPath, policy, out root, out error))
            {
                return false;
            }

            try
            {
                if (!VlatValidator.IsRelativeExternalUri(uri))
                {
                    error = "uriはJSONファイルを基準とした相対URIである必要があります。";
                    return false;
                }

                var decoded = Uri.UnescapeDataString(uri);
                if (!policy.AllowOutsideRoot && !TryCheckPortableRelativePath(decoded, out error))
                {
                    return false;
                }

                var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(jsonPath)) ?? string.Empty;
                var candidate = Path.GetFullPath(Path.Combine(
                    Path.GetFullPath(baseDirectory),
                    decoded.Replace('/', Path.DirectorySeparatorChar)));
                if (!policy.AllowOutsideRoot)
                {
                    if (!IsUnder(candidate, root))
                    {
                        error = "uriが許可されたフォルダの外を指しています（安全モード）。" +
                                "出所を確認したファイルだけ、VlatFileAccessPolicyで共通の親フォルダを許可して読み込んでください。";
                        return false;
                    }

                    if (!TryCheckNoLinks(root, candidate, out error))
                    {
                        return false;
                    }
                }

                fullPath = candidate;
                return true;
            }
            catch (Exception exception)
            {
                error = "uriをファイルパスへ変換できません: " + exception.Message;
                return false;
            }
        }

        /// <summary>ファイル長を先に確認してから、上限以下のファイルを丸ごと読み込みます。</summary>
        /// <param name="path">読み込むファイルの完全パス。</param>
        /// <param name="maxBytes">許可する最大バイト数。</param>
        /// <param name="contents">読み込んだ内容。</param>
        /// <param name="error">失敗時の理由。</param>
        /// <returns>読み込めたときは<c>true</c>。</returns>
        public static bool TryReadAllBytes(string path, long maxBytes, out byte[] contents, out string error)
        {
            return TryReadAllBytes(path, maxBytes, -1, out contents, out error);
        }

        /// <summary>
        /// 期待長が分かっているファイルを読み込みます。実ファイル長が期待長と違えば、確保せずに拒否します。
        /// </summary>
        /// <param name="path">読み込むファイルの完全パス。</param>
        /// <param name="maxBytes">許可する最大バイト数。</param>
        /// <param name="expectedLength">期待するバイト数。負なら確認しない。</param>
        /// <param name="contents">読み込んだ内容。</param>
        /// <param name="error">失敗時の理由。</param>
        /// <returns>読み込めたときは<c>true</c>。</returns>
        public static bool TryReadAllBytes(
            string path,
            long maxBytes,
            long expectedLength,
            out byte[] contents,
            out string error)
        {
            contents = null;
            error = null;
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var length = stream.Length;
                    if (length > maxBytes || length > int.MaxValue)
                    {
                        error = "ファイルが大きすぎます（" + FormatBytes(length) + "、上限 " +
                                FormatBytes(Math.Min(maxBytes, int.MaxValue)) + "）。";
                        return false;
                    }

                    if (expectedLength >= 0 && length != expectedLength)
                    {
                        error = "実ファイル長（" + length.ToString(CultureInfo.InvariantCulture) +
                                " byte）が宣言されたbyteLength（" + expectedLength.ToString(CultureInfo.InvariantCulture) +
                                " byte）と一致しません。";
                        return false;
                    }

                    var buffer = new byte[length];
                    var offset = 0;
                    while (offset < buffer.Length)
                    {
                        var read = stream.Read(buffer, offset, buffer.Length - offset);
                        if (read <= 0)
                        {
                            error = "読み込み中にファイルが短くなりました。";
                            return false;
                        }

                        offset += read;
                    }

                    if (stream.ReadByte() >= 0)
                    {
                        error = "読み込み中にファイルが長くなりました。";
                        return false;
                    }

                    contents = buffer;
                    return true;
                }
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        /// <summary>バイト数を利用者向けの表記にします。</summary>
        internal static string FormatBytes(long bytes)
        {
            const long mebibyte = 1024L * 1024;
            if (bytes >= mebibyte)
            {
                return (bytes / (double)mebibyte).ToString("0.#", CultureInfo.InvariantCulture) + " MiB";
            }

            return bytes.ToString(CultureInfo.InvariantCulture) + " byte";
        }

        internal static bool IsUnder(string fullPath, string rootDirectory)
        {
            // 「C:\dir-evil」が「C:\dir」の配下と判定されないよう、区切り文字まで含めて比較する。
            var root = TrimSeparator(rootDirectory);
            var last = root.Length == 0 ? '\0' : root[root.Length - 1];
            var prefix = last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar
                ? root
                : root + Path.DirectorySeparatorChar;
            return fullPath.Length > prefix.Length && fullPath.StartsWith(prefix, PathComparison);
        }

        private static string TrimSeparator(string path)
        {
            var root = Path.GetPathRoot(path) ?? string.Empty;
            while (path.Length > root.Length &&
                   (path[path.Length - 1] == Path.DirectorySeparatorChar ||
                    path[path.Length - 1] == Path.AltDirectorySeparatorChar))
            {
                path = path.Substring(0, path.Length - 1);
            }

            return path;
        }

        private static bool TryCheckPortableRelativePath(string decoded, out string error)
        {
            error = null;
            for (var i = 0; i < decoded.Length; i++)
            {
                var character = decoded[i];
                if (character < ' ' || character == '\u007F')
                {
                    error = "uriに制御文字は使えません。";
                    return false;
                }

                if (character == '\\')
                {
                    error = "uriの区切りには / を使ってください（\\ は安全モードで使えません）。";
                    return false;
                }

                if (character == ':')
                {
                    error = "uriに : は使えません（ドライブ指定・代替データストリームの防止）。";
                    return false;
                }
            }

            var segments = decoded.Split('/');
            for (var i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (segment.Length == 0 || segment == "." || segment == "..")
                {
                    continue;
                }

                var last = segment[segment.Length - 1];
                if (last == '.' || last == ' ')
                {
                    error = "uriのファイル名・フォルダ名を . や空白で終えることはできません。";
                    return false;
                }

                var stem = segment;
                var dot = stem.IndexOf('.');
                if (dot >= 0)
                {
                    stem = stem.Substring(0, dot);
                }

                stem = stem.TrimEnd(' ');
                for (var n = 0; n < ReservedDeviceNames.Length; n++)
                {
                    if (string.Equals(stem, ReservedDeviceNames[n], StringComparison.OrdinalIgnoreCase))
                    {
                        error = "uriにWindowsの予約デバイス名（" + ReservedDeviceNames[n] + "）は使えません。";
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool TryCheckNoLinks(string root, string fullPath, out string error)
        {
            error = null;
            var relative = fullPath.Substring(TrimSeparator(root).Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var current = TrimSeparator(root);
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0)
                {
                    continue;
                }

                current = Path.Combine(current, parts[i]);
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(current);
                }
                catch (FileNotFoundException)
                {
                    return true;
                }
                catch (DirectoryNotFoundException)
                {
                    return true;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    error = "シンボリックリンク・ジャンクションを経由するパスは安全モードで使えません。";
                    return false;
                }
            }

            return true;
        }
    }
}
