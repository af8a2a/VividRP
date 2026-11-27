#ifndef VIVIDRP_SHADER_PASS_GBUFFER_INCLUDED
#define VIVIDRP_SHADER_PASS_GBUFFER_INCLUDED

#if !defined(VIVIDRP_SHADERPASS_GBUFFER) && !defined(VIVIDRP_SHADERPASS_DEBUG)
#error VividShaderPassGBuffer requires VIVIDRP_SHADERPASS_GBUFFER or VIVIDRP_SHADERPASS_DEBUG.
#endif

#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/VividVertMesh.hlsl"

#if defined(VIVIDRP_SUBSTRATE_GBUFFER_OUTPUT)
uint _WriteSubstrateClosureBuffer;
#if defined(VIVID_VT_ENABLE_FEEDBACK_RW)
RWTexture2DArray<uint> _OutSubstrateClosureBuffer : register(u8);
#else
RWTexture2DArray<uint> _OutSubstrateClosureBuffer : register(u5);
#endif
#endif

VividPackedVaryingsMesh Vert(VividAttributesMesh input)
{
    return PackVividVaryingsMesh(VividVertMesh(input));
}

#if defined(VIVIDRP_SUBSTRATE_GBUFFER_OUTPUT)
#if !defined(_ALPHATEST_ON)
[earlydepthstencil]
#endif
VividSurfaceSummaryGBufferOutput FragGBuffer(VividPackedVaryingsMesh packedInput)
{
    UNITY_SETUP_INSTANCE_ID(packedInput);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(packedInput);

    FragInputs input = UnpackVividVaryingsMeshToFragInputs(packedInput);
    VividStandardLitSurfaceParams params;
    float ambientOcclusion;
    bool receiveSSR;
    bool receiveDecals;
    VividBuildStandardLitSubstrateSurface(
        input,
        params,
        ambientOcclusion,
        receiveSSR,
        receiveDecals);

    uint2 pixelCoord = (uint2)input.positionSS.xy;
#if defined(VIVIDRP_GPU_DRIVEN_DECAL_GBUFFER)
    ApplyVividGPUDrivenDecalsToStandardLitParams(params, receiveDecals, input.positionRWS, pixelCoord);
#endif

    VividOpenPBRV2Inputs v2Inputs = VividStandardLitToOpenPBRV2Inputs(params);
#if defined(VIVID_SLAB_HAS_CUSTOM_POST_SURFACE) && defined(VIVIDRP_SLAB_SURFACE_CONTRACT_INCLUDED)
    {
        VividSlabEvalContext slabCtx;
        slabCtx.uv0 = input.texCoord0.xy;
        slabCtx.uv0Ddx = ddx(input.texCoord0.xy);
        slabCtx.uv0Ddy = ddy(input.texCoord0.xy);
        slabCtx.geometryNormalWS = SafeNormalize(input.tangentToWorld[2]);
        slabCtx.geometryTangentWS = float4(SafeNormalize(input.tangentToWorld[0]), 1.0f);
        slabCtx.positionWS = input.positionRWS;
        slabCtx.positionCS = input.positionSS;
        slabCtx.parameterAddress = 0u;
        slabCtx.resourceAddress = 0u;
        slabCtx.materialIndex = 0u;
        VividSlabPostEvaluateSurface(slabCtx, v2Inputs, ambientOcclusion);
    }
#endif
    if (_WriteSubstrateClosureBuffer != 0u)
    {
        uint headerFlags = 0u;
        if (receiveDecals)
            headerFlags |= VIVID_SUBSTRATE_FLAG_RECEIVE_DECALS;
        if (any(v2Inputs.core.emissionLuminance * v2Inputs.core.emissionColor > 0.0f))
            headerFlags |= VIVID_SUBSTRATE_FLAG_HAS_EMISSION;
        VividWriteSubstrateOpenPBRV2Single(
            _OutSubstrateClosureBuffer,
            pixelCoord,
            v2Inputs,
            headerFlags);
    }

    VividSurfaceSummaryData summary = VividBuildStandardLitSubstrateTopLayerSummary(
        v2Inputs,
        ambientOcclusion,
        receiveSSR,
        receiveDecals);
    return VividPackSurfaceSummaryGBuffer(summary);
}
#else
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
#endif

half4 FragDebug(VividPackedVaryingsMesh packedInput) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(packedInput);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(packedInput);

    FragInputs input = UnpackVividVaryingsMeshToFragInputs(packedInput);
    return half4(VividGetDebugColor(input), 1.0);
}

#endif
