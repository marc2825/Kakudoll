using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using VRC.Dynamics;
using VRC.SDK3.Avatars;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace VLat.HeadFrameExperiment
{
    /// <summary>隔離SDK projectで、Head FrameのVRC Constraint追従をEdit/Play Modeで比較します。</summary>
    [InitializeOnLoad]
    public static class SdkHeadFrameSweep
    {
        private const string ActiveKey = "VLat.HeadFrameSdk.Active";
        private const string OutputKey = "VLat.HeadFrameSdk.Output";
        private const string CompletedKey = "VLat.HeadFrameSdk.Completed";
        private const string FailureKey = "VLat.HeadFrameSdk.Failure";
        private const string PhaseKey = "VLat.HeadFrameSdk.Phase";
        private static IEnumerator routine;
        private static bool play;
        private static readonly string[] Backends = { "Unity", "VrcDirect", "VrcConverted", "VrcDisabled" };
        private static readonly int[] Methods = { 2, 3, 6 };
        private static readonly float[] Yaws = { -90, -45, 0, 45, 90, 45, -45 };
        private static readonly float[] Pitches = { 0, 0, 0, 0, 0, 30, -30 };
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        static SdkHeadFrameSweep()
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
            string path = Path.GetFullPath("results-sdk");
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i)
                if (args[i] == "-headFrameOutput") path = Path.GetFullPath(args[i + 1]);
            Directory.CreateDirectory(path);
            SessionState.SetString(OutputKey, path);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(CompletedKey, false);
            SessionState.SetBool(FailureKey, false);
            string inputs = Path.GetFullPath("sdk-experiment-inputs.json");
            File.Copy(inputs, Path.Combine(path, "inputs.json"), false);
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
                File.WriteAllText(Path.Combine(SessionState.GetString(OutputKey, "results-sdk"), "HARNESS_FAILED.txt"), ex.ToString());
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
                ? UnityEngine.SceneManagement.SceneManager.CreateScene("HeadFrameSdkPlayMode")
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            StringBuilder rows = new StringBuilder("phase,backend,axes,pose,method,world_yaw,world_pitch,cpu_yaw,cpu_pitch,gpu_yaw,gpu_pitch,yaw_error,pitch_error,proxy_position_error_m,proxy_rotation_error_deg,shader_root_rigid_error,coverage_mismatch,world_error_m,status\n");
            StringBuilder setup = new StringBuilder("phase,backend,axes,unity_constraint_remaining,vrc_constraint_count,conversion_issues,is_active,global_weight,locked,solve_local,source_count\n");
            using (HeadFrameCapture capture = new HeadFrameCapture())
            {
                for (int backend = 0; backend < Backends.Length; ++backend)
                {
                    for (int axes = 0; axes < 2; ++axes)
                    {
                        using (HeadFrameFixture f = new HeadFrameFixture(scene, axes == 1))
                        {
                            ParentConstraint unity = f.ConstrainedProxy.GetComponent<ParentConstraint>();
                            VRCParentConstraint vrc = null;
                            bool issues = false;
                            if (backend == 2)
                            {
                                issues = AvatarDynamicsSetup.DoConvertUnityConstraints(new IConstraint[] { unity }, null, false);
                                vrc = f.ConstrainedProxy.GetComponent<VRCParentConstraint>();
                                if (issues || vrc == null || f.ConstrainedProxy.GetComponent<ParentConstraint>() != null)
                                    throw new InvalidOperationException("SDKによるUnity Constraint変換に失敗しました。");
                            }
                            else if (backend != 0)
                            {
                                UnityEngine.Object.DestroyImmediate(unity);
                                vrc = f.ConstrainedProxy.gameObject.AddComponent<VRCParentConstraint>();
                                vrc.Sources.Add(new VRCConstraintSource(f.Canonical, 1));
                                vrc.GlobalWeight = 1;
                                vrc.IsActive = true;
                                vrc.Locked = true;
                                vrc.SolveInLocalSpace = false;
                                vrc.ApplyConfigurationChanges();
                                if (backend == 3) { vrc.IsActive = false; vrc.ApplyConfigurationChanges(); }
                            }
                            Append(setup, Phase, Backends[backend], axes,
                                f.ConstrainedProxy.GetComponent<ParentConstraint>() != null ? 1 : 0,
                                f.ConstrainedProxy.GetComponents<VRCParentConstraint>().Length, issues,
                                vrc != null ? vrc.IsActive : unity.constraintActive,
                                vrc != null ? vrc.GlobalWeight : unity.weight,
                                vrc != null ? vrc.Locked : unity.locked,
                                vrc != null && vrc.SolveInLocalSpace, vrc != null ? vrc.Sources.Count : unity.sourceCount);
                            IEnumerator wait = WaitForUpdates();
                            while (wait.MoveNext()) yield return wait.Current;
                            for (int index = 0; index < Methods.Length; ++index)
                            {
                                int method = Methods[index];
                                f.Calibration[method] = f.Canonical.worldToLocalMatrix * capture.ObjectMatrix(f, method);
                                HeadFrameMath.SetCalibration(f.Renderers[method].sharedMaterial, f.Calibration[method]);
                            }
                            for (int pose = 0; pose < 8; ++pose)
                            {
                                f.SetPose(pose);
                                wait = WaitForUpdates();
                                while (wait.MoveNext()) yield return wait.Current;
                                float positionError = Vector3.Distance(f.Canonical.position, f.ConstrainedProxy.position);
                                float rotationError = Quaternion.Angle(f.Canonical.rotation, f.ConstrainedProxy.rotation);
                                for (int view = 0; view < Yaws.Length; ++view)
                                {
                                    f.SetCamera(Yaws[view], Pitches[view]);
                                    Vector2 expected = HeadFrameMath.Angles(f.Canonical.InverseTransformPoint(f.Camera.transform.position));
                                    Color[] baseline = capture.Read(f, 0, 8);
                                    for (int index = 0; index < Methods.Length; ++index)
                                    {
                                        int method = Methods[index];
                                        Matrix4x4 matrix = capture.ObjectMatrix(f, method);
                                        Transform root = f.Renderers[method].rootBone;
                                        float matrixError = HeadFrameMath.MatrixError(matrix, Matrix4x4.TRS(root.position, root.rotation, Vector3.one));
                                        Vector2 actual = HeadFrameCapture.Decode(capture.Sample(f, method, 0));
                                        float yawError = Mathf.Abs(Mathf.DeltaAngle(expected.x, actual.x));
                                        float pitchError = Mathf.Abs(expected.y - actual.y);
                                        HeadFrameCapture.CompareGeometry(baseline, capture.Read(f, method, 8), out int coverage, out float worldError, out int covered);
                                        bool pass = yawError <= 0.05f && pitchError <= 0.05f && matrixError <= 1e-5f && coverage <= 2 && worldError <= 0.0001f;
                                        Append(rows, Phase, Backends[backend], axes, HeadFrameFixture.Poses[pose], f.Names[method], Yaws[view], Pitches[view],
                                            expected.x, expected.y, actual.x, actual.y, yawError, pitchError,
                                            positionError, rotationError, matrixError, coverage, worldError, pass ? "PASS" : "FAIL");
                                    }
                                }
                            }
                            Debug.Log("SDK Head Frame " + Phase + " " + Backends[backend] + " axes=" + axes + " completed.");
                        }
                    }
                }
            }
            string output = SessionState.GetString(OutputKey, "results-sdk");
            File.WriteAllText(Path.Combine(output, Phase + "-angles.csv"), rows.ToString());
            File.WriteAllText(Path.Combine(output, Phase + "-setup.csv"), setup.ToString());
            Debug.Log("SDK Head Frame " + Phase + " measurement completed.");
        }

        private static string Phase => play ? "PlayMode" : "EditMode";

        private static void Append(StringBuilder builder, params object[] values)
        {
            for (int i = 0; i < values.Length; ++i)
            {
                if (i > 0) builder.Append(',');
                builder.Append(Convert.ToString(values[i], Invariant));
            }
            builder.AppendLine();
        }
    }
}
