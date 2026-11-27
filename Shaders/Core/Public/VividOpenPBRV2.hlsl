#ifndef VIVIDRP_OPENPBR_V2_INCLUDED
#define VIVIDRP_OPENPBR_V2_INCLUDED

#include "VividOpenPBROpaque.hlsl"
#include "VividOpenPBROpaqueGBuffer.hlsl"
#include "VividSubstrateBuffer.hlsl"

// OpenPBR V2 single-closure adapter & O(1) extended directional albedo evaluator.
// Supports Coat (with independent coat normal, roughness, IOR, color, darkening),
// Fuzz/Sheen (Disney LTC sheen), and Specular Anisotropy (with tangent + rotation),
// matching Vendor OpenPBR 1.1 (openpbr_bsdf.h / openpbr_coating_lobe.h / openpbr_fuzz_lobe.h).

struct VividOpenPBRV2AlbedoComponents
{
    float3 diffuse;
    float3 baseSpecular;
    float3 coatSpecular;
    float3 fuzzSpecular;
    float adjustedBaseRoughness;
    float adjustedCoatRoughness;
};

OpenPBR_Basis VividMakeOpenPBRBasisFromTangent(float3 normalWS, float3 tangentWS, float handedness)
{
    float3 n = VividSanitizeSurfaceSummaryNormal(normalWS);
    float3 t = tangentWS - n * dot(tangentWS, n);
    float tLenSq = dot(t, t);
    if (tLenSq <= 1e-8f)
        return openpbr_make_basis(n);
    t *= rsqrt(tLenSq);
    return openpbr_make_basis(n, t, handedness < 0.0f ? -1.0f : 1.0f);
}

bool VividTryResolveOpenPBRV2Inputs(
    VividOpenPBRV2Inputs inputs,
    out OpenPBR_ResolvedInputs resolved)
{
    resolved = openpbr_make_default_resolved_inputs();
    if (VividValidateOpenPBROpaqueInputs(inputs.core, 0u) != 0u)
        return false;

    if (!isfinite(inputs.coatWeight) || !all(isfinite(inputs.coatColor))
        || !isfinite(inputs.coatRoughness) || !isfinite(inputs.coatIor) || !isfinite(inputs.coatDarkening)
        || !isfinite(inputs.fuzzWeight) || !all(isfinite(inputs.fuzzColor)) || !isfinite(inputs.fuzzRoughness)
        || !isfinite(inputs.specularAnisotropy) || !isfinite(inputs.specularAnisotropyRotation))
    {
        return false;
    }

    resolved.base_weight = inputs.core.baseWeight;
    resolved.base_color = inputs.core.baseColor;
    resolved.base_diffuse_roughness = inputs.core.baseDiffuseRoughness;
    resolved.base_metalness = inputs.core.baseMetalness;
    resolved.specular_weight = inputs.core.specularWeight;
    resolved.specular_color = inputs.core.specularColor;
    resolved.specular_roughness = inputs.core.specularRoughness;
    resolved.specular_ior = inputs.core.specularIor;

    float3 unitNormal = normalize(inputs.core.normalWS);
    if (inputs.specularAnisotropy > 0.0f)
    {
        resolved.geometry_basis = VividMakeOpenPBRBasisFromTangent(
            unitNormal,
            inputs.tangentWS,
            inputs.tangentHandedness);
        resolved.specular_roughness_anisotropy = saturate(inputs.specularAnisotropy);
        float s, c;
        sincos(inputs.specularAnisotropyRotation, s, c);
        resolved.specular_anisotropy_rotation_cos_sin = float2(c, s);
    }
    else
    {
        resolved.geometry_basis = openpbr_make_basis(unitNormal);
        resolved.specular_roughness_anisotropy = 0.0f;
        resolved.specular_anisotropy_rotation_cos_sin = float2(1.0f, 0.0f);
    }

    // Coat (independent normal when requested)
    resolved.coat_weight = saturate(inputs.coatWeight);
    resolved.coat_color = saturate(inputs.coatColor);
    resolved.coat_roughness = saturate(inputs.coatRoughness);
    resolved.coat_roughness_anisotropy = 0.0f;
    resolved.coat_ior = clamp(
        inputs.coatIor,
        VIVID_OPENPBR_OPAQUE_MINIMUM_SPECULAR_IOR,
        VIVID_OPENPBR_OPAQUE_MAXIMUM_SPECULAR_IOR);
    resolved.coat_darkening = saturate(inputs.coatDarkening);
    resolved.coat_anisotropy_rotation_cos_sin = float2(1.0f, 0.0f);
    if (resolved.coat_weight > 0.0f && inputs.hasIndependentCoatNormal != 0u)
    {
        float3 coatN = VividSanitizeSurfaceSummaryNormal(inputs.coatNormalWS);
        resolved.geometry_coat_basis = openpbr_make_basis(coatN);
    }
    else
    {
        resolved.geometry_coat_basis = resolved.geometry_basis;
    }

    // Fuzz / Sheen
    resolved.fuzz_weight = saturate(inputs.fuzzWeight);
    resolved.fuzz_color = saturate(inputs.fuzzColor);
    resolved.fuzz_roughness = saturate(inputs.fuzzRoughness);

    // Emission & opaque coverage
    resolved.emission_luminance = inputs.core.emissionLuminance;
    resolved.emission_color = inputs.core.emissionColor;
    resolved.geometry_opacity = VIVID_OPENPBR_OPAQUE_GEOMETRY_OPACITY;
    resolved.geometry_thin_walled = false;

    // Inactive transmission/subsurface/thin-film lobes remain at vendor defaults.
    resolved.transmission_weight = 0.0f;
    resolved.subsurface_weight = 0.0f;
    resolved.thin_film_weight = 0.0f;
    resolved.transmission_dispersion_scale = 0.0f;
    return true;
}

OpenPBR_PreparedBsdf VividPrepareOpenPBRV2(
    OpenPBR_ResolvedInputs resolved,
    float3 viewDirectionWS)
{
    // Opaque deferred shading normal horizon clamp: a perturbed normal-mapped base normal
    // can dip slightly behind viewDirectionWS at grazing silhouettes while the geometric/coat
    // normal is still front-facing. Prevent Vendor openpbr_prepare from classifying that rim
    // pixel as "inside" (which would drop the exterior coat lobe to black).
    float baseNdotV = dot(resolved.geometry_basis.n, viewDirectionWS);
    if (baseNdotV <= 1e-4f)
    {
        float3 shiftedN = SafeNormalize(
            resolved.geometry_basis.n + viewDirectionWS * (1e-4f - baseNdotV));
        float handedness = dot(cross(resolved.geometry_basis.n, resolved.geometry_basis.t), resolved.geometry_basis.b) < 0.0f
            ? -1.0f
            : 1.0f;
        resolved.geometry_basis = VividMakeOpenPBRBasisFromTangent(
            shiftedN,
            resolved.geometry_basis.t,
            handedness);
    }

    return openpbr_prepare(
        resolved,
        VIVID_OPENPBR_OPAQUE_PREPARE_PATH_THROUGHPUT.xxx,
        OpenPBR_BaseRgbWavelengths_nm,
        VIVID_OPENPBR_OPAQUE_EXTERIOR_IOR,
        viewDirectionWS);
}

// Verbatim OpenPBR 1.1 layer-interaction roughness adjustment (openpbr_bsdf.h:411-445).
void VividComputeOpenPBRV2AdjustedRoughnesses(
    VividOpenPBRV2Inputs inputs,
    out float adjustedCoatRoughness,
    out float adjustedBaseRoughness)
{
    const float fuzzEffect = 0.005f;
    float fuzzFactor = openpbr_average(saturate(inputs.fuzzColor)) * saturate(inputs.fuzzRoughness) * fuzzEffect;
    float coatRough = saturate(inputs.coatRoughness);
    adjustedCoatRoughness = lerp(
        coatRough,
        openpbr_fourth_root(min(1.0f,
            openpbr_fourth_power(coatRough) + fuzzFactor * openpbr_fourth_power(saturate(inputs.fuzzRoughness)))),
        saturate(inputs.fuzzWeight));

    float relCoatIor = clamp(
        inputs.coatIor,
        VIVID_OPENPBR_OPAQUE_MINIMUM_SPECULAR_IOR,
        VIVID_OPENPBR_OPAQUE_MAXIMUM_SPECULAR_IOR);
    float coatRoughAdjFactor = lerp(1.0f, 0.0f, relCoatIor >= 1.0f ? (1.0f / relCoatIor) : relCoatIor);
    float baseRough = saturate(inputs.core.specularRoughness);
    adjustedBaseRoughness = lerp(
        baseRough,
        openpbr_fourth_root(min(1.0f,
            openpbr_fourth_power(baseRough) + coatRoughAdjFactor * openpbr_fourth_power(adjustedCoatRoughness))),
        saturate(inputs.coatWeight));
}

// O(1) directional albedo for OpenPBR V2 (Base + Coat + Fuzz + Anisotropy),
// reusing Vendor's prepared coating/fuzz lobe state and bound energy/LTC LUTs.
VividOpenPBRV2AlbedoComponents VividEvaluateOpenPBRV2AlbedoComponents(
    VividOpenPBRV2Inputs inputs,
    OpenPBR_ResolvedInputs resolved,
    OpenPBR_PreparedBsdf prepared,
    float3 viewDirectionWS)
{
    VividOpenPBRV2AlbedoComponents comp = (VividOpenPBRV2AlbedoComponents)0;
    VividComputeOpenPBRV2AdjustedRoughnesses(inputs, comp.adjustedCoatRoughness, comp.adjustedBaseRoughness);

    // 1. Base layer directional albedo with coat-adjusted roughness & relative IOR (openpbr_bsdf.h:369-390).
    VividOpenPBROpaqueInputs effectiveBase = inputs.core;
    effectiveBase.specularRoughness = comp.adjustedBaseRoughness;
    if (resolved.coat_weight > 0.0f)
    {
        float weightedCoatIor = lerp(VIVID_OPENPBR_OPAQUE_EXTERIOR_IOR, resolved.coat_ior, resolved.coat_weight);
        float weightedSpecIor = openpbr_apply_specular_weight_to_ior(resolved.specular_ior, resolved.specular_weight);
        float relIor = weightedSpecIor >= weightedCoatIor
            ? (weightedSpecIor / max(weightedCoatIor, 1e-4f))
            : (weightedCoatIor / max(weightedSpecIor, 1e-4f));
        // Base dielectric lobe uses relative IOR against coat; specularWeight stays intact for darkenedMetal.
        effectiveBase.specularIor = relIor;
        // Neutralize double specular_weight application in dielectric IOR when coat modulates IOR:
        // evaluate dielectric & metal separately via core evaluator by factoring specularWeight cleanly.
        float savedSpecWeight = effectiveBase.specularWeight;
        float savedMetalness = effectiveBase.baseMetalness;
        effectiveBase.baseMetalness = 0.0f;
        effectiveBase.specularWeight = 1.0f;
        OpenPBR_DiffuseSpecular dielAlbedo = VividEvaluateOpenPBROpaqueAlbedo(
            effectiveBase, resolved.geometry_basis.n, viewDirectionWS);
        dielAlbedo.diffuse *= (1.0f - savedMetalness);
        dielAlbedo.specular *= (1.0f - savedMetalness);

        float3 metalSpec = 0.0f;
        if (savedMetalness * savedSpecWeight > 0.0f)
        {
            VividOpenPBROpaqueInputs mIn = effectiveBase;
            mIn.baseMetalness = 1.0f;
            mIn.specularWeight = savedSpecWeight;
            metalSpec = VividEvaluateOpenPBROpaqueAlbedo(
                mIn, resolved.geometry_basis.n, viewDirectionWS).specular * savedMetalness;
        }
        comp.diffuse = dielAlbedo.diffuse;
        comp.baseSpecular = dielAlbedo.specular + metalSpec;
    }
    else
    {
        OpenPBR_DiffuseSpecular baseAlbedo = VividEvaluateOpenPBROpaqueAlbedo(
            effectiveBase, resolved.geometry_basis.n, viewDirectionWS);
        comp.diffuse = baseAlbedo.diffuse;
        comp.baseSpecular = baseAlbedo.specular;
    }

    // 2. Coat attenuation + coat specular lobe (exact Vendor coating lobe equations).
    float3 coatNormal = resolved.geometry_coat_basis.n;
    if (resolved.coat_weight > 0.0f)
    {
        float coatRefl = prepared.fuzz_lobe.coating_lobe.in_reflected;
        float3 coatInScale = prepared.fuzz_lobe.coating_lobe.in_base_layer_scale;

        // Cosine-weighted hemisphere average cosine = 2/3 for diffuse outgoing passage.
        const float avgDiffCos = 0.6666667f;
        float coatOutReflDiff = openpbr_proportion_reflected(prepared.fuzz_lobe.coating_lobe, avgDiffCos);
        float3 coatOutScaleDiff = openpbr_base_layer_scale_one_side(
            prepared.fuzz_lobe.coating_lobe, avgDiffCos, coatOutReflDiff);

        // Specular outgoing direction blends from mirror reflection toward hemisphere avg with roughness.
        float3 reflBase = reflect(-viewDirectionWS, resolved.geometry_basis.n);
        float specOutCos = lerp(
            saturate(dot(reflBase, coatNormal)),
            avgDiffCos,
            comp.adjustedBaseRoughness * comp.adjustedBaseRoughness);
        specOutCos = max(specOutCos, 1e-4f);
        float coatOutReflSpec = openpbr_proportion_reflected(prepared.fuzz_lobe.coating_lobe, specOutCos);
        float3 coatOutScaleSpec = openpbr_base_layer_scale_one_side(
            prepared.fuzz_lobe.coating_lobe, specOutCos, coatOutReflSpec);

        comp.diffuse *= (coatInScale * coatOutScaleDiff);
        comp.baseSpecular *= (coatInScale * coatOutScaleSpec);
        comp.coatSpecular = coatRefl.xxx;
    }

    // 3. Fuzz (LTC Sheen) attenuation + sheen specular lobe (exact Vendor fuzz lobe equations).
    if (resolved.fuzz_weight > 0.0f)
    {
        float fuzzRefl = prepared.fuzz_lobe.view_reflected;
        float fuzzInScale = 1.0f - fuzzRefl;

        const float avgDiffCos = 0.6666667f;
        float fuzzOutScaleDiff = 1.0f - openpbr_proportion_reflected(prepared.fuzz_lobe, float3(0.0f, 0.0f, avgDiffCos));

        float3 reflBase = reflect(-viewDirectionWS, resolved.geometry_basis.n);
        float3 reflBaseLocal = openpbr_world_to_local(prepared.fuzz_lobe.basis, reflBase);
        float specOutCos = lerp(saturate(reflBaseLocal.z), avgDiffCos, comp.adjustedBaseRoughness * comp.adjustedBaseRoughness);
        float fuzzOutScaleSpec = 1.0f - openpbr_proportion_reflected(prepared.fuzz_lobe, float3(0.0f, 0.0f, max(specOutCos, 1e-4f)));

        float3 reflCoat = reflect(-viewDirectionWS, coatNormal);
        float3 reflCoatLocal = openpbr_world_to_local(prepared.fuzz_lobe.basis, reflCoat);
        float coatOutCos = lerp(saturate(reflCoatLocal.z), avgDiffCos, comp.adjustedCoatRoughness * comp.adjustedCoatRoughness);
        float fuzzOutScaleCoat = 1.0f - openpbr_proportion_reflected(prepared.fuzz_lobe, float3(0.0f, 0.0f, max(coatOutCos, 1e-4f)));

        comp.diffuse *= (fuzzInScale * fuzzOutScaleDiff);
        comp.baseSpecular *= (fuzzInScale * fuzzOutScaleSpec);
        comp.coatSpecular *= (fuzzInScale * fuzzOutScaleCoat);
        comp.fuzzSpecular = resolved.fuzz_color * fuzzRefl;
    }

    return comp;
}

OpenPBR_DiffuseSpecular VividEvaluateOpenPBRV2Albedo(
    VividOpenPBRV2Inputs inputs,
    OpenPBR_ResolvedInputs resolved,
    OpenPBR_PreparedBsdf prepared,
    float3 viewDirectionWS)
{
    VividOpenPBRV2AlbedoComponents comp = VividEvaluateOpenPBRV2AlbedoComponents(
        inputs, resolved, prepared, viewDirectionWS);
    return openpbr_make_diffuse_specular(
        comp.diffuse,
        comp.baseSpecular + comp.coatSpecular + comp.fuzzSpecular);
}

// TopLayer Summary builder for OpenPBR V2 so Lumen GI & Lumen Reflections see
// coat normal & coat roughness when a significant clearcoat is active.
VividSurfaceSummaryData VividBuildOpenPBRV2TopLayerSummary(VividOpenPBRV2Inputs inputs)
{
    VividSurfaceSummaryData summary = VividBuildOpenPBROpaqueSummary(inputs.core);
    if (inputs.coatWeight > 0.0f)
    {
        float coatRough = saturate(inputs.coatRoughness);
        summary.perceptualRoughness = lerp(
            summary.perceptualRoughness,
            min(summary.perceptualRoughness, coatRough),
            saturate(inputs.coatWeight));
        if (inputs.hasIndependentCoatNormal != 0u && inputs.coatWeight >= 0.5f)
        {
            summary.normalWS = VividSanitizeSurfaceSummaryNormal(inputs.coatNormalWS);
        }
    }
    return summary;
}

// Anisotropic bent normal for prefiltered isotropic environment map sampling
// (standard Frostbite / UE5 GetAnisotropicBentNormal approximation).
float3 VividComputeAnisotropicBentNormal(
    OpenPBR_Basis basis,
    float3 viewDirectionWS,
    float anisotropy,
    float2 rotCosSin)
{
    if (anisotropy <= 1e-4f)
        return basis.n;
    float3 t = basis.t * rotCosSin.x + basis.b * rotCosSin.y;
    float3 b = -basis.t * rotCosSin.y + basis.b * rotCosSin.x;
    float3 anisoDir = b; // OpenPBR positive anisotropy stretches highlight along bitangent (alpha_x > alpha_y)
    float3 anisoTangent = cross(anisoDir, viewDirectionWS);
    float3 anisoNormal = cross(anisoTangent, anisoDir);
    float bendFactor = saturate(anisotropy) * 0.5f;
    return SafeNormalize(lerp(basis.n, SafeNormalize(anisoNormal), bendFactor));
}

// ============================================================================
// Phase 2.2: Physical Multi-Closure Operators (HorizontalMix & VerticalLayer)
// Supports up to 4 OpenPBR closures with independent normals, dielectric
// Fresnel transmission, and Beer-Lambert optical slab absorption.
// ============================================================================

float3 VividComputeSlabBeerLambertTransmittance(
    float3 transmittanceColorLinear,
    float opticalThickness,
    float cosTheta)
{
    if (opticalThickness <= 1e-4f)
        return 1.0f.xxx;
    float3 safeColor = clamp(transmittanceColorLinear, 1e-4f, 1.0f);
    float invCos = rcp(max(saturate(cosTheta), 0.05f));
    return exp(log(safeColor) * (opticalThickness * invCos));
}

// View-side energy transmitted through a top VerticalLayer slab into the underlying stack.
// Metallic top slabs block transmission cleanly (retiring DualSlab metallic-inversion hacks).
float3 VividComputeVerticalLayerViewTransmission(
    VividOpenPBRV2Inputs topInputs,
    OpenPBR_DiffuseSpecular topAlbedo,
    float3 viewDirectionWS,
    float opticalThickness,
    float3 transmittanceColorLinear)
{
    float dielectricFrac = saturate(1.0f - topInputs.core.baseMetalness);
    if (dielectricFrac <= 0.0f)
        return 0.0f.xxx;
    float3 unreflected = saturate(1.0f.xxx - topAlbedo.diffuse - topAlbedo.specular);
    float muV = max(dot(topInputs.core.normalWS, viewDirectionWS), 0.05f);
    float3 beerV = VividComputeSlabBeerLambertTransmittance(transmittanceColorLinear, opticalThickness, muV);
    return dielectricFrac * unreflected * beerV;
}

// Light-side energy transmitted through a top VerticalLayer slab along outgoing direction L.
float3 VividComputeVerticalLayerLightTransmission(
    VividOpenPBRV2Inputs topInputs,
    float3 lightDirectionWS,
    float opticalThickness,
    float3 transmittanceColorLinear)
{
    float dielectricFrac = saturate(1.0f - topInputs.core.baseMetalness);
    if (dielectricFrac <= 0.0f)
        return 0.0f.xxx;
    float muL = max(dot(topInputs.core.normalWS, lightDirectionWS), 0.001f);
    float weightedIor = openpbr_apply_specular_weight_to_ior(
        topInputs.core.specularIor,
        topInputs.core.specularWeight);
    float fTopL = openpbr_fresnel(weightedIor, muL);
    float3 beerL = VividComputeSlabBeerLambertTransmittance(transmittanceColorLinear, opticalThickness, muL);
    return dielectricFrac * saturate(1.0f - fTopL) * beerL;
}

// Evaluates bottom-to-top operator chain (closures[0..N-1], operators[0..N-2]) to produce
// exact per-closure view weights and indirect diffuse/specular transmission weights.
void VividComputeMultiClosureIndirectWeights(
    uint closureCount,
    VividOpenPBRV2Inputs closures[VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL],
    OpenPBR_DiffuseSpecular closureAlbedos[VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL],
    VividSubstrateOperatorDesc operators[VIVID_SUBSTRATE_MAX_OPERATORS_PER_PIXEL],
    float3 viewDirectionWS,
    out float3 viewWeights[VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL],
    out float3 indirectDiffWeights[VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL],
    out float3 indirectSpecWeights[VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL])
{
    [unroll]
    for (uint i = 0u; i < VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL; ++i)
    {
        viewWeights[i] = (i == 0u) ? 1.0f.xxx : 0.0f.xxx;
        indirectDiffWeights[i] = (i == 0u) ? 1.0f.xxx : 0.0f.xxx;
        indirectSpecWeights[i] = (i == 0u) ? 1.0f.xxx : 0.0f.xxx;
    }

    const float avgDiffCos = 0.6666667f;
    uint opCount = closureCount > 1u ? (closureCount - 1u) : 0u;

    [unroll]
    for (uint opIdx = 0u; opIdx < VIVID_SUBSTRATE_MAX_OPERATORS_PER_PIXEL; ++opIdx)
    {
        if (opIdx >= opCount)
            break;

        uint fgIdx = opIdx + 1u;
        VividSubstrateOperatorDesc op = operators[opIdx];
        float w = saturate(op.weight);

        if (op.opKind == VIVID_SUBSTRATE_TOPOLOGY_VERTICAL_LAYER)
        {
            float3 transV = VividComputeVerticalLayerViewTransmission(
                closures[fgIdx],
                closureAlbedos[fgIdx],
                viewDirectionWS,
                op.opticalThickness,
                op.transmittanceColor);

            float weightedIor = openpbr_apply_specular_weight_to_ior(
                closures[fgIdx].core.specularIor,
                closures[fgIdx].core.specularWeight);
            float fOutDiff = openpbr_fresnel(weightedIor, avgDiffCos);
            float3 transOutDiff = saturate(1.0f - fOutDiff)
                * VividComputeSlabBeerLambertTransmittance(op.transmittanceColor, op.opticalThickness, avgDiffCos);

            float3 bgViewScale = lerp(1.0f.xxx, transV, w);
            float3 bgDiffScale = lerp(1.0f.xxx, transV * transOutDiff, w);

            [unroll]
            for (uint prev = 0u; prev < VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL; ++prev)
            {
                if (prev < fgIdx)
                {
                    float3 reflPrev = reflect(-viewDirectionWS, closures[prev].core.normalWS);
                    float muSpec = lerp(
                        saturate(dot(reflPrev, closures[fgIdx].core.normalWS)),
                        avgDiffCos,
                        closures[prev].core.specularRoughness * closures[prev].core.specularRoughness);
                    muSpec = max(muSpec, 0.05f);
                    float fOutSpec = openpbr_fresnel(weightedIor, muSpec);
                    float3 transOutSpec = saturate(1.0f - fOutSpec)
                        * VividComputeSlabBeerLambertTransmittance(op.transmittanceColor, op.opticalThickness, muSpec);
                    float3 bgSpecScale = lerp(1.0f.xxx, transV * transOutSpec, w);

                    viewWeights[prev] *= bgViewScale;
                    indirectDiffWeights[prev] *= bgDiffScale;
                    indirectSpecWeights[prev] *= bgSpecScale;
                }
            }
            viewWeights[fgIdx] = w.xxx;
            indirectDiffWeights[fgIdx] = w.xxx;
            indirectSpecWeights[fgIdx] = w.xxx;
        }
        else // VIVID_SUBSTRATE_TOPOLOGY_HORIZONTAL_MIX
        {
            float bgScale = 1.0f - w;
            [unroll]
            for (uint prev = 0u; prev < VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL; ++prev)
            {
                if (prev < fgIdx)
                {
                    viewWeights[prev] *= bgScale;
                    indirectDiffWeights[prev] *= bgScale;
                    indirectSpecWeights[prev] *= bgScale;
                }
            }
            viewWeights[fgIdx] = w.xxx;
            indirectDiffWeights[fgIdx] = w.xxx;
            indirectSpecWeights[fgIdx] = w.xxx;
        }
    }
}

// Evaluates per-closure direct-light transmission weights for a specific light direction L,
// folding view-side transmission (precomputed in viewWeights) with light-side outgoing
// Fresnel + Beer-Lambert passage through every VerticalLayer above each closure.
void VividComputeMultiClosureDirectWeights(
    uint closureCount,
    VividOpenPBRV2Inputs closures[VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL],
    VividSubstrateOperatorDesc operators[VIVID_SUBSTRATE_MAX_OPERATORS_PER_PIXEL],
    float3 viewWeights[VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL],
    float3 lightDirectionWS,
    out float3 directWeights[VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL])
{
    [unroll]
    for (uint i = 0u; i < VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL; ++i)
        directWeights[i] = viewWeights[i];

    uint opCount = closureCount > 1u ? (closureCount - 1u) : 0u;
    [unroll]
    for (uint opIdx = 0u; opIdx < VIVID_SUBSTRATE_MAX_OPERATORS_PER_PIXEL; ++opIdx)
    {
        if (opIdx >= opCount)
            break;

        VividSubstrateOperatorDesc op = operators[opIdx];
        if (op.opKind != VIVID_SUBSTRATE_TOPOLOGY_VERTICAL_LAYER)
            continue;

        uint fgIdx = opIdx + 1u;
        float w = saturate(op.weight);
        float3 transL = VividComputeVerticalLayerLightTransmission(
            closures[fgIdx],
            lightDirectionWS,
            op.opticalThickness,
            op.transmittanceColor);
        float3 bgLightScale = lerp(1.0f.xxx, transL, w);

        [unroll]
        for (uint prev = 0u; prev < VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL; ++prev)
        {
            if (prev < fgIdx)
                directWeights[prev] *= bgLightScale;
        }
    }
}

VividSurfaceSummaryData VividBuildMultiClosureTopLayerSummary(
    uint closureCount,
    VividOpenPBRV2Inputs closures[VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL],
    VividSubstrateOperatorDesc operators[VIVID_SUBSTRATE_MAX_OPERATORS_PER_PIXEL])
{
    VividSurfaceSummaryData summary = VividBuildOpenPBRV2TopLayerSummary(closures[0]);
    uint opCount = closureCount > 1u ? (closureCount - 1u) : 0u;
    [unroll]
    for (uint opIdx = 0u; opIdx < VIVID_SUBSTRATE_MAX_OPERATORS_PER_PIXEL; ++opIdx)
    {
        if (opIdx >= opCount)
            break;
        VividSurfaceSummaryData fg = VividBuildOpenPBRV2TopLayerSummary(closures[opIdx + 1u]);
        float w = saturate(operators[opIdx].weight);
        if (operators[opIdx].opKind == VIVID_SUBSTRATE_TOPOLOGY_VERTICAL_LAYER)
        {
            float topSpecularDom = saturate(w * (1.0f - fg.perceptualRoughness * 0.5f));
            summary.diffuseAlbedo = lerp(summary.diffuseAlbedo, fg.diffuseAlbedo, w * saturate(1.0f - closures[opIdx + 1u].core.baseMetalness));
            summary.specularF0 = lerp(summary.specularF0, fg.specularF0, topSpecularDom);
            summary.perceptualRoughness = lerp(summary.perceptualRoughness, min(summary.perceptualRoughness, fg.perceptualRoughness), w);
            summary.normalWS = VividSanitizeSurfaceSummaryNormal(lerp(summary.normalWS, fg.normalWS, w));
        }
        else
        {
            summary.diffuseAlbedo = lerp(summary.diffuseAlbedo, fg.diffuseAlbedo, w);
            summary.specularF0 = lerp(summary.specularF0, fg.specularF0, w);
            summary.perceptualRoughness = lerp(summary.perceptualRoughness, fg.perceptualRoughness, w);
            summary.normalWS = VividSanitizeSurfaceSummaryNormal(lerp(summary.normalWS, fg.normalWS, w));
        }
    }
    return summary;
}

#endif // VIVIDRP_OPENPBR_V2_INCLUDED
