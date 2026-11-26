#ifndef VIVIDRP_OPENPBR_OPAQUE_DEFERRED_LIGHTING_INCLUDED
#define VIVIDRP_OPENPBR_OPAQUE_DEFERRED_LIGHTING_INCLUDED

#include "VividOpenPBROpaque.hlsl"
#include "VividOpenPBRV2.hlsl"
#include "VividOpenPBROpaqueGBuffer.hlsl"
#include "VividSimpleSlabDeferredLighting.hlsl"

// High-sample importance-sampling reference used by validation fixtures to
// verify the O(1) production directional albedo against Vendor OpenPBR.
OpenPBR_DiffuseSpecular VividIntegrateOpenPBROpaqueAlbedo(OpenPBR_PreparedBsdf prepared, uint sampleCount)
{
    OpenPBR_DiffuseSpecular albedo = openpbr_make_zero_diffuse_specular();
    [loop]
    for (uint i = 0u; i < sampleCount; ++i)
    {
        // Stratify the lobe selector; decorrelate the two directional dimensions.
        float3 random = float3((i + 0.5) / sampleCount,
            frac((i + 0.5) * 0.754877666), frac((i + 0.5) * 0.569840296));
        float3 direction;
        float pdf;
        OpenPBR_DiffuseSpecular weight;
        OpenPBR_BsdfLobeType sampledType;
        openpbr_sample(prepared, random, direction, weight, pdf, sampledType);
        // A rejected sample contributes zero; its other outputs are undefined.
        if (pdf > 0.0)
        {
            albedo.diffuse += weight.diffuse;
            albedo.specular += weight.specular;
        }
    }
    return openpbr_scale_diffuse_specular(albedo, 1.0 / sampleCount);
}

OpenPBR_DiffuseSpecular VividEvaluateOpenPBROpaqueLight(
    OpenPBR_PreparedBsdf prepared, float3 normalWS, float3 directionWS, float3 radiance)
{
    if (dot(normalWS, directionWS) <= 0.0)
        return openpbr_make_zero_diffuse_specular();
    // eval already contains NdotL and all vendor multiple scattering.
    return openpbr_scale_diffuse_specular(openpbr_eval(prepared, directionWS), radiance);
}

void VividAccumulateOpenPBROpaqueLight(OpenPBR_DiffuseSpecular light,
    inout VividSimpleSlabDeferredLighting result)
{
    result.diffuseLighting += light.diffuse;
    result.specularLighting += light.specular;
}

float3 VividEvaluateOpenPBROpaqueDiffuseBounce(VividOpenPBROpaqueInputs inputs,
    float3 directionWS, float3 incidentRadiance)
{
    // Screen traces and Surface Cache hits share a view-independent Lambertian
    // radiance contract. The visible surface still uses the complete OpenPBR BSDF.
    return saturate(VividOpenPBROpaqueDiffuseColor(inputs)) * max(incidentRadiance, 0.0)
        * (INV_PI * saturate(dot(inputs.normalWS, directionWS)));
}

VividSimpleSlabDeferredLighting VividEvaluateOpenPBROpaqueIndirect(
    VividOpenPBROpaqueInputs inputs, OpenPBR_DiffuseSpecular albedo,
    VividLightingLoopContext lightLoop, float3 positionWS, float3 viewDirectionWS, float ao,
    float4 realtimeDiffuseGI, bool realtimeOpenPbrDiffuse,
    float4 realtimeRoughSpecular = 0.0f, bool hasRealtimeRoughSpecular = false,
    float3 lumenSpecular = 0.0f, bool hasLumenSpecular = false)
{
    VividSimpleSlabDeferredLighting result = (VividSimpleSlabDeferredLighting)0;
    // Dense ReSTIR has already integrated the directional BRDF. Its filter
    // domain only factors out the receiver's base diffuse color.
    float3 diffuse = realtimeDiffuseGI.rgb;
    if (realtimeOpenPbrDiffuse)
        diffuse *= VividOpenPBROpaqueDiffuseColor(inputs);
    if (realtimeDiffuseGI.a < 1.0f)
    {
        bool allowAmbientFallback = VividAllowUnoccludedSkyFallback();
        float3 fallback = VividHasProbeVolumeGI()
            ? SampleVividProbeVolume(positionWS, inputs.normalWS, viewDirectionWS, 0xFFFFFFFFu, allowAmbientFallback)
            : (allowAmbientFallback ? VividSampleAmbientProbe(inputs.normalWS) : 0.0);
        diffuse = lerp(realtimeOpenPbrDiffuse ? fallback * albedo.diffuse : fallback,
            diffuse, realtimeDiffuseGI.a);
    }
    float3 reflection = GetSpecularDominantDir(inputs.normalWS,
        reflect(-viewDirectionWS, inputs.normalWS), inputs.specularRoughness,
        saturate(dot(inputs.normalWS, viewDirectionWS)));
    // Realtime split-sum approximation: existing GGX-prefiltered environment
    // times the complete OpenPBR directional albedo. No Slab FGD/MS is applied.
    float3 radiance = VividSampleSimpleSlabEnvironment(lightLoop, positionWS,
        inputs.normalWS, reflection, inputs.specularRoughness,
        realtimeRoughSpecular, realtimeDiffuseGI.a, hasRealtimeRoughSpecular,
        lumenSpecular, hasLumenSpecular);
    result.indirectDiffuseLighting = (realtimeOpenPbrDiffuse ? diffuse : diffuse * albedo.diffuse) * ao;
    result.indirectSpecularLighting = radiance * albedo.specular * ao;
    result.diffuseLighting = result.indirectDiffuseLighting;
    result.specularLighting = result.indirectSpecularLighting;
    // Native export v1 does not enable SSR. Keep replacement terms empty.
    return result;
}

OpenPBR_DiffuseSpecular VividEvaluateOpenPBROpaqueAreaLight(
    VividOpenPBROpaqueInputs inputs, OpenPBR_DiffuseSpecular albedo,
    float3 positionWS, float3 viewDirectionWS, AreaLightData light, out float3 diffuseBounce)
{
    diffuseBounce = 0.0;
    ApplyRectangularAreaLightBarnDoor(light, positionWS);
    float intensity = EvaluateAreaLightIntensity(light, positionWS);
    float nDotV = dot(inputs.normalWS, viewDirectionWS);
    if (intensity <= 0.0 || nDotV <= 0.0)
        return openpbr_make_zero_diffuse_specular();
    float3x3 basis = GetOrthoBasisViewNormal(viewDirectionWS, inputs.normalWS, nDotV);
    float3 center = mul(basis, light.positionWS - positionWS);
    float3 right = mul(basis, light.rightWS);
    float3 up = mul(basis, light.upWS);
    bool rectangle = light.lightType == VIVID_AREA_LIGHT_TYPE_RECTANGLE;
    float4 diffuse = EvaluateLTC_Area(rectangle, center, right, up,
        light.width * 0.5, light.height * 0.5, float3x3(1,0,0,0,1,0,0,0,1));
    float3x3 specularMatrix = SampleLtcMatrix(inputs.specularRoughness,
        saturate(nDotV), VIVID_LTC_LIGHTING_MODEL_GGX);
    float4 specular = EvaluateLTC_Area(rectangle, center, right, up,
        light.width * 0.5, light.height * 0.5, transpose(specularMatrix));
    float3 color = max(light.color, 0.0) * intensity;
    diffuseBounce = diffuse.rgb * diffuse.a * color * saturate(VividOpenPBROpaqueDiffuseColor(inputs));
    // Lambert/GGX LTC shapes with OpenPBR integrated amplitudes. This is a
    // shape approximation for EON and multiple scattering, not an OpenPBR fit.
    return openpbr_make_diffuse_specular(diffuse.rgb * diffuse.a * color * albedo.diffuse,
        specular.rgb * specular.a * color * albedo.specular);
}

VividSimpleSlabDeferredLighting VividEvaluateOpenPBRV2Indirect(
    VividOpenPBRV2Inputs inputs,
    OpenPBR_ResolvedInputs resolved,
    VividOpenPBRV2AlbedoComponents comp,
    VividLightingLoopContext lightLoop,
    float3 positionWS,
    float3 viewDirectionWS,
    float ao,
    float4 realtimeDiffuseGI,
    bool realtimeOpenPbrDiffuse,
    float4 realtimeRoughSpecular = 0.0f,
    bool hasRealtimeRoughSpecular = false,
    float3 lumenSpecular = 0.0f,
    bool hasLumenSpecular = false)
{
    VividSimpleSlabDeferredLighting result = (VividSimpleSlabDeferredLighting)0;
    float3 baseNormal = resolved.geometry_basis.n;
    float3 coatNormal = resolved.geometry_coat_basis.n;

    // Diffuse GI
    float3 diffuse = realtimeDiffuseGI.rgb;
    if (realtimeOpenPbrDiffuse)
        diffuse *= VividOpenPBROpaqueDiffuseColor(inputs.core);
    if (realtimeDiffuseGI.a < 1.0f)
    {
        bool allowAmbientFallback = VividAllowUnoccludedSkyFallback();
        float3 fallback = VividHasProbeVolumeGI()
            ? SampleVividProbeVolume(positionWS, baseNormal, viewDirectionWS, 0xFFFFFFFFu, allowAmbientFallback)
            : (allowAmbientFallback ? VividSampleAmbientProbe(baseNormal) : 0.0f);
        diffuse = lerp(realtimeOpenPbrDiffuse ? fallback * comp.diffuse : fallback,
            diffuse, realtimeDiffuseGI.a);
    }

    // Base Specular IBL (with anisotropic bent normal when specular_anisotropy > 0)
    float3 bentBaseNormal = VividComputeAnisotropicBentNormal(
        resolved.geometry_basis,
        viewDirectionWS,
        resolved.specular_roughness_anisotropy,
        resolved.specular_anisotropy_rotation_cos_sin);
    float3 baseReflDir = GetSpecularDominantDir(
        bentBaseNormal,
        reflect(-viewDirectionWS, bentBaseNormal),
        comp.adjustedBaseRoughness,
        saturate(dot(bentBaseNormal, viewDirectionWS)));
    bool coatOwnsLumenSpecular = hasLumenSpecular && resolved.coat_weight >= 0.5f;
    float3 baseRadiance = VividSampleSimpleSlabEnvironment(
        lightLoop,
        positionWS,
        bentBaseNormal,
        baseReflDir,
        comp.adjustedBaseRoughness,
        realtimeRoughSpecular,
        realtimeDiffuseGI.a,
        hasRealtimeRoughSpecular,
        lumenSpecular,
        hasLumenSpecular && !coatOwnsLumenSpecular);
    float3 specIndirect = baseRadiance * comp.baseSpecular;

    // Independent Coat Specular IBL lobe
    if (resolved.coat_weight > 0.0f)
    {
        float3 coatReflDir = GetSpecularDominantDir(
            coatNormal,
            reflect(-viewDirectionWS, coatNormal),
            comp.adjustedCoatRoughness,
            saturate(dot(coatNormal, viewDirectionWS)));
        float3 coatRadiance = VividSampleSimpleSlabEnvironment(
            lightLoop,
            positionWS,
            coatNormal,
            coatReflDir,
            comp.adjustedCoatRoughness,
            realtimeRoughSpecular,
            realtimeDiffuseGI.a,
            hasRealtimeRoughSpecular,
            lumenSpecular,
            coatOwnsLumenSpecular);
        specIndirect += coatRadiance * comp.coatSpecular;
    }

    // Fuzz (Sheen) indirect lobe (broad grazing retro-reflection / rough environment)
    if (resolved.fuzz_weight > 0.0f)
    {
        float fuzzRough = max(resolved.fuzz_roughness, 0.5f);
        float3 fuzzRadiance = VividSampleSimpleSlabEnvironment(
            lightLoop,
            positionWS,
            baseNormal,
            baseNormal,
            fuzzRough,
            realtimeRoughSpecular,
            realtimeDiffuseGI.a,
            hasRealtimeRoughSpecular,
            0.0f,
            false);
        specIndirect += fuzzRadiance * comp.fuzzSpecular;
    }

    result.indirectDiffuseLighting = (realtimeOpenPbrDiffuse ? diffuse : diffuse * comp.diffuse) * ao;
    result.indirectSpecularLighting = specIndirect * ao;
    result.diffuseLighting = result.indirectDiffuseLighting;
    result.specularLighting = result.indirectSpecularLighting;
    return result;
}

OpenPBR_DiffuseSpecular VividEvaluateOpenPBRV2AreaLight(
    VividOpenPBRV2Inputs inputs,
    OpenPBR_ResolvedInputs resolved,
    VividOpenPBRV2AlbedoComponents comp,
    float3 positionWS,
    float3 viewDirectionWS,
    AreaLightData light,
    out float3 diffuseBounce)
{
    diffuseBounce = 0.0f;
    ApplyRectangularAreaLightBarnDoor(light, positionWS);
    float intensity = EvaluateAreaLightIntensity(light, positionWS);
    float3 baseNormal = resolved.geometry_basis.n;
    float3 coatNormal = resolved.geometry_coat_basis.n;
    float nDotV = dot(baseNormal, viewDirectionWS);
    float coatNDotV = dot(coatNormal, viewDirectionWS);
    if (intensity <= 0.0f || (nDotV <= 0.0f && coatNDotV <= 0.0f))
        return openpbr_make_zero_diffuse_specular();

    float3 color = max(light.color, 0.0f) * intensity;
    bool rectangle = light.lightType == VIVID_AREA_LIGHT_TYPE_RECTANGLE;
    float3 diffOut = 0.0f;
    float3 specOut = 0.0f;

    if (nDotV > 0.0f)
    {
        float3x3 basis = GetOrthoBasisViewNormal(viewDirectionWS, baseNormal, nDotV);
        float3 center = mul(basis, light.positionWS - positionWS);
        float3 right = mul(basis, light.rightWS);
        float3 up = mul(basis, light.upWS);
        float4 diffuse = EvaluateLTC_Area(rectangle, center, right, up,
            light.width * 0.5f, light.height * 0.5f, float3x3(1,0,0,0,1,0,0,0,1));
        float3x3 specularMatrix = SampleLtcMatrix(comp.adjustedBaseRoughness,
            saturate(nDotV), VIVID_LTC_LIGHTING_MODEL_GGX);
        float4 specular = EvaluateLTC_Area(rectangle, center, right, up,
            light.width * 0.5f, light.height * 0.5f, transpose(specularMatrix));
        diffuseBounce = diffuse.rgb * diffuse.a * color * saturate(VividOpenPBROpaqueDiffuseColor(inputs.core));
        diffOut += diffuse.rgb * diffuse.a * color * comp.diffuse;
        specOut += specular.rgb * specular.a * color * comp.baseSpecular;
        if (resolved.fuzz_weight > 0.0f)
            specOut += diffuse.rgb * diffuse.a * color * comp.fuzzSpecular;
    }

    if (resolved.coat_weight > 0.0f && coatNDotV > 0.0f)
    {
        float3x3 coatBasis = GetOrthoBasisViewNormal(viewDirectionWS, coatNormal, coatNDotV);
        float3 coatCenter = mul(coatBasis, light.positionWS - positionWS);
        float3 coatRight = mul(coatBasis, light.rightWS);
        float3 coatUp = mul(coatBasis, light.upWS);
        float3x3 coatMatrix = SampleLtcMatrix(comp.adjustedCoatRoughness,
            saturate(coatNDotV), VIVID_LTC_LIGHTING_MODEL_GGX);
        float4 coatSpec = EvaluateLTC_Area(rectangle, coatCenter, coatRight, coatUp,
            light.width * 0.5f, light.height * 0.5f, transpose(coatMatrix));
        specOut += coatSpec.rgb * coatSpec.a * color * comp.coatSpecular;
    }

    return openpbr_make_diffuse_specular(diffOut, specOut);
}

#endif
