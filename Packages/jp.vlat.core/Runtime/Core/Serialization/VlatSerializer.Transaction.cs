using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VLat.Core.Validation;

namespace VLat.Core.Serialization
{
    /// <summary>中断された保存を復旧した結果の種類です。</summary>
    public enum VlatInterruptedSaveOutcome
    {
        /// <summary>中断された保存はありませんでした。</summary>
        None,

        /// <summary>新しい内容は全て書き込み済みでした。残っていた一時ファイルを片付けました。</summary>
        CompletedSaveKept,

        /// <summary>保存前の内容へ戻しました。</summary>
        PreviousContentRestored,

        /// <summary>復旧できませんでした。診断を確認してください。ファイルは変更していないか、途中までです。</summary>
        Failed,
    }

    /// <summary>中断された保存の復旧結果です。</summary>
    public sealed class VlatInterruptedSaveRecovery
    {
        /// <summary>結果を作成します。</summary>
        /// <param name="outcome">復旧の結果。</param>
        /// <param name="diagnostics">利用者向けの診断。nullなら空。</param>
        public VlatInterruptedSaveRecovery(VlatInterruptedSaveOutcome outcome, IEnumerable<VlatDiagnostic> diagnostics)
        {
            Outcome = outcome;
            Diagnostics = new List<VlatDiagnostic>(diagnostics ?? Array.Empty<VlatDiagnostic>()).AsReadOnly();
        }

        /// <summary>復旧の結果。</summary>
        public VlatInterruptedSaveOutcome Outcome { get; }

        /// <summary>利用者向けの診断。</summary>
        public IReadOnlyList<VlatDiagnostic> Diagnostics { get; }

        /// <summary>ファイルが一貫した状態（中断なし・保存後・保存前のいずれか）になったかどうか。</summary>
        public bool Success => Outcome != VlatInterruptedSaveOutcome.Failed;
    }

    public static partial class VlatSerializer
    {
        private const string JournalFormat = "vlat-save-journal/1";
        private const string JournalSuffix = ".vlat-save-journal";
        private const long MaxJournalBytes = 1024 * 1024;
        private static readonly Regex SidecarName = new Regex(@"^\..+\.[0-9a-f]{32}\.(?:tmp|bak)$", RegexOptions.CultureInvariant);

        /// <summary>試験専用。保存の各段階で呼び出し、<see cref="SimulatedCrashException"/>で異常終了を模擬します。</summary>
#pragma warning disable CS0649 // 試験からのみ代入する。
        [ThreadStatic]
        internal static Action<VlatSaveStep, int> SaveStepHook;
#pragma warning restore CS0649

        /// <summary>
        /// 前回の保存が異常終了などで中断されていた場合に、JSONとbinを一貫した状態へ戻します。
        /// 新しい内容が全て書き込み済みなら保存後の状態を残し、そうでなければ保存前の内容へ戻します。
        /// </summary>
        /// <param name="jsonPath">.vlat.jsonファイルのパス。</param>
        /// <param name="policy">復旧で触れてよい範囲。保存時と同じポリシーを指定します。nullなら既定。</param>
        /// <returns>復旧結果。</returns>
        public static VlatInterruptedSaveRecovery RecoverInterruptedSave(string jsonPath, VlatFileAccessPolicy policy = null)
        {
            policy = policy ?? VlatFileAccessPolicy.Default;
            var diagnostics = new List<VlatDiagnostic>();
            string journalPath;
            try
            {
                journalPath = GetJournalPath(Path.GetFullPath(jsonPath));
            }
            catch (Exception exception)
            {
                diagnostics.Add(Error("$", 0, 0, "保存ジャーナルの場所を決定できません: " + exception.Message));
                return new VlatInterruptedSaveRecovery(VlatInterruptedSaveOutcome.Failed, diagnostics);
            }

            if (!File.Exists(journalPath))
            {
                return new VlatInterruptedSaveRecovery(VlatInterruptedSaveOutcome.None, diagnostics);
            }

            FileStream journalStream;
            if (!TryOpenJournalExclusive(journalPath, out journalStream, diagnostics))
            {
                return new VlatInterruptedSaveRecovery(VlatInterruptedSaveOutcome.Failed, diagnostics);
            }

            bool restored;
            using (journalStream)
            {
                List<JournalEntry> entries;
                if (!TryReadJournal(journalStream, Path.GetFullPath(jsonPath), policy, out entries, diagnostics))
                {
                    return new VlatInterruptedSaveRecovery(VlatInterruptedSaveOutcome.Failed, diagnostics);
                }

                var allNew = true;
                for (var i = 0; i < entries.Count; i++)
                {
                    allNew &= HasContent(entries[i].Target, entries[i].Length, entries[i].Sha256);
                }

                try
                {
                    if (allNew)
                    {
                        for (var i = 0; i < entries.Count; i++)
                        {
                            DeleteIfExists(entries[i].Temporary);
                            DeleteIfExists(entries[i].Backup);
                        }

                        restored = false;
                    }
                    else
                    {
                        for (var i = entries.Count - 1; i >= 0; i--)
                        {
                            RestoreEntry(entries[i], diagnostics);
                        }

                        restored = true;
                    }
                }
                catch (Exception exception)
                {
                    diagnostics.Add(Error("$", 0, 0, "中断された保存を復旧できません（保存ジャーナルは残しています）: " + exception.Message));
                    return new VlatInterruptedSaveRecovery(VlatInterruptedSaveOutcome.Failed, diagnostics);
                }
            }

            try
            {
                File.Delete(journalPath);
            }
            catch (Exception exception)
            {
                diagnostics.Add(Warning("$", "復旧後に保存ジャーナルを削除できませんでした: " + exception.Message));
            }

            diagnostics.Add(Warning("$", restored
                ? "前回の保存は途中で中断されていたため、保存前の内容へ戻しました。"
                : "前回の保存は書き込み済みでした。残っていた一時ファイルを片付けました。"));
            return new VlatInterruptedSaveRecovery(
                restored ? VlatInterruptedSaveOutcome.PreviousContentRestored : VlatInterruptedSaveOutcome.CompletedSaveKept,
                diagnostics);
        }

        /// <summary>保存ジャーナルのパスを返します。試験と文書の説明用です。</summary>
        internal static string GetJournalPath(string fullJsonPath)
        {
            var directory = Path.GetDirectoryName(fullJsonPath) ?? string.Empty;
            return Path.Combine(directory, "." + Path.GetFileName(fullJsonPath) + JournalSuffix);
        }

        /// <summary>Load時に、中断された保存が残っていないかを読み取り専用で確認します。</summary>
        private static IReadOnlyList<VlatDiagnostic> InspectInterruptedSave(string fullJsonPath, VlatFileAccessPolicy policy)
        {
            var diagnostics = new List<VlatDiagnostic>();
            var journalPath = GetJournalPath(fullJsonPath);
            if (!File.Exists(journalPath))
            {
                return diagnostics;
            }

            FileStream journalStream;
            if (!TryOpenJournalExclusive(journalPath, out journalStream, diagnostics))
            {
                return diagnostics;
            }

            using (journalStream)
            {
                List<JournalEntry> entries;
                if (!TryReadJournal(journalStream, fullJsonPath, policy, out entries, diagnostics))
                {
                    return diagnostics;
                }

                var newCount = 0;
                for (var i = 0; i < entries.Count; i++)
                {
                    if (HasContent(entries[i].Target, entries[i].Length, entries[i].Sha256))
                    {
                        newCount++;
                    }
                }

                if (newCount == entries.Count)
                {
                    diagnostics.Add(Warning("$", "前回の保存は書き込み済みですが、後片付けの前に中断されています。" +
                                                 "Kakudoll Editorで開くか RecoverInterruptedSave で一時ファイルを片付けられます。"));
                }
                else if (newCount == 0)
                {
                    diagnostics.Add(Warning("$", "前回の保存は反映前に中断されています。保存前の内容を読み込みます。" +
                                                 "Kakudoll Editorで開くか RecoverInterruptedSave で一時ファイルを片付けられます。"));
                }
                else
                {
                    diagnostics.Add(Error("$", 0, 0, "前回の保存が途中で中断され、JSONとbinの組み合わせが一致していない可能性があります。" +
                                                     "Kakudoll Editorで開くか RecoverInterruptedSave を呼ぶと、保存前の内容へ戻します。"));
                }
            }

            return diagnostics;
        }

        private static bool TryWriteTransaction(string fullJsonPath, IReadOnlyList<PendingWrite> writes, out string error)
        {
            error = null;
            var journalPath = GetJournalPath(fullJsonPath);
            FileStream journal;
            try
            {
                var jsonDirectory = Path.GetDirectoryName(fullJsonPath);
                if (!string.IsNullOrEmpty(jsonDirectory))
                {
                    Directory.CreateDirectory(jsonDirectory);
                }

                // CreateNewが、同じ文書への同時保存と、復旧されていない中断保存の両方を検出する。
                journal = new FileStream(journalPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            }
            catch (IOException) when (File.Exists(journalPath))
            {
                error = "前回の保存が完了していないか、別の保存が進行中のため保存しません。" +
                        "Kakudoll Editorで開き直すか RecoverInterruptedSave で復旧してください。";
                return false;
            }
            catch (Exception exception)
            {
                error = "VLatファイルを保存できません: " + exception.Message;
                return false;
            }

            var journalKept = false;
            try
            {
                try
                {
                    var bytes = new UTF8Encoding(false).GetBytes(CreateJournal(fullJsonPath, writes));
                    journal.Write(bytes, 0, bytes.Length);
                    journal.Flush(true);
                    InvokeHook(VlatSaveStep.JournalWritten, 0);

                    for (var i = 0; i < writes.Count; i++)
                    {
                        writes[i].Stage();
                        InvokeHook(VlatSaveStep.Staged, i);
                    }

                    for (var i = 0; i < writes.Count; i++)
                    {
                        writes[i].Commit();
                        InvokeHook(VlatSaveStep.Committed, i);
                    }
                }
                catch (SimulatedCrashException)
                {
                    // 試験用の異常終了。プロセスが落ちたときと同じく、rollbackもjournal削除もしない。
                    journalKept = true;
                    throw;
                }
                catch (Exception exception)
                {
                    var rollbackErrors = new StringBuilder();
                    for (var i = writes.Count - 1; i >= 0; i--)
                    {
                        string rollbackError;
                        if (!writes[i].Rollback(out rollbackError) && !string.IsNullOrEmpty(rollbackError))
                        {
                            if (rollbackErrors.Length > 0)
                            {
                                rollbackErrors.Append(" / ");
                            }

                            rollbackErrors.Append(rollbackError);
                        }
                    }

                    error = "VLatファイルを保存できません: " + exception.Message;
                    if (rollbackErrors.Length > 0)
                    {
                        // 復元に失敗したら、journalを残して次回Loadで検出・RecoverInterruptedSaveで再試行できるようにする。
                        journalKept = true;
                        error += "（復元にも失敗しました: " + rollbackErrors + "。次に開くときに復旧を試みます）";
                    }

                    return false;
                }
            }
            finally
            {
                journal.Dispose();
                if (!journalKept)
                {
                    try
                    {
                        File.Delete(journalPath);
                    }
                    catch (Exception)
                    {
                        // 全commit済みのjournalが残っても、次回Loadは「書き込み済み」と判定する。
                    }
                }
            }

            InvokeHook(VlatSaveStep.JournalDeleted, 0);

            // journal削除で保存が確定する。backup削除失敗をrollbackへ流すと、
            // 先に削除済みのbackupを復元できないため、cleanupはbest-effortで行う。
            for (var i = 0; i < writes.Count; i++)
            {
                writes[i].Complete();
            }

            return true;
        }

        private static void InvokeHook(VlatSaveStep step, int index)
        {
            var hook = SaveStepHook;
            if (hook != null)
            {
                hook(step, index);
            }
        }

        private static string CreateJournal(string fullJsonPath, IReadOnlyList<PendingWrite> writes)
        {
            var jsonDirectory = Path.GetDirectoryName(fullJsonPath) ?? string.Empty;
            var entries = new JArray();
            for (var i = 0; i < writes.Count; i++)
            {
                var write = writes[i];
                entries.Add(new JObject
                {
                    ["target"] = Path.GetRelativePath(jsonDirectory, write.TargetPath).Replace('\\', '/'),
                    ["temporary"] = Path.GetFileName(write.TemporaryPath),
                    ["backup"] = Path.GetFileName(write.BackupPath),
                    ["hadOriginal"] = File.Exists(write.TargetPath),
                    ["length"] = write.Length,
                    ["sha256"] = write.Sha256,
                });
            }

            var root = new JObject
            {
                ["format"] = JournalFormat,
                ["json"] = Path.GetFileName(fullJsonPath),
                ["entries"] = entries,
            };
            return root.ToString(Formatting.Indented);
        }

        private static bool TryOpenJournalExclusive(string journalPath, out FileStream stream, List<VlatDiagnostic> diagnostics)
        {
            stream = null;
            try
            {
                stream = new FileStream(journalPath, FileMode.Open, FileAccess.Read, FileShare.None);
                return true;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (IOException)
            {
                diagnostics.Add(Error("$", 0, 0, "別の処理がこのファイルを保存中です。保存が終わってから開き直してください。"));
                return false;
            }
            catch (Exception exception)
            {
                diagnostics.Add(Error("$", 0, 0, "保存ジャーナルを読めません: " + exception.Message));
                return false;
            }
        }

        private static bool TryReadJournal(
            FileStream stream,
            string fullJsonPath,
            VlatFileAccessPolicy policy,
            out List<JournalEntry> entries,
            List<VlatDiagnostic> diagnostics)
        {
            entries = null;
            const string broken = "保存ジャーナルが壊れているため、自動では復旧しません。JSONとbinを手元のバックアップと比べて確認してください: ";
            try
            {
                if (stream.Length > MaxJournalBytes)
                {
                    diagnostics.Add(Error("$", 0, 0, broken + "サイズが上限を超えています。"));
                    return false;
                }

                string text;
                using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), false, 4096, true))
                {
                    text = reader.ReadToEnd();
                }

                var root = JObject.Parse(text, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if ((string)root["format"] != JournalFormat ||
                    !string.Equals((string)root["json"], Path.GetFileName(fullJsonPath), StringComparison.Ordinal))
                {
                    diagnostics.Add(Error("$", 0, 0, broken + "形式または対象ファイル名が一致しません。"));
                    return false;
                }

                var array = root["entries"] as JArray;
                if (array == null || array.Count == 0 || array.Count > policy.Limits.MaxBufferCount + 1)
                {
                    diagnostics.Add(Error("$", 0, 0, broken + "entriesが不正です。"));
                    return false;
                }

                var result = new List<JournalEntry>(array.Count);
                foreach (var token in array)
                {
                    var item = token as JObject;
                    if (item == null)
                    {
                        diagnostics.Add(Error("$", 0, 0, broken + "entryが不正です。"));
                        return false;
                    }

                    var target = (string)item["target"];
                    var temporary = (string)item["temporary"];
                    var backup = (string)item["backup"];
                    var sha256 = (string)item["sha256"];
                    var lengthToken = item["length"];
                    var hadOriginalToken = item["hadOriginal"];
                    if (target == null || temporary == null || backup == null || sha256 == null ||
                        lengthToken == null || lengthToken.Type != JTokenType.Integer ||
                        hadOriginalToken == null || hadOriginalToken.Type != JTokenType.Boolean ||
                        !Regex.IsMatch(sha256, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant))
                    {
                        diagnostics.Add(Error("$", 0, 0, broken + "entryの項目が不足しています。"));
                        return false;
                    }

                    string targetPath;
                    string pathError;
                    if (!VlatExternalFiles.TryResolve(fullJsonPath, Uri.EscapeDataString(target).Replace("%2F", "/"),
                            policy, out targetPath, out pathError))
                    {
                        diagnostics.Add(Error("$", 0, 0, broken + pathError));
                        return false;
                    }

                    var targetName = Path.GetFileName(targetPath);
                    if (!IsSidecarOf(temporary, targetName, ".tmp") || !IsSidecarOf(backup, targetName, ".bak"))
                    {
                        diagnostics.Add(Error("$", 0, 0, broken + "一時ファイル名が規則と一致しません。"));
                        return false;
                    }

                    var directory = Path.GetDirectoryName(targetPath) ?? string.Empty;
                    result.Add(new JournalEntry(
                        targetPath,
                        Path.Combine(directory, temporary),
                        Path.Combine(directory, backup),
                        (bool)hadOriginalToken,
                        (long)lengthToken,
                        sha256));
                }

                entries = result;
                return true;
            }
            catch (Exception exception)
            {
                diagnostics.Add(Error("$", 0, 0, broken + exception.Message));
                return false;
            }
        }

        private static bool IsSidecarOf(string sidecar, string targetName, string suffix)
        {
            return SidecarName.IsMatch(sidecar) &&
                   sidecar.Length == targetName.Length + 1 + 1 + 32 + suffix.Length &&
                   sidecar.StartsWith("." + targetName + ".", StringComparison.Ordinal) &&
                   sidecar.EndsWith(suffix, StringComparison.Ordinal);
        }

        private static void RestoreEntry(JournalEntry entry, List<VlatDiagnostic> diagnostics)
        {
            DeleteIfExists(entry.Temporary);
            if (File.Exists(entry.Backup))
            {
                // backupはcommit時にだけ作られる。targetは新しい内容か、置換途中で失われている。
                if (File.Exists(entry.Target))
                {
                    File.Replace(entry.Backup, entry.Target, null);
                }
                else
                {
                    File.Move(entry.Backup, entry.Target);
                }

                return;
            }

            if (entry.HadOriginal)
            {
                if (!File.Exists(entry.Target))
                {
                    diagnostics.Add(Warning("$", "保存前のファイルが見つかりません: " + Path.GetFileName(entry.Target)));
                }

                // backupがなければ未commit。targetは保存前の内容のまま。
                return;
            }

            // 新規作成されたtargetは、新しい内容と一致するときだけ削除する（他の処理が作ったファイルは残す）。
            if (HasContent(entry.Target, entry.Length, entry.Sha256))
            {
                File.Delete(entry.Target);
            }
        }

        private static bool HasContent(string path, long length, string sha256)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length != length)
                {
                    return false;
                }

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    return string.Equals(HashStream(stream), sha256, StringComparison.Ordinal);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string HashStream(Stream stream)
        {
            using (var sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(stream));
            }
        }

        private static string HashBytes(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(data));
            }
        }

        private static string ToHex(byte[] hash)
        {
            var builder = new StringBuilder(hash.Length * 2);
            for (var i = 0; i < hash.Length; i++)
            {
                builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static void DeleteIfExists(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static VlatDiagnostic Warning(string path, string message)
        {
            return new VlatDiagnostic(VlatDiagnosticSeverity.Warning, path, 0, 0, message);
        }

        private sealed class JournalEntry
        {
            public JournalEntry(string target, string temporary, string backup, bool hadOriginal, long length, string sha256)
            {
                Target = target;
                Temporary = temporary;
                Backup = backup;
                HadOriginal = hadOriginal;
                Length = length;
                Sha256 = sha256;
            }

            public string Target { get; }

            public string Temporary { get; }

            public string Backup { get; }

            public bool HadOriginal { get; }

            public long Length { get; }

            public string Sha256 { get; }
        }

        private sealed class PendingWrite
        {
            private readonly byte[] _contents;
            private string _temporaryPath;
            private string _backupPath;
            private bool _hadOriginal;
            private bool _committed;

            public PendingWrite(string targetPath, byte[] contents)
            {
                TargetPath = targetPath;
                _contents = contents;
                // journalへ先に記録するため、一時ファイルとbackupの名前を事前に決める。
                _temporaryPath = CreateSidecarPath(targetPath, ".tmp");
                _backupPath = CreateSidecarPath(targetPath, ".bak");
                TemporaryPath = _temporaryPath;
                BackupPath = _backupPath;
                Length = contents.LongLength;
                Sha256 = HashBytes(contents);
            }

            public string TargetPath { get; }

            public string TemporaryPath { get; }

            public string BackupPath { get; }

            public long Length { get; }

            public string Sha256 { get; }

            public void Stage()
            {
                var directory = Path.GetDirectoryName(TargetPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (var stream = new FileStream(
                           _temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    stream.Write(_contents, 0, _contents.Length);
                    stream.Flush(true);
                }
            }

            public void Commit()
            {
                _hadOriginal = File.Exists(TargetPath);
                if (_hadOriginal)
                {
                    File.Replace(_temporaryPath, TargetPath, _backupPath);
                }
                else
                {
                    File.Move(_temporaryPath, TargetPath);
                    _backupPath = null;
                }

                _committed = true;
                _temporaryPath = null;
            }

            public void Complete()
            {
                try
                {
                    SafeDelete(_backupPath);
                }
                catch (Exception)
                {
                    // 保存自体は完了しているため、一意名のバックアップを残して処理を継続します。
                }

                _backupPath = null;
            }

            public bool Rollback(out string error)
            {
                error = null;
                try
                {
                    if (_committed)
                    {
                        if (_hadOriginal && !string.IsNullOrEmpty(_backupPath) && File.Exists(_backupPath))
                        {
                            if (File.Exists(TargetPath))
                            {
                                File.Replace(_backupPath, TargetPath, null);
                            }
                            else
                            {
                                File.Move(_backupPath, TargetPath);
                            }
                        }
                        else if (!_hadOriginal)
                        {
                            SafeDelete(TargetPath);
                        }
                    }
                    else if (!string.IsNullOrEmpty(_backupPath) && File.Exists(_backupPath))
                    {
                        // File.Replaceが途中で失敗し、元ファイルがbackup名へ移っただけの場合は戻す。
                        // targetも残っているなら、どちらが元か判断できないためbackupを消さずに残す。
                        if (File.Exists(TargetPath))
                        {
                            SafeDelete(_temporaryPath);
                            error = TargetPath + ": 置換途中の状態を判別できないため、backupを残しました。";
                            return false;
                        }

                        File.Move(_backupPath, TargetPath);
                    }

                    SafeDelete(_temporaryPath);
                    SafeDelete(_backupPath);
                    return true;
                }
                catch (Exception exception)
                {
                    error = TargetPath + ": " + exception.Message;
                    return false;
                }
            }

            private static string CreateSidecarPath(string targetPath, string suffix)
            {
                var directory = Path.GetDirectoryName(targetPath) ?? string.Empty;
                var fileName = Path.GetFileName(targetPath);
                return Path.Combine(
                    directory,
                    "." + fileName + "." + Guid.NewGuid().ToString("N") + suffix);
            }

            private static void SafeDelete(string path)
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    /// <summary>試験専用。保存処理の段階です。</summary>
    internal enum VlatSaveStep
    {
        JournalWritten,
        Staged,
        Committed,
        JournalDeleted,
    }

    /// <summary>試験専用。保存中の異常終了（プロセス強制終了・電源断）を模擬する例外です。</summary>
    internal sealed class SimulatedCrashException : Exception
    {
        public SimulatedCrashException(string message)
            : base(message)
        {
        }
    }
}
