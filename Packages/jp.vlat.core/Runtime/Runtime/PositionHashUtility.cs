using System;
using System.Globalization;
using UnityEngine;

namespace VLat.Runtime
{
    /// <summary>MeshBinding用の頂点位置ハッシュを計算します。</summary>
    public static class PositionHashUtility
    {
        private const ulong Prime1 = 11400714785074694791UL;
        private const ulong Prime2 = 14029467366897019727UL;
        private const ulong Prime3 = 1609587929392839161UL;
        private const ulong Prime4 = 9650029242287828579UL;
        private const ulong Prime5 = 2870177450012600261UL;

        /// <summary>Meshの頂点位置をインデックス順にxxHash64へ変換します。</summary>
        /// <param name="mesh">読み取り対象のMesh。内容は変更しません。</param>
        /// <returns><c>xxh64:</c>に小文字16進数を続けたハッシュ。</returns>
        /// <exception cref="ArgumentNullException">meshがnullの場合に送出されます。</exception>
        public static string Calculate(Mesh mesh)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            return Calculate(mesh.vertices);
        }

        /// <summary>頂点位置配列をインデックス順にxxHash64へ変換します。</summary>
        /// <param name="vertices">読み取り対象の頂点位置。</param>
        /// <returns><c>xxh64:</c>に小文字16進数を続けたハッシュ。</returns>
        /// <exception cref="ArgumentNullException">verticesがnullの場合に送出されます。</exception>
        public static string Calculate(Vector3[] vertices)
        {
            if (vertices == null)
            {
                throw new ArgumentNullException(nameof(vertices));
            }

            var bytes = new byte[checked(vertices.Length * 12)];
            var offset = 0;
            for (var index = 0; index < vertices.Length; index++)
            {
                var vertex = vertices[index];
                WriteSingleLittleEndian(bytes, offset, vertex.x);
                WriteSingleLittleEndian(bytes, offset + 4, vertex.y);
                WriteSingleLittleEndian(bytes, offset + 8, vertex.z);
                offset += 12;
            }

            var hash = ComputeXxHash64(bytes, 0UL);
            return "xxh64:" + hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        internal static ulong ComputeXxHash64(byte[] bytes, ulong seed)
        {
            var length = bytes.Length;
            var offset = 0;
            ulong hash;
            unchecked
            {
                if (length >= 32)
                {
                    var value1 = seed + Prime1 + Prime2;
                    var value2 = seed + Prime2;
                    var value3 = seed;
                    var value4 = seed - Prime1;
                    var limit = length - 32;
                    do
                    {
                        value1 = Round(value1, ReadUInt64LittleEndian(bytes, offset));
                        value2 = Round(value2, ReadUInt64LittleEndian(bytes, offset + 8));
                        value3 = Round(value3, ReadUInt64LittleEndian(bytes, offset + 16));
                        value4 = Round(value4, ReadUInt64LittleEndian(bytes, offset + 24));
                        offset += 32;
                    }
                    while (offset <= limit);

                    hash = RotateLeft(value1, 1)
                        + RotateLeft(value2, 7)
                        + RotateLeft(value3, 12)
                        + RotateLeft(value4, 18);
                    hash = MergeRound(hash, value1);
                    hash = MergeRound(hash, value2);
                    hash = MergeRound(hash, value3);
                    hash = MergeRound(hash, value4);
                }
                else
                {
                    hash = seed + Prime5;
                }

                hash += (ulong)length;
                while (offset <= length - 8)
                {
                    var value = Round(0UL, ReadUInt64LittleEndian(bytes, offset));
                    hash ^= value;
                    hash = (RotateLeft(hash, 27) * Prime1) + Prime4;
                    offset += 8;
                }

                if (offset <= length - 4)
                {
                    hash ^= ReadUInt32LittleEndian(bytes, offset) * Prime1;
                    hash = (RotateLeft(hash, 23) * Prime2) + Prime3;
                    offset += 4;
                }

                while (offset < length)
                {
                    hash ^= bytes[offset] * Prime5;
                    hash = RotateLeft(hash, 11) * Prime1;
                    offset++;
                }

                hash ^= hash >> 33;
                hash *= Prime2;
                hash ^= hash >> 29;
                hash *= Prime3;
                hash ^= hash >> 32;
                return hash;
            }
        }

        private static ulong Round(ulong accumulator, ulong input)
        {
            unchecked
            {
                accumulator += input * Prime2;
                accumulator = RotateLeft(accumulator, 31);
                return accumulator * Prime1;
            }
        }

        private static ulong MergeRound(ulong accumulator, ulong value)
        {
            unchecked
            {
                accumulator ^= Round(0UL, value);
                return (accumulator * Prime1) + Prime4;
            }
        }

        private static ulong RotateLeft(ulong value, int count)
        {
            return (value << count) | (value >> (64 - count));
        }

        private static ulong ReadUInt64LittleEndian(byte[] bytes, int offset)
        {
            return bytes[offset]
                | ((ulong)bytes[offset + 1] << 8)
                | ((ulong)bytes[offset + 2] << 16)
                | ((ulong)bytes[offset + 3] << 24)
                | ((ulong)bytes[offset + 4] << 32)
                | ((ulong)bytes[offset + 5] << 40)
                | ((ulong)bytes[offset + 6] << 48)
                | ((ulong)bytes[offset + 7] << 56);
        }

        private static ulong ReadUInt32LittleEndian(byte[] bytes, int offset)
        {
            return bytes[offset]
                | ((ulong)bytes[offset + 1] << 8)
                | ((ulong)bytes[offset + 2] << 16)
                | ((ulong)bytes[offset + 3] << 24);
        }

        private static void WriteSingleLittleEndian(byte[] destination, int offset, float value)
        {
            var bits = BitConverter.SingleToInt32Bits(value);
            destination[offset] = (byte)bits;
            destination[offset + 1] = (byte)(bits >> 8);
            destination[offset + 2] = (byte)(bits >> 16);
            destination[offset + 3] = (byte)(bits >> 24);
        }
    }
}
