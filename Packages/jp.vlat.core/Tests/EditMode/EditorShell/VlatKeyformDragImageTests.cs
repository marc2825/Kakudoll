using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using VLat.Editor.Authoring;

namespace VLat.Tests.Edit
{
    /// <summary>実3視点描画を、カメラの解析式から独立作成した元BlendShapeと画素比較します。</summary>
    public sealed class VlatKeyformDragImageTests
    {
        /// <summary>一時表示・確定・補間・Expression・Beforeを独立画像と比較します。</summary>
        [Test]
        public void DragPixels_MatchAnalyticShapeOracleAcrossThreeViews()
        {
            using var actual = new VlatKeyformPreviewFixture(true);
            using var oracle = new VlatKeyformPreviewFixture(true);
            VlatKeyformDragTests.Prepare(actual, all: true);
            var p = actual.Preview; var camera = p.GetCamera(VlatPreviewView.Main);
            var output = Path.GetFullPath(Path.Combine(Application.dataPath,"..","Library","VLat1DImages"));
            Directory.CreateDirectory(output);
            var deltas = new Vector3[8]; var baked = actual.Bake();
            // 独立oracle: perspectiveのピクセル倍率と既知の単一Headボーンから計算する。
            for (var i = 0; i < deltas.Length; i++)
            {
                var world = actual.Renderer.transform.TransformPoint(baked[i]);
                var depth = Vector3.Dot(world - camera.transform.position, camera.transform.forward);
                var units = 2 * depth * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad / 2) / camera.pixelHeight;
                deltas[i] = actual.Head.InverseTransformVector((camera.transform.right * 24 + camera.transform.up * 12) * units);
            }
            oracle.Mesh.AddBlendShapeFrame("AnalyticDrag", 100, deltas, null, null);
            var beforeSub = VlatKeyformPreviewFixture.Pixels(p, VlatPreviewView.SubA);
            var before = VlatKeyformPreviewFixture.Pixels(p, VlatPreviewView.Main, Path.Combine(output,"main-before.png"));
            Assert.That(p.BeginKeyformDrag(VlatKeyformDragTests.Start), Is.True, p.DragStatus);
            p.UpdateKeyformDrag(VlatKeyformDragTests.Start + new Vector2(24,12)); p.RenderIfNeeded();
            var temporary = VlatKeyformPreviewFixture.Pixels(p,VlatPreviewView.Main,Path.Combine(output,"main-temporary.png"));
            CollectionAssert.AreEqual(beforeSub,VlatKeyformPreviewFixture.Pixels(p,VlatPreviewView.SubA));
            Assert.That(p.DragStatus, Does.Contain("未確定"));
            oracle.Renderer.SetBlendShapeWeight(3,100); oracle.Preview.SetAngles(VlatPreviewView.Main,0,0); oracle.Preview.RenderIfNeeded();
            Assert.That(MaxError(temporary,VlatKeyformPreviewFixture.Pixels(oracle.Preview,VlatPreviewView.Main)), Is.LessThanOrEqualTo(1));
            Assert.That(p.CommitKeyformDrag(VlatKeyformDragTests.Start + new Vector2(24,12)), Is.True);
            p.SetGeometrySelectionEnabled(false); p.RenderIfNeeded();
            var changed = 0; for (var i = 0; i < before.Length; i++) if (Math.Abs(temporary[i].r-before[i].r)>8) changed++;
            Assert.That(changed, Is.GreaterThan(100));
            var report = "Unity="+Application.unityVersion+" GPU="+SystemInfo.graphicsDeviceName+" API="+SystemInfo.graphicsDeviceType+"\n"+
                "256x256 AA1 Unlit; oracle=analytic perspective world displacement + single Head inverse + original BlendShape weights 100/50/0; maxRGBA <=1/255\n"+
                "temporary Main vs oracle PASS; SubA unchanged while dragging; changedPixels="+changed+"\n";
            for (var expression = 0; expression <= 1; expression++)
            {
                p.SetExpressionWeight(0,expression); p.RenderIfNeeded();
                oracle.Preview.SetExpressionWeight(0,expression); oracle.Preview.RenderIfNeeded();
                for (var viewIndex = 0; viewIndex < 3; viewIndex++)
                {
                    var view = (VlatPreviewView)viewIndex; var prefix="expression"+expression+"-"+view;
                    oracle.Renderer.SetBlendShapeWeight(3,viewIndex==0 ? 100 : viewIndex==1 ? 50 : 0);
                    oracle.Preview.SetAngles(view,viewIndex*45,0); oracle.Preview.RenderIfNeeded();
                    var pixels=VlatKeyformPreviewFixture.Pixels(p,view,Path.Combine(output,prefix+"-after.png"));
                    var expected=VlatKeyformPreviewFixture.Pixels(oracle.Preview,view,Path.Combine(output,prefix+"-oracle.png"));
                    var error=MaxError(pixels,expected); Assert.That(error, Is.LessThanOrEqualTo(1),prefix);
                    report+=prefix+" maxRGBA="+error+"\n";
                }
            }
            var path=Path.Combine(actual.DirectoryPath,"image.vlat.json");
            Assert.That(actual.Session.SaveAs(path),Is.True);
            var saved=VlatKeyformPreviewFixture.Pixels(p,VlatPreviewView.SubA);
            Assert.That(actual.Session.Open(path,VlatCloseDecision.Discard),Is.True);
            p.SetDocument(actual.Session.Document); p.SetKeyformGroupSelection(0); p.RenderIfNeeded();
            Assert.That(MaxError(saved,VlatKeyformPreviewFixture.Pixels(p,VlatPreviewView.SubA)),Is.Zero);
            p.SetApplicationEnabled(false); p.RenderIfNeeded();
            oracle.Renderer.SetBlendShapeWeight(3,0); oracle.Preview.SetAngles(VlatPreviewView.Main,0,0); oracle.Preview.RenderIfNeeded();
            Assert.That(MaxError(VlatKeyformPreviewFixture.Pixels(p,VlatPreviewView.Main),VlatKeyformPreviewFixture.Pixels(oracle.Preview,VlatPreviewView.Main)),Is.LessThanOrEqualTo(1));
            report+="save/reload SubA exact; Before expression oracle PASS\n";
            File.WriteAllText(Path.Combine(output,"comparison.txt"),report); TestContext.WriteLine(report);
        }
        private static int MaxError(Color32[] a,Color32[] b)
        {
            Assert.That(a.Length,Is.EqualTo(b.Length)); var max=0;
            for(var i=0;i<a.Length;i++)
            {
                max=Math.Max(max,Math.Abs(a[i].r-b[i].r)); max=Math.Max(max,Math.Abs(a[i].g-b[i].g));
                max=Math.Max(max,Math.Abs(a[i].b-b[i].b)); max=Math.Max(max,Math.Abs(a[i].a-b[i].a));
            }
            return max;
        }
    }
}
