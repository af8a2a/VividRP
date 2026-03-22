using System.Runtime.InteropServices;
using UnityEngine;

namespace VividRP.Runtime.RenderPass.Core.Sigma
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct SigmaSharedConstants
    {
        public Matrix4x4 gWorldToView;
        public Matrix4x4 gViewToClip;
        public Matrix4x4 gWorldToClipPrev;
        public Matrix4x4 gWorldToViewPrev;
        public Vector4 gRotator;
        public Vector4 gRotatorPost;
        public Vector4 gViewVectorWorld;
        public Vector4 gLightDirectionView;
        public Vector4 gFrustum;
        public Vector4 gFrustumPrev;
        public Vector4 gCameraDelta;
        public Vector4 gMvScale;
        public Vector2 gResourceSizeInv;
        public Vector2 gResourceSizeInvPrev;
        public Vector2 gRectSize;
        public Vector2 gRectSizeInv;
        public Vector2 gRectSizePrev;
        public Vector2 gResolutionScale;
        public Vector2 gRectOffset;
        public uint gPrintfAtX;
        public uint gPrintfAtY;
        public uint gRectOriginX;
        public uint gRectOriginY;
        public int gRectSizeMinusOneX;
        public int gRectSizeMinusOneY;
        public int gTilesSizeMinusOneX;
        public int gTilesSizeMinusOneY;
        public float gOrthoMode;
        public float gUnproject;
        public float gDenoisingRange;
        public float gPlaneDistSensitivity;
        public float gStabilizationStrength;
        public float gDebug;
        public float gSplitScreen;
        public float gViewZScale;
        public float gMinRectDimMulUnproject;
        public uint gFrameIndex;
        public uint gIsRectChanged;

        public static SigmaSharedConstants Compute(
            Matrix4x4 worldToView,
            Matrix4x4 viewToClip,
            Matrix4x4 worldToViewPrev,
            Matrix4x4 viewToClipPrev,
            Vector3 cameraPosition,
            Vector3 cameraPositionPrev,
            Vector3 lightDirectionWS,
            int width, int height,
            int widthPrev, int heightPrev,
            uint frameIndex,
            float denoisingRange,
            float planeDistSensitivity,
            float stabilizationStrength,
            bool isOrtho)
        {
            var constants = new SigmaSharedConstants();

            constants.gWorldToView = worldToView;
            constants.gViewToClip = viewToClip;
            constants.gWorldToClipPrev = viewToClipPrev * worldToViewPrev;
            constants.gWorldToViewPrev = worldToViewPrev;

            // Frustum params from projection matrix
            // For perspective: proj[0][0] = 1/tanHalfFovX, proj[1][1] = 1/tanHalfFovY
            float tanHalfFovX = 1.0f / viewToClip[0, 0];
            float tanHalfFovY = 1.0f / viewToClip[1, 1];
            constants.gFrustum = new Vector4(tanHalfFovX, tanHalfFovY, 1.0f / tanHalfFovX, 1.0f / tanHalfFovY);

            float tanHalfFovXPrev = 1.0f / viewToClipPrev[0, 0];
            float tanHalfFovYPrev = 1.0f / viewToClipPrev[1, 1];
            constants.gFrustumPrev = new Vector4(tanHalfFovXPrev, tanHalfFovYPrev, 1.0f / tanHalfFovXPrev, 1.0f / tanHalfFovYPrev);

            // Unproject: pixel size in world units at viewZ=1
            constants.gUnproject = isOrtho ? 1.0f : 1.0f / (0.5f * height * viewToClip[1, 1]);

            // Rotator: frame-dependent rotation for sampling pattern
            float angle = 2.0f * Mathf.PI * SequenceHelpers.Halton(frameIndex + 1, 2);
            float ca = Mathf.Cos(angle);
            float sa = Mathf.Sin(angle);
            constants.gRotator = new Vector4(ca, sa, -sa, ca);

            float anglePost = 2.0f * Mathf.PI * SequenceHelpers.Halton(frameIndex + 1, 3);
            float caPost = Mathf.Cos(anglePost);
            float saPost = Mathf.Sin(anglePost);
            constants.gRotatorPost = new Vector4(caPost, saPost, -saPost, caPost);

            // View vector (for ortho mode)
            var viewForward = new Vector3(worldToView[2, 0], worldToView[2, 1], worldToView[2, 2]).normalized;
            constants.gViewVectorWorld = new Vector4(viewForward.x, viewForward.y, viewForward.z, 0f);

            // Light direction in view space
            var lightDirView = worldToView.MultiplyVector(lightDirectionWS).normalized;
            constants.gLightDirectionView = new Vector4(lightDirView.x, lightDirView.y, lightDirView.z, 0f);

            // Camera delta
            var delta = cameraPosition - cameraPositionPrev;
            constants.gCameraDelta = new Vector4(delta.x, delta.y, delta.z, 0f);

            // Motion vector scale (screen-space MV in pixels)
            constants.gMvScale = new Vector4(1f, 1f, 0f, 0f);

            // Resource dimensions
            constants.gResourceSizeInv = new Vector2(1.0f / width, 1.0f / height);
            constants.gResourceSizeInvPrev = new Vector2(1.0f / widthPrev, 1.0f / heightPrev);
            constants.gRectSize = new Vector2(width, height);
            constants.gRectSizeInv = new Vector2(1.0f / width, 1.0f / height);
            constants.gRectSizePrev = new Vector2(widthPrev, heightPrev);
            constants.gResolutionScale = new Vector2(1f, 1f);
            constants.gRectOffset = Vector2.zero;

            constants.gPrintfAtX = 0;
            constants.gPrintfAtY = 0;
            constants.gRectOriginX = 0;
            constants.gRectOriginY = 0;
            constants.gRectSizeMinusOneX = width - 1;
            constants.gRectSizeMinusOneY = height - 1;

            int tileW = Mathf.CeilToInt(width / 16f);
            int tileH = Mathf.CeilToInt(height / 16f);
            constants.gTilesSizeMinusOneX = tileW - 1;
            constants.gTilesSizeMinusOneY = tileH - 1;

            constants.gOrthoMode = isOrtho ? 1f : 0f;
            constants.gDenoisingRange = denoisingRange;
            constants.gPlaneDistSensitivity = planeDistSensitivity;
            constants.gStabilizationStrength = stabilizationStrength;
            constants.gDebug = 0f;
            constants.gSplitScreen = 0f;
            constants.gViewZScale = 1f; // reversed-Z
            constants.gMinRectDimMulUnproject = Mathf.Min(width, height) * constants.gUnproject;
            constants.gFrameIndex = frameIndex;
            constants.gIsRectChanged = (width != widthPrev || height != heightPrev) ? 1u : 0u;

            return constants;
        }
    }

    internal static class SequenceHelpers
    {
        public static float Halton(uint index, int baseVal)
        {
            float result = 0f;
            float f = 1f / baseVal;
            uint i = index;
            while (i > 0)
            {
                result += f * (i % (uint)baseVal);
                i /= (uint)baseVal;
                f /= baseVal;
            }
            return result;
        }
    }
}
