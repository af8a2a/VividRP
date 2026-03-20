#ifndef TILE_CLASSIFICATION_INCLUDED
#define TILE_CLASSIFICATION_INCLUDED

// 推荐在外部定义 TILE_SIZE，默认提供宏备用
#ifndef CLASSIFY_TILE_SIZE
#define CLASSIFY_TILE_SIZE 8
#endif


// 自动检测 SM 6.0，或者允许通过外部定义强制开启
#if defined(UNITY_COMPILER_DXC)
#define USE_WAVE_INTRINSICS 1
#else
#define USE_WAVE_INTRINSICS 0
#endif
// 使用一个 uint 的不同 bit 来支持同一 Pass 中的多种分类（例如：Tile同时包含SSS和ClearCoat）
groupshared uint gs_TileMask;

namespace TileClassifaction
{
    /// 将 2D Tile 坐标打包为单个 32-bit uint
    /// 低 16 位存储 X，高 16 位存储 Y
    inline uint PackTileCoord(uint2 coord)
    {
        // & 0xFFFF 是防御性编程，防止出现负数或超大值溢出污染高位
        return (coord.x & 0xFFFF) | (coord.y << 16);
    }

    /// 将单个 32-bit uint 解包为 2D Tile 坐标
    inline uint2 UnpackTileCoord(uint packedCoord)
    {
        return uint2(packedCoord & 0xFFFF, packedCoord >> 16);
    }

    /// 1. 在 Compute Shader 开头调用，初始化共享内存
    void InitializeTileClassification(uint groupIndex)
    {
        if (groupIndex == 0)
        {
            gs_TileMask = 0;
        }
        GroupMemoryBarrierWithGroupSync();
    }

    /// 2. 每个线程基于当前像素状态调用，提交分类结果 (mask位表示具体的分类类别)
    void SubmitPixelClassification(uint classificationMask)
    {
        #if USE_WAVE_INTRINSICS
        // [SM 6.0 优化路径]
        // 1. 在 Wave 内部进行比特位或运算 (寄存器级，无内存访问)
        uint waveCombinedMask = WaveActiveBitOr(classificationMask);

        // 2. 只有该 Wave 的第一个活跃 Lane 负责写入 groupshared
        // 这将原子冲突次数从 'ThreadsPerGroup' 降到了 'ThreadsPerGroup / WaveSize'
        if (WaveIsFirstLane())
        {
            InterlockedOr(gs_TileMask, waveCombinedMask);
        }
        #else
        // [回退路径]
        if (classificationMask > 0)
        {
            InterlockedOr(gs_TileMask, classificationMask);
        }
        #endif
        // 注意：即便使用了 Wave 指令，跨 Wave 的同步依然需要 Barrier
        GroupMemoryBarrierWithGroupSync();
    }

    /// 3. 生成 Indirect Dispatch 参数并写入打包后的 Tile ID
    void FinalizeTileClassificationDispatch(
        uint groupIndex,
        uint2 tileCoord,
        uint targetMask,
        RWByteAddressBuffer indirectArgs,
        uint argsByteOffset,
        RWStructuredBuffer<uint> tileList) // <--- 更新为 uint
    {
        if (groupIndex == 0 && (gs_TileMask & targetMask) != 0)
        {
            uint globalTileIndex;
            // Dispatch(tileCount, 1, 1)，累加 DispatchArgs.x
            indirectArgs.InterlockedAdd(argsByteOffset, 1, globalTileIndex);

            // 写入打包后的坐标
            tileList[globalTileIndex] = PackTileCoord(tileCoord);
        }
    }

    /// 3b. 针对 Indirect Draw 的重载
    void FinalizeTileClassificationDraw(
        uint groupIndex,
        uint2 tileCoord,
        uint targetMask,
        RWByteAddressBuffer indirectArgs,
        uint argsByteOffset,
        RWStructuredBuffer<uint> tileList) // <--- 更新为 uint
    {
        if (groupIndex == 0 && (gs_TileMask & targetMask) != 0)
        {
            uint globalInstanceIndex;
            // DrawInstancedIndirect，累加 instanceCount
            indirectArgs.InterlockedAdd(argsByteOffset, 1, globalInstanceIndex);

            // 写入打包后的坐标
            tileList[globalInstanceIndex] = PackTileCoord(tileCoord);
        }
    }
}
#endif // TILE_CLASSIFICATION_INCLUDED
