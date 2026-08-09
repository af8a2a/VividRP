using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace VividRP.Runtime
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public static unsafe class MeshOptimizerBindings
    {
        private const string DllName = "meshoptimizer";
        private const CallingConvention DllCallingConvention = CallingConvention.Cdecl;

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_generateVertexRemap(uint* destination, uint* indices, nuint indexCount, void* vertices,
            nuint vertexCount, nuint vertexSize);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_generateVertexRemapMulti(uint* destination, uint* indices, nuint indexCount, nuint vertexCount,
            meshopt_Stream* streams, nuint streamCount);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_generateVertexRemapCustom(uint* destination, uint* indices, nuint indexCount, float* vertexPositions,
            nuint vertexCount, nuint vertexPositionsStride, IntPtr callback, void* context);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_remapVertexBuffer(void* destination, void* vertices, nuint vertexCount, nuint vertexSize, uint* remap);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_remapIndexBuffer(uint* destination, uint* indices, nuint indexCount, uint* remap);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_generateShadowIndexBuffer(uint* destination, uint* indices, nuint indexCount, void* vertices,
            nuint vertexCount, nuint vertexSize, nuint vertexStride);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_generateShadowIndexBufferMulti(uint* destination, uint* indices, nuint indexCount, nuint vertexCount,
            meshopt_Stream* streams, nuint streamCount);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_generatePositionRemap(uint* destination, float* vertexPositions, nuint vertexCount,
            nuint vertexPositionsStride);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_generateAdjacencyIndexBuffer(uint* destination, uint* indices, nuint indexCount, float* vertexPositions,
            nuint vertexCount, nuint vertexPositionsStride);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_generateTessellationIndexBuffer(uint* destination, uint* indices, nuint indexCount,
            float* vertexPositions, nuint vertexCount, nuint vertexPositionsStride);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_generateProvokingIndexBuffer(uint* destination, uint* reorder, uint* indices, nuint indexCount,
            nuint vertexCount);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_optimizeVertexCache(uint* destination, uint* indices, nuint indexCount, nuint vertexCount);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_optimizeVertexCacheStrip(uint* destination, uint* indices, nuint indexCount, nuint vertexCount);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_optimizeVertexCacheFifo(uint* destination, uint* indices, nuint indexCount, nuint vertexCount,
            uint cacheSize);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_optimizeOverdraw(uint* destination, uint* indices, nuint indexCount, float* vertexPositions,
            nuint vertexCount, nuint vertexPositionsStride, float threshold);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_optimizeVertexFetch(void* destination, uint* indices, nuint indexCount, void* vertices,
            nuint vertexCount, nuint vertexSize);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_optimizeVertexFetchRemap(uint* destination, uint* indices, nuint indexCount, nuint vertexCount);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_encodeIndexBuffer(byte* buffer, nuint bufferSize, uint* indices, nuint indexCount);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_encodeIndexBufferBound(nuint indexCount, nuint vertexCount);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_encodeIndexVersion(int version);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern int meshopt_decodeIndexBuffer(void* destination, nuint indexCount, nuint indexSize, byte* buffer, nuint bufferSize);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern int meshopt_decodeIndexVersion(byte* buffer, nuint bufferSize);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_encodeIndexSequence(byte* buffer, nuint bufferSize, uint* indices, nuint indexCount);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_encodeIndexSequenceBound(nuint indexCount, nuint vertexCount);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern int meshopt_decodeIndexSequence(void* destination, nuint indexCount, nuint indexSize, byte* buffer, nuint bufferSize);

        // Experimental in meshoptimizer v1.1; ABI may change in a later release.
        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_encodeMeshlet(byte* buffer, nuint bufferSize, uint* vertices, nuint vertexCount, byte* triangles,
            nuint triangleCount);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_encodeMeshletBound(nuint maxVertices, nuint maxTriangles);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern int meshopt_decodeMeshlet(void* vertices, nuint vertexCount, nuint vertexSize, void* triangles,
            nuint triangleCount, nuint triangleSize, byte* buffer, nuint bufferSize);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern int meshopt_decodeMeshletRaw(uint* vertices, nuint vertexCount, uint* triangles, nuint triangleCount, byte* buffer,
            nuint bufferSize);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_encodeVertexBuffer(byte* buffer, nuint bufferSize, void* vertices, nuint vertexCount, nuint vertexSize);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_encodeVertexBufferBound(nuint vertexCount, nuint vertexSize);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_encodeVertexBufferLevel(byte* buffer, nuint bufferSize, void* vertices, nuint vertexCount,
            nuint vertexSize, int level, int version);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_encodeVertexVersion(int version);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern int meshopt_decodeVertexBuffer(void* destination, nuint vertexCount, nuint vertexSize, byte* buffer, nuint bufferSize);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern int meshopt_decodeVertexVersion(byte* buffer, nuint bufferSize);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_decodeFilterOct(void* buffer, nuint count, nuint stride);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_decodeFilterQuat(void* buffer, nuint count, nuint stride);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_decodeFilterExp(void* buffer, nuint count, nuint stride);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_decodeFilterColor(void* buffer, nuint count, nuint stride);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_encodeFilterOct(void* destination, nuint count, nuint stride, int bits, float* data);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_encodeFilterQuat(void* destination, nuint count, nuint stride, int bits, float* data);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_encodeFilterExp(void* destination, nuint count, nuint stride, int bits, float* data,
            meshopt_EncodeExpMode mode);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_encodeFilterColor(void* destination, nuint count, nuint stride, int bits, float* data);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_simplify(uint* destination, uint* indices, nuint indexCount, float* vertexPositions, nuint vertexCount,
            nuint vertexPositionsStride, nuint targetIndexCount, float targetError, uint options, float* resultError = null);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_simplifyWithAttributes(uint* destination, uint* indices, nuint indexCount, float* vertexPositions,
            nuint vertexCount, nuint vertexPositionsStride, float* vertexAttributes, nuint vertexAttributesStride, float* attributeWeights,
            nuint attributeCount, byte* vertexLock, nuint targetIndexCount, float targetError, uint options, float* resultError = null);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_simplifyWithUpdate(uint* indices, nuint indexCount, float* vertexPositions, nuint vertexCount,
            nuint vertexPositionsStride, float* vertexAttributes, nuint vertexAttributesStride, float* attributeWeights, nuint attributeCount,
            byte* vertexLock, nuint targetIndexCount, float targetError, uint options, float* resultError = null);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_simplifySloppy(uint* destination, uint* indices, nuint indexCount, float* vertexPositions,
            nuint vertexCount, nuint vertexPositionsStride, byte* vertexLock, nuint targetIndexCount, float targetError, float* resultError = null);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_simplifyPrune(uint* destination, uint* indices, nuint indexCount, float* vertexPositions,
            nuint vertexCount, nuint vertexPositionsStride, float targetError);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_simplifyPoints(uint* destination, float* vertexPositions, nuint vertexCount,
            nuint vertexPositionsStride, float* vertexColors, nuint vertexColorsStride, float colorWeight, nuint targetVertexCount);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern float meshopt_simplifyScale(float* vertexPositions, nuint vertexCount, nuint vertexPositionsStride);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_stripify(uint* destination, uint* indices, nuint indexCount, nuint vertexCount, uint restartIndex);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_stripifyBound(nuint indexCount);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_unstripify(uint* destination, uint* indices, nuint indexCount, uint restartIndex);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_unstripifyBound(nuint indexCount);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern meshopt_VertexCacheStatistics meshopt_analyzeVertexCache(uint* indices, nuint indexCount, nuint vertexCount,
            uint cacheSize, uint warpSize, uint primitiveGroupSize);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern meshopt_VertexFetchStatistics meshopt_analyzeVertexFetch(uint* indices, nuint indexCount, nuint vertexCount,
            nuint vertexSize);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern meshopt_OverdrawStatistics meshopt_analyzeOverdraw(uint* indices, nuint indexCount, float* vertexPositions,
            nuint vertexCount, nuint vertexPositionsStride);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern meshopt_CoverageStatistics meshopt_analyzeCoverage(uint* indices, nuint indexCount, float* vertexPositions,
            nuint vertexCount, nuint vertexPositionsStride);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_buildMeshlets(meshopt_Meshlet* meshlets, uint* meshletVertices, byte* meshletTriangles, uint* indices,
            nuint indexCount, float* vertexPositions, nuint vertexCount, nuint vertexPositionsStride, nuint maxVertices, nuint maxTriangles,
            float coneWeight);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_buildMeshletsScan(meshopt_Meshlet* meshlets, uint* meshletVertices, byte* meshletTriangles,
            uint* indices, nuint indexCount, nuint vertexCount, nuint maxVertices, nuint maxTriangles);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_buildMeshletsBound(nuint indexCount, nuint maxVertices, nuint maxTriangles);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_buildMeshletsFlex(meshopt_Meshlet* meshlets, uint* meshletVertices, byte* meshletTriangles,
            uint* indices, nuint indexCount, float* vertexPositions, nuint vertexCount, nuint vertexPositionsStride, nuint maxVertices,
            nuint minTriangles, nuint maxTriangles, float coneWeight, float splitFactor);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_buildMeshletsSpatial(meshopt_Meshlet* meshlets, uint* meshletVertices, byte* meshletTriangles,
            uint* indices, nuint indexCount, float* vertexPositions, nuint vertexCount, nuint vertexPositionsStride, nuint maxVertices,
            nuint minTriangles, nuint maxTriangles, float fillWeight);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_optimizeMeshlet(uint* meshletVertices, byte* meshletTriangles, nuint triangleCount,
            nuint vertexCount);

        // Experimental in meshoptimizer v1.1; ABI may change in a later release.
        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_optimizeMeshletLevel(uint* meshletVertices, nuint vertexCount, byte* meshletTriangles,
            nuint triangleCount, int level);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern meshopt_Bounds meshopt_computeClusterBounds(uint* indices, nuint indexCount, float* vertexPositions,
            nuint vertexCount, nuint vertexPositionsStride);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern meshopt_Bounds meshopt_computeMeshletBounds(uint* meshletVertices, byte* meshletTriangles, nuint triangleCount,
            float* vertexPositions, nuint vertexCount, nuint vertexPositionsStride);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern meshopt_Bounds meshopt_computeSphereBounds(float* positions, nuint count, nuint positionsStride, float* radii,
            nuint radiiStride);

        // Experimental in meshoptimizer v1.1; ABI may change in a later release.
        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_extractMeshletIndices(uint* vertices, byte* triangles, uint* indices, nuint indexCount);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_partitionClusters(uint* destination, uint* clusterIndices, nuint totalIndexCount,
            uint* clusterIndexCounts, nuint clusterCount, float* vertexPositions, nuint vertexCount, nuint vertexPositionsStride,
            nuint targetPartitionSize);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_spatialSortRemap(uint* destination, float* vertexPositions, nuint vertexCount,
            nuint vertexPositionsStride);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_spatialSortTriangles(uint* destination, uint* indices, nuint indexCount, float* vertexPositions,
            nuint vertexCount, nuint vertexPositionsStride);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_spatialClusterPoints(uint* destination, float* vertexPositions, nuint vertexCount,
            nuint vertexPositionsStride, nuint clusterSize);

        // Experimental in meshoptimizer v1.1; ABI may change in a later release.
        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_opacityMapMeasure(byte* levels, uint* sources, int* opacityMapIndices, uint* indices,
            nuint indexCount, float* vertexUvs, nuint vertexCount, nuint vertexUvsStride, uint textureWidth, uint textureHeight,
            int maxLevel, float targetEdge);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_opacityMapRasterize(byte* result, int level, int states, float* uv0, float* uv1, float* uv2,
            byte* textureData, nuint textureStride, nuint texturePitch, uint textureWidth, uint textureHeight);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_opacityMapEntrySize(int level, int states);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern nuint meshopt_opacityMapCompact(byte* data, nuint dataSize, byte* levels, uint* offsets, nuint opacityMapCount,
            int* opacityMapIndices, nuint triangleCount, int states);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern ushort meshopt_quantizeHalf(float value);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern float meshopt_quantizeFloat(float value, int mantissaBits);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern float meshopt_dequantizeHalf(ushort value);

        [DllImport(DllName, CallingConvention = DllCallingConvention, ExactSpelling = true)]
        public static extern void meshopt_setAllocator(IntPtr allocate, IntPtr deallocate);
    }

    [StructLayout(LayoutKind.Sequential)]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public struct meshopt_Meshlet
    {
        public uint VertexOffset;
        public uint TriangleOffset;
        public uint VertexCount;
        public uint TriangleCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public unsafe struct meshopt_Bounds
    {
        public fixed float Center[3];
        public float Radius;

        public fixed float ConeApex[3];
        public fixed float ConeAxis[3];
        public float ConeCutoff;

        public fixed sbyte coneAxisS8[3];
        public sbyte ConeCutoffS8;
    }

    [StructLayout(LayoutKind.Sequential)]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public struct meshopt_VertexCacheStatistics
    {
        public uint VerticesTransformed;
        public uint WarpsExecuted;
        public float AverageCacheMissRatio;
        public float AverageTransformedVertexRatio;
    }

    [StructLayout(LayoutKind.Sequential)]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public struct meshopt_VertexFetchStatistics
    {
        public uint BytesFetched;
        public float Overfetch;
    }

    [StructLayout(LayoutKind.Sequential)]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public struct meshopt_OverdrawStatistics
    {
        public uint PixelsCovered;
        public uint PixelsShaded;
        public float Overdraw;
    }

    [StructLayout(LayoutKind.Sequential)]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public unsafe struct meshopt_CoverageStatistics
    {
        public fixed float Coverage[3];
        public float Extent;
    }

    [Flags]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public enum meshopt_SimplifyOptions : uint
    {
        None = 0,
        LockBorder = 1 << 0,
        Sparse = 1 << 1,
        ErrorAbsolute = 1 << 2,
        Prune = 1 << 3,
        Regularize = 1 << 4,
        Permissive = 1 << 5,
        RegularizeLight = 1 << 6,
    }

    [Flags]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public enum meshopt_SimplifyVertexFlags : byte
    {
        None = 0,
        Lock = 1 << 0,
        Protect = 1 << 1,
        Priority = 1 << 2,
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public enum meshopt_EncodeExpMode
    {
        Separate,
        SharedVector,
        SharedComponent,
        Clamped,
    }

    [StructLayout(LayoutKind.Sequential)]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public unsafe struct meshopt_Stream
    {
        public void* data;
        public nuint size;
        public nuint stride;
    }
}
