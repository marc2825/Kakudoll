using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using VLat.Core.Evaluation;
using VLat.Core.Model;
using VLat.Core.Serialization;
using VLat.Core.Validation;
using VLat.Runtime;

namespace VLat.Editor.Authoring
{
    /// <summary>同一MeshのBlendShape位置差分を読み取り専用でv0.2キーへ取り込みます。</summary>
    public static class VlatBlendShapeKeyformImporter
    {
        /// <summary>単一100%フレームの位置差分を取り込みます。-1は明示的な中立キーです。</summary>
        /// <remarks>
        /// 元Mesh、Renderer weight、Material、Expressionは変更しません。
        /// 法線・接線差分は対象外であることを結果の警告へ記録します。
        /// 外部Mesh間の転写やPoseからの逆変換は行いません。
        /// </remarks>
        public static VlatLoadResult ImportPositions(Mesh source, MeshBinding binding, string topologyHash,
            IReadOnlyList<int> shapeIndices, IReadOnlyList<double> yaws, double supportStart,
            double supportEnd, string binaryUri = "keyforms.vlat.bin")
        {
            return ImportPositions(source, binding, topologyHash, shapeIndices, yaws, supportStart, supportEnd,
                binaryUri, sourceHashesVerified: false);
        }

        // sourceHashesVerified: 呼出側が同じ呼出の中で同じsource Meshから位置/Topology指紋を計算済み
        // （VlatKeyformBindingUtility.Capture）の場合だけtrue。同値であることが確定した再計算だけを省き、
        // 有限値・名前・頂点数・frame・document検証は従来どおり行う。
        internal static VlatLoadResult ImportPositions(Mesh source, MeshBinding binding, string topologyHash,
            IReadOnlyList<int> shapeIndices, IReadOnlyList<double> yaws, double supportStart,
            double supportEnd, string binaryUri, bool sourceHashesVerified)
        {
            RequireReadable(source);
            var basePositions = source.vertices;
            for (var v = 0; v < basePositions.Length; v++)
            {
                var p = basePositions[v];
                if (!IsFinite(p.x) || !IsFinite(p.y) || !IsFinite(p.z))
                    throw new ArgumentException("基準Meshの頂点位置に非有限値があります。", nameof(source));
            }
            if (binding == null || source.vertexCount != binding.VertexCount ||
                source.name != binding.MeshName ||
                (!sourceHashesVerified &&
                 (!string.Equals(PositionHashUtility.Calculate(source), binding.PositionHash, StringComparison.OrdinalIgnoreCase) ||
                  CalculateTopologyHash(source) != topologyHash)))
                throw new ArgumentException("基準Meshの名前・頂点数・位置・Topologyが一致しません。", nameof(binding));
            if (shapeIndices == null || yaws == null || shapeIndices.Count != yaws.Count ||
                yaws.Count < 1 || yaws.Count > VlatValidator.MaximumKeyformKeys)
                throw new ArgumentException("同じ数のShape indexとYawを1〜16件指定してください。");
            var deltas = new Float3[shapeIndices.Count][];
            var positions = new Vector3[source.vertexCount];
            for (var k = 0; k < shapeIndices.Count; k++)
            {
                var shape = shapeIndices[k];
                if (shape < -1 || shape >= source.blendShapeCount)
                    throw new ArgumentOutOfRangeException(nameof(shapeIndices));
                Array.Clear(positions, 0, positions.Length);
                if (shape >= 0)
                {
                    if (source.GetBlendShapeFrameCount(shape) != 1 ||
                        source.GetBlendShapeFrameWeight(shape, 0) != 100f)
                        throw new ArgumentException("単一100%フレームのみ対応します。複数フレームや他のweightは明示的に変換してください。");
                    source.GetBlendShapeFrameVertices(shape, 0, positions, null, null);
                }
                var key = new Float3[positions.Length];
                for (var v = 0; v < key.Length; v++)
                    key[v] = new Float3(positions[v].x, positions[v].y, positions[v].z);
                deltas[k] = key;
            }
            var result = KeyformDocumentBuilder.Create(binding, topologyHash, yaws, deltas, supportStart, supportEnd, binaryUri);
            var diagnostics = new List<VlatDiagnostic>(result.Diagnostics)
            {
                new VlatDiagnostic(VlatDiagnosticSeverity.Warning, "$.keyformGroups", 0, 0,
                    "位置差分のみ取り込みました。法線・接線差分、表情設定、SkinningのPose変換は対象外です。"),
            };
            return new VlatLoadResult(result.Document, result.Buffers, diagnostics);
        }

        private static void HashInt32Pair(HashAlgorithm sha, byte[] buffer, int first, int second)
        {
            WriteInt32(buffer, 0, first);
            WriteInt32(buffer, 4, second);
            sha.TransformBlock(buffer, 0, 8, null, 0);
        }

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        /// <summary>三角形Topologyを元頂点indexで指紋化します。読み取りのみでMeshを変更しません。</summary>
        public static string CalculateTopologyHash(Mesh source)
        {
            RequireReadable(source);
            long count = 0;
            for (var s = 0; s < source.subMeshCount; s++)
            {
                if (source.GetTopology(s) != MeshTopology.Triangles)
                    throw new ArgumentException("三角形Meshのみ対応します。", nameof(source));
                count += source.GetIndexCount(s);
            }
            if (count > 2000000) throw new ArgumentException("Topology指紋の上限は200万indexです。", nameof(source));
            // 入力byte列は従来（BinaryWriterのlittle-endian int32列）と同一:
            //   vertexCount, subMeshCount, 各subMeshの (topology, indexCount, indices...)。
            // 1要素ずつのstream書込みを避け、index配列をまとめてhashへ流す。
            using (var sha = SHA256.Create())
            {
                var header = new byte[8];
                HashInt32Pair(sha, header, source.vertexCount, source.subMeshCount);
                byte[] block = null;
                for (var s = 0; s < source.subMeshCount; s++)
                {
                    var indices = source.GetIndices(s, true);
                    HashInt32Pair(sha, header, (int)source.GetTopology(s), indices.Length);
                    var length = indices.Length * 4;
                    if (block == null || block.Length < length) block = new byte[length];
                    if (BitConverter.IsLittleEndian) Buffer.BlockCopy(indices, 0, block, 0, length);
                    else for (var i = 0; i < indices.Length; i++) WriteInt32(block, i * 4, indices[i]);
                    sha.TransformBlock(block, 0, length, null, 0);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                var hash = sha.Hash;
                var result = new StringBuilder("sha256:");
                for (var i = 0; i < hash.Length; i++) result.Append(hash[i].ToString("x2"));
                return result.ToString();
            }
        }

        private static void RequireReadable(Mesh source)
        {
            if (source == null || !source.isReadable || source.vertexCount < 1 ||
                source.vertexCount > VlatValidator.MaximumKeyformVertices)
                throw new ArgumentException("読み取り可能な1〜200000頂点のMeshが必要です。", nameof(source));
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
