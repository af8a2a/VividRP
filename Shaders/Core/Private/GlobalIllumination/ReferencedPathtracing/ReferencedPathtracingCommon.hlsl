#ifndef VIVIDRP_REFERENCED_PATH_TRACING_COMMON_INCLUDED
#define VIVIDRP_REFERENCED_PATH_TRACING_COMMON_INCLUDED

struct ReferencedPathtracingPayload
{
    float3 positionWS;
    float3 normalWS;
    float3 faceNormalWS;
    float3 diffuse;
    float rayConeSpreadAngle;
    uint hit;
};

void InitializeReferencedPathtracingPayload(out ReferencedPathtracingPayload payload)
{
    payload.positionWS = 0.0;
    payload.normalWS = 0.0;
    payload.faceNormalWS = 0.0;
    payload.diffuse = 0.0;
    payload.rayConeSpreadAngle = 0.0;
    payload.hit = 0u;
}

#endif
