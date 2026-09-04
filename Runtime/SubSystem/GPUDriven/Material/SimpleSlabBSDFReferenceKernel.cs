using Unity.Mathematics;

namespace VividRP.Runtime.GPUDriven
{
    internal readonly struct SimpleSlabBSDFResponse
    {
        internal SimpleSlabBSDFResponse(float3 diffuse, float3 specular)
        {
            Diffuse = diffuse;
            Specular = specular;
        }

        internal float3 Diffuse { get; }

        internal float3 Specular { get; }

        internal float3 Combined => Diffuse + Specular;
    }

    // CPU reference for the standalone HLSL kernel. Production shading remains
    // in HLSL; this mirror owns deterministic contract and GPU-test baselines.
    internal static class SimpleSlabBSDFReferenceKernel
    {
        internal const uint Version =
            MaterialProgramContract.SimpleSlabBSDFKernelVersion;
        internal const float Pi = 3.14159265358979323846f;
        internal const float MinimumHalfVectorLengthSquared = 1e-8f;
        internal const float MinimumVisibilityDenominator = 1e-6f;

        internal static float DistributionGGX(
            float alphaRoughness,
            float nDotH)
        {
            float alpha = math.max(
                alphaRoughness,
                SimpleSlabContract.MinimumAlphaRoughness);
            float alphaSquared = alpha * alpha;
            float saturatedNdotH = math.saturate(nDotH);
            float denominator = (1.0f - saturatedNdotH) * (1.0f + saturatedNdotH)
                + saturatedNdotH * saturatedNdotH * alphaSquared;
            return alphaSquared / (Pi * denominator * denominator);
        }

        internal static float VisibilitySmithGGXCorrelated(
            float alphaRoughness,
            float nDotV,
            float nDotL)
        {
            float alpha = math.max(
                alphaRoughness,
                SimpleSlabContract.MinimumAlphaRoughness);
            float alphaSquared = alpha * alpha;
            float saturatedNdotV = math.saturate(nDotV);
            float saturatedNdotL = math.saturate(nDotL);
            float ggxV = saturatedNdotL * math.sqrt(
                saturatedNdotV * saturatedNdotV
                    * (1.0f - alphaSquared)
                + alphaSquared);
            float ggxL = saturatedNdotV * math.sqrt(
                saturatedNdotL * saturatedNdotL
                    * (1.0f - alphaSquared)
                + alphaSquared);
            return 0.5f / math.max(
                ggxV + ggxL,
                MinimumVisibilityDenominator);
        }

        internal static SimpleSlabBSDFResponse EvaluateAnalyticDirect(
            float3 diffuseAlbedo,
            float3 specularF0,
            float perceptualRoughness,
            float3 normalWS,
            float3 viewDirectionWS,
            float3 lightDirectionWS)
        {
            float nDotV = math.dot(normalWS, viewDirectionWS);
            float nDotL = math.dot(normalWS, lightDirectionWS);
            if (nDotV <= 0.0f || nDotL <= 0.0f)
                return default;

            float3 halfVector = viewDirectionWS + lightDirectionWS;
            float halfVectorLengthSquared = math.lengthsq(halfVector);
            if (halfVectorLengthSquared <= MinimumHalfVectorLengthSquared)
                return default;

            halfVector *= math.rsqrt(halfVectorLengthSquared);
            float nDotH = math.saturate(math.dot(normalWS, halfVector));
            float vDotH = math.saturate(
                math.dot(viewDirectionWS, halfVector));
            float alphaRoughness =
                SimpleSlabContract.PerceptualRoughnessToAlpha(
                    perceptualRoughness);
            float distribution = DistributionGGX(alphaRoughness, nDotH);
            float visibility = VisibilitySmithGGXCorrelated(
                alphaRoughness,
                nDotV,
                nDotL);
            float3 fresnel = SimpleSlabContract.EvaluateSchlickFresnel(
                specularF0,
                vDotH);
            float3 diffuse = math.saturate(diffuseAlbedo)
                * ((1.0f / Pi) * nDotL);
            float3 specular = fresnel
                * (distribution * visibility * nDotL);
            return new SimpleSlabBSDFResponse(diffuse, specular);
        }
    }
}
