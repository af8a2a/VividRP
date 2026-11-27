// @vivid-base "Packages/com.vivid.render-pipelines/Shaders/Material/Templates/SubstrateOpaque.slab.template"
//
// VividRP Fabric Material (.slab.shader) modeled after HDRP Fabric (CottonWool & Silk).
// - Supports both HDRP Fabric archetypes on OpenPBR V2 Substrate:
//     * CottonWool (Cotton, Denim, Wool, Linen, Velvet): EON rough diffuse + OpenPBR Disney LTC Fuzz/Sheen.
//     * Silk (Silk, Shot Silk, Satin, Nylon): Tinted anisotropic specular + grazing silk sheen.
// - Supports HDRP channel-packed ThreadMap (R: AO, G/A: Normal, B: Smoothness), Fuzz detail map,
//   optional procedural woven thread fallback, AlphaClip, and Double-Sided rendering (Cull Off).
// - Automatically compiles to both Raster GBuffer and standalone zero-switch GPUDriven ComputeShader.
Shader "VividRP/Material/FabricSlab"
{
    Properties
    {
        [Main(SurfaceOptions, _, on, off)] _SurfaceOptions("Surface Options", Float) = 1
        [SubEnum(SurfaceOptions, CottonWool, 0, Silk, 1)] _FabricType("Fabric Type", Float) = 0.0
        [SubEnum(SurfaceOptions, Back, 2, Front, 1, Off (Double Sided), 0)] _Cull("Cull", Float) = 0.0
        [SubToggle(SurfaceOptions, _)] _ReceiveShadows("Receive Shadows", Float) = 1.0
        [SubToggle(SurfaceOptions, _)] _ReceiveSSR("Receive SSR", Float) = 0.0
        [SubToggle(SurfaceOptions, _)] _ReceiveDecals("Receive Decals", Float) = 1.0

        [Main(SurfaceInputs, _, on, off)] _SurfaceInputs("Surface Inputs", Float) = 1
        [AdvancedHeaderProperty] [MainTexture] [Tex(SurfaceInputs, _BaseColor)] _BaseMap("Base Map", 2D) = "white" {}
        [Advanced] [HideInInspector] [MainColor] _BaseColor("Base Color", Color) = (0.55, 0.55, 0.55, 1.0)
        [Advanced] [TilingOffset(SurfaceInputs)] _BaseMap_ST("Base UV Tiling & Offset", Vector) = (1, 1, 0, 0)
        [Tex(SurfaceInputs)] _MaskMap("Mask Map (R: AO, G: Fuzz Mask, A: Smoothness)", 2D) = "white" {}
        [Sub(SurfaceInputs)] _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.25
        [Sub(SurfaceInputs)] _DiffuseRoughness("Diffuse Roughness", Range(0.0, 1.0)) = 0.85
        [Sub(SurfaceInputs)] _OcclusionStrength("Occlusion Strength", Range(0.0, 1.0)) = 1.0
        [Tex(SurfaceInputs, _BumpScale)] [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        [HideInInspector] _BumpScale("Normal Scale", Float) = 1.0
        [Tex(SurfaceInputs, _EmissionColor)] _EmissionMap("Emission Map", 2D) = "black" {}
        [HideInInspector] [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 0)

        [Main(FabricSheen, _, on, off)] _FabricSheen("Fabric Sheen & Anisotropy", Float) = 1
        [Sub(FabricSheen)] _SheenColor("Sheen / Specular Tint", Color) = (0.55, 0.55, 0.55, 1.0)
        [Sub(FabricSheen)] _FuzzWeight("Fuzz / Sheen Weight", Range(0.0, 1.0)) = 0.65
        [Sub(FabricSheen)] _FuzzRoughness("Fuzz Roughness", Range(0.1, 1.0)) = 0.55
        [Sub(FabricSheen)] _Anisotropy("Silk Anisotropy", Range(-1.0, 1.0)) = 0.0
        [Sub(FabricSheen)] _AnisotropyRotation("Anisotropy Rotation (Rad)", Range(0.0, 6.2831853)) = 0.0
        [Sub(FabricSheen)] _SilkSpecularBoost("Silk Specular Boost", Range(0.0, 1.0)) = 0.75

        [Main(ThreadDetail, _, on, off)] _ThreadDetail("Thread & Fuzz Detail", Float) = 1
        [SubToggle(ThreadDetail, _)] _UseThreadMap("Use Thread Map", Float) = 0.0
        [Tex(ThreadDetail)] _ThreadMap("Thread Map (R: AO, G/A: Normal, B: Smoothness)", 2D) = "bump" {}
        [Sub(ThreadDetail)] _ThreadMap_ST("Thread UV Tiling & Offset", Vector) = (60, 60, 0, 0)
        [Sub(ThreadDetail)] _ThreadAOStrength("Thread AO Strength", Range(0.0, 1.0)) = 0.5
        [Sub(ThreadDetail)] _ThreadNormalStrength("Thread Normal Strength", Range(0.0, 2.0)) = 0.6
        [Sub(ThreadDetail)] _ThreadSmoothnessScale("Thread Smoothness Scale", Range(0.0, 1.0)) = 0.2
        [SubToggle(ThreadDetail, _)] _UseFuzzMap("Use Fuzz Detail Map", Float) = 0.0
        [Tex(ThreadDetail)] _FuzzMap("Fuzz Detail Map", 2D) = "gray" {}
        [Sub(ThreadDetail)] _FuzzMapUVScale("Fuzz Map UV Scale", Range(0.05, 64.0)) = 8.0
        [Sub(ThreadDetail)] _FuzzDetailStrength("Fuzz Detail Strength", Range(0.0, 1.0)) = 0.15
        [SubToggle(ThreadDetail, _)] _UseProceduralWeave("Procedural Weave (Fallback)", Float) = 1.0
        [Sub(ThreadDetail)] _ProceduralWeaveScale("Procedural Weave Scale", Range(8.0, 200.0)) = 72.0
        [Sub(ThreadDetail)] _ProceduralWeaveStrength("Procedural Weave Strength", Range(0.0, 1.0)) = 0.35

        [HideInInspector] _MainTex("BaseMap", 2D) = "white" {}
        [HideInInspector] _Color("Base Color", Color) = (0.55, 0.55, 0.55, 1.0)
    }

    // @vivid-cbuffer-begin
    float _FabricType;
    float _ReceiveShadows;
    float _ReceiveSSR;
    float _ReceiveDecals;
    float4 _BaseColor;
    float4 _BaseMap_ST;
    float _Smoothness;
    float _DiffuseRoughness;
    float _OcclusionStrength;
    float _BumpScale;
    float4 _EmissionColor;
    float4 _SheenColor;
    float _FuzzWeight;
    float _FuzzRoughness;
    float _Anisotropy;
    float _AnisotropyRotation;
    float _SilkSpecularBoost;
    float _UseThreadMap;
    float4 _ThreadMap_ST;
    float _ThreadAOStrength;
    float _ThreadNormalStrength;
    float _ThreadSmoothnessScale;
    float _UseFuzzMap;
    float _FuzzMapUVScale;
    float _FuzzDetailStrength;
    float _UseProceduralWeave;
    float _ProceduralWeaveScale;
    float _ProceduralWeaveStrength;
    // @vivid-cbuffer-end

    // @vivid-textures-begin
    TEXTURE2D(_BaseMap);
    SAMPLER(sampler_BaseMap);
    TEXTURE2D(_MaskMap);
    SAMPLER(sampler_MaskMap);
    TEXTURE2D(_BumpMap);
    SAMPLER(sampler_BumpMap);
    TEXTURE2D(_EmissionMap);
    SAMPLER(sampler_EmissionMap);
    TEXTURE2D(_ThreadMap);
    SAMPLER(sampler_ThreadMap);
    TEXTURE2D(_FuzzMap);
    SAMPLER(sampler_FuzzMap);
    // @vivid-textures-end

    // @vivid-surface-begin
    #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividFabricSurface.hlsl"

    #define VIVID_SLAB_HAS_CUSTOM_POST_SURFACE 1
    void VividSlabPostEvaluateSurface(
        const VividSlabEvalContext ctx,
        inout VividOpenPBRV2Inputs v2,
        inout float ambientOcclusion)
    {
        VividFabricSurfaceParams fp = VividCreateDefaultFabricSurfaceParams();
        float3 geomNormalWS = SafeNormalize(ctx.geometryNormalWS);
        float3 geomTangentWS = ctx.geometryTangentWS.xyz - geomNormalWS * dot(ctx.geometryTangentWS.xyz, geomNormalWS);
        float tangentLenSq = dot(geomTangentWS, geomTangentWS);
        float tangentSign = ctx.geometryTangentWS.w < 0.0f ? -1.0f : 1.0f;
        float3 unitTangentWS = tangentLenSq > 1e-8f ? geomTangentWS * rsqrt(tangentLenSq) : float3(1.0f, 0.0f, 0.0f);
        float3 unitBitangentWS = cross(geomNormalWS, unitTangentWS) * tangentSign;

        float3 normalTS = float3(0.0f, 0.0f, 1.0f);

        #if defined(VIVID_SLAB_TARGET_RASTER)
            float2 baseUv = ctx.uv0 * _BaseMap_ST.xy + _BaseMap_ST.zw;
            float4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, baseUv);
            float4 maskSample = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, baseUv);
            float4 packedNormal = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, baseUv);
            float3 emissionSample = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, baseUv).rgb;

            normalTS = UnpackNormalScale(packedNormal, _BumpScale);

            fp.fabricType = _FabricType > 0.5f ? VIVID_FABRIC_TYPE_SILK : VIVID_FABRIC_TYPE_COTTON_WOOL;
            fp.baseColor = saturate(baseSample.rgb * _BaseColor.rgb);
            fp.sheenColor = saturate(_SheenColor.rgb);
            fp.smoothness = saturate(_Smoothness * maskSample.a);
            fp.diffuseRoughness = saturate(_DiffuseRoughness);
            fp.fuzzWeight = saturate(_FuzzWeight * maskSample.g);
            fp.fuzzRoughness = clamp(_FuzzRoughness, 0.10f, 1.0f);
            fp.anisotropy = clamp(_Anisotropy, -1.0f, 1.0f);
            fp.anisotropyRotation = _AnisotropyRotation;
            fp.silkSpecularBoost = saturate(_SilkSpecularBoost);
            fp.ambientOcclusion = lerp(1.0f, saturate(maskSample.r), saturate(_OcclusionStrength));
            fp.emission = max(emissionSample * _EmissionColor.rgb, 0.0f.xxx);

            if (_UseThreadMap > 0.5f)
            {
                float2 threadUv = ctx.uv0 * _ThreadMap_ST.xy + _ThreadMap_ST.zw;
                float4 threadSample = SAMPLE_TEXTURE2D(_ThreadMap, sampler_ThreadMap, threadUv);
                VividApplyFabricThreadMap(
                    threadSample,
                    _ThreadAOStrength,
                    _ThreadNormalStrength,
                    _ThreadSmoothnessScale,
                    normalTS,
                    fp.smoothness,
                    fp.ambientOcclusion);
            }
            else if (_UseProceduralWeave > 0.5f)
            {
                VividApplyProceduralFabricWeave(
                    ctx,
                    fp.fabricType,
                    _ProceduralWeaveScale,
                    _ProceduralWeaveStrength,
                    normalTS,
                    fp.baseColor,
                    fp.smoothness,
                    fp.ambientOcclusion);
            }

            if (_UseFuzzMap > 0.5f && _FuzzDetailStrength > 1e-4f)
            {
                float2 fuzzUv = ctx.uv0 * max(_FuzzMapUVScale, 0.01f);
                float fuzzDetail = SAMPLE_TEXTURE2D(_FuzzMap, sampler_FuzzMap, fuzzUv).r;
                float fuzzMod = lerp(1.0f, lerp(0.65f, 1.25f, saturate(fuzzDetail)), saturate(_FuzzDetailStrength));
                fp.baseColor = saturate(fp.baseColor * fuzzMod);
                fp.fuzzWeight = saturate(fp.fuzzWeight * lerp(1.0f, saturate(fuzzDetail + 0.35f), saturate(_FuzzDetailStrength)));
            }
        #else
            // GPUDriven Compute target: consume per-instance VividMaterialData base albedo/roughness/emission
            // with procedural woven CottonWool fabric defaults (zero switch divergence).
            const VividMaterialData matData = PullMaterialData(ctx.materialIndex);
            fp.fabricType = VIVID_FABRIC_TYPE_COTTON_WOOL;
            fp.baseColor = saturate(matData.AlbedoColor.rgb);
            fp.sheenColor = saturate(lerp(fp.baseColor, 1.0f.xxx, 0.35f));
            fp.smoothness = saturate(1.0f - saturate(matData.Roughness));
            fp.diffuseRoughness = 0.85f;
            fp.fuzzWeight = 0.65f;
            fp.fuzzRoughness = 0.55f;
            fp.ambientOcclusion = 1.0f;
            fp.emission = max(matData.Emission.rgb, 0.0f.xxx);

            VividApplyProceduralFabricWeave(
                ctx,
                fp.fabricType,
                72.0f,
                0.35f,
                normalTS,
                fp.baseColor,
                fp.smoothness,
                fp.ambientOcclusion);
        #endif

        float3 normalWS = geomNormalWS;
        if (tangentLenSq > 1e-8f)
        {
            normalWS = SafeNormalize(
                normalTS.x * unitTangentWS
                + normalTS.y * unitBitangentWS
                + normalTS.z * geomNormalWS);
        }

        fp.normalWS = normalWS;
        fp.tangentWS = unitTangentWS;
        fp.tangentHandedness = tangentSign;

        v2 = VividFabricParamsToOpenPBRV2Inputs(fp);
        ambientOcclusion = saturate(fp.ambientOcclusion);
    }
    // @vivid-surface-end

    VIVID_SLAB_USE_BASE_SUBSHADER

    CustomEditor "VividRP.Editor.FabricShaderGUI"

    FallBack Off
}
