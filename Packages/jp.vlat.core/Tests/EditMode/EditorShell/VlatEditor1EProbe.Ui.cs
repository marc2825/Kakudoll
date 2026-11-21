using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VLat.Editor.Authoring;
using Object = UnityEngine.Object;

namespace VLat.Tests.Edit
{
    public static partial class VlatEditor1EProbe
    {
        /// <summary>
        /// 隔離プロジェクトで実EditorWindowを代表的な状態にし、画面を画像として保存します。
        /// 見た目確認用の診断であり、画素比較の合否判定には使用しません。
        /// </summary>
        public static void UiSnapshot() => Start(UiRoutine(), "ui-snapshot");

        private static IEnumerator UiRoutine()
        {
            var shots = new JArray(); Report["snapshots"] = shots;
            using var f = new VlatKeyformPreviewFixture(true);
            f.Preview.Dispose(); f.Session.NewDocument(VlatCloseDecision.Discard);
            ConfigureLargeFixture(f, 96, 48, 1);
            var window = VlatEditorWindow.CreateForRecoveryDirectory(Path.Combine(f.DirectoryPath, "ui-window"));
            window.position = new Rect(20, 20, 1440, 920); window.Show(); window.Focus();
            try
            {
                for (var i = 0; i < 4; i++) yield return null;
                Capture(window, "01-empty-document", shots);
                var p = window.PreviewController; var s = window.DocumentController;
                f.Adopt(p); window.CreateGUI();
                for (var i = 0; i < 4; i++) yield return null;
                Capture(window, "02-vrm-loaded", shots);
                Assert.That(s.AddKeyformGroup(), Is.True);
                Assert.That(s.BindKeyformTarget(0, f.Source.gameObject, f.SourceRenderer), Is.True, s.LastMessage);
                Assert.That(s.AddKeyform(0, 45), Is.True); Assert.That(s.AddKeyform(0, 90), Is.True);
                Assert.That(s.ImportKeyformPositions(0, 1, f.Source.gameObject, f.SourceRenderer, 0), Is.True, s.LastMessage);
                window.CreateGUI();
                window.rootVisualElement.Q<ListView>("keyform-list").selectedIndex = 1;
                for (var i = 0; i < 4; i++) yield return null;
                Capture(window, "03-key-selected", shots);
                window.rootVisualElement.Q<Toggle>("geometry-selection-toggle").value = true;
                PrepareBenchSelection(p, 30);
                for (var i = 0; i < 4; i++) yield return null;
                Capture(window, "04-vertex-selection", shots);
                window.rootVisualElement.Q<Toggle>("keyform-drag-toggle").value = true;
                for (var i = 0; i < 4; i++) yield return null;
                Capture(window, "05-drag-mode", shots);
            }
            finally { window.DiscardChanges(); window.Close(); }
        }

        private static void Capture(EditorWindow window, string name, JArray shots)
        {
            window.Repaint();
            var width = (int)window.position.width; var height = (int)window.position.height;
            var entry = new JObject { ["name"] = name, ["width"] = width, ["height"] = height };
            shots.Add(entry);
            var parent = typeof(EditorWindow).GetField("m_Parent", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window);
            MethodInfo grab = null;
            for (var type = parent?.GetType(); type != null && grab == null; type = type.BaseType)
                grab = type.GetMethod("GrabPixels", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (grab == null) { entry["status"] = "NOT RUN: GUIView.GrabPixels unavailable"; return; }
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "VLat UI Snapshot" };
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                grab.Invoke(parent, new object[] { target, new Rect(0, 0, width, height) });
                var previous = RenderTexture.active; RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                RenderTexture.active = previous;
                // GrabPixelsのRTは上下が逆に読める。PNGを画面と同じ向きにする。
                var pixels = texture.GetPixels32(); var flipped = new Color32[pixels.Length];
                for (var y = 0; y < height; y++) Array.Copy(pixels, y * width, flipped, (height - 1 - y) * width, width);
                texture.SetPixels32(flipped); texture.Apply(); pixels = flipped; var distinct = new System.Collections.Generic.HashSet<int>();
                for (var i = 0; i < pixels.Length && distinct.Count < 64; i += 97) distinct.Add(pixels[i].r << 16 | pixels[i].g << 8 | pixels[i].b);
                entry["sampledDistinctColors"] = distinct.Count;
                File.WriteAllBytes(Path.Combine(Output, name + ".png"), texture.EncodeToPNG());
                entry["status"] = distinct.Count > 4 ? "CAPTURED" : "BLANK";
            }
            catch (Exception exception) { entry["status"] = "FAIL: " + exception.GetBaseException().Message; }
            finally { Object.DestroyImmediate(texture); target.Release(); Object.DestroyImmediate(target); }
        }
    }
}
