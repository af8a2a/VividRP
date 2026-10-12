#ifndef VIVIDRP_OPENPBR_OPAQUE_DEFERRED_LIGHTING_INCLUDED
#define VIVIDRP_OPENPBR_OPAQUE_DEFERRED_LIGHTING_INCLUDED

#include "VividOpenPBROpaque.hlsl"
#include "VividOpenPBROpaqueGBuffer.hlsl"
#include "VividSimpleSlabDeferredLighting.hlsl"

// A bounded numerical directional-albedo estimate, not another energy
// compensation term. All weights come from the vendor BSDF. The reference
// tests can raise this count independently of the production path.
#ifndef VIVID_OPENPBR_DEFERRED_ALBEDO_SAMPLES
#define VIVID_OPENPBR_DEFERRED_ALBEDO_SAMPLES 32u
#endif

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

OpenPBR_DiffuseSpecular VividIntegrateOpenPBROpaqueAlbedo(OpenPBR_PreparedBsdf prepared)
{
    return VividIntegrateOpenPBROpaqueAlbedo(prepared, VIVID_OPENPBR_DEFERRED_ALBEDO_SAMPLES);
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

VividSimpleSlabDeferredLighting VividEvaluateOpenPBROpaqueIndirect(
    VividOpenPBROpaqueInputs inputs, OpenPBR_DiffuseSpecular albedo,
    VividLightingLoopContext lightLoop, float3 positionWS, float3 viewDirectionWS, float ao)
{
    VividSimpleSlabDeferredLighting result = (VividSimpleSlabDeferredLighting)0;
    float3 irradiance = VividHasProbeVolumeGI()
        ? SampleVividProbeVolume(positionWS, inputs.normalWS, viewDirectionWS, 0xFFFFFFFFu)
        : VividSampleAmbientProbe(inputs.normalWS);
    float3 reflection = GetSpecularDominantDir(inputs.normalWS,
        reflect(-viewDirectionWS, inputs.normalWS), inputs.specularRoughness,
        saturate(dot(inputs.normalWS, viewDirectionWS)));
    // Realtime split-sum approximation: existing GGX-prefiltered environment
    // times the complete OpenPBR directional albedo. No Slab FGD/MS is applied.
    float3 radiance = VividSampleSimpleSlabEnvironment(lightLoop, positionWS,
        inputs.normalWS, reflection, inputs.specularRoughness);
    result.indirectDiffuseLighting = irradiance * albedo.diffuse * ao;
    result.indirectSpecularLighting = radiance * albedo.specular * ao;
    result.diffuseLighting = result.indirectDiffuseLighting;
    result.specularLighting = result.indirectSpecularLighting;
    // Native export v1 does not enable SSR. Keep replacement terms empty.
    return result;
}

OpenPBR_DiffuseSpecular VividEvaluateOpenPBROpaqueAreaLight(
    VividOpenPBROpaqueInputs inputs, OpenPBR_DiffuseSpecular albedo,
    float3 positionWS, float3 viewDirectionWS, AreaLightData light)
{
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
    // Lambert/GGX LTC shapes with OpenPBR integrated amplitudes. This is a
    // shape approximation for EON and multiple scattering, not an OpenPBR fit.
    return openpbr_make_diffuse_specular(diffuse.rgb * diffuse.a * color * albedo.diffuse,
        specular.rgb * specular.a * color * albedo.specular);
}

#endif
