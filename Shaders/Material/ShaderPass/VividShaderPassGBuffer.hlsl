#ifndef VIVIDRP_SHADER_PASS_GBUFFER_INCLUDED
#define VIVIDRP_SHADER_PASS_GBUFFER_INCLUDED

#if !defined(VIVIDRP_SHADERPASS_GBUFFER) && !defined(VIVIDRP_SHADERPASS_DEBUG)
#error VividShaderPassGBuffer requires VIVIDRP_SHADERPASS_GBUFFER or VIVIDRP_SHADERPASS_DEBUG.
#endif

#include "Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/VividVertMesh.hlsl"

VividPackedVaryingsMesh Vert(VividAttributesMesh input)
{
    return PackVividVaryingsMesh(VividVertMesh(input));
}

VividGBufferFragmentOutput FragGBuffer(VividPackedVaryingsMesh packedInput)
{
    UNITY_SETUP_INSTANCE_ID(packedInput);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(packedInput);

    FragInputs input = UnpackVividVaryingsMeshToFragInputs(packedInput);
    VividGBufferSurfaceData surfaceData = VividBuildGBufferSurfaceData(input);
#if defined(VIVIDRP_GPU_DRIVEN_DECAL_GBUFFER)
    ApplyVividGPUDrivenDecalsToGBufferSurfaceData(surfaceData, input.positionRWS, (uint2)input.positionSS.xy);
#endif
    return PackVividGBufferSurfaceData(surfaceData);
}

half4 FragDebug(VividPackedVaryingsMesh packedInput) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(packedInput);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(packedInput);

    FragInputs input = UnpackVividVaryingsMeshToFragInputs(packedInput);
    return half4(VividGetDebugColor(input), 1.0);
}

#endif
