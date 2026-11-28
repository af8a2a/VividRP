// @vivid-base "Packages/com.vivid.render-pipelines/Shaders/Material/Templates/SubstrateOpaque.slab.template"
// @vivid-wpo-padding _FurLength + _GravityStrength + _WindStrength + 0.02
// @vivid-shell-layers 8
// @vivid-shadow-shell-layers 2
//
// VividRP Shell-Based Fur Material (.slab.shader, Phase 1 Raster Vertical Slice).
// Driven via DrawMeshInstanced / RenderMeshInstanced with per-instance _ShellHeight in [0, 1]:
//   - @vivid-vertex-begin..end:   Shell normal extrusion (h * _FurLength) + quadratic gravity bend
//                                 (h^2 * _GravityWS) + optional wind sway with exact previous-frame WPO
//                                 for artifact-free MotionVectors & TAA.
//   - @vivid-coverage-begin..end: Procedural tapered strand lattice + spatio-temporal Interleaved Gradient
//                                 Noise dithering (base skin h == 0 is never clipped). Uses PreDepth
//                                 Early-Z in VividGBuffer to eliminate inter-shell UAV races and cut overdraw.
//   - @vivid-surface-begin..end:  Root-to-tip coloring & volumetric AO + OpenPBR V2 Fuzz (Disney LTC Sheen)
//                                 + gravity/comb-aligned Keratin Anisotropic Specular.
Shader "VividRP/Material/FurSlab"
{
    Properties
    {
        [Main(SurfaceOptions, _, on, off)] _SurfaceOptions("Surface Options", Float) = 1
        [SubEnum(SurfaceOptions, Back, 2, Front, 1, Off, 0)] _Cull("Cull", Float) = 2.0
        [SubToggle(SurfaceOptions, _)] _ReceiveShadows("Receive Shadows", Float) = 1.0
        [SubToggle(SurfaceOptions, _)] _ReceiveSSR("Receive SSR", Float) = 0.0
        [SubToggle(SurfaceOptions, _)] _ReceiveDecals("Receive Decals", Float) = 0.0

        [Main(FurGeometry, _, on, off)] _FurGeometry("Fur Shells & Motion", Float) = 1
        [Sub(FurGeometry)] _FurLength("Fur Length", Range(0.0, 0.25)) = 0.055
        [Sub(FurGeometry)] _GravityWS("Gravity Vector (WS)", Vector) = (0.0, -1.0, 0.0, 0.0)
        [Sub(FurGeometry)] _GravityStrength("Gravity Bend Strength", Range(0.0, 2.0)) = 0.45
        [Sub(FurGeometry)] _CombDirWS("Comb Flow Direction (WS)", Vector) = (0.0, -1.0, 0.0, 0.0)
        [Sub(FurGeometry)] _CombStrength("Comb Alignment", Range(0.0, 1.0)) = 0.85
        [Sub(FurGeometry)] _WindDirWS("Wind Direction (WS)", Vector) = (1.0, 0.0, 0.35, 0.0)
        [Sub(FurGeometry)] _WindStrength("Wind Sway Strength", Range(0.0, 1.0)) = 0.0
        [Sub(FurGeometry)] _WindFrequency("Wind Frequency", Range(0.1, 10.0)) = 2.2

        [Main(FurStrands, _, on, off)] _FurStrands("Strand Density & Dithering", Float) = 1
        [Tex(FurStrands)] _FurMaskMap("Fur Mask (R: Length/Density, G: AO)", 2D) = "white" {}
        [Sub(FurStrands)] _StrandTiling("Strand UV Aspect Tiling", Vector) = (2.0, 1.0, 0.0, 0.0)
        [Sub(FurStrands)] _FurDensity("Strand Density", Range(16.0, 320.0)) = 128.0
        [Sub(FurStrands)] _StrandThickness("Strand Thickness", Range(0.10, 0.65)) = 0.44
        [Sub(FurStrands)] _StrandTaper("Strand Tip Taper", Range(0.35, 2.5)) = 0.90
        [Sub(FurStrands)] _LengthVariance("Strand Length Variance", Range(0.0, 0.85)) = 0.45
        [Sub(FurStrands)] _ClumpStrength("Clump Strength", Range(0.0, 1.0)) = 0.35
        [Sub(FurStrands)] _AlphaCutoff("Base Alpha Cutoff", Range(0.01, 0.95)) = 0.35
        [Sub(FurStrands)] _DitherStrength("Tip Dither Blend (TAA)", Range(0.0, 1.0)) = 0.60
        [SubToggle(FurStrands, _)] _TemporalDither("Temporal IGN Jitter (TAA)", Float) = 1.0
        [SubToggle(FurStrands, _)] _HybridRaymarch("Hybrid Slab Raymarch (4-8 Shells)", Float) = 0.0
        [Sub(FurStrands)] _HybridSteps("Hybrid Raymarch Steps", Range(1.0, 8.0)) = 4.0
        [Sub(FurStrands)] _HybridMaxParallax("Hybrid Max Grazing Parallax", Range(1.0, 5.0)) = 3.0
        [Sub(FurStrands)] _GPUDrivenShellCount("GPUDriven Shell Count (1-8)", Range(1.0, 8.0)) = 8.0
        [Sub(FurStrands)] _ShellHeightPower("GPUDriven Shell Height Power", Range(0.5, 2.0)) = 1.0
        [SubToggle(FurStrands, _)] _GPUDrivenDistanceLod("GPUDriven Distance Shell LOD", Float) = 1.0
        [Sub(FurStrands)] _GPUDrivenLodNearDistance("GPUDriven LOD Near Distance", Float) = 4.0
        [Sub(FurStrands)] _GPUDrivenLodFarDistance("GPUDriven LOD Far Distance", Float) = 25.0
        [Sub(FurStrands)] _GPUDrivenMinLodShellCount("GPUDriven Min LOD Shells", Range(1.0, 8.0)) = 2.0
        [Sub(FurStrands)] _GPUDrivenShadowShellCount("GPUDriven Shadow Shells (1-4)", Range(1.0, 4.0)) = 2.0
        [Sub(FurStrands)] _GPUDrivenShadowMaxHeight("GPUDriven Shadow Max Height", Range(0.0, 1.0)) = 0.35

        [Main(FurShading, _, on, off)] _FurShading("Root-to-Tip & Substrate Shading", Float) = 1
        [Tex(FurShading)] _BaseMap("Base Albedo Map", 2D) = "white" {}
        [Sub(FurShading)] _BaseMap_ST("Base UV Tiling & Offset", Vector) = (1, 1, 0, 0)
        [Sub(FurShading)] _RootColor("Root Color", Color) = (0.14, 0.09, 0.06, 1.0)
        [Sub(FurShading)] _TipColor("Tip Color", Color) = (0.58, 0.42, 0.27, 1.0)
        [Sub(FurShading)] _ColorGradientPower("Root-to-Tip Gradient Power", Range(0.2, 3.0)) = 1.1
        [Sub(FurShading)] _StrandColorVariance("Per-Strand Color Variance", Range(0.0, 0.45)) = 0.16
        [Sub(FurShading)] _RootAO("Root Self-Occlusion (AO)", Range(0.02, 1.0)) = 0.16
        [Sub(FurShading)] _AOPower("AO Depth Power", Range(0.25, 3.0)) = 1.35
        [Sub(FurShading)] _StrandNormalStrength("Strand Micro-Normal Strength", Range(0.0, 0.75)) = 0.24
        [Sub(FurShading)] _RootRoughness("Root Specular Roughness", Range(0.15, 0.95)) = 0.50
        [Sub(FurShading)] _TipRoughness("Tip Specular Roughness", Range(0.15, 0.95)) = 0.34
        [Sub(FurShading)] _SpecularWeight("Keratin Specular Weight", Range(0.0, 1.0)) = 0.65
        [Sub(FurShading)] _SpecularTint("Keratin Specular Tint", Color) = (1.0, 0.94, 0.86, 1.0)
        [Sub(FurShading)] _Anisotropy("Strand Anisotropy", Range(0.0, 1.0)) = 0.72
        [Sub(FurShading)] _FuzzWeight("Fuzz Rim Sheen Weight", Range(0.0, 1.0)) = 0.85
        [Sub(FurShading)] _FuzzColor("Fuzz Rim Sheen Color", Color) = (0.94, 0.84, 0.72, 1.0)
        [Sub(FurShading)] _FuzzRoughness("Fuzz Rim Roughness", Range(0.15, 1.0)) = 0.46

        // Per-instance shell height h in [0, 1]; defaults to 0 (skin layer) when rendered non-instanced.
        [HideInInspector] _ShellHeight("Shell Height", Range(0.0, 1.0)) = 0.0
        [HideInInspector] _ShellStep("Shell Height Step", Range(0.001, 1.0)) = 0.0435
    }

    // @vivid-cbuffer-begin
    float _ReceiveShadows;
    float _ReceiveSSR;
    float _ReceiveDecals;
    float _FurLength;
    float4 _GravityWS;
    float _GravityStrength;
    float4 _CombDirWS;
    float _CombStrength;
    float4 _WindDirWS;
    float _WindStrength;
    float _WindFrequency;
    float4 _StrandTiling;
    float _FurDensity;
    float _StrandThickness;
    float _StrandTaper;
    float _LengthVariance;
    float _ClumpStrength;
    float _AlphaCutoff;
    float _DitherStrength;
    float _TemporalDither;
    float _HybridRaymarch;
    float _HybridSteps;
    float _HybridMaxParallax;
    float _GPUDrivenShellCount;
    float _ShellHeightPower;
    float _GPUDrivenDistanceLod;
    float _GPUDrivenLodNearDistance;
    float _GPUDrivenLodFarDistance;
    float _GPUDrivenMinLodShellCount;
    float _GPUDrivenShadowShellCount;
    float _GPUDrivenShadowMaxHeight;
    float4 _BaseMap_ST;
    float4 _RootColor;
    float4 _TipColor;
    float _ColorGradientPower;
    float _StrandColorVariance;
    float _RootAO;
    float _AOPower;
    float _StrandNormalStrength;
    float _RootRoughness;
    float _TipRoughness;
    float _SpecularWeight;
    float4 _SpecularTint;
    float _Anisotropy;
    float _FuzzWeight;
    float4 _FuzzColor;
    float _FuzzRoughness;
    // @vivid-cbuffer-end

    // @vivid-instancing-begin
    float _ShellStep;
    UNITY_INSTANCING_BUFFER_START(VividFurInstancedProps)
        UNITY_DEFINE_INSTANCED_PROP(float, _ShellHeight)
    UNITY_INSTANCING_BUFFER_END(VividFurInstancedProps)
    // @vivid-instancing-end

    // @vivid-textures-begin
    TEXTURE2D(_BaseMap);
    SAMPLER(sampler_BaseMap);
    TEXTURE2D(_FurMaskMap);
    SAMPLER(sampler_FurMaskMap);
    // @vivid-textures-end

    // @vivid-vertex-begin
    #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividFurSurface.hlsl"

    #define VIVID_SLAB_HAS_VERTEX_OFFSET 1
    #define VIVID_SLAB_HAS_PREV_VERTEX_OFFSET 1
    #define VIVID_SLAB_HAS_SHELL_PARAMS_HOOK 1

    bool VividSlabResolveShellLayer(
        const VividSlabVertexContext ctx,
        out float shellHeight,
        out float shellStep)
    {
        float heightPower = clamp(VIVID_SLAB_PARAM(ctx, _ShellHeightPower), 0.5f, 2.0f);
        #if defined(VIVIDRP_SHADERPASS_SHADOW_CASTER)
            uint targetCount = clamp(
                (uint)round(VIVID_SLAB_PARAM(ctx, _GPUDrivenShadowShellCount)),
                1u,
                max(ctx.shellCount, 1u));
            if (ctx.shellIndex >= targetCount)
            {
                shellHeight = 0.0f;
                shellStep = 1.0f;
                return false;
            }
            float maxShadowHeight = saturate(VIVID_SLAB_PARAM(ctx, _GPUDrivenShadowMaxHeight));
            shellHeight = VividComputeSlabShellHeight(ctx.shellIndex, targetCount, heightPower) * maxShadowHeight;
            shellStep = targetCount > 1u ? maxShadowHeight * rcp((float)(targetCount - 1u)) : maxShadowHeight;
            return true;
        #else
            uint baseCount = clamp(
                (uint)round(VIVID_SLAB_PARAM(ctx, _GPUDrivenShellCount)),
                1u,
                max(ctx.shellCount, 1u));
            bool enableLod = VIVID_SLAB_PARAM(ctx, _GPUDrivenDistanceLod) > 0.5f;
            float distToCamera = length(ctx.instancePositionWS - _WorldSpaceCameraPos);
            uint targetCount = VividComputeSlabLodShellCount(
                baseCount,
                enableLod,
                distToCamera,
                VIVID_SLAB_PARAM(ctx, _GPUDrivenLodNearDistance),
                VIVID_SLAB_PARAM(ctx, _GPUDrivenLodFarDistance),
                (uint)round(VIVID_SLAB_PARAM(ctx, _GPUDrivenMinLodShellCount)));
            if (ctx.shellIndex >= targetCount)
            {
                shellHeight = 0.0f;
                shellStep = 1.0f;
                return false;
            }
            shellHeight = VividComputeSlabShellHeight(ctx.shellIndex, targetCount, heightPower);
            shellStep = VividComputeSlabShellStep(targetCount);
            return true;
        #endif
    }

    float3 VividSlabEvaluateVertexOffset(const VividSlabVertexContext ctx)
    {
        #if defined(VIVID_SLAB_TARGET_RASTER)
            float h = saturate(UNITY_ACCESS_INSTANCED_PROP(VividFurInstancedProps, _ShellHeight));
        #else
            float h = saturate(ctx.shellHeight);
        #endif
        return VividEvaluateFurVertexOffset(
            ctx.positionWS,
            ctx.normalWS,
            h,
            VIVID_SLAB_PARAM(ctx, _FurLength),
            VIVID_SLAB_PARAM(ctx, _GravityWS).xyz,
            VIVID_SLAB_PARAM(ctx, _GravityStrength),
            VIVID_SLAB_PARAM(ctx, _WindDirWS).xyz,
            VIVID_SLAB_PARAM(ctx, _WindStrength),
            VIVID_SLAB_PARAM(ctx, _WindFrequency),
            _TimeParameters.x);
    }

    float3 VividSlabEvaluatePreviousVertexOffset(const VividSlabVertexContext ctx)
    {
        #if defined(VIVID_SLAB_TARGET_RASTER)
            float h = saturate(UNITY_ACCESS_INSTANCED_PROP(VividFurInstancedProps, _ShellHeight));
            return VividEvaluateFurVertexOffset(
                ctx.positionWS,
                ctx.normalWS,
                h,
                VIVID_SLAB_PARAM(ctx, _FurLength),
                VIVID_SLAB_PARAM(ctx, _GravityWS).xyz,
                VIVID_SLAB_PARAM(ctx, _GravityStrength),
                VIVID_SLAB_PARAM(ctx, _WindDirWS).xyz,
                VIVID_SLAB_PARAM(ctx, _WindStrength),
                VIVID_SLAB_PARAM(ctx, _WindFrequency),
                _LastTimeParameters.x);
        #else
            return VividSlabEvaluateVertexOffset(ctx);
        #endif
    }
    // @vivid-vertex-end

    // @vivid-coverage-begin
    #define VIVID_SLAB_HAS_CUSTOM_COVERAGE 1
    // PreDepth, ShadowCaster, and VisibilityRaster run clip(alpha - cutoff),
    // while VividGBuffer and MotionVectors rely on hardware [earlydepthstencil] + ZTest Equal
    // against PreDepth to eliminate overlapping-shell UAV races and cut inner-shell overdraw.
    #define VIVID_SLAB_GBUFFER_PREDEPTH_EARLY_Z 1

    void VividSlabEvaluateCoverage(
        const VividSlabEvalContext ctx,
        out float alpha,
        out float cutoff)
    {
        #if defined(VIVID_SLAB_TARGET_RASTER)
            float h = saturate(UNITY_ACCESS_INSTANCED_PROP(VividFurInstancedProps, _ShellHeight));
            float shellStep = _ShellStep;
        #else
            float h = saturate(ctx.shellHeight);
            float shellStep = ctx.shellStep;
        #endif

        if (h <= 1e-4f)
        {
            alpha = 1.0f;
            cutoff = 0.0f;
            return;
        }

        float4 baseMapST = VIVID_SLAB_PARAM(ctx, _BaseMap_ST);
        float2 baseUv = ctx.uv0 * baseMapST.xy + baseMapST.zw;
        float densityMask = VIVID_SLAB_SAMPLE_TEX2D(ctx, _FurMaskMap, baseUv).r;
        #if defined(VIVIDRP_SHADERPASS_SHADOW_CASTER)
            // Keep VSM/CSM shadow pages camera-independent and temporally stable.
            bool useTemporalDither = false;
        #else
            bool useTemporalDither = VIVID_SLAB_PARAM(ctx, _TemporalDither) > 0.5f;
        #endif
        float ign = VividFurInterleavedGradientNoise(
            ctx.positionCS.xy,
            _JitterParams.xy,
            useTemporalDither);

        float2 strandTiling = VIVID_SLAB_PARAM(ctx, _StrandTiling).xy;
        float furDensity = VIVID_SLAB_PARAM(ctx, _FurDensity);
        float strandThickness = VIVID_SLAB_PARAM(ctx, _StrandThickness);
        float strandTaper = VIVID_SLAB_PARAM(ctx, _StrandTaper);
        float lengthVariance = VIVID_SLAB_PARAM(ctx, _LengthVariance);
        float clumpStrength = VIVID_SLAB_PARAM(ctx, _ClumpStrength);
        float alphaCutoff = VIVID_SLAB_PARAM(ctx, _AlphaCutoff);
        float ditherStrength = VIVID_SLAB_PARAM(ctx, _DitherStrength);

        #if !defined(VIVIDRP_SHADERPASS_SHADOW_CASTER)
        if (VIVID_SLAB_PARAM(ctx, _HybridRaymarch) > 0.5f)
        {
            VividFurHybridHit hybridHit = VividEvaluateFurHybridSlabWithDerivatives(
                ctx.positionWS,
                ctx.positionWSDdx,
                ctx.positionWSDdy,
                ctx.geometryNormalWS,
                _WorldSpaceCameraPos,
                ctx.uv0,
                ctx.uv0Ddx,
                ctx.uv0Ddy,
                strandTiling,
                furDensity,
                h,
                shellStep,
                VIVID_SLAB_PARAM(ctx, _FurLength),
                VIVID_SLAB_PARAM(ctx, _GravityWS).xyz,
                VIVID_SLAB_PARAM(ctx, _GravityStrength),
                strandThickness,
                strandTaper,
                lengthVariance,
                clumpStrength,
                densityMask,
                alphaCutoff,
                ditherStrength,
                ign,
                (int)round(VIVID_SLAB_PARAM(ctx, _HybridSteps)),
                VIVID_SLAB_PARAM(ctx, _HybridMaxParallax));

            alpha = hybridHit.hit ? 1.0f : 0.0f;
            cutoff = 0.5f;
            return;
        }
        #endif

        VividFurStrandSample strand = VividEvaluateFurProceduralStrands(
            ctx.uv0,
            strandTiling,
            furDensity,
            h,
            strandThickness,
            strandTaper,
            lengthVariance,
            clumpStrength,
            densityMask);

        alpha = strand.alpha;
        // Blend base alpha threshold with spatio-temporal IGN dither for soft TAA-reconstructed tips.
        float ditherCutoff = lerp(0.04f, 0.96f, ign);
        cutoff = lerp(saturate(alphaCutoff), ditherCutoff, saturate(ditherStrength));
    }
    // @vivid-coverage-end

    // @vivid-surface-begin
    #define VIVID_SLAB_HAS_CUSTOM_POST_SURFACE 1
    #define VIVID_SLAB_THIN_WALLED_TRANSMISSION 1
    void VividSlabPostEvaluateSurface(
        const VividSlabEvalContext ctx,
        inout VividOpenPBRV2Inputs v2,
        inout float ambientOcclusion)
    {
        VividFurSurfaceParams fp = VividCreateDefaultFurSurfaceParams();
        fp.normalWS = SafeNormalize(ctx.geometryNormalWS);
        fp.tangentWS = ctx.geometryTangentWS.xyz;
        fp.tangentHandedness = ctx.geometryTangentWS.w;

        #if defined(VIVID_SLAB_TARGET_RASTER)
            float h = saturate(UNITY_ACCESS_INSTANCED_PROP(VividFurInstancedProps, _ShellHeight));
            float shellStep = _ShellStep;
        #else
            float h = saturate(ctx.shellHeight);
            float shellStep = ctx.shellStep;
        #endif

        float4 baseMapST = VIVID_SLAB_PARAM(ctx, _BaseMap_ST);
        float2 baseUv = ctx.uv0 * baseMapST.xy + baseMapST.zw;
        float3 baseTex = VIVID_SLAB_SAMPLE_TEX2D(ctx, _BaseMap, baseUv).rgb;
        float4 maskSample = VIVID_SLAB_SAMPLE_TEX2D(ctx, _FurMaskMap, baseUv);

        float2 strandTiling = VIVID_SLAB_PARAM(ctx, _StrandTiling).xy;
        float furDensity = VIVID_SLAB_PARAM(ctx, _FurDensity);
        float strandThickness = VIVID_SLAB_PARAM(ctx, _StrandThickness);
        float strandTaper = VIVID_SLAB_PARAM(ctx, _StrandTaper);
        float lengthVariance = VIVID_SLAB_PARAM(ctx, _LengthVariance);
        float clumpStrength = VIVID_SLAB_PARAM(ctx, _ClumpStrength);
        float3 gravityWS = VIVID_SLAB_PARAM(ctx, _GravityWS).xyz;
        float gravityStrength = VIVID_SLAB_PARAM(ctx, _GravityStrength);

        VividFurStrandSample strand;
        float effectiveShellHeight = h;
        if (VIVID_SLAB_PARAM(ctx, _HybridRaymarch) > 0.5f && h > 1e-4f)
        {
            float ign = VividFurInterleavedGradientNoise(
                ctx.positionCS.xy,
                _JitterParams.xy,
                VIVID_SLAB_PARAM(ctx, _TemporalDither) > 0.5f);
            VividFurHybridHit hybridHit = VividEvaluateFurHybridSlabWithDerivatives(
                ctx.positionWS,
                ctx.positionWSDdx,
                ctx.positionWSDdy,
                ctx.geometryNormalWS,
                _WorldSpaceCameraPos,
                ctx.uv0,
                ctx.uv0Ddx,
                ctx.uv0Ddy,
                strandTiling,
                furDensity,
                h,
                shellStep,
                VIVID_SLAB_PARAM(ctx, _FurLength),
                gravityWS,
                gravityStrength,
                strandThickness,
                strandTaper,
                lengthVariance,
                clumpStrength,
                maskSample.r,
                VIVID_SLAB_PARAM(ctx, _AlphaCutoff),
                VIVID_SLAB_PARAM(ctx, _DitherStrength),
                ign,
                (int)round(VIVID_SLAB_PARAM(ctx, _HybridSteps)),
                VIVID_SLAB_PARAM(ctx, _HybridMaxParallax));

            if (hybridHit.hit)
            {
                strand = hybridHit.strand;
                effectiveShellHeight = hybridHit.hitShellHeight;
            }
            else
            {
                strand = VividEvaluateFurProceduralStrands(
                    ctx.uv0,
                    strandTiling,
                    furDensity,
                    h,
                    strandThickness,
                    strandTaper,
                    lengthVariance,
                    clumpStrength,
                    maskSample.r);
            }
        }
        else
        {
            strand = VividEvaluateFurProceduralStrands(
                ctx.uv0,
                strandTiling,
                furDensity,
                h,
                strandThickness,
                strandTaper,
                lengthVariance,
                clumpStrength,
                maskSample.r);
        }

        fp.shellHeight = effectiveShellHeight;
        fp.rootColor = saturate(VIVID_SLAB_PARAM(ctx, _RootColor).rgb * baseTex);
        fp.tipColor = saturate(VIVID_SLAB_PARAM(ctx, _TipColor).rgb * baseTex);
        fp.colorGradientPower = VIVID_SLAB_PARAM(ctx, _ColorGradientPower);
        fp.rootAO = VIVID_SLAB_PARAM(ctx, _RootAO);
        fp.aoPower = VIVID_SLAB_PARAM(ctx, _AOPower);
        fp.rootRoughness = VIVID_SLAB_PARAM(ctx, _RootRoughness);
        fp.tipRoughness = VIVID_SLAB_PARAM(ctx, _TipRoughness);
        fp.specularWeight = VIVID_SLAB_PARAM(ctx, _SpecularWeight);
        fp.specularTint = saturate(VIVID_SLAB_PARAM(ctx, _SpecularTint).rgb);
        fp.anisotropy = VIVID_SLAB_PARAM(ctx, _Anisotropy);
        fp.fuzzWeight = VIVID_SLAB_PARAM(ctx, _FuzzWeight);
        fp.fuzzColor = saturate(VIVID_SLAB_PARAM(ctx, _FuzzColor).rgb);
        fp.fuzzRoughness = VIVID_SLAB_PARAM(ctx, _FuzzRoughness);
        fp.strandVariation = strand.strandVariation;
        fp.strandColorVariance = VIVID_SLAB_PARAM(ctx, _StrandColorVariance);
        fp.strandOffsetTS = strand.strandOffsetTS;
        fp.strandNormalStrength = VIVID_SLAB_PARAM(ctx, _StrandNormalStrength);
        fp.gravityDirWS = gravityWS;
        fp.combDirWS = VIVID_SLAB_PARAM(ctx, _CombDirWS).xyz;
        fp.combStrength = VIVID_SLAB_PARAM(ctx, _CombStrength);
        fp.ambientOcclusion = saturate(ambientOcclusion * maskSample.g);

        float finalAO = 1.0f;
        v2 = VividFurParamsToOpenPBRV2Inputs(fp, finalAO);
        ambientOcclusion = finalAO;
    }
    // @vivid-surface-end

    VIVID_SLAB_USE_BASE_SUBSHADER

    CustomEditor "VividRP.Editor.FurShaderGUI"

    FallBack Off
}
