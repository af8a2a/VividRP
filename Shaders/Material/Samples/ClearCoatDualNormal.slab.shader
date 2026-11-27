// @vivid-base "Packages/com.vivid.render-pipelines/Shaders/Material/Templates/SubstrateOpaque.slab.template"
// @vivid-graph "Packages/com.vivid.render-pipelines/Runtime/Resources/MaterialGraphs/OpenPBROpaque.vmatg"

// Sample: Full OpenPBR V2 ClearCoat with Independent Dual-Normal & Anisotropic Carbon Fiber Base.
// Demonstrates:
//   - v2.coatWeight, v2.coatRoughness, v2.coatIor, v2.coatColor, v2.coatDarkening
//   - Independent dual-normal: flat-tow 2x2 twill carbon-fiber base normal (v2.core.normalWS)
//     underneath an undistorted mirror-smooth clearcoat normal (v2.coatNormalWS, v2.hasIndependentCoatNormal = 1u)
//   - Orthogonal per-tow base anisotropy (v2.specularAnisotropy + v2.specularAnisotropyRotation)
//     contrasting cleanly against the isotropic glass-like clearcoat specular lobe
//   - Dual-target compilation: compiles into both Raster GBuffer and standalone GPUDriven Compute.
Shader "VividRP/Material/Samples/ClearCoatDualNormal"
{
    Properties
    {
        VIVID_SLAB_GRAPH_PROPERTIES
        _BaseColor ("Base Color", Color) = (0.18, 0.20, 0.23, 1.0)
        _BaseMetalness ("Base Metalness", Range(0.0, 1.0)) = 0.55
        _BaseRoughness ("Base Roughness", Range(0.04, 1.0)) = 0.32
        _BaseAnisotropy ("Base Fiber Anisotropy", Range(0.0, 1.0)) = 0.78
        _WeaveScale ("Carbon Weave Scale", Range(8.0, 160.0)) = 84.0
        _WeaveNormalStrength ("Base Weave Normal Strength", Range(0.0, 1.0)) = 0.22
        _CoatWeight ("Clear Coat Weight", Range(0.0, 1.0)) = 1.0
        _CoatRoughness ("Clear Coat Roughness", Range(0.0, 1.0)) = 0.02
        _CoatIor ("Clear Coat IOR", Range(1.0, 2.5)) = 1.65
        _CoatColor ("Clear Coat Tint", Color) = (1.0, 0.98, 0.96, 1.0)
        _CoatDarkening ("Clear Coat Darkening", Range(0.0, 1.0)) = 1.0
    }

    // @vivid-cbuffer-begin
    float4 _BaseColor;
    float _BaseMetalness;
    float _BaseRoughness;
    float _BaseAnisotropy;
    float _WeaveScale;
    float _WeaveNormalStrength;
    float _CoatWeight;
    float _CoatRoughness;
    float _CoatIor;
    float4 _CoatColor;
    float _CoatDarkening;
    // @vivid-cbuffer-end

    // @vivid-surface-begin
    // Custom OpenPBR V2 post-hook runs in both Raster GBuffer and GPUDriven Compute.
    #define VIVID_SLAB_HAS_CUSTOM_POST_SURFACE 1
    void VividSlabPostEvaluateSurface(
        const VividSlabEvalContext ctx,
        inout VividOpenPBRV2Inputs v2,
        inout float ambientOcclusion)
    {
        #if defined(VIVID_SLAB_TARGET_RASTER)
            float3 baseColor = saturate(_BaseColor.rgb);
            float baseMetalness = saturate(_BaseMetalness);
            float baseRoughness = clamp(_BaseRoughness, 0.04f, 1.0f);
            float baseAnisotropy = saturate(_BaseAnisotropy);
            float weaveScale = max(_WeaveScale, 1.0f);
            float weaveStrength = saturate(_WeaveNormalStrength);
            float coatWeight = saturate(_CoatWeight);
            float coatRoughness = saturate(_CoatRoughness);
            float coatIor = clamp(_CoatIor, 1.0f, 3.0f);
            float3 coatColor = saturate(_CoatColor.rgb);
            float coatDarkening = saturate(_CoatDarkening);
        #else
            float3 baseColor = float3(0.18f, 0.20f, 0.23f);
            float baseMetalness = 0.55f;
            float baseRoughness = 0.32f;
            float baseAnisotropy = 0.78f;
            float weaveScale = 84.0f;
            float weaveStrength = 0.22f;
            float coatWeight = 1.0f;
            float coatRoughness = 0.02f;
            float coatIor = 1.65f;
            float3 coatColor = float3(1.0f, 0.98f, 0.96f);
            float coatDarkening = 1.0f;
        #endif

        // Tightly-woven 2x2 diagonal twill carbon-fiber ribbons (flat-topped tows + subtle micro-filaments).
        // Scale U by 2x to match sphere UV aspect ratio (2*pi azimuth vs pi elevation).
        float2 uvScale = float2(2.0f, 1.0f) * weaveScale;
        float2 grid = ctx.uv0 * uvScale;
        int2 cell = (int2)floor(grid);
        float2 localUv = frac(grid);

        int phase = ((cell.x + cell.y) % 4 + 4) % 4;
        bool isWarp = phase < 2;
        float along = isWarp
            ? (localUv.x + (float)phase) * 0.5f
            : (localUv.y + (float)(phase - 2)) * 0.5f;
        float across = isWarp ? localUv.y : localUv.x;

        // Flat-topped ribbon profile: gentle camber along tow, flat crown with narrow tucked seam across tow.
        float sinAlong = sin(along * PI);
        float cosAlong = cos(along * PI);
        float alongProfile = smoothstep(0.0f, 0.24f, along) * smoothstep(1.0f, 0.76f, along);
        float acrossRibbon = smoothstep(0.0f, 0.16f, across) * smoothstep(1.0f, 0.84f, across);
        float seamGroove = saturate(alongProfile * acrossRibbon);

        // Gentle longitudinal camber + crisp narrow bevel at tow seams + fine parallel micro-filaments.
        float slopeAlong = 0.45f * cosAlong * acrossRibbon;
        float edgeBevel = (smoothstep(0.18f, 0.0f, across) - smoothstep(0.82f, 1.0f, across)) * sinAlong * 0.65f;
        float microStrand = cos(across * 6.0f * TWO_PI) * 0.10f * seamGroove;
        float slopeAcross = edgeBevel + microStrand;

        float2 slopeTS = isWarp
            ? float2(-slopeAlong, -slopeAcross)
            : float2(-slopeAcross, -slopeAlong);

        // Derivative-based anti-aliasing prevents moire/shimmer at grazing angles or far distance.
        float filterWidth = max(length(ctx.uv0Ddx * uvScale), length(ctx.uv0Ddy * uvScale));
        float aaAtten = 1.0f - smoothstep(0.35f, 1.35f, filterWidth);

        float effectiveStrength = weaveStrength * aaAtten;
        float strandTint = 1.0f + sin(across * 6.0f * TWO_PI) * 0.04f;
        float weaveBrightness = lerp(1.0f, lerp(0.55f, 1.08f, seamGroove) * strandTint, aaAtten);
        float weaveRoughMod = lerp(0.0f, lerp(0.12f, -0.04f, seamGroove), aaAtten);

        float3 smoothNormalWS = SafeNormalize(ctx.geometryNormalWS);
        float3 baseNormalWS = smoothNormalWS;
        float3 tangentWS = ctx.geometryTangentWS.xyz - smoothNormalWS * dot(ctx.geometryTangentWS.xyz, smoothNormalWS);
        float tangentLenSq = dot(tangentWS, tangentWS);
        float tangentSign = ctx.geometryTangentWS.w < 0.0f ? -1.0f : 1.0f;
        if (tangentLenSq > 1e-8f)
        {
            tangentWS *= rsqrt(tangentLenSq);
            if (effectiveStrength > 1e-4f)
            {
                float3 bitangentWS = cross(smoothNormalWS, tangentWS) * tangentSign;
                float3 normalTS = SafeNormalize(float3(slopeTS * effectiveStrength, 1.0f));
                baseNormalWS = SafeNormalize(
                    normalTS.x * tangentWS
                    + normalTS.y * bitangentWS
                    + normalTS.z * smoothNormalWS);
            }
        }
        else
        {
            tangentWS = float3(1.0f, 0.0f, 0.0f);
        }

        // Underlying anisotropic woven carbon-fiber base layer.
        v2.core.baseColor = saturate(baseColor * weaveBrightness);
        v2.core.baseMetalness = baseMetalness;
        v2.core.specularRoughness = clamp(baseRoughness + weaveRoughMod, 0.04f, 1.0f);
        v2.core.specularIor = 1.5f;
        v2.core.normalWS = baseNormalWS;
        v2.specularAnisotropy = baseAnisotropy * aaAtten;
        v2.specularAnisotropyRotation = isWarp ? 0.0f : (PI * 0.5f);
        v2.tangentWS = tangentWS;
        v2.tangentHandedness = tangentSign;
        ambientOcclusion = lerp(ambientOcclusion, ambientOcclusion * lerp(0.72f, 1.0f, seamGroove), aaAtten);

        // Top glossy automotive clearcoat with an independent smooth geometric normal.
        v2.coatWeight = coatWeight;
        v2.coatRoughness = coatRoughness;
        v2.coatIor = coatIor;
        v2.coatColor = coatColor;
        v2.coatDarkening = coatDarkening;
        v2.coatNormalWS = smoothNormalWS;
        v2.hasIndependentCoatNormal = (coatWeight > 0.0f && weaveStrength > 1e-4f) ? 1u : 0u;
    }
    // @vivid-surface-end

    VIVID_SLAB_USE_BASE_SUBSHADER
}
