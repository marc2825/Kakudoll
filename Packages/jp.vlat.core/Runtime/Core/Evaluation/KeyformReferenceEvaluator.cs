using System;
using System.Collections.Generic;
using VLat.Core.Model;
using VLat.Core.Validation;

namespace VLat.Core.Evaluation
{
    /// <summary>検証済みのスパース差分をCPUで評価する参照実装です。Unity Runtimeへの自動適用は行いません。</summary>
    public sealed class KeyformReferenceEvaluator
    {
        private readonly YawKeyformEvaluator _yaw;
        private readonly double[] _weights;
        private readonly int[][] _indices;
        private readonly Float3[][] _deltas;
        private readonly int _vertexCount;

        /// <summary>ドキュメントとバイナリを検証し、外部変更から独立した評価データを構築します。</summary>
        public KeyformReferenceEvaluator(VlatDocument document, IReadOnlyList<byte[]> buffers)
        {
            var diagnostics = VlatValidator.Validate(document, buffers ?? Array.Empty<byte[]>());
            for (var i = 0; i < diagnostics.Count; i++)
                if (diagnostics[i].Severity == VlatDiagnosticSeverity.Error)
                    throw new ArgumentException(diagnostics[i].Message, nameof(document));
            if (document.KeyformGroups.Count != 1)
                throw new ArgumentException("KeyformGroupが1件必要です。", nameof(document));
            var group = document.KeyformGroups[0];
            _vertexCount = document.Metadata.TargetAvatar.MeshBindings[group.MeshBinding].VertexCount;
            _yaw = new YawKeyformEvaluator(group);
            _weights = new double[group.Keys.Count];
            _indices = new int[group.Keys.Count][];
            _deltas = new Float3[group.Keys.Count][];
            for (var k = 0; k < group.Keys.Count; k++)
            {
                var delta = group.Keys[k].Delta;
                if (delta == null)
                {
                    _indices[k] = Array.Empty<int>();
                    _deltas[k] = Array.Empty<Float3>();
                    continue;
                }
                var indices = document.BufferViews[delta.Indices.BufferView];
                var positions = document.BufferViews[delta.Positions.BufferView];
                var count = checked((int)(indices.ByteLength / 4));
                _indices[k] = new int[count];
                _deltas[k] = new Float3[count];
                for (var v = 0; v < count; v++)
                {
                    _indices[k][v] = ReadInt(buffers[indices.Buffer], checked((int)indices.ByteOffset + v * 4));
                    var offset = checked((int)positions.ByteOffset + v * 12);
                    var data = buffers[positions.Buffer];
                    _deltas[k][v] = new Float3(ReadFloat(data, offset), ReadFloat(data, offset + 4), ReadFloat(data, offset + 8));
                }
            }
        }

        /// <summary>頂点順の差分を呼び出し側バッファへ上書きします。定常評価でGC割り当てはありません。</summary>
        /// <remarks>同じ評価器の並行呼び出しには対応しません。座標は基準メッシュローカルです。</remarks>
        public void Evaluate(double yaw, Float3[] destination)
        {
            if (destination == null || destination.Length != _vertexCount)
                throw new ArgumentException("出力配列の長さは元メッシュ頂点数と一致する必要があります。", nameof(destination));
            Array.Clear(destination, 0, destination.Length);
            _yaw.Evaluate(yaw, _weights);
            for (var k = 0; k < _weights.Length; k++)
            {
                var weight = _weights[k];
                if (weight == 0) continue;
                for (var v = 0; v < _indices[k].Length; v++)
                {
                    var index = _indices[k][v];
                    var old = destination[index];
                    var d = _deltas[k][v];
                    destination[index] = new Float3(old.X + weight * d.X, old.Y + weight * d.Y, old.Z + weight * d.Z);
                }
            }
        }

        private static int ReadInt(byte[] data, int offset) =>
            data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16 | data[offset + 3] << 24;

        private static float ReadFloat(byte[] data, int offset) => BitConverter.Int32BitsToSingle(ReadInt(data, offset));
    }
}
