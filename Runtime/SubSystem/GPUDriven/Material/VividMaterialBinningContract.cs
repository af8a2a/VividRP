using UnityEngine;

namespace VividRP.Runtime.GPUDriven
{
    /// <summary>
    /// Shared C# / HLSL contract for Phase 3.1 VisibilityBuffer per-ProgramID
    /// Material Tile Binning (VisibilityBufferGBufferResolve.compute).
    /// </summary>
    internal static class VividMaterialBinningContract
    {
        internal const int TileSize = 8;

        // Bin 0: Empty/Sky clear tiles
        // Bins [1 .. MaxDirectProgramBins]: Direct ProgramID bins (1-indexed)
        // Bin [MaxDirectProgramBins + 1]: Overflow catch-all for ProgramID > MaxDirectProgramBins or invalid/error pixels
        internal const int EmptyBinIndex = 0;
        internal const int FirstDirectProgramBinIndex = 1;
        internal const int MaxDirectProgramBins = 64;
        internal const int OverflowBinIndex = FirstDirectProgramBinIndex + MaxDirectProgramBins; // 65
        internal const int TotalBinCount = OverflowBinIndex + 1; // 66

        // Each bin gets 3 uints of DispatchIndirect arguments: (tileCount, 1, 1)
        internal const int IndirectArgsUintsPerBin = 3;
        internal const int IndirectArgsByteStride = IndirectArgsUintsPerBin * sizeof(uint); // 12 bytes
        internal const int TotalIndirectArgsUints = TotalBinCount * IndirectArgsUintsPerBin; // 198 uints

        internal static int ComputeTileCountX(int width)
        {
            return Mathf.Max(1, (width + TileSize - 1) / TileSize);
        }

        internal static int ComputeTileCountY(int height)
        {
            return Mathf.Max(1, (height + TileSize - 1) / TileSize);
        }

        internal static int ComputeTileStride(int width, int height)
        {
            return ComputeTileCountX(width) * ComputeTileCountY(height);
        }

        internal static int ComputeTileListElementCount(int width, int height)
        {
            return TotalBinCount * ComputeTileStride(width, height);
        }

        internal static int MapProgramIdToBinIndex(uint programId, bool validProgram)
        {
            if (!validProgram || programId == 0u)
                return OverflowBinIndex;

            if (programId <= (uint)MaxDirectProgramBins)
                return (int)programId;

            return OverflowBinIndex;
        }

        internal static int GetIndirectArgsByteOffset(int binIndex)
        {
            return binIndex * IndirectArgsByteStride;
        }
    }
}
