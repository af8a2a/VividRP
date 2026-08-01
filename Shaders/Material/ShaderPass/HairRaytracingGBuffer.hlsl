#ifndef VIVIDRP_HAIR_RAYTRACING_GBUFFER_INCLUDED
#define VIVIDRP_HAIR_RAYTRACING_GBUFFER_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/RaytracingGBufferCommon.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Material/Hair/HairChiangAdapter.hlsl"

[shader("closesthit")]
void HairRaytracingGBufferClosestHit(
    inout RaytracingGBufferPayload payload : SV_RayPayload,
    AttributeData attributeData : SV_IntersectionAttributes)
{
    VividHairSurfaceGeometry geometry =
        VividHairBuildDotsSurfaceGeometry(attributeData);
    float3 baseColor = VividHairGetBaseColor();

    payload.rayConeWidth = max(
        payload.rayConeWidth
            + geometry.hitDistance * payload.rayConeSpreadAngle,
        0.000001);
    payload.positionWS = geometry.positionWS;
    payload.faceNormalWS = geometry.faceNormalWS;
    payload.shadingNormalWS = geometry.radialNormalWS;
    payload.baseColor = baseColor;
    payload.emission = VividHairGetEmission();
    payload.nrdDiffuseMaterialFactor = 1.0;
    payload.nrdSpecularMaterialFactor = 1.0;
    payload.diffuseAlbedo = baseColor;
    payload.specularAlbedo = VividHairGetSpecularF0();
    payload.materialAlbedoValid = 1u;
    payload.linearRoughness = VividHairGetLongitudinalRoughness();
    payload.metalness = 0.0;
    payload.hitDistance = geometry.hitDistance;
    payload.hit = 1u;
}

#endif
