using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VLat.Editor.Authoring
{
    // 一つのpointer gestureだけを所有する。Document/Undo/Recoveryは所有・変更しない。
    internal sealed class VlatKeyformDragGesture : IDisposable
    {
        private readonly Transform _root, _head;
        private readonly SkinnedMeshRenderer _renderer;
        private readonly Mesh _source, _committedMesh;
        private readonly Camera _camera;
        private readonly Transform[] _bones;
        private readonly Matrix4x4[] _boneMatrices;
        private readonly Rect _cameraRect;
        private readonly Matrix4x4 _rootMatrix, _headMatrix, _rendererMatrix, _view, _projection, _inverseVP;
        private readonly Vector3[] _sourcePositions, _initialDeltas, _positions, _deltas, _world, _ndc;
        private readonly Matrix4x4[] _worldToBind;
        private readonly float[] _weights, _rendererWeights;
        private readonly bool[] _selected;
        private readonly int[] _affectedVertices;
        // 影響頂点ごとの開始screen逆投影(同次座標/world)。gesture中は不変なので開始時に一度だけ計算する。
        private readonly Vector4[] _affectedHomogeneous;
        private readonly Vector3[] _affectedInitialWorld;
        private readonly Vector4 _inverseColumnX, _inverseColumnY;
        private readonly int _width, _height;
        private readonly SkinQuality _quality;
        private readonly SkinWeights _globalQuality;
        private readonly Transform _rootBone;
        private readonly Matrix4x4[] _bindposes;
        private readonly BoneWeight1[] _boneWeights;
        private readonly byte[] _bonesPerVertex;
        private readonly Vector2 _pointerStart;
        private readonly string _fingerprint;
        private bool _disposed;
        internal VlatAuthoringDocument Document { get; }
        internal long Revision { get; }
        internal int GroupIndex { get; }
        internal int KeyIndex { get; }
        internal string GroupIdentity { get; }
        internal string KeyIdentity { get; }
        internal Mesh TemporaryMesh { get; private set; }
        internal Renderer Renderer => _renderer;
        internal Mesh SourceMesh => _source;
        internal IReadOnlyList<Vector3> Deltas => _deltas;

        internal VlatKeyformDragGesture(VlatAuthoringDocument document, int groupIndex, int keyIndex,
            Transform root, Transform head, SkinnedMeshRenderer renderer, Mesh source, Camera camera,
            VlatVertexSelectionSession selection, Vector2 pointerStart)
        {
            Document = document; Revision = document.Revision; GroupIndex = groupIndex; KeyIndex = keyIndex;
            var group = document.KeyformGroups[groupIndex];
            GroupIdentity = group.EditorIdentity; KeyIdentity = group.Keys[keyIndex].EditorIdentity;
            _root = root; _head = head; _renderer = renderer; _source = source; _camera = camera;
            _committedMesh = renderer.sharedMesh;
            _pointerStart = pointerStart;
            _width = camera.pixelWidth; _height = camera.pixelHeight; _cameraRect = camera.rect;
            if (_cameraRect != new Rect(0, 0, 1, 1)) throw new ArgumentException("Main Preview全体を使用するCameraだけが編集可能です。");
            _view = camera.worldToCameraMatrix; _projection = camera.projectionMatrix;
            _inverseVP = RequireInverse(_projection * _view);
            _rootMatrix = root.localToWorldMatrix; _headMatrix = head.localToWorldMatrix;
            _rendererMatrix = renderer.localToWorldMatrix;
            RequireInverse(_rootMatrix); RequireInverse(_headMatrix); RequireInverse(_rendererMatrix);
            RequireFinite(pointerStart.x); RequireFinite(pointerStart.y);
            if (_width <= 0 || _height <= 0 || selection.SelectedCount == 0 || selection.Weights.Count != source.vertexCount)
                throw new ArgumentException("頂点を選択し、有効なMain Previewでドラッグしてください。");
            VlatAuthoringPackResult pack;
            using (VlatEditorProfiling.Measure(VlatEditorPhase.DragBeginPack))
                pack = VlatAuthoringPersistence.PackForEditing(document);
            if (!pack.Success) throw new ArgumentException("Document/Bindingが不正です。Validationを確認してください。");
            using (VlatEditorProfiling.Measure(VlatEditorPhase.DragBeginFingerprint))
                _fingerprint = VlatSessionFingerprint.Content(document, pack);
            _rendererWeights = new float[_committedMesh.blendShapeCount];
            for (var i = 0; i < _rendererWeights.Length; i++) _rendererWeights[i] = renderer.GetBlendShapeWeight(i);
            using (VlatEditorProfiling.Measure(VlatEditorPhase.DragBeginBinding))
                RequireBinding();
            _quality = renderer.quality; _globalQuality = QualitySettings.skinWeights; _rootBone = renderer.rootBone;
            _bindposes = source.bindposes;
            _boneWeights = source.GetAllBoneWeights().ToArray();
            _bonesPerVertex = source.GetBonesPerVertex().ToArray();
            _bones = renderer.bones; _boneMatrices = new Matrix4x4[_bones.Length];
            if (_bones.Length != _bindposes.Length) throw new ArgumentException("Bone参照とbindposeが一致しません。Avatarを再読込してください。");
            for (var b = 0; b < _bones.Length; b++)
            {
                if (_bones[b] == null) throw new ArgumentException("Bone参照が失われています。");
                _boneMatrices[b] = _bones[b].localToWorldMatrix;
            }
            _sourcePositions = source.vertices;
            var count = _sourcePositions.Length;
            _initialDeltas = new Vector3[count]; _positions = new Vector3[count]; _deltas = new Vector3[count];
            _world = new Vector3[count]; _ndc = new Vector3[count]; _worldToBind = new Matrix4x4[count];
            _weights = new float[count]; _selected = new bool[count];
            foreach (var delta in group.Keys[keyIndex].Deltas) _initialDeltas[delta.VertexIndex] = delta.PositionDelta;
            var affectedCount = 0;
            for (var i = 0; i < count; i++)
            {
                _selected[i] = selection.IsSelected(i);
                _weights[i] = selection.Weights[i];
                if (!Finite(_weights[i]) || _weights[i] < 0 || _weights[i] > 1) throw new ArgumentException("Soft Selection weightが不正です。");
                _deltas[i] = _initialDeltas[i]; _positions[i] = _sourcePositions[i] + _deltas[i];
                RequireFinite(_deltas[i]); RequireFinite(_positions[i]);
                if (_weights[i] > 0) affectedCount++;
            }
            _affectedVertices = new int[affectedCount];
            for (int i = 0, at = 0; i < count; i++) if (_weights[i] > 0) _affectedVertices[at++] = i;
            _affectedHomogeneous = new Vector4[affectedCount];
            _affectedInitialWorld = new Vector3[affectedCount];
            _inverseColumnX = _inverseVP.GetColumn(0); _inverseColumnY = _inverseVP.GetColumn(1);
            try
            {
                TemporaryMesh = Object.Instantiate(source);
                TemporaryMesh.name = "VLat Drag Temporary";
                TemporaryMesh.hideFlags = HideFlags.HideAndDontSave;
                TemporaryMesh.ClearBlendShapes(); // Expressionは既存geometry modeでstash済み。二重補正を避ける。
                using (VlatEditorProfiling.Measure(VlatEditorPhase.DragBeginSkinning))
                    CaptureSkinning();
                for (var affected = 0; affected < _affectedVertices.Length; affected++)
                {
                    // 従来MouseMoveごとに計算していた開始screenの逆投影と同じ式・同じ値。
                    var i = _affectedVertices[affected];
                    var initial = _inverseVP * new Vector4(_ndc[i].x, _ndc[i].y, _ndc[i].z, 1);
                    _affectedHomogeneous[affected] = initial;
                    _affectedInitialWorld[affected] = new Vector3(initial.x, initial.y, initial.z) / initial.w;
                }
                // calibrationの全頂点offsetを一度だけ戻す。以後は影響集合だけを更新する。
                for (var i = 0; i < count; i++) _positions[i] = _sourcePositions[i] + _initialDeltas[i];
                Update(pointerStart);
            }
            catch { Dispose(); throw; }
        }

        // Unity自身のaffine skinningを4回だけBakeし、元indexごとのbind→world線形写像を採取する。
        // quality/bone数/scaleを独自に推測せず、開始時のみ実行。MouseMoveではBakeも割当も行わない。
        private void CaptureSkinning()
        {
            var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave, name = "VLat Drag Calibration" };
            var vertices = new List<Vector3>(_positions.Length);
            try
            {
                _renderer.sharedMesh = TemporaryMesh;
                for (var pass = 0; pass < 4; pass++)
                {
                    var offset = pass == 0 ? Vector3.zero : pass == 1 ? Vector3.right : pass == 2 ? Vector3.up : Vector3.forward;
                    using (VlatEditorProfiling.Measure(VlatEditorPhase.DragSkinUpload))
                    {
                        for (var i = 0; i < _positions.Length; i++) _positions[i] = _sourcePositions[i] + _initialDeltas[i] + offset;
                        TemporaryMesh.vertices = _positions;
                    }
                    using (VlatEditorProfiling.Measure(VlatEditorPhase.DragSkinBake))
                    {
                        _renderer.BakeMesh(baked, false);
                        baked.GetVertices(vertices);
                    }
                    if (vertices.Count != _positions.Length) throw new ArgumentException("BakeMeshの元頂点対応を確認できません。");
                    using (VlatEditorProfiling.Measure(VlatEditorPhase.DragSkinColumns))
                    for (var i = 0; i < vertices.Count; i++)
                    {
                        var world = _rendererMatrix.MultiplyPoint3x4(vertices[i]);
                        RequireFinite(world); // 全頂点の有限性検査は従来どおり（不正rigの拒否）。
                        if (pass == 0) { _world[i] = world; continue; }
                        // bind→world線形写像の列は、影響頂点だけが使う（逆行列・MouseMoveとも weight>0 のみ）。
                        if (_weights[i] == 0) continue;
                        var column = world - _world[i];
                        ref var map = ref _worldToBind[i];
                        // SetColumn(pass-1, (Vector4)column) と同じ値（w=0）を直接設定する。
                        if (pass == 1) { map.m00 = column.x; map.m10 = column.y; map.m20 = column.z; map.m30 = 0; }
                        else if (pass == 2) { map.m01 = column.x; map.m11 = column.y; map.m21 = column.z; map.m31 = 0; }
                        else { map.m02 = column.x; map.m12 = column.y; map.m22 = column.z; map.m32 = 0; }
                    }
                }
                var vp = _projection * _view;
                var nearClipPlane = _camera.nearClipPlane; // 同期loop中は不変。頂点ごとのnative property呼出を避ける。
                using (VlatEditorProfiling.Measure(VlatEditorPhase.DragSkinInverse))
                for (var i = 0; i < _positions.Length; i++)
                {
                    if (_weights[i] == 0) continue;
                    _worldToBind[i][3, 3] = 1;
                    _worldToBind[i] = RequireBlockInverse(_worldToBind[i]);
                    var clip = vp * new Vector4(_world[i].x, _world[i].y, _world[i].z, 1);
                    if (!Finite(clip.w) || Mathf.Abs(clip.w) < 1e-6f || -_view.MultiplyPoint3x4(_world[i]).z <= nearClipPlane)
                        throw new ArgumentException("選択頂点を安全に逆投影できません（カメラの背面/near plane）。");
                    _ndc[i] = new Vector3(clip.x, clip.y, clip.z) / clip.w;
                    RequireFinite(_ndc[i]);
                }
            }
            finally
            {
                RestoreRenderer();
                Object.DestroyImmediate(baked);
            }
        }

        internal void Update(Vector2 pointer)
        {
            RequireCurrent(); RequireFinite(pointer.x); RequireFinite(pointer.y);
            var delta = pointer - _pointerStart;
            var zero = delta.x == 0 && delta.y == 0;
            // inverseVP * (ndc + (dx, dy, 0, 0)) = inverseVP * ndc + dx * column0 + dy * column1。
            // 透視除算前の同次座標は線形なので、開始時の同次座標へ全頂点共通のoffsetを足す。
            var screenOffset = _inverseColumnX * (delta.x * 2f / _width) + _inverseColumnY * (delta.y * 2f / _height);
            using (VlatEditorProfiling.Measure(VlatEditorPhase.DragMoveCompute))
            for (var affected = 0; affected < _affectedVertices.Length; affected++)
            {
                var i = _affectedVertices[affected];
                _deltas[i] = _initialDeltas[i];
                if (!zero)
                {
                    var homogeneous = _affectedHomogeneous[affected] + screenOffset;
                    if (!Finite(homogeneous.w) || Mathf.Abs(homogeneous.w) < 1e-6f) throw new ArgumentException("逆投影が特異です。");
                    var world = new Vector3(homogeneous.x, homogeneous.y, homogeneous.z) / homogeneous.w;
                    // 元screenの逆投影は開始時に同じ式で計算済み。移動量0では差分を作らない。
                    _deltas[i] += _worldToBind[i].MultiplyVector(world - _affectedInitialWorld[affected]) * _weights[i];
                }
                RequireFinite(_deltas[i]);
                _positions[i] = _sourcePositions[i] + _deltas[i]; RequireFinite(_positions[i]);
            }
            using (VlatEditorProfiling.Measure(VlatEditorPhase.DragMoveUpload))
            {
                TemporaryMesh.vertices = _positions;
                TemporaryMesh.RecalculateBounds();
            }
        }

        internal void RequireCurrent()
        {
            if (_disposed || Document == null || Document.Revision != Revision ||
                GroupIndex >= Document.KeyformGroups.Count || Document.KeyformGroups[GroupIndex].EditorIdentity != GroupIdentity ||
                KeyIndex >= Document.KeyformGroups[GroupIndex].Keys.Count || Document.KeyformGroups[GroupIndex].Keys[KeyIndex].EditorIdentity != KeyIdentity ||
                _root == null || _head == null || _renderer == null || _source == null || _camera == null ||
                _renderer.sharedMesh != _committedMesh || _renderer.quality != _quality || QualitySettings.skinWeights != _globalQuality ||
                _renderer.rootBone != _rootBone || _root.localToWorldMatrix != _rootMatrix ||
                _head.localToWorldMatrix != _headMatrix || _renderer.localToWorldMatrix != _rendererMatrix ||
                _camera.worldToCameraMatrix != _view || _camera.projectionMatrix != _projection ||
                _camera.pixelWidth != _width || _camera.pixelHeight != _height || _camera.rect != _cameraRect)
                throw new InvalidOperationException("編集開始時の対象・Revision・Camera・Transformが変わったためドラッグを中止しました。");
            for (var i = 0; i < _bones.Length; i++)
                if (_bones[i] == null || _bones[i].localToWorldMatrix != _boneMatrices[i])
                    throw new InvalidOperationException("Bone姿勢が変わったためドラッグを中止しました。");
        }

        internal List<VlatAuthoringVertexDelta> CreateCommit()
        {
            RequireCurrent();
            using (VlatEditorProfiling.Measure(VlatEditorPhase.DragCommitVerify))
            {
                RequireBinding(); RequireSkinningUnchanged();
            }
            VlatAuthoringPackResult pack;
            using (VlatEditorProfiling.Measure(VlatEditorPhase.DragCommitPack))
                // commit直前は再利用せず新しくpackし、開始時の内容指紋と照合する(多重防御を維持)。
                pack = VlatAuthoringPersistence.PackForEditing(Document, refresh: true);
            string fingerprint = null;
            if (pack.Success)
                using (VlatEditorProfiling.Measure(VlatEditorPhase.DragCommitFingerprint))
                    fingerprint = VlatSessionFingerprint.Content(Document, pack);
            if (!pack.Success || fingerprint != _fingerprint)
                throw new InvalidOperationException("Document/Bindingが編集中に変更されました。");
            using (VlatEditorProfiling.Measure(VlatEditorPhase.DragCommitCollect))
            {
                var result = new List<VlatAuthoringVertexDelta>();
                for (var i = 0; i < _deltas.Length; i++)
                    if (!_deltas[i].Equals(Vector3.zero)) result.Add(new VlatAuthoringVertexDelta { VertexIndex = i, PositionDelta = _deltas[i] });
                return result;
            }
        }

        internal void Render(Camera camera)
        {
            RequireCurrent();
            var bounds = _renderer.localBounds;
            var offscreen = _renderer.updateWhenOffscreen;
            var matrices = _renderer.forceMatrixRecalculationPerRender;
            try
            {
                _renderer.sharedMesh = TemporaryMesh;
                _renderer.localBounds = TemporaryMesh.bounds;
                _renderer.updateWhenOffscreen = true;
                _renderer.forceMatrixRecalculationPerRender = true;
                camera.Render();
            }
            finally
            {
                RestoreRenderer();
                if (_renderer != null)
                {
                    _renderer.localBounds = bounds;
                    _renderer.updateWhenOffscreen = offscreen;
                    _renderer.forceMatrixRecalculationPerRender = matrices;
                }
            }
        }

        private void RequireBinding()
        {
            // clone上だけで元Meshに一時的に戻して既存の厳密な照合を通す。
            try
            {
                _renderer.sharedMesh = _source;
                var group = Document.KeyformGroups[GroupIndex];
                VlatKeyformBindingUtility.RequireMatch(_root.gameObject, _renderer,
                    Document.Metadata.MeshBindings[group.MeshBinding], group.TopologyHash);
            }
            finally { RestoreRenderer(); }
        }

        private void RequireSkinningUnchanged()
        {
            var bones = _renderer.bones; var poses = _source.bindposes;
            var weights = _source.GetAllBoneWeights(); var counts = _source.GetBonesPerVertex();
            var same = bones.Length == _bones.Length && poses.Length == _bindposes.Length &&
                weights.Length == _boneWeights.Length && counts.Length == _bonesPerVertex.Length;
            if (same)
            {
                for (var i = 0; i < bones.Length; i++) same &= bones[i] == _bones[i];
                for (var i = 0; i < poses.Length; i++) same &= poses[i] == _bindposes[i];
                for (var i = 0; i < weights.Length; i++)
                    same &= weights[i].boneIndex == _boneWeights[i].boneIndex && weights[i].weight == _boneWeights[i].weight;
                for (var i = 0; i < counts.Length; i++) same &= counts[i] == _bonesPerVertex[i];
            }
            if (!same) throw new InvalidOperationException("Skinning定義が編集中に変更されました。");
        }

        private void RestoreRenderer()
        {
            if (_renderer == null) return;
            _renderer.sharedMesh = _committedMesh;
            for (var i = 0; i < _rendererWeights.Length; i++) _renderer.SetBlendShapeWeight(i, _rendererWeights[i]);
        }

        // [A 0; 0 1]（Aは3x3、列のw=0、[3,3]=1）専用の逆行列。RequireInverseと同じ判定・閾値・message:
        // 非有限値、条件数（4x4成分の最大絶対値の積 > 1e6）、積と単位行列の差 > 1e-3 を拒否する。
        // 3x3部分だけをdoubleの余因子展開で解き、頂点ごとのnative逆行列と4x4 indexer走査を避ける。
        internal static Matrix4x4 RequireBlockInverse(in Matrix4x4 m)
        {
            float a00 = m.m00, a01 = m.m01, a02 = m.m02;
            float a10 = m.m10, a11 = m.m11, a12 = m.m12;
            float a20 = m.m20, a21 = m.m21, a22 = m.m22;
            if (!Finite(a00) || !Finite(a01) || !Finite(a02) || !Finite(a10) || !Finite(a11) || !Finite(a12) ||
                !Finite(a20) || !Finite(a21) || !Finite(a22))
                throw new ArgumentException("非有限値を保存できません。");
            double c00 = (double)a11 * a22 - (double)a12 * a21;
            double c01 = (double)a12 * a20 - (double)a10 * a22;
            double c02 = (double)a10 * a21 - (double)a11 * a20;
            var determinant = a00 * c00 + a01 * c01 + a02 * c02;
            if (determinant == 0 || double.IsNaN(determinant) || double.IsInfinity(determinant))
                throw new ArgumentException("変換行列が特異または不安定です。Scale/Poseを確認してください。");
            var r = 1.0 / determinant;
            var i00 = (float)(c00 * r);
            var i01 = (float)(((double)a02 * a21 - (double)a01 * a22) * r);
            var i02 = (float)(((double)a01 * a12 - (double)a02 * a11) * r);
            var i10 = (float)(c01 * r);
            var i11 = (float)(((double)a00 * a22 - (double)a02 * a20) * r);
            var i12 = (float)(((double)a02 * a10 - (double)a00 * a12) * r);
            var i20 = (float)(c02 * r);
            var i21 = (float)(((double)a01 * a20 - (double)a00 * a21) * r);
            var i22 = (float)(((double)a00 * a11 - (double)a01 * a10) * r);
            // 4x4全体では0と1の成分も含むため、最大絶対値は1以上。
            var norm = Mathf.Max(1f, MaxAbs(a00, a01, a02, a10, a11, a12, a20, a21, a22));
            var inverseNorm = Mathf.Max(1f, MaxAbs(i00, i01, i02, i10, i11, i12, i20, i21, i22));
            if (!Finite(norm * inverseNorm) || norm * inverseNorm > 1000000f)
                throw new ArgumentException("変換行列の条件数が大きすぎます。Scale/Pose/位置を確認してください。");
            if (!Finite(i00) || !Finite(i01) || !Finite(i02) || !Finite(i10) || !Finite(i11) || !Finite(i12) ||
                !Finite(i20) || !Finite(i21) || !Finite(i22) ||
                !NearIdentity(a00 * i00 + a01 * i10 + a02 * i20, 1) || !NearIdentity(a00 * i01 + a01 * i11 + a02 * i21, 0) ||
                !NearIdentity(a00 * i02 + a01 * i12 + a02 * i22, 0) || !NearIdentity(a10 * i00 + a11 * i10 + a12 * i20, 0) ||
                !NearIdentity(a10 * i01 + a11 * i11 + a12 * i21, 1) || !NearIdentity(a10 * i02 + a11 * i12 + a12 * i22, 0) ||
                !NearIdentity(a20 * i00 + a21 * i10 + a22 * i20, 0) || !NearIdentity(a20 * i01 + a21 * i11 + a22 * i21, 0) ||
                !NearIdentity(a20 * i02 + a21 * i12 + a22 * i22, 1))
                throw new ArgumentException("変換行列が特異または不安定です。Scale/Poseを確認してください。");
            var inverse = Matrix4x4.identity;
            inverse.m00 = i00; inverse.m01 = i01; inverse.m02 = i02;
            inverse.m10 = i10; inverse.m11 = i11; inverse.m12 = i12;
            inverse.m20 = i20; inverse.m21 = i21; inverse.m22 = i22;
            return inverse;
        }

        private static float MaxAbs(float a, float b, float c, float d, float e, float f, float g, float h, float i) =>
            Mathf.Max(Mathf.Max(Mathf.Max(Mathf.Abs(a), Mathf.Abs(b)), Mathf.Max(Mathf.Abs(c), Mathf.Abs(d))),
                Mathf.Max(Mathf.Max(Mathf.Abs(e), Mathf.Abs(f)), Mathf.Max(Mathf.Max(Mathf.Abs(g), Mathf.Abs(h)), Mathf.Abs(i))));

        private static bool NearIdentity(float value, float expected) => Finite(value) && Mathf.Abs(value - expected) <= .001f;

        internal static Matrix4x4 RequireInverse(Matrix4x4 matrix)
        {
            for (var i = 0; i < 16; i++) RequireFinite(matrix[i]);
            var inverse = matrix.inverse;
            var product = matrix * inverse;
            var norm = 0f; var inverseNorm = 0f;
            for (var i = 0; i < 16; i++) { norm = Mathf.Max(norm, Mathf.Abs(matrix[i])); inverseNorm = Mathf.Max(inverseNorm, Mathf.Abs(inverse[i])); }
            if (!Finite(norm * inverseNorm) || norm * inverseNorm > 1000000f)
                throw new ArgumentException("変換行列の条件数が大きすぎます。Scale/Pose/位置を確認してください。");
            for (var i = 0; i < 16; i++)
                if (!Finite(inverse[i]) || !Finite(product[i]) || Mathf.Abs(product[i] - Matrix4x4.identity[i]) > .001f)
                    throw new ArgumentException("変換行列が特異または不安定です。Scale/Poseを確認してください。");
            return inverse;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static void RequireFinite(float value) { if (!Finite(value)) throw new ArgumentException("非有限値を保存できません。"); }
        private static void RequireFinite(Vector3 value) { RequireFinite(value.x); RequireFinite(value.y); RequireFinite(value.z); }

        /// <summary>ドラッグ専用Meshを解放します。確定済みDocumentと原本は変更しません。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (TemporaryMesh != null) Object.DestroyImmediate(TemporaryMesh);
            TemporaryMesh = null;
        }
    }
}
