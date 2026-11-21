using System;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using VLat.Editor.Authoring;

namespace VLat.Tests.Edit
{
    /// <summary>多頂点・多Renderer・複数boneと測定器の健全性を実Previewで回帰検証します。</summary>
    public sealed class VlatEditor1ERegressionTests
    {
        /// <summary>本Unityの実GC.Alloc recorderが既知の割当を検出することを確認します。</summary>
        [Test]
        public void AllocationRecorder_DetectsKnownAllocation()
        {
            using var recorder=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"GC.Alloc",1,
                ProfilerRecorderOptions.SumAllSamplesInFrame|ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            var buffer=new byte[4096];recorder.Stop();GC.KeepAlive(buffer);
            Assert.That(recorder.Count,Is.GreaterThan(0));Assert.That(recorder.GetSample(0).Count,Is.GreaterThanOrEqualTo(1));
        }

        /// <summary>大量のdelta置換も既存Unity履歴で一括Undo/Redoされ、保存点・分岐が一致します。</summary>
        [Test]
        public void BulkPositions_CompleteUndoRoundTrips()
        {
            using var f=new VlatKeyformPreviewFixture(true);f.Preview.Dispose();f.Session.NewDocument(VlatCloseDecision.Discard);
            VlatEditor1EProbe.ConfigureLargeFixture(f,128,128,1);f.Seed(f.Session,true);f.Preview=new VlatPreviewController();f.Adopt(f.Preview);f.Connect();
            VlatKeyformDragTests.Prepare(f,all:true);var path=Path.Combine(f.DirectoryPath,"bulk.vlat.json");Assert.That(f.Session.SaveAs(path),Is.True);
            var before=f.Session.ContentFingerprint;var file=File.ReadAllBytes(path);Undo.ClearAll();
            VlatKeyformDragTests.Drag(f.Preview,new Vector2(4,2));var after=f.Session.ContentFingerprint;
            Assert.That(f.Session.Document.KeyformGroups[0].Keys[0].Deltas.Count,Is.EqualTo(16384));
            Undo.PerformUndo();f.Session.RefreshAfterUndoRedo();Assert.That(f.Session.ContentFingerprint,Is.EqualTo(before));Assert.That(f.Session.IsDirty,Is.False);
            Undo.PerformRedo();f.Session.RefreshAfterUndoRedo();Assert.That(f.Session.ContentFingerprint,Is.EqualTo(after));Assert.That(f.Session.IsDirty,Is.True);
            Undo.PerformUndo();f.Session.RefreshAfterUndoRedo();f.Preview.SetDocument(f.Session.Document);f.Preview.RenderIfNeeded();
            VlatKeyformDragTests.Drag(f.Preview,new Vector2(-2,3));var branch=f.Session.ContentFingerprint;
            Undo.PerformRedo();f.Session.RefreshAfterUndoRedo();Assert.That(f.Session.ContentFingerprint,Is.EqualTo(branch));Assert.That(branch,Is.Not.EqualTo(after));
            CollectionAssert.AreEqual(file,File.ReadAllBytes(path));
        }

        /// <summary>compact Recoveryの内容と保存点が、従来の整形JSON snapshotでも復元できることを確認します。</summary>
        [Test]
        public void CompactRecovery_RoundTripsAndAcceptsIndentedSnapshot()
        {
            using var f=new VlatKeyformPreviewFixture();var directory=Path.Combine(f.DirectoryPath,"session");
            var manifestPath=Path.Combine(directory,"editor-session.json");var manifest=JObject.Parse(File.ReadAllText(manifestPath));
            var snapshotPath=Path.Combine(directory,(string)manifest["snapshotFile"]);var compact=File.ReadAllText(snapshotPath);
            Assert.That(compact.Contains("\n"),Is.False);var indented=JObject.Parse(compact).ToString();Assert.That(compact.Length,Is.LessThan(indented.Length));
            File.WriteAllText(snapshotPath,indented);manifest["snapshotHash"]=VlatSessionFingerprint.Hash(System.Text.Encoding.UTF8.GetBytes(indented));File.WriteAllText(manifestPath,manifest.ToString());
            using var restored=new VlatEditorDocumentController(new VlatRecoveryStore(directory));Assert.That(restored.TryRestore(),Is.True);
            Assert.That(restored.ContentFingerprint,Is.EqualTo(f.Session.ContentFingerprint));Assert.That(restored.IsDirty,Is.EqualTo(f.Session.IsDirty));
        }

        /// <summary>編集Sceneの環境光にPreviewが依存せず、照明・dirty状態も書き換えません。</summary>
        [Test]
        public void PreviewLighting_IsolatedAndRestoredIncludingDragFailure()
        {
            var scene=SceneManager.GetActiveScene();var mode=RenderSettings.ambientMode;var color=RenderSettings.ambientLight;
            var reflection=RenderSettings.reflectionIntensity;var probe=RenderSettings.ambientProbe;
            try
            {
                RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.red;
                RenderSettings.reflectionIntensity=.7f;var dirty=scene.isDirty;
                using var f=new VlatKeyformPreviewFixture();f.Material.shader=Shader.Find("Standard");
                f.Preview.SetAngles(VlatPreviewView.Main,10,0);f.Preview.RenderIfNeeded();
                var before=VlatKeyformPreviewFixture.Pixels(f.Preview,VlatPreviewView.Main);
                Assert.That(RenderSettings.ambientLight,Is.EqualTo(Color.red));Assert.That(RenderSettings.reflectionIntensity,Is.EqualTo(.7f));
                RenderSettings.ambientLight=Color.green;
                f.Preview.SetAngles(VlatPreviewView.Main,10,0);f.Preview.RenderIfNeeded();
                CollectionAssert.AreEqual(before,VlatKeyformPreviewFixture.Pixels(f.Preview,VlatPreviewView.Main));
                VlatKeyformDragTests.Prepare(f);Assert.That(f.Preview.BeginKeyformDrag(VlatKeyformDragTests.Start),Is.True);
                UnityEngine.Object.DestroyImmediate(f.Preview.DragMesh);f.Preview.RenderIfNeeded();
                Assert.That(RenderSettings.ambientLight,Is.EqualTo(Color.green));Assert.That(RenderSettings.reflectionIntensity,Is.EqualTo(.7f));
                Assert.That(scene.isDirty,Is.EqualTo(dirty));
            }
            finally {RenderSettings.ambientMode=mode;RenderSettings.ambientLight=color;RenderSettings.reflectionIntensity=reflection;RenderSettings.ambientProbe=probe;}
        }

        /// <summary>外部変更で壊れたrigでも、一時Mesh解放がUnity native errorを出さないことを確認します。</summary>
        [Test]
        public void InvalidRig_DisposeWithoutRestoringMalformedBounds()
        {
            using var f=new VlatKeyformPreviewFixture();VlatKeyformDragTests.Prepare(f);
            Assert.That(f.Preview.BeginKeyformDrag(VlatKeyformDragTests.Start),Is.True);var temporary=f.Preview.DragMesh;
            var content=f.Session.ContentFingerprint;f.Renderer.bones=Array.Empty<Transform>();
            Assert.That(f.Preview.CommitKeyformDrag(VlatKeyformDragTests.Start+Vector2.one),Is.False);
            Assert.That(f.Preview.RenderIfNeeded(),Is.False);Assert.That(f.Preview.DiagnosticSummary,Does.Contain("bindpose"));
            f.Preview.Dispose();Assert.That(temporary==null,Is.True);Assert.That(f.Session.ContentFingerprint,Is.EqualTo(content));
        }

        /// <summary>Bone/bindposeの不整合はPreview生成時に診断し、原本や文書を変更しません。</summary>
        [Test]
        public void InvalidRig_BindingPreviewReportsDiagnostic()
        {
            using var f=new VlatKeyformPreviewFixture();f.Preview.SetDocument(null);f.Preview.RenderIfNeeded();
            f.Renderer.bones=Array.Empty<Transform>();var content=f.Session.ContentFingerprint;
            f.Preview.SetDocument(f.Session.Document);f.Preview.RenderIfNeeded();
            Assert.That(f.Preview.DiagnosticSummary,Does.Contain("bindpose"));Assert.That(f.Session.ContentFingerprint,Is.EqualTo(content));
        }

        /// <summary>疎な選択の更新でもcalibration offsetが選択外へ残らず、開始点へ戻ると完全一致します。</summary>
        [TestCase(false)] [TestCase(true)]
        public void SparseUpdate_ReturnToStartPreservesAllVertices(bool populatedKey)
        {
            using var f=new VlatKeyformPreviewFixture(!populatedKey);VlatKeyformDragTests.Prepare(f);
            var expected=f.Mesh.vertices;foreach(var d in f.Session.Document.KeyformGroups[0].Keys[0].Deltas) expected[d.VertexIndex]+=d.PositionDelta;
            Assert.That(f.Preview.BeginKeyformDrag(VlatKeyformDragTests.Start),Is.True);CollectionAssert.AreEqual(expected,f.Preview.DragMesh.vertices);
            f.Preview.UpdateKeyformDrag(VlatKeyformDragTests.Start+Vector2.one*8);var moved=f.Preview.DragMesh.vertices;
            for(var i=0;i<expected.Length;i++)if(f.Preview.VertexSelection.Weights[i]==0)Assert.That(moved[i],Is.EqualTo(expected[i]));
            f.Preview.UpdateKeyformDrag(VlatKeyformDragTests.Start);CollectionAssert.AreEqual(expected,f.Preview.DragMesh.vertices);
        }

        /// <summary>多骨gridの前・側・背面と非一様Scaleで画面変位と選択外頂点の不変を検証します。</summary>
        [TestCase(0,false)] [TestCase(90,false)] [TestCase(-90,false)] [TestCase(180,false)]
        [TestCase(45,true)] [TestCase(-45,true)]
        public void GridSkinning_DragLocalityAndOriginals(float yaw,bool orthographic)
        {
            using var f=new VlatKeyformPreviewFixture(true);f.Preview.Dispose();f.Session.NewDocument(VlatCloseDecision.Discard);
            VlatEditor1EProbe.ConfigureLargeFixture(f,64,32,3);f.Seed(f.Session,true);
            f.Preview=new VlatPreviewController();f.Adopt(f.Preview);f.Connect();
            var original=f.Mesh.vertices;var originalWeights=f.Mesh.boneWeights;
            f.Clone.transform.position=new Vector3(2,1,-3);f.Clone.transform.rotation=Quaternion.Euler(12,35,7);
            f.Clone.transform.localScale=new Vector3(1.2f,.85f,1.1f);f.Head.localRotation=Quaternion.Euler(9,17,-6);f.Head.localScale=new Vector3(.9f,1.2f,1.05f);
            f.Preview.SetAngles(VlatPreviewView.Main,yaw,12);
            var camera=f.Preview.GetCamera(VlatPreviewView.Main);camera.orthographic=orthographic;camera.orthographicSize=1.2f;
            VlatKeyformDragTests.Prepare(f,soft:.12f);
            var before=f.Bake();var weights=new float[before.Length];for(var i=0;i<weights.Length;i++)weights[i]=f.Preview.VertexSelection.Weights[i];
            Assert.That(Array.Exists(weights,w=>w>0 && w<1),Is.True);Assert.That(Array.Exists(weights,w=>w==0),Is.True);
            var content=f.Session.ContentFingerprint;var delta=new Vector2(6,-4);
            Assert.That(f.Preview.BeginKeyformDrag(VlatKeyformDragTests.Start),Is.True,f.Preview.DragStatus);
            Assert.That(f.Preview.UpdateKeyformDrag(VlatKeyformDragTests.Start+delta),Is.True);
            Assert.That(f.Session.ContentFingerprint,Is.EqualTo(content));
            Assert.That(f.Preview.CommitKeyformDrag(VlatKeyformDragTests.Start+delta),Is.True,f.Preview.DragStatus);f.Preview.RenderIfNeeded();
            var after=f.Bake();var max=0f;
            for(var i=0;i<before.Length;i++)
            {
                var a=camera.WorldToScreenPoint(f.Renderer.transform.TransformPoint(before[i]));
                var b=camera.WorldToScreenPoint(f.Renderer.transform.TransformPoint(after[i]));
                max=Mathf.Max(max,Vector3.Distance(b-a,new Vector3(delta.x,delta.y,0)*weights[i]));
                if(weights[i]==0)Assert.That(after[i],Is.EqualTo(before[i]));
            }
            Assert.That(max,Is.LessThan(.01f));CollectionAssert.AreEqual(original,f.Mesh.vertices);CollectionAssert.AreEqual(originalWeights,f.Mesh.boneWeights);
            Assert.That(f.SourceRenderer.GetBlendShapeWeight(0),Is.Zero);
            TestContext.WriteLine("GRID 2048 vertices/3 renderers/2 bones yaw="+yaw+" ortho="+orthographic+" maxScreenErrorPx="+max);
        }
    }
}
