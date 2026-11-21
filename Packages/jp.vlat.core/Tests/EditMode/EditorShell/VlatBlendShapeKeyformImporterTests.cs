using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using VLat.Core.Evaluation;
using VLat.Core.Model;
using VLat.Core.Serialization;
using VLat.Editor.Authoring;
using VLat.Runtime;

namespace VLat.Tests.Edit
{
    /// <summary>実UnityのBlendShape取込と旧バックエンドの拒否を検証します。</summary>
    public sealed class VlatBlendShapeKeyformImporterTests
    {
        private Mesh _source;

        /// <summary>権利制約のない合成メッシュを準備します。</summary>
        [SetUp]
        public void SetUp()
        {
            _source = new Mesh { name = "Face" };
            _source.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            _source.triangles = new[] { 0, 1, 2 };
            _source.AddBlendShapeFrame("Left", 100, new[] { Vector3.left, Vector3.zero, Vector3.zero }, null, null);
            _source.AddBlendShapeFrame("Right", 100, new[] { Vector3.zero, Vector3.zero, Vector3.forward }, null, null);
        }

        /// <summary>合成メッシュを破棄します。</summary>
        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(_source);

        /// <summary>取込・保存・再読込・評価でindexと位置差分を保持し、原本を変更しないことを検証します。</summary>
        [Test]
        public void Import_RoundTripsPositionsWithoutChangingSource()
        {
            var positions = _source.vertices;
            var triangles = _source.triangles;
            var hash = PositionHashUtility.Calculate(_source);
            var topology = VlatBlendShapeKeyformImporter.CalculateTopologyHash(_source);
            var result = Import(Binding(), topology);
            Assert.That(result.Success, Is.True);
            var folder = Path.Combine(Path.GetTempPath(), "vlat-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var path = Path.Combine(folder, "import.vlat.json");
                Assert.That(VlatSerializer.Save(path, result.Document, result.Buffers).Success, Is.True);
                var loaded = VlatSerializer.Load(path);
                Assert.That(loaded.Success, Is.True);
                var evaluator = new KeyformReferenceEvaluator(loaded.Document, loaded.Buffers);
                var output = new Float3[3];
                evaluator.Evaluate(-45, output);
                Assert.That(output[0], Is.EqualTo(new Float3(-1, 0, 0)));
                evaluator.Evaluate(45, output);
                Assert.That(output[2], Is.EqualTo(new Float3(0, 0, 1)));
                Assert.That(PositionHashUtility.Calculate(_source), Is.EqualTo(hash));
                Assert.That(VlatBlendShapeKeyformImporter.CalculateTopologyHash(_source), Is.EqualTo(topology));
                CollectionAssert.AreEqual(positions, _source.vertices);
                CollectionAssert.AreEqual(triangles, _source.triangles);
                Assert.That(_source.blendShapeCount, Is.EqualTo(2));
                var delta = new Vector3[3];
                _source.GetBlendShapeFrameVertices(0, 0, delta, null, null);
                Assert.That(delta[0], Is.EqualTo(Vector3.left));
            }
            finally { Directory.Delete(folder, true); }
        }

        /// <summary>頂点数が同じでも、頂点順やTopologyの違う基準Meshを拒否します。</summary>
        [Test]
        public void Import_RejectsDifferentBaseGeometryAndTopology()
        {
            var binding = Binding();
            var topology = VlatBlendShapeKeyformImporter.CalculateTopologyHash(_source);
            _source.triangles = new[] { 0, 2, 1 };
            Assert.Throws<ArgumentException>(() => Import(binding, topology));
            _source.triangles = new[] { 0, 1, 2 };
            _source.vertices = new[] { Vector3.right, Vector3.zero, Vector3.up };
            Assert.Throws<ArgumentException>(() => Import(binding, topology));
        }

        /// <summary>複数フレームや100%以外のフレームを推測変換しないことを検証します。</summary>
        [Test]
        public void Import_RejectsUnsupportedFrames()
        {
            _source.ClearBlendShapes();
            _source.AddBlendShapeFrame("Multi", 50, new Vector3[3], null, null);
            var binding = Binding();
            var topology = VlatBlendShapeKeyformImporter.CalculateTopologyHash(_source);
            Assert.Throws<ArgumentException>(() => VlatBlendShapeKeyformImporter.ImportPositions(
                _source, binding, topology, new[] { 0 }, new[] { 0d }, -90, 90));
            _source.AddBlendShapeFrame("Multi", 100, new Vector3[3], null, null);
            Assert.Throws<ArgumentException>(() => VlatBlendShapeKeyformImporter.ImportPositions(
                _source, binding, topology, new[] { 0 }, new[] { 0d }, -90, 90));
        }

        /// <summary>Editor変換と既存packがキーを失わず往復することを検証します。</summary>
        [Test]
        public void EditorConversion_PreservesKeyformsWithoutLoss()
        {
            var result = Import(Binding(), VlatBlendShapeKeyformImporter.CalculateTopologyHash(_source));
            var converted = VlatAuthoringConversion.FromImmutable(result.Document, result.Buffers);
            try
            {
                Assert.That(converted.Success, Is.True);
                var packed = VlatAuthoringPersistence.Pack(converted.Document);
                Assert.That(packed.Success, Is.True);
                Assert.That(packed.Document.KeyformGroups[0].Keys.Count, Is.EqualTo(3));
                CollectionAssert.AreEqual(result.Buffers[0], packed.Buffers[0]);
                var preview = VlatPreviewController.CreateEvaluationSnapshot(converted.Document);
                Assert.That(preview.Success, Is.True);
                Assert.That(preview.Document.KeyformGroups[0].Keys.Count, Is.EqualTo(3));
                CollectionAssert.AreEqual(packed.Buffers[0], preview.Buffers[0]);
            }
            finally { if (converted.Document != null) UnityEngine.Object.DestroyImmediate(converted.Document); }
        }

        /// <summary>旧CPU Runtimeがキーを無視して成功を返さず、原本Rendererを変更しないことを検証します。</summary>
        [Test]
        public void LegacyRuntime_RejectsKeyformsWithoutChangingRenderer()
        {
            var root = new GameObject("Avatar");
            var cameraObject = new GameObject("Camera");
            try
            {
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = _source;
                renderer.SetBlendShapeWeight(0, 35);
                var result = Import(Binding(), VlatBlendShapeKeyformImporter.CalculateTopologyHash(_source));
                var applier = root.AddComponent<VlatApplier>();
                var initialized = applier.Initialize(root.transform, root.transform,
                    cameraObject.AddComponent<Camera>(), result.Document, result.Buffers);
                Assert.That(initialized.Success, Is.False);
                Assert.That(renderer.sharedMesh, Is.SameAs(_source));
                Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(35));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        private MeshBinding Binding() =>
            new MeshBinding("Face", _source.name, _source.vertexCount, PositionHashUtility.Calculate(_source), null);

        private VlatLoadResult Import(MeshBinding binding, string topology) =>
            VlatBlendShapeKeyformImporter.ImportPositions(_source, binding, topology,
                new[] { 0, -1, 1 }, new[] { -45d, 0, 45 }, -90, 90);
    }
}
