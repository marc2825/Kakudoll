using System;
using System.Collections.Generic;
using UnityEngine;

namespace VLat.Editor.Authoring
{
    [Serializable]
    internal sealed class VlatAuthoringMaterialSlot
    {
        [SerializeField] private int _slot;
        [SerializeField] private string _materialName = string.Empty;
        [SerializeField] private string _shader = string.Empty;

        internal int Slot { get => _slot; set => _slot = value; }
        internal string MaterialName { get => _materialName; set => _materialName = value; }
        internal string Shader { get => _shader; set => _shader = value; }
    }

    [Serializable]
    internal sealed class VlatAuthoringMeshBinding
    {
        [SerializeField] private string _hierarchyPath = string.Empty;
        [SerializeField] private string _meshName = string.Empty;
        [SerializeField] private int _vertexCount;
        [SerializeField] private string _positionHash = string.Empty;
        [SerializeField] private List<VlatAuthoringMaterialSlot> _materials = new List<VlatAuthoringMaterialSlot>();

        internal string HierarchyPath { get => _hierarchyPath; set => _hierarchyPath = value; }
        internal string MeshName { get => _meshName; set => _meshName = value; }
        internal int VertexCount { get => _vertexCount; set => _vertexCount = value; }
        internal string PositionHash { get => _positionHash; set => _positionHash = value; }
        internal List<VlatAuthoringMaterialSlot> Materials => _materials;
    }

    [Serializable]
    internal sealed class VlatAuthoringMetadata
    {
        [SerializeField] private string _version = "0.1.0";
        [SerializeField] private string _generator = "Kakudoll";
        [SerializeField] private string _avatarName = string.Empty;
        [SerializeField] private string _vrmVersion = "1.0";
        [SerializeField] private List<VlatAuthoringMeshBinding> _meshBindings = new List<VlatAuthoringMeshBinding>();

        internal string Version { get => _version; set => _version = value; }
        internal string Generator { get => _generator; set => _generator = value; }
        internal string AvatarName { get => _avatarName; set => _avatarName = value; }
        internal string VrmVersion { get => _vrmVersion; set => _vrmVersion = value; }
        internal List<VlatAuthoringMeshBinding> MeshBindings => _meshBindings;
    }

    [Serializable]
    internal sealed class VlatAuthoringTexture
    {
        [SerializeField] private string _name;
        [SerializeField] private bool _isEmbedded;
        [SerializeField] private string _uri;
        [SerializeField] private string _mimeType;
        [SerializeField] private byte[] _embeddedBytes;

        internal string Name { get => _name; set => _name = value; }
        internal bool IsEmbedded { get => _isEmbedded; set => _isEmbedded = value; }
        internal string Uri { get => _uri; set => _uri = value; }
        internal string MimeType { get => _mimeType; set => _mimeType = value; }
        internal byte[] EmbeddedBytes { get => _embeddedBytes; set => _embeddedBytes = value; }
    }

    [Serializable]
    internal sealed class VlatAuthoringBand
    {
        [SerializeField, HideInInspector] private string _editorIdentity = VlatAuthoringIdentity.NewIdentity();
        [SerializeField] private string _id = string.Empty;
        [SerializeField] private string _name = string.Empty;
        [SerializeField] private double _yawMin;
        [SerializeField] private double _yawMax;
        [SerializeField] private double _pitchMin;
        [SerializeField] private double _pitchMax;
        [SerializeField] private double _featherYaw;
        [SerializeField] private double _featherPitch;
        [SerializeField] private double _hysteresis;

        internal string EditorIdentity => VlatAuthoringIdentity.Ensure(ref _editorIdentity);
        internal string Id { get => _id; set => _id = value; }
        internal string Name { get => _name; set => _name = value; }
        internal double YawMin { get => _yawMin; set => _yawMin = value; }
        internal double YawMax { get => _yawMax; set => _yawMax = value; }
        internal double PitchMin { get => _pitchMin; set => _pitchMin = value; }
        internal double PitchMax { get => _pitchMax; set => _pitchMax = value; }
        internal double FeatherYaw { get => _featherYaw; set => _featherYaw = value; }
        internal double FeatherPitch { get => _featherPitch; set => _featherPitch = value; }
        internal double Hysteresis { get => _hysteresis; set => _hysteresis = value; }
    }

    [Serializable]
    internal sealed class VlatAuthoringVertexDelta
    {
        [SerializeField] private int _vertexIndex;
        [SerializeField] private Vector3 _positionDelta;
        [SerializeField] private Vector3 _normalDelta;

        internal int VertexIndex { get => _vertexIndex; set => _vertexIndex = value; }
        internal Vector3 PositionDelta { get => _positionDelta; set => _positionDelta = value; }
        internal Vector3 NormalDelta { get => _normalDelta; set => _normalDelta = value; }

        internal VlatAuthoringVertexDelta Copy()
        {
            return new VlatAuthoringVertexDelta
            {
                VertexIndex = VertexIndex,
                PositionDelta = PositionDelta,
                NormalDelta = NormalDelta,
            };
        }
    }

    [Serializable]
    internal abstract class VlatAuthoringModifier
    {
        [SerializeField, HideInInspector] private string _editorIdentity = VlatAuthoringIdentity.NewIdentity();

        internal string EditorIdentity => VlatAuthoringIdentity.Ensure(ref _editorIdentity);
    }

    [Serializable]
    internal sealed class VlatAuthoringVertexDeltaModifier : VlatAuthoringModifier
    {
        [SerializeField] private int _meshBinding;
        [SerializeField] private bool _hasNormals;
        [SerializeField] private List<VlatAuthoringVertexDelta> _deltas = new List<VlatAuthoringVertexDelta>();

        internal int MeshBinding { get => _meshBinding; set => _meshBinding = value; }
        internal bool HasNormals { get => _hasNormals; set => _hasNormals = value; }
        internal List<VlatAuthoringVertexDelta> Deltas => _deltas;
    }

    [Serializable]
    internal sealed class VlatAuthoringVisibilityModifier : VlatAuthoringModifier
    {
        [SerializeField] private int _meshBinding;
        [SerializeField] private int _subMesh;
        [SerializeField] private bool _visibleInBand;

        internal int MeshBinding { get => _meshBinding; set => _meshBinding = value; }
        internal int SubMesh { get => _subMesh; set => _subMesh = value; }
        internal bool VisibleInBand { get => _visibleInBand; set => _visibleInBand = value; }
    }

    [Serializable]
    internal sealed class VlatAuthoringOpacityModifier : VlatAuthoringModifier
    {
        [SerializeField] private int _meshBinding;
        [SerializeField] private int _materialSlot;
        [SerializeField] private double _value;

        internal int MeshBinding { get => _meshBinding; set => _meshBinding = value; }
        internal int MaterialSlot { get => _materialSlot; set => _materialSlot = value; }
        internal double Value { get => _value; set => _value = value; }
    }

    [Serializable]
    internal sealed class VlatAuthoringMaterialPropertyModifier : VlatAuthoringModifier
    {
        [SerializeField] private int _meshBinding;
        [SerializeField] private int _materialSlot;
        [SerializeField] private string _property = string.Empty;
        [SerializeField] private string _valueType = string.Empty;
        [SerializeField] private bool _isScalar = true;
        [SerializeField] private double _scalar;
        [SerializeField] private List<double> _components = new List<double>();
        [SerializeField] private bool _interpolate;

        internal int MeshBinding { get => _meshBinding; set => _meshBinding = value; }
        internal int MaterialSlot { get => _materialSlot; set => _materialSlot = value; }
        internal string Property { get => _property; set => _property = value; }
        internal string ValueType { get => _valueType; set => _valueType = value; }
        internal bool IsScalar { get => _isScalar; set => _isScalar = value; }
        internal double Scalar { get => _scalar; set => _scalar = value; }
        internal List<double> Components => _components;
        internal bool Interpolate { get => _interpolate; set => _interpolate = value; }
    }

    [Serializable]
    internal sealed class VlatAuthoringRenderQueueModifier : VlatAuthoringModifier
    {
        [SerializeField] private int _meshBinding;
        [SerializeField] private int _materialSlot;
        [SerializeField] private int _offset;

        internal int MeshBinding { get => _meshBinding; set => _meshBinding = value; }
        internal int MaterialSlot { get => _materialSlot; set => _materialSlot = value; }
        internal int Offset { get => _offset; set => _offset = value; }
    }

    [Serializable]
    internal sealed class VlatAuthoringLayer
    {
        [SerializeField, HideInInspector] private string _editorIdentity = VlatAuthoringIdentity.NewIdentity();
        [SerializeField] private string _id = string.Empty;
        [SerializeField] private string _name = string.Empty;
        [SerializeField] private string _tag = string.Empty;
        [SerializeField] private bool _enabled = true;
        [SerializeField] private int _priority;
        [SerializeField] private string _bandId = string.Empty;
        [SerializeReference] private List<VlatAuthoringModifier> _modifiers = new List<VlatAuthoringModifier>();

        internal string EditorIdentity => VlatAuthoringIdentity.Ensure(ref _editorIdentity);
        internal string Id { get => _id; set => _id = value; }
        internal string Name { get => _name; set => _name = value; }
        internal string Tag { get => _tag; set => _tag = value; }
        internal bool Enabled { get => _enabled; set => _enabled = value; }
        internal int Priority { get => _priority; set => _priority = value; }
        internal string BandId { get => _bandId; set => _bandId = value; }
        internal List<VlatAuthoringModifier> Modifiers => _modifiers;
    }

    internal sealed class VlatAuthoringDocument : ScriptableObject
    {
        [SerializeField] private VlatAuthoringMetadata _metadata = new VlatAuthoringMetadata();
        [SerializeField] private List<VlatAuthoringTexture> _textures = new List<VlatAuthoringTexture>();
        [SerializeField] private List<VlatAuthoringBand> _bands = new List<VlatAuthoringBand>();
        [SerializeField] private List<VlatAuthoringLayer> _layers = new List<VlatAuthoringLayer>();
        [SerializeField] private List<VlatAuthoringKeyformGroup> _keyformGroups = new List<VlatAuthoringKeyformGroup>();
        [SerializeField] private string _sourcePath;
        [SerializeField] private bool _isDirty;
        [SerializeField, HideInInspector] private long _revision;

        // 編集境界のpack結果。Revisionは内容変更command(Finish)とUndo/Redoの復元で必ず変わるため、
        // 同じDocument instanceかつ同じRevisionなら内容は同一。直列化・Undo対象外。
        [NonSerialized] private VlatAuthoringPackResult _editingPack;
        [NonSerialized] private long _editingPackRevision;

        internal VlatAuthoringMetadata Metadata => _metadata;
        internal List<VlatAuthoringTexture> Textures => _textures;
        internal List<VlatAuthoringBand> Bands => _bands;
        internal List<VlatAuthoringLayer> Layers => _layers;
        internal List<VlatAuthoringKeyformGroup> KeyformGroups => _keyformGroups;
        internal string SourcePath => _sourcePath;
        internal bool IsDirty => _isDirty;
        internal long Revision => _revision;

        internal static VlatAuthoringDocument CreateTemporary()
        {
            var document = CreateInstance<VlatAuthoringDocument>();
            document.hideFlags = HideFlags.HideAndDontSave;
            return document;
        }

        internal VlatAuthoringPackResult GetCachedEditingPack()
        {
            return _editingPack != null && _editingPackRevision == _revision ? _editingPack : null;
        }

        internal void SetCachedEditingPack(VlatAuthoringPackResult pack)
        {
            _editingPack = pack;
            _editingPackRevision = _revision;
        }

        // Undo記録・Recovery・指紋計算でUnity直列化を読み終えた後に、pack済み差分文字列を解放する。
        internal void ReleaseSerializationCaches()
        {
            for (var g = 0; g < _keyformGroups.Count; g++)
            {
                var group = _keyformGroups[g];
                if (group == null) continue;
                for (var k = 0; k < group.Keys.Count; k++) group.Keys[k]?.ReleasePackedCache();
            }
        }

        internal void SetSourcePath(string sourcePath)
        {
            _sourcePath = sourcePath;
        }

        internal void MarkDirty()
        {
            _isDirty = true;
        }

        internal void AdvanceRevision()
        {
            _revision = VlatAuthoringRevision.Next();
        }

        internal void MarkSaved(string sourcePath)
        {
            _sourcePath = sourcePath;
            _isDirty = false;
        }

        internal void EnsureEditorIdentities()
        {
            for (var groupIndex = 0; groupIndex < _keyformGroups.Count; groupIndex++)
            {
                var group = _keyformGroups[groupIndex];
                if (group == null) continue;
                _ = group.EditorIdentity;
                for (var keyIndex = 0; keyIndex < group.Keys.Count; keyIndex++)
                    if (group.Keys[keyIndex] != null) _ = group.Keys[keyIndex].EditorIdentity;
            }

            for (var bandIndex = 0; bandIndex < _bands.Count; bandIndex++)
            {
                var band = _bands[bandIndex];
                if (band != null)
                {
                    _ = band.EditorIdentity;
                }
            }

            for (var layerIndex = 0; layerIndex < _layers.Count; layerIndex++)
            {
                var layer = _layers[layerIndex];
                if (layer == null)
                {
                    continue;
                }

                _ = layer.EditorIdentity;
                for (var modifierIndex = 0; modifierIndex < layer.Modifiers.Count; modifierIndex++)
                {
                    var modifier = layer.Modifiers[modifierIndex];
                    if (modifier != null)
                    {
                        _ = modifier.EditorIdentity;
                    }
                }
            }
        }
    }

    internal static class VlatAuthoringIdentity
    {
        internal static string NewIdentity()
        {
            return Guid.NewGuid().ToString("N");
        }

        internal static string Ensure(ref string identity)
        {
            if (string.IsNullOrEmpty(identity))
            {
                identity = NewIdentity();
            }

            return identity;
        }
    }

    internal static class VlatAuthoringRevision
    {
        private static long _last = DateTime.UtcNow.Ticks;

        internal static long Next()
        {
            var now = DateTime.UtcNow.Ticks;
            if (now > _last)
            {
                _last = now;
                return now;
            }

            _last++;
            return _last;
        }
    }
}
