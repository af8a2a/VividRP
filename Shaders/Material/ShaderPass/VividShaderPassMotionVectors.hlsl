#ifndef VIVIDRP_SHADER_PASS_MOTION_VECTORS_INCLUDED
#define VIVIDRP_SHADER_PASS_MOTION_VECTORS_INCLUDED

#if !defined(VIVIDRP_SHADERPASS_MOTION_VECTORS)
#error VividShaderPassMotionVectors requires VIVIDRP_SHADERPASS_MOTION_VECTORS.
#endif

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/MotionVectorsCommon.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/VividVertMesh.hlsl"

VividPackedVaryingsMesh Vert(VividAttributesMesh input)
{
    return PackVividVaryingsMesh(VividVertMesh(input));
}

float4 Frag(VividPackedVaryingsMesh packedInput) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(packedInput);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(packedInput);

    FragInputs input = UnpackVividVaryingsMeshToFragInputs(packedInput);
    VividApplyAlphaClip(input);
    return EncodeMotionVectorFromCsPositions(packedInput.positionCSNoJitter, packedInput.previousPositionCSNoJitter);
}

#endif
