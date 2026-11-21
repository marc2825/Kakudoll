using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VLat.Core.Model;
using VLat.Core.Validation;

namespace VLat.Core.Serialization
{
    /// <summary>VLatドキュメントの読み込み結果を表します。</summary>
    public sealed class VlatLoadResult
    {
        /// <summary>読み込み結果を生成します。</summary>
        /// <param name="document">解析できたドキュメント。構文上生成できなかった場合はnull。</param>
        /// <param name="buffers">buffers配列と同じ順序のバイナリ内容。</param>
        /// <param name="diagnostics">読み込み時に検出された診断。</param>
        public VlatLoadResult(
            VlatDocument document,
            IEnumerable<byte[]> buffers,
            IEnumerable<VlatDiagnostic> diagnostics)
        {
            Document = document;
            Buffers = CopyBuffers(buffers);
            Diagnostics = CopyDiagnostics(diagnostics);
            Success = document != null && !ContainsError(Diagnostics);
        }

        /// <summary>解析できたドキュメントを取得します。構文上生成できなかった場合はnullです。</summary>
        public VlatDocument Document { get; }

        /// <summary>buffers配列と同じ順序のバイナリ内容を取得します。</summary>
        public IReadOnlyList<byte[]> Buffers { get; }

        /// <summary>読み込み時に検出された診断を取得します。</summary>
        public IReadOnlyList<VlatDiagnostic> Diagnostics { get; }

        /// <summary>エラーなしで読み込めたかどうかを取得します。</summary>
        public bool Success { get; }

        private static IReadOnlyList<byte[]> CopyBuffers(IEnumerable<byte[]> source)
        {
            var result = new List<byte[]>();
            if (source != null)
            {
                foreach (var buffer in source)
                {
                    if (buffer == null)
                    {
                        result.Add(null);
                        continue;
                    }

                    var copy = new byte[buffer.Length];
                    System.Buffer.BlockCopy(buffer, 0, copy, 0, buffer.Length);
                    result.Add(copy);
                }
            }

            return new ReadOnlyCollection<byte[]>(result);
        }

        internal static IReadOnlyList<VlatDiagnostic> CopyDiagnostics(IEnumerable<VlatDiagnostic> source)
        {
            var result = new List<VlatDiagnostic>();
            if (source != null)
            {
                foreach (var diagnostic in source)
                {
                    result.Add(diagnostic);
                }
            }

            return new ReadOnlyCollection<VlatDiagnostic>(result);
        }

        internal static bool ContainsError(IReadOnlyList<VlatDiagnostic> diagnostics)
        {
            for (var i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i] != null && diagnostics[i].Severity == VlatDiagnosticSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>VLatドキュメントの保存結果を表します。</summary>
    public sealed class VlatSaveResult
    {
        /// <summary>保存結果を生成します。</summary>
        /// <param name="diagnostics">保存時に検出された診断。</param>
        public VlatSaveResult(IEnumerable<VlatDiagnostic> diagnostics)
        {
            Diagnostics = VlatLoadResult.CopyDiagnostics(diagnostics);
            Success = !VlatLoadResult.ContainsError(Diagnostics);
        }

        /// <summary>保存時に検出された診断を取得します。</summary>
        public IReadOnlyList<VlatDiagnostic> Diagnostics { get; }

        /// <summary>すべてのファイルを保存できたかどうかを取得します。</summary>
        public bool Success { get; }
    }
}
