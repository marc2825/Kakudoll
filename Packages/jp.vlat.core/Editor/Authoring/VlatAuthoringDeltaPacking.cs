using System;
using System.Collections.Generic;
using UnityEngine;

namespace VLat.Editor.Authoring
{
    // Authoring keyformの位置差分をUnity直列化(Undo/Recovery)用にまとめるEditor専用codec。
    // 保存形式(.vlat.json/.vlat.bin)とは無関係で、値はIEEE 754 binary32のbit列をそのまま保持する。
    internal static class VlatAuthoringDeltaPacking
    {
        // レイアウト(little-endian):
        //   byte   format (=1)
        //   byte   flags  (bit0: 法線列あり)
        //   int32  count
        //   int32[count]      vertex index
        //   float32[3*count]  position delta
        //   float32[3*count]  normal delta (flags bit0のときだけ)
        private const byte Format = 1;
        private const byte HasNormals = 1;
        private const int HeaderBytes = 6;
        // 誤ったデータで巨大な割当を行わないための上限。編集commandの16 MiB制限より十分大きい。
        internal const int MaximumCount = 1 << 24;

        [ThreadStatic] private static byte[] _buffer;

        /// <summary>差分列をbase64へまとめます。空・nullは空文字列です。</summary>
        internal static string Encode(IReadOnlyList<VlatAuthoringVertexDelta> deltas)
        {
            if (deltas == null || deltas.Count == 0) return string.Empty;
            var count = deltas.Count;
            var normals = false;
            for (var i = 0; i < count; i++)
                if (deltas[i] != null && !deltas[i].NormalDelta.Equals(Vector3.zero)) { normals = true; break; }
            var length = HeaderBytes + count * (normals ? 28 : 16);
            var buffer = _buffer;
            if (buffer == null || buffer.Length < length) _buffer = buffer = new byte[Math.Max(length, 4096)];
            buffer[0] = Format;
            buffer[1] = normals ? HasNormals : (byte)0;
            WriteInt(buffer, 2, count);
            var indexAt = HeaderBytes;
            var positionAt = indexAt + count * 4;
            var normalAt = positionAt + count * 12;
            for (var i = 0; i < count; i++)
            {
                var delta = deltas[i];
                // null要素は従来のUnity直列化と同じく既定値の要素として保持する。
                var index = delta?.VertexIndex ?? 0;
                var position = delta?.PositionDelta ?? Vector3.zero;
                WriteInt(buffer, indexAt + i * 4, index);
                WriteFloat(buffer, positionAt + i * 12, position.x);
                WriteFloat(buffer, positionAt + i * 12 + 4, position.y);
                WriteFloat(buffer, positionAt + i * 12 + 8, position.z);
                if (!normals) continue;
                var normal = delta?.NormalDelta ?? Vector3.zero;
                WriteFloat(buffer, normalAt + i * 12, normal.x);
                WriteFloat(buffer, normalAt + i * 12 + 4, normal.y);
                WriteFloat(buffer, normalAt + i * 12 + 8, normal.z);
            }

            return Convert.ToBase64String(buffer, 0, length);
        }

        /// <summary>base64を差分列へ戻します。不正な入力では例外を投げずに理由を返します。</summary>
        internal static bool TryDecode(string packed, out List<VlatAuthoringVertexDelta> deltas, out string error)
        {
            deltas = new List<VlatAuthoringVertexDelta>();
            error = null;
            if (string.IsNullOrEmpty(packed)) return true;
            byte[] data;
            try { data = Convert.FromBase64String(packed); }
            catch (FormatException) { error = "packed deltaのbase64が不正です。"; return false; }
            if (data.Length < HeaderBytes || data[0] != Format || (data[1] & ~HasNormals) != 0)
            {
                error = "packed deltaの形式が未対応です。";
                return false;
            }

            var count = ReadInt(data, 2);
            var normals = (data[1] & HasNormals) != 0;
            if (count < 1 || count > MaximumCount ||
                data.Length != HeaderBytes + (long)count * (normals ? 28 : 16))
            {
                error = "packed deltaの要素数と長さが一致しません。";
                return false;
            }

            deltas.Capacity = count;
            var indexAt = HeaderBytes;
            var positionAt = indexAt + count * 4;
            var normalAt = positionAt + count * 12;
            for (var i = 0; i < count; i++)
            {
                var item = new VlatAuthoringVertexDelta
                {
                    VertexIndex = ReadInt(data, indexAt + i * 4),
                    PositionDelta = new Vector3(
                        ReadFloat(data, positionAt + i * 12),
                        ReadFloat(data, positionAt + i * 12 + 4),
                        ReadFloat(data, positionAt + i * 12 + 8)),
                };
                if (normals)
                    item.NormalDelta = new Vector3(
                        ReadFloat(data, normalAt + i * 12),
                        ReadFloat(data, normalAt + i * 12 + 4),
                        ReadFloat(data, normalAt + i * 12 + 8));
                deltas.Add(item);
            }

            return true;
        }

        private static void WriteInt(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteFloat(byte[] buffer, int offset, float value) =>
            WriteInt(buffer, offset, BitConverter.SingleToInt32Bits(value));

        private static int ReadInt(byte[] data, int offset) =>
            data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16 | data[offset + 3] << 24;

        private static float ReadFloat(byte[] data, int offset) => BitConverter.Int32BitsToSingle(ReadInt(data, offset));
    }
}
