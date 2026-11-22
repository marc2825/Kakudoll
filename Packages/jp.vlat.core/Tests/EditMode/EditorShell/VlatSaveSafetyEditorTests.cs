using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using VLat.Core.Serialization;
using VLat.Core.Validation;
using VLat.Editor.Authoring;

namespace VLat.Tests.Edit
{
    /// <summary>Editorの保存・読込が、中断された保存の自動復旧と安全なbin名を扱うことを検証します。</summary>
    public sealed class VlatSaveSafetyEditorTests
    {
        private const string KeyformFixture = "Packages/jp.vlat.core/Tests/EditMode/Fixtures/Keyform/three-keys.vlat.json";
        private string _directory;
        private VlatAuthoringDocument _document;

        /// <summary>fixtureを読み込み、一時フォルダを用意します。</summary>
        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VlatSaveSafetyEditor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            var load = VlatAuthoringPersistence.Load(Path.GetFullPath(KeyformFixture));
            Assert.That(load.Success, Is.True, Join(load));
            _document = load.Document;
        }

        /// <summary>フックと一時ファイルを片付けます。</summary>
        [TearDown]
        public void TearDown()
        {
            VlatSerializer.SaveStepHook = null;
            if (_document != null)
            {
                UnityEngine.Object.DestroyImmediate(_document);
            }

            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        /// <summary>ファイル名に % や空白・日本語を含んでも、binのuriが正しく往復することを検証します。</summary>
        [Test]
        public void Save_FileNameWithPercentRoundTrips()
        {
            var jsonPath = Path.Combine(_directory, "100% 顔.vlat.json");
            var save = VlatAuthoringPersistence.Save(jsonPath, _document);
            Assert.That(save.Success, Is.True, Join(save.Diagnostics));
            Assert.That(File.Exists(Path.Combine(_directory, "100% 顔.vlat.bin")), Is.True);
            StringAssert.Contains("\"100%25 顔.vlat.bin\"", File.ReadAllText(jsonPath));
            var reload = VlatAuthoringPersistence.Load(jsonPath);
            Assert.That(reload.Success, Is.True, Join(reload));
            UnityEngine.Object.DestroyImmediate(reload.Document);
        }

        /// <summary>中断された保存は、Editorで開くと保存前の内容へ自動で戻り、警告で知らせることを検証します。</summary>
        [Test]
        public void Load_RecoversInterruptedSaveAutomatically()
        {
            var jsonPath = Path.Combine(_directory, "doc.vlat.json");
            Assert.That(VlatAuthoringPersistence.Save(jsonPath, _document).Success, Is.True);
            var savedJson = File.ReadAllBytes(jsonPath);
            var savedBin = File.ReadAllBytes(Path.Combine(_directory, "doc.vlat.bin"));

            // 内容を変えて保存し、binだけ置き換えた直後に異常終了したことにする。
            _document.Metadata.AvatarName = "Changed";
            VlatSerializer.SaveStepHook = (step, index) =>
            {
                if (step == VlatSaveStep.Committed && index == 0) throw new SimulatedCrashException("simulated crash");
            };
            Assert.Throws<SimulatedCrashException>(() => VlatAuthoringPersistence.Save(jsonPath, _document));
            VlatSerializer.SaveStepHook = null;

            var reload = VlatAuthoringPersistence.Load(jsonPath);
            Assert.That(reload.Success, Is.True, Join(reload));
            Assert.That(Join(reload), Does.Contain("保存前の内容へ戻しました"));
            CollectionAssert.AreEqual(savedJson, File.ReadAllBytes(jsonPath));
            CollectionAssert.AreEqual(savedBin, File.ReadAllBytes(Path.Combine(_directory, "doc.vlat.bin")));
            Assert.That(Directory.GetFiles(_directory, ".*"), Is.Empty);
            UnityEngine.Object.DestroyImmediate(reload.Document);

            // 復旧後は通常どおり上書き保存できる。
            Assert.That(VlatAuthoringPersistence.Save(jsonPath, _document).Success, Is.True);
        }

        private static string Join(VlatAuthoringLoadResult result)
        {
            return Join(result.Diagnostics);
        }

        private static string Join(System.Collections.Generic.IReadOnlyList<VlatDiagnostic> diagnostics)
        {
            var builder = new StringBuilder();
            foreach (var diagnostic in diagnostics)
            {
                builder.Append(diagnostic.Severity).Append(' ').Append(diagnostic.Path).Append(": ").AppendLine(diagnostic.Message);
            }

            return builder.ToString();
        }
    }
}
