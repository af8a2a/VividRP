using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven
{
    [GenerateHLSL(PackingRules.Exact)]
    [Flags]
    public enum VividInstancePassMask
    {
        None = 0,
        Main = 1 << 0,
        Shadows = 1 << 1,
    }

    [GenerateHLSL(PackingRules.Exact)]
    [Flags]
    public enum VividInstanceFlags
    {
        None = 0,
        Disabled = 1 << 0,
        FlipWindingOrder = 1 << 1,
    }

    [GenerateHLSL(PackingRules.Exact, needAccessors = false)]
    [StructLayout(LayoutKind.Sequential)]
    public struct VividInstanceData
    {
        public float4x4 ObjectToWorldMatrix;
        public float4x4 WorldToObjectMatrix;
        public float4 AABBMin;
        public float4 AABBMax;

        public uint TopMeshLODStartIndex;
        public uint TotalMeshLODCount;
        public uint MaterialIndex;
        public uint MeshLODLevelCount;

        public float LODErrorScale;
        public VividInstancePassMask PassMask;
        public VividInstanceFlags Flags;
        public uint Padding0;
    }

    [GenerateHLSL(PackingRules.Exact)]
    [Flags]
    public enum VividGeometryFlags
    {
        None = 0,
        SpecularAA = 1 << 0,
    }

    [GenerateHLSL(PackingRules.Exact)]
    [Flags]
    public enum VividMaterialFlags
    {
        None = 0,
        Unlit = 1 << 0,
    }

    [GenerateHLSL(PackingRules.Exact)]
    [Flags]
    public enum VividRendererListID
    {
        Default = 0,
        CullFront = 1 << 0,
        CullOff = 1 << 1,
        AlphaTest = 1 << 2,
        Count = (CullFront | CullOff | AlphaTest) + 1,
    }

    [GenerateHLSL(PackingRules.Exact, needAccessors = false)]
    [StructLayout(LayoutKind.Sequential)]
    public struct VividMaterialData
    {
        public float4 AlbedoColor;
        public float4 TextureTilingOffset;
        public float4 Emission;

        public uint AlbedoIndex;
        public uint NormalsIndex;
        public float NormalsStrength;
        public uint MasksIndex;

        public float Roughness;
        public float Metallic;
        public float SpecularAAScreenSpaceVariance;
        public float SpecularAAThreshold;

        public VividGeometryFlags GeometryFlags;
        public VividMaterialFlags MaterialFlags;
        public VividRendererListID RendererListID;
        public float AlphaClipThreshold;

        public const uint NoTextureIndex = uint.MaxValue;
    }

    [GenerateHLSL]
    public static class VividMeshletConfiguration
    {
        public const uint MaxMeshletVertices = 128;
        public const uint MaxMeshletTriangles = 128;
        public const uint MaxMeshletIndices = MaxMeshletTriangles * 3;
        public const float MeshletConeWeight = 0.25f;
    }

    [GenerateHLSL(PackingRules.Exact, needAccessors = false)]
    [StructLayout(LayoutKind.Sequential)]
    [Serializable]
    public struct VividMeshlet
    {
        public uint VertexOffset;
        public uint TriangleOffset;
        public uint VertexCount;
        public uint TriangleCount;

        public float4 BoundingSphere;
        public float4 ConeApexCutoff;
        public float4 ConeAxis;
    }

    [GenerateHLSL(PackingRules.Exact, needAccessors = false)]
    [StructLayout(LayoutKind.Sequential)]
    [Serializable]
    public struct VividMeshLODNode
    {
        public float4 Bounds;
        public float4 ParentBounds;

        public float ParentError;
        public float Error;
        public uint MeshletStartIndex;
        public uint MeshletCount;

        public uint LevelIndex;
        public uint Padding0;
        public uint Padding1;
        public uint Padding2;
    }

    [GenerateHLSL(PackingRules.Exact, needAccessors = false)]
    [StructLayout(LayoutKind.Sequential)]
    [Serializable]
    public struct VividMeshletVertex
    {
        public float4 Position;
        public float4 Normal;
        public float4 Tangent;
        public float4 UV;
    }

    [GenerateHLSL(PackingRules.Exact, needAccessors = false)]
    [StructLayout(LayoutKind.Sequential)]
    public struct VividMeshletRenderRequestPacked
    {
        public uint InstanceID_LOD;
        public uint MeshletID;
    }

    [GenerateHLSL(PackingRules.Exact, needAccessors = false)]
    [StructLayout(LayoutKind.Sequential)]
    public struct IndirectDispatchArgs
    {
        public uint ThreadGroupsX;
        public uint ThreadGroupsY;
        public uint ThreadGroupsZ;
    }

    [GenerateHLSL(PackingRules.Exact, needAccessors = false)]
    [StructLayout(LayoutKind.Auto)]
    public unsafe struct VividGPUCullingContext
    {
        public float4x4 ViewProjectionMatrix;
        public float4x4 ViewMatrix;
        public float4 CameraPosition;

        [HLSLArray(6, typeof(Vector4))]
        public fixed float FrustumPlanes[6 * 4];

        public float4 CullingSphereLS;

        public int PassMask;
        public int CameraIsPerspective;
        public uint BaseStartInstance;
        public uint MeshletListBuildJobsOffset;
        public uint MeshletRenderRequestsOffset;

        public uint Padding0;
        public uint Padding1;
        public uint Padding2;
    }

    [GenerateHLSL(PackingRules.Exact, needAccessors = false)]
    [StructLayout(LayoutKind.Auto)]
    public struct VividGPULODSelectionContext
    {
        public float4x4 ViewProjectionMatrix;
        public float4 CameraPosition;
        public float4 CameraUp;
        public float4 CameraRight;
        public float2 ScreenSizePixels;

        public uint Padding0;
        public uint Padding1;
    }
}
