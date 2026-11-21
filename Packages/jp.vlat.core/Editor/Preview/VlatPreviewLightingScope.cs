using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace VLat.Editor.Authoring
{
    // Unity 2022.3自身のPreviewRenderUtilityと同じoverride境界を使う。
    // 編集Sceneのskybox/非同期ambient bakeをPreviewへ混ぜず、Sceneへ書き戻さない。
    internal readonly struct VlatPreviewLightingScope : IDisposable
    {
        private readonly bool _active;
        internal VlatPreviewLightingScope(Scene scene)
        {
            _active = Unsupported.SetOverrideLightingSettings(scene);
            if (!_active) throw new InvalidOperationException("Preview専用Lightingを設定できません。");
            try
            {
                var ambient = new Color(.2f, .2f, .2f, 1f);
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = ambient;
                var probe = new SphericalHarmonicsL2(); probe.AddAmbientLight(ambient);
                RenderSettings.ambientProbe = probe;
                RenderSettings.reflectionIntensity = 0;
                RenderSettings.fog = false;
            }
            catch { Unsupported.RestoreOverrideLightingSettings(); throw; }
        }
        /// <summary>例外時も編集SceneのLighting設定へ復帰します。</summary>
        public void Dispose() { if (_active) Unsupported.RestoreOverrideLightingSettings(); }
    }
}
