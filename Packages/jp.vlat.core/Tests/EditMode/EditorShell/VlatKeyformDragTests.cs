using System;
using System.IO;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using Unity.Profiling;
using UnityEngine;
using VLat.Editor.Authoring;
using Object = UnityEngine.Object;

namespace VLat.Tests.Edit
{
    /// <summary>実Preview、元index、Unity Skinning、Undo/保存/Recoveryを通すドラッグ回帰です。</summary>
    public sealed class VlatKeyformDragTests
    {
        internal static readonly Vector2 Start = new Vector2(128, 128);

        internal static void Prepare(VlatKeyformPreviewFixture f, bool all = false, float soft = .0001f,
            VlatPreviewController preview = null, VlatEditorDocumentController session = null)
        {
            var p = preview ?? f.Preview; var s = session ?? f.Session;
            s.SelectKeyform(0, 0);
            p.SetDocument(s.Document); p.SetKeyformGroupSelection(0); p.SetKeyformEditingSelection(s);
            p.SetGeometrySelectionEnabled(true); p.SetVertexSoftRadius(soft);
            p.SetVertexVisibilityMode(VlatVertexVisibilityMode.SelectThrough);
            p.RenderIfNeeded();
            if (all)
            {
                p.SetVertexSelectionTool(VlatVertexSelectionTool.Rectangle);
                p.BeginVertexSelection(new Vector2(-10000,-10000), VlatVertexSelectionOperation.Replace);
                p.EndVertexSelection(new Vector2(10000,10000));
            }
            else
            {
                p.SetVertexSelectionTool(VlatVertexSelectionTool.Single);
                var screen = (Vector2)p.VertexSelection.ScreenPositions[7];
                p.BeginVertexSelection(screen, VlatVertexSelectionOperation.Replace); p.EndVertexSelection(screen);
            }
            Assert.That(p.VertexSelection.SelectedCount, Is.GreaterThan(0));
            p.SetKeyformDragEnabled(true); p.RenderIfNeeded();
        }

        internal static void Drag(VlatPreviewController p, Vector2 delta)
        {
            Assert.That(p.BeginKeyformDrag(Start), Is.True, p.DragStatus);
            Assert.That(p.UpdateKeyformDrag(Start + delta), Is.True, p.DragStatus);
            Assert.That(p.RenderIfNeeded(), Is.True, p.DragStatus);
            Assert.That(p.CommitKeyformDrag(Start + delta), Is.True, p.DragStatus);
            p.RenderIfNeeded();
        }

        /// <summary>Perspective/Ortho、非一様Scaleと回転で固定深度・Soft weightの画面変位を独立に確認します。</summary>
        [TestCase(false, false, .0001f)]
        [TestCase(true, false, .0001f)]
        [TestCase(false, true, .0001f)]
        [TestCase(true, true, 1f)]
        [TestCase(false, true, 1f)]
        public void ScreenDrag_FrozenDepthAndSoftWeights(bool ortho, bool transformed, float soft)
        {
            using var f = new VlatKeyformPreviewFixture();
            if (transformed)
            {
                f.Clone.transform.position = new Vector3(2, -1, 3);
                f.Clone.transform.rotation = Quaternion.Euler(12, 35, -9);
                f.Clone.transform.localScale = new Vector3(1.2f, .8f, 1.5f);
                f.Head.localRotation = Quaternion.Euler(-7, 18, 11);
                f.Head.localScale = new Vector3(.8f, 1.1f, .9f);
                f.Preview.SetAngles(VlatPreviewView.Main, 31, 19);
            }
            var camera = f.Preview.GetCamera(VlatPreviewView.Main);
            camera.orthographic = ortho; camera.orthographicSize = 1.2f;
            Prepare(f, soft: soft);
            var before = f.Bake(); var screen = new Vector3[before.Length];
            var weights = new float[before.Length];
            for (var i = 0; i < before.Length; i++)
            {
                screen[i] = camera.WorldToScreenPoint(f.Renderer.transform.TransformPoint(before[i]));
                weights[i] = f.Preview.VertexSelection.Weights[i];
            }
            if (soft > .1f) Assert.That(Array.Exists(weights, w => w > 0 && w < 1), Is.True);
            var revision = f.Session.Document.Revision; var fingerprint = f.Session.ContentFingerprint;
            Assert.That(f.Preview.BeginKeyformDrag(Start), Is.True, f.Preview.DragStatus);
            var temporary = f.Preview.DragMesh;
            f.Preview.SetVertexSoftRadius(.0001f); // 開始時weightで計算する。
            Assert.That(f.Preview.UpdateKeyformDrag(Start + new Vector2(14, -8)), Is.True, f.Preview.DragStatus);
            Assert.That(f.Session.Document.Revision, Is.EqualTo(revision));
            Assert.That(f.Session.ContentFingerprint, Is.EqualTo(fingerprint));
            Assert.That(f.Preview.CommitKeyformDrag(Start + new Vector2(14, -8)), Is.True, f.Preview.DragStatus);
            Assert.That(temporary == null, Is.True);
            f.Preview.RenderIfNeeded();
            var after = f.Bake(); var maxError = 0f;
            for (var i = 0; i < after.Length; i++)
            {
                var actual = camera.WorldToScreenPoint(f.Renderer.transform.TransformPoint(after[i]));
                var expected = screen[i] + new Vector3(14, -8, 0) * weights[i];
                maxError = Mathf.Max(maxError, Vector3.Distance(actual, expected));
                Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.003f), "vertex=" + i);
                if (weights[i] == 0) Assert.That(after[i], Is.EqualTo(before[i]));
            }
            TestContext.WriteLine("SCREEN oracle=Camera.WorldToScreenPoint(BakeMesh) maxErrorPx=" + maxError +
                " ortho=" + ortho + " root/headNonuniform=" + transformed + " soft=" + soft);
        }

        /// <summary>一Gesture一Undo、保存点、Redo分岐、再読込、Recoveryを確認します。</summary>
        [Test]
        public void GestureUndo_SavepointBranchReloadAndRecovery()
        {
            using var f = new VlatKeyformPreviewFixture(true);
            Prepare(f);
            var path = Path.Combine(f.DirectoryPath, "saved.vlat.json");
            Assert.That(f.Session.SaveAs(path), Is.True);
            var clean = f.Session.ContentFingerprint;
            var json = File.ReadAllBytes(path);
            Undo.ClearAll();
            Drag(f.Preview, new Vector2(14, 0)); var edited = f.Session.ContentFingerprint;
            Assert.That(f.Session.IsDirty, Is.True);
            Assert.That(f.Session.Document.KeyformGroups[0].Keys[0].Deltas.Count, Is.EqualTo(1));
            Undo.PerformUndo(); f.Session.RefreshAfterUndoRedo();
            Assert.That(f.Session.ContentFingerprint, Is.EqualTo(clean)); Assert.That(f.Session.IsDirty, Is.False);
            Undo.PerformRedo(); f.Session.RefreshAfterUndoRedo(); Assert.That(f.Session.ContentFingerprint, Is.EqualTo(edited));
            Assert.That(f.Session.SaveAs(path), Is.True); var savedJson = File.ReadAllBytes(path);
            f.Preview.SetDocument(f.Session.Document); f.Preview.RenderIfNeeded();
            Drag(f.Preview, new Vector2(0, 10)); var second = f.Session.ContentFingerprint;
            Undo.PerformUndo(); f.Session.RefreshAfterUndoRedo(); Assert.That(f.Session.IsDirty, Is.False);
            Undo.PerformUndo(); f.Session.RefreshAfterUndoRedo(); Assert.That(f.Session.IsDirty, Is.True);
            CollectionAssert.AreEqual(savedJson, File.ReadAllBytes(path));
            Undo.PerformRedo(); f.Session.RefreshAfterUndoRedo();
            f.Preview.SetDocument(f.Session.Document); f.Preview.RenderIfNeeded();
            Drag(f.Preview, new Vector2(-7, 0)); var branch = f.Session.ContentFingerprint;
            Undo.PerformRedo(); f.Session.RefreshAfterUndoRedo(); Assert.That(f.Session.ContentFingerprint, Is.EqualTo(branch));
            Assert.That(branch, Is.Not.EqualTo(second));
            using (var restored = new VlatEditorDocumentController(new VlatRecoveryStore(Path.Combine(f.DirectoryPath, "session"))))
            {
                Assert.That(restored.TryRestore(), Is.True); Assert.That(restored.ContentFingerprint, Is.EqualTo(branch));
                Assert.That(restored.IsDirty, Is.True);
            }
            Assert.That(f.Session.SaveAs(path), Is.True); var final = f.Session.ContentFingerprint;
            Assert.That(f.Session.Open(path, VlatCloseDecision.Discard), Is.True);
            Assert.That(f.Session.ContentFingerprint, Is.EqualTo(final)); Assert.That(f.Session.IsDirty, Is.False);
            Assert.That(json, Is.Not.EqualTo(File.ReadAllBytes(path)));
        }

        /// <summary>ゼロ移動と開始点へ戻る操作はUndo/Dirty/Recoveryを追加しません。</summary>
        [TestCase(false)] [TestCase(true)]
        public void NoMotion_NoHistoryOrRecoveryWrite(bool returnToStart)
        {
            using var f = new VlatKeyformPreviewFixture(); Prepare(f);
            Assert.That(f.Session.SaveAs(Path.Combine(f.DirectoryPath, "zero.vlat.json")), Is.True);
            var revision = f.Session.Document.Revision; var before = RecoveryFiles(f.DirectoryPath);
            Undo.ClearAll(); Assert.That(f.Preview.BeginKeyformDrag(Start), Is.True);
            if (returnToStart) f.Preview.UpdateKeyformDrag(Start + Vector2.one * 10);
            Assert.That(f.Preview.CommitKeyformDrag(Start), Is.True);
            Assert.That(f.Session.Document.Revision, Is.EqualTo(revision)); Assert.That(f.Session.IsDirty, Is.False);
            CollectionAssert.AreEqual(before, RecoveryFiles(f.DirectoryPath));
            Undo.PerformUndo(); f.Session.RefreshAfterUndoRedo(); Assert.That(f.Session.Document.Revision, Is.EqualTo(revision));
        }

        /// <summary>変更競合と例外相当の非有限入力は一時Meshだけを破棄します。</summary>
        [TestCase("cancel")] [TestCase("camera")] [TestCase("root")] [TestCase("head")]
        [TestCase("key")] [TestCase("revision")] [TestCase("binding")]
        [TestCase("target")] [TestCase("rebuild")] [TestCase("nan")]
        [TestCase("bones")] [TestCase("bindposes")] [TestCase("quality")]
        [TestCase("mesh")] [TestCase("dispose")]
        public void InterruptedGesture_PreservesCommittedContent(string boundary)
        {
            using var f = new VlatKeyformPreviewFixture(); Prepare(f);
            Undo.ClearAll(); var revision = f.Session.Document.Revision;
            Assert.That(f.Preview.BeginKeyformDrag(Start), Is.True, f.Preview.DragStatus);
            var mesh = f.Preview.DragMesh;
            f.Preview.UpdateKeyformDrag(Start + Vector2.one * 10);
            var before = f.Session.ContentFingerprint;
            switch (boundary)
            {
                case "cancel": f.Preview.CancelKeyformDrag(); break;
                case "camera": f.Preview.GetCamera(VlatPreviewView.Main).fieldOfView = 40; break;
                case "root": f.Clone.transform.position += Vector3.one; break;
                case "head": f.Head.localRotation = Quaternion.Euler(0,20,0); break;
                case "key": f.Session.SelectKeyform(0,1); break;
                case "revision": f.Session.UpdateKeyformYaw(0,0,5); break;
                case "binding": f.Session.Document.Metadata.MeshBindings[0].PositionHash = "invalid"; break;
                case "bones": f.Renderer.bones = Array.Empty<Transform>(); break;
                case "bindposes": f.Mesh.bindposes = new[] { Matrix4x4.Translate(Vector3.one) }; break;
                case "quality": f.Renderer.quality = SkinQuality.Bone2; break;
                case "mesh": f.Renderer.sharedMesh = f.Mesh; break;
                case "dispose": f.Preview.Dispose(); break;
                case "target": f.Preview.SetVertexSelectionTarget(-1); break;
                case "rebuild": f.Preview.SetGeometrySelectionEnabled(false); f.Preview.RenderIfNeeded(); break;
                case "nan": Assert.That(f.Preview.UpdateKeyformDrag(new Vector2(float.NaN,0)), Is.False); break;
            }
            var afterExternal = f.Session.ContentFingerprint;
            Assert.That(f.Preview.CommitKeyformDrag(Start + Vector2.one * 10), Is.False, boundary);
            Assert.That(f.Preview.IsDragging, Is.False); Assert.That(mesh == null, Is.True);
            Assert.That(f.Session.ContentFingerprint, Is.EqualTo(afterExternal));
            if (boundary != "revision") Assert.That(f.Session.Document.Revision, Is.EqualTo(revision));
            Assert.That(f.Session.Document.KeyformGroups[0].Keys[0].Deltas[0].PositionDelta.x, Is.EqualTo(-.25f));
            TestContext.WriteLine("CANCEL " + boundary + "=" + f.Preview.DragStatus);
            // テストが意図的に壊したcloneのrigを戻してからfixture標準の解放を行う。
            if (boundary == "bones") f.Renderer.bones = new[] { f.Head };
        }

        /// <summary>空選択と特異行列、無効Bindingは操作開始を拒否します。</summary>
        [TestCase("empty")] [TestCase("scale")] [TestCase("projection")] [TestCase("binding")]
        public void InvalidStart_IsRejected(string invalid)
        {
            using var f = new VlatKeyformPreviewFixture(); Prepare(f);
            var revision = f.Session.Document.Revision;
            if (invalid == "empty") f.Preview.ClearVertexSelection();
            if (invalid == "scale") f.Head.localScale = Vector3.zero;
            if (invalid == "projection") f.Preview.GetCamera(VlatPreviewView.Main).projectionMatrix = Matrix4x4.zero;
            if (invalid == "binding") f.Session.Document.Metadata.MeshBindings[0].MeshName = "missing";
            Assert.That(f.Preview.BeginKeyformDrag(Start), Is.False, invalid);
            Assert.That(f.Preview.DragMesh, Is.Null); Assert.That(f.Session.Document.Revision, Is.EqualTo(revision));
        }

        /// <summary>ドラッグcommit候補全体を先に検証し、不正入力を部分適用しません。</summary>
        [TestCase("nan")] [TestCase("duplicate")] [TestCase("range")] [TestCase("normal")]
        public void InvalidCommit_IsAtomic(string invalid)
        {
            using var f = new VlatKeyformPreviewFixture(); Prepare(f);
            var doc = f.Session.Document; var group = doc.KeyformGroups[0]; var key = group.Keys[0];
            var input = new List<VlatAuthoringVertexDelta> { new VlatAuthoringVertexDelta { VertexIndex = 0, PositionDelta = Vector3.one } };
            if (invalid == "nan") input[0].PositionDelta = new Vector3(float.NaN,0,0);
            if (invalid == "duplicate") input.Add(input[0]);
            if (invalid == "range") input[0].VertexIndex = 8;
            if (invalid == "normal") input[0].NormalDelta = Vector3.one;
            var revision = doc.Revision; var fingerprint = f.Session.ContentFingerprint; Undo.ClearAll();
            Assert.That(f.Session.CommitKeyformDrag(doc, revision,0,0,group.EditorIdentity,key.EditorIdentity,input), Is.False);
            Assert.That(doc.Revision, Is.EqualTo(revision)); Assert.That(f.Session.ContentFingerprint, Is.EqualTo(fingerprint));
            Undo.PerformUndo(); Assert.That(doc.Revision, Is.EqualTo(revision));
        }

        /// <summary>一時変形、Cancel、Commit、終了の全経路で原本Mesh/Material/weightを保持します。</summary>
        [Test]
        public void Originals_AndExpressionStash_ArePreserved()
        {
            using var f = new VlatKeyformPreviewFixture();
            var vertices = f.Mesh.vertices; var shapes = f.Mesh.blendShapeCount; var material = EditorJsonUtility.ToJson(f.Material);
            var sceneDirty = f.Source.gameObject.scene.isDirty;
            f.SourceRenderer.SetBlendShapeWeight(0, 23); f.Preview.SetExpressionWeight(0,.8f);
            f.Preview.SetApplicationEnabled(false); f.Preview.RenderIfNeeded();
            Prepare(f);
            Assert.That(f.Preview.GetExpressionWeight(0), Is.EqualTo(0));
            Assert.That(f.Preview.BeginKeyformDrag(Start), Is.True); f.Preview.UpdateKeyformDrag(Start + Vector2.one * 10);
            var temp = f.Preview.DragMesh; f.Preview.RenderIfNeeded(); f.Preview.CancelKeyformDrag(); f.Preview.RenderIfNeeded();
            Assert.That(temp == null, Is.True);
            Drag(f.Preview, Vector2.one * 10);
            f.Preview.SetGeometrySelectionEnabled(false); f.Preview.RenderIfNeeded();
            Assert.That(f.Preview.GetExpressionWeight(0), Is.EqualTo(.8f)); Assert.That(f.Preview.ApplicationEnabled, Is.False);
            CollectionAssert.AreEqual(vertices, f.Mesh.vertices); Assert.That(f.Mesh.blendShapeCount, Is.EqualTo(shapes));
            Assert.That(EditorJsonUtility.ToJson(f.Material), Is.EqualTo(material));
            Assert.That(f.SourceRenderer.GetBlendShapeWeight(0), Is.EqualTo(23)); Assert.That(f.SourceRenderer.sharedMesh, Is.SameAs(f.Mesh));
            Assert.That(f.Source.gameObject.scene.isDirty, Is.EqualTo(sceneDirty));
        }

        /// <summary>複数ボーンの実Skinningでも画面移動が一致し、特異な混合変換は拒否します。</summary>
        [TestCase(false)] [TestCase(true)]
        public void MultiBonePose_UsesActualSkinningOrRejectsSingularity(bool singular)
        {
            using var f = new VlatKeyformPreviewFixture(true);
            var bone = new GameObject("SecondBone").transform; bone.SetParent(f.Head, false);
            bone.localRotation = Quaternion.Euler(singular ? new Vector3(0,180,0) : new Vector3(20,30,10));
            if (!singular) bone.localScale = new Vector3(.8f,1.1f,1.2f);
            f.Preview.SetDocument(null); // 既存binderを解放してから合成fixtureのrigを準備する。
            f.Mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity };
            var weights = new BoneWeight[8];
            for (var i=0;i<8;i++) weights[i] = new BoneWeight { boneIndex0=0, boneIndex1=1, weight0=.5f, weight1=.5f };
            f.Mesh.boneWeights = weights; f.Renderer.bones = new[] { f.Head, bone };
            Prepare(f, all:true);
            if (singular)
            {
                Assert.That(f.Preview.BeginKeyformDrag(Start), Is.False, f.Preview.DragStatus);
                Assert.That(f.Session.Document.KeyformGroups[0].Keys[0].Deltas.Count, Is.Zero);
                return;
            }
            var camera=f.Preview.GetCamera(VlatPreviewView.Main); var before=f.Bake();
            Drag(f.Preview,new Vector2(17,9)); var after=f.Bake();
            for(var i=0;i<8;i++)
            {
                var a=camera.WorldToScreenPoint(f.Renderer.transform.TransformPoint(before[i]));
                var b=camera.WorldToScreenPoint(f.Renderer.transform.TransformPoint(after[i]));
                Assert.That(Vector3.Distance(b-a,new Vector3(17,9,0)),Is.LessThan(.003f));
            }
        }

        /// <summary>MouseMoveの数値・Mesh更新は事前確保bufferを再使用します。</summary>
        [Test]
        public void MouseMove_DoesNotAllocateManagedMemory()
        {
            using var f = new VlatKeyformPreviewFixture(); Prepare(f,all:true);
            Assert.That(f.Preview.BeginKeyformDrag(Start),Is.True);
            for(var i=0;i<10;i++) f.Preview.UpdateKeyformDrag(Start+new Vector2(i,5));
            using var recorder=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"GC.Alloc",1,
                ProfilerRecorderOptions.SumAllSamplesInFrame|ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            var began=System.Diagnostics.Stopwatch.GetTimestamp();
            for(var i=0;i<200;i++) f.Preview.UpdateKeyformDrag(Start+new Vector2(i%20,5));
            var ticks=System.Diagnostics.Stopwatch.GetTimestamp()-began;
            recorder.Stop();
            var bytes=recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;
            Assert.That(bytes,Is.Zero);
            TestContext.WriteLine("DRAG_UPDATE 200 iterations GC="+bytes+" allocation calls; elapsedMs="+(ticks*1000d/System.Diagnostics.Stopwatch.Frequency)+"; 8-vertex synthetic only");
        }

        /// <summary>一時Mesh消失による描画例外後も確定Meshへ復帰し、文書は変わりません。</summary>
        [Test]
        public void TemporaryResourceLost_RestoresRendererAndDocument()
        {
            using var f=new VlatKeyformPreviewFixture(); Prepare(f);
            var committed=f.Renderer.sharedMesh; var content=f.Session.ContentFingerprint;
            var pixels=VlatKeyformPreviewFixture.Pixels(f.Preview,VlatPreviewView.Main);
            Assert.That(f.Preview.BeginKeyformDrag(Start),Is.True);
            f.Preview.UpdateKeyformDrag(Start+Vector2.one*15);
            Object.DestroyImmediate(f.Preview.DragMesh);
            f.Preview.RenderIfNeeded();
            Assert.That(f.Preview.IsDragging,Is.False); Assert.That(f.Renderer.sharedMesh,Is.SameAs(committed));
            Assert.That(f.Session.ContentFingerprint,Is.EqualTo(content));
            CollectionAssert.AreEqual(pixels,VlatKeyformPreviewFixture.Pixels(f.Preview,VlatPreviewView.Main));
        }

        private static string[] RecoveryFiles(string directory)
        {
            var files = Directory.GetFiles(Path.Combine(directory, "session")); Array.Sort(files);
            for (var i = 0; i < files.Length; i++) files[i] = Path.GetFileName(files[i]) + ":" + File.ReadAllText(files[i]);
            return files;
        }
    }
}
