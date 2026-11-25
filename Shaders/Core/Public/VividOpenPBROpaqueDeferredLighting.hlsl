#ifndef VIVIDRP_OPENPBR_OPAQUE_DEFERRED_LIGHTING_INCLUDED
#define VIVIDRP_OPENPBR_OPAQUE_DEFERRED_LIGHTING_INCLUDED

#include "VividOpenPBROpaque.hlsl"
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

// Analytic GGX VNDF single-scatter Fresnel moments for isotropic OpenPBR metal:
//   x = E[(1 - VdotH)^5]
//   y = E[(VdotH) * (1 - VdotH)^6]
// Exact at roughness == 0 and smooth across all view angles.
float2 VividEvaluateOpenPBROpaqueMetalFresnelMoments(float perceptualRoughness, float nDotV)
{
    float r = saturate(perceptualRoughness);
    float mu = saturate(nDotV);
    float a = r * r;
    float g = (3.550254 * a) / (1.0 + 0.474257 * a);
    float base = sqrt(mu * mu + 0.123840 * mu * g + g * g);
    float base2 = base * base;
    float u5 = saturate(base + r * (-0.070284 * base + 3.119923 * r * base
        - 0.372022 * base2 - 0.167350 * r * base2
        - 2.211443 * g - 0.325312 * r * g));
    float oneMinusU5 = 1.0 - u5;
    float m5 = openpbr_fifth_power(oneMinusU5);
    float delta16 = (base - mu) * (-0.317033 + 0.712343 * r + 0.882984 * base - 0.947608 * g)
        + r * (0.007521 + 0.119960 * r - 0.108890 * base - 0.055646 * base2 - 0.065363 * g);
    float m16 = m5 * max(0.0, u5 * oneMinusU5 + delta16);
    return float2(m5, m16);
}

float3 VividEvaluateOpenPBROpaqueDiffuseAlbedo(VividOpenPBROpaqueInputs inputs, float nDotV)
{
    float mu = saturate(nDotV);
    float opaqueDielectric = 1.0 - inputs.baseMetalness;
    if (mu <= 0.0 || opaqueDielectric <= 0.0)
        return 0.0;

    float3 diffAlbedo = inputs.baseColor * (inputs.baseWeight * opaqueDielectric);
    float rd = inputs.baseDiffuseRoughness;
    float af = 1.0 / (1.0 + OpenPBR_FONConstantA * rd);
    float eFo = openpbr_E_FON_approx(mu, rd);
    float avgEf = af * (1.0 + OpenPBR_FONConstantB * rd);
    float3 rhoMs = (diffAlbedo * diffAlbedo) * avgEf
        / max(1.0 - diffAlbedo * (1.0 - avgEf), 1e-7);
    float3 eonDirAlbedo = diffAlbedo * eFo + rhoMs * (1.0 - eFo);

    float alpha = inputs.specularRoughness * inputs.specularRoughness;
    float weightedIor = openpbr_apply_specular_weight_to_ior(inputs.specularIor, inputs.specularWeight);
    float dielCompLut = openpbr_look_up_opaque_dielectric_energy_complement(weightedIor, alpha, mu);
    return eonDirAlbedo * dielCompLut;
}

// O(1) directional albedo for OpenPBROpaqueV1 using Vendor's bound energy LUTs
// and closed-form EON / GGX metal moment terms (replaces per-pixel MC loops).
OpenPBR_DiffuseSpecular VividEvaluateOpenPBROpaqueAlbedo(
    VividOpenPBROpaqueInputs inputs, float3 normalWS, float3 viewDirectionWS)
{
    float mu = saturate(dot(normalWS, viewDirectionWS));
    if (mu <= 0.0)
        return openpbr_make_zero_diffuse_specular();

    float roughness = saturate(inputs.specularRoughness);
    float alpha = roughness * roughness;
    float opaqueDielectric = 1.0 - inputs.baseMetalness;
    float darkenedMetal = inputs.baseMetalness * inputs.specularWeight;
    float3 weightedBaseColor = inputs.baseColor * inputs.baseWeight;

    bool hasMetalLut = false;
    float metalLoss = 0.0;

    float3 diffuse = 0.0;
    float3 specDielectric = 0.0;
    if (opaqueDielectric > 0.0)
    {
        float weightedIor = openpbr_apply_specular_weight_to_ior(inputs.specularIor, inputs.specularWeight);
        float dielCompLut = openpbr_look_up_opaque_dielectric_energy_complement(weightedIor, alpha, mu);

        float dielRefl;
        if (alpha == 0.0)
        {
            dielRefl = openpbr_fresnel(weightedIor, mu);
        }
        else if (weightedIor > OpenPBR_IorMax)
        {
            // Beyond OpenPBR_IorMax (2.5), extrapolate specular reflectance using exact
            // Fresnel ratio at effective microfacet cosine, clamped to single-scatter E_white.
            float compAtMax = openpbr_look_up_opaque_dielectric_energy_complement(OpenPBR_IorMax, alpha, mu);
            float reflAtMax = saturate(1.0 - compAtMax);
            float muEff = lerp(mu, 0.5 + 0.35 * mu, roughness);
            float fMax = max(openpbr_fresnel(OpenPBR_IorMax, muEff), 1e-5);
            float fCur = openpbr_fresnel(weightedIor, muEff);
            metalLoss = openpbr_look_up_ideal_metal_energy_complement(alpha, mu);
            hasMetalLut = true;
            dielRefl = min(reflAtMax * (fCur / fMax), saturate(1.0 - metalLoss));
        }
        else
        {
            dielRefl = saturate(1.0 - dielCompLut);
        }
        specDielectric = inputs.specularColor * (opaqueDielectric * dielRefl);

        float3 diffAlbedo = weightedBaseColor * opaqueDielectric;
        float rd = inputs.baseDiffuseRoughness;
        float af = 1.0 / (1.0 + OpenPBR_FONConstantA * rd);
        float eFo = openpbr_E_FON_approx(mu, rd);
        float avgEf = af * (1.0 + OpenPBR_FONConstantB * rd);
        float3 rhoMs = (diffAlbedo * diffAlbedo) * avgEf
            / max(1.0 - diffAlbedo * (1.0 - avgEf), 1e-7);
        diffuse = (diffAlbedo * eFo + rhoMs * (1.0 - eFo)) * dielCompLut;
    }

    float3 specMetal = 0.0;
    if (darkenedMetal > 0.0)
    {
        if (!hasMetalLut && alpha > 0.0)
            metalLoss = openpbr_look_up_ideal_metal_energy_complement(alpha, mu);
        float eWhite = saturate(1.0 - metalLoss);
        float2 moments = VividEvaluateOpenPBROpaqueMetalFresnelMoments(roughness, mu);
        float3 bFactor = openpbr_compute_metal_schlick_b_factor(weightedBaseColor, inputs.specularColor);
        float3 fresnelSS = saturate(
            weightedBaseColor + (1.0 - weightedBaseColor) * moments.x - bFactor * moments.y);
        specMetal = darkenedMetal * eWhite * fresnelSS;
        if (alpha >= OpenPBR_MinAlphaWithVisibleEnergyLoss)
        {
            float3 fresnelAvg = openpbr_metal_average_fresnel_with_f82_tint(weightedBaseColor, inputs.specularColor);
            specMetal += (darkenedMetal * metalLoss) * (fresnelAvg * fresnelAvg);
        }
    }

    return openpbr_make_diffuse_specular(diffuse, specDielectric + specMetal);
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
