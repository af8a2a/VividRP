#ifndef VIVIDRP_SIMPLE_SLAB_DEFERRED_LIGHTING_INCLUDED
#define VIVIDRP_SIMPLE_SLAB_DEFERRED_LIGHTING_INCLUDED

#include "Core.hlsl"
#include "SurfaceSummaryGBuffer.hlsl"
#include "VividSimpleSlabDirectLighting.hlsl"
#include "LightingLoop.hlsl"
#include "VividSkyLighting.hlsl"
#include "VividProbeVolume.hlsl"
#include "VividAreaLightCommon.hlsl"
#include "LTCAreaLight.hlsl"

#define VIVID_SIMPLE_SLAB_DEFERRED_LIGHTING_VERSION 3u

struct VividSimpleSlabPreLightData
{
    VividSimpleSlabEnergy energy;
    float3 reflectionDirectionWS;
    float3x3 viewNormalBasis;
    float3x3 ltcSpecular;
};

struct VividSimpleSlabIndirectLighting
{
    // Colored, AO-weighted radiance, without emission or pre-exposure.
    float3 diffuse;
    float3 singleScatterSpecular;
    float3 multipleScatterSpecular;
};

struct VividSimpleSlabDeferredLighting
{
    float3 diffuseLighting;
    float3 specularLighting;
    float3 indirectDiffuseLighting;
    float3 indirectSpecularLighting;
    // Only this part of the environment response may be replaced by SSR.
    float3 screenSpaceReplaceableSpecularLighting;
    float3 screenSpaceReflectionFGD;
};

VividSimpleSlabPreLightData VividPrepareSimpleSlabDeferredLighting(
    VividSimpleSlabData slab, float3 viewDirectionWS)
{
    VividSimpleSlabPreLightData data;
    float nDotV = dot(slab.normalWS, viewDirectionWS);
    float clampedNdotV = saturate(ClampNdotV(nDotV));
    data.energy = VividLoadSimpleSlabEnergy(slab, viewDirectionWS);
    // Single scattering retains the existing GGX prefilter convention.
    data.reflectionDirectionWS = GetSpecularDominantDir(slab.normalWS,
        reflect(-viewDirectionWS, slab.normalWS), slab.perceptualRoughness, clampedNdotV);
    data.viewNormalBasis = GetOrthoBasisViewNormal(viewDirectionWS, slab.normalWS, nDotV);
    data.ltcSpecular = SampleLtcMatrix(slab.perceptualRoughness,
        clampedNdotV, VIVID_LTC_LIGHTING_MODEL_GGX);
    return data;
}

float3 VividSampleSimpleSlabEnvironment(VividLightingLoopContext lightLoop,
    float3 positionWS, float3 normalWS, float3 directionWS, float perceptualRoughness)
{
    float3 radiance = VividSampleSkyIBL(directionWS, perceptualRoughness);
    float3 weightedProbeRadiance;
    float probeWeight;
    if (VividLightingLoop::TryEvaluateReflectionProbes(lightLoop, positionWS,
            normalWS, directionWS, perceptualRoughness, weightedProbeRadiance, probeWeight))
    {
        // Each lobe has its own direction-dependent hierarchy coverage. Probe
        // radiance is already weighted; only the remaining weight belongs to sky.
        radiance = radiance * (1.0f - saturate(probeWeight)) + weightedProbeRadiance;
    }
    return radiance;
}

VividSimpleSlabIndirectLighting VividEvaluateSimpleSlabIndirectLighting(
    VividSurfaceSummaryData summary, VividSimpleSlabData slab,
    VividSimpleSlabPreLightData preLight, VividLightingLoopContext lightLoop,
    float3 positionWS, float3 viewDirectionWS)
{
    float3 diffuseIrradiance;
    if (VividHasDeferredExportFlag(summary.deferredExportHeader,
            VIVID_DEFERRED_EXPORT_FLAG_HAS_DIFFUSE_IRRADIANCE))
        diffuseIrradiance = summary.diffuseIrradiance;
    else if (VividHasProbeVolumeGI())
        diffuseIrradiance = SampleVividProbeVolume(positionWS,
            slab.normalWS, viewDirectionWS, 0xFFFFFFFFu);
    else
        diffuseIrradiance = VividSampleAmbientProbe(slab.normalWS);

    float3 singleScatterRadiance = VividSampleSimpleSlabEnvironment(
        lightLoop, positionWS, slab.normalWS, preLight.reflectionDirectionWS,
        slab.perceptualRoughness);
    float3 multipleScatterRadiance = 0.0f;
    UNITY_BRANCH
    if (any(preLight.energy.multipleScatterSpecularAlbedo > 0.0f))
    {
        // V1 real-time broad-lobe approximation: sample the roughest existing
        // prefilter around N, independently of the GGX reflection direction.
        // This is NOT an exact convolution of the directional loss L(l).
        multipleScatterRadiance = VividSampleSimpleSlabEnvironment(
            lightLoop, positionWS, slab.normalWS, slab.normalWS, 1.0f);
    }

    float ao = saturate(summary.ambientOcclusion);
    VividSimpleSlabIndirectLighting lighting;
    lighting.diffuse = diffuseIrradiance * saturate(slab.diffuseAlbedo)
        * preLight.energy.diffuseTransmission * ao;
    lighting.singleScatterSpecular = singleScatterRadiance * preLight.energy.singleScatterSpecularAlbedo * ao;
    lighting.multipleScatterSpecular = multipleScatterRadiance * preLight.energy.multipleScatterSpecularAlbedo * ao;
    return lighting;
}

VividSimpleSlabDirectLighting VividEvaluateSimpleSlabAreaLight(
    VividSimpleSlabData slab, VividSimpleSlabPreLightData preLight,
    float3 positionWS, AreaLightData areaLight)
{
    VividSimpleSlabDirectLighting lighting = VividCreateEmptySimpleSlabDirectLighting();
    ApplyRectangularAreaLightBarnDoor(areaLight, positionWS);
    float intensity = EvaluateAreaLightIntensity(areaLight, positionWS);
    if (intensity <= 0.0f)
        return lighting;

    float3 center = mul(preLight.viewNormalBasis, areaLight.positionWS - positionWS);
    float3 right = mul(preLight.viewNormalBasis, areaLight.rightWS);
    float3 up = mul(preLight.viewNormalBasis, areaLight.upWS);
    bool isRectangle = areaLight.lightType == VIVID_AREA_LIGHT_TYPE_RECTANGLE;
    float4 diffuseLtc = EvaluateLTC_Area(isRectangle, center, right, up,
        areaLight.width * 0.5f, areaLight.height * 0.5f,
        float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1));
    float4 specularLtc = EvaluateLTC_Area(isRectangle, center, right, up,
        areaLight.width * 0.5f, areaLight.height * 0.5f, transpose(preLight.ltcSpecular));
    float3 color = max(areaLight.color, 0.0f) * intensity;
    lighting.diffuse = diffuseLtc.rgb * diffuseLtc.a * color
        * saturate(slab.diffuseAlbedo) * preLight.energy.diffuseTransmission;
    // Reuse the GGX fit ONLY for single scattering. The broad MS lobe uses the
    // normalized Lambert shape, preserving its white-furnace integral. This is
    // a shape approximation, not a fitted directional-loss LTC distribution.
    lighting.specular = color * (
        specularLtc.rgb * specularLtc.a * preLight.energy.singleScatterSpecularAlbedo
        + diffuseLtc.rgb * diffuseLtc.a * preLight.energy.multipleScatterSpecularAlbedo);
    return lighting;
}

VividSimpleSlabDeferredLighting VividComposeSimpleSlabDeferredLighting(
    VividSimpleSlabDirectLighting directLighting,
    VividSimpleSlabIndirectLighting indirectLighting,
    VividSimpleSlabEnergy energy)
{
    VividSimpleSlabDeferredLighting result;
    result.diffuseLighting = directLighting.diffuse + indirectLighting.diffuse;
    result.indirectSpecularLighting = indirectLighting.singleScatterSpecular + indirectLighting.multipleScatterSpecular;
    result.specularLighting = directLighting.specular + result.indirectSpecularLighting;
    result.indirectDiffuseLighting = indirectLighting.diffuse;
    result.screenSpaceReplaceableSpecularLighting = indirectLighting.singleScatterSpecular;
    result.screenSpaceReflectionFGD = energy.singleScatterSpecularAlbedo;
    // ClearDeferredLit owns emission. Neither albedo nor energy is applied again here.
    return result;
}

#endif
