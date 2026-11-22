using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using UniVRM10;
using UnityEngine;
using VLat.Core.Evaluation;
using VLat.Core.Model;
using VLat.Core.Serialization;
using VLat.Core.Validation;

namespace VLat.Runtime
{
    /// <summary>VLatランタイムの初期化結果を表します。</summary>
    public sealed class VlatRuntimeInitializationResult
    {
        private readonly ReadOnlyCollection<VlatRuntimeDiagnostic> _diagnostics;

        internal VlatRuntimeInitializationResult(
            bool success,
            MeshBindingResolutionReport bindingReport,
            IList<VlatRuntimeDiagnostic> diagnostics)
        {
            Success = success;
            BindingReport = bindingReport;
            var copy = diagnostics == null
                ? Array.Empty<VlatRuntimeDiagnostic>()
                : CopyDiagnostics(diagnostics);
            _diagnostics = new ReadOnlyCollection<VlatRuntimeDiagnostic>(copy);
        }

        /// <summary>少なくとも1つの有効Layerを持つ評価ループを初期化できたかどうかを取得します。</summary>
        public bool Success { get; }

        /// <summary>MeshBindingの解決レポートを取得します。解決前に失敗した場合はnullです。</summary>
        public MeshBindingResolutionReport BindingReport { get; }

        /// <summary>初期化中に得られた診断を取得します。</summary>
        public IReadOnlyList<VlatRuntimeDiagnostic> Diagnostics => _diagnostics;

        private static VlatRuntimeDiagnostic[] CopyDiagnostics(IList<VlatRuntimeDiagnostic> source)
        {
            var copy = new VlatRuntimeDiagnostic[source.Count];
            for (var index = 0; index < source.Count; index++)
            {
                copy[index] = source[index];
            }

            return copy;
        }
    }

    /// <summary>
    /// 評価カメラの頭部ローカル角度に応じてVLat差分を非破壊で適用します。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(11010)]
    public sealed class VlatApplier : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField]
        private Vrm10Instance _vrmInstance;

        [SerializeField]
        private Camera _evaluationCamera;

        [SerializeField]
        [Tooltip("絶対パス、またはStreamingAssetsからの相対.vlat.jsonパス")]
        private string _documentPath = string.Empty;

        [SerializeField]
        private bool _loadOnStart;

        [Header("Application")]
        [SerializeField]
        private bool _applicationEnabled = true;

        [Header("Debug")]
        [SerializeField]
        private bool _debugAngleOverrideEnabled;

        [SerializeField]
        [Range(-180f, 180f)]
        private float _debugYaw;

        [SerializeField]
        [Range(-90f, 90f)]
        private float _debugPitch;

        [SerializeField]
        private float _currentYaw;

        [SerializeField]
        private float _currentPitch;

        [SerializeField]
        private int _activeBandCount;

        [SerializeField]
        private int _appliedModifierCount;

        private Transform _head;
        private VlatDocument _document;
        private AngleEvaluator _angleEvaluator;
        private ConflictResolver _conflictResolver;
        private ResolvedState _resolvedState;
        private double[] _bandWeights = Array.Empty<double>();
        private DynamicBlendShapeBinder _blendShapeBinder;
        private MaterialOverrideBinder _materialBinder;
        private RuntimeTextureSet _textureSet;
        private VertexApplication[] _vertexApplications = Array.Empty<VertexApplication>();
        private Dictionary<VertexApplicationKey, int> _vertexApplicationIndices;
        private float[] _desiredVertexWeights = Array.Empty<float>();
        private VlatRuntimeDiagnostic[] _diagnostics = Array.Empty<VlatRuntimeDiagnostic>();
        private MeshBindingResolutionReport _bindingReport;
        private bool _initialized;

        /// <summary>VLat補正を適用するかどうかを取得または設定します。</summary>
        /// <remarks>falseへ変更すると、生成済みリソースを維持したまま基準状態へ即時に戻します。</remarks>
        public bool ApplicationEnabled
        {
            get => _applicationEnabled;
            set
            {
                if (_applicationEnabled == value)
                {
                    return;
                }

                _applicationEnabled = value;
                if (!_initialized)
                {
                    return;
                }

                if (value && isActiveAndEnabled)
                {
                    EvaluateNow();
                }
                else
                {
                    ResetApplicationState();
                }
            }
        }

        /// <summary>デバッグ角度で評価カメラの角度を上書きするかどうかを取得または設定します。</summary>
        public bool DebugAngleOverrideEnabled
        {
            get => _debugAngleOverrideEnabled;
            set => _debugAngleOverrideEnabled = value;
        }

        /// <summary>デバッグ上書き用yaw角度を度数で取得または設定します。</summary>
        public float DebugYaw
        {
            get => _debugYaw;
            set => _debugYaw = Mathf.Clamp(value, -180f, 180f);
        }

        /// <summary>デバッグ上書き用pitch角度を度数で取得または設定します。</summary>
        public float DebugPitch
        {
            get => _debugPitch;
            set => _debugPitch = Mathf.Clamp(value, -90f, 90f);
        }

        /// <summary>最後に評価したyaw角度を度数で取得します。</summary>
        public float CurrentYaw => _currentYaw;

        /// <summary>最後に重みが0より大きくなったAngleBand数を取得します。</summary>
        public int ActiveBandCount => _activeBandCount;

        /// <summary>最後に競合解決され適用対象となったModifier数を取得します。</summary>
        public int AppliedModifierCount => _appliedModifierCount;

        /// <summary>最後に評価したpitch角度を度数で取得します。</summary>
        public float CurrentPitch => _currentPitch;

        /// <summary>評価ループが初期化済みかどうかを取得します。</summary>
        public bool IsInitialized => _initialized;

        /// <summary>最後の初期化で得られた診断を取得します。</summary>
        public IReadOnlyList<VlatRuntimeDiagnostic> Diagnostics => _diagnostics;

        /// <summary>最後の初期化で得られたMeshBinding解決レポートを取得します。</summary>
        public MeshBindingResolutionReport BindingReport => _bindingReport;

        /// <summary>VRM 1.0インスタンスとファイルから評価ループを初期化します。</summary>
        /// <param name="instance">UniVRMでロード済みのVRM 1.0インスタンス。</param>
        /// <param name="evaluationCamera">角度算出に使用する単一カメラ。</param>
        /// <param name="jsonPath">読み込む.vlat.jsonのパス。</param>
        /// <returns>再バインド結果と診断を含む初期化結果。</returns>
        public VlatRuntimeInitializationResult InitializeFromFile(
            Vrm10Instance instance,
            Camera evaluationCamera,
            string jsonPath)
        {
            DisposeRuntime();
            var diagnostics = new List<VlatRuntimeDiagnostic>();
            if (string.IsNullOrWhiteSpace(jsonPath))
            {
                diagnostics.Add(Error("$", "jsonPathは空にできません。"));
                return CompleteFailedInitialization(diagnostics, null);
            }

            var loadResult = VlatSerializer.Load(jsonPath);
            CopyCoreDiagnostics(loadResult.Diagnostics, diagnostics);
            if (!loadResult.Success || loadResult.Document == null)
            {
                return CompleteFailedInitialization(diagnostics, null);
            }

            var textureSet = RuntimeTextureSet.Load(
                loadResult.Document,
                loadResult.Buffers,
                jsonPath,
                diagnostics);
            if (ContainsError(diagnostics))
            {
                textureSet.Dispose();
                return CompleteFailedInitialization(diagnostics, null);
            }

            return InitializeVrmInternal(
                instance,
                evaluationCamera,
                loadResult.Document,
                loadResult.Buffers,
                textureSet,
                diagnostics,
                true);
        }

        /// <summary>VRM 1.0インスタンスとデシリアライズ済みドキュメントから評価ループを初期化します。</summary>
        /// <param name="instance">UniVRMでロード済みのVRM 1.0インスタンス。</param>
        /// <param name="evaluationCamera">角度算出に使用する単一カメラ。</param>
        /// <param name="document">検証対象のVLatドキュメント。</param>
        /// <param name="binaryBuffers">document.buffersと同じ順序のバイナリ内容。</param>
        /// <param name="textures">document.texturesと同じ順序の呼び出し側所有Texture。</param>
        /// <returns>再バインド結果と診断を含む初期化結果。</returns>
        public VlatRuntimeInitializationResult Initialize(
            Vrm10Instance instance,
            Camera evaluationCamera,
            VlatDocument document,
            IReadOnlyList<byte[]> binaryBuffers,
            Texture[] textures = null)
        {
            DisposeRuntime();
            return InitializeVrmInternal(
                instance,
                evaluationCamera,
                document,
                binaryBuffers,
                RuntimeTextureSet.FromCaller(textures),
                new List<VlatRuntimeDiagnostic>(),
                false);
        }

        /// <summary>任意のアバタールート、頭部Transform、カメラから評価ループを初期化します。</summary>
        /// <remarks>UniVRMに依存しないランタイム生成アバターや自動テストで使用できます。</remarks>
        /// <param name="avatarRoot">MeshBindingのヒエラルキーパス基準となるルート。</param>
        /// <param name="head">評価カメラ位置をローカル化する頭部Transform。</param>
        /// <param name="evaluationCamera">角度算出に使用する単一カメラ。</param>
        /// <param name="document">検証対象のVLatドキュメント。</param>
        /// <param name="binaryBuffers">document.buffersと同じ順序のバイナリ内容。</param>
        /// <param name="textures">document.texturesと同じ順序の呼び出し側所有Texture。</param>
        /// <returns>再バインド結果と診断を含む初期化結果。</returns>
        public VlatRuntimeInitializationResult Initialize(
            Transform avatarRoot,
            Transform head,
            Camera evaluationCamera,
            VlatDocument document,
            IReadOnlyList<byte[]> binaryBuffers,
            Texture[] textures = null)
        {
            DisposeRuntime();
            var textureSet = RuntimeTextureSet.FromCaller(textures);
            return InitializeInternal(
                avatarRoot,
                head,
                evaluationCamera,
                document,
                binaryBuffers,
                textureSet,
                new List<VlatRuntimeDiagnostic>(),
                false,
                null);
        }

        /// <summary>現在のカメラ位置またはデバッグ角度で1回評価し、変化した状態だけを書き込みます。</summary>
        /// <returns>評価と適用を実行できたときは<c>true</c>。</returns>
        public bool EvaluateNow()
        {
            if (!_initialized || !_applicationEnabled || !isActiveAndEnabled)
            {
                return false;
            }

            ViewAngles angles;
            if (_debugAngleOverrideEnabled)
            {
                angles = new ViewAngles(_debugYaw, _debugPitch);
            }
            else
            {
                if (_head == null || _evaluationCamera == null)
                {
                    ResetApplicationState();
                    return false;
                }

                var localPosition = _head.InverseTransformPoint(_evaluationCamera.transform.position);
                if (!AngleEvaluator.TryCalculateAngles(
                        new Float3(localPosition.x, localPosition.y, localPosition.z),
                        out angles))
                {
                    ResetApplicationState();
                    return false;
                }
            }

            _currentYaw = (float)angles.Yaw;
            _currentPitch = (float)angles.Pitch;
            _activeBandCount = 0;
            for (var bandIndex = 0; bandIndex < _document.AngleBands.Count; bandIndex++)
            {
                var weight = AngleEvaluator.EvaluateBand(
                    _document.AngleBands[bandIndex],
                    angles.Yaw,
                    angles.Pitch);
                _bandWeights[bandIndex] = weight;
                if (weight > 0d)
                {
                    _activeBandCount++;
                }
            }

            _conflictResolver.Resolve(angles, _bandWeights, _resolvedState);
            ApplyVertexDeltas(_resolvedState);
            _materialBinder.Apply(_resolvedState);
            _appliedModifierCount = _resolvedState.Count;
            return true;
        }

        private void Start()
        {
            if (!_loadOnStart || _initialized)
            {
                return;
            }

            var path = _documentPath;
            if (!string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path))
            {
                path = Path.Combine(Application.streamingAssetsPath, path);
            }

            var result = InitializeFromFile(_vrmInstance, _evaluationCamera, path);
            LogDiagnostics(result.Diagnostics);
        }

        private void LateUpdate()
        {
            EvaluateNow();
        }

        private void OnEnable()
        {
            if (_initialized && _applicationEnabled)
            {
                EvaluateNow();
            }
        }

        private void OnDisable()
        {
            if (_initialized)
            {
                ResetApplicationState();
            }
        }

        private void OnDestroy()
        {
            DisposeRuntime();
        }

        private VlatRuntimeInitializationResult InitializeVrmInternal(
            Vrm10Instance instance,
            Camera evaluationCamera,
            VlatDocument document,
            IReadOnlyList<byte[]> binaryBuffers,
            RuntimeTextureSet textureSet,
            List<VlatRuntimeDiagnostic> diagnostics,
            bool alreadyValidated)
        {
            _vrmInstance = instance;
            _evaluationCamera = evaluationCamera;

            if (instance == null)
            {
                textureSet.Dispose();
                diagnostics.Add(Error("$", "Vrm10Instanceは必須です。"));
                return CompleteFailedInitialization(diagnostics, null);
            }

            if (!UniVrmAdapter.TryGetHead(instance, out var head))
            {
                textureSet.Dispose();
                diagnostics.Add(Error("$", "VRM 1.0 Humanoidのhead Transformを取得できません。"));
                return CompleteFailedInitialization(diagnostics, null);
            }

            if (!instance.isActiveAndEnabled)
            {
                diagnostics.Add(Warning("$", "Vrm10Instanceが無効なためExpressionの自動更新は実行されません。"));
            }
            else if (instance.UpdateType == Vrm10Instance.UpdateTypes.None)
            {
                diagnostics.Add(Warning("$", "Vrm10Instance.UpdateTypeがNoneです。Kakudollより前にVRMを手動更新してください。"));
            }

            return InitializeInternal(
                instance.transform,
                head,
                evaluationCamera,
                document,
                binaryBuffers,
                textureSet,
                diagnostics,
                alreadyValidated,
                instance);
        }

        private VlatRuntimeInitializationResult InitializeInternal(
            Transform avatarRoot,
            Transform head,
            Camera evaluationCamera,
            VlatDocument document,
            IReadOnlyList<byte[]> binaryBuffers,
            RuntimeTextureSet textureSet,
            List<VlatRuntimeDiagnostic> diagnostics,
            bool alreadyValidated,
            Vrm10Instance instance)
        {
            if (avatarRoot == null || head == null || evaluationCamera == null || document == null)
            {
                textureSet.Dispose();
                diagnostics.Add(Error(
                    "$",
                    "avatarRoot、head、evaluationCamera、documentは必須です。"));
                return CompleteFailedInitialization(diagnostics, null);
            }

            if (document.KeyformGroups.Count != 0)
            {
                textureSet.Dispose();
                diagnostics.Add(Error("$.keyformGroups",
                    "このCPU RuntimeはKeyform再生に未対応です。Core参照評価器を使用してください。"));
                return CompleteFailedInitialization(diagnostics, null);
            }

            if (textureSet.Textures.Length != document.Textures.Count)
            {
                textureSet.Dispose();
                diagnostics.Add(Error(
                    "$.textures",
                    "texturesはdocument.texturesと同じ要素数である必要があります。"));
                return CompleteFailedInitialization(diagnostics, null);
            }

            var buffers = binaryBuffers ?? Array.Empty<byte[]>();
            if (!alreadyValidated)
            {
                CopyCoreDiagnostics(VlatValidator.Validate(document, buffers), diagnostics);
                if (ContainsError(diagnostics))
                {
                    textureSet.Dispose();
                    return CompleteFailedInitialization(diagnostics, null);
                }
            }

            MeshBindingResolutionReport report;
            try
            {
                report = UniVrmAdapter.Resolve(avatarRoot, document);
            }
            catch (Exception exception)
            {
                textureSet.Dispose();
                diagnostics.Add(Error("$.vlat.targetAvatar.meshBindings", exception.Message));
                return CompleteFailedInitialization(diagnostics, null);
            }

            var renderers = new Renderer[report.Count];
            for (var bindingIndex = 0; bindingIndex < report.Count; bindingIndex++)
            {
                var resolution = report[bindingIndex];
                if (resolution.IsSuccess)
                {
                    renderers[bindingIndex] = resolution.Renderer;
                    if (resolution.Stage != MeshBindingMatchStage.ExactHierarchyPath)
                    {
                        diagnostics.Add(Warning(
                            "$.vlat.targetAvatar.meshBindings[" + bindingIndex + "]",
                            resolution.Message));
                    }
                }
                else
                {
                    diagnostics.Add(Warning(
                        "$.vlat.targetAvatar.meshBindings[" + bindingIndex + "]",
                        resolution.Message + " 参照するLayerを無効化します。"));
                }
            }

            var layerIsValid = InspectLayerTargets(document, report, renderers, diagnostics);
            var blendShapeBinder = new DynamicBlendShapeBinder();
            var vertexApplications = BindVertexDeltas(
                document,
                buffers,
                report,
                layerIsValid,
                blendShapeBinder,
                diagnostics);
            var activeDocument = CreateActiveDocument(document, layerIsValid);
            if (activeDocument.Layers.Count == 0)
            {
                blendShapeBinder.Dispose();
                textureSet.Dispose();
                diagnostics.Add(Error("$.layers", "再バインド後に有効なLayerがありません。"));
                return CompleteFailedInitialization(diagnostics, report);
            }

            var materialBinder = new MaterialOverrideBinder();
            MaterialOverrideInitializationResult materialResult;
            try
            {
                materialResult = materialBinder.Initialize(activeDocument, renderers, textureSet.Textures);
            }
            catch (Exception exception)
            {
                materialBinder.Dispose();
                blendShapeBinder.Dispose();
                textureSet.Dispose();
                diagnostics.Add(Error("$.layers", "マテリアル補正の初期化に失敗しました: " + exception.Message));
                return CompleteFailedInitialization(diagnostics, report);
            }

            CopyRuntimeDiagnostics(materialResult.Diagnostics, diagnostics);
            if (!materialResult.Success)
            {
                materialBinder.Dispose();
                blendShapeBinder.Dispose();
                textureSet.Dispose();
                return CompleteFailedInitialization(diagnostics, report);
            }

            _vrmInstance = instance;
            _head = head;
            _evaluationCamera = evaluationCamera;
            _document = activeDocument;
            _angleEvaluator = new AngleEvaluator(activeDocument.AngleBands);
            _conflictResolver = new ConflictResolver(activeDocument);
            _resolvedState = _conflictResolver.CreateState();
            _bandWeights = new double[_angleEvaluator.BandCount];
            _blendShapeBinder = blendShapeBinder;
            _materialBinder = materialBinder;
            _textureSet = textureSet;
            _bindingReport = report;
            _vertexApplications = vertexApplications;
            _desiredVertexWeights = new float[vertexApplications.Length];
            _vertexApplicationIndices = new Dictionary<VertexApplicationKey, int>(vertexApplications.Length);
            for (var index = 0; index < vertexApplications.Length; index++)
            {
                var application = vertexApplications[index];
                _vertexApplicationIndices.Add(
                    new VertexApplicationKey(application.Layer, application.Modifier),
                    index);
            }

            _initialized = true;
            _diagnostics = ToArray(diagnostics);
            if (_applicationEnabled && isActiveAndEnabled)
            {
                EvaluateNow();
            }
            else
            {
                ResetApplicationState();
            }

            return new VlatRuntimeInitializationResult(true, report, diagnostics);
        }

        private static bool[] InspectLayerTargets(
            VlatDocument document,
            MeshBindingResolutionReport report,
            Renderer[] renderers,
            List<VlatRuntimeDiagnostic> diagnostics)
        {
            var layerIsValid = new bool[document.Layers.Count];
            for (var layerIndex = 0; layerIndex < document.Layers.Count; layerIndex++)
            {
                var layer = document.Layers[layerIndex];
                var valid = layer != null && layer.Enabled;
                if (valid)
                {
                    for (var modifierIndex = 0; modifierIndex < layer.Modifiers.Count; modifierIndex++)
                    {
                        var modifier = layer.Modifiers[modifierIndex];
                        var bindingIndex = GetMeshBindingIndex(modifier);
                        if (bindingIndex < 0 || bindingIndex >= report.Count || !report[bindingIndex].IsSuccess)
                        {
                            valid = false;
                            break;
                        }

                        var renderer = renderers[bindingIndex];
                        if (modifier is VertexDeltaModifier && !(renderer is SkinnedMeshRenderer))
                        {
                            valid = false;
                            break;
                        }

                        if (modifier is VisibilityModifier visibility)
                        {
                            var mesh = report[bindingIndex].Mesh;
                            if (mesh == null || visibility.SubMesh < 0 || visibility.SubMesh >= mesh.subMeshCount)
                            {
                                valid = false;
                                break;
                            }

                            if (mesh.subMeshCount > 1)
                            {
                                var visibilityMaterials = renderer.sharedMaterials;
                                if (visibility.SubMesh >= visibilityMaterials.Length ||
                                    visibilityMaterials[visibility.SubMesh] == null)
                                {
                                    valid = false;
                                    break;
                                }
                            }
                        }

                        var materialSlot = GetMaterialSlot(modifier);
                        if (materialSlot >= 0)
                        {
                            var materials = renderer == null ? null : renderer.sharedMaterials;
                            if (materials == null || materialSlot >= materials.Length || materials[materialSlot] == null)
                            {
                                valid = false;
                                break;
                            }
                        }
                    }
                }

                layerIsValid[layerIndex] = valid;
                if (layer != null && layer.Enabled && !valid)
                {
                    diagnostics.Add(Warning(
                        "$.layers[" + layerIndex + "]",
                        "Unity上の対象を安全に解決できないためLayer全体を無効化しました。"));
                }
            }

            return layerIsValid;
        }

        private static VertexApplication[] BindVertexDeltas(
            VlatDocument document,
            IReadOnlyList<byte[]> buffers,
            MeshBindingResolutionReport report,
            bool[] layerIsValid,
            DynamicBlendShapeBinder binder,
            List<VlatRuntimeDiagnostic> diagnostics)
        {
            var applications = new List<VertexApplication>();
            for (var layerIndex = 0; layerIndex < document.Layers.Count; layerIndex++)
            {
                if (!layerIsValid[layerIndex])
                {
                    continue;
                }

                var layer = document.Layers[layerIndex];
                for (var modifierIndex = 0; modifierIndex < layer.Modifiers.Count; modifierIndex++)
                {
                    if (!(layer.Modifiers[modifierIndex] is VertexDeltaModifier modifier))
                    {
                        continue;
                    }

                    var renderer = report[modifier.MeshBinding].Renderer as SkinnedMeshRenderer;
                    var result = binder.Bind(
                        renderer,
                        document,
                        buffers,
                        layer.Id,
                        modifierIndex,
                        modifier);
                    if (!result.Success)
                    {
                        layerIsValid[layerIndex] = false;
                        var message = result.Diagnostic == null
                            ? "vertexDeltaを登録できません。"
                            : result.Diagnostic.Message;
                        diagnostics.Add(Warning(
                            "$.layers[" + layerIndex + "].modifiers[" + modifierIndex + "]",
                            message + " Layer全体を無効化しました。"));
                        break;
                    }

                    applications.Add(new VertexApplication(
                        layer,
                        modifier,
                        result.Renderer,
                        result.BlendShapeIndex));
                }
            }

            return applications.ToArray();
        }

        private static VlatDocument CreateActiveDocument(VlatDocument source, bool[] layerIsValid)
        {
            var layers = new List<VlatLayer>();
            for (var index = 0; index < source.Layers.Count; index++)
            {
                if (layerIsValid[index])
                {
                    layers.Add(source.Layers[index]);
                }
            }

            return new VlatDocument(
                source.Metadata,
                source.Buffers,
                source.BufferViews,
                source.Textures,
                source.AngleBands,
                layers);
        }

        private void ApplyVertexDeltas(ResolvedState state)
        {
            Array.Clear(_desiredVertexWeights, 0, _desiredVertexWeights.Length);
            for (var itemIndex = 0; itemIndex < state.Count; itemIndex++)
            {
                var resolved = state[itemIndex];
                if (!(resolved.Modifier is VertexDeltaModifier))
                {
                    continue;
                }

                var key = new VertexApplicationKey(resolved.Layer, resolved.Modifier);
                if (_vertexApplicationIndices.TryGetValue(key, out var applicationIndex))
                {
                    _desiredVertexWeights[applicationIndex] = (float)(resolved.Weight * 100d);
                }
            }

            for (var index = 0; index < _vertexApplications.Length; index++)
            {
                var application = _vertexApplications[index];
                var desired = _desiredVertexWeights[index];
                if (Mathf.Approximately(desired, application.LastWeight))
                {
                    continue;
                }

                application.Renderer.SetBlendShapeWeight(application.BlendShapeIndex, desired);
                application.LastWeight = desired;
            }
        }

        private void ResetApplicationState()
        {
            _blendShapeBinder?.ClearWeights();
            _materialBinder?.ResetToBase();
            _conflictResolver?.ResetHysteresis();
            for (var index = 0; index < _vertexApplications.Length; index++)
            {
                _vertexApplications[index].LastWeight = 0f;
            }

            _activeBandCount = 0;
            _appliedModifierCount = 0;
        }

        private void DisposeRuntime()
        {
            _initialized = false;
            _materialBinder?.Dispose();
            _blendShapeBinder?.Dispose();
            _textureSet?.Dispose();
            _materialBinder = null;
            _blendShapeBinder = null;
            _textureSet = null;
            _document = null;
            _angleEvaluator = null;
            _conflictResolver = null;
            _resolvedState = null;
            _bandWeights = Array.Empty<double>();
            _vertexApplications = Array.Empty<VertexApplication>();
            _vertexApplicationIndices = null;
            _desiredVertexWeights = Array.Empty<float>();
            _bindingReport = null;
            _activeBandCount = 0;
            _appliedModifierCount = 0;
        }

        private VlatRuntimeInitializationResult CompleteFailedInitialization(
            List<VlatRuntimeDiagnostic> diagnostics,
            MeshBindingResolutionReport report)
        {
            _diagnostics = ToArray(diagnostics);
            _bindingReport = report;
            return new VlatRuntimeInitializationResult(false, report, diagnostics);
        }

        private static int GetMeshBindingIndex(VlatModifier modifier)
        {
            if (modifier is VertexDeltaModifier vertexDelta)
            {
                return vertexDelta.MeshBinding;
            }

            if (modifier is VisibilityModifier visibility)
            {
                return visibility.MeshBinding;
            }

            if (modifier is OpacityModifier opacity)
            {
                return opacity.MeshBinding;
            }

            if (modifier is MaterialPropertyModifier materialProperty)
            {
                return materialProperty.MeshBinding;
            }

            return modifier is RenderQueueModifier renderQueue ? renderQueue.MeshBinding : -1;
        }

        private static int GetMaterialSlot(VlatModifier modifier)
        {
            if (modifier is OpacityModifier opacity)
            {
                return opacity.MaterialSlot;
            }

            if (modifier is MaterialPropertyModifier materialProperty)
            {
                return materialProperty.MaterialSlot;
            }

            return modifier is RenderQueueModifier renderQueue ? renderQueue.MaterialSlot : -1;
        }

        private static void CopyCoreDiagnostics(
            IReadOnlyList<VlatDiagnostic> source,
            List<VlatRuntimeDiagnostic> destination)
        {
            for (var index = 0; index < source.Count; index++)
            {
                var diagnostic = source[index];
                if (diagnostic == null)
                {
                    continue;
                }

                destination.Add(new VlatRuntimeDiagnostic(
                    diagnostic.Severity == VlatDiagnosticSeverity.Error
                        ? VlatRuntimeDiagnosticSeverity.Error
                        : VlatRuntimeDiagnosticSeverity.Warning,
                    diagnostic.Path,
                    diagnostic.Message));
            }
        }

        private static void CopyRuntimeDiagnostics(
            IReadOnlyList<VlatRuntimeDiagnostic> source,
            List<VlatRuntimeDiagnostic> destination)
        {
            for (var index = 0; index < source.Count; index++)
            {
                destination.Add(source[index]);
            }
        }

        private static bool ContainsError(IReadOnlyList<VlatRuntimeDiagnostic> diagnostics)
        {
            for (var index = 0; index < diagnostics.Count; index++)
            {
                if (diagnostics[index] != null &&
                    diagnostics[index].Severity == VlatRuntimeDiagnosticSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }

        private static VlatRuntimeDiagnostic Error(string path, string message)
        {
            return new VlatRuntimeDiagnostic(VlatRuntimeDiagnosticSeverity.Error, path, message);
        }

        private static VlatRuntimeDiagnostic Warning(string path, string message)
        {
            return new VlatRuntimeDiagnostic(VlatRuntimeDiagnosticSeverity.Warning, path, message);
        }

        private static VlatRuntimeDiagnostic[] ToArray(IReadOnlyList<VlatRuntimeDiagnostic> source)
        {
            var result = new VlatRuntimeDiagnostic[source.Count];
            for (var index = 0; index < source.Count; index++)
            {
                result[index] = source[index];
            }

            return result;
        }

        private static void LogDiagnostics(IReadOnlyList<VlatRuntimeDiagnostic> diagnostics)
        {
            for (var index = 0; index < diagnostics.Count; index++)
            {
                var diagnostic = diagnostics[index];
                var message = diagnostic.Path + ": " + diagnostic.Message;
                if (diagnostic.Severity == VlatRuntimeDiagnosticSeverity.Error)
                {
                    Debug.LogError(message);
                }
                else if (diagnostic.Severity == VlatRuntimeDiagnosticSeverity.Warning)
                {
                    Debug.LogWarning(message);
                }
                else
                {
                    Debug.Log(message);
                }
            }
        }

        private sealed class VertexApplication
        {
            public VertexApplication(
                VlatLayer layer,
                VertexDeltaModifier modifier,
                SkinnedMeshRenderer renderer,
                int blendShapeIndex)
            {
                Layer = layer;
                Modifier = modifier;
                Renderer = renderer;
                BlendShapeIndex = blendShapeIndex;
            }

            public VlatLayer Layer { get; }

            public VertexDeltaModifier Modifier { get; }

            public SkinnedMeshRenderer Renderer { get; }

            public int BlendShapeIndex { get; }

            public float LastWeight { get; set; }
        }

        private readonly struct VertexApplicationKey : IEquatable<VertexApplicationKey>
        {
            public VertexApplicationKey(VlatLayer layer, VlatModifier modifier)
            {
                Layer = layer;
                Modifier = modifier;
            }

            private VlatLayer Layer { get; }

            private VlatModifier Modifier { get; }

            public bool Equals(VertexApplicationKey other)
            {
                return ReferenceEquals(Layer, other.Layer) && ReferenceEquals(Modifier, other.Modifier);
            }

            public override bool Equals(object obj)
            {
                return obj is VertexApplicationKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (RuntimeHelpers.GetHashCode(Layer) * 397) ^
                        RuntimeHelpers.GetHashCode(Modifier);
                }
            }
        }
    }
}
