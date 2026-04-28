using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Runtime
{
    public sealed class VividAntialiasingData : ContextItem
    {
        public bool hasAntialiasingPass;
        public VividAntialiasingMode requestedMode;
        public VividAntialiasingMode effectiveMode;
        public Vector2Int renderSize;
        public Vector2Int outputSize;
        public bool usesTemporalJitter;
        public bool resetHistory;

        public override void Reset()
        {
            hasAntialiasingPass = false;
            requestedMode = VividAntialiasingMode.None;
            effectiveMode = VividAntialiasingMode.None;
            renderSize = Vector2Int.one;
            outputSize = Vector2Int.one;
            usesTemporalJitter = false;
            resetHistory = false;
        }
    }

    internal static class VividAntialiasingRuntimeUtility
    {
        private static readonly Dictionary<EntityId, VividAntialiasingMode> s_PreviousEffectiveModes = new();

        internal static void Clear()
        {
            s_PreviousEffectiveModes.Clear();
        }

        internal static void Resolve(
            Camera camera,
            VividAdditionalCameraData additionalData,
            bool hasAntialiasingPass,
            VividAntialiasingData data)
        {
            if (data == null)
                return;

            var outputSize = ResolveOutputSize(camera);
            data.hasAntialiasingPass = hasAntialiasingPass;
            data.requestedMode = additionalData != null ? additionalData.antialiasing : VividAntialiasingMode.None;
            data.effectiveMode = hasAntialiasingPass
                ? ResolveEffectiveMode(additionalData)
                : VividAntialiasingMode.None;
            data.outputSize = outputSize;
            data.renderSize = ResolveRenderSize(outputSize, additionalData, data.effectiveMode);
            data.usesTemporalJitter = UsesTemporalJitter(data.effectiveMode);
            data.resetHistory = ShouldResetHistory(camera, data.effectiveMode);
        }

        internal static void ApplyJitter(
            Camera camera,
            VividAdditionalCameraData additionalData,
            VividAntialiasingData data)
        {
            if (camera == null)
                return;

            if (camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection)
                return;

            var nonJitteredProj = CameraProjectionMatrixUtility.GetNonJitteredProjectionMatrix(camera);
            var effectiveMode = data != null ? data.effectiveMode : VividAntialiasingMode.None;

            if (additionalData != null && effectiveMode != VividAntialiasingMode.FidelityFXSuperResolution3)
                additionalData.ResetFsr3JitterData();

            switch (effectiveMode)
            {
                case VividAntialiasingMode.TemporalAntiAliasing:
                    ApplyTaaJitter(camera, additionalData, nonJitteredProj);
                    return;
                case VividAntialiasingMode.SpatialTemporalPostProcessing:
                    ApplyStpJitter(camera, nonJitteredProj);
                    return;
                case VividAntialiasingMode.FidelityFXSuperResolution3:
                    ApplyFsr3Jitter(camera, additionalData, data, nonJitteredProj);
                    return;
#if DLSS_PLUGIN_INTEGRATE
                case VividAntialiasingMode.DeepLearningSuperSampling:
                    ApplyDlssJitter(camera, additionalData, nonJitteredProj);
                    return;
#endif
                default:
                    CameraProjectionMatrixUtility.SetProjectionMatrices(camera, nonJitteredProj, nonJitteredProj);
                    return;
            }
        }

        internal static Vector2Int ResolveOutputSize(Camera camera)
        {
            if (camera == null)
                return Vector2Int.one;

            var width = camera.pixelWidth > 0 ? camera.pixelWidth : camera.scaledPixelWidth;
            var height = camera.pixelHeight > 0 ? camera.pixelHeight : camera.scaledPixelHeight;
            width = Mathf.Max(1, width > 0 ? width : Screen.width);
            height = Mathf.Max(1, height > 0 ? height : Screen.height);
            return new Vector2Int(width, height);
        }

        internal static Vector2Int ResolveRenderSize(
            Vector2Int outputSize,
            VividAdditionalCameraData additionalData,
            VividAntialiasingMode effectiveMode)
        {
            var width = Mathf.Max(1, outputSize.x);
            var height = Mathf.Max(1, outputSize.y);

            if (effectiveMode != VividAntialiasingMode.FidelityFXSuperResolution3 || additionalData == null)
                return new Vector2Int(width, height);

            return FSR3UpscalerUtility.ResolveRenderSize(width, height, additionalData.fsr3Quality);
        }

        internal static bool UsesTemporalJitter(VividAntialiasingMode mode)
        {
            if (mode == VividAntialiasingMode.TemporalAntiAliasing
                || mode == VividAntialiasingMode.SpatialTemporalPostProcessing
                || mode == VividAntialiasingMode.FidelityFXSuperResolution3)
            {
                return true;
            }

#if DLSS_PLUGIN_INTEGRATE
            if (mode == VividAntialiasingMode.DeepLearningSuperSampling)
                return true;
#endif

            return false;
        }

        private static VividAntialiasingMode ResolveEffectiveMode(VividAdditionalCameraData additionalData)
        {
            if (additionalData == null)
                return VividAntialiasingMode.None;

            switch (additionalData.antialiasing)
            {
                case VividAntialiasingMode.CMAA2:
                case VividAntialiasingMode.TemporalAntiAliasing:
                    return additionalData.antialiasing;
                case VividAntialiasingMode.SpatialTemporalPostProcessing:
                    return STP.IsSupported()
                        ? VividAntialiasingMode.SpatialTemporalPostProcessing
                        : VividAntialiasingMode.None;
                case VividAntialiasingMode.FidelityFXSuperResolution3:
                    return FSR3UpscalerPass.IsSupported
                        ? VividAntialiasingMode.FidelityFXSuperResolution3
                        : VividAntialiasingMode.None;
#if DLSS_PLUGIN_INTEGRATE
                case VividAntialiasingMode.DeepLearningSuperSampling:
                    return DLSSExtension.IsSuperResolutionSupported
                        ? VividAntialiasingMode.DeepLearningSuperSampling
                        : VividAntialiasingMode.None;
#endif
                default:
                    return VividAntialiasingMode.None;
            }
        }

        private static bool ShouldResetHistory(Camera camera, VividAntialiasingMode effectiveMode)
        {
            if (camera == null)
                return effectiveMode != VividAntialiasingMode.None;

            var cameraId = camera.GetEntityId();
            var hasPreviousMode = s_PreviousEffectiveModes.TryGetValue(cameraId, out var previousMode);
            s_PreviousEffectiveModes[cameraId] = effectiveMode;

            return effectiveMode != VividAntialiasingMode.None
                && (!hasPreviousMode || previousMode != effectiveMode);
        }

        private static void ApplyTaaJitter(
            Camera camera,
            VividAdditionalCameraData additionalData,
            Matrix4x4 nonJitteredProj)
        {
            var taaSettings = TAASettings.FromCamera(additionalData);
            if (!taaSettings.Enabled)
            {
                CameraProjectionMatrixUtility.SetProjectionMatrices(camera, nonJitteredProj, nonJitteredProj);
                return;
            }

            var pixelWidth = camera.pixelWidth;
            var pixelHeight = camera.pixelHeight;
            if (pixelWidth <= 0 || pixelHeight <= 0)
            {
                CameraProjectionMatrixUtility.SetProjectionMatrices(camera, nonJitteredProj, nonJitteredProj);
                return;
            }

            var jitter = HaltonJitter.Get(Time.frameCount, taaSettings.SampleCount);
            jitter *= taaSettings.JitterSpread;

            var jitterMatrix = Matrix4x4.identity;
            jitterMatrix.m03 = jitter.x * 2.0f / pixelWidth;
            jitterMatrix.m13 = jitter.y * 2.0f / pixelHeight;
            CameraProjectionMatrixUtility.SetProjectionMatrices(camera, jitterMatrix * nonJitteredProj, nonJitteredProj);
        }

        private static void ApplyFsr3Jitter(
            Camera camera,
            VividAdditionalCameraData additionalData,
            VividAntialiasingData data,
            Matrix4x4 nonJitteredProj)
        {
            if (additionalData == null || data == null)
            {
                CameraProjectionMatrixUtility.SetProjectionMatrices(camera, nonJitteredProj, nonJitteredProj);
                return;
            }

            var outputSize = data.outputSize;
            var renderSize = data.renderSize;
            if (outputSize.x <= 0 || outputSize.y <= 0 || renderSize.x <= 0 || renderSize.y <= 0)
            {
                additionalData.ResetFsr3JitterData();
                CameraProjectionMatrixUtility.SetProjectionMatrices(camera, nonJitteredProj, nonJitteredProj);
                return;
            }

            var phaseCount = FSR3UpscalerUtility.GetJitterPhaseCount(renderSize.x, outputSize.x);
            var jitterOffset = FSR3UpscalerUtility.GetJitterOffset(Time.frameCount, phaseCount);
            additionalData.SetFsr3JitterData(jitterOffset, phaseCount);

            var jitterMatrix = Matrix4x4.identity;
            jitterMatrix.m03 = jitterOffset.x * 2.0f / renderSize.x;
            jitterMatrix.m13 = -jitterOffset.y * 2.0f / renderSize.y;
            CameraProjectionMatrixUtility.SetProjectionMatrices(camera, jitterMatrix * nonJitteredProj, nonJitteredProj);
        }

        private static void ApplyStpJitter(Camera camera, Matrix4x4 nonJitteredProj)
        {
            var pixelWidth = camera.pixelWidth;
            var pixelHeight = camera.pixelHeight;
            if (pixelWidth <= 0 || pixelHeight <= 0)
            {
                CameraProjectionMatrixUtility.SetProjectionMatrices(camera, nonJitteredProj, nonJitteredProj);
                return;
            }

            var jitter = -STP.Jit16(Time.frameCount);
            var jitterMatrix = Matrix4x4.identity;
            jitterMatrix.m03 = jitter.x * 2.0f / pixelWidth;
            jitterMatrix.m13 = jitter.y * 2.0f / pixelHeight;
            CameraProjectionMatrixUtility.SetProjectionMatrices(camera, jitterMatrix * nonJitteredProj, nonJitteredProj);
        }

#if DLSS_PLUGIN_INTEGRATE
        private static void ApplyDlssJitter(
            Camera camera,
            VividAdditionalCameraData additionalData,
            Matrix4x4 nonJitteredProj)
        {
            var pixelWidth = camera.pixelWidth;
            var pixelHeight = camera.pixelHeight;
            if (pixelWidth <= 0 || pixelHeight <= 0)
            {
                CameraProjectionMatrixUtility.SetProjectionMatrices(camera, nonJitteredProj, nonJitteredProj);
                return;
            }

            var sampleCount = additionalData != null
                ? Mathf.Max(4, additionalData.taaSampleCount)
                : 8;
            var jitterSpread = additionalData != null
                ? additionalData.taaJitterSpread
                : 1.0f;
            var jitter = HaltonJitter.Get(Time.frameCount, sampleCount) * jitterSpread;
            var jitterMatrix = Matrix4x4.identity;
            jitterMatrix.m03 = jitter.x * 2.0f / pixelWidth;
            jitterMatrix.m13 = jitter.y * 2.0f / pixelHeight;

            CameraProjectionMatrixUtility.SetProjectionMatrices(camera, jitterMatrix * nonJitteredProj, nonJitteredProj);
        }
#endif
    }
}
