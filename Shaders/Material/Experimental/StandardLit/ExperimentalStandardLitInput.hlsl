#ifndef VIVIDRP_EXPERIMENTAL_STANDARD_LIT_INPUT_INCLUDED
#define VIVIDRP_EXPERIMENTAL_STANDARD_LIT_INPUT_INCLUDED

// Reuse the proven StandardLit sampling helpers without changing the existing
// material. Rename its legacy entry point so this shader can provide the
// experimental closure entry point expected by the shared GBuffer pass.
#define VividBuildGBufferSurfaceData VividBuildLegacyStandardLitGBufferSurfaceData
#include "Packages/com.vivid.render-pipelines/Shaders/Material/StandardLit/StandardLitInput.hlsl"
#undef VividBuildGBufferSurfaceData

#include "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/Closure/ExperimentalClosure.hlsl"

float4 _TopLayerBaseColor;
float4 _TopLayerBaseMap_ST;
float _TopLayerWeight;
float _TopLayerOperator;
float _TopLayerMetallic;
float _TopLayerSmoothness;
float _TopLayerSpecularIOR;

TEXTURE2D(_TopLayerBaseMap);
SAMPLER(sampler_TopLayerBaseMap);
TEXTURE2D(_TopLayerMaskMap);
SAMPLER(sampler_TopLayerMaskMap);

VividExperimentalStandardSurfaceParameters SampleExperimentalStandardLitSurface(
    FragInputs input)
{
    float2 uv = GetStandardLitBaseUV(input);
    float4 baseSample = SampleBase(uv, input.positionSS);
    ApplyAlphaClip(baseSample.a);

    float2 metallicSmoothness = SampleMetallicSmoothness(uv, baseSample.a);

    VividExperimentalStandardSurfaceParameters parameters;
    parameters.baseColor = baseSample.rgb;
    parameters.normalWS = SampleNormalWS(input, uv);
    parameters.perceptualRoughness = 1.0 - metallicSmoothness.y;
    parameters.metallic = metallicSmoothness.x;
    parameters.ambientOcclusion = SampleAmbientOcclusion(uv);
    parameters.coverage = 1.0;
    parameters.specularIor = _SpecularIOR;
    parameters.transmissionWeight = saturate(_TransmissionWeight);
    parameters.subsurfaceWeight = saturate(_SubsurfaceWeight);

#if defined(_CLEARCOAT)
    parameters.clearCoatWeight = saturate(_ClearCoatMask);
    parameters.clearCoatPerceptualRoughness =
        1.0 - saturate(_ClearCoatSmoothness);
#else
    parameters.clearCoatWeight = 0.0;
    parameters.clearCoatPerceptualRoughness = 0.0;
#endif

    parameters.materialFeatures =
        GetStandardLitMaterialFeatures(parameters.clearCoatWeight);
    parameters.emissive = SampleEmission(uv);

    float2 lightmapUV = TransformVividLightmapUV(input.texCoord1.xy);
    parameters.builtinData = BuildVividBuiltinData(
        SampleStandardLitBakedGI(
            lightmapUV,
            parameters.normalWS,
            input.positionRWS),
        HasStandardLitBakedGI(),
        lightmapUV,
        input.positionRWS);
    return parameters;
}

VividExperimentalStandardSurface BuildExperimentalStandardLitSurface(
    FragInputs input)
{
    return VividResolveExperimentalStandardSurface(
        SampleExperimentalStandardLitSurface(input));
}

VividExperimentalClosureMaterial BuildExperimentalStandardLitMaterial(
    FragInputs input,
    VividExperimentalStandardSurface baseSurface)
{
    float topLayerWeight = saturate(_TopLayerWeight);
    if (topLayerWeight <=
        VIVID_EXPERIMENTAL_CLOSURE_LAYER_WEIGHT_EPSILON)
    {
        return VividCompileExperimentalStandardSurface(baseSurface);
    }

    float2 topLayerUV = input.texCoord0.xy * _TopLayerBaseMap_ST.xy
        + _TopLayerBaseMap_ST.zw;
    float topLayerMask = SAMPLE_TEXTURE2D(
        _TopLayerMaskMap,
        sampler_TopLayerMaskMap,
        topLayerUV).r;
    topLayerWeight *= saturate(topLayerMask);
    if (topLayerWeight <=
        VIVID_EXPERIMENTAL_CLOSURE_LAYER_WEIGHT_EPSILON)
    {
        return VividCompileExperimentalStandardSurface(baseSurface);
    }

    float3 topLayerBaseColor = SAMPLE_TEXTURE2D(
        _TopLayerBaseMap,
        sampler_TopLayerBaseMap,
        topLayerUV).rgb * _TopLayerBaseColor.rgb;

    VividExperimentalStandardSurfaceParameters topParameters;
    topParameters.baseColor = topLayerBaseColor;
    topParameters.normalWS = baseSurface.normalWS;
    topParameters.perceptualRoughness = 1.0
        - saturate(_TopLayerSmoothness);
    topParameters.metallic = saturate(_TopLayerMetallic);
    topParameters.ambientOcclusion = 1.0;
    topParameters.coverage = 1.0;
    topParameters.specularIor = _TopLayerSpecularIOR;
    topParameters.clearCoatWeight = 0.0;
    topParameters.clearCoatPerceptualRoughness = 0.0;
    topParameters.transmissionWeight = 0.0;
    topParameters.subsurfaceWeight = 0.0;
    topParameters.emissive = 0.0;
    topParameters.materialFeatures = VIVID_MATERIALFEATURE_LIT;
    topParameters.builtinData = baseSurface.builtinData;

    return VividCompileExperimentalLayeredSurface(
        baseSurface,
        VividResolveExperimentalStandardSurface(topParameters),
        (uint)round(saturate(_TopLayerOperator)),
        topLayerWeight);
}

VividGBufferSurfaceData VividBuildGBufferSurfaceData(FragInputs input)
{
    VividExperimentalStandardSurface surface = BuildExperimentalStandardLitSurface(input);
    VividExperimentalClosureMaterial material =
        BuildExperimentalStandardLitMaterial(input, surface);
    return VividExportExperimentalClosureToLegacyGBuffer(material);
}

#endif
