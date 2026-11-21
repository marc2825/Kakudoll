using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VLat.HeadFrameExperiment
{
    /// <summary>複製した実FBXで、Head Frameと元skin形状をEdit/Play Modeで比較します。</summary>
    [InitializeOnLoad]
    public static class RealAvatarSweep
    {
        private const string ActiveKey = "VLat.RealAvatarSweep.Active";
        private const string OutputKey = "VLat.RealAvatarSweep.Output";
        private const string CompletedKey = "VLat.RealAvatarSweep.Completed";
        private const string FailureKey = "VLat.RealAvatarSweep.Failure";
        private const string PhaseKey = "VLat.RealAvatarSweep.Phase";
        private static IEnumerator routine;
        private static bool play;
        private static readonly float[] Yaws = { -90, -45, 0, 45, 90, 45, -45 };
        private static readonly float[] Pitches = { 0, 0, 0, 0, 0, 30, -30 };
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        static RealAvatarSweep()
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
            if (!Application.isBatchMode || EditorApplication.isPlaying || !File.Exists("avatar-inputs.json"))
                throw new InvalidOperationException("隔離projectのEdit Mode batch実行専用です。");
            if (Application.unityVersion != "2022.3.22f1") throw new InvalidOperationException("この検証は2022.3.22f1へ固定しています。");
            string path = Path.GetFullPath("results-avatars");
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i)
                if (args[i] == "-headFrameOutput") path = Path.GetFullPath(args[i + 1]);
            if (Directory.Exists(path)) throw new InvalidOperationException("測定結果の上書きを拒否します。新規出力先を指定してください。");
            Directory.CreateDirectory(path);
            SessionState.SetString(OutputKey, path);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(CompletedKey, false);
            SessionState.SetBool(FailureKey, false);
            JObject inputs = JObject.Parse(File.ReadAllText("avatar-inputs.json"));
            foreach (JObject model in (JArray)inputs["models"]) model.Remove("sourcePath");
            File.WriteAllText(Path.Combine(path, "inputs.json"), inputs.ToString());
            File.Copy("sdk-experiment-inputs.json", Path.Combine(path, "sdk-inputs.json"), false);
            File.WriteAllText(Path.Combine(path, "environment.txt"),
                "UTC=" + DateTime.UtcNow.ToString("O", Invariant) + "\nUnity=" + Application.unityVersion +
                "\nGPU=" + SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType +
                "\nPlayerSettings.gpuSkinning=" + PlayerSettings.gpuSkinning +
                "\nVRChat client / actual Player / Stereo / Mirror / Head Chop=NOT RUN\n");
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
                File.WriteAllText(Path.Combine(SessionState.GetString(OutputKey, "results-avatars"), "HARNESS_FAILED.txt"), ex.ToString());
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
            var scene = play
                ? UnityEngine.SceneManagement.SceneManager.CreateScene("RealAvatarHeadFramePlayMode")
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            JArray models = (JArray)JObject.Parse(File.ReadAllText("avatar-inputs.json"))["models"];
            List<object> setup = new List<object>();
            List<object> audits = new List<object>();
            StringBuilder rows = new StringBuilder("phase,avatar,pose,method,world_yaw,world_pitch,cpu_yaw,cpu_pitch,gpu_yaw,gpu_pitch,raw_yaw,raw_pitch,yaw_error,pitch_error,proxy_position_error_m,proxy_rotation_error_deg,shader_root_rigid_error,coverage_mismatch,world_error_m,covered_pixels,status\n");
            StringBuilder vertexRows = new StringBuilder("phase,avatar,pose,method,vertex_count,vertex_world_error_m,status\n");
            string output = SessionState.GetString(OutputKey, "results-avatars");
            using (HeadFrameCapture capture = new HeadFrameCapture())
            using (RealAvatarVertexProbe vertexProbe = new RealAvatarVertexProbe())
            {
                for (int model = 0; model < models.Count; ++model)
                {
                    string assetPath = (string)models[model]["assetPath"], avatar = (string)models[model]["name"];
                    if (!play) RealAvatarAudit.ConfigureImport(assetPath);
                    GameObject auditInstance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(assetPath));
                    try { audits.Add(RealAvatarAudit.Inspect(avatar, auditInstance, assetPath)); }
                    finally { UnityEngine.Object.DestroyImmediate(auditInstance); }
                    using (RealAvatarFixture f = new RealAvatarFixture(scene, assetPath))
                    {
                        setup.Add(f.Setup(avatar));
                        IEnumerator wait = WaitForUpdates();
                        while (wait.MoveNext()) yield return wait.Current;
                        if (!play) f.SavePrivatePreview(output, avatar);
                        f.Calibrate(capture);
                        for (int pose = 0; pose < 8; ++pose)
                        {
                            f.SetPose(pose);
                            wait = WaitForUpdates();
                            while (wait.MoveNext()) yield return wait.Current;
                            float positionError = Vector3.Distance(f.Canonical.position, f.Proxy.position);
                            float rotationError = Quaternion.Angle(f.Canonical.rotation, f.Proxy.rotation);
                            // Scale changes move the Head pivot; recenter before matrix readback as well as view measurements.
                            f.SetCamera(0, 0);
                            Vector3[] baselineVertices = vertexProbe.Read(f, 0);
                            for (int method = 0; method < f.Renderers.Length; ++method)
                            {
                                if (f.Renderers[method] == null) continue;
                                float error = RealAvatarVertexProbe.MaxError(baselineVertices, vertexProbe.Read(f, method));
                                Append(vertexRows, Phase, avatar, HeadFrameFixture.Poses[pose], f.Names[method], baselineVertices.Length,
                                    error, error <= 0.0001f ? "PASS" : "FAIL");
                            }
                            float[] matrixErrors = new float[f.Renderers.Length];
                            for (int method = 0; method < f.Renderers.Length; ++method)
                            {
                                if (f.Renderers[method] == null) continue;
                                Matrix4x4 matrix = f.ObjectMatrix(capture, method);
                                Transform root = f.Renderers[method].rootBone;
                                if (root == null) throw new InvalidOperationException("rootBone nullはこの実験では未対応です。");
                                matrixErrors[method] = HeadFrameMath.MatrixError(matrix, Matrix4x4.TRS(root.position, root.rotation, Vector3.one));
                            }
                            for (int view = 0; view < Yaws.Length; ++view)
                            {
                                f.SetCamera(Yaws[view], Pitches[view]);
                                Vector2 expected = HeadFrameMath.Angles(f.Canonical.InverseTransformPoint(f.Camera.transform.position));
                                Color[] baseline = f.Read(capture, 0, 8);
                                for (int method = 0; method < f.Renderers.Length; ++method)
                                {
                                    if (f.Renderers[method] == null)
                                    {
                                        Append(rows, Phase, avatar, HeadFrameFixture.Poses[pose], f.Names[method], Yaws[view], Pitches[view],
                                            expected.x, expected.y, "", "", "", "", "", "", positionError, rotationError,
                                            "", "", "", "", "NOT RUN");
                                        continue;
                                    }
                                    Vector2 actual = HeadFrameCapture.Decode(f.Sample(capture, method, 0));
                                    Vector2 raw = HeadFrameCapture.Decode(f.Sample(capture, method, 6));
                                    float yawError = Mathf.Abs(Mathf.DeltaAngle(expected.x, actual.x)), pitchError = Mathf.Abs(expected.y - actual.y);
                                    HeadFrameCapture.CompareGeometry(baseline, f.Read(capture, method, 8), out int coverage, out float worldError, out int covered);
                                    bool pass = yawError <= 0.05f && pitchError <= 0.05f && matrixErrors[method] <= 1e-5f && coverage <= 2 && worldError <= 0.0001f;
                                    Append(rows, Phase, avatar, HeadFrameFixture.Poses[pose], f.Names[method], Yaws[view], Pitches[view],
                                        expected.x, expected.y, actual.x, actual.y, raw.x, raw.y, yawError, pitchError, positionError, rotationError,
                                        matrixErrors[method], coverage, worldError, covered, pass ? "PASS" : "FAIL");
                                }
                            }
                            Debug.Log("Real avatar Head Frame " + Phase + " " + avatar + " " + HeadFrameFixture.Poses[pose] + " completed.");
                            // Give the Editor a chance to process cancellation / domain changes between poses.
                            yield return null;
                        }
                        f.VerifySources();
                        Debug.Log("Real avatar source Mesh/Material unchanged: " + avatar + " " + Phase);
                    }
                    File.WriteAllText(Path.Combine(output, Phase + "-angles.csv"), rows.ToString());
                    File.WriteAllText(Path.Combine(output, Phase + "-vertices.csv"), vertexRows.ToString());
                    File.WriteAllText(Path.Combine(output, Phase + "-setup.json"), JsonConvert.SerializeObject(setup, Formatting.Indented));
                    File.WriteAllText(Path.Combine(output, Phase + "-audit.json"), JsonConvert.SerializeObject(audits, Formatting.Indented));
                }
            }
            Debug.Log("Real avatar Head Frame " + Phase + " measurement completed.");
        }

        private static string Phase => play ? "PlayMode" : "EditMode";

        private static void Append(StringBuilder builder, params object[] values)
        {
            for (int i = 0; i < values.Length; ++i)
            {
                if (i > 0) builder.Append(',');
                string value = Convert.ToString(values[i], Invariant);
                builder.Append(value.IndexOfAny(new[] { ',', '\n', '\r', '"' }) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"");
            }
            builder.AppendLine();
        }
    }
}
