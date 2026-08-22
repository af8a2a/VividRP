#ifndef VIVIDRP_VISIBILITY_BUFFER_INCLUDED
#define VIVIDRP_VISIBILITY_BUFFER_INCLUDED

#define VIVID_VISIBILITY_BUFFER_TRIANGLE_INDEX_BITS 8u
#define VIVID_VISIBILITY_BUFFER_TRIANGLE_INDEX_MASK ((1u << VIVID_VISIBILITY_BUFFER_TRIANGLE_INDEX_BITS) - 1u)
#define VIVID_VISIBILITY_BUFFER_INSTANCE_ID_BIAS 1u

struct VividVisibilityBufferValue
{
    uint InstanceID;
    uint MeshletID;
    uint IndexID;
};

struct VividVisibilityBufferFragmentOutput
{
    uint2 visibility : SV_Target0;
    float4 attributes0 : SV_Target1;
    float4 attributes1 : SV_Target2;
};

float2 VividEncodeVisibilityBufferNormalOct(float3 normalWS)
{
    float3 normal = normalWS * rsqrt(max(dot(normalWS, normalWS), 1e-12));
    normal /= max(abs(normal.x) + abs(normal.y) + abs(normal.z), 1e-6);
    float2 encoded = normal.xy;
    if (normal.z < 0.0)
    {
        float2 signs = float2(
            encoded.x >= 0.0 ? 1.0 : -1.0,
            encoded.y >= 0.0 ? 1.0 : -1.0);
        encoded = (1.0 - abs(encoded.yx)) * signs;
    }
    return encoded * 0.5 + 0.5;
}

VividVisibilityBufferFragmentOutput PackVividVisibilityBufferFragmentOutput(
    uint2 visibility,
    float2 uv0,
    float3 geometricNormalWS)
{
    VividVisibilityBufferFragmentOutput output;
    output.visibility = visibility;
    output.attributes0 = float4(uv0, ddx(uv0));
    output.attributes1 = float4(
        ddy(uv0),
        VividEncodeVisibilityBufferNormalOct(geometricNormalWS));
    return output;
}

bool IsPackedVisibilityBufferValueValid(const uint2 packedValue)
{
    return packedValue.x != 0u;
}

uint2 PackVisibilityBufferValue(const VividVisibilityBufferValue value)
{
    return uint2(
        value.InstanceID + VIVID_VISIBILITY_BUFFER_INSTANCE_ID_BIAS,
        (value.MeshletID << VIVID_VISIBILITY_BUFFER_TRIANGLE_INDEX_BITS) |
        ((value.IndexID / 3u) & VIVID_VISIBILITY_BUFFER_TRIANGLE_INDEX_MASK)
    );
}

VividVisibilityBufferValue UnpackVisibilityBufferValue(const uint2 packedValue)
{
    VividVisibilityBufferValue value;
    value.InstanceID = packedValue.x - VIVID_VISIBILITY_BUFFER_INSTANCE_ID_BIAS;
    value.MeshletID = packedValue.y >> VIVID_VISIBILITY_BUFFER_TRIANGLE_INDEX_BITS;
    value.IndexID = (packedValue.y & VIVID_VISIBILITY_BUFFER_TRIANGLE_INDEX_MASK) * 3u;
    return value;
}

#endif // VIVIDRP_VISIBILITY_BUFFER_INCLUDED
