using System.Collections.Generic;
using Newtonsoft.Json;

namespace VLat.Core.Model
{
    /// <summary>1体のアバターに対するVLat差分ドキュメントを表します。</summary>
    public sealed class VlatDocument
    {
        /// <summary>VLat差分ドキュメントを生成します。</summary>
        /// <param name="vlat">フォーマット情報と対象アバター情報。</param>
        /// <param name="buffers">外部バイナリバッファの定義。</param>
        /// <param name="bufferViews">バイナリバッファ内の範囲定義。</param>
        /// <param name="textures">テクスチャ定義。</param>
        /// <param name="angleBands">角度帯の定義。</param>
        /// <param name="layers">差分レイヤの定義。</param>
        /// <param name="keyformGroups">v0.2のキー集合。v0.1では省略します。</param>
        [JsonConstructor]
        public VlatDocument(
            VlatMetadata vlat,
            IEnumerable<VlatBuffer> buffers,
            IEnumerable<VlatBufferView> bufferViews,
            IEnumerable<VlatTexture> textures,
            IEnumerable<AngleBand> angleBands,
            IEnumerable<VlatLayer> layers,
            IEnumerable<KeyformGroup> keyformGroups = null)
        {
            Metadata = vlat;
            Buffers = ImmutableModelCollection.Copy(buffers);
            BufferViews = ImmutableModelCollection.Copy(bufferViews);
            Textures = ImmutableModelCollection.Copy(textures);
            AngleBands = ImmutableModelCollection.Copy(angleBands);
            Layers = ImmutableModelCollection.Copy(layers);
            KeyformGroups = ImmutableModelCollection.Copy(keyformGroups);
        }

        /// <summary>フォーマット情報と対象アバター情報を取得します。</summary>
        [JsonProperty("vlat", Order = 0, Required = Required.Always)]
        public VlatMetadata Metadata { get; }

        /// <summary>外部バイナリバッファの定義を取得します。</summary>
        [JsonProperty("buffers", Order = 1, Required = Required.DisallowNull)]
        public IReadOnlyList<VlatBuffer> Buffers { get; }

        /// <summary>バイナリバッファ内の範囲定義を取得します。</summary>
        [JsonProperty("bufferViews", Order = 2, Required = Required.DisallowNull)]
        public IReadOnlyList<VlatBufferView> BufferViews { get; }

        /// <summary>テクスチャ定義を取得します。</summary>
        [JsonProperty("textures", Order = 3, Required = Required.DisallowNull)]
        public IReadOnlyList<VlatTexture> Textures { get; }

        /// <summary>角度帯の定義を取得します。</summary>
        [JsonProperty("angleBands", Order = 4, Required = Required.Always)]
        public IReadOnlyList<AngleBand> AngleBands { get; }

        /// <summary>差分レイヤの定義を取得します。</summary>
        [JsonProperty("layers", Order = 5, Required = Required.Always)]
        public IReadOnlyList<VlatLayer> Layers { get; }

        /// <summary>v0.2のキー集合を取得します。</summary>
        [JsonProperty("keyformGroups", Order = 6, Required = Required.DisallowNull)]
        public IReadOnlyList<KeyformGroup> KeyformGroups { get; }

        /// <summary>旧v0.1出力に拡張フィールドを書き込まないための条件を返します。</summary>
        public bool ShouldSerializeKeyformGroups() => KeyformGroups.Count > 0;
    }

    /// <summary>VLatフォーマット情報と対象アバター情報を表します。</summary>
    public sealed class VlatMetadata
    {
        /// <summary>VLatフォーマット情報を生成します。</summary>
        /// <param name="version">SemVer形式のフォーマットバージョン。</param>
        /// <param name="generator">生成したツールの識別名。</param>
        /// <param name="targetAvatar">対象アバター情報。</param>
        [JsonConstructor]
        public VlatMetadata(string version, string generator, TargetAvatar targetAvatar)
        {
            Version = version;
            Generator = generator;
            TargetAvatar = targetAvatar;
        }

        /// <summary>SemVer形式のフォーマットバージョンを取得します。</summary>
        [JsonProperty("version", Order = 0, Required = Required.Always)]
        public string Version { get; }

        /// <summary>生成したツールの識別名を取得します。</summary>
        [JsonProperty("generator", Order = 1, Required = Required.Always)]
        public string Generator { get; }

        /// <summary>対象アバター情報を取得します。</summary>
        [JsonProperty("targetAvatar", Order = 2, Required = Required.Always)]
        public TargetAvatar TargetAvatar { get; }
    }

    /// <summary>外部バイナリバッファを表します。</summary>
    public sealed class VlatBuffer
    {
        /// <summary>外部バイナリバッファ定義を生成します。</summary>
        /// <param name="uri">JSONファイルを基準とした相対URI。</param>
        /// <param name="byteLength">バッファのバイト長。</param>
        [JsonConstructor]
        public VlatBuffer(string uri, long byteLength)
        {
            Uri = uri;
            ByteLength = byteLength;
        }

        /// <summary>JSONファイルを基準とした相対URIを取得します。</summary>
        [JsonProperty("uri", Order = 0, Required = Required.Always)]
        public string Uri { get; }

        /// <summary>バッファのバイト長を取得します。</summary>
        [JsonProperty("byteLength", Order = 1, Required = Required.Always)]
        public long ByteLength { get; }
    }

    /// <summary>バイナリバッファ内の連続した範囲を表します。</summary>
    public sealed class VlatBufferView
    {
        /// <summary>バッファ範囲を生成します。</summary>
        /// <param name="buffer">参照するbuffers配列のインデックス。</param>
        /// <param name="byteOffset">バッファ先頭からのバイトオフセット。</param>
        /// <param name="byteLength">範囲のバイト長。</param>
        [JsonConstructor]
        public VlatBufferView(int buffer, long byteOffset, long byteLength)
        {
            Buffer = buffer;
            ByteOffset = byteOffset;
            ByteLength = byteLength;
        }

        /// <summary>参照するbuffers配列のインデックスを取得します。</summary>
        [JsonProperty("buffer", Order = 0, Required = Required.Always)]
        public int Buffer { get; }

        /// <summary>バッファ先頭からのバイトオフセットを取得します。</summary>
        [JsonProperty("byteOffset", Order = 1, Required = Required.Always)]
        public long ByteOffset { get; }

        /// <summary>範囲のバイト長を取得します。</summary>
        [JsonProperty("byteLength", Order = 2, Required = Required.Always)]
        public long ByteLength { get; }
    }

    /// <summary>外部URIまたはbufferViewを参照するテクスチャを表します。</summary>
    public sealed class VlatTexture
    {
        /// <summary>テクスチャ定義を生成します。</summary>
        /// <param name="name">表示用の名前。</param>
        /// <param name="uri">外部テクスチャの相対URI。</param>
        /// <param name="bufferView">埋め込み画像を格納するbufferView。</param>
        /// <param name="mimeType">埋め込み画像のMIMEタイプ。</param>
        [JsonConstructor]
        public VlatTexture(string name, string uri, int? bufferView, string mimeType)
        {
            Name = name;
            Uri = uri;
            BufferView = bufferView;
            MimeType = mimeType;
        }

        /// <summary>表示用の名前を取得します。</summary>
        [JsonProperty("name", Order = 0, NullValueHandling = NullValueHandling.Ignore, Required = Required.DisallowNull)]
        public string Name { get; }

        /// <summary>外部テクスチャの相対URIを取得します。</summary>
        [JsonProperty("uri", Order = 1, NullValueHandling = NullValueHandling.Ignore, Required = Required.DisallowNull)]
        public string Uri { get; }

        /// <summary>埋め込み画像を格納するbufferViewを取得します。</summary>
        [JsonProperty("bufferView", Order = 2, NullValueHandling = NullValueHandling.Ignore, Required = Required.DisallowNull)]
        public int? BufferView { get; }

        /// <summary>埋め込み画像のMIMEタイプを取得します。</summary>
        [JsonProperty("mimeType", Order = 3, NullValueHandling = NullValueHandling.Ignore, Required = Required.DisallowNull)]
        public string MimeType { get; }
    }
}
