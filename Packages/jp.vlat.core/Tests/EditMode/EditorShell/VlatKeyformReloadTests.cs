using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VLat.Editor.Authoring;

namespace VLat.Tests.Edit
{
    /// <summary>Unityが実行するAssembly/Domain Reloadと、別プロセス再起動の証拠を採取します。</summary>
    public sealed class VlatKeyformReloadTests
    {
        internal static readonly string DomainStamp = Guid.NewGuid().ToString("N");
        [SerializeField] private VlatEditorWindow _window;
        [SerializeField] private string _directory;
        [SerializeField] private string _fingerprint;
        [SerializeField] private string _identity;
        [SerializeField] private string _sourceHash;
        [SerializeField] private string _beforeStamp;
        [SerializeField] private int _process;

        /// <summary>実Domain Reloadを要求し、静的状態の再生成と実Windowの復元を照合します。</summary>
        [UnityTest]
        public IEnumerator ActualDomainReload_RestoresUnsavedWindowAndSavepoint()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VLatActualReload-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _window = VlatEditorWindow.CreateForRecoveryDirectory(Path.Combine(_directory, "session"));
            _window.Show(); yield return null;
            VlatKeyformReloadFixture.Seed(_window.DocumentController, Path.Combine(_directory, "source.vlat.json"));
            _window.CreateGUI();
            _fingerprint = _window.DocumentController.ContentFingerprint;
            _identity = _window.DocumentController.Selection.Identity;
            _sourceHash = VlatSessionFingerprint.Source(_window.DocumentController.SourcePath);
            _beforeStamp = DomainStamp;
            _process = System.Diagnostics.Process.GetCurrentProcess().Id;
            SessionState.SetBool("VLat1B.beforeReload", false);
            AssemblyReloadEvents.beforeAssemblyReload += RecordBeforeReload;
            EditorUtility.RequestScriptReload();
            yield return new WaitForDomainReload();
            yield return null;
            Assert.That(DomainStamp, Is.Not.EqualTo(_beforeStamp), "static状態が再生成されていること");
            Assert.That(System.Diagnostics.Process.GetCurrentProcess().Id, Is.EqualTo(_process));
            Assert.That(SessionState.GetBool("VLat1B.beforeReload", false), Is.True);
            Assert.That(_window, Is.Not.Null);
            Assert.That(_window.DocumentController.ContentFingerprint, Is.EqualTo(_fingerprint));
            Assert.That(_window.DocumentController.Selection.Identity, Is.EqualTo(_identity));
            Assert.That(_window.DocumentController.IsDirty, Is.True);
            Assert.That(VlatSessionFingerprint.Source(_window.DocumentController.SourcePath), Is.EqualTo(_sourceHash));
            Assert.That(_window.DocumentController.Save(), Is.True);
            Assert.That(_window.DocumentController.IsDirty, Is.False);
            TestContext.WriteLine("ACTUAL_DOMAIN_RELOAD PASS pid=" + _process + " before=" + _beforeStamp +
                " after=" + DomainStamp + "; beforeAssemblyReload observed; dirty/selection/source preserved.");
        }

        private static void RecordBeforeReload() => SessionState.SetBool("VLat1B.beforeReload", true);

        /// <summary>Reload前後を通して試験用の所有物を解放します。</summary>
        [TearDown]
        public void TearDown()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= RecordBeforeReload;
            if (_window != null) { _window.DiscardChanges(); _window.Close(); }
            if (!string.IsNullOrEmpty(_directory) && Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
    }

    internal static class VlatKeyformReloadFixture
    {
        internal static void Seed(VlatEditorDocumentController session, string source)
        {
            var root = new GameObject("SyntheticReloadSource");
            var mesh = new Mesh { name = "ReloadMesh" };
            try
            {
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.AddBlendShapeFrame("Left", 100, new[] { Vector3.left, Vector3.zero, Vector3.zero }, null, null);
                var renderer = root.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = mesh;
                session.NewDocument(VlatCloseDecision.Discard);
                Assert.That(session.AddKeyformGroup(), Is.True);
                Assert.That(session.BindKeyformTarget(0, root, renderer), Is.True);
                Assert.That(session.SaveAs(source), Is.True);
                Assert.That(session.ImportKeyformPositions(0, 0, root, renderer, 0), Is.True);
                session.SelectKeyform(0, 0);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(mesh); }
        }
    }

    [Serializable]
    internal sealed class VlatRestartEvidence
    {
        [SerializeField] internal int process;
        [SerializeField] internal int previousProcess;
        [SerializeField] internal string domain;
        [SerializeField] internal string content;
        [SerializeField] internal string identity;
        [SerializeField] internal string sourceHash;
        [SerializeField] internal string result;
    }

    /// <summary>専用隔離プロジェクトでのみ明示実行する、Editor再起動の2段階Probeです。</summary>
    public static class VlatKeyformRestartProbe
    {
        private static string DirectoryPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "VLat1BRestart"));

        /// <summary>未保存Keyformと復元期待値を記録し、Editorプロセスを終了します。</summary>
        public static void Seed()
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                var window = VlatEditorWindow.CreateForRecoveryDirectory(Path.Combine(DirectoryPath, "session"));
                window.Show();
                VlatKeyformReloadFixture.Seed(window.DocumentController, Path.Combine(DirectoryPath, "source.vlat.json"));
                window.CreateGUI();
                var proof = Evidence(window); proof.result = "SEEDED";
                File.WriteAllText(Path.Combine(DirectoryPath, "seed.json"), JsonUtility.ToJson(proof, true));
                // 正常なEditor終了でOnDisableを通す。Restoreは別Unityプロセスから実行する。
                EditorApplication.Exit(0);
            }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        /// <summary>別プロセスでRecoveryを読み、保存元を変更せず同じ未保存内容を復元したことを検証します。</summary>
        public static void Restore()
        {
            try
            {
                var seed = JsonUtility.FromJson<VlatRestartEvidence>(File.ReadAllText(Path.Combine(DirectoryPath, "seed.json")));
                var window = VlatEditorWindow.CreateForRecoveryDirectory(Path.Combine(DirectoryPath, "session"));
                window.Show(); window.CreateGUI();
                var proof = Evidence(window);
                Assert.That(proof.process, Is.Not.EqualTo(seed.process));
                Assert.That(proof.domain, Is.Not.EqualTo(seed.domain));
                Assert.That(proof.content, Is.EqualTo(seed.content)); Assert.That(proof.identity, Is.EqualTo(seed.identity));
                Assert.That(proof.sourceHash, Is.EqualTo(seed.sourceHash));
                Assert.That(window.DocumentController.IsDirty, Is.True);
                proof.previousProcess = seed.process; proof.result = "PASS";
                File.WriteAllText(Path.Combine(DirectoryPath, "restart-result.json"), JsonUtility.ToJson(proof, true));
                window.DiscardChanges(); window.Close(); EditorApplication.Exit(0);
            }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        private static VlatRestartEvidence Evidence(VlatEditorWindow window) => new VlatRestartEvidence
        {
            process = System.Diagnostics.Process.GetCurrentProcess().Id, domain = VlatKeyformReloadTests.DomainStamp,
            content = window.DocumentController.ContentFingerprint, identity = window.DocumentController.Selection.Identity,
            sourceHash = VlatSessionFingerprint.Source(window.DocumentController.SourcePath),
        };
    }
}
