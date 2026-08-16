using System;
using UnityEngine;

namespace VividRP.Runtime.Experimental.Materials
{
    public enum VividExperimentalClosureModel : uint
    {
        Slab = 0,
    }

    public enum VividExperimentalClosureComplexity : uint
    {
        Fast = 0,
        Single = 1,
        Complex = 2,
    }

    [Flags]
    public enum VividExperimentalClosureFeatures : uint
    {
        None = 0,
        Coat = 1 << 0,
        Transmission = 1 << 1,
        Subsurface = 1 << 2,
    }

    [Flags]
    public enum VividExperimentalCompatibilityLoss : uint
    {
        None = 0,
        SpecularIor = 1 << 0,
        CoatRoughness = 1 << 1,
        Transmission = 1 << 2,
        Subsurface = 1 << 3,
    }

    public static class VividExperimentalClosureContract
    {
        public const uint SemanticVersion = 1;
        public const uint MaxClosureCount = 2;
        public const float LegacyDielectricIor = 1.5f;
        public const float LegacyCoatLinearRoughness = 0.01f;

        public static float SanitizeIor(float specularIor)
        {
            return float.IsNaN(specularIor) || float.IsInfinity(specularIor)
                ? LegacyDielectricIor
                : Mathf.Clamp(specularIor, 1.0f, 3.0f);
        }

        public static float IorToF0(float specularIor)
        {
            float ior = SanitizeIor(specularIor);
            float ratio = (ior - 1.0f) / (ior + 1.0f);
            return ratio * ratio;
        }

        public static Vector3 ResolveSpecularF0(
            Vector3 baseColor,
            float metallic,
            float specularIor)
        {
            float clampedMetallic = Mathf.Clamp01(metallic);
            float dielectricF0 = IorToF0(specularIor);
            return Vector3.Lerp(
                new Vector3(dielectricF0, dielectricF0, dielectricF0),
                Vector3.Max(baseColor, Vector3.zero),
                clampedMetallic);
        }

        public static VividExperimentalClosureComplexity Classify(
            uint closureCount,
            VividExperimentalClosureFeatures features)
        {
            if (closureCount > 1)
                return VividExperimentalClosureComplexity.Complex;

            return features == VividExperimentalClosureFeatures.None
                ? VividExperimentalClosureComplexity.Fast
                : VividExperimentalClosureComplexity.Single;
        }

        public static VividExperimentalCompatibilityLoss GetLegacyCompatibilityLoss(
            float specularIor,
            float clearCoatWeight,
            float clearCoatPerceptualRoughness,
            float transmissionWeight,
            float subsurfaceWeight)
        {
            VividExperimentalCompatibilityLoss loss =
                VividExperimentalCompatibilityLoss.None;

            float sanitizedIor = SanitizeIor(specularIor);
            if (Mathf.Abs(sanitizedIor - LegacyDielectricIor) > 0.0001f)
                loss |= VividExperimentalCompatibilityLoss.SpecularIor;

            float coatRoughness = Mathf.Clamp01(clearCoatPerceptualRoughness);
            float coatLinearRoughness = Mathf.Max(
                coatRoughness * coatRoughness,
                0.0001f);
            if (clearCoatWeight > 0.0f
                && Mathf.Abs(
                    coatLinearRoughness
                    - LegacyCoatLinearRoughness) > 0.0001f)
            {
                loss |= VividExperimentalCompatibilityLoss.CoatRoughness;
            }

            if (transmissionWeight > 0.0f)
                loss |= VividExperimentalCompatibilityLoss.Transmission;
            if (subsurfaceWeight > 0.0f)
                loss |= VividExperimentalCompatibilityLoss.Subsurface;

            return loss;
        }
    }
}
