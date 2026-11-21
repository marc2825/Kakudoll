using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VLat.Editor.Authoring;
using Object = UnityEngine.Object;

namespace VLat.Tests.Edit
{
    /// <summary>実Domain ReloadでWindow所有のKeyform Preview資源が解放されることを確認します。</summary>
    public sealed class VlatKeyformPreviewReloadTests
    {
        private static readonly string Domain = Guid.NewGuid().ToString("N");
        [SerializeField] private string _beforeDomain;
        [SerializeField] private VlatEditorWindow _window;
        [SerializeField] private int[] _sourceIds;
        [SerializeField] private int _generatedId;
        [SerializeField] private int _targetId;
        [SerializeField] private Scene _scene;
        [SerializeField] private int _sceneHandle;
        [SerializeField] private string _directory;
        [SerializeField] private string _fingerprint;

        /// <summary>実script reloadを通し、旧Mesh/RT/Scene消失、Document復元、再接続可能な空Previewを検証します。</summary>
        [UnityTest]
        public IEnumerator ActualDomainReload_ReleasesKeyformPreviewAndRestoresDocument()
        {
            var f = new VlatKeyformPreviewFixture();
            _directory = f.DirectoryPath;
            _sourceIds = new[] { f.Source.gameObject.GetInstanceID(), f.Mesh.GetInstanceID(), f.Material.GetInstanceID(),
                f.Source.Vrm.GetInstanceID(), f.Source.Vrm.Expression.Happy.GetInstanceID(), f.Source.Vrm.Expression.Aa.GetInstanceID() };
            _window = VlatEditorWindow.CreateForRecoveryDirectory(Path.Combine(_directory, "reload-window"));
            _window.Show(); yield return null;
            f.Seed(_window.DocumentController); f.Adopt(_window.PreviewController); _window.CreateGUI();
            _fingerprint = _window.DocumentController.ContentFingerprint;
            _generatedId = f.Renderer.sharedMesh.GetInstanceID();
            _targetId = _window.PreviewController.GetTexture(VlatPreviewView.Main).GetInstanceID();
            _scene = _window.PreviewController.PreviewScene; _sceneHandle = _scene.handle;
            // fixture自身の別session/previewだけを先に解放。Windowの資源は実Reloadに任せる。
            f.Preview.Dispose(); f.Session.Dispose();
            _beforeDomain = Domain;
            EditorUtility.RequestScriptReload(); yield return new WaitForDomainReload(); yield return null;
            Assert.That(Domain, Is.Not.EqualTo(_beforeDomain));
            Assert.That(EditorUtility.InstanceIDToObject(_generatedId), Is.Null);
            Assert.That(EditorUtility.InstanceIDToObject(_targetId), Is.Null);
            Assert.That(_scene.handle, Is.EqualTo(_sceneHandle), "旧SceneのhandleがReloadを跨いで保持されること");
            Assert.That(_scene.IsValid(), Is.False);
            Assert.That(_window.DocumentController.ContentFingerprint, Is.EqualTo(_fingerprint));
            Assert.That(_window.DocumentController.IsDirty, Is.True);
            Assert.That(_window.PreviewController.HasAvatar, Is.False);
            Assert.That(_window.PreviewController.PreviewScene.IsValid(), Is.True);
            TestContext.WriteLine("ACTUAL_PREVIEW_DOMAIN_RELOAD PASS oldMesh/RT/Scene destroyed, document preserved, GPU NOT TESTED");
        }

        /// <summary>Reloadを跨いだ合成原本と実Windowを解放します。</summary>
        [TearDown]
        public void TearDown()
        {
            if (_window != null) { _window.DiscardChanges(); _window.Close(); }
            if (_sourceIds != null)
                foreach (var id in _sourceIds)
                {
                    var target = EditorUtility.InstanceIDToObject(id);
                    if (target != null) Object.DestroyImmediate(target);
                }
            if (!string.IsNullOrEmpty(_directory) && Directory.Exists(_directory)) Directory.Delete(_directory, true);
            Undo.ClearAll();
        }
    }
}
