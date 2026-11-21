using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using VLat.Core.Evaluation;
using VLat.Core.Model;
using VLat.Core.Serialization;
using VLat.Core.Validation;

namespace VLat.Tests.Edit
{
    /// <summary>v0.2の厳密キー補間、v0.1互換性、保存、破損拒否を検証します。</summary>
    public sealed class KeyformTests
    {
        private const string Hash = "sha256:0000000000000000000000000000000000000000000000000000000000000000";

        /// <summary>キー方向をexactに再現し、使用済み出力バッファを毎回上書きすることを検証します。</summary>
        [TestCase(-45, 1, 0, 0)]
        [TestCase(0, 0, 1, 0)]
        [TestCase(60, 0, 0, 1)]
        [TestCase(315, 1, 0, 0)]
        [TestCase(-300, 0, 0, 1)]
        public void Weights_AreCardinalAtKeys(double yaw, double a, double b, double c)
        {
            var evaluator = new YawKeyformEvaluator(Group(-90, 120, -45, 0, 60));
            var weights = new[] { 9d, 8d, 7d };
            Assert.That(evaluator.Evaluate(yaw, weights), Is.Zero);
            CollectionAssert.AreEqual(new[] { a, b, c }, weights);
        }

        /// <summary>不等間隔のキー間とsupport内外で、既知の独立期待値に一致することを検証します。</summary>
        [TestCase(-67.5, 0.5, 0, 0, 0.5)]
        [TestCase(-22.5, 0.5, 0.5, 0, 0)]
        [TestCase(15, 0, 0.84375, 0.15625, 0)]
        [TestCase(90, 0, 0, 0.5, 0.5)]
        [TestCase(120, 0, 0, 0, 1)]
        [TestCase(-90, 0, 0, 0, 1)]
        [TestCase(180, 0, 0, 0, 1)]
        public void Weights_MatchKnownValues(double yaw, double a, double b, double c, double baseline)
        {
            var weights = new double[3];
            Assert.That(new YawKeyformEvaluator(Group(-90, 120, -45, 0, 60)).Evaluate(yaw, weights), Is.EqualTo(baseline));
            CollectionAssert.AreEqual(new[] { a, b, c }, weights);
        }

        /// <summary>±180度を跨ぐ支持区間で同じ方向が同じ形状になり、境界に段差がないことを検証します。</summary>
        [Test]
        public void WrapSupport_IsContinuousAndEquivalent()
        {
            var evaluator = new YawKeyformEvaluator(Group(120, 240, 150, 180, 210));
            var a = new double[3];
            var b = new double[3];
            evaluator.Evaluate(-180, a);
            CollectionAssert.AreEqual(new[] { 0d, 1d, 0d }, a);
            foreach (var yaw in new[] { -180d, -150, 120, 150, 210, 240 })
            {
                evaluator.Evaluate(yaw - 1e-6, a);
                evaluator.Evaluate(yaw + 1e-6, b);
                for (var i = 0; i < 3; i++) Assert.That(Math.Abs(a[i] - b[i]), Is.LessThan(1e-10));
            }
            for (var yaw = -720; yaw <= 720; yaw++)
            {
                var baseline = evaluator.Evaluate(yaw, a);
                evaluator.Evaluate(yaw + 360, b);
                CollectionAssert.AreEqual(a, b);
                Assert.That(baseline + a.Sum(), Is.EqualTo(1).Within(1e-14));
                Assert.That(a.All(w => w >= 0 && w <= 1), Is.True);
                Assert.That(a.Count(w => w != 0), Is.LessThanOrEqualTo(2));
            }
        }

        /// <summary>単一キーでも支持区間両端で中立へ戻ることを検証します。</summary>
        [Test]
        public void SingleKey_FadesOnBothSides()
        {
            var evaluator = new YawKeyformEvaluator(Group(-60, 60, 0));
            var weights = new double[1];
            Assert.That(evaluator.Evaluate(-30, weights), Is.EqualTo(0.5));
            Assert.That(evaluator.Evaluate(0, weights), Is.Zero);
            Assert.That(weights[0], Is.EqualTo(1));
            Assert.That(evaluator.Evaluate(30, weights), Is.EqualTo(0.5));
            Assert.That(evaluator.Evaluate(double.MaxValue, weights), Is.InRange(0, 1));
        }

        /// <summary>不正な支持区間、順序、重複、非有限Yawを拒否することを検証します。</summary>
        [Test]
        public void InvalidDefinitions_AreRejected()
        {
            foreach (var group in new[]
            {
                Group(-90, 90), Group(-90, 90, 0, 0), Group(-90, 90, 10, 0),
                Group(-90, 90, -90), Group(-90, 90, 90), Group(-90, 90, double.NaN),
                Group(0, 360, 90), Group(180, 270, 200), Group(-181, 0, -90),
                Group(0, 0, 0), Group(double.NaN, 90, 0), Group(-90, double.PositiveInfinity, 0),
                Group(-90, 90, Enumerable.Range(0, 17).Select(i => (double)i).ToArray()),
            }) Assert.Throws<ArgumentException>(() => new YawKeyformEvaluator(group));
            Assert.Throws<ArgumentException>(() => new YawKeyformEvaluator(null));
            var evaluator = new YawKeyformEvaluator(Group(-90, 90, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Evaluate(double.NaN, new double[1]));
            Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Evaluate(double.PositiveInfinity, new double[1]));
            Assert.Throws<ArgumentException>(() => evaluator.Evaluate(0, new double[2]));
        }

        /// <summary>非対称差分、異なるスパースindex、中立キーの保存往復と入力非破壊を検証します。</summary>
        [Test]
        public void BuildSaveLoadEvaluate_RoundTripsDeterministically()
        {
            var built = Build();
            Assert.That(built.Success, Is.True, Diagnostics(built));
            var firstJson = VlatSerializer.Serialize(built.Document);
            var binary = (byte[])built.Buffers[0].Clone();
            var folder = Path.Combine(Path.GetTempPath(), "vlat-keyforms-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var path = Path.Combine(folder, "test.vlat.json");
                Assert.That(VlatSerializer.Save(path, built.Document, built.Buffers).Success, Is.True);
                var loaded = VlatSerializer.Load(path);
                Assert.That(loaded.Success, Is.True, Diagnostics(loaded));
                Assert.That(VlatSerializer.Serialize(loaded.Document), Is.EqualTo(firstJson));
                CollectionAssert.AreEqual(binary, loaded.Buffers[0]);
                Assert.That(VlatSerializer.Save(path, loaded.Document, loaded.Buffers).Success, Is.True);
                CollectionAssert.AreEqual(binary, File.ReadAllBytes(Path.Combine(folder, "keyforms.vlat.bin")));
                var evaluator = new KeyformReferenceEvaluator(loaded.Document, loaded.Buffers);
                var output = new Float3[3];
                evaluator.Evaluate(-45, output);
                CollectionAssert.AreEqual(new[] { new Float3(1, 0, 0), default(Float3), default(Float3) }, output);
                evaluator.Evaluate(0, output);
                Assert.That(output.All(p => p.Equals(default(Float3))), Is.True);
                evaluator.Evaluate(60, output);
                Assert.That(output[2], Is.EqualTo(new Float3(0, 2, -4)));
                evaluator.Evaluate(30, output);
                Assert.That(output[2], Is.EqualTo(new Float3(0, 1, -2)));
                evaluator.Evaluate(180, output);
                Assert.That(output.All(p => p.Equals(default(Float3))), Is.True);
                Array.Clear(loaded.Buffers[0], 0, loaded.Buffers[0].Length);
                evaluator.Evaluate(-45, output);
                Assert.That(output[0].X, Is.EqualTo(1), "評価器は外部のバイナリ書換から独立する");
                CollectionAssert.AreEqual(binary, built.Buffers[0]);
            }
            finally { Directory.Delete(folder, true); }
        }

        /// <summary>中立のみの文書はゼロ長bufferViewを生成せず保存可能であることを検証します。</summary>
        [Test]
        public void NeutralDocument_NeedsNoBinary()
        {
            var result = KeyformDocumentBuilder.Create(Binding(), Hash, new[] { 0d },
                new[] { new Float3[3] }, -90, 90);
            Assert.That(result.Success, Is.True, Diagnostics(result));
            Assert.That(result.Buffers.Count, Is.Zero);
            Assert.That(result.Document.BufferViews.Count, Is.Zero);
            var reloaded = VlatSerializer.Deserialize(VlatSerializer.Serialize(result.Document));
            Assert.That(reloaded.Success, Is.True, Diagnostics(reloaded));
            new KeyformReferenceEvaluator(reloaded.Document, null).Evaluate(0, new Float3[3]);
        }

        /// <summary>旧v0.1文書の出力を維持し、新フィールドの旧版混入を拒否します。</summary>
        [Test]
        public void LegacyFormat_IsPreservedAndExtensionRequires02()
        {
            var legacy = VlatSerializer.Serialize(new VlatDocument(
                new VlatMetadata("0.1.0", "Test", new TargetAvatar("Avatar", "1.0", null)),
                null, null, null, Array.Empty<AngleBand>(), Array.Empty<VlatLayer>()));
            var loaded = VlatSerializer.Deserialize(legacy);
            Assert.That(loaded.Success, Is.True);
            Assert.That(VlatSerializer.Serialize(loaded.Document), Does.Not.Contain("keyformGroups"));
            var root = JObject.Parse(legacy);
            root["keyformGroups"] = new JArray();
            Assert.That(VlatSerializer.Deserialize(root.ToString()).Success, Is.False);
            root = JObject.Parse(VlatSerializer.Serialize(Build().Document));
            root["vlat"]["version"] = "0.1.0";
            Assert.That(VlatSerializer.Deserialize(root.ToString()).Success, Is.False);
        }

        /// <summary>未知の機能・欠損値・別Mesh・重複角・不正型の入力を拒否します。</summary>
        [TestCase("unknown", 1)]
        [TestCase("interpolation", "rbf")]
        [TestCase("coordinateSpace", "world")]
        [TestCase("topologyHash", "sha256:bad")]
        [TestCase("meshBinding", 1)]
        [TestCase("supportStart", 180)]
        [TestCase("supportEnd", 300)]
        public void InvalidGroupJson_IsRejected(string field, object value)
        {
            var root = JObject.Parse(VlatSerializer.Serialize(Build().Document));
            root["keyformGroups"][0][field] = JToken.FromObject(value);
            Assert.That(VlatSerializer.Deserialize(root.ToString()).Success, Is.False);
        }

        /// <summary>必須nullキー・未知の型・ケース違いを黙って既定値に変換しないことを検証します。</summary>
        [Test]
        public void StrictKeyJson_RejectsMissingAndMistypedFields()
        {
            var source = VlatSerializer.Serialize(Build().Document);
            foreach (var mutation in new Action<JObject>[]
            {
                r => ((JObject)r["keyformGroups"][0]["keys"][1]).Remove("delta"),
                r => r["keyformGroups"][0]["keys"][1]["Yaw"] = 0,
                r => r["keyformGroups"][0]["keys"][1]["yaw"] = "-45",
                r => r["keyformGroups"][0]["keys"][1]["yaw"] = -45,
                r => r["keyformGroups"][0]["keys"][0]["delta"]["meshBinding"] = 1,
                r => r["keyformGroups"][0]["keys"][0]["delta"]["type"] = "opacity",
                r => r["keyformGroups"][0]["keys"][0]["delta"]["Indices"] = new JObject(),
                r => r["keyformGroups"] = JValue.CreateNull(),
                r => r["keyformGroups"] = new JArray(),
                r => r["vlat"]["version"] = new JObject(),
                r => r["keyformGroups"][0]["keys"][0] = JValue.CreateNull(),
                r => r["keyformGroups"][0]["keys"][0]["delta"]["normals"] = r["keyformGroups"][0]["keys"][0]["delta"]["positions"].DeepClone(),
            })
            {
                var root = JObject.Parse(source);
                mutation(root);
                Assert.That(VlatSerializer.Deserialize(root.ToString()).Success, Is.False, root.ToString());
            }
        }

        /// <summary>破損bufferの非有限値、範囲外index、短いbuffer、巨大宣言を拒否します。</summary>
        [Test]
        public void CorruptPayload_IsRejected()
        {
            var result = Build();
            foreach (var offset in new[] { 0, 4 })
            {
                var bad = (byte[])result.Buffers[0].Clone();
                var bits = offset == 0 ? int.MaxValue : 0x7f800000;
                for (var b = 0; b < 4; b++) bad[offset + b] = (byte)(bits >> (8 * b));
                Assert.Throws<ArgumentException>(() => new KeyformReferenceEvaluator(result.Document, new[] { bad }));
            }
            Assert.Throws<ArgumentException>(() => new KeyformReferenceEvaluator(result.Document, new[] { new byte[1] }));
            var root = JObject.Parse(VlatSerializer.Serialize(result.Document));
            root["buffers"][0]["byteLength"] = VlatValidator.MaximumKeyformBufferBytes + 1;
            Assert.That(VlatSerializer.Deserialize(root.ToString()).Success, Is.False);
        }

        /// <summary>Builderが異なる頂点数と非有限値を拒否し、入力配列を変更しないことを検証します。</summary>
        [Test]
        public void Builder_RejectsInvalidSamplesAndDoesNotMutate()
        {
            foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.MaxValue })
                Assert.Throws<ArgumentException>(() => KeyformDocumentBuilder.Create(Binding(), Hash,
                    new[] { 0d }, new[] { new[] { new Float3(invalid, 0, 0), default, default } }, -90, 90));
            Assert.Throws<ArgumentException>(() => KeyformDocumentBuilder.Create(Binding(), Hash,
                new[] { 0d }, new[] { new Float3[2] }, -90, 90));
            var sample = new[] { new Float3(1, 2, 3), default, default };
            var result = KeyformDocumentBuilder.Create(Binding(), Hash, new[] { 0d }, new[] { sample }, -90, 90);
            Assert.That(sample[0], Is.EqualTo(new Float3(1, 2, 3)));
            sample[0] = default;
            var output = new Float3[3];
            new KeyformReferenceEvaluator(result.Document, result.Buffers).Evaluate(0, output);
            Assert.That(output[0], Is.EqualTo(new Float3(1, 2, 3)));
        }

        /// <summary>ホットパスの反復評価に管理ヒープ割り当てがないことを検証します。</summary>
        [Test]
        public void Evaluation_AllocatesZeroBytesAfterWarmup()
        {
            var result = Build();
            var evaluator = new KeyformReferenceEvaluator(result.Document, result.Buffers);
            var output = new Float3[3];
            for (var i = 0; i < 10000; i++) evaluator.Evaluate(i % 360, output);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100000; i++) evaluator.Evaluate(i % 360, output);
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
        }

        /// <summary>JSON Schemaと共有する固定fixtureをCoreで読み、期待するbinaryを保持することを検証します。</summary>
        [Test]
        public void SharedSchemaFixture_IsAcceptedByCore()
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            const string relative = "Packages/jp.vlat.core/Tests/EditMode/Fixtures/Keyform/three-keys.vlat.json";
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, relative))) directory = directory.Parent;
            Assert.That(directory, Is.Not.Null, "リポジトリ内またはUnity開発プロジェクトから実行してください。");
            var loaded = VlatSerializer.Load(Path.Combine(directory.FullName, relative));
            Assert.That(loaded.Success, Is.True, Diagnostics(loaded));
            CollectionAssert.AreEqual(Build().Buffers[0], loaded.Buffers[0]);
            var output = new Float3[3];
            new KeyformReferenceEvaluator(loaded.Document, loaded.Buffers).Evaluate(60, output);
            Assert.That(output[2], Is.EqualTo(new Float3(0, 2, -4)));
        }

        private static KeyformGroup Group(double start, double end, params double[] yaws) =>
            new KeyformGroup("test", 0, Hash, "bind-mesh-local", "yaw-smoothstep", start, end,
                yaws.Select(yaw => new YawKeyform(yaw, null)));

        private static MeshBinding Binding() => new MeshBinding("Face", "Face", 3, "xxh64:0000000000000000", null);

        private static VlatLoadResult Build() => KeyformDocumentBuilder.Create(Binding(), Hash,
            new[] { -45d, 0, 60 }, new[]
            {
                new[] { new Float3(1, 0, 0), default, default },
                new Float3[3],
                new[] { default, default, new Float3(0, 2, -4) },
            }, -90, 120);

        private static string Diagnostics(VlatLoadResult result) => string.Join("\n", result.Diagnostics.Select(d => d.Message));
    }
}
