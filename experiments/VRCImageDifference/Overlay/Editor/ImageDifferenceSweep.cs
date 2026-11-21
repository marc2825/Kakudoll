using System;
using System.Collections;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VLat.HeadFrameExperiment
{
    /// <summary>複製した実FBXで、保存済み20件の表面画像差を三角形・深度・法線へ分離します。</summary>
    [InitializeOnLoad]
    public static class ImageDifferenceSweep
    {
        private const string ActiveKey = "VLat.ImageDifferenceSweep.Active";
        private const string OutputKey = "VLat.ImageDifferenceSweep.Output";
        private const string CompletedKey = "VLat.ImageDifferenceSweep.Completed";
        private const string FailureKey = "VLat.ImageDifferenceSweep.Failure";
        private const string PhaseKey = "VLat.ImageDifferenceSweep.Phase";
        private static IEnumerator routine;
        private static bool play;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        static ImageDifferenceSweep()
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
            if (!Application.isBatchMode || EditorApplication.isPlaying || !File.Exists("image-fail-inputs.json"))
                throw new InvalidOperationException("隔離projectのEdit Mode batch実行専用です。");
            if (Application.unityVersion != "2022.3.22f1") throw new InvalidOperationException("この検証は2022.3.22f1へ固定しています。");
            string path = Path.GetFullPath("results-image-r1");
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i)
                if (args[i] == "-imageOutput") path = Path.GetFullPath(args[i + 1]);
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
                File.WriteAllText(Path.Combine(SessionState.GetString(OutputKey, "results-image-r1"), "HARNESS_FAILED.txt"), ex.ToString());
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
            var scene = play ? UnityEngine.SceneManagement.SceneManager.CreateScene("ImageDifferencePlayMode")
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            JArray models = (JArray)JObject.Parse(File.ReadAllText("avatar-inputs.json"))["models"];
            JArray cases = (JArray)JObject.Parse(File.ReadAllText("image-fail-inputs.json"))["records"];
            List<object> results = new List<object>();
            List<object> variants = new List<object>();
            string output = SessionState.GetString(OutputKey, "results-image-r1");
            using (HeadFrameCapture original = new HeadFrameCapture())
            using (RealAvatarVertexProbe vertices = new RealAvatarVertexProbe())
            using (ClipVertexProbe clips = new ClipVertexProbe())
            using (SurfaceCapture probe = new SurfaceCapture(64))
            {
                foreach (JObject model in models)
                {
                    string avatar = (string)model["name"], asset = (string)model["assetPath"];
                    if (!play) RealAvatarAudit.ConfigureImport(asset);
                    using (RealAvatarFixture f = new RealAvatarFixture(scene, asset))
                    {
                        IEnumerator wait = WaitForUpdates(); while (wait.MoveNext()) yield return wait.Current;
                        f.Calibrate(original);
                        foreach (JObject item in cases)
                        {
                            if ((string)item["phase"] != "EditMode" || (string)item["avatar"] != avatar) continue;
                            string poseName = (string)item["pose"], method = (string)item["method"];
                            int pose = Array.IndexOf(HeadFrameFixture.Poses, poseName), candidate = Array.IndexOf(f.Names, method);
                            float yaw = (float)item["world_yaw"], pitch = (float)item["world_pitch"];
                            string key = avatar + "-" + poseName + "-" + method + "-" + yaw + "-" + pitch;
                            f.SetPose(pose); wait = WaitForUpdates(); while (wait.MoveNext()) yield return wait.Current;
                            f.SetCamera(yaw, pitch);
                            Color[] oldA = f.Read(original, 0, 8), oldB = f.Read(original, candidate, 8);
                            HeadFrameCapture.CompareGeometry(oldA, oldB, out int oldCoverage, out float oldError, out int oldCovered);
                            Color[] a = probe.Read(f, 0, 0); Matrix4x4 view = probe.LastView, projection = probe.LastProjection;
                            Color[] b = probe.Read(f, candidate, 0);
                            float matrixError = Mathf.Max(HeadFrameMath.MatrixError(view, probe.LastView), HeadFrameMath.MatrixError(projection, probe.LastProjection));
                            Color[] idsA = probe.Read(f, 0, 1), idsB = probe.Read(f, candidate, 1);
                            int[][] triangleSlots = new int[f.Renderers[0].sharedMesh.subMeshCount][];
                            for (int slot = 0; slot < triangleSlots.Length; ++slot) triangleSlots[slot] = f.Renderers[0].sharedMesh.GetTriangles(slot);
                            Color[] depthA = probe.Read(f, 0, 2), depthB = probe.Read(f, candidate, 2);
                            Color[] normalA = probe.Read(f, 0, 3), normalB = probe.Read(f, candidate, 3);
                            Color[] colorA = probe.Read(f, 0, 4), colorB = probe.Read(f, candidate, 4);
                            HeadFrameCapture.CompareGeometry(a, b, out int coverage, out float error, out int covered);
                            Vector3[] va = vertices.Read(f, 0), vb = vertices.Read(f, candidate);
                            Vector3[] clipA = clips.Read(f, 0, 0), clipB = clips.Read(f, candidate, 0);
                            Vector3[] clipWA = clips.Read(f, 0, 1), clipWB = clips.Read(f, candidate, 1);
                            float cpuBaselineMax = RealAvatarVertexProbe.MaxError(CpuSkin(f.Renderers[0]), va);
                            float cpuCandidateMax = RealAvatarVertexProbe.MaxError(CpuSkin(f.Renderers[candidate]), vb);
                            float maxSame = 0, normalSame = 0, depthMax = 0, solidMax = 0;
                            int switched = 0, large = 0;
                            int matchedPixels = 0;
                            double matchedAllMax = 0;
                            Color[] correspondence = new Color[a.Length];
                            List<object> pixels = new List<object>();
                            for (int i = 0; i < a.Length; ++i)
                            {
                                if (a[i].a <= 0.5f || b[i].a <= 0.5f) continue;
                                bool same = idsA[i].r == idsB[i].r && idsA[i].g == idsB[i].g;
                                float delta = Distance(a[i], b[i]);
                                if (same)
                                {
                                    maxSame = Mathf.Max(maxSame, delta); normalSame = Mathf.Max(normalSame, Distance(normalA[i], normalB[i]));
                                    int tri = Mathf.RoundToInt(idsA[i].r) - 1, slot = Mathf.RoundToInt(idsA[i].g);
                                    int[] t = triangleSlots[slot]; int[] vi = { t[tri * 3], t[tri * 3 + 1], t[tri * 3 + 2] };
                                    RasterModel modelA = RasterPredict(i, vi, 0, va, clipA, clipWA, a[i]);
                                    double[] surfaceA = Interpolate(va, vi, 0, modelA.weights), surfaceB = Interpolate(vb, vi, 0, modelA.weights);
                                    double matched = Math.Sqrt(Dot(Sub(surfaceA, surfaceB), Sub(surfaceA, surfaceB)));
                                    matchedAllMax = Math.Max(matchedAllMax, matched); ++matchedPixels;
                                    correspondence[i] = new Color((float)(matched / 0.0001), 0, 0, 1);
                                }
                                else ++switched;
                                depthMax = Mathf.Max(depthMax, Mathf.Abs(depthA[i].r - depthB[i].r));
                                solidMax = Mathf.Max(solidMax, Distance(colorA[i], colorB[i]));
                                float oldDelta = oldA[i].a > 0.5f && oldB[i].a > 0.5f ? Distance(oldA[i], oldB[i]) : 0;
                                if (delta <= 0.0001f && oldDelta <= 0.0001f) continue;
                                ++large;
                                int slotA = Mathf.RoundToInt(idsA[i].g), slotB = Mathf.RoundToInt(idsB[i].g);
                                int triA = Mathf.RoundToInt(idsA[i].r) - 1, triB = Mathf.RoundToInt(idsB[i].r) - 1;
                                Color[] aa = probe.Read(f, 0, 0, slotA, triA), ab = probe.Read(f, candidate, 0, slotA, triA);
                                Color[] ba = probe.Read(f, 0, 0, slotB, triB), bb = probe.Read(f, candidate, 0, slotB, triB);
                                Color[] aad = probe.Read(f, 0, 2, slotA, triA), abd = probe.Read(f, candidate, 2, slotA, triA);
                                Color[] bad = probe.Read(f, 0, 2, slotB, triB), bbd = probe.Read(f, candidate, 2, slotB, triB);
                                int[] ia = f.Renderers[0].sharedMesh.GetTriangles(slotA), ib = f.Renderers[0].sharedMesh.GetTriangles(slotB);
                                int[] vertexIds = { ia[triA * 3], ia[triA * 3 + 1], ia[triA * 3 + 2], ib[triB * 3], ib[triB * 3 + 1], ib[triB * 3 + 2] };
                                float primitiveVertexError = 0;
                                foreach (int id in vertexIds) primitiveVertexError = Mathf.Max(primitiveVertexError, Vector3.Distance(va[id], vb[id]));
                                object rayPlaneA = RayPlane(f.Camera, view, projection, i, va, vertexIds, 0, a[i]);
                                object rayPlaneB = RayPlane(f.Camera, view, projection, i, vb, vertexIds, same ? 0 : 3, b[i]);
                                RasterModel rasterA = RasterPredict(i, vertexIds, 0, va, clipA, clipWA, a[i]);
                                RasterModel rasterB = RasterPredict(i, vertexIds, same ? 0 : 3, vb, clipB, clipWB, b[i]);
                                double[] matchedA = Interpolate(va, vertexIds, 0, rasterA.weights);
                                double[] matchedB = Interpolate(vb, vertexIds, 0, rasterA.weights);
                                double matchedError = Math.Sqrt(Dot(Sub(matchedA, matchedB), Sub(matchedA, matchedB)));
                                pixels.Add(new { index = i, x = i % 64, y = i / 64, original_error_m = oldDelta, probe_error_m = delta,
                                    samePrimitive = same, baselinePrimitive = triA, baselineSlot = slotA, candidatePrimitive = triB, candidateSlot = slotB,
                                    baselineWorld = Values(a[i]), candidateWorld = Values(b[i]), baselineDepth = Values(depthA[i]), candidateDepth = Values(depthB[i]),
                                    primitiveA_baseline = Values(aa[i]), primitiveA_candidate = Values(ab[i]), primitiveB_baseline = Values(ba[i]), primitiveB_candidate = Values(bb[i]),
                                    primitiveA_depth_baseline = Values(aad[i]), primitiveA_depth_candidate = Values(abd[i]), primitiveB_depth_baseline = Values(bad[i]), primitiveB_depth_candidate = Values(bbd[i]),
                                    primitiveVertexError_m = primitiveVertexError, vertexIds, rayPlaneA, rayPlaneB, rasterA, rasterB, matchedBarycentricError_m = matchedError });
                            }
                            string local = Path.Combine(output, "private-images", Phase, key);
                            probe.SavePrivate(local, "original-baseline", oldA, 0); probe.SavePrivate(local, "original-candidate", oldB, 0);
                            SaveDifference(probe, local, "original-difference", oldA, oldB);
                            probe.SavePrivate(local, "correspondence-difference", correspondence, 4);
                            probe.SavePrivate(local, "primitive-baseline", idsA, 1); probe.SavePrivate(local, "primitive-candidate", idsB, 1);
                            probe.SavePrivate(local, "normal-baseline", normalA, 3); probe.SavePrivate(local, "normal-candidate", normalB, 3);
                            probe.SavePrivate(local, "depth-baseline", depthA, 0); probe.SavePrivate(local, "depth-candidate", depthB, 0);
                            probe.SavePrivate(local, "solid-baseline", colorA, 4); probe.SavePrivate(local, "solid-candidate", colorB, 4);
                            results.Add(new { phase = Phase, avatar, pose = poseName, method, yaw, pitch, originalExpected_m = (float)item["world_error_m"],
                                originalError_m = oldError, originalCoverage = oldCoverage, originalCovered = oldCovered,
                                probeError_m = error, coverage, covered, samePrimitiveMax_m = maxSame, normalSameMax = normalSame,
                                depthMax, solidMax, switchedPixels = switched, largePixels = large, viewProjectionMatrixError = matrixError,
                                vertexMax_m = RealAvatarVertexProbe.MaxError(va, vb), cpuBaselineMax_m = cpuBaselineMax, cpuCandidateMax_m = cpuCandidateMax,
                                matchedPixels, matchedBarycentricAllMax_m = matchedAllMax,
                                matchedBarycentricStatus = switched == 0 && coverage == 0 && matchedPixels == covered && matchedAllMax <= 0.0001 ? "PASS" : "FAIL", pixels });
                            File.WriteAllText(Path.Combine(output, Phase + "-cases.json"), JsonConvert.SerializeObject(results, Formatting.Indented));
                            // One variable at a time. These are diagnostic alternatives, not new acceptance thresholds.
                            AddVariant(variants, key, "BaselineRepeat", oldA, f.Read(original, 0, 8));
                            AddVariant(variants, key, "ProbeVsOriginalBaseline", oldA, a);
                            AddVariant(variants, key, "ProbeVsOriginalCandidate", oldB, b);
                            AddVariant(variants, key, "WorldClip", probe.Read(f, 0, 0, worldClip: true), probe.Read(f, candidate, 0, worldClip: true));
                            AddVariant(variants, key, "CullBack", probe.Read(f, 0, 0, cull: 2), probe.Read(f, candidate, 0, cull: 2));
                            AddVariant(variants, key, "ZAlways", probe.Read(f, 0, 0, ztest: 8), probe.Read(f, candidate, 0, ztest: 8));
                            AddVariant(variants, key, "BakedStatic", probe.ReadBaked(f, 0), probe.ReadBaked(f, candidate));
                            foreach (int size in new[] { 128, 256 })
                                using (SurfaceCapture other = new SurfaceCapture(size))
                                    AddVariant(variants, key, "Resolution" + size, other.Read(f, 0, 0), other.Read(f, candidate, 0));
                            if (pose == 5)
                            {
                                foreach (SkinnedMeshRenderer r in f.Renderers) if (r != null) r.SetBlendShapeWeight(f.BlinkIndex, 0);
                                AddVariant(variants, key, "BlinkOff", probe.Read(f, 0, 0), probe.Read(f, candidate, 0));
                                foreach (SkinnedMeshRenderer r in f.Renderers) if (r != null) r.SetBlendShapeWeight(f.BlinkIndex, 100);
                            }
                            // Clone only: rigid Head100% replaces mixed weights while preserving topology/bindposes/morphs.
                            Mesh ma = f.Renderers[0].sharedMesh, mb = f.Renderers[candidate].sharedMesh;
                            Mesh ca = UnityEngine.Object.Instantiate(ma), cb = UnityEngine.Object.Instantiate(mb);
                            try
                            {
                                int headIndex = Array.IndexOf(f.Source.bones, f.Head);
                                if (headIndex < 0) throw new InvalidOperationException("Head bone missing.");
                                BoneWeight[] weights = new BoneWeight[ca.vertexCount];
                                for (int i = 0; i < weights.Length; ++i) weights[i] = new BoneWeight { boneIndex0 = headIndex, weight0 = 1 };
                                ca.boneWeights = weights; cb.boneWeights = weights;
                                f.Renderers[0].sharedMesh = ca; f.Renderers[candidate].sharedMesh = cb;
                                AddVariant(variants, key, "HeadOnly", probe.Read(f, 0, 0), probe.Read(f, candidate, 0));
                            }
                            finally
                            {
                                f.Renderers[0].sharedMesh = ma; f.Renderers[candidate].sharedMesh = mb;
                                UnityEngine.Object.DestroyImmediate(ca); UnityEngine.Object.DestroyImmediate(cb);
                            }
                            Mesh negative = UnityEngine.Object.Instantiate(mb);
                            try
                            {
                                Vector3[] moved = negative.vertices;
                                for (int i = 0; i < moved.Length; ++i) moved[i] += Vector3.right * 0.001f;
                                negative.vertices = moved; f.Renderers[candidate].sharedMesh = negative;
                                float negativeVertexError = RealAvatarVertexProbe.MaxError(vb, vertices.Read(f, candidate));
                                AddVariant(variants, key, "NegativeMeshShift1mm", probe.Read(f, 0, 0), probe.Read(f, candidate, 0), negativeVertexError);
                            }
                            finally { f.Renderers[candidate].sharedMesh = mb; UnityEngine.Object.DestroyImmediate(negative); }
                            Debug.Log("Image investigation " + Phase + " " + key + " old=" + oldError + " probe=" + error + " switched=" + switched + " same=" + maxSame);
                            yield return null;
                        }
                        f.VerifySources();
                    }
                    File.WriteAllText(Path.Combine(output, Phase + "-cases.json"), JsonConvert.SerializeObject(results, Formatting.Indented));
                    File.WriteAllText(Path.Combine(output, Phase + "-variants.json"), JsonConvert.SerializeObject(variants, Formatting.Indented));
                }
            }
            File.WriteAllText(Path.Combine(output, Phase + "-cases.json"), JsonConvert.SerializeObject(results, Formatting.Indented));
            File.WriteAllText(Path.Combine(output, Phase + "-variants.json"), JsonConvert.SerializeObject(variants, Formatting.Indented));
            File.WriteAllText(Path.Combine(output, Phase + "-shader-messages.json"), JsonConvert.SerializeObject(ShaderUtil.GetShaderMessages(Shader.Find("VLat/Experiments/SurfaceProbe")), Formatting.Indented));
            File.WriteAllText(Path.Combine(output, Phase + "-clip-shader-messages.json"), JsonConvert.SerializeObject(ShaderUtil.GetShaderMessages(Shader.Find("VLat/Experiments/ClipVertexProbe")), Formatting.Indented));
        }

        private static string Phase => play ? "PlayMode" : "EditMode";
        private static float Distance(Color a, Color b) => new Vector3(a.r - b.r, a.g - b.g, a.b - b.b).magnitude;
        private static float[] Values(Color c) => new[] { c.r, c.g, c.b, c.a };
        private static object RayPlane(Camera camera, Matrix4x4 view, Matrix4x4 projection, int pixel,
            Vector3[] vertices, int[] indices, int offset, Color observed)
        {
            // Double geometry after the GPU point probe, independent of the raster interpolator.
            double[] p = D(vertices[indices[offset]]), q = D(vertices[indices[offset + 1]]), r = D(vertices[indices[offset + 2]]);
            double[] n = Cross(Sub(q, p), Sub(r, p));
            double[] origin = D(camera.transform.position);
            // Perspective projection used by the unchanged S1 fixture. Both readback Y conventions are checked.
            double x = ((pixel % 64 + 0.5) / 64.0 * 2 - 1), y = ((pixel / 64 + 0.5) / 64.0 * 2 - 1);
            double[] ray = Ray(view, projection, x, y), flip = Ray(view, projection, x, -y);
            double[] sample = new[] { (double)observed.r, observed.g, observed.b };
            if (RayDistance(sample, origin, flip) < RayDistance(sample, origin, ray)) ray = flip;
            double denominator = Dot(n, ray), cosine = Math.Abs(denominator) / Math.Sqrt(Dot(n, n) * Dot(ray, ray));
            if (Math.Abs(denominator) < 1e-30) return new { valid = false };
            double t = Dot(n, Sub(p, origin)) / denominator;
            double[] hit = { origin[0] + ray[0] * t, origin[1] + ray[1] * t, origin[2] + ray[2] * t };
            double planeResidual = Math.Abs(Dot(n, Sub(sample, p))) / Math.Sqrt(Dot(n, n));
            return new { valid = true, hit, grazingCosine = cosine, planeResidual_m = planeResidual,
                rayResidual_m = RayDistance(sample, origin, ray), hitToFragment_m = Math.Sqrt(Dot(Sub(hit, sample), Sub(hit, sample))) };
        }
        private static double[] Ray(Matrix4x4 view, Matrix4x4 projection, double x, double y)
        {
            double[] local = { (x + projection.m02) / projection.m00, (y + projection.m12) / projection.m11, -1 };
            Matrix4x4 inv = view.inverse;
            return new[] { inv.m00 * local[0] + inv.m01 * local[1] + inv.m02 * local[2],
                inv.m10 * local[0] + inv.m11 * local[1] + inv.m12 * local[2], inv.m20 * local[0] + inv.m21 * local[1] + inv.m22 * local[2] };
        }
        private static double RayDistance(double[] sample, double[] origin, double[] ray)
        {
            double[] cross = Cross(Sub(sample, origin), ray); return Math.Sqrt(Dot(cross, cross) / Dot(ray, ray));
        }
        private static double[] D(Vector3 p) => new[] { (double)p.x, p.y, p.z };
        private static double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        private static double[] Cross(double[] a, double[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        private sealed class RasterModel
        {
            public bool flippedY;
            public double[][] screen, snapped;
            public double[] weights, predicted, unsnappedPredicted;
            public double predictedError_m, unsnappedError_m;
        }
        private static RasterModel RasterPredict(int pixel, int[] ids, int offset, Vector3[] world, Vector3[] clip, Vector3[] clipW, Color observed)
        {
            RasterModel best = null;
            for (int flip = 0; flip < 2; ++flip)
            {
                var model = new RasterModel { flippedY = flip == 1, screen = new double[3][], snapped = new double[3][] };
                double[] inverseW = new double[3];
                for (int i = 0; i < 3; ++i)
                {
                    int id = ids[offset + i]; inverseW[i] = 1.0 / clipW[id].x;
                    double x = (clip[id].x * inverseW[i] * 0.5 + 0.5) * 64;
                    double y = (clip[id].y * inverseW[i] * (flip == 0 ? 0.5 : -0.5) + 0.5) * 64;
                    model.screen[i] = new[] { x, y };
                    model.snapped[i] = new[] { Math.Round(x * 256) / 256, Math.Round(y * 256) / 256 };
                }
                model.weights = Barycentric(model.snapped, inverseW, pixel % 64 + 0.5, pixel / 64 + 0.5);
                model.predicted = Interpolate(world, ids, offset, model.weights);
                model.unsnappedPredicted = Interpolate(world, ids, offset, Barycentric(model.screen, inverseW, pixel % 64 + 0.5, pixel / 64 + 0.5));
                double[] sample = new[] { (double)observed.r, observed.g, observed.b };
                model.predictedError_m = Math.Sqrt(Dot(Sub(model.predicted, sample), Sub(model.predicted, sample)));
                model.unsnappedError_m = Math.Sqrt(Dot(Sub(model.unsnappedPredicted, sample), Sub(model.unsnappedPredicted, sample)));
                if (best == null || model.predictedError_m < best.predictedError_m) best = model;
            }
            return best;
        }
        private static double[] Barycentric(double[][] p, double[] inverseW, double x, double y)
        {
            double area = (p[1][1] - p[2][1]) * (p[0][0] - p[2][0]) + (p[2][0] - p[1][0]) * (p[0][1] - p[2][1]);
            double a = ((p[1][1] - p[2][1]) * (x - p[2][0]) + (p[2][0] - p[1][0]) * (y - p[2][1])) / area;
            double b = ((p[2][1] - p[0][1]) * (x - p[2][0]) + (p[0][0] - p[2][0]) * (y - p[2][1])) / area;
            double c = 1 - a - b;
            double sum = a * inverseW[0] + b * inverseW[1] + c * inverseW[2];
            return new[] { a * inverseW[0] / sum, b * inverseW[1] / sum, c * inverseW[2] / sum };
        }
        private static double[] Interpolate(Vector3[] world, int[] ids, int offset, double[] weights)
        {
            double[] output = new double[3];
            for (int i = 0; i < 3; ++i)
            {
                Vector3 p = world[ids[offset + i]];
                output[0] += weights[i] * p.x; output[1] += weights[i] * p.y; output[2] += weights[i] * p.z;
            }
            return output;
        }
        private static Vector3[] CpuSkin(SkinnedMeshRenderer renderer)
        {
            Mesh mesh = renderer.sharedMesh;
            Vector3[] source = mesh.vertices;
            Vector3[] delta = new Vector3[source.Length], normals = new Vector3[source.Length], tangents = new Vector3[source.Length];
            for (int shape = 0; shape < mesh.blendShapeCount; ++shape)
            {
                float weight = renderer.GetBlendShapeWeight(shape);
                if (weight == 0) continue;
                // The selected blink uses one 100% frame. Reject other semantics instead of guessing interpolation.
                if (mesh.GetBlendShapeFrameCount(shape) != 1 || mesh.GetBlendShapeFrameWeight(shape, 0) != 100)
                    throw new InvalidOperationException("CPU reference supports only this single 100% blink frame.");
                mesh.GetBlendShapeFrameVertices(shape, 0, delta, normals, tangents);
                for (int i = 0; i < source.Length; ++i) source[i] += delta[i] * (weight / 100);
            }
            Matrix4x4[] bind = mesh.bindposes;
            Transform[] bones = renderer.bones;
            Matrix4x4[] matrices = new Matrix4x4[bind.Length];
            for (int i = 0; i < matrices.Length; ++i) matrices[i] = bones[i].localToWorldMatrix * bind[i];
            var counts = mesh.GetBonesPerVertex(); var weights = mesh.GetAllBoneWeights();
            Vector3[] result = new Vector3[source.Length]; int cursor = 0;
            for (int vertex = 0; vertex < source.Length; ++vertex)
            {
                double x = 0, y = 0, z = 0;
                for (int influence = 0; influence < counts[vertex]; ++influence)
                {
                    var w = weights[cursor++]; Matrix4x4 m = matrices[w.boneIndex]; Vector3 p = source[vertex];
                    x += w.weight * ((double)m.m00 * p.x + (double)m.m01 * p.y + (double)m.m02 * p.z + m.m03);
                    y += w.weight * ((double)m.m10 * p.x + (double)m.m11 * p.y + (double)m.m12 * p.z + m.m13);
                    z += w.weight * ((double)m.m20 * p.x + (double)m.m21 * p.y + (double)m.m22 * p.z + m.m23);
                }
                result[vertex] = new Vector3((float)x, (float)y, (float)z);
            }
            return result;
        }
        private static void AddVariant(List<object> rows, string key, string variant, Color[] a, Color[] b, float vertexError = -1)
        {
            HeadFrameCapture.CompareGeometry(a, b, out int coverage, out float maximum, out int covered);
            rows.Add(new { phase = Phase, key, variant, coverage, maximum_m = maximum, covered, vertexWorldError_m = vertexError,
                strictSurfaceStatus = coverage <= 2 && maximum <= 0.0001f ? "PASS" : "FAIL" });
        }
        private static void SaveDifference(SurfaceCapture probe, string folder, string name, Color[] a, Color[] b)
        {
            Color[] data = new Color[a.Length];
            for (int i = 0; i < data.Length; ++i)
                data[i] = new Color(Mathf.Clamp01(Distance(a[i], b[i]) / 0.0001f), a[i].a != b[i].a ? 1 : 0, 0, a[i].a > 0.5f || b[i].a > 0.5f ? 1 : 0);
            probe.SavePrivate(folder, name, data, 4);
        }
    }
}
