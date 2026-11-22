using System;

namespace VLat.Core.Serialization
{
    /// <summary>
    /// 外部から受け取るVLatファイルを読むときの資源上限です。
    /// 上限を超える入力は、メモリを確保する前に診断で拒否します。
    /// </summary>
    /// <remarks>
    /// 既定値の根拠は <c>docs/spec/02_FORMAT_SPEC.md</c> の「読み込み上限」にあります。
    /// 上限はファイル形式の妥当性ではなく、このリーダー実装の資源ポリシーです。
    /// </remarks>
    public sealed class VlatReadLimits
    {
        /// <summary>.vlat.jsonの既定の最大バイト数（16 MiB）。</summary>
        public const long DefaultMaxJsonBytes = 16L * 1024 * 1024;

        /// <summary>JSONの既定の最大ネスト深さ（32）。</summary>
        public const int DefaultMaxJsonDepth = 32;

        /// <summary>JSONの既定の最大トークン数（終端記号を除く。1,000,000）。</summary>
        public const int DefaultMaxJsonTokens = 1000000;

        /// <summary>buffers配列の既定の最大要素数（256）。</summary>
        public const int DefaultMaxBufferCount = 256;

        /// <summary>1つの外部バッファの既定の最大バイト数（512 MiB）。</summary>
        public const long DefaultMaxBufferBytes = 512L * 1024 * 1024;

        /// <summary>全外部バッファの既定の合計最大バイト数（512 MiB）。</summary>
        public const long DefaultMaxTotalBufferBytes = 512L * 1024 * 1024;

        /// <summary>textures配列の既定の最大要素数（256）。</summary>
        public const int DefaultMaxTextureCount = 256;

        /// <summary>1つのエンコード済みテクスチャの既定の最大バイト数（64 MiB）。</summary>
        public const long DefaultMaxTextureEncodedBytes = 64L * 1024 * 1024;

        /// <summary>テクスチャ1辺の既定の最大ピクセル数（8192）。</summary>
        public const int DefaultMaxTextureDimension = 8192;

        /// <summary>全テクスチャのデコード後（RGBA32換算）の既定の合計最大バイト数（1 GiB）。</summary>
        public const long DefaultMaxTotalTextureDecodedBytes = 1024L * 1024 * 1024;

        /// <summary>上限を指定して作成します。省略した値は既定値になります。</summary>
        /// <param name="maxJsonBytes">.vlat.jsonの最大バイト数。文字列入力では最大文字数として扱います。</param>
        /// <param name="maxJsonDepth">JSONのobject/arrayの最大ネスト深さ。ルートobjectが1です。</param>
        /// <param name="maxJsonTokens">JSONの最大トークン数（object/array開始、プロパティ名、値）。</param>
        /// <param name="maxBufferCount">buffers配列の最大要素数。</param>
        /// <param name="maxBufferBytes">1つの外部バッファの最大バイト数。</param>
        /// <param name="maxTotalBufferBytes">全外部バッファの合計最大バイト数。</param>
        /// <param name="maxTextureCount">textures配列の最大要素数。</param>
        /// <param name="maxTextureEncodedBytes">1つのエンコード済み画像の最大バイト数。</param>
        /// <param name="maxTextureDimension">画像1辺の最大ピクセル数。</param>
        /// <param name="maxTotalTextureDecodedBytes">全画像のデコード後（RGBA32換算）の合計最大バイト数。</param>
        /// <exception cref="ArgumentOutOfRangeException">いずれかの値が1未満の場合に送出されます。</exception>
        public VlatReadLimits(
            long maxJsonBytes = DefaultMaxJsonBytes,
            int maxJsonDepth = DefaultMaxJsonDepth,
            int maxJsonTokens = DefaultMaxJsonTokens,
            int maxBufferCount = DefaultMaxBufferCount,
            long maxBufferBytes = DefaultMaxBufferBytes,
            long maxTotalBufferBytes = DefaultMaxTotalBufferBytes,
            int maxTextureCount = DefaultMaxTextureCount,
            long maxTextureEncodedBytes = DefaultMaxTextureEncodedBytes,
            int maxTextureDimension = DefaultMaxTextureDimension,
            long maxTotalTextureDecodedBytes = DefaultMaxTotalTextureDecodedBytes)
        {
            MaxJsonBytes = RequirePositive(maxJsonBytes, nameof(maxJsonBytes));
            MaxJsonDepth = (int)RequirePositive(maxJsonDepth, nameof(maxJsonDepth));
            MaxJsonTokens = (int)RequirePositive(maxJsonTokens, nameof(maxJsonTokens));
            MaxBufferCount = (int)RequirePositive(maxBufferCount, nameof(maxBufferCount));
            MaxBufferBytes = RequirePositive(maxBufferBytes, nameof(maxBufferBytes));
            MaxTotalBufferBytes = RequirePositive(maxTotalBufferBytes, nameof(maxTotalBufferBytes));
            MaxTextureCount = (int)RequirePositive(maxTextureCount, nameof(maxTextureCount));
            MaxTextureEncodedBytes = RequirePositive(maxTextureEncodedBytes, nameof(maxTextureEncodedBytes));
            MaxTextureDimension = (int)RequirePositive(maxTextureDimension, nameof(maxTextureDimension));
            MaxTotalTextureDecodedBytes = RequirePositive(maxTotalTextureDecodedBytes, nameof(maxTotalTextureDecodedBytes));
        }

        /// <summary>既定の上限です。インスタンスは不変です。</summary>
        public static VlatReadLimits Default { get; } = new VlatReadLimits();

        /// <summary>.vlat.jsonの最大バイト数。文字列入力では最大文字数です。</summary>
        public long MaxJsonBytes { get; }

        /// <summary>JSONのobject/arrayの最大ネスト深さ。ルートobjectが1です。</summary>
        public int MaxJsonDepth { get; }

        /// <summary>JSONの最大トークン数（object/array開始、プロパティ名、値）。</summary>
        public int MaxJsonTokens { get; }

        /// <summary>buffers配列の最大要素数。</summary>
        public int MaxBufferCount { get; }

        /// <summary>1つの外部バッファの最大バイト数。</summary>
        public long MaxBufferBytes { get; }

        /// <summary>全外部バッファの合計最大バイト数。</summary>
        public long MaxTotalBufferBytes { get; }

        /// <summary>textures配列の最大要素数。</summary>
        public int MaxTextureCount { get; }

        /// <summary>1つのエンコード済み画像の最大バイト数。</summary>
        public long MaxTextureEncodedBytes { get; }

        /// <summary>画像1辺の最大ピクセル数。</summary>
        public int MaxTextureDimension { get; }

        /// <summary>全画像のデコード後（RGBA32換算）の合計最大バイト数。</summary>
        public long MaxTotalTextureDecodedBytes { get; }

        private static long RequirePositive(long value, string name)
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(name, value, "上限は1以上である必要があります。");
            }

            return value;
        }
    }
}
