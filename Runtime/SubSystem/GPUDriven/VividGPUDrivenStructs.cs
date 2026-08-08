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
        Terrain = 1 << 1,
    }

    [GenerateHLSL(PackingRules.Exact)]
    [Flags]
    public enum VividSurfaceBindingFlags : uint
    {
        None = 0,
        BaseColor = 1 << 0,
        Normal = 1 << 1,
        Mask = 1 << 2,
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

        public uint SurfaceBindingIndex;
        public float NormalsStrength;
        public float Roughness;
        public float Metallic;

        public float SpecularAAScreenSpaceVariance;
        public float SpecularAAThreshold;
        public VividGeometryFlags GeometryFlags;
        public VividMaterialFlags MaterialFlags;

        public VividRendererListID RendererListID;
        public float AlphaClipThreshold;
        public uint Padding0;
        public uint Padding1;
    }

    [GenerateHLSL(PackingRules.Exact, needAccessors = false)]
    [StructLayout(LayoutKind.Sequential)]
    public struct VividSurfaceBindingData
    {
        public uint BaseColorResource;
        public uint NormalResource;
        public uint MaskResource;
        public VividSurfaceBindingFlags Flags;

        public float4 UVScaleBias;

        public const uint InvalidResource = uint.MaxValue;
    }

    [GenerateHLSL(PackingRules.Exact, needAccessors = false)]
    [StructLayout(LayoutKind.Sequential)]
    public struct VividTerrainMaterialData
    {
        public uint LayerStartIndex;
        public uint LayerCount;
        public uint ControlBindingIndex0;
        public uint ControlBindingIndex1;
    }

    [GenerateHLSL(PackingRules.Exact, needAccessors = false)]
    [StructLayout(LayoutKind.Sequential)]
    public struct VividTerrainLayerGPUData
    {
        public float4 TextureTilingOffset;

        public uint SurfaceBindingIndex;
        public float NormalsStrength;
        public float Roughness;
        public float Metallic;

        public uint MaskMode;
        public uint Padding0;
        public uint Padding1;
        public uint Padding2;
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
        public float PositionX;
        public float PositionY;
        public float PositionZ;
        public uint PackedNormal;
        public uint PackedTangent;
        public float2 UV;
        public uint Reserved;

        public float3 Position
        {
            readonly get => new(PositionX, PositionY, PositionZ);
            set
            {
                PositionX = value.x;
                PositionY = value.y;
                PositionZ = value.z;
            }
        }
    }

    public static class VividMeshletVertexPacking
    {
        public const uint OctahedralComponentMask = 0x7FFFu;
        public const uint NormalValidBit = 1u << 30;
        public const uint TangentNegativeHandednessBit = 1u << 30;
        public const uint TangentValidBit = 1u << 31;

        private const float DirectionLengthSquaredEpsilon = 1e-20f;
        private const float OctahedralComponentMaximum = OctahedralComponentMask;

        public static VividMeshletVertex Pack(
            float3 position,
            float3 normal,
            float4 tangent,
            float2 uv)
        {
            return new VividMeshletVertex
            {
                PositionX = position.x,
                PositionY = position.y,
                PositionZ = position.z,
                PackedNormal = PackNormal(normal),
                PackedTangent = PackTangent(tangent),
                UV = uv,
                Reserved = 0u,
            };
        }

        public static uint PackNormal(float3 normal)
        {
            return TryPackDirection(normal, out uint packedDirection)
                ? packedDirection | NormalValidBit
                : 0u;
        }

        public static uint PackTangent(float4 tangent)
        {
            if (!TryPackDirection(tangent.xyz, out uint packedDirection))
            {
                return 0u;
            }

            uint handedness = tangent.w < 0.0f ? TangentNegativeHandednessBit : 0u;
            return packedDirection | handedness | TangentValidBit;
        }

        public static float3 UnpackNormal(uint packedNormal)
        {
            return (packedNormal & NormalValidBit) != 0u
                ? UnpackDirection(packedNormal)
                : default;
        }

        public static float4 UnpackTangent(uint packedTangent)
        {
            if ((packedTangent & TangentValidBit) == 0u)
            {
                return default;
            }

            float3 direction = UnpackDirection(packedTangent);
            float handedness = (packedTangent & TangentNegativeHandednessBit) != 0u ? -1.0f : 1.0f;
            return new float4(direction, handedness);
        }

        private static bool TryPackDirection(float3 direction, out uint packedDirection)
        {
            packedDirection = 0u;
            if (!math.all(math.isfinite(direction)))
            {
                return false;
            }

            float lengthSquared = math.lengthsq(direction);
            if (!math.isfinite(lengthSquared) || lengthSquared <= DirectionLengthSquaredEpsilon)
            {
                return false;
            }

            direction *= math.rsqrt(lengthSquared);
            float reciprocalL1Norm = math.rcp(math.abs(direction.x) + math.abs(direction.y) + math.abs(direction.z));
            float2 octahedral = direction.xy * reciprocalL1Norm;
            if (direction.z < 0.0f)
            {
                octahedral = (1.0f - math.abs(octahedral.yx)) * SignNotZero(octahedral);
            }

            float2 encoded = math.saturate(octahedral * 0.5f + 0.5f);
            uint2 quantized = (uint2) math.round(encoded * OctahedralComponentMaximum);
            packedDirection = (quantized.x & OctahedralComponentMask)
                              | ((quantized.y & OctahedralComponentMask) << 15);
            return true;
        }

        private static float3 UnpackDirection(uint packedDirection)
        {
            float2 octahedral = new float2(
                packedDirection & OctahedralComponentMask,
                (packedDirection >> 15) & OctahedralComponentMask
            );
            octahedral = octahedral / OctahedralComponentMaximum * 2.0f - 1.0f;

            var direction = new float3(
                octahedral,
                1.0f - math.abs(octahedral.x) - math.abs(octahedral.y)
            );
            float fold = math.saturate(-direction.z);
            direction.xy += math.select(new float2(fold), new float2(-fold), direction.xy >= 0.0f);
            return math.normalizesafe(direction);
        }

        private static float2 SignNotZero(float2 value)
        {
            return math.select(new float2(-1.0f), new float2(1.0f), value >= 0.0f);
        }
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
    public struct VividIndirectDrawArgs
    {
        public uint VertexCountPerInstance;
        public uint InstanceCount;
        public uint StartVertex;
        public uint StartInstance;
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
