using System;
using System.Collections.Generic;
using UnityEngine;
using VLat.Core.Model;
using VLat.Core.Validation;

namespace VLat.Editor.Authoring
{
    internal sealed partial class VlatEditorDocumentController
    {
        internal bool AddKeyformGroup()
        {
            RequireDocument();
            if (Document.KeyformGroups.Count != 0 || Document.Bands.Count != 0 ||
                Document.Layers.Count != 0 || Document.Textures.Count != 0)
                return RejectKeyform("$.keyformGroups", "グループは 1 つだけ作れます。新しいファイルで作ってください。");
            var group = new VlatAuthoringKeyformGroup();
            group.Keys.Add(new VlatAuthoringKeyform { Yaw = 0 });
            VlatAuthoringCommands.AddKeyformGroup(Document, group);
            Selection.SelectKeyformGroup(group);
            CommitChange("グループを作りました。");
            return true;
        }

        internal void SelectKeyformGroup(int groupIndex)
        {
            Selection.SelectKeyformGroup(GetKeyformGroup(groupIndex));
            PersistRecovery();
        }

        internal void SelectKeyform(int groupIndex, int keyIndex)
        {
            var group = GetKeyformGroup(groupIndex);
            Selection.SelectKeyform(group, group.Keys[keyIndex]);
            PersistRecovery();
        }

        internal void RemoveKeyformGroup(int groupIndex)
        {
            GetKeyformGroup(groupIndex);
            VlatAuthoringCommands.RemoveKeyformGroup(Document, groupIndex);
            Selection.SelectDocument();
            CommitChange("グループを消しました。");
        }

        internal bool UpdateKeyformGroup(int groupIndex, string id, double start, double end)
        {
            var group = GetKeyformGroup(groupIndex);
            if (!ValidateKeyformAngles(group, id, start, end, KeyformYaws(group))) return false;
            if (group.Id == id && group.SupportStart == start && group.SupportEnd == end) return CompleteUnchanged();
            VlatAuthoringCommands.SetKeyformGroup(Document, groupIndex, id, start, end);
            CommitChange("効く範囲とグループの設定を変えました。");
            return true;
        }

        internal bool AddKeyform(int groupIndex, double yaw)
        {
            var group = GetKeyformGroup(groupIndex);
            var yaws = KeyformYaws(group);
            yaws.Add(yaw);
            if (!ValidateKeyformAngles(group, group.Id, group.SupportStart, group.SupportEnd, yaws)) return false;
            var key = new VlatAuthoringKeyform { Yaw = yaw };
            VlatAuthoringCommands.AddKeyform(Document, groupIndex, key);
            Selection.SelectKeyform(group, key);
            CommitChange(FormatYaw(yaw) + " にキーを足しました。");
            return true;
        }

        internal bool RemoveKeyform(int groupIndex, int keyIndex)
        {
            var group = GetKeyformGroup(groupIndex);
            if (keyIndex < 0 || keyIndex >= group.Keys.Count)
                return RejectKeyform("$.keyformGroups[0].keys", "選択キーが存在しません。");
            if (group.Keys.Count <= 1)
                return RejectKeyform("$.keyformGroups[0].keys", "最後のキーは消せません。はじめからやり直すときは、グループを消します。");
            var removedYaw = group.Keys[keyIndex].Yaw;
            VlatAuthoringCommands.RemoveKeyform(Document, groupIndex, keyIndex);
            Selection.SelectKeyform(group, group.Keys[Math.Min(keyIndex, group.Keys.Count - 1)]);
            CommitChange(FormatYaw(removedYaw) + " のキーを消しました。");
            return true;
        }

        internal bool UpdateKeyformYaw(int groupIndex, int keyIndex, double yaw)
        {
            var group = GetKeyformGroup(groupIndex);
            var yaws = KeyformYaws(group);
            yaws[keyIndex] = yaw;
            if (!ValidateKeyformAngles(group, group.Id, group.SupportStart, group.SupportEnd, yaws, keyIndex)) return false;
            if (group.Keys[keyIndex].Yaw == yaw) return CompleteUnchanged();
            VlatAuthoringCommands.SetKeyformYaw(Document, groupIndex, keyIndex, yaw);
            CommitChange("キーの角度を " + FormatYaw(yaw) + " にしました。");
            return true;
        }

        // sourceMeshはPreviewのパーツから指定するときの差し替え前Mesh（VlatKeyformBindingUtility.Capture参照）。
        internal bool BindKeyformTarget(int groupIndex, GameObject avatarRoot, Renderer renderer, Mesh sourceMesh = null)
        {
            var group = GetKeyformGroup(groupIndex);
            try
            {
                var binding = VlatKeyformBindingUtility.Capture(avatarRoot, renderer, sourceMesh, out var topology);
                // 位置差分を持つキーを別基準へ誤って転用しない。新規groupで明示的に作り直す。
                if (HasKeyformPositions(group))
                    VlatKeyformBindingUtility.RequireMatch(avatarRoot, renderer, GetKeyformBinding(group), group.TopologyHash, sourceMesh);
                if (group.MeshBinding >= 0 && group.MeshBinding < Document.Metadata.MeshBindings.Count &&
                    topology == group.TopologyHash && SameBinding(GetKeyformBinding(group), binding)) return CompleteUnchanged();
                VlatAuthoringCommands.BindKeyformGroup(Document, groupIndex, binding, topology);
                CommitChange("直すパーツを " + binding.HierarchyPath + " にしました。");
                return true;
            }
            catch (ArgumentException exception)
            {
                return RejectKeyform("$.keyformGroups[0].meshBinding", exception.Message);
            }
        }

        internal bool ImportKeyformPositions(int groupIndex, int keyIndex, GameObject avatarRoot,
            Renderer renderer, int shapeIndex, Mesh sourceMesh = null)
        {
            var group = GetKeyformGroup(groupIndex);
            var key = group.Keys[keyIndex];
            try
            {
                var binding = GetKeyformBinding(group);
                VlatKeyformBindingUtility.RequireMatch(avatarRoot, renderer, binding, group.TopologyHash, sourceMesh);
                var result = VlatBlendShapeKeyformImporter.ImportPositions(
                    sourceMesh != null ? sourceMesh : VlatKeyformBindingUtility.GetSourceMesh(renderer), VlatKeyformBindingUtility.ToImmutable(binding),
                    group.TopologyHash, new[] { shapeIndex }, new[] { key.Yaw }, group.SupportStart, group.SupportEnd);
                if (!result.Success)
                {
                    _diagnostics = result.Diagnostics;
                    LastMessage = "取り込めませんでした。今の形はそのまま残っています。";
                    return false;
                }
                var delta = result.Document.KeyformGroups[0].Keys[0].Delta;
                var positions = delta == null ? new List<VlatAuthoringVertexDelta>() :
                    VlatAuthoringConversion.CopyVertexDelta(result.Document, result.Buffers, delta).Deltas;
                long count = positions.Count;
                foreach (var other in group.Keys) if (!ReferenceEquals(other, key)) count += other.Deltas.Count;
                if (count * 16 > VlatValidator.MaximumKeyformBufferBytes)
                    return RejectKeyform("$.keyformGroups[0].keys", "差分の合計が16 MiBを超えるため取り込めません。");
                if (SamePositions(key.Deltas, positions))
                {
                    _diagnostics = result.Diagnostics;
                    LastMessage = "取り込んだ形が今と同じなので、何も変えませんでした。";
                    return true;
                }
                VlatAuthoringCommands.SetKeyformPositions(Document, groupIndex, keyIndex, positions);
                CommitChange(shapeIndex < 0 ? "このキーの形を空にしました。" : "BlendShape から形を取り込みました。");
                var diagnostics = new List<VlatDiagnostic>(_diagnostics);
                diagnostics.AddRange(result.Diagnostics);
                _diagnostics = diagnostics.AsReadOnly();
                return true;
            }
            catch (ArgumentException exception)
            {
                return RejectKeyform("$.keyformGroups[0].keys[" + keyIndex + "].delta", exception.Message);
            }
        }

        // 一時Previewからの一括commit。全検証が終わるまでUnity UndoもDocumentも変更しない。
        internal bool CommitKeyformDrag(VlatAuthoringDocument expectedDocument, long revision,
            int groupIndex, int keyIndex, string groupIdentity, string keyIdentity,
            IReadOnlyList<VlatAuthoringVertexDelta> positions)
        {
            if (Document == null || Document != expectedDocument || Document.Revision != revision ||
                groupIndex < 0 || groupIndex >= Document.KeyformGroups.Count)
                return RejectKeyform("$.keyformGroups", "Document/Revisionが変更されました。");
            var group = Document.KeyformGroups[groupIndex];
            if (group.EditorIdentity != groupIdentity || keyIndex < 0 || keyIndex >= group.Keys.Count ||
                group.Keys[keyIndex].EditorIdentity != keyIdentity || Selection.Identity != keyIdentity ||
                Selection.ParentIdentity != groupIdentity || positions == null)
                return RejectKeyform("$.keyformGroups", "編集キーが変更されました。");
            VlatAuthoringPackResult pack;
            using (VlatEditorProfiling.Measure(VlatEditorPhase.CommitPrecheck))
                pack = VlatAuthoringPersistence.PackForEditing(Document);
            if (!pack.Success) return RejectKeyform("$.keyformGroups", "Document/Bindingが不正です。");
            var vertexCount = GetKeyformBinding(group).VertexCount;
            var previous = -1;
            long count = positions.Count;
            foreach (var other in group.Keys) if (!ReferenceEquals(other, group.Keys[keyIndex])) count += other.Deltas.Count;
            if (count * 16 > VlatValidator.MaximumKeyformBufferBytes)
                return RejectKeyform("$.keyformGroups", "差分の合計が16 MiBを超えます。");
            for (var i = 0; i < positions.Count; i++)
            {
                var item = positions[i];
                if (item == null || item.VertexIndex <= previous || item.VertexIndex >= vertexCount ||
                    !FinitePosition(item.PositionDelta) || !item.NormalDelta.Equals(Vector3.zero))
                    return RejectKeyform("$.keyformGroups", "頂点差分のindex/位置/法線が不正です。");
                previous = item.VertexIndex;
            }
            if (SamePositions(group.Keys[keyIndex].Deltas, positions)) return CompleteUnchanged();
            using (VlatEditorProfiling.Measure(VlatEditorPhase.CommitUndoSnapshot))
                VlatAuthoringCommands.SetKeyformPositions(Document, groupIndex, keyIndex, positions, "Drag Kakudoll Keyform Positions");
            CommitChange("形を変えました。");
            return true;
        }

        private static bool FinitePosition(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        internal double SuggestKeyformYaw(int groupIndex)
        {
            var group = GetKeyformGroup(groupIndex);
            var start = group.SupportStart;
            var largest = double.MinValue;
            var suggestion = start;
            for (var k = 0; k <= group.Keys.Count; k++)
            {
                var end = k == group.Keys.Count ? group.SupportEnd : group.Keys[k].Yaw;
                if (end - start > largest) { largest = end - start; suggestion = start + (end - start) / 2; }
                start = end;
            }
            return suggestion;
        }

        private static string FormatYaw(double yaw) =>
            (yaw < 0 ? "−" : string.Empty) + Math.Abs(yaw).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "°";

        private VlatAuthoringKeyformGroup GetKeyformGroup(int index)
        {
            RequireDocument();
            if (index < 0 || index >= Document.KeyformGroups.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return Document.KeyformGroups[index];
        }

        private VlatAuthoringMeshBinding GetKeyformBinding(VlatAuthoringKeyformGroup group)
        {
            if (group.MeshBinding < 0 || group.MeshBinding >= Document.Metadata.MeshBindings.Count)
                throw new ArgumentException("直すパーツが決まっていません。");
            return Document.Metadata.MeshBindings[group.MeshBinding];
        }

        private bool CompleteUnchanged()
        {
            ValidateEditing();
            LastMessage = "内容が同じなので、何も変えませんでした。";
            return true;
        }

        private static bool SameBinding(VlatAuthoringMeshBinding left, VlatAuthoringMeshBinding right)
        {
            if (left == null || left.HierarchyPath != right.HierarchyPath || left.MeshName != right.MeshName ||
                left.VertexCount != right.VertexCount || left.PositionHash != right.PositionHash || left.Materials.Count != right.Materials.Count) return false;
            for (var i = 0; i < left.Materials.Count; i++)
                if (left.Materials[i].Slot != right.Materials[i].Slot || left.Materials[i].MaterialName != right.Materials[i].MaterialName ||
                    left.Materials[i].Shader != right.Materials[i].Shader) return false;
            return true;
        }

        private static bool SamePositions(IReadOnlyList<VlatAuthoringVertexDelta> left, IReadOnlyList<VlatAuthoringVertexDelta> right)
        {
            if (left.Count != right.Count) return false;
            for (var i = 0; i < left.Count; i++)
                if (left[i].VertexIndex != right[i].VertexIndex || !left[i].PositionDelta.Equals(right[i].PositionDelta)) return false;
            return true;
        }

        private static bool HasKeyformPositions(VlatAuthoringKeyformGroup group)
        {
            foreach (var key in group.Keys) if (key.Deltas.Count != 0) return true;
            return false;
        }

        private static List<double> KeyformYaws(VlatAuthoringKeyformGroup group)
        {
            var yaws = new List<double>(group.Keys.Count);
            foreach (var key in group.Keys) yaws.Add(key.Yaw);
            return yaws;
        }

        private bool ValidateKeyformAngles(VlatAuthoringKeyformGroup group, string id, double start, double end,
            List<double> yaws, int editedKeyIndex = -1)
        {
            yaws.Sort();
            var keys = new YawKeyform[yaws.Count];
            for (var k = 0; k < keys.Length; k++) keys[k] = new YawKeyform(yaws[k], null);
            // 未bindingの編集中も角度編集は可能。ここでは角度契約だけをCoreで検証する。
            var definition = new KeyformGroup(id, 0, "sha256:" + new string('0', 64),
                group.CoordinateSpace, group.Interpolation, start, end, keys);
            var diagnostics = VlatValidator.ValidateKeyformDefinition(definition);
            if (!VlatAuthoringResultUtility.ContainsError(diagnostics)) return true;
            var related = new List<VlatDiagnostic>(diagnostics.Count);
            foreach (var diagnostic in diagnostics)
                related.Add(new VlatDiagnostic(diagnostic.Severity,
                    editedKeyIndex >= 0 && diagnostic.Path.Contains(".keys[")
                        ? "$.keyformGroups[0].keys[" + editedKeyIndex + "].yaw" : diagnostic.Path,
                    diagnostic.Line, diagnostic.Column, diagnostic.Message));
            // 候補をsortした後のindexではなく、変更を拒否した実際の編集キーへ診断を対応付ける。
            _diagnostics = related.AsReadOnly();
            LastMessage = "変えられませんでした。" + related[0].Message;
            return false;
        }

        private bool RejectKeyform(string path, string message)
        {
            _diagnostics = new[] { VlatAuthoringResultUtility.Error(path, message) };
            LastMessage = message + "（今の内容はそのまま残っています）";
            return false;
        }
    }
}
