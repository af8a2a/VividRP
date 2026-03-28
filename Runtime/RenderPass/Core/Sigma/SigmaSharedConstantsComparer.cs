using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace VividRP.Runtime.RenderPass.Core.Sigma
{
    internal readonly struct SigmaSharedConstantsComparison
    {
        public SigmaSharedConstantsComparison(
            bool hasDifferences,
            float maxFloatDifference,
            int differentFieldCount,
            string fieldSignature,
            string summary)
        {
            HasDifferences = hasDifferences;
            MaxFloatDifference = maxFloatDifference;
            DifferentFieldCount = differentFieldCount;
            FieldSignature = fieldSignature;
            Summary = summary;
        }

        public bool HasDifferences { get; }

        public float MaxFloatDifference { get; }

        public int DifferentFieldCount { get; }

        public string FieldSignature { get; }

        public string Summary { get; }
    }

    internal static class SigmaSharedConstantsComparer
    {
        private const float DefaultTolerance = 1e-4f;
        private const int MaxReportedDifferences = 8;

        public static SigmaSharedConstantsComparison Compare(
            in SigmaSharedConstants manual,
            in SigmaSharedConstants native,
            float tolerance = DefaultTolerance)
        {
            var differenceNames = new List<string>();
            var summary = new StringBuilder();
            float maxFloatDifference = 0.0f;

            CompareMatrix(ref differenceNames, summary, "gWorldToView", manual.gWorldToView, native.gWorldToView, tolerance, ref maxFloatDifference);
            CompareMatrix(ref differenceNames, summary, "gViewToClip", manual.gViewToClip, native.gViewToClip, tolerance, ref maxFloatDifference);
            CompareMatrix(ref differenceNames, summary, "gWorldToClipPrev", manual.gWorldToClipPrev, native.gWorldToClipPrev, tolerance, ref maxFloatDifference);
            CompareMatrix(ref differenceNames, summary, "gWorldToViewPrev", manual.gWorldToViewPrev, native.gWorldToViewPrev, tolerance, ref maxFloatDifference);
            CompareVector4(ref differenceNames, summary, "gRotator", manual.gRotator, native.gRotator, tolerance, ref maxFloatDifference);
            CompareVector4(ref differenceNames, summary, "gRotatorPost", manual.gRotatorPost, native.gRotatorPost, tolerance, ref maxFloatDifference);
            CompareVector4(ref differenceNames, summary, "gViewVectorWorld", manual.gViewVectorWorld, native.gViewVectorWorld, tolerance, ref maxFloatDifference);
            CompareVector4(ref differenceNames, summary, "gLightDirectionView", manual.gLightDirectionView, native.gLightDirectionView, tolerance, ref maxFloatDifference);
            CompareVector4(ref differenceNames, summary, "gFrustum", manual.gFrustum, native.gFrustum, tolerance, ref maxFloatDifference);
            CompareVector4(ref differenceNames, summary, "gFrustumPrev", manual.gFrustumPrev, native.gFrustumPrev, tolerance, ref maxFloatDifference);
            CompareVector4(ref differenceNames, summary, "gCameraDelta", manual.gCameraDelta, native.gCameraDelta, tolerance, ref maxFloatDifference);
            CompareVector4(ref differenceNames, summary, "gMvScale", manual.gMvScale, native.gMvScale, tolerance, ref maxFloatDifference);
            CompareVector2(ref differenceNames, summary, "gResourceSizeInv", manual.gResourceSizeInv, native.gResourceSizeInv, tolerance, ref maxFloatDifference);
            CompareVector2(ref differenceNames, summary, "gResourceSizeInvPrev", manual.gResourceSizeInvPrev, native.gResourceSizeInvPrev, tolerance, ref maxFloatDifference);
            CompareVector2(ref differenceNames, summary, "gRectSize", manual.gRectSize, native.gRectSize, tolerance, ref maxFloatDifference);
            CompareVector2(ref differenceNames, summary, "gRectSizeInv", manual.gRectSizeInv, native.gRectSizeInv, tolerance, ref maxFloatDifference);
            CompareVector2(ref differenceNames, summary, "gRectSizePrev", manual.gRectSizePrev, native.gRectSizePrev, tolerance, ref maxFloatDifference);
            CompareVector2(ref differenceNames, summary, "gResolutionScale", manual.gResolutionScale, native.gResolutionScale, tolerance, ref maxFloatDifference);
            CompareVector2(ref differenceNames, summary, "gRectOffset", manual.gRectOffset, native.gRectOffset, tolerance, ref maxFloatDifference);
            CompareFloat(ref differenceNames, summary, "gOrthoMode", manual.gOrthoMode, native.gOrthoMode, tolerance, ref maxFloatDifference);
            CompareFloat(ref differenceNames, summary, "gUnproject", manual.gUnproject, native.gUnproject, tolerance, ref maxFloatDifference);
            CompareFloat(ref differenceNames, summary, "gDenoisingRange", manual.gDenoisingRange, native.gDenoisingRange, tolerance, ref maxFloatDifference);
            CompareFloat(ref differenceNames, summary, "gPlaneDistSensitivity", manual.gPlaneDistSensitivity, native.gPlaneDistSensitivity, tolerance, ref maxFloatDifference);
            CompareFloat(ref differenceNames, summary, "gStabilizationStrength", manual.gStabilizationStrength, native.gStabilizationStrength, tolerance, ref maxFloatDifference);
            CompareFloat(ref differenceNames, summary, "gDebug", manual.gDebug, native.gDebug, tolerance, ref maxFloatDifference);
            CompareFloat(ref differenceNames, summary, "gSplitScreen", manual.gSplitScreen, native.gSplitScreen, tolerance, ref maxFloatDifference);
            CompareFloat(ref differenceNames, summary, "gViewZScale", manual.gViewZScale, native.gViewZScale, tolerance, ref maxFloatDifference);
            CompareFloat(ref differenceNames, summary, "gMinRectDimMulUnproject", manual.gMinRectDimMulUnproject, native.gMinRectDimMulUnproject, tolerance, ref maxFloatDifference);
            CompareUInt(ref differenceNames, summary, "gPrintfAtX", manual.gPrintfAtX, native.gPrintfAtX, ref maxFloatDifference);
            CompareUInt(ref differenceNames, summary, "gPrintfAtY", manual.gPrintfAtY, native.gPrintfAtY, ref maxFloatDifference);
            CompareUInt(ref differenceNames, summary, "gRectOriginX", manual.gRectOriginX, native.gRectOriginX, ref maxFloatDifference);
            CompareUInt(ref differenceNames, summary, "gRectOriginY", manual.gRectOriginY, native.gRectOriginY, ref maxFloatDifference);
            CompareInt(ref differenceNames, summary, "gRectSizeMinusOneX", manual.gRectSizeMinusOneX, native.gRectSizeMinusOneX, ref maxFloatDifference);
            CompareInt(ref differenceNames, summary, "gRectSizeMinusOneY", manual.gRectSizeMinusOneY, native.gRectSizeMinusOneY, ref maxFloatDifference);
            CompareInt(ref differenceNames, summary, "gTilesSizeMinusOneX", manual.gTilesSizeMinusOneX, native.gTilesSizeMinusOneX, ref maxFloatDifference);
            CompareInt(ref differenceNames, summary, "gTilesSizeMinusOneY", manual.gTilesSizeMinusOneY, native.gTilesSizeMinusOneY, ref maxFloatDifference);
            CompareUInt(ref differenceNames, summary, "gFrameIndex", manual.gFrameIndex, native.gFrameIndex, ref maxFloatDifference);
            CompareUInt(ref differenceNames, summary, "gIsRectChanged", manual.gIsRectChanged, native.gIsRectChanged, ref maxFloatDifference);

            if (differenceNames.Count == 0)
            {
                return new SigmaSharedConstantsComparison(
                    false,
                    0.0f,
                    0,
                    "match",
                    "All SIGMA shared constants match within tolerance.");
            }

            return new SigmaSharedConstantsComparison(
                true,
                maxFloatDifference,
                differenceNames.Count,
                string.Join("|", differenceNames),
                summary.ToString());
        }

        private static void CompareMatrix(
            ref List<string> differenceNames,
            StringBuilder summary,
            string fieldName,
            Matrix4x4 manual,
            Matrix4x4 native,
            float tolerance,
            ref float maxFloatDifference)
        {
            float localMax = 0.0f;

            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    localMax = Mathf.Max(localMax, Mathf.Abs(manual[row, column] - native[row, column]));
                }
            }

            if (localMax <= tolerance)
            {
                return;
            }

            AddDifference(
                ref differenceNames,
                summary,
                fieldName,
                localMax,
                ref maxFloatDifference,
                $"{fieldName} max|Δ|={FormatFloat(localMax)} manual={FormatMatrix(manual)} native={FormatMatrix(native)}");
        }

        private static void CompareVector4(
            ref List<string> differenceNames,
            StringBuilder summary,
            string fieldName,
            Vector4 manual,
            Vector4 native,
            float tolerance,
            ref float maxFloatDifference)
        {
            float localMax = Mathf.Max(
                Mathf.Abs(manual.x - native.x),
                Mathf.Abs(manual.y - native.y),
                Mathf.Abs(manual.z - native.z),
                Mathf.Abs(manual.w - native.w));

            if (localMax <= tolerance)
            {
                return;
            }

            AddDifference(
                ref differenceNames,
                summary,
                fieldName,
                localMax,
                ref maxFloatDifference,
                $"{fieldName} max|Δ|={FormatFloat(localMax)} manual={FormatVector4(manual)} native={FormatVector4(native)}");
        }

        private static void CompareVector2(
            ref List<string> differenceNames,
            StringBuilder summary,
            string fieldName,
            Vector2 manual,
            Vector2 native,
            float tolerance,
            ref float maxFloatDifference)
        {
            float localMax = Mathf.Max(
                Mathf.Abs(manual.x - native.x),
                Mathf.Abs(manual.y - native.y));

            if (localMax <= tolerance)
            {
                return;
            }

            AddDifference(
                ref differenceNames,
                summary,
                fieldName,
                localMax,
                ref maxFloatDifference,
                $"{fieldName} max|Δ|={FormatFloat(localMax)} manual={FormatVector2(manual)} native={FormatVector2(native)}");
        }

        private static void CompareFloat(
            ref List<string> differenceNames,
            StringBuilder summary,
            string fieldName,
            float manual,
            float native,
            float tolerance,
            ref float maxFloatDifference)
        {
            float localMax = Mathf.Abs(manual - native);
            if (localMax <= tolerance)
            {
                return;
            }

            AddDifference(
                ref differenceNames,
                summary,
                fieldName,
                localMax,
                ref maxFloatDifference,
                $"{fieldName} max|Δ|={FormatFloat(localMax)} manual={FormatFloat(manual)} native={FormatFloat(native)}");
        }

        private static void CompareUInt(
            ref List<string> differenceNames,
            StringBuilder summary,
            string fieldName,
            uint manual,
            uint native,
            ref float maxFloatDifference)
        {
            if (manual == native)
            {
                return;
            }

            AddDifference(
                ref differenceNames,
                summary,
                fieldName,
                Mathf.Abs((float)manual - native),
                ref maxFloatDifference,
                $"{fieldName} manual={manual.ToString(CultureInfo.InvariantCulture)} native={native.ToString(CultureInfo.InvariantCulture)}");
        }

        private static void CompareInt(
            ref List<string> differenceNames,
            StringBuilder summary,
            string fieldName,
            int manual,
            int native,
            ref float maxFloatDifference)
        {
            if (manual == native)
            {
                return;
            }

            AddDifference(
                ref differenceNames,
                summary,
                fieldName,
                Mathf.Abs(manual - native),
                ref maxFloatDifference,
                $"{fieldName} manual={manual.ToString(CultureInfo.InvariantCulture)} native={native.ToString(CultureInfo.InvariantCulture)}");
        }

        private static void AddDifference(
            ref List<string> differenceNames,
            StringBuilder summary,
            string fieldName,
            float localMax,
            ref float maxFloatDifference,
            string detail)
        {
            differenceNames.Add(fieldName);
            maxFloatDifference = Mathf.Max(maxFloatDifference, localMax);

            if (differenceNames.Count > MaxReportedDifferences)
            {
                return;
            }

            if (summary.Length > 0)
            {
                summary.Append("; ");
            }

            summary.Append(detail);
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("G6", CultureInfo.InvariantCulture);
        }

        private static string FormatVector2(Vector2 value)
        {
            return $"({FormatFloat(value.x)}, {FormatFloat(value.y)})";
        }

        private static string FormatVector4(Vector4 value)
        {
            return $"({FormatFloat(value.x)}, {FormatFloat(value.y)}, {FormatFloat(value.z)}, {FormatFloat(value.w)})";
        }

        private static string FormatMatrix(Matrix4x4 value)
        {
            return
                $"[{FormatFloat(value.m00)}, {FormatFloat(value.m01)}, {FormatFloat(value.m02)}, {FormatFloat(value.m03)} | " +
                $"{FormatFloat(value.m10)}, {FormatFloat(value.m11)}, {FormatFloat(value.m12)}, {FormatFloat(value.m13)} | " +
                $"{FormatFloat(value.m20)}, {FormatFloat(value.m21)}, {FormatFloat(value.m22)}, {FormatFloat(value.m23)} | " +
                $"{FormatFloat(value.m30)}, {FormatFloat(value.m31)}, {FormatFloat(value.m32)}, {FormatFloat(value.m33)}]";
        }
    }
}
