#ifndef VIVIDRP_RAYTRACING_GBUFFER_MATERIAL_PASS_INCLUDED
#define VIVIDRP_RAYTRACING_GBUFFER_MATERIAL_PASS_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/RaytracingGBufferCommon.hlsl"

#define VIVIDRP_INDIRECT_DIFFUSE_DEFINE_RAYTRACING_SHADERS 0
#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/IndirectDiffuse.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/StandardLitOpenPBRAdapter.hlsl"

static const float kRaytracingGBufferTextureLodBias = 0.5;

float3 RaytracingGBufferTransformPositionToWorld(float3 positionOS)
{
    return mul(ObjectToWorld3x4(), float4(positionOS, 1.0));
}

float ComputeRaytracingGBufferTextureBaseLambda(
    VividIndirectDiffuseHitGeometry geometry,
    float rayConeWidth,
    float rayConeSpreadAngle,
    out float hitConeWidth)
{
    uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());

    float3 position0WS = RaytracingGBufferTransformPositionToWorld(
        UnityRayTracingFetchVertexAttribute3(triangleIndices.x, kVertexAttributePosition));
    float3 position1WS = RaytracingGBufferTransformPositionToWorld(
        UnityRayTracingFetchVertexAttribute3(triangleIndices.y, kVertexAttributePosition));
    float3 position2WS = RaytracingGBufferTransformPositionToWorld(
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

[shader("closesthit")]
void StandardLitRaytracingGBufferClosestHit(
    inout RaytracingGBufferPayload payload : SV_RayPayload,
    AttributeData attributeData : SV_IntersectionAttributes)
{
    VividIndirectDiffuseHitGeometry geometry = VividIndirectDiffuseBuildHitGeometry(attributeData);
    float hitConeWidth;
    float textureBaseLambda = ComputeRaytracingGBufferTextureBaseLambda(
        geometry,
        payload.rayConeWidth,
        payload.rayConeSpreadAngle,
        hitConeWidth);
    float baseTextureLod = max(
        computeTargetTextureLOD(_BaseMap, textureBaseLambda) + kRaytracingGBufferTextureLodBias,
        0.0);
    float normalTextureLod = 0.0;
#if defined(_NORMALMAP)
    normalTextureLod = max(
        computeTargetTextureLOD(_BumpMap, textureBaseLambda) + kRaytracingGBufferTextureLodBias,
        0.0);
#endif

    VividReferencedPathtracingMaterial material = VividReferencedPathtracingResolveStandardLitOpenPBR(
        geometry,
        textureBaseLambda,
        baseTextureLod,
        normalTextureLod);

    payload.rayConeWidth = hitConeWidth;
    payload.positionWS = geometry.positionWS;
    payload.faceNormalWS = geometry.faceNormalWS;
    payload.shadingNormalWS = material.shadingNormalWS;
    payload.baseColor = material.openPbrInputs.base_color;
    payload.emission = material.emission;
    payload.linearRoughness = material.openPbrInputs.specular_roughness;
    payload.metalness = material.openPbrInputs.base_metalness;
    payload.hitDistance = geometry.hitDistance;
    payload.hit = 1u;
}

[shader("anyhit")]
void StandardLitRaytracingGBufferAnyHit(
    inout RaytracingGBufferPayload payload : SV_RayPayload,
    AttributeData attributeData : SV_IntersectionAttributes)
{
#if defined(_ALPHATEST_ON)
    float2 uv = VividIndirectDiffuseFetchUV(attributeData);
    if (VividIndirectDiffuseIsAlphaClipped(SampleBase(uv).a))
        IgnoreHit();
#endif
}

#endif
