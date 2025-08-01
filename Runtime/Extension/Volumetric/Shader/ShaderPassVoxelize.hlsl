#ifndef REPLICA_SHADER_PASS_VOXELIZE
#define REPLICA_SHADER_PASS_VOXELIZE

#include "./VolumetricLighting.hlsl"

StructuredBuffer<VolumetricFogRenderingData> _VolumetricFogRenderingData;
ByteAddressBuffer _Indirections;

CBUFFER_START(LocalVolumetricFogData)
float4 _VolumetricFogObbRight;
float4 _VolumetricFogObbUp;
float4 _VolumetricFogObbForward;
float4 _VolumetricFogObbCenter;
float4 _VolumetricFogObbExtents;
float4 _VolumetricFogRcpPosFaceFade;
float4 _VolumetricFogRcpNegFaceFade;
float4 _VolumetricFogProperty; // (rgb: albedo, a: extinction)
uint _VolumetricFogGlobalIndex;
CBUFFER_END

#define _VolumetricFogAlbedo _VolumetricFogProperty.rgb;
#define _VolumetricFogExtinction _VolumetricFogProperty.a;

struct v2f {
    float4 positionCS : SV_POSITION;
    float3 viewdirWS : TEXCOORD0;
    float3 positionOS: TEXCOORD1;
    float3 test : TEXCOORD2;
    nointerpolation uint depthSlice : SV_RenderTargetArrayIndex;
};

struct VoxelizeInput {
    float3 positionNVCS; // normalized volume clip space position between (0, 0, 0) and (1, 1, 1)
    float3 positionOS;  // object space poisition
};

float VBufferDistanceToSliceIndex(uint sliceIndex) {
    float de = _VBufferRcpSliceCount;
    return DecodeLogarithmicDepthGeneralized(((float)sliceIndex+0.5)*de + de, _VBufferDistanceDecodingParams);
}

float EyeDepthToLinear(float linearDepth, float4 zBufferParam) {
    linearDepth = rcp(linearDepth);
    linearDepth -= zBufferParam.w;

    return linearDepth / zBufferParam.z;
}

float ComputeVolumeFadeFactor(float3 coordNDC) {
    const float3 rcpPosFaceFade = _VolumetricFogRcpPosFaceFade.xyz;
    const float3 rcpNegFaceFade = _VolumetricFogRcpNegFaceFade.xyz;
    float3 posF = Remap10(coordNDC, rcpPosFaceFade, rcpPosFaceFade);
    float3 negF = Remap01(coordNDC, rcpNegFaceFade, 0);

    float fade = posF.x * posF.y * posF.z * negF.x * negF.y * negF.z;

    return fade;
}

v2f Vert(uint instanceId : INSTANCEID_SEMANTIC, uint vertexId : VERTEXID_SEMANTIC) {
    v2f o;

    int renderIndex = _Indirections.Load(_VolumetricFogGlobalIndex << 2);

    uint sliceCount = _VolumetricFogRenderingData[renderIndex].sliceCount;
    uint sliceStartIndex = _VolumetricFogRenderingData[renderIndex].startSliceIndex;
    float4 viewSpaceBounds = _VolumetricFogRenderingData[renderIndex].viewSpaceBounds;

    o.depthSlice = sliceStartIndex + instanceId;
    o.positionCS = GetQuadVertexPosition(vertexId);
    o.positionCS.xy = o.positionCS.xy * viewSpaceBounds.zw + viewSpaceBounds.xy;
    float sliceDistance = VBufferDistanceToSliceIndex(o.depthSlice);
    o.positionCS.z = EyeDepthToLinear(sliceDistance, _ZBufferParams);
    o.positionCS.w = 1;

    float3 positionWS = ComputeWorldSpacePosition(o.positionCS, UNITY_MATRIX_I_VP);
    o.viewdirWS = GetWorldSpaceViewDir(positionWS);
    o.positionOS = mul(UNITY_MATRIX_I_M, float4(positionWS, 1));
    return o;
}

void VolumetricFogVoxelize(VoxelizeInput input, inout float3 albedo, inout float extinction);

void Frag(v2f i, out float4 outColor : SV_Target0) {
    float sliceDistance = VBufferDistanceToSliceIndex(i.depthSlice);

    float3 rayDirWS = normalize(-i.viewdirWS);
    float3 rayOriginWS = GetCurrentViewPosition();
    float3 voxelCenterWS = rayOriginWS + sliceDistance * rayDirWS;

    float3x3 obbFrame = float3x3(_VolumetricFogObbRight.xyz, _VolumetricFogObbUp.xyz, _VolumetricFogObbForward.xyz);
    float3 voxelCenterBS = mul(obbFrame, voxelCenterWS - _VolumetricFogObbCenter.xyz);
    float3 voxelCenterCS = (voxelCenterBS * rcp(_VolumetricFogObbExtents.xyz));
    float3 absVoxelCenterCS = abs(voxelCenterCS);

    bool overlap = Max3(absVoxelCenterCS.x, absVoxelCenterCS.y, absVoxelCenterCS.z) <= 1;
    if (!overlap) clip(-1);

    float3 voxelCenterNDC = saturate(voxelCenterCS * 0.5 + 0.5);

    VoxelizeInput volInput;
    volInput.positionNVCS = voxelCenterNDC;
    volInput.positionOS = voxelCenterBS;

    float3 albedo = _VolumetricFogAlbedo;
    float extinction = _VolumetricFogExtinction;
    VolumetricFogVoxelize(volInput, albedo, extinction);

    float fade = ComputeVolumeFadeFactor(voxelCenterNDC);
    extinction *= fade;

    // prevent numerical explosion
    extinction = max(extinction, 0.0001);

    outColor = float4(albedo * extinction, extinction);
}

#endif