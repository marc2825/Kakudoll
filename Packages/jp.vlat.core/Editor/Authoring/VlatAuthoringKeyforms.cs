using System;
using System.Collections.Generic;
using UnityEngine;

namespace VLat.Editor.Authoring
{
    // 既存Documentの所有下に置くtyped data。Undo/RecoveryはDocumentの直列化を共用する。
    // 位置差分はUnity直列化上だけpack済み文字列で保持し(D-011)、要素ごとのobject直列化を避ける。
    [Serializable]
    internal sealed class VlatAuthoringKeyform : ISerializationCallbackReceiver
    {
        [SerializeField, HideInInspector] private string _editorIdentity = VlatAuthoringIdentity.NewIdentity();
        [SerializeField] private double _yaw;
        // Recovery manifest v1/v2の旧snapshotを読むための旧表現。直列化時は常に空にする。
        [SerializeField, HideInInspector] private List<VlatAuthoringVertexDelta> _deltas = new List<VlatAuthoringVertexDelta>();
        // v3の表現。VlatAuthoringDeltaPackingのbase64。空文字列は差分なし。
        [SerializeField, HideInInspector] private string _packedDeltas = string.Empty;
        [NonSerialized] private List<VlatAuthoringVertexDelta> _runtimeDeltas;
        [NonSerialized] private string _serializationError;

        internal string EditorIdentity => VlatAuthoringIdentity.Ensure(ref _editorIdentity);
        internal double Yaw { get => _yaw; set => _yaw = value; }
        // 空列は明示的中立キー。法線はv0.2では使用しない。
        internal List<VlatAuthoringVertexDelta> Deltas => _runtimeDeltas ??= new List<VlatAuthoringVertexDelta>();
        // pack済み差分を復元できなかった場合の理由。正常時はnull。保存・Recovery復元はこれを拒否する。
        internal string SerializationError => _serializationError;

        // 直列化の出力として作ったpack文字列を、読み取りが終わった境界で解放する。
        // 大規模ではUTF-16文字列が差分の約2.7倍のmemoryを常駐させるため。次の直列化で必ず再生成される。
        // 復元できなかったpack済みデータは、利用者が差分を入れ直すまで解放しない。
        internal void ReleasePackedCache()
        {
            if (_serializationError == null) _packedDeltas = string.Empty;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            // 旧表現だけを持つ状態(callback未実行など)では、先に実行時の差分列へ移して内容を失わない。
            if (_runtimeDeltas == null)
            {
                _runtimeDeltas = _deltas ?? new List<VlatAuthoringVertexDelta>();
                _deltas = new List<VlatAuthoringVertexDelta>();
            }
            // 復元できなかったデータは、差分が入れ直されるまで元のpack済み文字列のまま保持し、
            // 空の中立キーとして上書きしない（Recovery/Undo往復でエラー状態を失わないため）。
            if (_serializationError == null || _runtimeDeltas.Count != 0)
            {
                _packedDeltas = VlatAuthoringDeltaPacking.Encode(_runtimeDeltas);
                _serializationError = null;
            }
            if (_deltas == null) _deltas = new List<VlatAuthoringVertexDelta>();
            else _deltas.Clear();
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            _serializationError = null;
            var legacy = _deltas != null && _deltas.Count != 0;
            if (string.IsNullOrEmpty(_packedDeltas))
            {
                _runtimeDeltas = legacy ? new List<VlatAuthoringVertexDelta>(_deltas) : new List<VlatAuthoringVertexDelta>();
            }
            else if (legacy)
            {
                _runtimeDeltas = new List<VlatAuthoringVertexDelta>();
                _serializationError = "旧表現とpack済み表現の差分が同時に存在します。";
            }
            else if (VlatAuthoringDeltaPacking.TryDecode(_packedDeltas, out var decoded, out var error))
            {
                _runtimeDeltas = decoded;
                _packedDeltas = string.Empty;
            }
            else
            {
                _runtimeDeltas = new List<VlatAuthoringVertexDelta>();
                _serializationError = error;
            }

            _deltas = new List<VlatAuthoringVertexDelta>();
        }
    }

    [Serializable]
    internal sealed class VlatAuthoringKeyformGroup
    {
        [SerializeField, HideInInspector] private string _editorIdentity = VlatAuthoringIdentity.NewIdentity();
        [SerializeField] private string _id = "shape-keys";
        [SerializeField] private int _meshBinding = -1;
        [SerializeField] private string _topologyHash = string.Empty;
        [SerializeField] private string _coordinateSpace = "bind-mesh-local";
        [SerializeField] private string _interpolation = "yaw-smoothstep";
        [SerializeField] private double _supportStart = -120;
        [SerializeField] private double _supportEnd = 120;
        [SerializeField] private List<VlatAuthoringKeyform> _keys = new List<VlatAuthoringKeyform>();

        internal string EditorIdentity => VlatAuthoringIdentity.Ensure(ref _editorIdentity);
        internal string Id { get => _id; set => _id = value; }
        internal int MeshBinding { get => _meshBinding; set => _meshBinding = value; }
        internal string TopologyHash { get => _topologyHash; set => _topologyHash = value; }
        internal string CoordinateSpace { get => _coordinateSpace; set => _coordinateSpace = value; }
        internal string Interpolation { get => _interpolation; set => _interpolation = value; }
        internal double SupportStart { get => _supportStart; set => _supportStart = value; }
        internal double SupportEnd { get => _supportEnd; set => _supportEnd = value; }
        internal List<VlatAuthoringKeyform> Keys => _keys;
    }
}
