using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace VividRP.Runtime.GPUDriven.Meshlets
{
    internal static class VividMeshSdfBricks
    {
        // Keep these constants in sync with MeshSdfTrace.hlsl.
        internal const int CellSize = 12;
        internal const int StoredSize = CellSize + 1;
        internal const uint EmptyOutside = uint.MaxValue;
        internal const uint EmptyInside = uint.MaxValue - 1;
        internal const uint Dense = uint.MaxValue;
        internal const float BandVoxels = 2f;

        internal static int3 BrickCount(int3 dimensions) => (dimensions + CellSize - 2) / CellSize;

        internal static uint PlanAtlas(NativeArray<ushort> source, int3 dimensions, float voxelSize,
            List<uint> pages, ref int nextSlot)
        {
            uint first = (uint)pages.Count;
            int3 counts = BrickCount(dimensions);
            int band = ((half)(voxelSize * BandVoxels)).value;
            for (int z = 0; z < counts.z; z++)
            for (int y = 0; y < counts.y; y++)
            for (int x = 0; x < counts.x; x++)
            {
                int3 origin = new int3(x, y, z) * CellSize;
                int3 size = math.min(dimensions - origin, StoredSize);
                uint entry = Classify(source, dimensions, origin, size, band);
                pages.Add(entry == 0 ? (uint)nextSlot++ : entry);
            }
            return first;
        }

        internal static bool TryGetAtlasLayout(int slots, int maxDimension, out int3 layout)
        {
            layout = default;
            int maxAcross = maxDimension / StoredSize;
            if (maxAcross < 1 || (long)maxAcross * maxAcross * maxAcross < slots)
                return false;

            int across = 1;
            while ((long)across * across * across < slots)
                across++;
            int layers = math.max(1, (slots + across * across - 1) / (across * across));
            layout = new int3(across, across, layers);
            // Unity's staging and texture upload sizes must fit in a signed byte count.
            return (long)layout.x * layout.y * layout.z * StoredSize * StoredSize * StoredSize * sizeof(ushort) <= int.MaxValue;
        }

        internal static void WriteAtlas(NativeArray<ushort> source, int3 dimensions, List<uint> pages,
            uint first, NativeArray<ushort> destination, int across)
        {
            int3 counts = BrickCount(dimensions);
            int page = (int)first;
            int width = across * StoredSize;
            for (int z = 0; z < counts.z; z++)
            for (int y = 0; y < counts.y; y++)
            for (int x = 0; x < counts.x; x++)
            {
                uint entry = pages[page++];
                if (entry == EmptyOutside || entry == EmptyInside)
                    continue;
                int slot = (int)entry;
                int3 tile = new int3(slot % across, slot / across % across, slot / (across * across)) * StoredSize;
                int3 origin = new int3(x, y, z) * CellSize;
                int3 size = math.min(dimensions - origin, StoredSize);
                for (int slice = 0; slice < size.z; slice++)
                for (int row = 0; row < size.y; row++)
                {
                    int read = origin.x + dimensions.x * (origin.y + row + dimensions.y * (origin.z + slice));
                    int write = tile.x + width * (tile.y + row + width * (tile.z + slice));
                    NativeArray<ushort>.Copy(source, read, destination, write, size.x);
                }
            }
        }

        internal static int Plan(
            NativeArray<ushort> source, int3 dimensions, float voxelSize, List<uint> pages, out uint pageOffset)
        {
            int denseWords = (source.Length + 1) / 2;
            pageOffset = Dense;
            int3 counts = BrickCount(dimensions);
            if (math.any(counts == 0))
                return denseWords;

            int start = pages.Count;
            int payloadWords = 0;
            int band = ((half)(voxelSize * BandVoxels)).value;
            for (int z = 0; z < counts.z; z++)
            for (int y = 0; y < counts.y; y++)
            for (int x = 0; x < counts.x; x++)
            {
                var origin = new int3(x, y, z) * CellSize;
                int3 size = math.min(dimensions - origin, CellSize + 1);
                uint entry = Classify(source, dimensions, origin, size, band);
                if (entry != EmptyOutside && entry != EmptyInside)
                {
                    entry = (uint)payloadWords;
                    payloadWords += (size.x * size.y * size.z + 1) / 2;
                }
                pages.Add(entry);
            }

            if (payloadWords + pages.Count - start >= denseWords)
            {
                pages.RemoveRange(start, pages.Count - start);
                return denseWords;
            }
            pageOffset = (uint)start;
            return payloadWords;
        }

        internal static void Write(
            NativeArray<ushort> source, int3 dimensions, List<uint> pages, uint pageOffset,
            NativeArray<ushort> destination, int destinationTexel)
        {
            if (pageOffset == Dense)
            {
                NativeArray<ushort>.Copy(source, 0, destination, destinationTexel, source.Length);
                return;
            }

            int3 counts = BrickCount(dimensions);
            int page = (int)pageOffset;
            for (int z = 0; z < counts.z; z++)
            for (int y = 0; y < counts.y; y++)
            for (int x = 0; x < counts.x; x++)
            {
                uint entry = pages[page++];
                if (entry == EmptyOutside || entry == EmptyInside)
                    continue;

                var origin = new int3(x, y, z) * CellSize;
                int3 size = math.min(dimensions - origin, CellSize + 1);
                int writeIndex = destinationTexel + 2 * (int)entry;
                for (int slice = 0; slice < size.z; slice++)
                for (int row = 0; row < size.y; row++)
                {
                    int readIndex = origin.x + dimensions.x * (origin.y + row + dimensions.y * (origin.z + slice));
                    NativeArray<ushort>.Copy(source, readIndex, destination, writeIndex, size.x);
                    writeIndex += size.x;
                }
            }
        }

        private static uint Classify(NativeArray<ushort> source, int3 dimensions, int3 origin, int3 size, int band)
        {
            // Include the upper sample face so every cell in an omitted brick interpolates to the same sign.
            int sign = source[origin.x + dimensions.x * (origin.y + dimensions.y * origin.z)] & 0x8000;
            for (int z = 0; z < size.z; z++)
            for (int y = 0; y < size.y; y++)
            {
                int index = origin.x + dimensions.x * (origin.y + y + dimensions.y * (origin.z + z));
                for (int x = 0; x < size.x; x++)
                {
                    int bits = source[index + x];
                    int magnitude = bits & 0x7fff;
                    if ((bits & 0x8000) != sign || magnitude <= band || magnitude >= 0x7c00)
                        return 0;
                }
            }
            return sign == 0 ? EmptyOutside : EmptyInside;
        }
    }
}
