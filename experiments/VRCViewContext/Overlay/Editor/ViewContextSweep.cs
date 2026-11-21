using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using System.Collections.Generic;
using Newtonsoft.Json;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace VLat.HeadFrameExperiment
{
    /// <summary>隔離SDK projectで、View Contextの実カメラ・明示的模擬入力をEdit/Play Modeで比較します。</summary>
    [InitializeOnLoad]
    public static class ViewContextSweep
    {
        private const string ActiveKey = "VLat.ViewContextSweep.Active";
        private const string OutputKey = "VLat.ViewContextSweep.Output";
        private const string CompletedKey = "VLat.ViewContextSweep.Completed";
        private const string FailureKey = "VLat.ViewContextSweep.Failure";
        private const string PhaseKey = "VLat.ViewContextSweep.Phase";
        private static IEnumerator routine;
        private static bool play;
        private static readonly int[] Methods = { 2, 3 };
        private static readonly int[] Poses = { 0, 1, 3, 4 };
        private static readonly float[] Yaws = { -90, -45, 0, 45, 90, 45, -45 };
        private static readonly float[] Pitches = { 0, 0, 0, 0, 0, 30, -30 };
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        static ViewContextSweep()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            // SDK initialization can change API/define settings AFTER executeMethod and reload assemblies.
            // SessionState survives that reload; restart the incomplete phase in the isolated project.
            if (SessionState.GetBool(ActiveKey, false) && SessionState.GetString(PhaseKey, "") == "EditMode")
                EditorApplication.delayCall += ResumeEditPhase;
        }

        /// <summary>新規の隔離project専用batch実験を開始します。</summary>
        /// <remarks>既存のユーザーprojectでは実行しないでください。-quitを指定せずrunnerへ終了を委譲します。</remarks>
        public static void RunBatch()
        {
            if (!Application.isBatchMode || EditorApplication.isPlaying)
                throw new InvalidOperationException("隔離projectのEdit Mode batch実行専用です。");
            string path = Path.GetFullPath("results-view");
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i)
                if (args[i] == "-headFrameOutput") path = Path.GetFullPath(args[i + 1]);
            if (!File.Exists("view-experiment-inputs.json") || Application.unityVersion != "2022.3.22f1")
                throw new InvalidOperationException("新規22f1 S2 staging専用です。");
            if (Directory.Exists(path)) throw new InvalidOperationException("新規の結果出力先を指定してください。");
            Directory.CreateDirectory(path);
            SessionState.SetString(OutputKey, path);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(CompletedKey, false);
            SessionState.SetBool(FailureKey, false);
            string inputs = Path.GetFullPath("sdk-experiment-inputs.json");
            File.Copy(inputs, Path.Combine(path, "sdk-inputs.json"), false);
            File.Copy("view-experiment-inputs.json", Path.Combine(path, "inputs.json"), false);
            File.WriteAllText(Path.Combine(path, "environment.txt"),
                "UTC=" + DateTime.UtcNow.ToString("O", Invariant) + "\nUnity=" + Application.unityVersion +
                "\nGPU=" + SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType +
                "\nPlayerSettings.gpuSkinning=" + PlayerSettings.gpuSkinning +
                "\nXR.enabled=" + UnityEngine.XR.XRSettings.enabled + "\nXR.device=" + UnityEngine.XR.XRSettings.loadedDeviceName +
                "\nVRChat client / actual XR stereo binding / real Mirror / Head Chop=NOT RUN\n");
            StartPhase(false);
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
                EditorApplication.delayCall += StartPlayPhase;
            else if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(CompletedKey, false))
            {
                SessionState.SetBool(ActiveKey, false);
                EditorApplication.Exit(SessionState.GetBool(FailureKey, false) ? 1 : 0);
            }
        }

        private static void StartPlayPhase() => StartPhase(true);

        private static void ResumeEditPhase()
        {
            if (!SessionState.GetBool(ActiveKey, false) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += ResumeEditPhase;
                return;
            }
            if (routine == null) StartPhase(false);
        }

        private static void StartPhase(bool isPlay)
        {
            if (routine != null) return;
            play = isPlay;
            SessionState.SetString(PhaseKey, isPlay ? "PlayMode" : "EditMode");
            routine = Measure();
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            try
            {
                if (routine.MoveNext()) return;
                StopRoutine();
                if (!play)
                {
                    SessionState.SetString(PhaseKey, "EnteringPlayMode");
                    EditorApplication.EnterPlaymode();
                }
                else
                {
                    SessionState.SetBool(CompletedKey, true);
                    EditorApplication.ExitPlaymode();
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                File.WriteAllText(Path.Combine(SessionState.GetString(OutputKey, "results-view"), "HARNESS_FAILED.txt"), ex.ToString());
                StopRoutine();
                SessionState.SetBool(FailureKey, true);
                SessionState.SetBool(CompletedKey, true);
                if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
                else { SessionState.SetBool(ActiveKey, false); EditorApplication.Exit(1); }
            }
        }

        private static void StopRoutine()
        {
            EditorApplication.update -= Tick;
            (routine as IDisposable)?.Dispose();
            routine = null;
        }

        private static IEnumerator WaitForUpdates()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (!play) { yield return null; yield return null; yield return null; }
            else
            {
                int until = Time.frameCount + 3;
                double deadline = EditorApplication.timeSinceStartup + 30;
                while (Time.frameCount < until)
                {
                    if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Play Modeのframeが進みません。");
                    yield return null;
                }
            }
        }

        private static IEnumerator Measure()
        {
            var scene = play ? UnityEngine.SceneManagement.SceneManager.CreateScene("ViewContextPlayMode")
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string output = SessionState.GetString(OutputKey, "results-view");
            StringBuilder rows = new StringBuilder("phase,axes,method,pose,world_yaw,world_pitch,scenario,eye,evidence,negative,mirror_mode,camera_mode,face_mode,stereo,ortho,require_stereo,direction_policy,draw_x,draw_y,draw_z,left_x,left_y,left_z,right_x,right_y,right_z,mirror_x,mirror_y,mirror_z,direction_x,direction_y,direction_z,expected_context,expected_source,expected_status,expected_warnings,expected_shared,expected_directional,gpu_context,gpu_source,gpu_status,gpu_warnings,gpu_shared,gpu_directional,expected_x,expected_y,expected_z,gpu_x,gpu_y,gpu_z,cpu_yaw,cpu_pitch,gpu_yaw,gpu_pitch,cpu_weight,vertex_weight,fragment_weight,vertex_status,yaw_error,pitch_error,position_error_m,weight_error,status\n");
            StringBuilder frames = new StringBuilder("phase,axes,pose,x,y,z,qx,qy,qz,qw\n");
            List<object> images = new List<object>();
            using (HeadFrameCapture capture = new HeadFrameCapture())
            {
                for (int axes = 0; axes < 2; ++axes)
                {
                    using (HeadFrameFixture f = new HeadFrameFixture(scene, axes == 1))
                    {
                        ParentConstraint unity = f.ConstrainedProxy.GetComponent<ParentConstraint>();
                        UnityEngine.Object.DestroyImmediate(unity);
                        VRCParentConstraint vrc = f.ConstrainedProxy.gameObject.AddComponent<VRCParentConstraint>();
                        vrc.Sources.Add(new VRCConstraintSource(f.Canonical, 1));
                        vrc.GlobalWeight = 1; vrc.IsActive = true; vrc.Locked = true; vrc.SolveInLocalSpace = false; vrc.ApplyConfigurationChanges();
                        GameObject secondObject = new GameObject("IndependentCameraB");
                        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(secondObject, scene); f.Owned.Add(secondObject);
                        Camera second = secondObject.AddComponent<Camera>(); second.CopyFrom(f.Camera);
                        second.overrideSceneCullingMask = f.Camera.overrideSceneCullingMask;
                        Material[] live = new Material[Methods.Length], injected = new Material[Methods.Length];
                        IEnumerator wait = WaitForUpdates(); while (wait.MoveNext()) yield return wait.Current;
                        for (int index = 0; index < Methods.Length; ++index)
                        {
                            Matrix4x4 calibration = f.Canonical.worldToLocalMatrix * capture.ObjectMatrix(f, Methods[index]);
                            live[index] = CreateMaterial("ViewContextDiagnostic", calibration, f);
                            injected[index] = CreateMaterial("ViewContextInjected", calibration, f);
                        }
                        for (int poseIndex = 0; poseIndex < Poses.Length; ++poseIndex)
                        {
                            int pose = Poses[poseIndex]; f.SetPose(pose);
                            wait = WaitForUpdates(); while (wait.MoveNext()) yield return wait.Current;
                            Quaternion q = f.Canonical.rotation; Vector3 head = f.Canonical.position;
                            Append(frames, Phase, axes, HeadFrameFixture.Poses[pose], head.x,head.y,head.z,q.x,q.y,q.z,q.w);
                            for (int methodIndex = 0; methodIndex < Methods.Length; ++methodIndex)
                            {
                                int method = Methods[methodIndex];
                                f.Show(method);
                                for (int view = 0; view < Yaws.Length; ++view)
                                for (int scenario = 0; scenario < ViewContextCase.Names.Length; ++scenario)
                                for (int eye = 0; eye < 2; ++eye)
                                {
                                    ViewContextCase c = ViewContextCase.Build(scenario, f, Yaws[view], Pitches[view], eye);
                                    Camera camera = eye == 0 ? f.Camera : second;
                                    // Native tests use real Unity camera bindings. Injected tests retain a normal raster camera.
                                    Vector3 raster = c.Native ? c.Draw : c.Status == 0 ? c.Draw : f.Camera.transform.position;
                                    if(!c.Native && c.MirrorMode>0 && c.Status==0)
                                        raster=c.ExpectedPosition+(eye==0?-1:1)*f.Camera.transform.right*0.032f;
                                    Vector3 look = c.Native && c.Ortho>0 ? -c.Direction : f.Canonical.position-raster;
                                    camera.transform.SetPositionAndRotation(raster, Quaternion.LookRotation(look, Vector3.up));
                                    camera.orthographic = c.Native && c.Ortho > 0; camera.orthographicSize = 0.85f;
                                    Material material = c.Native ? live[methodIndex] : injected[methodIndex];
                                    material.SetFloat("_AsymmetricProbe", 0); c.Apply(material);
                                    f.Renderers[method].sharedMaterial = material;
                                    Color angle = Sample(capture, camera, f.Renderers[method], 1);
                                    Color meta = Sample(capture, camera, f.Renderers[method], 3);
                                    Color flags = Sample(capture, camera, f.Renderers[method], 2);
                                    Color vertex = Sample(capture, camera, f.Renderers[method], 9);
                                    Vector3 position = c.Status == 0 ? ToVector(Sample(capture, camera, f.Renderers[method], 5)) : Vector3.zero;
                                    Vector2 actual = new Vector2((angle.r-0.5f)*360, (angle.g-0.5f)*180);
                                    float yawError = Mathf.Abs(Mathf.DeltaAngle(c.Angles.x,actual.x)), pitchError = Mathf.Abs(c.Angles.y-actual.y);
                                    float positionError = c.Status == 0 ? Vector3.Distance(position,c.ExpectedPosition) : 0;
                                    float weightError = Mathf.Max(Mathf.Abs(vertex.r-c.Weight),Mathf.Abs(vertex.g-c.Weight));
                                    bool pass = yawError <= 0.05f && pitchError <= 0.05f && positionError <= 1e-5f && weightError <= 0.0001f &&
                                        Mathf.Abs(meta.r-c.Context)<0.01f && Mathf.Abs(meta.g-c.Source)<0.01f && Mathf.Abs(meta.b-c.Status)<0.01f &&
                                        Mathf.Abs(flags.r-c.Warnings)<0.01f && Mathf.Abs(flags.g-c.Shared)<0.01f && Mathf.Abs(flags.b-c.Directional)<0.01f &&
                                        Mathf.Abs(vertex.b-c.Status)<0.01f && (angle.b>0.5f)==(c.Status==0);
                                    Append(rows, Phase,axes,f.Names[method],HeadFrameFixture.Poses[pose],Yaws[view],Pitches[view],c.Name,eye,
                                        c.Native?"UnityCamera":"Injected",c.Negative?1:0,c.MirrorMode,c.CameraMode,c.FaceMode,c.Stereo,c.Ortho,c.Require,c.DirectionPolicy,
                                        c.Draw.x,c.Draw.y,c.Draw.z,c.Left.x,c.Left.y,c.Left.z,c.Right.x,c.Right.y,c.Right.z,c.Mirror.x,c.Mirror.y,c.Mirror.z,
                                        c.Direction.x,c.Direction.y,c.Direction.z,c.Context,c.Source,c.Status,c.Warnings,c.Shared,c.Directional,
                                        meta.r,meta.g,meta.b,flags.r,flags.g,flags.b,
                                        c.Status==0?(object)c.ExpectedPosition.x:"",c.Status==0?(object)c.ExpectedPosition.y:"",c.Status==0?(object)c.ExpectedPosition.z:"",
                                        c.Status==0?(object)position.x:"",c.Status==0?(object)position.y:"",c.Status==0?(object)position.z:"",
                                        c.Angles.x,c.Angles.y,actual.x,actual.y,c.Weight,vertex.r,vertex.g,vertex.b,yawError,pitchError,positionError,weightError,pass?"PASS":"FAIL");
                                    if (!play && axes==0 && method==2 && pose==0 && (view==1 || view==2 || view==3) &&
                                        (scenario==0 || scenario==1 || scenario==5 || scenario==6 || scenario==8 || scenario==13 || scenario==25))
                                    {
                                        material.SetFloat("_AsymmetricProbe",0.04f);
                                        string name = c.Name+"-yaw"+Yaws[view]+"-eye"+eye+".png";
                                        SaveImage(capture,camera,f.Renderers[method],Path.Combine(output,"images",name));
                                        images.Add(new {file="images/"+name,scenario=c.Name,eye,worldYaw=Yaws[view],evidence=c.Native?"UnityCamera":"Injected",
                                            mode="view color",displacement="right-side only; positive-yaw weight; 0.04 m toy probe"});
                                        string modeName=name.Replace(".png","-mode.png");
                                        SaveImage(capture,camera,f.Renderers[method],Path.Combine(output,"images",modeName),10);
                                        images.Add(new {file="images/"+modeName,scenario=c.Name,eye,worldYaw=Yaws[view],evidence=c.Native?"UnityCamera":"Injected",
                                            mode="context palette",displacement="right-side only; positive-yaw weight; 0.04 m toy probe"});
                                        material.SetFloat("_AsymmetricProbe",0);
                                    }
                                }
                            }
                            Debug.Log("View Context "+Phase+" axes="+axes+" pose="+HeadFrameFixture.Poses[pose]+" completed.");
                            yield return null;
                        }
                    }
                    File.WriteAllText(Path.Combine(output,Phase+"-views.csv"),rows.ToString());
                    File.WriteAllText(Path.Combine(output,Phase+"-frames.csv"),frames.ToString());
                }
            }
            if (!play) File.WriteAllText(Path.Combine(output,"images.json"),JsonConvert.SerializeObject(images,Formatting.Indented));
            Shader[] shaders={Shader.Find("VLat/Experiments/ViewContextDiagnostic"),Shader.Find("VLat/Experiments/ViewContextInjected"),
                Shader.Find("VLat/Experiments/ViewContextStereoCompile"),Shader.Find("VLat/Experiments/ViewContextInstancedCompile")};
            List<object> messages = new List<object>();
            bool compilationFailed = false;
            foreach (Shader shader in shaders)
            {
                if(shader==null || !shader.isSupported) throw new InvalidOperationException("S2 compile probe Shader unavailable");
                ShaderVariantCollection variants=new ShaderVariantCollection();
                string[] keywords = shader.name.EndsWith("StereoCompile",StringComparison.Ordinal) ? new[]{"UNITY_SINGLE_PASS_STEREO"} :
                    shader.name.EndsWith("InstancedCompile",StringComparison.Ordinal) ? new[]{"STEREO_INSTANCING_ON"} : new string[0];
                try {variants.Add(new ShaderVariantCollection.ShaderVariant(shader,UnityEngine.Rendering.PassType.Normal,keywords));variants.WarmUp();}
                finally {UnityEngine.Object.DestroyImmediate(variants);}
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                {
                    messages.Add(new {shader=shader.name,message=message.message,severity=message.severity.ToString()});
                    if(message.severity.ToString()=="Error") compilationFailed=true;
                }
                Debug.Log("S2 Shader warmup completed (compile only): "+shader.name);
            }
            File.WriteAllText(Path.Combine(output,Phase+"-shader-messages.json"),JsonConvert.SerializeObject(messages,Formatting.Indented));
            if(compilationFailed) throw new InvalidOperationException("S2 compile-only variant failed; see shader-messages.json");
            Debug.Log("View Context "+Phase+" measurement completed.");
        }

        private static Material CreateMaterial(string shaderName,Matrix4x4 calibration,HeadFrameFixture owner)
        {
            Shader shader=Shader.Find("VLat/Experiments/"+shaderName);
            if (shader==null || !shader.isSupported) throw new InvalidOperationException("S2 Shader unavailable: "+shaderName);
            Material material=new Material(shader); HeadFrameMath.SetCalibration(material,calibration); owner.Owned.Add(material); return material;
        }
        private static Color Sample(HeadFrameCapture capture,Camera camera,SkinnedMeshRenderer renderer,int mode)
        {
            foreach(Color c in capture.Read(camera,renderer,mode)) if(c.a>0.5f) return c;
            throw new InvalidOperationException("S2 readback has no covered pixels, mode="+mode);
        }
        private static Vector3 ToVector(Color c)=>new Vector3(c.r,c.g,c.b);
        private static void SaveImage(HeadFrameCapture capture,Camera camera,SkinnedMeshRenderer renderer,string path,int mode=0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)); Texture2D image=new Texture2D(HeadFrameCapture.Size,HeadFrameCapture.Size,TextureFormat.RGB24,false);
            try {image.SetPixels(capture.Read(camera,renderer,mode));image.Apply(false,false);File.WriteAllBytes(path,image.EncodeToPNG());}
            finally {UnityEngine.Object.DestroyImmediate(image);}
        }
        private static string Phase=>play?"PlayMode":"EditMode";
        private static void Append(StringBuilder builder,params object[] values)
        {
            for(int i=0;i<values.Length;++i){if(i>0)builder.Append(',');builder.Append(Convert.ToString(values[i],Invariant));}builder.AppendLine();
        }
    }
}
