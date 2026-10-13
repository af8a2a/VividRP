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
        TwoSidedShadows = 1 << 2,
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
        TerrainRuntimeVirtualTexture = 1 << 2,
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

    [GenerateHLSL(PackingRules.Exact)]
    public enum VividMaterialProgramID : uint
    {
        StandardSingleSlab = 0,
        DualSlabHorizontalMix = 1,
        DualSlabVerticalLayer = 2,
        Invalid = uint.MaxValue,
    }

    [GenerateHLSL(PackingRules.Exact)]
    public enum VividMaterialCoverageProgramID : uint
    {
        BaseColorAlpha = 0,
    }

    [GenerateHLSL(PackingRules.Exact)]
    public enum VividMaterialSurfaceProgramID : uint
    {
        StandardSingleSlab = 0,
        DualSlab = 1,
        OpenPBROpaque = 2,
    }

    [GenerateHLSL(PackingRules.Exact)]
    public enum VividMaterialTransportProgramID : uint
    {
        None = 0,
    }

    [GenerateHLSL(PackingRules.Exact)]
    public enum VividMaterialParameterLayoutID : uint
    {
        LegacyMaterialData = 0,
        DualSlabMaterialData = 1,
        GenericParameterLanes = 2,
    }

    [GenerateHLSL(PackingRules.Exact)]
    public enum VividMaterialResourceLayoutID : uint
    {
        LegacySurfaceBinding = 0,
        DualSurfaceBinding = 1,
        GenericResourceRecords = 2,
    }

    [GenerateHLSL(PackingRules.Exact)]
    public enum VividDualSlabOperator : uint
    {
        HorizontalMix = 0,
        VerticalLayer = 1,
    }

    [GenerateHLSL(PackingRules.Exact)]
    public enum VividMaterialExecutionClass : uint
    {
        VisibilityDeferred = 0,
    }

    [GenerateHLSL(PackingRules.Exact)]
    [Flags]
    public enum VividMaterialRuntimeFlags : uint
    {
        None = 0,
        AlphaClip = 1 << 0,
        Unlit = 1 << 1,
    }

    [GenerateHLSL(PackingRules.Exact)]
    [Flags]
    public enum VividMaterialProgramCapabilities : uint
    {
        None = 0,
        LegacyGBufferExport = 1 << 0,
        AlphaClip = 1 << 1,
        Unlit = 1 << 2,
        OpenPBROpaqueExport = 1 << 3,
    }

    [GenerateHLSL]
    public static class VividMaterialConfiguration
    {
        public const uint VividMaterialProgramVersion =
            MaterialProgramContract.RuntimeAbiVersion;
    }

    [GenerateHLSL(PackingRules.Exact, needAccessors = false)]
    [StructLayout(LayoutKind.Sequential)]
    public struct VividMaterialRuntimeHeader
    {
        public VividMaterialProgramID ProgramID;
        public uint ParameterAddress;
        public uint ResourceBindingAddress;
        public VividMaterialRuntimeFlags Flags;
    }

    [GenerateHLSL(PackingRules.Exact, needAccessors = false)]
    [StructLayout(LayoutKind.Sequential)]
    public struct VividMaterialProgramData
    {
        public uint Version;
        public VividMaterialCoverageProgramID CoverageProgramID;
        public VividMaterialSurfaceProgramID SurfaceProgramID;
        public VividMaterialTransportProgramID TransportProgramID;

        public VividMaterialParameterLayoutID ParameterLayoutID;
        public VividMaterialResourceLayoutID ResourceLayoutID;
        public VividMaterialProgramCapabilities CapabilityFlags;
        public VividMaterialExecutionClass ExecutionClass;
    }

    [GenerateHLSL(PackingRules.Exact, needAccessors = false)]
    [StructLayout(LayoutKind.Sequential)]
    public struct VividMaterialData
    {
        public float4 AlbedoColor;
        public float4 TextureTilingOffset;
        public float4 Emission;
        public float4 MetallicSmoothnessRemap;
        public float4 AmbientOcclusionRemap;

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
    public struct VividSlabMaterialData
    {
        public float4 AlbedoColor;
        public float4 TextureTilingOffset;
        public float4 MetallicSmoothnessRemap;
        public float4 AmbientOcclusionRemap;

        public float NormalsStrength;
        public float Roughness;
        public float Metallic;
        public uint MaskMode;
    }

    [GenerateHLSL(PackingRules.Exact, needAccessors = false)]
    [StructLayout(LayoutKind.Sequential)]
    public struct VividDualSlabMaterialData
    {
        public float4 BaseAlbedoColor;
        public float4 BaseTextureTilingOffset;
        public float4 BaseMetallicSmoothnessRemap;
        public float4 BaseAmbientOcclusionRemap;

        public float BaseNormalsStrength;
        public float BaseRoughness;
        public float BaseMetallic;
        public uint BaseMaskMode;

        public float4 TopAlbedoColor;
        public float4 TopTextureTilingOffset;
        public float4 TopMetallicSmoothnessRemap;
        public float4 TopAmbientOcclusionRemap;

        public float TopNormalsStrength;
        public float TopRoughness;
        public float TopMetallic;
        public uint TopMaskMode;

        public float4 Emission;

        public VividDualSlabOperator LayerOperator;
        public float LayerWeight;
        public float AlphaClipThreshold;
        public uint Padding0;
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
    public struct VividMaterialResourceData
    {
        public uint BaseColorResource;
        public uint NormalResource;
        public uint MaskResource;
        public VividSurfaceBindingFlags SurfaceBindingFlags;
        public float4 UVScaleBias;

        public float4 TextureTilingOffset;
        public float4 MetallicSmoothnessRemap;
        public float4 AmbientOcclusionRemap;

        public float NormalsStrength;
        public uint MaskMode;
        public uint Padding0;
        public uint Padding1;

        public VividSurfaceBindingData SurfaceBinding
        {
            readonly get => new()
            {
                BaseColorResource = BaseColorResource,
                NormalResource = NormalResource,
                MaskResource = MaskResource,
                Flags = SurfaceBindingFlags,
                UVScaleBias = UVScaleBias,
            };
            set
            {
                BaseColorResource = value.BaseColorResource;
                NormalResource = value.NormalResource;
                MaskResource = value.MaskResource;
                SurfaceBindingFlags = value.Flags;
                UVScaleBias = value.UVScaleBias;
            }
        }
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
        public uint PackedVertexTriangleCounts;
        public uint PackedCone;
        public float4 BoundingSphere;

        public uint VertexCount
        {
            readonly get => PackedVertexTriangleCounts & VividMeshletMetadataPacking.UInt16Mask;
            set => PackedVertexTriangleCounts = VividMeshletMetadataPacking.SetLowUInt16(
                PackedVertexTriangleCounts,
                value,
                nameof(VertexCount));
        }

        public uint TriangleCount
        {
            readonly get => PackedVertexTriangleCounts >> 16;
            set => PackedVertexTriangleCounts = VividMeshletMetadataPacking.SetHighUInt16(
                PackedVertexTriangleCounts,
                value,
                nameof(TriangleCount));
        }

        public float4 ConeApexCutoff
        {
            // The packed layout uses conservative sphere-cone culling and no longer stores
            // meshoptimizer's apex. Expose the sphere center to keep the legacy CPU API usable.
            readonly get => new(BoundingSphere.xyz, VividMeshletMetadataPacking.UnpackConeCutoff(PackedCone));
            set => PackedCone = VividMeshletMetadataPacking.SetConeCutoff(PackedCone, value.w);
        }

        public float4 ConeAxis
        {
            readonly get => new(VividMeshletMetadataPacking.UnpackConeAxis(PackedCone), 0.0f);
            set => PackedCone = VividMeshletMetadataPacking.SetConeAxis(PackedCone, value.xyz);
        }
    }

    [GenerateHLSL(PackingRules.Exact, needAccessors = false)]
    [StructLayout(LayoutKind.Sequential)]
    [Serializable]
    public struct VividMeshLODNode
    {
        public float4 Bounds;
        public float Error;
        public uint PackedParentErrorRadius;
        public uint MeshletStartIndex;
        public uint PackedMeshletCountLevel;

        public float4 ParentBounds
        {
            readonly get => ParentError < 0.0f
                ? default
                : new float4(Bounds.xyz, VividMeshletMetadataPacking.UnpackParentRadius(PackedParentErrorRadius));
            set => PackedParentErrorRadius = VividMeshletMetadataPacking.SetParentBounds(
                PackedParentErrorRadius,
                Bounds,
                value);
        }

        public float ParentError
        {
            readonly get => VividMeshletMetadataPacking.UnpackParentError(PackedParentErrorRadius);
            set => PackedParentErrorRadius = VividMeshletMetadataPacking.SetParentError(
                PackedParentErrorRadius,
                value);
        }

        public uint MeshletCount
        {
            readonly get => PackedMeshletCountLevel & VividMeshletMetadataPacking.UInt16Mask;
            set => PackedMeshletCountLevel = VividMeshletMetadataPacking.SetLowUInt16(
                PackedMeshletCountLevel,
                value,
                nameof(MeshletCount));
        }

        public uint LevelIndex
        {
            readonly get => PackedMeshletCountLevel >> 16;
            set => PackedMeshletCountLevel = VividMeshletMetadataPacking.SetHighUInt16(
                PackedMeshletCountLevel,
                value,
                nameof(LevelIndex));
        }
    }

    public static class VividMeshletMetadataPacking
    {
        public const uint UInt16Mask = 0xFFFFu;
        public const uint ConeOctahedralComponentMask = 0x3FFu;
        public const uint ConeCutoffMask = 0x7FFu;
        public const uint ConeValidBit = 1u << 31;

        private const int ConeOctahedralComponentBits = 10;
        private const int ConeCutoffShift = 20;
        private const float DirectionLengthSquaredEpsilon = 1e-20f;
        private const float ConservativeConeAxisSetterErrorRadians = 0.00872664626f;
        // Parent error and radius use the high 16 bits of a float (bfloat16 style). Positive
        // values are rounded upward so LOD selection remains conservative across the full float range.
        private const uint MaximumFiniteFloat16 = 0x7F7Fu;

        public static VividMeshlet PackMeshlet(
            uint vertexOffset,
            uint triangleOffset,
            uint vertexCount,
            uint triangleCount,
            float4 boundingSphere,
            float3 coneAxis,
            float coneCutoff)
        {
            return new VividMeshlet
            {
                VertexOffset = vertexOffset,
                TriangleOffset = triangleOffset,
                PackedVertexTriangleCounts = PackUInt16Pair(
                    vertexCount,
                    triangleCount,
                    nameof(vertexCount),
                    nameof(triangleCount)),
                PackedCone = PackCone(coneAxis, coneCutoff),
                BoundingSphere = boundingSphere,
            };
        }

        public static VividMeshLODNode PackMeshLODNode(
            float4 bounds,
            float4 parentBounds,
            float parentError,
            float error,
            uint meshletStartIndex,
            uint meshletCount,
            uint levelIndex)
        {
            return new VividMeshLODNode
            {
                Bounds = bounds,
                Error = error,
                PackedParentErrorRadius = PackParentErrorRadius(bounds, parentBounds, parentError),
                MeshletStartIndex = meshletStartIndex,
                PackedMeshletCountLevel = PackUInt16Pair(
                    meshletCount,
                    levelIndex,
                    nameof(meshletCount),
                    nameof(levelIndex)),
            };
        }

        public static uint PackCone(float3 coneAxis, float coneCutoff)
        {
            if (!math.isfinite(coneCutoff)
                || !TryNormalizeDirection(coneAxis, out float3 normalizedAxis))
            {
                return 0u;
            }

            uint packedAxis = PackOctahedral10(normalizedAxis);
            float3 decodedAxis = UnpackOctahedral10(packedAxis);
            float expandedCutoff = ExpandConeCutoffForAxisQuantization(
                math.clamp(coneCutoff, -1.0f, 1.0f),
                normalizedAxis,
                decodedAxis);
            uint packedCutoff = PackSignedUNorm11Up(expandedCutoff);
            return packedAxis | (packedCutoff << ConeCutoffShift) | ConeValidBit;
        }

        public static uint SetConeAxis(uint packedCone, float3 coneAxis)
        {
            uint packedCutoffBits = packedCone & (ConeCutoffMask << ConeCutoffShift);
            float cutoff = IsConeValid(packedCone) || packedCutoffBits != 0u
                ? UnpackConeCutoff(packedCone)
                : 1.0f;
            if (!TryNormalizeDirection(coneAxis, out float3 normalizedAxis))
            {
                return packedCone & (ConeCutoffMask << ConeCutoffShift);
            }

            uint packedAxis = PackOctahedral10(normalizedAxis);
            float3 decodedAxis = UnpackOctahedral10(packedAxis);
            float expandedCutoff = ExpandConeCutoffForAxisQuantization(
                cutoff,
                normalizedAxis,
                decodedAxis);
            uint packedCutoff = PackSignedUNorm11Up(expandedCutoff);
            return packedAxis | (packedCutoff << ConeCutoffShift) | ConeValidBit;
        }

        public static uint SetConeCutoff(uint packedCone, float coneCutoff)
        {
            uint packedCutoff = math.isfinite(coneCutoff)
                ? PackSignedUNorm11Up(ExpandConeCutoffByAngle(
                    math.clamp(coneCutoff, -1.0f, 1.0f),
                    ConservativeConeAxisSetterErrorRadians))
                : ConeCutoffMask;
            return (packedCone & ~(ConeCutoffMask << ConeCutoffShift))
                   | (packedCutoff << ConeCutoffShift);
        }

        public static bool IsConeValid(uint packedCone)
        {
            return (packedCone & ConeValidBit) != 0u;
        }

        public static float3 UnpackConeAxis(uint packedCone)
        {
            return IsConeValid(packedCone) ? UnpackOctahedral10(packedCone) : default;
        }

        public static float UnpackConeCutoff(uint packedCone)
        {
            uint quantized = (packedCone >> ConeCutoffShift) & ConeCutoffMask;
            return quantized / (float) ConeCutoffMask * 2.0f - 1.0f;
        }

        public static float UnpackParentError(uint packedParentErrorRadius)
        {
            return Float16ToFloat(packedParentErrorRadius & UInt16Mask);
        }

        public static float UnpackParentRadius(uint packedParentErrorRadius)
        {
            return Float16ToFloat(packedParentErrorRadius >> 16);
        }

        public static uint SetParentError(uint packedParentErrorRadius, float parentError)
        {
            uint packedError = parentError < 0.0f
                ? FloatToFloat16(-1.0f)
                : PackPositiveFloat16Up(parentError);
            return (packedParentErrorRadius & 0xFFFF0000u) | packedError;
        }

        public static uint SetParentBounds(
            uint packedParentErrorRadius,
            float4 bounds,
            float4 parentBounds)
        {
            float parentRadius = ComputeConservativeParentRadius(bounds, parentBounds);
            return (packedParentErrorRadius & UInt16Mask) | (PackPositiveFloat16Up(parentRadius) << 16);
        }

        public static uint SetLowUInt16(uint packedValue, uint value, string parameterName)
        {
            ValidateUInt16(value, parameterName);
            return (packedValue & 0xFFFF0000u) | value;
        }

        public static uint SetHighUInt16(uint packedValue, uint value, string parameterName)
        {
            ValidateUInt16(value, parameterName);
            return (packedValue & UInt16Mask) | (value << 16);
        }

        private static uint PackParentErrorRadius(float4 bounds, float4 parentBounds, float parentError)
        {
            uint packedError = parentError < 0.0f
                ? FloatToFloat16(-1.0f)
                : PackPositiveFloat16Up(parentError);
            uint packedRadius = parentError < 0.0f
                ? 0u
                : PackPositiveFloat16Up(ComputeConservativeParentRadius(bounds, parentBounds));
            return packedError | (packedRadius << 16);
        }

        private static float ComputeConservativeParentRadius(float4 bounds, float4 parentBounds)
        {
            if (!math.all(math.isfinite(bounds)) || !math.all(math.isfinite(parentBounds)))
            {
                return Float16ToFloat(MaximumFiniteFloat16);
            }

            return math.max(0.0f, parentBounds.w) + math.distance(bounds.xyz, parentBounds.xyz);
        }

        private static uint PackUInt16Pair(
            uint lowValue,
            uint highValue,
            string lowParameterName,
            string highParameterName)
        {
            ValidateUInt16(lowValue, lowParameterName);
            ValidateUInt16(highValue, highParameterName);
            return lowValue | (highValue << 16);
        }

        private static void ValidateUInt16(uint value, string parameterName)
        {
            if (value > UInt16Mask)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    $"Packed meshlet metadata values must not exceed {UInt16Mask}.");
            }
        }

        private static bool TryNormalizeDirection(float3 direction, out float3 normalizedDirection)
        {
            normalizedDirection = default;
            if (!math.all(math.isfinite(direction)))
            {
                return false;
            }

            float lengthSquared = math.lengthsq(direction);
            if (!math.isfinite(lengthSquared) || lengthSquared <= DirectionLengthSquaredEpsilon)
            {
                return false;
            }

            normalizedDirection = direction * math.rsqrt(lengthSquared);
            return true;
        }

        private static uint PackOctahedral10(float3 direction)
        {
            float reciprocalL1Norm = math.rcp(math.csum(math.abs(direction)));
            float2 octahedral = direction.xy * reciprocalL1Norm;
            if (direction.z < 0.0f)
            {
                octahedral = (1.0f - math.abs(octahedral.yx)) * SignNotZero(octahedral);
            }

            uint2 quantized = (uint2) math.round(
                math.saturate(octahedral * 0.5f + 0.5f) * ConeOctahedralComponentMask);
            return (quantized.x & ConeOctahedralComponentMask)
                   | ((quantized.y & ConeOctahedralComponentMask) << ConeOctahedralComponentBits);
        }

        private static float3 UnpackOctahedral10(uint packedDirection)
        {
            float2 octahedral = new float2(
                packedDirection & ConeOctahedralComponentMask,
                (packedDirection >> ConeOctahedralComponentBits) & ConeOctahedralComponentMask);
            octahedral = octahedral / ConeOctahedralComponentMask * 2.0f - 1.0f;

            var direction = new float3(
                octahedral,
                1.0f - math.abs(octahedral.x) - math.abs(octahedral.y));
            float fold = math.saturate(-direction.z);
            direction.xy += math.select(new float2(fold), new float2(-fold), direction.xy >= 0.0f);
            return math.normalizesafe(direction);
        }

        private static float ExpandConeCutoffForAxisQuantization(
            float coneCutoff,
            float3 sourceAxis,
            float3 decodedAxis)
        {
            float cosDelta = math.clamp(math.dot(sourceAxis, decodedAxis), -1.0f, 1.0f);
            if (cosDelta <= coneCutoff)
            {
                return 1.0f;
            }

            float sinDelta = math.sqrt(math.max(0.0f, 1.0f - cosDelta * cosDelta));
            float sinCone = math.sqrt(math.max(0.0f, 1.0f - coneCutoff * coneCutoff));
            return math.min(1.0f, coneCutoff * cosDelta + sinCone * sinDelta);
        }

        private static float ExpandConeCutoffByAngle(float coneCutoff, float angleRadians)
        {
            float cosDelta = math.cos(angleRadians);
            if (cosDelta <= coneCutoff)
            {
                return 1.0f;
            }

            float sinCone = math.sqrt(math.max(0.0f, 1.0f - coneCutoff * coneCutoff));
            return math.min(
                1.0f,
                coneCutoff * cosDelta + sinCone * math.sin(angleRadians));
        }

        private static uint PackSignedUNorm11Up(float value)
        {
            float encoded = math.saturate(value * 0.5f + 0.5f) * ConeCutoffMask;
            return math.min((uint) math.ceil(encoded), ConeCutoffMask);
        }

        private static uint PackPositiveFloat16Up(float value)
        {
            if (float.IsNaN(value) || value <= 0.0f)
            {
                return 0u;
            }

            if (float.IsPositiveInfinity(value))
            {
                return MaximumFiniteFloat16;
            }

            uint bits = math.asuint(value);
            uint packed = bits >> 16;
            if ((bits & UInt16Mask) != 0u && packed < MaximumFiniteFloat16)
            {
                packed++;
            }

            return math.min(packed, MaximumFiniteFloat16);
        }

        private static uint FloatToFloat16(float value)
        {
            return math.asuint(value) >> 16;
        }

        private static float Float16ToFloat(uint value)
        {
            return math.asfloat((value & UInt16Mask) << 16);
        }

        private static float2 SignNotZero(float2 value)
        {
            return math.select(new float2(-1.0f), new float2(1.0f), value >= 0.0f);
        }
    }

    [GenerateHLSL(PackingRules.Exact, needAccessors = false)]
    [StructLayout(LayoutKind.Sequential)]
    [Serializable]
    public struct VividMeshletVertex
    {
        // Five 25-bit floats (sign/exponent/16-bit mantissa), two validity flags,
        // and a 22-bit octahedral normal + 10-bit tangent angle/handedness.
        public uint PositionXY;
        public uint PositionYZ;
        public uint PositionZUV;
        public uint UVAndFlags;
        public uint TangentFrame;

        public float3 Position
        {
            readonly get => new(
                VividMeshletVertexPacking.DecodeFloat(PositionXY),
                VividMeshletVertexPacking.DecodeFloat((PositionXY >> 25) | (PositionYZ << 7)),
                VividMeshletVertexPacking.DecodeFloat((PositionYZ >> 18) | (PositionZUV << 14)));
            set
            {
                uint x = VividMeshletVertexPacking.EncodeFloat(value.x);
                uint y = VividMeshletVertexPacking.EncodeFloat(value.y);
                uint z = VividMeshletVertexPacking.EncodeFloat(value.z);
                PositionXY = x | (y << 25);
                PositionYZ = (y >> 7) | (z << 18);
                PositionZUV = (PositionZUV & 0xFFFFF800u) | (z >> 14);
            }
        }

        public float2 UV
        {
            readonly get => new(
                VividMeshletVertexPacking.DecodeFloat((PositionZUV >> 11) | (UVAndFlags << 21)),
                VividMeshletVertexPacking.DecodeFloat(UVAndFlags >> 4));
            set
            {
                uint u = VividMeshletVertexPacking.EncodeFloat(value.x);
                uint v = VividMeshletVertexPacking.EncodeFloat(value.y);
                PositionZUV = (PositionZUV & 0x7FFu) | (u << 11);
                UVAndFlags = (UVAndFlags & 0xE0000000u) | (u >> 21) | (v << 4);
            }
        }

        // Compatibility accessors for CPU consumers of the former 32-byte vertex.
        public float PositionX { readonly get => Position.x; set { float3 p = Position; p.x = value; Position = p; } }
        public float PositionY { readonly get => Position.y; set { float3 p = Position; p.y = value; Position = p; } }
        public float PositionZ { readonly get => Position.z; set { float3 p = Position; p.z = value; Position = p; } }
        public readonly uint PackedNormal => VividMeshletVertexPacking.PackNormal(VividMeshletVertexPacking.UnpackNormal(this));
        public readonly uint PackedTangent => VividMeshletVertexPacking.PackTangent(VividMeshletVertexPacking.UnpackTangent(this));
        public readonly uint Reserved => 0u;
    }

    public static class VividMeshletVertexPacking
    {
        // Legacy oct15 helpers remain available for v1-v4 conversion and CPU compatibility.
        public const uint OctahedralComponentMask = 0x7FFFu;
        public const uint NormalValidBit = 1u << 30;
        public const uint TangentNegativeHandednessBit = 1u << 30;
        public const uint TangentValidBit = 1u << 31;
        public const uint QuantizedNormalValidBit = 1u << 29;
        public const uint QuantizedTangentValidBit = 1u << 30;
        public const uint FloatMask = 0x1FFFFFFu;
        public const int DroppedMantissaBits = 7;

        private const float DirectionLengthSquaredEpsilon = 1e-20f;
        private const float OctahedralComponentMaximum = OctahedralComponentMask;

        public static uint EncodeFloat(float value)
        {
            uint bits = math.asuint(value);
            uint magnitude = bits & 0x7FFFFFFFu;
            if (magnitude < 0x00800000u) return 0u; // reference flushes denormals and signed zero
            if (magnitude > 0x7F800000u) return 0xFF8001u; // canonical NaN survives truncation
            if (magnitude == 0x7F800000u) return bits >> DroppedMantissaBits;
            // Round to 16 mantissa bits; keep finite extremes finite.
            uint rounded = math.min((magnitude + 64u) & 0xFFFFFF80u, 0x7F7FFF80u);
            return (rounded | (bits & 0x80000000u)) >> DroppedMantissaBits;
        }

        public static float DecodeFloat(uint bits) => math.asfloat((bits & FloatMask) << DroppedMantissaBits);
        public static float3 QuantizePosition(float3 value) => new(
            DecodeFloat(EncodeFloat(value.x)), DecodeFloat(EncodeFloat(value.y)), DecodeFloat(EncodeFloat(value.z)));

        public static Bounds ExpandBoundsForQuantization(Bounds bounds)
        {
            float3 lo = bounds.min;
            float3 hi = bounds.max;
            float3 center = bounds.center;
            float3 extents = math.max((float3) bounds.extents,
                math.max(math.abs(QuantizePosition(lo) - center), math.abs(QuantizePosition(hi) - center)));
            // Round outward by one ULP: center/extent reconstruction must not shrink
            // a rounded endpoint back inside the quantized geometry.
            bounds.extents = (Vector3) math.asfloat(math.asuint(extents) + new uint3(1u));
            return bounds;
        }

        public static VividMeshletVertex Pack(float3 position, float3 normal, float4 tangent, float2 uv)
        {
            var vertex = new VividMeshletVertex { Position = position, UV = uv };
            bool normalValid = TryPackDirection(normal, out _);
            bool tangentValid = TryPackDirection(tangent.xyz, out _);
            if (normalValid)
            {
                vertex.UVAndFlags |= QuantizedNormalValidBit;
                normal = math.normalize(normal);
                float2 oct = normal.xy / math.csum(math.abs(normal));
                if (normal.z < 0) oct = (1 - math.abs(oct.yx)) * SignNotZero(oct);
                // Search the four neighboring oct11 points, as in vk_lod_clusters' precise encoder.
                float2 scaled = math.saturate(oct * 0.5f + 0.5f) * 2047f;
                uint2 lo = (uint2) math.floor(scaled);
                float best = -2f;
                for (uint i = 0; i < 4; i++)
                {
                    uint2 q = math.min(lo + new uint2(i & 1, i >> 1), new uint2(2047));
                    uint packed = q.x | (q.y << 11);
                    float score = math.dot(normal, UnpackNormal11(packed));
                    if (score > best) { best = score; vertex.TangentFrame = packed; }
                }
                if (tangentValid)
                {
                    // Use the decoded normal for both encoder and decoder's basis.
                    float3 decodedNormal = UnpackNormal11(vertex.TangentFrame);
                    BuildTangentBasis(decodedNormal, out float3 t, out float3 b);
                    float angle = math.atan2(math.dot(t, tangent.xyz), math.dot(b, tangent.xyz));
                    uint angleBits = (uint) math.floor(math.saturate(angle / math.PI * 0.5f + 0.5f) * 511f + 0.5f);
                    uint frame = (angleBits << 1) | (tangent.w < 0 ? 0u : 1u);
                    vertex.TangentFrame |= frame << 22;
                }
            }
            else if (tangentValid)
            {
                // No normal: the whole word stores a standalone oct15 tangent.
                vertex.TangentFrame = PackTangent(tangent);
            }
            if (tangentValid) vertex.UVAndFlags |= QuantizedTangentValidBit;
            return vertex;
        }

        public static float3 UnpackNormal(in VividMeshletVertex vertex) =>
            (vertex.UVAndFlags & QuantizedNormalValidBit) != 0 ? UnpackNormal11(vertex.TangentFrame) : default;

        public static float4 UnpackTangent(in VividMeshletVertex vertex)
        {
            if ((vertex.UVAndFlags & QuantizedTangentValidBit) == 0) return default;
            if ((vertex.UVAndFlags & QuantizedNormalValidBit) == 0) return UnpackTangent(vertex.TangentFrame);
            BuildTangentBasis(UnpackNormal11(vertex.TangentFrame), out float3 t, out float3 b);
            uint frame = vertex.TangentFrame >> 22;
            float angle = ((frame >> 1) / 511f * 2f - 1f) * math.PI;
            math.sincos(angle, out float sine, out float cosine);
            return new float4(cosine * b + sine * t, (frame & 1) != 0 ? 1f : -1f);
        }

        private static float3 UnpackNormal11(uint packed)
        {
            float2 oct = new float2(packed & 2047u, (packed >> 11) & 2047u) / 2047f * 2f - 1f;
            float3 n = new(oct, 1f - math.csum(math.abs(oct)));
            float fold = math.saturate(-n.z);
            n.xy -= SignNotZero(n.xy) * fold;
            return math.normalizesafe(n);
        }

        private static void BuildTangentBasis(float3 normal, out float3 tangent, out float3 bitangent)
        {
            if (normal.z < -0.99998796f)
            {
                tangent = new float3(0, -1, 0);
                bitangent = new float3(-1, 0, 0);
                return;
            }
            float a = 1f / (1f + normal.z);
            float b = -normal.x * normal.y * a;
            tangent = new float3(1f - normal.x * normal.x * a, b, -normal.x);
            bitangent = new float3(b, 1f - normal.y * normal.y * a, -normal.y);
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
    // Uploaded verbatim to a StructuredBuffer; field order must match the generated HLSL.
    [StructLayout(LayoutKind.Sequential)]
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
    [StructLayout(LayoutKind.Sequential)]
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
