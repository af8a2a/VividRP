#ifndef VIVIDRP_DEFERRED_LIGHTING_INCLUDED
#define VIVIDRP_DEFERRED_LIGHTING_INCLUDED

#include "AutoExposure.hlsl"
#include "SurfaceSummaryGBuffer.hlsl"
#include "VividSimpleSlabDirectLighting.hlsl"
#include "VividSimpleSlabDeferredLighting.hlsl"

// Shared 2D Deferred input contract for compute and retained raster entrypoints.
// GBuffer contains Surface Summary, never a legacy material ID. Sidecar is read
// only for Dual Slab. All callers bind the native Slab LUT and readiness flag.
Texture2D<float4> _GBuffer0;
Texture2D<float4> _GBuffer1;
Texture2D<float4> _GBuffer2;
Texture2D<float4> _GBuffer3;
Texture2D<float4> _DiffuseIrradiance;
Texture2D<float4> _LayerAux0;
Texture2D<float4> _LayerAux1;
Texture2D<float> _DepthTexture;
Texture2D<float> _DirectionalShadowTexture;
Texture2D<float> _GTAOTexture;
Texture2D<float4> _ScreenSpaceReflectionTexture;

uint _ScreenSpaceReflectionEnabled;
uint _VividSlabLutReady;

VividSurfaceSummaryData LoadVividSurfaceSummary(uint2 pixelCoord)
{
    float4 rt0 = _GBuffer0.Load(int3(pixelCoord, 0));
    float4 rt1 = _GBuffer1.Load(int3(pixelCoord, 0));
    float4 rt2 = _GBuffer2.Load(int3(pixelCoord, 0));
    float4 rt3 = _GBuffer3.Load(int3(pixelCoord, 0));
    float4 rt4 = _DiffuseIrradiance.Load(int3(pixelCoord, 0));
    return VividUnpackSurfaceSummaryGBuffer(rt0, rt1, rt2, rt3, rt4);
}

bool TryLoadVividDualSlabLayerData(
    uint2 pixelCoord,
    out VividDualSlabLayerData layerData)
{
    float4 layerAux0 = _LayerAux0.Load(int3(pixelCoord, 0));
    float4 layerAux1 = _LayerAux1.Load(int3(pixelCoord, 0));
    layerData = VividUnpackDualSlabLayerSidecar(layerAux0, layerAux1);
    return VividIsDualSlabLayerSidecarValid(layerAux0);
}

VividSimpleSlabData BuildVividSimpleSlabData(
    VividSurfaceSummaryData summary)
{
    VividSimpleSlabData slab;
    slab.diffuseAlbedo = summary.diffuseAlbedo;
    slab.specularF0 = summary.specularF0;
    slab.perceptualRoughness = summary.perceptualRoughness;
    slab.normalWS = summary.normalWS;
    return slab;
}

float SampleDirectionalShadow(uint2 pixelCoord)
{
    float2 uv = (float2(pixelCoord) + 0.5) * _ScreenSize.zw;
    return _DirectionalShadowTexture.SampleLevel(sampler_PointClamp, uv, 0).x;
}

float SampleGTAO(uint2 pixelCoord)
{
    return LOAD_TEXTURE2D(_GTAOTexture,pixelCoord).x;
}

float4 LoadScreenSpaceReflection(
    uint2 pixelCoord,
    VividSurfaceSummaryData summary)
{
    if (_ScreenSpaceReflectionEnabled == 0)
        return float4(0.0, 0.0, 0.0, 0.0);

    if (!VividHasDeferredExportFlag(
            summary.deferredExportHeader,
            VIVID_DEFERRED_EXPORT_FLAG_RECEIVE_SSR))
        return float4(0.0, 0.0, 0.0, 0.0);

    float4 reflection = _ScreenSpaceReflectionTexture.Load(int3(pixelCoord, 0));
    return float4(VividApplyPreExposure(max(reflection.rgb, 0.0)), saturate(reflection.a));
}

float EvaluateDeferredDirectionalShadowAttenuation(
    uint2 pixelCoord,
    uint lightIndex,
    DirectionalLightData directionalLight)
{
    if ((int)lightIndex != _MainDirectionalLightIndex)
        return 1.0;

    float directionalShadow = saturate(SampleDirectionalShadow(pixelCoord));
    return lerp(1.0, directionalShadow, saturate(directionalLight.shadowStrength));
}

VividSimpleSlabDeferredLighting EvaluateDeferredFastSlabLighting(
    VividSurfaceSummaryData summary, uint2 pixelCoord, float3 positionWS)
{
    VividSimpleSlabData slab = BuildVividSimpleSlabData(summary);
    float3 viewDirectionWS = SafeNormalize(GetWorldSpaceViewDir(positionWS));
    VividSimpleSlabPreLightData preLight =
        VividPrepareSimpleSlabDeferredLighting(slab, viewDirectionWS);
    VividLightingLoopContext lightLoop = VividLightingLoop::Create(pixelCoord, positionWS);
    VividSimpleSlabIndirectLighting indirectLighting = VividEvaluateSimpleSlabIndirectLighting(
        summary, slab, preLight, lightLoop, positionWS, viewDirectionWS);
    VividSimpleSlabDirectLighting directLighting = VividCreateEmptySimpleSlabDirectLighting();

    [loop]
    for (uint lightIndex = 0; lightIndex < _DirectionalLightCount; ++lightIndex)
    {
        DirectionalLightData light = GetDirectionalLight(lightIndex);
        float shadow = EvaluateDeferredDirectionalShadowAttenuation(pixelCoord, lightIndex, light);
        VividAccumulateSimpleSlabDirectLighting(
            VividEvaluateSimpleSlabDirectionalLight(slab, preLight.energy, viewDirectionWS, light, shadow),
            directLighting);
    }

    uint punctualCount = VividLightingLoop::GetPunctualLightCount(lightLoop);
    [loop]
    for (uint lightIndex = 0; lightIndex < punctualCount; ++lightIndex)
    {
        PunctualLightData light = VividLightingLoop::LoadPunctualLight(lightLoop, lightIndex);
        VividAccumulateSimpleSlabDirectLighting(
            VividEvaluateSimpleSlabPunctualLight(slab, preLight.energy, positionWS, viewDirectionWS, light),
            directLighting);
    }

    uint areaCount = VividLightingLoop::GetAreaLightCount(lightLoop);
    [loop]
    for (uint lightIndex = 0; lightIndex < areaCount; ++lightIndex)
    {
        AreaLightData light = VividLightingLoop::LoadAreaLight(lightLoop, lightIndex);
        VividAccumulateSimpleSlabDirectLighting(
            VividEvaluateSimpleSlabAreaLight(slab, preLight, positionWS, light),
            directLighting);
    }

    return VividComposeSimpleSlabDeferredLighting(directLighting, indirectLighting, preLight.energy);
}

float3 VividDeferredFresnelSchlick(float3 specularF0, float cosTheta)
{
    return VividSimpleSlabEvaluateSchlickFresnel(specularF0, cosTheta);
}

float VividDeferredRecoverMetallicChannel(
    float diffuseAlbedo,
    float specularF0)
{
    // Standard metallic workflow:
    // D = C * (1 - M), F0 = 0.04 * (1 - M) + C * M.
    // Eliminating C gives a quadratic in (1 - M). Prefer the farther
    // physically-valid root; the nearer root is the pure-metal solution.
    // ABI v1 is intentionally dielectric-biased in the irreducibly ambiguous
    // dark-color range where both roots are physically valid (max C <= 0.04).
    float coefficient = max(diffuseAlbedo + specularF0, 0.0);
    float discriminant = max(
        coefficient * coefficient - 0.16 * max(diffuseAlbedo, 0.0),
        0.0);
    float root = sqrt(discriminant);
    float oneMinusMetallicNear = (coefficient - root) * 12.5;
    float oneMinusMetallicFar = (coefficient + root) * 12.5;
    float oneMinusMetallic = oneMinusMetallicFar <= 1.0 + (1.0 / 255.0)
        ? oneMinusMetallicFar
        : oneMinusMetallicNear;
    return 1.0 - saturate(oneMinusMetallic);
}

float VividDeferredTopLayerOpacity(VividDualSlabLayerData topLayer)
{
    float maxDiffuseAlbedo = max(
        topLayer.diffuseAlbedo.x,
        max(topLayer.diffuseAlbedo.y, topLayer.diffuseAlbedo.z));
    // D + F0 = C + 0.04 * (1 - M). The largest channel therefore selects
    // max(C), avoiding the ambiguous secondary root from dark color channels.
    float3 coefficients = topLayer.diffuseAlbedo + topLayer.specularF0;
    float diffuseChannel = topLayer.diffuseAlbedo.x;
    float specularChannel = topLayer.specularF0.x;
    if (coefficients.y > coefficients.x)
    {
        diffuseChannel = topLayer.diffuseAlbedo.y;
        specularChannel = topLayer.specularF0.y;
    }
    if (coefficients.z > max(coefficients.x, coefficients.y))
    {
        diffuseChannel = topLayer.diffuseAlbedo.z;
        specularChannel = topLayer.specularF0.z;
    }
    float metallic = VividDeferredRecoverMetallicChannel(
        diffuseChannel,
        specularChannel);
    return saturate(maxDiffuseAlbedo + metallic);
}

float3 VividDeferredVerticalDirectionalTransmittance(
    VividSurfaceSummaryData topSummary,
    VividDualSlabLayerData topLayer,
    float3 viewDirectionWS,
    float3 lightDirectionWS)
{
    float3 viewFresnel = VividDeferredFresnelSchlick(
        topSummary.specularF0,
        dot(topSummary.normalWS, viewDirectionWS));
    float3 lightFresnel = VividDeferredFresnelSchlick(
        topSummary.specularF0,
        dot(topSummary.normalWS, lightDirectionWS));
    return saturate((1.0 - viewFresnel) * (1.0 - lightFresnel))
        * (1.0 - VividDeferredTopLayerOpacity(topLayer));
}

float3 VividDeferredVerticalEnvironmentTransmittance(
    VividDualSlabLayerData topLayer,
    VividSimpleSlabEnergy topEnergy)
{
    return topEnergy.diffuseTransmission
        * (1.0 - VividDeferredTopLayerOpacity(topLayer));
}

VividSurfaceSummaryData BuildVividDualSlabTopSummary(
    VividSurfaceSummaryData baseSummary,
    VividDualSlabLayerData topLayer)
{
    VividSurfaceSummaryData topSummary = baseSummary;
    topSummary.diffuseAlbedo = topLayer.diffuseAlbedo;
    topSummary.specularF0 = topLayer.specularF0;
    topSummary.perceptualRoughness = topLayer.perceptualRoughness;
    topSummary.emissive = 0.0;
    return topSummary;
}

VividSimpleSlabDeferredLighting EvaluateDeferredDualSlabLighting(
    VividSurfaceSummaryData baseSummary,
    VividDualSlabLayerData topLayer,
    uint2 pixelCoord,
    float3 positionWS)
{
    float layerWeight = saturate(topLayer.layerWeight);
    bool isVerticalLayer = VividHasDeferredExportFlag(
        baseSummary.deferredExportHeader,
        VIVID_DEFERRED_EXPORT_FLAG_VERTICAL_LAYER);
    VividSurfaceSummaryData topSummary = BuildVividDualSlabTopSummary(baseSummary, topLayer);
    VividSimpleSlabData baseSimpleSlab = BuildVividSimpleSlabData(baseSummary);
    VividSimpleSlabData topSimpleSlab = BuildVividSimpleSlabData(topSummary);
    float3 viewDirectionWS = SafeNormalize(GetWorldSpaceViewDir(positionWS));
    VividSimpleSlabPreLightData basePreLightData =
        VividPrepareSimpleSlabDeferredLighting(baseSimpleSlab, viewDirectionWS);
    VividSimpleSlabPreLightData topPreLightData =
        VividPrepareSimpleSlabDeferredLighting(topSimpleSlab, viewDirectionWS);
    VividSimpleSlabEnergy baseEnergy = basePreLightData.energy;
    VividSimpleSlabEnergy topEnergy = topPreLightData.energy;
    VividSimpleSlabDirectLighting baseSimpleDirectLighting = VividCreateEmptySimpleSlabDirectLighting();
    VividSimpleSlabDirectLighting topSimpleDirectLighting = VividCreateEmptySimpleSlabDirectLighting();
    VividLightingLoopContext lightLoop = VividLightingLoop::Create(pixelCoord, positionWS);

    float3 topWeight = layerWeight.xxx;
    float3 environmentTransmittance =
        VividDeferredVerticalEnvironmentTransmittance(topLayer, topEnergy);
    float3 baseEnvironmentWeight = isVerticalLayer
        ? lerp(1.0.xxx, environmentTransmittance, layerWeight)
        : (1.0 - layerWeight).xxx;
    VividSimpleSlabIndirectLighting baseIndirectLighting = VividEvaluateSimpleSlabIndirectLighting(
        baseSummary, baseSimpleSlab, basePreLightData, lightLoop, positionWS, viewDirectionWS);
    VividSimpleSlabIndirectLighting topIndirectLighting = VividEvaluateSimpleSlabIndirectLighting(
        topSummary, topSimpleSlab, topPreLightData, lightLoop, positionWS, viewDirectionWS);

    [loop]
    for (uint lightIndex = 0; lightIndex < _DirectionalLightCount; lightIndex++)
    {
        DirectionalLightData directionalLight = GetDirectionalLight(lightIndex);
        float shadowAttenuation = EvaluateDeferredDirectionalShadowAttenuation(
            pixelCoord,
            lightIndex,
            directionalLight);
        float3 baseWeight = isVerticalLayer
            ? lerp(
                1.0.xxx,
                VividDeferredVerticalDirectionalTransmittance(
                    topSummary,
                    topLayer,
                    viewDirectionWS,
                    SafeNormalize(directionalLight.directionWS)),
                layerWeight)
            : (1.0 - layerWeight).xxx;
        VividAccumulateSimpleSlabDirectLighting(
            VividScaleSimpleSlabDirectLighting(
                VividEvaluateSimpleSlabDirectionalLight(
                    baseSimpleSlab,
                    baseEnergy,
                    viewDirectionWS,
                    directionalLight,
                    shadowAttenuation),
                baseWeight),
            baseSimpleDirectLighting);
        VividAccumulateSimpleSlabDirectLighting(
            VividScaleSimpleSlabDirectLighting(
                VividEvaluateSimpleSlabDirectionalLight(
                    topSimpleSlab,
                    topEnergy,
                    viewDirectionWS,
                    directionalLight,
                    shadowAttenuation),
                topWeight),
            topSimpleDirectLighting);
    }

    uint punctualLightCount = VividLightingLoop::GetPunctualLightCount(lightLoop);
    [loop]
    for (uint localLightIndex = 0; localLightIndex < punctualLightCount; localLightIndex++)
    {
        PunctualLightData punctualLight = VividLightingLoop::LoadPunctualLight(
            lightLoop,
            localLightIndex);
        float3 lightDirectionWS;
        float4 lightDistances;
        GetVividPunctualLightVectors(
            positionWS,
            punctualLight,
            lightDirectionWS,
            lightDistances);
        float3 baseWeight = isVerticalLayer
            ? lerp(
                1.0.xxx,
                VividDeferredVerticalDirectionalTransmittance(
                    topSummary,
                    topLayer,
                    viewDirectionWS,
                    lightDirectionWS),
                layerWeight)
            : (1.0 - layerWeight).xxx;
        VividAccumulateSimpleSlabDirectLighting(
            VividScaleSimpleSlabDirectLighting(
                VividEvaluateSimpleSlabPunctualLight(
                    baseSimpleSlab,
                    baseEnergy,
                    positionWS,
                    viewDirectionWS,
                    punctualLight),
                baseWeight),
            baseSimpleDirectLighting);
        VividAccumulateSimpleSlabDirectLighting(
            VividScaleSimpleSlabDirectLighting(
                VividEvaluateSimpleSlabPunctualLight(
                    topSimpleSlab,
                    topEnergy,
                    positionWS,
                    viewDirectionWS,
                    punctualLight),
                topWeight),
            topSimpleDirectLighting);
    }

    uint areaLightCount = VividLightingLoop::GetAreaLightCount(lightLoop);
    [loop]
    for (uint lightIndex = 0; lightIndex < areaLightCount; ++lightIndex)
    {
        AreaLightData areaLight = VividLightingLoop::LoadAreaLight(lightLoop, lightIndex);
        VividAccumulateSimpleSlabDirectLighting(
            VividScaleSimpleSlabDirectLighting(
                VividEvaluateSimpleSlabAreaLight(baseSimpleSlab, basePreLightData, positionWS, areaLight),
                baseEnvironmentWeight),
            baseSimpleDirectLighting);
        VividAccumulateSimpleSlabDirectLighting(
            VividScaleSimpleSlabDirectLighting(
                VividEvaluateSimpleSlabAreaLight(topSimpleSlab, topPreLightData, positionWS, areaLight),
                topWeight),
            topSimpleDirectLighting);
    }

    VividSimpleSlabDeferredLighting result;
    result.indirectDiffuseLighting = baseIndirectLighting.diffuse * baseEnvironmentWeight
        + topIndirectLighting.diffuse * topWeight;
    result.screenSpaceReplaceableSpecularLighting = baseIndirectLighting.singleScatterSpecular * baseEnvironmentWeight
        + topIndirectLighting.singleScatterSpecular * topWeight;
    result.indirectSpecularLighting = result.screenSpaceReplaceableSpecularLighting
        + baseIndirectLighting.multipleScatterSpecular * baseEnvironmentWeight
        + topIndirectLighting.multipleScatterSpecular * topWeight;
    result.diffuseLighting = baseSimpleDirectLighting.diffuse + topSimpleDirectLighting.diffuse
        + result.indirectDiffuseLighting;
    result.specularLighting = baseSimpleDirectLighting.specular + topSimpleDirectLighting.specular
        + result.indirectSpecularLighting;
    // The single SSR signal is shared by both closures (Sidecar ABI v1 has one
    // normal/SSR trace). Match the exact weights of the replaced environment SS,
    // including vertical transmission, and preserve both MS lobes.
    result.screenSpaceReflectionFGD = baseEnergy.singleScatterSpecularAlbedo * baseEnvironmentWeight
        + topEnergy.singleScatterSpecularAlbedo * topWeight;
    // The pixel compositor adds combined material emission once, outside layers.
    return result;
}

// Non-sky pixels only. Returns complete pre-exposed lighting (including emission).
// Compute tile scheduling and raster geometry remain the caller's responsibility.
float3 VividEvaluateDeferredSurfacePixel(
    uint2 pixelCoord, float deviceDepth, out float4 debugLighting)
{
    debugLighting = 0.0;
    VividSurfaceSummaryData summary = LoadVividSurfaceSummary(pixelCoord);
    float3 emission = VividApplyPreExposure(max(summary.emissive, 0.0));
    uint deferredExportClass = VividGetDeferredExportClass(
        summary.deferredExportHeader);
    // Empty/Unlit need no BSDF resources. Reserved lit classes are not silently
    // treated as Unlit: V1 implements FastSlab and DualSlab only.
    if (deferredExportClass == VIVID_DEFERRED_EXPORT_CLASS_EMPTY
        || deferredExportClass == VIVID_DEFERRED_EXPORT_CLASS_UNLIT)
        return emission;

    if (deferredExportClass != VIVID_DEFERRED_EXPORT_CLASS_FAST_SLAB
        && deferredExportClass != VIVID_DEFERRED_EXPORT_CLASS_DUAL_SLAB)
    {
        debugLighting = float4(1.0, 0.0, 1.0, 1.0);
        return VividApplyPreExposure(float3(1.0, 0.0, 1.0));
    }

    if (_VividSlabLutReady == 0u)
    {
        debugLighting = float4(1.0, 0.0, 1.0, 1.0);
        return VividApplyPreExposure(float3(1.0, 0.0, 1.0));
    }

    summary.ambientOcclusion *= saturate(SampleGTAO(pixelCoord));

    float2 uv = (float2(pixelCoord) + 0.5) * _ScreenSize.zw;
    float3 positionWS = ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
    VividSimpleSlabDeferredLighting lightLoopOutput;
    UNITY_BRANCH
    if (deferredExportClass == VIVID_DEFERRED_EXPORT_CLASS_DUAL_SLAB)
    {
        VividDualSlabLayerData topLayer;
        if (!TryLoadVividDualSlabLayerData(pixelCoord, topLayer))
        {
            debugLighting = float4(1.0, 0.0, 1.0, 1.0);
            return VividApplyPreExposure(float3(1.0, 0.0, 1.0));
        }
        lightLoopOutput = EvaluateDeferredDualSlabLighting(
            summary,
            topLayer,
            pixelCoord,
            positionWS);
    }
    else
    {
        lightLoopOutput = EvaluateDeferredFastSlabLighting(
            summary,
            pixelCoord,
            positionWS);
    }
    float3 diffuseLighting = VividApplyPreExposure(max(lightLoopOutput.diffuseLighting, 0.0));
    float3 specularLighting = VividApplyPreExposure(max(lightLoopOutput.specularLighting, 0.0));
    float4 screenSpaceReflection = LoadScreenSpaceReflection(pixelCoord, summary);
    float reflectionWeight = screenSpaceReflection.a;
    float3 preExposedReplaceableSpecular = VividApplyPreExposure(max(lightLoopOutput.screenSpaceReplaceableSpecularLighting, 0.0));
    specularLighting = max(specularLighting - preExposedReplaceableSpecular * reflectionWeight, 0.0)
        + screenSpaceReflection.rgb * lightLoopOutput.screenSpaceReflectionFGD * reflectionWeight;
    float3 lighting = diffuseLighting + specularLighting;
    float3 indirectLighting = VividApplyPreExposure(max(
        lightLoopOutput.indirectDiffuseLighting + lightLoopOutput.indirectSpecularLighting, 0.0));
    debugLighting = float4(indirectLighting, reflectionWeight);
    return emission + lighting;
}

#endif
