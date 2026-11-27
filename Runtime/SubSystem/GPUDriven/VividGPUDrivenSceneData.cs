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

    internal readonly struct VividDynamicSlabProgramBinding
    {
        internal VividDynamicSlabProgramBinding(
            VividMaterialProgramID programID,
            Shader rasterShader,
            ComputeShader computeShader,
            int kernelIndex,
            LocalKeyword virtualTextureKeyword,
            int parameterStrideInWords,
            int resourceCount,
            bool requiresProgrammableVisibilityRaster,
            VividSlabProgramDescriptor descriptor)
        {
            ProgramID = programID;
            RasterShader = rasterShader;
            ComputeShader = computeShader;
            KernelIndex = kernelIndex;
            VirtualTextureKeyword = virtualTextureKeyword;
            ParameterStrideInWords = parameterStrideInWords;
            ResourceCount = resourceCount;
            RequiresProgrammableVisibilityRaster = requiresProgrammableVisibilityRaster;
            Descriptor = descriptor;
        }

        internal VividMaterialProgramID ProgramID { get; }
        internal Shader RasterShader { get; }
        internal ComputeShader ComputeShader { get; }
        internal int KernelIndex { get; }
        internal LocalKeyword VirtualTextureKeyword { get; }
        internal int ParameterStrideInWords { get; }
        internal int ResourceCount { get; }
        internal bool RequiresProgrammableVisibilityRaster { get; }
        internal VividSlabProgramDescriptor Descriptor { get; }
    }

    public sealed class VividGPUDrivenSceneData
    {
        private readonly List<VividInstanceData> m_Instances = new();
        private readonly List<VividGPUDrivenInstanceSourceData> m_InstanceSources = new();
        private readonly List<VividMaterialData> m_Materials = new();
        private readonly List<int> m_ShadowMaterialResourceCounts = new();
        private readonly List<VividDualSlabMaterialData> m_DualSlabMaterials = new();
        private readonly List<uint4> m_MaterialParameterLanes = new();
        private readonly List<VividMaterialResourceData> m_MaterialResources = new();
        private readonly List<VividMaterialRuntimeHeader> m_MaterialRuntimeHeaders = new();
        private readonly List<VividMaterialProgramData> m_MaterialPrograms = new(
            GPUDrivenMaterialCompiler.CreateRuntimeProgramTable());
        private readonly List<VividDynamicSlabProgramBinding> m_DynamicSlabPrograms = new();
        private readonly List<VividSurfaceBindingData> m_SurfaceBindings = new();
        private readonly List<VividTerrainMaterialData> m_TerrainMaterials = new();
        private readonly List<VividTerrainLayerGPUData> m_TerrainLayers = new();
        private readonly List<VividMeshLODNodePacked> m_MeshLODNodes = new();
        private readonly List<VividMeshLODGroup> m_MeshLODGroups = new();
        private readonly List<VividMeshLODGroupPacked> m_PackedMeshLODGroups = new();
        private readonly List<VividMeshLODGroupOverflow> m_MeshLODGroupOverflow = new();
        private readonly List<VividMeshlet> m_Meshlets = new();
        private readonly List<VividMeshletVertex> m_Vertices = new();
        private readonly List<byte> m_Indices = new();
        private int m_MaxMeshletListBuildJobCount;
        private int m_MaxVisibleMeshletRenderRequestCount;
        private uint m_MainViewRendererBatchMask;
        private uint m_ShadowRendererBatchMask;
        private ulong m_ActiveDirectProgramBinMask;
        private bool m_HasActiveOverflowProgramBin;
        private bool m_HasInstanceOverflowMaterial;
        private int m_ActiveMaterialBinsSyncedHeaderCount;

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

        internal IReadOnlyList<VividDynamicSlabProgramBinding> DynamicSlabPrograms =>
            m_DynamicSlabPrograms;

        internal bool TryGetDynamicSlabProgram(
            VividMaterialProgramID programID,
            out VividDynamicSlabProgramBinding binding)
        {
            for (int i = 0; i < m_DynamicSlabPrograms.Count; i++)
            {
                if (m_DynamicSlabPrograms[i].ProgramID == programID)
                {
                    binding = m_DynamicSlabPrograms[i];
                    return true;
                }
            }
            binding = default;
            return false;
        }

        internal bool HasProgrammableVisibilityRasterPrograms()
        {
            for (int i = 0; i < m_DynamicSlabPrograms.Count; i++)
            {
                if (m_DynamicSlabPrograms[i].RequiresProgrammableVisibilityRaster)
                    return true;
            }
            return false;
        }

        internal VividMaterialProgramID RegisterDynamicSlabProgram(
            VividSlabProgramDescriptor descriptor)
        {
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));

            for (int i = 0; i < m_DynamicSlabPrograms.Count; i++)
            {
                if (ReferenceEquals(m_DynamicSlabPrograms[i].Descriptor, descriptor))
                    return m_DynamicSlabPrograms[i].ProgramID;
            }

            uint programIndex = (uint)m_MaterialPrograms.Count;
            var programID = (VividMaterialProgramID)programIndex;
            VividMaterialProgramCapabilities capabilities = VividMaterialProgramCapabilities.None;
            if (descriptor.HasCustomCoverage)
                capabilities |= VividMaterialProgramCapabilities.AlphaClip;
            if (descriptor.RequiresProgrammableVisibilityRaster)
                capabilities |= VividMaterialProgramCapabilities.ProgrammableRaster;

            m_MaterialPrograms.Add(new VividMaterialProgramData
            {
                Version = GPUDrivenMaterialCompiler.ProgramVersion,
                CoverageProgramID = VividMaterialCoverageProgramID.BaseColorAlpha,
                SurfaceProgramID = VividMaterialSurfaceProgramID.DynamicSlab,
                TransportProgramID = VividMaterialTransportProgramID.None,
                ParameterLayoutID = VividMaterialParameterLayoutID.GenericParameterLanes,
                ResourceLayoutID = VividMaterialResourceLayoutID.GenericResourceRecords,
                CapabilityFlags = capabilities,
                ExecutionClass = VividMaterialExecutionClass.VisibilityDeferred,
            });

            ComputeShader cs = descriptor.ResolveComputeShader;
            int kernelIndex = cs != null && cs.HasKernel(descriptor.ResolveKernelName)
                ? cs.FindKernel(descriptor.ResolveKernelName)
                : -1;
            LocalKeyword vtKeyword = VividGPUDrivenSystem.ResolveTextureBackendKeyword(cs);
            m_DynamicSlabPrograms.Add(new VividDynamicSlabProgramBinding(
                programID,
                descriptor.RasterShader,
                cs,
                kernelIndex,
                vtKeyword,
                descriptor.ParameterStrideInWords,
                descriptor.ResourceCount,
                descriptor.RequiresProgrammableVisibilityRaster,
                descriptor));
            return programID;
        }

        public IReadOnlyList<VividSurfaceBindingData> SurfaceBindings => m_SurfaceBindings;

        public IReadOnlyList<VividTerrainMaterialData> TerrainMaterials => m_TerrainMaterials;

        public IReadOnlyList<VividTerrainLayerGPUData> TerrainLayers => m_TerrainLayers;

        public IReadOnlyList<VividMeshLODNodePacked> MeshLODNodes => m_MeshLODNodes;

        public IReadOnlyList<VividMeshLODGroup> MeshLODGroups => m_MeshLODGroups;
        public IReadOnlyList<VividMeshLODGroupPacked> PackedMeshLODGroups => m_PackedMeshLODGroups;
        public IReadOnlyList<VividMeshLODGroupOverflow> MeshLODGroupOverflow => m_MeshLODGroupOverflow;

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

        public int MeshLODGroupCount => m_MeshLODGroups.Count;

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

        internal ulong ActiveDirectProgramBinMask
        {
            get
            {
                EnsureActiveMaterialBinsSynced();
                return m_ActiveDirectProgramBinMask;
            }
        }

        internal bool HasActiveOverflowProgramBin
        {
            get
            {
                EnsureActiveMaterialBinsSynced();
                return m_HasActiveOverflowProgramBin
                    || m_HasInstanceOverflowMaterial
                    || m_TerrainMaterials.Count > 0;
            }
        }

        internal int ActiveBinnedDispatchCount
        {
            get
            {
                EnsureActiveMaterialBinsSynced();
                bool hasOverflow = m_HasActiveOverflowProgramBin
                    || m_HasInstanceOverflowMaterial
                    || m_TerrainMaterials.Count > 0;
                return 1
                    + math.countbits(m_ActiveDirectProgramBinMask)
                    + (hasOverflow ? 1 : 0);
            }
        }

        internal void RefreshActiveMaterialBins()
        {
            m_ActiveDirectProgramBinMask = 0ul;
            m_HasActiveOverflowProgramBin = false;
            int headerCount = m_MaterialRuntimeHeaders.Count;
            for (int i = 0; i < headerCount; i++)
            {
                RecordActiveMaterialBin(m_MaterialRuntimeHeaders[i].ProgramID);
            }

            m_ActiveMaterialBinsSyncedHeaderCount = headerCount;
        }

        private void EnsureActiveMaterialBinsSynced()
        {
            if (m_ActiveMaterialBinsSyncedHeaderCount != m_MaterialRuntimeHeaders.Count)
                RefreshActiveMaterialBins();
        }

        private void RecordActiveMaterialBin(VividMaterialProgramID programID)
        {
            bool validProgram = programID != VividMaterialProgramID.Invalid
                && (uint)programID < (uint)m_MaterialPrograms.Count;
            int binIndex = VividMaterialBinningContract.MapProgramIdToBinIndex(
                (uint)programID,
                validProgram);
            if (binIndex == VividMaterialBinningContract.OverflowBinIndex)
            {
                m_HasActiveOverflowProgramBin = true;
            }
            else
            {
                m_ActiveDirectProgramBinMask |=
                    VividMaterialBinningContract.ComputeDirectProgramBinBit(binIndex);
            }
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
                && (program.SurfaceProgramID == VividMaterialSurfaceProgramID.DualSlab
                    || program.SurfaceProgramID == VividMaterialSurfaceProgramID.OpenPBROpaque);
        }

        internal bool RequiresSubstrateBuffer(uint materialIndex)
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
                && (program.SurfaceProgramID == VividMaterialSurfaceProgramID.OpenPBROpaque
                    || program.SurfaceProgramID == VividMaterialSurfaceProgramID.StandardSingleSlab
                    || program.SurfaceProgramID == VividMaterialSurfaceProgramID.DynamicSlab);
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

        internal bool HasSubstrateMaterial()
        {
            for (uint materialIndex = 0u;
                 materialIndex < (uint) m_MaterialRuntimeHeaders.Count;
                 materialIndex++)
            {
                if (RequiresSubstrateBuffer(materialIndex))
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

        internal List<VividMeshLODNodePacked> MutableMeshLODNodes => m_MeshLODNodes;

        internal List<VividMeshLODGroup> MutableMeshLODGroups => m_MeshLODGroups;
        internal List<VividMeshLODGroupPacked> MutablePackedMeshLODGroups => m_PackedMeshLODGroups;
        internal List<VividMeshLODGroupOverflow> MutableMeshLODGroupOverflow => m_MeshLODGroupOverflow;

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
            if ((instanceData.Flags & VividInstanceFlags.Disabled) == 0
                && instanceData.MaterialIndex >= (uint)m_MaterialRuntimeHeaders.Count)
            {
                m_HasInstanceOverflowMaterial = true;
            }

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
            m_HasInstanceOverflowMaterial = false;
        }

        internal void ClearMaterials()
        {
            VividMaterialProgramData[] runtimePrograms =
                GPUDrivenMaterialCompiler.CreateRuntimeProgramTable();
            m_Materials.Clear();
            m_ShadowMaterialResourceCounts.Clear();
            m_DualSlabMaterials.Clear();
            m_MaterialParameterLanes.Clear();
            m_MaterialResources.Clear();
            m_MaterialRuntimeHeaders.Clear();
            m_MaterialPrograms.Clear();
            m_MaterialPrograms.AddRange(runtimePrograms);
            m_DynamicSlabPrograms.Clear();
            m_ActiveDirectProgramBinMask = 0ul;
            m_HasActiveOverflowProgramBin = false;
            m_ActiveMaterialBinsSyncedHeaderCount = 0;
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
            int shadowResourceCount = 0;
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

                uint parameterLaneAddress = runtimeHeader.ParameterAddress;
                uint parameterLaneCount;
                uint resourceRecordCount;
                if (TryGetDynamicSlabProgram(
                        runtimeHeader.ProgramID,
                        out VividDynamicSlabProgramBinding dynamicBinding))
                {
                    parameterLaneCount = (uint)((dynamicBinding.ParameterStrideInWords + 3) / 4);
                    resourceRecordCount = (uint)dynamicBinding.ResourceCount;
                    shadowResourceCount = dynamicBinding.ResourceCount;
                }
                else
                {
                    MaterialProgramRuntimeBinding programBinding =
                        GPUDrivenMaterialCompiler.GetRuntimeProgramBinding(
                            runtimeHeader.ProgramID);
                    parameterLaneCount =
                        (uint) (programBinding.ParameterStrideInWords / 4);
                    resourceRecordCount = (uint) programBinding.ResourceCount;
                    shadowResourceCount = programBinding.ResourceCount;
                }
                if (parameterLaneAddress > (uint) m_MaterialParameterLanes.Count
                    || parameterLaneCount
                        > (uint) m_MaterialParameterLanes.Count - parameterLaneAddress)
                {
                    throw new ArgumentException(
                        $"Material program {programIndex} requires {parameterLaneCount} parameter lanes at address {parameterLaneAddress}, but only {m_MaterialParameterLanes.Count} lanes are present.",
                        nameof(runtimeHeader));
                }

                uint resourceBindingAddress = runtimeHeader.ResourceBindingAddress;
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
            m_ShadowMaterialResourceCounts.Add(shadowResourceCount);
            RecordActiveMaterialBin(runtimeHeader.ProgramID);
            m_ActiveMaterialBinsSyncedHeaderCount = m_MaterialRuntimeHeaders.Count;
            return materialIndex;
        }

        internal bool ShadowMaterialIntersectsVTChanges(int materialIndex, IReadOnlyList<Rect> regions)
        {
            if ((uint)materialIndex >= (uint)m_Materials.Count
                || (uint)materialIndex >= (uint)m_MaterialRuntimeHeaders.Count
                || (uint)materialIndex >= (uint)m_ShadowMaterialResourceCounts.Count)
                return true;
            VividMaterialRuntimeHeader header = m_MaterialRuntimeHeaders[materialIndex];
            if (header.ProgramID == VividMaterialProgramID.Invalid)
            {
                uint index = m_Materials[materialIndex].SurfaceBindingIndex;
                if (index >= (uint)m_SurfaceBindings.Count)
                    return true;
                VividSurfaceBindingData binding = m_SurfaceBindings[(int)index];
                return (binding.Flags & VividSurfaceBindingFlags.BaseColor) != 0
                    && VTRegionIntersects(binding.UVScaleBias, regions);
            }
            // Frozen AOT descriptors currently expose the material resource range,
            // not per-stage usage. Cover every record rather than assume resource 0.
            int count = m_ShadowMaterialResourceCounts[materialIndex];
            uint start = header.ResourceBindingAddress;
            if (start > (uint)m_MaterialResources.Count
                || (uint)count > (uint)m_MaterialResources.Count - start)
                return true;
            for (int i = 0; i < count; i++)
            {
                VividMaterialResourceData resource = m_MaterialResources[(int)start + i];
                if (resource.SurfaceBindingFlags != 0 && VTRegionIntersects(resource.UVScaleBias, regions))
                    return true;
            }
            return false;
        }

        internal static bool VTRegionIntersects(float4 scaleBias, IReadOnlyList<Rect> regions)
        {
            if (!math.all(math.isfinite(scaleBias)))
                return true;
            float2 min = scaleBias.zw;
            float2 max = min + math.abs(scaleBias.xy);
            for (int i = 0; i < regions.Count; i++)
            {
                Rect region = regions[i];
                // Inclusive edges cover clamp UV=1 and floating-point rounding.
                const float epsilon = 1e-6f;
                if (min.x <= region.xMax + epsilon && max.x >= region.xMin - epsilon
                    && min.y <= region.yMax + epsilon && max.y >= region.yMin - epsilon)
                    return true;
            }
            return false;
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
            m_MeshLODGroups.Clear();
            m_PackedMeshLODGroups.Clear();
            m_MeshLODGroupOverflow.Clear();
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
