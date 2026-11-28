// @vivid-base "Packages/com.vivid.render-pipelines/Shaders/Material/Templates/SubstrateOpaque.slab.template"

// Physically-Based Human Skin / Subsurface Material (OpenPBR V2 + UE5 Substrate Inline MFP SSS):
//   - Scanned Texture Support: _BaseMap (albedo), _BumpMap (tangent normal), _SpecMap (oil/roughness mask)
//   - Core Specular: Broad epidermal micro-surface lobe (IOR ~ 1.40, roughness ~ 0.46)
//   - Coat Specular: Sharp oily sebum / T-zone lobe (coatIor = 1.40, coatDarkening = 0.0, roughness ~ 0.16)
//   - Fuzz / Sheen:  Soft warm vellus hair ("peach fuzz") grazing rim sheen
//   - Subsurface:    2-uint inline OpenPBR RGB Mean Free Path (Burley Normalized Diffusion)
//   - Micro-detail:  Optional procedural micro-pores + complexion variation (set strength = 0 when using scan maps)
//   - Dual-target:   Compiles cleanly into both Raster GBuffer and GPUDriven VisibilityBuffer Compute.
Shader "VividRP/Material/Skin/SkinSlab"
{
    Properties
    {
        [Enum(Back, 2, Front, 1, Off, 0)] _Cull ("Cull Mode", Float) = 2.0
        _BaseMap ("Skin Albedo Map", 2D) = "white" {}
        _BaseMap_ST ("Base UV Tiling & Offset", Vector) = (1, 1, 0, 0)
        _SkinColor ("Skin Base Color Tint", Color) = (0.76, 0.56, 0.45, 1.0)
        [Normal] _BumpMap ("Skin Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0.0, 2.0)) = 1.0
        _SpecMap ("Specular / Sebum Mask (R)", 2D) = "white" {}
        _SpecMapStrength ("Spec Map Influence", Range(0.0, 1.0)) = 0.0

        _SkinRoughness ("Epidermis Roughness", Range(0.05, 1.0)) = 0.46
        _SkinIor ("Skin IOR", Range(1.1, 2.0)) = 1.40
        _SebumWeight ("Sebum (Oil) Lobe Weight", Range(0.0, 1.0)) = 0.24
        _SebumRoughness ("Sebum Roughness", Range(0.04, 0.5)) = 0.16
        _FuzzWeight ("Peach Fuzz Weight", Range(0.0, 1.0)) = 0.22
        _FuzzRoughness ("Peach Fuzz Roughness", Range(0.1, 1.0)) = 0.68
        _FuzzColor ("Peach Fuzz Color", Color) = (0.95, 0.84, 0.74, 1.0)

        _SubsurfaceWeight ("Subsurface Weight", Range(0.0, 1.0)) = 1.0
        _SubsurfaceColor ("Subsurface Scatter Tint", Color) = (0.92, 0.60, 0.48, 1.0)
        _SubsurfaceColorBlend ("Subsurface Tint Blend", Range(0.0, 1.0)) = 0.30
        _SubsurfaceRadiusMm ("Subsurface Radius (mm)", Range(0.1, 50.0)) = 4.8
        _SubsurfaceRadiusScale ("Subsurface RGB Scale", Vector) = (1.0, 0.35, 0.19, 0.0)
        _SubsurfaceTransmission ("Thin Transmission Scale", Range(0.0, 1.0)) = 0.35

        _PoreScale ("Procedural Micro-Pore Scale", Range(4.0, 160.0)) = 56.0
        _PoreNormalStrength ("Procedural Micro-Pore Strength", Range(0.0, 1.0)) = 0.14
        _ComplexionVariation ("Procedural Complexion / Freckle Detail", Range(0.0, 0.5)) = 0.08
    }

    // @vivid-cbuffer-begin
    float4 _BaseMap_ST;
    float4 _SkinColor;
    float _BumpScale;
    float _SpecMapStrength;
    float _SkinRoughness;
    float _SkinIor;
    float _SebumWeight;
    float _SebumRoughness;
    float _FuzzWeight;
    float _FuzzRoughness;
    float4 _FuzzColor;
    float _SubsurfaceWeight;
    float4 _SubsurfaceColor;
    float _SubsurfaceColorBlend;
    float _SubsurfaceRadiusMm;
    float4 _SubsurfaceRadiusScale;
    float _SubsurfaceTransmission;
    float _PoreScale;
    float _PoreNormalStrength;
    float _ComplexionVariation;
    // @vivid-cbuffer-end

    // @vivid-textures-begin
    TEXTURE2D(_BaseMap);
    SAMPLER(sampler_BaseMap);
    TEXTURE2D(_BumpMap);
    SAMPLER(sampler_BumpMap);
    TEXTURE2D(_SpecMap);
    SAMPLER(sampler_SpecMap);
    // @vivid-textures-end

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
        float4 baseMapST = VIVID_SLAB_PARAM(ctx, _BaseMap_ST);
        float2 uv = ctx.uv0 * baseMapST.xy + baseMapST.zw;

        float3 baseMapRgb = VIVID_SLAB_SAMPLE_TEX2D(ctx, _BaseMap, uv).rgb;
        float4 packedNormal = VIVID_SLAB_SAMPLE_TEX2D(ctx, _BumpMap, uv);
        float specSample = VIVID_SLAB_SAMPLE_TEX2D(ctx, _SpecMap, uv).r;

        float3 skinTint = saturate(VIVID_SLAB_PARAM(ctx, _SkinColor).rgb);
        float bumpScale = clamp(VIVID_SLAB_PARAM(ctx, _BumpScale), 0.0f, 2.0f);
        float specInfluence = saturate(VIVID_SLAB_PARAM(ctx, _SpecMapStrength));
        float skinRoughness = clamp(VIVID_SLAB_PARAM(ctx, _SkinRoughness), 0.05f, 1.0f);
        float skinIor = clamp(VIVID_SLAB_PARAM(ctx, _SkinIor), 1.1f, 2.0f);
        float sebumWeight = saturate(VIVID_SLAB_PARAM(ctx, _SebumWeight));
        float sebumRoughness = clamp(VIVID_SLAB_PARAM(ctx, _SebumRoughness), 0.04f, 0.5f);
        float fuzzWeight = saturate(VIVID_SLAB_PARAM(ctx, _FuzzWeight));
        float fuzzRoughness = clamp(VIVID_SLAB_PARAM(ctx, _FuzzRoughness), 0.1f, 1.0f);
        float3 fuzzColor = saturate(VIVID_SLAB_PARAM(ctx, _FuzzColor).rgb);
        float sssWeight = saturate(VIVID_SLAB_PARAM(ctx, _SubsurfaceWeight));
        float3 sssTint = saturate(VIVID_SLAB_PARAM(ctx, _SubsurfaceColor).rgb);
        float sssTintBlend = saturate(VIVID_SLAB_PARAM(ctx, _SubsurfaceColorBlend));
        float sssRadiusMm = clamp(VIVID_SLAB_PARAM(ctx, _SubsurfaceRadiusMm), 0.1f, 50.0f);
        float3 sssRadiusScale = max(VIVID_SLAB_PARAM(ctx, _SubsurfaceRadiusScale).rgb, 0.01f.xxx);
        float sssTrans = saturate(VIVID_SLAB_PARAM(ctx, _SubsurfaceTransmission));
        float poreScale = max(VIVID_SLAB_PARAM(ctx, _PoreScale), 1.0f);
        float poreStrength = saturate(VIVID_SLAB_PARAM(ctx, _PoreNormalStrength));
        float complexionVar = saturate(VIVID_SLAB_PARAM(ctx, _ComplexionVariation));

        float3 skinAlbedo = saturate(baseMapRgb * skinTint);

        // Optional procedural micro-pore & freckle variation (zeroed automatically when strengths are 0)
        float n0 = 0.5f;
        float2 procGrad = 0.0f.xx;
        if (poreStrength > 1e-4f || complexionVar > 1e-4f)
        {
            float2 procUv = ctx.uv0 * float2(2.0f, 1.0f);
            float2 poreUv = procUv * poreScale;
            n0 = VividSkinValueNoise(poreUv);
            if (poreStrength > 1e-4f)
            {
                float nX = VividSkinValueNoise(poreUv + float2(0.15f, 0.0f));
                float nY = VividSkinValueNoise(poreUv + float2(0.0f, 0.15f));
                float fineN = VividSkinValueNoise(poreUv * 2.7f + 11.3f);
                procGrad = float2(
                    (nX - n0) + (fineN - 0.5f) * 0.35f,
                    (nY - n0) - (fineN - 0.5f) * 0.35f) * (poreStrength * 2.4f);
                ambientOcclusion *= lerp(1.0f - poreStrength * 0.25f, 1.0f, smoothstep(0.15f, 0.65f, n0));
            }
            if (complexionVar > 1e-4f)
            {
                float macroMottle = VividSkinValueNoise(procUv * 9.0f + 3.7f);
                float freckleNoise = smoothstep(0.76f, 0.95f, VividSkinValueNoise(procUv * 34.0f + 7.1f));
                skinAlbedo *= lerp(1.0f - complexionVar, 1.0f + complexionVar * 0.6f, macroMottle);
                skinAlbedo = lerp(skinAlbedo, skinAlbedo * float3(0.62f, 0.44f, 0.34f), freckleNoise * complexionVar * 2.2f);
                skinAlbedo = saturate(skinAlbedo);
            }
        }

        // Tangent-space normal composition: scanned normal map + optional procedural micro-pores
        float3 geomN = SafeNormalize(ctx.geometryNormalWS);
        float3 tangentProj = ctx.geometryTangentWS.xyz - geomN * dot(ctx.geometryTangentWS.xyz, geomN);
        float tangentLenSq = dot(tangentProj, tangentProj);
        float tangentSign = ctx.geometryTangentWS.w < 0.0f ? -1.0f : 1.0f;
        float3 T = tangentLenSq > 1e-8f ? tangentProj * rsqrt(tangentLenSq) : float3(1.0f, 0.0f, 0.0f);
        float3 B = cross(geomN, T) * tangentSign;

        float3 normalTS = UnpackNormalScale(packedNormal, bumpScale);
        normalTS.xy += procGrad;
        normalTS = SafeNormalize(normalTS);
        float3 worldN = SafeNormalize(T * normalTS.x + B * normalTS.y + geomN * normalTS.z);

        // Specular/sebum mask modulation (high specSample on nose/forehead/lips -> stronger oil lobe & slightly smoother skin)
        float oilMask = lerp(1.0f, saturate(specSample * 1.45f), specInfluence);
        float effectiveRoughness = clamp(
            skinRoughness + (n0 - 0.5f) * (0.08f * step(1e-4f, poreStrength)) - (specSample - 0.5f) * (0.18f * specInfluence),
            0.05f,
            1.0f);
        float effectiveSebumWeight = saturate(sebumWeight * oilMask);
        float effectiveSebumRoughness = clamp(
            sebumRoughness - (specSample - 0.5f) * (0.06f * specInfluence),
            0.04f,
            0.5f);

        // OpenPBR V2 Core (broad epidermal lobe + diffuse)
        v2.core.baseWeight = 1.0f;
        v2.core.baseColor = skinAlbedo;
        v2.core.baseDiffuseRoughness = 0.25f;
        v2.core.baseMetalness = 0.0f;
        v2.core.specularWeight = lerp(1.0f, saturate(0.75f + 0.50f * specSample), specInfluence);
        v2.core.specularColor = 1.0f.xxx;
        v2.core.specularRoughness = effectiveRoughness;
        v2.core.specularIor = skinIor;
        v2.core.normalWS = worldN;

        // OpenPBR V2 Coat -> Sharp sebum / oil second specular lobe (coatDarkening = 0 so it never darkens skin!)
        v2.coatWeight = effectiveSebumWeight;
        v2.coatColor = 1.0f.xxx;
        v2.coatRoughness = effectiveSebumRoughness;
        v2.coatIor = skinIor;
        v2.coatDarkening = 0.0f;
        v2.coatNormalWS = worldN;
        v2.hasIndependentCoatNormal = 0u;

        // OpenPBR V2 Fuzz -> Vellus peach-fuzz sheen at grazing silhouettes
        v2.fuzzWeight = fuzzWeight;
        v2.fuzzColor = fuzzColor;
        v2.fuzzRoughness = fuzzRoughness;

        // OpenPBR V2 Subsurface -> 2-uint inline MFP (meters)
        v2.subsurfaceWeight = sssWeight;
        // Blend albedo with subsurface tint so high-frequency pores/brows/freckles stay crisp via sqrt(albedo) remodulation
        v2.subsurfaceColor = saturate(lerp(skinAlbedo, skinAlbedo * sssTint, sssTintBlend));
        v2.subsurfaceMfpMeters = (sssRadiusMm * 0.001f) * sssRadiusScale;
        v2.subsurfaceTransmissionScale = sssTrans;
    }
    // @vivid-surface-end

    VIVID_SLAB_USE_BASE_SUBSHADER
}
