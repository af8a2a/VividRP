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
        private static readonly EntityIdComparer s_EntityIdComparer = new();
        private static readonly Dictionary<EntityId, AntialiasingHistoryKey> s_PreviousHistoryKeys = new(32, s_EntityIdComparer);
        private static bool s_HasResolvedStpSupport;
        private static bool s_CachedStpSupport;
        private static bool s_HasResolvedFsr3Support;
        private static bool s_CachedFsr3Support;
        private static bool s_HasResolvedTsrSupport;
        private static bool s_CachedTsrSupport;
#if DLSS_PLUGIN_INTEGRATE
        private static bool s_HasResolvedDlssSupport;
        private static bool s_CachedDlssSupport;
#endif

        internal static void Clear()
        {
            s_PreviousHistoryKeys.Clear();
            s_HasResolvedStpSupport = false;
            s_CachedStpSupport = false;
            s_HasResolvedFsr3Support = false;
            s_CachedFsr3Support = false;
            s_HasResolvedTsrSupport = false;
            s_CachedTsrSupport = false;
#if DLSS_PLUGIN_INTEGRATE
            s_HasResolvedDlssSupport = false;
            s_CachedDlssSupport = false;
#endif
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
            data.resetHistory = ShouldResetHistory(camera, additionalData, data.effectiveMode, outputSize);
        }

        internal static void ApplyJitter(
            Camera camera,
            VividAdditionalCameraData additionalData,
            VividAntialiasingData data,
            int frameIndex)
        {
            if (camera == null)
                return;

            if (camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection)
                return;

            var nonJitteredProj = CameraProjectionMatrixUtility.GetNonJitteredProjectionMatrix(camera);
            var effectiveMode = data != null ? data.effectiveMode : VividAntialiasingMode.None;

            if (additionalData != null)
            {
                if (effectiveMode != VividAntialiasingMode.FidelityFXSuperResolution3)
                    additionalData.ResetFsr3JitterData();

                if (effectiveMode != VividAntialiasingMode.TemporalSuperResolution)
                    additionalData.ResetTsrJitterData();
            }

            switch (effectiveMode)
            {
                case VividAntialiasingMode.TemporalAntiAliasing:
                    ApplyTaaJitter(camera, additionalData, nonJitteredProj, frameIndex);
                    return;
                case VividAntialiasingMode.SpatialTemporalPostProcessing:
                    ApplyStpJitter(camera, nonJitteredProj, frameIndex);
                    return;
                case VividAntialiasingMode.FidelityFXSuperResolution3:
                    ApplyFsr3Jitter(camera, additionalData, data, nonJitteredProj, frameIndex);
                    return;
                case VividAntialiasingMode.TemporalSuperResolution:
                    ApplyTsrJitter(camera, additionalData, data, nonJitteredProj, frameIndex);
                    return;
#if DLSS_PLUGIN_INTEGRATE
                case VividAntialiasingMode.DeepLearningSuperSampling:
                    ApplyDlssJitter(camera, additionalData, nonJitteredProj, frameIndex);
                    return;
#endif
                default:
                    CameraProjectionMatrixUtility.RestoreNoJitterProjection(camera, nonJitteredProj);
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

            if (additionalData == null)
                return new Vector2Int(width, height);

            if (effectiveMode == VividAntialiasingMode.FidelityFXSuperResolution3)
                return FSR3UpscalerUtility.ResolveRenderSize(width, height, additionalData.fsr3Quality);

            if (effectiveMode == VividAntialiasingMode.TemporalSuperResolution)
                return TSRUpscalerUtility.ResolveRenderSize(width, height, additionalData.tsrQuality);

            return new Vector2Int(width, height);
        }

        internal static bool UsesTemporalJitter(VividAntialiasingMode mode)
        {
            if (mode == VividAntialiasingMode.TemporalAntiAliasing
                || mode == VividAntialiasingMode.SpatialTemporalPostProcessing
                || mode == VividAntialiasingMode.FidelityFXSuperResolution3
                || mode == VividAntialiasingMode.TemporalSuperResolution)
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
                    return IsStpSupported()
                        ? VividAntialiasingMode.SpatialTemporalPostProcessing
                        : VividAntialiasingMode.None;
                case VividAntialiasingMode.FidelityFXSuperResolution3:
                    return IsFsr3Supported()
                        ? VividAntialiasingMode.FidelityFXSuperResolution3
                        : VividAntialiasingMode.None;
                case VividAntialiasingMode.TemporalSuperResolution:
                    return IsTsrSupported()
                        ? VividAntialiasingMode.TemporalSuperResolution
                        : VividAntialiasingMode.None;
#if DLSS_PLUGIN_INTEGRATE
                case VividAntialiasingMode.DeepLearningSuperSampling:
                    return IsDlssSupported()
                        ? VividAntialiasingMode.DeepLearningSuperSampling
                        : VividAntialiasingMode.None;
#endif
                default:
                    return VividAntialiasingMode.None;
            }
        }

        internal static bool IsStpSupported()
        {
            if (!s_HasResolvedStpSupport)
            {
                s_CachedStpSupport = STP.IsSupported();
                s_HasResolvedStpSupport = true;
            }

            return s_CachedStpSupport;
        }

        internal static bool IsFsr3Supported()
        {
            if (!s_HasResolvedFsr3Support)
            {
                s_CachedFsr3Support = FSR3UpscalerPass.IsSupported;
                s_HasResolvedFsr3Support = true;
            }

            return s_CachedFsr3Support;
        }

        internal static bool IsTsrSupported()
        {
            if (!s_HasResolvedTsrSupport)
            {
                s_CachedTsrSupport = TSRUpscalerPass.IsSupported;
                s_HasResolvedTsrSupport = true;
            }

            return s_CachedTsrSupport;
        }

#if DLSS_PLUGIN_INTEGRATE
        internal static bool IsDlssSupported()
        {
            if (!s_HasResolvedDlssSupport)
            {
                s_CachedDlssSupport = DLSSExtension.IsSuperResolutionSupported;
                s_HasResolvedDlssSupport = true;
            }

            return s_CachedDlssSupport;
        }
#endif

        private static bool ShouldResetHistory(
            Camera camera,
            VividAdditionalCameraData additionalData,
            VividAntialiasingMode effectiveMode,
            Vector2Int outputSize)
        {
            if (camera == null)
                return effectiveMode != VividAntialiasingMode.None;

            var cameraId = camera.GetEntityId();
            if (effectiveMode == VividAntialiasingMode.None)
            {
                s_PreviousHistoryKeys.Remove(cameraId);
                return false;
            }

            var historyKey = AntialiasingHistoryKey.Create(effectiveMode, outputSize, additionalData);
            var hasPreviousKey = s_PreviousHistoryKeys.TryGetValue(cameraId, out var previousKey);
            s_PreviousHistoryKeys[cameraId] = historyKey;

            return !hasPreviousKey || !previousKey.Equals(historyKey);
        }

        private static void ApplyTaaJitter(
            Camera camera,
            VividAdditionalCameraData additionalData,
            Matrix4x4 nonJitteredProj,
            int frameIndex)
        {
            var taaSettings = TAASettings.FromCamera(additionalData);
            if (!taaSettings.Enabled)
            {
                CameraProjectionMatrixUtility.RestoreNoJitterProjection(camera, nonJitteredProj);
                return;
            }

            var pixelWidth = camera.pixelWidth;
            var pixelHeight = camera.pixelHeight;
            if (pixelWidth <= 0 || pixelHeight <= 0)
            {
                CameraProjectionMatrixUtility.RestoreNoJitterProjection(camera, nonJitteredProj);
                return;
            }

            var jitter = HaltonJitter.Get(ResolveTemporalFrameIndex(frameIndex), taaSettings.SampleCount);
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
            Matrix4x4 nonJitteredProj,
            int frameIndex)
        {
            if (additionalData == null || data == null)
            {
                CameraProjectionMatrixUtility.RestoreNoJitterProjection(camera, nonJitteredProj);
                return;
            }

            var outputSize = data.outputSize;
            var renderSize = data.renderSize;
            if (outputSize.x <= 0 || outputSize.y <= 0 || renderSize.x <= 0 || renderSize.y <= 0)
            {
                additionalData.ResetFsr3JitterData();
                CameraProjectionMatrixUtility.RestoreNoJitterProjection(camera, nonJitteredProj);
                return;
            }

            var phaseCount = FSR3UpscalerUtility.GetJitterPhaseCount(renderSize.x, outputSize.x);
            var jitterOffset = FSR3UpscalerUtility.GetJitterOffset(ResolveTemporalFrameIndex(frameIndex), phaseCount);
            additionalData.SetFsr3JitterData(jitterOffset, phaseCount);

            var jitterMatrix = Matrix4x4.identity;
            jitterMatrix.m03 = jitterOffset.x * 2.0f / renderSize.x;
            jitterMatrix.m13 = -jitterOffset.y * 2.0f / renderSize.y;
            CameraProjectionMatrixUtility.SetProjectionMatrices(camera, jitterMatrix * nonJitteredProj, nonJitteredProj);
        }

        private static void ApplyTsrJitter(
            Camera camera,
            VividAdditionalCameraData additionalData,
            VividAntialiasingData data,
            Matrix4x4 nonJitteredProj,
            int frameIndex)
        {
            if (additionalData == null || data == null)
            {
                CameraProjectionMatrixUtility.RestoreNoJitterProjection(camera, nonJitteredProj);
                return;
            }

            var outputSize = data.outputSize;
            var renderSize = data.renderSize;
            if (outputSize.x <= 0 || outputSize.y <= 0 || renderSize.x <= 0 || renderSize.y <= 0)
            {
                additionalData.ResetTsrJitterData();
                CameraProjectionMatrixUtility.RestoreNoJitterProjection(camera, nonJitteredProj);
                return;
            }

            var phaseCount = TSRUpscalerUtility.GetJitterPhaseCount(renderSize.x, outputSize.x);
            var jitterOffset = TSRUpscalerUtility.GetJitterOffset(ResolveTemporalFrameIndex(frameIndex), phaseCount);
            additionalData.SetTsrJitterData(jitterOffset, phaseCount);

            var jitterMatrix = Matrix4x4.identity;
            jitterMatrix.m03 = jitterOffset.x * 2.0f / renderSize.x;
            jitterMatrix.m13 = -jitterOffset.y * 2.0f / renderSize.y;
            CameraProjectionMatrixUtility.SetProjectionMatrices(camera, jitterMatrix * nonJitteredProj, nonJitteredProj);
        }

        private static void ApplyStpJitter(Camera camera, Matrix4x4 nonJitteredProj, int frameIndex)
        {
            var pixelWidth = camera.pixelWidth;
            var pixelHeight = camera.pixelHeight;
            if (pixelWidth <= 0 || pixelHeight <= 0)
            {
                CameraProjectionMatrixUtility.RestoreNoJitterProjection(camera, nonJitteredProj);
                return;
            }

            var jitter = -STP.Jit16(ResolveTemporalFrameIndex(frameIndex));
            var jitterMatrix = Matrix4x4.identity;
            jitterMatrix.m03 = jitter.x * 2.0f / pixelWidth;
            jitterMatrix.m13 = jitter.y * 2.0f / pixelHeight;
            CameraProjectionMatrixUtility.SetProjectionMatrices(camera, jitterMatrix * nonJitteredProj, nonJitteredProj);
        }

#if DLSS_PLUGIN_INTEGRATE
        private static void ApplyDlssJitter(
            Camera camera,
            VividAdditionalCameraData additionalData,
            Matrix4x4 nonJitteredProj,
            int frameIndex)
        {
            var pixelWidth = camera.pixelWidth;
            var pixelHeight = camera.pixelHeight;
            if (pixelWidth <= 0 || pixelHeight <= 0)
            {
                CameraProjectionMatrixUtility.RestoreNoJitterProjection(camera, nonJitteredProj);
                return;
            }

            var sampleCount = additionalData != null
                ? Mathf.Max(4, additionalData.taaSampleCount)
                : 8;
            var jitterSpread = additionalData != null
                ? additionalData.taaJitterSpread
                : 1.0f;
            var jitter = HaltonJitter.Get(ResolveTemporalFrameIndex(frameIndex), sampleCount) * jitterSpread;
            var jitterMatrix = Matrix4x4.identity;
            jitterMatrix.m03 = jitter.x * 2.0f / pixelWidth;
            jitterMatrix.m13 = jitter.y * 2.0f / pixelHeight;

            CameraProjectionMatrixUtility.SetProjectionMatrices(camera, jitterMatrix * nonJitteredProj, nonJitteredProj);
        }
#endif

        private static int ResolveTemporalFrameIndex(int frameIndex)
        {
            return frameIndex >= 0 ? frameIndex : Time.frameCount;
        }

        private readonly struct AntialiasingHistoryKey : System.IEquatable<AntialiasingHistoryKey>
        {
            private readonly VividAntialiasingMode m_Mode;
            private readonly Vector2Int m_OutputSize;
            private readonly VividFsr3QualityMode m_Fsr3Quality;
            private readonly VividTsrQualityMode m_TsrQuality;

            private AntialiasingHistoryKey(
                VividAntialiasingMode mode,
                Vector2Int outputSize,
                VividFsr3QualityMode fsr3Quality,
                VividTsrQualityMode tsrQuality)
            {
                m_Mode = mode;
                m_OutputSize = outputSize;
                m_Fsr3Quality = fsr3Quality;
                m_TsrQuality = tsrQuality;
            }

            public static AntialiasingHistoryKey Create(
                VividAntialiasingMode mode,
                Vector2Int outputSize,
                VividAdditionalCameraData additionalData)
            {
                return new AntialiasingHistoryKey(
                    mode,
                    outputSize,
                    mode == VividAntialiasingMode.FidelityFXSuperResolution3 && additionalData != null
                        ? additionalData.fsr3Quality
                        : VividFsr3QualityMode.Balanced,
                    mode == VividAntialiasingMode.TemporalSuperResolution && additionalData != null
                        ? additionalData.tsrQuality
                        : VividTsrQualityMode.Balanced);
            }

            public bool Equals(AntialiasingHistoryKey other)
            {
                return m_Mode == other.m_Mode
                    && m_OutputSize == other.m_OutputSize
                    && m_Fsr3Quality == other.m_Fsr3Quality
                    && m_TsrQuality == other.m_TsrQuality;
            }

            public override bool Equals(object obj)
            {
                return obj is AntialiasingHistoryKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = (int)m_Mode;
                    hashCode = (hashCode * 397) ^ m_OutputSize.GetHashCode();
                    hashCode = (hashCode * 397) ^ (int)m_Fsr3Quality;
                    hashCode = (hashCode * 397) ^ (int)m_TsrQuality;
                    return hashCode;
                }
            }
        }

        private sealed class EntityIdComparer : IEqualityComparer<EntityId>
        {
            public bool Equals(EntityId x, EntityId y)
            {
                return EntityId.ToULong(x) == EntityId.ToULong(y);
            }

            public int GetHashCode(EntityId obj)
            {
                return EntityId.ToULong(obj).GetHashCode();
            }
        }
    }
}
