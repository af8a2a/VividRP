#ifndef VIVIDRP_OPENPBR_OPAQUE_DEFERRED_LIGHTING_INCLUDED
#define VIVIDRP_OPENPBR_OPAQUE_DEFERRED_LIGHTING_INCLUDED

#include "VividOpenPBROpaque.hlsl"
#include "VividOpenPBROpaqueGBuffer.hlsl"
#include "VividSimpleSlabDeferredLighting.hlsl"

// Importance sampling is retained for sharp specular lobes and as an independent
// high-sample reference. Its discrete lobe selection must not set diffuse energy.
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

float3 VividIntegrateOpenPBROpaqueDiffuseAlbedo(
    OpenPBR_PreparedBsdf prepared, float3 normalWS, float3 viewDirectionWS)
{
    // Opaque V1 has isotropic EON diffuse with no coat/fuzz/transmission. At fixed
    // NdotL its azimuth dependence is affine in cos(phi) on each half-circle.
    // Evaluating their mean cosines (+/- 2/pi) integrates azimuth exactly, while
    // eight Gauss-Legendre nodes integrate NdotL. All BSDF/energy terms still
    // come from vendor eval; no prepared internals or replacement BRDF are used.
    const float nodes[8] = {0.0198550718, 0.1016667613, 0.2372337950, 0.4082826788,
        0.5917173212, 0.7627662050, 0.8983332387, 0.9801449282};
    const float weights[8] = {0.0506142681, 0.1111905172, 0.1568533229, 0.1813418917,
        0.1813418917, 0.1568533229, 0.1111905172, 0.0506142681};
    float3 tangent = viewDirectionWS - dot(normalWS, viewDirectionWS) * normalWS;
    float tangentLengthSq = dot(tangent, tangent);
    tangent = tangentLengthSq > 1e-8 ? tangent * rsqrt(tangentLengthSq)
        : openpbr_make_basis(normalWS).t;
    float3 bitangent = cross(normalWS, tangent);
    float3 azimuthX = (2.0 * INV_PI) * tangent;
    float3 azimuthY = sqrt(1.0 - 4.0 * INV_PI * INV_PI) * bitangent;
    float3 albedo = 0.0;
    [loop]
    for (uint i = 0u; i < 8u; ++i)
    {
        float mu = nodes[i];
        float radius = sqrt(1.0 - mu * mu);
        float3 positive = normalWS * mu + radius * (azimuthY + azimuthX);
        float3 negative = normalWS * mu + radius * (azimuthY - azimuthX);
        // eval includes NdotL; each azimuth half has measure pi.
        albedo += (openpbr_eval(prepared, positive).diffuse
            + openpbr_eval(prepared, negative).diffuse) * (PI * weights[i]);
    }
    return albedo;
}

OpenPBR_DiffuseSpecular VividIntegrateOpenPBROpaqueAlbedo(
    OpenPBR_PreparedBsdf prepared, float3 normalWS, float3 viewDirectionWS)
{
    OpenPBR_DiffuseSpecular albedo = VividIntegrateOpenPBROpaqueAlbedo(
        prepared, VIVID_OPENPBR_DEFERRED_ALBEDO_SAMPLES);
    // Fixed mixture samples jump whenever a view-dependent lobe CDF crosses a
    // sample. Diffuse multiplies already filtered GI, so those jumps never converge.
    albedo.diffuse = VividIntegrateOpenPBROpaqueDiffuseAlbedo(prepared, normalWS, viewDirectionWS);
    return albedo;
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

#endif
