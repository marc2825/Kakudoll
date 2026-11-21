using System;
using NUnit.Framework;
using UnityEngine;
using VLat.Core.Model;
using VLat.Runtime;

namespace VLat.Tests.Edit
{
    /// <summary>UniVRM境界のMeshBinding再解決と頂点ハッシュを検証します。</summary>
    public sealed class UniVrmAdapterTests
    {
        /// <summary>空頂点列のxxHash64既知値と入力差分を検証します。</summary>
        [Test]
        public void PositionHash_UsesCanonicalLittleEndianXxHash64()
        {
            Assert.That(
                PositionHashUtility.Calculate(Array.Empty<Vector3>()),
                Is.EqualTo("xxh64:ef46db3751d8e999"));
            Assert.That(
                PositionHashUtility.Calculate(new[] { Vector3.zero }),
                Is.Not.EqualTo(PositionHashUtility.Calculate(new[] { Vector3.one })));
        }

        /// <summary>完全パス、名前+頂点数、頂点数+hashの優先順で再解決することを検証します。</summary>
        [Test]
        public void Resolve_UsesSpecifiedFallbackOrder()
        {
            var root = new GameObject("Avatar");
            var child = new GameObject("Hair");
            var mesh = CreateTriangleMesh("HairMesh");
            try
            {
                child.transform.SetParent(root.transform, false);
                var filter = child.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                child.AddComponent<MeshRenderer>();

                var exact = UniVrmAdapter.Resolve(
                    root.transform,
                    CreateDocument(new MeshBinding(
                        "Hair",
                        "WrongName",
                        999,
                        "xxh64:0000000000000000",
                        Array.Empty<VlatMaterialSlot>())));
                Assert.That(exact[0].Stage, Is.EqualTo(MeshBindingMatchStage.ExactHierarchyPath));

                var byName = UniVrmAdapter.Resolve(
                    root.transform,
                    CreateDocument(new MeshBinding(
                        "Missing",
                        mesh.name,
                        mesh.vertexCount,
                        "xxh64:0000000000000000",
                        Array.Empty<VlatMaterialSlot>())));
                Assert.That(byName[0].Stage, Is.EqualTo(MeshBindingMatchStage.MeshNameAndVertexCount));

                var byHash = UniVrmAdapter.Resolve(
                    root.transform,
                    CreateDocument(new MeshBinding(
                        "Missing",
                        "Renamed",
                        mesh.vertexCount,
                        PositionHashUtility.Calculate(mesh),
                        Array.Empty<VlatMaterialSlot>())));
                Assert.That(byHash[0].Stage, Is.EqualTo(MeshBindingMatchStage.VertexCountAndPositionHash));
                Assert.That(byHash[0].Renderer, Is.SameAs(child.GetComponent<MeshRenderer>()));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        /// <summary>同じフォールバック条件の候補が複数ある場合に誤った自動割当をしないことを検証します。</summary>
        [Test]
        public void Resolve_AmbiguousFallbackFailsSafely()
        {
            var root = new GameObject("Avatar");
            var mesh = CreateTriangleMesh("SharedMesh");
            try
            {
                CreateMeshObject(root.transform, "First", mesh);
                CreateMeshObject(root.transform, "Second", mesh);
                var report = UniVrmAdapter.Resolve(
                    root.transform,
                    CreateDocument(new MeshBinding(
                        "Missing",
                        mesh.name,
                        mesh.vertexCount,
                        PositionHashUtility.Calculate(mesh),
                        Array.Empty<VlatMaterialSlot>())));

                Assert.That(report.AllSucceeded, Is.False);
                Assert.That(report[0].Stage, Is.EqualTo(MeshBindingMatchStage.Failed));
                Assert.That(report[0].Renderer, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        /// <summary>ルート自身は空の相対パスになり、ルート外のTransformは拒否されることを検証します。</summary>
        [Test]
        public void GetRelativeHierarchyPath_HandlesRootAndRejectsUnrelatedTarget()
        {
            var root = new GameObject("Root");
            var unrelated = new GameObject("Unrelated");
            try
            {
                Assert.That(UniVrmAdapter.GetRelativeHierarchyPath(root.transform, root.transform), Is.Empty);
                Assert.Throws<ArgumentException>(
                    () => UniVrmAdapter.GetRelativeHierarchyPath(root.transform, unrelated.transform));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(unrelated);
            }
        }

        private static Mesh CreateTriangleMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = new[]
            {
                Vector3.zero,
                Vector3.right,
                Vector3.up,
            };
            mesh.triangles = new[] { 0, 1, 2 };
            return mesh;
        }

        private static void CreateMeshObject(Transform root, string name, Mesh mesh)
        {
            var child = new GameObject(name);
            child.transform.SetParent(root, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            child.AddComponent<MeshRenderer>();
        }

        private static VlatDocument CreateDocument(MeshBinding binding)
        {
            return new VlatDocument(
                new VlatMetadata(
                    "0.1.0",
                    "Tests",
                    new TargetAvatar("Avatar", "1.0", new[] { binding })),
                Array.Empty<VlatBuffer>(),
                Array.Empty<VlatBufferView>(),
                Array.Empty<VlatTexture>(),
                Array.Empty<AngleBand>(),
                Array.Empty<VlatLayer>());
        }
    }
}
