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

// v5 resident vertex: five uints / 20 bytes. Float payloads retain sign, exponent,
// and 16 mantissa bits. Shared positions decode identically across all clusters.
float DecodeVividMeshletFloat25(uint bits)
{
    return asfloat((bits & 0x1FFFFFFu) << 7);
}

float3 DecodeVividMeshletPosition(VividMeshletVertex vertex)
{
    return float3(DecodeVividMeshletFloat25(vertex.PositionXY),
        DecodeVividMeshletFloat25((vertex.PositionXY >> 25) | (vertex.PositionYZ << 7)),
        DecodeVividMeshletFloat25((vertex.PositionYZ >> 18) | (vertex.PositionZUV << 14)));
}

float2 DecodeVividMeshletUV(VividMeshletVertex vertex)
{
    return float2(DecodeVividMeshletFloat25((vertex.PositionZUV >> 11) | (vertex.UVAndFlags << 21)),
        DecodeVividMeshletFloat25(vertex.UVAndFlags >> 4));
}

float3 DecodeVividMeshletNormal(VividMeshletVertex vertex)
{
    if ((vertex.UVAndFlags & (1u << 29)) == 0u) return 0.0f;
    float2 oct = float2(vertex.TangentFrame & 2047u, (vertex.TangentFrame >> 11) & 2047u) / 2047.0f * 2.0f - 1.0f;
    float3 normal = float3(oct, 1.0f - abs(oct.x) - abs(oct.y));
    normal.xy -= (step(0.0f, normal.xy) * 2.0f - 1.0f) * saturate(-normal.z);
    return normalize(normal);
}

float4 DecodeVividMeshletTangent(VividMeshletVertex vertex)
{
    if ((vertex.UVAndFlags & (1u << 30)) == 0u) return 0.0f;
    if ((vertex.UVAndFlags & (1u << 29)) == 0u)
    {
        return float4(DecodeVividMeshletOctahedral15(vertex.TangentFrame),
            (vertex.TangentFrame & VIVID_MESHLET_TANGENT_NEGATIVE_HANDEDNESS_BIT) != 0u ? -1.0f : 1.0f);
    }
    float3 normal = DecodeVividMeshletNormal(vertex);
    float3 tangent, bitangent;
    if (normal.z < -0.99998796f)
    {
        tangent = float3(0, -1, 0);
        bitangent = float3(-1, 0, 0);
    }
    else
    {
        float a = 1.0f / (1.0f + normal.z);
        float b = -normal.x * normal.y * a;
        tangent = float3(1.0f - normal.x * normal.x * a, b, -normal.x);
        bitangent = float3(b, 1.0f - normal.y * normal.y * a, -normal.y);
    }
    uint frame = vertex.TangentFrame >> 22;
    float angle = ((frame >> 1) / 511.0f * 2.0f - 1.0f) * 3.14159265358979323846f;
    float sine, cosine;
    sincos(angle, sine, cosine);
    return float4(cosine * bitangent + sine * tangent, (frame & 1u) != 0u ? 1.0f : -1.0f);
}

VividDecodedMeshletVertex DecodeVividMeshletVertex(const VividMeshletVertex packedVertex)
{
    VividDecodedMeshletVertex vertex;
    vertex.Position = float4(DecodeVividMeshletPosition(packedVertex), 1.0f);
    vertex.Normal = float4(DecodeVividMeshletNormal(packedVertex), 0.0f);
    vertex.Tangent = DecodeVividMeshletTangent(packedVertex);
    vertex.UV = float4(DecodeVividMeshletUV(packedVertex), 0.0f, 0.0f);
    return vertex;
}

#endif // VIVIDRP_MESHLET_DECODE_INCLUDED
