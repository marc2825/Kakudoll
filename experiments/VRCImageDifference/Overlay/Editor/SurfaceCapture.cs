using System;
using System.IO;
using UnityEngine;

namespace VLat.HeadFrameExperiment
{
    internal sealed class SurfaceCapture : IDisposable
    {
        internal readonly int Size;
        internal Matrix4x4 LastView, LastProjection;
        private readonly RenderTexture target;
        private readonly Texture2D readback;
        private readonly Shader shader;

        internal SurfaceCapture(int size)
        {
            Size = size;
            shader = Shader.Find("VLat/Experiments/SurfaceProbe");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Surface probe is unavailable.");
            target = new RenderTexture(size, size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
            target.Create();
            readback = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
        }

        internal Color[] Read(RealAvatarFixture fixture, int candidate, int mode, int selectedSlot = -1,
            int selectedPrimitive = -1, bool worldClip = false, int cull = 0, int ztest = 4)
        {
            for (int i = 0; i < fixture.Renderers.Length; ++i)
                if (fixture.Renderers[i] != null) fixture.Renderers[i].enabled = i == candidate;
            SkinnedMeshRenderer r = fixture.Renderers[candidate];
            Material[] original = r.sharedMaterials;
            Material[] materials = new Material[original.Length];
            for (int slot = 0; slot < materials.Length; ++slot)
            {
                Material m = new Material(shader);
                m.SetFloat("_Mode", mode); m.SetFloat("_Slot", slot);
                m.SetFloat("_Select", selectedSlot < 0 ? -1 : (slot == selectedSlot ? selectedPrimitive : 16777215));
                m.SetFloat("_WorldClip", worldClip ? 1 : 0); m.SetFloat("_Cull", cull); m.SetFloat("_ZTest", ztest);
                materials[slot] = m;
            }
            r.sharedMaterials = materials;
            try { return Render(fixture.Camera); }
            finally
            {
                r.sharedMaterials = original;
                foreach (Material m in materials) UnityEngine.Object.DestroyImmediate(m);
            }
        }

        internal Color[] ReadBaked(RealAvatarFixture f, int candidate)
        {
            SkinnedMeshRenderer source = f.Renderers[candidate];
            Mesh mesh = new Mesh(); source.BakeMesh(mesh);
            GameObject go = new GameObject("StaticBakeProbe"); go.layer = 30;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, f.Root.scene);
            // BakeMesh(false) snapshot already includes scale in this measured Unity backend.
            // Restore a rigid renderer frame; parenting under the scaled renderer would apply it twice.
            go.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            Material material = new Material(shader); material.SetFloat("_Mode", 0);
            Material[] slots = new Material[mesh.subMeshCount];
            for (int i = 0; i < slots.Length; ++i) slots[i] = material;
            renderer.sharedMaterials = slots;
            for (int i = 0; i < f.Renderers.Length; ++i) if (f.Renderers[i] != null) f.Renderers[i].enabled = false;
            try { return Render(f.Camera); }
            finally { UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(material); }
        }

        private Color[] Render(Camera camera)
        {
            RenderTexture oldTarget = camera.targetTexture, oldActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                LastView = camera.worldToCameraMatrix; LastProjection = camera.projectionMatrix;
                camera.Render(); RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); readback.Apply(false, false);
                return readback.GetPixels();
            }
            finally { camera.targetTexture = oldTarget; RenderTexture.active = oldActive; }
        }

        internal void SavePrivate(string folder, string name, Color[] data, int visualization)
        {
            Directory.CreateDirectory(folder);
            // Paid-avatar surface images and raw buffers stay outside the repository.
            using (BinaryWriter writer = new BinaryWriter(File.Create(Path.Combine(folder, name + ".rgba32f"))))
                foreach (Color c in data) { writer.Write(c.r); writer.Write(c.g); writer.Write(c.b); writer.Write(c.a); }
            Texture2D image = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
            Color[] colors = new Color[data.Length];
            for (int i = 0; i < data.Length; ++i)
            {
                Color c = data[i];
                if (c.a <= 0.5f) colors[i] = Color.clear;
                else if (visualization == 1) colors[i] = new Color(Mathf.Repeat(c.r * 0.1031f + c.g * 0.37f, 1), Mathf.Repeat(c.r * 0.11369f, 1), Mathf.Repeat(c.r * 0.13787f, 1), 1);
                else if (visualization == 3) colors[i] = new Color(c.r * 0.5f + 0.5f, c.g * 0.5f + 0.5f, c.b * 0.5f + 0.5f, 1);
                else colors[i] = c;
            }
            image.SetPixels(colors); image.Apply();
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }

        /// <summary>診断用GPU資源を解放します。</summary>
        public void Dispose()
        {
            target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(readback);
        }
    }
}
