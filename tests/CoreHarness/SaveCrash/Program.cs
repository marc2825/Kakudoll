using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using VLat.Core.Model;
using VLat.Core.Serialization;

// 実プロセスの強制終了で保存の中断耐性を確かめる。Unity・VRChatの代替ではない。
//   dotnet run -c Release -- [回数] [作業フォルダ]
// 子プロセスが2つの版（A/B: JSONとbinの内容が違い、binの長さは同じ）を交互に保存し続け、
// 親がランダムな時点でkillする。毎回、復旧後にLoadでき、内容がAかBのどちらかに完全に一致することを確かめる。
internal static class Program
{
    private const int BinLength = 256 * 1024;

    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--child") return Child(args[1]);
        var iterations = args.Length >= 1 ? int.Parse(args[0]) : 50;
        var directory = args.Length >= 2 ? args[1] : Path.Combine(Path.GetTempPath(), "VlatSaveCrash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var jsonPath = Path.Combine(directory, "crash.vlat.json");
        var random = new Random(20261122);
        int torn = 0, restored = 0, kept = 0, clean = 0, failures = 0;
        for (var i = 0; i < iterations; i++)
        {
            var self = Environment.ProcessPath;
            var dll = typeof(Program).Assembly.Location;
            var start = self.EndsWith("dotnet", StringComparison.Ordinal) || self.EndsWith("dotnet.exe", StringComparison.Ordinal)
                ? new ProcessStartInfo(self, "\"" + dll + "\" --child \"" + jsonPath + "\"")
                : new ProcessStartInfo(self, "--child \"" + jsonPath + "\"");
            start.UseShellExecute = false;
            start.RedirectStandardOutput = true;
            using (var child = Process.Start(start))
            {
                child.StandardOutput.ReadLine(); // 最初の保存が終わるまで待つ
                Thread.Sleep(random.Next(0, 40));
                child.Kill();
                child.WaitForExit();
            }

            var inspected = VlatSerializer.Load(jsonPath);
            if (!inspected.Success) torn++;
            var recovery = VlatSerializer.RecoverInterruptedSave(jsonPath);
            switch (recovery.Outcome)
            {
                case VlatInterruptedSaveOutcome.PreviousContentRestored: restored++; break;
                case VlatInterruptedSaveOutcome.CompletedSaveKept: kept++; break;
                case VlatInterruptedSaveOutcome.None: clean++; break;
                default:
                    // 復旧できないと以降の保存が全て拒否されるため、最初の失敗で止める。
                    failures++;
                    Console.WriteLine("iteration " + i + ": recovery failed: " + recovery.Diagnostics[0].Message);
                    i = iterations;
                    continue;
            }

            var loaded = VlatSerializer.Load(jsonPath);
            var name = loaded.Success ? loaded.Document.Metadata.TargetAvatar.Name : null;
            var expected = name == "A" ? (byte)1 : name == "B" ? (byte)2 : (byte)0;
            if (!loaded.Success || expected == 0 || loaded.Buffers[0].Length != BinLength ||
                Array.Exists(loaded.Buffers[0], b => b != expected))
            {
                failures++;
                Console.WriteLine("iteration " + i + ": inconsistent after recovery (" + (name ?? "load failed") + ")");
            }
        }

        Console.WriteLine("iterations=" + iterations + " tornBeforeRecovery=" + torn + " restored=" + restored +
                          " completedKept=" + kept + " noJournal=" + clean + " failures=" + failures);
        try { Directory.Delete(directory, true); } catch (IOException) { }
        return failures == 0 ? 0 : 1;
    }

    private static int Child(string jsonPath)
    {
        var first = true;
        for (var n = 0; ; n++)
        {
            var name = n % 2 == 0 ? "A" : "B";
            var bin = new byte[BinLength];
            for (var i = 0; i < bin.Length; i++) bin[i] = name == "A" ? (byte)1 : (byte)2;
            var material = new VlatMaterialSlot(0, "Material", "VRM10/MToon10");
            var mesh = new MeshBinding("Face", "Face", 4, "xxh64:0123456789abcdef", new[] { material });
            var document = new VlatDocument(
                new VlatMetadata("0.1.0", "SaveCrash", new TargetAvatar(name, "1.0", new[] { mesh })),
                new[] { new VlatBuffer("crash.vlat.bin", BinLength) },
                Array.Empty<VlatBufferView>(), Array.Empty<VlatTexture>(), Array.Empty<AngleBand>(), Array.Empty<VlatLayer>());
            var result = VlatSerializer.Save(jsonPath, document, new[] { bin });
            if (!result.Success)
            {
                // 直前のkillで残ったjournalは親が復旧済み。ここに来るのは想定外。
                Console.Error.WriteLine(result.Diagnostics[0].Message);
                return 2;
            }

            if (first)
            {
                Console.WriteLine("ready");
                Console.Out.Flush();
                first = false;
            }
        }
    }
}
