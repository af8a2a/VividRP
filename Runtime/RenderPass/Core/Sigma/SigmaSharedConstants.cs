using System.Runtime.InteropServices;
using UnityEngine;

namespace VividRP.Runtime.RenderPass.Core.Sigma
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct SigmaSharedConstants
    {
        private const float Epsilon = 1e-6f;

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

        private readonly struct ProjectionData
        {
            public ProjectionData(bool isLeftHanded, bool isOrthographic, Vector4 frustum, float projectY)
            {
                IsLeftHanded = isLeftHanded;
                IsOrthographic = isOrthographic;
                Frustum = frustum;
                ProjectY = projectY;
            }

            public bool IsLeftHanded { get; }
            public bool IsOrthographic { get; }
            public Vector4 Frustum { get; }
            public float ProjectY { get; }
        }

        private readonly struct ProjectionPlanes
        {
            public ProjectionPlanes(
                Vector4 left,
                Vector4 right,
                Vector4 bottom,
                Vector4 top,
                Vector4 near,
                Vector4 far)
            {
                Left = left;
                Right = right;
                Bottom = bottom;
                Top = top;
                Near = near;
                Far = far;
            }

            public Vector4 Left { get; }
            public Vector4 Right { get; }
            public Vector4 Bottom { get; }
            public Vector4 Top { get; }
            public Vector4 Near { get; }
            public Vector4 Far { get; }
        }

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

            var currentProjection = DecomposeProjection(viewToClip, isOrtho);
            var previousProjection = DecomposeProjection(viewToClipPrev, isOrtho);

            var viewToClipLh = viewToClip;
            var viewToClipPrevLh = viewToClipPrev;
            var worldToViewLh = worldToView;
            var worldToViewPrevLh = worldToViewPrev;

            if (!currentProjection.IsLeftHanded)
            {
                viewToClipLh = ConvertProjectionToLeftHanded(viewToClipLh);
                viewToClipPrevLh = ConvertProjectionToLeftHanded(viewToClipPrevLh);
                worldToViewLh = ConvertWorldToViewToLeftHanded(worldToViewLh);
                worldToViewPrevLh = ConvertWorldToViewToLeftHanded(worldToViewPrevLh);
            }

            currentProjection = DecomposeProjection(viewToClipLh, isOrtho);
            previousProjection = DecomposeProjection(viewToClipPrevLh, isOrtho);

            var viewToWorldLh = worldToViewLh.inverse;
            var viewToWorldPrevLh = worldToViewPrevLh.inverse;

            var viewTranslation = GetTranslation(viewToWorldLh);
            var prevViewTranslation = GetTranslation(viewToWorldPrevLh);
            var translationDelta = prevViewTranslation - viewTranslation;

            viewToWorldLh = SetTranslation(viewToWorldLh, Vector3.zero);
            worldToViewLh = viewToWorldLh.inverse;

            viewToWorldPrevLh = SetTranslation(viewToWorldPrevLh, translationDelta);
            worldToViewPrevLh = viewToWorldPrevLh.inverse;

            constants.gWorldToView = worldToViewLh;
            constants.gViewToClip = viewToClipLh;
            constants.gWorldToClipPrev = viewToClipPrevLh * worldToViewPrevLh;
            constants.gWorldToViewPrev = worldToViewPrevLh;
            constants.gFrustum = currentProjection.Frustum;
            constants.gFrustumPrev = previousProjection.Frustum;
            constants.gUnproject = 1.0f / (0.5f * height * Mathf.Max(currentProjection.ProjectY, Epsilon));

            float angle = SequenceHelpers.Weyl1D(0.0f, (int)(frameIndex * 2u)) * Mathf.Deg2Rad * 90.0f;
            float angleBayer = SequenceHelpers.Bayer4x4(frameIndex * 2u) * Mathf.Deg2Rad * 360.0f;
            constants.gRotator = SequenceHelpers.CombineRotators(
                SequenceHelpers.GetRotator(angle),
                SequenceHelpers.GetRotator(angleBayer));

            float anglePost = SequenceHelpers.Weyl1D(0.0f, (int)(frameIndex * 2u + 1u)) * Mathf.Deg2Rad * 90.0f;
            float anglePostBayer = SequenceHelpers.Bayer4x4(frameIndex * 2u + 1u) * Mathf.Deg2Rad * 360.0f;
            constants.gRotatorPost = SequenceHelpers.CombineRotators(
                SequenceHelpers.GetRotator(anglePost),
                SequenceHelpers.GetRotator(anglePostBayer));

            // View vector (for ortho mode)
            var viewDirectionWorld = -GetColumn(viewToWorldLh, 2).normalized;
            constants.gViewVectorWorld = new Vector4(viewDirectionWorld.x, viewDirectionWorld.y, viewDirectionWorld.z, 0f);

            // Light direction in view space
            var lightDirView = worldToViewLh.MultiplyVector(lightDirectionWS).normalized;
            constants.gLightDirectionView = new Vector4(lightDirView.x, lightDirView.y, lightDirView.z, 0f);

            // Camera delta
            constants.gCameraDelta = new Vector4(translationDelta.x, translationDelta.y, translationDelta.z, 0f);

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

            constants.gOrthoMode = currentProjection.IsOrthographic ? -1f : 0f;
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

        private static ProjectionData DecomposeProjection(Matrix4x4 projection, bool isOrthographicHint)
        {
            bool isReversedZ = MvpToPlanes(projection, out var planes);
            bool isOrthographic = isOrthographicHint || IsOrthographicProjection(projection);

            float x0;
            float x1;
            float y0;
            float y1;

            if (isOrthographic)
            {
                x0 = -planes.Left.w;
                x1 = planes.Right.w;
                y0 = -planes.Bottom.w;
                y1 = planes.Top.w;

                if (projection[1, 1] < 0.0f)
                    Swap(ref y0, ref y1);
            }
            else
            {
                x0 = planes.Left.z / planes.Left.x;
                x1 = planes.Right.z / planes.Right.x;
                y0 = planes.Bottom.z / planes.Bottom.y;
                y1 = planes.Top.z / planes.Top.y;
            }

            float nearZ = -planes.Near.w;
            Vector4 clip = projection * new Vector4(0.0f, 0.0f, nearZ, 1.0f);
            Vector3 column2 = isOrthographic
                ? GetColumn(projection, 2) * (isReversedZ ? -1.0f : 1.0f)
                : new Vector3(0.0f, 0.0f, clip.w > 0.0f ? 1.0f : -1.0f);

            bool compare = Vector3.Dot(
                Vector3.Cross(GetColumn(projection, 0), GetColumn(projection, 1)),
                column2) > 0.0f;

            bool isLeftHanded = projection[1, 1] > 0.0f ? compare : !compare;
            float projectY = Mathf.Abs(2.0f / Mathf.Max(y1 - y0, Epsilon));
            Vector4 frustum = new Vector4(-x0, -y1, x0 - x1, y1 - y0);

            return new ProjectionData(isLeftHanded, isOrthographic, frustum, projectY);
        }

        internal static Matrix4x4 ConvertProjectionToLeftHanded(Matrix4x4 viewToClip)
        {
            for (int row = 0; row < 4; row++)
                viewToClip[row, 2] = -viewToClip[row, 2];

            return viewToClip;
        }

        internal static Matrix4x4 ConvertWorldToViewToLeftHanded(Matrix4x4 worldToView)
        {
            for (int column = 0; column < 4; column++)
                worldToView[2, column] = -worldToView[2, column];

            return worldToView;
        }

        private static bool MvpToPlanes(Matrix4x4 matrix, out ProjectionPlanes planes)
        {
            Vector4 left = NormalizePlane(matrix.GetRow(3) + matrix.GetRow(0));
            Vector4 right = NormalizePlane(matrix.GetRow(3) - matrix.GetRow(0));
            Vector4 bottom = NormalizePlane(matrix.GetRow(3) + matrix.GetRow(1));
            Vector4 top = NormalizePlane(matrix.GetRow(3) - matrix.GetRow(1));
            Vector4 far = NormalizePlane(matrix.GetRow(3) - matrix.GetRow(2));
            Vector4 near = NormalizePlane(matrix.GetRow(2));

            bool isReversedZ = Mathf.Abs(near.w) > Mathf.Abs(far.w);
            if (isReversedZ)
                Swap(ref near, ref far);

            if (GetLengthSquared(far) < Epsilon * Epsilon)
                far = new Vector4(-near.x, -near.y, -near.z, far.w);

            planes = new ProjectionPlanes(left, right, bottom, top, near, far);

            return isReversedZ;
        }

        private static bool IsOrthographicProjection(Matrix4x4 projection)
        {
            return Mathf.Abs(projection[3, 3] - 1.0f) <= 1e-5f;
        }

        private static Vector4 NormalizePlane(Vector4 plane)
        {
            float length = Mathf.Sqrt(GetLengthSquared(plane));
            return length > Epsilon ? plane / length : plane;
        }

        private static float GetLengthSquared(Vector4 vector)
        {
            return vector.x * vector.x + vector.y * vector.y + vector.z * vector.z;
        }

        private static Vector3 GetTranslation(Matrix4x4 matrix)
        {
            Vector4 column = matrix.GetColumn(3);
            return new Vector3(column.x, column.y, column.z);
        }

        private static Vector3 GetColumn(Matrix4x4 matrix, int index)
        {
            Vector4 column = matrix.GetColumn(index);
            return new Vector3(column.x, column.y, column.z);
        }

        private static Matrix4x4 SetTranslation(Matrix4x4 matrix, Vector3 translation)
        {
            matrix.SetColumn(3, new Vector4(translation.x, translation.y, translation.z, 1.0f));
            return matrix;
        }

        private static void Swap(ref Vector4 left, ref Vector4 right)
        {
            (left, right) = (right, left);
        }

        private static void Swap(ref float left, ref float right)
        {
            (left, right) = (right, left);
        }
    }

    internal static class SequenceHelpers
    {
        public static Vector4 GetRotator(float angle)
        {
            float ca = Mathf.Cos(angle);
            float sa = Mathf.Sin(angle);
            return new Vector4(ca, sa, -sa, ca);
        }

        public static Vector4 CombineRotators(Vector4 first, Vector4 second)
        {
            return new Vector4(
                first.x * second.x + first.z * second.y,
                first.y * second.x + first.w * second.y,
                first.x * second.z + first.z * second.w,
                first.y * second.z + first.w * second.w);
        }

        public static float Bayer4x4(uint frameIndex)
        {
            const uint sampleX = 0u;
            const uint sampleY = 0u;
            uint sampleOffset = ReverseBits4(frameIndex);
            uint a = 2068378560u * (1u - (sampleX >> 1)) + 1500172770u * (sampleX >> 1);
            uint b = (sampleY + ((sampleX & 1u) << 2)) << 2;
            return (((a >> (int)b) + sampleOffset) & 0xFu) / 16.0f;
        }

        public static float Weyl1D(float p, int n)
        {
            const float invPow2_24 = 1.0f / 16777216.0f;
            int wrapped = unchecked(n * 10368889);
            return Mathf.Repeat(p + wrapped * invPow2_24, 1.0f);
        }

        private static uint ReverseBits4(uint value)
        {
            value &= 0xFu;
            value = ((value & 0x5u) << 1) | ((value & 0xAu) >> 1);
            value = ((value & 0x3u) << 2) | ((value & 0xCu) >> 2);
            return value;
        }
    }
}
