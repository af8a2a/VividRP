#ifndef VIVIDRP_SHADER_PASS_SHADOW_CASTER_INCLUDED
#define VIVIDRP_SHADER_PASS_SHADOW_CASTER_INCLUDED

#if !defined(VIVIDRP_SHADERPASS_SHADOW_CASTER)
#error VividShaderPassShadowCaster requires VIVIDRP_SHADERPASS_SHADOW_CASTER.
#endif

#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/VividVertMesh.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Shadow/VividVirtualShadowMapCaster.hlsl"

float4 _ShadowBias;

float4 ApplyVividShadowClamping(float4 positionCS)
{
#if UNITY_REVERSED_Z
    float clampedZ = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
#else
    float clampedZ = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
#endif

    positionCS.z = lerp(positionCS.z, clampedZ, round(_ShadowBias.z) == 1.0 ? 1.0 : 0.0);
    return positionCS;
}

VividPackedVaryingsMesh Vert(VividAttributesMesh input)
{
    VividVaryingsMesh output = VividVertMesh(input);
    output.positionCS = ApplyVividShadowClamping(output.positionCS);
    return PackVividVaryingsMesh(output);
}

#if defined(VIVID_VSM_CASTER)
void Frag(VividPackedVaryingsMesh packedInput)
#else
half4 Frag(VividPackedVaryingsMesh packedInput) : SV_Target
#endif
{
    UNITY_SETUP_INSTANCE_ID(packedInput);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(packedInput);

    FragInputs input = UnpackVividVaryingsMeshToFragInputs(packedInput);
    VividApplyAlphaClip(input);
    VividWriteVSMDepth(packedInput.positionCS);
#if !defined(VIVID_VSM_CASTER)
    return 0.0;
#endif
}

#endif
