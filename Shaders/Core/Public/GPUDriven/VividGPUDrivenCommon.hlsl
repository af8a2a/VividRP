#ifndef VIVIDRP_GPU_DRIVEN_COMMON_INCLUDED
#define VIVIDRP_GPU_DRIVEN_COMMON_INCLUDED

#define VIVIDINSTANCEPASSMASK_MAIN 1u
#define VIVIDINSTANCEPASSMASK_SHADOWS 2u

#define VIVIDINSTANCEFLAGS_DISABLED 1u
#define VIVIDINSTANCEFLAGS_FLIP_WINDING_ORDER 2u

#define VIVIDSURFACEBINDINGFLAGS_BASE_COLOR 1u
#define VIVIDSURFACEBINDINGFLAGS_NORMAL 2u
#define VIVIDSURFACEBINDINGFLAGS_MASK 4u

#define VIVIDMATERIALFLAGS_UNLIT 1u
#define VIVIDMATERIALFLAGS_TERRAIN 2u

#define VIVIDRENDERERLISTID_CULL_FRONT 1u
#define VIVIDRENDERERLISTID_CULL_OFF 2u
#define VIVIDRENDERERLISTID_ALPHA_TEST 4u
#define VIVIDRENDERERLISTID_COUNT 8u
#define VIVID_MAX_MESHLET_INDICES 384u
#define VIVID_INVALID_FORCED_MESH_LOD_NODE_DEPTH 0xffffffffu
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Input.hlsl"
struct VividInstanceData
{
    float4x4 ObjectToWorldMatrix;
    float4x4 WorldToObjectMatrix;
    float4 AABBMin;
    float4 AABBMax;

    uint TopMeshLODStartIndex;
    uint TotalMeshLODCount;
    uint MaterialIndex;
    uint MeshLODLevelCount;

    float LODErrorScale;
    uint PassMask;
    uint Flags;
    uint Padding0;
};

struct VividMaterialData
{
    float4 AlbedoColor;
    float4 TextureTilingOffset;
    float4 Emission;

    uint SurfaceBindingIndex;
    float NormalsStrength;
    float Roughness;
    float Metallic;

    float SpecularAAScreenSpaceVariance;
    float SpecularAAThreshold;
    uint GeometryFlags;
    uint MaterialFlags;

    uint RendererListID;
    float AlphaClipThreshold;
    uint Padding0;
    uint Padding1;
};

struct VividSurfaceBindingData
{
    uint BaseColorResource;
    uint NormalResource;
    uint MaskResource;
    uint Flags;

    float4 UVScaleBias;
};

struct VividTerrainMaterialData
{
    uint LayerStartIndex;
    uint LayerCount;
    uint ControlBindingIndex0;
    uint ControlBindingIndex1;
};

struct VividTerrainLayerGPUData
{
    float4 TextureTilingOffset;

    uint SurfaceBindingIndex;
    float NormalsStrength;
    float Roughness;
    float Metallic;

    uint MaskMode;
    uint Padding0;
    uint Padding1;
    uint Padding2;
};

struct VividMeshLODNode
{
    float4 Bounds;
    float4 ParentBounds;

    float ParentError;
    float Error;
    uint MeshletStartIndex;
    uint MeshletCount;

    uint LevelIndex;
    uint Padding0;
    uint Padding1;
    uint Padding2;
};

struct VividMeshlet
{
    uint VertexOffset;
    uint TriangleOffset;
    uint VertexCount;
    uint TriangleCount;

    float4 BoundingSphere;
    float4 ConeApexCutoff;
    float4 ConeAxis;
};

struct VividMeshletVertex
{
    float4 Position;
    float4 Normal;
    float4 Tangent;
    float4 UV;
};

struct VividMeshletRenderRequestPacked
{
    uint InstanceID_LOD;
    uint MeshletID;
};

struct VividIndirectDrawArgs
{
    uint VertexCountPerInstance;
    uint InstanceCount;
    uint StartVertex;
    uint StartInstance;
};

static const uint VIVID_INDIRECT_DRAW_ARGS_STRIDE = 16u;
static const uint VIVID_INDIRECT_DRAW_ARGS_VERTEX_COUNT_OFFSET = 0u;
static const uint VIVID_INDIRECT_DRAW_ARGS_INSTANCE_COUNT_OFFSET = 4u;
static const uint VIVID_INDIRECT_DRAW_ARGS_START_VERTEX_OFFSET = 8u;
static const uint VIVID_INDIRECT_DRAW_ARGS_START_INSTANCE_OFFSET = 12u;

uint GetIndirectDrawArgsByteAddress(const uint drawArgsIndex)
{
    return drawArgsIndex * VIVID_INDIRECT_DRAW_ARGS_STRIDE;
}

struct VividGPUCullingContext
{
    float4x4 ViewProjectionMatrix;
    float4x4 ViewMatrix;
    float4 CameraPosition;
    float4 FrustumPlanes[6];
    float4 CullingSphereLS;

    int PassMask;
    int CameraIsPerspective;
    uint BaseStartInstance;
    uint MeshletListBuildJobsOffset;
    uint MeshletRenderRequestsOffset;

    uint Padding0;
    uint Padding1;
    uint Padding2;
};

struct VividGPULODSelectionContext
{
    float4x4 ViewProjectionMatrix;
    float4 CameraPosition;
    float4 CameraUp;
    float4 CameraRight;
    float2 ScreenSizePixels;

    uint Padding0;
    uint Padding1;
};

struct VividMeshletListBuildJob
{
    uint InstanceID;
    uint MeshLODNodeOffset;
    uint MeshLODNodeCount;
    uint Padding0;
};

StructuredBuffer<VividInstanceData> _InstanceData;
uint _InstanceDataCount;

StructuredBuffer<VividMaterialData> _MaterialData;
StructuredBuffer<VividSurfaceBindingData> _SurfaceBindingData;
uint _SurfaceBindingDataCount;
StructuredBuffer<VividTerrainMaterialData> _TerrainMaterialData;
uint _TerrainMaterialDataCount;
StructuredBuffer<VividTerrainLayerGPUData> _TerrainLayerData;
uint _TerrainLayerDataCount;
StructuredBuffer<VividMeshLODNode> _MeshLODNodes;
StructuredBuffer<VividMeshlet> _Meshlets;
uint _MeshLODNodeCount;
uint _MeshletCount;

VividInstanceData PullInstanceData(const uint instanceIndex)
{
    return _InstanceData[instanceIndex];
}

VividMaterialData PullMaterialData(const uint materialIndex)
{
    return _MaterialData[materialIndex];
}

VividSurfaceBindingData PullSurfaceBindingData(const uint surfaceBindingIndex)
{
    return _SurfaceBindingData[surfaceBindingIndex];
}

VividTerrainMaterialData PullTerrainMaterialData(const uint terrainMaterialIndex)
{
    return _TerrainMaterialData[terrainMaterialIndex];
}

VividTerrainLayerGPUData PullTerrainLayerData(const uint terrainLayerIndex)
{
    return _TerrainLayerData[terrainLayerIndex];
}

VividMeshLODNode PullMeshLODNode(const uint nodeIndex)
{
    return _MeshLODNodes[nodeIndex];
}

VividMeshlet PullMeshletData(const uint meshletIndex)
{
    return _Meshlets[meshletIndex];
}

float LengthSq(const float3 value)
{
    return dot(value, value);
}

float3 TransformPosition(const float4x4 matrixValue, const float3 positionOS)
{
    return mul(matrixValue, float4(positionOS, 1.0f)).xyz;
}

float4 TransformSphere(const float4 sphereOS, const float4x4 objectToWorldMatrix)
{
    const float3 centerWS = TransformPosition(objectToWorldMatrix, sphereOS.xyz);
    const float radiusOS = sphereOS.w;

    const float3 xWS = TransformPosition(objectToWorldMatrix, sphereOS.xyz + float3(radiusOS, 0.0f, 0.0f));
    const float3 yWS = TransformPosition(objectToWorldMatrix, sphereOS.xyz + float3(0.0f, radiusOS, 0.0f));
    const float3 zWS = TransformPosition(objectToWorldMatrix, sphereOS.xyz + float3(0.0f, 0.0f, radiusOS));

    const float radiusWS = max(length(xWS - centerWS), max(length(yWS - centerWS), length(zWS - centerWS)));
    return float4(centerWS, radiusWS);
}

float4 ComputeInstanceBoundingSphereWS(const VividInstanceData instanceData)
{
    const float3 aabbMinOS = instanceData.AABBMin.xyz;
    const float3 aabbMaxOS = instanceData.AABBMax.xyz;
    const float3 centerOS = (aabbMinOS + aabbMaxOS) * 0.5f;
    const float radiusOS = length(aabbMaxOS - centerOS);
    return TransformSphere(float4(centerOS, radiusOS), instanceData.ObjectToWorldMatrix);
}

bool FrustumVsSphere(const VividGPUCullingContext cullingContext, const float4 sphereWS)
{
    [unroll]
    for (uint planeIndex = 0; planeIndex < 6; ++planeIndex)
    {
        const float4 plane = cullingContext.FrustumPlanes[planeIndex];
        if (dot(plane.xyz, sphereWS.xyz) + plane.w < -sphereWS.w)
        {
            return false;
        }
    }

    return true;
}

bool DoLightSphereCulling(
    const float4 receiverBoundingSphereLS,
    const float4 casterBoundingSphereWS,
    const float3x3 worldToLightSpaceRotation
)
{
    const float3 casterCenterLS = mul(worldToLightSpaceRotation, casterBoundingSphereWS.xyz);
    const float casterRadius = casterBoundingSphereWS.w;

    const float3 receiverCenterLS = receiverBoundingSphereLS.xyz;
    const float receiverRadius = receiverBoundingSphereLS.w;
    const float3 receiverToCasterLS = casterCenterLS - receiverCenterLS;

    const float intersectionMaxDistance = casterRadius + receiverRadius;
    const float zSqAtSphereIntersection =
        intersectionMaxDistance * intersectionMaxDistance - dot(receiverToCasterLS.xy, receiverToCasterLS.xy);

    UNITY_FLATTEN
    if (zSqAtSphereIntersection < 0.0f)
    {
        return false;
    }

    UNITY_FLATTEN
    if (receiverToCasterLS.z < 0.0f && receiverToCasterLS.z * receiverToCasterLS.z > zSqAtSphereIntersection)
    {
        return false;
    }

    return true;
}

bool LightSphereCulling(const VividGPUCullingContext cullingContext, const float4 casterBoundingSphereWS)
{
    UNITY_BRANCH
    if (cullingContext.CullingSphereLS.w <= 0.0f)
    {
        return true;
    }

    const float3x3 worldToLightSpaceRotation = (float3x3) cullingContext.ViewMatrix;
    return DoLightSphereCulling(cullingContext.CullingSphereLS, casterBoundingSphereWS, worldToLightSpaceRotation);
}

float2 GetNormalizedScreenCoordinates(const float4x4 viewProjectionMatrix, const float3 positionWS)
{
    float4 clipPosition = mul(viewProjectionMatrix, float4(positionWS, 1.0f));
    clipPosition.xy /= max(clipPosition.w, 1e-5f);
    clipPosition.xy = clipPosition.xy * 0.5f + 0.5f;
    return clipPosition.xy;
}

float GetScreenBoundRadiusSq(const VividGPULODSelectionContext lodSelectionContext, const float4 boundsWS)
{
    const float2 center = GetNormalizedScreenCoordinates(lodSelectionContext.ViewProjectionMatrix, boundsWS.xyz);
    const float2 up = GetNormalizedScreenCoordinates(
        lodSelectionContext.ViewProjectionMatrix,
        boundsWS.xyz + lodSelectionContext.CameraUp.xyz * boundsWS.w
    );
    const float2 right = GetNormalizedScreenCoordinates(
        lodSelectionContext.ViewProjectionMatrix,
        boundsWS.xyz + lodSelectionContext.CameraRight.xyz * boundsWS.w
    );

    const float2 screenUp = (up - center) * lodSelectionContext.ScreenSizePixels;
    const float2 screenRight = (right - center) * lodSelectionContext.ScreenSizePixels;
    return max(dot(screenUp, screenUp), dot(screenRight, screenRight));
}

uint GetRendererListID(const VividInstanceData instanceData, const VividMaterialData materialData)
{
    uint rendererListID = materialData.RendererListID;

    if ((instanceData.Flags & VIVIDINSTANCEFLAGS_FLIP_WINDING_ORDER) != 0u &&
        (rendererListID & VIVIDRENDERERLISTID_CULL_OFF) == 0u)
    {
        if ((rendererListID & VIVIDRENDERERLISTID_CULL_FRONT) != 0u)
        {
            rendererListID &= ~VIVIDRENDERERLISTID_CULL_FRONT;
        }
        else
        {
            rendererListID |= VIVIDRENDERERLISTID_CULL_FRONT;
        }
    }

    return rendererListID;
}

float3 GetViewForwardDir(const float4x4 viewMatrix)
{
    // View forward in view space is (0, 0, -1). Rotating it by the inverse rotation of the view
    // matrix (i.e. transpose of the upper 3x3, since rotations are orthogonal) yields the world-
    // space forward. In HLSL `mul(v, M3x3)` is equivalent to `mul(transpose(M3x3), v)`, so
    // multiplying (0,0,-1) on the left by the upper 3x3 gives -row2.xyz.
    return normalize(-float3(viewMatrix._m20, viewMatrix._m21, viewMatrix._m22));
}

bool ConeCulling(
    const VividGPUCullingContext cullingContext,
    const VividInstanceData instanceData,
    const VividMeshlet meshlet
)
{
    const float3 coneApexWS = TransformPosition(instanceData.ObjectToWorldMatrix, meshlet.ConeApexCutoff.xyz);
    float3 coneAxisWS = mul(meshlet.ConeAxis.xyz, (float3x3) instanceData.WorldToObjectMatrix);
    const float axisLengthSq = LengthSq(coneAxisWS);

    if (axisLengthSq <= 1e-8f)
    {
        return true;
    }

    coneAxisWS *= rsqrt(axisLengthSq);

    const float3 viewDirWS = cullingContext.CameraIsPerspective != 0
        ? normalize(coneApexWS - cullingContext.CameraPosition.xyz)
        : GetViewForwardDir(cullingContext.ViewMatrix);
    const float dotResult = dot(viewDirWS, coneAxisWS);
    return !(dotResult >= meshlet.ConeApexCutoff.w);
}

bool ShouldSelectMeshLODNode(
    const VividGPULODSelectionContext lodSelectionContext,
    const VividMeshLODNode meshLODNode,
    const VividInstanceData instanceData,
    const float distanceToViewSq,
    const uint forcedMeshLODNodeDepth,
    const float meshLODErrorThreshold
)
{
    if (forcedMeshLODNodeDepth != VIVID_INVALID_FORCED_MESH_LOD_NODE_DEPTH)
    {
        const bool isLeaf = meshLODNode.LevelIndex == instanceData.MeshLODLevelCount - 1;
        return meshLODNode.LevelIndex == forcedMeshLODNodeDepth ||
               (meshLODNode.LevelIndex < forcedMeshLODNodeDepth && isLeaf);
    }

    const float4 boundsWS = TransformSphere(meshLODNode.Bounds, instanceData.ObjectToWorldMatrix);
    const float4 parentBoundsWS = TransformSphere(meshLODNode.ParentBounds, instanceData.ObjectToWorldMatrix);
    const float error = meshLODNode.Error * GetScreenBoundRadiusSq(lodSelectionContext, boundsWS);
    const float parentError = meshLODNode.ParentError >= 0.0f
        ? meshLODNode.ParentError * GetScreenBoundRadiusSq(lodSelectionContext, parentBoundsWS)
        : 3.402823466e+38f;
    const float threshold = meshLODErrorThreshold * distanceToViewSq * instanceData.LODErrorScale;
    return parentError > threshold && error <= threshold;
}

#endif // VIVIDRP_GPU_DRIVEN_COMMON_INCLUDED
