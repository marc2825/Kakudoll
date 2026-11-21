using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VLat.HeadFrameExperiment
{
    internal sealed class RealAvatarVertexProbe : IDisposable
    {
        private readonly RenderTexture target;
        private readonly Texture2D readback;
        private readonly Material material;
        private const int Side = 128;

        internal RealAvatarVertexProbe()
        {
            Shader shader = Shader.Find("VLat/Experiments/RealAvatarVertexProbe");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Vertex probe Shaderが利用できません。");
            target = new RenderTexture(Side, Side, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
            target.Create(); readback = new Texture2D(Side, Side, TextureFormat.RGBAFloat, false, true);
            material = new Material(shader); material.SetFloat("_Side", Side);
        }

        internal Vector3[] Read(RealAvatarFixture fixture, int candidate)
        {
            SkinnedMeshRenderer source = fixture.Renderers[candidate];
            if (source.sharedMesh.vertexCount > Side * Side) throw new InvalidOperationException("Vertex probeの容量を超えています。");
            // Topology-only clone. Positions, bindposes, full weights and morph payload retain the candidate's data.
            Mesh mesh = UnityEngine.Object.Instantiate(source.sharedMesh);
            int[] indices = new int[mesh.vertexCount]; for (int i = 0; i < indices.Length; ++i) indices[i] = i;
            mesh.subMeshCount = 1; mesh.SetIndices(indices, MeshTopology.Points, 0, false);
            GameObject go = new GameObject("VertexProbe"); go.transform.SetParent(source.transform, false); go.layer = 30;
            SkinnedMeshRenderer renderer = go.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh; renderer.sharedMaterial = material; renderer.bones = source.bones; renderer.rootBone = source.rootBone;
            renderer.quality = source.quality; renderer.updateWhenOffscreen = true; renderer.localBounds = source.localBounds;
            for (int shape = 0; shape < mesh.blendShapeCount; ++shape) renderer.SetBlendShapeWeight(shape, source.GetBlendShapeWeight(shape));
            for (int i = 0; i < fixture.Renderers.Length; ++i) if (fixture.Renderers[i] != null) fixture.Renderers[i].enabled = false;
            RenderTexture oldActive = RenderTexture.active, oldTarget = fixture.Camera.targetTexture;
            try
            {
                fixture.Camera.targetTexture = target; fixture.Camera.Render(); RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Side, Side), 0, 0); readback.Apply(false, false);
                Color[] pixels = readback.GetPixels(); Vector3[] result = new Vector3[mesh.vertexCount]; bool[] seen = new bool[result.Length];
                int count = 0;
                foreach (Color c in pixels)
                {
                    if (c.a < 0.5f) continue;
                    int id = Mathf.RoundToInt(c.a) - 1;
                    if (id < 0 || id >= result.Length || seen[id] || Mathf.Abs(c.a - (id + 1)) > 0.001f)
                        throw new InvalidOperationException("GPU vertex IDが不正または重複しています。");
                    seen[id] = true; ++count; result[id] = new Vector3(c.r, c.g, c.b);
                }
                if (count != result.Length) throw new InvalidOperationException("全vertexのGPU readbackが得られません。");
                return result;
            }
            finally
            {
                fixture.Camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
                UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        internal static float MaxError(Vector3[] baseline, Vector3[] actual)
        {
            if (baseline.Length != actual.Length) throw new InvalidOperationException("vertex countが一致しません。");
            float maximum = 0;
            for (int i = 0; i < baseline.Length; ++i)
            {
                float distance = Vector3.Distance(baseline[i], actual[i]);
                if (float.IsNaN(distance) || float.IsInfinity(distance)) throw new InvalidOperationException("vertex位置が非有限です。");
                maximum = Mathf.Max(maximum, distance);
            }
            return maximum;
        }

        /// <summary>全頂点probeのGPU資源と生成Materialを解放します。</summary>
        public void Dispose()
        {
            target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(readback); UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
