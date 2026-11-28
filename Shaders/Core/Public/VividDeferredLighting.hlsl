#ifndef VIVIDRP_DEFERRED_LIGHTING_INCLUDED
#define VIVIDRP_DEFERRED_LIGHTING_INCLUDED

#include "AutoExposure.hlsl"
#include "SurfaceSummaryGBuffer.hlsl"
#include "VividSubstrateBuffer.hlsl"
#include "VividHairFurBSDF.hlsl"
#include "VividSimpleSlabDirectLighting.hlsl"
#include "VividSimpleSlabDeferredLighting.hlsl"
#include "VividOpenPBROpaqueDeferredLighting.hlsl"

// Shared 2D Deferred input contract for compute and retained raster entrypoints.
// GBuffer contains TopLayer Surface Summary, never a legacy material ID.
// Substrate Closure Buffer stores authoritative OpenPBR/multi-closure payloads;
// LayerAux sidecar remains as legacy DualSlab/fallback storage.
Texture2D<float4> _GBuffer0;
Texture2D<float4> _GBuffer1;
Texture2D<float4> _GBuffer2;
Texture2D<float4> _GBuffer3;
Texture2D<float4> _DiffuseIrradiance;
Texture2D<float4> _LayerAux0;
Texture2D<float4> _LayerAux1;
Texture2DArray<uint> _SubstrateClosureBuffer;
Texture2D<float> _DepthTexture;
Texture2D<float> _DirectionalShadowTexture;
Texture2D<float> _GTAOTexture;
Texture2D<float4> _ScreenSpaceReflectionTexture;

uint _ScreenSpaceReflectionEnabled;
uint _VividSlabLutReady;
uint _VividOpenPBRLutsReady;
uint _SubstrateClosureBufferEnabled;

#if defined(VIVID_REALTIME_DIFFUSE_GI)
#include "VividBentNormalPacking.hlsl"
Texture2D<float4> _RealtimeDiffuseGI;
Texture2D<float4> _RealtimeBentNormal;
Texture2D<uint> _RealtimePackedBentNormal;
Texture2D<float4> _RealtimeRoughSpecular;
Texture2D<float3> _RealtimeLumenReflections;
uint _RealtimeBentNormalEnabled;
uint _RealtimeRoughSpecularEnabled;
uint _RealtimeLumenReflectionsEnabled;
uint _RealtimeDiffuseGIEnabled;
uint _RealtimeDiffuseGIOnly;
uint _RealtimeOpenPbrDiffuse;
#endif

bool VividHasRealtimeDiffuseGI()
{
#if defined(VIVID_REALTIME_DIFFUSE_GI)
    return _RealtimeDiffuseGIEnabled != 0u;
#else
    return false;
#endif
}

bool VividHasRealtimeRoughSpecular()
{
#if defined(VIVID_REALTIME_DIFFUSE_GI)
    return _RealtimeRoughSpecularEnabled != 0u;
#else
    return false;
#endif
}

bool VividHasRealtimeLumenReflections()
{
#if defined(VIVID_REALTIME_DIFFUSE_GI)
    return _RealtimeLumenReflectionsEnabled != 0u;
#else
    return false;
#endif
}

float4 LoadVividRealtimeRoughSpecular(uint2 pixelCoord)
{
#if defined(VIVID_REALTIME_DIFFUSE_GI)
    if (_RealtimeRoughSpecularEnabled != 0u)
    {
        float4 spec = _RealtimeRoughSpecular.Load(int3(pixelCoord, 0));
        if (_RealtimeRoughSpecularEnabled == 2u)
            spec.rgb *= VividGetOneOverPreExposure();
        return float4(max(spec.rgb, 0.0f), saturate(spec.a));
    }
#endif
    return 0.0f;
}

float3 LoadVividRealtimeLumenReflections(uint2 pixelCoord)
{
#if defined(VIVID_REALTIME_DIFFUSE_GI)
    if (_RealtimeLumenReflectionsEnabled != 0u)
    {
        float3 refl = _RealtimeLumenReflections.Load(int3(pixelCoord, 0));
        refl *= VividGetOneOverPreExposure();
        return max(isfinite(refl) ? refl : 0.0f, 0.0f);
    }
#endif
    return 0.0f;
}

float VividBentSpecularVisibility(uint2 pixelCoord, float3 normal, float3 view, float roughness)
{
#if defined(VIVID_REALTIME_DIFFUSE_GI)
    if (_RealtimeBentNormalEnabled == 2u)
    {
        // LumenScreenSpaceBentNormal.ush::CalculateSpecularOcclusion.
        float3 BentNormal = UnpackScreenBentNormal(_RealtimePackedBentNormal.Load(int3(pixelCoord, 0)));
        float AO = saturate(length(BentNormal));
        float ReflectionConeAngle = max(roughness, .1f) * PI;
        float UnoccludedAngle = AO * PI;
        float3 ReflectionVector = reflect(-view, normal);
        float AngleBetween = VividBentNormalAcosFast(clamp(dot(BentNormal, ReflectionVector) / max(AO, .001f), -1, 1));
        float AngleDifference = abs(ReflectionConeAngle - UnoccludedAngle);
        float SpecularOcclusion = smoothstep(0, 1.0, 1.0 - saturate(
            (AngleBetween - AngleDifference) / (ReflectionConeAngle + UnoccludedAngle - AngleDifference)));
        return lerp(0, SpecularOcclusion, saturate((UnoccludedAngle - .1f) / .2f));
    }
    if (_RealtimeBentNormalEnabled == 1u)
    {
        float4 history = _RealtimeBentNormal.Load(int3(pixelCoord, 0));
        float visibility = saturate(length(history.xyz));
        if (history.w > 0.0 && visibility < 0.999)
        {
            float openAngle = visibility * PI;
            float lobeAngle = max(roughness, 0.1) * PI;
            float separation = acos(clamp(dot(history.xyz, reflect(-view, normal)) / max(visibility, 0.001), -1.0, 1.0));
            float overlap = (openAngle + lobeAngle - separation) / max(2.0 * min(openAngle, lobeAngle), 1e-5);
            return smoothstep(0.0, 1.0, overlap) * saturate((openAngle - 0.1) / 0.2);
        }
    }
#endif
    return 1.0;
}

float4 LoadVividRealtimeDiffuseGI(uint2 pixelCoord, float3 diffuseAlbedo)
{
    #if defined(VIVID_REALTIME_DIFFUSE_GI)
        if (VividHasRealtimeDiffuseGI())
        {
            float4 gi = _RealtimeDiffuseGI.Load(int3(pixelCoord, 0));
            if (_RealtimeBentNormalEnabled == 2u)
            {
                float3 BentNormal = UnpackScreenBentNormal(_RealtimePackedBentNormal.Load(int3(pixelCoord, 0)));
                half AO = saturate(length(BentNormal));
                // DeferredShadingCommon.ush::AOMultiBounce with the Lumen albedo cap.
                half3 BaseColor = min(diffuseAlbedo, 0.5f);
                half3 a = 2.0404 * BaseColor - 0.3324;
                half3 b = -4.7951 * BaseColor + 0.6417;
                half3 c = 2.7552 * BaseColor + 0.6903;
                gi.rgb *= max(AO, ((AO * a + b) * AO + c) * AO);
            }
            // Compose the covered energy over black when fallback is suppressed.
            return _RealtimeDiffuseGIOnly != 0u ? float4(gi.rgb * gi.a, 1.0) : gi;
        }
    #endif
    return 0.0;
}

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
    if (VividHasRealtimeDiffuseGI())
        return 1.0;

    return LOAD_TEXTURE2D(_GTAOTexture,pixelCoord).x;
}

float4 LoadScreenSpaceReflection(
    uint2 pixelCoord,
    VividSurfaceSummaryData summary)
{
    if (_ScreenSpaceReflectionEnabled == 0u || VividHasRealtimeDiffuseGI())
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

bool TryEvaluateDeferredOpenPBROpaqueLighting(VividSurfaceSummaryData summary,
    uint2 pixelCoord, float3 positionWS, out VividSimpleSlabDeferredLighting result,
    out float3 diffuseBounce)
{
    result = (VividSimpleSlabDeferredLighting)0;
    diffuseBounce = 0.0;
    if (_VividOpenPBRLutsReady == 0u)
        return false;

    VividOpenPBROpaqueInputs inputs;
    bool loadedInputs = false;
    if (_SubstrateClosureBufferEnabled != 0u)
    {
        loadedInputs = VividTryLoadSubstrateOpenPBROpaqueSingle(
            _SubstrateClosureBuffer,
            pixelCoord,
            inputs);
    }
    if (!loadedInputs)
    {
        uint width, height, auxWidth, auxHeight, aux1Width, aux1Height;
        _GBuffer0.GetDimensions(width, height);
        _LayerAux0.GetDimensions(auxWidth, auxHeight);
        _LayerAux1.GetDimensions(aux1Width, aux1Height);
        if (width != auxWidth || height != auxHeight
            || width != aux1Width || height != aux1Height)
            return false;
        loadedInputs = VividTryUnpackOpenPBROpaqueGBuffer(
            summary,
            _DiffuseIrradiance.Load(int3(pixelCoord, 0)),
            _LayerAux0.Load(int3(pixelCoord, 0)),
            _LayerAux1.Load(int3(pixelCoord, 0)),
            inputs);
    }
    if (!loadedInputs)
        return false;
    OpenPBR_ResolvedInputs resolved;
    if (!VividTryResolveOpenPBROpaqueInputs(inputs, 0u, resolved))
        return false;
    float3 viewDirectionWS = SafeNormalize(GetWorldSpaceCameraViewDir(positionWS));
    if (dot(inputs.normalWS, viewDirectionWS) <= 0.0)
        return true;
    OpenPBR_PreparedBsdf prepared = VividPrepareOpenPBROpaque(resolved, viewDirectionWS);
    OpenPBR_DiffuseSpecular albedo = VividEvaluateOpenPBROpaqueAlbedo(
        inputs, resolved.geometry_basis.n, viewDirectionWS);
    VividLightingLoopContext lightLoop = VividLightingLoop::Create(pixelCoord, positionWS);
    bool realtimeOpenPbrDiffuse = false;
    #if defined(VIVID_REALTIME_DIFFUSE_GI)
        realtimeOpenPbrDiffuse = _RealtimeOpenPbrDiffuse != 0u;
    #endif
    result = VividEvaluateOpenPBROpaqueIndirect(inputs, albedo, lightLoop,
        positionWS, viewDirectionWS, saturate(summary.ambientOcclusion),
        LoadVividRealtimeDiffuseGI(pixelCoord, summary.diffuseAlbedo), realtimeOpenPbrDiffuse,
        LoadVividRealtimeRoughSpecular(pixelCoord), VividHasRealtimeRoughSpecular(),
        LoadVividRealtimeLumenReflections(pixelCoord), VividHasRealtimeLumenReflections());
    [loop]
    for (uint i = 0u; i < _DirectionalLightCount; ++i)
    {
        DirectionalLightData light = GetDirectionalLight(i);
        float shadow = EvaluateDeferredDirectionalShadowAttenuation(pixelCoord, i, light);
        diffuseBounce += VividEvaluateOpenPBROpaqueDiffuseBounce(inputs,
            SafeNormalize(light.directionWS), max(light.color, 0.0) * shadow);
        VividAccumulateOpenPBROpaqueLight(VividEvaluateOpenPBROpaqueLight(prepared,
            inputs.normalWS, SafeNormalize(light.directionWS), max(light.color, 0.0) * shadow), result);
    }
    uint punctualCount = VividLightingLoop::GetPunctualLightCount(lightLoop);
    [loop]
    for (uint i = 0u; i < punctualCount; ++i)
    {
        PunctualLightData light = VividLightingLoop::LoadPunctualLight(lightLoop, i);
        float3 direction;
        float4 distances;
        GetVividPunctualLightVectors(positionWS, light, direction, distances);
        if (distances.y <= VIVID_SIMPLE_SLAB_MIN_PUNCTUAL_DISTANCE_SQ)
            continue;
        float attenuation = VividPunctualLightAttenuationWithDistanceModification(
            light, positionWS - light.positionWS, distances);
        if (attenuation > 0.0)
        {
            diffuseBounce += VividEvaluateOpenPBROpaqueDiffuseBounce(inputs,
                direction, max(light.color, 0.0) * attenuation);
            VividAccumulateOpenPBROpaqueLight(VividEvaluateOpenPBROpaqueLight(prepared,
                inputs.normalWS, direction, max(light.color, 0.0) * attenuation), result);
        }
    }
    uint areaCount = VividLightingLoop::GetAreaLightCount(lightLoop);
    [loop]
    for (uint i = 0u; i < areaCount; ++i)
    {
        float3 areaBounce;
        VividAccumulateOpenPBROpaqueLight(VividEvaluateOpenPBROpaqueAreaLight(inputs,
            albedo, positionWS, viewDirectionWS, VividLightingLoop::LoadAreaLight(lightLoop, i), areaBounce), result);
        diffuseBounce += areaBounce;
    }
    return true;
}

// Thin-walled backlit transmission & terminator wrap approximation for fur/fuzz slabs
// using the frozen Substrate header flag VIVID_SUBSTRATE_FLAG_THIN_WALLED (no ABI expansion).
float3 EvaluateVividThinWalledTransmission(
    VividOpenPBRV2Inputs v2Inputs,
    float3 baseNormal,
    float3 viewDirectionWS,
    float3 lightDir,
    float3 lightColor,
    float shadow,
    float ambientOcclusion)
{
    float ao = saturate(ambientOcclusion);
    float tipTransmittance = saturate(v2Inputs.fuzzWeight) * ao * ao;
    if (tipTransmittance <= 1e-4f)
        return 0.0f.xxx;

    float3 transColor = saturate(lerp(v2Inputs.core.baseColor, v2Inputs.fuzzColor, 0.45f));
    float3 backHalf = SafeNormalize(lightDir + baseNormal * 0.35f);
    float vDotBackL = saturate(dot(viewDirectionWS, -backHalf));
    float ttLobe = pow(vDotBackL, 3.0f) * 0.85f + pow(vDotBackL, 12.0f) * 0.75f;

    float NdotL = dot(baseNormal, lightDir);
    float wrapNdotL = saturate((NdotL + 0.55f) / 1.55f);
    float backWrap = max(wrapNdotL - max(NdotL, 0.0f), 0.0f);

    // Unshadowed outer fur tips get full backlit transmission; self-shadowed coat tips retain
    // subtle warm multiple-scattering penetration modulated by ao^2 so dense roots stay dark.
    float shadowTransmission = lerp(0.16f, 1.0f, saturate(shadow));
    float3 transRadiance = max(lightColor, 0.0f) * shadowTransmission * INV_PI;
    return transRadiance * transColor * tipTransmittance * (backWrap * 0.65f + ttLobe * 0.55f);
}

bool TryEvaluateDeferredOpenPBRV2Lighting(VividSurfaceSummaryData summary,
    uint2 pixelCoord, float3 positionWS, out VividSimpleSlabDeferredLighting result,
    out float3 diffuseBounce)
{
    result = (VividSimpleSlabDeferredLighting)0;
    diffuseBounce = 0.0f;
    if (_VividOpenPBRLutsReady == 0u || _SubstrateClosureBufferEnabled == 0u)
        return false;

    VividOpenPBRV2Inputs v2Inputs;
    VividSubstratePixelHeader header;
    if (!VividTryLoadSubstrateOpenPBRV2Single(_SubstrateClosureBuffer, pixelCoord, v2Inputs, header))
        return false;

    OpenPBR_ResolvedInputs resolved;
    if (!VividTryResolveOpenPBRV2Inputs(v2Inputs, resolved))
        return false;

    bool thinWalledTransmissive = (header.flags & VIVID_SUBSTRATE_FLAG_THIN_WALLED) != 0u;
    float surfaceAO = saturate(summary.ambientOcclusion);

    float3 viewDirectionWS = SafeNormalize(GetWorldSpaceCameraViewDir(positionWS));
    float3 baseNormal = resolved.geometry_basis.n;
    float3 coatNormal = resolved.geometry_coat_basis.n;
    if (dot(baseNormal, viewDirectionWS) <= 0.0f
        && (resolved.coat_weight <= 0.0f || dot(coatNormal, viewDirectionWS) <= 0.0f))
        return true;

    OpenPBR_PreparedBsdf prepared = VividPrepareOpenPBRV2(resolved, viewDirectionWS);
    VividOpenPBRV2AlbedoComponents comp = VividEvaluateOpenPBRV2AlbedoComponents(
        v2Inputs, resolved, prepared, viewDirectionWS);
    VividLightingLoopContext lightLoop = VividLightingLoop::Create(pixelCoord, positionWS);
    bool realtimeOpenPbrDiffuse = false;
    #if defined(VIVID_REALTIME_DIFFUSE_GI)
        realtimeOpenPbrDiffuse = _RealtimeOpenPbrDiffuse != 0u;
    #endif
    result = VividEvaluateOpenPBRV2Indirect(
        v2Inputs, resolved, comp, lightLoop,
        positionWS, viewDirectionWS, surfaceAO,
        LoadVividRealtimeDiffuseGI(pixelCoord, summary.diffuseAlbedo), realtimeOpenPbrDiffuse,
        LoadVividRealtimeRoughSpecular(pixelCoord), VividHasRealtimeRoughSpecular(),
        LoadVividRealtimeLumenReflections(pixelCoord), VividHasRealtimeLumenReflections());

    [loop]
    for (uint i = 0u; i < _DirectionalLightCount; ++i)
    {
        DirectionalLightData light = GetDirectionalLight(i);
        float shadow = EvaluateDeferredDirectionalShadowAttenuation(pixelCoord, i, light);
        float3 lightDir = SafeNormalize(light.directionWS);
        float3 radiance = max(light.color, 0.0f) * shadow;
        if (dot(baseNormal, lightDir) > 0.0f || (resolved.coat_weight > 0.0f && dot(coatNormal, lightDir) > 0.0f))
        {
            diffuseBounce += VividEvaluateOpenPBROpaqueDiffuseBounce(v2Inputs.core, lightDir, radiance);
            VividAccumulateOpenPBROpaqueLight(
                openpbr_scale_diffuse_specular(openpbr_eval(prepared, lightDir), radiance), result);
        }
        if (thinWalledTransmissive)
        {
            result.diffuseLighting += EvaluateVividThinWalledTransmission(
                v2Inputs, baseNormal, viewDirectionWS, lightDir, light.color, shadow, surfaceAO);
        }
    }
    uint punctualCount = VividLightingLoop::GetPunctualLightCount(lightLoop);
    [loop]
    for (uint i = 0u; i < punctualCount; ++i)
    {
        PunctualLightData light = VividLightingLoop::LoadPunctualLight(lightLoop, i);
        float3 direction;
        float4 distances;
        GetVividPunctualLightVectors(positionWS, light, direction, distances);
        if (distances.y <= VIVID_SIMPLE_SLAB_MIN_PUNCTUAL_DISTANCE_SQ)
            continue;
        float attenuation = VividPunctualLightAttenuationWithDistanceModification(
            light, positionWS - light.positionWS, distances);
        if (attenuation > 0.0f)
        {
            if (dot(baseNormal, direction) > 0.0f || (resolved.coat_weight > 0.0f && dot(coatNormal, direction) > 0.0f))
            {
                float3 radiance = max(light.color, 0.0f) * attenuation;
                diffuseBounce += VividEvaluateOpenPBROpaqueDiffuseBounce(v2Inputs.core, direction, radiance);
                VividAccumulateOpenPBROpaqueLight(
                    openpbr_scale_diffuse_specular(openpbr_eval(prepared, direction), radiance), result);
            }
            if (thinWalledTransmissive)
            {
                result.diffuseLighting += EvaluateVividThinWalledTransmission(
                    v2Inputs, baseNormal, viewDirectionWS, direction, light.color * attenuation, 1.0f, surfaceAO);
            }
        }
    }
    uint areaCount = VividLightingLoop::GetAreaLightCount(lightLoop);
    [loop]
    for (uint i = 0u; i < areaCount; ++i)
    {
        float3 areaBounce;
        VividAccumulateOpenPBROpaqueLight(VividEvaluateOpenPBRV2AreaLight(
            v2Inputs, resolved, comp, positionWS, viewDirectionWS,
            VividLightingLoop::LoadAreaLight(lightLoop, i), areaBounce), result);
        diffuseBounce += areaBounce;
    }
    return true;
}

// Phase 2.2 Tier 3 physical Multi-Closure evaluator (2..4 OpenPBR closures with independent
// normals, HorizontalMix, and physical VerticalLayer Fresnel + Beer-Lambert attenuation).
bool TryEvaluateDeferredSubstrateMultiClosureLighting(VividSurfaceSummaryData summary,
    uint2 pixelCoord, float3 positionWS, out VividSimpleSlabDeferredLighting result,
    out float3 diffuseBounce)
{
    result = (VividSimpleSlabDeferredLighting)0;
    diffuseBounce = 0.0f;
    if (_VividOpenPBRLutsReady == 0u || _SubstrateClosureBufferEnabled == 0u)
        return false;

    VividSubstratePixelHeader header;
    VividOpenPBRV2Inputs closures[VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL];
    VividSubstrateOperatorDesc operators[VIVID_SUBSTRATE_MAX_OPERATORS_PER_PIXEL];
    if (!VividTryLoadSubstrateMultiClosure(_SubstrateClosureBuffer, pixelCoord, header, closures, operators))
        return false;

    float3 viewDirectionWS = SafeNormalize(GetWorldSpaceCameraViewDir(positionWS));
    OpenPBR_ResolvedInputs resolvedList[VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL];
    OpenPBR_PreparedBsdf preparedList[VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL];
    VividOpenPBRV2AlbedoComponents compList[VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL];
    OpenPBR_DiffuseSpecular albedoList[VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL];
    bool closureFrontFacing[VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL];

    [unroll]
    for (uint c = 0u; c < VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL; ++c)
    {
        closureFrontFacing[c] = false;
        albedoList[c] = openpbr_make_zero_diffuse_specular();
        compList[c] = (VividOpenPBRV2AlbedoComponents)0;
        resolvedList[c] = openpbr_make_default_resolved_inputs();
        if (c < header.closureCount)
        {
            if (!VividTryResolveOpenPBRV2Inputs(closures[c], resolvedList[c]))
                return false;
            float3 n = resolvedList[c].geometry_basis.n;
            float3 cn = resolvedList[c].geometry_coat_basis.n;
            if (dot(n, viewDirectionWS) > 0.0f
                || (resolvedList[c].coat_weight > 0.0f && dot(cn, viewDirectionWS) > 0.0f))
            {
                closureFrontFacing[c] = true;
                preparedList[c] = VividPrepareOpenPBRV2(resolvedList[c], viewDirectionWS);
                compList[c] = VividEvaluateOpenPBRV2AlbedoComponents(
                    closures[c], resolvedList[c], preparedList[c], viewDirectionWS);
                albedoList[c] = openpbr_make_diffuse_specular(
                    compList[c].diffuse,
                    compList[c].baseSpecular + compList[c].coatSpecular + compList[c].fuzzSpecular);
            }
        }
    }

    float3 viewWeights[VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL];
    float3 indirectDiffWeights[VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL];
    float3 indirectSpecWeights[VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL];
    VividComputeMultiClosureIndirectWeights(
        header.closureCount, closures, albedoList, operators, viewDirectionWS,
        viewWeights, indirectDiffWeights, indirectSpecWeights);

    VividLightingLoopContext lightLoop = VividLightingLoop::Create(pixelCoord, positionWS);
    bool realtimeOpenPbrDiffuse = false;
    #if defined(VIVID_REALTIME_DIFFUSE_GI)
        realtimeOpenPbrDiffuse = _RealtimeOpenPbrDiffuse != 0u;
    #endif
    float4 realtimeDiffuseGI = LoadVividRealtimeDiffuseGI(pixelCoord, summary.diffuseAlbedo);
    float4 realtimeRoughSpecular = LoadVividRealtimeRoughSpecular(pixelCoord);
    bool hasRealtimeRoughSpecular = VividHasRealtimeRoughSpecular();
    float3 lumenReflections = LoadVividRealtimeLumenReflections(pixelCoord);
    bool hasLumenReflections = VividHasRealtimeLumenReflections();
    float ao = saturate(summary.ambientOcclusion);

    // 1. Indirect lighting accumulated across all closures with independent normals
    [unroll]
    for (uint c = 0u; c < VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL; ++c)
    {
        if (c < header.closureCount && closureFrontFacing[c])
        {
            bool isTopClosure = (c + 1u) == header.closureCount;
            VividSimpleSlabDeferredLighting cIndirect = VividEvaluateOpenPBRV2Indirect(
                closures[c], resolvedList[c], compList[c], lightLoop,
                positionWS, viewDirectionWS, ao,
                realtimeDiffuseGI, realtimeOpenPbrDiffuse,
                realtimeRoughSpecular, hasRealtimeRoughSpecular,
                lumenReflections, hasLumenReflections && isTopClosure);
            float3 wDiff = indirectDiffWeights[c];
            float3 wSpec = indirectSpecWeights[c];
            result.indirectDiffuseLighting += cIndirect.indirectDiffuseLighting * wDiff;
            result.indirectSpecularLighting += cIndirect.indirectSpecularLighting * wSpec;
            result.diffuseLighting += cIndirect.diffuseLighting * wDiff;
            result.specularLighting += cIndirect.specularLighting * wSpec;
        }
    }

    // 2. Direct directional lights
    [loop]
    for (uint i = 0u; i < _DirectionalLightCount; ++i)
    {
        DirectionalLightData light = GetDirectionalLight(i);
        float shadow = EvaluateDeferredDirectionalShadowAttenuation(pixelCoord, i, light);
        float3 lightDir = SafeNormalize(light.directionWS);
        float3 radiance = max(light.color, 0.0f) * shadow;
        float3 directWeights[VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL];
        VividComputeMultiClosureDirectWeights(
            header.closureCount, closures, operators, viewWeights, lightDir, directWeights);

        [unroll]
        for (uint c = 0u; c < VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL; ++c)
        {
            if (c < header.closureCount && closureFrontFacing[c])
            {
                float3 n = resolvedList[c].geometry_basis.n;
                float3 cn = resolvedList[c].geometry_coat_basis.n;
                if (dot(n, lightDir) > 0.0f || (resolvedList[c].coat_weight > 0.0f && dot(cn, lightDir) > 0.0f))
                {
                    float3 wRad = radiance * directWeights[c];
                    diffuseBounce += VividEvaluateOpenPBROpaqueDiffuseBounce(closures[c].core, lightDir, wRad);
                    VividAccumulateOpenPBROpaqueLight(
                        openpbr_scale_diffuse_specular(openpbr_eval(preparedList[c], lightDir), wRad), result);
                }
            }
        }
    }

    // 3. Direct punctual lights
    uint punctualCount = VividLightingLoop::GetPunctualLightCount(lightLoop);
    [loop]
    for (uint i = 0u; i < punctualCount; ++i)
    {
        PunctualLightData light = VividLightingLoop::LoadPunctualLight(lightLoop, i);
        float3 direction;
        float4 distances;
        GetVividPunctualLightVectors(positionWS, light, direction, distances);
        if (distances.y <= VIVID_SIMPLE_SLAB_MIN_PUNCTUAL_DISTANCE_SQ)
            continue;
        float attenuation = VividPunctualLightAttenuationWithDistanceModification(
            light, positionWS - light.positionWS, distances);
        if (attenuation <= 0.0f)
            continue;
        float3 radiance = max(light.color, 0.0f) * attenuation;
        float3 directWeights[VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL];
        VividComputeMultiClosureDirectWeights(
            header.closureCount, closures, operators, viewWeights, direction, directWeights);

        [unroll]
        for (uint c = 0u; c < VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL; ++c)
        {
            if (c < header.closureCount && closureFrontFacing[c])
            {
                float3 n = resolvedList[c].geometry_basis.n;
                float3 cn = resolvedList[c].geometry_coat_basis.n;
                if (dot(n, direction) > 0.0f || (resolvedList[c].coat_weight > 0.0f && dot(cn, direction) > 0.0f))
                {
                    float3 wRad = radiance * directWeights[c];
                    diffuseBounce += VividEvaluateOpenPBROpaqueDiffuseBounce(closures[c].core, direction, wRad);
                    VividAccumulateOpenPBROpaqueLight(
                        openpbr_scale_diffuse_specular(openpbr_eval(preparedList[c], direction), wRad), result);
                }
            }
        }
    }

    // 4. Area lights (LTC weighted by indirect diffuse/specular transmission weights)
    uint areaCount = VividLightingLoop::GetAreaLightCount(lightLoop);
    [loop]
    for (uint i = 0u; i < areaCount; ++i)
    {
        AreaLightData areaLight = VividLightingLoop::LoadAreaLight(lightLoop, i);
        [unroll]
        for (uint c = 0u; c < VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL; ++c)
        {
            if (c < header.closureCount && closureFrontFacing[c])
            {
                float3 areaBounce;
                OpenPBR_DiffuseSpecular contrib = VividEvaluateOpenPBRV2AreaLight(
                    closures[c], resolvedList[c], compList[c], positionWS, viewDirectionWS,
                    areaLight, areaBounce);
                contrib.diffuse *= indirectDiffWeights[c];
                contrib.specular *= indirectSpecWeights[c];
                VividAccumulateOpenPBROpaqueLight(contrib, result);
                diffuseBounce += areaBounce * indirectDiffWeights[c];
            }
        }
    }

    return true;
}

VividSimpleSlabDeferredLighting EvaluateDeferredFastSlabLighting(
    VividSurfaceSummaryData summary, uint2 pixelCoord, float3 positionWS)
{
    VividSimpleSlabData slab = BuildVividSimpleSlabData(summary);
    float3 viewDirectionWS = SafeNormalize(GetWorldSpaceCameraViewDir(positionWS));
    VividSimpleSlabPreLightData preLight =
        VividPrepareSimpleSlabDeferredLighting(slab, viewDirectionWS);
    VividLightingLoopContext lightLoop = VividLightingLoop::Create(pixelCoord, positionWS);
    VividSimpleSlabIndirectLighting indirectLighting = VividEvaluateSimpleSlabIndirectLighting(
        summary, slab, preLight, lightLoop, positionWS, viewDirectionWS,
        LoadVividRealtimeDiffuseGI(pixelCoord, summary.diffuseAlbedo),
        LoadVividRealtimeRoughSpecular(pixelCoord), VividHasRealtimeRoughSpecular(),
        LoadVividRealtimeLumenReflections(pixelCoord), VividHasRealtimeLumenReflections());
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
    float3 viewDirectionWS = SafeNormalize(GetWorldSpaceCameraViewDir(positionWS));
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
    float4 realtimeRoughSpecular = LoadVividRealtimeRoughSpecular(pixelCoord);
    bool hasRealtimeRoughSpecular = VividHasRealtimeRoughSpecular();
    float3 realtimeLumenReflections = LoadVividRealtimeLumenReflections(pixelCoord);
    bool hasRealtimeLumenReflections = VividHasRealtimeLumenReflections();
    VividSimpleSlabIndirectLighting baseIndirectLighting = VividEvaluateSimpleSlabIndirectLighting(
        baseSummary, baseSimpleSlab, basePreLightData, lightLoop, positionWS, viewDirectionWS,
        LoadVividRealtimeDiffuseGI(pixelCoord, baseSummary.diffuseAlbedo),
        realtimeRoughSpecular, hasRealtimeRoughSpecular,
        realtimeLumenReflections, hasRealtimeLumenReflections);
    VividSimpleSlabIndirectLighting topIndirectLighting = VividEvaluateSimpleSlabIndirectLighting(
        topSummary, topSimpleSlab, topPreLightData, lightLoop, positionWS, viewDirectionWS,
        LoadVividRealtimeDiffuseGI(pixelCoord, topSummary.diffuseAlbedo),
        realtimeRoughSpecular, hasRealtimeRoughSpecular,
        realtimeLumenReflections, hasRealtimeLumenReflections);

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

// Stage 3.2 Tier 3 Hair/Fur BCSDF evaluator (UE5 Karis Far-Field Marschner +
// Zinke Dual Scattering + Yan Medulla + Lumen Diffuse GI / Reflections).
bool TryEvaluateDeferredHairLighting(
    VividSurfaceSummaryData summary,
    uint2 pixelCoord,
    float3 positionWS,
    out VividSimpleSlabDeferredLighting result,
    out float3 diffuseBounce)
{
    result = (VividSimpleSlabDeferredLighting)0;
    diffuseBounce = 0.0f.xxx;
    if (_SubstrateClosureBufferEnabled == 0u)
        return false;

    VividSubstrateHairInputs hair;
    VividSubstratePixelHeader header;
    if (!VividTryLoadSubstrateHair(_SubstrateClosureBuffer, pixelCoord, hair, header))
        return false;

    float3 viewDirectionWS = SafeNormalize(GetWorldSpaceCameraViewDir(positionWS));

    // summary.ambientOcclusion already carries hair.ambientOcclusion * SampleGTAO(pixelCoord).
    VividHairFurBSDFInputs bsdfIn = VividCreateDefaultHairFurBSDFInputs();
    bsdfIn.baseColor = hair.baseColor;
    bsdfIn.tangentWS = hair.tangentWS;
    bsdfIn.geomNormalWS = summary.normalWS;
    bsdfIn.roughness = hair.roughness;
    bsdfIn.radialRoughness = hair.radialRoughness;
    bsdfIn.cuticleAngleRad = hair.cuticleAngleRad;
    bsdfIn.specular = hair.specular;
    bsdfIn.scatter = hair.scatter;
    bsdfIn.backlit = hair.backlit;
    bsdfIn.medullaWeight = hair.medullaWeight;
    bsdfIn.medullaRoughness = hair.medullaRoughness;
    bsdfIn.shellDepth = hair.shellDepth;
    bsdfIn.ambientOcclusion = saturate(summary.ambientOcclusion);

    VividLightingLoopContext lightLoop = VividLightingLoop::Create(pixelCoord, positionWS);

    // 1. Indirect Lighting (Lumen Diffuse GI / ProbeVolume / Sky + Reflections via cylinder normal V_perp)
    float3 cylNormal = VividComputeHairCylinderNormal(bsdfIn.tangentWS, viewDirectionWS, summary.normalWS);
    float4 realtimeDiffuseGI = LoadVividRealtimeDiffuseGI(pixelCoord, summary.diffuseAlbedo);
    float3 diffuseIrradiance;
    if (realtimeDiffuseGI.a >= 1.0f)
        diffuseIrradiance = realtimeDiffuseGI.rgb;
    else if (VividHasDeferredExportFlag(summary.deferredExportHeader,
            VIVID_DEFERRED_EXPORT_FLAG_HAS_DIFFUSE_IRRADIANCE))
        diffuseIrradiance = summary.diffuseIrradiance;
    else if (VividHasProbeVolumeGI())
        diffuseIrradiance = SampleVividProbeVolume(positionWS,
            cylNormal, viewDirectionWS, 0xFFFFFFFFu, VividAllowUnoccludedSkyFallback());
    else
        diffuseIrradiance = VividAllowUnoccludedSkyFallback() ? VividSampleAmbientProbe(cylNormal) : 0.0f.xxx;
    if (realtimeDiffuseGI.a > 0.0f && realtimeDiffuseGI.a < 1.0f)
        diffuseIrradiance = lerp(diffuseIrradiance, realtimeDiffuseGI.rgb, realtimeDiffuseGI.a);

    float3 reflDir = GetSpecularDominantDir(
        cylNormal,
        reflect(-viewDirectionWS, cylNormal),
        bsdfIn.roughness,
        saturate(dot(cylNormal, viewDirectionWS)));
    float3 specRadiance = VividSampleSimpleSlabEnvironment(
        lightLoop,
        positionWS,
        cylNormal,
        reflDir,
        bsdfIn.roughness,
        LoadVividRealtimeRoughSpecular(pixelCoord),
        realtimeDiffuseGI.a,
        VividHasRealtimeRoughSpecular(),
        LoadVividRealtimeLumenReflections(pixelCoord),
        VividHasRealtimeLumenReflections());

    float f0 = VividHairF0FromSpecular(bsdfIn.specular);
    float sinThetaV = clamp(dot(bsdfIn.tangentWS, viewDirectionWS), -1.0f, 1.0f);
    float cosThetaV = sqrt(saturate(1.0f - sinThetaV * sinThetaV));
    float F_env = VividHairFresnelSchlick(f0, cosThetaV * 0.70710678f);
    float3 envDiffuseAlbedo = lerp(
        bsdfIn.baseColor,
        sqrt(max(bsdfIn.baseColor, 1e-4f.xxx)) * bsdfIn.baseColor,
        0.5f * saturate(bsdfIn.scatter));
    envDiffuseAlbedo = lerp(envDiffuseAlbedo, bsdfIn.baseColor * 0.85f, saturate(bsdfIn.medullaWeight) * 0.5f);

    result.indirectDiffuseLighting = max(diffuseIrradiance, 0.0f.xxx) * envDiffuseAlbedo * (1.0f - F_env) * bsdfIn.ambientOcclusion;
    result.indirectSpecularLighting = max(specRadiance, 0.0f.xxx) * F_env * lerp(0.35f, 1.0f, bsdfIn.ambientOcclusion);
    result.diffuseLighting = result.indirectDiffuseLighting;
    result.specularLighting = result.indirectSpecularLighting;
    result.screenSpaceReflectionFGD = F_env.xxx;

    // 2. Direct Directional Lights (Marschner R/TT/TRT + Medulla + Dual Scattering shadow penetration)
    [loop]
    for (uint i = 0u; i < _DirectionalLightCount; ++i)
    {
        DirectionalLightData light = GetDirectionalLight(i);
        float shadow = EvaluateDeferredDirectionalShadowAttenuation(pixelCoord, i, light);
        float3 lightDir = SafeNormalize(light.directionWS);
        float3 unshadowed = max(light.color, 0.0f.xxx);

        VividHairFurBSDFResponse resp = VividEvaluateHairFurDirect(bsdfIn, viewDirectionWS, lightDir, shadow);
        float3 directSpec = unshadowed * (resp.lobeR + resp.lobeTRT) * saturate(shadow);
        float3 directDiff = unshadowed * ((resp.lobeTT + resp.lobeMedulla) * saturate(shadow) + resp.lobeDualScatter);
        result.specularLighting += directSpec;
        result.diffuseLighting += directDiff;
        diffuseBounce += directDiff;
    }

    // 3. Direct Punctual Lights
    uint punctualCount = VividLightingLoop::GetPunctualLightCount(lightLoop);
    [loop]
    for (uint i = 0u; i < punctualCount; ++i)
    {
        PunctualLightData light = VividLightingLoop::LoadPunctualLight(lightLoop, i);
        float3 direction;
        float4 distances;
        GetVividPunctualLightVectors(positionWS, light, direction, distances);
        if (distances.y <= VIVID_SIMPLE_SLAB_MIN_PUNCTUAL_DISTANCE_SQ)
            continue;
        float attenuation = VividPunctualLightAttenuationWithDistanceModification(
            light, positionWS - light.positionWS, distances);
        if (attenuation > 0.0f)
        {
            float3 radiance = max(light.color, 0.0f.xxx) * attenuation;
            VividHairFurBSDFResponse resp = VividEvaluateHairFurDirect(bsdfIn, viewDirectionWS, direction, 1.0f);
            float3 directSpec = radiance * (resp.lobeR + resp.lobeTRT);
            float3 directDiff = radiance * (resp.lobeTT + resp.lobeMedulla + resp.lobeDualScatter);
            result.specularLighting += directSpec;
            result.diffuseLighting += directDiff;
            diffuseBounce += directDiff;
        }
    }

    // 4. Direct Area Lights (representative direction + projected solid-angle energy)
    uint areaCount = VividLightingLoop::GetAreaLightCount(lightLoop);
    [loop]
    for (uint i = 0u; i < areaCount; ++i)
    {
        AreaLightData light = VividLightingLoop::LoadAreaLight(lightLoop, i);
        ApplyRectangularAreaLightBarnDoor(light, positionWS);
        float intensity = EvaluateAreaLightIntensity(light, positionWS);
        if (intensity <= 0.0f)
            continue;
        float3 toLight = light.positionWS - positionWS;
        float distSq = max(dot(toLight, toLight), 1e-4f);
        float dist = sqrt(distSq);
        float3 lightDir = toLight / dist;
        float area = max(light.width * light.height, 1e-4f);
        float frontFacing = saturate(dot(light.forwardWS, -lightDir));
        float solidAngle = saturate(area * frontFacing / (distSq + area * VIVID_HAIR_INV_PI));
        if (solidAngle > 1e-5f)
        {
            float3 radiance = max(light.color, 0.0f.xxx) * (intensity * solidAngle);
            VividHairFurBSDFResponse resp = VividEvaluateHairFurDirect(bsdfIn, viewDirectionWS, lightDir, 1.0f);
            float3 directSpec = radiance * (resp.lobeR + resp.lobeTRT);
            float3 directDiff = radiance * (resp.lobeTT + resp.lobeMedulla + resp.lobeDualScatter);
            result.specularLighting += directSpec;
            result.diffuseLighting += directDiff;
            diffuseBounce += directDiff;
        }
    }

    return true;
}

// Compile-time tile tier specialization for DeferredLit compute kernels:
//   Tier 0 (VIVID_DEFERRED_TIER_0): FastSlab only (minimal VGPR footprint)
//   Tier 1 (VIVID_DEFERRED_TIER_1): FastSlab + OpenPBR Single Simple
//   Tier 2 (VIVID_DEFERRED_TIER_2): FastSlab + OpenPBR Single Simple/Complex (Coat/Fuzz/Aniso) + DualSlab
//   Tier 3 (default):               Full catch-all (Multi-Closure / Hair BCSDF / Error fallback)
#if defined(VIVID_DEFERRED_TIER_0)
    #define VIVID_DEFERRED_ENABLE_OPENPBR 0
    #define VIVID_DEFERRED_ENABLE_OPENPBR_V2 0
    #define VIVID_DEFERRED_ENABLE_SUBSTRATE_MULTI_CLOSURE 0
    #define VIVID_DEFERRED_ENABLE_SUBSTRATE_HAIR 0
    #define VIVID_DEFERRED_ENABLE_DUAL_SLAB 0
#elif defined(VIVID_DEFERRED_TIER_1)
    #define VIVID_DEFERRED_ENABLE_OPENPBR 1
    #define VIVID_DEFERRED_ENABLE_OPENPBR_V2 0
    #define VIVID_DEFERRED_ENABLE_SUBSTRATE_MULTI_CLOSURE 0
    #define VIVID_DEFERRED_ENABLE_SUBSTRATE_HAIR 0
    #define VIVID_DEFERRED_ENABLE_DUAL_SLAB 0
#elif defined(VIVID_DEFERRED_TIER_2)
    #define VIVID_DEFERRED_ENABLE_OPENPBR 1
    #define VIVID_DEFERRED_ENABLE_OPENPBR_V2 1
    #define VIVID_DEFERRED_ENABLE_SUBSTRATE_MULTI_CLOSURE 0
    #define VIVID_DEFERRED_ENABLE_SUBSTRATE_HAIR 0
    #define VIVID_DEFERRED_ENABLE_DUAL_SLAB 1
#else
    #define VIVID_DEFERRED_ENABLE_OPENPBR 1
    #define VIVID_DEFERRED_ENABLE_OPENPBR_V2 1
    #define VIVID_DEFERRED_ENABLE_SUBSTRATE_MULTI_CLOSURE 1
    #define VIVID_DEFERRED_ENABLE_SUBSTRATE_HAIR 1
    #define VIVID_DEFERRED_ENABLE_DUAL_SLAB 1
#endif

// Non-sky pixels only. Returns complete pre-exposed lighting (including emission).
// Compute tile scheduling and raster geometry remain the caller's responsibility.
float3 VividEvaluateDeferredSurfacePixel(
    uint2 pixelCoord, float deviceDepth, out float4 debugLighting, out float3 bounceSource)
{
    debugLighting = 0.0;
    VividSurfaceSummaryData summary = LoadVividSurfaceSummary(pixelCoord);
    bounceSource = max(summary.emissive, 0.0);
    float3 emission = VividApplyPreExposure(bounceSource);
    uint deferredExportClass = VividGetDeferredExportClass(
        summary.deferredExportHeader);
    // Empty/Unlit need no BSDF resources. Reserved lit classes are not silently
    // treated as Unlit. Native OpenPBR has its own payload and resource gate.
    if (deferredExportClass == VIVID_DEFERRED_EXPORT_CLASS_EMPTY
        || deferredExportClass == VIVID_DEFERRED_EXPORT_CLASS_UNLIT)
        return emission;

    bool supportedClass = deferredExportClass == VIVID_DEFERRED_EXPORT_CLASS_FAST_SLAB;
    #if VIVID_DEFERRED_ENABLE_OPENPBR
        supportedClass = supportedClass || deferredExportClass == VIVID_DEFERRED_EXPORT_CLASS_OPENPBR_OPAQUE;
    #endif
    #if VIVID_DEFERRED_ENABLE_DUAL_SLAB
        supportedClass = supportedClass || deferredExportClass == VIVID_DEFERRED_EXPORT_CLASS_DUAL_SLAB;
    #endif
    if (!supportedClass)
    {
        debugLighting = float4(1.0, 0.0, 1.0, 1.0);
        return VividApplyPreExposure(float3(1.0, 0.0, 1.0));
    }

    if (deferredExportClass != VIVID_DEFERRED_EXPORT_CLASS_OPENPBR_OPAQUE
        && _VividSlabLutReady == 0u)
    {
        debugLighting = float4(1.0, 0.0, 1.0, 1.0);
        return VividApplyPreExposure(float3(1.0, 0.0, 1.0));
    }

    summary.ambientOcclusion *= saturate(SampleGTAO(pixelCoord));

    float2 uv = (float2(pixelCoord) + 0.5) * _ScreenSize.zw;
    float3 positionWS = ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
    VividSimpleSlabDeferredLighting lightLoopOutput;
    float3 nativeDiffuseBounce = 0.0;
    #if VIVID_DEFERRED_ENABLE_OPENPBR && VIVID_DEFERRED_ENABLE_DUAL_SLAB
    UNITY_BRANCH
    if (deferredExportClass == VIVID_DEFERRED_EXPORT_CLASS_OPENPBR_OPAQUE)
    {
        bool evaluatedOpenPbr = false;
        #if VIVID_DEFERRED_ENABLE_OPENPBR_V2
        if (_SubstrateClosureBufferEnabled != 0u)
        {
            VividSubstratePixelHeader subHeader = VividUnpackSubstratePixelHeader(
                _SubstrateClosureBuffer.Load(int4(pixelCoord, 0, 0)));
            if (subHeader.mode == VIVID_SUBSTRATE_MODE_SINGLE_COMPLEX)
            {
                evaluatedOpenPbr = TryEvaluateDeferredOpenPBRV2Lighting(
                    summary, pixelCoord, positionWS, lightLoopOutput, nativeDiffuseBounce);
            }
            #if VIVID_DEFERRED_ENABLE_SUBSTRATE_MULTI_CLOSURE
            else if (subHeader.mode == VIVID_SUBSTRATE_MODE_MULTI_CLOSURE)
            {
                evaluatedOpenPbr = TryEvaluateDeferredSubstrateMultiClosureLighting(
                    summary, pixelCoord, positionWS, lightLoopOutput, nativeDiffuseBounce);
            }
            #endif
            #if VIVID_DEFERRED_ENABLE_SUBSTRATE_HAIR
            else if (subHeader.mode == VIVID_SUBSTRATE_MODE_HAIR)
            {
                evaluatedOpenPbr = TryEvaluateDeferredHairLighting(
                    summary, pixelCoord, positionWS, lightLoopOutput, nativeDiffuseBounce);
            }
            #endif
            else
            {
                evaluatedOpenPbr = TryEvaluateDeferredOpenPBROpaqueLighting(
                    summary, pixelCoord, positionWS, lightLoopOutput, nativeDiffuseBounce);
            }
        }
        else
        #endif
        {
            evaluatedOpenPbr = TryEvaluateDeferredOpenPBROpaqueLighting(
                summary, pixelCoord, positionWS, lightLoopOutput, nativeDiffuseBounce);
        }
        if (!evaluatedOpenPbr)
        {
            debugLighting = float4(1.0, 0.0, 1.0, 1.0);
            return VividApplyPreExposure(float3(1.0, 0.0, 1.0));
        }
    }
    else if (deferredExportClass == VIVID_DEFERRED_EXPORT_CLASS_DUAL_SLAB)
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
    #elif VIVID_DEFERRED_ENABLE_OPENPBR
    UNITY_BRANCH
    if (deferredExportClass == VIVID_DEFERRED_EXPORT_CLASS_OPENPBR_OPAQUE)
    {
        if (!TryEvaluateDeferredOpenPBROpaqueLighting(summary, pixelCoord, positionWS,
                lightLoopOutput, nativeDiffuseBounce))
        {
            debugLighting = float4(1.0, 0.0, 1.0, 1.0);
            return VividApplyPreExposure(float3(1.0, 0.0, 1.0));
        }
    }
    else
    {
        lightLoopOutput = EvaluateDeferredFastSlabLighting(
            summary,
            pixelCoord,
            positionWS);
    }
    #else
    lightLoopOutput = EvaluateDeferredFastSlabLighting(
        summary,
        pixelCoord,
        positionWS);
    #endif
    bounceSource += deferredExportClass == VIVID_DEFERRED_EXPORT_CLASS_OPENPBR_OPAQUE
        ? nativeDiffuseBounce
        : max(lightLoopOutput.diffuseLighting - lightLoopOutput.indirectDiffuseLighting, 0.0);
    float3 diffuseLighting = VividApplyPreExposure(max(lightLoopOutput.diffuseLighting, 0.0));
    float specularVisibility = VividBentSpecularVisibility(pixelCoord, summary.normalWS,
        SafeNormalize(GetWorldSpaceCameraViewDir(positionWS)), summary.perceptualRoughness);
    float3 specularLighting = VividApplyPreExposure(max(lightLoopOutput.specularLighting
        - (1.0 - specularVisibility) * lightLoopOutput.indirectSpecularLighting, 0.0));
    float4 screenSpaceReflection = LoadScreenSpaceReflection(pixelCoord, summary);
    float reflectionWeight = screenSpaceReflection.a;
    float3 preExposedReplaceableSpecular = VividApplyPreExposure(max(lightLoopOutput.screenSpaceReplaceableSpecularLighting, 0.0));
    specularLighting = max(specularLighting - preExposedReplaceableSpecular * (reflectionWeight * specularVisibility), 0.0)
        + screenSpaceReflection.rgb * lightLoopOutput.screenSpaceReflectionFGD * reflectionWeight;
    float3 lighting = diffuseLighting + specularLighting;
    float3 indirectLighting = VividApplyPreExposure(max(
        lightLoopOutput.indirectDiffuseLighting + lightLoopOutput.indirectSpecularLighting * specularVisibility, 0.0));
    debugLighting = float4(indirectLighting, reflectionWeight);
    return emission + lighting;
}

float3 VividEvaluateDeferredSurfacePixel(uint2 pixelCoord, float deviceDepth, out float4 debugLighting)
{
    float3 unusedBounceSource;
    return VividEvaluateDeferredSurfacePixel(pixelCoord, deviceDepth, debugLighting, unusedBounceSource);
}

#endif
