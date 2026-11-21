using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UniGLTF;
using UniVRM10;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VLat.Core.Validation;
using VLat.Core.Evaluation;
using VLat.Core.Model;
using VLat.Runtime;
using Object = UnityEngine.Object;

namespace VLat.Editor.Authoring
{
    internal enum VlatPreviewState
    {
        NoAvatar,
        AvatarOnly,
        AvatarWithVLat,
        VLatUnavailable,
        AvatarLoadError,
    }

    internal sealed partial class VlatPreviewController : IDisposable
    {
        private const int ViewCount = 3;
        private const int MinimumTextureSize = 16;
        private const int MaximumTextureSize = 4096;

        private static readonly MethodInfo ClearSceneDirtinessMethod =
            typeof(EditorSceneManager).GetMethod(
                "ClearSceneDirtiness",
                BindingFlags.Static | BindingFlags.NonPublic);

        private readonly Scene _previewScene;
        private readonly Camera[] _cameras = new Camera[ViewCount];
        private readonly RenderTexture[] _targets = new RenderTexture[ViewCount];
        private readonly int[] _targetWidths = new int[ViewCount];
        private readonly int[] _targetHeights = new int[ViewCount];
        private readonly VlatPreviewViewState[] _views =
        {
            new VlatPreviewViewState(0f),
            new VlatPreviewViewState(45f),
            new VlatPreviewViewState(90f),
        };
        private readonly List<string> _diagnostics = new List<string>();
        private readonly List<VlatRuntimeDiagnostic> _vlatDiagnostics =
            new List<VlatRuntimeDiagnostic>();
        private readonly VlatVertexSelectionSession _vertexSelection =
            new VlatVertexSelectionSession();

        private CancellationTokenSource _loadCancellation;
        private Vrm10Instance _avatar;
        private VlatEditorExpressionPreview _expressionPreview;
        private VlatApplier _applier;
        private VlatKeyformPreview _keyformPreview;
        private Transform _head;
        private int _selectedKeyformGroup = 0;
        private readonly double[] _evaluatedYaws = new double[ViewCount];
        private readonly double[,] _keyformWeights = new double[ViewCount, VlatValidator.MaximumKeyformKeys];
        private Matrix4x4 _observedRootMatrix;
        private Matrix4x4 _observedHeadMatrix;
        private Texture[] _vlatTextures = Array.Empty<Texture>();
        private VlatAuthoringDocument _document;
        private long _documentRevision = long.MinValue;
        private Vector3 _frameCenter;
        private bool _evaluationDirty;
        private bool _renderDirty;
        private bool _applicationEnabled = true;
        private bool _applicationEnabledBeforeGeometry = true;
        private bool _geometrySelectionEnabled;
        private float[] _expressionWeightsBeforeGeometry;
        private bool _disposed;
        private long _loadGeneration;
        private double _averageRenderMilliseconds;
        private double _lastRenderMilliseconds;
        private double _lastEvaluationMilliseconds;
        private double _lastRenderOnlyMilliseconds;
        private double _lastRebuildMilliseconds;
        private int _renderCount;
        private int _rendererCount;
        private int _resourceRecreationCount;
        private int _documentInvalidationCount;

        internal VlatPreviewController()
        {
            _previewScene = EditorSceneManager.NewPreviewScene();
            CreateEnvironment();
            _renderDirty = true;
        }

        internal bool HasAvatar => _avatar != null;
        internal bool IsLoading { get; private set; }
        internal string AvatarPath { get; private set; } = string.Empty;
        internal bool ApplicationEnabled => _applicationEnabled;
        internal VlatPreviewState State { get; private set; } = VlatPreviewState.NoAvatar;
        internal MeshBindingResolutionReport BindingReport { get; private set; }
        internal IReadOnlyList<VlatRuntimeDiagnostic> VlatDiagnostics => _vlatDiagnostics;
        internal IReadOnlyList<string> ExpressionNames => _expressionPreview == null
            ? Array.Empty<string>()
            : _expressionPreview.Names;
        internal IReadOnlyList<VlatVertexSelectionTarget> VertexTargets =>
            _vertexSelection.Targets;
        internal int VertexTargetIndex => _vertexSelection.TargetIndex;
        internal bool GeometrySelectionEnabled => _geometrySelectionEnabled;
        internal VlatVertexSelectionTool VertexSelectionTool => _vertexSelection.Tool;
        internal VlatVertexVisibilityMode VertexVisibilityMode => _vertexSelection.VisibilityMode;
        internal float VertexBrushRadius => _vertexSelection.BrushRadius;
        internal float VertexSoftRadius => _vertexSelection.SoftRadius;
        internal VlatSoftSelectionFalloff VertexFalloff => _vertexSelection.Falloff;
        internal string VertexSelectionStatus => _vertexSelection.StatusSummary;
        internal string VertexSelectionDiagnostic => _vertexSelection.Diagnostic;
        internal string VertexSelectionPerformance => _vertexSelection.PerformanceSummary;
        internal string DiagnosticSummary
        {
            get
            {
                var state = GetStateSummary();
                var main = _views[(int)VlatPreviewView.Main];
                state += " / main yaw " + main.Yaw.ToString("F1") +
                    " pitch " + main.Pitch.ToString("F1");
                return _diagnostics.Count == 0
                    ? state
                    : state + " | " + string.Join(" | ", _diagnostics);
            }
        }
        internal string PerformanceSummary =>
            "Refresh: " + _lastRenderMilliseconds.ToString("F2") + " ms" +
            " / avg " + _averageRenderMilliseconds.ToString("F2") + " ms" +
            " / VLat eval " + _lastEvaluationMilliseconds.ToString("F2") + " ms" +
            " / cameras " + _lastRenderOnlyMilliseconds.ToString("F2") + " ms" +
            " / rebuild " + _lastRebuildMilliseconds.ToString("F2") + " ms";
        internal string ResourceSummary =>
            "Preview resources: scene 1 / avatar " + (HasAvatar ? 1 : 0) +
            " / renderers " + _rendererCount +
            " / expressions " + (_expressionPreview == null ? 0 : _expressionPreview.Count) +
            " / cameras " + _cameras.Length +
            " / render textures " + CountRenderTextures() +
            " / RT recreations " + _resourceRecreationCount +
            " / refreshes " + _renderCount;
        internal Scene PreviewScene => _previewScene;
        internal int DocumentInvalidationCount => _documentInvalidationCount;

        internal VlatPreviewViewState GetViewState(VlatPreviewView view)
        {
            return _views[(int)view];
        }

        internal Texture GetTexture(VlatPreviewView view)
        {
            return _targets[(int)view];
        }

        internal Texture GetVertexSelectionOverlay()
        {
            return _vertexSelection.Overlay;
        }

        internal static VlatAuthoringPackResult CreateEvaluationSnapshot(
            VlatAuthoringDocument document)
        {
            return VlatAuthoringPersistence.PackForEditing(document);
        }

        internal void SetKeyformGroupSelection(int index)
        {
            if (_selectedKeyformGroup == index) return;
            CancelKeyformDrag("KeyformGroupが切り替わりました。");
            _selectedKeyformGroup = index;
            _evaluationDirty = true;
            _renderDirty = true;
        }

        internal double GetEvaluatedYaw(VlatPreviewView view) => _evaluatedYaws[(int)view];
        internal double GetKeyformWeight(VlatPreviewView view, int key) => _keyformWeights[(int)view, key];
        internal Camera GetCamera(VlatPreviewView view) => _cameras[(int)view];

        internal bool MoveCameraToKeyform(VlatPreviewView view, int keyIndex)
        {
            if (_document == null || _selectedKeyformGroup < 0 ||
                _selectedKeyformGroup >= _document.KeyformGroups.Count) return false;
            var keys = _document.KeyformGroups[_selectedKeyformGroup].Keys;
            if (keyIndex < 0 || keyIndex >= keys.Count) return false;
            // panを残すと観測Yawがキーからずれる。distanceだけを維持する。
            _views[(int)view].ClearPan();
            SetAngles(view, (float)keys[keyIndex].Yaw, 0f);
            return true;
        }

        internal void SetDocument(VlatAuthoringDocument document)
        {
            RequireNotDisposed();
            var revision = document == null ? long.MinValue : document.Revision;
            if (ReferenceEquals(_document, document) && _documentRevision == revision)
            {
                return;
            }

            _document = document;
            _documentRevision = revision;
            ReleaseEvaluation();
            _diagnostics.Clear();
            if (_avatar != null)
            {
                State = VlatPreviewState.AvatarOnly;
            }

            _evaluationDirty = true;
            _renderDirty = true;
            _documentInvalidationCount++;
            _vertexSelection.InvalidateGeometry();
        }

        internal async Task<bool> LoadAvatarAsync(string path)
        {
            RequireNotDisposed();
            if (string.IsNullOrWhiteSpace(path))
            {
                SetAvatarLoadError("VRM path is empty.");
                return false;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception exception)
            {
                SetAvatarLoadError("VRM path is invalid: " + exception.Message);
                return false;
            }

            if (!File.Exists(fullPath))
            {
                SetAvatarLoadError("VRM file was not found: " + fullPath);
                return false;
            }

            CancelPendingLoad();
            var cancellation = new CancellationTokenSource();
            _loadCancellation = cancellation;
            var generation = ++_loadGeneration;
            IsLoading = true;
            SetStatusDiagnostic("Loading VRM 1.0...");

            Vrm10Instance candidate = null;
            var activeScene = SceneManager.GetActiveScene();
            var activeSceneWasDirty = activeScene.isDirty;
            try
            {
                candidate = await Vrm10.LoadPathAsync(
                    fullPath,
                    canLoadVrm0X: false,
                    controlRigGenerationOption: ControlRigGenerationOption.None,
                    showMeshes: true,
                    awaitCaller: new ImmediateCaller(),
                    ct: cancellation.Token,
                    springboneRuntime: new Vrm10NopSpringboneRuntime());

                if (_disposed || cancellation.IsCancellationRequested || generation != _loadGeneration)
                {
                    DestroyObject(candidate == null ? null : candidate.gameObject);
                    return false;
                }

                if (candidate == null || candidate.Vrm == null ||
                    !UniVrmAdapter.TryGetHead(candidate, out _))
                {
                    DestroyObject(candidate == null ? null : candidate.gameObject);
                    SetAvatarLoadError("VRM 1.0 avatar or Humanoid head could not be resolved.");
                    return false;
                }

                AdoptAvatar(candidate, fullPath);
                candidate = null;
                return true;
            }
            catch (OperationCanceledException)
            {
                DestroyObject(candidate == null ? null : candidate.gameObject);
                if (!_disposed && generation == _loadGeneration)
                {
                    SetStatusDiagnostic("VRM loading was cancelled.");
                }

                return false;
            }
            catch (Exception exception)
            {
                DestroyObject(candidate == null ? null : candidate.gameObject);
                if (!_disposed && generation == _loadGeneration)
                {
                    SetAvatarLoadError("VRM 1.0 could not be loaded: " + exception.Message);
                }

                return false;
            }
            finally
            {
                RestoreCleanSceneState(activeScene, activeSceneWasDirty);
                if (!_disposed && generation == _loadGeneration)
                {
                    IsLoading = false;
                    if (ReferenceEquals(_loadCancellation, cancellation))
                    {
                        _loadCancellation = null;
                    }
                }

                cancellation.Dispose();
            }
        }

        internal void ClearAvatar()
        {
            RequireNotDisposed();
            CancelPendingLoad();
            ReleaseAvatar();
            AvatarPath = string.Empty;
            _diagnostics.Clear();
            State = VlatPreviewState.NoAvatar;
            _renderDirty = true;
        }

        internal void ResizeView(VlatPreviewView view, float width, float height, float pixelsPerPoint)
        {
            RequireNotDisposed();
            var textureWidth = Mathf.Clamp(
                Mathf.RoundToInt(width * pixelsPerPoint),
                MinimumTextureSize,
                MaximumTextureSize);
            var textureHeight = Mathf.Clamp(
                Mathf.RoundToInt(height * pixelsPerPoint),
                MinimumTextureSize,
                MaximumTextureSize);
            var index = (int)view;
            _targetWidths[index] = textureWidth;
            _targetHeights[index] = textureHeight;
            EnsureTarget(index);
        }

        private void EnsureTarget(int index)
        {
            var textureWidth = _targetWidths[index];
            var textureHeight = _targetHeights[index];
            if (textureWidth < MinimumTextureSize || textureHeight < MinimumTextureSize)
            {
                return;
            }

            var current = _targets[index];
            if (current != null && current.width == textureWidth && current.height == textureHeight)
            {
                return;
            }

            CancelKeyformDrag("Previewのサイズが変わりました。");
            ReleaseTarget(index);
            var target = new RenderTexture(textureWidth, textureHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "VLat Preview " + (VlatPreviewView)index,
                antiAliasing = 1,
                hideFlags = HideFlags.HideAndDontSave,
            };
            target.Create();
            _targets[index] = target;
            _cameras[index].targetTexture = target;
            _cameras[index].aspect = textureWidth / (float)textureHeight;
            _resourceRecreationCount++;
            _renderDirty = true;
            if (index == (int)VlatPreviewView.Main)
            {
                _vertexSelection.InvalidateProjection();
            }
        }

        internal void SetAngles(VlatPreviewView view, float yaw, float pitch)
        {
            CancelKeyformDrag();
            GetViewState(view).SetAngles(yaw, pitch);
            _renderDirty = true;
            InvalidateSelectionForCamera(view);
        }

        internal void Orbit(VlatPreviewView view, Vector2 delta)
        {
            CancelKeyformDrag();
            GetViewState(view).Orbit(delta);
            _renderDirty = true;
            InvalidateSelectionForCamera(view);
        }

        internal void Pan(VlatPreviewView view, Vector2 delta)
        {
            CancelKeyformDrag();
            GetViewState(view).PanBy(delta);
            _renderDirty = true;
            InvalidateSelectionForCamera(view);
        }

        internal void Zoom(VlatPreviewView view, float wheelDelta)
        {
            CancelKeyformDrag();
            GetViewState(view).Zoom(wheelDelta);
            _renderDirty = true;
            InvalidateSelectionForCamera(view);
        }

        internal void ResetView(VlatPreviewView view)
        {
            CancelKeyformDrag();
            GetViewState(view).Reset();
            _renderDirty = true;
            InvalidateSelectionForCamera(view);
        }

        internal float GetExpressionWeight(int index)
        {
            if (_expressionPreview == null)
            {
                return 0f;
            }

            return _expressionPreview.GetWeight(index);
        }

        internal void SetExpressionWeight(int index, float weight)
        {
            if (_expressionPreview == null || _geometrySelectionEnabled)
            {
                return;
            }

            _expressionPreview.SetWeight(index, weight);
            _evaluationDirty = true;
            _renderDirty = true;
        }

        internal void ResetExpressions()
        {
            if (_expressionPreview == null || _geometrySelectionEnabled)
            {
                return;
            }

            _expressionPreview.ResetWeights();
            _evaluationDirty = true;
            _renderDirty = true;
        }

        internal void SetApplicationEnabled(bool enabled)
        {
            if (_geometrySelectionEnabled)
            {
                _applicationEnabledBeforeGeometry = enabled;
                return;
            }

            _applicationEnabled = enabled;
            if (_applier != null)
            {
                _applier.ApplicationEnabled = enabled;
            }

            _renderDirty = true;
        }

        internal void SetGeometrySelectionEnabled(bool enabled)
        {
            RequireNotDisposed();
            if (_geometrySelectionEnabled == enabled)
            {
                return;
            }

            CancelKeyformDrag();
            KeyformDragEnabled = false;
            _geometrySelectionEnabled = enabled;
            if (enabled)
            {
                _applicationEnabledBeforeGeometry = _applicationEnabled;
                _applicationEnabled = false;
                if (_applier != null)
                {
                    _applier.ApplicationEnabled = false;
                }

                if (_expressionPreview != null)
                {
                    _expressionWeightsBeforeGeometry = _expressionPreview.CaptureWeights();
                    _expressionPreview.ResetWeights();
                }
            }
            else
            {
                _applicationEnabled = _applicationEnabledBeforeGeometry;
                if (_applier != null)
                {
                    _applier.ApplicationEnabled = _applicationEnabled;
                }

                if (_expressionPreview != null && _expressionWeightsBeforeGeometry != null)
                {
                    _expressionPreview.RestoreWeights(_expressionWeightsBeforeGeometry);
                }

                _expressionWeightsBeforeGeometry = null;
            }

            _vertexSelection.SetEnabled(enabled);
            _vertexSelection.InvalidateGeometry();
            _evaluationDirty = true;
            _renderDirty = true;
        }

        internal void SetVertexSelectionTarget(int index)
        {
            CancelKeyformDrag("選択Rendererが切り替わりました。");
            _vertexSelection.SetTarget(index);
            RefreshVertexSelection();
        }

        internal void SetVertexSelectionTool(VlatVertexSelectionTool tool)
        {
            _vertexSelection.SetTool(tool);
        }

        internal void SetVertexVisibilityMode(VlatVertexVisibilityMode mode)
        {
            _vertexSelection.SetVisibilityMode(mode);
        }

        internal void SetVertexBrushRadius(float radius)
        {
            _vertexSelection.SetBrushRadius(radius);
        }

        internal void SetVertexSoftRadius(float radius)
        {
            _vertexSelection.SetSoftRadius(radius);
        }

        internal void SetVertexFalloff(VlatSoftSelectionFalloff falloff)
        {
            _vertexSelection.SetFalloff(falloff);
        }

        internal void BeginVertexSelection(
            Vector2 screenPosition,
            VlatVertexSelectionOperation operation)
        {
            _vertexSelection.BeginGesture(screenPosition, operation);
        }

        internal void UpdateVertexSelection(Vector2 screenPosition)
        {
            _vertexSelection.UpdateGesture(screenPosition);
        }

        internal void EndVertexSelection(Vector2 screenPosition)
        {
            _vertexSelection.EndGesture(screenPosition);
        }

        internal void CancelVertexSelection()
        {
            _vertexSelection.CancelGesture();
        }

        internal void ClearVertexSelection()
        {
            _vertexSelection.ClearSelection();
        }

        internal bool RenderIfNeeded()
        {
            RequireNotDisposed();
            ObserveFrameChanges();
            if (!_renderDirty || _avatar == null)
            {
                return false;
            }

            if (_drag != null) return RenderDrag();

            var renderStarted = Stopwatch.GetTimestamp();
            var rebuildStarted = renderStarted;
            var evaluationTicks = 0L;
            var renderTicks = 0L;
            var completed = false;
            try
            {
                using var lighting = new VlatPreviewLightingScope(_previewScene);
                EnsureEvaluation();
                // 拒否済みrigを基準形状として描くこともnative errorになるため、描画前に中止する。
                if (State == VlatPreviewState.VLatUnavailable)
                    foreach (var target in _vertexSelection.Targets)
                        if (target.Renderer is SkinnedMeshRenderer skinned && skinned != null && skinned.enabled && !VlatKeyformPreview.HasMatchingBones(skinned))
                            throw new InvalidOperationException("Bone参照とbindposeが不整合です。Avatarを再読込してください。");
                _lastRebuildMilliseconds = TicksToMilliseconds(Stopwatch.GetTimestamp() - rebuildStarted);
                for (var index = 0; index < ViewCount; index++)
                {
                    var target = _targets[index];
                    if (target == null)
                    {
                        continue;
                    }

                    ConfigureCamera(index);
                    var evaluationStart = Stopwatch.GetTimestamp();
                    ApplyVlatAngle(index);
                    evaluationTicks += Stopwatch.GetTimestamp() - evaluationStart;
                    var renderStart = Stopwatch.GetTimestamp();
                    _cameras[index].Render();
                    renderTicks += Stopwatch.GetTimestamp() - renderStart;
                }

                RefreshVertexSelection();
                _renderDirty = false;
                completed = true;
            }
            catch (Exception exception)
            {
                ReleaseEvaluation();
                State = VlatPreviewState.VLatUnavailable;
                SetInfrastructureError("Preview rendering failed: " + exception.Message);
                // 失敗後は基準形状に戻し、次の明示操作/document更新で再描画する。
                _renderDirty = false;
                ClearTargets();
            }
            finally
            {
                var restoreStart = Stopwatch.GetTimestamp();
                try
                {
                    ConfigureCamera((int)VlatPreviewView.Main);
                    ApplyVlatAngle((int)VlatPreviewView.Main);
                }
                catch (Exception exception)
                {
                    ReleaseEvaluation();
                    State = VlatPreviewState.VLatUnavailable;
                    SetInfrastructureError("Preview state restore failed: " + exception.Message);
                    ClearTargets();
                    completed = false;
                }
                evaluationTicks += Stopwatch.GetTimestamp() - restoreStart;
                _lastEvaluationMilliseconds = TicksToMilliseconds(evaluationTicks);
                _lastRenderOnlyMilliseconds = TicksToMilliseconds(renderTicks);
                _lastRenderMilliseconds = TicksToMilliseconds(Stopwatch.GetTimestamp() - renderStarted);
                _renderCount++;
                _averageRenderMilliseconds +=
                    (_lastRenderMilliseconds - _averageRenderMilliseconds) / _renderCount;
            }
            return completed;
        }

        private void ClearTargets()
        {
            var previous = RenderTexture.active;
            try
            {
                for (var i = 0; i < ViewCount; i++)
                {
                    if (_targets[i] == null) continue;
                    RenderTexture.active = _targets[i];
                    GL.Clear(true, true, _cameras[i].backgroundColor);
                }
            }
            finally { RenderTexture.active = previous; }
        }

        /// <summary>Preview Sceneと所有するAvatar・Mesh・Camera・RTを解放します。</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CancelPendingLoad();
            ReleaseAvatar();
            _vertexSelection.Dispose();
            for (var index = 0; index < ViewCount; index++)
            {
                ReleaseTarget(index);
            }

            if (_previewScene.IsValid())
            {
                EditorSceneManager.ClosePreviewScene(_previewScene);
            }
        }

        private void CreateEnvironment()
        {
            for (var index = 0; index < ViewCount; index++)
            {
                var cameraObject = new GameObject("VLat Preview Camera " + index)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                var camera = cameraObject.AddComponent<Camera>();
                camera.cameraType = CameraType.Preview;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1f);
                camera.fieldOfView = 30f;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 1000f;
                camera.enabled = false;
                camera.scene = _previewScene;
                camera.renderingPath = RenderingPath.Forward;
                camera.cullingMask = ~0;
                _cameras[index] = camera;
                SceneManager.MoveGameObjectToScene(cameraObject, _previewScene);
                camera.overrideSceneCullingMask =
                    EditorSceneManager.GetSceneCullingMask(_previewScene);
            }

            var lightObject = new GameObject("VLat Preview Key Light")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(35f, 135f, 0f);
            SceneManager.MoveGameObjectToScene(lightObject, _previewScene);

            var fillObject = new GameObject("VLat Preview Fill Light")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            var fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.55f;
            fill.transform.rotation = Quaternion.Euler(20f, -45f, 0f);
            SceneManager.MoveGameObjectToScene(fillObject, _previewScene);
        }

        internal void AdoptAvatar(Vrm10Instance candidate, string path)
        {
            if (candidate == null)
            {
                throw new ArgumentNullException(nameof(candidate));
            }

            ReleaseAvatar();
            _avatar = candidate;
            _avatar.gameObject.hideFlags = HideFlags.HideAndDontSave;
            _avatar.UpdateType = Vrm10Instance.UpdateTypes.None;
            if (_avatar.gameObject.scene != _previewScene)
            {
                SceneManager.MoveGameObjectToScene(_avatar.gameObject, _previewScene);
            }

            AvatarPath = path;
            _expressionPreview = new VlatEditorExpressionPreview(_avatar);
            _vertexSelection.SetAvatar(_avatar.transform);
            _vertexSelection.SetEnabled(_geometrySelectionEnabled);
            if (_geometrySelectionEnabled)
            {
                _expressionWeightsBeforeGeometry = _expressionPreview.CaptureWeights();
                _expressionPreview.ResetWeights();
            }
            State = VlatPreviewState.AvatarOnly;
            FrameAvatar();
            for (var index = 0; index < ViewCount; index++)
            {
                EnsureTarget(index);
            }

            _evaluationDirty = true;
            _renderDirty = true;
            _diagnostics.Clear();
            RenderIfNeeded();
        }

        private void FrameAvatar()
        {
            var renderers = _avatar.GetComponentsInChildren<Renderer>(true);
            _rendererCount = renderers.Length;
            var hasBounds = false;
            var bounds = new Bounds(_avatar.transform.position, Vector3.one);
            for (var index = 0; index < renderers.Length; index++)
            {
                if (!renderers[index].enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderers[index].bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[index].bounds);
                }
            }

            _frameCenter = hasBounds ? bounds.center : _avatar.transform.position + Vector3.up;
            var radius = hasBounds ? Mathf.Max(bounds.extents.magnitude, 0.1f) : 1f;
            var distance = radius / Mathf.Tan(15f * Mathf.Deg2Rad) * 1.05f;
            for (var index = 0; index < ViewCount; index++)
            {
                _views[index].SetFramingDistance(distance);
            }
        }

        private void ConfigureCamera(int index)
        {
            var view = _views[index];
            var keyformFrame = _document != null && _document.KeyformGroups.Count != 0 && _head != null;
            var rotation = keyformFrame ? _head.rotation : Quaternion.identity;
            var target = (keyformFrame ? _head.position : _frameCenter) +
                rotation * new Vector3(view.Pan.x, view.Pan.y, 0f);
            var orbit = Quaternion.Euler(-view.Pitch, view.Yaw, 0f);
            var cameraTransform = _cameras[index].transform;
            cameraTransform.position = target + rotation * (orbit * new Vector3(0f, 0f, view.Distance));
            cameraTransform.rotation = Quaternion.LookRotation(target - cameraTransform.position, rotation * Vector3.up);
        }

        private void EnsureEvaluation()
        {
            if (!_evaluationDirty)
            {
                return;
            }

            using var phase = VlatEditorProfiling.Measure(VlatEditorPhase.PreviewEvaluate);
            ReleaseEvaluation();
            _evaluationDirty = false;
            _diagnostics.Clear();
            if (_expressionPreview != null)
            {
                _expressionPreview.Apply();
            }

            if (_avatar == null)
            {
                State = VlatPreviewState.NoAvatar;
                return;
            }

            if (_document == null)
            {
                State = VlatPreviewState.AvatarOnly;
                return;
            }

            var pack = CreateEvaluationSnapshot(_document);
            if (!pack.Success)
            {
                State = VlatPreviewState.VLatUnavailable;
                AddPackDiagnostics(pack.Diagnostics);
                return;
            }

            try
            {
                using (VlatEditorProfiling.Measure(VlatEditorPhase.PreviewResolve))
                    BindingReport = UniVrmAdapter.Resolve(_avatar.transform, pack.Document);
                _vertexSelection.SetBindingReport(BindingReport);
            }
            catch (Exception exception)
            {
                BindingReport = null;
                _vertexSelection.SetBindingReport(null);
                _diagnostics.Add("VLat selection binding unavailable: " + exception.Message);
            }

            UniVrmAdapter.TryGetHead(_avatar, out _head);
            if (pack.Document.KeyformGroups.Count != 0)
            {
                if (_selectedKeyformGroup < 0)
                {
                    State = VlatPreviewState.AvatarOnly;
                    _diagnostics.Add("KeyformGroupを選択すると補正をプレビューします。");
                    return;
                }
                try
                {
                    if (_head == null) throw new ArgumentException("Humanoid Headが見つかりません。");
                    if (_selectedKeyformGroup >= pack.Document.KeyformGroups.Count || BindingReport == null)
                        throw new ArgumentException("KeyformGroupまたはMeshBindingが見つかりません。");
                    var group = _document.KeyformGroups[_selectedKeyformGroup];
                    using (VlatEditorProfiling.Measure(VlatEditorPhase.PreviewKeyformBuild))
                    _keyformPreview = new VlatKeyformPreview(pack, _avatar.transform, group,
                        _document.Metadata.MeshBindings[group.MeshBinding], BindingReport);
                    State = VlatPreviewState.AvatarWithVLat;
                    _diagnostics.Add("Editor CPU Keyform Preview / bind-mesh-local / position only / GPUとの等価性は未検証。");
                }
                catch (Exception exception)
                {
                    State = VlatPreviewState.VLatUnavailable;
                    _diagnostics.Add("Keyform Preview Binding不正: " + exception.Message);
                }
                return;
            }

            if (pack.Document.Layers.Count == 0)
            {
                State = VlatPreviewState.AvatarOnly;
                _diagnostics.Add(
                    "VLat not applicable: document has no layers; baseline avatar preview continues.");
                return;
            }

            if (!UniVrmAdapter.TryGetHead(_avatar, out var head))
            {
                State = VlatPreviewState.AvatarLoadError;
                SetInfrastructureError("VRM 1.0 Humanoid head could not be resolved during preview evaluation.");
                return;
            }

            _vlatTextures = LoadTextures(_document);
            _applier = _avatar.gameObject.AddComponent<VlatApplier>();
            _applier.hideFlags = HideFlags.HideAndDontSave;
            _applier.DebugAngleOverrideEnabled = true;
            var result = _applier.Initialize(
                _avatar.transform,
                head,
                _cameras[(int)VlatPreviewView.Main],
                pack.Document,
                pack.Buffers,
                _vlatTextures);
            _applier.ApplicationEnabled = _applicationEnabled;
            BindingReport = result.BindingReport;
            _vertexSelection.SetBindingReport(BindingReport);
            for (var index = 0; index < result.Diagnostics.Count; index++)
            {
                var diagnostic = result.Diagnostics[index];
                _vlatDiagnostics.Add(diagnostic);
                _diagnostics.Add(
                    (result.Success ? "VLat diagnostic " : "VLat not applicable ") +
                    diagnostic.Path + ": " + diagnostic.Message);
            }

            if (!result.Success)
            {
                State = VlatPreviewState.VLatUnavailable;
                DestroyObject(_applier);
                _applier = null;
                return;
            }

            State = VlatPreviewState.AvatarWithVLat;
        }

        private Texture[] LoadTextures(VlatAuthoringDocument document)
        {
            var textures = new Texture[document.Textures.Count];
            for (var index = 0; index < document.Textures.Count; index++)
            {
                var definition = document.Textures[index];
                try
                {
                    byte[] bytes;
                    if (definition.IsEmbedded)
                    {
                        bytes = definition.EmbeddedBytes;
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(document.SourcePath))
                        {
                            throw new InvalidDataException("External texture requires a saved document path.");
                        }

                        var baseUri = new Uri(Path.GetFullPath(document.SourcePath));
                        var textureUri = new Uri(baseUri, definition.Uri);
                        if (!textureUri.IsFile)
                        {
                            throw new InvalidDataException("External texture must resolve to a file URI.");
                        }

                        bytes = File.ReadAllBytes(textureUri.LocalPath);
                    }

                    if (bytes == null || bytes.Length == 0)
                    {
                        throw new InvalidDataException("Texture bytes are empty.");
                    }

                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                    {
                        name = string.IsNullOrWhiteSpace(definition.Name)
                            ? "VLat Preview Texture " + index
                            : definition.Name,
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                    if (!ImageConversion.LoadImage(texture, bytes, true))
                    {
                        DestroyObject(texture);
                        throw new InvalidDataException("Texture image format is unsupported.");
                    }

                    textures[index] = texture;
                }
                catch (Exception exception)
                {
                    _diagnostics.Add("VLat preview texture[" + index + "]: " + exception.Message);
                }
            }

            return textures;
        }

        private void ObserveFrameChanges()
        {
            if (_avatar == null) return;
            if (_head == null) UniVrmAdapter.TryGetHead(_avatar, out _head);
            var rootMatrix = _avatar.transform.localToWorldMatrix;
            var headMatrix = _head == null ? Matrix4x4.identity : _head.localToWorldMatrix;
            if (rootMatrix != _observedRootMatrix || headMatrix != _observedHeadMatrix ||
                (_keyformPreview != null && !_keyformPreview.IsAvailable))
            {
                _observedRootMatrix = rootMatrix;
                _observedHeadMatrix = headMatrix;
                _renderDirty = true;
                _vertexSelection.InvalidateProjection();
            }
        }

        private void ApplyVlatAngle(int index)
        {
            if (_keyformPreview != null)
            {
                if (_head == null) throw new InvalidOperationException("Humanoid Headが失われました。");
                var local = _head.InverseTransformPoint(_cameras[index].transform.position);
                if (!AngleEvaluator.TryCalculateAngles(new Float3(local.x, local.y, local.z), out var angles))
                    throw new InvalidOperationException("カメラのHead-local方向を評価できません。");
                _evaluatedYaws[index] = angles.Yaw;
                if (KeyformDragEnabled && _geometrySelectionEnabled && index == 0 && _editingKey >= 0)
                    _keyformPreview.ApplyKey(_editingKey);
                else _keyformPreview.Apply(angles.Yaw, _applicationEnabled || KeyformDragEnabled);
                for (var key = 0; key < _keyformPreview.KeyCount; key++)
                    _keyformWeights[index, key] = _keyformPreview.GetWeight(key);
            }
            if (_applier == null || !_applier.IsInitialized) return;
            _applier.DebugYaw = _views[index].Yaw;
            _applier.DebugPitch = _views[index].Pitch;
            _applier.EvaluateNow();
        }

        private void AddPackDiagnostics(IReadOnlyList<VlatDiagnostic> diagnostics)
        {
            for (var index = 0; index < diagnostics.Count; index++)
            {
                var diagnostic = diagnostics[index];
                _diagnostics.Add(
                    "VLat not applicable " + diagnostic.Path + ": " + diagnostic.Message);
            }
        }

        private void ReleaseAvatar()
        {
            ReleaseEvaluation();
            if (_expressionPreview != null)
            {
                _expressionPreview.Dispose();
                _expressionPreview = null;
            }

            _expressionWeightsBeforeGeometry = null;
            _vertexSelection.SetAvatar(null);

            _rendererCount = 0;
            _head = null;
            if (_avatar != null)
            {
                DestroyObject(_avatar.gameObject);
                _avatar = null;
            }

            State = VlatPreviewState.NoAvatar;

            for (var index = 0; index < ViewCount; index++)
            {
                ReleaseTarget(index);
            }
        }

        private void ReleaseEvaluation()
        {
            CancelKeyformDrag();
            _keyformPreview?.Dispose();
            _keyformPreview = null;
            Array.Clear(_keyformWeights, 0, _keyformWeights.Length);
            Array.Clear(_evaluatedYaws, 0, _evaluatedYaws.Length);
            if (_applier != null)
            {
                DestroyObject(_applier);
                _applier = null;
            }

            for (var index = 0; index < _vlatTextures.Length; index++)
            {
                DestroyObject(_vlatTextures[index]);
            }

            _vlatTextures = Array.Empty<Texture>();
            BindingReport = null;
            _vertexSelection.SetBindingReport(null);
            _vertexSelection.InvalidateGeometry();
            _vlatDiagnostics.Clear();
        }

        private void RefreshVertexSelection()
        {
            if (!_geometrySelectionEnabled || KeyformDragEnabled || _avatar == null)
            {
                return;
            }

            var index = (int)VlatPreviewView.Main;
            if (_targetWidths[index] <= 0 || _targetHeights[index] <= 0)
            {
                return;
            }

            ConfigureCamera(index);
            _vertexSelection.Refresh(
                _cameras[index],
                _targetWidths[index],
                _targetHeights[index]);
        }

        private void InvalidateSelectionForCamera(VlatPreviewView view)
        {
            if (view == VlatPreviewView.Main)
            {
                _vertexSelection.InvalidateProjection();
            }
        }

        private void ReleaseTarget(int index)
        {
            var target = _targets[index];
            if (target == null)
            {
                return;
            }

            if (_cameras[index] != null)
            {
                _cameras[index].targetTexture = null;
            }

            target.Release();
            DestroyObject(target);
            _targets[index] = null;
        }

        private int CountRenderTextures()
        {
            var count = 0;
            for (var index = 0; index < _targets.Length; index++)
            {
                if (_targets[index] != null)
                {
                    count++;
                }
            }

            return count;
        }

        private string GetStateSummary()
        {
            switch (State)
            {
                case VlatPreviewState.AvatarOnly:
                    return "Avatar loaded / VLat not applied";
                case VlatPreviewState.AvatarWithVLat:
                    if (KeyformDragEnabled && _keyformPreview != null)
                        return "Keyform 2D編集 / Mainは選択キー固定 / Sub A・Bは確定済みYaw補間 / 表情は中立";
                    if (!_applicationEnabled)
                    {
                        return "Avatar loaded / VLat applied / Before (application disabled)";
                    }

                    if (_keyformPreview != null)
                        return "Keyform 補間 / yaw-smoothstep / " + GetKeyformBlendSummary(0) +
                            " / " + GetKeyformBlendSummary(1) + " / " + GetKeyformBlendSummary(2);
                    return _applier != null && _applier.IsInitialized
                        ? "Avatar loaded / VLat applied / active bands " + _applier.ActiveBandCount +
                            " / applied modifiers " + _applier.AppliedModifierCount
                        : "Avatar loaded / VLat applied";
                case VlatPreviewState.VLatUnavailable:
                    return "Avatar loaded / VLat not applicable / rebind required";
                case VlatPreviewState.AvatarLoadError:
                    return "Avatar load error";
                default:
                    return "No avatar loaded";
            }
        }

        // UIのevent-driven refresh時だけ文字列化し、補間/Camera render中は数値bufferだけを更新する。
        private string GetKeyformBlendSummary(int view)
        {
            var summary = ((VlatPreviewView)view) + " Head yaw " + _evaluatedYaws[view].ToString("F1") + "°";
            var sum = 0d;
            for (var key = 0; key < _keyformPreview.KeyCount; key++)
            {
                var weight = _keyformWeights[view, key];
                sum += weight;
                if (weight > 0d) summary += " k" + key + "=" + weight.ToString("F3");
            }
            return summary + " base=" + Math.Max(0d, 1d - sum).ToString("F3");
        }

        private void SetStatusDiagnostic(string message)
        {
            _diagnostics.Clear();
            _diagnostics.Add(message);
            _renderDirty = true;
        }

        private void SetAvatarLoadError(string message)
        {
            State = VlatPreviewState.AvatarLoadError;
            SetInfrastructureError(message);
        }

        private void SetInfrastructureError(string message)
        {
            _diagnostics.Clear();
            _diagnostics.Add("ERROR: " + message);
            _renderDirty = true;
        }

        private void CancelPendingLoad()
        {
            _loadGeneration++;
            if (_loadCancellation != null)
            {
                _loadCancellation.Cancel();
                _loadCancellation = null;
            }

            IsLoading = false;
        }

        private void RequireNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(VlatPreviewController));
            }
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

        private static void RestoreCleanSceneState(Scene scene, bool wasDirty)
        {
            if (wasDirty || !scene.IsValid() || !scene.isDirty || ClearSceneDirtinessMethod == null)
            {
                return;
            }

            ClearSceneDirtinessMethod.Invoke(null, new object[] { scene });
        }

        private static double TicksToMilliseconds(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }
    }
}
