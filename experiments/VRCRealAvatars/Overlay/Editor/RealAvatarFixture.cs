using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace VLat.HeadFrameExperiment
{
    internal sealed class RealAvatarFixture : IDisposable
    {
        internal readonly GameObject Root;
        internal readonly Transform Head, Neck, Canonical, Proxy;
        internal readonly Camera Camera;
        internal readonly SkinnedMeshRenderer Source;
        internal readonly SkinnedMeshRenderer[] Renderers = new SkinnedMeshRenderer[6];
        internal readonly string[] Names = { "Baseline", "A_HeadRoot", "B_ChildProxyRoot", "B_VrcProxyRoot", "C_RebasedChild", "RendererOnly" };
        internal readonly Matrix4x4[] Calibration = new Matrix4x4[6];
        internal readonly string[] Unsupported = new string[6];
        internal readonly string HeadResolution;
        internal readonly int BlinkIndex;
        internal readonly string BlinkName;
        internal readonly string SelectedPath;
        internal readonly List<UnityEngine.Object> Owned = new List<UnityEngine.Object>();
        private readonly Quaternion headRest, neckRest, rootRest;
        private readonly Vector3 rootScale;
        private readonly SkinnedMeshRenderer[] originals;
        private readonly string[] meshHashes, materialStates;
        private readonly List<Material> sourceMaterials = new List<Material>();
        private readonly float cameraDistance;
        private readonly GameObject cameraObject;

        internal RealAvatarFixture(Scene scene, string assetPath)
        {
            Root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(assetPath));
            SceneManager.MoveGameObjectToScene(Root, scene);
            Animator animator = Root.GetComponent<Animator>();
            if (animator != null) animator.enabled = false;
            Head = RealAvatarAudit.GetHead(Root, out HeadResolution);
            Neck = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Neck) : Head.parent;
            if (Neck == null) throw new InvalidOperationException("Neckを解決できません。");
            headRest = Head.localRotation; neckRest = Neck.localRotation; rootRest = Root.transform.localRotation; rootScale = Root.transform.localScale;
            originals = Root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            meshHashes = new string[originals.Length];
            double bestScore = -1;
            for (int i = 0; i < originals.Length; ++i)
            {
                SkinnedMeshRenderer r = originals[i];
                if (r.sharedMesh == null) continue;
                meshHashes[i] = RealAvatarAudit.Fingerprint(r.sharedMesh);
                JObject weights = JObject.FromObject(RealAvatarAudit.Weights(r, Head, Neck));
                // Blink availability identifies the facial morph renderer; hair can have more Head weight than the face.
                double score = FindBlink(r.sharedMesh) < 0 ? 0 : (double)weights["headBranchSum"];
                if (score > bestScore) { Source = r; bestScore = score; }
                Material[] slots = r.sharedMaterials;
                for (int slot = 0; slot < slots.Length; ++slot)
                    if (slots[slot] != null && !sourceMaterials.Contains(slots[slot])) sourceMaterials.Add(slots[slot]);
                r.enabled = false;
            }
            if (Source == null || bestScore <= 0) throw new InvalidOperationException("Head影響のあるRendererがありません。");
            SelectedPath = RealAvatarAudit.PathOf(Root.transform, Source.transform);
            materialStates = new string[sourceMaterials.Count];
            for (int i = 0; i < sourceMaterials.Count; ++i) materialStates[i] = EditorJsonUtility.ToJson(sourceMaterials[i]);
            BlinkIndex = FindBlink(Source.sharedMesh);
            BlinkName = BlinkIndex < 0 ? null : Source.sharedMesh.GetBlendShapeName(BlinkIndex);
            if (BlinkIndex < 0) throw new InvalidOperationException("通常blink BlendShapeを自動選択できません。監査して明示指定してください。");
            Canonical = Child("VLatCanonicalHead", Head);
            // Static rest calibration: avatar +Z/+Y is an authored convention, independent of bone-local axes.
            Canonical.rotation = Root.transform.rotation;
            Proxy = Child("VLatVrcProxy", Root.transform);
            Proxy.SetPositionAndRotation(Canonical.position, Canonical.rotation);
            VRCParentConstraint constraint = Proxy.gameObject.AddComponent<VRCParentConstraint>();
            constraint.Sources.Add(new VRCConstraintSource(Canonical, 1));
            constraint.GlobalWeight = 1; constraint.IsActive = true; constraint.Locked = true; constraint.SolveInLocalSpace = false;
            constraint.ApplyConfigurationChanges();
            Shader shader = Shader.Find("VLat/Experiments/HeadFrameDiagnostic");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("診断Shaderがありません。");
            for (int i = 0; i < Renderers.Length; ++i)
            {
                Mesh mesh;
                if (i == 4)
                {
                    try { mesh = HeadFrameMath.RebaseClone(Source.sharedMesh, Canonical.worldToLocalMatrix * Source.transform.localToWorldMatrix); }
                    catch (ArgumentException ex) { Unsupported[i] = ex.Message; continue; }
                }
                else mesh = UnityEngine.Object.Instantiate(Source.sharedMesh);
                Owned.Add(mesh);
                Transform t = Child(Names[i], Source.transform.parent);
                t.localPosition = Source.transform.localPosition; t.localRotation = Source.transform.localRotation; t.localScale = Source.transform.localScale;
                if (i == 4 || i == 5) { t.SetParent(Canonical, false); t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; t.localScale = Vector3.one; }
                t.gameObject.layer = 30;
                SkinnedMeshRenderer r = t.gameObject.AddComponent<SkinnedMeshRenderer>();
                r.sharedMesh = mesh; r.bones = Source.bones;
                r.rootBone = i == 0 || i == 5 ? Source.rootBone : (i == 1 ? Head : (i == 3 ? Proxy : Canonical));
                r.quality = Source.quality;
                Material[] sourceSlots = Source.sharedMaterials;
                Material[] slots = new Material[sourceSlots.Length];
                for (int slot = 0; slot < slots.Length; ++slot)
                {
                    // Never assign a diagnostic shader/property to an imported source Material.
                    Material m = sourceSlots[slot] == null ? new Material(shader) : UnityEngine.Object.Instantiate(sourceSlots[slot]);
                    m.shader = shader; m.name = Names[i] + "_Diagnostic_" + slot;
                    HeadFrameMath.SetCalibration(m, Matrix4x4.identity); Owned.Add(m); slots[slot] = m;
                }
                r.sharedMaterials = slots;
                r.updateWhenOffscreen = true;
                r.localBounds = new Bounds(Vector3.zero, Vector3.one * 20); // Deliberate diagnostic bypass, NOT a production bounds solution.
                r.enabled = false; Renderers[i] = r;
            }
            cameraObject = new GameObject("RealAvatarDiagnosticCamera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera = cameraObject.AddComponent<Camera>();
            Camera.enabled = false; Camera.clearFlags = CameraClearFlags.SolidColor; Camera.backgroundColor = Color.clear;
            Camera.nearClipPlane = 0.01f; Camera.farClipPlane = 100; Camera.fieldOfView = 35;
            Camera.allowHDR = true; Camera.allowMSAA = false; Camera.renderingPath = RenderingPath.Forward;
            Camera.cullingMask = 1 << 30;
            Camera.overrideSceneCullingMask = UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(scene);
            cameraDistance = Mathf.Max(0.5f, Vector3.Distance(Head.position, Root.transform.position) * 0.65f);
            SetCamera(0, 0);
        }

        internal void SetPose(int pose)
        {
            Root.transform.localRotation = rootRest; Root.transform.localScale = rootScale;
            Head.localRotation = headRest; Neck.localRotation = neckRest;
            for (int i = 0; i < Renderers.Length; ++i)
                if (Renderers[i] != null) Renderers[i].SetBlendShapeWeight(BlinkIndex, pose == 5 ? 100 : 0);
            if (pose == 1) Head.localRotation = Quaternion.Euler(0, 37, 0) * headRest;
            if (pose == 2) Head.localRotation = Quaternion.Euler(24, -19, 17) * headRest;
            if (pose == 3) Root.transform.localRotation = Quaternion.Euler(0, 63, 0) * rootRest;
            if (pose == 4) { Neck.localRotation = Quaternion.Euler(13, 21, -7) * neckRest; Head.localRotation = Quaternion.Euler(-17, 32, 11) * headRest; }
            if (pose == 6) Root.transform.localScale = rootScale * 0.5f;
            if (pose == 7) Root.transform.localScale = rootScale * 2;
            if (pose >= 5) { Neck.localRotation = Quaternion.Euler(7, -11, 0) * neckRest; Head.localRotation = Quaternion.Euler(-9, 23, 4) * headRest; }
        }

        internal void SetCamera(float yaw, float pitch)
        {
            float y = yaw * Mathf.Deg2Rad, p = pitch * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Sin(y) * Mathf.Cos(p), Mathf.Sin(p), Mathf.Cos(y) * Mathf.Cos(p)) * cameraDistance;
            offset *= Root.transform.localScale.x / rootScale.x;
            Camera.transform.SetPositionAndRotation(Canonical.position + offset, Quaternion.LookRotation(-offset, Vector3.up));
        }

        internal Color[] Read(HeadFrameCapture capture, int candidate, int mode)
        {
            for (int i = 0; i < Renderers.Length; ++i) if (Renderers[i] != null) Renderers[i].enabled = i == candidate;
            foreach (Material m in Renderers[candidate].sharedMaterials) m.SetFloat("_Mode", mode);
            return capture.Read(Camera, Renderers[candidate], mode);
        }

        internal Color Sample(HeadFrameCapture capture, int candidate, int mode)
        {
            foreach (Color pixel in Read(capture, candidate, mode)) if (pixel.a > 0.5f) return pixel;
            throw new InvalidOperationException(Names[candidate] + ": 描画pixelがありません。");
        }

        internal Matrix4x4 ObjectMatrix(HeadFrameCapture capture, int candidate)
        {
            Matrix4x4 matrix = Matrix4x4.identity;
            for (int col = 0; col < 4; ++col)
            {
                Color c = Sample(capture, candidate, col + 2);
                matrix.SetColumn(col, new Vector4(c.r, c.g, c.b, col == 3 ? 1 : 0));
            }
            return matrix;
        }

        internal void Calibrate(HeadFrameCapture capture)
        {
            for (int i = 0; i < Renderers.Length; ++i)
            {
                if (Renderers[i] == null) continue;
                Calibration[i] = Canonical.worldToLocalMatrix * ObjectMatrix(capture, i);
                foreach (Material material in Renderers[i].sharedMaterials) HeadFrameMath.SetCalibration(material, Calibration[i]);
            }
        }

        internal void VerifySources()
        {
            for (int i = 0; i < originals.Length; ++i)
                if (originals[i].sharedMesh != null && meshHashes[i] != RealAvatarAudit.Fingerprint(originals[i].sharedMesh))
                    throw new InvalidOperationException("読み取り専用source Meshが変更されました。");
            for (int i = 0; i < sourceMaterials.Count; ++i)
                if (materialStates[i] != EditorJsonUtility.ToJson(sourceMaterials[i])) throw new InvalidOperationException("source Materialが変更されました。");
        }

        internal void SavePrivatePreview(string output, string avatar)
        {
            // Local-only untextured diagnostic images. Do not redistribute paid avatar geometry/images in the repository.
            Directory.CreateDirectory(Path.Combine(output, "private-preview"));
            SkinnedMeshRenderer r = Renderers[0];
            Material[] diagnostic = r.sharedMaterials;
            Material gray = new Material(Shader.Find("Standard"));
            gray.color = new Color(0.7f, 0.7f, 0.7f); gray.SetFloat("_Glossiness", 0);
            Material[] slots = new Material[diagnostic.Length]; for (int i = 0; i < slots.Length; ++i) slots[i] = gray;
            GameObject lightObject = new GameObject("PrivatePreviewLight");
            SceneManager.MoveGameObjectToScene(lightObject, Root.scene);
            Light light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f;
            light.cullingMask = 1 << 30; light.transform.rotation = Quaternion.Euler(25, 200, 0);
            RenderTexture target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
            Texture2D pixels = new Texture2D(512, 512, TextureFormat.RGB24, false);
            RenderTexture oldActive = RenderTexture.active, oldTarget = Camera.targetTexture;
            try
            {
                for (int i = 0; i < Renderers.Length; ++i) if (Renderers[i] != null) Renderers[i].enabled = i == 0;
                r.sharedMaterials = slots; Camera.targetTexture = target;
                for (int view = 0; view < 2; ++view)
                {
                    SetCamera(view * 45, 0); Camera.Render(); RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); pixels.Apply(false, false);
                    File.WriteAllBytes(Path.Combine(output, "private-preview", avatar + "-" + (view * 45) + ".png"), pixels.EncodeToPNG());
                }
            }
            finally
            {
                r.sharedMaterials = diagnostic; Camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
                UnityEngine.Object.DestroyImmediate(gray); UnityEngine.Object.DestroyImmediate(lightObject);
                target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
                SetCamera(0, 0);
            }
        }

        internal object Setup(string avatar) => new { avatar, selectedRenderer = SelectedPath, selectionRule = "maximum Head branch weight among renderers containing a blink BlendShape",
            HeadResolution, BlinkIndex, BlinkName, vertexCount = Source.sharedMesh.vertexCount,
            materialSlots = Source.sharedMaterials.Length, sourceRoot = Source.rootBone == null ? null : RealAvatarAudit.PathOf(Root.transform, Source.rootBone),
            rebase = Unsupported[4] == null ? "rigid converter accepted" : Unsupported[4],
            rebaseMatrix = MatrixValues(Canonical.worldToLocalMatrix * Source.transform.localToWorldMatrix),
            sourceQuality = Source.quality.ToString(), qualitySkinWeights = QualitySettings.skinWeights.ToString(),
            nonDestructiveFingerprint = "positions/normals/tangents/indices/bindposes/full weights/morph names+frame weights; source FBX SHA-256 covers morph payload",
            canonicalBasis = "avatar rest +Z forward / +Y up; independent visual inspection required", constraint = "direct VRCParentConstraint active/locked/source1/weight1/world space" };

        private static float[] MatrixValues(Matrix4x4 m) { float[] values = new float[16]; for (int i = 0; i < 16; ++i) values[i] = m[i]; return values; }
        private static Transform Child(string name, Transform parent) { Transform t = new GameObject(name).transform; t.SetParent(parent, false); return t; }
        private static int FindBlink(Mesh mesh)
        {
            int fallback = -1;
            for (int s = 0; s < mesh.blendShapeCount; ++s)
            {
                string name = mesh.GetBlendShapeName(s), lower = name.ToLowerInvariant();
                if (lower == "blink" || lower == "vrc.blink" || name == "まばたき") return s;
                if (fallback < 0 && lower.Contains("blink")) fallback = s;
            }
            return fallback;
        }

        /// <summary>実験専用の複製instanceと生成Mesh・Materialを解放します。</summary>
        public void Dispose()
        {
            UnityEngine.Object.DestroyImmediate(Root); UnityEngine.Object.DestroyImmediate(cameraObject);
            foreach (UnityEngine.Object obj in Owned) if (obj != null && !AssetDatabase.Contains(obj)) UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
