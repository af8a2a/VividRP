using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace VividRP.Runtime
{
    public static unsafe class MeshOptimizerBindings
    {
        private const string DllName = "meshoptimizer";
        private const CharSet DllCharSet = CharSet.Auto;
        private const CallingConvention DllCallingConvention = CallingConvention.Cdecl;

        [DllImport(DllName, CharSet = DllCharSet, CallingConvention = DllCallingConvention)]
        public static extern nuint meshopt_buildMeshlets(meshopt_Meshlet* meshlets, uint* meshletVertices, byte* meshletTriangles, uint* indices,
            nuint indexCount, float* vertexPositions, nuint vertexCount, nuint vertexPositionsStride, nuint maxVertices, nuint maxTriangles, float coneWeight);

        [DllImport(DllName, CharSet = DllCharSet, CallingConvention = DllCallingConvention)]
        public static extern nuint meshopt_buildMeshletsBound(nuint indexCount, nuint maxVertices, nuint maxTriangles);

        [DllImport(DllName, CharSet = DllCharSet, CallingConvention = DllCallingConvention)]
        public static extern meshopt_Bounds meshopt_computeMeshletBounds(uint* meshletVertices, byte* meshletTriangles, nuint triangleCount,
            float* vertexPositions, nuint vertexCount, nuint vertexPositionsStride);

        [DllImport(DllName, CharSet = DllCharSet, CallingConvention = DllCallingConvention)]
        public static extern nuint meshopt_simplify(uint* destination, uint* indices, nuint indexCount, float* vertexPositions, nuint vertexCount,
            nuint vertexPositionsStride, nuint targetIndexCount, float targetError, uint options, float* resultError = null);

        [DllImport(DllName, CharSet = DllCharSet, CallingConvention = DllCallingConvention)]
        public static extern nuint meshopt_simplifySloppy(uint* destination, uint* indices, nuint indexCount, float* vertexPositions, nuint vertexCount,
            nuint vertexPositionsStride, nuint targetIndexCount, float targetError, float* resultError = null);

        [DllImport(DllName, CharSet = DllCharSet, CallingConvention = DllCallingConvention)]
        public static extern void meshopt_optimizeVertexCache(uint* destination, uint* indices, nuint indexCount, nuint vertexCount);

        [DllImport(DllName, CharSet = DllCharSet, CallingConvention = DllCallingConvention)]
        public static extern nuint meshopt_generateVertexRemapMulti(uint* destination, uint* indices, nuint indexCount, nuint vertexCount,
            meshopt_Stream* streams, nuint streamCount);

        [DllImport(DllName, CharSet = DllCharSet, CallingConvention = DllCallingConvention)]
        public static extern void meshopt_remapVertexBuffer(void* destination, void* vertices, nuint vertexCount, nuint vertexSize, uint* remap);

        [DllImport(DllName, CharSet = DllCharSet, CallingConvention = DllCallingConvention)]
        public static extern void meshopt_remapIndexBuffer(uint* destination, uint* indices, nuint indexCount, uint* remap);

        [DllImport(DllName, CharSet = DllCharSet, CallingConvention = DllCallingConvention)]
        public static extern void meshopt_spatialSortTriangles(uint* destination, uint* indices, nuint indexCount, float* vertexPositions, nuint vertexCount,
            nuint vertexPositionsStride);

        [DllImport(DllName, CharSet = DllCharSet, CallingConvention = DllCallingConvention)]
        public static extern void meshopt_spatialSortRemap(uint* destination, float* vertexPositions, nuint vertexCount, nuint vertexPositionsStride);
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

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [Flags]
    public enum meshopt_SimplifyOptions : uint
    {
        None = 0,
        LockBorder = 1 << 0,
        Sparse = 1 << 1,
        ErrorAbsolute = 1 << 2,
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public unsafe struct meshopt_Stream
    {
        public void* data;
        public nuint size;
        public nuint stride;
    }
}
