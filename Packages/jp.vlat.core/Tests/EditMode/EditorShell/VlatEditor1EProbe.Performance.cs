using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using VLat.Editor.Authoring;
using Object = UnityEngine.Object;

namespace VLat.Tests.Edit
{
    public static partial class VlatEditor1EProbe
    {
        /// <summary>実Preview・Window操作の小中大Mesh測定を隔離プロジェクトで実行します。</summary>
        public static void Benchmark() => Start(Performance(), "benchmark");
        private static void BeforeWindowUndo() => Debug.Log("VLAT_1E native Undo completed, entering Window callbacks");
        private static void AfterWindowUndo() => Debug.Log("VLAT_1E Window Undo callbacks completed");

        // -vlat1eBreakdown 1 の診断専用。observerは事前確保配列へ加算するだけで割当しない。
        // 最終性能値・割当件数の採取には使わず、処理区間の内訳を別runで記録する。
        private static readonly long[] PhaseTicks=new long[(int)VlatEditorPhase.Count];
        private static readonly int[] PhaseCalls=new int[(int)VlatEditorPhase.Count];
        private static readonly Action<VlatEditorPhase,long> PhaseObserver=(phase,ticks)=>{PhaseTicks[(int)phase]+=ticks;PhaseCalls[(int)phase]++;};
        private static bool Breakdown => Arg("-vlat1eBreakdown")=="1";

        private sealed class Samples
        {
            private readonly List<double> times=new List<double>();
            private readonly List<long> bytes=new List<long>();
            private readonly double[] phaseMs=new double[(int)VlatEditorPhase.Count];
            private readonly int[] phaseCalls=new int[(int)VlatEditorPhase.Count];
            internal void Measure(Action action, string phase = null)
            {
                var ticksBefore=(long[])PhaseTicks.Clone();var callsBefore=(int[])PhaseCalls.Clone();
                MeasureCore(action,phase);
                for(var i=0;i<phaseMs.Length;i++){phaseMs[i]+=(PhaseTicks[i]-ticksBefore[i])*1000d/System.Diagnostics.Stopwatch.Frequency;phaseCalls[i]+=PhaseCalls[i]-callsBefore[i];}
            }
            private void MeasureCore(Action action, string phase)
            {
                if(phase!=null) {Report["currentPhase"]=phase;File.WriteAllText(Path.Combine(Output,"partial.json"),Report.ToString());Debug.Log("VLAT_1E "+phase);}
                using var allocations=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"GC.Alloc",1,
                    ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
                var began=System.Diagnostics.Stopwatch.GetTimestamp();
                action();var elapsed=System.Diagnostics.Stopwatch.GetTimestamp()-began;
                allocations.Stop();long allocated=0;for(var i=0;i<allocations.Count;i++)allocated+=allocations.GetSample(i).Count;
                times.Add(elapsed*1000d/System.Diagnostics.Stopwatch.Frequency);bytes.Add(allocated);
                if(phase!=null) {Report["lastOperation"]=new JObject { ["phase"]=phase,["milliseconds"]=times[times.Count-1],["allocationCalls"]=allocated };
                    File.AppendAllText(Path.Combine(Output,"operations.jsonl"),Report["lastOperation"].ToString(Newtonsoft.Json.Formatting.None)+"\n");}
            }
            internal JObject Result()
            {
                var sorted=times.ToArray();Array.Sort(sorted);var sortedBytes=bytes.ToArray();Array.Sort(sortedBytes);double sum=0;long gc=0;foreach(var t in times)sum+=t;foreach(var b in bytes)gc+=b;
                return new JObject { ["count"]=times.Count,["meanMs"]=sum/times.Count,["medianMs"]=(sorted[(sorted.Length-1)/2]+sorted[sorted.Length/2])*.5,
                    ["p95Ms"]=sorted[(int)Math.Ceiling(sorted.Length*.95)-1],["maxMs"]=sorted[sorted.Length-1],
                    ["meanManagedAllocations"]=gc/(double)bytes.Count,["medianManagedAllocations"]=(sortedBytes[(sortedBytes.Length-1)/2]+sortedBytes[sortedBytes.Length/2])*.5,
                    ["p95ManagedAllocations"]=sortedBytes[(int)Math.Ceiling(sortedBytes.Length*.95)-1],["maxManagedAllocations"]=sortedBytes[sortedBytes.Length-1],
                    ["rawMs"]=JArray.FromObject(times),["rawManagedAllocations"]=JArray.FromObject(bytes),["phaseBreakdown"]=PhaseResult() };
            }
            private JObject PhaseResult()
            {
                if(!Breakdown) return null;
                var result=new JObject();
                for(var i=0;i<phaseMs.Length;i++) if(phaseCalls[i]!=0)
                    result[((VlatEditorPhase)i).ToString()]=new JObject { ["meanInclusiveMsPerSample"]=phaseMs[i]/times.Count,["callsPerSample"]=phaseCalls[i]/(double)times.Count };
                return result;
            }
        }

        private static IEnumerator Performance()
        {
            VlatEditorProfiling.Observer=Breakdown?PhaseObserver:null;
            Report["phaseBreakdown"]=Breakdown?"DIAGNOSTIC: VlatEditorProfiling observer enabled; inclusive Stopwatch ms per VLat phase. Not the final timing run.":"disabled";
            var calibration=new Samples();object allocation=null;calibration.Measure(()=>allocation=new byte[4096]);GC.KeepAlive(allocation);
            Report["allocationCalibration"]=calibration.Result();Assert.That((double)Report["allocationCalibration"]["meanManagedAllocations"],Is.GreaterThanOrEqualTo(1));
            Report["allocationMethod"]="ProfilerRecorder GC.Alloc / current thread / SumAllSamplesInFrame / sample.Count allocation calls (not bytes); calibrated byte[4096]";
            var old=GC.GetAllocatedBytesForCurrentThread();allocation=new byte[4096];Report["dotNetCounterCalibrationBytes"]=GC.GetAllocatedBytesForCurrentThread()-old;GC.KeepAlive(allocation);
            var profiles=new JArray();Report["profiles"]=profiles;Report["warmupPreview"]=3;Report["warmupGestures"]=2;
            Report["renderTiming"]="CPU wall time including Camera.Render submission/wait; GPU execution time NOT RUN";
            var names=new[]{"small","medium","large"};var widths=new[]{64,256,512};var heights=new[]{32,128,256};
            var rendererCounts=new[]{1,3,6};var keys=new[]{3,5,9};var selectionPixels=new[]{2,20,60};
            if(Arg("-vlat1eProfile")=="custom") {names=new[]{"custom"};widths=new[]{IntArg("-vlat1eWidth",128)};heights=new[]{IntArg("-vlat1eHeight",64)};
                rendererCounts=new[]{IntArg("-vlat1eRenderers",2)};keys=new[]{IntArg("-vlat1eKeys",3)};selectionPixels=new[]{IntArg("-vlat1eSelectionPixels",10)};}
            for(var profile=0;profile<names.Length;profile++)
            {
                if(Arg("-vlat1eProfile","all")!="all" && Arg("-vlat1eProfile")!=names[profile]) continue;
                Collect();var entry=new JObject { ["name"]=names[profile],["beforeCreate"]=Memory() };profiles.Add(entry);
                Debug.Log("VLAT_1E profile begin "+names[profile]);
                using(var f=new VlatKeyformPreviewFixture(true))
                {
                    f.Preview.Dispose();f.Session.NewDocument(VlatCloseDecision.Discard);
                    ConfigureLargeFixture(f,widths[profile],heights[profile],rendererCounts[profile]);
                    if(Arg("-vlat1eTraceUndo")=="1")Undo.undoRedoPerformed+=BeforeWindowUndo;
                    var window=VlatEditorWindow.CreateForRecoveryDirectory(Path.Combine(f.DirectoryPath,"benchmark-window"));
                    if(Arg("-vlat1eTraceUndo")=="1")Undo.undoRedoPerformed+=AfterWindowUndo;
                    window.position=new Rect(30,30,1200,1000);window.Show();window.Focus();yield return null;
                    try
                    {
                        var p=window.PreviewController;var s=window.DocumentController;
                        Assert.That(s.AddKeyformGroup(),Is.True);Assert.That(s.BindKeyformTarget(0,f.Source.gameObject,f.SourceRenderer),Is.True);
                        for(var key=0;key<keys[profile];key++)
                        {
                            if(key>0)Assert.That(s.AddKeyform(0,key*90d/(keys[profile]-1)),Is.True);
                            Assert.That(s.ImportKeyformPositions(0,key,f.Source.gameObject,f.SourceRenderer,key%2),Is.True,s.LastMessage);
                        }
                        f.Adopt(p);window.CreateGUI();yield return null;
                        window.SelectKey(0,false);
                        for(var i=0;i<3;i++) {p.GetViewState((VlatPreviewView)i).SetFramingDistance(2.5f);p.ResizeView((VlatPreviewView)i,256,256,1);}
                        var render=new Samples();var angle=0;
                        Action renderAction=()=>{p.SetAngles(VlatPreviewView.Main,angle++%90,0);p.RenderIfNeeded();};
                        for(var i=0;i<3;i++)renderAction();
                        for(var i=0;i<IntArg("-vlat1eTrials",8);i++)render.Measure(renderAction);
                        p.SetAngles(VlatPreviewView.Main,0,0);p.RenderIfNeeded();
                        Snapshot(p,names[profile],Output);
                        window.rootVisualElement.Q<Toggle>("geometry-selection-toggle").value=true;
                        PrepareBenchSelection(p,selectionPixels[profile]);
                        window.rootVisualElement.Q<Toggle>("keyform-drag-toggle").value=true;
                        var weighted=0;foreach(var w in p.VertexSelection.Weights) if(w>0)weighted++;
                        entry["verticesPerRenderer"]=f.Mesh.vertexCount;entry["rendererCount"]=rendererCounts[profile];
                        entry["sharedSourceMeshCount"]=1;entry["keyCount"]=keys[profile];entry["selectedVertices"]=p.VertexSelection.SelectedCount;
                        entry["weightedVertices"]=weighted;entry["rtWidth"]=p.GetTexture(VlatPreviewView.Main).width;entry["rtHeight"]=p.GetTexture(VlatPreviewView.Main).height;
                        entry["ready"]=Memory();
                        var start=new Vector2(128,128);var offset=new Vector2(4,2);var step=0;
                        Action begin=()=>p.BeginKeyformDrag(start);
                        Action move=()=>p.UpdateKeyformDrag(start+new Vector2(++step%8,2));
                        Action commit=()=>p.CommitKeyformDrag(start+offset);
                        for(var i=0;i<2;i++){begin();move();p.CancelKeyformDrag();}
                        var down=new Samples();for(var i=0;i<IntArg("-vlat1eTrials",8);i++){down.Measure(begin);Assert.That(p.IsDragging,Is.True,p.DragStatus);p.CancelKeyformDrag();}
                        begin();Assert.That(p.IsDragging,Is.True,p.DragStatus);var meshId=p.DragMesh.GetInstanceID();entry["duringDrag"]=Memory();
                        var moves=new Samples();for(var i=0;i<64;i++)moves.Measure(move);
                        Assert.That(p.DragMesh.GetInstanceID(),Is.EqualTo(meshId));entry["sameMeshAcross64Moves"]=true;p.CancelKeyformDrag();
                        var up=new Samples();var undo=new Samples();var redo=new Samples();
                        Action undoAction=()=>Click(window,"undo-button");
                        Action redoAction=()=>Click(window,"redo-button");
                        for(var i=0;i<IntArg("-vlat1eTrials",8);i++)
                        {
                            begin();move();up.Measure(commit,names[profile]+" commit "+i);Assert.That(p.IsDragging,Is.False);
                            undo.Measure(undoAction,names[profile]+" undo "+i);redo.Measure(redoAction,names[profile]+" redo "+i);
                        }
                        // UI Toolkit経由のMouseDown/Move/UpはRefresh/描画も含む。同じ文書/Previewを使用する。
                        var uiDown=new Samples();var uiMove=new Samples();var uiUp=new Samples();
                        Action pointerDown=()=>VlatKeyformDragWindowTests.SendPointer(window,EventType.MouseDown,start);
                        Action pointerMove=()=>VlatKeyformDragWindowTests.SendPointer(window,EventType.MouseDrag,start+offset);
                        Action pointerUp=()=>VlatKeyformDragWindowTests.SendPointer(window,EventType.MouseUp,start+offset);
                        for(var i=0;i<IntArg("-vlat1eTrials",8);i++)
                        {
                            uiDown.Measure(pointerDown,names[profile]+" mouseDown "+i);Assert.That(p.IsDragging,Is.True,p.DragStatus);
                            uiMove.Measure(pointerMove,names[profile]+" mouseMove "+i);uiUp.Measure(pointerUp,names[profile]+" mouseUp "+i);Assert.That(p.IsDragging,Is.False);
                        }
                        entry["timings"]=new JObject { ["previewThreeViews"]=render.Result(),["begin"]=down.Result(),["moveNumericAndMesh"]=moves.Result(),
                            ["commit"]=up.Result(),["undoAndPreview"]=undo.Result(),["redoAndPreview"]=redo.Result(),
                            ["uiMouseDown"]=uiDown.Result(),["uiMouseMoveAndRender"]=uiMove.Result(),["uiMouseUpAndRefresh"]=uiUp.Result() };
                        Debug.Log("VLAT_1E timings complete "+names[profile]);File.WriteAllText(Path.Combine(Output,"partial.json"),Report.ToString());
                        var soak=new JArray();entry["soak"]=soak;var soakStart=System.Diagnostics.Stopwatch.StartNew();
                        Undo.ClearAll();Collect();soak.Add(new JObject { ["cycle"]=0,["memory"]=Memory() });
                        for(var i=0;i<IntArg("-vlat1eCycles",60);i++)
                        {
                            Report["currentPhase"]=names[profile]+" soak "+i;File.WriteAllText(Path.Combine(Output,"partial.json"),Report.ToString());
                            begin();Assert.That(p.IsDragging,Is.True,p.DragStatus);
                            for(var j=0;j<8;j++){move();p.RenderIfNeeded();}commit();undoAction();Undo.ClearAll();
                            if((i+1)%20==0){Collect();soak.Add(new JObject { ["cycle"]=i+1,["memory"]=Memory() });yield return null;}
                        }
                        entry["soakSeconds"]=soakStart.Elapsed.TotalSeconds;
                        var save=new Samples();var open=new Samples();var savedPath=Path.Combine(f.DirectoryPath,"benchmark.vlat.json");
                        Action saveAction=()=>s.SaveAs(savedPath);Action openAction=()=>s.Open(savedPath,VlatCloseDecision.Discard);
                        for(var i=0;i<IntArg("-vlat1eTrials",8);i++)save.Measure(saveAction,names[profile]+" save "+i);
                        var content=s.ContentFingerprint;for(var i=0;i<IntArg("-vlat1eTrials",8);i++)open.Measure(openAction,names[profile]+" open "+i);
                        Assert.That(s.ContentFingerprint,Is.EqualTo(content));entry["save"]=save.Result();entry["open"]=open.Result();
                        entry["beforeDispose"]=Memory();
                    }
                    finally {Undo.undoRedoPerformed-=BeforeWindowUndo;Undo.undoRedoPerformed-=AfterWindowUndo;window.DiscardChanges();window.Close();}
                }
                var unload=Resources.UnloadUnusedAssets();while(!unload.isDone)yield return null;Collect();entry["afterDispose"]=Memory();
                Assert.That((int)entry["afterDispose"]["vlatNamedMeshCount"],Is.Zero);Assert.That((int)entry["afterDispose"]["vlatNamedRenderTextureCount"],Is.Zero);
                File.WriteAllText(Path.Combine(Output,"partial.json"),Report.ToString());yield return null;
            }
        }

        private static void PrepareBenchSelection(VlatPreviewController p,int pixels)
        {
            p.SetVertexSelectionTarget(0);p.SetVertexVisibilityMode(VlatVertexVisibilityMode.SelectThrough);p.SetVertexSoftRadius(.035f);
            p.SetVertexSelectionTool(VlatVertexSelectionTool.Rectangle);p.RenderIfNeeded();
            p.BeginVertexSelection(new Vector2(128-pixels,128-pixels),VlatVertexSelectionOperation.Replace);
            p.EndVertexSelection(new Vector2(128+pixels,128+pixels));
            Assert.That(p.VertexSelection.SelectedCount,Is.GreaterThan(0),p.VertexSelectionDiagnostic);
        }

        internal static void ConfigureLargeFixture(VlatKeyformPreviewFixture f,int width,int height,int renderers)
        {
            Object.DestroyImmediate(f.Mesh);var count=width*height;
            var mesh=new Mesh {name="VLat1EGrid",indexFormat=IndexFormat.UInt32};f.Mesh=mesh;
            var vertices=new Vector3[count];var weights=new BoneWeight[count];var wide=new Vector3[count];var deep=new Vector3[count];var smile=new Vector3[count];
            for(var y=0;y<height;y++)for(var x=0;x<width;x++)
            {
                var i=y*width+x;var yaw=x/(float)(width-1)*Mathf.PI*2;var v=y/(float)(height-1);var radius=Mathf.Sin(v*Mathf.PI);
                vertices[i]=new Vector3(Mathf.Sin(yaw)*radius*.4f,(v-.5f)*.9f,Mathf.Cos(yaw)*radius*.3f);
                weights[i]=new BoneWeight {boneIndex0=0,boneIndex1=1,weight0=.2f+.6f*v,weight1=.8f-.6f*v};
                if(y>height/3 && y<height*2/3 && x<width/5) {wide[i]=Vector3.right*.015f;deep[i]=Vector3.forward*.02f;smile[i]=Vector3.up*.01f;}
            }
            var triangles=new int[(width-1)*(height-1)*6];var at=0;
            for(var y=0;y<height-1;y++)for(var x=0;x<width-1;x++) {var i=y*width+x;triangles[at++]=i;triangles[at++]=i+width;triangles[at++]=i+1;triangles[at++]=i+1;triangles[at++]=i+width;triangles[at++]=i+width+1;}
            mesh.vertices=vertices;mesh.triangles=triangles;mesh.boneWeights=weights;mesh.bindposes=new[]{Matrix4x4.identity,Matrix4x4.identity};
            mesh.AddBlendShapeFrame("Wide",100,wide,null,null);mesh.AddBlendShapeFrame("Deep",100,deep,null,null);mesh.AddBlendShapeFrame("Smile",100,smile,null,null);
            mesh.RecalculateNormals();mesh.RecalculateBounds();var head=f.Source.transform.Find("Head");var bone=new GameObject("SecondBone").transform;bone.SetParent(head,false);bone.localRotation=Quaternion.Euler(7,12,3);
            f.SourceRenderer.sharedMesh=mesh;f.SourceRenderer.bones=new[]{head,bone};f.SourceRenderer.localBounds=mesh.bounds;
            for(var i=1;i<renderers;i++) {var go=new GameObject("OtherRenderer"+i);go.transform.SetParent(head,false);go.transform.localPosition=new Vector3(i*1.2f,0,0);
                var r=go.AddComponent<SkinnedMeshRenderer>();r.sharedMesh=mesh;r.sharedMaterial=f.Material;r.bones=new[]{head,bone};r.rootBone=head;r.localBounds=mesh.bounds;}
        }
    }
}
