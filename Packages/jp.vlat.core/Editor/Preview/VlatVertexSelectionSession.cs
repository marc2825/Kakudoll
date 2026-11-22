using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using VLat.Runtime;
using Object = UnityEngine.Object;

namespace VLat.Editor.Authoring
{
    internal enum VlatVertexSelectionTool
    {
        Single,
        Brush,
        Rectangle,
    }

    internal enum VlatVertexSelectionOperation
    {
        Replace,
        Add,
        Subtract,
    }

    internal enum VlatVertexVisibilityMode
    {
        VisibleOnly,
        SelectThrough,
    }

    internal enum VlatSoftSelectionFalloff
    {
        Smoothstep,
        Linear,
    }

    internal sealed class VlatVertexSelectionTarget
    {
        internal VlatVertexSelectionTarget(
            Renderer renderer,
            Mesh sourceMesh,
            string hierarchyPath)
        {
            Renderer = renderer;
            SourceMesh = sourceMesh;
            HierarchyPath = hierarchyPath;
            BindingIndex = -1;
            BindingStatus = "Unbound";
        }

        internal Renderer Renderer { get; }

        internal Mesh SourceMesh { get; }

        internal string HierarchyPath { get; }

        internal int BindingIndex { get; set; }

        internal string BindingStatus { get; set; }

        internal int VertexCount => SourceMesh == null ? 0 : SourceMesh.vertexCount;

        internal string DisplayName =>
            Renderer.name + " / " + SourceMesh.name + " / " + VertexCount +
            " vertices / " + BindingStatus;
    }

    /// <summary>
    /// Preview clone上の頂点選択、可視判定、soft-selection、overlayを所有します。
    /// </summary>
    internal sealed class VlatVertexSelectionSession : IDisposable
    {
        private const int MaximumDepthDimension = 1024;
        private const float MinimumBrushRadius = 2f;
        private const float MinimumSoftRadius = 0.0001f;
        private const float DepthRelativeTolerance = 0.0025f;
        private const float ComparisonEpsilon = 0.000001f;

        private static readonly Color32 UnselectedColor = new Color32(80, 170, 230, 105);
        private static readonly Color32 SelectedColor = new Color32(255, 145, 30, 255);
        private static readonly Color32 RectangleColor = new Color32(255, 220, 80, 240);

        private readonly List<VlatVertexSelectionTarget> _targets =
            new List<VlatVertexSelectionTarget>();
        private readonly List<GeometryCache> _occluders = new List<GeometryCache>();

        private Transform _avatarRoot;
        private GeometryCache _targetGeometry;
        private bool[] _selected = Array.Empty<bool>();
        private float[] _weights = Array.Empty<float>();
        private float[] _distances = Array.Empty<float>();
        private int[][] _adjacency = Array.Empty<int[]>();
        private int[] _heapVertices = Array.Empty<int>();
        private float[] _heapDistances = Array.Empty<float>();
        private int[] _heapPositions = Array.Empty<int>();
        private float[] _depthBuffer = Array.Empty<float>();
        private bool[] _visible = Array.Empty<bool>();
        private Vector3[] _screenPositions = Array.Empty<Vector3>();
        private Color32[] _overlayPixels = Array.Empty<Color32>();
        private Texture2D _overlay;
        private int _depthWidth;
        private int _depthHeight;
        private int _viewportWidth;
        private int _viewportHeight;
        private int _heapCount;
        private int _selectedCount;
        private int _targetIndex = -1;
        private bool _enabled;
        private bool _geometryDirty = true;
        private bool _projectionDirty = true;
        private bool _overlayDirty = true;
        private bool _gestureActive;
        private VlatVertexSelectionTool _gestureTool;
        private VlatVertexSelectionOperation _gestureOperation;
        private VlatVertexVisibilityMode _gestureVisibility;
        private float _gestureBrushRadius;
        private Vector2 _gestureStart;
        private Vector2 _gestureLast;
        private Vector2 _gestureCurrent;
        private string _diagnostic = string.Empty;
        private double _lastGeometryMilliseconds;
        private double _lastProjectionMilliseconds;
        private double _lastQueryMilliseconds;
        private double _lastSoftSelectionMilliseconds;
        private int _geometryBuildCount;
        private int _adjacencyBuildCount;
        private int _projectionUpdateCount;
        private int _queryCount;
        private bool _disposed;

        internal IReadOnlyList<VlatVertexSelectionTarget> Targets => _targets;

        internal int TargetIndex => _targetIndex;

        internal int VertexCount => _selected.Length;

        internal int SelectedCount => _selectedCount;

        internal IReadOnlyList<float> Weights => _weights;

        internal IReadOnlyList<bool> Visibility => _visible;

        internal IReadOnlyList<Vector3> ScreenPositions => _screenPositions;

        internal Texture Overlay => _enabled ? _overlay : null;

        internal bool Enabled => _enabled;

        internal float BrushRadius { get; private set; } = 18f;

        internal float SoftRadius { get; private set; } = 0.08f;

        internal VlatSoftSelectionFalloff Falloff { get; private set; } =
            VlatSoftSelectionFalloff.Smoothstep;

        internal VlatVertexVisibilityMode VisibilityMode { get; private set; } =
            VlatVertexVisibilityMode.VisibleOnly;

        internal VlatVertexSelectionTool Tool { get; private set; } =
            VlatVertexSelectionTool.Single;

        internal int GeometryBuildCount => _geometryBuildCount;

        internal int AdjacencyBuildCount => _adjacencyBuildCount;

        internal int ProjectionUpdateCount => _projectionUpdateCount;

        internal string Diagnostic => _diagnostic;

        internal string StatusSummary
        {
            get
            {
                if (!_enabled)
                {
                    return "Geometry selection inactive";
                }

                if (_targetIndex < 0 || _targetIndex >= _targets.Count)
                {
                    return "No editable renderer target";
                }

                var target = _targets[_targetIndex];
                return "Target: " + target.Renderer.name + " / original vertices " + VertexCount +
                    " / selected " + _selectedCount + " / " + target.BindingStatus;
            }
        }

        internal string PerformanceSummary =>
            "Selection: vertices " + VertexCount +
            " / geometry " + _lastGeometryMilliseconds.ToString("F2") + " ms" +
            " / projection+visibility " + _lastProjectionMilliseconds.ToString("F2") + " ms" +
            " / query " + _lastQueryMilliseconds.ToString("F2") + " ms" +
            " / soft " + _lastSoftSelectionMilliseconds.ToString("F2") + " ms" +
            " / queries " + _queryCount;

        internal void SetAvatar(Transform avatarRoot)
        {
            RequireNotDisposed();
            ReleaseGeometry();
            ReleaseOverlay();
            _targets.Clear();
            _avatarRoot = avatarRoot;
            _targetIndex = -1;
            _diagnostic = string.Empty;
            if (_avatarRoot == null)
            {
                return;
            }

            var renderers = _avatarRoot.GetComponentsInChildren<Renderer>(true);
            for (var pass = 0; pass < 2; pass++)
            {
                for (var index = 0; index < renderers.Length; index++)
                {
                    var renderer = renderers[index];
                    var isSkinned = renderer is SkinnedMeshRenderer;
                    if ((pass == 0) != isSkinned)
                    {
                        continue;
                    }

                    var mesh = GetSharedMesh(renderer);
                    if (mesh == null || mesh.vertexCount == 0)
                    {
                        continue;
                    }

                    string path;
                    try
                    {
                        path = UniVrmAdapter.GetRelativeHierarchyPath(
                            _avatarRoot,
                            renderer.transform);
                    }
                    catch (ArgumentException)
                    {
                        continue;
                    }

                    _targets.Add(new VlatVertexSelectionTarget(renderer, mesh, path));
                }
            }

            if (_targets.Count > 0)
            {
                SetTarget(0);
            }
            else
            {
                _diagnostic = "Avatar has no readable mesh renderer target.";
            }
        }

        internal void SetBindingReport(MeshBindingResolutionReport report)
        {
            for (var index = 0; index < _targets.Count; index++)
            {
                _targets[index].BindingIndex = -1;
                _targets[index].BindingStatus = report == null ? "Unbound" : "Binding unresolved";
            }

            if (report == null)
            {
                return;
            }

            for (var bindingIndex = 0; bindingIndex < report.Count; bindingIndex++)
            {
                var resolution = report[bindingIndex];
                if (!resolution.IsSuccess)
                {
                    continue;
                }

                for (var targetIndex = 0; targetIndex < _targets.Count; targetIndex++)
                {
                    var target = _targets[targetIndex];
                    if (target.Renderer != resolution.Renderer)
                    {
                        continue;
                    }

                    target.BindingIndex = resolution.BindingIndex;
                    target.BindingStatus = "Binding " + resolution.BindingIndex +
                        " / " + resolution.Stage;
                    break;
                }
            }
        }

        internal void SetEnabled(bool enabled)
        {
            RequireNotDisposed();
            if (_enabled == enabled)
            {
                return;
            }

            _enabled = enabled;
            CancelGesture();
            _overlayDirty = true;
            if (!enabled && _overlay != null)
            {
                ClearOverlayPixels();
                UploadOverlay();
            }
        }

        internal void SetTarget(int index)
        {
            RequireNotDisposed();
            if (index < 0 || index >= _targets.Count)
            {
                index = -1;
            }

            if (_targetIndex == index && _targetGeometry != null)
            {
                return;
            }

            _targetIndex = index;
            ReleaseGeometry();
            _geometryDirty = true;
            _projectionDirty = true;
            _overlayDirty = true;
            _diagnostic = index < 0 ? "No editable renderer target is selected." : string.Empty;
        }

        internal void SetTool(VlatVertexSelectionTool tool)
        {
            Tool = tool;
        }

        internal void SetVisibilityMode(VlatVertexVisibilityMode mode)
        {
            VisibilityMode = mode;
            _overlayDirty = true;
            UpdateOverlay();
        }

        internal void SetBrushRadius(float radius)
        {
            BrushRadius = Mathf.Max(MinimumBrushRadius, radius);
        }

        internal void SetSoftRadius(float radius)
        {
            SoftRadius = Mathf.Max(MinimumSoftRadius, radius);
            RecomputeSoftSelection();
            UpdateOverlay();
        }

        internal void SetFalloff(VlatSoftSelectionFalloff falloff)
        {
            Falloff = falloff;
            RecomputeSoftSelection();
            UpdateOverlay();
        }

        internal void InvalidateGeometry()
        {
            _geometryDirty = true;
            _projectionDirty = true;
            _overlayDirty = true;
        }

        internal void InvalidateProjection()
        {
            _projectionDirty = true;
            _overlayDirty = true;
        }

        internal void Refresh(Camera camera, int viewportWidth, int viewportHeight)
        {
            RequireNotDisposed();
            if (!_enabled || camera == null || _targetIndex < 0 ||
                _targetIndex >= _targets.Count || viewportWidth <= 0 || viewportHeight <= 0)
            {
                return;
            }

            if (_geometryDirty && !BuildGeometry())
            {
                UpdateOverlay();
                return;
            }

            if (_projectionDirty || _viewportWidth != viewportWidth ||
                _viewportHeight != viewportHeight)
            {
                UpdateProjectionAndVisibility(camera, viewportWidth, viewportHeight);
            }

            UpdateOverlay();
        }

        internal void BeginGesture(
            Vector2 screenPosition,
            VlatVertexSelectionOperation operation)
        {
            if (!_enabled || _targetGeometry == null)
            {
                return;
            }

            _gestureActive = true;
            _gestureTool = Tool;
            _gestureOperation = operation;
            _gestureVisibility = VisibilityMode;
            _gestureBrushRadius = BrushRadius;
            _gestureStart = screenPosition;
            _gestureLast = screenPosition;
            _gestureCurrent = screenPosition;

            if (_gestureTool == VlatVertexSelectionTool.Brush)
            {
                if (_gestureOperation == VlatVertexSelectionOperation.Replace)
                {
                    ClearSelectionInternal();
                }

                ClearSoftWeightsInternal();
                _lastQueryMilliseconds = 0d;
                ApplyCircle(screenPosition, _gestureBrushRadius, _gestureOperation);
                FinishSelectionQuery(false);
            }
            else
            {
                _overlayDirty = true;
                UpdateOverlay();
            }
        }

        internal void UpdateGesture(Vector2 screenPosition)
        {
            if (!_gestureActive)
            {
                return;
            }

            _gestureCurrent = screenPosition;
            if (_gestureTool == VlatVertexSelectionTool.Brush)
            {
                ApplyBrushSegment(_gestureLast, screenPosition);
                _gestureLast = screenPosition;
                FinishSelectionQuery(false);
            }
            else if (_gestureTool == VlatVertexSelectionTool.Rectangle)
            {
                _overlayDirty = true;
                UpdateOverlay();
            }
        }

        internal void EndGesture(Vector2 screenPosition)
        {
            if (!_gestureActive)
            {
                return;
            }

            _gestureCurrent = screenPosition;
            if (_gestureTool == VlatVertexSelectionTool.Single)
            {
                ApplySingle(screenPosition);
                FinishSelectionQuery();
            }
            else if (_gestureTool == VlatVertexSelectionTool.Rectangle)
            {
                ApplyRectangle(_gestureStart, screenPosition);
                FinishSelectionQuery();
            }
            else if (_gestureTool == VlatVertexSelectionTool.Brush)
            {
                FinishSelectionQuery();
            }

            _gestureActive = false;
            _overlayDirty = true;
            UpdateOverlay();
        }

        internal void CancelGesture()
        {
            if (_gestureActive && _gestureTool == VlatVertexSelectionTool.Brush)
            {
                RecomputeSoftSelection();
            }

            _gestureActive = false;
            _overlayDirty = true;
            UpdateOverlay();
        }

        internal void ClearSelection()
        {
            ClearSelectionInternal();
            RecomputeSoftSelection();
            UpdateOverlay();
        }

        internal bool IsSelected(int originalVertexIndex)
        {
            return originalVertexIndex >= 0 && originalVertexIndex < _selected.Length &&
                _selected[originalVertexIndex];
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleaseGeometry();
            _targets.Clear();
            _avatarRoot = null;
            ReleaseOverlay();
        }

        private bool BuildGeometry()
        {
            var stopwatch = Stopwatch.StartNew();
            ReleaseGeometryCaches();
            _geometryDirty = false;
            _projectionDirty = true;
            _overlayDirty = true;
            if (_avatarRoot == null || _targetIndex < 0 || _targetIndex >= _targets.Count)
            {
                _diagnostic = "Geometry cache cannot be built without an avatar target.";
                return false;
            }

            try
            {
                var renderers = _avatarRoot.GetComponentsInChildren<Renderer>(true);
                for (var index = 0; index < renderers.Length; index++)
                {
                    var renderer = renderers[index];
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    var sourceMesh = GetSharedMesh(renderer);
                    if (sourceMesh == null || sourceMesh.vertexCount == 0)
                    {
                        continue;
                    }

                    GeometryCache cache;
                    try
                    {
                        cache = GeometryCache.Create(renderer, sourceMesh);
                    }
                    catch (Exception exception)
                    {
                        if (renderer == _targets[_targetIndex].Renderer)
                        {
                            _diagnostic = "Selected renderer geometry is not readable: " +
                                exception.Message;
                            ReleaseGeometryCaches();
                            return false;
                        }

                        continue;
                    }

                    if (cache == null)
                    {
                        if (renderer == _targets[_targetIndex].Renderer)
                        {
                            _diagnostic =
                                "BakeMesh/source vertex count mismatch; original index mapping is unavailable.";
                            ReleaseGeometryCaches();
                            return false;
                        }

                        continue;
                    }

                    _occluders.Add(cache);
                    if (renderer == _targets[_targetIndex].Renderer)
                    {
                        _targetGeometry = cache;
                    }
                }

                if (_targetGeometry == null)
                {
                    _diagnostic = "Selected renderer geometry could not be cached.";
                    ReleaseGeometryCaches();
                    return false;
                }

                var sourceVertexCount = _targets[_targetIndex].SourceMesh.vertexCount;
                if (_targetGeometry.LocalPositions.Length != sourceVertexCount)
                {
                    _diagnostic = "BakeMesh/source vertex count mismatch; original index mapping is unavailable.";
                    ReleaseGeometryCaches();
                    return false;
                }

                _selected = new bool[sourceVertexCount];
                _weights = new float[sourceVertexCount];
                _distances = new float[sourceVertexCount];
                _visible = new bool[sourceVertexCount];
                _screenPositions = _targetGeometry.ScreenPositions;
                _heapVertices = new int[sourceVertexCount];
                _heapDistances = new float[sourceVertexCount];
                _heapPositions = new int[sourceVertexCount];
                BuildAdjacency(_targetGeometry.Triangles, sourceVertexCount);
                _selectedCount = 0;
                _diagnostic = _targets[_targetIndex].BindingIndex < 0
                    ? "Target is not resolved to a Kakudoll meshBinding; selection cannot be committed."
                    : string.Empty;
                _geometryBuildCount++;
                return true;
            }
            catch (Exception exception)
            {
                _diagnostic = "Geometry cache failed: " + exception.Message;
                ReleaseGeometryCaches();
                return false;
            }
            finally
            {
                stopwatch.Stop();
                _lastGeometryMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            }
        }

        private void BuildAdjacency(int[] triangles, int vertexCount)
        {
            var temporary = new List<int>[vertexCount];
            for (var index = 0; index < vertexCount; index++)
            {
                temporary[index] = new List<int>();
            }

            for (var index = 0; index + 2 < triangles.Length; index += 3)
            {
                var a = triangles[index];
                var b = triangles[index + 1];
                var c = triangles[index + 2];
                if (!IsVertexIndexValid(a, vertexCount) ||
                    !IsVertexIndexValid(b, vertexCount) ||
                    !IsVertexIndexValid(c, vertexCount))
                {
                    continue;
                }

                AddUndirectedEdge(temporary, a, b);
                AddUndirectedEdge(temporary, b, c);
                AddUndirectedEdge(temporary, c, a);
            }

            _adjacency = new int[vertexCount][];
            for (var vertex = 0; vertex < vertexCount; vertex++)
            {
                var neighbors = temporary[vertex];
                neighbors.Sort();
                var uniqueCount = 0;
                for (var index = 0; index < neighbors.Count; index++)
                {
                    if (index == 0 || neighbors[index] != neighbors[index - 1])
                    {
                        uniqueCount++;
                    }
                }

                var compact = new int[uniqueCount];
                var write = 0;
                for (var index = 0; index < neighbors.Count; index++)
                {
                    if (index == 0 || neighbors[index] != neighbors[index - 1])
                    {
                        compact[write++] = neighbors[index];
                    }
                }

                _adjacency[vertex] = compact;
            }

            _adjacencyBuildCount++;
        }

        private void UpdateProjectionAndVisibility(
            Camera camera,
            int viewportWidth,
            int viewportHeight)
        {
            var stopwatch = Stopwatch.StartNew();
            _viewportWidth = viewportWidth;
            _viewportHeight = viewportHeight;
            var depthScale = Mathf.Min(
                1f,
                MaximumDepthDimension / (float)Mathf.Max(viewportWidth, viewportHeight));
            var depthWidth = Mathf.Max(1, Mathf.RoundToInt(viewportWidth * depthScale));
            var depthHeight = Mathf.Max(1, Mathf.RoundToInt(viewportHeight * depthScale));
            EnsureDepthBuffer(depthWidth, depthHeight);
            for (var index = 0; index < _depthBuffer.Length; index++)
            {
                _depthBuffer[index] = float.PositiveInfinity;
            }

            for (var cacheIndex = 0; cacheIndex < _occluders.Count; cacheIndex++)
            {
                var cache = _occluders[cacheIndex];
                var matrix = cache.Renderer.transform.localToWorldMatrix;
                for (var vertex = 0; vertex < cache.LocalPositions.Length; vertex++)
                {
                    var world = matrix.MultiplyPoint3x4(cache.LocalPositions[vertex]);
                    cache.WorldPositions[vertex] = world;
                    cache.ScreenPositions[vertex] = camera.WorldToScreenPoint(world);
                }

                Rasterize(cache);
            }

            var scaleX = _depthWidth / (float)_viewportWidth;
            var scaleY = _depthHeight / (float)_viewportHeight;
            for (var vertex = 0; vertex < _screenPositions.Length; vertex++)
            {
                var screen = _screenPositions[vertex];
                if (screen.z <= camera.nearClipPlane || screen.x < 0f || screen.y < 0f ||
                    screen.x >= _viewportWidth || screen.y >= _viewportHeight)
                {
                    _visible[vertex] = false;
                    continue;
                }

                var x = Mathf.Clamp(Mathf.FloorToInt(screen.x * scaleX), 0, _depthWidth - 1);
                var y = Mathf.Clamp(Mathf.FloorToInt(screen.y * scaleY), 0, _depthHeight - 1);
                var nearest = FindNeighborhoodDepth(x, y);
                var tolerance = Mathf.Max(0.0005f, screen.z * DepthRelativeTolerance);
                _visible[vertex] = float.IsPositiveInfinity(nearest) || screen.z <= nearest + tolerance;
            }

            _projectionDirty = false;
            _overlayDirty = true;
            _projectionUpdateCount++;
            stopwatch.Stop();
            _lastProjectionMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        }

        private void Rasterize(GeometryCache cache)
        {
            var triangles = cache.Triangles;
            var screen = cache.ScreenPositions;
            var scaleX = _depthWidth / (float)_viewportWidth;
            var scaleY = _depthHeight / (float)_viewportHeight;
            for (var triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
            {
                var indexA = triangles[triangle];
                var indexB = triangles[triangle + 1];
                var indexC = triangles[triangle + 2];
                if (!IsVertexIndexValid(indexA, screen.Length) ||
                    !IsVertexIndexValid(indexB, screen.Length) ||
                    !IsVertexIndexValid(indexC, screen.Length))
                {
                    continue;
                }

                var a3 = screen[indexA];
                var b3 = screen[indexB];
                var c3 = screen[indexC];
                if (a3.z <= 0f || b3.z <= 0f || c3.z <= 0f)
                {
                    continue;
                }

                var a = new Vector2(a3.x * scaleX, a3.y * scaleY);
                var b = new Vector2(b3.x * scaleX, b3.y * scaleY);
                var c = new Vector2(c3.x * scaleX, c3.y * scaleY);
                var area = Edge(a, b, c);
                if (Mathf.Abs(area) <= ComparisonEpsilon)
                {
                    continue;
                }

                var minimumX = Mathf.Clamp(
                    Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))),
                    0,
                    _depthWidth - 1);
                var maximumX = Mathf.Clamp(
                    Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))),
                    0,
                    _depthWidth - 1);
                var minimumY = Mathf.Clamp(
                    Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))),
                    0,
                    _depthHeight - 1);
                var maximumY = Mathf.Clamp(
                    Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))),
                    0,
                    _depthHeight - 1);
                var inverseArea = 1f / area;
                for (var y = minimumY; y <= maximumY; y++)
                {
                    for (var x = minimumX; x <= maximumX; x++)
                    {
                        var point = new Vector2(x + 0.5f, y + 0.5f);
                        var wa = Edge(b, c, point) * inverseArea;
                        var wb = Edge(c, a, point) * inverseArea;
                        var wc = Edge(a, b, point) * inverseArea;
                        if (wa < -ComparisonEpsilon || wb < -ComparisonEpsilon ||
                            wc < -ComparisonEpsilon)
                        {
                            continue;
                        }

                        var inverseDepth = (wa / a3.z) + (wb / b3.z) + (wc / c3.z);
                        if (inverseDepth <= 0f)
                        {
                            continue;
                        }

                        var depth = 1f / inverseDepth;
                        var bufferIndex = (y * _depthWidth) + x;
                        if (depth < _depthBuffer[bufferIndex])
                        {
                            _depthBuffer[bufferIndex] = depth;
                        }
                    }
                }
            }
        }

        private float FindNeighborhoodDepth(int centerX, int centerY)
        {
            var nearest = float.PositiveInfinity;
            for (var offsetY = -1; offsetY <= 1; offsetY++)
            {
                var y = centerY + offsetY;
                if (y < 0 || y >= _depthHeight)
                {
                    continue;
                }

                for (var offsetX = -1; offsetX <= 1; offsetX++)
                {
                    var x = centerX + offsetX;
                    if (x < 0 || x >= _depthWidth)
                    {
                        continue;
                    }

                    var value = _depthBuffer[(y * _depthWidth) + x];
                    if (value < nearest)
                    {
                        nearest = value;
                    }
                }
            }

            return nearest;
        }

        private void ApplySingle(Vector2 position)
        {
            var stopwatch = Stopwatch.StartNew();
            var selectedIndex = FindSingleCandidate(
                position,
                _gestureBrushRadius,
                _gestureVisibility);
            if (_gestureOperation == VlatVertexSelectionOperation.Replace)
            {
                ClearSelectionInternal();
            }

            if (selectedIndex >= 0)
            {
                SetSelected(selectedIndex, _gestureOperation);
            }

            stopwatch.Stop();
            _lastQueryMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        }

        private int FindSingleCandidate(
            Vector2 position,
            float radius,
            VlatVertexVisibilityMode visibility)
        {
            var radiusSquared = radius * radius;
            var bestIndex = -1;
            var bestVisible = false;
            var bestDepth = float.PositiveInfinity;
            var bestDistance = float.PositiveInfinity;
            for (var vertex = 0; vertex < _screenPositions.Length; vertex++)
            {
                var screen = _screenPositions[vertex];
                if (screen.z <= 0f || !IsInsideViewport(screen))
                {
                    continue;
                }

                var isVisible = _visible[vertex];
                if (visibility == VlatVertexVisibilityMode.VisibleOnly && !isVisible)
                {
                    continue;
                }

                var deltaX = screen.x - position.x;
                var deltaY = screen.y - position.y;
                var distance = (deltaX * deltaX) + (deltaY * deltaY);
                if (distance > radiusSquared)
                {
                    continue;
                }

                if (bestIndex < 0 ||
                    IsBetterSingleCandidate(
                        isVisible,
                        screen.z,
                        distance,
                        vertex,
                        bestVisible,
                        bestDepth,
                        bestDistance,
                        bestIndex))
                {
                    bestIndex = vertex;
                    bestVisible = isVisible;
                    bestDepth = screen.z;
                    bestDistance = distance;
                }
            }

            return bestIndex;
        }

        private static bool IsBetterSingleCandidate(
            bool visible,
            float depth,
            float distance,
            int index,
            bool bestVisible,
            float bestDepth,
            float bestDistance,
            int bestIndex)
        {
            if (visible != bestVisible)
            {
                return visible;
            }

            if (Mathf.Abs(depth - bestDepth) > ComparisonEpsilon)
            {
                return depth < bestDepth;
            }

            if (Mathf.Abs(distance - bestDistance) > ComparisonEpsilon)
            {
                return distance < bestDistance;
            }

            return index < bestIndex;
        }

        private void ApplyBrushSegment(Vector2 start, Vector2 end)
        {
            var delta = end - start;
            var length = delta.magnitude;
            var step = Mathf.Max(1f, _gestureBrushRadius * 0.5f);
            var samples = Mathf.Max(1, Mathf.CeilToInt(length / step));
            for (var sample = 1; sample <= samples; sample++)
            {
                ApplyCircle(
                    Vector2.Lerp(start, end, sample / (float)samples),
                    _gestureBrushRadius,
                    _gestureOperation);
            }
        }

        private void ApplyCircle(
            Vector2 center,
            float radius,
            VlatVertexSelectionOperation operation)
        {
            var stopwatch = Stopwatch.StartNew();
            var radiusSquared = radius * radius;
            for (var vertex = 0; vertex < _screenPositions.Length; vertex++)
            {
                if (!IsEligible(vertex, _gestureVisibility))
                {
                    continue;
                }

                var screen = _screenPositions[vertex];
                var deltaX = screen.x - center.x;
                var deltaY = screen.y - center.y;
                if ((deltaX * deltaX) + (deltaY * deltaY) <= radiusSquared)
                {
                    SetSelected(vertex, operation);
                }
            }

            stopwatch.Stop();
            _lastQueryMilliseconds += stopwatch.Elapsed.TotalMilliseconds;
        }

        private void ApplyRectangle(Vector2 start, Vector2 end)
        {
            var stopwatch = Stopwatch.StartNew();
            if (_gestureOperation == VlatVertexSelectionOperation.Replace)
            {
                ClearSelectionInternal();
            }

            var minimumX = Mathf.Min(start.x, end.x);
            var maximumX = Mathf.Max(start.x, end.x);
            var minimumY = Mathf.Min(start.y, end.y);
            var maximumY = Mathf.Max(start.y, end.y);
            for (var vertex = 0; vertex < _screenPositions.Length; vertex++)
            {
                if (!IsEligible(vertex, _gestureVisibility))
                {
                    continue;
                }

                var screen = _screenPositions[vertex];
                if (screen.x >= minimumX && screen.x <= maximumX &&
                    screen.y >= minimumY && screen.y <= maximumY)
                {
                    SetSelected(vertex, _gestureOperation);
                }
            }

            stopwatch.Stop();
            _lastQueryMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        }

        private bool IsEligible(int vertex, VlatVertexVisibilityMode visibility)
        {
            var screen = _screenPositions[vertex];
            return screen.z > 0f && IsInsideViewport(screen) &&
                (visibility == VlatVertexVisibilityMode.SelectThrough || _visible[vertex]);
        }

        private bool IsInsideViewport(Vector3 screen)
        {
            return screen.x >= 0f && screen.x < _viewportWidth &&
                screen.y >= 0f && screen.y < _viewportHeight;
        }

        private void SetSelected(int vertex, VlatVertexSelectionOperation operation)
        {
            var select = operation != VlatVertexSelectionOperation.Subtract;
            if (_selected[vertex] == select)
            {
                return;
            }

            _selected[vertex] = select;
            _selectedCount += select ? 1 : -1;
        }

        private void FinishSelectionQuery(bool recomputeSoftSelection = true)
        {
            _queryCount++;
            if (recomputeSoftSelection)
            {
                RecomputeSoftSelection();
            }

            _overlayDirty = true;
            UpdateOverlay();
        }

        private void RecomputeSoftSelection()
        {
            var stopwatch = Stopwatch.StartNew();
            if (_weights.Length == 0 || _targetGeometry == null)
            {
                stopwatch.Stop();
                _lastSoftSelectionMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
                return;
            }

            _heapCount = 0;
            for (var vertex = 0; vertex < _weights.Length; vertex++)
            {
                _weights[vertex] = 0f;
                _distances[vertex] = float.PositiveInfinity;
                _heapPositions[vertex] = -1;
                if (_selected[vertex])
                {
                    _distances[vertex] = 0f;
                    HeapInsertOrDecrease(vertex, 0f);
                }
            }

            while (_heapCount > 0)
            {
                var vertex = HeapPop(out var distance);
                if (distance > SoftRadius)
                {
                    break;
                }

                var neighbors = _adjacency[vertex];
                for (var index = 0; index < neighbors.Length; index++)
                {
                    var neighbor = neighbors[index];
                    var edgeLength = Vector3.Distance(
                        _targetGeometry.LocalPositions[vertex],
                        _targetGeometry.LocalPositions[neighbor]);
                    var candidate = distance + edgeLength;
                    if (candidate + ComparisonEpsilon < _distances[neighbor] &&
                        candidate <= SoftRadius)
                    {
                        _distances[neighbor] = candidate;
                        HeapInsertOrDecrease(neighbor, candidate);
                    }
                }
            }

            for (var vertex = 0; vertex < _weights.Length; vertex++)
            {
                if (_selected[vertex])
                {
                    _weights[vertex] = 1f;
                }
                else if (_distances[vertex] <= SoftRadius)
                {
                    var value = Mathf.Clamp01(1f - (_distances[vertex] / SoftRadius));
                    _weights[vertex] = Falloff == VlatSoftSelectionFalloff.Smoothstep
                        ? value * value * (3f - (2f * value))
                        : value;
                }
            }

            stopwatch.Stop();
            _lastSoftSelectionMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            _overlayDirty = true;
        }

        private void HeapInsertOrDecrease(int vertex, float distance)
        {
            var position = _heapPositions[vertex];
            if (position >= 0)
            {
                if (distance >= _heapDistances[position])
                {
                    return;
                }

                _heapDistances[position] = distance;
                HeapBubbleUp(position);
                return;
            }

            position = _heapCount++;
            _heapVertices[position] = vertex;
            _heapDistances[position] = distance;
            _heapPositions[vertex] = position;
            HeapBubbleUp(position);
        }

        private int HeapPop(out float distance)
        {
            var vertex = _heapVertices[0];
            distance = _heapDistances[0];
            _heapPositions[vertex] = -1;
            _heapCount--;
            if (_heapCount > 0)
            {
                _heapVertices[0] = _heapVertices[_heapCount];
                _heapDistances[0] = _heapDistances[_heapCount];
                _heapPositions[_heapVertices[0]] = 0;
                HeapBubbleDown(0);
            }

            return vertex;
        }

        private void HeapBubbleUp(int position)
        {
            while (position > 0)
            {
                var parent = (position - 1) / 2;
                if (!HeapIsLess(position, parent))
                {
                    break;
                }

                HeapSwap(position, parent);
                position = parent;
            }
        }

        private void HeapBubbleDown(int position)
        {
            while (true)
            {
                var left = (position * 2) + 1;
                if (left >= _heapCount)
                {
                    return;
                }

                var right = left + 1;
                var smallest = right < _heapCount && HeapIsLess(right, left) ? right : left;
                if (!HeapIsLess(smallest, position))
                {
                    return;
                }

                HeapSwap(position, smallest);
                position = smallest;
            }
        }

        private bool HeapIsLess(int left, int right)
        {
            var leftDistance = _heapDistances[left];
            var rightDistance = _heapDistances[right];
            if (Mathf.Abs(leftDistance - rightDistance) > ComparisonEpsilon)
            {
                return leftDistance < rightDistance;
            }

            return _heapVertices[left] < _heapVertices[right];
        }

        private void HeapSwap(int left, int right)
        {
            var vertex = _heapVertices[left];
            var distance = _heapDistances[left];
            _heapVertices[left] = _heapVertices[right];
            _heapDistances[left] = _heapDistances[right];
            _heapVertices[right] = vertex;
            _heapDistances[right] = distance;
            _heapPositions[_heapVertices[left]] = left;
            _heapPositions[_heapVertices[right]] = right;
        }

        private void UpdateOverlay()
        {
            if (!_enabled || !_overlayDirty || _depthWidth <= 0 || _depthHeight <= 0)
            {
                return;
            }

            EnsureOverlay(_depthWidth, _depthHeight);
            ClearOverlayPixels();
            if (_targetGeometry != null)
            {
                var scaleX = _depthWidth / (float)_viewportWidth;
                var scaleY = _depthHeight / (float)_viewportHeight;
                for (var vertex = 0; vertex < _screenPositions.Length; vertex++)
                {
                    if (!IsEligible(vertex, VisibilityMode))
                    {
                        continue;
                    }

                    var screen = _screenPositions[vertex];
                    var x = Mathf.RoundToInt(screen.x * scaleX);
                    var y = Mathf.RoundToInt(screen.y * scaleY);
                    var weight = _weights[vertex];
                    if (_selected[vertex])
                    {
                        DrawPoint(x, y, 2, SelectedColor);
                    }
                    else if (weight > 0f)
                    {
                        DrawPoint(x, y, 1, SoftColor(weight));
                    }
                    else
                    {
                        DrawPoint(x, y, 0, UnselectedColor);
                    }
                }

                if (_gestureActive && _gestureTool == VlatVertexSelectionTool.Rectangle)
                {
                    DrawRectangle(_gestureStart, _gestureCurrent, scaleX, scaleY);
                }
            }

            UploadOverlay();
            _overlayDirty = false;
        }

        private void DrawRectangle(Vector2 start, Vector2 end, float scaleX, float scaleY)
        {
            var minimumX = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Min(start.x, end.x) * scaleX),
                0,
                _depthWidth - 1);
            var maximumX = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Max(start.x, end.x) * scaleX),
                0,
                _depthWidth - 1);
            var minimumY = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Min(start.y, end.y) * scaleY),
                0,
                _depthHeight - 1);
            var maximumY = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Max(start.y, end.y) * scaleY),
                0,
                _depthHeight - 1);
            for (var x = minimumX; x <= maximumX; x++)
            {
                SetOverlayPixel(x, minimumY, RectangleColor);
                SetOverlayPixel(x, maximumY, RectangleColor);
            }

            for (var y = minimumY; y <= maximumY; y++)
            {
                SetOverlayPixel(minimumX, y, RectangleColor);
                SetOverlayPixel(maximumX, y, RectangleColor);
            }
        }

        private void DrawPoint(int centerX, int centerY, int radius, Color32 color)
        {
            for (var offsetY = -radius; offsetY <= radius; offsetY++)
            {
                for (var offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    if ((offsetX * offsetX) + (offsetY * offsetY) <= radius * radius + 1)
                    {
                        SetOverlayPixel(centerX + offsetX, centerY + offsetY, color);
                    }
                }
            }
        }

        private void SetOverlayPixel(int x, int y, Color32 color)
        {
            if (x < 0 || x >= _depthWidth || y < 0 || y >= _depthHeight)
            {
                return;
            }

            _overlayPixels[(y * _depthWidth) + x] = color;
        }

        private void EnsureDepthBuffer(int width, int height)
        {
            if (_depthWidth == width && _depthHeight == height &&
                _depthBuffer.Length == width * height)
            {
                return;
            }

            _depthWidth = width;
            _depthHeight = height;
            _depthBuffer = new float[width * height];
            _overlayDirty = true;
        }

        private void EnsureOverlay(int width, int height)
        {
            if (_overlay != null && _overlay.width == width && _overlay.height == height)
            {
                return;
            }

            DestroyObject(_overlay);
            _overlay = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                name = "Kakudoll Vertex Selection Overlay",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            _overlayPixels = new Color32[width * height];
        }

        private void ClearOverlayPixels()
        {
            for (var index = 0; index < _overlayPixels.Length; index++)
            {
                _overlayPixels[index] = default;
            }
        }

        private void UploadOverlay()
        {
            if (_overlay == null || _overlayPixels.Length == 0)
            {
                return;
            }

            _overlay.SetPixels32(_overlayPixels);
            _overlay.Apply(false, false);
        }

        private void ClearSelectionInternal()
        {
            for (var index = 0; index < _selected.Length; index++)
            {
                _selected[index] = false;
            }

            _selectedCount = 0;
        }

        private void ClearSoftWeightsInternal()
        {
            for (var index = 0; index < _weights.Length; index++)
            {
                _weights[index] = 0f;
            }
        }

        private void ReleaseGeometry()
        {
            ReleaseGeometryCaches();
            _selected = Array.Empty<bool>();
            _weights = Array.Empty<float>();
            _distances = Array.Empty<float>();
            _adjacency = Array.Empty<int[]>();
            _heapVertices = Array.Empty<int>();
            _heapDistances = Array.Empty<float>();
            _heapPositions = Array.Empty<int>();
            _visible = Array.Empty<bool>();
            _screenPositions = Array.Empty<Vector3>();
            _selectedCount = 0;
            _gestureActive = false;
            _geometryDirty = true;
            _projectionDirty = true;
            _overlayDirty = true;
        }

        private void ReleaseGeometryCaches()
        {
            for (var index = 0; index < _occluders.Count; index++)
            {
                _occluders[index].Dispose();
            }

            _occluders.Clear();
            _targetGeometry = null;
        }

        private void ReleaseOverlay()
        {
            DestroyObject(_overlay);
            _overlay = null;
            _overlayPixels = Array.Empty<Color32>();
            _depthBuffer = Array.Empty<float>();
            _depthWidth = 0;
            _depthHeight = 0;
            _viewportWidth = 0;
            _viewportHeight = 0;
        }

        private void RequireNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(VlatVertexSelectionSession));
            }
        }

        private static Mesh GetSharedMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                return skinned.sharedMesh;
            }

            var filter = renderer.GetComponent<MeshFilter>();
            return filter == null ? null : filter.sharedMesh;
        }

        private static bool IsVertexIndexValid(int index, int vertexCount)
        {
            return index >= 0 && index < vertexCount;
        }

        private static void AddUndirectedEdge(List<int>[] adjacency, int a, int b)
        {
            if (a == b)
            {
                return;
            }

            adjacency[a].Add(b);
            adjacency[b].Add(a);
        }

        private static float Edge(Vector2 a, Vector2 b, Vector2 point)
        {
            return ((point.x - a.x) * (b.y - a.y)) -
                ((point.y - a.y) * (b.x - a.x));
        }

        private static Color32 SoftColor(float weight)
        {
            var t = Mathf.Clamp01(weight);
            return new Color32(
                (byte)Mathf.RoundToInt(Mathf.Lerp(50f, 255f, t)),
                (byte)Mathf.RoundToInt(Mathf.Lerp(130f, 220f, t)),
                (byte)Mathf.RoundToInt(Mathf.Lerp(255f, 40f, t)),
                225);
        }

        private static void DestroyObject(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }

        private sealed class GeometryCache : IDisposable
        {
            private GeometryCache(
                Renderer renderer,
                Vector3[] localPositions,
                int[] triangles,
                Mesh ownedBakedMesh)
            {
                Renderer = renderer;
                LocalPositions = localPositions;
                Triangles = triangles;
                OwnedBakedMesh = ownedBakedMesh;
                WorldPositions = new Vector3[localPositions.Length];
                ScreenPositions = new Vector3[localPositions.Length];
            }

            internal Renderer Renderer { get; }

            internal Vector3[] LocalPositions { get; }

            internal Vector3[] WorldPositions { get; }

            internal Vector3[] ScreenPositions { get; }

            internal int[] Triangles { get; }

            private Mesh OwnedBakedMesh { get; }

            internal static GeometryCache Create(Renderer renderer, Mesh sourceMesh)
            {
                Mesh bakedMesh = null;
                Vector3[] positions;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    bakedMesh = new Mesh
                    {
                        name = "Kakudoll Selection Bake " + renderer.name,
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                    skinned.BakeMesh(bakedMesh);
                    if (bakedMesh.vertexCount != sourceMesh.vertexCount)
                    {
                        DestroyObject(bakedMesh);
                        return null;
                    }

                    positions = bakedMesh.vertices;
                }
                else
                {
                    positions = sourceMesh.vertices;
                }

                if (positions.Length != sourceMesh.vertexCount)
                {
                    DestroyObject(bakedMesh);
                    return null;
                }

                return new GeometryCache(renderer, positions, sourceMesh.triangles, bakedMesh);
            }

            public void Dispose()
            {
                DestroyObject(OwnedBakedMesh);
            }
        }
    }
}
