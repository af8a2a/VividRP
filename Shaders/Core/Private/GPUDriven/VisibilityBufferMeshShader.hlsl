// Standalone DXC mesh-shader program. This source intentionally avoids
// ShaderLab and Unity's shader-library globals; its resource layout is owned by
// VividMeshShader.dll.

#include "Packages/com.vivid.render-pipelines/Runtime/SubSystem/GPUDriven/VividGPUDrivenStructs.cs.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividMeshletDecode.hlsli"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividVisibilityBuffer.hlsl"

#define VIVID_MESH_SHADER_INDIRECT_ARGS_STRIDE 16u
#define VIVID_MESH_SHADER_INSTANCE_COUNT_OFFSET 4u
#define VIVID_MESH_SHADER_START_INSTANCE_OFFSET 12u
#define VIVID_DISPATCH_MESH_MAX_GROUPS_PER_DIMENSION 65535u
// 65,535 x 64 stays below D3D12's 2^22 total mesh-group limit.
#define VIVID_DISPATCH_MESH_MAX_TOTAL_GROUPS 4194240u

struct VividMeshDispatchPayload
{
    uint StartInstance;
    uint InstanceCount;
    uint DispatchGroupCountX;
};

struct VividMeshVertexOutput
{
    float4 positionCS : SV_Position;
    float2 uv0 : TEXCOORD0;
    float3 geometricNormalWS : TEXCOORD1;
};

struct VividMeshPrimitiveOutput
{
    uint2 visibilityValue : TEXCOORD2;
};

cbuffer VividMeshDispatchConstants : register(b0)
{
    float4x4 _ViewProjectionMatrix;
    uint _RendererListIndex;
    uint _MaxRequestCount;
    uint _DispatchPadding0;
    uint _DispatchPadding1;
};

StructuredBuffer<VividMeshletRenderRequestPacked> _VisibleMeshletRenderRequests : register(t0);
ByteAddressBuffer _VisibleMeshletIndirectArgs : register(t1);
StructuredBuffer<VividInstanceData> _InstanceData : register(t2);
StructuredBuffer<VividMeshlet> _Meshlets : register(t3);
StructuredBuffer<VividMeshletVertex> _SharedVertexBuffer : register(t4);
ByteAddressBuffer _SharedIndexBuffer : register(t5);

groupshared VividMeshDispatchPayload g_MeshDispatchPayload;

uint PullMeshletIndex(const VividDecodedMeshlet meshlet, const uint indexID)
{
    const uint absoluteIndexID = meshlet.TriangleOffset + indexID;
    const uint packedIndices = _SharedIndexBuffer.Load((absoluteIndexID / 4u) * 4u);
    return (packedIndices >> ((absoluteIndexID % 4u) * 8u)) & 0xFFu;
}

[numthreads(1, 1, 1)]
void AmplificationMain()
{
    const uint argsAddress = _RendererListIndex * VIVID_MESH_SHADER_INDIRECT_ARGS_STRIDE;
    const uint startInstance = _VisibleMeshletIndirectArgs.Load(
        argsAddress + VIVID_MESH_SHADER_START_INSTANCE_OFFSET);
    const uint availableRequestCount = startInstance < _MaxRequestCount
        ? _MaxRequestCount - startInstance
        : 0u;
    const uint instanceCount = min(
        _VisibleMeshletIndirectArgs.Load(
            argsAddress + VIVID_MESH_SHADER_INSTANCE_COUNT_OFFSET),
        min(availableRequestCount, VIVID_DISPATCH_MESH_MAX_TOTAL_GROUPS));
    const uint dispatchGroupCountX = min(
        instanceCount,
        VIVID_DISPATCH_MESH_MAX_GROUPS_PER_DIMENSION);
    const uint dispatchGroupCountY = dispatchGroupCountX == 0u
        ? 1u
        : (instanceCount + dispatchGroupCountX - 1u) / dispatchGroupCountX;

    g_MeshDispatchPayload.StartInstance = startInstance;
    g_MeshDispatchPayload.InstanceCount = instanceCount;
    g_MeshDispatchPayload.DispatchGroupCountX = max(dispatchGroupCountX, 1u);
    DispatchMesh(dispatchGroupCountX, dispatchGroupCountY, 1u, g_MeshDispatchPayload);
}

[numthreads(128, 1, 1)]
[outputtopology("triangle")]
void MeshMain(
    in payload VividMeshDispatchPayload meshPayload,
    const uint3 groupID : SV_GroupID,
    const uint groupThreadID : SV_GroupThreadID,
    out vertices VividMeshVertexOutput outputVertices[MAX_MESHLET_VERTICES],
    out indices uint3 outputTriangles[MAX_MESHLET_TRIANGLES],
    out primitives VividMeshPrimitiveOutput outputPrimitives[MAX_MESHLET_TRIANGLES])
{
    const uint requestOffset = groupID.x + groupID.y * meshPayload.DispatchGroupCountX;
    const bool requestIsValid = requestOffset < meshPayload.InstanceCount;
    const uint safeRequestOffset = requestIsValid ? requestOffset : 0u;
    const VividMeshletRenderRequestPacked renderRequest =
        _VisibleMeshletRenderRequests[meshPayload.StartInstance + safeRequestOffset];
    const VividInstanceData instanceData = _InstanceData[renderRequest.InstanceID_LOD];
    const VividDecodedMeshlet meshlet = DecodeVividMeshlet(_Meshlets[renderRequest.MeshletID]);
    const uint vertexCount = requestIsValid ? meshlet.VertexCount : 0u;
    const uint triangleCount = requestIsValid ? meshlet.TriangleCount : 0u;

    SetMeshOutputCounts(vertexCount, triangleCount);

    if (!requestIsValid)
        return;

    if (groupThreadID < vertexCount)
    {
        const VividDecodedMeshletVertex vertex =
            DecodeVividMeshletVertex(_SharedVertexBuffer[meshlet.VertexOffset + groupThreadID]);
        const float3 positionWS = mul(
            instanceData.ObjectToWorldMatrix,
            float4(vertex.Position.xyz, 1.0f)).xyz;

        VividMeshVertexOutput output;
        output.positionCS = mul(_ViewProjectionMatrix, float4(positionWS, 1.0f));
        output.uv0 = vertex.UV.xy;
        const float3 geometricNormalWS =
            mul(vertex.Normal.xyz, (float3x3)instanceData.WorldToObjectMatrix);
        output.geometricNormalWS = geometricNormalWS
            * rsqrt(max(dot(geometricNormalWS, geometricNormalWS), 1e-20f));
        outputVertices[groupThreadID] = output;
    }

    if (groupThreadID < triangleCount)
    {
        const uint firstIndex = groupThreadID * 3u;
        outputTriangles[groupThreadID] = uint3(
            PullMeshletIndex(meshlet, firstIndex),
            PullMeshletIndex(meshlet, firstIndex + 1u),
            PullMeshletIndex(meshlet, firstIndex + 2u));

        VividVisibilityBufferValue visibilityBufferValue;
        visibilityBufferValue.InstanceID = renderRequest.InstanceID_LOD;
        visibilityBufferValue.MeshletID = renderRequest.MeshletID;
        visibilityBufferValue.IndexID = firstIndex;
        outputPrimitives[groupThreadID].visibilityValue =
            PackVisibilityBufferValue(visibilityBufferValue);
    }
}

VividVisibilityBufferFragmentOutput PixelMain(
    VividMeshVertexOutput input,
    nointerpolation uint2 visibilityValue : TEXCOORD2,
    linear float3 barycentrics : SV_Barycentrics)
{
    const float2 uv0Ddx = ddx(input.uv0);
    const float2 uv0Ddy = ddy(input.uv0);
    return PackVividVisibilityBufferFragmentOutput(
        visibilityValue,
        input.uv0,
        uv0Ddx,
        uv0Ddy,
        input.geometricNormalWS,
        barycentrics);
}
