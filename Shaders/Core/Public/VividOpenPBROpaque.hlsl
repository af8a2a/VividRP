#ifndef VIVIDRP_OPENPBR_OPAQUE_INCLUDED
#define VIVIDRP_OPENPBR_OPAQUE_INCLUDED

#include "VividOpenPBROpaqueContract.hlsl"
#include "../../Material/ShaderPass/OpenPBR/OpenPBR.hlsl"

// This profile does not change the shared bridge's compile-time features or PT.
bool VividTryResolveOpenPBROpaqueInputs(
    VividOpenPBROpaqueInputs inputs, uint requestedFeatures,
    out OpenPBR_ResolvedInputs resolved)
{
    resolved = openpbr_make_default_resolved_inputs();
    if (VividValidateOpenPBROpaqueInputs(inputs, requestedFeatures) != 0u)
        return false;

    resolved.base_weight = inputs.baseWeight;
    resolved.base_color = inputs.baseColor;
    resolved.base_diffuse_roughness = inputs.baseDiffuseRoughness;
    resolved.base_metalness = inputs.baseMetalness;
    resolved.specular_weight = inputs.specularWeight;
    resolved.specular_color = inputs.specularColor;
    resolved.specular_roughness = inputs.specularRoughness;
    resolved.specular_ior = inputs.specularIor;
    // Only normalize normals that already passed the input tolerance. Invalid
    // normals are rejected above; the vendor receives a unit construction input.
    resolved.geometry_basis = openpbr_make_basis(normalize(inputs.normalWS));
    resolved.geometry_coat_basis = resolved.geometry_basis;
    resolved.emission_luminance = inputs.emissionLuminance;
    resolved.emission_color = inputs.emissionColor;
    // Coverage belongs to the host. Surviving opaque pixels have unit opacity.
    resolved.geometry_opacity = VIVID_OPENPBR_OPAQUE_GEOMETRY_OPACITY;
    resolved.geometry_thin_walled = false;
    // Inactive lobe parameters retain vendor defaults, but no advanced lobe is active.
    resolved.coat_weight = 0.0f;
    resolved.fuzz_weight = 0.0f;
    resolved.transmission_weight = 0.0f;
    resolved.subsurface_weight = 0.0f;
    resolved.thin_film_weight = 0.0f;
    resolved.specular_roughness_anisotropy = 0.0f;
    resolved.coat_roughness_anisotropy = 0.0f;
    resolved.transmission_dispersion_scale = 0.0f;
    return true;
}

bool VividOpenPBROpaqueBasisIsValid(OpenPBR_Basis basis)
{
    const float tolerance = VIVID_OPENPBR_OPAQUE_NORMAL_LENGTH_SQUARED_TOLERANCE;
    return all(isfinite(basis.n)) && all(isfinite(basis.t)) && all(isfinite(basis.b))
        // Match the vendor's unit-vector precondition for an existing record.
        && abs(length(basis.n) - 1.0f) < 1e-6f
        && abs(length(basis.t) - 1.0f) < 1e-6f
        && abs(length(basis.b) - 1.0f) < 1e-6f
        && abs(dot(basis.n, basis.t)) <= tolerance
        && abs(dot(basis.n, basis.b)) <= tolerance
        && abs(dot(basis.t, basis.b)) <= tolerance;
}

bool VividOpenPBROpaqueResolvedInputsAreFinite(OpenPBR_ResolvedInputs resolved)
{
    // Zero weights do not shield dormant operands from 0 * NaN or Inf.
    return isfinite(resolved.base_weight)
        && all(isfinite(resolved.base_color))
        && isfinite(resolved.base_diffuse_roughness)
        && isfinite(resolved.base_metalness)
        && isfinite(resolved.subsurface_weight)
        && all(isfinite(resolved.subsurface_color))
        && isfinite(resolved.subsurface_radius)
        && all(isfinite(resolved.subsurface_radius_scale))
        && isfinite(resolved.subsurface_scatter_anisotropy)
        && isfinite(resolved.specular_weight)
        && all(isfinite(resolved.specular_color))
        && isfinite(resolved.specular_roughness)
        && isfinite(resolved.specular_roughness_anisotropy)
        && isfinite(resolved.specular_ior)
        && all(isfinite(resolved.specular_anisotropy_rotation_cos_sin))
        && isfinite(resolved.coat_weight)
        && all(isfinite(resolved.coat_color))
        && isfinite(resolved.coat_roughness)
        && isfinite(resolved.coat_roughness_anisotropy)
        && isfinite(resolved.coat_ior)
        && isfinite(resolved.coat_darkening)
        && all(isfinite(resolved.coat_anisotropy_rotation_cos_sin))
        && isfinite(resolved.fuzz_weight)
        && all(isfinite(resolved.fuzz_color))
        && isfinite(resolved.fuzz_roughness)
        && isfinite(resolved.transmission_weight)
        && all(isfinite(resolved.transmission_color))
        && isfinite(resolved.transmission_depth)
        && all(isfinite(resolved.transmission_scatter))
        && isfinite(resolved.transmission_scatter_anisotropy)
        && isfinite(resolved.transmission_dispersion_scale)
        && isfinite(resolved.transmission_dispersion_abbe_number)
        && isfinite(resolved.thin_film_weight)
        && isfinite(resolved.thin_film_thickness)
        && isfinite(resolved.thin_film_ior)
        && isfinite(resolved.emission_luminance)
        && all(isfinite(resolved.emission_color))
        && isfinite(resolved.geometry_opacity)
        && all(isfinite(resolved.geometry_basis.n))
        && all(isfinite(resolved.geometry_basis.t))
        && all(isfinite(resolved.geometry_basis.b))
        && all(isfinite(resolved.geometry_coat_basis.n))
        && all(isfinite(resolved.geometry_coat_basis.t))
        && all(isfinite(resolved.geometry_coat_basis.b));
}

bool VividOpenPBROpaqueResolvedInputsAreCanonical(OpenPBR_ResolvedInputs resolved)
{
    OpenPBR_ResolvedInputs defaults = openpbr_make_default_resolved_inputs();
    // This profile exposes eleven semantic fields, not extra dormant degrees of
    // freedom. Keep every other numeric field at its initialized vendor default.
    return resolved.subsurface_weight == defaults.subsurface_weight
        && all(resolved.subsurface_color == defaults.subsurface_color)
        && resolved.subsurface_radius == defaults.subsurface_radius
        && all(resolved.subsurface_radius_scale == defaults.subsurface_radius_scale)
        && resolved.subsurface_scatter_anisotropy == defaults.subsurface_scatter_anisotropy
        && resolved.specular_roughness_anisotropy == defaults.specular_roughness_anisotropy
        && all(resolved.specular_anisotropy_rotation_cos_sin == defaults.specular_anisotropy_rotation_cos_sin)
        && resolved.coat_weight == defaults.coat_weight
        && all(resolved.coat_color == defaults.coat_color)
        && resolved.coat_roughness == defaults.coat_roughness
        && resolved.coat_roughness_anisotropy == defaults.coat_roughness_anisotropy
        && resolved.coat_ior == defaults.coat_ior
        && resolved.coat_darkening == defaults.coat_darkening
        && all(resolved.coat_anisotropy_rotation_cos_sin == defaults.coat_anisotropy_rotation_cos_sin)
        && resolved.fuzz_weight == defaults.fuzz_weight
        && all(resolved.fuzz_color == defaults.fuzz_color)
        && resolved.fuzz_roughness == defaults.fuzz_roughness
        && resolved.transmission_weight == defaults.transmission_weight
        && all(resolved.transmission_color == defaults.transmission_color)
        && resolved.transmission_depth == defaults.transmission_depth
        && all(resolved.transmission_scatter == defaults.transmission_scatter)
        && resolved.transmission_scatter_anisotropy == defaults.transmission_scatter_anisotropy
        && resolved.transmission_dispersion_scale == defaults.transmission_dispersion_scale
        && resolved.transmission_dispersion_abbe_number == defaults.transmission_dispersion_abbe_number
        && resolved.thin_film_weight == defaults.thin_film_weight
        && resolved.thin_film_thickness == defaults.thin_film_thickness
        && resolved.thin_film_ior == defaults.thin_film_ior
        && resolved.geometry_opacity == defaults.geometry_opacity;
}

// Check an existing vendor record before admitting it to this profile. Nonzero
// excluded inputs are rejected; dormant parameters must retain vendor defaults.
uint VividValidateOpenPBROpaqueResolvedInputs(OpenPBR_ResolvedInputs resolved)
{
    uint features = 0u;
    if (resolved.coat_weight != 0.0f) features |= VIVID_OPENPBR_OPAQUE_FEATURE_COAT;
    if (resolved.fuzz_weight != 0.0f) features |= VIVID_OPENPBR_OPAQUE_FEATURE_FUZZ;
    if (resolved.transmission_weight != 0.0f) features |= VIVID_OPENPBR_OPAQUE_FEATURE_TRANSMISSION;
    if (resolved.subsurface_weight != 0.0f) features |= VIVID_OPENPBR_OPAQUE_FEATURE_SUBSURFACE;
    if (resolved.thin_film_weight != 0.0f) features |= VIVID_OPENPBR_OPAQUE_FEATURE_THIN_FILM;
    if (resolved.specular_roughness_anisotropy != 0.0f) features |= VIVID_OPENPBR_OPAQUE_FEATURE_SPECULAR_ANISOTROPY;
    if (resolved.coat_roughness_anisotropy != 0.0f) features |= VIVID_OPENPBR_OPAQUE_FEATURE_COAT_ANISOTROPY;
    if (resolved.transmission_dispersion_scale != 0.0f) features |= VIVID_OPENPBR_OPAQUE_FEATURE_DISPERSION;
    if (resolved.geometry_thin_walled) features |= VIVID_OPENPBR_OPAQUE_FEATURE_THIN_WALLED;
    if (any(resolved.specular_anisotropy_rotation_cos_sin != float2(1.0f, 0.0f)))
        features |= VIVID_OPENPBR_OPAQUE_FEATURE_SPECULAR_ANISOTROPY;
    if (any(resolved.coat_anisotropy_rotation_cos_sin != float2(1.0f, 0.0f)))
        features |= VIVID_OPENPBR_OPAQUE_FEATURE_COAT_ANISOTROPY;

    VividOpenPBROpaqueInputs inputs;
    inputs.baseWeight = resolved.base_weight;
    inputs.baseColor = resolved.base_color;
    inputs.baseDiffuseRoughness = resolved.base_diffuse_roughness;
    inputs.baseMetalness = resolved.base_metalness;
    inputs.specularWeight = resolved.specular_weight;
    inputs.specularColor = resolved.specular_color;
    inputs.specularRoughness = resolved.specular_roughness;
    inputs.specularIor = resolved.specular_ior;
    inputs.normalWS = resolved.geometry_basis.n;
    inputs.emissionLuminance = resolved.emission_luminance;
    inputs.emissionColor = resolved.emission_color;
    uint errors = VividValidateOpenPBROpaqueInputs(inputs, features);
    if (!VividOpenPBROpaqueResolvedInputsAreFinite(resolved))
        errors |= VIVID_OPENPBR_OPAQUE_ERROR_NON_FINITE;
    if (!VividOpenPBROpaqueResolvedInputsAreCanonical(resolved))
        errors |= VIVID_OPENPBR_OPAQUE_ERROR_OUT_OF_RANGE;
    if (!VividOpenPBROpaqueBasisIsValid(resolved.geometry_basis)
        || !VividOpenPBROpaqueBasisIsValid(resolved.geometry_coat_basis)
        || any(abs(resolved.geometry_basis.n - resolved.geometry_coat_basis.n)
            > VIVID_OPENPBR_OPAQUE_NORMAL_LENGTH_SQUARED_TOLERANCE)
        || any(abs(resolved.geometry_basis.t - resolved.geometry_coat_basis.t)
            > VIVID_OPENPBR_OPAQUE_NORMAL_LENGTH_SQUARED_TOLERANCE)
        || any(abs(resolved.geometry_basis.b - resolved.geometry_coat_basis.b)
            > VIVID_OPENPBR_OPAQUE_NORMAL_LENGTH_SQUARED_TOLERANCE))
    {
        errors |= VIVID_OPENPBR_OPAQUE_ERROR_INVALID_NORMAL;
    }
    return errors;
}

// Preconditions: validated profile inputs; finite unit V; host-owned sidedness.
// Keep this prepared, view-dependent state local, outside per-light loops.
OpenPBR_PreparedBsdf VividPrepareOpenPBROpaque(
    OpenPBR_ResolvedInputs resolved, float3 viewDirectionWS)
{
    return openpbr_prepare(resolved,
        VIVID_OPENPBR_OPAQUE_PREPARE_PATH_THROUGHPUT.xxx,
        OpenPBR_BaseRgbWavelengths_nm,
        VIVID_OPENPBR_OPAQUE_EXTERIOR_IOR,
        viewDirectionWS);
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
// and closed-form EON / GGX metal moment terms (shared by Deferred and Preview).
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

#endif
