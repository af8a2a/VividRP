#ifndef VIVIDRP_RAYTRACING_GBUFFER_COMMON_INCLUDED
#define VIVIDRP_RAYTRACING_GBUFFER_COMMON_INCLUDED

struct RaytracingGBufferPayload
{
    float rayConeWidth;
    float rayConeSpreadAngle;

    float3 positionWS;
    float3 previousPositionWS;
    float3 faceNormalWS;
    float3 shadingNormalWS;
    float3 baseColor;
    float3 emission;
    float3 nrdDiffuseMaterialFactor;
    float3 nrdSpecularMaterialFactor;
    float3 diffuseAlbedo;
    float3 specularAlbedo;
    float linearRoughness;
    float metalness;
    float hitDistance;
    uint materialAlbedoValid;
    uint materialPreviousPositionValid;
    uint hit;
};

void InitializeRaytracingGBufferPayload(out RaytracingGBufferPayload payload)
{
    payload.rayConeWidth = 0.0;
    payload.rayConeSpreadAngle = 0.0;
    payload.positionWS = 0.0;
    payload.previousPositionWS = 0.0;
    payload.faceNormalWS = 0.0;
    payload.shadingNormalWS = float3(0.0, 1.0, 0.0);
    payload.baseColor = 0.0;
    payload.emission = 0.0;
    payload.nrdDiffuseMaterialFactor = 1.0;
    payload.nrdSpecularMaterialFactor = 1.0;
    payload.diffuseAlbedo = 0.0;
    payload.specularAlbedo = 0.0;
    payload.linearRoughness = 1.0;
    payload.metalness = 0.0;
    payload.hitDistance = 0.0;
    payload.materialAlbedoValid = 0u;
    payload.materialPreviousPositionValid = 0u;
    payload.hit = 0u;
}

#endif
