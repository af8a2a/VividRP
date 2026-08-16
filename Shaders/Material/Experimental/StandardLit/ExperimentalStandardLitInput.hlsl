#ifndef VIVIDRP_EXPERIMENTAL_STANDARD_LIT_INPUT_INCLUDED
#define VIVIDRP_EXPERIMENTAL_STANDARD_LIT_INPUT_INCLUDED

// Reuse the proven StandardLit sampling helpers without changing the existing
// material. Rename its legacy entry point so this shader can provide the
// experimental closure entry point expected by the shared GBuffer pass.
#define VividBuildGBufferSurfaceData VividBuildLegacyStandardLitGBufferSurfaceData
#include "Packages/com.vivid.render-pipelines/Shaders/Material/StandardLit/StandardLitInput.hlsl"
#undef VividBuildGBufferSurfaceData

#include "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/Closure/ExperimentalClosure.hlsl"

VividExperimentalStandardSurface BuildExperimentalStandardLitSurface(
    FragInputs input)
{
    float2 uv = GetStandardLitBaseUV(input);
    float4 baseSample = SampleBase(uv, input.positionSS);
    ApplyAlphaClip(baseSample.a);

    float2 metallicSmoothness = SampleMetallicSmoothness(uv, baseSample.a);

    VividExperimentalStandardSurface surface;
    surface.baseColor = baseSample.rgb;
    surface.normalWS = SampleNormalWS(input, uv);
    surface.linearRoughness = (1.0 - metallicSmoothness.y) * (1.0 - metallicSmoothness.y);
    surface.metallic = metallicSmoothness.x;
    surface.ambientOcclusion = SampleAmbientOcclusion(uv);
    surface.coverage = 1.0;

#if defined(_CLEARCOAT)
    surface.clearCoatWeight = saturate(_ClearCoatMask);
    float clearCoatPerceptualRoughness = 1.0 - saturate(_ClearCoatSmoothness);
    surface.clearCoatLinearRoughness = max(
        clearCoatPerceptualRoughness * clearCoatPerceptualRoughness,
        0.0001);
#else
    surface.clearCoatWeight = 0.0;
    surface.clearCoatLinearRoughness = 0.0001;
#endif

    surface.materialFeatures = GetStandardLitMaterialFeatures(surface.clearCoatWeight);
    surface.emissive = SampleEmission(uv);

    float2 lightmapUV = TransformVividLightmapUV(input.texCoord1.xy);
    surface.builtinData = BuildVividBuiltinData(
        SampleStandardLitBakedGI(lightmapUV, surface.normalWS, input.positionRWS),
        HasStandardLitBakedGI(),
        lightmapUV,
        input.positionRWS);
    return surface;
}

VividGBufferSurfaceData VividBuildGBufferSurfaceData(FragInputs input)
{
    VividExperimentalStandardSurface surface = BuildExperimentalStandardLitSurface(input);
    VividExperimentalClosureMaterial material =
        VividCompileExperimentalStandardSurface(surface);
    return VividExportExperimentalClosureToLegacyGBuffer(material);
}

#endif
