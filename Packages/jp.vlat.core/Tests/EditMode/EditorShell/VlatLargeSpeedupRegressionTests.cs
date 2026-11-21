using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VLat.Core.Validation;
using VLat.Editor.Authoring;
using VLat.Runtime;
using Object = UnityEngine.Object;

namespace VLat.Tests.Edit
{
    /// <summary>2026-11-20の大規模高速化（検証・並べ替え・Topology指紋・Binding照合）が結果を変えないことを検証します。</summary>
    public sealed class VlatLargeSpeedupRegressionTests
    {
        // 変更前の実装（BinaryWriterで1要素ずつ書き、streamをSHA-256）をそのまま参照実装にする。
        private static string ReferenceTopologyHash(Mesh source)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            using (var sha = SHA256.Create())
            {
                writer.Write(source.vertexCount);
                writer.Write(source.subMeshCount);
                for (var s = 0; s < source.subMeshCount; s++)
                {
                    var indices = source.GetIndices(s, true);
                    writer.Write((int)source.GetTopology(s));
                    writer.Write(indices.Length);
                    for (var i = 0; i < indices.Length; i++) writer.Write(indices[i]);
                }
                writer.Flush();
                stream.Position = 0;
                var hash = sha.ComputeHash(stream);
                var result = new StringBuilder("sha256:");
                for (var i = 0; i < hash.Length; i++) result.Append(hash[i].ToString("x2"));
                return result.ToString();
            }
        }

        private static Mesh GridMesh(int width, int height, bool uint32, bool emptySecondSubMesh)
        {
            var mesh = new Mesh { name = "SpeedupGrid", indexFormat = uint32 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            var vertices = new Vector3[width * height];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++) vertices[y * width + x] = new Vector3(x * 0.01f, y * 0.01f, (x ^ y) * 0.001f);
            mesh.vertices = vertices;
            var first = new List<int>(); var second = new List<int>();
            for (var y = 0; y < height - 1; y++)
                for (var x = 0; x < width - 1; x++)
                {
                    var i = y * width + x;
                    var target = !emptySecondSubMesh && y >= height / 2 ? second : first;
                    target.AddRange(new[] { i, i + width, i + 1, i + 1, i + width, i + width + 1 });
                }
            mesh.subMeshCount = 2;
            mesh.SetTriangles(first, 0);
            mesh.SetTriangles(second, 1);
            return mesh;
        }

        /// <summary>Topology指紋は変更前の実装とbit同一（UInt16/UInt32、複数subMesh、空subMesh）。</summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void TopologyHash_MatchesPreviousImplementation(bool uint32, bool emptySecond)
        {
            var mesh = GridMesh(uint32 ? 300 : 40, 30, uint32, emptySecond);
            try
            {
                Assert.That(VlatBlendShapeKeyformImporter.CalculateTopologyHash(mesh), Is.EqualTo(ReferenceTopologyHash(mesh)));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        /// <summary>Binding採取の結果は各指紋の直接計算と一致し、probeの有限値検査は省略されません。</summary>
        [Test]
        public void Capture_KeepsHashesAndStillRejectsNonFiniteMesh()
        {
            var root = new GameObject("SpeedupRoot");
            var mesh = GridMesh(20, 10, false, false);
            try
            {
                var face = new GameObject("Face"); face.transform.SetParent(root.transform, false);
                var renderer = face.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = mesh;
                var binding = VlatKeyformBindingUtility.Capture(root, renderer, out var topology);
                Assert.That(topology, Is.EqualTo(ReferenceTopologyHash(mesh)));
                Assert.That(binding.PositionHash, Is.EqualTo(PositionHashUtility.Calculate(mesh)));
                Assert.That(binding.VertexCount, Is.EqualTo(mesh.vertexCount));
                Assert.DoesNotThrow(() => VlatKeyformBindingUtility.RequireMatch(root, renderer, binding, topology));
                Assert.Throws<ArgumentException>(() => VlatKeyformBindingUtility.RequireMatch(root, renderer, binding,
                    "sha256:" + new string('0', 64)));

                var vertices = mesh.vertices; vertices[3] = new Vector3(float.NaN, 0, 0); mesh.vertices = vertices;
                Assert.Throws<ArgumentException>(() => VlatKeyformBindingUtility.Capture(root, renderer, out _),
                    "probeの有限値検査は維持する");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
            }
        }

        private static VlatAuthoringDocument KeyformDocument(int meshBinding, IEnumerable<VlatAuthoringVertexDelta> deltas)
        {
            var document = VlatAuthoringPersistence.CreateNew("Speedup Avatar");
            document.Metadata.MeshBindings.Add(new VlatAuthoringMeshBinding
            {
                HierarchyPath = "Face", MeshName = "SpeedupGrid", VertexCount = 10, PositionHash = "xxh64:0000000000000000",
            });
            var group = new VlatAuthoringKeyformGroup { MeshBinding = meshBinding, TopologyHash = "sha256:" + new string('0', 64) };
            var key = new VlatAuthoringKeyform { Yaw = 0 };
            key.Deltas.AddRange(deltas);
            group.Keys.Add(key);
            VlatAuthoringCommands.AddKeyformGroup(document, group);
            return document;
        }

        private static VlatAuthoringVertexDelta Delta(int index, float x = 0.25f) =>
            new VlatAuthoringVertexDelta { VertexIndex = index, PositionDelta = new Vector3(x, 0, 0) };

        /// <summary>頂点数既知（配列判定）と未知（HashSet判定）の両方で、診断のpath・message・順序は従来どおりです。</summary>
        [Test]
        public void DeltaValidation_DiagnosticsUnchanged()
        {
            var deltas = new[] { Delta(5), Delta(5), Delta(12), Delta(2, float.NaN), null, Delta(-1) };
            const string prefix = "$.keyformGroups[0].keys[0].delta.deltas";
            var known = KeyformDocument(0, deltas);
            var unknown = KeyformDocument(-1, deltas);
            try
            {
                var knownDiagnostics = VlatAuthoringPersistence.Pack(known).Diagnostics
                    .Where(d => d.Path.StartsWith(prefix, StringComparison.Ordinal)).Select(d => d.Path + " | " + d.Message).ToList();
                CollectionAssert.AreEqual(new[]
                {
                    prefix + "[1].vertexIndex | 同じvertexIndexを重複して保持できません。",
                    prefix + "[2].vertexIndex | vertexIndexがmeshBindingの頂点範囲外です。",
                    prefix + "[3].positionDelta | positionDeltaは有限値である必要があります。",
                    prefix + "[4] | vertexDelta要素がnullです。",
                    prefix + "[5].vertexIndex | vertexIndexがmeshBindingの頂点範囲外です。",
                }, knownDiagnostics);

                var unknownDiagnostics = VlatAuthoringPersistence.Pack(unknown).Diagnostics
                    .Where(d => d.Path.StartsWith(prefix, StringComparison.Ordinal)).Select(d => d.Path + " | " + d.Message).ToList();
                CollectionAssert.AreEqual(new[]
                {
                    prefix + "[1].vertexIndex | 同じvertexIndexを重複して保持できません。",
                    prefix + "[3].positionDelta | positionDeltaは有限値である必要があります。",
                    prefix + "[4] | vertexDelta要素がnullです。",
                    prefix + "[5].vertexIndex | vertexIndexがmeshBindingの頂点範囲外です。",
                }, unknownDiagnostics, "頂点数未知では上限側の範囲外を判定しない（従来どおり）");
            }
            finally
            {
                Undo.ClearAll();
                Object.DestroyImmediate(known);
                Object.DestroyImmediate(unknown);
            }
        }

        /// <summary>並べ替えの省略は昇順入力だけで行い、未整列の入力も従来どおり昇順で保存されます。</summary>
        [Test]
        public void Pack_SortsUnsortedDeltasAndKeepsSortedOnes()
        {
            var unsorted = KeyformDocument(0, new[] { Delta(7), Delta(2), Delta(5, -0.5f) });
            var sorted = KeyformDocument(0, new[] { Delta(2), Delta(5, -0.5f), Delta(7) });
            try
            {
                var a = VlatAuthoringPersistence.Pack(unsorted); var b = VlatAuthoringPersistence.Pack(sorted);
                Assert.That(a.Success, Is.True, string.Join("\n", a.Diagnostics.Select(d => d.Path + " " + d.Message)));
                Assert.That(b.Success, Is.True);
                Assert.That(a.Json, Is.EqualTo(b.Json));
                Assert.That(a.Buffers.Count, Is.EqualTo(1));
                CollectionAssert.AreEqual(b.Buffers[0], a.Buffers[0], "同じ内容は同じcanonical binaryになる");
                var loaded = VlatAuthoringConversion.FromImmutable(a.Document, a.Buffers, Path.GetFullPath("speedup.vlat.json"));
                try
                {
                    Assert.That(loaded.Success, Is.True);
                    CollectionAssert.AreEqual(new[] { 2, 5, 7 },
                        loaded.Document.KeyformGroups[0].Keys[0].Deltas.Select(d => d.VertexIndex).ToArray());
                }
                finally { if (loaded.Document != null) Object.DestroyImmediate(loaded.Document); }
            }
            finally
            {
                Undo.ClearAll();
                Object.DestroyImmediate(unsorted);
                Object.DestroyImmediate(sorted);
            }
        }
    }
}
