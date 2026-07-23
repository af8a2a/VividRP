using System.Runtime.InteropServices;
using UnityEngine;
using VividRP.Runtime.RenderPass.Core.Sigma;

namespace VividRP.Runtime.RenderPass.Core
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct ReblurSharedConstants
    {
        public Matrix4x4 gWorldToClip;
        public Matrix4x4 gViewToClip;
        public Matrix4x4 gViewToWorld;
        public Matrix4x4 gWorldToViewPrev;
        public Matrix4x4 gWorldToClipPrev;
        public Matrix4x4 gWorldPrevToWorld;
        public Vector4 gRotatorPre;
        public Vector4 gRotator;
        public Vector4 gRotatorPost;
        public Vector4 gFrustum;
        public Vector4 gFrustumPrev;
        public Vector4 gCameraDelta;
        public Vector4 gHitDistParams;
        public Vector4 gViewVectorWorld;
        public Vector4 gViewVectorWorldPrev;
        public Vector4 gMvScale;
        public Vector2 gAntilagParams;
        public Vector2 gResourceSize;
        public Vector2 gResourceSizeInv;
        public Vector2 gResourceSizeInvPrev;
        public Vector2 gRectSize;
        public Vector2 gRectSizeInv;
        public Vector2 gRectSizePrev;
        public Vector2 gResolutionScale;
        public Vector2 gResolutionScalePrev;
        public Vector2 gRectOffset;
        public Vector2 gSpecProbabilityThresholdsForMvModification;
        public Vector2 gJitter;
        public uint gPrintfAtX;
        public uint gPrintfAtY;
        public uint gRectOriginX;
        public uint gRectOriginY;
        public int gRectSizeMinusOneX;
        public int gRectSizeMinusOneY;
        public float gDisocclusionThreshold;
        public float gDisocclusionThresholdAlternate;
        public float gCameraAttachedReflectionMaterialID;
        public float gStrandMaterialID;
        public float gStrandThickness;
        public float gStabilizationStrength;
        public float gDebug;
        public float gOrthoMode;
        public float gUnproject;
        public float gDenoisingRange;
        public float gPlaneDistSensitivity;
        public float gFramerateScale;
        public float gMinBlurRadius;
        public float gMaxBlurRadius;
        public float gDiffPrepassBlurRadius;
        public float gSpecPrepassBlurRadius;
        public float gMaxAccumulatedFrameNum;
        public float gMaxFastAccumulatedFrameNum;
        public float gAntiFirefly;
        public float gLobeAngleFraction;
        public float gRoughnessFraction;
        public float gHistoryFixFrameNum;
        public float gHistoryFixBasePixelStride;
        public float gHistoryFixAlternatePixelStride;
        public float gHistoryFixAlternatePixelStrideMaterialID;
        public float gFastHistoryClampingSigmaScale;
        public float gMinRectDimMulUnproject;
        public float gUsePrepassNotOnlyForSpecularMotionEstimation;
        public float gSplitScreen;
        public float gSplitScreenPrev;
        public float gCheckerboardResolveAccumSpeed;
        public float gViewZScale;
        public float gFireflySuppressorMinRelativeScale;
        public float gMinHitDistanceWeight;
        public float gDiffMinMaterial;
        public float gSpecMinMaterial;
        public float gResponsiveAccumulationInvRoughnessThreshold;
        public uint gResponsiveAccumulationMinAccumulatedFrameNum;
        public uint gHasHistoryConfidence;
        public uint gHasDisocclusionThresholdMix;
        public uint gDiffCheckerboard;
        public uint gSpecCheckerboard;
        public uint gFrameIndex;
        public uint gIsRectChanged;
        public uint gResetHistory;
        public uint gReturnHistoryLengthInsteadOfOcclusion;

        public static ReblurSharedConstants Compute(
            VividCameraData cameraData,
            VividTemporalData temporalData,
            int width,
            int height,
            bool hasValidHistory)
        {
            var camera = cameraData?.camera;
            var worldToView = cameraData?.GetViewMatrix() ?? Matrix4x4.identity;
            var viewToClip = cameraData?.GetGPUProjectionMatrixNoJitter(renderIntoTexture: true)
                ?? Matrix4x4.identity;
            var worldToViewPrevious = hasValidHistory && temporalData != null
                ? temporalData.previousViewMatrix
                : worldToView;
            var viewToClipPrevious = hasValidHistory && temporalData != null
                ? temporalData.previousProjectionMatrix
                : viewToClip;
            var cameraPosition = GetTranslation(worldToView.inverse);
            var cameraPositionPrevious = GetTranslation(worldToViewPrevious.inverse);
            uint frameIndex = (uint)Mathf.Max(
                cameraData != null && cameraData.frameIndex >= 0
                    ? cameraData.frameIndex
                    : Time.frameCount,
                0);

            var sigma = SigmaSharedConstants.Compute(
                worldToView,
                viewToClip,
                worldToViewPrevious,
                viewToClipPrevious,
                cameraPosition,
                cameraPositionPrevious,
                Vector3.up,
                width,
                height,
                width,
                height,
                frameIndex,
                Mathf.Max(camera?.farClipPlane ?? 1000.0f, 1.0f),
                0.02f,
                0.0f,
                camera != null && camera.orthographic);

            float anglePre = SequenceHelpers.Weyl1D(0.5f, (int)frameIndex) * Mathf.Deg2Rad * 90.0f;
            var viewToWorldPrevious = sigma.gWorldToViewPrev.inverse;
            var viewDirectionPrevious = -GetColumn(viewToWorldPrevious, 2).normalized;
            float deltaTimeMs = Mathf.Max(Time.deltaTime * 1000.0f, 0.001f);
            float framerateScale = Mathf.Max(33.333f / deltaTimeMs, 1.0f);
            float denoisingRange = Mathf.Max(camera?.farClipPlane ?? 1000.0f, 1.0f);

            return new ReblurSharedConstants
            {
                gWorldToClip = sigma.gViewToClip * sigma.gWorldToView,
                gViewToClip = sigma.gViewToClip,
                gViewToWorld = sigma.gWorldToView.inverse,
                gWorldToViewPrev = sigma.gWorldToViewPrev,
                gWorldToClipPrev = sigma.gWorldToClipPrev,
                gWorldPrevToWorld = Matrix4x4.identity,
                gRotatorPre = SequenceHelpers.GetRotator(anglePre),
                gRotator = sigma.gRotator,
                gRotatorPost = sigma.gRotatorPost,
                gFrustum = sigma.gFrustum,
                gFrustumPrev = sigma.gFrustumPrev,
                gCameraDelta = sigma.gCameraDelta,
                gHitDistParams = new Vector4(3.0f, 0.1f, 20.0f, -25.0f),
                gViewVectorWorld = sigma.gViewVectorWorld,
                gViewVectorWorldPrev = new Vector4(
                    viewDirectionPrevious.x,
                    viewDirectionPrevious.y,
                    viewDirectionPrevious.z,
                    0.0f),
                gMvScale = new Vector4(1.0f / width, 1.0f / height, 1.0f, 0.0f),
                gAntilagParams = new Vector2(4.0f, 3.0f),
                gResourceSize = new Vector2(width, height),
                gResourceSizeInv = new Vector2(1.0f / width, 1.0f / height),
                gResourceSizeInvPrev = new Vector2(1.0f / width, 1.0f / height),
                gRectSize = new Vector2(width, height),
                gRectSizeInv = new Vector2(1.0f / width, 1.0f / height),
                gRectSizePrev = new Vector2(width, height),
                gResolutionScale = Vector2.one,
                gResolutionScalePrev = Vector2.one,
                gRectOffset = Vector2.zero,
                gSpecProbabilityThresholdsForMvModification = new Vector2(2.0f, 3.0f),
                gJitter = Vector2.zero,
                gPrintfAtX = 9999u,
                gPrintfAtY = 9999u,
                gRectOriginX = 0u,
                gRectOriginY = 0u,
                gRectSizeMinusOneX = width - 1,
                gRectSizeMinusOneY = height - 1,
                gDisocclusionThreshold = 0.01f + 1.0f / height,
                gDisocclusionThresholdAlternate = 0.05f + 1.0f / height,
                gCameraAttachedReflectionMaterialID = 999.0f,
                gStrandMaterialID = 999.0f,
                gStrandThickness = 80e-6f,
                gStabilizationStrength = 0.0f,
                gDebug = 0.0f,
                gOrthoMode = sigma.gOrthoMode,
                gUnproject = sigma.gUnproject,
                gDenoisingRange = denoisingRange,
                gPlaneDistSensitivity = 0.02f,
                gFramerateScale = framerateScale,
                gMinBlurRadius = 1.0f,
                gMaxBlurRadius = 30.0f,
                gDiffPrepassBlurRadius = 30.0f,
                gSpecPrepassBlurRadius = 50.0f,
                gMaxAccumulatedFrameNum = hasValidHistory ? 30.0f : 0.0f,
                gMaxFastAccumulatedFrameNum = hasValidHistory ? 6.0f : 0.0f,
                gAntiFirefly = 0.0f,
                gLobeAngleFraction = 0.15f * 0.15f,
                gRoughnessFraction = 0.15f,
                gHistoryFixFrameNum = 3.0f,
                gHistoryFixBasePixelStride = 14.0f,
                gHistoryFixAlternatePixelStride = 14.0f,
                gHistoryFixAlternatePixelStrideMaterialID = 999.0f,
                gFastHistoryClampingSigmaScale = 2.0f,
                gMinRectDimMulUnproject = Mathf.Min(width, height) * sigma.gUnproject,
                gUsePrepassNotOnlyForSpecularMotionEstimation = 1.0f,
                gSplitScreen = 0.0f,
                gSplitScreenPrev = 0.0f,
                gCheckerboardResolveAccumSpeed = 0.5f,
                gViewZScale = 1.0f,
                gFireflySuppressorMinRelativeScale = 2.0f,
                gMinHitDistanceWeight = 0.1f,
                gDiffMinMaterial = 4.0f,
                gSpecMinMaterial = 4.0f,
                gResponsiveAccumulationInvRoughnessThreshold = 1000.0f,
                gResponsiveAccumulationMinAccumulatedFrameNum = 3u,
                gHasHistoryConfidence = 0u,
                gHasDisocclusionThresholdMix = 0u,
                gDiffCheckerboard = 2u,
                gSpecCheckerboard = 2u,
                gFrameIndex = frameIndex,
                gIsRectChanged = hasValidHistory ? 0u : 1u,
                gResetHistory = hasValidHistory ? 0u : 1u,
                gReturnHistoryLengthInsteadOfOcclusion = 0u
            };
        }

        private static Vector3 GetTranslation(Matrix4x4 matrix)
        {
            var column = matrix.GetColumn(3);
            return new Vector3(column.x, column.y, column.z);
        }

        private static Vector3 GetColumn(Matrix4x4 matrix, int column)
        {
            var value = matrix.GetColumn(column);
            return new Vector3(value.x, value.y, value.z);
        }
    }
}
