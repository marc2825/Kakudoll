using System;
using System.Diagnostics;
using Unity.Profiling;

namespace VLat.Editor.Authoring
{
    // 編集操作の処理区間。Unity Profilerの"VLat.Editor.*" markerと、試験専用observerの両方へ出す。
    internal enum VlatEditorPhase
    {
        DragBeginPack,
        DragBeginFingerprint,
        DragBeginBinding,
        DragBeginSkinning,
        DragMoveCompute,
        DragMoveUpload,
        DragCommitVerify,
        DragCommitPack,
        DragCommitFingerprint,
        DragCommitCollect,
        CommitPrecheck,
        CommitUndoSnapshot,
        ValidateEditingPack,
        ValidateEditingFingerprint,
        RecoveryWrite,
        RecoverySerialize,
        RecoveryHash,
        RecoveryFileWrite,
        UndoRefresh,
        WindowRefreshAll,
        PreviewEvaluate,
        Save,
        Open,
        PackStructure,
        PackPlan,
        PackBinary,
        PackBuild,
        PackCoreValidate,
        PackJson,
        BindingPaths,
        BindingTopology,
        BindingPositionHash,
        BindingProbe,
        PreviewResolve,
        PreviewKeyformBuild,
        DragSkinUpload,
        DragSkinBake,
        DragSkinColumns,
        DragSkinInverse,
        Count,
    }

    /// <summary>
    /// VLat Editorの処理区間を計測するための内部helperです。
    /// 通常は<see cref="ProfilerMarker"/>だけを開始/終了し、managed割当を発生させません。
    /// 試験・性能診断ではobserverを設定すると区間ごとのStopwatch tickを受け取れます。
    /// </summary>
    internal static class VlatEditorProfiling
    {
        private static readonly ProfilerMarker[] Markers = CreateMarkers();

        /// <summary>
        /// 試験専用の区間観測者です。既定はnullで、製品動作では呼び出しません。
        /// 引数は区間と、<see cref="Stopwatch.GetTimestamp"/>単位の経過tickです。
        /// observerがmanaged割当を行うと計測対象の割当件数へ混入するため、事前確保した配列へ加算してください。
        /// </summary>
        internal static Action<VlatEditorPhase, long> Observer;

        /// <summary>区間計測を開始します。<c>using</c>で囲んで使用します。</summary>
        internal static Scope Measure(VlatEditorPhase phase) => new Scope(phase);

        private static ProfilerMarker[] CreateMarkers()
        {
            var markers = new ProfilerMarker[(int)VlatEditorPhase.Count];
            for (var i = 0; i < markers.Length; i++)
                markers[i] = new ProfilerMarker("VLat.Editor." + ((VlatEditorPhase)i).ToString());
            return markers;
        }

        /// <summary>区間計測のscopeです。値型のため<c>using</c>でboxingしません。</summary>
        internal readonly struct Scope : IDisposable
        {
            private readonly VlatEditorPhase _phase;
            private readonly long _started;

            internal Scope(VlatEditorPhase phase)
            {
                _phase = phase;
                Markers[(int)phase].Begin();
                _started = Stopwatch.GetTimestamp();
            }

            /// <summary>区間計測を終了し、observerがあれば経過tickを通知します。</summary>
            public void Dispose()
            {
                var elapsed = Stopwatch.GetTimestamp() - _started;
                Markers[(int)_phase].End();
                Observer?.Invoke(_phase, elapsed);
            }
        }
    }
}
