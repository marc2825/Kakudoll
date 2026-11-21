using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using VLat.Editor.Authoring;

namespace VLat.Tests.Edit
{
    /// <summary>実D3D描画を独立した手動BlendShape参照画像と比較し、証拠を出力します。</summary>
    public sealed class VlatKeyformPreviewImageTests
    {
        /// <summary>0/45/90度×中立/表情のAfterとBeforeを既知の元BlendShape重みで描いたoracleと比較します。</summary>
        [Test]
        public void FixedCameraPixels_MatchIndependentBlendShapeOracleAndDifferFromBefore()
        {
            using var actual = new VlatKeyformPreviewFixture();
            using var oracle = new VlatKeyformPreviewFixture(true);
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "VLat1CImages"));
            Directory.CreateDirectory(output);
            var report = "Unity=" + Application.unityVersion + " GPU=" + SystemInfo.graphicsDeviceName +
                " API=" + SystemInfo.graphicsDeviceType + "\n" +
                "256x256, AA1, Unlit/Color, white cube, background(0.12,0.12,0.12); " +
                "oracle=original shapes at independent weights; max channel error <= 1/255; " +
                "foreground >= 1000 pixels, After/Before changed >= 100 pixels per view\n";
            for (var expression = 0; expression <= 1; expression++)
            {
                actual.Preview.SetExpressionWeight(0, expression); actual.Preview.RenderIfNeeded();
                oracle.Preview.SetExpressionWeight(0, expression); oracle.Preview.RenderIfNeeded();
                for (var i = 0; i < 3; i++)
                {
                    var view = (VlatPreviewView)i;
                    var prefix = "expression" + expression + "-" + view;
                    // 参照にはKeyform deltaを持たせず、入力shapeの既知の重みを直接設定する。
                    oracle.Renderer.SetBlendShapeWeight(0, i == 0 ? 100 : i == 1 ? 50 : 0);
                    oracle.Renderer.SetBlendShapeWeight(1, i == 0 ? 0 : i == 1 ? 50 : 100);
                    oracle.Preview.SetAngles(view, i * 45, 0); oracle.Preview.RenderIfNeeded();
                    var after = VlatKeyformPreviewFixture.Pixels(actual.Preview, view, Path.Combine(output, prefix + "-after.png"));
                    var expected = VlatKeyformPreviewFixture.Pixels(oracle.Preview, view, Path.Combine(output, prefix + "-oracle.png"));
                    var maxError = MaxError(after, expected);
                    TestContext.WriteLine(prefix + " actual camera=" + actual.Preview.GetCamera(view).transform.position +
                        " oracle camera=" + oracle.Preview.GetCamera(view).transform.position +
                        " oracle weights=" + oracle.Renderer.GetBlendShapeWeight(0) + "," + oracle.Renderer.GetBlendShapeWeight(1) +
                        " bakedVertex0=" + oracle.Bake()[0]);
                    Assert.That(maxError, Is.LessThanOrEqualTo(1), prefix + " independent oracle");
                    actual.Preview.SetApplicationEnabled(false); actual.Preview.RenderIfNeeded();
                    var before = VlatKeyformPreviewFixture.Pixels(actual.Preview, view, Path.Combine(output, prefix + "-before.png"));
                    oracle.Renderer.SetBlendShapeWeight(0, 0); oracle.Renderer.SetBlendShapeWeight(1, 0);
                    oracle.Preview.SetAngles(view, i * 45, 0); oracle.Preview.RenderIfNeeded();
                    Assert.That(MaxError(before, VlatKeyformPreviewFixture.Pixels(oracle.Preview, view)), Is.LessThanOrEqualTo(1));
                    var changed = 0; var foreground = 0;
                    for (var pixel = 0; pixel < after.Length; pixel++)
                    {
                        if (after[pixel].r > 200) foreground++;
                        if (Math.Abs(after[pixel].r - before[pixel].r) > 8) changed++;
                    }
                    Assert.That(foreground, Is.GreaterThanOrEqualTo(1000), prefix + " renders nonempty geometry");
                    Assert.That(changed, Is.GreaterThanOrEqualTo(100), prefix + " meaningful correction");
                    report += prefix + " maxChannelError=" + maxError + " foreground=" + foreground + " changed=" + changed + "\n";
                    actual.Preview.SetApplicationEnabled(true); actual.Preview.RenderIfNeeded();
                }
            }
            File.WriteAllText(Path.Combine(output, "comparison.txt"), report);
            TestContext.WriteLine(report);
        }

        private static int MaxError(Color32[] actual, Color32[] expected)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length)); var max = 0;
            for (var i = 0; i < actual.Length; i++)
            {
                max = Math.Max(max, Math.Abs(actual[i].r - expected[i].r));
                max = Math.Max(max, Math.Abs(actual[i].g - expected[i].g));
                max = Math.Max(max, Math.Abs(actual[i].b - expected[i].b));
                max = Math.Max(max, Math.Abs(actual[i].a - expected[i].a));
            }
            return max;
        }
    }
}
