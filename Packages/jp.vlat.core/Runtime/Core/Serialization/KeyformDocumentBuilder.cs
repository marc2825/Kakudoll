using System;
using System.Collections.Generic;
using System.IO;
using VLat.Core.Evaluation;
using VLat.Core.Model;
using VLat.Core.Validation;

namespace VLat.Core.Serialization
{
    /// <summary>基準メッシュに対する位置差分から、決定的なv0.2 JSON/bin入力を構築します。</summary>
    public static class KeyformDocumentBuilder
    {
        /// <summary>単独グループを生成します。全キーの配列は共通の元頂点順で指定してください。</summary>
        /// <param name="binding">位置ハッシュを含む基準メッシュの参照。</param>
        /// <param name="topologyHash">基準メッシュの三角形配列ハッシュ。</param>
        /// <param name="yaws">支持区間内の展開済みYaw。昇順で指定します。</param>
        /// <param name="deltas">各Yawでの頂点位置差分。法線・接線はこのスライスの対象外です。</param>
        /// <param name="supportStart">中立に戻る支持区間の開始角度。</param>
        /// <param name="supportEnd">中立に戻る支持区間の終了角度。</param>
        /// <param name="binaryUri">保存先JSONからの相対bin URI。</param>
        /// <returns>検証済みドキュメント、バイナリ、および診断。</returns>
        public static VlatLoadResult Create(MeshBinding binding, string topologyHash,
            IReadOnlyList<double> yaws, IReadOnlyList<Float3[]> deltas,
            double supportStart, double supportEnd, string binaryUri = "keyforms.vlat.bin")
        {
            if (binding == null || binding.VertexCount < 1 || binding.VertexCount > VlatValidator.MaximumKeyformVertices)
                throw new ArgumentException("基準メッシュの頂点数は1〜200000です。", nameof(binding));
            if (yaws == null || deltas == null || yaws.Count != deltas.Count ||
                yaws.Count < 1 || yaws.Count > VlatValidator.MaximumKeyformKeys)
                throw new ArgumentException("同じ数のYawと差分を1〜16キー指定してください。");
            var definitions = new YawKeyform[yaws.Count];
            for (var k = 0; k < yaws.Count; k++) definitions[k] = new YawKeyform(yaws[k], null);
            var definition = Group(topologyHash, supportStart, supportEnd, definitions);
            var definitionDiagnostics = VlatValidator.ValidateKeyformDefinition(definition);
            if (VlatLoadResult.ContainsError(definitionDiagnostics))
                return new VlatLoadResult(null, null, definitionDiagnostics);

            var counts = new int[yaws.Count];
            long bytes = 0;
            // 全入力を検証してからバイナリを確保します。
            for (var k = 0; k < yaws.Count; k++)
            {
                if (deltas[k] == null || deltas[k].Length != binding.VertexCount)
                    throw new ArgumentException("全キーの頂点数と元頂点順を基準メッシュに一致させてください。", nameof(deltas));
                for (var v = 0; v < binding.VertexCount; v++)
                {
                    var d = deltas[k][v];
                    RequireFloat(d.X); RequireFloat(d.Y); RequireFloat(d.Z);
                    if (!IsZero(d)) counts[k]++;
                }
                bytes += counts[k] * 16L;
            }
            if (bytes > VlatValidator.MaximumKeyformBufferBytes)
                throw new ArgumentException("スパース差分が16 MiBを超えます。", nameof(deltas));
            var views = new List<VlatBufferView>();
            byte[] binary;
            using (var stream = new MemoryStream((int)bytes))
            using (var writer = new BinaryWriter(stream))
            {
                for (var k = 0; k < yaws.Count; k++)
                {
                    if (counts[k] == 0) continue; // 中立キーはnull。長さ0のbufferViewは生成しません。
                    var indexView = views.Count;
                    views.Add(new VlatBufferView(0, stream.Position, counts[k] * 4L));
                    for (var v = 0; v < binding.VertexCount; v++)
                        if (!IsZero(deltas[k][v])) writer.Write((uint)v);
                    views.Add(new VlatBufferView(0, stream.Position, counts[k] * 12L));
                    for (var v = 0; v < binding.VertexCount; v++)
                    {
                        var d = deltas[k][v];
                        if (IsZero(d)) continue;
                        writer.Write((float)d.X); writer.Write((float)d.Y); writer.Write((float)d.Z);
                    }
                    definitions[k] = new YawKeyform(yaws[k], new VertexDeltaModifier(0,
                        new VlatAccessor(indexView, "UINT32"), new VlatAccessor(indexView + 1, "FLOAT32")));
                }
                binary = stream.ToArray();
            }
            var document = new VlatDocument(
                new VlatMetadata(VlatValidator.KeyformVersion, "VLat Keyform Builder",
                    new TargetAvatar("Keyform Avatar", "1.0", new[] { binding })),
                binary.Length == 0 ? Array.Empty<VlatBuffer>() : new[] { new VlatBuffer(binaryUri, binary.Length) },
                views, null, Array.Empty<AngleBand>(), Array.Empty<VlatLayer>(),
                new[] { Group(topologyHash, supportStart, supportEnd, definitions) });
            var buffers = binary.Length == 0 ? Array.Empty<byte[]>() : new[] { binary };
            return new VlatLoadResult(document, buffers, VlatValidator.Validate(document, buffers));
        }

        private static KeyformGroup Group(string hash, double start, double end, YawKeyform[] keys) =>
            new KeyformGroup("shape-keys", 0, hash, "bind-mesh-local", "yaw-smoothstep", start, end, keys);

        private static bool IsZero(Float3 value) =>
            (float)value.X == 0 && (float)value.Y == 0 && (float)value.Z == 0;

        private static void RequireFloat(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value > float.MaxValue || value < -float.MaxValue)
                throw new ArgumentException("差分は有限なFLOAT32で表せる必要があります。");
        }
    }
}
