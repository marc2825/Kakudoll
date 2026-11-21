using System.Collections.Generic;
using System.Collections.ObjectModel;
using VLat.Core.Model;
using VLat.Core.Validation;

namespace VLat.Editor.Authoring
{
    internal sealed class VlatAuthoringLoadResult
    {
        internal VlatAuthoringLoadResult(
            VlatAuthoringDocument document,
            IEnumerable<VlatDiagnostic> diagnostics)
        {
            Document = document;
            Diagnostics = VlatAuthoringResultUtility.CopyDiagnostics(diagnostics);
            Success = document != null && !VlatAuthoringResultUtility.ContainsError(Diagnostics);
        }

        internal VlatAuthoringDocument Document { get; }
        internal IReadOnlyList<VlatDiagnostic> Diagnostics { get; }
        internal bool Success { get; }
    }

    internal sealed class VlatAuthoringPackResult
    {
        internal VlatAuthoringPackResult(
            VlatDocument document,
            IEnumerable<byte[]> buffers,
            string json,
            IEnumerable<VlatDiagnostic> diagnostics)
        {
            Document = document;
            Buffers = VlatAuthoringResultUtility.CopyBuffers(buffers);
            Json = json;
            Diagnostics = VlatAuthoringResultUtility.CopyDiagnostics(diagnostics);
            Success = document != null && !VlatAuthoringResultUtility.ContainsError(Diagnostics);
        }

        internal VlatDocument Document { get; }
        internal IReadOnlyList<byte[]> Buffers { get; }
        internal string Json { get; }
        internal IReadOnlyList<VlatDiagnostic> Diagnostics { get; }
        internal bool Success { get; }
        // 同じpack結果の内容指紋。JSON/bufferは不変なので一度だけ計算する。
        internal string ContentFingerprint { get; set; }
    }

    internal sealed class VlatAuthoringSaveResult
    {
        internal VlatAuthoringSaveResult(IEnumerable<VlatDiagnostic> diagnostics)
        {
            Diagnostics = VlatAuthoringResultUtility.CopyDiagnostics(diagnostics);
            Success = !VlatAuthoringResultUtility.ContainsError(Diagnostics);
        }

        internal IReadOnlyList<VlatDiagnostic> Diagnostics { get; }
        internal bool Success { get; }
    }

    internal static class VlatAuthoringResultUtility
    {
        internal static IReadOnlyList<VlatDiagnostic> CopyDiagnostics(IEnumerable<VlatDiagnostic> source)
        {
            var result = source == null
                ? new List<VlatDiagnostic>()
                : new List<VlatDiagnostic>(source);
            return new ReadOnlyCollection<VlatDiagnostic>(result);
        }

        internal static IReadOnlyList<byte[]> CopyBuffers(IEnumerable<byte[]> source)
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

        internal static bool ContainsError(IReadOnlyList<VlatDiagnostic> diagnostics)
        {
            for (var index = 0; index < diagnostics.Count; index++)
            {
                var diagnostic = diagnostics[index];
                if (diagnostic != null && diagnostic.Severity == VlatDiagnosticSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }

        internal static VlatDiagnostic Error(string path, string message)
        {
            return new VlatDiagnostic(VlatDiagnosticSeverity.Error, path, 0, 0, message);
        }
    }
}
