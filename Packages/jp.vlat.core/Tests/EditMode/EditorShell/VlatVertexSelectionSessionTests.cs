using System;
using NUnit.Framework;
using UnityEngine;
using VLat.Core.Model;
using VLat.Editor.Authoring;
using VLat.Runtime;
using Object = UnityEngine.Object;

namespace VLat.Tests.Edit
{
    /// <summary>Phase 2D-1の頂点identity、picking、visibility、soft-selectionを検証します。</summary>
    public sealed class VlatVertexSelectionSessionTests
    {
        /// <summary>meshBindingとoriginal indexがclone側の選択後も維持されることを検証します。</summary>
        [Test]
        public void Identity_UsesResolvedBindingAndOriginalSourceVertexIndex()
        {
            using (var fixture = SelectionFixture.CreateOverlappingTriangles())
            using (var session = new VlatVertexSelectionSession())
            {
                session.SetAvatar(fixture.Root.transform);
                var document = CreateBindingDocument(fixture.Mesh, "Target");
                session.SetBindingReport(UniVrmAdapter.Resolve(fixture.Root.transform, document));
                session.SetEnabled(true);
                session.Refresh(fixture.Camera, 200, 200);

                var point = session.ScreenPositions[0];
                session.BeginGesture(point, VlatVertexSelectionOperation.Replace);
                session.EndGesture(point);

                Assert.That(session.Targets[0].BindingIndex, Is.EqualTo(0));
                Assert.That(session.Targets[0].SourceMesh, Is.SameAs(fixture.Mesh));
                Assert.That(session.VertexCount, Is.EqualTo(fixture.Mesh.vertexCount));
                Assert.That(session.IsSelected(0), Is.True);
                Assert.That(session.IsSelected(3), Is.False);
            }
        }

        /// <summary>BakeMeshのdeformed positionがsource index順を保つ条件を固定します。</summary>
        [Test]
        public void Identity_SkinnedBakePreservesCountOrderingAndSourceAssets()
        {
            using (var fixture = SelectionFixture.CreateSkinnedTriangle())
            using (var session = new VlatVertexSelectionSession())
            {
                var sourceVertices = fixture.Mesh.vertices;
                var sourceMesh = fixture.SkinnedRenderer.sharedMesh;
                var sourceMaterial = fixture.SkinnedRenderer.sharedMaterial;
                var sourceColor = sourceMaterial.color;
                var sourceChildCount = fixture.Root.transform.childCount;
                session.SetAvatar(fixture.Root.transform);
                session.SetEnabled(true);
                session.Refresh(fixture.Camera, 200, 200);

                Assert.That(session.VertexCount, Is.EqualTo(sourceVertices.Length));
                Assert.That(session.ScreenPositions.Count, Is.EqualTo(sourceVertices.Length));
                var projectedOriginalIndexOne = session.ScreenPositions[1];
                session.BeginGesture(
                    projectedOriginalIndexOne,
                    VlatVertexSelectionOperation.Replace);
                session.EndGesture(projectedOriginalIndexOne);

                Assert.That(session.IsSelected(1), Is.True);
                Assert.That(fixture.SkinnedRenderer.sharedMesh, Is.SameAs(sourceMesh));
                Assert.That(fixture.SkinnedRenderer.sharedMaterial, Is.SameAs(sourceMaterial));
                Assert.That(sourceMaterial.color, Is.EqualTo(sourceColor));
                Assert.That(fixture.Mesh.vertices, Is.EqualTo(sourceVertices));
                Assert.That(fixture.Root.transform.childCount, Is.EqualTo(sourceChildCount));
                Assert.That(session.Overlay, Is.Not.Null);
            }
        }

        /// <summary>known camera projectionとcamera-only invalidationがcache契約を守ることを検証します。</summary>
        [Test]
        public void Projection_KnownCameraUpdatesWithoutRebuildingGeometryOrAdjacency()
        {
            using (var fixture = SelectionFixture.CreateOverlappingTriangles())
            using (var session = new VlatVertexSelectionSession())
            {
                session.SetAvatar(fixture.Root.transform);
                session.SetEnabled(true);
                session.Refresh(fixture.Camera, 200, 200);
                var before = session.ScreenPositions[2];
                var geometryBuilds = session.GeometryBuildCount;
                var adjacencyBuilds = session.AdjacencyBuildCount;
                var projectionUpdates = session.ProjectionUpdateCount;

                Assert.That(before.x, Is.EqualTo(100f).Within(0.01f));
                Assert.That(before.y, Is.EqualTo(150f).Within(0.01f));

                fixture.Camera.transform.position += Vector3.right;
                session.InvalidateProjection();
                session.Refresh(fixture.Camera, 200, 200);

                Assert.That(session.ScreenPositions[2].x, Is.Not.EqualTo(before.x).Within(0.01f));
                Assert.That(session.GeometryBuildCount, Is.EqualTo(geometryBuilds));
                Assert.That(session.AdjacencyBuildCount, Is.EqualTo(adjacencyBuilds));
                Assert.That(session.ProjectionUpdateCount, Is.EqualTo(projectionUpdates + 1));
            }
        }

        /// <summary>Singleがthreshold、visibility、depth、indexの固定順で決定されることを検証します。</summary>
        [Test]
        public void Single_ChoosesNearestVisibleFrontAndClearsOnEmptyReplace()
        {
            using (var fixture = SelectionFixture.CreateOverlappingTriangles())
            using (var session = new VlatVertexSelectionSession())
            {
                session.SetAvatar(fixture.Root.transform);
                session.SetEnabled(true);
                session.SetBrushRadius(5f);
                session.Refresh(fixture.Camera, 200, 200);
                var overlap = session.ScreenPositions[0];

                session.BeginGesture(overlap, VlatVertexSelectionOperation.Replace);
                session.EndGesture(overlap);
                Assert.That(session.IsSelected(0), Is.True);
                Assert.That(session.IsSelected(3), Is.False);

                session.BeginGesture(new Vector2(5f, 195f), VlatVertexSelectionOperation.Replace);
                session.EndGesture(new Vector2(5f, 195f));
                Assert.That(session.SelectedCount, Is.Zero);

                session.BeginGesture(overlap, VlatVertexSelectionOperation.Replace);
                session.EndGesture(overlap);
                Assert.That(session.IsSelected(0), Is.True);
            }
        }

        /// <summary>BrushのVisible Only/Select ThroughとReplace/Add/Subtractを検証します。</summary>
        [Test]
        public void Brush_RespectsRadiusOperationsAndVisibilityMode()
        {
            using (var fixture = SelectionFixture.CreateOverlappingTriangles())
            using (var session = new VlatVertexSelectionSession())
            {
                session.SetAvatar(fixture.Root.transform);
                session.SetEnabled(true);
                session.SetTool(VlatVertexSelectionTool.Brush);
                session.SetBrushRadius(4f);
                session.Refresh(fixture.Camera, 200, 200);
                var overlap = session.ScreenPositions[0];

                session.BeginGesture(overlap, VlatVertexSelectionOperation.Replace);
                session.EndGesture(overlap);
                Assert.That(session.IsSelected(0), Is.True);
                Assert.That(session.IsSelected(3), Is.False);
                Assert.That(session.IsSelected(1), Is.False, "radius外は選択しません。");

                session.SetVisibilityMode(VlatVertexVisibilityMode.SelectThrough);
                session.BeginGesture(overlap, VlatVertexSelectionOperation.Add);
                session.EndGesture(overlap);
                Assert.That(session.IsSelected(0), Is.True);
                Assert.That(session.IsSelected(3), Is.True);

                session.BeginGesture(overlap, VlatVertexSelectionOperation.Subtract);
                session.EndGesture(overlap);
                Assert.That(session.IsSelected(0), Is.False);
                Assert.That(session.IsSelected(3), Is.False);
            }
        }

        /// <summary>Rectangleのinside/outsideとReplace/Add/Subtractを検証します。</summary>
        [Test]
        public void Rectangle_AppliesDeterministicSetOperations()
        {
            using (var fixture = SelectionFixture.CreateOverlappingTriangles())
            using (var session = new VlatVertexSelectionSession())
            {
                session.SetAvatar(fixture.Root.transform);
                session.SetEnabled(true);
                session.SetTool(VlatVertexSelectionTool.Rectangle);
                session.SetVisibilityMode(VlatVertexVisibilityMode.SelectThrough);
                session.Refresh(fixture.Camera, 200, 200);
                var first = session.ScreenPositions[0];
                var second = session.ScreenPositions[1];

                SelectRectangle(session, first, 2f, VlatVertexSelectionOperation.Replace);
                Assert.That(session.IsSelected(0), Is.True);
                Assert.That(session.IsSelected(3), Is.True);
                Assert.That(session.IsSelected(1), Is.False);

                SelectRectangle(session, second, 2f, VlatVertexSelectionOperation.Add);
                Assert.That(session.IsSelected(1), Is.True);
                Assert.That(session.IsSelected(4), Is.True);

                SelectRectangle(session, first, 2f, VlatVertexSelectionOperation.Subtract);
                Assert.That(session.IsSelected(0), Is.False);
                Assert.That(session.IsSelected(3), Is.False);
                Assert.That(session.IsSelected(1), Is.True);
            }
        }

        /// <summary>triangle depthがfront/backとoverlapping geometryを区別することを検証します。</summary>
        [Test]
        public void Occlusion_ExcludesHiddenBackButSelectThroughIncludesIt()
        {
            using (var fixture = SelectionFixture.CreateOverlappingTriangles())
            using (var session = new VlatVertexSelectionSession())
            {
                session.SetAvatar(fixture.Root.transform);
                session.SetEnabled(true);
                session.Refresh(fixture.Camera, 200, 200);

                Assert.That(session.Visibility[0], Is.True);
                Assert.That(session.Visibility[3], Is.False);

                session.SetTool(VlatVertexSelectionTool.Brush);
                session.SetBrushRadius(4f);
                var overlap = session.ScreenPositions[0];
                session.BeginGesture(overlap, VlatVertexSelectionOperation.Replace);
                session.EndGesture(overlap);
                Assert.That(session.IsSelected(3), Is.False);

                session.SetVisibilityMode(VlatVertexVisibilityMode.SelectThrough);
                session.BeginGesture(overlap, VlatVertexSelectionOperation.Replace);
                session.EndGesture(overlap);
                Assert.That(session.IsSelected(0), Is.True);
                Assert.That(session.IsSelected(3), Is.True);
            }
        }

        /// <summary>geodesic weightが単調で、radius外と非連結surfaceへ伝播しないことを検証します。</summary>
        [Test]
        public void SoftSelection_IsGeodesicMonotonicAndStopsAtDisconnectedSurface()
        {
            using (var fixture = SelectionFixture.CreateGeodesicMesh())
            using (var session = new VlatVertexSelectionSession())
            {
                session.SetAvatar(fixture.Root.transform);
                session.SetEnabled(true);
                session.SetSoftRadius(0.25f);
                session.SetBrushRadius(2f);
                session.Refresh(fixture.Camera, 200, 200);
                var seed = session.ScreenPositions[0];
                session.BeginGesture(seed, VlatVertexSelectionOperation.Replace);
                session.EndGesture(seed);

                Assert.That(session.Weights[0], Is.EqualTo(1f));
                Assert.That(session.Weights[1], Is.GreaterThan(session.Weights[3]));
                Assert.That(session.Weights[3], Is.GreaterThan(0f));
                Assert.That(session.Weights[4], Is.EqualTo(0f), "近接した非連結surfaceへ伝播しません。");

                var adjacencyBuilds = session.AdjacencyBuildCount;
                session.SetSoftRadius(0.15f);
                Assert.That(session.Weights[3], Is.EqualTo(0f));
                Assert.That(session.AdjacencyBuildCount, Is.EqualTo(adjacencyBuilds));
            }
        }

        /// <summary>selection optionとcamera操作がdocument dirtyやselection identityを変えないことを検証します。</summary>
        [Test]
        public void SessionOptions_DoNotDirtyDocumentAndTargetSwitchClearsSafely()
        {
            var document = VlatAuthoringPersistence.CreateNew("Selection Session");
            var revision = document.Revision;
            try
            {
                using (var fixture = SelectionFixture.CreateTwoTargets())
                using (var session = new VlatVertexSelectionSession())
                {
                    session.SetAvatar(fixture.Root.transform);
                    session.SetEnabled(true);
                    session.SetBrushRadius(32f);
                    session.SetSoftRadius(0.3f);
                    session.SetVisibilityMode(VlatVertexVisibilityMode.SelectThrough);
                    session.Refresh(fixture.Camera, 200, 200);
                    var point = session.ScreenPositions[0];
                    session.BeginGesture(point, VlatVertexSelectionOperation.Replace);
                    session.EndGesture(point);
                    Assert.That(session.SelectedCount, Is.GreaterThan(0));

                    var selectedBeforeCamera = session.SelectedCount;
                    fixture.Camera.transform.position += Vector3.up * 0.1f;
                    session.InvalidateProjection();
                    session.Refresh(fixture.Camera, 200, 200);
                    Assert.That(session.SelectedCount, Is.EqualTo(selectedBeforeCamera));

                    session.SetTarget(1);
                    session.Refresh(fixture.Camera, 200, 200);
                    Assert.That(session.SelectedCount, Is.Zero);
                    Assert.That(document.IsDirty, Is.False);
                    Assert.That(document.Revision, Is.EqualTo(revision));
                }
            }
            finally
            {
                Object.DestroyImmediate(document);
            }
        }

        private static void SelectRectangle(
            VlatVertexSelectionSession session,
            Vector2 center,
            float halfSize,
            VlatVertexSelectionOperation operation)
        {
            session.BeginGesture(
                center - new Vector2(halfSize, halfSize),
                operation);
            session.EndGesture(center + new Vector2(halfSize, halfSize));
        }

        private static VlatDocument CreateBindingDocument(Mesh mesh, string path)
        {
            var binding = new MeshBinding(
                path,
                mesh.name,
                mesh.vertexCount,
                "test-position-hash",
                Array.Empty<VlatMaterialSlot>());
            return new VlatDocument(
                new VlatMetadata(
                    "0.1.0",
                    "Phase 2D-1 Test",
                    new TargetAvatar("Test Avatar", "1.0", new[] { binding })),
                Array.Empty<VlatBuffer>(),
                Array.Empty<VlatBufferView>(),
                Array.Empty<VlatTexture>(),
                Array.Empty<AngleBand>(),
                Array.Empty<VlatLayer>());
        }

        private sealed class SelectionFixture : IDisposable
        {
            private SelectionFixture()
            {
            }

            internal GameObject Root { get; private set; }

            internal Mesh Mesh { get; private set; }

            internal Camera Camera { get; private set; }

            internal SkinnedMeshRenderer SkinnedRenderer { get; private set; }

            private RenderTexture TargetTexture { get; set; }

            private Mesh SecondaryMesh { get; set; }

            private Material OwnedMaterial { get; set; }

            internal static SelectionFixture CreateOverlappingTriangles()
            {
                var fixture = CreateBase();
                fixture.Mesh = CreateMesh(
                    "Overlapping Selection Mesh",
                    new[]
                    {
                        new Vector3(-1f, -1f, 0f),
                        new Vector3(1f, -1f, 0f),
                        new Vector3(0f, 1f, 0f),
                        new Vector3(-1f, -1f, 1f),
                        new Vector3(1f, -1f, 1f),
                        new Vector3(0f, 1f, 1f),
                    },
                    new[] { 0, 1, 2, 3, 4, 5 });
                fixture.Mesh.subMeshCount = 2;
                fixture.Mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
                fixture.Mesh.SetTriangles(new[] { 3, 4, 5 }, 1);
                AddStaticRenderer(fixture, "Target", fixture.Mesh);
                return fixture;
            }

            internal static SelectionFixture CreateGeodesicMesh()
            {
                var fixture = CreateBase();
                fixture.Mesh = CreateMesh(
                    "Geodesic Selection Mesh",
                    new[]
                    {
                        new Vector3(0f, 0f, 0f),
                        new Vector3(0.1f, 0f, 0f),
                        new Vector3(0f, 0.1f, 0f),
                        new Vector3(0.2f, 0f, 0f),
                        new Vector3(0.01f, 0.01f, 0f),
                        new Vector3(0.03f, 0.01f, 0f),
                        new Vector3(0.01f, 0.03f, 0f),
                    },
                    new[] { 0, 1, 2, 1, 3, 2, 4, 5, 6 });
                AddStaticRenderer(fixture, "Target", fixture.Mesh);
                return fixture;
            }

            internal static SelectionFixture CreateTwoTargets()
            {
                var fixture = CreateBase();
                fixture.Mesh = CreateMesh(
                    "First Target Mesh",
                    new[]
                    {
                        new Vector3(-0.5f, -0.5f, 0f),
                        new Vector3(0.5f, -0.5f, 0f),
                        new Vector3(0f, 0.5f, 0f),
                    },
                    new[] { 0, 1, 2 });
                fixture.SecondaryMesh = CreateMesh(
                    "Second Target Mesh",
                    new[]
                    {
                        new Vector3(-0.3f, -0.3f, 0.2f),
                        new Vector3(0.3f, -0.3f, 0.2f),
                        new Vector3(0f, 0.3f, 0.2f),
                    },
                    new[] { 0, 1, 2 });
                AddStaticRenderer(fixture, "First", fixture.Mesh);
                AddStaticRenderer(fixture, "Second", fixture.SecondaryMesh);
                return fixture;
            }

            internal static SelectionFixture CreateSkinnedTriangle()
            {
                var fixture = CreateBase();
                fixture.Mesh = CreateMesh(
                    "Skinned Selection Mesh",
                    new[]
                    {
                        new Vector3(-0.5f, -0.5f, 0f),
                        new Vector3(0.5f, -0.5f, 0f),
                        new Vector3(0f, 0.5f, 0f),
                    },
                    new[] { 0, 1, 2 });
                var rendererObject = new GameObject("SkinnedTarget");
                rendererObject.transform.SetParent(fixture.Root.transform, false);
                var boneObject = new GameObject("Bone");
                boneObject.transform.SetParent(fixture.Root.transform, false);
                var renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
                var weights = new BoneWeight[3];
                for (var index = 0; index < weights.Length; index++)
                {
                    weights[index].boneIndex0 = 0;
                    weights[index].weight0 = 1f;
                }

                fixture.Mesh.boneWeights = weights;
                fixture.Mesh.bindposes = new[]
                {
                    boneObject.transform.worldToLocalMatrix * rendererObject.transform.localToWorldMatrix,
                };
                fixture.Mesh.AddBlendShapeFrame(
                    "Test",
                    100f,
                    new[] { Vector3.zero, new Vector3(0.2f, 0f, 0f), Vector3.zero },
                    new Vector3[3],
                    new Vector3[3]);
                renderer.sharedMesh = fixture.Mesh;
                renderer.sharedMaterial = fixture.CreateMaterial();
                renderer.bones = new[] { boneObject.transform };
                renderer.rootBone = boneObject.transform;
                renderer.SetBlendShapeWeight(0, 100f);
                fixture.SkinnedRenderer = renderer;
                return fixture;
            }

            public void Dispose()
            {
                if (Camera != null)
                {
                    Camera.targetTexture = null;
                }

                if (TargetTexture != null)
                {
                    TargetTexture.Release();
                    Object.DestroyImmediate(TargetTexture);
                }

                if (Root != null)
                {
                    Object.DestroyImmediate(Root);
                }

                if (Camera != null)
                {
                    Object.DestroyImmediate(Camera.gameObject);
                }

                Object.DestroyImmediate(Mesh);
                Object.DestroyImmediate(SecondaryMesh);
                Object.DestroyImmediate(OwnedMaterial);
            }

            private static SelectionFixture CreateBase()
            {
                var fixture = new SelectionFixture
                {
                    Root = new GameObject("Selection Avatar"),
                    TargetTexture = new RenderTexture(200, 200, 16),
                };
                fixture.TargetTexture.Create();
                var cameraObject = new GameObject("Selection Camera");
                fixture.Camera = cameraObject.AddComponent<Camera>();
                fixture.Camera.orthographic = true;
                fixture.Camera.orthographicSize = 2f;
                fixture.Camera.nearClipPlane = 0.1f;
                fixture.Camera.farClipPlane = 100f;
                fixture.Camera.transform.position = new Vector3(0f, 0f, -5f);
                fixture.Camera.transform.rotation = Quaternion.identity;
                fixture.Camera.targetTexture = fixture.TargetTexture;
                return fixture;
            }

            private static Mesh CreateMesh(string name, Vector3[] vertices, int[] triangles)
            {
                var mesh = new Mesh { name = name };
                mesh.vertices = vertices;
                mesh.triangles = triangles;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }

            private Material CreateMaterial()
            {
                if (OwnedMaterial == null)
                {
                    OwnedMaterial = new Material(Shader.Find("Standard"))
                    {
                        name = "Selection Source Material",
                        color = new Color(0.8f, 0.7f, 0.6f, 1f),
                    };
                }

                return OwnedMaterial;
            }

            private static void AddStaticRenderer(
                SelectionFixture fixture,
                string name,
                Mesh mesh)
            {
                var target = new GameObject(name);
                target.transform.SetParent(fixture.Root.transform, false);
                target.AddComponent<MeshFilter>().sharedMesh = mesh;
                target.AddComponent<MeshRenderer>().sharedMaterial = fixture.CreateMaterial();
            }
        }
    }
}
