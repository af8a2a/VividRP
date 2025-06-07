
#ifndef RAYTRACING_SHADERPASS_VISIBILITY_INCLUDED
#define RAYTRACING_SHADERPASS_VISIBILITY_INCLUDED

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




    // float2 uv = fragInput.texCoord0.xy;
    // uv = TRANSFORM_TEX(uv, _BaseMap);
    // float4 albedoAlpha = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uv, 0);
    // float alpha = Alpha(albedoAlpha.a, _BaseColor, _Cutoff);
    // float3 albedo = albedoAlpha.rgb * _BaseColor.rgb;

    rayIntersection.color.x = 0;
}

// Generic function that handles the reflection code
[shader("anyhit")]
void AnyHitMain(inout RayIntersectionVisibility rayIntersection : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
{
    rayIntersection.color.x = 0.5;
    IgnoreHit();
}

#endif /* RAYTRACING_SHADERPASS_VISIBILITY_INCLUDED */