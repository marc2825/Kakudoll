using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using VLat.Core.Model;
using VLat.Core.Serialization;
using VLat.Runtime;

namespace VLat.Tests.Edit
{
    /// <summary>Runtimeの外部texture読み込みが、bufferと同じアクセス範囲と画像上限に従うことを検証します。</summary>
    public sealed class RuntimeTextureSetTests
    {
        private string _directory;

        /// <summary>試験用の一時フォルダを作成します。</summary>
        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VlatRuntimeTextures-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_directory, "doc"));
        }

        /// <summary>一時フォルダを削除します。</summary>
        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        /// <summary>JSONフォルダ内の画像は読み込め、Disposeで生成したTextureを破棄することを検証します。</summary>
        [Test]
        public void Load_ReadsImageInsideJsonDirectoryAndDisposeDestroysTextures()
        {
            WritePng(Path.Combine(_directory, "doc", "ok.png"), 4, 2);
            var diagnostics = new List<VlatRuntimeDiagnostic>();
            var set = RuntimeTextureSet.Load(Document("ok.png"), Array.Empty<byte[]>(), JsonPath(), null, diagnostics);
            Assert.That(diagnostics, Is.Empty);
            var texture = set.Textures[0];
            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.width, Is.EqualTo(4));
            set.Dispose();
            Assert.That(texture == null, Is.True, "Disposeで生成したTextureが破棄されていません。");
        }

        /// <summary>安全モードでは、JSONフォルダ外の画像を読まないことを検証します。従来モードでは読めます。</summary>
        [Test]
        public void Load_RejectsImageOutsideJsonDirectoryInSafeMode()
        {
            WritePng(Path.Combine(_directory, "outside.png"), 2, 2);
            var diagnostics = new List<VlatRuntimeDiagnostic>();
            using (var set = RuntimeTextureSet.Load(Document("../outside.png"), Array.Empty<byte[]>(), JsonPath(), null, diagnostics))
            {
                Assert.That(set.Textures[0], Is.Null);
                Assert.That(diagnostics.Count, Is.EqualTo(1));
                StringAssert.Contains("外を指しています", diagnostics[0].Message);
            }

            diagnostics.Clear();
            using (var set = RuntimeTextureSet.Load(
                       Document("../outside.png"), Array.Empty<byte[]>(), JsonPath(), VlatFileAccessPolicy.TrustedLegacy, diagnostics))
            {
                Assert.That(diagnostics, Is.Empty);
                Assert.That(set.Textures[0], Is.Not.Null);
            }
        }

        /// <summary>ヘッダーの寸法が上限を超える画像は、デコードせずに拒否することを検証します。</summary>
        [Test]
        public void Load_RejectsOversizedImageBeforeDecoding()
        {
            var bytes = File.ReadAllBytes(WritePng(Path.Combine(_directory, "doc", "huge.png"), 2, 2));
            // IHDRの幅を書き換える（CRCは合わなくなるが、デコード前に拒否されるので問題ない）。
            bytes[16] = 0;
            bytes[17] = 0;
            bytes[18] = 0x23;
            bytes[19] = 0x29; // 9001
            File.WriteAllBytes(Path.Combine(_directory, "doc", "huge.png"), bytes);
            var diagnostics = new List<VlatRuntimeDiagnostic>();
            using (var set = RuntimeTextureSet.Load(Document("huge.png"), Array.Empty<byte[]>(), JsonPath(), null, diagnostics))
            {
                Assert.That(set.Textures[0], Is.Null);
                StringAssert.Contains("寸法", diagnostics[0].Message);
            }
        }

        /// <summary>同じ画像を何度も参照しても、デコード後の合計上限で止まることを検証します。</summary>
        [Test]
        public void Load_RepeatedReferencesShareTheDecodedBudget()
        {
            WritePng(Path.Combine(_directory, "doc", "same.png"), 8, 8);
            var policy = new VlatFileAccessPolicy(null, false, new VlatReadLimits(maxTotalTextureDecodedBytes: 8 * 8 * 4 * 2));
            var diagnostics = new List<VlatRuntimeDiagnostic>();
            using (var set = RuntimeTextureSet.Load(
                       Document("same.png", "same.png", "same.png"), Array.Empty<byte[]>(), JsonPath(), policy, diagnostics))
            {
                Assert.That(set.Textures[0], Is.Not.Null);
                Assert.That(set.Textures[1], Is.Not.Null);
                Assert.That(set.Textures[2], Is.Null);
                Assert.That(diagnostics.Count, Is.EqualTo(1));
                Assert.That(diagnostics[0].Path, Is.EqualTo("$.textures[2]"));
            }
        }

        private string JsonPath()
        {
            return Path.Combine(_directory, "doc", "doc.vlat.json");
        }

        private static VlatDocument Document(params string[] uris)
        {
            var textures = new VlatTexture[uris.Length];
            for (var i = 0; i < uris.Length; i++)
            {
                textures[i] = new VlatTexture("t" + i, uris[i], null, null);
            }

            var metadata = new VlatMetadata("0.1.0", "Tests", new TargetAvatar("Avatar", "1.0", Array.Empty<MeshBinding>()));
            return new VlatDocument(metadata, Array.Empty<VlatBuffer>(), Array.Empty<VlatBufferView>(), textures,
                Array.Empty<AngleBand>(), Array.Empty<VlatLayer>());
        }

        private static string WritePng(string path, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                File.WriteAllBytes(path, texture.EncodeToPNG());
                return path;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
