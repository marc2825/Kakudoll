using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace VLat.HeadFrameExperiment
{
    /// <summary>隔離projectの診断Shaderのcompile状態を読取記録します。</summary>
    public static class ImageDifferenceAudit
    {
        /// <summary>計測終了後にbatchmodeと-quitで実行し、新規JSONへShaderのmessageを保存します。</summary>
        public static void RunBatch()
        {
            if (!Application.isBatchMode || !File.Exists("image-fail-inputs.json"))
                throw new InvalidOperationException("調査用の隔離project専用です。");
            string output = "image-shader-audit.json";
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i) if (args[i] == "-imageAuditOutput") output = args[i + 1];
            if (File.Exists(output)) throw new InvalidOperationException("既存auditの上書きを拒否します。");
            List<object> shaders = new List<object>();
            foreach (string name in new[] { "HeadFrameDiagnostic", "SurfaceProbe", "ClipVertexProbe" })
            {
                Shader shader = Shader.Find("VLat/Experiments/" + name);
                if (shader == null || !shader.isSupported) throw new InvalidOperationException("Shader unavailable: " + name);
                shaders.Add(new { name, messages = ShaderUtil.GetShaderMessages(shader) });
            }
            File.WriteAllText(output, JsonConvert.SerializeObject(new { unity = Application.unityVersion,
                gpu = SystemInfo.graphicsDeviceName, api = SystemInfo.graphicsDeviceType.ToString(),
                reversedZ = SystemInfo.usesReversedZBuffer, gpuSkinningSetting = PlayerSettings.gpuSkinning,
                qualitySkinWeights = QualitySettings.skinWeights.ToString(), shaders }, Formatting.Indented));
        }
    }
}
