#ifndef VIVIDRP_REFERENCED_PATH_TRACING_MATERIAL_PASS_INCLUDED
#define VIVIDRP_REFERENCED_PATH_TRACING_MATERIAL_PASS_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingCommon.hlsl"

#define VIVIDRP_INDIRECT_DIFFUSE_DEFINE_RAYTRACING_SHADERS 0
#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/IndirectDiffuse.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/StandardLitOpenPBRAdapter.hlsl"

static const float kReferencedPathtracingTextureLodBias = 0.5;

float3 ReferencedPathtracingTransformPositionToWorld(float3 positionOS)
{
    return mul(ObjectToWorld3x4(), float4(positionOS, 1.0));
}

float ComputeReferencedPathtracingTextureBaseLambda(
    VividIndirectDiffuseHitGeometry geometry,
    float rayConeWidth,
    float rayConeSpreadAngle,
    out float hitConeWidth)
{
    uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());

    float3 position0WS = ReferencedPathtracingTransformPositionToWorld(
        UnityRayTracingFetchVertexAttribute3(triangleIndices.x, kVertexAttributePosition));
    float3 position1WS = ReferencedPathtracingTransformPositionToWorld(
        UnityRayTracingFetchVertexAttribute3(triangleIndices.y, kVertexAttributePosition));
    float3 position2WS = ReferencedPathtracingTransformPositionToWorld(
        UnityRayTracingFetchVertexAttribute3(triangleIndices.z, kVertexAttributePosition));
    float triangleAreaWS = length(cross(position1WS - position0WS, position2WS - position0WS));

    float2 uv0 = UnityRayTracingFetchVertexAttribute4(triangleIndices.x, kVertexAttributeTexCoord0).xy
        * _BaseMap_ST.xy;
    float2 uv1 = UnityRayTracingFetchVertexAttribute4(triangleIndices.y, kVertexAttributeTexCoord0).xy
        * _BaseMap_ST.xy;
    float2 uv2 = UnityRayTracingFetchVertexAttribute4(triangleIndices.z, kVertexAttributeTexCoord0).xy
        * _BaseMap_ST.xy;
    float2 uvEdge1 = uv1 - uv0;
    float2 uvEdge2 = uv2 - uv0;
    float triangleAreaUV = abs(uvEdge1.x * uvEdge2.y - uvEdge1.y * uvEdge2.x);

    hitConeWidth = max(rayConeWidth + geometry.hitDistance * rayConeSpreadAngle, 0.000001);
    return computeBaseTextureLOD(
        WorldRayDirection(),
        geometry.faceNormalWS,
        hitConeWidth,
        max(triangleAreaUV, 0.000000000001),
        max(triangleAreaWS, 0.000000000001));
}

bool VividReferencedPathtracingIsFinite(float value)
{
    return !isnan(value) && !isinf(value);
}

bool VividReferencedPathtracingIsFinite(float3 value)
{
    return !any(isnan(value)) && !any(isinf(value));
}

[shader("closesthit")]
void StandardLitReferencedPathtracingClosestHit(
    inout ReferencedPathtracingPayload payload : SV_RayPayload,
    AttributeData attributeData : SV_IntersectionAttributes)
{
    VividIndirectDiffuseHitGeometry geometry = VividIndirectDiffuseBuildHitGeometry(attributeData);
    float hitConeWidth;
    float textureBaseLambda = ComputeReferencedPathtracingTextureBaseLambda(
        geometry,
        payload.rayConeWidth,
        payload.rayConeSpreadAngle,
        hitConeWidth);
    float baseTextureLod = max(
        computeTargetTextureLOD(_BaseMap, textureBaseLambda) + kReferencedPathtracingTextureLodBias,
        0.0);
    float normalTextureLod = 0.0;
#if defined(_NORMALMAP)
    normalTextureLod = max(
        computeTargetTextureLOD(_BumpMap, textureBaseLambda) + kReferencedPathtracingTextureLodBias,
        0.0);
#endif
    VividReferencedPathtracingMaterial material = VividReferencedPathtracingResolveStandardLitOpenPBR(
        geometry,
        textureBaseLambda,
        baseTextureLod,
        normalTextureLod);

    float3 viewDirectionWS = normalize(-WorldRayDirection());
    OpenPBR_PreparedBsdf preparedBsdf = openpbr_prepare(
        material.openPbrInputs,
        max(payload.pathThroughput, 0.0),
        OpenPBR_BaseRgbWavelengths_nm,
        OpenPBR_VacuumIor,
        viewDirectionWS);

    payload.positionWS = geometry.positionWS;
    payload.faceNormalWS = geometry.faceNormalWS;
    payload.rayConeWidth = hitConeWidth;
    payload.emission = VividReferencedPathtracingIsFinite(preparedBsdf.emission)
        ? max(preparedBsdf.emission, 0.0)
        : 0.0;
    payload.mainLightDiffuseBsdf = 0.0;
    payload.mainLightSpecularBsdf = 0.0;
    payload.nextDirectionWS = 0.0;
    payload.nextThroughputWeight = 0.0;
    payload.nextPdf = 0.0;
    payload.linearRoughness = material.openPbrInputs.specular_roughness;
    payload.hitDistance = geometry.hitDistance;
    payload.nextLobeClass = 0u;

    float3 mainLightDirectionWS = normalize(_ReferencedMainLightDirectionWS.xyz);
    if (dot(mainLightDirectionWS, geometry.faceNormalWS) > 0.0
        && any(_ReferencedMainLightColor.rgb > 0.0))
    {
        // openpbr_eval returns f(wo, wi) * abs(NdotL). The ray-generation stage multiplies
        // this response by _ReferencedMainLightColor, whose RGB value is illuminance in lux.
        OpenPBR_DiffuseSpecular mainLightResponse = openpbr_eval(
            preparedBsdf,
            mainLightDirectionWS);
        float mainLightBsdfPdf = openpbr_pdf(preparedBsdf, mainLightDirectionWS);
        float3 mainLightDiffuseBsdf = openpbr_extract_diffuse_from_diffuse_specular(mainLightResponse);
        float3 mainLightSpecularBsdf = openpbr_extract_specular_from_diffuse_specular(mainLightResponse);
        if (VividReferencedPathtracingIsFinite(mainLightDiffuseBsdf)
            && VividReferencedPathtracingIsFinite(mainLightSpecularBsdf)
            && VividReferencedPathtracingIsFinite(mainLightBsdfPdf)
            && mainLightBsdfPdf >= 0.0)
        {
            payload.mainLightDiffuseBsdf = max(mainLightDiffuseBsdf, 0.0);
            payload.mainLightSpecularBsdf = max(mainLightSpecularBsdf, 0.0);
        }
    }

    float3 sampledDirectionWS;
    OpenPBR_DiffuseSpecular sampledWeight;
    float sampledPdf;
    OpenPBR_BsdfLobeType sampledLobeType;
    openpbr_sample(
        preparedBsdf,
        saturate(payload.bsdfRandom),
        sampledDirectionWS,
        sampledWeight,
        sampledPdf,
        sampledLobeType);

    if (sampledPdf > 0.0
        && VividReferencedPathtracingIsFinite(sampledPdf)
        && VividReferencedPathtracingIsFinite(sampledDirectionWS))
    {
        float3 nextThroughputWeight = openpbr_get_sum_of_diffuse_specular(sampledWeight);
        if (VividReferencedPathtracingIsFinite(nextThroughputWeight)
            && dot(sampledDirectionWS, geometry.faceNormalWS) > 0.000001)
        {
            payload.nextDirectionWS = normalize(sampledDirectionWS);
            payload.nextThroughputWeight = max(nextThroughputWeight, 0.0);
            payload.nextPdf = sampledPdf;
            payload.nextLobeClass = (sampledLobeType & OpenPBR_BsdfLobeTypeDiffuse) != 0u
                ? 1u
                : 2u;
        }
    }

    payload.hit = 1u;
}

[shader("anyhit")]
void StandardLitReferencedPathtracingAnyHit(
    inout ReferencedPathtracingPayload payload : SV_RayPayload,
    AttributeData attributeData : SV_IntersectionAttributes)
{
#if defined(_ALPHATEST_ON)
    float2 uv = VividIndirectDiffuseFetchUV(attributeData);
    if (VividIndirectDiffuseIsAlphaClipped(SampleBase(uv).a))
        IgnoreHit();
#endif
}

#endif
