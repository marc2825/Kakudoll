using System;
using System.Globalization;

namespace VLat.Core.Serialization
{
    /// <summary>
    /// 画像をデコードする前に、ヘッダーから形式と寸法を確認し、1回の読み込み全体の
    /// デコード後メモリ（RGBA32換算）を上限内に収めます。
    /// </summary>
    /// <remarks>
    /// UnityのImageConversion.LoadImageが扱うPNGとJPEGだけを受け付けます。
    /// 同じ画像を複数のtextureが参照する場合も、デコードする回数だけ予算を消費します。
    /// </remarks>
    public sealed class VlatTextureBudget
    {
        private readonly VlatReadLimits _limits;
        private long _reservedBytes;

        /// <summary>上限を指定して作成します。</summary>
        /// <param name="limits">読み込み上限。nullなら既定。</param>
        public VlatTextureBudget(VlatReadLimits limits = null)
        {
            _limits = limits ?? VlatReadLimits.Default;
        }

        /// <summary>これまでに予約したデコード後のバイト数（RGBA32換算）。</summary>
        public long ReservedBytes => _reservedBytes;

        /// <summary>エンコード済み画像のバイト数が上限以下かを確認します（ファイルを読む前に使います）。</summary>
        /// <param name="encodedLength">エンコード済み画像のバイト数。</param>
        /// <param name="error">上限超過時の理由。</param>
        /// <returns>上限以下なら<c>true</c>。</returns>
        public bool CheckEncodedLength(long encodedLength, out string error)
        {
            error = null;
            if (encodedLength > _limits.MaxTextureEncodedBytes)
            {
                error = "画像ファイルが大きすぎます（" + VlatExternalFiles.FormatBytes(encodedLength) + "、上限 " +
                        VlatExternalFiles.FormatBytes(_limits.MaxTextureEncodedBytes) + "）。";
                return false;
            }

            return true;
        }

        /// <summary>画像のヘッダーを確認し、デコード後のメモリを予約します。</summary>
        /// <param name="encoded">エンコード済み画像（PNGまたはJPEG）。</param>
        /// <param name="error">拒否時の理由。</param>
        /// <returns>デコードしてよいときは<c>true</c>。</returns>
        public bool TryReserve(byte[] encoded, out string error)
        {
            if (encoded == null || encoded.Length == 0)
            {
                error = "画像データが空です。";
                return false;
            }

            if (!CheckEncodedLength(encoded.LongLength, out error))
            {
                return false;
            }

            string format;
            int width;
            int height;
            if (!VlatImageHeader.TryReadDimensions(encoded, out format, out width, out height, out error))
            {
                return false;
            }

            if (width > _limits.MaxTextureDimension || height > _limits.MaxTextureDimension)
            {
                error = "画像の寸法が大きすぎます（" + width.ToString(CultureInfo.InvariantCulture) + "x" +
                        height.ToString(CultureInfo.InvariantCulture) + "、1辺の上限 " +
                        _limits.MaxTextureDimension.ToString(CultureInfo.InvariantCulture) + "）。";
                return false;
            }

            var decoded = (long)width * height * 4;
            if (decoded > _limits.MaxTotalTextureDecodedBytes - _reservedBytes)
            {
                error = "画像のデコード後の合計が上限（" +
                        VlatExternalFiles.FormatBytes(_limits.MaxTotalTextureDecodedBytes) + "）を超えます。";
                return false;
            }

            _reservedBytes += decoded;
            error = null;
            return true;
        }
    }

    /// <summary>PNG・JPEGのヘッダーから寸法を読み取ります。画素はデコードしません。</summary>
    public static class VlatImageHeader
    {
        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>画像の形式と寸法を読み取ります。</summary>
        /// <param name="data">エンコード済み画像。</param>
        /// <param name="format">"png" または "jpeg"。</param>
        /// <param name="width">幅（ピクセル）。</param>
        /// <param name="height">高さ（ピクセル）。</param>
        /// <param name="error">読み取れないときの理由。</param>
        /// <returns>PNGまたはJPEGとして寸法を読み取れたときは<c>true</c>。</returns>
        public static bool TryReadDimensions(byte[] data, out string format, out int width, out int height, out string error)
        {
            format = null;
            width = 0;
            height = 0;
            error = null;
            if (data == null)
            {
                error = "画像データがありません。";
                return false;
            }

            if (StartsWith(data, PngSignature))
            {
                format = "png";
                return TryReadPng(data, out width, out height, out error);
            }

            if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            {
                format = "jpeg";
                return TryReadJpeg(data, out width, out height, out error);
            }

            error = "PNGまたはJPEGではありません（対応する画像形式はPNGとJPEGです）。";
            return false;
        }

        private static bool TryReadPng(byte[] data, out int width, out int height, out string error)
        {
            width = 0;
            height = 0;
            error = null;
            // 署名(8) + IHDR長さ(4) + 型(4) + 幅(4) + 高さ(4)
            if (data.Length < 24 || ReadInt32BigEndian(data, 8) != 13 ||
                data[12] != (byte)'I' || data[13] != (byte)'H' || data[14] != (byte)'D' || data[15] != (byte)'R')
            {
                error = "PNGのIHDRが不正です。";
                return false;
            }

            var rawWidth = ReadInt32BigEndian(data, 16);
            var rawHeight = ReadInt32BigEndian(data, 20);
            if (rawWidth < 1 || rawHeight < 1)
            {
                error = "PNGの寸法が不正です。";
                return false;
            }

            width = rawWidth;
            height = rawHeight;
            return true;
        }

        private static bool TryReadJpeg(byte[] data, out int width, out int height, out string error)
        {
            width = 0;
            height = 0;
            error = null;
            var offset = 2;
            while (offset < data.Length)
            {
                if (data[offset] != 0xFF)
                {
                    error = "JPEGのマーカーが不正です。";
                    return false;
                }

                while (offset < data.Length && data[offset] == 0xFF)
                {
                    offset++;
                }

                if (offset >= data.Length)
                {
                    break;
                }

                var marker = data[offset++];
                if (marker == 0xD9 || marker == 0xDA)
                {
                    break;
                }

                if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                {
                    continue;
                }

                if (offset + 2 > data.Length)
                {
                    break;
                }

                var length = (data[offset] << 8) | data[offset + 1];
                if (length < 2 || offset + length > data.Length)
                {
                    error = "JPEGのセグメント長が不正です。";
                    return false;
                }

                var isFrame = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                if (isFrame)
                {
                    if (length < 7)
                    {
                        error = "JPEGのフレームヘッダーが不正です。";
                        return false;
                    }

                    height = (data[offset + 3] << 8) | data[offset + 4];
                    width = (data[offset + 5] << 8) | data[offset + 6];
                    if (width < 1 || height < 1)
                    {
                        error = "JPEGの寸法が不正です。";
                        return false;
                    }

                    return true;
                }

                offset += length;
            }

            error = "JPEGのフレームヘッダーが見つかりません。";
            return false;
        }

        private static bool StartsWith(byte[] data, byte[] prefix)
        {
            if (data.Length < prefix.Length)
            {
                return false;
            }

            for (var i = 0; i < prefix.Length; i++)
            {
                if (data[i] != prefix[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static int ReadInt32BigEndian(byte[] data, int offset)
        {
            return unchecked((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }
    }
}
