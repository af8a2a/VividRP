using System;
using Unity.Mathematics;

namespace VividRP.Runtime.GPUDriven
{
    internal enum OpenPBROpaqueFieldSemantic : uint
    {
        BaseWeight = 0u,
        BaseColor = 1u,
        BaseDiffuseRoughness = 2u,
        BaseMetalness = 3u,
        SpecularWeight = 4u,
        SpecularColor = 5u,
        SpecularRoughness = 6u,
        SpecularIor = 7u,
        NormalWS = 8u,
        EmissionLuminance = 9u,
        EmissionColor = 10u,
    }

    [Flags]
    internal enum OpenPBROpaqueUnsupportedFeatures : uint
    {
        None = 0u,
        Coat = 1u << 0,
        Fuzz = 1u << 1,
        Transmission = 1u << 2,
        Subsurface = 1u << 3,
        ThinFilm = 1u << 4,
        SpecularAnisotropy = 1u << 5,
        CoatAnisotropy = 1u << 6,
        Dispersion = 1u << 7,
        ThinWalled = 1u << 8,
    }

    [Flags]
    internal enum OpenPBROpaqueValidationErrors : uint
    {
        None = 0u,
        UnsupportedFeatures = 1u << 0,
        NonFinite = 1u << 1,
        OutOfRange = 1u << 2,
        InvalidNormal = 1u << 3,
    }

    // Semantic inputs only. This is not a serialized asset or a GPU buffer ABI.
    internal struct OpenPBROpaqueInputs
    {
        internal float BaseWeight;
        internal float3 BaseColor;
        internal float BaseDiffuseRoughness;
        internal float BaseMetalness;
        internal float SpecularWeight;
        internal float3 SpecularColor;
        internal float SpecularRoughness;
        internal float SpecularIor;
        internal float3 NormalWS;
        internal float EmissionLuminance;
        internal float3 EmissionColor;
    }

    internal static class OpenPBROpaqueContract
    {
        internal const uint Version = 1u;
        internal const uint FingerprintVersion = 1u;
        internal const uint VendorSpecificationMajor = 1u;
        internal const uint VendorSpecificationMinor = 1u;
        internal const uint FieldCount = 11u;
        internal const uint ClosureCount = 1u;

        internal const float DefaultBaseWeight = 1.0f;
        internal const float DefaultBaseColor = 0.8f;
        internal const float DefaultBaseDiffuseRoughness = 0.0f;
        internal const float DefaultBaseMetalness = 0.0f;
        internal const float DefaultSpecularWeight = 1.0f;
        internal const float DefaultSpecularColor = 1.0f;
        internal const float DefaultSpecularRoughness = 0.3f;
        internal const float DefaultSpecularIor = 1.5f;
        internal const float DefaultEmissionLuminance = 0.0f;
        internal const float DefaultEmissionColor = 1.0f;

        // These are Vivid profile limits, not limits of the complete OpenPBR model.
        internal const float MinimumUnitInput = 0.0f;
        internal const float MaximumUnitInput = 1.0f;
        internal const float MinimumSpecularIor = 1.0f;
        internal const float MaximumSpecularIor = 3.0f;
        internal const float NormalLengthSquaredTolerance = 0.0001f;
        internal const float VendorMinimumMicrofacetRoughness = 0.001f;
        internal const float ExteriorIor = 1.0f;
        internal const float GeometryOpacity = 1.0f;
        internal const float PreparePathThroughput = 1.0f;
        internal const float RgbWavelengthRed = 620.0f;
        internal const float RgbWavelengthGreen = 540.0f;
        internal const float RgbWavelengthBlue = 450.0f;

        internal const bool Isotropic = true;
        internal const bool DirectResponseIncludesCosine = true;
        internal const bool SampleWeightIncludesCosineOverPdf = true;
        internal const bool CoverageIsExternal = true;
        internal const bool AmbientOcclusionAffectsDirectLighting = false;
        internal const bool AmbientOcclusionAffectsIndirectLighting = true;
        internal const bool PreparedEmissionIsAddedOnce = true;
        internal const bool AdditionalSimpleSlabCompensation = false;
        internal const bool SupportsIdealDeltaSpecular = false;
        internal const bool NormalizeAcceptedNormalForBasis = true;

        internal static readonly ulong Fingerprint = ComputeFingerprint();

        internal static OpenPBROpaqueInputs CreateDefault()
        {
            return new OpenPBROpaqueInputs
            {
                BaseWeight = DefaultBaseWeight,
                BaseColor = new float3(DefaultBaseColor),
                BaseDiffuseRoughness = DefaultBaseDiffuseRoughness,
                BaseMetalness = DefaultBaseMetalness,
                SpecularWeight = DefaultSpecularWeight,
                SpecularColor = new float3(DefaultSpecularColor),
                SpecularRoughness = DefaultSpecularRoughness,
                SpecularIor = DefaultSpecularIor,
                NormalWS = new float3(0.0f, 0.0f, 1.0f),
                EmissionLuminance = DefaultEmissionLuminance,
                EmissionColor = new float3(DefaultEmissionColor),
            };
        }

        // The caller must report all requested excluded features, including unknown
        // mask bits. No normalization, clamping or unsupported-lobe fallback occurs.
        internal static OpenPBROpaqueValidationErrors Validate(
            in OpenPBROpaqueInputs inputs,
            OpenPBROpaqueUnsupportedFeatures requestedFeatures)
        {
            OpenPBROpaqueValidationErrors errors = requestedFeatures
                    == OpenPBROpaqueUnsupportedFeatures.None
                ? OpenPBROpaqueValidationErrors.None
                : OpenPBROpaqueValidationErrors.UnsupportedFeatures;

            bool finite = math.isfinite(inputs.BaseWeight)
                && math.all(math.isfinite(inputs.BaseColor))
                && math.isfinite(inputs.BaseDiffuseRoughness)
                && math.isfinite(inputs.BaseMetalness)
                && math.isfinite(inputs.SpecularWeight)
                && math.all(math.isfinite(inputs.SpecularColor))
                && math.isfinite(inputs.SpecularRoughness)
                && math.isfinite(inputs.SpecularIor)
                && math.all(math.isfinite(inputs.NormalWS))
                && math.isfinite(inputs.EmissionLuminance)
                && math.all(math.isfinite(inputs.EmissionColor))
                && math.all(math.isfinite(
                    inputs.EmissionColor * inputs.EmissionLuminance));
            if (!finite)
                errors |= OpenPBROpaqueValidationErrors.NonFinite;

            if (!IsUnitInput(inputs.BaseWeight)
                || !IsUnitInput(inputs.BaseColor)
                || !IsUnitInput(inputs.BaseDiffuseRoughness)
                || !IsUnitInput(inputs.BaseMetalness)
                || !IsUnitInput(inputs.SpecularWeight)
                || !IsUnitInput(inputs.SpecularColor)
                || !IsUnitInput(inputs.SpecularRoughness)
                || inputs.SpecularIor < MinimumSpecularIor
                || inputs.SpecularIor > MaximumSpecularIor
                || inputs.EmissionLuminance < 0.0f
                || math.any(inputs.EmissionColor < 0.0f))
            {
                errors |= OpenPBROpaqueValidationErrors.OutOfRange;
            }

            float normalLengthSquared = math.lengthsq(inputs.NormalWS);
            if (!math.isfinite(normalLengthSquared)
                || math.abs(normalLengthSquared - 1.0f)
                    > NormalLengthSquaredTolerance)
            {
                errors |= OpenPBROpaqueValidationErrors.InvalidNormal;
            }
            return errors;
        }

        private static bool IsUnitInput(float value)
        {
            return value >= MinimumUnitInput && value <= MaximumUnitInput;
        }

        private static bool IsUnitInput(float3 value)
        {
            return math.all(value >= MinimumUnitInput)
                && math.all(value <= MaximumUnitInput);
        }

        private static ulong ComputeFingerprint()
        {
            ulong hash = MaterialProgramHashUtility.OffsetBasis;
            MaterialProgramHashUtility.Add(ref hash, FingerprintVersion);
            MaterialProgramHashUtility.Add(ref hash, Version);
            MaterialProgramHashUtility.Add(ref hash, VendorSpecificationMajor);
            MaterialProgramHashUtility.Add(ref hash, VendorSpecificationMinor);
            MaterialProgramHashUtility.Add(ref hash, FieldCount);
            MaterialProgramHashUtility.Add(ref hash, ClosureCount);
            // Field semantic and float component count in declaration order.
            for (uint semantic = 0u; semantic < FieldCount; ++semantic)
            {
                MaterialProgramHashUtility.Add(ref hash, semantic);
                bool vector = semantic == (uint) OpenPBROpaqueFieldSemantic.BaseColor
                    || semantic == (uint) OpenPBROpaqueFieldSemantic.SpecularColor
                    || semantic == (uint) OpenPBROpaqueFieldSemantic.NormalWS
                    || semantic == (uint) OpenPBROpaqueFieldSemantic.EmissionColor;
                MaterialProgramHashUtility.Add(ref hash, vector ? 3u : 1u);
            }
            AddFloat(ref hash, DefaultBaseWeight);
            AddFloat(ref hash, DefaultBaseColor);
            AddFloat(ref hash, DefaultBaseDiffuseRoughness);
            AddFloat(ref hash, DefaultBaseMetalness);
            AddFloat(ref hash, DefaultSpecularWeight);
            AddFloat(ref hash, DefaultSpecularColor);
            AddFloat(ref hash, DefaultSpecularRoughness);
            AddFloat(ref hash, DefaultSpecularIor);
            AddFloat(ref hash, 0.0f); // Default normal x.
            AddFloat(ref hash, 0.0f); // Default normal y.
            AddFloat(ref hash, 1.0f); // Default normal z.
            AddFloat(ref hash, DefaultEmissionLuminance);
            AddFloat(ref hash, DefaultEmissionColor);
            AddFloat(ref hash, MinimumUnitInput);
            AddFloat(ref hash, MaximumUnitInput);
            AddFloat(ref hash, MinimumSpecularIor);
            AddFloat(ref hash, MaximumSpecularIor);
            AddFloat(ref hash, NormalLengthSquaredTolerance);
            AddFloat(ref hash, VendorMinimumMicrofacetRoughness);
            AddFloat(ref hash, ExteriorIor);
            AddFloat(ref hash, GeometryOpacity);
            AddFloat(ref hash, PreparePathThroughput);
            AddFloat(ref hash, RgbWavelengthRed);
            AddFloat(ref hash, RgbWavelengthGreen);
            AddFloat(ref hash, RgbWavelengthBlue);
            // No feature mask bits are supported, including future/unknown bits.
            MaterialProgramHashUtility.Add(ref hash, 0u);
            MaterialProgramHashUtility.Add(ref hash, Isotropic);
            MaterialProgramHashUtility.Add(ref hash, DirectResponseIncludesCosine);
            MaterialProgramHashUtility.Add(ref hash, SampleWeightIncludesCosineOverPdf);
            MaterialProgramHashUtility.Add(ref hash, CoverageIsExternal);
            MaterialProgramHashUtility.Add(ref hash, AmbientOcclusionAffectsDirectLighting);
            MaterialProgramHashUtility.Add(ref hash, AmbientOcclusionAffectsIndirectLighting);
            MaterialProgramHashUtility.Add(ref hash, PreparedEmissionIsAddedOnce);
            MaterialProgramHashUtility.Add(ref hash, AdditionalSimpleSlabCompensation);
            MaterialProgramHashUtility.Add(ref hash, SupportsIdealDeltaSpecular);
            MaterialProgramHashUtility.Add(ref hash, NormalizeAcceptedNormalForBasis);
            return hash;
        }

        private static void AddFloat(ref ulong hash, float value)
        {
            MaterialProgramHashUtility.Add(ref hash, math.asuint(value));
        }
    }
}
