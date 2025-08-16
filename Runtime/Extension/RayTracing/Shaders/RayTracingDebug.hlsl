// We need only need 1 bounce for RTAS Debug
#pragma max_recursion_depth 1


#pragma use_dxc
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

#include "ShaderVariablesRaytracing.hlsl"
#include "RaytracingIntersection.hlsl"

// Output structure of the reflection raytrace shader
int _LayerMask;
int _DebugMode;
float4x4 _PixelCoordToViewDirWS;
RW_TEXTURE2D(float4, _OutputDebugBuffer);

[shader("miss")]
void MissShaderDebug(inout RayIntersectionDebug rayIntersection : SV_RayPayload)
{
    rayIntersection.t = FLT_INF;
}




float3 IntToColor(uint val)
{
    int r = (val & 0xFF0000) >> 16;
    int g = (val & 0x00FF00) >> 8;
    int b = val & 0x0000FF;
    return float3(r / 255.0, g / 255.0, b / 255.0);
}


float3 GetPrimaryCameraPosition()
{
    return _WorldSpaceCameraPos;
}


[shader("raygeneration")]
void RTASDebug()
{
    
    uint3 LaunchIndex = DispatchRaysIndex();
    
    
    // Pixel coordinate of the current pixel
    uint2 currentPixelCoord = uint2(LaunchIndex.x, LaunchIndex.y);
    
    
    // Create the ray descriptor for this pixel
    RayDesc rayDescriptor;
    rayDescriptor.TMin = 0.01;
    rayDescriptor.TMax = FLT_INF;
    rayDescriptor.Origin = GetCameraPositionWS();
    rayDescriptor.Direction = -normalize(mul(float4(currentPixelCoord, 1.0, 1.0), _PixelCoordToViewDirWS).xyz);
    

    
    // Create and init the RayIntersection structure for this
    RayIntersectionDebug rayIntersection;
    rayIntersection.t = 0.01;
    rayIntersection.instanceIndex = 0;
    rayIntersection.primitiveIndex = 0;

    
    // Evaluate the ray intersection
    TraceRay(_RaytracingAccelerationStructure, RAY_FLAG_CULL_BACK_FACING_TRIANGLES | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH, _LayerMask,
        0, 1, 0, rayDescriptor, rayIntersection);
    
    // Compute the debug ID to use based on the mode
    uint debugID = _DebugMode == 0 ? rayIntersection.instanceIndex + 1 : rayIntersection.primitiveIndex + 1;
    float3 debugColor = rayIntersection.t != FLT_INF ? IntToColor(JenkinsHash(debugID)): 0.0;
    
    // Alright we are done
    _OutputDebugBuffer[currentPixelCoord] = float4(debugColor, 1.0);
}
