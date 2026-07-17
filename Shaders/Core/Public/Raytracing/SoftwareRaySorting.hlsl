//*********************************************************
//
// Copyright (c) Microsoft. All rights reserved.
// This code is licensed under the MIT License (MIT).
// THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF
// ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY
// IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR
// PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT.
//
// Adapted for VividRP from DirectX-Graphics-Samples:
// D3D12RaytracingRealTimeDenoisedAmbientOcclusion/RTAO/Shaders/Ray sorting.
//
//*********************************************************

#ifndef VIVIDRP_SOFTWARE_RAY_SORTING_INCLUDED
#define VIVIDRP_SOFTWARE_RAY_SORTING_INCLUDED


// Desc: Counting Sort of rays based on their origin depth and direction.
// Supports:
// - Up to 8K rays per ray group + 12 bit hash key
// - Max Ray Group dimensions: 64x128
// Rays can be disabled by setting their ray origin depth to 0 (i.e. invalidating them). 
// Such rays will get moved to the end of the sorted ray group 
// and have a source index offset of [0xff, 0xff]. 
// The ray hash is calculated from ray direction and origin depth
// Ref: Costa2014, Ray Reordering Techniques for GPU Ray-Cast Ambient Occlusion

// Algorithm: Counting Sort
// - Load ray origin depths and ray direction hashes into SMem.
// - Calculate min max origin depth per ray group.
// - Generate hash keys from ray directions and origin depths.
// - Calculates histograms for the key hashes.
// - Calculates a prefix sum of the histograms.
// - Scatter write the ray source index offsets based on their hash and the prefix sum for the hash key into SMem cache.
// - Linearly spill sorted ray source index offsets from SMem cache into VRAM.
// Shared Memory layout
// 1 - [8K 8b] - Ray Direction Hash
// 2 - [8K 16b] - Ray origin depth
// 3 - [4 x 512 x 16b] - Min/Max depth // In the final algorithm only small part is used of this range
// 4 - [8K 16b] - Ray hash key
// 5 - [4K 16b] - histrogram
// 6 - [8K 16b] - SrcRay index
//
//// Memory diagram:
//// - each lane represent 8 bits of a 32 bit element going from least to most significant bits top to bottom
//// - each column represents 2K elements
//// - each memory diagram represents the end state after an algorithm step.
//// - cells that changed are represent by an x in the second diagram on the right
//// - X-Y region is aliased with cells representing X or Y
//// - "-" zeroed out region
////
//// Memory layout at subsequent algorithm steps:
//// - Load ray origin depths and ray direction hashes into SMem.
////  | - - 1 1 |  Least significant bits
////  | - - 1 1 |
////  | 2 2 2 2 |
////  | 2 2 2 2 |  Most significant bits
////
//// - Calculate min max origin depth per ray group.
////  | 3 3 1 1 |     | x x - - | 
////  | 3 3 1 1 |     | x x - - | 
////  | 2 2 2 2 |     | - - - - | 
////  | 2 2 2 2 |     | - - - - | 
////
//// - Generate hash keys from ray directions and origin depths.
////  | 5 5 - - |     | x x x x | 
////  | 5 5 - - |     | x x x x | 
////  | 4 4 4 4 |     | x x x x | 
////  | 4 4 4 4 |     | x x x x | 
////
//// - Calculates a prefix sum of the histograms.
////  | 5 5 - - |     | x x - - | 
////  | 5 5 - - |     | x x - - | 
////  | 4 4 4 4 |     | - - - - | 
////  | 4 4 4 4 |     | - - - - | 
////
//// - Scatter write the ray source index offsets based on their hash and the prefix sum for the hash key into SMem cache.
////  | 5 5 6 6 |     | - - x x | 
////  | 5 5 6 6 |     | - - x x | 
////  | 4-6 4 4 |     | x x - - | 
////  | 4-6 4 4 |     | x x - - | 



// Software counting sort for coherent ray dispatch. This file only provides
// shader-library functions; it intentionally declares no resources or entry point.
// The caller must compile with DXC for Shader Model 6.0 or newer because the
// optional depth-key path uses wave intrinsics.
//
// Expected packed input layout:
//   bits  0..7  : octahedral direction X in [0, 1]
//   bits  8..15 : octahedral direction Y in [0, 1]
//   bits 16..31 : positive ray-origin depth as a half
// A zero depth marks an inactive ray. The output maps a sorted ray offset to its
// source offset inside the ray group. Bit 7 of output.y marks inactive rays.
//
// A future compute shader can call the library as follows:
//   [numthreads(
//       VividSoftwareRaySorting::ThreadGroupWidth,
//       VividSoftwareRaySorting::ThreadGroupHeight,
//       1)]
//   void SortRays(uint2 groupId : SV_GroupID, uint groupIndex : SV_GroupIndex)
//   {
//       VividSoftwareRaySorting::Sort(
//           _PackedRays, _SortedToSource, _Dimensions, true, 0.0,
//           groupId, groupIndex);
//   }

#ifndef VIVID_SOFTWARE_RAY_SORTING_DIRECTION_KEY_BITS_1D
#define VIVID_SOFTWARE_RAY_SORTING_DIRECTION_KEY_BITS_1D 4
#endif

// The upstream sample disables depth and index hashing by default because the
// extra sorting cost did not improve its ray-tracing time.
#ifndef VIVID_SOFTWARE_RAY_SORTING_DEPTH_KEY_BITS
#define VIVID_SOFTWARE_RAY_SORTING_DEPTH_KEY_BITS 0
#endif

#ifndef VIVID_SOFTWARE_RAY_SORTING_INDEX_KEY_BITS
#define VIVID_SOFTWARE_RAY_SORTING_INDEX_KEY_BITS 0
#endif

#define VIVID_SOFTWARE_RAY_SORTING_KEY_BITS \
    (VIVID_SOFTWARE_RAY_SORTING_DEPTH_KEY_BITS + \
     2 * VIVID_SOFTWARE_RAY_SORTING_DIRECTION_KEY_BITS_1D + \
     VIVID_SOFTWARE_RAY_SORTING_INDEX_KEY_BITS)
#define VIVID_SOFTWARE_RAY_SORTING_KEY_COUNT \
    (1 << VIVID_SOFTWARE_RAY_SORTING_KEY_BITS)

#if VIVID_SOFTWARE_RAY_SORTING_DIRECTION_KEY_BITS_1D > 4
#error Vivid Software Ray Sorting supports at most four direction-key bits per axis.
#endif

#if VIVID_SOFTWARE_RAY_SORTING_INDEX_KEY_BITS > 2
#error Vivid Software Ray Sorting supports at most two index-key bits.
#endif

#if VIVID_SOFTWARE_RAY_SORTING_KEY_BITS > 12
#error Vivid Software Ray Sorting supports at most 12 total key bits for 8192 rays.
#endif



//********************************************************************
// Ray Count SMem cache.
// Supports up to 16 bit (64K) ray counts per bin.
// Used for:
//  - to store number of binned rays. 
//  - as an intermediate cache for prefix sum computations.
// Stores 16bit values, with two values per entry.
// Stored as two ping-pong buffers.
// - Hi bits: odd ping-pong buffer ID
// - Lo bits: even ping-pong buffer ID
//********************************************************************

//********************************************************************
// SMEM stores 16 bit values, two 16bit values per 32bit entry:
// - Hi bits: odd indices
// - Lo bits: even indices
// SMEM is used for two mutually exclusive and temporally overlapping purposes
// so as to fit all caching within Shared Memory limits:
// - First it caches the hashed key per pixel - 15 bits
// - Second it caches the source index offset for a given sorted pixel - 2D 7+8bit index.
//   The values for the two purposes overlap in the cache during the shader execution. 
//   The key is generated first, but the source indices overwrite it later, while
//   the key may still be needer by another thread. 
//   Therefore the most significant bit is used
//   to denote whether the stored hashed key is still valid. 
//   If the key is no longer valid, it is regenerated again. 
//   To lower the collision and keep as many cached keys, the cache
//   is extended to the remaining shader memory limit and the keys
//   are stored at an offset. This way, the last 2 * offset
//   keys won't be invalidated.
//
//  PERFORMANCE tip:
//   Use as little rays and as small hash key bit size to leave 
//   as much room as possible for the hashed keys.


// The algorithm aliases several logical arrays into 32 KiB of group-shared
// memory. It therefore requires the fixed 64x16 thread group used upstream.
groupshared uint g_VividSoftwareRaySortingSharedMemory[8192];

namespace VividSoftwareRaySorting
{
    static const uint ThreadGroupWidth = 64;
    static const uint ThreadGroupHeight = 16;
    static const uint ThreadGroupSize = ThreadGroupWidth * ThreadGroupHeight;

    static const uint RayGroupWidth = 64;
    static const uint RayGroupHeight = 128;
    static const uint RayGroupSize = RayGroupWidth * RayGroupHeight;

    static const uint SharedMemorySize = 8192;
    static const uint MinimumWaveLaneCount = 16;
    static const uint MaximumWaveCount =
        (RayGroupSize + MinimumWaveLaneCount - 1) / MinimumWaveLaneCount;

    static const uint HistogramOffset = 0;
    static const uint DirectionKey8BitOffset = VIVID_SOFTWARE_RAY_SORTING_KEY_COUNT;
    static const uint Key16BitOffset = 8192;
    static const uint Depth16BitOffset = 8192;
    static const uint WaveDepthMinimumOffset = 0;
    static const uint WaveDepthMaximumOffset = MaximumWaveCount;
    static const uint RayIndexOffset = VIVID_SOFTWARE_RAY_SORTING_KEY_COUNT;

    static const uint InactiveRayKey = VIVID_SOFTWARE_RAY_SORTING_KEY_COUNT - 1;
    static const uint InactiveRayIndexBit = 0x4000;
    static const uint InactiveRayIndexBitY = 0x80;
    static const uint Invalid16BitKeyBit = 0x8000;
    static const float InvalidRayOriginDepth = 0.0;
    static const float MaximumHalfValue = 65504.0;
    static const float Pi = 3.14159265358979323846;

    bool IsActiveRay(uint2 rayGroupRayIndexOffset)
    {
        return (rayGroupRayIndexOffset.y & InactiveRayIndexBitY) == 0;
    }

    uint2 GetRawRayIndexOffset(uint2 rayGroupRayIndexOffset)
    {
        return uint2(
            rayGroupRayIndexOffset.x,
            rayGroupRayIndexOffset.y & ~InactiveRayIndexBitY);
    }
    //********************************************************************
    // Store a 16 bit value in the Shared Memory.
    // The 16 bit value is stored in 32bit value range <0, 8K)
    // in layered fashion to avoid bank conflicts on subsequent 
    // index accesses among subsequent threads. 
    // It is stored in 16bit layers at index16b starting from indexOffset32b at first row
    // which is the row of least significant 16 bits.
    //  index16B - 32bit offset up to (16K - 1).
    //  indexOffset32b - 32bit offset up to (8K - 1). 
    //  index16b / 2 + indexOffset32b must be less than 8K.
    // For example: 
    //  - index16b == {0 - 4}, 
    //  - indexOffset32b == 6
    //  Shared memory {8x32b}:
    //  | - - - - - 0 1 2 |  Least significant bits
    //  | - - - - - 0 1 2 |
    //  | 3 4 - - - - - - |
    //  | 3 4 - - - - - - |  Most significant bits

    void Store16BitUint(uint index16Bit, uint value, uint indexOffset32Bit)
    {
        uint offsetIndex = indexOffset32Bit + index16Bit;
        bool useHigh16Bits = offsetIndex >= SharedMemorySize;
        uint sharedMemoryIndex = offsetIndex - useHigh16Bits * SharedMemorySize;
        uint packedValue = (value & 0xffff) << (useHigh16Bits * 16);
        uint bitsToKeep = useHigh16Bits ? 0x0000ffff : 0xffff0000;

        InterlockedAnd(g_VividSoftwareRaySortingSharedMemory[sharedMemoryIndex], bitsToKeep);
        InterlockedAdd(g_VividSoftwareRaySortingSharedMemory[sharedMemoryIndex], packedValue);
    }

    void Store16BitUintLow(uint index16Bit, uint value, uint indexOffset32Bit)
    {
        uint sharedMemoryIndex = indexOffset32Bit + index16Bit;
        InterlockedAnd(g_VividSoftwareRaySortingSharedMemory[sharedMemoryIndex], 0xffff0000);
        InterlockedAdd(
            g_VividSoftwareRaySortingSharedMemory[sharedMemoryIndex],
            value & 0xffff);
    }

    uint Load16BitUint(uint index16Bit, uint indexOffset32Bit)
    {
        uint offsetIndex = indexOffset32Bit + index16Bit;
        bool useHigh16Bits = offsetIndex >= SharedMemorySize;
        uint sharedMemoryIndex = offsetIndex - useHigh16Bits * SharedMemorySize;
        uint packedValue = g_VividSoftwareRaySortingSharedMemory[sharedMemoryIndex];
        return (packedValue >> (useHigh16Bits * 16)) & 0xffff;
    }

    uint Load16BitUintLow(uint index16Bit, uint indexOffset32Bit)
    {
        uint packedValue =
            g_VividSoftwareRaySortingSharedMemory[indexOffset32Bit + index16Bit];
        return packedValue & 0xffff;
    }

    uint Load16BitUintHigh(uint index16Bit, uint indexOffset32Bit)
    {
        uint sharedMemoryIndex =
            (indexOffset32Bit + index16Bit) - SharedMemorySize;
        uint packedValue = g_VividSoftwareRaySortingSharedMemory[sharedMemoryIndex];
        return (packedValue >> 16) & 0xffff;
    }

    void Store16BitFloat(uint index16Bit, float value, uint indexOffset32Bit)
    {
        Store16BitUint(index16Bit, f32tof16(value), indexOffset32Bit);
    }

    void Store16BitFloatLow(uint index16Bit, float value, uint indexOffset32Bit)
    {
        Store16BitUintLow(index16Bit, f32tof16(value), indexOffset32Bit);
    }

    float Load16BitFloat(uint index16Bit, uint indexOffset32Bit)
    {
        return f16tof32(Load16BitUint(index16Bit, indexOffset32Bit));
    }

    float Load16BitFloatLow(uint index16Bit, uint indexOffset32Bit)
    {
        return f16tof32(Load16BitUintLow(index16Bit, indexOffset32Bit));
    }

    float Load16BitFloatHigh(uint index16Bit, uint indexOffset32Bit)
    {
        return f16tof32(Load16BitUintHigh(index16Bit, indexOffset32Bit));
    }

    uint AddTo16BitValue(uint index16Bit, uint value, uint indexOffset32Bit)
    {
        uint offsetIndex = indexOffset32Bit + index16Bit;
        bool useHigh16Bits = offsetIndex >= SharedMemorySize;
        uint sharedMemoryIndex = offsetIndex - useHigh16Bits * SharedMemorySize;
        uint packedValue = (value & 0xffff) << (useHigh16Bits * 16);
        uint previousValue;
        InterlockedAdd(
            g_VividSoftwareRaySortingSharedMemory[sharedMemoryIndex],
            packedValue,
            previousValue);

        return (previousValue >> (useHigh16Bits * 16)) & 0xffff;
    }

    void Store8BitUintLow(uint index8Bit, uint value, uint indexOffset32Bit)
    {
        uint offsetIndex = indexOffset32Bit + index8Bit;
        bool useHigh8Bits = offsetIndex >= SharedMemorySize;
        uint sharedMemoryIndex =
            offsetIndex - useHigh8Bits * (SharedMemorySize - indexOffset32Bit);
        uint packedValue = (value & 0xff) << (useHigh8Bits * 8);
        uint bitsToKeep = useHigh8Bits ? 0xffff00ff : 0xffffff00;

        InterlockedAnd(g_VividSoftwareRaySortingSharedMemory[sharedMemoryIndex], bitsToKeep);
        InterlockedAdd(g_VividSoftwareRaySortingSharedMemory[sharedMemoryIndex], packedValue);
    }

    uint Load8BitUintLow(uint index8Bit, uint indexOffset32Bit)
    {
        uint offsetIndex = indexOffset32Bit + index8Bit;
        bool useHigh8Bits = offsetIndex >= SharedMemorySize;
        uint sharedMemoryIndex =
            offsetIndex - useHigh8Bits * (SharedMemorySize - indexOffset32Bit);
        uint packedValue = g_VividSoftwareRaySortingSharedMemory[sharedMemoryIndex];
        return (packedValue >> (useHigh8Bits * 8)) & 0xff;
    }

    void InitializeSharedMemory(uint groupIndex)
    {
        for (uint index = groupIndex;
             index < SharedMemorySize;
             index += ThreadGroupSize)
        {
            g_VividSoftwareRaySortingSharedMemory[index] = 0;
        }
        GroupMemoryBarrierWithGroupSync();
    }

    void UnpackRay(uint packedRay, out float2 encodedDirection, out float originDepth)
    {
        encodedDirection = float2(
            (packedRay & 0xff) * (1.0 / 255.0),
            ((packedRay >> 8) & 0xff) * (1.0 / 255.0));
        originDepth = f16tof32(packedRay >> 16);
    }

    uint PackRay(float2 encodedDirection, float originDepth)
    {
        uint2 packedDirection = (uint2)round(saturate(encodedDirection) * 255.0);
        return
            packedDirection.x |
            (packedDirection.y << 8) |
            (f32tof16(max(originDepth, 0.0)) << 16);
    }

    float2 EncodeOctahedralDirection(float3 direction)
    {
        direction /= max(
            abs(direction.x) + abs(direction.y) + abs(direction.z),
            1e-6);
        float2 encodedDirection = direction.xy;
        if (direction.z < 0.0)
        {
            encodedDirection =
                (1.0 - abs(encodedDirection.yx)) *
                float2(
                    encodedDirection.x >= 0.0 ? 1.0 : -1.0,
                    encodedDirection.y >= 0.0 ? 1.0 : -1.0);
        }
        return encodedDirection * 0.5 + 0.5;
    }

    uint PackRay(float3 direction, float originDepth)
    {
        return PackRay(EncodeOctahedralDirection(direction), originDepth);
    }

    float3 DecodeOctahedralDirection(float2 encodedDirection)
    {
        float2 value = encodedDirection * 2.0 - 1.0;
        float3 direction = float3(
            value.x,
            value.y,
            1.0 - abs(value.x) - abs(value.y));
        float fold = saturate(-direction.z);
        direction.x += direction.x >= 0.0 ? -fold : fold;
        direction.y += direction.y >= 0.0 ? -fold : fold;
        return normalize(direction);
    }

    uint CreateDirectionHashKey(
        float2 encodedDirection,
        bool useOctahedralDirectionQuantization)
    {
        float2 directionKey = encodedDirection;
        if (!useOctahedralDirectionQuantization)
        {
            float3 direction = DecodeOctahedralDirection(encodedDirection);
            float azimuthAngle = atan2(direction.y, direction.x);
            float polarAngle = acos(direction.z);
            directionKey = float2(
                azimuthAngle / (2.0 * Pi) + 0.5,
                polarAngle / Pi);
        }

        const uint DirectionKeyBinCount1D =
            1 << VIVID_SOFTWARE_RAY_SORTING_DIRECTION_KEY_BITS_1D;
        const uint MaximumDirectionKeyBin = DirectionKeyBinCount1D - 1;
        uint2 quantizedDirection = (uint2)min(
            saturate(directionKey) * MaximumDirectionKeyBin,
            MaximumDirectionKeyBin);

        return
            (quantizedDirection.y << VIVID_SOFTWARE_RAY_SORTING_DIRECTION_KEY_BITS_1D) |
            quantizedDirection.x;
    }

    uint CreateDepthHashKey(
        float originDepth,
        float2 rayGroupMinimumMaximumDepth,
        float binDepthSize)
    {
#if VIVID_SOFTWARE_RAY_SORTING_DEPTH_KEY_BITS == 0
        return 0;
#else
        float relativeDepth = originDepth - rayGroupMinimumMaximumDepth.x;
        float rayGroupDepthRange =
            rayGroupMinimumMaximumDepth.y - rayGroupMinimumMaximumDepth.x;
        const uint DepthKeyBinCount =
            1 << VIVID_SOFTWARE_RAY_SORTING_DEPTH_KEY_BITS;
        const uint MaximumDepthKeyBin = DepthKeyBinCount - 1;
        float depthBinSize = max(
            rayGroupDepthRange / MaximumDepthKeyBin,
            binDepthSize);
        return min((uint)(relativeDepth / depthBinSize), MaximumDepthKeyBin);
#endif
    }

    uint CreateIndexHashKey(uint2 rayIndex)
    {
#if VIVID_SOFTWARE_RAY_SORTING_INDEX_KEY_BITS == 0
        return 0;
#else
        const uint IndexKeyBinCount =
            1 << VIVID_SOFTWARE_RAY_SORTING_INDEX_KEY_BITS;
        const uint MaximumIndexKeyBin = IndexKeyBinCount - 1;
        uint quadrant =
            ((rayIndex.y >= RayGroupHeight / 2) << 1) |
            (rayIndex.x >= RayGroupWidth / 2);
        return
            (quadrant >> (2 - VIVID_SOFTWARE_RAY_SORTING_INDEX_KEY_BITS)) &
            MaximumIndexKeyBin;
#endif
    }

    uint CreateRayHashKey(
        uint2 rayIndex,
        uint directionHashKey,
        float originDepth,
        float2 rayGroupMinimumMaximumDepth,
        float binDepthSize)
    {
        uint depthHashKey = CreateDepthHashKey(
            originDepth,
            rayGroupMinimumMaximumDepth,
            binDepthSize);
        uint indexHashKey = CreateIndexHashKey(rayIndex);
        uint hashKey =
            (depthHashKey <<
                (2 * VIVID_SOFTWARE_RAY_SORTING_DIRECTION_KEY_BITS_1D +
                 VIVID_SOFTWARE_RAY_SORTING_INDEX_KEY_BITS)) |
            (directionHashKey << VIVID_SOFTWARE_RAY_SORTING_INDEX_KEY_BITS) |
            indexHashKey;

        return min(hashKey, InactiveRayKey - 1);
    }

    uint CreateRayHashKey(
        uint2 rayIndex,
        float2 encodedDirection,
        float originDepth,
        float2 rayGroupMinimumMaximumDepth,
        bool useOctahedralDirectionQuantization,
        float binDepthSize)
    {
        uint directionHashKey = CreateDirectionHashKey(
            encodedDirection,
            useOctahedralDirectionQuantization);
        return CreateRayHashKey(
            rayIndex,
            directionHashKey,
            originDepth,
            rayGroupMinimumMaximumDepth,
            binDepthSize);
    }

    uint2 GetRayGroupDimensions(uint2 groupId, uint2 dimensions)
    {
        uint2 groupStart = groupId * uint2(RayGroupWidth, RayGroupHeight);
        uint2 remainingDimensions = uint2(
            groupStart.x < dimensions.x ? dimensions.x - groupStart.x : 0,
            groupStart.y < dimensions.y ? dimensions.y - groupStart.y : 0);
        return min(
            remainingDimensions,
            uint2(RayGroupWidth, RayGroupHeight));
    }

    void CachePartialHashKeysAndDepths(
        Texture2D<uint> packedRays,
        uint2 dimensions,
        bool useOctahedralDirectionQuantization,
        uint2 groupId,
        uint groupIndex)
    {
        uint2 rayGroupDimensions = GetRayGroupDimensions(groupId, dimensions);
        uint2 groupStart = groupId * uint2(RayGroupWidth, RayGroupHeight);
        uint rayCount = rayGroupDimensions.x * rayGroupDimensions.y;

        for (uint ray = groupIndex; ray < rayCount; ray += ThreadGroupSize)
        {
            uint2 rayIndex = uint2(
                ray % rayGroupDimensions.x,
                ray / rayGroupDimensions.x);
            uint2 pixel = groupStart + rayIndex;
            float2 encodedDirection;
            float originDepth;
            UnpackRay(packedRays[pixel], encodedDirection, originDepth);

            uint directionHashKey = 0;
            if (originDepth != InvalidRayOriginDepth)
            {
                directionHashKey = CreateDirectionHashKey(
                    encodedDirection,
                    useOctahedralDirectionQuantization);
            }

            Store16BitFloat(ray, originDepth, Depth16BitOffset);
            Store8BitUintLow(ray, directionHashKey, DirectionKey8BitOffset);
        }
        GroupMemoryBarrierWithGroupSync();
    }

    float2 CalculateRayGroupMinimumMaximumDepth(
        uint2 dimensions,
        uint2 groupId,
        uint groupIndex)
    {
#if VIVID_SOFTWARE_RAY_SORTING_DEPTH_KEY_BITS == 0
        return 0.0;
#else
        uint2 rayGroupDimensions = GetRayGroupDimensions(groupId, dimensions);
        uint rayCount = rayGroupDimensions.x * rayGroupDimensions.y;

        // Match the upstream estimate: one wave samples the cached depths at
        // sparse, bank-friendly positions instead of performing a full reduction.
        if (groupIndex < WaveGetLaneCount())
        {
            uint sampleDistance = rayCount / WaveGetLaneCount();
            uint sampleIndex = groupIndex * sampleDistance;
            const uint MaximumWaveLaneCount = 128;
            uint mask = ~(MaximumWaveLaneCount - 1);
            sampleIndex = (sampleIndex & mask) + groupIndex;
            sampleIndex = min(sampleIndex, max(rayCount, 1u) - 1);

            float originDepth =
                Load16BitFloatHigh(sampleIndex, Depth16BitOffset);
            bool isRayValid = originDepth != InvalidRayOriginDepth;
            float waveDepthMinimum = WaveActiveMin(
                isRayValid ? originDepth : MaximumHalfValue);
            float waveDepthMaximum = WaveActiveMax(originDepth);

            if (WaveGetLaneIndex() == 0)
            {
                Store16BitFloatLow(
                    0,
                    waveDepthMinimum,
                    WaveDepthMinimumOffset);
                Store16BitFloatLow(
                    0,
                    waveDepthMaximum,
                    WaveDepthMaximumOffset);
            }
        }
        GroupMemoryBarrierWithGroupSync();

        return float2(
            Load16BitFloatLow(0, WaveDepthMinimumOffset),
            Load16BitFloatLow(0, WaveDepthMaximumOffset));
#endif
    }

    void FinalizeHashKeysAndCalculateHistogram(
        uint2 dimensions,
        uint2 groupId,
        uint groupIndex,
        float2 rayGroupMinimumMaximumDepth,
        float binDepthSize)
    {
        for (uint key = groupIndex;
             key < VIVID_SOFTWARE_RAY_SORTING_KEY_COUNT;
             key += ThreadGroupSize)
        {
            Store16BitUint(key, 0, HistogramOffset);
        }
        GroupMemoryBarrierWithGroupSync();

        uint2 rayGroupDimensions = GetRayGroupDimensions(groupId, dimensions);
        uint rayCount = rayGroupDimensions.x * rayGroupDimensions.y;
        for (uint ray = groupIndex; ray < rayCount; ray += ThreadGroupSize)
        {
            float originDepth = Load16BitFloat(ray, Depth16BitOffset);
            uint hashKey = InactiveRayKey;
            if (originDepth != InvalidRayOriginDepth)
            {
                uint directionHashKey = Load8BitUintLow(
                    ray,
                    DirectionKey8BitOffset);
                uint2 rayIndex = uint2(
                    ray % rayGroupDimensions.x,
                    ray / rayGroupDimensions.x);
                hashKey = CreateRayHashKey(
                    rayIndex,
                    directionHashKey,
                    originDepth,
                    rayGroupMinimumMaximumDepth,
                    binDepthSize);
            }

            AddTo16BitValue(hashKey, 1, HistogramOffset);
            Store16BitUint(ray, hashKey, Key16BitOffset);
        }
        GroupMemoryBarrierWithGroupSync();
    }

    void GenerateHashKeysAndHistogram(
        Texture2D<uint> packedRays,
        uint2 dimensions,
        bool useOctahedralDirectionQuantization,
        float binDepthSize,
        uint2 groupId,
        uint groupIndex,
        out float2 rayGroupMinimumMaximumDepth)
    {
        CachePartialHashKeysAndDepths(
            packedRays,
            dimensions,
            useOctahedralDirectionQuantization,
            groupId,
            groupIndex);
        rayGroupMinimumMaximumDepth = CalculateRayGroupMinimumMaximumDepth(
            dimensions,
            groupId,
            groupIndex);
        FinalizeHashKeysAndCalculateHistogram(
            dimensions,
            groupId,
            groupIndex,
            rayGroupMinimumMaximumDepth,
            binDepthSize);
    }

    void PrefixSum(uint groupIndex)
    {
        // VIVID_SOFTWARE_RAY_SORTING_KEY_COUNT is a power of two.
        for (uint step = 2;
             step <= VIVID_SOFTWARE_RAY_SORTING_KEY_COUNT;
             step <<= 1)
        {
            uint stepCount = VIVID_SOFTWARE_RAY_SORTING_KEY_COUNT / step;
            for (uint item = groupIndex;
                 item < stepCount;
                 item += ThreadGroupSize)
            {
                uint baseIndex = item * step;
                uint leftIndex = baseIndex + step / 2 - 1;
                uint rightIndex = baseIndex + step - 1;
                uint sum =
                    Load16BitUint(leftIndex, HistogramOffset) +
                    Load16BitUint(rightIndex, HistogramOffset);
                Store16BitUint(rightIndex, sum, HistogramOffset);
            }
            GroupMemoryBarrierWithGroupSync();
        }

        if (groupIndex == 0)
        {
            Store16BitUint(
                VIVID_SOFTWARE_RAY_SORTING_KEY_COUNT - 1,
                0,
                HistogramOffset);
        }
        GroupMemoryBarrierWithGroupSync();

        for (uint step = VIVID_SOFTWARE_RAY_SORTING_KEY_COUNT;
             step >= 2;
             step >>= 1)
        {
            uint stepCount = VIVID_SOFTWARE_RAY_SORTING_KEY_COUNT / step;
            for (uint item = groupIndex;
                 item < stepCount;
                 item += ThreadGroupSize)
            {
                uint baseIndex = item * step;
                uint leftIndex = baseIndex + step / 2 - 1;
                uint rightIndex = baseIndex + step - 1;
                uint leftValue = Load16BitUint(leftIndex, HistogramOffset);
                uint rightValue = Load16BitUint(rightIndex, HistogramOffset);

                Store16BitUint(leftIndex, rightValue, HistogramOffset);
                Store16BitUint(
                    rightIndex,
                    leftValue + rightValue,
                    HistogramOffset);
            }
            GroupMemoryBarrierWithGroupSync();
        }
    }

    uint FlattenRayIndex(uint2 index)
    {
        return index.x + (index.y << 7);
    }

    uint2 UnflattenRayIndex(uint index)
    {
        return uint2(index & 0x7f, index >> 7);
    }

    void ScatterSortedIndicesToSharedMemory(
        Texture2D<uint> packedRays,
        uint2 dimensions,
        bool useOctahedralDirectionQuantization,
        float binDepthSize,
        uint2 groupId,
        uint groupIndex,
        float2 rayGroupMinimumMaximumDepth)
    {
        uint2 rayGroupDimensions = GetRayGroupDimensions(groupId, dimensions);
        uint2 groupStart = groupId * uint2(RayGroupWidth, RayGroupHeight);
        uint rayCount = rayGroupDimensions.x * rayGroupDimensions.y;

        for (uint ray = groupIndex; ray < rayCount; ray += ThreadGroupSize)
        {
            uint2 rayIndex = uint2(
                ray % rayGroupDimensions.x,
                ray / rayGroupDimensions.x);
            uint key;
            bool isRayValid;

            uint cachedValue = Load16BitUint(ray, Key16BitOffset);
            bool isHashKeyEntry = (cachedValue & Invalid16BitKeyBit) == 0;
            if (isHashKeyEntry)
            {
                isRayValid = cachedValue != InactiveRayKey;
                key = cachedValue;
            }
            else
            {
                float2 encodedDirection;
                float originDepth;
                UnpackRay(
                    packedRays[groupStart + rayIndex],
                    encodedDirection,
                    originDepth);
                isRayValid = originDepth != InvalidRayOriginDepth;
                key = isRayValid
                    ? CreateRayHashKey(
                        rayIndex,
                        encodedDirection,
                        originDepth,
                        rayGroupMinimumMaximumDepth,
                        useOctahedralDirectionQuantization,
                        binDepthSize)
                    : InactiveRayKey;
            }

            uint sortedIndex = AddTo16BitValue(key, 1, HistogramOffset);
            uint encodedRayIndex = FlattenRayIndex(rayIndex);
            encodedRayIndex |= isRayValid ? 0 : InactiveRayIndexBit;
            encodedRayIndex |= Invalid16BitKeyBit;
            Store16BitUint(sortedIndex, encodedRayIndex, RayIndexOffset);
        }
        GroupMemoryBarrierWithGroupSync();
    }

    void SpillSortedIndices(
        RWTexture2D<uint2> sortedToSourceRayIndexOffset,
        uint2 dimensions,
        uint2 groupId,
        uint groupIndex)
    {
        uint2 rayGroupDimensions = GetRayGroupDimensions(groupId, dimensions);
        uint2 groupStart = groupId * uint2(RayGroupWidth, RayGroupHeight);
        uint rayCount = rayGroupDimensions.x * rayGroupDimensions.y;

        for (uint index = groupIndex; index < rayCount; index += ThreadGroupSize)
        {
            uint packedSourceIndex = Load16BitUint(index, RayIndexOffset);
            packedSourceIndex &= ~Invalid16BitKeyBit;
            uint2 sortedIndex = uint2(
                index % rayGroupDimensions.x,
                index / rayGroupDimensions.x);
            sortedToSourceRayIndexOffset[groupStart + sortedIndex] =
                UnflattenRayIndex(packedSourceIndex);
        }
    }

    void Sort(
        Texture2D<uint> packedRays,
        RWTexture2D<uint2> sortedToSourceRayIndexOffset,
        uint2 dimensions,
        bool useOctahedralDirectionQuantization,
        float binDepthSize,
        uint2 groupId,
        uint groupIndex)
    {
        InitializeSharedMemory(groupIndex);

        float2 rayGroupMinimumMaximumDepth;
        GenerateHashKeysAndHistogram(
            packedRays,
            dimensions,
            useOctahedralDirectionQuantization,
            binDepthSize,
            groupId,
            groupIndex,
            rayGroupMinimumMaximumDepth);

        PrefixSum(groupIndex);

        ScatterSortedIndicesToSharedMemory(
            packedRays,
            dimensions,
            useOctahedralDirectionQuantization,
            binDepthSize,
            groupId,
            groupIndex,
            rayGroupMinimumMaximumDepth);

        SpillSortedIndices(
            sortedToSourceRayIndexOffset,
            dimensions,
            groupId,
            groupIndex);
    }
}

#undef VIVID_SOFTWARE_RAY_SORTING_KEY_COUNT
#undef VIVID_SOFTWARE_RAY_SORTING_KEY_BITS

#endif // VIVIDRP_SOFTWARE_RAY_SORTING_INCLUDED
