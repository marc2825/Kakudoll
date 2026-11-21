using NUnit.Framework;
using VLat.Core.Evaluation;
using VLat.Core.Model;

namespace VLat.Tests.Edit
{
    /// <summary>AngleEvaluatorの境界値を検証します。</summary>
    public sealed class AngleEvaluatorTests
    {
        /// <summary>頭部ローカル方向から仕様どおりのyawとpitchが得られることを検証します。</summary>
        [Test]
        public void TryCalculateAngles_UsesHeadLocalAxes()
        {
            Assert.That(AngleEvaluator.TryCalculateAngles(new Float3(0d, 0d, 1d), out var front), Is.True);
            Assert.That(front.Yaw, Is.EqualTo(0d).Within(1e-9d));
            Assert.That(front.Pitch, Is.EqualTo(0d).Within(1e-9d));

            Assert.That(AngleEvaluator.TryCalculateAngles(new Float3(1d, 0d, 0d), out var side), Is.True);
            Assert.That(side.Yaw, Is.EqualTo(90d).Within(1e-9d));

            Assert.That(AngleEvaluator.TryCalculateAngles(new Float3(0d, 1d, 1d), out var above), Is.True);
            Assert.That(above.Pitch, Is.EqualTo(45d).Within(1e-9d));

            Assert.That(AngleEvaluator.TryCalculateAngles(new Float3(0d, 0d, -1d), out var rear), Is.True);
            Assert.That(rear.Yaw, Is.EqualTo(180d).Within(1e-9d));
        }

        /// <summary>零長方向の評価が安全に失敗し、出力重みを0へ戻すことを検証します。</summary>
        [Test]
        public void Evaluate_ZeroDirectionClearsDestination()
        {
            var evaluator = new AngleEvaluator(new[] { CreateBand(-10d, 10d, -10d, 10d, 5d, 5d) });
            var weights = new[] { 1d };

            Assert.That(evaluator.Evaluate(new Float3(0d, 0d, 0d), weights), Is.False);
            Assert.That(weights[0], Is.Zero);
        }

        /// <summary>minがmaxより大きいyaw範囲が±180度跨ぎとして評価されることを検証します。</summary>
        [Test]
        public void EvaluateBand_SupportsYawWrapAround()
        {
            var band = CreateBand(170d, -170d, -20d, 20d, 10d, 0d);

            Assert.That(AngleEvaluator.EvaluateBand(band, 180d, 0d), Is.EqualTo(1d));
            Assert.That(AngleEvaluator.EvaluateBand(band, -175d, 0d), Is.EqualTo(1d));
            Assert.That(AngleEvaluator.EvaluateBand(band, 165d, 0d), Is.EqualTo(0.5d).Within(1e-9d));
            Assert.That(AngleEvaluator.EvaluateBand(band, 0d, 0d), Is.Zero);
        }

        /// <summary>yaw全域を表す-180〜180度が零幅として扱われないことを検証します。</summary>
        [Test]
        public void EvaluateBand_TreatsMinus180To180AsFullYawRange()
        {
            var band = CreateBand(-180d, 180d, -10d, 10d, 0d, 0d);

            Assert.That(AngleEvaluator.EvaluateBand(band, -90d, 0d), Is.EqualTo(1d));
            Assert.That(AngleEvaluator.EvaluateBand(band, 90d, 0d), Is.EqualTo(1d));
        }

        /// <summary>featherが0のときに境界外の重みが直ち0になることを検証します。</summary>
        [Test]
        public void EvaluateBand_ZeroFeatherIsHardBoundary()
        {
            var band = CreateBand(-10d, 10d, -10d, 10d, 0d, 0d);

            Assert.That(AngleEvaluator.EvaluateBand(band, 10d, 0d), Is.EqualTo(1d));
            Assert.That(AngleEvaluator.EvaluateBand(band, 10.000001d, 0d), Is.Zero);
        }

        /// <summary>yawとpitchのフェザー重みが積で合成されることを検証します。</summary>
        [Test]
        public void EvaluateBand_MultipliesYawAndPitchWeights()
        {
            var band = CreateBand(-10d, 10d, -10d, 10d, 20d, 20d);

            Assert.That(AngleEvaluator.EvaluateBand(band, 20d, 20d), Is.EqualTo(0.25d).Within(1e-9d));
        }

        /// <summary>visibilityの退場境界だけがヒステリシス幅で拡張されることを検証します。</summary>
        [Test]
        public void VisibilityHysteresis_UsesSeparateEnterAndExitThresholds()
        {
            var band = new AngleBand(
                "band",
                "Band",
                new AngleRange(-10d, 10d),
                new AngleRange(-10d, 10d),
                new AngleFeather(20d, 20d),
                5d);
            var hysteresis = new VisibilityHysteresis();

            var entryWeight = AngleEvaluator.EvaluateBand(band, 20d, 0d);
            Assert.That(entryWeight, Is.EqualTo(0.5d).Within(1e-9d));
            Assert.That(hysteresis.Evaluate(band, 20d, 0d, entryWeight), Is.True);

            var heldWeight = AngleEvaluator.EvaluateBand(band, 24d, 0d);
            Assert.That(heldWeight, Is.LessThan(0.5d));
            Assert.That(hysteresis.Evaluate(band, 24d, 0d, heldWeight), Is.True);
            Assert.That(hysteresis.Evaluate(band, 26d, 0d, 0d), Is.False);

            Assert.That(hysteresis.Evaluate(band, 24d, 0d, heldWeight), Is.False);
        }

        /// <summary>pitch軸が拡張された退場境界を超えたときにOFFになることを検証します。</summary>
        [Test]
        public void VisibilityHysteresis_ExitsOnPitchBoundary()
        {
            var band = new AngleBand(
                "band",
                "Band",
                new AngleRange(-10d, 10d),
                new AngleRange(-10d, 10d),
                new AngleFeather(20d, 20d),
                5d);
            var hysteresis = new VisibilityHysteresis();

            Assert.That(hysteresis.Evaluate(band, 0d, 20d, 0.5d), Is.True);
            Assert.That(hysteresis.Evaluate(band, 0d, 24d, 0.1d), Is.True);
            Assert.That(hysteresis.Evaluate(band, 0d, 26d, 0d), Is.False);
        }

        /// <summary>複数visibility Modifier用の状態インスタンスが互いに干渉しないことを検証します。</summary>
        [Test]
        public void VisibilityHysteresis_InstancesKeepIndependentState()
        {
            var band = new AngleBand(
                "band",
                "Band",
                new AngleRange(170d, -170d),
                new AngleRange(-10d, 10d),
                new AngleFeather(20d, 10d),
                5d);
            var first = new VisibilityHysteresis();
            var second = new VisibilityHysteresis();

            Assert.That(first.Evaluate(band, 180d, 0d, 1d), Is.True);
            Assert.That(second.IsActive, Is.False);
            Assert.That(first.Evaluate(band, 160d, 0d, 0.5d), Is.True);
            Assert.That(first.Evaluate(band, 154d, 0d, 0d), Is.False);
            Assert.That(second.IsActive, Is.False);
        }

        private static AngleBand CreateBand(
            double yawMinimum,
            double yawMaximum,
            double pitchMinimum,
            double pitchMaximum,
            double yawFeather,
            double pitchFeather)
        {
            return new AngleBand(
                "band",
                "Band",
                new AngleRange(yawMinimum, yawMaximum),
                new AngleRange(pitchMinimum, pitchMaximum),
                new AngleFeather(yawFeather, pitchFeather));
        }
    }
}
