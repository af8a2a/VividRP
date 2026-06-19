#ifndef VIVIDRP_SHADER_PASS_DEPTH_ONLY_INCLUDED
#define VIVIDRP_SHADER_PASS_DEPTH_ONLY_INCLUDED

#if !defined(VIVIDRP_SHADERPASS_DEPTH_ONLY)
#error VividShaderPassDepthOnly requires VIVIDRP_SHADERPASS_DEPTH_ONLY.
#endif

#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/VividVertMesh.hlsl"

VividPackedVaryingsMesh Vert(VividAttributesMesh input)
{
    return PackVividVaryingsMesh(VividVertMesh(input));
}

half4 FragPreDepth(VividPackedVaryingsMesh packedInput) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(packedInput);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(packedInput);

    FragInputs input = UnpackVividVaryingsMeshToFragInputs(packedInput);
    VividApplyAlphaClip(input);
    return 0.0;
}

#endif
