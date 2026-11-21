using System;
using System.Collections.Generic;
using VLat.Core.Model;

namespace VLat.Core.Validation
{
    /// <summary>Keyform拡張の構造とバイナリ制約を検証します。</summary>
    public static partial class VlatValidator
    {
        /// <summary>Keyform拡張の対応バージョンです。旧バックエンドの対応版とは区別します。</summary>
        public const string KeyformVersion = "0.2.0";

        /// <summary>初期Keyformドキュメントで扱う頂点数の上限です。</summary>
        public const int MaximumKeyformVertices = 200000;

        /// <summary>一つのグループに格納できるキー数の上限です。</summary>
        public const int MaximumKeyformKeys = 16;

        /// <summary>Keyformドキュメントの全バイナリ合計の上限です。</summary>
        public const long MaximumKeyformBufferBytes = 16 * 1024 * 1024;

        private static void ValidateKeyformGroups(VlatDocument document,
            IReadOnlyList<byte[]> buffers, List<VlatDiagnostic> diagnostics)
        {
            if (document.KeyformGroups.Count == 0) return;
            const string path = "$.keyformGroups";
            if (document.Metadata?.Version != KeyformVersion)
                AddError(diagnostics, path, "Keyform拡張にはversion 0.2.0が必要です。");
            // 初回スライスでは合成順序を推測せず、単独グループのみを許可します。
            if (document.KeyformGroups.Count != 1 || document.Layers.Count != 0 ||
                document.AngleBands.Count != 0 || document.Textures.Count != 0)
                AddError(diagnostics, path, "初期Keyformは単独1グループのみです。Layer/AngleBand/Textureとの合成は未対応です。");

            long total = 0;
            foreach (var buffer in document.Buffers)
            {
                if (buffer == null || buffer.ByteLength < 0) continue;
                if (buffer.ByteLength > MaximumKeyformBufferBytes - total)
                {
                    AddError(diagnostics, "$.buffers", "Keyformバイナリは合計16 MiB以下にしてください。");
                    return;
                }
                total += buffer.ByteLength;
            }

            for (var i = 0; i < document.KeyformGroups.Count; i++)
            {
                var group = document.KeyformGroups[i];
                var p = path + "[" + Invariant(i) + "]";
                ValidateKeyformDefinition(group, p, diagnostics);
                if (group == null) continue;
                MeshBinding binding;
                if (TryGetMeshBinding(document, group.MeshBinding, p, diagnostics, out binding) &&
                    (binding.VertexCount < 1 || binding.VertexCount > MaximumKeyformVertices))
                    AddError(diagnostics, p + ".meshBinding", "Keyformの頂点数は1〜200000です。");
                if (group.Keys.Count > MaximumKeyformKeys) continue;
                for (var k = 0; k < group.Keys.Count; k++)
                {
                    var delta = group.Keys[k]?.Delta;
                    if (delta == null) continue;
                    if (delta.MeshBinding != group.MeshBinding)
                        AddError(diagnostics, p + ".keys[" + Invariant(k) + "]", "全キーは同じmeshBindingを参照する必要があります。");
                    if (delta.Normals != null)
                        AddError(diagnostics, p + ".keys[" + Invariant(k) + "]", "初期Keyformの法線差分は未対応です。");
                    ValidateModifier(document, buffers, delta, p + ".keys[" + Invariant(k) + "].delta", diagnostics);
                }
            }
        }

        /// <summary>補間に必要なキー集合の定義を検証します。バイナリはドキュメント検証で確認します。</summary>
        public static IReadOnlyList<VlatDiagnostic> ValidateKeyformDefinition(KeyformGroup group)
        {
            var diagnostics = new List<VlatDiagnostic>();
            ValidateKeyformDefinition(group, "$.keyformGroups[0]", diagnostics);
            return diagnostics.AsReadOnly();
        }

        private static void ValidateKeyformDefinition(KeyformGroup group, string path,
            List<VlatDiagnostic> diagnostics)
        {
            if (group == null)
            {
                AddError(diagnostics, path, "KeyformGroupはnullにできません。");
                return;
            }
            if (string.IsNullOrWhiteSpace(group.Id)) AddError(diagnostics, path + ".id", "idは必須です。");
            if (group.MeshBinding < 0) AddError(diagnostics, path + ".meshBinding", "meshBindingは非負です。");
            if (!IsTopologyHash(group.TopologyHash))
                AddError(diagnostics, path + ".topologyHash", "topologyHashはsha256:と64桁の小文字16進数です。");
            if (group.CoordinateSpace != "bind-mesh-local")
                AddError(diagnostics, path + ".coordinateSpace", "bind-mesh-local以外は未対応です。");
            if (group.Interpolation != "yaw-smoothstep")
                AddError(diagnostics, path + ".interpolation", "yaw-smoothstep以外は未対応です。");
            if (!IsFinite(group.SupportStart) || group.SupportStart < -180 || group.SupportStart >= 180 ||
                !IsFinite(group.SupportEnd) || group.SupportEnd <= group.SupportStart ||
                group.SupportEnd - group.SupportStart >= 360)
                AddError(diagnostics, path, "支持開始は[-180,180)、支持幅は(0,360)度です。");
            if (group.Keys.Count < 1 || group.Keys.Count > MaximumKeyformKeys)
                AddError(diagnostics, path + ".keys", "キー数は1〜16です。");
            var previous = group.SupportStart;
            for (var k = 0; k < group.Keys.Count; k++)
            {
                var key = group.Keys[k];
                if (key == null || !IsFinite(key.Yaw) || key.Yaw <= previous || key.Yaw >= group.SupportEnd)
                    AddError(diagnostics, path + ".keys[" + Invariant(k) + "]", "キーは支持区間の内側に、重複なしのYaw昇順で配置してください。");
                if (key != null) previous = key.Yaw;
            }
        }

        private static bool IsTopologyHash(string value)
        {
            if (value == null || value.Length != 71 || !value.StartsWith("sha256:", StringComparison.Ordinal)) return false;
            for (var i = 7; i < value.Length; i++)
                if (!(value[i] >= '0' && value[i] <= '9') && !(value[i] >= 'a' && value[i] <= 'f')) return false;
            return true;
        }
    }
}
