#ifndef VIVIDRP_REFERENCED_PATH_TRACING_MATERIAL_PASS_INCLUDED
#define VIVIDRP_REFERENCED_PATH_TRACING_MATERIAL_PASS_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingCommon.hlsl"

[shader("closesthit")]
void StandardLitReferencedPathtracingClosestHit(
    inout ReferencedPathtracingPayload payload : SV_RayPayload,
    ReferencedPathtracingAttributeData attributeData : SV_IntersectionAttributes)
{
    payload.positionWS = WorldRayOrigin() + WorldRayDirection() * RayTCurrent();
    payload.hit = 1u;
}

#endif
