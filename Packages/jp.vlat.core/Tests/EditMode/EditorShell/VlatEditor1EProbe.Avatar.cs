using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VLat.Editor.Authoring;

namespace VLat.Tests.Edit
{
    public static partial class VlatEditor1EProbe
    {
        /// <summary>権利確認済みの実VRMを実Windowで編集し、次のEditorプロセスへ保存・Recoveryを渡します。</summary>
        public static void AvatarSeed() => Start(Avatar(false), "avatar-seed");
        /// <summary>別Editorプロセスで実VRMと文書を復元し、保存時の画素・内容・原本を比較します。</summary>
        public static void AvatarRestore() => Start(Avatar(true), "avatar-restore");

        private static IEnumerator Avatar(bool restore)
        {
            var stateDirectory = Arg("-vlat1eState"); Directory.CreateDirectory(stateDirectory);
            var statePath = Path.Combine(stateDirectory,"state.json");
            if (!restore) Assert.That(File.Exists(statePath), Is.False, "既存の測定状態を上書きしません。隔離Library/VLat1EAvatarを退避してください。");
            var path = Arg("-vlat1eAvatar"); var originalHash = FileHash(path);
            Assert.That(originalHash, Is.EqualTo(Arg("-vlat1eAvatarHash")));
            Report["avatarSHA256"] = originalHash; Report["credit"] = "Seed-san © VirtualCast, Inc.";
            // Binding/import用の原本と表示用cloneを別々のPreview Sceneで所有する。Scene/Prefab Assetへ書き戻さない。
            using var source = new VlatPreviewController(); var sourceLoad = source.LoadAvatarAsync(path);
            while (!sourceLoad.IsCompleted) yield return null;
            Assert.That(sourceLoad.Result, Is.True, source.DiagnosticSummary);
            var inventory = new JArray(); var targetIndex = -1; var shapeCount = 0;
            for (var i=0;i<source.VertexTargets.Count;i++)
            {
                var t=source.VertexTargets[i]; var mesh=t.SourceMesh;
                inventory.Add(new JObject { ["path"]=t.HierarchyPath,["vertices"]=mesh.vertexCount,
                    ["blendShapes"]=mesh.blendShapeCount,["bones"]=(t.Renderer as SkinnedMeshRenderer)?.bones.Length });
                if (mesh.blendShapeCount>shapeCount && t.Renderer is SkinnedMeshRenderer) { shapeCount=mesh.blendShapeCount;targetIndex=i; }
            }
            Assert.That(targetIndex, Is.GreaterThanOrEqualTo(0)); Report["renderers"]=inventory;
            var target=source.VertexTargets[targetIndex]; var sourceRenderer=(SkinnedMeshRenderer)target.Renderer;
            var sourceRoot=sourceRenderer.transform.root.gameObject; var sourceBefore=OriginalSignature(source);
            var window=VlatEditorWindow.CreateForRecoveryDirectory(Path.Combine(stateDirectory,"recovery"));
            window.position=new Rect(40,40,1200,1000); window.Show(); window.Focus(); yield return null;
            var p=window.PreviewController; var s=window.DocumentController;
            var load=p.LoadAvatarAsync(path); while(!load.IsCompleted) yield return null;
            Assert.That(load.Result,Is.True,p.DiagnosticSummary); window.CreateGUI(); yield return null;
            var savedPath=Path.Combine(stateDirectory,"Seed-san.vlat.json");
            if (!restore)
            {
                Click(window,"add-keyform-group-button");
                window.rootVisualElement.Q<ObjectField>("keyform-avatar-root").value=sourceRoot;
                window.rootVisualElement.Q<ObjectField>("keyform-renderer").value=sourceRenderer;
                Click(window,"bind-keyform-target-button");
                var shapes=new JArray();
                for(var key=0;key<3;key++)
                {
                    if(key>0) { Click(window,"add-keyform-button"); window.rootVisualElement.Q<DoubleField>("keyform-yaw").value=key*45; }
                    window.SelectKey(key,false);
                    window.rootVisualElement.Q<DropdownField>("keyform-shape").index=key+1;
                    Click(window,"import-keyform-button");
                    Assert.That(s.Document.KeyformGroups[0].Keys[key].Deltas.Count,Is.GreaterThan(0),s.LastMessage);
                    shapes.Add(target.SourceMesh.GetBlendShapeName(key));
                }
                Report["importedShapes"]=shapes;
                var edits=new JArray();
                for(var key=0;key<3;key++)
                {
                    window.SelectKey(key,false);
                    Click(window,"keyform-camera-Main"); yield return null;
                    yield return AvatarDrag(window,target.HierarchyPath,new Vector2(4+key,2),edits,true);
                }
                Report["drags"]=edits;
                p.SetGeometrySelectionEnabled(false); p.ResetExpressions(); Views(p);
                Snapshot(p,"saved",stateDirectory); Snapshot(p,"saved",Output);
                if(p.ExpressionNames.Count>0) { p.SetExpressionWeight(0,.7f);p.RenderIfNeeded();Snapshot(p,"expression",stateDirectory);Snapshot(p,"expression",Output);p.ResetExpressions();p.RenderIfNeeded(); }
                Report["expressions"]=JArray.FromObject(p.ExpressionNames);
                window.SaveDocumentPathProvider=()=>savedPath; Click(window,"save-as-button");
                Assert.That(s.IsDirty,Is.False,s.LastMessage); var saved=s.ContentFingerprint; var savedHash=FileHash(savedPath);
                window.SelectKey(0,false);
                yield return AvatarDrag(window,target.HierarchyPath,new Vector2(-3,2),edits,false);
                Assert.That(s.IsDirty,Is.True); var recovery=s.ContentFingerprint;
                p.SetGeometrySelectionEnabled(false); p.ResetExpressions(); Views(p);
                Snapshot(p,"recovery",stateDirectory); Snapshot(p,"recovery",Output);
                var state=new JObject { ["process"]=Report["process"],["domain"]=Report["domain"],
                    ["savedContent"]=saved,["recoveryContent"]=recovery,["savedHash"]=savedHash,["originalHash"]=originalHash,
                    ["target"]=target.HierarchyPath,["expressionCount"]=p.ExpressionNames.Count };
                File.WriteAllText(statePath,state.ToString());
                // 未確定ドラッグを残して実プロセス終了。OnDisableが確定済み文書だけをcheckpointする。
                yield return AvatarDrag(window,target.HierarchyPath,new Vector2(8,4),new JArray(),false,false);
                Assert.That(p.IsDragging,Is.True); Assert.That(s.ContentFingerprint,Is.EqualTo(recovery));
                Report["savedContent"]=saved; Report["recoveryContent"]=recovery;Report["pendingGestureAtExit"]=true;
            }
            else
            {
                var state=JObject.Parse(File.ReadAllText(statePath));
                Assert.That((int)Report["process"],Is.Not.EqualTo((int)state["process"]));
                Assert.That((string)Report["domain"],Is.Not.EqualTo((string)state["domain"]));
                Assert.That(s.ContentFingerprint,Is.EqualTo((string)state["recoveryContent"])); Assert.That(s.IsDirty,Is.True);
                Assert.That(p.IsDragging,Is.False); Assert.That(FileHash(savedPath),Is.EqualTo((string)state["savedHash"]));
                p.ResetExpressions();Views(p);Snapshot(p,"recovered",Output);Report["recoveryImages"]=Compare(p,"recovery",stateDirectory);
                Assert.That(s.Open(savedPath,VlatCloseDecision.Discard),Is.True);window.CreateGUI();yield return null;
                Assert.That(s.ContentFingerprint,Is.EqualTo((string)state["savedContent"])); Assert.That(s.IsDirty,Is.False);
                // OpenはDocument選択へ戻る仕様。グループは1つだけなので、Previewはファイル選択のままそのグループを表示する。
                p.ResetExpressions();Views(p);Snapshot(p,"reopened",Output);Report["savedImages"]=Compare(p,"saved",stateDirectory);
                if(p.ExpressionNames.Count>0) { p.SetExpressionWeight(0,.7f);p.RenderIfNeeded();Report["expressionImages"]=Compare(p,"expression",stateDirectory);p.ResetExpressions(); }
                Click(window,"remove-keyform-group-button");Assert.That(s.Document.KeyformGroups.Count,Is.Zero);
                Views(p); Snapshot(p,"removed",Output); Report["removeData"]= "PASS";
                window.DiscardChanges();window.Close();
            }
            Assert.That(OriginalSignature(source),Is.EqualTo(sourceBefore),"原本Mesh/shape/Material/weight");
            Assert.That(FileHash(path),Is.EqualTo(originalHash)); Report["originalUnchanged"]=true;
            Report["humanOperationFeel"]="NOT RUN";
        }

        private static IEnumerator AvatarDrag(VlatEditorWindow window,string hierarchy,Vector2 movement,JArray results,bool undo,bool commit=true)
        {
            var p=window.PreviewController;var s=window.DocumentController;
            p.SetGeometrySelectionEnabled(false); window.CreateGUI();yield return null;
            window.rootVisualElement.Q<Toggle>("geometry-selection-toggle").value=true;
            for(var i=0;i<p.VertexTargets.Count;i++) if(p.VertexTargets[i].HierarchyPath==hierarchy) p.SetVertexSelectionTarget(i);
            p.SetVertexSoftRadius(.025f);p.SetVertexVisibilityMode(VlatVertexVisibilityMode.VisibleOnly);p.RenderIfNeeded();
            // UIに収まる可視頂点を実ポインタで選択する。画像上の近傍検索は本番Selectionを通す。
            var texture=p.GetTexture(VlatPreviewView.Main); var chosen=-1;var nearest=float.PositiveInfinity;
            for(var i=0;i<p.VertexSelection.ScreenPositions.Count;i++)
            {
                var v=p.VertexSelection.ScreenPositions[i];if(!p.VertexSelection.Visibility[i] || v.z<=0 || v.x<10 || v.x>texture.width-10 || v.y<10 || v.y>texture.height-10) continue;
                var d=Vector2.SqrMagnitude((Vector2)v-new Vector2(texture.width*.5f,texture.height*.5f));
                if(d<nearest) {nearest=d;chosen=i;}
            }
            Assert.That(chosen,Is.GreaterThanOrEqualTo(0),p.VertexSelectionDiagnostic);
            var start=(Vector2)p.VertexSelection.ScreenPositions[chosen];
            VlatKeyformDragWindowTests.SendPointer(window,EventType.MouseDown,start);
            VlatKeyformDragWindowTests.SendPointer(window,EventType.MouseUp,start);
            Assert.That(p.VertexSelection.SelectedCount,Is.GreaterThan(0));var weighted=0;
            foreach(var w in p.VertexSelection.Weights) if(w>0) weighted++;
            window.rootVisualElement.Q<Toggle>("keyform-drag-toggle").value=true;
            var before=s.ContentFingerprint;Undo.ClearAll();
            VlatKeyformDragWindowTests.SendPointer(window,EventType.MouseDown,start);Assert.That(p.IsDragging,Is.True,p.DragStatus);
            VlatKeyformDragWindowTests.SendPointer(window,EventType.MouseDrag,start+movement*.5f);
            VlatKeyformDragWindowTests.SendPointer(window,EventType.MouseDrag,start+movement);
            Assert.That(s.ContentFingerprint,Is.EqualTo(before));
            if(!commit) yield break;
            VlatKeyformDragWindowTests.SendPointer(window,EventType.MouseUp,start+movement);
            Assert.That(p.IsDragging,Is.False);var after=s.ContentFingerprint;Assert.That(after,Is.Not.EqualTo(before),p.DragStatus);
            if(undo) {Click(window,"undo-button");Assert.That(s.ContentFingerprint,Is.EqualTo(before));Click(window,"redo-button");Assert.That(s.ContentFingerprint,Is.EqualTo(after));}
            results.Add(new JObject { ["selected"]=p.VertexSelection.SelectedCount,["weighted"]=weighted,["movementPixels"]=movement.ToString(),["before"]=before,["after"]=after,["undoRedo"]=undo });
        }

        private static string OriginalSignature(VlatPreviewController source)
        {
            // 測定外の原本証跡。大きなJToken木を作らずstreamで全shapeをhash化する。
            using var sha=System.Security.Cryptography.SHA256.Create();
            using var stream=new System.Security.Cryptography.CryptoStream(Stream.Null,sha,System.Security.Cryptography.CryptoStreamMode.Write);
            using var writer=new BinaryWriter(stream);
            foreach(var t in source.VertexTargets)
            {
                var m=t.SourceMesh;writer.Write(t.HierarchyPath);writer.Write(m.GetInstanceID());
                WriteVectors(writer,m.vertices);foreach(var index in m.triangles) writer.Write(index);
                foreach(var pose in m.bindposes) for(var i=0;i<16;i++) writer.Write(pose[i]);
                foreach(var w in m.boneWeights) {writer.Write(w.boneIndex0);writer.Write(w.boneIndex1);writer.Write(w.boneIndex2);writer.Write(w.boneIndex3);writer.Write(w.weight0);writer.Write(w.weight1);writer.Write(w.weight2);writer.Write(w.weight3);}
                var v=new Vector3[m.vertexCount];var n=new Vector3[m.vertexCount];var tan=new Vector3[m.vertexCount];
                for(var i=0;i<m.blendShapeCount;i++)
                {
                    writer.Write(m.GetBlendShapeName(i));writer.Write((t.Renderer as SkinnedMeshRenderer)?.GetBlendShapeWeight(i) ?? 0);
                    for(var j=0;j<m.GetBlendShapeFrameCount(i);j++)
                    {m.GetBlendShapeFrameVertices(i,j,v,n,tan);writer.Write(m.GetBlendShapeFrameWeight(i,j));WriteVectors(writer,v);WriteVectors(writer,n);WriteVectors(writer,tan);}
                }
                foreach(var material in t.Renderer.sharedMaterials) writer.Write(EditorJsonUtility.ToJson(material));
            }
            writer.Flush();stream.FlushFinalBlock();return Convert.ToBase64String(sha.Hash);
        }
        private static void WriteVectors(BinaryWriter writer,Vector3[] values)
        {foreach(var v in values) {writer.Write(v.x);writer.Write(v.y);writer.Write(v.z);}}

    }
}
