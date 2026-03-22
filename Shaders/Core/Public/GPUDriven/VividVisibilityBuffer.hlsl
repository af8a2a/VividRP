#ifndef VIVIDRP_VISIBILITY_BUFFER_INCLUDED
#define VIVIDRP_VISIBILITY_BUFFER_INCLUDED

#define VIVID_VISIBILITY_BUFFER_TRIANGLE_INDEX_BITS 8u
#define VIVID_VISIBILITY_BUFFER_TRIANGLE_INDEX_MASK ((1u << VIVID_VISIBILITY_BUFFER_TRIANGLE_INDEX_BITS) - 1u)

struct VividVisibilityBufferValue
{
    uint InstanceID;
    uint MeshletID;
    uint IndexID;
};

uint2 PackVisibilityBufferValue(const VividVisibilityBufferValue value)
{
    return uint2(
        value.InstanceID,
        (value.MeshletID << VIVID_VISIBILITY_BUFFER_TRIANGLE_INDEX_BITS) |
        ((value.IndexID / 3u) & VIVID_VISIBILITY_BUFFER_TRIANGLE_INDEX_MASK)
    );
}

VividVisibilityBufferValue UnpackVisibilityBufferValue(const uint2 packedValue)
{
    VividVisibilityBufferValue value;
    value.InstanceID = packedValue.x;
    value.MeshletID = packedValue.y >> VIVID_VISIBILITY_BUFFER_TRIANGLE_INDEX_BITS;
    value.IndexID = (packedValue.y & VIVID_VISIBILITY_BUFFER_TRIANGLE_INDEX_MASK) * 3u;
    return value;
}

#endif // VIVIDRP_VISIBILITY_BUFFER_INCLUDED
