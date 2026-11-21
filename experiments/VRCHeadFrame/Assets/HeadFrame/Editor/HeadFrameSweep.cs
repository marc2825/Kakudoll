using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VLat.HeadFrameExperiment
{
    /// <summary>既存VLatから独立したHead Frame診断シーン生成・GPU比較実験を提供します。</summary>
    public static class HeadFrameSweep
    {
        private static IEnumerator routine;
        private static bool batch;
        private static string output;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly float[] Yaws = { -90, -45, 0, 45, 90, 45, -45 };
        private static readonly float[] Pitches = { 0, 0, 0, 0, 0, 30, -30 };

        /// <summary>UnityのexecuteMethodから、グラフィックスを使用する比較実験を開始します。</summary>
        /// <remarks>-quitと-nographicsは指定せず、終了は実験runnerへ委譲します。</remarks>
        public static void RunBatch()
        {
            batch = true;
            Begin();
        }

        [MenuItem("VLat Experiments/Run Head Frame Sweep")]
        private static void RunMenu()
        {
            batch = false;
            Begin();
        }

        private static void Begin()
        {
            if (routine != null) throw new InvalidOperationException("実験は実行中です。");
            output = Path.GetFullPath("artifacts/head-frame-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", Invariant));
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i)
                if (args[i] == "-headFrameOutput") output = Path.GetFullPath(args[i + 1]);
            Directory.CreateDirectory(output);
            routine = Sweep();
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            try
            {
                if (routine.MoveNext()) return;
                Finish(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                File.WriteAllText(Path.Combine(output, "HARNESS_FAILED.txt"), ex.ToString());
                Finish(1);
            }
        }

        private static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            (routine as IDisposable)?.Dispose();
            routine = null;
            if (batch) EditorApplication.Exit(code);
        }

        private static IEnumerator Sweep()
        {
            bool originalGpuSkinning = PlayerSettings.gpuSkinning;
            StringBuilder rows = new StringBuilder("gpu_skinning,axes,pose,method,world_yaw,world_pitch,cpu_yaw,cpu_pitch,gpu_yaw,gpu_pitch,yaw_error,pitch_error,raw_yaw,raw_pitch,coverage_mismatch,world_error_m,common_pixels,status\n");
            StringBuilder matrices = new StringBuilder("gpu_skinning,axes,pose,method,shader_vs_renderer,shader_vs_transform,shader_vs_root,shader_vs_root_rigid,calibration_drift,cpu_bake_rigid_error_m,cpu_bake_full_error_m,object_to_world_column_major,object_to_frame_rest_column_major\n");
            StringBuilder summary = new StringBuilder();
            summary.AppendLine("# Head Frame GPU sweep");
            summary.AppendLine("UTC: " + DateTime.UtcNow.ToString("O", Invariant));
            summary.AppendLine("Unity: " + Application.unityVersion);
            summary.AppendLine("OS: " + SystemInfo.operatingSystem);
            summary.AppendLine("GPU: " + SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceVersion);
            summary.AppendLine("Color space: " + QualitySettings.activeColorSpace + "; RT: linear ARGBFloat 64x64, MSAA off");
            summary.AppendLine("Angle tolerance: 0.05 deg; world pixel tolerance: 0.0001 m; coverage difference <= 2 pixels.");
            summary.AppendLine("NonUniform / NegativeScale are diagnostic INCONCLUSIVE, excluded from supported gate.");
            summary.AppendLine("gpu_skinning is the PlayerSettings toggle, not proof of the Editor skinning execution backend.");
            summary.AppendLine("CPU BakeMesh(false) is checked with both rigid-Transform and full-Transform world reconstruction.");
            summary.AppendLine("Analytic angle checks: " + CheckAngles() + " PASS.");
            int count = 0;
            using (HeadFrameCapture capture = new HeadFrameCapture())
            {
                try
                {
                    for (int gpu = 0; gpu < 2; ++gpu)
                    {
                        PlayerSettings.gpuSkinning = gpu == 1;
                        yield return null;
                        for (int axes = 0; axes < 2; ++axes)
                        {
                            Scene scene = EditorSceneManager.NewPreviewScene();
                            HeadFrameFixture f = null;
                            try
                            {
                                f = new HeadFrameFixture(scene, axes == 1);
                                string sourceBefore = MeshSignature(f.Source);
                                yield return null;
                                yield return null;
                                for (int method = 0; method < f.Renderers.Length; ++method)
                                {
                                    Matrix4x4 observed = capture.ObjectMatrix(f, method);
                                    f.Calibration[method] = f.Canonical.worldToLocalMatrix * observed;
                                    HeadFrameMath.SetCalibration(f.Renderers[method].sharedMaterial, f.Calibration[method]);
                                }
                                float[] maxAngles = new float[7], maxWorld = new float[7], maxDrift = new float[7];
                                float[] bakeErrors = new float[7], maxBake = new float[7];
                                int[] fails = new int[7];
                                for (int pose = 0; pose < HeadFrameFixture.Poses.Length; ++pose)
                                {
                                    f.SetPose(pose);
                                    f.SetCamera(0, 0);
                                    // Allow native skinning and Constraint evaluation between pose and draw.
                                    EditorApplication.QueuePlayerLoopUpdate();
                                    yield return null;
                                    yield return null;
                                    for (int method = 0; method < f.Renderers.Length; ++method)
                                    {
                                        Matrix4x4 observed = capture.ObjectMatrix(f, method);
                                        SkinnedMeshRenderer renderer = f.Renderers[method];
                                        float drift = HeadFrameMath.MatrixError(f.Canonical.worldToLocalMatrix * observed, f.Calibration[method]);
                                        CpuBakeError(renderer, out float bakeRigid, out float bakeFull);
                                        bakeErrors[method] = bakeRigid;
                                        if (pose < 8) maxBake[method] = Mathf.Max(maxBake[method], bakeRigid);
                                        if (pose < 8) maxDrift[method] = Mathf.Max(maxDrift[method], drift);
                                        Append(matrices, gpu, axes, HeadFrameFixture.Poses[pose], f.Names[method],
                                            HeadFrameMath.MatrixError(observed, renderer.localToWorldMatrix),
                                            HeadFrameMath.MatrixError(observed, renderer.transform.localToWorldMatrix),
                                            HeadFrameMath.MatrixError(observed, renderer.rootBone.localToWorldMatrix),
                                            HeadFrameMath.MatrixError(observed, Matrix4x4.TRS(renderer.rootBone.position, renderer.rootBone.rotation, Vector3.one)),
                                            drift, bakeRigid, bakeFull,
                                            MatrixText(observed), MatrixText(f.Calibration[method]));
                                    }
                                    for (int view = 0; view < Yaws.Length; ++view)
                                    {
                                        f.SetCamera(Yaws[view], Pitches[view]);
                                        Color[] baseline = capture.Read(f, 0, 8);
                                        Vector2 expected = HeadFrameMath.Angles(f.Canonical.InverseTransformPoint(f.Camera.transform.position));
                                        for (int method = 0; method < f.Renderers.Length; ++method)
                                        {
                                            Vector2 actual = HeadFrameCapture.Decode(capture.Sample(f, method, 0));
                                            Vector2 raw = HeadFrameCapture.Decode(capture.Sample(f, method, 6));
                                            HeadFrameCapture.CompareGeometry(baseline, capture.Read(f, method, 8), out int mismatch, out float worldError, out int covered);
                                            float yawError = Mathf.Abs(Mathf.DeltaAngle(expected.x, actual.x));
                                            float pitchError = Mathf.Abs(expected.y - actual.y);
                                            bool pass = yawError <= 0.05f && pitchError <= 0.05f && mismatch <= 2 && worldError <= 0.0001f && bakeErrors[method] <= 0.0001f;
                                            string status = pose >= 8 ? "INCONCLUSIVE" : (pass ? "PASS" : "FAIL");
                                            if (pose < 8)
                                            {
                                                maxAngles[method] = Mathf.Max(maxAngles[method], yawError, pitchError);
                                                maxWorld[method] = Mathf.Max(maxWorld[method], worldError);
                                                if (!pass) ++fails[method];
                                            }
                                            Append(rows, gpu, axes, HeadFrameFixture.Poses[pose], f.Names[method], Yaws[view], Pitches[view],
                                                expected.x, expected.y, actual.x, actual.y, yawError, pitchError, raw.x, raw.y, mismatch, worldError, covered, status);
                                            ++count;
                                        }
                                    }
                                }
                                if (sourceBefore != MeshSignature(f.Source)) throw new InvalidOperationException("元Meshが変更されました。");
                                summary.AppendLine();
                                summary.AppendLine("GPU skinning setting=" + gpu + ", alternate axes=" + axes);
                                for (int method = 0; method < f.Renderers.Length; ++method)
                                    summary.AppendLine(string.Format(Invariant, "- {0}: {1}; failures={2}/56; max angle={3:G9} deg; max world={4:G9} m; max calibration drift={5:G9}; max CPU bake rigid error={6:G9} m",
                                        f.Names[method], fails[method] == 0 ? "PASS" : "FAIL", fails[method], maxAngles[method], maxWorld[method], maxDrift[method], maxBake[method]));
                                summary.AppendLine("Source mesh fingerprint unchanged: PASS (" + sourceBefore + ")");
                            }
                            finally
                            {
                                f?.Dispose();
                                EditorSceneManager.ClosePreviewScene(scene);
                            }
                        }
                    }
                }
                finally { PlayerSettings.gpuSkinning = originalGpuSkinning; }
            }
            summary.AppendLine();
            summary.AppendLine("Recorded cases: " + count + ". Harness completed; candidate FAIL does not mean harness failure.");
            summary.AppendLine("VRChat client / stereo / mirror / camera / head chop: NOT RUN.");
            File.WriteAllText(Path.Combine(output, "angles.csv"), rows.ToString());
            File.WriteAllText(Path.Combine(output, "matrices.csv"), matrices.ToString());
            File.WriteAllText(Path.Combine(output, "summary.md"), summary.ToString());
            Debug.Log("Head Frame sweep completed: " + output);
        }

        private static void CpuBakeError(SkinnedMeshRenderer renderer, out float rigidError, out float fullError)
        {
            Mesh mesh = renderer.sharedMesh;
            Mesh baked = new Mesh();
            try
            {
                renderer.BakeMesh(baked, false);
                Vector3[] source = mesh.vertices, actual = baked.vertices;
                Vector3[] dp = new Vector3[source.Length], dn = new Vector3[source.Length], dt = new Vector3[source.Length];
                mesh.GetBlendShapeFrameVertices(0, 0, dp, dn, dt);
                BoneWeight[] weights = mesh.boneWeights;
                Matrix4x4[] binds = mesh.bindposes;
                Transform[] bones = renderer.bones;
                float expression = renderer.GetBlendShapeWeight(0) / 100;
                rigidError = 0;
                fullError = 0;
                Matrix4x4 rigid = Matrix4x4.TRS(renderer.transform.position, renderer.transform.rotation, Vector3.one);
                if (source.Length != actual.Length) throw new InvalidOperationException("BakeMesh頂点数が不一致です。");
                for (int i = 0; i < source.Length; ++i)
                {
                    Vector3 p = source[i] + dp[i] * expression;
                    BoneWeight w = weights[i];
                    Vector3 world = (bones[w.boneIndex0].localToWorldMatrix * binds[w.boneIndex0]).MultiplyPoint3x4(p) * w.weight0;
                    if (w.weight1 > 0) world += (bones[w.boneIndex1].localToWorldMatrix * binds[w.boneIndex1]).MultiplyPoint3x4(p) * w.weight1;
                    rigidError = Mathf.Max(rigidError, (world - rigid.MultiplyPoint3x4(actual[i])).magnitude);
                    fullError = Mathf.Max(fullError, (world - renderer.transform.TransformPoint(actual[i])).magnitude);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
        }

        private static int CheckAngles()
        {
            Vector3[] directions = { Vector3.forward, new Vector3(1,0,1), new Vector3(-1,0,1), Vector3.right, Vector3.left,
                new Vector3(0,0.5f,0.8660254f), new Vector3(0,-0.5f,0.8660254f), Vector3.back, Vector3.up, Vector3.down };
            Vector2[] expected = { new Vector2(0,0), new Vector2(45,0), new Vector2(-45,0), new Vector2(90,0), new Vector2(-90,0),
                new Vector2(0,30), new Vector2(0,-30), new Vector2(180,0), new Vector2(0,90), new Vector2(0,-90) };
            for (int i = 0; i < directions.Length; ++i)
                if ((HeadFrameMath.Angles(directions[i]) - expected[i]).magnitude > 0.0001f)
                    throw new InvalidOperationException("解析角度参照の検証失敗: " + i);
            bool rejected = false;
            try { HeadFrameMath.Angles(Vector3.zero); }
            catch (ArgumentException) { rejected = true; }
            if (!rejected) throw new InvalidOperationException("原点の未定義角度を受理しました。");
            return directions.Length + 1;
        }

        private static string MeshSignature(Mesh mesh)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                foreach (Vector3 p in mesh.vertices) { writer.Write(p.x); writer.Write(p.y); writer.Write(p.z); }
                foreach (Vector3 p in mesh.normals) { writer.Write(p.x); writer.Write(p.y); writer.Write(p.z); }
                foreach (int index in mesh.triangles) writer.Write(index);
                foreach (Matrix4x4 m in mesh.bindposes) for (int i = 0; i < 16; ++i) writer.Write(m[i]);
                foreach (BoneWeight w in mesh.boneWeights) { writer.Write(w.boneIndex0); writer.Write(w.weight0); writer.Write(w.boneIndex1); writer.Write(w.weight1); }
                Vector3[] dp = new Vector3[mesh.vertexCount], dn = new Vector3[mesh.vertexCount], dt = new Vector3[mesh.vertexCount];
                for (int s = 0; s < mesh.blendShapeCount; ++s)
                {
                    writer.Write(mesh.GetBlendShapeName(s));
                    for (int f = 0; f < mesh.GetBlendShapeFrameCount(s); ++f)
                    {
                        writer.Write(mesh.GetBlendShapeFrameWeight(s, f));
                        mesh.GetBlendShapeFrameVertices(s, f, dp, dn, dt);
                        for (int i = 0; i < dp.Length; ++i)
                        {
                            writer.Write(dp[i].x); writer.Write(dp[i].y); writer.Write(dp[i].z);
                            writer.Write(dn[i].x); writer.Write(dn[i].y); writer.Write(dn[i].z);
                            writer.Write(dt[i].x); writer.Write(dt[i].y); writer.Write(dt[i].z);
                        }
                    }
                }
                writer.Flush();
                using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }

        private static string MatrixText(Matrix4x4 matrix)
        {
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < 16; ++i)
            {
                if (i > 0) b.Append(' ');
                b.Append(matrix[i].ToString("G9", Invariant));
            }
            return b.ToString();
        }

        private static void Append(StringBuilder b, params object[] values)
        {
            for (int i = 0; i < values.Length; ++i)
            {
                if (i > 0) b.Append(',');
                b.Append(Convert.ToString(values[i], Invariant));
            }
            b.AppendLine();
        }

        /// <summary>診断fixtureを別シーン・別フォルダへ生成します。対話実行では既存シーンを置き換えません。</summary>
        [MenuItem("VLat Experiments/Create Head Frame Scene")]
        public static void CreateScene()
        {
            // Unity cannot save Preview Scenes or create an additive scene beside an untitled one.
            // Single is permitted only in the explicitly isolated batch project.
            if (!Application.isBatchMode)
            {
                for (int i = 0; i < SceneManager.sceneCount; ++i)
                    if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                        throw new InvalidOperationException("Unityの制約により、無題のシーンを保存してから診断シーンを追加してください。");
            }
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            HeadFrameFixture f = new HeadFrameFixture(scene, false);
            string folder = AssetDatabase.GenerateUniqueAssetPath("Assets/HeadFrame/Generated");
            AssetDatabase.CreateFolder("Assets/HeadFrame", Path.GetFileName(folder));
            for (int i = 0; i < f.Owned.Count; ++i)
            {
                UnityEngine.Object asset = f.Owned[i];
                AssetDatabase.CreateAsset(asset, folder + "/" + i.ToString("D2", Invariant) + (asset is Mesh ? ".asset" : ".mat"));
            }
            using (HeadFrameCapture capture = new HeadFrameCapture())
            {
                for (int i = 0; i < f.Renderers.Length; ++i)
                    HeadFrameMath.SetCalibration(f.Renderers[i].sharedMaterial, f.Canonical.worldToLocalMatrix * capture.ObjectMatrix(f, i));
            }
            f.Show(2);
            f.Camera.enabled = true;
            f.Renderers[2].sharedMaterial.SetFloat("_Mode", 0);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene, folder + "/HeadFrame.unity"))
                throw new IOException("診断シーンを保存できませんでした。");
            Selection.activeGameObject = f.Head.gameObject;
        }

        /// <summary>隔離したbatch実行で保存済み診断シーンの再読込とShader定数の永続化を検証します。</summary>
        public static void VerifySavedScene()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("この検証は隔離batch実行専用です。");
            EditorSceneManager.OpenScene("Assets/HeadFrame/Generated/HeadFrame.unity", OpenSceneMode.Single);
            SkinnedMeshRenderer[] renderers = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>();
            if (renderers.Length != 7) throw new InvalidOperationException("保存済みRenderer数が不正です。");
            Camera camera = UnityEngine.Object.FindObjectOfType<Camera>();
            SkinnedMeshRenderer selected = null;
            for (int i = 0; i < renderers.Length; ++i)
            {
                if (renderers[i].sharedMesh == null || renderers[i].sharedMaterial == null || renderers[i].rootBone == null)
                    throw new InvalidOperationException("保存済みアセット参照が不正です。");
                if (renderers[i].enabled) selected = renderers[i];
            }
            if (selected == null || selected.name != "B_ChildProxyRoot") throw new InvalidOperationException("既定Rendererが不正です。");
            using (HeadFrameCapture capture = new HeadFrameCapture())
            {
                Color[] pixels = capture.Read(camera, selected, 0);
                Vector2 angles = default;
                bool covered = false;
                for (int i = 0; i < pixels.Length; ++i)
                    if (pixels[i].a > 0.5f) { angles = HeadFrameCapture.Decode(pixels[i]); covered = true; break; }
                if (!covered || angles.magnitude > 0.05f) throw new InvalidOperationException("保存済みShader校正が不正です。");
            }
            Debug.Log("Saved scene reload / references / GPU calibration: PASS");
        }
    }
}
