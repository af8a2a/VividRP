

// Generic function that handles the reflection code
[shader("closesthit")]
void ClosestHitDebug(inout RayIntersectionDebug rayIntersection : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
{

    rayIntersection.t = RayTCurrent();
    rayIntersection.barycentrics = attributeData.barycentrics;
    rayIntersection.primitiveIndex = PrimitiveIndex();
    rayIntersection.instanceIndex = InstanceIndex();
}

// Generic function that handles the reflection code
[shader("anyhit")]
void AnyHitDebug(inout RayIntersectionDebug rayIntersection : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
{

    // Debug data
    rayIntersection.t = RayTCurrent();
    rayIntersection.barycentrics = attributeData.barycentrics;
    rayIntersection.primitiveIndex = PrimitiveIndex();
    rayIntersection.instanceIndex = InstanceIndex();
}

