#ifndef VIVIDRP_MESHLET_DECODE_INCLUDED
#define VIVIDRP_MESHLET_DECODE_INCLUDED

// The packed VividMeshLODNode, VividMeshlet, and VividMeshletVertex structures
// must be declared before this file is included. Keeping this header free from
// Unity shader-library dependencies allows it to be consumed by standalone DXC.

struct VividDecodedMeshLODNode
{
    float4 Bounds;
    float4 ParentBounds;
    float ParentError;
    float Error;
    uint MeshletStartIndex;
    uint MeshletCount;
    uint LevelIndex;
};

struct VividDecodedMeshlet
{
    uint VertexOffset;
    uint TriangleOffset;
    uint VertexCount;
    uint TriangleCount;
    float4 BoundingSphere;
    float3 ConeAxis;
    float ConeCutoff;
    uint ConeValid;
};

struct VividDecodedMeshletVertex
{
    float4 Position;
    float4 Normal;
    float4 Tangent;
    float4 UV;
};

static const uint VIVID_MESHLET_OCTAHEDRAL_COMPONENT_MASK = 0x7FFFu;
static const uint VIVID_MESHLET_NORMAL_VALID_BIT = 1u << 30;
static const uint VIVID_MESHLET_TANGENT_NEGATIVE_HANDEDNESS_BIT = 1u << 30;
static const uint VIVID_MESHLET_TANGENT_VALID_BIT = 1u << 31;
static const uint VIVID_MESHLET_CONE_OCTAHEDRAL_COMPONENT_MASK = 0x3FFu;
static const uint VIVID_MESHLET_CONE_CUTOFF_MASK = 0x7FFu;
static const uint VIVID_MESHLET_CONE_VALID_BIT = 1u << 31;

float3 DecodeVividMeshletOctahedral15(const uint packedDirection)
{
    float2 octahedral = float2(
        packedDirection & VIVID_MESHLET_OCTAHEDRAL_COMPONENT_MASK,
        (packedDirection >> 15) & VIVID_MESHLET_OCTAHEDRAL_COMPONENT_MASK
    );
    octahedral = octahedral / float(VIVID_MESHLET_OCTAHEDRAL_COMPONENT_MASK) * 2.0f - 1.0f;

    float3 direction = float3(
        octahedral,
        1.0f - abs(octahedral.x) - abs(octahedral.y)
    );
    const float fold = saturate(-direction.z);
    const float2 foldSign = step(0.0f, direction.xy) * 2.0f - 1.0f;
    direction.xy -= foldSign * fold;
    return direction * rsqrt(max(dot(direction, direction), 1e-20f));
}

float3 DecodeVividMeshletOctahedral10(const uint packedDirection)
{
    float2 octahedral = float2(
        packedDirection & VIVID_MESHLET_CONE_OCTAHEDRAL_COMPONENT_MASK,
        (packedDirection >> 10) & VIVID_MESHLET_CONE_OCTAHEDRAL_COMPONENT_MASK
    );
    octahedral = octahedral / float(VIVID_MESHLET_CONE_OCTAHEDRAL_COMPONENT_MASK) * 2.0f - 1.0f;

    float3 direction = float3(
        octahedral,
        1.0f - abs(octahedral.x) - abs(octahedral.y)
    );
    const float fold = saturate(-direction.z);
    const float2 foldSign = step(0.0f, direction.xy) * 2.0f - 1.0f;
    direction.xy -= foldSign * fold;
    return direction * rsqrt(max(dot(direction, direction), 1e-20f));
}

VividDecodedMeshlet DecodeVividMeshlet(const VividMeshlet packedMeshlet)
{
    VividDecodedMeshlet meshlet;
    meshlet.VertexOffset = packedMeshlet.VertexOffset;
    meshlet.TriangleOffset = packedMeshlet.TriangleOffset;
    meshlet.VertexCount = packedMeshlet.PackedVertexTriangleCounts & 0xFFFFu;
    meshlet.TriangleCount = packedMeshlet.PackedVertexTriangleCounts >> 16;
    meshlet.BoundingSphere = packedMeshlet.BoundingSphere;
    meshlet.ConeValid = (packedMeshlet.PackedCone & VIVID_MESHLET_CONE_VALID_BIT) != 0u;
    meshlet.ConeAxis = meshlet.ConeValid != 0u
        ? DecodeVividMeshletOctahedral10(packedMeshlet.PackedCone)
        : 0.0f;
    const uint packedCutoff = (packedMeshlet.PackedCone >> 20) & VIVID_MESHLET_CONE_CUTOFF_MASK;
    meshlet.ConeCutoff = packedCutoff / float(VIVID_MESHLET_CONE_CUTOFF_MASK) * 2.0f - 1.0f;
    return meshlet;
}

VividDecodedMeshLODNode DecodeVividMeshLODNode(const VividMeshLODNode packedNode)
{
    VividDecodedMeshLODNode node;
    node.Bounds = packedNode.Bounds;
    node.ParentError = asfloat((packedNode.PackedParentErrorRadius & 0xFFFFu) << 16);
    const float parentRadius = asfloat(packedNode.PackedParentErrorRadius & 0xFFFF0000u);
    node.ParentBounds = node.ParentError < 0.0f
        ? 0.0f
        : float4(packedNode.Bounds.xyz, parentRadius);
    node.Error = packedNode.Error;
    node.MeshletStartIndex = packedNode.MeshletStartIndex;
    node.MeshletCount = packedNode.PackedMeshletCountLevel & 0xFFFFu;
    node.LevelIndex = packedNode.PackedMeshletCountLevel >> 16;
    return node;
}

VividDecodedMeshletVertex DecodeVividMeshletVertex(const VividMeshletVertex packedVertex)
{
    VividDecodedMeshletVertex vertex;
    vertex.Position = float4(
        packedVertex.PositionX,
        packedVertex.PositionY,
        packedVertex.PositionZ,
        1.0f);
    vertex.Normal = (packedVertex.PackedNormal & VIVID_MESHLET_NORMAL_VALID_BIT) != 0u
        ? float4(DecodeVividMeshletOctahedral15(packedVertex.PackedNormal), 0.0f)
        : 0.0f;
    vertex.Tangent = (packedVertex.PackedTangent & VIVID_MESHLET_TANGENT_VALID_BIT) != 0u
        ? float4(
            DecodeVividMeshletOctahedral15(packedVertex.PackedTangent),
            (packedVertex.PackedTangent & VIVID_MESHLET_TANGENT_NEGATIVE_HANDEDNESS_BIT) != 0u
                ? -1.0f
                : 1.0f)
        : 0.0f;
    vertex.UV = float4(packedVertex.UV, 0.0f, 0.0f);
    return vertex;
}

#endif // VIVIDRP_MESHLET_DECODE_INCLUDED
