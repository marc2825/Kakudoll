using System;
using NUnit.Framework;
using Unity.PerformanceTesting;

namespace VLat.Tests.Play
{
    /// <summary>VLatランタイムの定常CPU時間とGC割り当てを検証します。</summary>
    public sealed class VlatRuntimePerformanceTests
    {
        private const string BatchTimeGroup = "VLat.EvaluateNow.Batch";
        private const string PerFrameTimeGroup = "VLat.EvaluateNow.MedianPerFrame";
        private const string GcAllocationGroup = "VLat.EvaluateNow.SteadyState.GCAllocCalls";
        private const double MaximumMedianMicroseconds = 300d;
        private const int GcMeasurementBatchCount = 21;

        /// <summary>7万頂点・20 Layer・60 ModifierでNFR-1を満たすことを検証します。</summary>
        [Test]
        [Performance]
        [Category("Performance")]
        public void EvaluateNow_ReferenceWorkload_MeetsCpuAndGcBudgets()
        {
            var fixture = SyntheticVlatFixture.CreatePerformance();
            try
            {
                Assert.That(
                    fixture.OriginalMesh.vertexCount,
                    Is.EqualTo(SyntheticVlatFixture.PerformanceVertexCount));
                Assert.That(
                    fixture.Document.Layers.Count,
                    Is.EqualTo(SyntheticVlatFixture.PerformanceLayerCount));
                Assert.That(
                    CountModifiers(fixture),
                    Is.EqualTo(SyntheticVlatFixture.PerformanceModifierCount));

                var initializeResult = fixture.Applier.Initialize(
                    fixture.AvatarRoot,
                    fixture.Head,
                    fixture.EvaluationCamera,
                    fixture.Document,
                    fixture.Buffers);
                Assert.That(initializeResult.Success, Is.True, FirstDiagnostic(initializeResult));
                Assert.That(fixture.Applier.ActiveBandCount, Is.EqualTo(20));
                Assert.That(fixture.Applier.AppliedModifierCount, Is.GreaterThan(0));

                var gcRecorder = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
                gcRecorder.enabled = false;
                GC.Collect();
                for (var warmupIndex = 0; warmupIndex < 5; warmupIndex++)
                {
                    fixture.EvaluateBatch();
                }

                Action steadyStateBatch = fixture.EvaluateBatch;
                var timeGroup = new SampleGroup(
                    BatchTimeGroup,
                    SampleUnit.Microsecond,
                    false);

                // GC計測区間は事前生成したdelegateだけを呼び、テスト側の列挙や文字列生成を含めない。
                Measure.Method(steadyStateBatch)
                    .SampleGroup(timeGroup)
                    .WarmupCount(0)
                    .MeasurementCount(21)
                    .IterationsPerMeasurement(1)
                    .Run();

                PerformanceTest.Active.CalculateStatisticalValues();
                var measuredTime = PerformanceTest.GetSampleGroup(BatchTimeGroup);
                Assert.That(measuredTime, Is.Not.Null);
                var medianPerFrame = measuredTime.Median / SyntheticVlatFixture.EvaluationsPerBatch;
                Measure.Custom(
                    new SampleGroup(PerFrameTimeGroup, SampleUnit.Microsecond, false),
                    medianPerFrame);
                Assert.That(
                    medianPerFrame,
                    Is.LessThanOrEqualTo(MaximumMedianMicroseconds),
                    "7万頂点・20 Layer・60 Modifierの定常評価中央値が0.3 ms/frameを超えました。");

                long gcAllocationCalls;
                try
                {
                    gcRecorder.enabled = true;
                    for (var batchIndex = 0; batchIndex < GcMeasurementBatchCount; batchIndex++)
                    {
                        fixture.EvaluateBatch();
                    }
                }
                finally
                {
                    gcRecorder.enabled = false;
                }

                gcAllocationCalls = gcRecorder.sampleBlockCount;
                Measure.Custom(
                    new SampleGroup(GcAllocationGroup, SampleUnit.Undefined, false),
                    gcAllocationCalls);
                Assert.That(
                    gcAllocationCalls,
                    Is.EqualTo(0L),
                    "強制GC後の再warm-upを経た定常評価1344回でGC.Allocが記録されました。");
            }
            finally
            {
                fixture.Dispose();
            }
        }

        private static int CountModifiers(SyntheticVlatFixture fixture)
        {
            var count = 0;
            for (var layerIndex = 0; layerIndex < fixture.Document.Layers.Count; layerIndex++)
            {
                count += fixture.Document.Layers[layerIndex].Modifiers.Count;
            }

            return count;
        }

        private static string FirstDiagnostic(VLat.Runtime.VlatRuntimeInitializationResult result)
        {
            if (result.Diagnostics == null || result.Diagnostics.Count == 0)
            {
                return string.Empty;
            }

            var diagnostic = result.Diagnostics[0];
            return diagnostic.Path + ": " + diagnostic.Message;
        }
    }
}
