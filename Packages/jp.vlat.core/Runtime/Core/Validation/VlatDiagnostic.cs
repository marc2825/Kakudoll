using Newtonsoft.Json;

namespace VLat.Core.Validation
{
    /// <summary>VLatドキュメントの診断重要度を表します。</summary>
    public enum VlatDiagnosticSeverity
    {
        /// <summary>処理を継続できない不正を表します。</summary>
        Error,

        /// <summary>処理は継続できるものの互換性などに注意が必要な状態を表します。</summary>
        Warning
    }

    /// <summary>VLatドキュメント内の問題箇所とメッセージを表します。</summary>
    public sealed class VlatDiagnostic
    {
        /// <summary>診断情報を生成します。</summary>
        /// <param name="severity">診断重要度。</param>
        /// <param name="path">JSON Path形式の問題箇所。</param>
        /// <param name="line">1始まりの行番号。不明な場合は0。</param>
        /// <param name="column">1始まりの列番号。不明な場合は0。</param>
        /// <param name="message">問題の説明。</param>
        [JsonConstructor]
        public VlatDiagnostic(
            VlatDiagnosticSeverity severity,
            string path,
            int line,
            int column,
            string message)
        {
            Severity = severity;
            Path = path ?? "$";
            Line = line;
            Column = column;
            Message = message ?? string.Empty;
        }

        /// <summary>診断重要度を取得します。</summary>
        public VlatDiagnosticSeverity Severity { get; }

        /// <summary>JSON Path形式の問題箇所を取得します。</summary>
        public string Path { get; }

        /// <summary>1始まりの行番号を取得します。不明な場合は0です。</summary>
        public int Line { get; }

        /// <summary>1始まりの列番号を取得します。不明な場合は0です。</summary>
        public int Column { get; }

        /// <summary>問題の説明を取得します。</summary>
        public string Message { get; }
    }
}
