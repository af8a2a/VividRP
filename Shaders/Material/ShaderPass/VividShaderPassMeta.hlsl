#ifndef VIVIDRP_SHADER_PASS_META_INCLUDED
#define VIVIDRP_SHADER_PASS_META_INCLUDED

#if !defined(VIVIDRP_SHADERPASS_META)
#error VividShaderPassMeta requires VIVIDRP_SHADERPASS_META.
#endif

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/MetaPass.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/VividVertMesh.hlsl"

VividPackedVaryingsMesh Vert(VividAttributesMesh input)
{
    return PackVividVaryingsMesh(VividVertMesh(input));
}

float4 Frag(VividPackedVaryingsMesh packedInput) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(packedInput);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(packedInput);

    FragInputs input = UnpackVividVaryingsMeshToFragInputs(packedInput);
    return UnityMetaFragment(VividBuildMetaInput(input));
}

#endif
