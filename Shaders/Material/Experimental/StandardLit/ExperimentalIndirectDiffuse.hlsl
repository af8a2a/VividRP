#ifndef VIVIDRP_EXPERIMENTAL_INDIRECT_DIFFUSE_INCLUDED
#define VIVIDRP_EXPERIMENTAL_INDIRECT_DIFFUSE_INCLUDED

#define VIVIDRP_INDIRECT_DIFFUSE_DEFINE_RAYTRACING_SHADERS 0
#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/IndirectDiffuse.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/Closure/ExperimentalClosure.hlsl"

VividExperimentalStandardSurfaceParameters SampleExperimentalStandardLitSurface(
    VividIndirectDiffuseHitGeometry geometry,
    out float4 baseSample)
{
    baseSample = SampleBase(geometry.uv);
    float metallic;
    float smoothness;
    float ambientOcclusion;
    SampleStandardLitPBR(
        geometry.uv,
        baseSample.a,
        0.0,
        metallic,
        smoothness,
        ambientOcclusion);

    VividExperimentalStandardSurfaceParameters parameters;
    parameters.baseColor = baseSample.rgb;
    parameters.normalWS = VividIndirectDiffuseSampleNormalWS(geometry);
    parameters.perceptualRoughness = 1.0 - smoothness;
    parameters.metallic = metallic;
    parameters.ambientOcclusion = ambientOcclusion;
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

    parameters.emissive = SampleEmission(geometry.uv);
    parameters.materialFeatures = VIVID_MATERIALFEATURE_LIT;
    if (_ReceiveSSR > 0.5)
        parameters.materialFeatures |= VIVID_MATERIALFEATURE_SSR_RECEIVE;
    if (_ReceiveDecals > 0.5)
        parameters.materialFeatures |= VIVID_MATERIALFEATURE_DECAL_RECEIVE;
    if (parameters.clearCoatWeight > 0.0)
        parameters.materialFeatures |= VIVID_MATERIALFEATURE_CLEAR_COAT;

    parameters.builtinData = CreateVividBuiltinData(
        SampleStandardLitIndirectDiffuseBakedGI(
            geometry,
            parameters.normalWS),
        HasStandardLitIndirectDiffuseBakedGI(),
        0.0,
        1.0);
    return parameters;
}

VividGBufferSurfaceData BuildExperimentalStandardLitSurfaceData(
    VividIndirectDiffuseHitGeometry geometry,
    out float4 baseSample)
{
    VividExperimentalStandardSurface surface =
        VividResolveExperimentalStandardSurface(
            SampleExperimentalStandardLitSurface(geometry, baseSample));
    VividExperimentalClosureMaterial material =
        VividCompileExperimentalStandardSurface(surface);
    return VividExportExperimentalClosureToLegacyGBuffer(material);
}

void VividExperimentalIndirectDiffuseClosestHit(
    AttributeData attributeData,
    inout VividIndirectDiffusePayload payload)
{
    if (VividIndirectDiffuseIsVisibilityTrace(payload))
    {
        payload.hit = 1u;
        payload.signedHitDistance = RayTCurrent();
        return;
    }

    VividIndirectDiffuseHitGeometry geometry =
        VividIndirectDiffuseBuildHitGeometry(attributeData);
    float4 baseSample;
    VividGBufferSurfaceData surfaceData =
        BuildExperimentalStandardLitSurfaceData(geometry, baseSample);
    VividIndirectDiffuseWritePayload(geometry, surfaceData, payload);
}

#if !defined(SHADERSTAGE_RGS)
[shader("closesthit")]
void VIVIDRP_INDIRECT_DIFFUSE_CLOSEST_HIT_NAME(
    inout VividIndirectDiffusePayload payload : SV_RayPayload,
    AttributeData attributeData : SV_IntersectionAttributes)
{
    VividExperimentalIndirectDiffuseClosestHit(attributeData, payload);
}

[shader("anyhit")]
void VIVIDRP_INDIRECT_DIFFUSE_ANY_HIT_NAME(
    inout VividIndirectDiffusePayload payload : SV_RayPayload,
    AttributeData attributeData : SV_IntersectionAttributes)
{
    uint result = VividIndirectDiffuseAnyHit(attributeData);

    if (result == VIVID_RAYTRACING_HIT_ACCEPT
        && VividIndirectDiffuseIsVisibilityTrace(payload))
    {
        payload.hit = 1u;
        payload.signedHitDistance = RayTCurrent();
        AcceptHitAndEndSearch();
        return;
    }

    if (result == VIVID_RAYTRACING_HIT_IGNORE)
        IgnoreHit();
    else if (result == VIVID_RAYTRACING_HIT_ACCEPT_AND_END_SEARCH)
        AcceptHitAndEndSearch();
}
#endif

#endif
