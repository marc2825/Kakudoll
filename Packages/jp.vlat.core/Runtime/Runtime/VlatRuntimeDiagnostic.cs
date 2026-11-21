using System;

namespace VLat.Runtime
{
    /// <summary>ランタイム診断の重大度を表します。</summary>
    public enum VlatRuntimeDiagnosticSeverity
    {
        /// <summary>処理結果を補足する情報です。</summary>
        Information,

        /// <summary>一部機能をフォールバックまたは無効化した警告です。</summary>
        Warning,

        /// <summary>要求された処理を継続できないエラーです。</summary>
        Error,
    }

    /// <summary>VLatランタイムの初期化または適用時に得られた診断を表します。</summary>
    public sealed class VlatRuntimeDiagnostic
    {
        /// <summary>ランタイム診断を生成します。</summary>
        /// <param name="severity">診断の重大度。</param>
        /// <param name="path">関連するVLat JSON PathまたはUnityオブジェクトパス。</param>
        /// <param name="message">利用者向けメッセージ。</param>
        public VlatRuntimeDiagnostic(
            VlatRuntimeDiagnosticSeverity severity,
            string path,
            string message)
        {
            Severity = severity;
            Path = path ?? string.Empty;
            Message = message ?? string.Empty;
        }

        /// <summary>診断の重大度を取得します。</summary>
        public VlatRuntimeDiagnosticSeverity Severity { get; }

        /// <summary>関連するVLat JSON PathまたはUnityオブジェクトパスを取得します。</summary>
        public string Path { get; }

        /// <summary>利用者向けメッセージを取得します。</summary>
        public string Message { get; }
    }
}
