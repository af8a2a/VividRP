using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Runtime.PrimitiveScene
{
    internal readonly struct VividPrimitiveHandle : IEquatable<VividPrimitiveHandle>
    {
        internal static readonly VividPrimitiveHandle Invalid = new(-1, 0u);

        internal VividPrimitiveHandle(int index, uint generation)
        {
            Index = index;
            Generation = generation;
        }

        internal int Index { get; }

        internal uint Generation { get; }

        internal bool IsValid => Index >= 0 && Generation != 0u;

        public bool Equals(VividPrimitiveHandle other)
        {
            return Index == other.Index && Generation == other.Generation;
        }

        public override bool Equals(object obj)
        {
            return obj is VividPrimitiveHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Index * 397) ^ (int) Generation;
            }
        }
    }

    internal readonly struct VividPrimitiveGeometryHandle : IEquatable<VividPrimitiveGeometryHandle>
    {
        internal static readonly VividPrimitiveGeometryHandle Invalid = new(-1, 0u);

        internal VividPrimitiveGeometryHandle(int index, uint generation)
        {
            Index = index;
            Generation = generation;
        }

        internal int Index { get; }

        internal uint Generation { get; }

        internal bool IsValid => Index >= 0 && Generation != 0u;

        public bool Equals(VividPrimitiveGeometryHandle other)
        {
            return Index == other.Index && Generation == other.Generation;
        }

        public override bool Equals(object obj)
        {
            return obj is VividPrimitiveGeometryHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Index * 397) ^ (int) Generation;
            }
        }
    }

    internal readonly struct VividPrimitiveMaterialHandle : IEquatable<VividPrimitiveMaterialHandle>
    {
        internal static readonly VividPrimitiveMaterialHandle Invalid = new(-1, 0u);

        internal VividPrimitiveMaterialHandle(int index, uint generation)
        {
            Index = index;
            Generation = generation;
        }

        internal int Index { get; }

        internal uint Generation { get; }

        internal bool IsValid => Index >= 0 && Generation != 0u;

        public bool Equals(VividPrimitiveMaterialHandle other)
        {
            return Index == other.Index && Generation == other.Generation;
        }

        public override bool Equals(object obj)
        {
            return obj is VividPrimitiveMaterialHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Index * 397) ^ (int) Generation;
            }
        }
    }

    [GenerateHLSL(PackingRules.Exact)]
    [Flags]
    internal enum VividPrimitiveFlags : uint
    {
        None = 0,
        Valid = 1u << 0,
        Disabled = 1u << 1,
        FlipWindingOrder = 1u << 2,
        Static = 1u << 3,
        Skinned = 1u << 4,
        Terrain = 1u << 5,
        ReceiveShadows = 1u << 6,
    }

    [GenerateHLSL(PackingRules.Exact)]
    [Flags]
    internal enum VividPrimitiveDrawSectionFlags : uint
    {
        None = 0,
        Valid = 1u << 0,
        Terrain = 1u << 1,
    }

    [GenerateHLSL(PackingRules.Exact, needAccessors = false)]
    [StructLayout(LayoutKind.Sequential)]
    internal struct VividPrimitiveData
    {
        public float4 WorldBoundsMin;
        public float4 WorldBoundsMax;

        public uint TransformIndex;
        public uint DrawSectionOffset;
        public uint DrawSectionCount;
        public uint RenderingLayerMask;

        public uint PassMask;
        public VividPrimitiveFlags Flags;
        public uint Generation;
        public uint CustomDataAddress;
    }

    [GenerateHLSL(PackingRules.Exact, needAccessors = false)]
    [StructLayout(LayoutKind.Sequential)]
    internal struct VividPrimitiveTransformData
    {
        public float4x4 ObjectToWorldMatrix;
        public float4x4 WorldToObjectMatrix;
    }

    [GenerateHLSL(PackingRules.Exact, needAccessors = false)]
    [StructLayout(LayoutKind.Sequential)]
    internal struct VividPrimitivePreviousTransformData
    {
        public float4x4 PreviousObjectToWorldMatrix;
    }

    [GenerateHLSL(PackingRules.Exact, needAccessors = false)]
    [StructLayout(LayoutKind.Sequential)]
    internal struct VividPrimitiveDrawSectionData
    {
        public uint GeometryIndex;
        public uint GeometryGeneration;
        public uint MaterialIndex;
        public uint MaterialGeneration;

        public uint SourceSectionIndex;
        public VividPrimitiveDrawSectionFlags Flags;
        public uint Padding0;
        public uint Padding1;
    }

    [GenerateHLSL(PackingRules.Exact, needAccessors = false)]
    [StructLayout(LayoutKind.Sequential)]
    internal struct VividPrimitiveGeometryData
    {
        public uint Generation;
        public uint LegacyTopMeshLODStartIndex;
        public uint LegacyTotalMeshLODCount;
        public uint LegacyMeshLODLevelCount;
    }

    [GenerateHLSL(PackingRules.Exact, needAccessors = false)]
    [StructLayout(LayoutKind.Sequential)]
    internal struct VividPrimitiveMaterialData
    {
        public uint Generation;
        public uint LegacyMaterialIndex;
        public VividRendererListID RendererListID;
        public VividMaterialFlags MaterialFlags;
    }

    [GenerateHLSL(PackingRules.Exact, needAccessors = false)]
    [StructLayout(LayoutKind.Sequential)]
    internal struct VividLegacyInstanceMappingData
    {
        public uint PrimitiveIndex;
        public uint PrimitiveGeneration;
        public uint DrawSectionIndex;
        public uint Flags;
    }

    internal enum VividPrimitiveResourceDomain : byte
    {
        None = 0,
        MeshletGeometry = 1,
        TerrainGeometry = 2,
        MaterialProxy = 3,
        UnityMaterial = 4,
        TerrainMaterial = 5,
        MissingMaterial = 6,
    }

    internal readonly struct VividPrimitiveResourceKey : IEquatable<VividPrimitiveResourceKey>
    {
        internal static readonly VividPrimitiveResourceKey Invalid = default;

        internal VividPrimitiveResourceKey(
            VividPrimitiveResourceDomain domain,
            EntityId objectId,
            EntityId ownerId,
            int sourceSectionIndex)
        {
            Domain = domain;
            ObjectId = objectId;
            OwnerId = ownerId;
            SourceSectionIndex = sourceSectionIndex;
        }

        internal VividPrimitiveResourceDomain Domain { get; }

        internal EntityId ObjectId { get; }

        internal EntityId OwnerId { get; }

        internal int SourceSectionIndex { get; }

        internal bool IsValid
        {
            get
            {
                if (Domain == VividPrimitiveResourceDomain.MissingMaterial)
                {
                    return ObjectId.Equals(EntityId.None)
                        && !OwnerId.Equals(EntityId.None)
                        && SourceSectionIndex >= 0;
                }

                return Domain is VividPrimitiveResourceDomain.MeshletGeometry
                        or VividPrimitiveResourceDomain.TerrainGeometry
                        or VividPrimitiveResourceDomain.MaterialProxy
                        or VividPrimitiveResourceDomain.UnityMaterial
                        or VividPrimitiveResourceDomain.TerrainMaterial
                    && !ObjectId.Equals(EntityId.None)
                    && OwnerId.Equals(EntityId.None)
                    && SourceSectionIndex == -1;
            }
        }

        public bool Equals(VividPrimitiveResourceKey other)
        {
            return Domain == other.Domain
                && ObjectId.Equals(other.ObjectId)
                && OwnerId.Equals(other.OwnerId)
                && SourceSectionIndex == other.SourceSectionIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is VividPrimitiveResourceKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (int) Domain;
                hashCode = (hashCode * 397) ^ EntityId.ToULong(ObjectId).GetHashCode();
                hashCode = (hashCode * 397) ^ EntityId.ToULong(OwnerId).GetHashCode();
                return (hashCode * 397) ^ SourceSectionIndex;
            }
        }
    }

    internal readonly struct VividPrimitiveDrawSectionDescriptor
    {
        internal VividPrimitiveDrawSectionDescriptor(
            int sourceSectionIndex,
            VividPrimitiveResourceKey geometryKey,
            VividPrimitiveResourceKey materialKey,
            VividPrimitiveDrawSectionFlags flags)
        {
            SourceSectionIndex = sourceSectionIndex;
            GeometryKey = geometryKey;
            MaterialKey = materialKey;
            Flags = flags;
        }

        internal int SourceSectionIndex { get; }

        internal VividPrimitiveResourceKey GeometryKey { get; }

        internal VividPrimitiveResourceKey MaterialKey { get; }

        internal VividPrimitiveDrawSectionFlags Flags { get; }
    }

    internal readonly struct VividPrimitiveSourceDescriptor
    {
        internal VividPrimitiveSourceDescriptor(
            EntityId sourceEntityId,
            Matrix4x4 objectToWorldMatrix,
            Matrix4x4 worldToObjectMatrix,
            Bounds worldBounds,
            uint renderingLayerMask,
            VividInstancePassMask passMask,
            VividPrimitiveFlags flags,
            IReadOnlyList<VividPrimitiveDrawSectionDescriptor> drawSections)
        {
            SourceEntityId = sourceEntityId;
            ObjectToWorldMatrix = objectToWorldMatrix;
            WorldToObjectMatrix = worldToObjectMatrix;
            WorldBounds = worldBounds;
            RenderingLayerMask = renderingLayerMask;
            PassMask = passMask;
            Flags = flags;
            DrawSections = drawSections ?? Array.Empty<VividPrimitiveDrawSectionDescriptor>();
        }

        internal EntityId SourceEntityId { get; }

        internal Matrix4x4 ObjectToWorldMatrix { get; }

        internal Matrix4x4 WorldToObjectMatrix { get; }

        internal Bounds WorldBounds { get; }

        internal uint RenderingLayerMask { get; }

        internal VividInstancePassMask PassMask { get; }

        internal VividPrimitiveFlags Flags { get; }

        internal IReadOnlyList<VividPrimitiveDrawSectionDescriptor> DrawSections { get; }
    }

    internal readonly struct VividPrimitiveSceneStats
    {
        internal VividPrimitiveSceneStats(
            int activePrimitiveCount,
            int primitiveSlotCount,
            int freePrimitiveSlotCount,
            int activeDrawSectionCount,
            int drawSectionHighWaterMark,
            int activeGeometryCount,
            int geometrySlotCount,
            int freeGeometrySlotCount,
            int activeMaterialCount,
            int materialSlotCount,
            int freeMaterialSlotCount,
            uint sceneRevision,
            int changedPrimitiveCount,
            int fullResyncCount,
            int dirtyPageCount,
            int lastUploadRangeCount,
            long lastUploadBytes)
        {
            ActivePrimitiveCount = activePrimitiveCount;
            PrimitiveSlotCount = primitiveSlotCount;
            FreePrimitiveSlotCount = freePrimitiveSlotCount;
            ActiveDrawSectionCount = activeDrawSectionCount;
            DrawSectionHighWaterMark = drawSectionHighWaterMark;
            ActiveGeometryCount = activeGeometryCount;
            GeometrySlotCount = geometrySlotCount;
            FreeGeometrySlotCount = freeGeometrySlotCount;
            ActiveMaterialCount = activeMaterialCount;
            MaterialSlotCount = materialSlotCount;
            FreeMaterialSlotCount = freeMaterialSlotCount;
            SceneRevision = sceneRevision;
            ChangedPrimitiveCount = changedPrimitiveCount;
            FullResyncCount = fullResyncCount;
            DirtyPageCount = dirtyPageCount;
            LastUploadRangeCount = lastUploadRangeCount;
            LastUploadBytes = lastUploadBytes;
        }

        internal int ActivePrimitiveCount { get; }
        internal int PrimitiveSlotCount { get; }
        internal int FreePrimitiveSlotCount { get; }
        internal int ActiveDrawSectionCount { get; }
        internal int DrawSectionHighWaterMark { get; }
        internal int ActiveGeometryCount { get; }
        internal int GeometrySlotCount { get; }
        internal int FreeGeometrySlotCount { get; }
        internal int ActiveMaterialCount { get; }
        internal int MaterialSlotCount { get; }
        internal int FreeMaterialSlotCount { get; }
        internal uint SceneRevision { get; }
        internal int ChangedPrimitiveCount { get; }
        internal int FullResyncCount { get; }
        internal int DirtyPageCount { get; }
        internal int LastUploadRangeCount { get; }
        internal long LastUploadBytes { get; }
    }
}
