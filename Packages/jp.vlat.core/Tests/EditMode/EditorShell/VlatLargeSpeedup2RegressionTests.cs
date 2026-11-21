using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VLat.Editor.Authoring;
using Object = UnityEngine.Object;

namespace VLat.Tests.Edit
{
    /// <summary>2026-11-20第2弾の高速化（ドラッグ開始の逆行列、pack planの非複製）が判定と結果を変えないことを検証します。</summary>
    public sealed class VlatLargeSpeedup2RegressionTests
    {
        private static Matrix4x4 Block(float a00, float a01, float a02, float a10, float a11, float a12, float a20, float a21, float a22)
        {
            var m = Matrix4x4.zero;
            m.m00 = a00; m.m01 = a01; m.m02 = a02;
            m.m10 = a10; m.m11 = a11; m.m12 = a12;
            m.m20 = a20; m.m21 = a21; m.m22 = a22;
            m.m33 = 1;
            return m;
        }

        private static string Outcome(Func<Matrix4x4> invert, out Matrix4x4 result)
        {
            result = default;
            try { result = invert(); return "ok"; }
            catch (ArgumentException exception) { return exception.Message; }
        }

        /// <summary>[A 0; 0 1] 専用の逆行列は、従来の4x4逆行列と受理/拒否・messageが一致し、値も一致します。</summary>
        [Test]
        public void BlockInverse_MatchesGeneralInverse()
        {
            var cases = new List<Matrix4x4>
            {
                Block(1, 0, 0, 0, 1, 0, 0, 0, 1),
                Block(0, 0, 0, 0, 1, 0, 0, 0, 1),                    // 特異
                Block(1e-7f, 0, 0, 0, 1, 0, 0, 0, 1),                // 特異に近い（条件数で拒否）
                Block(1e3f, 0, 0, 0, 1e-4f, 0, 0, 0, 1),             // 条件数 1e7
                Block(2000, 0, 0, 0, 1, 0, 0, 0, 1),                 // 大きいscaleでも条件数は許容内
                Block(float.NaN, 0, 0, 0, 1, 0, 0, 0, 1),            // 非有限
                Block(0.5f, 0, 0, 0, 1, 0, 0, 0, 0.5f),              // 混合Skinning相当（det 0.25）
            };
            var random = new System.Random(20261120);
            for (var n = 0; n < 200; n++)
            {
                float R() => (float)(random.NextDouble() * 2 - 1);
                cases.Add(Block(1 + R() * .5f, R() * .5f, R() * .5f, R() * .5f, 1 + R() * .5f, R() * .5f, R() * .5f, R() * .5f, 1 + R() * .5f));
            }
            foreach (var matrix in cases)
            {
                var expected = Outcome(() => VlatKeyformDragGesture.RequireInverse(matrix), out var general);
                var actual = Outcome(() => VlatKeyformDragGesture.RequireBlockInverse(matrix), out var block);
                Assert.That(actual, Is.EqualTo(expected), matrix.ToString("G9"));
                if (expected != "ok") continue;
                for (var i = 0; i < 16; i++)
                    Assert.That(block[i], Is.EqualTo(general[i]).Within(1e-5f * Mathf.Max(1f, Mathf.Abs(general[i]))), "entry " + i);
                Assert.That(block.m30, Is.Zero); Assert.That(block.m03, Is.Zero); Assert.That(block.m33, Is.EqualTo(1f));
            }
        }

        /// <summary>planでdeltaを複製しなくても、pack結果は呼出後のAuthoring変更から独立しています。</summary>
        [Test]
        public void Pack_ResultIsIndependentOfLaterAuthoringChanges()
        {
            var document = VlatAuthoringPersistence.CreateNew("Plan Alias Avatar");
            try
            {
                document.Metadata.MeshBindings.Add(new VlatAuthoringMeshBinding
                {
                    HierarchyPath = "Face", MeshName = "Grid", VertexCount = 10, PositionHash = "xxh64:0000000000000000",
                });
                var group = new VlatAuthoringKeyformGroup { MeshBinding = 0, TopologyHash = "sha256:" + new string('0', 64) };
                var key = new VlatAuthoringKeyform { Yaw = 0 };
                key.Deltas.Add(new VlatAuthoringVertexDelta { VertexIndex = 1, PositionDelta = new Vector3(0.25f, 0, 0) });
                key.Deltas.Add(new VlatAuthoringVertexDelta { VertexIndex = 4, PositionDelta = new Vector3(0, -0.5f, 0) });
                group.Keys.Add(key);
                VlatAuthoringCommands.AddKeyformGroup(document, group);
                var pack = VlatAuthoringPersistence.Pack(document);
                Assert.That(pack.Success, Is.True);
                var json = pack.Json; var bytes = (byte[])pack.Buffers[0].Clone();
                key.Deltas[0].PositionDelta = new Vector3(9, 9, 9);
                key.Deltas[1].VertexIndex = 7;
                Assert.That(pack.Json, Is.EqualTo(json));
                CollectionAssert.AreEqual(bytes, pack.Buffers[0]);
                Assert.That(VlatAuthoringPersistence.Pack(document).Buffers[0], Is.Not.EqualTo(bytes), "変更後の再packは新しい内容");
            }
            finally
            {
                Undo.ClearAll();
                Object.DestroyImmediate(document);
            }
        }
    }
}
