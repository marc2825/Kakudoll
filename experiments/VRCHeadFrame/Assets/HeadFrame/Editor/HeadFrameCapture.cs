using System;
using UnityEngine;

namespace VLat.HeadFrameExperiment
{
    internal sealed class HeadFrameCapture : IDisposable
    {
        private readonly RenderTexture target;
        private readonly Texture2D readback;
        internal const int Size = 64;

        internal HeadFrameCapture()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat) ||
                !SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat))
                throw new InvalidOperationException("GPU浮動小数点readbackを実行できません。-nographicsは使用しないでください。");
            target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
            target.Create();
            readback = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
        }

        internal Color[] Read(HeadFrameFixture f, int candidate, int mode)
        {
            f.Show(candidate);
            return Read(f.Camera, f.Renderers[candidate], mode);
        }

        internal Color[] Read(Camera camera, SkinnedMeshRenderer renderer, int mode)
        {
            renderer.sharedMaterial.SetFloat("_Mode", mode);
            RenderTexture oldActive = RenderTexture.active;
            RenderTexture oldTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                readback.Apply(false, false);
                return readback.GetPixels();
            }
            finally
            {
                camera.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
            }
        }

        internal Color Sample(HeadFrameFixture f, int candidate, int mode)
        {
            Color[] pixels = Read(f, candidate, mode);
            // Select a covered pixel rather than assuming the head pivot is inside the mesh.
            for (int i = 0; i < pixels.Length; ++i)
                if (pixels[i].a > 0.5f) return pixels[i];
            throw new InvalidOperationException(f.Names[candidate] + ": 描画pixelがありません。");
        }

        internal Matrix4x4 ObjectMatrix(HeadFrameFixture f, int candidate)
        {
            Matrix4x4 matrix = Matrix4x4.identity;
            for (int column = 0; column < 4; ++column)
            {
                Color c = Sample(f, candidate, column + 2);
                matrix.SetColumn(column, new Vector4(c.r, c.g, c.b, column == 3 ? 1 : 0));
            }
            return matrix;
        }

        internal static Vector2 Decode(Color c)
        {
            if (c.b < 0.5f || !Finite(c.r) || !Finite(c.g)) throw new InvalidOperationException("Shader角度が無効です。");
            return new Vector2((c.r - 0.5f) * 360, (c.g - 0.5f) * 180);
        }

        internal static void CompareGeometry(Color[] baseline, Color[] actual, out int coverageMismatch, out float worldError, out int covered)
        {
            coverageMismatch = 0;
            worldError = 0;
            covered = 0;
            for (int i = 0; i < actual.Length; ++i)
            {
                bool a = baseline[i].a > 0.5f, b = actual[i].a > 0.5f;
                if (a != b) ++coverageMismatch;
                if (!a || !b) continue;
                ++covered;
                Vector3 delta = new Vector3(baseline[i].r - actual[i].r, baseline[i].g - actual[i].g, baseline[i].b - actual[i].b);
                if (!Finite(delta.x) || !Finite(delta.y) || !Finite(delta.z)) throw new InvalidOperationException("GPU頂点位置が非有限です。");
                worldError = Mathf.Max(worldError, delta.magnitude);
            }
            if (covered == 0) throw new InvalidOperationException("形状比較の共通描画pixelがありません。");
        }

        private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        /// <summary>実験用RenderTextureとreadback Textureを解放します。</summary>
        public void Dispose()
        {
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(readback);
        }
    }
}
