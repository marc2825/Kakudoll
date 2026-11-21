using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.SceneManagement;

namespace VLat.HeadFrameExperiment
{
    internal sealed class HeadFrameFixture : IDisposable
    {
        internal readonly GameObject Root;
        internal readonly Transform Body, Neck, Head, Canonical, ChildProxy, ConstrainedProxy;
        internal readonly Camera Camera;
        internal readonly SkinnedMeshRenderer[] Renderers = new SkinnedMeshRenderer[7];
        internal readonly string[] Names = { "Baseline", "A_HeadRoot", "B_ChildProxyRoot", "B_ConstraintProxyRoot", "B_RendererOnly", "C_RebasedChild", "C_RebasedConstraint" };
        internal readonly Quaternion HeadRest;
        internal readonly Mesh Source;
        internal readonly List<UnityEngine.Object> Owned = new List<UnityEngine.Object>();
        internal readonly Matrix4x4[] Calibration = new Matrix4x4[7];
        internal static readonly string[] Poses = { "Rest", "HeadYaw", "HeadPitchRoll", "BodyYaw", "NeckHead", "BlendShape", "HalfScale", "DoubleScale", "NonUniform", "NegativeScale" };

        internal HeadFrameFixture(Scene scene, bool alternateAxes)
        {
            Root = new GameObject("HeadFrameFixture");
            SceneManager.MoveGameObjectToScene(Root, scene);
            Root.transform.position = new Vector3(1.1f, 0, -0.7f);
            Body = Child("Body", Root.transform, Vector3.zero);
            Neck = Child("Neck", Body, new Vector3(0, 1.2f, 0));
            Head = Child("Head", Neck, new Vector3(0, 0.4f, 0));
            HeadRest = alternateAxes ? Quaternion.Euler(90, 0, 90) : Quaternion.identity;
            Head.localRotation = HeadRest;
            Canonical = Child("CanonicalReference", Head, Vector3.zero);
            Canonical.localRotation = Quaternion.Inverse(HeadRest);
            ChildProxy = Child("ChildProxy", Head, Vector3.zero);
            ChildProxy.localRotation = Quaternion.Inverse(HeadRest);
            ConstrainedProxy = Child("ConstrainedProxy", Root.transform, Vector3.zero);
            ConstrainedProxy.SetPositionAndRotation(Canonical.position, Canonical.rotation);
            ParentConstraint constraint = ConstrainedProxy.gameObject.AddComponent<ParentConstraint>();
            constraint.AddSource(new ConstraintSource { sourceTransform = Canonical, weight = 1 });
            constraint.weight = 1;
            constraint.locked = true;
            constraint.constraintActive = true;

            Transform sourceTransform = Child("SourceRenderer", Root.transform, new Vector3(0.23f, 0.11f, -0.17f));
            sourceTransform.localRotation = Quaternion.Euler(11, -23, 7);
            Source = CreateMesh(sourceTransform);
            Owned.Add(Source);
            Shader shader = Shader.Find("VLat/Experiments/HeadFrameDiagnostic");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("診断Shaderが利用できません。");
            for (int i = 0; i < Renderers.Length; ++i)
            {
                Transform t = Child(Names[i], Root.transform, sourceTransform.localPosition);
                t.localRotation = sourceTransform.localRotation;
                bool rebase = i >= 5;
                Transform proxy = i == 6 ? ConstrainedProxy : ChildProxy;
                if (i == 4 || rebase)
                {
                    t.SetParent(proxy, false);
                    t.localPosition = Vector3.zero;
                    t.localRotation = Quaternion.identity;
                }
                SkinnedMeshRenderer renderer = t.gameObject.AddComponent<SkinnedMeshRenderer>();
                renderer.bones = new[] { Neck, Head };
                renderer.rootBone = i == 0 || i == 4 ? Body : (i == 1 ? Head : (i == 3 || i == 6 ? ConstrainedProxy : ChildProxy));
                // Every candidate gets its own mesh and material; Source stays read-only.
                Mesh mesh = rebase
                    ? HeadFrameMath.RebaseClone(Source, proxy.worldToLocalMatrix * sourceTransform.localToWorldMatrix)
                    : UnityEngine.Object.Instantiate(Source);
                Owned.Add(mesh);
                renderer.sharedMesh = mesh;
                Material material = new Material(shader) { name = Names[i] + "_Diagnostic" };
                HeadFrameMath.SetCalibration(material, Matrix4x4.identity);
                Owned.Add(material);
                renderer.sharedMaterial = material;
                renderer.quality = SkinQuality.Bone4;
                renderer.updateWhenOffscreen = true;
                // Synthetic fixture only. Real avatar bounds/culling is a separate gate.
                renderer.localBounds = new Bounds(Vector3.zero, Vector3.one * 4);
                renderer.enabled = false;
                Renderers[i] = renderer;
            }
            UnityEngine.Object.DestroyImmediate(sourceTransform.gameObject);
            Transform camera = Child("DiagnosticCamera", Root.transform, Vector3.zero);
            Camera = camera.gameObject.AddComponent<Camera>();
            Camera.enabled = false;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = Color.clear;
            Camera.nearClipPlane = 0.01f;
            Camera.farClipPlane = 30;
            Camera.fieldOfView = 35;
            Camera.allowHDR = true;
            Camera.allowMSAA = false;
            Camera.renderingPath = RenderingPath.Forward;
            Camera.cullingMask = ~0;
            Camera.overrideSceneCullingMask = UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(scene);
            SetCamera(0, 0);
        }

        internal void SetPose(int pose)
        {
            Root.transform.localScale = Vector3.one;
            Body.localRotation = Quaternion.identity;
            Neck.localRotation = Quaternion.identity;
            Head.localRotation = HeadRest;
            for (int i = 0; i < Renderers.Length; ++i) Renderers[i].SetBlendShapeWeight(0, pose == 5 ? 100 : 0);
            if (pose == 1) Head.localRotation = Quaternion.Euler(0, 37, 0) * HeadRest;
            if (pose == 2) Head.localRotation = Quaternion.Euler(24, -19, 17) * HeadRest;
            if (pose == 3) Body.localRotation = Quaternion.Euler(0, 63, 0);
            if (pose == 4)
            {
                Neck.localRotation = Quaternion.Euler(13, 21, -7);
                Head.localRotation = Quaternion.Euler(-17, 32, 11) * HeadRest;
            }
            if (pose == 6) Root.transform.localScale = Vector3.one * 0.5f;
            if (pose == 7) Root.transform.localScale = Vector3.one * 2;
            if (pose == 5 || pose == 6 || pose == 7)
            {
                Neck.localRotation = Quaternion.Euler(7, -11, 0);
                Head.localRotation = Quaternion.Euler(-9, 23, 4) * HeadRest;
            }
            if (pose == 8)
            {
                Root.transform.localScale = new Vector3(1, 1.5f, 0.7f);
                Head.localRotation = Quaternion.Euler(17, 29, 11) * HeadRest;
            }
            if (pose == 9) Root.transform.localScale = new Vector3(-1, 1, 1);
        }

        internal void SetCamera(float yaw, float pitch)
        {
            // World orbit, deliberately NOT orbiting in the moving Head frame.
            float y = yaw * Mathf.Deg2Rad, p = pitch * Mathf.Deg2Rad;
            float distance = 2 * Mathf.Max(Mathf.Abs(Root.transform.localScale.x), Mathf.Abs(Root.transform.localScale.y), Mathf.Abs(Root.transform.localScale.z));
            Vector3 offset = new Vector3(Mathf.Sin(y) * Mathf.Cos(p), Mathf.Sin(p), Mathf.Cos(y) * Mathf.Cos(p)) * distance;
            Camera.transform.position = Canonical.position + offset;
            Camera.transform.rotation = Quaternion.LookRotation(-offset, Vector3.up);
        }

        internal void Show(int candidate)
        {
            for (int i = 0; i < Renderers.Length; ++i) Renderers[i].enabled = i == candidate;
        }

        private Mesh CreateMesh(Transform renderer)
        {
            Vector3[] p = { new Vector3(-0.23f, 0, 0), new Vector3(0.23f, 0, 0), new Vector3(0, 0.26f, 0), new Vector3(0, -0.26f, 0), new Vector3(0, 0, 0.2f), new Vector3(0, 0, -0.2f) };
            BoneWeight[] weights = new BoneWeight[p.Length];
            Vector3[] delta = new Vector3[p.Length];
            for (int i = 0; i < p.Length; ++i)
            {
                p[i] = renderer.InverseTransformPoint(Canonical.TransformPoint(p[i]));
                weights[i] = i == 3
                    ? new BoneWeight { boneIndex0 = 1, weight0 = 0.5f, boneIndex1 = 0, weight1 = 0.5f }
                    : new BoneWeight { boneIndex0 = 1, weight0 = 1 };
                delta[i] = renderer.InverseTransformVector(new Vector3(i == 4 ? 0.03f : 0, i == 2 ? 0.02f : 0, 0));
            }
            Mesh mesh = new Mesh { name = "SyntheticHead_Source", vertices = p, boneWeights = weights };
            mesh.triangles = new[] { 0,2,4, 4,2,1, 1,2,5, 5,2,0, 4,3,0, 1,3,4, 5,3,1, 0,3,5 };
            mesh.bindposes = new[] { Neck.worldToLocalMatrix * renderer.localToWorldMatrix, Head.worldToLocalMatrix * renderer.localToWorldMatrix };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.AddBlendShapeFrame("ExpressionProbe", 100, delta, new Vector3[p.Length], new Vector3[p.Length]);
            return mesh;
        }

        private static Transform Child(string name, Transform parent, Vector3 position)
        {
            Transform t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = position;
            return t;
        }

        /// <summary>実験専用に生成したオブジェクトとMesh・Materialを破棄します。</summary>
        public void Dispose()
        {
            UnityEngine.Object.DestroyImmediate(Root);
            for (int i = 0; i < Owned.Count; ++i)
                if (Owned[i] != null && !UnityEditor.AssetDatabase.Contains(Owned[i])) UnityEngine.Object.DestroyImmediate(Owned[i]);
        }
    }
}
