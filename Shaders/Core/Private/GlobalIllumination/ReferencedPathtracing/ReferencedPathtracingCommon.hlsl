#ifndef VIVIDRP_REFERENCED_PATH_TRACING_COMMON_INCLUDED
#define VIVIDRP_REFERENCED_PATH_TRACING_COMMON_INCLUDED

float4 _ReferencedMainLightDirectionWS;
float4 _ReferencedMainLightColor;

struct ReferencedPathtracingPayload
{
    // Raygen inputs consumed by closest-hit.
    float3 pathThroughput;
    float3 bsdfRandom;
    float rayConeWidth;
    float rayConeSpreadAngle;

    // Compact closest-hit outputs consumed by the iterative path loop.
    float3 positionWS;
    float3 faceNormalWS;
    float3 emission;
    float3 mainLightDiffuseBsdf;
    float3 mainLightSpecularBsdf;
    float3 nextDirectionWS;
    float3 nextThroughputWeight;
    float nextPdf;
    float linearRoughness;
    float hitDistance;
    uint nextLobeClass;
    uint hit;
};

void InitializeReferencedPathtracingPayload(out ReferencedPathtracingPayload payload)
{
    payload.pathThroughput = 1.0;
    payload.bsdfRandom = 0.0;
    payload.rayConeWidth = 0.0;
    payload.rayConeSpreadAngle = 0.0;
    payload.positionWS = 0.0;
    payload.faceNormalWS = 0.0;
    payload.emission = 0.0;
    payload.mainLightDiffuseBsdf = 0.0;
    payload.mainLightSpecularBsdf = 0.0;
    payload.nextDirectionWS = 0.0;
    payload.nextThroughputWeight = 0.0;
    payload.nextPdf = 0.0;
    payload.linearRoughness = 1.0;
    payload.hitDistance = 0.0;
    payload.nextLobeClass = 0u;
    payload.hit = 0u;
}

#endif
