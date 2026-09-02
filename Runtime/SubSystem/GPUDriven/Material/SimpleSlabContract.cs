using System;
using System.Globalization;
using Unity.Mathematics;

namespace VividRP.Runtime.GPUDriven
{
    internal enum SimpleSlabFieldSemantic : uint
    {
        DiffuseAlbedo = 0u,
        SpecularF0 = 1u,
        PerceptualRoughness = 2u,
        NormalWS = 3u,
    }

    internal enum SimpleSlabDiffuseModel : uint
    {
        Lambert = 1u,
    }

    internal enum SimpleSlabSpecularModel : uint
    {
        IsotropicGgxSmithCorrelated = 1u,
    }

    internal enum SimpleSlabFresnelModel : uint
    {
        SchlickDerivedAchromaticF90 = 1u,
    }

    internal enum SimpleSlabEnergyModel : uint
    {
        DirectionalAlbedoMultipleScattering = 1u,
    }

    internal readonly struct SimpleSlabContractFingerprint :
        IEquatable<SimpleSlabContractFingerprint>
    {
        internal SimpleSlabContractFingerprint(uint version, ulong value)
        {
            Version = version;
            Value = value;
        }

        internal uint Version { get; }

        internal ulong Value { get; }

        public bool Equals(SimpleSlabContractFingerprint other)
        {
            return Version == other.Version && Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is SimpleSlabContractFingerprint other
                && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int) Version * 397)
                    ^ (int) Value
                    ^ (int) (Value >> 32);
            }
        }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "simple_slab_v={0} 0x{1:X16}",
                Version,
                Value);
        }

        public static bool operator ==(
            SimpleSlabContractFingerprint left,
            SimpleSlabContractFingerprint right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            SimpleSlabContractFingerprint left,
            SimpleSlabContractFingerprint right)
        {
            return !left.Equals(right);
        }
    }

    internal static class SimpleSlabContract
    {
        internal const uint Version =
            MaterialProgramContract.SimpleSlabContractVersion;
        internal const uint FingerprintVersion =
            MaterialProgramContract.SimpleSlabFingerprintVersion;
        internal const uint FieldCount = 4u;

        // Inputs are finite values in the active linear working color space.
        internal const float MinimumAlphaRoughness = 0.002f;
        internal const float DerivedF90FadeThreshold = 0.02f;
        internal const float DerivedF90Scale = 50.0f;

        internal const bool DirectResponseIncludesNdotL = true;
        internal const bool AmbientOcclusionAffectsDirectLighting = false;
        internal const bool AmbientOcclusionAffectsIndirectLighting = true;
        internal const bool EmissionIsAddedAfterLighting = true;

        internal const SimpleSlabDiffuseModel DiffuseModel =
            SimpleSlabDiffuseModel.Lambert;
        internal const SimpleSlabSpecularModel SpecularModel =
            SimpleSlabSpecularModel.IsotropicGgxSmithCorrelated;
        internal const SimpleSlabFresnelModel FresnelModel =
            SimpleSlabFresnelModel.SchlickDerivedAchromaticF90;
        internal const SimpleSlabEnergyModel EnergyModel =
            SimpleSlabEnergyModel.DirectionalAlbedoMultipleScattering;
        internal const MaterialDeferredExportSurfaceSummaryAbi SurfaceSummaryAbi =
            MaterialDeferredExportSurfaceSummaryAbi.SurfaceSummaryV1;

        internal static readonly SimpleSlabContractFingerprint Fingerprint =
            ComputeFingerprint();

        internal static float PerceptualRoughnessToAlpha(float perceptualRoughness)
        {
            float saturatedRoughness = math.saturate(perceptualRoughness);
            return math.max(
                saturatedRoughness * saturatedRoughness,
                MinimumAlphaRoughness);
        }

        internal static float DeriveAchromaticF90(float3 specularF0)
        {
            float3 saturatedF0 = math.saturate(specularF0);
            float averageF0 = math.csum(saturatedF0) * (1.0f / 3.0f);
            return math.saturate(averageF0 * DerivedF90Scale);
        }

        internal static float3 EvaluateSchlickFresnel(
            float3 specularF0,
            float cosine)
        {
            float3 saturatedF0 = math.saturate(specularF0);
            float f90 = DeriveAchromaticF90(saturatedF0);
            float oneMinusCosine = 1.0f - math.saturate(cosine);
            float schlickWeight = oneMinusCosine * oneMinusCosine;
            schlickWeight *= schlickWeight * oneMinusCosine;
            return math.lerp(saturatedF0, new float3(f90), schlickWeight);
        }

        private static SimpleSlabContractFingerprint ComputeFingerprint()
        {
            ulong hash = MaterialProgramHashUtility.OffsetBasis;
            MaterialProgramHashUtility.Add(ref hash, FingerprintVersion);
            MaterialProgramHashUtility.Add(ref hash, Version);
            MaterialProgramHashUtility.Add(ref hash, (uint) SurfaceSummaryAbi);
            MaterialProgramHashUtility.Add(ref hash, FieldCount);
            MaterialProgramHashUtility.Add(
                ref hash,
                (uint) SimpleSlabFieldSemantic.DiffuseAlbedo);
            MaterialProgramHashUtility.Add(
                ref hash,
                (uint) SimpleSlabFieldSemantic.SpecularF0);
            MaterialProgramHashUtility.Add(
                ref hash,
                (uint) SimpleSlabFieldSemantic.PerceptualRoughness);
            MaterialProgramHashUtility.Add(
                ref hash,
                (uint) SimpleSlabFieldSemantic.NormalWS);
            MaterialProgramHashUtility.Add(ref hash, (uint) DiffuseModel);
            MaterialProgramHashUtility.Add(ref hash, (uint) SpecularModel);
            MaterialProgramHashUtility.Add(ref hash, (uint) FresnelModel);
            MaterialProgramHashUtility.Add(ref hash, (uint) EnergyModel);
            MaterialProgramHashUtility.Add(
                ref hash,
                math.asuint(MinimumAlphaRoughness));
            MaterialProgramHashUtility.Add(
                ref hash,
                math.asuint(DerivedF90FadeThreshold));
            MaterialProgramHashUtility.Add(
                ref hash,
                math.asuint(DerivedF90Scale));
            MaterialProgramHashUtility.Add(
                ref hash,
                DirectResponseIncludesNdotL);
            MaterialProgramHashUtility.Add(
                ref hash,
                AmbientOcclusionAffectsDirectLighting);
            MaterialProgramHashUtility.Add(
                ref hash,
                AmbientOcclusionAffectsIndirectLighting);
            MaterialProgramHashUtility.Add(
                ref hash,
                EmissionIsAddedAfterLighting);
            return new SimpleSlabContractFingerprint(FingerprintVersion, hash);
        }
    }
}
