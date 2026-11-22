using System;
using System.IO;
using UniVRM10;
using UnityEditor;
using UnityEngine;
using VLat.Core.Model;
using VLat.Core.Serialization;
using VLat.Runtime;

namespace VLat.Editor
{
    internal static class SampleDocumentGenerator
    {
        private const string MenuPath = "Kakudoll/Dev/Generate Sample Document";
        private const float DeltaMagnitude = 0.02f;

        [MenuItem(MenuPath, true)]
        private static bool CanGenerate()
        {
            var selected = Selection.activeGameObject;
            return selected != null
                && selected.GetComponent<SkinnedMeshRenderer>() != null
                && selected.GetComponentInParent<Vrm10Instance>() != null;
        }

        [MenuItem(MenuPath)]
        private static void Generate()
        {
            var selected = Selection.activeGameObject;
            var renderer = selected == null ? null : selected.GetComponent<SkinnedMeshRenderer>();
            var instance = selected == null ? null : selected.GetComponentInParent<Vrm10Instance>();
            if (renderer == null || instance == null || renderer.sharedMesh == null)
            {
                EditorUtility.DisplayDialog(
                    "Kakudoll Sample",
                    "Vrm10Instance配下のSkinnedMeshRendererを選択してください。",
                    "OK");
                return;
            }

            var mesh = renderer.sharedMesh;
            Vector3[] vertices;
            try
            {
                vertices = mesh.vertices;
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog(
                    "Kakudoll Sample",
                    "選択Meshの頂点を読み取れません。Read/Write設定を確認してください。\n" + exception.Message,
                    "OK");
                return;
            }

            if (vertices.Length == 0 || mesh.subMeshCount == 0)
            {
                EditorUtility.DisplayDialog("Kakudoll Sample", "頂点とsubMeshを持つMeshを選択してください。", "OK");
                return;
            }

            var jsonPath = EditorUtility.SaveFilePanel(
                "Save Kakudoll sample document",
                Application.dataPath,
                renderer.name + ".vlat.json",
                "json");
            if (string.IsNullOrEmpty(jsonPath))
            {
                return;
            }

            if (!jsonPath.EndsWith(".vlat.json", StringComparison.OrdinalIgnoreCase))
            {
                jsonPath = Path.ChangeExtension(jsonPath, null) + ".vlat.json";
            }

            try
            {
                var binary = CreateVertexDeltaBinary(vertices, out var positionOffset);
                var binaryPath = jsonPath.Substring(0, jsonPath.Length - ".json".Length) + ".bin";
                var document = CreateDocument(
                    instance,
                    renderer,
                    mesh,
                    vertices.Length,
                    positionOffset,
                    binary.Length,
                    Path.GetFileName(binaryPath));
                var result = VlatSerializer.Save(jsonPath, document, new[] { binary });
                if (!result.Success)
                {
                    var message = result.Diagnostics.Count == 0
                        ? "不明な保存エラーです。"
                        : result.Diagnostics[0].Path + ": " + result.Diagnostics[0].Message;
                    EditorUtility.DisplayDialog("Kakudoll Sample", message, "OK");
                    return;
                }

                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog(
                    "Kakudoll Sample",
                    "選択したMeshを対象とする左右横顔用サンプルを保存しました。\n" + jsonPath,
                    "OK");
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Kakudoll Sample", "生成に失敗しました。\n" + exception.Message, "OK");
            }
        }

        private static VlatDocument CreateDocument(
            Vrm10Instance instance,
            SkinnedMeshRenderer renderer,
            Mesh mesh,
            int vertexCount,
            int positionOffset,
            int binaryLength,
            string binaryUri)
        {
            var sourceMaterials = renderer.sharedMaterials;
            var materials = new VlatMaterialSlot[sourceMaterials.Length];
            for (var index = 0; index < sourceMaterials.Length; index++)
            {
                var material = sourceMaterials[index];
                var materialName = material == null || string.IsNullOrEmpty(material.name)
                    ? "Material " + index
                    : material.name;
                var shaderName = material == null || material.shader == null || string.IsNullOrEmpty(material.shader.name)
                    ? "Unknown"
                    : material.shader.name;
                materials[index] = new VlatMaterialSlot(index, materialName, shaderName);
            }

            var binding = new MeshBinding(
                UniVrmAdapter.GetRelativeHierarchyPath(instance.transform, renderer.transform),
                mesh.name,
                vertexCount,
                PositionHashUtility.Calculate(mesh),
                materials);
            var metadata = new VlatMetadata(
                "0.1.0",
                "Kakudoll Sample Generator",
                new TargetAvatar(instance.name, "1.0", new[] { binding }));
            var indices = new VlatAccessor(0, "UINT32");
            var positions = new VlatAccessor(1, "FLOAT32");
            var leftBand = new AngleBand(
                "profile-left",
                "Left profile",
                new AngleRange(60d, 120d),
                new AngleRange(-90d, 90d),
                new AngleFeather(10d, 0d));
            var rightBand = new AngleBand(
                "profile-right",
                "Right profile",
                new AngleRange(-120d, -60d),
                new AngleRange(-90d, 90d),
                new AngleFeather(10d, 0d));
            var leftLayer = CreateLayer("sample-left", "profile-left", indices, positions);
            var rightLayer = CreateLayer("sample-right", "profile-right", indices, positions);
            return new VlatDocument(
                metadata,
                new[] { new VlatBuffer(binaryUri, binaryLength) },
                new[]
                {
                    new VlatBufferView(0, 0, positionOffset),
                    new VlatBufferView(0, positionOffset, binaryLength - positionOffset),
                },
                Array.Empty<VlatTexture>(),
                new[] { leftBand, rightBand },
                new[] { leftLayer, rightLayer });
        }

        private static VlatLayer CreateLayer(
            string id,
            string bandId,
            VlatAccessor indices,
            VlatAccessor positions)
        {
            return new VlatLayer(
                id,
                id,
                "bangs",
                true,
                0,
                bandId,
                new VlatModifier[]
                {
                    new VisibilityModifier(0, 0, false),
                    new VertexDeltaModifier(0, indices, positions),
                });
        }

        private static byte[] CreateVertexDeltaBinary(Vector3[] vertices, out int positionOffset)
        {
            positionOffset = checked(vertices.Length * 4);
            var binary = new byte[checked(positionOffset + (vertices.Length * 12))];
            var center = Vector3.zero;
            for (var index = 0; index < vertices.Length; index++)
            {
                center += vertices[index];
            }

            center /= vertices.Length;
            for (var index = 0; index < vertices.Length; index++)
            {
                WriteUInt32LittleEndian(binary, index * 4, (uint)index);
                var relative = vertices[index] - center;
                var outward = new Vector3(relative.x, relative.y, 0f);
                if (outward.sqrMagnitude <= 1e-12f)
                {
                    outward = Vector3.right;
                }
                else
                {
                    outward.Normalize();
                }

                var delta = outward * DeltaMagnitude;
                var offset = positionOffset + (index * 12);
                WriteSingleLittleEndian(binary, offset, delta.x);
                WriteSingleLittleEndian(binary, offset + 4, delta.y);
                WriteSingleLittleEndian(binary, offset + 8, delta.z);
            }

            return binary;
        }

        private static void WriteUInt32LittleEndian(byte[] destination, int offset, uint value)
        {
            destination[offset] = (byte)value;
            destination[offset + 1] = (byte)(value >> 8);
            destination[offset + 2] = (byte)(value >> 16);
            destination[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteSingleLittleEndian(byte[] destination, int offset, float value)
        {
            var bits = BitConverter.SingleToInt32Bits(value);
            destination[offset] = (byte)bits;
            destination[offset + 1] = (byte)(bits >> 8);
            destination[offset + 2] = (byte)(bits >> 16);
            destination[offset + 3] = (byte)(bits >> 24);
        }
    }
}
