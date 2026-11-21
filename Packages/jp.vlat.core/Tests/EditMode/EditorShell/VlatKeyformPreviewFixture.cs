using System;
using System.IO;
using NUnit.Framework;
using UniVRM10;
using UnityEditor;
using UnityEngine;
using VLat.Editor.Authoring;
using Object = UnityEngine.Object;

namespace VLat.Tests.Edit
{
    // 配布可能な合成の立体fixture。原本RendererとPreview cloneを別々に保持する。
    internal sealed class VlatKeyformPreviewFixture : IDisposable
    {
        internal Vrm10Instance Source;
        internal SkinnedMeshRenderer SourceRenderer;
        internal Mesh Mesh;
        internal Material Material;
        private VRM10Object _vrm;
        private VRM10Expression _happy;
        private VRM10Expression _aa;
        internal string DirectoryPath;
        internal VlatEditorDocumentController Session;
        internal VlatPreviewController Preview;
        internal Vrm10Instance Clone;
        internal SkinnedMeshRenderer Renderer => Clone.GetComponentInChildren<SkinnedMeshRenderer>();
        internal Transform Head => Clone.transform.Find("Head");

        internal VlatKeyformPreviewFixture(bool neutral = false)
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "VLat1C-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            Source = new GameObject("KeyformSource").AddComponent<Vrm10Instance>();
            var head = new GameObject("Head").transform;
            head.SetParent(Source.transform, false); head.localPosition = Vector3.up * 1.5f;
            var humanoid = new SerializedObject(Source.Humanoid);
            humanoid.FindProperty("m_Head").objectReferenceValue = head;
            humanoid.ApplyModifiedPropertiesWithoutUndo();
            var face = new GameObject("Face"); face.transform.SetParent(head, false);
            SourceRenderer = face.AddComponent<SkinnedMeshRenderer>();
            Mesh = new Mesh { name = "KeyformCube" };
            var vertices = new Vector3[8]; var weights = new BoneWeight[8];
            var wide = new Vector3[8]; var deep = new Vector3[8]; var smile = new Vector3[8];
            for (var i = 0; i < 8; i++)
            {
                var x = (i & 1) == 0 ? -1f : 1f;
                var y = (i & 2) == 0 ? -1f : 1f;
                var z = (i & 4) == 0 ? -1f : 1f;
                vertices[i] = new Vector3(x * .4f, y * .45f, z * .3f);
                weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                wide[i] = new Vector3(x * .25f, 0, 0);
                deep[i] = new Vector3(0, 0, z * .3f);
                smile[i] = new Vector3(0, y * .15f, 0);
            }
            Mesh.vertices = vertices;
            Mesh.triangles = new[] { 0,2,1,1,2,3,4,5,6,5,7,6,0,1,4,1,5,4,2,6,3,3,6,7,0,4,2,2,4,6,1,3,5,3,7,5 };
            Mesh.boneWeights = weights; Mesh.bindposes = new[] { Matrix4x4.identity };
            Mesh.AddBlendShapeFrame("Wide", 100, wide, null, null);
            Mesh.AddBlendShapeFrame("Deep", 100, deep, null, null);
            Mesh.AddBlendShapeFrame("Smile", 100, smile, null, null);
            Mesh.RecalculateNormals(); Mesh.RecalculateBounds();
            SourceRenderer.sharedMesh = Mesh; SourceRenderer.bones = new[] { head };
            SourceRenderer.rootBone = head; SourceRenderer.localBounds = Mesh.bounds;
            Material = new Material(Shader.Find("Unlit/Color")) { name = "KeyformFixtureMaterial", color = Color.white };
            SourceRenderer.sharedMaterial = Material;
            _happy = ScriptableObject.CreateInstance<VRM10Expression>(); _happy.name = "happy";
            _happy.MorphTargetBindings = new[] { new MorphTargetBinding("Head/Face", 2, 1f) };
            _aa = ScriptableObject.CreateInstance<VRM10Expression>(); _aa.name = "aa";
            _aa.MorphTargetBindings = new[] { new MorphTargetBinding("Head/Face", 2, .5f) };
            _vrm = ScriptableObject.CreateInstance<VRM10Object>(); _vrm.Expression.Happy = _happy;
            _vrm.Expression.Aa = _aa; Source.Vrm = _vrm;
            Session = new VlatEditorDocumentController(new VlatRecoveryStore(Path.Combine(DirectoryPath, "session")));
            Session.NewDocument(VlatCloseDecision.Discard);
            Seed(Session, neutral);
            Preview = new VlatPreviewController();
            Adopt(Preview);
            Connect();
        }

        internal void Seed(VlatEditorDocumentController session, bool neutral = false)
        {
            Assert.That(session.AddKeyformGroup(), Is.True);
            Assert.That(session.BindKeyformTarget(0, Source.gameObject, SourceRenderer), Is.True);
            if (!neutral) Assert.That(session.ImportKeyformPositions(0, 0, Source.gameObject, SourceRenderer, 0), Is.True);
            Assert.That(session.AddKeyform(0, 90), Is.True);
            if (!neutral) Assert.That(session.ImportKeyformPositions(0, 1, Source.gameObject, SourceRenderer, 1), Is.True);
        }

        internal void Adopt(VlatPreviewController preview)
        {
            Clone = Object.Instantiate(Source);
            Clone.name = Source.name;
            preview.AdoptAvatar(Clone, "synthetic-keyform.vrm");
            for (var i = 0; i < 3; i++) preview.ResizeView((VlatPreviewView)i, 256, 256, 1);
        }

        internal void Connect()
        {
            Preview.SetDocument(Session.Document);
            Preview.SetKeyformGroupSelection(Session.Selection.FindKeyformGroupIndex(Session.Document));
            Assert.That(Preview.RenderIfNeeded(), Is.True, Preview.DiagnosticSummary);
        }

        internal Vector3[] Bake()
        {
            var baked = new Mesh();
            try { Renderer.BakeMesh(baked); return baked.vertices; }
            finally { Object.DestroyImmediate(baked); }
        }

        internal static Color32[] Pixels(VlatPreviewController preview, VlatPreviewView view, string path = null)
        {
            var target = (RenderTexture)preview.GetTexture(view);
            var old = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                if (path != null) File.WriteAllBytes(path, image.EncodeToPNG());
                return image.GetPixels32();
            }
            finally { RenderTexture.active = old; Object.DestroyImmediate(image); }
        }

        /// <summary>合成原本・専用sessionとPreviewの所有資源を解放します。</summary>
        public void Dispose()
        {
            Preview.Dispose(); Session.Dispose();
            Object.DestroyImmediate(Source.gameObject); Object.DestroyImmediate(_vrm); Object.DestroyImmediate(_happy); Object.DestroyImmediate(_aa);
            Object.DestroyImmediate(Mesh); Object.DestroyImmediate(Material);
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
            Undo.ClearAll();
        }
    }
}
