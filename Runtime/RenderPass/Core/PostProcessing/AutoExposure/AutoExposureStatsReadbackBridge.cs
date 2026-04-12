#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal readonly struct AutoExposureStatsReadbackSnapshot
    {
        public readonly Camera camera;
        public readonly string cameraName;
        public readonly int frameIndex;
        public readonly AutoExposureSettingsData settings;
        public readonly Vector4 exposureState;
        public readonly Vector4 preExposureState;
        public readonly uint[] histogram;
        public readonly bool exposureEnabled;
        public readonly bool autoExposureEnabled;
        public readonly bool hasValidHistory;
        public readonly bool hasExposureState;
        public readonly bool hasPreExposureState;
        public readonly bool hasHistogram;

        public AutoExposureStatsReadbackSnapshot(
            Camera camera,
            string cameraName,
            int frameIndex,
            AutoExposureSettingsData settings,
            Vector4 exposureState,
            Vector4 preExposureState,
            uint[] histogram,
            bool exposureEnabled,
            bool autoExposureEnabled,
            bool hasValidHistory,
            bool hasExposureState,
            bool hasPreExposureState,
            bool hasHistogram)
        {
            this.camera = camera;
            this.cameraName = cameraName;
            this.frameIndex = frameIndex;
            this.settings = settings;
            this.exposureState = exposureState;
            this.preExposureState = preExposureState;
            this.histogram = histogram;
            this.exposureEnabled = exposureEnabled;
            this.autoExposureEnabled = autoExposureEnabled;
            this.hasValidHistory = hasValidHistory;
            this.hasExposureState = hasExposureState;
            this.hasPreExposureState = hasPreExposureState;
            this.hasHistogram = hasHistogram;
        }
    }

    internal static class AutoExposureStatsReadbackBridge
    {
        private const int HistogramBucketCount = 64;
        private const float InspectorRequestTimeoutSeconds = 1.0f;
        private const float SnapshotStaleTimeoutSeconds = 5.0f;

        private static readonly Dictionary<Camera, SnapshotState> s_Snapshots = new();
        private static readonly Vector4 s_DefaultExposureState = new(1f, 1f, AutoExposureSettingsResolver.MiddleGrey, 1f);

        private static float s_LastInspectorRequestTime = float.NegativeInfinity;

        private sealed class SnapshotState
        {
            public readonly uint[] histogram = new uint[HistogramBucketCount];

            public string cameraName = string.Empty;
            public AutoExposureSettingsData settings = AutoExposureSettingsData.CreateDefault();
            public Vector4 exposureState = s_DefaultExposureState;
            public Vector4 preExposureState = s_DefaultExposureState;
            public bool exposureEnabled;
            public bool autoExposureEnabled;
            public bool hasValidHistory;
            public bool hasExposureState;
            public bool hasPreExposureState;
            public bool hasHistogram;
            public bool exposurePending;
            public bool preExposurePending;
            public bool histogramPending;
            public int frameIndex;
            public float lastTouchedTime = float.NegativeInfinity;
            public float lastCompletedTime = float.NegativeInfinity;
        }

        internal static void TouchInspectorRequest()
        {
            s_LastInspectorRequestTime = Time.realtimeSinceStartup;
        }

        internal static bool TryGetLatestSnapshot(out AutoExposureStatsReadbackSnapshot snapshot)
        {
            PruneStaleSnapshots();

            SnapshotState latestState = null;
            Camera latestCamera = null;
            var latestTime = float.NegativeInfinity;

            foreach (var pair in s_Snapshots)
            {
                var state = pair.Value;
                if (state == null)
                    continue;

                var candidateTime = Mathf.Max(state.lastTouchedTime, state.lastCompletedTime);
                if (candidateTime <= latestTime)
                    continue;

                latestState = state;
                latestCamera = pair.Key;
                latestTime = candidateTime;
            }

            if (latestState == null)
            {
                snapshot = default;
                return false;
            }

            snapshot = new AutoExposureStatsReadbackSnapshot(
                latestCamera,
                latestState.cameraName,
                latestState.frameIndex,
                latestState.settings,
                latestState.exposureState,
                latestState.preExposureState,
                latestState.histogram,
                latestState.exposureEnabled,
                latestState.autoExposureEnabled,
                latestState.hasValidHistory,
                latestState.hasExposureState,
                latestState.hasPreExposureState,
                latestState.hasHistogram);
            return true;
        }

        internal static void Request(
            CommandBuffer commandBuffer,
            Camera camera,
            AutoExposureSettingsData settings,
            bool exposureEnabled,
            bool autoExposureEnabled,
            bool hasValidHistory,
            GraphicsBuffer exposureBuffer,
            GraphicsBuffer preExposureBuffer,
            GraphicsBuffer histogramBuffer)
        {
            if (!IsInspectorRequestActive() || commandBuffer == null || camera == null)
                return;

            var state = GetOrCreateState(camera);
            state.cameraName = camera.name;
            state.settings = settings;
            state.exposureEnabled = exposureEnabled;
            state.autoExposureEnabled = autoExposureEnabled;
            state.hasValidHistory = hasValidHistory;
            state.frameIndex = Time.frameCount;
            state.lastTouchedTime = Time.realtimeSinceStartup;

            if (!exposureEnabled)
            {
                state.hasExposureState = false;
                state.hasPreExposureState = false;
                state.hasHistogram = false;
                state.exposurePending = false;
                state.preExposurePending = false;
                state.histogramPending = false;
                Array.Clear(state.histogram, 0, state.histogram.Length);
                return;
            }

            if (exposureBuffer != null && !state.exposurePending)
            {
                state.exposurePending = true;
                commandBuffer.RequestAsyncReadback(exposureBuffer, request => HandleExposureReadback(camera, request));
            }

            if (preExposureBuffer != null && !state.preExposurePending)
            {
                state.preExposurePending = true;
                commandBuffer.RequestAsyncReadback(preExposureBuffer, request => HandlePreExposureReadback(camera, request));
            }
            else if (preExposureBuffer == null)
            {
                state.hasPreExposureState = false;
                state.preExposurePending = false;
            }

            if (autoExposureEnabled && histogramBuffer != null)
            {
                if (!state.histogramPending)
                {
                    state.histogramPending = true;
                    commandBuffer.RequestAsyncReadback(histogramBuffer, request => HandleHistogramReadback(camera, request));
                }
            }
            else
            {
                state.hasHistogram = false;
                state.histogramPending = false;
                Array.Clear(state.histogram, 0, state.histogram.Length);
            }
        }

        private static bool IsInspectorRequestActive()
        {
            return Time.realtimeSinceStartup - s_LastInspectorRequestTime <= InspectorRequestTimeoutSeconds;
        }

        private static SnapshotState GetOrCreateState(Camera camera)
        {
            if (s_Snapshots.TryGetValue(camera, out var state) && state != null)
                return state;

            state = new SnapshotState();
            s_Snapshots[camera] = state;
            return state;
        }

        private static void HandleExposureReadback(Camera camera, AsyncGPUReadbackRequest request)
        {
            if (!s_Snapshots.TryGetValue(camera, out var state) || state == null)
                return;

            state.exposurePending = false;
            if (request.hasError)
            {
                state.hasExposureState = false;
                return;
            }

            var data = request.GetData<Vector4>();
            if (data.Length < 1)
            {
                state.hasExposureState = false;
                return;
            }

            state.exposureState = data[0];
            state.hasExposureState = true;
            state.lastCompletedTime = Time.realtimeSinceStartup;
        }

        private static void HandlePreExposureReadback(Camera camera, AsyncGPUReadbackRequest request)
        {
            if (!s_Snapshots.TryGetValue(camera, out var state) || state == null)
                return;

            state.preExposurePending = false;
            if (request.hasError)
            {
                state.hasPreExposureState = false;
                return;
            }

            var data = request.GetData<Vector4>();
            if (data.Length < 1)
            {
                state.hasPreExposureState = false;
                return;
            }

            state.preExposureState = data[0];
            state.hasPreExposureState = true;
            state.lastCompletedTime = Time.realtimeSinceStartup;
        }

        private static void HandleHistogramReadback(Camera camera, AsyncGPUReadbackRequest request)
        {
            if (!s_Snapshots.TryGetValue(camera, out var state) || state == null)
                return;

            state.histogramPending = false;
            if (request.hasError)
            {
                state.hasHistogram = false;
                Array.Clear(state.histogram, 0, state.histogram.Length);
                return;
            }

            var data = request.GetData<uint>();
            var count = Mathf.Min(data.Length, state.histogram.Length);
            for (var i = 0; i < count; i++)
                state.histogram[i] = data[i];

            for (var i = count; i < state.histogram.Length; i++)
                state.histogram[i] = 0;

            state.hasHistogram = count > 0;
            state.lastCompletedTime = Time.realtimeSinceStartup;
        }

        private static void PruneStaleSnapshots()
        {
            if (s_Snapshots.Count == 0)
                return;

            var now = Time.realtimeSinceStartup;
            List<Camera> staleCameras = null;

            foreach (var pair in s_Snapshots)
            {
                var state = pair.Value;
                if (state == null)
                    continue;

                if (pair.Key == null)
                {
                    staleCameras ??= new List<Camera>();
                    staleCameras.Add(pair.Key);
                    continue;
                }

                var age = now - Mathf.Max(state.lastTouchedTime, state.lastCompletedTime);
                if (age <= SnapshotStaleTimeoutSeconds)
                    continue;

                staleCameras ??= new List<Camera>();
                staleCameras.Add(pair.Key);
            }

            if (staleCameras == null)
                return;

            for (var i = 0; i < staleCameras.Count; i++)
                s_Snapshots.Remove(staleCameras[i]);
        }
    }
}
#endif
