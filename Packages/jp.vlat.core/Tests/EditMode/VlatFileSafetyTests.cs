using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using NUnit.Framework;
using VLat.Core.Model;
using VLat.Core.Serialization;
using VLat.Core.Validation;

namespace VLat.Tests.Edit
{
    /// <summary>
    /// 外部から受け取るファイルに対する、読み込み上限・外部URIのアクセス範囲・保存の中断耐性を検証します。
    /// 巨大ファイルは作らず、小さい上限を指定して境界を確認します。
    /// </summary>
    public sealed class VlatFileSafetyTests
    {
        private const string ValidHash = "xxh64:0123456789abcdef";

        private string _directory;

        /// <summary>試験ごとに一時フォルダを用意します。</summary>
        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VlatFileSafety-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            VlatSerializer.SaveStepHook = null;
        }

        /// <summary>一時フォルダとフックを片付けます。</summary>
        [TearDown]
        public void TearDown()
        {
            VlatSerializer.SaveStepHook = null;
            if (Directory.Exists(_directory))
            {
                DeleteTree(_directory);
            }
        }

        // リンク・ジャンクションの先を辿らずに削除する（リンク自体だけを外す）。
        private static void DeleteTree(string directory)
        {
            foreach (var child in Directory.GetDirectories(directory))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                {
                    Directory.Delete(child, false);
                }
                else
                {
                    DeleteTree(child);
                }
            }

            foreach (var file in Directory.GetFiles(directory))
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }

            Directory.Delete(directory, false);
        }

        // ---- #3 読み込み上限 ----

        /// <summary>上限を超えるJSONファイルは、内容を解析せずに拒否されることを検証します。</summary>
        [Test]
        public void Load_RejectsJsonLargerThanLimit()
        {
            var jsonPath = Path.Combine(_directory, "large.vlat.json");
            File.WriteAllText(jsonPath, VlatSerializer.Serialize(CreateDocument()));
            var size = new FileInfo(jsonPath).Length;

            var rejected = VlatSerializer.Load(jsonPath, Policy(new VlatReadLimits(maxJsonBytes: size - 1)));
            Assert.That(rejected.Success, Is.False);
            Assert.That(rejected.Document, Is.Null);
            StringAssert.Contains("大きすぎます", rejected.Diagnostics[0].Message);

            Assert.That(VlatSerializer.Load(jsonPath, Policy(new VlatReadLimits(maxJsonBytes: size))).Success, Is.True);
            var json = File.ReadAllText(jsonPath);
            Assert.That(VlatSerializer.Deserialize(json, new VlatReadLimits(maxJsonBytes: json.Length - 1)).Success, Is.False);
        }

        /// <summary>既定の深さ上限を超えるJSONを、DOM構築前に拒否することを検証します。</summary>
        [Test]
        public void Deserialize_RejectsDeepNestingAndTooManyTokens()
        {
            var deep = new StringBuilder("{\"vlat\":");
            for (var i = 0; i < 40; i++) deep.Append('[');
            for (var i = 0; i < 40; i++) deep.Append(']');
            deep.Append('}');
            var deepResult = VlatSerializer.Deserialize(deep.ToString());
            Assert.That(deepResult.Success, Is.False);
            StringAssert.Contains("ネストが深すぎます", deepResult.Diagnostics[0].Message);

            var valid = VlatSerializer.Serialize(CreateDocument());
            Assert.That(VlatSerializer.Deserialize(valid).Success, Is.True);
            var tokenResult = VlatSerializer.Deserialize(valid, new VlatReadLimits(maxJsonTokens: 10));
            Assert.That(tokenResult.Success, Is.False);
            StringAssert.Contains("要素が多すぎます", tokenResult.Diagnostics[0].Message);
            var shallow = VlatSerializer.Deserialize(valid, new VlatReadLimits(maxJsonDepth: 2));
            Assert.That(shallow.Success, Is.False);
        }

        /// <summary>buffer数・宣言長・合計が上限を超えるとファイルを読まずに拒否することを検証します。</summary>
        [Test]
        public void Load_RejectsDeclaredBufferSizesBeforeReading()
        {
            var two = CreateDocument(new VlatBuffer("a.bin", 4), new VlatBuffer("b.bin", 4));
            var jsonPath = WriteJson("two.vlat.json", two);
            // binが存在しないので、読みに行けば「読み込めません」が出る。上限で止まればその診断は出ない。
            var tooMany = VlatSerializer.Load(jsonPath, Policy(new VlatReadLimits(maxBufferCount: 1)));
            AssertError(tooMany.Diagnostics, "$.buffers", "bufferが多すぎます");
            AssertNoMessage(tooMany.Diagnostics, "読み込めません");

            var tooLarge = VlatSerializer.Load(jsonPath, Policy(new VlatReadLimits(maxBufferBytes: 3)));
            AssertError(tooLarge.Diagnostics, "$.buffers[0].byteLength", "bufferが大きすぎます");
            AssertNoMessage(tooLarge.Diagnostics, "読み込めません");

            var tooMuchTotal = VlatSerializer.Load(jsonPath, Policy(new VlatReadLimits(maxTotalBufferBytes: 7)));
            AssertError(tooMuchTotal.Diagnostics, "$.buffers", "合計が大きすぎます");

            var textures = new VlatTexture[]
            {
                new VlatTexture("a", "a.png", null, null),
                new VlatTexture("b", "b.png", null, null),
            };
            var withTextures = new VlatDocument(CreateMetadata(), Array.Empty<VlatBuffer>(), Array.Empty<VlatBufferView>(),
                textures, Array.Empty<AngleBand>(), Array.Empty<VlatLayer>());
            var texturePath = WriteJson("textures.vlat.json", withTextures);
            AssertError(
                VlatSerializer.Load(texturePath, Policy(new VlatReadLimits(maxTextureCount: 1))).Diagnostics,
                "$.textures",
                "textureが多すぎます");
        }

        /// <summary>宣言長より大きい実ファイル（宣言長の偽装）を、内容を確保せずに拒否することを検証します。</summary>
        [Test]
        public void Load_RejectsForgedDeclaredLengthWithoutReadingContents()
        {
            var jsonPath = WriteJson("forged.vlat.json", CreateDocument(new VlatBuffer("forged.bin", 4)));
            File.WriteAllBytes(Path.Combine(_directory, "forged.bin"), new byte[64 * 1024]);

            var result = VlatSerializer.Load(jsonPath);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Buffers[0], Is.Null);
            AssertError(result.Diagnostics, "$.buffers[0].byteLength", "実ファイル長");
        }

        /// <summary>読み込み上限を超える文書は、保存しても開けないため保存前に拒否することを検証します。</summary>
        [Test]
        public void Save_RejectsDocumentsThatCannotBeLoadedBack()
        {
            var jsonPath = Path.Combine(_directory, "limit.vlat.json");
            var document = CreateDocument(new VlatBuffer("limit.bin", 8));
            var result = VlatSerializer.Save(jsonPath, document, new[] { new byte[8] }, Policy(new VlatReadLimits(maxBufferBytes: 4)));
            Assert.That(result.Success, Is.False);
            Assert.That(Directory.GetFileSystemEntries(_directory), Is.Empty);
        }

        /// <summary>PNG・JPEGのヘッダー寸法を読み、不正・未対応の画像を拒否することを検証します。</summary>
        [Test]
        public void ImageHeader_ReadsPngAndJpegDimensionsAndRejectsOthers()
        {
            string format;
            int width;
            int height;
            string error;
            Assert.That(VlatImageHeader.TryReadDimensions(Png(300, 200), out format, out width, out height, out error), Is.True, error);
            Assert.That(format, Is.EqualTo("png"));
            Assert.That(width, Is.EqualTo(300));
            Assert.That(height, Is.EqualTo(200));

            Assert.That(VlatImageHeader.TryReadDimensions(Jpeg(640, 480), out format, out width, out height, out error), Is.True, error);
            Assert.That(format, Is.EqualTo("jpeg"));
            Assert.That(width, Is.EqualTo(640));
            Assert.That(height, Is.EqualTo(480));

            var truncated = new byte[12];
            Array.Copy(Png(1, 1), truncated, truncated.Length);
            Assert.That(VlatImageHeader.TryReadDimensions(truncated, out format, out width, out height, out error), Is.False);
            Assert.That(VlatImageHeader.TryReadDimensions(Png(0, 10), out format, out width, out height, out error), Is.False);
            Assert.That(VlatImageHeader.TryReadDimensions(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }, out format, out width, out height, out error), Is.False);
            Assert.That(VlatImageHeader.TryReadDimensions(Encoding.ASCII.GetBytes("GIF89a......"), out format, out width, out height, out error), Is.False);
            Assert.That(VlatImageHeader.TryReadDimensions(null, out format, out width, out height, out error), Is.False);
        }

        /// <summary>寸法・ファイルサイズ・複数参照によるデコード後合計の上限を検証します。</summary>
        [Test]
        public void TextureBudget_RejectsOversizedImagesAndRepeatedReferences()
        {
            string error;
            var limits = new VlatReadLimits(maxTextureDimension: 1024, maxTotalTextureDecodedBytes: 1024L * 1024 * 4 * 2,
                maxTextureEncodedBytes: 1000);
            var budget = new VlatTextureBudget(limits);
            Assert.That(budget.TryReserve(Png(1025, 1), out error), Is.False);
            StringAssert.Contains("寸法", error);
            Assert.That(budget.CheckEncodedLength(1001, out error), Is.False);

            // 同じ画像を3回参照すると、2回分の予算で3回目を拒否する。
            var image = Png(1024, 1024);
            Assert.That(budget.TryReserve(image, out error), Is.True, error);
            Assert.That(budget.TryReserve(image, out error), Is.True, error);
            Assert.That(budget.TryReserve(image, out error), Is.False);
            Assert.That(budget.ReservedBytes, Is.EqualTo(1024L * 1024 * 4 * 2));
            Assert.That(new VlatTextureBudget().TryReserve(Png(8193, 1), out error), Is.False);
        }

        /// <summary>上限の構成値は1以上でなければならないことを検証します。</summary>
        [Test]
        public void ReadLimits_RejectNonPositiveValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new VlatReadLimits(maxJsonBytes: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VlatReadLimits(maxTextureDimension: -1));
            Assert.That(VlatReadLimits.Default.MaxJsonDepth, Is.EqualTo(VlatReadLimits.DefaultMaxJsonDepth));
        }

        // ---- #4 外部URIのアクセス範囲 ----

        /// <summary>安全モードが、JSONフォルダ外・エンコード・区切り・予約名などのURIを読込・保存の両方で拒否することを検証します。</summary>
        [TestCase("../outside.bin")]
        [TestCase("sub/../../outside.bin")]
        [TestCase("%2E%2E/outside.bin")]
        [TestCase("..%2Foutside.bin")]
        [TestCase("..\\outside.bin")]
        [TestCase("sub\\data.bin")]
        [TestCase("sub/a:b.bin")]
        [TestCase("NUL")]
        [TestCase("sub/con.bin")]
        [TestCase("COM1.txt")]
        [TestCase("data.bin.")]
        [TestCase("data.bin ")]
        [TestCase("data%00.bin")]
        public void SafeMode_RejectsUnsafeUrisOnLoadAndSave(string uri)
        {
            var inner = Path.Combine(_directory, "inner");
            Directory.CreateDirectory(inner);
            File.WriteAllBytes(Path.Combine(_directory, "outside.bin"), new byte[] { 1, 2, 3, 4 });
            var jsonPath = Path.Combine(inner, "doc.vlat.json");
            var document = CreateDocument(new VlatBuffer(uri, 4));
            var before = Snapshot(_directory);

            var saved = VlatSerializer.Save(jsonPath, document, new[] { new byte[] { 9, 9, 9, 9 } });
            Assert.That(saved.Success, Is.False);
            AssertError(saved.Diagnostics, "$.buffers[0].uri", null);
            CollectionAssert.AreEquivalent(before, Snapshot(_directory), "失敗した保存がファイルを作成・変更しています。");

            File.WriteAllText(jsonPath, VlatSerializer.Serialize(document));
            var loaded = VlatSerializer.Load(jsonPath);
            Assert.That(loaded.Success, Is.False);
            AssertError(loaded.Diagnostics, "$.buffers[0].uri", null);
        }

        /// <summary>絶対URI・ドライブ指定・UNCは、安全モードでも従来モードでも拒否することを検証します。</summary>
        [TestCase("C:outside.bin")]
        [TestCase("C:/outside.bin")]
        [TestCase("\\\\server\\share\\outside.bin")]
        [TestCase("//server/share/outside.bin")]
        [TestCase("file:///tmp/outside.bin")]
        [TestCase("%2Ftmp%2Foutside.bin")]
        public void AllModes_RejectRootedUris(string uri)
        {
            var jsonPath = WriteJson("rooted.vlat.json", CreateDocument(new VlatBuffer(uri, 4)));
            AssertError(VlatSerializer.Load(jsonPath).Diagnostics, "$.buffers[0].uri", null);
            AssertError(VlatSerializer.Load(jsonPath, VlatFileAccessPolicy.TrustedLegacy).Diagnostics, "$.buffers[0].uri", null);
        }

        /// <summary>許可フォルダの名前を前方一致で含む別フォルダ（dir と dir-evil）を区別することを検証します。</summary>
        [Test]
        public void SafeMode_DistinguishesSiblingDirectoryWithSamePrefix()
        {
            var root = Path.Combine(_directory, "dir");
            Directory.CreateDirectory(Path.Combine(root, "doc"));
            Directory.CreateDirectory(Path.Combine(_directory, "dir-evil"));
            File.WriteAllBytes(Path.Combine(_directory, "dir-evil", "data.bin"), new byte[] { 1, 2, 3, 4 });
            File.WriteAllBytes(Path.Combine(root, "data.bin"), new byte[] { 1, 2, 3, 4 });
            var policy = new VlatFileAccessPolicy(root);

            var evilPath = Path.Combine(root, "doc", "evil.vlat.json");
            File.WriteAllText(evilPath, VlatSerializer.Serialize(CreateDocument(new VlatBuffer("../../dir-evil/data.bin", 4))));
            AssertError(VlatSerializer.Load(evilPath, policy).Diagnostics, "$.buffers[0].uri", "外を指しています");

            var okPath = Path.Combine(root, "doc", "ok.vlat.json");
            File.WriteAllText(okPath, VlatSerializer.Serialize(CreateDocument(new VlatBuffer("../data.bin", 4))));
            Assert.That(VlatSerializer.Load(okPath, policy).Success, Is.True);

            var outsideJson = Path.Combine(_directory, "dir-evil", "doc.vlat.json");
            File.WriteAllText(outsideJson, VlatSerializer.Serialize(CreateDocument()));
            AssertError(VlatSerializer.Load(outsideJson, policy).Diagnostics, "$", "外にあります");
        }

        /// <summary>安全モードが、JSONフォルダ内のシンボリックリンク・ジャンクションを経由する参照を拒否することを検証します。</summary>
        [Test]
        public void SafeMode_RejectsPathsThroughLinks()
        {
            var outside = Path.Combine(_directory, "outside");
            var inner = Path.Combine(_directory, "inner");
            Directory.CreateDirectory(outside);
            Directory.CreateDirectory(inner);
            File.WriteAllBytes(Path.Combine(outside, "data.bin"), new byte[] { 1, 2, 3, 4 });
            if (!TryCreateDirectoryLink(Path.Combine(inner, "link"), outside))
            {
                Assert.Ignore("この環境ではディレクトリリンクを作成できないため未実施（NOT RUN）。");
            }

            var jsonPath = Path.Combine(inner, "doc.vlat.json");
            var document = CreateDocument(new VlatBuffer("link/data.bin", 4));
            File.WriteAllText(jsonPath, VlatSerializer.Serialize(document));
            AssertError(VlatSerializer.Load(jsonPath).Diagnostics, "$.buffers[0].uri", "シンボリックリンク");
            Assert.That(VlatSerializer.Load(jsonPath, VlatFileAccessPolicy.TrustedLegacy).Success, Is.True);

            var saved = VlatSerializer.Save(jsonPath, document, new[] { new byte[] { 9, 9, 9, 9 } });
            Assert.That(saved.Success, Is.False);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Path.Combine(outside, "data.bin")));
        }

        /// <summary>JSONフォルダ内の通常の相対URI（サブフォルダ・日本語・空白・%エンコード）は従来どおり読み書きできることを検証します。</summary>
        [TestCase("data.bin", "data.bin")]
        [TestCase("sub/data.bin", "sub/data.bin")]
        [TestCase("sub/./data.bin", "sub/data.bin")]
        [TestCase("sub/../data.bin", "data.bin")]
        [TestCase("顔%20データ.bin", "顔 データ.bin")]
        [TestCase("100%25.bin", "100%.bin")]
        public void SafeMode_AllowsRelativeUrisInsideJsonDirectory(string uri, string expectedFile)
        {
            var jsonPath = Path.Combine(_directory, "doc.vlat.json");
            var document = CreateDocument(new VlatBuffer(uri, 4));
            Assert.That(VlatSerializer.Save(jsonPath, document, new[] { new byte[] { 1, 2, 3, 4 } }).Success, Is.True);
            Assert.That(File.Exists(Path.Combine(_directory, expectedFile.Replace('/', Path.DirectorySeparatorChar))), Is.True);
            var loaded = VlatSerializer.Load(jsonPath);
            Assert.That(loaded.Success, Is.True);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, loaded.Buffers[0]);
        }

        // ---- #6 保存の中断耐性 ----

        /// <summary>
        /// 保存の各段階で異常終了したとき、次回Loadが状態を正しく判定し、復旧で保存前か保存後の
        /// 一貫した状態へ戻り、一時ファイルが残らないことを検証します。binは同じ長さにして、
        /// 長さ検査だけでは見つからない取り違えも検出できることを確認します。
        /// </summary>
        [TestCase("JournalWritten", 0, "old", VlatInterruptedSaveOutcome.PreviousContentRestored)]
        [TestCase("Staged", 0, "old", VlatInterruptedSaveOutcome.PreviousContentRestored)]
        [TestCase("Staged", 1, "old", VlatInterruptedSaveOutcome.PreviousContentRestored)]
        [TestCase("Committed", 0, "torn", VlatInterruptedSaveOutcome.PreviousContentRestored)]
        [TestCase("Committed", 1, "new", VlatInterruptedSaveOutcome.CompletedSaveKept)]
        public void Save_CrashAtEachStepIsDetectedAndRecoverable(
            string stepName,
            int index,
            string expectedState,
            VlatInterruptedSaveOutcome expectedOutcome)
        {
            var step = (VlatSaveStep)Enum.Parse(typeof(VlatSaveStep), stepName);
            var jsonPath = Path.Combine(_directory, "crash.vlat.json");
            var oldDocument = CreateDocument("Old", new VlatBuffer("crash.vlat.bin", 4));
            var newDocument = CreateDocument("New", new VlatBuffer("crash.vlat.bin", 4));
            var oldBin = new byte[] { 1, 1, 1, 1 };
            var newBin = new byte[] { 2, 2, 2, 2 };
            Assert.That(VlatSerializer.Save(jsonPath, oldDocument, new[] { oldBin }).Success, Is.True);
            var oldJson = File.ReadAllBytes(jsonPath);

            VlatSerializer.SaveStepHook = (current, currentIndex) =>
            {
                if (current == step && currentIndex == index) throw new SimulatedCrashException("simulated crash");
            };
            Assert.Throws<SimulatedCrashException>(() => VlatSerializer.Save(jsonPath, newDocument, new[] { newBin }));
            VlatSerializer.SaveStepHook = null;
            var journal = VlatSerializer.GetJournalPath(Path.GetFullPath(jsonPath));
            Assert.That(File.Exists(journal), Is.True, "中断した保存のjournalが残っていません。");

            var inspected = VlatSerializer.Load(jsonPath);
            if (expectedState == "torn")
            {
                Assert.That(inspected.Success, Is.False, "JSONとbinの取り違えを検出できていません。");
                AssertError(inspected.Diagnostics, "$", "中断");
            }
            else
            {
                Assert.That(inspected.Success, Is.True);
                Assert.That(inspected.Document.Metadata.TargetAvatar.Name, Is.EqualTo(expectedState == "new" ? "New" : "Old"));
                AssertWarning(inspected.Diagnostics, "中断");
            }

            var blocked = VlatSerializer.Save(jsonPath, newDocument, new[] { newBin });
            Assert.That(blocked.Success, Is.False, "復旧前の上書き保存を拒否していません。");

            var recovery = VlatSerializer.RecoverInterruptedSave(jsonPath);
            Assert.That(recovery.Outcome, Is.EqualTo(expectedOutcome));
            Assert.That(File.Exists(journal), Is.False);
            var recovered = VlatSerializer.Load(jsonPath);
            Assert.That(recovered.Success, Is.True);
            Assert.That(recovered.Diagnostics, Is.Empty);
            if (expectedOutcome == VlatInterruptedSaveOutcome.PreviousContentRestored)
            {
                CollectionAssert.AreEqual(oldJson, File.ReadAllBytes(jsonPath));
                CollectionAssert.AreEqual(oldBin, recovered.Buffers[0]);
            }
            else
            {
                Assert.That(recovered.Document.Metadata.TargetAvatar.Name, Is.EqualTo("New"));
                CollectionAssert.AreEqual(newBin, recovered.Buffers[0]);
            }

            AssertNoSidecars();
            Assert.That(VlatSerializer.RecoverInterruptedSave(jsonPath).Outcome, Is.EqualTo(VlatInterruptedSaveOutcome.None));
        }

        /// <summary>新規保存の途中で異常終了したとき、復旧で新規に作られたbinだけを削除することを検証します。</summary>
        [Test]
        public void Save_CrashDuringFirstSaveRemovesOnlyCreatedFiles()
        {
            var jsonPath = Path.Combine(_directory, "first.vlat.json");
            var unrelated = Path.Combine(_directory, "unrelated.bin");
            File.WriteAllBytes(unrelated, new byte[] { 7 });
            VlatSerializer.SaveStepHook = (step, index) =>
            {
                if (step == VlatSaveStep.Committed && index == 0) throw new SimulatedCrashException("simulated crash");
            };
            Assert.Throws<SimulatedCrashException>(() => VlatSerializer.Save(
                jsonPath, CreateDocument(new VlatBuffer("first.vlat.bin", 4)), new[] { new byte[] { 3, 3, 3, 3 } }));
            VlatSerializer.SaveStepHook = null;

            Assert.That(VlatSerializer.RecoverInterruptedSave(jsonPath).Outcome,
                Is.EqualTo(VlatInterruptedSaveOutcome.PreviousContentRestored));
            Assert.That(File.Exists(Path.Combine(_directory, "first.vlat.bin")), Is.False);
            Assert.That(File.Exists(jsonPath), Is.False);
            CollectionAssert.AreEqual(new byte[] { 7 }, File.ReadAllBytes(unrelated));
            AssertNoSidecars();
        }

        /// <summary>
        /// journal削除後（保存確定後）に異常終了した場合は、保存後の内容が読め、backupの残骸だけが残ることを検証します。
        /// これは保証範囲の文書化に対応する（残骸は自動削除しない）。
        /// </summary>
        [Test]
        public void Save_CrashAfterCommitPointLeavesOnlyBackupFiles()
        {
            var jsonPath = Path.Combine(_directory, "late.vlat.json");
            Assert.That(VlatSerializer.Save(jsonPath, CreateDocument("Old", new VlatBuffer("late.vlat.bin", 4)), new[] { new byte[4] }).Success, Is.True);
            VlatSerializer.SaveStepHook = (step, index) =>
            {
                if (step == VlatSaveStep.JournalDeleted) throw new SimulatedCrashException("simulated crash");
            };
            Assert.Throws<SimulatedCrashException>(() => VlatSerializer.Save(
                jsonPath, CreateDocument("New", new VlatBuffer("late.vlat.bin", 4)), new[] { new byte[] { 5, 5, 5, 5 } }));
            VlatSerializer.SaveStepHook = null;

            var loaded = VlatSerializer.Load(jsonPath);
            Assert.That(loaded.Success, Is.True);
            Assert.That(loaded.Diagnostics, Is.Empty);
            Assert.That(loaded.Document.Metadata.TargetAvatar.Name, Is.EqualTo("New"));
            Assert.That(Directory.GetFiles(_directory, ".*.bak").Length, Is.EqualTo(2));
            Assert.That(Directory.GetFiles(_directory, ".*.tmp"), Is.Empty);
        }

        /// <summary>書込み権限不足・容量不足などの通常の例外では、元のファイルへ戻しjournalも残さないことを検証します。</summary>
        [TestCase("Staged", 0)]
        [TestCase("Staged", 1)]
        [TestCase("Committed", 0)]
        public void Save_IoFailureRollsBackWithoutJournal(string stepName, int index)
        {
            var step = (VlatSaveStep)Enum.Parse(typeof(VlatSaveStep), stepName);
            var jsonPath = Path.Combine(_directory, "io.vlat.json");
            var oldBin = new byte[] { 1, 2, 3, 4 };
            Assert.That(VlatSerializer.Save(jsonPath, CreateDocument("Old", new VlatBuffer("io.vlat.bin", 4)), new[] { oldBin }).Success, Is.True);
            var oldJson = File.ReadAllBytes(jsonPath);
            VlatSerializer.SaveStepHook = (current, currentIndex) =>
            {
                if (current == step && currentIndex == index)
                {
                    if (index == 0) throw new IOException("ディスクの空き容量が不足しています（模擬）。");
                    throw new UnauthorizedAccessException("書き込み権限がありません（模擬）。");
                }
            };
            var result = VlatSerializer.Save(jsonPath, CreateDocument("New", new VlatBuffer("io.vlat.bin", 4)), new[] { new byte[] { 9, 9, 9, 9 } });
            VlatSerializer.SaveStepHook = null;

            Assert.That(result.Success, Is.False);
            CollectionAssert.AreEqual(oldJson, File.ReadAllBytes(jsonPath));
            CollectionAssert.AreEqual(oldBin, File.ReadAllBytes(Path.Combine(_directory, "io.vlat.bin")));
            Assert.That(File.Exists(VlatSerializer.GetJournalPath(Path.GetFullPath(jsonPath))), Is.False);
            AssertNoSidecars();
        }

        /// <summary>別の保存が進行中（journalが開かれている）なら、保存・読込・復旧のいずれも何も変更しないことを検証します。</summary>
        [Test]
        public void Save_ConcurrentSaveIsRejected()
        {
            var jsonPath = Path.Combine(_directory, "busy.vlat.json");
            var oldBin = new byte[] { 1, 2, 3, 4 };
            Assert.That(VlatSerializer.Save(jsonPath, CreateDocument(new VlatBuffer("busy.vlat.bin", 4)), new[] { oldBin }).Success, Is.True);
            var journal = VlatSerializer.GetJournalPath(Path.GetFullPath(jsonPath));
            using (new FileStream(journal, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
            {
                var result = VlatSerializer.Save(jsonPath, CreateDocument(new VlatBuffer("busy.vlat.bin", 4)), new[] { new byte[] { 9, 9, 9, 9 } });
                Assert.That(result.Success, Is.False);
                StringAssert.Contains("進行中", result.Diagnostics[0].Message);
                CollectionAssert.AreEqual(oldBin, File.ReadAllBytes(Path.Combine(_directory, "busy.vlat.bin")));
                AssertError(VlatSerializer.Load(jsonPath).Diagnostics, "$", "保存中");
                Assert.That(VlatSerializer.RecoverInterruptedSave(jsonPath).Outcome, Is.EqualTo(VlatInterruptedSaveOutcome.Failed));
            }

            File.Delete(journal);
        }

        /// <summary>改ざん・破損したjournalでは、許可範囲外や規則外のファイルに触れず復旧を拒否することを検証します。</summary>
        [TestCase("{\"format\":\"vlat-save-journal/1\",\"json\":\"evil.vlat.json\",\"entries\":[{\"target\":\"../victim.bin\",\"temporary\":\".victim.bin.0123456789abcdef0123456789abcdef.tmp\",\"backup\":\".victim.bin.0123456789abcdef0123456789abcdef.bak\",\"hadOriginal\":false,\"length\":1,\"sha256\":\"4bf5122f344554c53bde2ebb8cd2b7e3d1600ad631c385a5d7cce23c7785459a\"}]}")]
        [TestCase("{\"format\":\"vlat-save-journal/1\",\"json\":\"evil.vlat.json\",\"entries\":[{\"target\":\"evil.bin\",\"temporary\":\"../victim.bin\",\"backup\":\".evil.bin.0123456789abcdef0123456789abcdef.bak\",\"hadOriginal\":false,\"length\":1,\"sha256\":\"4bf5122f344554c53bde2ebb8cd2b7e3d1600ad631c385a5d7cce23c7785459a\"}]}")]
        [TestCase("{\"format\":\"other\",\"json\":\"evil.vlat.json\",\"entries\":[]}")]
        [TestCase("not json")]
        public void Recover_RejectsTamperedJournal(string journalText)
        {
            var inner = Path.Combine(_directory, "inner");
            Directory.CreateDirectory(inner);
            var victim = Path.Combine(_directory, "victim.bin");
            File.WriteAllBytes(victim, new byte[] { 1 });
            var jsonPath = Path.Combine(inner, "evil.vlat.json");
            File.WriteAllText(jsonPath, VlatSerializer.Serialize(CreateDocument()));
            File.WriteAllText(VlatSerializer.GetJournalPath(Path.GetFullPath(jsonPath)), journalText);

            var recovery = VlatSerializer.RecoverInterruptedSave(jsonPath);
            Assert.That(recovery.Outcome, Is.EqualTo(VlatInterruptedSaveOutcome.Failed));
            Assert.That(File.Exists(victim), Is.True);
            Assert.That(VlatSerializer.Load(jsonPath).Success, Is.False);
        }

        // ---- 補助 ----

        private static VlatFileAccessPolicy Policy(VlatReadLimits limits)
        {
            return new VlatFileAccessPolicy(null, false, limits);
        }

        private string WriteJson(string name, VlatDocument document)
        {
            var path = Path.Combine(_directory, name);
            File.WriteAllText(path, VlatSerializer.Serialize(document));
            return path;
        }

        private static VlatMetadata CreateMetadata(string avatarName = "Avatar")
        {
            var material = new VlatMaterialSlot(0, "Material", "VRM10/MToon10");
            var mesh = new MeshBinding("Face", "Face", 4, ValidHash, new[] { material });
            return new VlatMetadata("0.1.0", "Tests", new TargetAvatar(avatarName, "1.0", new[] { mesh }));
        }

        private static VlatDocument CreateDocument(params VlatBuffer[] buffers)
        {
            return CreateDocument("Avatar", buffers);
        }

        private static VlatDocument CreateDocument(string avatarName, params VlatBuffer[] buffers)
        {
            return new VlatDocument(CreateMetadata(avatarName), buffers, Array.Empty<VlatBufferView>(),
                Array.Empty<VlatTexture>(), Array.Empty<AngleBand>(), Array.Empty<VlatLayer>());
        }

        private void AssertNoSidecars()
        {
            var leftovers = new List<string>();
            foreach (var path in Directory.GetFiles(_directory, ".*", SearchOption.AllDirectories))
            {
                leftovers.Add(Path.GetFileName(path));
            }

            Assert.That(leftovers, Is.Empty, "一時ファイル・backup・journalが残っています。");
        }

        private static List<string> Snapshot(string directory)
        {
            var entries = new List<string>();
            foreach (var path in Directory.GetFileSystemEntries(directory, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(path);
                entries.Add(path.Substring(directory.Length) + (info.Exists ? ":" + info.Length + ":" +
                    Convert.ToBase64String(File.ReadAllBytes(path)) : "/"));
            }

            return entries;
        }

        private static bool TryCreateDirectoryLink(string link, string target)
        {
            try
            {
                var windows = Path.DirectorySeparatorChar == '\\';
                var start = windows
                    ? new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"")
                    : new ProcessStartInfo("/bin/ln", "-s \"" + target + "\" \"" + link + "\"");
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                start.RedirectStandardOutput = true;
                start.RedirectStandardError = true;
                using (var process = Process.Start(start))
                {
                    process.WaitForExit(10000);
                    return process.HasExited && process.ExitCode == 0 && Directory.Exists(link);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static byte[] Png(int width, int height)
        {
            var data = new byte[33];
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(data, 0);
            WriteBigEndian(data, 8, 13);
            Encoding.ASCII.GetBytes("IHDR").CopyTo(data, 12);
            WriteBigEndian(data, 16, width);
            WriteBigEndian(data, 20, height);
            data[24] = 8;
            data[25] = 6;
            return data;
        }

        private static byte[] Jpeg(int width, int height)
        {
            return new byte[]
            {
                0xFF, 0xD8,
                0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, // APP0（中身は読み飛ばす）
                0xFF, 0xC0, 0x00, 0x0B, 0x08,
                (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width,
                0x01, 0x01, 0x11, 0x00,
                0xFF, 0xD9,
            };
        }

        private static void WriteBigEndian(byte[] data, int offset, int value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8);
            data[offset + 3] = (byte)value;
        }

        private static void AssertError(IReadOnlyList<VlatDiagnostic> diagnostics, string path, string messagePart)
        {
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.Severity == VlatDiagnosticSeverity.Error && diagnostic.Path == path &&
                    (messagePart == null || diagnostic.Message.IndexOf(messagePart, StringComparison.Ordinal) >= 0))
                {
                    return;
                }
            }

            Assert.Fail("期待したErrorがありません: {0} {1}\n{2}", path, messagePart, Describe(diagnostics));
        }

        private static void AssertWarning(IReadOnlyList<VlatDiagnostic> diagnostics, string messagePart)
        {
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.Severity == VlatDiagnosticSeverity.Warning &&
                    diagnostic.Message.IndexOf(messagePart, StringComparison.Ordinal) >= 0)
                {
                    return;
                }
            }

            Assert.Fail("期待したWarningがありません: {0}\n{1}", messagePart, Describe(diagnostics));
        }

        private static void AssertNoMessage(IReadOnlyList<VlatDiagnostic> diagnostics, string messagePart)
        {
            foreach (var diagnostic in diagnostics)
            {
                Assert.That(diagnostic.Message, Does.Not.Contain(messagePart), Describe(diagnostics));
            }
        }

        private static string Describe(IReadOnlyList<VlatDiagnostic> diagnostics)
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
