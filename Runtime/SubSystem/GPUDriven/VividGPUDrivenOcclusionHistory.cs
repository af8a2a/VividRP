using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven
{
    internal readonly struct VividGPUDrivenOcclusionCullingParameters
    {
        internal VividGPUDrivenOcclusionCullingParameters(
            RTHandle depthPyramid,
            Matrix4x4 viewProjectionMatrix,
            int width,
            int height,
            int textureWidth,
            int textureHeight,
            int mipCount,
            float depthBias)
        {
            DepthPyramid = depthPyramid;
            ViewProjectionMatrix = viewProjectionMatrix;
            Width = width;
            Height = height;
            TextureWidth = textureWidth;
            TextureHeight = textureHeight;
            MipCount = mipCount;
            DepthBias = depthBias;
        }

        internal RTHandle DepthPyramid { get; }

        internal Matrix4x4 ViewProjectionMatrix { get; }

        internal int Width { get; }

        internal int Height { get; }

        internal int TextureWidth { get; }

        internal int TextureHeight { get; }

        internal int MipCount { get; }

        internal float DepthBias { get; }

        internal bool IsEnabled => DepthPyramid != null
            && Width > 0
            && Height > 0
            && TextureWidth >= Width
            && TextureHeight >= Height
            && MipCount > 0;
    }

    internal sealed class VividGPUDrivenOcclusionHistoryState : CameraRelativeState
    {
        internal CameraHistoryTexture History;
        internal bool HasLatestParameters;
        internal bool HasPreviousParameters;
        internal VividGPUDrivenOcclusionCullingParameters LatestParameters;
        internal VividGPUDrivenOcclusionCullingParameters PreviousParameters;

        internal void InvalidateSnapshots()
        {
            History = null;
            HasLatestParameters = false;
            HasPreviousParameters = false;
            LatestParameters = default;
            PreviousParameters = default;
        }

        public override void Dispose()
        {
            InvalidateSnapshots();
        }
    }

    internal static class VividGPUDrivenOcclusionHistorySystem
    {
        // FidelityFX SPD generates at most 12 mips from mip 0 in one dispatch.
        internal const int MaxMipCount = 13;
        internal const float ConservativeDepthBias = 0.0005f;

        private static readonly CameraRelativeSystem<VividGPUDrivenOcclusionHistoryState> s_States = new();

        internal static CameraHistoryTexture PrepareCurrent(
            Camera camera,
            int width,
            int height)
        {
            if (camera == null
                || camera.stereoEnabled
                || !SystemInfo.IsFormatSupported(
                    GraphicsFormat.R32_SFloat,
                    GraphicsFormatUsage.Sample | GraphicsFormatUsage.LoadStore))
            {
                return null;
            }

            var cameraHistory = camera.GetVividCameraHistory();
            if (!cameraHistory.IsFrameActive)
                return null;

            return cameraHistory.GetOrCreateTexture(
                CameraHistoryIds.GPUDrivenOccluderDepthPyramid,
                2,
                CreateDescriptor(width, height));
        }

        internal static bool TryGetPreviousParameters(
            Camera camera,
            bool featureEnabled,
            bool resetHistory,
            int currentWidth,
            int currentHeight,
            out VividGPUDrivenOcclusionCullingParameters parameters)
        {
            parameters = default;
            if (!featureEnabled
                || resetHistory
                || camera == null
                || camera.stereoEnabled
                || !s_States.TryGetBase(camera, out var state)
                || state == null
                || !state.HasLatestParameters
                || state.LatestParameters.Width != currentWidth
                || state.LatestParameters.Height != currentHeight
                || !IsUsableSnapshot(state.LatestParameters))
            {
                return false;
            }

            var cameraHistory = camera.GetVividCameraHistory();
            if (!cameraHistory.TryGetTexture(
                    CameraHistoryIds.GPUDrivenOccluderDepthPyramid,
                    out var history)
                || history == null
                || !ReferenceEquals(history, state.History)
                || !history.IsValid(1)
                || !IsCompatible(state.LatestParameters, history.Descriptor))
            {
                return false;
            }

            var previous = history.GetPrevious();
            if (previous == null)
                return false;

            parameters = new VividGPUDrivenOcclusionCullingParameters(
                previous,
                state.LatestParameters.ViewProjectionMatrix,
                state.LatestParameters.Width,
                state.LatestParameters.Height,
                state.LatestParameters.TextureWidth,
                state.LatestParameters.TextureHeight,
                state.LatestParameters.MipCount,
                ConservativeDepthBias);
            return true;
        }

        internal static bool TryGetObservationParameters(
            Camera camera,
            out VividGPUDrivenOcclusionCullingParameters testAllParameters,
            out VividGPUDrivenOcclusionCullingParameters testCulledParameters)
        {
            testAllParameters = default;
            testCulledParameters = default;
            if (camera == null
                || camera.stereoEnabled
                || !s_States.TryGetBase(camera, out var state)
                || state == null
                || !state.HasPreviousParameters
                || !state.HasLatestParameters
                || !IsUsableSnapshot(state.PreviousParameters)
                || !IsUsableSnapshot(state.LatestParameters)
                || !AreCompatibleSnapshots(state.PreviousParameters, state.LatestParameters))
            {
                return false;
            }

            var cameraHistory = camera.GetVividCameraHistory();
            if (!cameraHistory.TryGetTexture(
                    CameraHistoryIds.GPUDrivenOccluderDepthPyramid,
                    out var history)
                || history == null
                || !ReferenceEquals(history, state.History)
                || !IsCompatible(state.LatestParameters, history.Descriptor))
            {
                return false;
            }

            testAllParameters = state.PreviousParameters;
            testCulledParameters = state.LatestParameters;
            return true;
        }

        internal static void InvalidateSnapshots(Camera camera)
        {
            if (camera != null && s_States.TryGetBase(camera, out var state))
                state?.InvalidateSnapshots();
        }

        internal static bool CommitCurrent(
            Camera camera,
            CameraHistoryTexture history,
            Matrix4x4 viewProjectionMatrix,
            int width,
            int height,
            int textureWidth,
            int textureHeight,
            int mipCount)
        {
            if (camera == null
                || history == null
                || !IsFinite(viewProjectionMatrix)
                || width <= 0
                || height <= 0
                || textureWidth < width
                || textureHeight < height
                || mipCount <= 0)
            {
                return false;
            }

            RTHandle current = history.GetCurrent();
            if (current == null)
                return false;

            var currentParameters = new VividGPUDrivenOcclusionCullingParameters(
                current,
                viewProjectionMatrix,
                width,
                height,
                textureWidth,
                textureHeight,
                Mathf.Clamp(mipCount, 1, MaxMipCount),
                ConservativeDepthBias);
            var state = s_States.GetOrCreateBase(camera);
            bool continuesSameHistory = ReferenceEquals(state.History, history)
                && state.HasLatestParameters
                && IsUsableSnapshot(state.LatestParameters)
                && !ReferenceEquals(state.LatestParameters.DepthPyramid, current);
            if (continuesSameHistory)
            {
                state.PreviousParameters = state.LatestParameters;
                state.HasPreviousParameters = true;
            }
            else if (!ReferenceEquals(state.History, history) || !state.HasLatestParameters)
            {
                state.PreviousParameters = default;
                state.HasPreviousParameters = false;
            }

            state.History = history;
            state.LatestParameters = currentParameters;
            state.HasLatestParameters = true;
            history.MarkWritten();
            return true;
        }

        internal static int CalculateMipCount(int width, int height)
        {
            int maxDimension = Mathf.Max(1, Mathf.Max(width, height));
            return Mathf.Clamp(Mathf.FloorToInt(Mathf.Log(maxDimension, 2.0f)) + 1, 1, MaxMipCount);
        }

        internal static int CalculateTextureDimension(int viewportDimension)
        {
            return Mathf.Max(1, viewportDimension);
        }

        internal static void PurgeDestroyedCameras()
        {
            s_States.PurgeDestroyedCameras();
        }

        internal static void Clear()
        {
            s_States.Dispose();
        }

        private static CameraHistoryTextureDescriptor CreateDescriptor(int width, int height)
        {
            return new CameraHistoryTextureDescriptor(
                CalculateTextureDimension(width),
                CalculateTextureDimension(height),
                GraphicsFormat.R32_SFloat,
                filterMode: FilterMode.Point,
                wrapMode: TextureWrapMode.Clamp,
                enableRandomWrite: true,
                useMipMap: true,
                autoGenerateMips: false);
        }

        private static bool IsCompatible(
            in VividGPUDrivenOcclusionCullingParameters parameters,
            in CameraHistoryTextureDescriptor descriptor)
        {
            return descriptor.Dimension == TextureDimension.Tex2D
                && descriptor.Slices == 1
                && descriptor.MsaaSamples == MSAASamples.None
                && descriptor.ColorFormat == GraphicsFormat.R32_SFloat
                && descriptor.EnableRandomWrite
                && descriptor.UseMipMap
                && parameters.Width > 0
                && parameters.Height > 0
                && parameters.TextureWidth == descriptor.Width
                && parameters.TextureHeight == descriptor.Height
                && parameters.Width <= parameters.TextureWidth
                && parameters.Height <= parameters.TextureHeight
                && parameters.MipCount == CalculateMipCount(descriptor.Width, descriptor.Height);
        }

        private static bool AreCompatibleSnapshots(
            in VividGPUDrivenOcclusionCullingParameters previous,
            in VividGPUDrivenOcclusionCullingParameters latest)
        {
            return previous.Width == latest.Width
                && previous.Height == latest.Height
                && previous.TextureWidth == latest.TextureWidth
                && previous.TextureHeight == latest.TextureHeight
                && previous.MipCount == latest.MipCount
                && !ReferenceEquals(previous.DepthPyramid, latest.DepthPyramid);
        }

        private static bool IsUsableSnapshot(
            in VividGPUDrivenOcclusionCullingParameters parameters)
        {
            return parameters.IsEnabled
                && parameters.DepthPyramid.rt != null
                && IsFinite(parameters.ViewProjectionMatrix);
        }

        private static bool IsFinite(Matrix4x4 matrix)
        {
            for (int index = 0; index < 16; index++)
            {
                float value = matrix[index];
                if (float.IsNaN(value) || float.IsInfinity(value))
                    return false;
            }

            return true;
        }
    }
}
