using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VRM10.MToon10;
using VLat.Core.Evaluation;
using VLat.Core.Model;

namespace VLat.Runtime
{
    /// <summary>マテリアル補正バインダーの初期化結果を表します。</summary>
    public sealed class MaterialOverrideInitializationResult
    {
        private readonly VlatRuntimeDiagnostic[] _diagnostics;

        internal MaterialOverrideInitializationResult(
            bool success,
            int clonedMaterialCount,
            VlatRuntimeDiagnostic[] diagnostics)
        {
            Success = success;
            ClonedMaterialCount = clonedMaterialCount;
            _diagnostics = diagnostics ?? Array.Empty<VlatRuntimeDiagnostic>();
        }

        /// <summary>致命的なバインドエラーなしで初期化できたかどうかを取得します。</summary>
        public bool Success { get; }

        /// <summary>非破壊適用のために生成したマテリアル複製数を取得します。</summary>
        public int ClonedMaterialCount { get; }

        /// <summary>初期化時に収集した診断数を取得します。</summary>
        public int DiagnosticCount => _diagnostics.Length;

        /// <summary>初期化時に収集した診断を読み取り専用一覧として取得します。</summary>
        public IReadOnlyList<VlatRuntimeDiagnostic> Diagnostics => _diagnostics;

        /// <summary>指定位置の初期化診断を取得します。</summary>
        /// <param name="index">診断のインデックス。</param>
        /// <returns>指定された初期化診断。</returns>
        public VlatRuntimeDiagnostic GetDiagnostic(int index)
        {
            return _diagnostics[index];
        }
    }

    /// <summary>
    /// 解決済みマテリアル補正と可視性補正を、元アセットを変更せずRendererへ適用します。
    /// </summary>
    /// <remarks>
    /// 初期化・適用・復元・破棄はUnityのメインスレッドから呼び出してください。
    /// 定常時の<see cref="Apply"/>は事前確保済み配列とMaterialPropertyBlockを再利用します。
    /// </remarks>
    public sealed class MaterialOverrideBinder : IDisposable
    {
        private const string MainColorProperty = "_Color";
        private const string MToonShaderName = "VRM10/MToon10";

        private static readonly int MainColorId = Shader.PropertyToID(MainColorProperty);
        private static readonly int AlphaModeId = Shader.PropertyToID("_AlphaMode");

        private RendererState[] _renderers = Array.Empty<RendererState>();
        private SlotState[] _slots = Array.Empty<SlotState>();
        private GroupState[] _groups = Array.Empty<GroupState>();
        private Material[] _clonedMaterials = Array.Empty<Material>();
        private Texture[] _textures = Array.Empty<Texture>();
        private VlatRuntimeDiagnostic[] _diagnostics = Array.Empty<VlatRuntimeDiagnostic>();
        private bool _initialized;

        /// <summary>初期化済みで適用可能かどうかを取得します。</summary>
        public bool IsInitialized => _initialized;

        /// <summary>現在保持している複製マテリアル数を取得します。</summary>
        public int ClonedMaterialCount => _clonedMaterials.Length;

        /// <summary>直近の初期化で得られた診断数を取得します。</summary>
        public int DiagnosticCount => _diagnostics.Length;

        /// <summary>直近の初期化で得られた診断を読み取り専用一覧として取得します。</summary>
        public IReadOnlyList<VlatRuntimeDiagnostic> Diagnostics => _diagnostics;

        /// <summary>直近の初期化で得られた指定位置の診断を取得します。</summary>
        /// <param name="index">診断のインデックス。</param>
        /// <returns>指定された初期化診断。</returns>
        public VlatRuntimeDiagnostic GetDiagnostic(int index)
        {
            return _diagnostics[index];
        }

        /// <summary>
        /// ドキュメント内のマテリアル・可視性ModifierをRendererへ事前バインドします。
        /// </summary>
        /// <param name="document">バインド対象のVLatドキュメント。</param>
        /// <param name="renderers">meshBindingsと同じ順序の解決済みRenderer。未解決要素はnullにできます。</param>
        /// <param name="textures">document.texturesと同じ順序で読み込んだTexture。</param>
        /// <returns>複製数と警告・エラーを含む初期化結果。</returns>
        /// <exception cref="ArgumentNullException">いずれかの引数がnullの場合に送出されます。</exception>
        public MaterialOverrideInitializationResult Initialize(
            VlatDocument document,
            Renderer[] renderers,
            Texture[] textures)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (renderers == null)
            {
                throw new ArgumentNullException(nameof(renderers));
            }

            if (textures == null)
            {
                throw new ArgumentNullException(nameof(textures));
            }

            ReleaseResources();

            var diagnostics = new List<VlatRuntimeDiagnostic>();
            var rendererStates = new List<RendererState>();
            var rendererLookup = new Dictionary<Renderer, int>();
            var slots = new List<SlotState>();
            var slotLookup = new Dictionary<SlotKey, int>();
            var groups = new List<GroupState>();
            var groupLookup = new Dictionary<GroupKey, int>();

            BuildGroups(
                document,
                renderers,
                diagnostics,
                rendererStates,
                rendererLookup,
                slots,
                slotLookup,
                groups,
                groupLookup);

            _renderers = rendererStates.ToArray();
            _slots = slots.ToArray();
            _groups = groups.ToArray();
            _textures = (Texture[])textures.Clone();

            if (_textures.Length < document.Textures.Count)
            {
                AddDiagnostic(
                    diagnostics,
                    VlatRuntimeDiagnosticSeverity.Warning,
                    "textures",
                    "読み込み済みTexture配列がVLatドキュメントのtextures数より短いため、一部のtextureIndex補正を適用できません。");
            }

            var textureCheckCount = _textures.Length < document.Textures.Count
                ? _textures.Length
                : document.Textures.Count;
            for (var textureIndex = 0; textureIndex < textureCheckCount; textureIndex++)
            {
                if (_textures[textureIndex] == null)
                {
                    AddDiagnostic(
                        diagnostics,
                        VlatRuntimeDiagnosticSeverity.Warning,
                        "textures[" + textureIndex + "]",
                        "Textureを読み込めなかったため、このtextureIndexを使う補正はベースTextureへフォールバックします。");
                }
            }

            CreateRequiredMaterialInstances(diagnostics);
            FinalizeSlotBaselines(diagnostics);
            FinalizeGroupBaselines(diagnostics);
            AssignGeneratedMaterialArrays();

            _diagnostics = diagnostics.ToArray();
            _initialized = true;

            var success = true;
            for (var index = 0; index < _diagnostics.Length; index++)
            {
                if (_diagnostics[index].Severity == VlatRuntimeDiagnosticSeverity.Error)
                {
                    success = false;
                    break;
                }
            }

            return new MaterialOverrideInitializationResult(
                success,
                _clonedMaterials.Length,
                _diagnostics);
        }

        /// <summary>
        /// 競合解決済み状態を差分適用し、Unityオブジェクトへ実際に書き込んだ回数を返します。
        /// </summary>
        /// <param name="state">初期化時と同じドキュメントから生成された解決済み状態。</param>
        /// <returns>Renderer、MaterialまたはMaterialPropertyBlockへの実書込回数。</returns>
        /// <exception cref="InvalidOperationException">未初期化の場合に送出されます。</exception>
        /// <exception cref="ArgumentNullException">stateがnullの場合に送出されます。</exception>
        /// <exception cref="ArgumentException">競合ターゲット構成が初期化時と異なる場合に送出されます。</exception>
        public int Apply(ResolvedState state)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("MaterialOverrideBinderは初期化されていません。");
            }

            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (state.GroupCount != _groups.Length)
            {
                throw new ArgumentException(
                    "ResolvedStateの競合ターゲット構成が初期化時のドキュメントと一致しません。",
                    nameof(state));
            }

            var writeCount = EnsureGeneratedMaterialsAssigned();
            PrepareFrame(state);

            for (var index = 0; index < state.Count; index++)
            {
                var resolved = state[index];
                if (resolved.GroupId < 0 || resolved.GroupId >= _groups.Length)
                {
                    continue;
                }

                Accumulate(_groups[resolved.GroupId], resolved);
            }

            for (var groupIndex = 0; groupIndex < _groups.Length; groupIndex++)
            {
                writeCount += ApplyGroup(_groups[groupIndex]);
            }

            for (var slotIndex = 0; slotIndex < _slots.Length; slotIndex++)
            {
                writeCount += ApplySlotMainColor(_slots[slotIndex]);
            }

            for (var slotIndex = 0; slotIndex < _slots.Length; slotIndex++)
            {
                var slot = _slots[slotIndex];
                if (!slot.BlockDirty)
                {
                    continue;
                }

                var renderer = _renderers[slot.RendererIndex].Renderer;
                if (renderer != null)
                {
                    renderer.SetPropertyBlock(slot.WorkingBlock, slot.MaterialSlot);
                    writeCount++;
                }

                slot.BlockDirty = false;
            }

            return writeCount;
        }

        /// <summary>
        /// 元のMaterial配列・MaterialPropertyBlock・Renderer.enabledへ戻します。
        /// 保持中の複製Materialは破棄せず、次回<see cref="Apply"/>で再利用します。
        /// </summary>
        public void ResetToBase()
        {
            if (!_initialized)
            {
                return;
            }

            RestoreOriginalState();
            ResetGeneratedStateToBase();
        }

        /// <summary>
        /// 元状態を復元し、生成したMaterialを破棄して保持資源を解放します。
        /// </summary>
        public void Dispose()
        {
            ReleaseResources();
        }

        private static void BuildGroups(
            VlatDocument document,
            Renderer[] resolvedRenderers,
            List<VlatRuntimeDiagnostic> diagnostics,
            List<RendererState> rendererStates,
            Dictionary<Renderer, int> rendererLookup,
            List<SlotState> slots,
            Dictionary<SlotKey, int> slotLookup,
            List<GroupState> groups,
            Dictionary<GroupKey, int> groupLookup)
        {
            for (var layerIndex = 0; layerIndex < document.Layers.Count; layerIndex++)
            {
                var layer = document.Layers[layerIndex];
                if (layer == null)
                {
                    continue;
                }

                for (var modifierIndex = 0; modifierIndex < layer.Modifiers.Count; modifierIndex++)
                {
                    var modifier = layer.Modifiers[modifierIndex];
                    if (modifier == null || modifier is VertexDeltaModifier)
                    {
                        continue;
                    }

                    var key = GroupKey.Create(modifier);
                    int groupIndex;
                    var path = "layers[" + layerIndex + "].modifiers[" + modifierIndex + "]";
                    if (!groupLookup.TryGetValue(key, out groupIndex))
                    {
                        groupIndex = groups.Count;
                        groupLookup.Add(key, groupIndex);
                        groups.Add(CreateGroup(
                            modifier,
                            path,
                            resolvedRenderers,
                            diagnostics,
                            rendererStates,
                            rendererLookup,
                            slots,
                            slotLookup));
                    }
                    else
                    {
                        ValidateGroupCompatibility(groups[groupIndex], modifier, path, diagnostics);
                    }

                    MergeRequirements(groups[groupIndex], modifier, slots);
                }
            }
        }

        private static GroupState CreateGroup(
            VlatModifier modifier,
            string path,
            Renderer[] resolvedRenderers,
            List<VlatRuntimeDiagnostic> diagnostics,
            List<RendererState> rendererStates,
            Dictionary<Renderer, int> rendererLookup,
            List<SlotState> slots,
            Dictionary<SlotKey, int> slotLookup)
        {
            var visibility = modifier as VisibilityModifier;
            if (visibility != null)
            {
                Renderer renderer;
                if (!TryResolveRenderer(visibility.MeshBinding, path, resolvedRenderers, diagnostics, out renderer))
                {
                    return GroupState.Unsupported();
                }

                var rendererIndex = EnsureRenderer(renderer, rendererStates, rendererLookup);
                var mesh = GetSharedMesh(renderer);
                if (mesh == null)
                {
                    AddDiagnostic(
                        diagnostics,
                        VlatRuntimeDiagnosticSeverity.Error,
                        path,
                        "visibility対象Rendererの共有Meshを取得できません。");
                    return GroupState.Unsupported();
                }

                if (visibility.SubMesh < 0 || visibility.SubMesh >= mesh.subMeshCount)
                {
                    AddDiagnostic(
                        diagnostics,
                        VlatRuntimeDiagnosticSeverity.Error,
                        path + ".subMesh",
                        "visibilityのsubMeshが対象Meshの範囲外です。");
                    return GroupState.Unsupported();
                }

                if (mesh.subMeshCount == 1)
                {
                    rendererStates[rendererIndex].OwnsEnabled = true;
                    return GroupState.RendererVisibility(rendererIndex, renderer.enabled);
                }

                int slotIndex;
                if (!TryEnsureSlot(
                        renderer,
                        rendererIndex,
                        visibility.SubMesh,
                        path,
                        diagnostics,
                        rendererStates,
                        slots,
                        slotLookup,
                        out slotIndex))
                {
                    return GroupState.Unsupported();
                }

                AddDiagnostic(
                    diagnostics,
                    VlatRuntimeDiagnosticSeverity.Warning,
                    path,
                    "複数subMeshのvisibilityは対象material slotのalphaマスクへフォールバックします。");
                return GroupState.SlotVisibility(slotIndex);
            }

            var opacity = modifier as OpacityModifier;
            if (opacity != null)
            {
                int slotIndex;
                if (!TryResolveSlot(
                        opacity.MeshBinding,
                        opacity.MaterialSlot,
                        path,
                        resolvedRenderers,
                        diagnostics,
                        rendererStates,
                        rendererLookup,
                        slots,
                        slotLookup,
                        out slotIndex))
                {
                    return GroupState.Unsupported();
                }

                return GroupState.Opacity(slotIndex);
            }

            var property = modifier as MaterialPropertyModifier;
            if (property != null)
            {
                int slotIndex;
                if (!TryResolveSlot(
                        property.MeshBinding,
                        property.MaterialSlot,
                        path,
                        resolvedRenderers,
                        diagnostics,
                        rendererStates,
                        rendererLookup,
                        slots,
                        slotLookup,
                        out slotIndex))
                {
                    return GroupState.Unsupported();
                }

                if (string.IsNullOrEmpty(property.Property))
                {
                    AddDiagnostic(
                        diagnostics,
                        VlatRuntimeDiagnosticSeverity.Error,
                        path + ".property",
                        "materialPropertyのpropertyが空です。");
                    return GroupState.Unsupported();
                }

                var propertyId = Shader.PropertyToID(property.Property);
                if (string.Equals(property.ValueType, "float", StringComparison.Ordinal))
                {
                    return GroupState.Float(slotIndex, propertyId, property.Property);
                }

                if (string.Equals(property.ValueType, "color", StringComparison.Ordinal))
                {
                    return GroupState.Color(slotIndex, propertyId, property.Property);
                }

                if (string.Equals(property.ValueType, "vector", StringComparison.Ordinal))
                {
                    return GroupState.Vector(slotIndex, propertyId, property.Property);
                }

                if (string.Equals(property.ValueType, "textureIndex", StringComparison.Ordinal))
                {
                    return GroupState.Texture(slotIndex, propertyId, property.Property);
                }

                AddDiagnostic(
                    diagnostics,
                    VlatRuntimeDiagnosticSeverity.Error,
                    path + ".valueType",
                    "未対応のmaterialProperty valueTypeです。");
                return GroupState.Unsupported();
            }

            var renderQueue = modifier as RenderQueueModifier;
            if (renderQueue != null)
            {
                int slotIndex;
                if (!TryResolveSlot(
                        renderQueue.MeshBinding,
                        renderQueue.MaterialSlot,
                        path,
                        resolvedRenderers,
                        diagnostics,
                        rendererStates,
                        rendererLookup,
                        slots,
                        slotLookup,
                        out slotIndex))
                {
                    return GroupState.Unsupported();
                }

                return GroupState.RenderQueue(slotIndex);
            }

            AddDiagnostic(
                diagnostics,
                VlatRuntimeDiagnosticSeverity.Warning,
                path,
                "MaterialOverrideBinderが扱わないModifier型です。");
            return GroupState.Unsupported();
        }

        private static void ValidateGroupCompatibility(
            GroupState group,
            VlatModifier modifier,
            string path,
            List<VlatRuntimeDiagnostic> diagnostics)
        {
            var expectedKind = GetKind(modifier);
            if (group.Kind == GroupKind.Unsupported ||
                expectedKind == group.Kind ||
                (expectedKind == GroupKind.VisibilityUnknown &&
                 (group.Kind == GroupKind.RendererVisibility || group.Kind == GroupKind.SlotVisibility)))
            {
                return;
            }

            group.Kind = GroupKind.Unsupported;
            AddDiagnostic(
                diagnostics,
                VlatRuntimeDiagnosticSeverity.Error,
                path,
                "同一material propertyに異なるvalueTypeが指定されています。");
        }

        private static GroupKind GetKind(VlatModifier modifier)
        {
            if (modifier is VisibilityModifier)
            {
                return GroupKind.VisibilityUnknown;
            }

            if (modifier is OpacityModifier)
            {
                return GroupKind.Opacity;
            }

            var property = modifier as MaterialPropertyModifier;
            if (property != null)
            {
                if (string.Equals(property.ValueType, "float", StringComparison.Ordinal))
                {
                    return GroupKind.Float;
                }

                if (string.Equals(property.ValueType, "color", StringComparison.Ordinal))
                {
                    return GroupKind.Color;
                }

                if (string.Equals(property.ValueType, "vector", StringComparison.Ordinal))
                {
                    return GroupKind.Vector;
                }

                if (string.Equals(property.ValueType, "textureIndex", StringComparison.Ordinal))
                {
                    return GroupKind.Texture;
                }
            }

            return modifier is RenderQueueModifier ? GroupKind.RenderQueue : GroupKind.Unsupported;
        }

        private static void MergeRequirements(
            GroupState group,
            VlatModifier modifier,
            List<SlotState> slots)
        {
            if (group.SlotIndex < 0 || group.SlotIndex >= slots.Count)
            {
                return;
            }

            var slot = slots[group.SlotIndex];
            var opacity = modifier as OpacityModifier;
            if (opacity != null)
            {
                slot.NeedsMainColor = true;
                if (opacity.Value < 1d)
                {
                    slot.NeedsTransparentMode = true;
                }

                return;
            }

            var visibility = modifier as VisibilityModifier;
            if (visibility != null && group.Kind == GroupKind.SlotVisibility)
            {
                slot.NeedsMainColor = true;
                if (!visibility.VisibleInBand)
                {
                    slot.NeedsTransparentMode = true;
                }

                return;
            }

            var property = modifier as MaterialPropertyModifier;
            if (property != null)
            {
                if (group.PropertyId == MainColorId)
                {
                    slot.NeedsMainColor = true;
                }

                return;
            }

            if (modifier is RenderQueueModifier)
            {
                slot.NeedsDirectMaterialClone = true;
            }
        }

        private static bool TryResolveRenderer(
            int meshBinding,
            string path,
            Renderer[] resolvedRenderers,
            List<VlatRuntimeDiagnostic> diagnostics,
            out Renderer renderer)
        {
            renderer = null;
            if (meshBinding < 0 || meshBinding >= resolvedRenderers.Length)
            {
                AddDiagnostic(
                    diagnostics,
                    VlatRuntimeDiagnosticSeverity.Error,
                    path + ".meshBinding",
                    "meshBindingに対応するRenderer要素がありません。");
                return false;
            }

            renderer = resolvedRenderers[meshBinding];
            if (renderer != null)
            {
                return true;
            }

            AddDiagnostic(
                diagnostics,
                VlatRuntimeDiagnosticSeverity.Error,
                path + ".meshBinding",
                "meshBindingをRendererへ解決できませんでした。");
            return false;
        }

        private static bool TryResolveSlot(
            int meshBinding,
            int materialSlot,
            string path,
            Renderer[] resolvedRenderers,
            List<VlatRuntimeDiagnostic> diagnostics,
            List<RendererState> rendererStates,
            Dictionary<Renderer, int> rendererLookup,
            List<SlotState> slots,
            Dictionary<SlotKey, int> slotLookup,
            out int slotIndex)
        {
            slotIndex = -1;
            Renderer renderer;
            if (!TryResolveRenderer(meshBinding, path, resolvedRenderers, diagnostics, out renderer))
            {
                return false;
            }

            var rendererIndex = EnsureRenderer(renderer, rendererStates, rendererLookup);
            return TryEnsureSlot(
                renderer,
                rendererIndex,
                materialSlot,
                path,
                diagnostics,
                rendererStates,
                slots,
                slotLookup,
                out slotIndex);
        }

        private static int EnsureRenderer(
            Renderer renderer,
            List<RendererState> rendererStates,
            Dictionary<Renderer, int> rendererLookup)
        {
            int rendererIndex;
            if (rendererLookup.TryGetValue(renderer, out rendererIndex))
            {
                return rendererIndex;
            }

            rendererIndex = rendererStates.Count;
            rendererLookup.Add(renderer, rendererIndex);
            rendererStates.Add(new RendererState(renderer));
            return rendererIndex;
        }

        private static bool TryEnsureSlot(
            Renderer renderer,
            int rendererIndex,
            int materialSlot,
            string path,
            List<VlatRuntimeDiagnostic> diagnostics,
            List<RendererState> rendererStates,
            List<SlotState> slots,
            Dictionary<SlotKey, int> slotLookup,
            out int slotIndex)
        {
            slotIndex = -1;
            var materials = rendererStates[rendererIndex].OriginalMaterials;
            if (materialSlot < 0 || materialSlot >= materials.Length)
            {
                AddDiagnostic(
                    diagnostics,
                    VlatRuntimeDiagnosticSeverity.Error,
                    path + ".materialSlot",
                    "materialSlotがRenderer.sharedMaterialsの範囲外です。");
                return false;
            }

            if (materials[materialSlot] == null)
            {
                AddDiagnostic(
                    diagnostics,
                    VlatRuntimeDiagnosticSeverity.Error,
                    path + ".materialSlot",
                    "対象material slotにMaterialが設定されていません。");
                return false;
            }

            var key = new SlotKey(renderer, materialSlot);
            if (slotLookup.TryGetValue(key, out slotIndex))
            {
                return true;
            }

            slotIndex = slots.Count;
            slotLookup.Add(key, slotIndex);
            slots.Add(new SlotState(rendererIndex, materialSlot, materials[materialSlot], renderer, path));
            return true;
        }

        private void CreateRequiredMaterialInstances(List<VlatRuntimeDiagnostic> diagnostics)
        {
            var clones = new List<Material>();
            for (var slotIndex = 0; slotIndex < _slots.Length; slotIndex++)
            {
                var slot = _slots[slotIndex];
                var source = slot.OriginalMaterial;
                var needsModeSwitch = slot.NeedsTransparentMode && !IsTransparentMaterial(source);
                if (!slot.NeedsDirectMaterialClone && !needsModeSwitch)
                {
                    slot.EffectiveMaterial = source;
                    continue;
                }

                var clone = new Material(source)
                {
                    name = source.name + " (VLat Runtime)",
                    hideFlags = HideFlags.DontSave,
                };
                slot.GeneratedMaterial = clone;
                slot.EffectiveMaterial = clone;
                clones.Add(clone);

                if (needsModeSwitch && !TryConfigureMToonTransparentWithZWrite(clone))
                {
                    AddDiagnostic(
                        diagnostics,
                        VlatRuntimeDiagnosticSeverity.Warning,
                        slot.DiagnosticPath,
                        "対象ShaderはMToon10 TransparentWithZWriteの安全な自動設定に未対応です。alpha補正の描画結果を確認してください。");
                }
            }

            _clonedMaterials = clones.ToArray();

            for (var rendererIndex = 0; rendererIndex < _renderers.Length; rendererIndex++)
            {
                var renderer = _renderers[rendererIndex];
                var needsArray = false;
                for (var slotIndex = 0; slotIndex < _slots.Length; slotIndex++)
                {
                    var slot = _slots[slotIndex];
                    if (slot.RendererIndex == rendererIndex && slot.GeneratedMaterial != null)
                    {
                        needsArray = true;
                        break;
                    }
                }

                if (!needsArray)
                {
                    continue;
                }

                renderer.WorkingMaterials = (Material[])renderer.OriginalMaterials.Clone();
                for (var slotIndex = 0; slotIndex < _slots.Length; slotIndex++)
                {
                    var slot = _slots[slotIndex];
                    if (slot.RendererIndex == rendererIndex && slot.GeneratedMaterial != null)
                    {
                        renderer.WorkingMaterials[slot.MaterialSlot] = slot.GeneratedMaterial;
                    }
                }
            }
        }

        private void FinalizeSlotBaselines(List<VlatRuntimeDiagnostic> diagnostics)
        {
            for (var slotIndex = 0; slotIndex < _slots.Length; slotIndex++)
            {
                var slot = _slots[slotIndex];
                slot.BaseRenderQueue = slot.EffectiveMaterial.renderQueue;
                if (!slot.NeedsMainColor)
                {
                    continue;
                }

                if (!slot.EffectiveMaterial.HasProperty(MainColorId))
                {
                    AddDiagnostic(
                        diagnostics,
                        VlatRuntimeDiagnosticSeverity.Warning,
                        slot.DiagnosticPath,
                        "対象Shaderに_Colorがないためopacityまたはvisibility alphaを適用できません。");
                    continue;
                }

                slot.MainColorSupported = true;
                slot.BaseMainColor = ReadColor(slot, MainColorId);
                slot.LastMainColor = slot.BaseMainColor;
                slot.LastMainColorValid = true;
            }
        }

        private void FinalizeGroupBaselines(List<VlatRuntimeDiagnostic> diagnostics)
        {
            for (var groupIndex = 0; groupIndex < _groups.Length; groupIndex++)
            {
                var group = _groups[groupIndex];
                if (group.Kind == GroupKind.Unsupported ||
                    group.Kind == GroupKind.RendererVisibility ||
                    group.Kind == GroupKind.SlotVisibility ||
                    group.Kind == GroupKind.Opacity)
                {
                    group.ResetCacheToBase();
                    continue;
                }

                if (group.SlotIndex < 0 || group.SlotIndex >= _slots.Length)
                {
                    group.Kind = GroupKind.Unsupported;
                    continue;
                }

                if (group.Kind == GroupKind.RenderQueue)
                {
                    var queueSlot = _slots[group.SlotIndex];
                    group.BaseQueue = queueSlot.BaseRenderQueue;
                    group.ResetCacheToBase();
                    continue;
                }

                var slot = _slots[group.SlotIndex];
                var material = slot.EffectiveMaterial;
                if (material == null || !material.HasProperty(group.PropertyId))
                {
                    AddDiagnostic(
                        diagnostics,
                        VlatRuntimeDiagnosticSeverity.Warning,
                        group.PropertyName,
                        "対象ShaderにmaterialPropertyがないため、この補正を無効化します。");
                    group.Kind = GroupKind.Unsupported;
                    continue;
                }

                switch (group.Kind)
                {
                    case GroupKind.Float:
                        group.BaseScalar = ReadFloat(slot, group.PropertyId);
                        break;
                    case GroupKind.Color:
                        group.BaseVector = ReadColor(slot, group.PropertyId);
                        group.IsMainColor = group.PropertyId == MainColorId;
                        break;
                    case GroupKind.Vector:
                        group.BaseVector = ReadVector(slot, group.PropertyId);
                        group.IsMainColor = group.PropertyId == MainColorId;
                        break;
                    case GroupKind.Texture:
                        group.BaseTexture = ReadTexture(slot, group.PropertyId);
                        break;
                }

                group.ResetCacheToBase();
            }
        }

        private void AssignGeneratedMaterialArrays()
        {
            for (var rendererIndex = 0; rendererIndex < _renderers.Length; rendererIndex++)
            {
                var state = _renderers[rendererIndex];
                if (state.Renderer == null || state.WorkingMaterials == null)
                {
                    continue;
                }

                state.Renderer.sharedMaterials = state.WorkingMaterials;
                state.GeneratedMaterialsAssigned = true;
            }
        }

        private int EnsureGeneratedMaterialsAssigned()
        {
            var writes = 0;
            for (var rendererIndex = 0; rendererIndex < _renderers.Length; rendererIndex++)
            {
                var state = _renderers[rendererIndex];
                if (state.Renderer == null || state.WorkingMaterials == null || state.GeneratedMaterialsAssigned)
                {
                    continue;
                }

                state.Renderer.sharedMaterials = state.WorkingMaterials;
                state.GeneratedMaterialsAssigned = true;
                writes++;
            }

            return writes;
        }

        private void PrepareFrame(ResolvedState state)
        {
            for (var rendererIndex = 0; rendererIndex < _renderers.Length; rendererIndex++)
            {
                var renderer = _renderers[rendererIndex];
                renderer.DesiredEnabled = renderer.OriginalEnabled;
            }

            for (var slotIndex = 0; slotIndex < _slots.Length; slotIndex++)
            {
                var slot = _slots[slotIndex];
                slot.DesiredMainColor = slot.BaseMainColor;
                slot.OpacityFactor = 1f;
                slot.VisibilityFactor = 1f;
                slot.BlockDirty = false;
            }

            for (var groupIndex = 0; groupIndex < _groups.Length; groupIndex++)
            {
                _groups[groupIndex].Prepare(state.GetBaseWeight(groupIndex));
            }
        }

        private void Accumulate(GroupState group, ResolvedModifier resolved)
        {
            if (group.Kind == GroupKind.Unsupported)
            {
                return;
            }

            var visibility = resolved.Modifier as VisibilityModifier;
            if (visibility != null)
            {
                if (group.Kind == GroupKind.RendererVisibility || group.Kind == GroupKind.SlotVisibility)
                {
                    group.DesiredBool = visibility.VisibleInBand;
                }

                return;
            }

            var opacity = resolved.Modifier as OpacityModifier;
            if (opacity != null && group.Kind == GroupKind.Opacity)
            {
                group.DesiredScalar += opacity.Value * resolved.Weight;
                return;
            }

            var property = resolved.Modifier as MaterialPropertyModifier;
            if (property != null)
            {
                AccumulateMaterialProperty(group, property, resolved.Weight, resolved.IsSwitch);
                return;
            }

            var renderQueue = resolved.Modifier as RenderQueueModifier;
            if (renderQueue != null && group.Kind == GroupKind.RenderQueue)
            {
                group.DesiredQueue = SaturatingAdd(group.BaseQueue, renderQueue.Offset);
            }
        }

        private void AccumulateMaterialProperty(
            GroupState group,
            MaterialPropertyModifier modifier,
            double weight,
            bool isSwitch)
        {
            var value = modifier.BandValue;
            if (value == null)
            {
                return;
            }

            if (group.Kind == GroupKind.Float)
            {
                if (!value.IsScalar)
                {
                    return;
                }

                if (isSwitch)
                {
                    group.DesiredScalar = value.Scalar;
                }
                else
                {
                    group.DesiredScalar += value.Scalar * weight;
                }

                return;
            }

            if (group.Kind == GroupKind.Color || group.Kind == GroupKind.Vector)
            {
                if (value.IsScalar || value.ComponentCount != 4)
                {
                    return;
                }

                var vector = new Vector4(
                    ToFiniteFloat(value.GetComponent(0)),
                    ToFiniteFloat(value.GetComponent(1)),
                    ToFiniteFloat(value.GetComponent(2)),
                    ToFiniteFloat(value.GetComponent(3)));
                if (isSwitch)
                {
                    group.DesiredVector = vector;
                }
                else
                {
                    var factor = ToFiniteFloat(weight);
                    group.DesiredVector += vector * factor;
                }

                return;
            }

            if (group.Kind != GroupKind.Texture || !value.IsScalar)
            {
                return;
            }

            if (double.IsNaN(value.Scalar) ||
                double.IsInfinity(value.Scalar) ||
                value.Scalar < 0d ||
                value.Scalar > int.MaxValue ||
                Math.Truncate(value.Scalar) != value.Scalar)
            {
                return;
            }

            var textureIndex = (int)value.Scalar;
            if (textureIndex >= 0 && textureIndex < _textures.Length && _textures[textureIndex] != null)
            {
                group.DesiredTexture = _textures[textureIndex];
            }
        }

        private int ApplyGroup(GroupState group)
        {
            switch (group.Kind)
            {
                case GroupKind.RendererVisibility:
                    return ApplyRendererVisibility(group);
                case GroupKind.SlotVisibility:
                    _slots[group.SlotIndex].VisibilityFactor = group.DesiredBool ? 1f : 0f;
                    return 0;
                case GroupKind.Opacity:
                    _slots[group.SlotIndex].OpacityFactor = Mathf.Clamp01(ToFiniteFloat(group.DesiredScalar));
                    return 0;
                case GroupKind.Float:
                    return ApplyFloat(group);
                case GroupKind.Color:
                    return ApplyColor(group);
                case GroupKind.Vector:
                    return ApplyVector(group);
                case GroupKind.Texture:
                    return ApplyTexture(group);
                case GroupKind.RenderQueue:
                    return ApplyRenderQueue(group);
                default:
                    return 0;
            }
        }

        private int ApplyRendererVisibility(GroupState group)
        {
            var rendererState = _renderers[group.RendererIndex];
            rendererState.DesiredEnabled = group.DesiredBool;
            if (rendererState.Renderer == null ||
                (rendererState.LastEnabledValid && rendererState.LastEnabled == rendererState.DesiredEnabled))
            {
                return 0;
            }

            rendererState.Renderer.enabled = rendererState.DesiredEnabled;
            rendererState.LastEnabled = rendererState.DesiredEnabled;
            rendererState.LastEnabledValid = true;
            return 1;
        }

        private int ApplyFloat(GroupState group)
        {
            var value = ToFiniteFloat(group.DesiredScalar);
            if (group.LastScalarValid && SameFloat(group.LastScalar, value))
            {
                return 0;
            }

            var slot = _slots[group.SlotIndex];
            slot.WorkingBlock.SetFloat(group.PropertyId, value);
            slot.BlockDirty = true;
            group.LastScalar = value;
            group.LastScalarValid = true;
            return 0;
        }

        private int ApplyColor(GroupState group)
        {
            var value = ToColor(group.DesiredVector);
            if (group.IsMainColor)
            {
                _slots[group.SlotIndex].DesiredMainColor = value;
                return 0;
            }

            if (group.LastVectorValid && SameVector(group.LastVector, value))
            {
                return 0;
            }

            var slot = _slots[group.SlotIndex];
            slot.WorkingBlock.SetColor(group.PropertyId, value);
            slot.BlockDirty = true;
            group.LastVector = value;
            group.LastVectorValid = true;
            return 0;
        }

        private int ApplyVector(GroupState group)
        {
            var value = group.DesiredVector;
            if (group.IsMainColor)
            {
                _slots[group.SlotIndex].DesiredMainColor = ToColor(value);
                return 0;
            }

            if (group.LastVectorValid && SameVector(group.LastVector, value))
            {
                return 0;
            }

            var slot = _slots[group.SlotIndex];
            slot.WorkingBlock.SetVector(group.PropertyId, value);
            slot.BlockDirty = true;
            group.LastVector = value;
            group.LastVectorValid = true;
            return 0;
        }

        private int ApplyTexture(GroupState group)
        {
            if (group.LastTextureValid && ReferenceEquals(group.LastTexture, group.DesiredTexture))
            {
                return 0;
            }

            var slot = _slots[group.SlotIndex];
            slot.WorkingBlock.SetTexture(group.PropertyId, group.DesiredTexture);
            slot.BlockDirty = true;
            group.LastTexture = group.DesiredTexture;
            group.LastTextureValid = true;
            return 0;
        }

        private int ApplyRenderQueue(GroupState group)
        {
            if (group.LastQueueValid && group.LastQueue == group.DesiredQueue)
            {
                return 0;
            }

            var slot = _slots[group.SlotIndex];
            if (slot.GeneratedMaterial == null)
            {
                return 0;
            }

            slot.GeneratedMaterial.renderQueue = group.DesiredQueue;
            group.LastQueue = group.DesiredQueue;
            group.LastQueueValid = true;
            return 1;
        }

        private static int ApplySlotMainColor(SlotState slot)
        {
            if (!slot.NeedsMainColor || !slot.MainColorSupported)
            {
                return 0;
            }

            var color = slot.DesiredMainColor;
            color.a = ToFiniteFloat(color.a * slot.OpacityFactor * slot.VisibilityFactor);
            if (slot.LastMainColorValid && SameVector(slot.LastMainColor, color))
            {
                return 0;
            }

            slot.WorkingBlock.SetColor(MainColorId, color);
            slot.BlockDirty = true;
            slot.LastMainColor = color;
            slot.LastMainColorValid = true;
            return 0;
        }

        private void RestoreOriginalState()
        {
            for (var rendererIndex = 0; rendererIndex < _renderers.Length; rendererIndex++)
            {
                var state = _renderers[rendererIndex];
                if (state.Renderer == null)
                {
                    continue;
                }

                if (state.WorkingMaterials != null && state.GeneratedMaterialsAssigned)
                {
                    state.Renderer.sharedMaterials = state.OriginalMaterials;
                    state.GeneratedMaterialsAssigned = false;
                }

                if (state.OwnsEnabled)
                {
                    state.Renderer.enabled = state.OriginalEnabled;
                    state.DesiredEnabled = state.OriginalEnabled;
                    state.LastEnabled = state.OriginalEnabled;
                    state.LastEnabledValid = true;
                }
            }

            for (var slotIndex = 0; slotIndex < _slots.Length; slotIndex++)
            {
                var slot = _slots[slotIndex];
                var renderer = _renderers[slot.RendererIndex].Renderer;
                if (renderer == null)
                {
                    continue;
                }

                renderer.SetPropertyBlock(
                    slot.OriginalBlockWasEmpty ? null : slot.OriginalBlock,
                    slot.MaterialSlot);
                slot.ReloadWorkingBlock(renderer);
                slot.DesiredMainColor = slot.BaseMainColor;
                slot.LastMainColor = slot.BaseMainColor;
                slot.LastMainColorValid = true;
                slot.OpacityFactor = 1f;
                slot.VisibilityFactor = 1f;
                slot.BlockDirty = false;
            }
        }

        private void ResetGeneratedStateToBase()
        {
            for (var groupIndex = 0; groupIndex < _groups.Length; groupIndex++)
            {
                var group = _groups[groupIndex];
                if (group.Kind == GroupKind.RenderQueue && group.SlotIndex >= 0)
                {
                    var material = _slots[group.SlotIndex].GeneratedMaterial;
                    if (material != null)
                    {
                        material.renderQueue = group.BaseQueue;
                    }
                }

                group.ResetCacheToBase();
            }
        }

        private void ReleaseResources()
        {
            if (_initialized)
            {
                RestoreOriginalState();
            }

            for (var index = 0; index < _clonedMaterials.Length; index++)
            {
                var material = _clonedMaterials[index];
                if (material == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(material);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(material);
                }
            }

            _renderers = Array.Empty<RendererState>();
            _slots = Array.Empty<SlotState>();
            _groups = Array.Empty<GroupState>();
            _clonedMaterials = Array.Empty<Material>();
            _textures = Array.Empty<Texture>();
            _initialized = false;
        }

        private static bool TryConfigureMToonTransparentWithZWrite(Material material)
        {
            if (!IsMToon10Material(material))
            {
                return false;
            }

            try
            {
                var context = new MToon10Context(material)
                {
                    AlphaMode = MToon10AlphaMode.Transparent,
                    TransparentWithZWriteMode = MToon10TransparentWithZWriteMode.On,
                    RenderQueueOffsetNumber = 0,
                };
                context.Validate();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsMToon10Material(Material material)
        {
            return material != null &&
                material.shader != null &&
                string.Equals(material.shader.name, MToonShaderName, StringComparison.Ordinal) &&
                material.HasProperty(AlphaModeId);
        }

        private static bool IsTransparentMaterial(Material material)
        {
            if (material == null)
            {
                return false;
            }

            if (IsMToon10Material(material))
            {
                return material.GetInt(AlphaModeId) == 2;
            }

            return material.renderQueue >= (int)RenderQueue.Transparent;
        }

        private static Mesh GetSharedMesh(Renderer renderer)
        {
            var skinned = renderer as SkinnedMeshRenderer;
            if (skinned != null)
            {
                return skinned.sharedMesh;
            }

            var filter = renderer.GetComponent<MeshFilter>();
            return filter == null ? null : filter.sharedMesh;
        }

        private static float ReadFloat(SlotState slot, int propertyId)
        {
            return slot.WorkingBlock.HasFloat(propertyId)
                ? slot.WorkingBlock.GetFloat(propertyId)
                : slot.EffectiveMaterial.GetFloat(propertyId);
        }

        private static Color ReadColor(SlotState slot, int propertyId)
        {
            if (slot.WorkingBlock.HasColor(propertyId))
            {
                return slot.WorkingBlock.GetColor(propertyId);
            }

            if (slot.WorkingBlock.HasVector(propertyId))
            {
                return ToColor(slot.WorkingBlock.GetVector(propertyId));
            }

            return slot.EffectiveMaterial.GetColor(propertyId);
        }

        private static Vector4 ReadVector(SlotState slot, int propertyId)
        {
            if (slot.WorkingBlock.HasVector(propertyId))
            {
                return slot.WorkingBlock.GetVector(propertyId);
            }

            if (slot.WorkingBlock.HasColor(propertyId))
            {
                return slot.WorkingBlock.GetColor(propertyId);
            }

            return slot.EffectiveMaterial.GetVector(propertyId);
        }

        private static Texture ReadTexture(SlotState slot, int propertyId)
        {
            return slot.WorkingBlock.HasTexture(propertyId)
                ? slot.WorkingBlock.GetTexture(propertyId)
                : slot.EffectiveMaterial.GetTexture(propertyId);
        }

        private static int SaturatingAdd(int left, int right)
        {
            var result = (long)left + right;
            if (result > int.MaxValue)
            {
                return int.MaxValue;
            }

            return result < int.MinValue ? int.MinValue : (int)result;
        }

        private static float ToFiniteFloat(double value)
        {
            if (double.IsNaN(value))
            {
                return 0f;
            }

            if (value > float.MaxValue)
            {
                return float.MaxValue;
            }

            if (value < -float.MaxValue)
            {
                return -float.MaxValue;
            }

            return (float)value;
        }

        private static bool SameFloat(float left, float right)
        {
            return left.Equals(right);
        }

        private static bool SameVector(Vector4 left, Vector4 right)
        {
            return left.x.Equals(right.x) &&
                left.y.Equals(right.y) &&
                left.z.Equals(right.z) &&
                left.w.Equals(right.w);
        }

        private static Color ToColor(Vector4 value)
        {
            return new Color(value.x, value.y, value.z, value.w);
        }

        private static void AddDiagnostic(
            List<VlatRuntimeDiagnostic> diagnostics,
            VlatRuntimeDiagnosticSeverity severity,
            string path,
            string message)
        {
            diagnostics.Add(new VlatRuntimeDiagnostic(severity, path, message));
        }

        private enum GroupKind
        {
            Unsupported,
            VisibilityUnknown,
            RendererVisibility,
            SlotVisibility,
            Opacity,
            Float,
            Color,
            Vector,
            Texture,
            RenderQueue,
        }

        private sealed class RendererState
        {
            public RendererState(Renderer renderer)
            {
                Renderer = renderer;
                OriginalMaterials = renderer.sharedMaterials;
                OriginalEnabled = renderer.enabled;
                DesiredEnabled = OriginalEnabled;
                LastEnabled = OriginalEnabled;
                LastEnabledValid = true;
            }

            public Renderer Renderer { get; }
            public Material[] OriginalMaterials { get; }
            public Material[] WorkingMaterials { get; set; }
            public bool GeneratedMaterialsAssigned { get; set; }
            public bool OwnsEnabled { get; set; }
            public bool OriginalEnabled { get; }
            public bool DesiredEnabled { get; set; }
            public bool LastEnabled { get; set; }
            public bool LastEnabledValid { get; set; }
        }

        private sealed class SlotState
        {
            public SlotState(
                int rendererIndex,
                int materialSlot,
                Material originalMaterial,
                Renderer renderer,
                string diagnosticPath)
            {
                RendererIndex = rendererIndex;
                MaterialSlot = materialSlot;
                OriginalMaterial = originalMaterial;
                EffectiveMaterial = originalMaterial;
                DiagnosticPath = diagnosticPath;
                OriginalBlock = new MaterialPropertyBlock();
                WorkingBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(OriginalBlock, materialSlot);
                OriginalBlockWasEmpty = OriginalBlock.isEmpty;
                ReloadWorkingBlock(renderer);
                BaseMainColor = Color.white;
                DesiredMainColor = Color.white;
                LastMainColor = Color.white;
                OpacityFactor = 1f;
                VisibilityFactor = 1f;
            }

            public int RendererIndex { get; }
            public int MaterialSlot { get; }
            public Material OriginalMaterial { get; }
            public Material EffectiveMaterial { get; set; }
            public Material GeneratedMaterial { get; set; }
            public string DiagnosticPath { get; }
            public MaterialPropertyBlock OriginalBlock { get; }
            public MaterialPropertyBlock WorkingBlock { get; }
            public bool OriginalBlockWasEmpty { get; }
            public bool NeedsDirectMaterialClone { get; set; }
            public bool NeedsTransparentMode { get; set; }
            public bool NeedsMainColor { get; set; }
            public bool MainColorSupported { get; set; }
            public int BaseRenderQueue { get; set; }
            public Color BaseMainColor { get; set; }
            public Color DesiredMainColor { get; set; }
            public Color LastMainColor { get; set; }
            public bool LastMainColorValid { get; set; }
            public float OpacityFactor { get; set; }
            public float VisibilityFactor { get; set; }
            public bool BlockDirty { get; set; }

            public void ReloadWorkingBlock(Renderer renderer)
            {
                renderer.GetPropertyBlock(WorkingBlock, MaterialSlot);
                if (WorkingBlock.isEmpty)
                {
                    renderer.GetPropertyBlock(WorkingBlock);
                }
            }
        }

        private sealed class GroupState
        {
            private GroupState(GroupKind kind, int rendererIndex, int slotIndex, int propertyId, string propertyName)
            {
                Kind = kind;
                RendererIndex = rendererIndex;
                SlotIndex = slotIndex;
                PropertyId = propertyId;
                PropertyName = propertyName;
            }

            public GroupKind Kind { get; set; }
            public int RendererIndex { get; }
            public int SlotIndex { get; }
            public int PropertyId { get; }
            public string PropertyName { get; }
            public bool IsMainColor { get; set; }
            public double BaseScalar { get; set; }
            public double DesiredScalar { get; set; }
            public Vector4 BaseVector { get; set; }
            public Vector4 DesiredVector { get; set; }
            public Texture BaseTexture { get; set; }
            public Texture DesiredTexture { get; set; }
            public int BaseQueue { get; set; }
            public int DesiredQueue { get; set; }
            public bool BaseBool { get; set; }
            public bool DesiredBool { get; set; }
            public float LastScalar { get; set; }
            public bool LastScalarValid { get; set; }
            public Vector4 LastVector { get; set; }
            public bool LastVectorValid { get; set; }
            public Texture LastTexture { get; set; }
            public bool LastTextureValid { get; set; }
            public int LastQueue { get; set; }
            public bool LastQueueValid { get; set; }

            public static GroupState Unsupported()
            {
                return new GroupState(GroupKind.Unsupported, -1, -1, 0, string.Empty);
            }

            public static GroupState RendererVisibility(int rendererIndex, bool baseValue)
            {
                return new GroupState(GroupKind.RendererVisibility, rendererIndex, -1, 0, string.Empty)
                {
                    BaseBool = baseValue,
                    DesiredBool = baseValue,
                };
            }

            public static GroupState SlotVisibility(int slotIndex)
            {
                return new GroupState(GroupKind.SlotVisibility, -1, slotIndex, 0, string.Empty)
                {
                    BaseBool = true,
                    DesiredBool = true,
                };
            }

            public static GroupState Opacity(int slotIndex)
            {
                return new GroupState(GroupKind.Opacity, -1, slotIndex, MainColorId, MainColorProperty)
                {
                    BaseScalar = 1d,
                    DesiredScalar = 1d,
                };
            }

            public static GroupState Float(int slotIndex, int propertyId, string propertyName)
            {
                return new GroupState(GroupKind.Float, -1, slotIndex, propertyId, propertyName);
            }

            public static GroupState Color(int slotIndex, int propertyId, string propertyName)
            {
                return new GroupState(GroupKind.Color, -1, slotIndex, propertyId, propertyName);
            }

            public static GroupState Vector(int slotIndex, int propertyId, string propertyName)
            {
                return new GroupState(GroupKind.Vector, -1, slotIndex, propertyId, propertyName);
            }

            public static GroupState Texture(int slotIndex, int propertyId, string propertyName)
            {
                return new GroupState(GroupKind.Texture, -1, slotIndex, propertyId, propertyName);
            }

            public static GroupState RenderQueue(int slotIndex)
            {
                return new GroupState(GroupKind.RenderQueue, -1, slotIndex, 0, string.Empty);
            }

            public void Prepare(double baseWeight)
            {
                switch (Kind)
                {
                    case GroupKind.RendererVisibility:
                    case GroupKind.SlotVisibility:
                        DesiredBool = BaseBool;
                        break;
                    case GroupKind.Opacity:
                    case GroupKind.Float:
                        DesiredScalar = BaseScalar * baseWeight;
                        break;
                    case GroupKind.Color:
                    case GroupKind.Vector:
                        DesiredVector = BaseVector * ToFiniteFloat(baseWeight);
                        break;
                    case GroupKind.Texture:
                        DesiredTexture = BaseTexture;
                        break;
                    case GroupKind.RenderQueue:
                        DesiredQueue = BaseQueue;
                        break;
                }
            }

            public void ResetCacheToBase()
            {
                DesiredScalar = BaseScalar;
                DesiredVector = BaseVector;
                DesiredTexture = BaseTexture;
                DesiredQueue = BaseQueue;
                DesiredBool = BaseBool;
                LastScalar = ToFiniteFloat(BaseScalar);
                LastScalarValid = true;
                LastVector = BaseVector;
                LastVectorValid = true;
                LastTexture = BaseTexture;
                LastTextureValid = true;
                LastQueue = BaseQueue;
                LastQueueValid = true;
            }
        }

        private readonly struct SlotKey : IEquatable<SlotKey>
        {
            public SlotKey(Renderer renderer, int materialSlot)
            {
                Renderer = renderer;
                MaterialSlot = materialSlot;
            }

            private Renderer Renderer { get; }
            private int MaterialSlot { get; }

            public bool Equals(SlotKey other)
            {
                return ReferenceEquals(Renderer, other.Renderer) && MaterialSlot == other.MaterialSlot;
            }

            public override bool Equals(object obj)
            {
                return obj is SlotKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((Renderer == null ? 0 : Renderer.GetInstanceID()) * 397) ^ MaterialSlot;
                }
            }
        }

        private readonly struct GroupKey : IEquatable<GroupKey>
        {
            private GroupKey(int kind, int meshBinding, int targetIndex, string property)
            {
                Kind = kind;
                MeshBinding = meshBinding;
                TargetIndex = targetIndex;
                Property = property;
            }

            private int Kind { get; }
            private int MeshBinding { get; }
            private int TargetIndex { get; }
            private string Property { get; }

            public static GroupKey Create(VlatModifier modifier)
            {
                var visibility = modifier as VisibilityModifier;
                if (visibility != null)
                {
                    return new GroupKey(1, visibility.MeshBinding, visibility.SubMesh, null);
                }

                var opacity = modifier as OpacityModifier;
                if (opacity != null)
                {
                    return new GroupKey(2, opacity.MeshBinding, opacity.MaterialSlot, null);
                }

                var property = modifier as MaterialPropertyModifier;
                if (property != null)
                {
                    return new GroupKey(3, property.MeshBinding, property.MaterialSlot, property.Property);
                }

                var renderQueue = modifier as RenderQueueModifier;
                if (renderQueue != null)
                {
                    return new GroupKey(4, renderQueue.MeshBinding, renderQueue.MaterialSlot, null);
                }

                return new GroupKey(0, -1, -1, modifier.Type);
            }

            public bool Equals(GroupKey other)
            {
                return Kind == other.Kind &&
                    MeshBinding == other.MeshBinding &&
                    TargetIndex == other.TargetIndex &&
                    string.Equals(Property, other.Property, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is GroupKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = Kind;
                    hash = (hash * 397) ^ MeshBinding;
                    hash = (hash * 397) ^ TargetIndex;
                    return (hash * 397) ^ (Property == null ? 0 : StringComparer.Ordinal.GetHashCode(Property));
                }
            }
        }
    }
}
