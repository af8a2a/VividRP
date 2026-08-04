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
        internal bool HasValidMetadata;
        internal Matrix4x4 ViewProjectionMatrix = Matrix4x4.identity;
        internal int Width;
        internal int Height;
        internal int TextureWidth;
        internal int TextureHeight;
        internal int MipCount;

        public override void Dispose()
        {
            HasValidMetadata = false;
            ViewProjectionMatrix = Matrix4x4.identity;
            Width = 0;
            Height = 0;
            TextureWidth = 0;
            TextureHeight = 0;
            MipCount = 0;
        }
    }

    internal static class VividGPUDrivenOcclusionHistorySystem
    {
        internal const int MaxMipCount = 16;
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
                || !state.HasValidMetadata
                || state.Width != currentWidth
                || state.Height != currentHeight
                || !IsFinite(state.ViewProjectionMatrix))
            {
                return false;
            }

            var cameraHistory = camera.GetVividCameraHistory();
            if (!cameraHistory.TryGetTexture(
                    CameraHistoryIds.GPUDrivenOccluderDepthPyramid,
                    out var history)
                || history == null
                || !history.IsValid(1)
                || !IsCompatible(state, history.Descriptor))
            {
                return false;
            }

            var previous = history.GetPrevious();
            if (previous == null)
                return false;

            parameters = new VividGPUDrivenOcclusionCullingParameters(
                previous,
                state.ViewProjectionMatrix,
                state.Width,
                state.Height,
                state.TextureWidth,
                state.TextureHeight,
                state.MipCount,
                ConservativeDepthBias);
            return true;
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

            var state = s_States.GetOrCreateBase(camera);
            state.HasValidMetadata = true;
            state.ViewProjectionMatrix = viewProjectionMatrix;
            state.Width = width;
            state.Height = height;
            state.TextureWidth = textureWidth;
            state.TextureHeight = textureHeight;
            state.MipCount = Mathf.Clamp(mipCount, 1, MaxMipCount);
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
            VividGPUDrivenOcclusionHistoryState state,
            in CameraHistoryTextureDescriptor descriptor)
        {
            return descriptor.Dimension == TextureDimension.Tex2D
                && descriptor.Slices == 1
                && descriptor.MsaaSamples == MSAASamples.None
                && descriptor.ColorFormat == GraphicsFormat.R32_SFloat
                && descriptor.EnableRandomWrite
                && descriptor.UseMipMap
                && state.Width > 0
                && state.Height > 0
                && state.TextureWidth == descriptor.Width
                && state.TextureHeight == descriptor.Height
                && state.Width <= state.TextureWidth
                && state.Height <= state.TextureHeight
                && state.MipCount == CalculateMipCount(descriptor.Width, descriptor.Height);
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
