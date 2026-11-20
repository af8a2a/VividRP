#ifndef VIVID_SPARSE_MESH_DISTANCE_FIELD_INCLUDED
#define VIVID_SPARSE_MESH_DISTANCE_FIELD_INCLUDED

// DistanceFieldLightingShared.ush, OFFSET_DATA_STRUCT == 0. Asset mips are stored coarsest first.
StructuredBuffer<float4> _SparseSdfMips;
StructuredBuffer<uint> _SparseSdfTable;
StructuredBuffer<float4> _SparseSdfObjects;
Texture3D<float> _SparseSdfBricks;
float4 _SparseSdfAtlasSize; // reciprocal texture dimensions, brick rows per slice axis

float SampleSparseMeshSignedDistanceField(float3 sampleVolumePosition, uint mipOffset, out float maxEncodedDistance)
{
    uint2 header = asuint(_SparseSdfMips[mipOffset].xy);
    uint3 dims = uint3(header.x, header.x >> 10, header.x >> 20) & 1023u;
    float4 scale = _SparseSdfMips[mipOffset + 1u];
    float4 add = _SparseSdfMips[mipOffset + 2u];
    float3 indirectionPos = sampleVolumePosition * scale.xyz + add.xyz;
    int3 coord = (int3)indirectionPos;
    uint brickIndex = _SparseSdfTable[header.y + (coord.z * dims.y + coord.y) * dims.x + coord.x];
    maxEncodedDistance = scale.w + add.w;
    float distanceField = maxEncodedDistance;
    if (brickIndex != 0xffffffffu)
    {
        uint across = uint(_SparseSdfAtlasSize.w);
        uint3 brickOffset = uint3(brickIndex % across, (brickIndex / across) % across, brickIndex / (across * across));
        float3 texelSize = _SparseSdfAtlasSize.xyz;
        float3 brickLocalUV = indirectionPos - coord;
        float3 atlasUV = brickOffset * (8.0 * texelSize) + brickLocalUV * (7.0 * texelSize) + 0.5 * texelSize;
        float encoded = _SparseSdfBricks.SampleLevel(sampler_LinearClamp, atlasUV, 0);
        distanceField = encoded * scale.w + add.w;
    }
    return distanceField;
}

float DistanceToMeshSurfaceStandalone(float3 sampleVolumePosition, uint asset)
{
    uint offset = asset * 9u;
    uint numMips = asuint(_SparseSdfMips[offset].x) >> 30;
    float distanceField = 0;
    for (uint reversedMip = 0; reversedMip < numMips; ++reversedMip)
    {
        float maxEncodedDistance;
        distanceField = SampleSparseMeshSignedDistanceField(sampleVolumePosition, offset + 3u * reversedMip, maxEncodedDistance);
        if (abs(distanceField) > .25 * maxEncodedDistance)
            break;
    }
    return distanceField;
}

// MeshDistanceFieldCommon.ush::DistanceToNearestSurfaceForObject. The world distance unit here is metres.
float SparseDistanceToNearestSurfaceForObject(uint objectOffset, float4 localToVolume, float3 localPosition, float maxDistance)
{
    float3 volumePosition = localPosition.zxy * localToVolume.xyz + _SparseSdfObjects[objectOffset + 1u].xyz;
    float3 volumePositionExtent = _SparseSdfObjects[objectOffset + 2u].xyz;
    float3 volumeToWorldScale = _SparseSdfObjects[objectOffset + 3u].xyz;
    float3 clampedVolumePosition = clamp(volumePosition, -volumePositionExtent, volumePositionExtent);
    float3 toBox = (abs(volumePosition) - volumePositionExtent) * volumeToWorldScale;
    float distanceToBoxOutside = length(max(0.0, toBox));
    float distanceToBoxInside = min(0.0, max(toBox.x, max(toBox.y, toBox.z)));
    float distanceToBox = distanceToBoxOutside + distanceToBoxInside;
    float distanceToOccluder = maxDistance;
    if (distanceToBox < maxDistance)
    {
        float volumeScale = min(volumeToWorldScale.x, min(volumeToWorldScale.y, volumeToWorldScale.z));
        distanceToOccluder = DistanceToMeshSurfaceStandalone(clampedVolumePosition, uint(localToVolume.w))
            * volumeScale + max(0.0, distanceToBox);
        distanceToOccluder = max(distanceToOccluder, distanceToBox);
    }
    return distanceToOccluder;
}

#endif
