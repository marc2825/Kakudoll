namespace VLat.Core.Serialization
{
    /// <summary>
    /// .vlat.jsonが参照する外部ファイル（buffer・texture）の読み書きを許可する範囲と、読み込み上限です。
    /// </summary>
    /// <remarks>
    /// 既定（<see cref="Default"/>）は安全モードです。JSONファイルのフォルダ（または
    /// <see cref="AllowedRootDirectory"/>）の外、シンボリックリンク・ジャンクション経由、
    /// Windowsの予約デバイス名、<c>\</c> や <c>:</c> を含むURIを拒否します。
    /// <c>../</c> を含む親相対URIはファイル形式としては有効なので、信頼できるファイルに限り
    /// 共通の親フォルダを <see cref="AllowedRootDirectory"/> に指定するか、
    /// <see cref="TrustedLegacy"/> で従来どおり範囲を制限せずに扱えます。
    /// </remarks>
    public sealed class VlatFileAccessPolicy
    {
        /// <summary>アクセス範囲と上限を指定して作成します。</summary>
        /// <param name="allowedRootDirectory">外部ファイルを置いてよいフォルダ。nullならJSONファイルのフォルダ。</param>
        /// <param name="allowOutsideRoot">trueなら範囲を制限しない（信頼できるファイル専用の従来動作）。</param>
        /// <param name="limits">読み込み上限。nullなら <see cref="VlatReadLimits.Default"/>。</param>
        public VlatFileAccessPolicy(
            string allowedRootDirectory = null,
            bool allowOutsideRoot = false,
            VlatReadLimits limits = null)
        {
            AllowedRootDirectory = string.IsNullOrWhiteSpace(allowedRootDirectory) ? null : allowedRootDirectory;
            AllowOutsideRoot = allowOutsideRoot;
            Limits = limits ?? VlatReadLimits.Default;
        }

        /// <summary>安全モードの既定ポリシー。JSONファイルのフォルダ内だけを読み書きします。</summary>
        public static VlatFileAccessPolicy Default { get; } = new VlatFileAccessPolicy();

        /// <summary>
        /// 範囲を制限しない従来の動作。自分で作成した、または出所を確認したファイルにだけ使ってください。
        /// 読み込み上限は既定のまま適用します。
        /// </summary>
        public static VlatFileAccessPolicy TrustedLegacy { get; } = new VlatFileAccessPolicy(null, true);

        /// <summary>外部ファイルを置いてよいフォルダ。nullならJSONファイルのフォルダです。</summary>
        public string AllowedRootDirectory { get; }

        /// <summary>範囲を制限しないかどうか。</summary>
        public bool AllowOutsideRoot { get; }

        /// <summary>読み込み上限。</summary>
        public VlatReadLimits Limits { get; }
    }
}
