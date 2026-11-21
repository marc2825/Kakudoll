using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UIElements;
using VLat.Editor.Authoring;
using Object = UnityEngine.Object;

namespace VLat.Tests.Edit
{
    /// <summary>専用隔離プロジェクトで明示実行する、実VRM E2E・性能測定Probeです。</summary>
    public static partial class VlatEditor1EProbe
    {
        private static readonly Stack<IEnumerator> Routines = new Stack<IEnumerator>();
        private static readonly List<string> Errors = new List<string>();
        private static string Output;
        private static JObject Report;
        private static string Arg(string name, string fallback = null)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length-1; i++) if (args[i] == name) return args[i+1];
            return fallback;
        }
        private static int IntArg(string name, int fallback) => int.Parse(Arg(name, fallback.ToString()));
        private static void Start(IEnumerator routine, string mode)
        {
            Output = Arg("-vlat1eOutput");
            if (string.IsNullOrEmpty(Output)) throw new ArgumentException("隔離出力先 -vlat1eOutput が必要です。");
            Directory.CreateDirectory(Output);
            Report = new JObject
            {
                ["mode"] = mode, ["status"] = "RUNNING", ["utc"] = DateTime.UtcNow.ToString("O"),
                ["unity"] = Application.unityVersion, ["os"] = SystemInfo.operatingSystem,
                ["cpu"] = SystemInfo.processorType, ["logicalCores"] = SystemInfo.processorCount,
                ["systemMemoryMB"] = SystemInfo.systemMemorySize, ["gpu"] = SystemInfo.graphicsDeviceName,
                ["graphicsAPI"] = SystemInfo.graphicsDeviceType.ToString(), ["graphicsMemoryMB"] = SystemInfo.graphicsMemorySize,
                ["profilerEnabled"] = Profiler.enabled, ["batchMode"] = Application.isBatchMode,
                ["process"] = System.Diagnostics.Process.GetCurrentProcess().Id,
                ["domain"] = Guid.NewGuid().ToString("N"), ["sourceAssetRedistributed"] = false,
            };
            File.WriteAllText(Path.Combine(Output,"partial.json"),Report.ToString());
            Application.logMessageReceived += OnLog;
            Routines.Push(routine); EditorApplication.update += Tick;
        }
        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Errors.Add(message);
        }
        private static void Tick()
        {
            try
            {
                if (Routines.Count == 0)
                {
                    Assert.That(Errors, Is.Empty, "Unity errors");
                    Finish("PASS", 0); return;
                }
                var routine = Routines.Peek();
                if (!routine.MoveNext()) { Routines.Pop(); return; }
                if (routine.Current is IEnumerator nested) Routines.Push(nested);
            }
            catch (Exception exception)
            {
                Report["failure"] = exception.ToString(); Finish("FAIL", 1);
            }
        }
        private static void Finish(string status, int code)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog;
            Report["status"] = status; Report["errors"] = JArray.FromObject(Errors);
            File.WriteAllText(Path.Combine(Output,"result.json"), Report.ToString());
            Debug.Log("VLAT_1E " + status + " " + Output);
            EditorApplication.Exit(code);
        }
        private static void Click(VlatEditorWindow window, string id)
        {
            var button = window.rootVisualElement.Q<Button>(id); Assert.That(button, Is.Not.Null, id);
            Assert.That(button.enabledInHierarchy, Is.True, id);
            using var evt = NavigationSubmitEvent.GetPooled(); evt.target = button; button.SendEvent(evt);
        }
        private static void Views(VlatPreviewController p)
        {
            for (var i=0;i<3;i++)
            {
                p.ResetView((VlatPreviewView)i); p.Zoom((VlatPreviewView)i,-18);
                p.SetAngles((VlatPreviewView)i,i*45,0); p.ResizeView((VlatPreviewView)i,512,512,1);
            }
            p.RenderIfNeeded();
        }
        private static string FileHash(string path)
        {
            using var sha=SHA256.Create(); using var stream=File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        }
        private static JObject Memory()
        {
            long meshBytes=0, rtBytes=0; var ownedMeshes=0;var ownedTargets=0;var meshes=Resources.FindObjectsOfTypeAll<Mesh>();
            var targets=Resources.FindObjectsOfTypeAll<RenderTexture>();
            foreach(var mesh in meshes) {meshBytes+=Profiler.GetRuntimeMemorySizeLong(mesh);if(mesh.name.Contains("VLat"))ownedMeshes++;}
            foreach(var target in targets) {rtBytes+=Profiler.GetRuntimeMemorySizeLong(target);if(target.name.Contains("VLat"))ownedTargets++;}
            return new JObject { ["managedBytes"]=GC.GetTotalMemory(false), ["unityAllocatedBytes"]=Profiler.GetTotalAllocatedMemoryLong(),
                ["unityReservedBytes"]=Profiler.GetTotalReservedMemoryLong(),["meshCount"]=meshes.Length,["meshBytes"]=meshBytes,
                ["renderTextureCount"]=targets.Length,["renderTextureBytes"]=rtBytes,
                ["vlatNamedMeshCount"]=ownedMeshes,["vlatNamedRenderTextureCount"]=ownedTargets,
                ["processPrivateBytes"]=null,["processPrivateBytesStatus"]="NOT RUN: Mono Process API returned unsupported zero; use OS profiler for this metric" };
        }
        private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        private static void Snapshot(VlatPreviewController p, string prefix, string directory)
        {
            Directory.CreateDirectory(directory); p.RenderIfNeeded();
            for(var i=0;i<3;i++) VlatKeyformPreviewFixture.Pixels(p,(VlatPreviewView)i,Path.Combine(directory,prefix+"-"+i+".png"));
            var cameras=new JArray();for(var i=0;i<3;i++)
            { var c=p.GetCamera((VlatPreviewView)i);cameras.Add(new JObject { ["view"]=i,["position"]=c.transform.position.ToString("R"),["rotation"]=c.transform.rotation.ToString("R"),
                ["distance"]=p.GetViewState((VlatPreviewView)i).Distance,["yaw"]=p.GetEvaluatedYaw((VlatPreviewView)i),["fieldOfView"]=c.fieldOfView,["width"]=c.pixelWidth,["height"]=c.pixelHeight }); }
            File.WriteAllText(Path.Combine(directory,prefix+"-state.json"),new JObject { ["cameras"]=cameras,["enabled"]=p.ApplicationEnabled,["geometry"]=p.GeometrySelectionEnabled,["dragMode"]=p.KeyformDragEnabled,["diagnostic"]=p.DiagnosticSummary }.ToString());
        }
        private static JArray Compare(VlatPreviewController p, string prefix, string directory)
        {
            var results=new JArray();
            for(var i=0;i<3;i++)
            {
                var expected=new Texture2D(2,2,TextureFormat.RGBA32,false);
                try
                {
                    Assert.That(expected.LoadImage(File.ReadAllBytes(Path.Combine(directory,prefix+"-"+i+".png"))),Is.True);
                    var before=expected.GetPixels32(); var now=VlatKeyformPreviewFixture.Pixels(p,(VlatPreviewView)i);
                    Assert.That(now.Length,Is.EqualTo(before.Length)); var max=0;var changed=0;var foreground=0;
                    for(var k=0;k<now.Length;k++)
                    {
                        var error=Math.Max(Math.Abs(now[k].r-before[k].r),Math.Max(Math.Abs(now[k].g-before[k].g),Math.Abs(now[k].b-before[k].b)));
                        max=Math.Max(max,Math.Max(error,Math.Abs(now[k].a-before[k].a))); if(error>1) changed++;
                        if(now[k].r>60 || now[k].g>60 || now[k].b>60) foreground++;
                    }
                    results.Add(new JObject { ["view"]=i,["maxChannelError"]=max,["changedPixelsAbove1"]=changed,["foregroundPixels"]=foreground });
                    File.WriteAllText(Path.Combine(Output,prefix+"-comparison.json"),results.ToString());
                    Assert.That(max,Is.LessThanOrEqualTo(1),prefix+" view "+i); Assert.That(foreground,Is.GreaterThan(100));
                }
                finally { Object.DestroyImmediate(expected); }
            }
            return results;
        }
    }
}
