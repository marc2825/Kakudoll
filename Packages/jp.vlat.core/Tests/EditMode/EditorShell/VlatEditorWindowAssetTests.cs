using NUnit.Framework;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VLat.Editor.Authoring;

namespace VLat.Tests.Edit
{
    /// <summary>Phase 2B UI Toolkit shell assetの構造を検証します。</summary>
    public sealed class VlatEditorWindowAssetTests
    {
        /// <summary>toolbar、hierarchy、inspector、validation領域がUXMLに存在することを検証します。</summary>
        [Test]
        public void Uxml_ContainsRequiredEditorShellControls()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/jp.vlat.core/Editor/UI/VlatEditorWindow.uxml");
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/jp.vlat.core/Editor/UI/VlatEditorWindow.uss");
            Assert.That(tree, Is.Not.Null);
            Assert.That(style, Is.Not.Null);

            var root = new VisualElement();
            tree.CloneTree(root);

            Assert.That(root.Q<Button>("new-button"), Is.Not.Null);
            Assert.That(root.Q<Button>("open-button"), Is.Not.Null);
            Assert.That(root.Q<Button>("save-button"), Is.Not.Null);
            Assert.That(root.Q<Button>("save-as-button"), Is.Not.Null);
            Assert.That(root.Q<Button>("undo-button"), Is.Not.Null);
            Assert.That(root.Q<Button>("redo-button"), Is.Not.Null);
            Assert.That(root.Q<ListView>("band-list"), Is.Not.Null);
            Assert.That(root.Q<ListView>("layer-list"), Is.Not.Null);
            Assert.That(root.Q<ListView>("modifier-list"), Is.Not.Null);
            Assert.That(root.Q<ScrollView>("inspector-pane"), Is.Not.Null);
            Assert.That(root.Q<ListView>("diagnostic-list"), Is.Not.Null);
            Assert.That(root.Q<Button>("select-vrm-button"), Is.Not.Null);
            Assert.That(root.Q<Image>("preview-main"), Is.Not.Null);
            Assert.That(root.Q<Image>("preview-sub-a"), Is.Not.Null);
            Assert.That(root.Q<Image>("preview-sub-b"), Is.Not.Null);
            Assert.That(root.Q<FloatField>("preview-yaw"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("start-screen"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("angle-rail-host"), Is.Not.Null);
            Assert.That(root.Q<Toggle>("tool-view-toggle"), Is.Not.Null);
            Assert.That(root.Q<Toggle>("keyform-drag-toggle"), Is.Not.Null);
            Assert.That(root.Q<Button>("add-keyform-button"), Is.Not.Null);
            Assert.That(root.Q<Button>("start-editing-button"), Is.Not.Null);
            Assert.That(root.Q<ScrollView>("details-pane"), Is.Not.Null);
            Assert.That(root.Q<Toggle>("preview-after-toggle"), Is.Not.Null);
            Assert.That(root.Q<DropdownField>("expression-dropdown"), Is.Not.Null);
            Assert.That(root.Q<Toggle>("geometry-selection-toggle"), Is.Not.Null);
            Assert.That(root.Q<DropdownField>("vertex-target-dropdown"), Is.Not.Null);
            Assert.That(root.Q<DropdownField>("vertex-tool-dropdown"), Is.Not.Null);
            Assert.That(root.Q<DropdownField>("vertex-visibility-dropdown"), Is.Not.Null);
            Assert.That(root.Q<Slider>("vertex-brush-radius"), Is.Not.Null);
            Assert.That(root.Q<Slider>("vertex-soft-radius"), Is.Not.Null);
            Assert.That(root.Q<Image>("selection-overlay"), Is.Not.Null);
        }

        /// <summary>実EditorWindowがUXMLをcloneし、必須領域を構築できることを検証します。</summary>
        [Test]
        public void EditorWindow_CreateGuiBuildsShell()
        {
            var window = ScriptableObject.CreateInstance<VlatEditorWindow>();
            try
            {
                window.CreateGUI();

                Assert.That(window.rootVisualElement.Q<Button>("new-button"), Is.Not.Null);
                Assert.That(window.rootVisualElement.Q<ScrollView>("inspector-pane"), Is.Not.Null);
                Assert.That(window.rootVisualElement.Q<Label>("status-label"), Is.Not.Null);
                Assert.That(window.rootVisualElement.Q<Image>("preview-main"), Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        /// <summary>UI Imageが最新のController-owned RenderTextureを直接参照することを検証します。</summary>
        [Test]
        public void PreviewImage_TracksCurrentRenderTextureAcrossResize()
        {
            var window = ScriptableObject.CreateInstance<VlatEditorWindow>();
            try
            {
                window.CreateGUI();
                var previewField = typeof(VlatEditorWindow).GetField(
                    "_preview",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var refreshMethod = typeof(VlatEditorWindow).GetMethod(
                    "RefreshPreviewUi",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(previewField, Is.Not.Null);
                Assert.That(refreshMethod, Is.Not.Null);
                var preview = previewField.GetValue(window) as VlatPreviewController;
                Assert.That(preview, Is.Not.Null);

                var imageNames = new[] { "preview-main", "preview-sub-a", "preview-sub-b" };
                for (var index = 0; index < imageNames.Length; index++)
                {
                    preview.ResizeView((VlatPreviewView)index, 160f, 120f, 1f);
                }

                refreshMethod.Invoke(window, null);
                for (var index = 0; index < imageNames.Length; index++)
                {
                    var assignedImage = window.rootVisualElement.Q<Image>(imageNames[index]);
                    Assert.That(
                        assignedImage.image,
                        Is.SameAs(preview.GetTexture((VlatPreviewView)index)));
                    Assert.That(assignedImage.scaleMode, Is.EqualTo(ScaleMode.ScaleToFit));
                }

                var image = window.rootVisualElement.Q<Image>(imageNames[0]);
                var first = preview.GetTexture(VlatPreviewView.Main);

                preview.ResizeView(VlatPreviewView.Main, 320f, 180f, 1f);
                refreshMethod.Invoke(window, null);
                var second = preview.GetTexture(VlatPreviewView.Main);
                Assert.That(second, Is.Not.SameAs(first));
                Assert.That(first == null, Is.True);
                Assert.That(image.image, Is.SameAs(second));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }
    }
}
