using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VividRP.Runtime
{
    internal enum VirtualTextureStatsViewMode
    {
        [InspectorName("Auto (Focused Window)")]
        Auto = 0,
        Global = 1,
        [InspectorName("Selected Camera")]
        SelectedCamera = 2,
    }

    internal readonly struct VTDebugStats
    {
        internal VTDebugStats(
            int activeSpaceCount,
            int residentPageCount,
            int freePageCount,
            int pendingUploadCount,
            int evictionCount,
            int faultCount,
            int deduplicatedRequestCount,
            int feedbackOverflowCount,
            int inFlightUploadBatchCount,
            int duplicateUploadCount,
            int skippedUploadCount,
            int fallbackSampleCount,
            int lastReadbackFrame,
            string statusMessage)
            : this(
                activeSpaceCount,
                residentPageCount,
                freePageCount,
                pendingUploadCount,
                evictionCount,
                faultCount,
                deduplicatedRequestCount,
                feedbackOverflowCount,
                inFlightUploadBatchCount,
                duplicateUploadCount,
                skippedUploadCount,
                fallbackSampleCount,
                lastReadbackFrame,
                statusMessage,
                VirtualTextureViewId.Invalid,
                default,
                null,
                -1,
                0,
                0,
                0,
                0,
                false,
                0,
                false)
        {
        }

        internal VTDebugStats(
            int activeSpaceCount,
            int residentPageCount,
            int freePageCount,
            int pendingUploadCount,
            int evictionCount,
            int faultCount,
            int deduplicatedRequestCount,
            int feedbackOverflowCount,
            int inFlightUploadBatchCount,
            int duplicateUploadCount,
            int skippedUploadCount,
            int fallbackSampleCount,
            int lastReadbackFrame,
            string statusMessage,
            VirtualTextureViewId viewId,
            CameraType cameraType,
            string cameraName,
            int cameraFrameIndex,
            int actualWidth,
            int actualHeight,
            int pixelWidth,
            int pixelHeight,
            bool feedbackSupported,
            int feedbackCapacity,
            bool isViewSpecific)
        {
            ActiveSpaceCount = activeSpaceCount;
            ResidentPageCount = residentPageCount;
            FreePageCount = freePageCount;
            PendingUploadCount = pendingUploadCount;
            EvictionCount = evictionCount;
            FaultCount = faultCount;
            DeduplicatedRequestCount = deduplicatedRequestCount;
            FeedbackOverflowCount = feedbackOverflowCount;
            InFlightUploadBatchCount = inFlightUploadBatchCount;
            DuplicateUploadCount = duplicateUploadCount;
            SkippedUploadCount = skippedUploadCount;
            FallbackSampleCount = fallbackSampleCount;
            LastReadbackFrame = lastReadbackFrame;
            StatusMessage = statusMessage;
            ViewId = viewId;
            CameraType = cameraType;
            CameraName = cameraName;
            CameraFrameIndex = cameraFrameIndex;
            ActualWidth = actualWidth;
            ActualHeight = actualHeight;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            FeedbackSupported = feedbackSupported;
            FeedbackCapacity = feedbackCapacity;
            IsViewSpecific = isViewSpecific;
        }

        internal int ActiveSpaceCount { get; }

        internal int ResidentPageCount { get; }

        internal int FreePageCount { get; }

        internal int PendingUploadCount { get; }

        internal int EvictionCount { get; }

        internal int FaultCount { get; }

        internal int DeduplicatedRequestCount { get; }

        internal int FeedbackOverflowCount { get; }

        internal int InFlightUploadBatchCount { get; }

        internal int DuplicateUploadCount { get; }

        internal int SkippedUploadCount { get; }

        internal int FallbackSampleCount { get; }

        internal int LastReadbackFrame { get; }

        internal string StatusMessage { get; }

        internal VirtualTextureViewId ViewId { get; }

        internal CameraType CameraType { get; }

        internal string CameraName { get; }

        internal int CameraFrameIndex { get; }

        internal int ActualWidth { get; }

        internal int ActualHeight { get; }

        internal int PixelWidth { get; }

        internal int PixelHeight { get; }

        internal bool FeedbackSupported { get; }

        internal int FeedbackCapacity { get; }

        internal bool IsViewSpecific { get; }

        internal string ViewLabel
        {
            get
            {
                if (!IsViewSpecific)
                    return "Global";

                if (!ViewId.IsValid && string.IsNullOrEmpty(CameraName))
                    return "Selected Camera / <none>";

                string cameraName = string.IsNullOrEmpty(CameraName) ? "<unnamed>" : CameraName;
                return $"{CameraType} / {cameraName}";
            }
        }

        internal string RenderSizeLabel => ActualWidth > 0 && ActualHeight > 0
            ? $"{ActualWidth} x {ActualHeight}"
            : "N/A";

        internal string PixelSizeLabel => PixelWidth > 0 && PixelHeight > 0
            ? $"{PixelWidth} x {PixelHeight}"
            : "N/A";

        internal VTDebugStats WithLastReadbackFrame(int lastReadbackFrame)
        {
            return new VTDebugStats(
                ActiveSpaceCount,
                ResidentPageCount,
                FreePageCount,
                PendingUploadCount,
                EvictionCount,
                FaultCount,
                DeduplicatedRequestCount,
                FeedbackOverflowCount,
                InFlightUploadBatchCount,
                DuplicateUploadCount,
                SkippedUploadCount,
                FallbackSampleCount,
                lastReadbackFrame,
                StatusMessage,
                ViewId,
                CameraType,
                CameraName,
                CameraFrameIndex,
                ActualWidth,
                ActualHeight,
                PixelWidth,
                PixelHeight,
                FeedbackSupported,
                FeedbackCapacity,
                IsViewSpecific);
        }
    }

    internal static class VTDebugStatsRegistry
    {
        private static VTDebugStats s_LastStats;
        private static readonly Dictionary<VirtualTextureViewId, VTDebugStats> s_LastStatsByViewId = new();
        private static readonly Dictionary<CameraType, VTDebugStats> s_LastStatsByCameraType = new();

        private static bool s_HasFocusedViewOverride;
        private static VirtualTextureViewId s_FocusedViewOverrideId = VirtualTextureViewId.Invalid;
        private static CameraType s_FocusedViewOverrideCameraType;

#if UNITY_EDITOR
        private static VirtualTextureViewId s_LastEditorFocusedViewId = VirtualTextureViewId.Invalid;
        private static CameraType? s_LastEditorFocusedCameraType;
        private static readonly Dictionary<Type, bool> s_EditorGameViewTypeCache = new();
#endif

        internal static VTDebugStats LastStats => s_LastStats;

        internal static VTDebugStats DisplayStats => TryGetDisplayStats(out VTDebugStats stats)
            ? stats
            : s_LastStats;

        internal static VTDebugStats GetDisplayStats(
            VirtualTextureStatsViewMode viewMode,
            Camera selectedCamera)
        {
            return viewMode switch
            {
                VirtualTextureStatsViewMode.Global => s_LastStats,
                VirtualTextureStatsViewMode.SelectedCamera => GetSelectedCameraStats(selectedCamera),
                _ => DisplayStats,
            };
        }

        internal static void Report(in VTDebugStats stats)
        {
            s_LastStats = stats.LastReadbackFrame < 0 && s_LastStats.LastReadbackFrame >= 0
                ? stats.WithLastReadbackFrame(s_LastStats.LastReadbackFrame)
                : stats;
        }

        internal static void ReportView(in VTDebugStats stats)
        {
            if (!stats.IsViewSpecific)
                return;

            VTDebugStats reportedStats = stats;
            if (reportedStats.LastReadbackFrame < 0)
            {
                if (reportedStats.ViewId.IsValid
                    && s_LastStatsByViewId.TryGetValue(reportedStats.ViewId, out VTDebugStats previousViewStats)
                    && previousViewStats.LastReadbackFrame >= 0)
                {
                    reportedStats = reportedStats.WithLastReadbackFrame(previousViewStats.LastReadbackFrame);
                }
                else if (!reportedStats.ViewId.IsValid
                         && s_LastStatsByCameraType.TryGetValue(reportedStats.CameraType, out VTDebugStats previousCameraTypeStats)
                         && previousCameraTypeStats.LastReadbackFrame >= 0)
                {
                    reportedStats = reportedStats.WithLastReadbackFrame(previousCameraTypeStats.LastReadbackFrame);
                }
            }

            if (reportedStats.ViewId.IsValid)
                s_LastStatsByViewId[reportedStats.ViewId] = reportedStats;

            s_LastStatsByCameraType[reportedStats.CameraType] = reportedStats;
        }

        internal static void Clear()
        {
            s_LastStats = default;
            s_LastStatsByViewId.Clear();
            s_LastStatsByCameraType.Clear();
            ClearFocusedViewOverrideForTesting();
#if UNITY_EDITOR
            s_LastEditorFocusedViewId = VirtualTextureViewId.Invalid;
            s_LastEditorFocusedCameraType = null;
#endif
        }

        internal static void SetFocusedViewOverrideForTesting(
            VirtualTextureViewId viewId,
            CameraType cameraType)
        {
            s_HasFocusedViewOverride = true;
            s_FocusedViewOverrideId = viewId;
            s_FocusedViewOverrideCameraType = cameraType;
        }

        internal static void ClearFocusedViewOverrideForTesting()
        {
            s_HasFocusedViewOverride = false;
            s_FocusedViewOverrideId = VirtualTextureViewId.Invalid;
            s_FocusedViewOverrideCameraType = default;
        }

        internal static bool TryGetFocusedViewForSystem(
            out VirtualTextureViewId viewId,
            out CameraType cameraType)
        {
            if (s_HasFocusedViewOverride)
            {
                viewId = s_FocusedViewOverrideId;
                cameraType = s_FocusedViewOverrideCameraType;
                return true;
            }

#if UNITY_EDITOR
            return TryGetEditorFocusedView(out viewId, out cameraType);
#else
            viewId = VirtualTextureViewId.Invalid;
            cameraType = default;
            return false;
#endif
        }

        private static bool TryGetDisplayStats(out VTDebugStats stats)
        {
            stats = default;

            if (s_HasFocusedViewOverride)
                return TryGetViewStats(s_FocusedViewOverrideId, s_FocusedViewOverrideCameraType, out stats);

#if UNITY_EDITOR
            if (TryGetEditorFocusedView(out VirtualTextureViewId focusedViewId, out CameraType focusedCameraType)
                && TryGetViewStats(focusedViewId, focusedCameraType, out stats))
            {
                return true;
            }
#endif

            return false;
        }

        private static bool TryGetViewStats(
            VirtualTextureViewId viewId,
            CameraType cameraType,
            out VTDebugStats stats)
        {
            if (viewId.IsValid && s_LastStatsByViewId.TryGetValue(viewId, out stats))
                return true;

            if (s_LastStatsByCameraType.TryGetValue(cameraType, out stats))
                return true;

            stats = default;
            return false;
        }

        private static VTDebugStats GetSelectedCameraStats(Camera selectedCamera)
        {
            if (selectedCamera == null)
            {
                return CreateUnavailableViewStats(
                    null,
                    "[VividRP] Select a camera to inspect VT stats.");
            }

            VirtualTextureViewId viewId = VirtualTextureViewId.FromCamera(selectedCamera);
            if (viewId.IsValid && s_LastStatsByViewId.TryGetValue(viewId, out VTDebugStats stats))
                return stats;

            return CreateUnavailableViewStats(
                selectedCamera,
                "[VividRP] No VT stats are available for the selected camera yet.");
        }

        private static VTDebugStats CreateUnavailableViewStats(
            Camera camera,
            string statusMessage)
        {
            CameraType cameraType = camera != null ? camera.cameraType : default;
            return new VTDebugStats(
                s_LastStats.ActiveSpaceCount,
                s_LastStats.ResidentPageCount,
                s_LastStats.FreePageCount,
                s_LastStats.PendingUploadCount,
                s_LastStats.EvictionCount,
                0,
                0,
                0,
                s_LastStats.InFlightUploadBatchCount,
                s_LastStats.DuplicateUploadCount,
                s_LastStats.SkippedUploadCount,
                0,
                -1,
                statusMessage,
                VirtualTextureViewId.FromCamera(camera),
                cameraType,
                camera != null ? camera.name : null,
                -1,
                ResolveCameraActualWidth(camera),
                ResolveCameraActualHeight(camera),
                camera != null ? camera.pixelWidth : 0,
                camera != null ? camera.pixelHeight : 0,
                IsFeedbackSupported(cameraType),
                s_LastStats.FeedbackCapacity,
                true);
        }

        private static int ResolveCameraActualWidth(Camera camera)
        {
            if (camera == null)
                return 0;

            int width = camera.scaledPixelWidth > 0 ? camera.scaledPixelWidth : camera.pixelWidth;
            return Mathf.Max(0, width);
        }

        private static int ResolveCameraActualHeight(Camera camera)
        {
            if (camera == null)
                return 0;

            int height = camera.scaledPixelHeight > 0 ? camera.scaledPixelHeight : camera.pixelHeight;
            return Mathf.Max(0, height);
        }

        private static bool IsFeedbackSupported(CameraType cameraType)
        {
            return cameraType == CameraType.Game || cameraType == CameraType.SceneView;
        }

#if UNITY_EDITOR
        private static bool TryGetEditorFocusedView(out VirtualTextureViewId viewId, out CameraType cameraType)
        {
            if (TryGetEditorViewFromWindow(EditorWindow.focusedWindow, out viewId, out cameraType)
                || TryGetEditorViewFromWindow(EditorWindow.mouseOverWindow, out viewId, out cameraType))
            {
                s_LastEditorFocusedViewId = viewId;
                s_LastEditorFocusedCameraType = cameraType;
                return true;
            }

            if (s_LastEditorFocusedCameraType.HasValue)
            {
                viewId = s_LastEditorFocusedViewId;
                cameraType = s_LastEditorFocusedCameraType.Value;
                return true;
            }

            viewId = VirtualTextureViewId.Invalid;
            cameraType = default;
            return false;
        }

        private static bool TryGetEditorViewFromWindow(
            EditorWindow window,
            out VirtualTextureViewId viewId,
            out CameraType cameraType)
        {
            viewId = VirtualTextureViewId.Invalid;
            cameraType = default;

            if (window == null)
                return false;

            if (window is SceneView sceneView)
            {
                cameraType = CameraType.SceneView;
                viewId = VirtualTextureViewId.FromCamera(sceneView.camera);
                return true;
            }

            Type windowType = window.GetType();
            if (IsEditorGameViewType(windowType))
            {
                cameraType = CameraType.Game;
                return true;
            }

            return false;
        }

        private static bool IsEditorGameViewType(Type windowType)
        {
            if (windowType == null)
                return false;

            if (s_EditorGameViewTypeCache.TryGetValue(windowType, out bool isGameViewType))
                return isGameViewType;

            string typeName = windowType.Name;
            isGameViewType = string.Equals(typeName, "GameView", StringComparison.Ordinal)
                             || string.Equals(typeName, "PlayModeView", StringComparison.Ordinal)
                             || typeName.EndsWith("GameView", StringComparison.Ordinal);
            s_EditorGameViewTypeCache.Add(windowType, isGameViewType);
            return isGameViewType;
        }
#endif
    }
}
