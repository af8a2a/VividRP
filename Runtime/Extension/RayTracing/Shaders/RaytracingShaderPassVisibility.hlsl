
#ifndef RAYTRACING_SHADERPASS_VISIBILITY_INCLUDED
#define RAYTRACING_SHADERPASS_VISIBILITY_INCLUDED

float3 TransformPreviousObjectToWorld(float3 positionOS)
{
    return mul(UNITY_PREV_MATRIX_M,  float4(positionOS, 1.0)).xyz;
}


// Generic function that handles the reflection code
[shader("closesthit")]
void ClosestHitMain(inout RayIntersectionVisibility rayIntersection : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
{
    // Make sure to add the additional travel distance
    rayIntersection.t = RayTCurrent();

    // Hit point data.
    IntersectionVertex currentVertex;
    FragInputs fragInput;
    GetCurrentVertexAndBuildFragInputs(attributeData, currentVertex, fragInput);
    PositionInputs posInput = GetPositionInput(rayIntersection.pixelCoord, _ScreenSize.zw, fragInput.positionRWS);

    float3 positionOS = ObjectRayOrigin() + ObjectRayDirection() * rayIntersection.t;

    float3 previousPositionWS = TransformPreviousObjectToWorld(positionOS);

    rayIntersection.velocity = saturate(length(previousPositionWS - fragInput.positionRWS));
    rayIntersection.color.x = 0;
}

// Generic function that handles the reflection code
[shader("anyhit")]
void AnyHitMain(inout RayIntersectionVisibility rayIntersection : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
{
    rayIntersection.color.x = 0;
    IgnoreHit();
}

#endif /* RAYTRACING_SHADERPASS_VISIBILITY_INCLUDED */