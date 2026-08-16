#ifndef VIVIDRP_EXPERIMENTAL_STANDARD_LIT_INPUT_INCLUDED
#define VIVIDRP_EXPERIMENTAL_STANDARD_LIT_INPUT_INCLUDED

// Reuse the proven StandardLit sampling helpers without changing the existing
// material. Rename its legacy entry point so this shader can provide the
// experimental closure entry point expected by the shared GBuffer pass.
#define VividBuildGBufferSurfaceData VividBuildLegacyStandardLitGBufferSurfaceData
#include "Packages/com.vivid.render-pipelines/Shaders/Material/StandardLit/StandardLitInput.hlsl"
#undef VividBuildGBufferSurfaceData

#include "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/Closure/ExperimentalClosure.hlsl"

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

VividGBufferSurfaceData VividBuildGBufferSurfaceData(FragInputs input)
{
    VividExperimentalStandardSurface surface = BuildExperimentalStandardLitSurface(input);
    VividExperimentalClosureMaterial material =
        VividCompileExperimentalStandardSurface(surface);
    return VividExportExperimentalClosureToLegacyGBuffer(material);
}

#endif
