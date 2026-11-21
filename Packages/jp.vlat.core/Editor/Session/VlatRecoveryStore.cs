using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;
using System.Text;

namespace VLat.Editor.Authoring
{
    [Serializable]
    internal sealed class VlatRecoveryManifest
    {
        // v3: keyform位置差分をpack済み表現で保持するsnapshot(D-011)。v1/v2も読込可能。
        // v3を旧実装が読むと未知versionとして隔離されるため、差分を黙って失わない。
        [SerializeField] internal int version = 3;
        [SerializeField] internal string snapshotFile = string.Empty;
        [SerializeField] internal string sourcePath = string.Empty;
        [SerializeField] internal long savedRevision;
        [SerializeField] internal string savedContent;
        [SerializeField] internal string sourceSignature;
        [SerializeField] internal string snapshotHash;
        [SerializeField] internal bool sourceConflict;
        [SerializeField] internal int selectionKind;
        [SerializeField] internal string selectionIdentity = string.Empty;
        [SerializeField] internal string selectionParentIdentity = string.Empty;
    }

    internal sealed class VlatRecoveryState
    {
        internal VlatRecoveryState(
            VlatAuthoringDocument document,
            long savedRevision,
            VlatEditorSelectionKind selectionKind,
            string selectionIdentity,
            string selectionParentIdentity, string savedContent, string sourceSignature, bool sourceConflict)
        {
            SavedContent = savedContent;
            SourceSignature = sourceSignature;
            SourceConflict = sourceConflict;
            Document = document;
            SavedRevision = savedRevision;
            SelectionKind = selectionKind;
            SelectionIdentity = selectionIdentity;
            SelectionParentIdentity = selectionParentIdentity;
        }

        internal string SavedContent { get; }
        internal string SourceSignature { get; }
        internal bool SourceConflict { get; }
        internal VlatAuthoringDocument Document { get; }
        internal long SavedRevision { get; }
        internal VlatEditorSelectionKind SelectionKind { get; }
        internal string SelectionIdentity { get; }
        internal string SelectionParentIdentity { get; }
    }

    internal sealed class VlatRecoveryStore
    {
        private const string ManifestFileName = "editor-session.json";
        private readonly string _directory;

        internal VlatRecoveryStore(string directory = null)
        {
            _directory = string.IsNullOrWhiteSpace(directory)
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "VLat"))
                : Path.GetFullPath(directory);
        }

        internal string DirectoryPath => _directory;
        internal string LastReadError { get; private set; }
        internal string RejectedDirectory { get; private set; }

        internal void Write(
            VlatAuthoringDocument document,
            long savedRevision,
            VlatEditorSelection selection, string savedContent = null, string sourceSignature = null, bool sourceConflict = false)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (selection == null)
            {
                throw new ArgumentNullException(nameof(selection));
            }

            Directory.CreateDirectory(_directory);
            var snapshotFile = "snapshot-" + Guid.NewGuid().ToString("N") + ".json";
            var snapshotPath = Path.Combine(_directory, snapshotFile);
            var manifestPath = Path.Combine(_directory, ManifestFileName);
            var manifestTempPath = manifestPath + ".tmp";

            var published = false;
            try
            {
                DeleteIfExists(manifestTempPath);
                // 大量のvertexDeltaではindentだけでsnapshotが数倍になる。manifest v2の
                // schema/hash/atomic publishは維持し、機械用snapshotの空白だけを省く。
                byte[] snapshot;
                using (VlatEditorProfiling.Measure(VlatEditorPhase.RecoverySerialize))
                {
                    snapshot = SnapshotEncoding.GetBytes(EditorJsonUtility.ToJson(document, false));
                    document.ReleaseSerializationCaches();
                }
                using (VlatEditorProfiling.Measure(VlatEditorPhase.RecoveryFileWrite))
                    WriteDurable(snapshotPath, snapshot);
                // 読込側はReadAllText→UTF-8 bytesでhashする。BOMなしUTF-8なので同じbytes列になる。
                string snapshotHash;
                using (VlatEditorProfiling.Measure(VlatEditorPhase.RecoveryHash))
                    snapshotHash = VlatSessionFingerprint.Hash(snapshot);
                var manifest = new VlatRecoveryManifest
                {
                    snapshotFile = snapshotFile,
                    sourcePath = document.SourcePath ?? string.Empty,
                    savedRevision = savedRevision,
                    savedContent = savedContent,
                    sourceSignature = sourceSignature,
                    sourceConflict = sourceConflict,
                    snapshotHash = snapshotHash,
                    selectionKind = (int)selection.Kind,
                    selectionIdentity = selection.Identity,
                    selectionParentIdentity = selection.ParentIdentity,
                };
                WriteDurable(manifestTempPath, SnapshotEncoding.GetBytes(JsonUtility.ToJson(manifest, true)));
                ReplaceFile(manifestTempPath, manifestPath);
                published = true;
                // publish後のcleanup失敗で、manifestが指すsnapshotを消さない。
                try { DeleteOtherSnapshots(snapshotFile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            catch
            {
                DeleteIfExists(manifestTempPath);
                if (!published) DeleteIfExists(snapshotPath);
                throw;
            }
        }

        internal bool TryRead(out VlatRecoveryState state)
        {
            state = null;
            LastReadError = null;
            var manifestPath = Path.Combine(_directory, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                return false;
            }

            VlatAuthoringDocument document = null;
            try
            {
                var manifest = JsonUtility.FromJson<VlatRecoveryManifest>(File.ReadAllText(manifestPath));
                if (manifest == null ||
                    (manifest.version != 1 && manifest.version != 2 && manifest.version != 3) ||
                    string.IsNullOrWhiteSpace(manifest.snapshotFile) ||
                    !manifest.snapshotFile.StartsWith("snapshot-", StringComparison.Ordinal) ||
                    !manifest.snapshotFile.EndsWith(".json", StringComparison.Ordinal) ||
                    !string.Equals(
                        Path.GetFileName(manifest.snapshotFile),
                        manifest.snapshotFile,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Recovery manifestが不正です。");
                }

                var snapshotPath = Path.Combine(_directory, manifest.snapshotFile);
                var snapshot = File.ReadAllText(snapshotPath);
                if (manifest.version >= 2 && manifest.snapshotHash != VlatSessionFingerprint.Hash(Encoding.UTF8.GetBytes(snapshot)))
                    throw new InvalidDataException("Recovery snapshotのhashが一致しません。");
                var json = JObject.Parse(snapshot);
                json = json["MonoBehaviour"] as JObject ?? json;
                if (!(json["_metadata"] is JObject) || !(json["_textures"] is JArray) ||
                    !(json["_bands"] is JArray) || !(json["_layers"] is JArray) || json["_revision"] == null)
                    throw new InvalidDataException("Recovery snapshotの必須データがありません。");
                document = VlatAuthoringDocument.CreateTemporary();
                EditorJsonUtility.FromJsonOverwrite(snapshot, document);
                ValidateSnapshot(document);
                document.hideFlags = HideFlags.HideAndDontSave;
                document.SetSourcePath(string.IsNullOrEmpty(manifest.sourcePath) ? null : manifest.sourcePath);
                document.EnsureEditorIdentities();

                var kind = Enum.IsDefined(typeof(VlatEditorSelectionKind), manifest.selectionKind)
                    ? (VlatEditorSelectionKind)manifest.selectionKind
                    : VlatEditorSelectionKind.Document;
                state = new VlatRecoveryState(
                    document,
                    manifest.savedRevision,
                    kind,
                    manifest.selectionIdentity,
                    manifest.selectionParentIdentity,
                    manifest.savedContent, manifest.sourceSignature, manifest.sourceConflict);
                return true;
            }
            catch (Exception exception)
            {
                LastReadError = "Recoveryを復元できません: " + exception.Message;
                if (document != null)
                {
                    UnityEngine.Object.DestroyImmediate(document);
                }

                Quarantine();
                return false;
            }
        }

        private static void ValidateSnapshot(VlatAuthoringDocument document)
        {
            if (document.Metadata == null || document.Metadata.MeshBindings == null || document.KeyformGroups == null)
                throw new InvalidDataException("RecoveryのDocumentが不正です。");
            if (document.KeyformGroups.Count > 1) throw new InvalidDataException("RecoveryのKeyform能力が未対応です。");
            foreach (var group in document.KeyformGroups)
            {
                if (group == null || group.Keys == null || group.Keys.Count < 1 || group.Keys.Count > 16 ||
                    group.CoordinateSpace != "bind-mesh-local" || group.Interpolation != "yaw-smoothstep")
                    throw new InvalidDataException("RecoveryのKeyform形式が不正または未対応です。");
                foreach (var key in group.Keys)
                {
                    if (key == null || key.Deltas == null) throw new InvalidDataException("Recoveryのキーが不正です。");
                    if (key.SerializationError != null)
                        throw new InvalidDataException("Recoveryのキー差分が不正です: " + key.SerializationError);
                }
            }
        }

        private void Quarantine()
        {
            try
            {
                RejectedDirectory = Path.Combine(_directory, "Rejected", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(RejectedDirectory);
                foreach (var file in Directory.GetFiles(_directory, "snapshot-*.json"))
                    File.Move(file, Path.Combine(RejectedDirectory, Path.GetFileName(file)));
                foreach (var name in new[] { ManifestFileName, ManifestFileName + ".tmp" })
                {
                    var path = Path.Combine(_directory, name);
                    if (File.Exists(path)) File.Move(path, Path.Combine(RejectedDirectory, name));
                }
                LastReadError += " 隔離先: " + RejectedDirectory;
            }
            catch (Exception exception) { LastReadError += " 隔離に失敗しました: " + exception.Message; }
        }

        internal void Clear()
        {
            if (!Directory.Exists(_directory))
            {
                return;
            }

            DeleteIfExists(Path.Combine(_directory, ManifestFileName));
            DeleteIfExists(Path.Combine(_directory, ManifestFileName + ".tmp"));
            var snapshots = Directory.GetFiles(_directory, "snapshot-*.json");
            for (var index = 0; index < snapshots.Length; index++)
            {
                DeleteIfExists(snapshots[index]);
            }
        }

        // StreamWriterの既定と同じBOMなし・不正surrogateで例外のUTF-8。従来と同じbytes列を書く。
        private static readonly UTF8Encoding SnapshotEncoding = new UTF8Encoding(false, true);
        private const int DurableChunkBytes = 1 << 20;

        private static void WriteDurable(string path, byte[] content)
        {
            // WriteThroughとFlush(true)によるdurabilityは従来どおり。4 KiB単位の書込みthroughを
            // 1 MiB単位にまとめ、大きなsnapshotでの同期I/O回数だけを減らす。
            using (var stream = new FileStream(
                       path,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       DurableChunkBytes,
                       FileOptions.WriteThrough))
            {
                for (var offset = 0; offset < content.Length; offset += DurableChunkBytes)
                    stream.Write(content, offset, Math.Min(DurableChunkBytes, content.Length - offset));
                stream.Flush(true);
            }
        }

        private static void ReplaceFile(string source, string destination)
        {
            if (File.Exists(destination))
            {
                File.Replace(source, destination, null);
            }
            else
            {
                File.Move(source, destination);
            }
        }

        private void DeleteOtherSnapshots(string currentFile)
        {
            var snapshots = Directory.GetFiles(_directory, "snapshot-*.json");
            for (var index = 0; index < snapshots.Length; index++)
            {
                if (!string.Equals(
                        Path.GetFileName(snapshots[index]),
                        currentFile,
                        StringComparison.Ordinal))
                {
                    DeleteIfExists(snapshots[index]);
                }
            }
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
