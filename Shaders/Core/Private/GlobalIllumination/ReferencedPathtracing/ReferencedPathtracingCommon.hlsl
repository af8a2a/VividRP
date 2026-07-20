#ifndef VIVIDRP_REFERENCED_PATH_TRACING_COMMON_INCLUDED
#define VIVIDRP_REFERENCED_PATH_TRACING_COMMON_INCLUDED

struct ReferencedPathtracingPayload
{
    float3 positionWS;
    uint hit;
};

struct ReferencedPathtracingAttributeData
{
    float2 barycentrics;
};

void InitializeReferencedPathtracingPayload(out ReferencedPathtracingPayload payload)
{
    payload.positionWS = 0.0;
    payload.hit = 0u;
}

#endif
