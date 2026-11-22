using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using UnityEngine;
using VLat.Core.Model;

namespace VLat.Runtime
{
    /// <summary>動的ブレンドシェイプの登録失敗理由を表します。</summary>
    public enum DynamicBlendShapeBindError
    {
        /// <summary>失敗していません。</summary>
        None = 0,

        /// <summary>破棄済みのバインダーが使用されました。</summary>
        Disposed,

        /// <summary>必須の引数が指定されていません。</summary>
        InvalidArgument,

        /// <summary>Rendererの共有Meshが外部から差し替えられています。</summary>
        RendererMeshChanged,

        /// <summary>対象Meshの頂点数がMeshBindingと一致しません。</summary>
        VertexCountMismatch,

        /// <summary>accessorのcomponentTypeが仕様と一致しません。</summary>
        UnsupportedComponentType,

        /// <summary>bufferまたはbufferViewの参照や範囲が不正です。</summary>
        InvalidBufferView,

        /// <summary>accessorのバイト長が要素型と一致しません。</summary>
        InvalidAccessorLength,

        /// <summary>indices、positions、normalsの要素数が一致しません。</summary>
        AccessorCountMismatch,

        /// <summary>頂点indexが対象Meshの範囲外です。</summary>
        VertexIndexOutOfRange,

        /// <summary>同じ頂点indexが複数回指定されています。</summary>
        DuplicateVertexIndex,

        /// <summary>位置または法線差分に非有限値が含まれています。</summary>
        NonFiniteDelta,

        /// <summary>同名のブレンドシェイプが既に存在します。</summary>
        DuplicateBlendShapeName,

        /// <summary>Unity Mesh APIによる複製または登録に失敗しました。</summary>
        MeshOperationFailed
    }

    /// <summary>動的ブレンドシェイプの登録結果を表します。</summary>
    public sealed class DynamicBlendShapeBindResult
    {
        internal DynamicBlendShapeBindResult(
            bool success,
            DynamicBlendShapeBindError error,
            VlatRuntimeDiagnostic diagnostic,
            SkinnedMeshRenderer renderer,
            int blendShapeIndex,
            string blendShapeName)
        {
            Success = success;
            Error = error;
            Diagnostic = diagnostic;
            Renderer = renderer;
            BlendShapeIndex = blendShapeIndex;
            BlendShapeName = blendShapeName;
        }

        /// <summary>登録に成功したかどうかを取得します。</summary>
        public bool Success { get; }

        /// <summary>失敗理由を取得します。成功時は<see cref="DynamicBlendShapeBindError.None"/>です。</summary>
        public DynamicBlendShapeBindError Error { get; }

        /// <summary>失敗時のランタイム診断を取得します。成功時はnullです。</summary>
        public VlatRuntimeDiagnostic Diagnostic { get; }

        /// <summary>ブレンドシェイプを適用するRendererを取得します。失敗時はnullです。</summary>
        public SkinnedMeshRenderer Renderer { get; }

        /// <summary>登録したブレンドシェイプのインデックスを取得します。失敗時は-1です。</summary>
        public int BlendShapeIndex { get; }

        /// <summary>登録したブレンドシェイプ名を取得します。名前を構築できなかった失敗時はnullです。</summary>
        public string BlendShapeName { get; }
    }

    /// <summary>
    /// vertexDeltaを複製Mesh上の動的ブレンドシェイプとして登録し、元Meshへの復帰を管理します。
    /// </summary>
    /// <remarks>すべてのメソッドはUnityのメインスレッドから呼び出してください。</remarks>
    public sealed class DynamicBlendShapeBinder : IDisposable
    {
        private const string BlendShapePrefix = "VLAT_";

        private readonly Dictionary<SkinnedMeshRenderer, MeshBindingState> _states =
            new Dictionary<SkinnedMeshRenderer, MeshBindingState>();

        private bool _disposed;

        /// <summary>
        /// vertexDeltaを対象Rendererへ登録します。入力不正時は例外を送出せず失敗結果を返します。
        /// </summary>
        /// <param name="renderer">対象のSkinnedMeshRenderer。</param>
        /// <param name="document">bufferViewとMeshBindingを保持するVLatドキュメント。</param>
        /// <param name="binaryBuffers">document.buffersと同じ順序のバイナリ内容。</param>
        /// <param name="layerId">ブレンドシェイプ名に使用するレイヤID。</param>
        /// <param name="modifier">登録するvertexDelta Modifier。</param>
        /// <returns>成功時はRendererとブレンドシェイプインデックスを含む登録結果。</returns>
        public DynamicBlendShapeBindResult Bind(
            SkinnedMeshRenderer renderer,
            VlatDocument document,
            IReadOnlyList<byte[]> binaryBuffers,
            string layerId,
            VertexDeltaModifier modifier)
        {
            return BindCore(renderer, document, binaryBuffers, layerId, null, modifier);
        }

        /// <summary>
        /// vertexDeltaをModifier番号で一意化した名前で対象Rendererへ登録します。
        /// 入力不正時は例外を送出せず失敗結果を返します。
        /// </summary>
        /// <param name="renderer">対象のSkinnedMeshRenderer。</param>
        /// <param name="document">bufferViewとMeshBindingを保持するVLatドキュメント。</param>
        /// <param name="binaryBuffers">document.buffersと同じ順序のバイナリ内容。</param>
        /// <param name="layerId">ブレンドシェイプ名に使用するレイヤID。</param>
        /// <param name="modifierIndex">Layer内でのModifier番号。</param>
        /// <param name="modifier">登録するvertexDelta Modifier。</param>
        /// <returns>成功時はRendererとブレンドシェイプインデックスを含む登録結果。</returns>
        public DynamicBlendShapeBindResult Bind(
            SkinnedMeshRenderer renderer,
            VlatDocument document,
            IReadOnlyList<byte[]> binaryBuffers,
            string layerId,
            int modifierIndex,
            VertexDeltaModifier modifier)
        {
            return BindCore(renderer, document, binaryBuffers, layerId, modifierIndex, modifier);
        }

        private DynamicBlendShapeBindResult BindCore(
            SkinnedMeshRenderer renderer,
            VlatDocument document,
            IReadOnlyList<byte[]> binaryBuffers,
            string layerId,
            int? modifierIndex,
            VertexDeltaModifier modifier)
        {
            if (_disposed)
            {
                return Failure(
                    DynamicBlendShapeBindError.Disposed,
                    "DynamicBlendShapeBinderは既に破棄されています。",
                    null);
            }

            if (renderer == null || document == null || binaryBuffers == null || modifier == null ||
                string.IsNullOrEmpty(layerId) || (modifierIndex.HasValue && modifierIndex.Value < 0))
            {
                return Failure(
                    DynamicBlendShapeBindError.InvalidArgument,
                    "renderer、document、binaryBuffers、layerId、modifierは必須で、modifierIndexは0以上である必要があります。",
                    null);
            }

            var blendShapeName = modifierIndex.HasValue
                ? BlendShapePrefix + layerId + "__modifier_" +
                    modifierIndex.Value.ToString(CultureInfo.InvariantCulture)
                : BlendShapePrefix + layerId;
            var currentMesh = renderer.sharedMesh;
            if (currentMesh == null)
            {
                return Failure(
                    DynamicBlendShapeBindError.InvalidArgument,
                    "対象RendererにsharedMeshが設定されていません。",
                    blendShapeName);
            }

            MeshBindingState state;
            if (_states.TryGetValue(renderer, out state))
            {
                if (currentMesh != state.GeneratedMesh)
                {
                    return Failure(
                        DynamicBlendShapeBindError.RendererMeshChanged,
                        "登録後にRenderer.sharedMeshが外部から差し替えられています。",
                        blendShapeName);
                }
            }

            var sourceMesh = state == null ? currentMesh : state.GeneratedMesh;
            if (sourceMesh.GetBlendShapeIndex(blendShapeName) >= 0)
            {
                return Failure(
                    DynamicBlendShapeBindError.DuplicateBlendShapeName,
                    "同名のブレンドシェイプが既に存在します: " + blendShapeName,
                    blendShapeName);
            }

            MeshBinding meshBinding;
            DynamicBlendShapeBindResult bindingFailure;
            if (!TryGetMeshBinding(document, modifier, blendShapeName, out meshBinding, out bindingFailure))
            {
                return bindingFailure;
            }

            if (meshBinding.VertexCount != sourceMesh.vertexCount)
            {
                return Failure(
                    DynamicBlendShapeBindError.VertexCountMismatch,
                    "MeshBinding.vertexCountと対象Meshの頂点数が一致しません。",
                    blendShapeName);
            }

            DenseBlendShapeData denseData;
            if (!TryReadDenseData(
                    document,
                    binaryBuffers,
                    modifier,
                    sourceMesh.vertexCount,
                    blendShapeName,
                    out denseData,
                    out bindingFailure))
            {
                return bindingFailure;
            }

            Mesh generatedMesh = null;
            var createdMesh = false;
            int blendShapeIndex;
            try
            {
                if (state == null)
                {
                    generatedMesh = UnityEngine.Object.Instantiate(sourceMesh);
                    generatedMesh.name = sourceMesh.name + " (Kakudoll Runtime)";
                    createdMesh = true;
                }
                else
                {
                    generatedMesh = state.GeneratedMesh;
                }

                generatedMesh.AddBlendShapeFrame(
                    blendShapeName,
                    100f,
                    denseData.Positions,
                    denseData.Normals,
                    denseData.Tangents);

                blendShapeIndex = generatedMesh.GetBlendShapeIndex(blendShapeName);
                if (blendShapeIndex < 0)
                {
                    if (createdMesh)
                    {
                        DestroyGeneratedMesh(generatedMesh);
                    }

                    return Failure(
                        DynamicBlendShapeBindError.MeshOperationFailed,
                        "Meshへブレンドシェイプを登録できませんでした。",
                        blendShapeName);
                }
            }
            catch (Exception exception)
            {
                if (createdMesh)
                {
                    DestroyGeneratedMesh(generatedMesh);
                }

                return Failure(
                    DynamicBlendShapeBindError.MeshOperationFailed,
                    "Meshの複製またはブレンドシェイプ登録に失敗しました: " + exception.Message,
                    blendShapeName);
            }

            if (state == null)
            {
                try
                {
                    renderer.sharedMesh = generatedMesh;
                }
                catch (Exception exception)
                {
                    DestroyGeneratedMesh(generatedMesh);
                    return Failure(
                        DynamicBlendShapeBindError.MeshOperationFailed,
                        "Rendererへの複製Mesh設定に失敗しました: " + exception.Message,
                        blendShapeName);
                }

                state = new MeshBindingState(renderer, sourceMesh, generatedMesh);
                _states.Add(renderer, state);
            }

            state.BlendShapeIndices.Add(blendShapeIndex);

            return new DynamicBlendShapeBindResult(
                true,
                DynamicBlendShapeBindError.None,
                null,
                renderer,
                blendShapeIndex,
                blendShapeName);
        }

        /// <summary>登録済みのVLatブレンドシェイプweightをすべて0に戻します。複製Meshは維持されます。</summary>
        public void ClearWeights()
        {
            if (_disposed)
            {
                return;
            }

            foreach (var pair in _states)
            {
                var state = pair.Value;
                var renderer = state.Renderer;
                var mesh = state.GeneratedMesh;
                if (renderer == null || mesh == null || renderer.sharedMesh != mesh)
                {
                    continue;
                }

                for (var index = 0; index < state.BlendShapeIndices.Count; index++)
                {
                    renderer.SetBlendShapeWeight(state.BlendShapeIndices[index], 0f);
                }
            }
        }

        /// <summary>すべてのRendererを元Meshへ戻し、生成したMeshを破棄します。呼び出し後も再登録できます。</summary>
        public void Restore()
        {
            foreach (var pair in _states)
            {
                var state = pair.Value;
                if (state.Renderer != null && state.Renderer.sharedMesh == state.GeneratedMesh)
                {
                    state.Renderer.sharedMesh = state.OriginalMesh;
                }

                DestroyGeneratedMesh(state.GeneratedMesh);
            }

            _states.Clear();
        }

        /// <summary>元Meshへの復帰と生成Meshの破棄を行い、このバインダーを使用不能にします。</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Restore();
            _disposed = true;
        }

        private static bool TryGetMeshBinding(
            VlatDocument document,
            VertexDeltaModifier modifier,
            string blendShapeName,
            out MeshBinding meshBinding,
            out DynamicBlendShapeBindResult failure)
        {
            meshBinding = null;
            failure = null;
            var metadata = document.Metadata;
            var targetAvatar = metadata == null ? null : metadata.TargetAvatar;
            var meshBindings = targetAvatar == null ? null : targetAvatar.MeshBindings;
            if (meshBindings == null || modifier.MeshBinding < 0 || modifier.MeshBinding >= meshBindings.Count)
            {
                failure = Failure(
                    DynamicBlendShapeBindError.InvalidArgument,
                    "modifier.meshBindingの参照先が存在しません。",
                    blendShapeName);
                return false;
            }

            meshBinding = meshBindings[modifier.MeshBinding];
            if (meshBinding == null || meshBinding.VertexCount < 0)
            {
                failure = Failure(
                    DynamicBlendShapeBindError.InvalidArgument,
                    "参照先MeshBindingが不正です。",
                    blendShapeName);
                return false;
            }

            return true;
        }

        private static bool TryReadDenseData(
            VlatDocument document,
            IReadOnlyList<byte[]> binaryBuffers,
            VertexDeltaModifier modifier,
            int vertexCount,
            string blendShapeName,
            out DenseBlendShapeData denseData,
            out DynamicBlendShapeBindResult failure)
        {
            denseData = null;
            failure = null;

            BinaryAccessorSlice indices;
            BinaryAccessorSlice positions;
            if (!BinaryAccessorReader.TryRead(
                    document,
                    binaryBuffers,
                    modifier.Indices,
                    "UINT32",
                    4,
                    blendShapeName,
                    out indices,
                    out failure) ||
                !BinaryAccessorReader.TryRead(
                    document,
                    binaryBuffers,
                    modifier.Positions,
                    "FLOAT32",
                    12,
                    blendShapeName,
                    out positions,
                    out failure))
            {
                return false;
            }

            if (indices.Count != positions.Count)
            {
                failure = Failure(
                    DynamicBlendShapeBindError.AccessorCountMismatch,
                    "indicesとpositionsの要素数が一致しません。",
                    blendShapeName);
                return false;
            }

            BinaryAccessorSlice normals = default;
            if (modifier.Normals != null)
            {
                if (!BinaryAccessorReader.TryRead(
                        document,
                        binaryBuffers,
                        modifier.Normals,
                        "FLOAT32",
                        12,
                        blendShapeName,
                        out normals,
                        out failure))
                {
                    return false;
                }

                if (indices.Count != normals.Count)
                {
                    failure = Failure(
                        DynamicBlendShapeBindError.AccessorCountMismatch,
                        "indicesとnormalsの要素数が一致しません。",
                        blendShapeName);
                    return false;
                }
            }

            var deltaPositions = new Vector3[vertexCount];
            var deltaNormals = new Vector3[vertexCount];
            var deltaTangents = new Vector3[vertexCount];
            var assigned = new bool[vertexCount];

            for (var itemIndex = 0; itemIndex < indices.Count; itemIndex++)
            {
                var vertexIndexValue = BinaryAccessorReader.ReadUInt32(indices, itemIndex * 4);
                if (vertexIndexValue >= (uint)vertexCount)
                {
                    failure = Failure(
                        DynamicBlendShapeBindError.VertexIndexOutOfRange,
                        "頂点indexが対象Meshの範囲外です（要素" + itemIndex + "）。",
                        blendShapeName);
                    return false;
                }

                var vertexIndex = (int)vertexIndexValue;
                if (assigned[vertexIndex])
                {
                    failure = Failure(
                        DynamicBlendShapeBindError.DuplicateVertexIndex,
                        "同じ頂点indexが複数回指定されています（index " + vertexIndex + "）。",
                        blendShapeName);
                    return false;
                }

                assigned[vertexIndex] = true;
                Vector3 value;
                if (!BinaryAccessorReader.TryReadFiniteVector3(positions, itemIndex * 12, out value))
                {
                    failure = Failure(
                        DynamicBlendShapeBindError.NonFiniteDelta,
                        "positionsにNaNまたはInfinityが含まれています（要素" + itemIndex + "）。",
                        blendShapeName);
                    return false;
                }

                deltaPositions[vertexIndex] = value;
                if (modifier.Normals != null)
                {
                    if (!BinaryAccessorReader.TryReadFiniteVector3(normals, itemIndex * 12, out value))
                    {
                        failure = Failure(
                            DynamicBlendShapeBindError.NonFiniteDelta,
                            "normalsにNaNまたはInfinityが含まれています（要素" + itemIndex + "）。",
                            blendShapeName);
                        return false;
                    }

                    deltaNormals[vertexIndex] = value;
                }
            }

            denseData = new DenseBlendShapeData(deltaPositions, deltaNormals, deltaTangents);
            return true;
        }

        private static DynamicBlendShapeBindResult Failure(
            DynamicBlendShapeBindError error,
            string diagnostic,
            string blendShapeName)
        {
            return new DynamicBlendShapeBindResult(
                false,
                error,
                new VlatRuntimeDiagnostic(VlatRuntimeDiagnosticSeverity.Error, "$", diagnostic),
                null,
                -1,
                blendShapeName);
        }

        private static void DestroyGeneratedMesh(Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(mesh);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private sealed class MeshBindingState
        {
            public MeshBindingState(
                SkinnedMeshRenderer renderer,
                Mesh originalMesh,
                Mesh generatedMesh)
            {
                Renderer = renderer;
                OriginalMesh = originalMesh;
                GeneratedMesh = generatedMesh;
                BlendShapeIndices = new List<int>();
            }

            public SkinnedMeshRenderer Renderer { get; }

            public Mesh OriginalMesh { get; }

            public Mesh GeneratedMesh { get; }

            public List<int> BlendShapeIndices { get; }
        }

        private sealed class DenseBlendShapeData
        {
            public DenseBlendShapeData(Vector3[] positions, Vector3[] normals, Vector3[] tangents)
            {
                Positions = positions;
                Normals = normals;
                Tangents = tangents;
            }

            public Vector3[] Positions { get; }

            public Vector3[] Normals { get; }

            public Vector3[] Tangents { get; }
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct UInt32SingleUnion
        {
            [FieldOffset(0)]
            public uint UInt32;

            [FieldOffset(0)]
            public float Single;
        }

        private readonly struct BinaryAccessorSlice
        {
            public BinaryAccessorSlice(byte[] data, int offset, int byteLength, int elementSize)
            {
                Data = data;
                Offset = offset;
                ByteLength = byteLength;
                ElementSize = elementSize;
            }

            public byte[] Data { get; }

            public int Offset { get; }

            public int ByteLength { get; }

            public int ElementSize { get; }

            public int Count => ByteLength / ElementSize;
        }

        private static class BinaryAccessorReader
        {
            public static bool TryRead(
                VlatDocument document,
                IReadOnlyList<byte[]> binaryBuffers,
                VlatAccessor accessor,
                string expectedComponentType,
                int elementSize,
                string blendShapeName,
                out BinaryAccessorSlice slice,
                out DynamicBlendShapeBindResult failure)
            {
                slice = default;
                failure = null;
                if (accessor == null)
                {
                    failure = Failure(
                        DynamicBlendShapeBindError.InvalidArgument,
                        "vertexDeltaのaccessorが指定されていません。",
                        blendShapeName);
                    return false;
                }

                if (!string.Equals(accessor.ComponentType, expectedComponentType, StringComparison.Ordinal))
                {
                    failure = Failure(
                        DynamicBlendShapeBindError.UnsupportedComponentType,
                        "accessor.componentTypeは" + expectedComponentType + "である必要があります。",
                        blendShapeName);
                    return false;
                }

                var bufferViews = document.BufferViews;
                var bufferDefinitions = document.Buffers;
                if (bufferViews == null || bufferDefinitions == null ||
                    accessor.BufferView < 0 || accessor.BufferView >= bufferViews.Count ||
                    binaryBuffers.Count != bufferDefinitions.Count)
                {
                    failure = Failure(
                        DynamicBlendShapeBindError.InvalidBufferView,
                        "accessorのbufferView参照またはbinaryBuffersの個数が不正です。",
                        blendShapeName);
                    return false;
                }

                var bufferView = bufferViews[accessor.BufferView];
                if (bufferView == null || bufferView.Buffer < 0 ||
                    bufferView.Buffer >= bufferDefinitions.Count || bufferView.Buffer >= binaryBuffers.Count)
                {
                    failure = Failure(
                        DynamicBlendShapeBindError.InvalidBufferView,
                        "bufferViewのbuffer参照が不正です。",
                        blendShapeName);
                    return false;
                }

                var bufferDefinition = bufferDefinitions[bufferView.Buffer];
                var buffer = binaryBuffers[bufferView.Buffer];
                if (bufferDefinition == null || buffer == null ||
                    bufferDefinition.ByteLength != buffer.LongLength ||
                    bufferView.ByteOffset < 0 || bufferView.ByteLength <= 0 ||
                    bufferView.ByteOffset > buffer.LongLength ||
                    bufferView.ByteLength > buffer.LongLength - bufferView.ByteOffset ||
                    bufferView.ByteOffset > int.MaxValue || bufferView.ByteLength > int.MaxValue)
                {
                    failure = Failure(
                        DynamicBlendShapeBindError.InvalidBufferView,
                        "bufferViewが実バッファの範囲外、または宣言byteLengthと実データ長が不一致です。",
                        blendShapeName);
                    return false;
                }

                if (bufferView.ByteLength % elementSize != 0)
                {
                    failure = Failure(
                        DynamicBlendShapeBindError.InvalidAccessorLength,
                        "bufferView.byteLengthが要素サイズ" + elementSize + "の倍数ではありません。",
                        blendShapeName);
                    return false;
                }

                slice = new BinaryAccessorSlice(
                    buffer,
                    (int)bufferView.ByteOffset,
                    (int)bufferView.ByteLength,
                    elementSize);
                return true;
            }

            public static uint ReadUInt32(BinaryAccessorSlice slice, int relativeOffset)
            {
                var data = slice.Data;
                var offset = slice.Offset + relativeOffset;
                return (uint)(data[offset] |
                              (data[offset + 1] << 8) |
                              (data[offset + 2] << 16) |
                              (data[offset + 3] << 24));
            }

            public static bool TryReadFiniteVector3(
                BinaryAccessorSlice slice,
                int relativeOffset,
                out Vector3 value)
            {
                var x = ReadSingle(slice, relativeOffset);
                var y = ReadSingle(slice, relativeOffset + 4);
                var z = ReadSingle(slice, relativeOffset + 8);
                if (float.IsNaN(x) || float.IsInfinity(x) ||
                    float.IsNaN(y) || float.IsInfinity(y) ||
                    float.IsNaN(z) || float.IsInfinity(z))
                {
                    value = default;
                    return false;
                }

                value = new Vector3(x, y, z);
                return true;
            }

            private static float ReadSingle(BinaryAccessorSlice slice, int relativeOffset)
            {
                var union = new UInt32SingleUnion
                {
                    UInt32 = ReadUInt32(slice, relativeOffset)
                };
                return union.Single;
            }
        }
    }
}
