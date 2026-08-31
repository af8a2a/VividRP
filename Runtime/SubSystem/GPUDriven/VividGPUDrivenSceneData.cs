using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine;

namespace VividRP.Runtime.GPUDriven
{
    [Flags]
    internal enum VividGPUDrivenInstanceSourceFlags : byte
    {
        None = 0,
        TerrainGeometry = 1 << 0,
        MaterialProxy = 1 << 1,
        TerrainMaterial = 1 << 2,
        MissingMaterial = 1 << 3,
    }

    internal readonly struct VividGPUDrivenInstanceSourceData
    {
        internal VividGPUDrivenInstanceSourceData(
            EntityId primitiveEntityId,
            EntityId geometryEntityId,
            EntityId materialEntityId,
            int sourceSectionIndex,
            VividGPUDrivenInstanceSourceFlags flags)
        {
            PrimitiveEntityId = primitiveEntityId;
            GeometryEntityId = geometryEntityId;
            MaterialEntityId = materialEntityId;
            SourceSectionIndex = sourceSectionIndex;
            Flags = flags;
        }

        internal EntityId PrimitiveEntityId { get; }
        internal EntityId GeometryEntityId { get; }
        internal EntityId MaterialEntityId { get; }
        internal int SourceSectionIndex { get; }
        internal VividGPUDrivenInstanceSourceFlags Flags { get; }
    }

    public sealed class VividGPUDrivenSceneData
    {
        private readonly List<VividInstanceData> m_Instances = new();
        private readonly List<VividGPUDrivenInstanceSourceData> m_InstanceSources = new();
        private readonly List<VividMaterialData> m_Materials = new();
        private readonly List<VividDualSlabMaterialData> m_DualSlabMaterials = new();
        private readonly List<uint4> m_MaterialParameterLanes = new();
        private readonly List<VividMaterialResourceData> m_MaterialResources = new();
        private readonly List<VividMaterialRuntimeHeader> m_MaterialRuntimeHeaders = new();
        private readonly List<VividMaterialProgramData> m_MaterialPrograms = new(
            GPUDrivenMaterialCompiler.CreateRuntimeProgramTable());
        private readonly List<VividSurfaceBindingData> m_SurfaceBindings = new();
        private readonly List<VividTerrainMaterialData> m_TerrainMaterials = new();
        private readonly List<VividTerrainLayerGPUData> m_TerrainLayers = new();
        private readonly List<VividMeshLODNode> m_MeshLODNodes = new();
        private readonly List<VividMeshlet> m_Meshlets = new();
        private readonly List<VividMeshletVertex> m_Vertices = new();
        private readonly List<byte> m_Indices = new();
        private int m_MaxMeshletListBuildJobCount;
        private int m_MaxVisibleMeshletRenderRequestCount;
        private uint m_MainViewRendererBatchMask;
        private uint m_ShadowRendererBatchMask;

        public IReadOnlyList<VividInstanceData> Instances => m_Instances;

        public IReadOnlyList<VividMaterialData> Materials => m_Materials;

        public IReadOnlyList<VividDualSlabMaterialData> DualSlabMaterials =>
            m_DualSlabMaterials;

        public IReadOnlyList<uint4> MaterialParameterLanes =>
            m_MaterialParameterLanes;

        public IReadOnlyList<VividMaterialResourceData> MaterialResources =>
            m_MaterialResources;

        public IReadOnlyList<VividMaterialRuntimeHeader> MaterialRuntimeHeaders =>
            m_MaterialRuntimeHeaders;

        public IReadOnlyList<VividMaterialProgramData> MaterialPrograms => m_MaterialPrograms;

        public IReadOnlyList<VividSurfaceBindingData> SurfaceBindings => m_SurfaceBindings;

        public IReadOnlyList<VividTerrainMaterialData> TerrainMaterials => m_TerrainMaterials;

        public IReadOnlyList<VividTerrainLayerGPUData> TerrainLayers => m_TerrainLayers;

        public IReadOnlyList<VividMeshLODNode> MeshLODNodes => m_MeshLODNodes;

        public IReadOnlyList<VividMeshlet> Meshlets => m_Meshlets;

        public IReadOnlyList<VividMeshletVertex> Vertices => m_Vertices;

        public IReadOnlyList<byte> Indices => m_Indices;

        public int InstanceCount => m_Instances.Count;

        public int MaterialCount => m_Materials.Count;

        public int DualSlabMaterialCount => m_DualSlabMaterials.Count;

        public int MaterialParameterLaneCount => m_MaterialParameterLanes.Count;

        public int MaterialResourceCount => m_MaterialResources.Count;

        public int MaterialRuntimeHeaderCount => m_MaterialRuntimeHeaders.Count;

        public int MaterialProgramCount => m_MaterialPrograms.Count;

        public int SurfaceBindingCount => m_SurfaceBindings.Count;

        public int TerrainMaterialCount => m_TerrainMaterials.Count;

        public int TerrainLayerCount => m_TerrainLayers.Count;

        public int MeshLODNodeCount => m_MeshLODNodes.Count;

        public int MeshletCount => m_Meshlets.Count;

        public int VertexCount => m_Vertices.Count;

        public int IndexCount => m_Indices.Count;

        internal int MaxMeshletListBuildJobCount => Mathf.Max(1, m_MaxMeshletListBuildJobCount);

        internal int MaxVisibleMeshletRenderRequestCount => Mathf.Max(1, m_MaxVisibleMeshletRenderRequestCount);

        internal bool IsMainViewRendererBatchActive(VividRendererListID batchKey)
        {
            int batchIndex = (int) batchKey;
            return (uint) batchIndex < (uint) VividRendererListID.Count
                && (m_MainViewRendererBatchMask & (1u << batchIndex)) != 0;
        }

        internal bool IsShadowRendererBatchActive(VividRendererListID batchKey)
        {
            int batchIndex = (int) batchKey;
            return (uint) batchIndex < (uint) VividRendererListID.Count
                && (m_ShadowRendererBatchMask & (1u << batchIndex)) != 0;
        }

        internal bool RequiresDualSlabSidecar(uint materialIndex)
        {
            if (materialIndex >= (uint) m_MaterialRuntimeHeaders.Count)
                return false;

            VividMaterialRuntimeHeader runtimeHeader =
                m_MaterialRuntimeHeaders[(int) materialIndex];
            if ((runtimeHeader.Flags & VividMaterialRuntimeFlags.Unlit) != 0
                || runtimeHeader.ProgramID == VividMaterialProgramID.Invalid)
            {
                return false;
            }

            uint programIndex = (uint) runtimeHeader.ProgramID;
            if (programIndex >= (uint) m_MaterialPrograms.Count)
                return false;

            VividMaterialProgramData program = m_MaterialPrograms[(int) programIndex];
            return program.Version == GPUDrivenMaterialCompiler.ProgramVersion
                && program.SurfaceProgramID == VividMaterialSurfaceProgramID.DualSlab;
        }

        internal bool HasDualSlabSidecarMaterial()
        {
            for (uint materialIndex = 0u;
                 materialIndex < (uint) m_MaterialRuntimeHeaders.Count;
                 materialIndex++)
            {
                if (RequiresDualSlabSidecar(materialIndex))
                    return true;
            }

            return false;
        }

        internal List<VividInstanceData> MutableInstances => m_Instances;

        internal IReadOnlyList<VividGPUDrivenInstanceSourceData> InstanceSources => m_InstanceSources;

        internal List<VividMaterialData> MutableMaterials => m_Materials;

        internal List<VividDualSlabMaterialData> MutableDualSlabMaterials =>
            m_DualSlabMaterials;

        internal List<uint4> MutableMaterialParameterLanes =>
            m_MaterialParameterLanes;

        internal List<VividMaterialResourceData> MutableMaterialResources =>
            m_MaterialResources;

        internal List<VividMaterialRuntimeHeader> MutableMaterialRuntimeHeaders =>
            m_MaterialRuntimeHeaders;

        internal List<VividMaterialProgramData> MutableMaterialPrograms => m_MaterialPrograms;

        internal List<VividSurfaceBindingData> MutableSurfaceBindings => m_SurfaceBindings;

        internal List<VividTerrainMaterialData> MutableTerrainMaterials => m_TerrainMaterials;

        internal List<VividTerrainLayerGPUData> MutableTerrainLayers => m_TerrainLayers;

        internal List<VividMeshLODNode> MutableMeshLODNodes => m_MeshLODNodes;

        internal List<VividMeshlet> MutableMeshlets => m_Meshlets;

        internal List<VividMeshletVertex> MutableVertices => m_Vertices;

        internal List<byte> MutableIndices => m_Indices;

        internal void AddInstance(
            in VividInstanceData instanceData,
            int maxVisibleMeshletRenderRequestCount)
        {
            AddInstance(instanceData, default, maxVisibleMeshletRenderRequestCount);
        }

        internal void AddInstance(
            in VividInstanceData instanceData,
            in VividGPUDrivenInstanceSourceData sourceData,
            int maxVisibleMeshletRenderRequestCount)
        {
            m_Instances.Add(instanceData);
            m_InstanceSources.Add(sourceData);
            RegisterRendererBatch(instanceData);

            uint maxNodesPerJob = global::VividRP.Runtime.GPUDriven.Meshlets.VividMeshletListBuildJob.MaxLODNodesPerThreadGroup;
            if (maxNodesPerJob == 0)
                maxNodesPerJob = 1;

            ulong jobCount = ((ulong) instanceData.TotalMeshLODCount + maxNodesPerJob - 1u) / maxNodesPerJob;
            m_MaxMeshletListBuildJobCount = SaturatingAdd(
                m_MaxMeshletListBuildJobCount,
                jobCount > int.MaxValue ? int.MaxValue : (int) jobCount);
            m_MaxVisibleMeshletRenderRequestCount = SaturatingAdd(
                m_MaxVisibleMeshletRenderRequestCount,
                Mathf.Max(0, maxVisibleMeshletRenderRequestCount));
        }

        internal void ClearInstances()
        {
            m_Instances.Clear();
            m_InstanceSources.Clear();
            m_MaxMeshletListBuildJobCount = 0;
            m_MaxVisibleMeshletRenderRequestCount = 0;
            m_MainViewRendererBatchMask = 0;
            m_ShadowRendererBatchMask = 0;
        }

        internal void ClearMaterials()
        {
            VividMaterialProgramData[] runtimePrograms =
                GPUDrivenMaterialCompiler.CreateRuntimeProgramTable();
            m_Materials.Clear();
            m_DualSlabMaterials.Clear();
            m_MaterialParameterLanes.Clear();
            m_MaterialResources.Clear();
            m_MaterialRuntimeHeaders.Clear();
            m_MaterialPrograms.Clear();
            m_MaterialPrograms.AddRange(runtimePrograms);
        }

        internal int AddMaterial(
            in VividMaterialData materialData,
            in VividMaterialRuntimeHeader runtimeHeader)
        {
            if (m_MaterialRuntimeHeaders.Count != m_Materials.Count)
            {
                throw new InvalidOperationException(
                    "Material runtime headers must remain index-aligned with material data.");
            }

            int materialIndex = m_Materials.Count;
            bool usesLegacyParameterLayout =
                runtimeHeader.ProgramID == VividMaterialProgramID.Invalid;
            if (runtimeHeader.ProgramID != VividMaterialProgramID.Invalid)
            {
                uint programIndex = (uint) runtimeHeader.ProgramID;
                if (programIndex >= (uint) m_MaterialPrograms.Count)
                {
                    throw new ArgumentException(
                        $"Material program {programIndex} is not registered.",
                        nameof(runtimeHeader));
                }

                VividMaterialProgramData runtimeData =
                    m_MaterialPrograms[(int) programIndex];
                if (runtimeData.ParameterLayoutID
                        != VividMaterialParameterLayoutID.GenericParameterLanes
                    || runtimeData.ResourceLayoutID
                        != VividMaterialResourceLayoutID.GenericResourceRecords)
                {
                    throw new ArgumentException(
                        $"Material program {programIndex} does not use the generic runtime layout.",
                        nameof(runtimeHeader));
                }

                MaterialProgramRuntimeBinding programBinding =
                    GPUDrivenMaterialCompiler.GetRuntimeProgramBinding(
                        runtimeHeader.ProgramID);
                uint parameterLaneAddress = runtimeHeader.ParameterAddress;
                uint parameterLaneCount =
                    (uint) (programBinding.ParameterStrideInWords / 4);
                if (parameterLaneAddress > (uint) m_MaterialParameterLanes.Count
                    || parameterLaneCount
                        > (uint) m_MaterialParameterLanes.Count - parameterLaneAddress)
                {
                    throw new ArgumentException(
                        $"Material program {programIndex} requires {parameterLaneCount} parameter lanes at address {parameterLaneAddress}, but only {m_MaterialParameterLanes.Count} lanes are present.",
                        nameof(runtimeHeader));
                }

                uint resourceBindingAddress = runtimeHeader.ResourceBindingAddress;
                uint resourceRecordCount = (uint) programBinding.ResourceCount;
                if (resourceBindingAddress > (uint) m_MaterialResources.Count
                    || resourceRecordCount
                        > (uint) m_MaterialResources.Count - resourceBindingAddress)
                {
                    throw new ArgumentException(
                        $"Material program {programIndex} requires {resourceRecordCount} generic resource records at address {resourceBindingAddress}, but only {m_MaterialResources.Count} records are present.",
                        nameof(runtimeHeader));
                }
            }

            if (usesLegacyParameterLayout
                && runtimeHeader.ParameterAddress != (uint) materialIndex)
            {
                throw new ArgumentException(
                    $"Material parameter address {runtimeHeader.ParameterAddress} does not match material index {materialIndex}.",
                    nameof(runtimeHeader));
            }
            m_Materials.Add(materialData);
            m_MaterialRuntimeHeaders.Add(runtimeHeader);
            return materialIndex;
        }

        internal int AddLegacyMaterial(in VividMaterialData materialData)
        {
            uint parameterAddress = (uint) m_Materials.Count;
            VividMaterialRuntimeHeader runtimeHeader =
                GPUDrivenMaterialCompiler.CreateLegacyFallbackHeader(
                    parameterAddress,
                    materialData.SurfaceBindingIndex);
            return AddMaterial(materialData, runtimeHeader);
        }

        internal void ClearSurfaceBindings()
        {
            m_SurfaceBindings.Clear();
        }

        internal void ClearTerrainMaterials()
        {
            m_TerrainMaterials.Clear();
            m_TerrainLayers.Clear();
        }

        internal void ClearDynamic()
        {
            ClearInstances();
            ClearMaterials();
            ClearSurfaceBindings();
            ClearTerrainMaterials();
        }

        internal void Clear()
        {
            ClearDynamic();
            m_MeshLODNodes.Clear();
            m_Meshlets.Clear();
            m_Vertices.Clear();
            m_Indices.Clear();
        }

        private void RegisterRendererBatch(in VividInstanceData instanceData)
        {
            if ((instanceData.Flags & VividInstanceFlags.Disabled) != 0
                || instanceData.MaterialIndex >= (uint) m_Materials.Count)
            {
                return;
            }

            VividRendererListID materialBatchKey =
                m_Materials[(int) instanceData.MaterialIndex].RendererListID;
            VividRendererListID mainBatchKey = materialBatchKey;
            if ((instanceData.Flags & VividInstanceFlags.FlipWindingOrder) != 0
                && (mainBatchKey & VividRendererListID.CullOff) == 0)
            {
                mainBatchKey ^= VividRendererListID.CullFront;
            }

            if ((instanceData.PassMask & VividInstancePassMask.Main) != 0)
                RegisterRendererBatch(ref m_MainViewRendererBatchMask, mainBatchKey);
            if ((instanceData.PassMask & VividInstancePassMask.Shadows) != 0)
            {
                VividRendererListID shadowBatchKey =
                    (instanceData.Flags & VividInstanceFlags.TwoSidedShadows) != 0
                        ? (materialBatchKey & ~VividRendererListID.CullFront)
                            | VividRendererListID.CullOff
                        : mainBatchKey;
                RegisterRendererBatch(ref m_ShadowRendererBatchMask, shadowBatchKey);
            }
        }

        private static void RegisterRendererBatch(
            ref uint rendererBatchMask,
            VividRendererListID batchKey)
        {
            int batchIndex = (int) batchKey;
            if ((uint) batchIndex < (uint) VividRendererListID.Count)
                rendererBatchMask |= 1u << batchIndex;
        }

        private static int SaturatingAdd(int current, int increment)
        {
            if (increment <= 0)
                return current;

            return current > int.MaxValue - increment
                ? int.MaxValue
                : current + increment;
        }
    }
}
