using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using VLat.Core.Serialization;

namespace VLat.Editor.Authoring
{
    // 履歴ではなく保存基準の指紋。編集/Undo/保存境界でのみ計算し、毎frameは計算しない。
    internal static class VlatSessionFingerprint
    {
        internal static string Content(VlatAuthoringDocument document, VlatAuthoringPackResult pack)
        {
            if (pack.Success) return pack.ContentFingerprint ??= Hash(Encoding.UTF8.GetBytes(pack.Json), pack.Buffers);
            // 未bindingなどの保存できないdraftも、revisionや選択identityに依存せず比較する。
            var snapshot = JObject.Parse(EditorJsonUtility.ToJson(document));
            document.ReleaseSerializationCaches();
            snapshot = snapshot["MonoBehaviour"] as JObject ?? snapshot;
            var content = new JObject();
            foreach (var name in new[] { "_metadata", "_textures", "_bands", "_layers", "_keyformGroups", "references" })
                if (snapshot[name] != null) content[name] = snapshot[name].DeepClone();
            RemoveIdentities(content);
            return Hash(Encoding.UTF8.GetBytes(content.ToString(Formatting.None)));
        }

        internal static string Source(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                var loaded = VlatSerializer.Load(path);
                return loaded.Success ? Hash(File.ReadAllBytes(path), loaded.Buffers) : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        internal static string Hash(byte[] bytes, System.Collections.Generic.IReadOnlyList<byte[]> buffers = null)
        {
            using (var sha = SHA256.Create())
            using (var stream = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write))
            {
                Write(stream, bytes);
                if (buffers != null) foreach (var buffer in buffers) Write(stream, buffer);
                stream.FlushFinalBlock();
                return "sha256:" + BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static void Write(Stream stream, byte[] data)
        {
            // 境界を含めてhash化し、JSON/binの異なる区切りを同じ列と扱わない。
            var length = data.Length;
            for (var b = 0; b < 4; b++) stream.WriteByte((byte)(length >> (b * 8)));
            stream.Write(data, 0, data.Length);
        }

        private static void RemoveIdentities(JToken token)
        {
            if (token is JObject obj) obj.Remove("_editorIdentity");
            foreach (var child in token.Children()) RemoveIdentities(child);
        }
    }
}
