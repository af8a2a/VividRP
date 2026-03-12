using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal static class ColorGradingCurvePresets
    {
        private const float CurveTolerance = 1e-3f;
        private static readonly float[] s_CurveSamples = { 0f, 0.25f, 0.5f, 0.75f, 1f };

        internal static TextureCurve CreateLinearCurve()
        {
            return new TextureCurve(
                new[]
                {
                    new Keyframe(0f, 0f),
                    new Keyframe(1f, 1f),
                },
                0f,
                false,
                new Vector2(0f, 1f));
        }

        internal static TextureCurve CreateFlatCurve(float value, bool loop)
        {
            return new TextureCurve(
                Array.Empty<Keyframe>(),
                value,
                loop,
                new Vector2(0f, 1f));
        }

        internal static bool IsLinearCurve(TextureCurve curve)
        {
            if (curve == null)
                return true;

            foreach (var sample in s_CurveSamples)
            {
                if (Mathf.Abs(curve.Evaluate(sample) - sample) > CurveTolerance)
                    return false;
            }

            return true;
        }

        internal static bool IsFlatCurve(TextureCurve curve, float value)
        {
            if (curve == null)
                return true;

            foreach (var sample in s_CurveSamples)
            {
                if (Mathf.Abs(curve.Evaluate(sample) - value) > CurveTolerance)
                    return false;
            }

            return true;
        }

        internal static bool IsApproximately(Color left, Color right, float epsilon = CurveTolerance)
        {
            return Mathf.Abs(left.r - right.r) <= epsilon
                && Mathf.Abs(left.g - right.g) <= epsilon
                && Mathf.Abs(left.b - right.b) <= epsilon
                && Mathf.Abs(left.a - right.a) <= epsilon;
        }
    }
}
