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
        [SubToggle(FurGeometry, _)] _UseFlowMap("Use DCC Flow Map", Float) = 0.0
        [Tex(FurGeometry)] [ShowIf(_UseFlowMap, Equal, 1)] _FurFlowMap("Flow Map (RG: CombTS, B: Loft, A: Length)", 2D) = "gray" {}
        [SubToggle(FurGeometry, _)] [ShowIf(_UseFlowMap, Equal, 1)] _FlowMapFlipY("Flow Map Flip Y (Houdini/Blender)", Float) = 0.0
        [Sub(FurGeometry)] [ShowIf(_UseFlowMap, Equal, 1)] _FlowMapStrength("Flow Map Strength", Range(0.0, 1.0)) = 1.0
        [Sub(FurGeometry)] _WindDirWS("Wind Direction (WS)", Vector) = (1.0, 0.0, 0.35, 0.0)
        [Sub(FurGeometry)] _WindStrength("Wind Sway Strength", Range(0.0, 1.0)) = 0.0
        [Sub(FurGeometry)] _WindFrequency("Wind Frequency", Range(0.1, 10.0)) = 2.2

        [Main(FurStrands, _, on, off)] _FurStrands("Strand Density & Dithering", Float) = 1
        [SubEnum(FurStrands, Procedural Lattice, 0, 2D Pattern Map, 1, 3D Flipbook Atlas, 2)] _FurPatternMode("Strand Pattern Mode", Float) = 0.0
        [Tex(FurStrands)] _FurMaskMap("Fur Mask (R: Length/Density, G: AO)", 2D) = "white" {}
        [Tex(FurStrands)] [ShowIf(_FurPatternMode, GreaterEqual, 1)] _FurPatternMap("Pattern Map (R: Height/Alpha, GB: Offset, A: ID)", 2D) = "white" {}
        [Sub(FurStrands)] [ShowIf(_FurPatternMode, GreaterEqual, 1)] _PatternTiling("Pattern UV Tiling", Vector) = (8.0, 4.0, 0.0, 0.0)
        [Sub(FurStrands)] [ShowIf(_FurPatternMode, Equal, 2)] _PatternFlipbookGrid("Flipbook Grid (Cols, Rows)", Vector) = (4.0, 4.0, 0.0, 0.0)
        [Sub(FurStrands)] [ShowIf(_FurPatternMode, Equal, 1)] _PatternHeightContrast("2D Pattern Taper Contrast", Range(0.02, 0.50)) = 0.12
        [Sub(FurStrands)] [ShowIf(_FurPatternMode, Equal, 0)] _StrandTiling("Strand UV Aspect Tiling", Vector) = (2.0, 1.0, 0.0, 0.0)
        [Sub(FurStrands)] [ShowIf(_FurPatternMode, Equal, 0)] _FurDensity("Strand Density", Range(16.0, 320.0)) = 128.0
        [Sub(FurStrands)] [ShowIf(_FurPatternMode, Equal, 0)] _StrandThickness("Strand Thickness", Range(0.10, 0.65)) = 0.44
        [Sub(FurStrands)] [ShowIf(_FurPatternMode, Equal, 0)] _StrandTaper("Strand Tip Taper", Range(0.35, 2.5)) = 0.90
        [Sub(FurStrands)] [ShowIf(_FurPatternMode, Equal, 0)] _LengthVariance("Strand Length Variance", Range(0.0, 0.85)) = 0.45
        [Sub(FurStrands)] [ShowIf(_FurPatternMode, Equal, 0)] _ClumpStrength("Clump Strength", Range(0.0, 1.0)) = 0.35
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
        [SubEnum(FurShading, OpenPBR V2 Plush, 0, Marschner Hair Fur, 1)] _FurLightingModel("Lighting Model", Float) = 0.0
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
        [Sub(FurShading)] [ShowIf(_FurLightingModel, Equal, 0)] _SpecularTint("Keratin Specular Tint", Color) = (1.0, 0.94, 0.86, 1.0)
        [Sub(FurShading)] [ShowIf(_FurLightingModel, Equal, 0)] _Anisotropy("Strand Anisotropy", Range(0.0, 1.0)) = 0.72
        [Sub(FurShading)] [ShowIf(_FurLightingModel, Equal, 0)] _FuzzWeight("Fuzz Rim Sheen Weight", Range(0.0, 1.0)) = 0.85
        [Sub(FurShading)] [ShowIf(_FurLightingModel, Equal, 0)] _FuzzColor("Fuzz Rim Sheen Color", Color) = (0.94, 0.84, 0.72, 1.0)
        [Sub(FurShading)] [ShowIf(_FurLightingModel, Equal, 0)] _FuzzRoughness("Fuzz Rim Roughness", Range(0.15, 1.0)) = 0.46
        [Sub(FurShading)] [ShowIf(_FurLightingModel, Equal, 1)] _HairRadialRoughness("Radial Roughness", Range(0.05, 1.0)) = 0.45
        [Sub(FurShading)] [ShowIf(_FurLightingModel, Equal, 1)] _HairCuticleAngle("Cuticle Tilt (Deg)", Range(0.0, 12.0)) = 2.5
        [Sub(FurShading)] [ShowIf(_FurLightingModel, Equal, 1)] _HairScatter("Dual Scattering Strength", Range(0.0, 1.0)) = 0.85
        [Sub(FurShading)] [ShowIf(_FurLightingModel, Equal, 1)] _HairBacklit("Backlit TT Transmission", Range(0.0, 1.0)) = 0.85
        [Sub(FurShading)] [ShowIf(_FurLightingModel, Equal, 1)] _FurMedullaWeight("Medulla Weight", Range(0.0, 1.0)) = 0.55
        [Sub(FurShading)] [ShowIf(_FurLightingModel, Equal, 1)] _FurMedullaRoughness("Medulla Softness", Range(0.10, 1.0)) = 0.65
        [Sub(FurShading)] [ShowIf(_FurLightingModel, Equal, 1)] _StrandTangentJitter("Strand Tangent Jitter", Range(0.0, 0.75)) = 0.12

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
    float _FurLightingModel;
    float _HairRadialRoughness;
    float _HairCuticleAngle;
    float _HairScatter;
    float _HairBacklit;
    float _FurMedullaWeight;
    float _FurMedullaRoughness;
    float _UseFlowMap;
    float _FlowMapFlipY;
    float _FlowMapStrength;
    float _FurPatternMode;
    float4 _PatternTiling;
    float4 _PatternFlipbookGrid;
    float _PatternHeightContrast;
    float _StrandTangentJitter;
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
    TEXTURE2D(_FurFlowMap);
    SAMPLER(sampler_FurFlowMap);
    TEXTURE2D(_FurPatternMap);
    SAMPLER(sampler_FurPatternMap);
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

    // Shared Phase D1 DCC FlowMap + 2D Pattern + 4x4 Flipbook 3D Pattern helpers
    // (placed in @vivid-vertex-begin..end so Coverage, GBuffer, VisibilityRaster, and Compute Resolve all share them).
    VividFurFlowSample VividFurEvaluateFlowSample(const VividSlabEvalContext ctx, float2 baseUv)
    {
        float3 fallbackCombWS = VIVID_SLAB_PARAM(ctx, _CombDirWS).xyz;
        bool useFlowMap = VIVID_SLAB_PARAM(ctx, _UseFlowMap) > 0.5f;
        if (!useFlowMap)
        {
            VividFurFlowSample defaultFlow;
            defaultFlow.combDirWS = fallbackCombWS;
            defaultFlow.loftOverride = -1.0f;
            defaultFlow.lengthScale = 1.0f;
            return defaultFlow;
        }

        float4 rawFlow = VIVID_SLAB_SAMPLE_TEX2D(ctx, _FurFlowMap, baseUv);
        return VividDecodeFurFlowMapSample(
            rawFlow,
            true,
            VIVID_SLAB_PARAM(ctx, _FlowMapFlipY) > 0.5f,
            VIVID_SLAB_PARAM(ctx, _FlowMapStrength),
            ctx.geometryNormalWS,
            ctx.geometryTangentWS,
            fallbackCombWS);
    }

    VividFurStrandSample VividFurEvaluatePatternStrandAt(
        const VividSlabEvalContext ctx,
        float2 uvSample,
        float hSample,
        float densityMask,
        int patternMode)
    {
        float2 patternTiling = VIVID_SLAB_PARAM(ctx, _PatternTiling).xy;
        if (patternMode == 1)
        {
            float2 patUv = uvSample * patternTiling;
            float2 patDdx = ctx.uv0Ddx * patternTiling;
            float2 patDdy = ctx.uv0Ddy * patternTiling;
            float4 patSample = VIVID_SLAB_SAMPLE_TEX2D_GRAD(ctx, _FurPatternMap, patUv, patDdx, patDdy);
            return VividEvaluateFur2DPatternSample(
                patSample,
                hSample,
                densityMask,
                VIVID_SLAB_PARAM(ctx, _PatternHeightContrast));
        }

        // Mode 2: 3D Flipbook Atlas (trilinear interpolation across adjacent height slices)
        float2 gridColsRows = max(floor(VIVID_SLAB_PARAM(ctx, _PatternFlipbookGrid).xy + 0.5f), 1.0f.xx);
        uint totalSlices = max((uint)(gridColsRows.x * gridColsRows.y), 1u);
        float maxLen = max(saturate(densityMask), 1e-3f);
        float hNorm = saturate(hSample / maxLen);
        float sliceF = hNorm * (float)(totalSlices - 1u);
        uint slice0 = (uint)floor(sliceF);
        uint slice1 = min(slice0 + 1u, totalSlices - 1u);
        float sliceFrac = frac(sliceF);

        float2 uvA0, ddxA0, ddyA0;
        VividFurComputeFlipbookSliceUV(uvSample, ctx.uv0Ddx, ctx.uv0Ddy, patternTiling, gridColsRows, slice0, uvA0, ddxA0, ddyA0);
        float4 s0 = VIVID_SLAB_SAMPLE_TEX2D_GRAD(ctx, _FurPatternMap, uvA0, ddxA0, ddyA0);

        float4 s1 = s0;
        if (slice1 != slice0 && sliceFrac > 1e-4f)
        {
            float2 uvA1, ddxA1, ddyA1;
            VividFurComputeFlipbookSliceUV(uvSample, ctx.uv0Ddx, ctx.uv0Ddy, patternTiling, gridColsRows, slice1, uvA1, ddxA1, ddyA1);
            s1 = VIVID_SLAB_SAMPLE_TEX2D_GRAD(ctx, _FurPatternMap, uvA1, ddxA1, ddyA1);
        }

        return VividEvaluateFurFlipbookSample(s0, s1, sliceFrac, hSample, densityMask);
    }

    VividFurHybridHit VividFurRaymarchPatternHybridSlab(
        const VividSlabEvalContext ctx,
        float shellHeightTop,
        float shellStep,
        float3 bendDirWS,
        float bendStrength,
        float densityMask,
        float alphaCutoff,
        float ditherStrength,
        float ign,
        int stepCount,
        float maxParallaxScale,
        int patternMode)
    {
        VividFurHybridHit hitResult;
        hitResult.hit = false;
        hitResult.hitShellHeight = saturate(shellHeightTop);
        hitResult.strand.alpha = 0.0f;
        hitResult.strand.strandOffsetTS = 0.0f.xx;
        hitResult.strand.strandVariation = 0.5f;
        hitResult.strand.tipFactor = 1.0f;

        float hTop = saturate(shellHeightTop);
        if (hTop <= 1e-4f)
        {
            hitResult.hit = true;
            hitResult.hitShellHeight = 0.0f;
            hitResult.strand = VividFurEvaluatePatternStrandAt(ctx, ctx.uv0, 0.0f, densityMask, patternMode);
            return hitResult;
        }

        float slabDeltaH;
        float2 uvDeltaPerSlab;
        VividFurComputeHybridSlabUvDelta(
            ctx.positionWS,
            ctx.positionWSDdx,
            ctx.positionWSDdy,
            ctx.geometryNormalWS,
            _WorldSpaceCameraPos,
            ctx.uv0Ddx,
            ctx.uv0Ddy,
            hTop,
            shellStep,
            VIVID_SLAB_PARAM(ctx, _FurLength),
            bendDirWS,
            bendStrength,
            maxParallaxScale,
            slabDeltaH,
            uvDeltaPerSlab);

        int steps = clamp(stepCount, 1, 8);
        float invSteps = rcp((float)steps);
        float ignAlpha = frac(ign + 0.6180339887f);
        float ditherCutoff = lerp(0.04f, 0.96f, ignAlpha);
        float threshold = lerp(saturate(alphaCutoff), ditherCutoff, saturate(ditherStrength));

        [loop]
        for (int k = 0; k < steps; ++k)
        {
            float s = ((float)k + ign) * invSteps;
            float hSample = max(hTop - slabDeltaH * s, 0.0f);
            float2 uvSample = ctx.uv0 + uvDeltaPerSlab * s;

            VividFurStrandSample sampleVal = VividFurEvaluatePatternStrandAt(ctx, uvSample, hSample, densityMask, patternMode);
            if (sampleVal.alpha >= threshold)
            {
                hitResult.hit = true;
                hitResult.hitShellHeight = hSample;
                hitResult.strand = sampleVal;
                return hitResult;
            }
        }

        return hitResult;
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
        VividFurFlowSample flowSample = VividFurEvaluateFlowSample(ctx, baseUv);
        float densityMask = saturate(VIVID_SLAB_SAMPLE_TEX2D(ctx, _FurMaskMap, baseUv).r * flowSample.lengthScale);
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

        int patternMode = (int)round(VIVID_SLAB_PARAM(ctx, _FurPatternMode));
        float2 strandTiling = VIVID_SLAB_PARAM(ctx, _StrandTiling).xy;
        float furDensity = VIVID_SLAB_PARAM(ctx, _FurDensity);
        float strandThickness = VIVID_SLAB_PARAM(ctx, _StrandThickness);
        float strandTaper = VIVID_SLAB_PARAM(ctx, _StrandTaper);
        float lengthVariance = VIVID_SLAB_PARAM(ctx, _LengthVariance);
        float clumpStrength = VIVID_SLAB_PARAM(ctx, _ClumpStrength);
        float alphaCutoff = VIVID_SLAB_PARAM(ctx, _AlphaCutoff);
        float ditherStrength = VIVID_SLAB_PARAM(ctx, _DitherStrength);
        float3 gravityWS = VIVID_SLAB_PARAM(ctx, _GravityWS).xyz;
        float gravityStrength = VIVID_SLAB_PARAM(ctx, _GravityStrength);
        float3 bendDirWS = flowSample.loftOverride >= 0.0f ? lerp(gravityWS, flowSample.combDirWS, 0.5f) : gravityWS;

        #if !defined(VIVIDRP_SHADERPASS_SHADOW_CASTER)
        if (VIVID_SLAB_PARAM(ctx, _HybridRaymarch) > 0.5f)
        {
            VividFurHybridHit hybridHit;
            if (patternMode >= 1)
            {
                hybridHit = VividFurRaymarchPatternHybridSlab(
                    ctx,
                    h,
                    shellStep,
                    bendDirWS,
                    gravityStrength,
                    densityMask,
                    alphaCutoff,
                    ditherStrength,
                    ign,
                    (int)round(VIVID_SLAB_PARAM(ctx, _HybridSteps)),
                    VIVID_SLAB_PARAM(ctx, _HybridMaxParallax),
                    patternMode);
            }
            else
            {
                hybridHit = VividEvaluateFurHybridSlabWithDerivatives(
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
                    bendDirWS,
                    gravityStrength,
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
            }

            alpha = hybridHit.hit ? 1.0f : 0.0f;
            cutoff = 0.5f;
            return;
        }
        #endif

        VividFurStrandSample strand;
        if (patternMode >= 1)
        {
            strand = VividFurEvaluatePatternStrandAt(ctx, ctx.uv0, h, densityMask, patternMode);
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
                densityMask);
        }

        alpha = strand.alpha;
        // Blend base alpha threshold with spatio-temporal IGN dither for soft TAA-reconstructed tips.
        float ditherCutoff = lerp(0.04f, 0.96f, ign);
        cutoff = lerp(saturate(alphaCutoff), ditherCutoff, saturate(ditherStrength));
    }
    // @vivid-coverage-end

    // @vivid-surface-begin
    #define VIVID_SLAB_HAS_CUSTOM_POST_SURFACE 1
    #define VIVID_SLAB_THIN_WALLED_TRANSMISSION 1
    #define VIVID_SLAB_SUPPORTS_HAIR_CLOSURE 1

    VividFurSurfaceParams VividFurEvaluateSharedParams(
        const VividSlabEvalContext ctx,
        float baseAmbientOcclusion)
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
        VividFurFlowSample flowSample = VividFurEvaluateFlowSample(ctx, baseUv);
        float effectiveDensityMask = saturate(maskSample.r * flowSample.lengthScale);

        int patternMode = (int)round(VIVID_SLAB_PARAM(ctx, _FurPatternMode));
        float2 strandTiling = VIVID_SLAB_PARAM(ctx, _StrandTiling).xy;
        float furDensity = VIVID_SLAB_PARAM(ctx, _FurDensity);
        float strandThickness = VIVID_SLAB_PARAM(ctx, _StrandThickness);
        float strandTaper = VIVID_SLAB_PARAM(ctx, _StrandTaper);
        float lengthVariance = VIVID_SLAB_PARAM(ctx, _LengthVariance);
        float clumpStrength = VIVID_SLAB_PARAM(ctx, _ClumpStrength);
        float3 gravityWS = VIVID_SLAB_PARAM(ctx, _GravityWS).xyz;
        float gravityStrength = VIVID_SLAB_PARAM(ctx, _GravityStrength);
        float3 bendDirWS = flowSample.loftOverride >= 0.0f ? lerp(gravityWS, flowSample.combDirWS, 0.5f) : gravityWS;

        VividFurStrandSample strand;
        float effectiveShellHeight = h;
        if (VIVID_SLAB_PARAM(ctx, _HybridRaymarch) > 0.5f && h > 1e-4f)
        {
            float ign = VividFurInterleavedGradientNoise(
                ctx.positionCS.xy,
                _JitterParams.xy,
                VIVID_SLAB_PARAM(ctx, _TemporalDither) > 0.5f);
            VividFurHybridHit hybridHit;
            if (patternMode >= 1)
            {
                hybridHit = VividFurRaymarchPatternHybridSlab(
                    ctx,
                    h,
                    shellStep,
                    bendDirWS,
                    gravityStrength,
                    effectiveDensityMask,
                    VIVID_SLAB_PARAM(ctx, _AlphaCutoff),
                    VIVID_SLAB_PARAM(ctx, _DitherStrength),
                    ign,
                    (int)round(VIVID_SLAB_PARAM(ctx, _HybridSteps)),
                    VIVID_SLAB_PARAM(ctx, _HybridMaxParallax),
                    patternMode);
            }
            else
            {
                hybridHit = VividEvaluateFurHybridSlabWithDerivatives(
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
                    bendDirWS,
                    gravityStrength,
                    strandThickness,
                    strandTaper,
                    lengthVariance,
                    clumpStrength,
                    effectiveDensityMask,
                    VIVID_SLAB_PARAM(ctx, _AlphaCutoff),
                    VIVID_SLAB_PARAM(ctx, _DitherStrength),
                    ign,
                    (int)round(VIVID_SLAB_PARAM(ctx, _HybridSteps)),
                    VIVID_SLAB_PARAM(ctx, _HybridMaxParallax));
            }

            if (hybridHit.hit)
            {
                strand = hybridHit.strand;
                effectiveShellHeight = hybridHit.hitShellHeight;
            }
            else
            {
                if (patternMode >= 1)
                {
                    strand = VividFurEvaluatePatternStrandAt(ctx, ctx.uv0, h, effectiveDensityMask, patternMode);
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
                        effectiveDensityMask);
                }
            }
        }
        else
        {
            if (patternMode >= 1)
            {
                strand = VividFurEvaluatePatternStrandAt(ctx, ctx.uv0, h, effectiveDensityMask, patternMode);
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
                    effectiveDensityMask);
            }
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
        fp.gravityStrength = gravityStrength;
        fp.combDirWS = flowSample.combDirWS;
        fp.combStrength = flowSample.loftOverride >= 0.0f ? max(VIVID_SLAB_PARAM(ctx, _CombStrength), VIVID_SLAB_PARAM(ctx, _FlowMapStrength)) : VIVID_SLAB_PARAM(ctx, _CombStrength);
        fp.ambientOcclusion = saturate(baseAmbientOcclusion * maskSample.g);
        fp.hairRadialRoughness = VIVID_SLAB_PARAM(ctx, _HairRadialRoughness);
        fp.hairCuticleAngleDeg = VIVID_SLAB_PARAM(ctx, _HairCuticleAngle);
        fp.hairScatter = VIVID_SLAB_PARAM(ctx, _HairScatter);
        fp.hairBacklit = VIVID_SLAB_PARAM(ctx, _HairBacklit);
        fp.furMedullaWeight = VIVID_SLAB_PARAM(ctx, _FurMedullaWeight);
        fp.furMedullaRoughness = VIVID_SLAB_PARAM(ctx, _FurMedullaRoughness);
        fp.flowLoftOverride = flowSample.loftOverride;
        fp.strandTangentJitter = VIVID_SLAB_PARAM(ctx, _StrandTangentJitter);
        return fp;
    }

    bool VividSlabTryEvaluateHairClosure(
        const VividSlabEvalContext ctx,
        out VividSubstrateHairInputs hairInputs,
        out float3 summaryNormalWS)
    {
        hairInputs = VividCreateDefaultSubstrateHairInputs();
        summaryNormalWS = SafeNormalize(ctx.geometryNormalWS);
        if (VIVID_SLAB_PARAM(ctx, _FurLightingModel) < 0.5f)
            return false;

        VividFurSurfaceParams fp = VividFurEvaluateSharedParams(ctx, 1.0f);
        hairInputs = VividFurParamsToSubstrateHairInputs(fp, summaryNormalWS);
        return true;
    }

    void VividSlabPostEvaluateSurface(
        const VividSlabEvalContext ctx,
        inout VividOpenPBRV2Inputs v2,
        inout float ambientOcclusion)
    {
        VividFurSurfaceParams fp = VividFurEvaluateSharedParams(ctx, ambientOcclusion);
        float finalAO = 1.0f;
        v2 = VividFurParamsToOpenPBRV2Inputs(fp, finalAO);
        ambientOcclusion = finalAO;
    }
    // @vivid-surface-end

    VIVID_SLAB_USE_BASE_SUBSHADER

    CustomEditor "VividRP.Editor.FurShaderGUI"

    FallBack Off
}
