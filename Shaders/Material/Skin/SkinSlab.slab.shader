// @vivid-base "Packages/com.vivid.render-pipelines/Shaders/Material/Templates/SubstrateOpaque.slab.template"
// @vivid-graph "Packages/com.vivid.render-pipelines/Runtime/Resources/MaterialGraphs/OpenPBROpaque.vmatg"

// Physically-Based Human Skin / Subsurface Material (OpenPBR V2 + UE5 Substrate Inline MFP SSS):
//   - Core Specular: Broad epidermal micro-surface lobe (IOR ~ 1.40, roughness ~ 0.46)
//   - Coat Specular: Sharp oily sebum / T-zone lobe (coatIor = 1.40, coatDarkening = 0.0, roughness ~ 0.16)
//   - Fuzz / Sheen:  Soft warm vellus hair ("peach fuzz") grazing rim sheen
//   - Subsurface:    2-uint inline OpenPBR RGB Mean Free Path (Burley Normalized Diffusion)
//   - Micro-detail:  Procedural micro-pores + subtle complexion/freckle variation testing sqrt(albedo) preservation
//   - Dual-target:   Compiles cleanly into both Raster GBuffer and GPUDriven VisibilityBuffer Compute.
Shader "VividRP/Material/Skin/SkinSlab"
{
    Properties
    {
        VIVID_SLAB_GRAPH_PROPERTIES
        _SkinColor ("Skin Base Color", Color) = (0.76, 0.56, 0.45, 1.0)
        _SkinRoughness ("Epidermis Roughness", Range(0.05, 1.0)) = 0.46
        _SkinIor ("Skin IOR", Range(1.1, 2.0)) = 1.40
        _SebumWeight ("Sebum (Oil) Lobe Weight", Range(0.0, 1.0)) = 0.24
        _SebumRoughness ("Sebum Roughness", Range(0.04, 0.5)) = 0.16
        _FuzzWeight ("Peach Fuzz Weight", Range(0.0, 1.0)) = 0.22
        _FuzzRoughness ("Peach Fuzz Roughness", Range(0.1, 1.0)) = 0.68
        _FuzzColor ("Peach Fuzz Color", Color) = (0.95, 0.84, 0.74, 1.0)
        _SubsurfaceWeight ("Subsurface Weight", Range(0.0, 1.0)) = 1.0
        _SubsurfaceColor ("Subsurface Scatter Color", Color) = (0.82, 0.52, 0.40, 1.0)
        _SubsurfaceRadiusMm ("Subsurface Radius (mm)", Range(0.1, 50.0)) = 4.8
        _SubsurfaceRadiusScale ("Subsurface RGB Scale", Vector) = (1.0, 0.35, 0.19, 0.0)
        _SubsurfaceTransmission ("Thin Transmission Scale", Range(0.0, 1.0)) = 0.35
        _PoreScale ("Micro-Pore Scale", Range(4.0, 160.0)) = 56.0
        _PoreNormalStrength ("Micro-Pore Normal Strength", Range(0.0, 1.0)) = 0.14
        _ComplexionVariation ("Complexion / Freckle Detail", Range(0.0, 0.5)) = 0.08
    }

    // @vivid-cbuffer-begin
    float4 _SkinColor;
    float _SkinRoughness;
    float _SkinIor;
    float _SebumWeight;
    float _SebumRoughness;
    float _FuzzWeight;
    float _FuzzRoughness;
    float4 _FuzzColor;
    float _SubsurfaceWeight;
    float4 _SubsurfaceColor;
    float _SubsurfaceRadiusMm;
    float4 _SubsurfaceRadiusScale;
    float _SubsurfaceTransmission;
    float _PoreScale;
    float _PoreNormalStrength;
    float _ComplexionVariation;
    // @vivid-cbuffer-end

    // @vivid-surface-begin
    #define VIVID_SLAB_HAS_CUSTOM_POST_SURFACE 1

    float VividSkinHash21(float2 p)
    {
        p = frac(p * float2(123.34f, 456.21f));
        p += dot(p, p + 45.32f);
        return frac(p.x * p.y);
    }

    float VividSkinValueNoise(float2 p)
    {
        float2 i = floor(p);
        float2 f = frac(p);
        f = f * f * (3.0f - 2.0f * f);
        float a = VividSkinHash21(i);
        float b = VividSkinHash21(i + float2(1.0f, 0.0f));
        float c = VividSkinHash21(i + float2(0.0f, 1.0f));
        float d = VividSkinHash21(i + float2(1.0f, 1.0f));
        return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
    }

    void VividSlabPostEvaluateSurface(
        const VividSlabEvalContext ctx,
        inout VividOpenPBRV2Inputs v2,
        inout float ambientOcclusion)
    {
        float3 skinColor = saturate(VIVID_SLAB_PARAM(ctx, _SkinColor).rgb);
        float skinRoughness = clamp(VIVID_SLAB_PARAM(ctx, _SkinRoughness), 0.05f, 1.0f);
        float skinIor = clamp(VIVID_SLAB_PARAM(ctx, _SkinIor), 1.1f, 2.0f);
        float sebumWeight = saturate(VIVID_SLAB_PARAM(ctx, _SebumWeight));
        float sebumRoughness = clamp(VIVID_SLAB_PARAM(ctx, _SebumRoughness), 0.04f, 0.5f);
        float fuzzWeight = saturate(VIVID_SLAB_PARAM(ctx, _FuzzWeight));
        float fuzzRoughness = clamp(VIVID_SLAB_PARAM(ctx, _FuzzRoughness), 0.1f, 1.0f);
        float3 fuzzColor = saturate(VIVID_SLAB_PARAM(ctx, _FuzzColor).rgb);
        float sssWeight = saturate(VIVID_SLAB_PARAM(ctx, _SubsurfaceWeight));
        float3 sssColor = saturate(VIVID_SLAB_PARAM(ctx, _SubsurfaceColor).rgb);
        float sssRadiusMm = clamp(VIVID_SLAB_PARAM(ctx, _SubsurfaceRadiusMm), 0.1f, 50.0f);
        float3 sssRadiusScale = max(VIVID_SLAB_PARAM(ctx, _SubsurfaceRadiusScale).rgb, 0.01f.xxx);
        float sssTrans = saturate(VIVID_SLAB_PARAM(ctx, _SubsurfaceTransmission));
        float poreScale = max(VIVID_SLAB_PARAM(ctx, _PoreScale), 1.0f);
        float poreStrength = saturate(VIVID_SLAB_PARAM(ctx, _PoreNormalStrength));
        float complexionVar = saturate(VIVID_SLAB_PARAM(ctx, _ComplexionVariation));

        // 2-octave procedural micro-pore cellular dimples + subtle skin complexion/freckle variation
        float2 uv = ctx.uv0 * float2(2.0f, 1.0f);
        float2 poreUv = uv * poreScale;
        float n0 = VividSkinValueNoise(poreUv);
        float nX = VividSkinValueNoise(poreUv + float2(0.15f, 0.0f));
        float nY = VividSkinValueNoise(poreUv + float2(0.0f, 0.15f));
        float fineN = VividSkinValueNoise(poreUv * 2.7f + 11.3f);
        float macroMottle = VividSkinValueNoise(uv * 9.0f + 3.7f);
        float freckleNoise = smoothstep(0.76f, 0.95f, VividSkinValueNoise(uv * 34.0f + 7.1f));

        // Subtle high-frequency melanin & hemoglobin mottling (tests sqrt(albedo) crisp detail preservation)
        float3 mottledColor = skinColor * lerp(1.0f - complexionVar, 1.0f + complexionVar * 0.6f, macroMottle);
        mottledColor = lerp(mottledColor, mottledColor * float3(0.62f, 0.44f, 0.34f), freckleNoise * complexionVar * 2.2f);
        mottledColor = saturate(mottledColor);

        // Tangent-space micro-pore normal perturbation
        float3 N = SafeNormalize(v2.core.normalWS);
        float3 T = SafeNormalize(ctx.geometryTangentWS.xyz - N * dot(ctx.geometryTangentWS.xyz, N));
        float3 B = SafeNormalize(cross(N, T) * (ctx.geometryTangentWS.w < 0.0f ? -1.0f : 1.0f));
        float dU = (nX - n0) + (fineN - 0.5f) * 0.35f;
        float dV = (nY - n0) - (fineN - 0.5f) * 0.35f;
        float3 perturbedN = SafeNormalize(N + (T * dU + B * dV) * (poreStrength * 2.4f));

        // OpenPBR V2 Core (broad epidermal lobe + diffuse)
        v2.core.baseWeight = 1.0f;
        v2.core.baseColor = mottledColor;
        v2.core.baseDiffuseRoughness = 0.25f;
        v2.core.baseMetalness = 0.0f;
        v2.core.specularWeight = 1.0f;
        v2.core.specularColor = 1.0f.xxx;
        v2.core.specularRoughness = clamp(skinRoughness + (n0 - 0.5f) * 0.08f, 0.05f, 1.0f);
        v2.core.specularIor = skinIor;
        v2.core.normalWS = perturbedN;

        // OpenPBR V2 Coat -> Sharp sebum / oil second specular lobe (coatDarkening = 0 so it never darkens skin!)
        v2.coatWeight = sebumWeight;
        v2.coatColor = 1.0f.xxx;
        v2.coatRoughness = sebumRoughness;
        v2.coatIor = skinIor;
        v2.coatDarkening = 0.0f;
        v2.coatNormalWS = perturbedN;
        v2.hasIndependentCoatNormal = 0u;

        // OpenPBR V2 Fuzz -> Vellus peach-fuzz sheen at grazing silhouettes
        v2.fuzzWeight = fuzzWeight;
        v2.fuzzColor = fuzzColor;
        v2.fuzzRoughness = fuzzRoughness;

        // OpenPBR V2 Subsurface -> 2-uint inline MFP (meters)
        v2.subsurfaceWeight = sssWeight;
        // Blend artist scatter tint with mottled albedo so high-frequency freckles remain crisp via sqrt demodulation
        v2.subsurfaceColor = saturate(lerp(mottledColor, sssColor * (mottledColor / max(skinColor, 0.05f.xxx)), 0.35f));
        v2.subsurfaceMfpMeters = (sssRadiusMm * 0.001f) * sssRadiusScale;
        v2.subsurfaceTransmissionScale = sssTrans;

        // Subtle pore micro-cavity AO
        ambientOcclusion *= lerp(1.0f - poreStrength * 0.25f, 1.0f, smoothstep(0.15f, 0.65f, n0));
    }
    // @vivid-surface-end

    VIVID_SLAB_USE_BASE_SUBSHADER
}
