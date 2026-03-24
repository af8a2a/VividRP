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
