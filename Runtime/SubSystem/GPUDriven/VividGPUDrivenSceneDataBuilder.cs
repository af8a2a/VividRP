using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Runtime.GPUDriven
{
    internal sealed class VividGPUDrivenSceneDataBuilder
    {
        private static readonly int s_BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_BaseMapPropertyId = Shader.PropertyToID("_BaseMap");
        private static readonly int s_MainTexPropertyId = Shader.PropertyToID("_MainTex");
        private static readonly int s_BumpMapPropertyId = Shader.PropertyToID("_BumpMap");
        private static readonly int s_BumpScalePropertyId = Shader.PropertyToID("_BumpScale");
        private static readonly int s_MetallicPropertyId = Shader.PropertyToID("_Metallic");
        private static readonly int s_SmoothnessPropertyId = Shader.PropertyToID("_Smoothness");
        private static readonly int s_MetallicRemapMinPropertyId = Shader.PropertyToID("_MetallicRemapMin");
        private static readonly int s_MetallicRemapMaxPropertyId = Shader.PropertyToID("_MetallicRemapMax");
        private static readonly int s_SmoothnessRemapMinPropertyId = Shader.PropertyToID("_SmoothnessRemapMin");
        private static readonly int s_SmoothnessRemapMaxPropertyId = Shader.PropertyToID("_SmoothnessRemapMax");
        private static readonly int s_AORemapMinPropertyId = Shader.PropertyToID("_AORemapMin");
        private static readonly int s_AORemapMaxPropertyId = Shader.PropertyToID("_AORemapMax");
        private static readonly int s_RMOMapPropertyId = Shader.PropertyToID("_RMOMap");
        private static readonly int s_MetallicGlossMapPropertyId = Shader.PropertyToID("_MetallicGlossMap");
        private static readonly int s_RoughnessMapPropertyId = Shader.PropertyToID("_RoughnessMap");
        private static readonly int s_MaskMapPropertyId = Shader.PropertyToID("_MaskMap");
        private static readonly int s_EmissionColorPropertyId = Shader.PropertyToID("_EmissionColor");
        private static readonly int s_AlphaClipPropertyId = Shader.PropertyToID("_AlphaClip");
        private static readonly int s_CutoffPropertyId = Shader.PropertyToID("_Cutoff");
        private static readonly int s_CullPropertyId = Shader.PropertyToID("_Cull");
        private const string SimpleForwardShaderName = "VividRP/Material/SimpleForward";
        private static readonly EntityIdComparer s_EntityIdComparer = new();
        private static readonly EntityIdSubMeshIndexComparer s_EntityIdSubMeshIndexComparer = new();

        private readonly Dictionary<EntityId, int> m_MaterialIndexByObjectId = new(s_EntityIdComparer);
        private readonly Dictionary<EntityId, MaterialMetadata> m_MaterialMetadataByObjectId = new(s_EntityIdComparer);
        private readonly Dictionary<EntityId, MeshletAssetMetadata> m_MeshMetadataByObjectId = new(s_EntityIdComparer);
        private readonly HashSet<EntityId> m_PreviousReferencedMeshletAssetIds = new(s_EntityIdComparer);
        private readonly HashSet<EntityId> m_CurrentReferencedMeshletAssetIds = new(s_EntityIdComparer);
        private readonly HashSet<EntityId> m_PreviousReferencedMaterialProxyIds = new(s_EntityIdComparer);
        private readonly HashSet<EntityId> m_CurrentReferencedMaterialProxyIds = new(s_EntityIdComparer);
        private readonly HashSet<EntityId> m_PreviousReferencedTerrainDataIds = new(s_EntityIdComparer);
        private readonly HashSet<EntityId> m_CurrentReferencedTerrainDataIds = new(s_EntityIdComparer);
        private readonly HashSet<(EntityId entityId, int subMeshIndex)> m_MissingProxyWarningKeys = new(s_EntityIdSubMeshIndexComparer);
        private readonly List<VividMeshletCollectionAsset> m_CurrentReferencedMeshletAssets = new();
        private readonly List<VividMeshletCollectionAsset> m_TrackedMeshletAssets = new();
        private readonly List<GPUDrivenMaterialProxy> m_CurrentReferencedMaterialProxies = new();
        private readonly List<GPUDrivenMaterialProxy> m_TrackedMaterialProxies = new();
        private readonly List<VividTerrainData> m_CurrentReferencedTerrainData = new();
        private readonly List<VividTerrainData> m_TrackedTerrainData = new();
        private readonly List<bool> m_RendererRenderability = new();
        private readonly List<VividInstanceData> m_PreviousInstanceData = new();
        private static readonly Dictionary<Shader, bool> s_SimpleForwardShaderMatchCache = new();
        private static Shader s_SimpleForwardShader;
        private static bool s_SimpleForwardShaderResolved;
        private bool m_HasBuiltStaticData;
        private bool m_UsesFallbackMaterials;
        private IGPUDrivenTextureBackend m_PreviousTextureBackend;
        private uint m_PreviousSurfaceBindingRevision;
        private uint m_PreviousDatabaseStructureRevision;
        private uint m_PreviousDatabaseResourceRevision;
        private uint m_PreviousDatabaseInstanceRevision;
        private uint m_PreviousMeshletAssetGlobalContentRevision;

        public bool Build(
            VividGPUDrivenSceneData sceneData,
            VividMeshletRendererDatabase database,
            IGPUDrivenTextureBackend textureBackend
        )
        {
            return Build(sceneData, database, textureBackend, out _);
        }

        public bool Build(
            VividGPUDrivenSceneData sceneData,
            VividMeshletRendererDatabase database,
            IGPUDrivenTextureBackend textureBackend,
            out bool materialDataChanged
        )
        {
            return Build(sceneData, database, textureBackend, out materialDataChanged, out _);
        }

        public bool Build(
            VividGPUDrivenSceneData sceneData,
            VividMeshletRendererDatabase database,
            IGPUDrivenTextureBackend textureBackend,
            out bool materialDataChanged,
            out bool instanceDataChanged
        )
        {
            if (sceneData == null)
            {
                throw new ArgumentNullException(nameof(sceneData));
            }

            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            if (textureBackend == null)
            {
                throw new ArgumentNullException(nameof(textureBackend));
            }

            if (CanSkipBuild(database, textureBackend))
            {
                materialDataChanged = false;
                instanceDataChanged = false;
                return false;
            }

            bool staticDataChanged = !m_HasBuiltStaticData;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenPrepareFrameBuildSceneDataCollectReferencesMarker.Auto())
            {
                UpdateRendererRenderability(database);
                CollectReferencedMeshletAssetIds(database);
                CollectReferencedMaterialProxyIds(database);
                CollectReferencedTerrainDataIds(database);
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenPrepareFrameBuildSceneDataDetectChangesMarker.Auto())
            {
                if (!staticDataChanged && !AreEntityIdSetsEqual(m_CurrentReferencedMeshletAssetIds, m_PreviousReferencedMeshletAssetIds))
                {
                    staticDataChanged = true;
                }

                if (!staticDataChanged && HasTrackedMeshletAssetVersionChanges(database))
                {
                    staticDataChanged = true;
                }

                materialDataChanged = staticDataChanged
                                      || m_UsesFallbackMaterials
                                      || !ReferenceEquals(m_PreviousTextureBackend, textureBackend);
                if (!materialDataChanged && !AreEntityIdSetsEqual(m_CurrentReferencedMaterialProxyIds, m_PreviousReferencedMaterialProxyIds))
                {
                    materialDataChanged = true;
                }

                if (!materialDataChanged && HasTrackedMaterialProxyVersionChanges(database, textureBackend))
                {
                    materialDataChanged = true;
                }

                if (!materialDataChanged && !AreEntityIdSetsEqual(m_CurrentReferencedTerrainDataIds, m_PreviousReferencedTerrainDataIds))
                {
                    materialDataChanged = true;
                }

                if (!materialDataChanged && HasTrackedTerrainMaterialVersionChanges(database))
                {
                    materialDataChanged = true;
                }

                if (!materialDataChanged && m_PreviousSurfaceBindingRevision != textureBackend.BindingRevision)
                {
                    materialDataChanged = true;
                }
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenPrepareFrameBuildSceneDataClearSceneMarker.Auto())
            {
                if (staticDataChanged)
                {
                    sceneData.Clear();
                    m_MeshMetadataByObjectId.Clear();
                    m_MaterialMetadataByObjectId.Clear();
                }
                else if (materialDataChanged)
                {
                    sceneData.ClearDynamic();
                    m_MaterialMetadataByObjectId.Clear();
                }
                else
                {
                    sceneData.ClearInstances();
                }

                m_MaterialIndexByObjectId.Clear();
            }

            IGPUDrivenTextureBindingLifecycle bindingLifecycle = materialDataChanged
                ? textureBackend as IGPUDrivenTextureBindingLifecycle
                : null;
            bindingLifecycle?.BeginSurfaceBindingUpdate();
            try
            {
                using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenPrepareFrameBuildSceneDataAppendRenderersMarker.Auto())
                {
                    IReadOnlyList<VividMeshletRendererRenderData> rendererData = database.rendererData;
                    IReadOnlyList<VividMeshletRendererResources> rendererResources = database.rendererResources;
                    int rendererCount = Mathf.Min(rendererData.Count, rendererResources.Count);

                    for (int rendererIndex = 0; rendererIndex < rendererCount; rendererIndex++)
                    {
                        if (!m_RendererRenderability[rendererIndex])
                        {
                            continue;
                        }

                        AppendRendererSceneData(
                            sceneData,
                            rendererData[rendererIndex],
                            rendererResources[rendererIndex],
                            textureBackend
                        );
                    }
                }

                bindingLifecycle?.EndSurfaceBindingUpdate();
            }
            catch
            {
                bindingLifecycle?.CancelSurfaceBindingUpdate();
                throw;
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenPrepareFrameBuildSceneDataInstanceDiffMarker.Auto())
            {
                instanceDataChanged = !AreInstanceDataListsEqual(sceneData.MutableInstances, m_PreviousInstanceData);
                UpdatePreviousInstanceData(sceneData.MutableInstances);
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenPrepareFrameBuildSceneDataSwapReferencesMarker.Auto())
            {
                SwapReferencedMeshletAssetIds();
                SwapReferencedMaterialProxyIds();
                SwapReferencedTerrainDataIds();
                UpdateTrackedDependencies();
            }
            m_HasBuiltStaticData = true;
            m_PreviousTextureBackend = textureBackend;
            m_PreviousSurfaceBindingRevision = textureBackend.BindingRevision;
            m_PreviousDatabaseStructureRevision = database.StructureRevision;
            m_PreviousDatabaseResourceRevision = database.ResourceRevision;
            m_PreviousDatabaseInstanceRevision = database.InstanceRevision;
            m_PreviousMeshletAssetGlobalContentRevision = VividMeshletCollectionAsset.GlobalContentRevision;
            return staticDataChanged;
        }

        private bool CanSkipBuild(
            VividMeshletRendererDatabase database,
            IGPUDrivenTextureBackend textureBackend)
        {
            if (!m_HasBuiltStaticData
                || m_UsesFallbackMaterials
                || !ReferenceEquals(m_PreviousTextureBackend, textureBackend)
                || m_PreviousDatabaseStructureRevision != database.StructureRevision
                || m_PreviousDatabaseResourceRevision != database.ResourceRevision
                || m_PreviousDatabaseInstanceRevision != database.InstanceRevision
                || m_PreviousSurfaceBindingRevision != textureBackend.BindingRevision)
            {
                return false;
            }

            return !HaveTrackedMeshletAssetsChanged()
                && !HaveTrackedMaterialProxiesChanged(textureBackend)
                && !HaveTrackedTerrainDataChanged();
        }

        private bool HaveTrackedMeshletAssetsChanged()
        {
            uint globalContentRevision = VividMeshletCollectionAsset.GlobalContentRevision;
            if (m_PreviousMeshletAssetGlobalContentRevision == globalContentRevision)
            {
                return false;
            }

            for (int assetIndex = 0; assetIndex < m_TrackedMeshletAssets.Count; assetIndex++)
            {
                VividMeshletCollectionAsset asset = m_TrackedMeshletAssets[assetIndex];
                if (asset == null
                    || !m_MeshMetadataByObjectId.TryGetValue(asset.GetEntityId(), out MeshletAssetMetadata metadata)
                    || metadata.AssetVersion != asset.ContentVersion)
                {
                    return true;
                }
            }

            m_PreviousMeshletAssetGlobalContentRevision = globalContentRevision;
            return false;
        }

        private bool HaveTrackedMaterialProxiesChanged(
            IGPUDrivenTextureBackend textureBackend)
        {
            for (int proxyIndex = 0; proxyIndex < m_TrackedMaterialProxies.Count; proxyIndex++)
            {
                GPUDrivenMaterialProxy materialProxy = m_TrackedMaterialProxies[proxyIndex];
                if (materialProxy == null
                    || !m_MaterialMetadataByObjectId.TryGetValue(materialProxy.GetEntityId(), out MaterialMetadata metadata)
                    || metadata.Revision != ComputeMaterialProxyRevision(materialProxy, textureBackend))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HaveTrackedTerrainDataChanged()
        {
            for (int terrainIndex = 0; terrainIndex < m_TrackedTerrainData.Count; terrainIndex++)
            {
                VividTerrainData terrainData = m_TrackedTerrainData[terrainIndex];
                if (terrainData == null
                    || !m_MaterialMetadataByObjectId.TryGetValue(terrainData.GetEntityId(), out MaterialMetadata metadata)
                    || metadata.Revision != ComputeTerrainMaterialRevision(terrainData))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AreEntityIdSetsEqual(HashSet<EntityId> current, HashSet<EntityId> previous)
        {
            if (ReferenceEquals(current, previous))
            {
                return true;
            }

            if (current == null || previous == null || current.Count != previous.Count)
            {
                return false;
            }

            foreach (EntityId entityId in current)
            {
                if (!previous.Contains(entityId))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AreInstanceDataListsEqual(
            List<VividInstanceData> currentInstanceData,
            List<VividInstanceData> previousInstanceData
        )
        {
            if (ReferenceEquals(currentInstanceData, previousInstanceData))
            {
                return true;
            }

            if (currentInstanceData == null || previousInstanceData == null || currentInstanceData.Count != previousInstanceData.Count)
            {
                return false;
            }

            for (int index = 0; index < currentInstanceData.Count; index++)
            {
                if (!InstanceDataEquals(currentInstanceData[index], previousInstanceData[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private void UpdatePreviousInstanceData(List<VividInstanceData> currentInstanceData)
        {
            m_PreviousInstanceData.Clear();
            if (currentInstanceData is { Count: > 0 })
            {
                m_PreviousInstanceData.AddRange(currentInstanceData);
            }
        }

        private static bool InstanceDataEquals(in VividInstanceData lhs, in VividInstanceData rhs)
        {
            return Float4x4Equals(lhs.ObjectToWorldMatrix, rhs.ObjectToWorldMatrix)
                   && Float4x4Equals(lhs.WorldToObjectMatrix, rhs.WorldToObjectMatrix)
                   && Float4Equals(lhs.AABBMin, rhs.AABBMin)
                   && Float4Equals(lhs.AABBMax, rhs.AABBMax)
                   && lhs.TopMeshLODStartIndex == rhs.TopMeshLODStartIndex
                   && lhs.TotalMeshLODCount == rhs.TotalMeshLODCount
                   && lhs.MaterialIndex == rhs.MaterialIndex
                   && lhs.MeshLODLevelCount == rhs.MeshLODLevelCount
                   && lhs.LODErrorScale == rhs.LODErrorScale
                   && lhs.PassMask == rhs.PassMask
                   && lhs.Flags == rhs.Flags
                   && lhs.Padding0 == rhs.Padding0;
        }

        private static bool Float4x4Equals(in float4x4 lhs, in float4x4 rhs)
        {
            return Float4Equals(lhs.c0, rhs.c0)
                   && Float4Equals(lhs.c1, rhs.c1)
                   && Float4Equals(lhs.c2, rhs.c2)
                   && Float4Equals(lhs.c3, rhs.c3);
        }

        private static bool Float4Equals(in float4 lhs, in float4 rhs)
        {
            return lhs.x == rhs.x
                   && lhs.y == rhs.y
                   && lhs.z == rhs.z
                   && lhs.w == rhs.w;
        }

        private void CollectReferencedMeshletAssetIds(VividMeshletRendererDatabase database)
        {
            m_CurrentReferencedMeshletAssetIds.Clear();
            m_CurrentReferencedMeshletAssets.Clear();

            IReadOnlyList<VividMeshletRendererResources> rendererResources = database.rendererResources;
            int rendererCount = m_RendererRenderability.Count;

            for (int rendererIndex = 0; rendererIndex < rendererCount; rendererIndex++)
            {
                if (!m_RendererRenderability[rendererIndex])
                {
                    continue;
                }

                VividMeshletCollectionAsset[] meshletCollections = rendererResources[rendererIndex].MeshletCollections;
                for (int subMeshIndex = 0; subMeshIndex < meshletCollections.Length; subMeshIndex++)
                {
                    VividMeshletCollectionAsset meshletCollection = meshletCollections[subMeshIndex];
                    if (meshletCollection == null)
                    {
                        continue;
                    }

                    if (m_CurrentReferencedMeshletAssetIds.Add(meshletCollection.GetEntityId()))
                        m_CurrentReferencedMeshletAssets.Add(meshletCollection);
                }
            }
        }

        private void CollectReferencedMaterialProxyIds(VividMeshletRendererDatabase database)
        {
            m_CurrentReferencedMaterialProxyIds.Clear();
            m_CurrentReferencedMaterialProxies.Clear();
            m_UsesFallbackMaterials = false;

            IReadOnlyList<VividMeshletRendererResources> rendererResources = database.rendererResources;
            int rendererCount = m_RendererRenderability.Count;

            for (int rendererIndex = 0; rendererIndex < rendererCount; rendererIndex++)
            {
                VividMeshletRendererResources resources = rendererResources[rendererIndex];
                if (resources.IsTerrain || !m_RendererRenderability[rendererIndex])
                {
                    continue;
                }

                VividMeshletCollectionAsset[] meshletCollections = resources.MeshletCollections;
                for (int subMeshIndex = 0; subMeshIndex < meshletCollections.Length; subMeshIndex++)
                {
                    if (meshletCollections[subMeshIndex] == null)
                    {
                        continue;
                    }

                    GPUDrivenMaterialProxy materialProxy = GetMaterialProxyForSubMesh(resources.MaterialProxies, subMeshIndex);
                    if (materialProxy != null)
                    {
                        if (m_CurrentReferencedMaterialProxyIds.Add(materialProxy.GetEntityId()))
                            m_CurrentReferencedMaterialProxies.Add(materialProxy);
                    }
                    else
                    {
                        m_UsesFallbackMaterials = true;
                    }
                }
            }
        }

        private void SwapReferencedMeshletAssetIds()
        {
            m_PreviousReferencedMeshletAssetIds.Clear();
            foreach (EntityId assetId in m_CurrentReferencedMeshletAssetIds)
            {
                m_PreviousReferencedMeshletAssetIds.Add(assetId);
            }
        }

        private void SwapReferencedMaterialProxyIds()
        {
            m_PreviousReferencedMaterialProxyIds.Clear();
            foreach (EntityId proxyId in m_CurrentReferencedMaterialProxyIds)
            {
                m_PreviousReferencedMaterialProxyIds.Add(proxyId);
            }
        }

        private void CollectReferencedTerrainDataIds(VividMeshletRendererDatabase database)
        {
            m_CurrentReferencedTerrainDataIds.Clear();
            m_CurrentReferencedTerrainData.Clear();

            IReadOnlyList<VividMeshletRendererResources> rendererResources = database.rendererResources;
            int rendererCount = m_RendererRenderability.Count;
            for (int rendererIndex = 0; rendererIndex < rendererCount; rendererIndex++)
            {
                VividMeshletRendererResources resources = rendererResources[rendererIndex];
                if (!resources.IsTerrain || !m_RendererRenderability[rendererIndex])
                {
                    continue;
                }

                VividTerrainData terrainData = resources.TerrainData;
                if (terrainData != null)
                {
                    if (m_CurrentReferencedTerrainDataIds.Add(terrainData.GetEntityId()))
                        m_CurrentReferencedTerrainData.Add(terrainData);
                    if (terrainData.Layers.Count == 0)
                    {
                        m_UsesFallbackMaterials = true;
                    }
                }
            }
        }

        private void UpdateTrackedDependencies()
        {
            m_TrackedMeshletAssets.Clear();
            m_TrackedMeshletAssets.AddRange(m_CurrentReferencedMeshletAssets);
            m_TrackedMaterialProxies.Clear();
            m_TrackedMaterialProxies.AddRange(m_CurrentReferencedMaterialProxies);
            m_TrackedTerrainData.Clear();
            m_TrackedTerrainData.AddRange(m_CurrentReferencedTerrainData);
        }

        private void SwapReferencedTerrainDataIds()
        {
            m_PreviousReferencedTerrainDataIds.Clear();
            foreach (EntityId terrainDataId in m_CurrentReferencedTerrainDataIds)
            {
                m_PreviousReferencedTerrainDataIds.Add(terrainDataId);
            }
        }

        private bool HasTrackedMeshletAssetVersionChanges(VividMeshletRendererDatabase database)
        {
            IReadOnlyList<VividMeshletRendererResources> rendererResources = database.rendererResources;
            int rendererCount = m_RendererRenderability.Count;

            for (int rendererIndex = 0; rendererIndex < rendererCount; rendererIndex++)
            {
                if (!m_RendererRenderability[rendererIndex])
                {
                    continue;
                }

                VividMeshletCollectionAsset[] meshletCollections = rendererResources[rendererIndex].MeshletCollections;
                for (int subMeshIndex = 0; subMeshIndex < meshletCollections.Length; subMeshIndex++)
                {
                    VividMeshletCollectionAsset meshletCollection = meshletCollections[subMeshIndex];
                    if (meshletCollection == null)
                    {
                        continue;
                    }

                    EntityId assetId = meshletCollection.GetEntityId();
                    if (!m_MeshMetadataByObjectId.TryGetValue(assetId, out MeshletAssetMetadata metadata) ||
                        metadata.AssetVersion != meshletCollection.ContentVersion)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool HasTrackedMaterialProxyVersionChanges(
            VividMeshletRendererDatabase database,
            IGPUDrivenTextureBackend textureBackend)
        {
            IReadOnlyList<VividMeshletRendererResources> rendererResources = database.rendererResources;
            int rendererCount = m_RendererRenderability.Count;

            for (int rendererIndex = 0; rendererIndex < rendererCount; rendererIndex++)
            {
                VividMeshletRendererResources resources = rendererResources[rendererIndex];
                if (resources.IsTerrain || !m_RendererRenderability[rendererIndex])
                {
                    continue;
                }

                VividMeshletCollectionAsset[] meshletCollections = resources.MeshletCollections;
                for (int subMeshIndex = 0; subMeshIndex < meshletCollections.Length; subMeshIndex++)
                {
                    if (meshletCollections[subMeshIndex] == null)
                    {
                        continue;
                    }

                    GPUDrivenMaterialProxy materialProxy = GetMaterialProxyForSubMesh(resources.MaterialProxies, subMeshIndex);
                    if (materialProxy == null)
                    {
                        continue;
                    }

                    EntityId materialProxyId = materialProxy.GetEntityId();
                    if (!m_MaterialMetadataByObjectId.TryGetValue(materialProxyId, out MaterialMetadata metadata) ||
                        metadata.Revision != ComputeMaterialProxyRevision(materialProxy, textureBackend))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool HasTrackedTerrainMaterialVersionChanges(VividMeshletRendererDatabase database)
        {
            IReadOnlyList<VividMeshletRendererResources> rendererResources = database.rendererResources;
            int rendererCount = m_RendererRenderability.Count;

            for (int rendererIndex = 0; rendererIndex < rendererCount; rendererIndex++)
            {
                VividMeshletRendererResources resources = rendererResources[rendererIndex];
                if (!resources.IsTerrain || !m_RendererRenderability[rendererIndex])
                {
                    continue;
                }

                VividTerrainData terrainData = resources.TerrainData;
                if (terrainData == null)
                {
                    continue;
                }

                EntityId terrainDataId = terrainData.GetEntityId();
                uint revision = ComputeTerrainMaterialRevision(terrainData);
                if (!m_MaterialMetadataByObjectId.TryGetValue(terrainDataId, out MaterialMetadata metadata)
                    || metadata.Revision != revision)
                {
                    return true;
                }
            }

            return false;
        }

        private void AppendRendererSceneData(
            VividGPUDrivenSceneData sceneData,
            in VividMeshletRendererRenderData trackedData,
            in VividMeshletRendererResources trackedResources,
            IGPUDrivenTextureBackend textureBackend
        )
        {
            int subMeshCount = trackedResources.MeshletCollections.Length;
            for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
            {
                VividMeshletCollectionAsset meshletCollection = trackedResources.MeshletCollections[subMeshIndex];
                if (meshletCollection == null)
                {
                    continue;
                }

                MeshletAssetMetadata meshMetadata = GetOrAppendMeshletAsset(sceneData, meshletCollection);
                Material material = GetMaterialForSubMesh(trackedResources.SharedMaterials, subMeshIndex);
                GPUDrivenMaterialProxy materialProxy = GetMaterialProxyForSubMesh(trackedResources.MaterialProxies, subMeshIndex);
                int materialIndex = trackedResources.IsTerrain
                    ? GetOrAppendTerrainMaterial(
                        sceneData,
                        trackedResources.Terrain,
                        trackedResources.TerrainData,
                        material,
                        textureBackend)
                    : GetOrAppendMaterial(
                        sceneData,
                        trackedResources.MeshletRenderer,
                        materialProxy,
                        material,
                        subMeshIndex,
                        textureBackend
                    );
                Bounds localBounds = trackedResources.LocalBounds != null
                                     && subMeshIndex < trackedResources.LocalBounds.Length
                    ? trackedResources.LocalBounds[subMeshIndex]
                    : trackedData.localBounds;

                VividInstanceData instanceData = CreateInstanceData(
                    trackedData,
                    materialIndex,
                    meshMetadata,
                    localBounds);
                sceneData.AddInstance(
                    instanceData,
                    CreateInstanceSourceData(
                        trackedData,
                        trackedResources,
                        meshletCollection,
                        materialProxy,
                        material,
                        subMeshIndex),
                    meshMetadata.MaxVisibleMeshletRenderRequestCount);
            }
        }

        private static VividGPUDrivenInstanceSourceData CreateInstanceSourceData(
            in VividMeshletRendererRenderData trackedData,
            in VividMeshletRendererResources trackedResources,
            VividMeshletCollectionAsset meshletCollection,
            GPUDrivenMaterialProxy materialProxy,
            Material material,
            int sourceSectionIndex)
        {
            VividGPUDrivenInstanceSourceFlags flags = VividGPUDrivenInstanceSourceFlags.None;
            EntityId materialEntityId;
            if (trackedResources.IsTerrain)
            {
                flags |= VividGPUDrivenInstanceSourceFlags.TerrainGeometry
                    | VividGPUDrivenInstanceSourceFlags.TerrainMaterial;
                materialEntityId = trackedData.meshletRendererEntityId;
            }
            else if (materialProxy != null)
            {
                flags |= VividGPUDrivenInstanceSourceFlags.MaterialProxy;
                materialEntityId = materialProxy.GetEntityId();
            }
            else if (material != null)
            {
                materialEntityId = material.GetEntityId();
            }
            else
            {
                flags |= VividGPUDrivenInstanceSourceFlags.MissingMaterial;
                materialEntityId = EntityId.None;
            }

            return new VividGPUDrivenInstanceSourceData(
                trackedData.meshletRendererEntityId,
                meshletCollection != null ? meshletCollection.GetEntityId() : EntityId.None,
                materialEntityId,
                sourceSectionIndex,
                flags);
        }

        private void UpdateRendererRenderability(VividMeshletRendererDatabase database)
        {
            m_RendererRenderability.Clear();

            IReadOnlyList<VividMeshletRendererRenderData> rendererData = database.rendererData;
            IReadOnlyList<VividMeshletRendererResources> rendererResources = database.rendererResources;
            int rendererCount = Mathf.Min(rendererData.Count, rendererResources.Count);
            for (int rendererIndex = 0; rendererIndex < rendererCount; rendererIndex++)
            {
                m_RendererRenderability.Add(IsRenderable(rendererData[rendererIndex], rendererResources[rendererIndex]));
            }
        }

        private static bool IsRenderable(
            in VividMeshletRendererRenderData trackedData,
            in VividMeshletRendererResources trackedResources
        )
        {
            if ((trackedData.flags & VividMeshletRendererFlags.Valid) == 0)
            {
                return false;
            }

            if (trackedResources.SourceMesh == null && !trackedResources.IsTerrain)
            {
                return false;
            }

            if (trackedResources.MeshletCollections == null || trackedResources.MeshletCollections.Length == 0)
            {
                return false;
            }

            if (trackedResources.IsTerrain)
            {
                bool hasGeometry = false;
                int maximumLODLevelCount = trackedResources.TerrainData != null
                    ? trackedResources.TerrainData.BakeSettings.MaxMeshLODLevelCount
                    : VividTerrainBakeSettings.LegacyMaxMeshLODLevelCount;
                for (int chunkIndex = 0; chunkIndex < trackedResources.MeshletCollections.Length; chunkIndex++)
                {
                    VividMeshletCollectionAsset chunkGeometry = trackedResources.MeshletCollections[chunkIndex];
                    if (chunkGeometry == null)
                    {
                        continue;
                    }

                    if (chunkGeometry.MeshLODLevelCount < VividTerrainData.MinimumChunkLODCount
                        || chunkGeometry.MeshLODLevelCount > maximumLODLevelCount)
                    {
                        return false;
                    }

                    hasGeometry = true;
                }

                return hasGeometry;
            }

            return true;
        }

        private MeshletAssetMetadata GetOrAppendMeshletAsset(
            VividGPUDrivenSceneData sceneData,
            VividMeshletCollectionAsset meshletCollection
        )
        {
            EntityId objectId = meshletCollection.GetEntityId();
            if (m_MeshMetadataByObjectId.TryGetValue(objectId, out MeshletAssetMetadata metadata))
            {
                return metadata;
            }

            uint meshletBaseOffset = (uint) sceneData.MeshletCount;
            uint vertexBaseOffset = (uint) sceneData.VertexCount;
            uint indexBaseOffset = (uint) sceneData.IndexCount;
            uint meshLODStartIndex = (uint) sceneData.MeshLODNodeCount;

            VividMeshlet[] sourceMeshlets = meshletCollection.Meshlets ?? Array.Empty<VividMeshlet>();
            for (int meshletIndex = 0; meshletIndex < sourceMeshlets.Length; meshletIndex++)
            {
                VividMeshlet meshlet = sourceMeshlets[meshletIndex];
                meshlet.VertexOffset += vertexBaseOffset;
                meshlet.TriangleOffset += indexBaseOffset;
                sceneData.MutableMeshlets.Add(meshlet);
            }

            VividMeshLODNode[] sourceMeshLODNodes = meshletCollection.MeshLODNodes ?? Array.Empty<VividMeshLODNode>();
            int maxVisibleMeshletRenderRequestCount = 0;
            for (int nodeIndex = 0; nodeIndex < sourceMeshLODNodes.Length; nodeIndex++)
            {
                VividMeshLODNode node = sourceMeshLODNodes[nodeIndex];
                node.MeshletStartIndex += meshletBaseOffset;
                sceneData.MutableMeshLODNodes.Add(node);
                maxVisibleMeshletRenderRequestCount = (int) Math.Min(
                    int.MaxValue,
                    (ulong) maxVisibleMeshletRenderRequestCount + node.MeshletCount);
            }

            sceneData.MutableVertices.AddRange(meshletCollection.VertexBuffer ?? Array.Empty<VividMeshletVertex>());
            sceneData.MutableIndices.AddRange(meshletCollection.IndexBuffer ?? Array.Empty<byte>());

            metadata = new MeshletAssetMetadata(
                meshLODStartIndex,
                (uint) sourceMeshLODNodes.Length,
                (uint) Mathf.Max(1, meshletCollection.MeshLODLevelCount),
                meshletCollection.ContentVersion,
                maxVisibleMeshletRenderRequestCount
            );

            m_MeshMetadataByObjectId[objectId] = metadata;
            return metadata;
        }

        private int GetOrAppendTerrainMaterial(
            VividGPUDrivenSceneData sceneData,
            VividTerrain terrain,
            VividTerrainData terrainData,
            Material sourceMaterial,
            IGPUDrivenTextureBackend textureBackend
        )
        {
            uint terrainRevision = ComputeTerrainMaterialRevision(terrainData);
            uint terrainRVTRecordIndex = 0u;
            bool usesTerrainRVT = terrainData != null
                                  && textureBackend is IGPUDrivenTerrainRuntimeVirtualTextureBackend terrainRVTBackend
                                  && terrainRVTBackend.TryGetOrCreateTerrainRuntimeVirtualTexture(
                                      terrain,
                                      terrainData,
                                      terrainRevision,
                                      out terrainRVTRecordIndex);
            EntityId objectId = usesTerrainRVT && terrain != null
                ? terrain.GetEntityId()
                : terrainData != null
                    ? terrainData.GetEntityId()
                    : EntityId.None;
            if (m_MaterialIndexByObjectId.TryGetValue(objectId, out int materialIndex))
            {
                return materialIndex;
            }

            if (terrainData != null
                && m_MaterialMetadataByObjectId.TryGetValue(objectId, out MaterialMetadata metadata))
            {
                materialIndex = metadata.MaterialIndex;
                m_MaterialIndexByObjectId.Add(objectId, materialIndex);
                return materialIndex;
            }

            uint surfaceBindingIndex = (uint) sceneData.SurfaceBindingCount;
            VividMaterialData materialData;
            if (terrainData != null && terrainData.Layers.Count > 0)
            {
                int supportedLayerCount = terrainData.SupportedSurfaceLayerCount;
                VividVirtualTextureAsset compositeVirtualTexture = terrainData.CompositeVirtualTexture;
                bool usesComposite = supportedLayerCount > 1
                                     && compositeVirtualTexture != null
                                     && textureBackend.CanUseStreamedVirtualTexture(compositeVirtualTexture);
                if (usesComposite)
                {
                    surfaceBindingIndex = AppendSurfaceBinding(
                        sceneData,
                        textureBackend,
                        new GPUDrivenSurfaceTextureSet(
                            compositeVirtualTexture,
                            null,
                            null,
                            null,
                            GPUDrivenMaterialMaskMode.PackedMetallicOcclusionSmoothness));
                    materialData = CreateCompositeTerrainMaterialData(
                        surfaceBindingIndex,
                        usesTerrainRVT,
                        terrainRVTRecordIndex);
                }
                else
                {
                    uint terrainMaterialIndex = VividSurfaceBindingData.InvalidResource;
                    bool usesLayerBlend = supportedLayerCount > 1;
                    for (int layerIndex = 0; layerIndex < supportedLayerCount; layerIndex++)
                    {
                        VividTerrainLayerData layer = terrainData.Layers[layerIndex];
                        uint layerSurfaceBindingIndex = AppendSurfaceBinding(
                            sceneData,
                            textureBackend,
                            new GPUDrivenSurfaceTextureSet(
                                layer.StreamedVirtualTexture,
                                layer.DiffuseTexture,
                                layer.NormalMapTexture,
                                layer.MaskMapTexture,
                                layer.MaskMapTexture != null
                                    ? GPUDrivenMaterialMaskMode.PackedMetallicOcclusionSmoothness
                                    : GPUDrivenMaterialMaskMode.None)
                        );
                        if (usesLayerBlend)
                        {
                            sceneData.MutableTerrainLayers.Add(CreateTerrainLayerGPUData(
                                terrainData,
                                layer,
                                layerSurfaceBindingIndex));
                        }
                    }

                    if (usesLayerBlend)
                    {
                        uint controlBindingIndex0 = AppendTerrainControlBinding(
                            sceneData,
                            textureBackend,
                            terrainData,
                            0);
                        uint controlBindingIndex1 = supportedLayerCount > 4
                            ? AppendTerrainControlBinding(sceneData, textureBackend, terrainData, 1)
                            : VividSurfaceBindingData.InvalidResource;
                        terrainMaterialIndex = (uint) sceneData.TerrainMaterialCount;
                        sceneData.MutableTerrainMaterials.Add(new VividTerrainMaterialData
                        {
                            LayerStartIndex = (uint) (sceneData.TerrainLayerCount - supportedLayerCount),
                            LayerCount = (uint) supportedLayerCount,
                            ControlBindingIndex0 = controlBindingIndex0,
                            ControlBindingIndex1 = controlBindingIndex1,
                        });
                    }

                    materialData = CreateTerrainMaterialData(
                        terrainData,
                        terrainData.Layers[0],
                        surfaceBindingIndex,
                        terrainMaterialIndex);
                }
            }
            else
            {
                GPUDrivenSurfaceTextureSet surfaceTextures = ExtractSurfaceTextures(sourceMaterial);
                materialData = CreateMaterialData(sourceMaterial, surfaceBindingIndex);
                sceneData.MutableSurfaceBindings.Add(textureBackend.CreateSurfaceBinding(surfaceTextures));
            }

            materialIndex = sceneData.AddLegacyMaterial(materialData);
            m_MaterialIndexByObjectId.Add(objectId, materialIndex);
            if (terrainData != null)
            {
                m_MaterialMetadataByObjectId[objectId] = new MaterialMetadata(
                    materialIndex,
                    terrainRevision
                );
            }
            return materialIndex;
        }

        private static uint ComputeTerrainMaterialRevision(VividTerrainData terrainData)
        {
            if (terrainData == null)
            {
                return 0u;
            }

            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint) terrainData.Layers.Count) * 16777619u;
                hash = (hash ^ (uint) terrainData.Size.GetHashCode()) * 16777619u;
                hash = (hash ^ GetObjectRevisionId(terrainData.SourceMaterial)) * 16777619u;
                VividVirtualTextureAsset compositeVirtualTexture = terrainData.CompositeVirtualTexture;
                hash = (hash ^ GetObjectRevisionId(compositeVirtualTexture)) * 16777619u;
                hash = (hash ^ (compositeVirtualTexture != null
                    ? compositeVirtualTexture.ContentVersion
                    : 0u)) * 16777619u;
                int supportedLayerCount = terrainData.SupportedSurfaceLayerCount;
                if (supportedLayerCount == 0)
                {
                    return hash;
                }

                for (int layerIndex = 0; layerIndex < supportedLayerCount; layerIndex++)
                {
                    VividTerrainLayerData layer = terrainData.Layers[layerIndex];
                    hash = (hash ^ GetObjectRevisionId(layer.DiffuseTexture)) * 16777619u;
                    hash = (hash ^ GetObjectRevisionId(layer.NormalMapTexture)) * 16777619u;
                    hash = (hash ^ GetObjectRevisionId(layer.MaskMapTexture)) * 16777619u;
                    hash = (hash ^ GetObjectRevisionId(layer.StreamedVirtualTexture)) * 16777619u;
                    hash = (hash ^ (layer.StreamedVirtualTexture != null
                        ? layer.StreamedVirtualTexture.ContentVersion
                        : 0u)) * 16777619u;
                    hash = (hash ^ (uint) layer.TileSize.GetHashCode()) * 16777619u;
                    hash = (hash ^ (uint) layer.TileOffset.GetHashCode()) * 16777619u;
                    hash = (hash ^ (uint) layer.Metallic.GetHashCode()) * 16777619u;
                    hash = (hash ^ (uint) layer.Smoothness.GetHashCode()) * 16777619u;
                    hash = (hash ^ (uint) layer.NormalScale.GetHashCode()) * 16777619u;
                }

                hash = (hash ^ (uint) terrainData.ControlMaps.Count) * 16777619u;
                int controlMapCount = Mathf.Min(
                    terrainData.ControlMaps.Count,
                    VividTerrainData.MaximumControlMapCount);
                for (int controlMapIndex = 0; controlMapIndex < controlMapCount; controlMapIndex++)
                {
                    hash = (hash ^ GetObjectRevisionId(terrainData.ControlMaps[controlMapIndex])) * 16777619u;
                    VividVirtualTextureAsset controlVirtualTexture = controlMapIndex < terrainData.ControlVirtualTextures.Count
                        ? terrainData.ControlVirtualTextures[controlMapIndex]
                        : null;
                    hash = (hash ^ GetObjectRevisionId(controlVirtualTexture)) * 16777619u;
                    hash = (hash ^ (controlVirtualTexture != null
                        ? controlVirtualTexture.ContentVersion
                        : 0u)) * 16777619u;
                }
                return hash;
            }
        }

        private static uint GetObjectRevisionId(UnityEngine.Object target)
        {
            if (target == null)
            {
                return 0u;
            }

            ulong entityId = EntityId.ToULong(target.GetEntityId());
            return (uint) (entityId ^ (entityId >> 32));
        }

        private int GetOrAppendMaterial(
            VividGPUDrivenSceneData sceneData,
            MeshletRenderer meshletRenderer,
            GPUDrivenMaterialProxy materialProxy,
            Material material,
            int subMeshIndex,
            IGPUDrivenTextureBackend textureBackend
        )
        {
            EntityId objectId = materialProxy != null
                ? materialProxy.GetEntityId()
                : material != null
                    ? material.GetEntityId()
                    : EntityId.None;
            if (m_MaterialIndexByObjectId.TryGetValue(objectId, out int materialIndex))
            {
                return materialIndex;
            }

            if (materialProxy != null &&
                m_MaterialMetadataByObjectId.TryGetValue(objectId, out MaterialMetadata metadata))
            {
                materialIndex = metadata.MaterialIndex;
                m_MaterialIndexByObjectId.Add(objectId, materialIndex);
                return materialIndex;
            }

            GPUDrivenSurfaceTextureSet surfaceTextures;
            VividMaterialData materialData;
            VividMaterialRuntimeHeader runtimeHeader;
            uint surfaceBindingIndex = (uint) sceneData.SurfaceBindingCount;
            if (materialProxy != null)
            {
                surfaceTextures = ExtractSurfaceTextures(materialProxy, textureBackend);
                GPUDrivenCompiledMaterialInstance compiledMaterial =
                    GPUDrivenMaterialCompiler.CompileStandardSingleSlab(
                        materialProxy,
                        (uint) sceneData.MaterialCount,
                        surfaceBindingIndex);
                materialData = compiledMaterial.LegacyMaterialData;
                runtimeHeader = compiledMaterial.RuntimeHeader;
            }
            else
            {
                WarnMissingMaterialProxy(meshletRenderer, material, subMeshIndex);
                surfaceTextures = ExtractSurfaceTextures(material);
                materialData = CreateMaterialData(material, surfaceBindingIndex);
                runtimeHeader = GPUDrivenMaterialCompiler.CreateLegacyFallbackHeader(
                    (uint) sceneData.MaterialCount,
                    surfaceBindingIndex);
            }

            sceneData.MutableSurfaceBindings.Add(textureBackend.CreateSurfaceBinding(surfaceTextures));
            materialIndex = sceneData.AddMaterial(materialData, runtimeHeader);
            m_MaterialIndexByObjectId.Add(objectId, materialIndex);
            if (materialProxy != null)
            {
                m_MaterialMetadataByObjectId[objectId] = new MaterialMetadata(
                    materialIndex,
                    ComputeMaterialProxyRevision(materialProxy, textureBackend));
            }
            return materialIndex;
        }

        private void WarnMissingMaterialProxy(
            MeshletRenderer meshletRenderer,
            Material material,
            int subMeshIndex
        )
        {
            var warningKey = material != null
                ? (material.GetEntityId(), -1)
                : (meshletRenderer != null ? meshletRenderer.GetEntityId() : EntityId.None, subMeshIndex);

            if (!m_MissingProxyWarningKeys.Add(warningKey))
            {
                return;
            }

            string rendererName = meshletRenderer != null ? meshletRenderer.name : "<unknown>";
            string materialName = material != null ? material.name : "<null>";
            Debug.LogWarning(
                $"[VividRP] MeshletRenderer '{rendererName}' submesh {subMeshIndex} is missing a GPUDriven material proxy. Falling back to source Material '{materialName}'.",
                meshletRenderer
            );
        }

        private static VividMaterialData CreateMaterialData(
            Material material,
            uint surfaceBindingIndex
        )
        {
            return new VividMaterialData
            {
                AlbedoColor = GetColor(material, s_BaseColorPropertyId, Color.white),
                TextureTilingOffset = GetTilingOffset(material),
                Emission = GetColor(material, s_EmissionColorPropertyId, Color.black),
                MetallicSmoothnessRemap = new float4(
                    GetFloat(material, s_MetallicRemapMinPropertyId, 0.0f),
                    GetFloat(material, s_MetallicRemapMaxPropertyId, 1.0f),
                    GetFloat(material, s_SmoothnessRemapMinPropertyId, 0.0f),
                    GetFloat(material, s_SmoothnessRemapMaxPropertyId, 1.0f)),
                AmbientOcclusionRemap = new float4(
                    GetFloat(material, s_AORemapMinPropertyId, 0.0f),
                    GetFloat(material, s_AORemapMaxPropertyId, 1.0f),
                    0.0f,
                    0.0f),
                SurfaceBindingIndex = surfaceBindingIndex,
                NormalsStrength = GetFloat(material, s_BumpScalePropertyId, 1.0f),
                Roughness = GetRoughness(material),
                Metallic = GetFloat(material, s_MetallicPropertyId, 0.0f),
                SpecularAAScreenSpaceVariance = 0.0f,
                SpecularAAThreshold = 0.0f,
                GeometryFlags = VividGeometryFlags.None,
                MaterialFlags = GetMaterialFlags(material),
                RendererListID = GetRendererListId(material),
                AlphaClipThreshold = GetAlphaClipThreshold(material),
                Padding0 = (uint) GetMaskMode(material),
                Padding1 = 0,
            };
        }

        private static VividMaterialData CreateTerrainMaterialData(
            VividTerrainData terrainData,
            in VividTerrainLayerData layer,
            uint surfaceBindingIndex,
            uint terrainMaterialIndex
        )
        {
            float4 textureTilingOffset = GetTerrainLayerTilingOffset(terrainData, layer);
            bool usesLayerBlend = terrainMaterialIndex != VividSurfaceBindingData.InvalidResource;

            return new VividMaterialData
            {
                AlbedoColor = new float4(1.0f),
                TextureTilingOffset = textureTilingOffset,
                Emission = new float4(0.0f),
                MetallicSmoothnessRemap = new float4(0.0f, 1.0f, 0.0f, 1.0f),
                AmbientOcclusionRemap = new float4(0.0f, 1.0f, 0.0f, 0.0f),
                SurfaceBindingIndex = surfaceBindingIndex,
                NormalsStrength = layer.NormalScale,
                Roughness = 1.0f - Mathf.Clamp01(layer.Smoothness),
                Metallic = Mathf.Clamp01(layer.Metallic),
                SpecularAAScreenSpaceVariance = 0.0f,
                SpecularAAThreshold = 0.0f,
                GeometryFlags = VividGeometryFlags.None,
                MaterialFlags = usesLayerBlend ? VividMaterialFlags.Terrain : VividMaterialFlags.None,
                RendererListID = VividRendererListID.Default,
                AlphaClipThreshold = 0.0f,
                Padding0 = layer.MaskMapTexture != null
                    ? (uint) GPUDrivenMaterialMaskMode.PackedMetallicOcclusionSmoothness
                    : (uint) GPUDrivenMaterialMaskMode.None,
                Padding1 = usesLayerBlend ? terrainMaterialIndex : 0u,
            };
        }

        private static VividMaterialData CreateCompositeTerrainMaterialData(
            uint surfaceBindingIndex,
            bool usesTerrainRVT = false,
            uint terrainRVTRecordIndex = 0u)
        {
            return new VividMaterialData
            {
                AlbedoColor = new float4(1.0f),
                TextureTilingOffset = new float4(1.0f, 1.0f, 0.0f, 0.0f),
                Emission = new float4(0.0f),
                MetallicSmoothnessRemap = new float4(0.0f, 1.0f, 0.0f, 1.0f),
                AmbientOcclusionRemap = new float4(0.0f, 1.0f, 0.0f, 0.0f),
                SurfaceBindingIndex = surfaceBindingIndex,
                NormalsStrength = 1.0f,
                Roughness = 1.0f,
                Metallic = 0.0f,
                SpecularAAScreenSpaceVariance = 0.0f,
                SpecularAAThreshold = 0.0f,
                GeometryFlags = VividGeometryFlags.None,
                MaterialFlags = usesTerrainRVT
                    ? VividMaterialFlags.TerrainRuntimeVirtualTexture
                    : VividMaterialFlags.None,
                RendererListID = VividRendererListID.Default,
                AlphaClipThreshold = 0.0f,
                Padding0 = (uint) GPUDrivenMaterialMaskMode.PackedMetallicOcclusionSmoothness,
                Padding1 = usesTerrainRVT ? terrainRVTRecordIndex : 0u,
            };
        }

        private static VividTerrainLayerGPUData CreateTerrainLayerGPUData(
            VividTerrainData terrainData,
            in VividTerrainLayerData layer,
            uint surfaceBindingIndex)
        {
            return new VividTerrainLayerGPUData
            {
                TextureTilingOffset = GetTerrainLayerTilingOffset(terrainData, layer),
                SurfaceBindingIndex = surfaceBindingIndex,
                NormalsStrength = layer.NormalScale,
                Roughness = 1.0f - Mathf.Clamp01(layer.Smoothness),
                Metallic = Mathf.Clamp01(layer.Metallic),
                MaskMode = layer.MaskMapTexture != null
                    ? (uint) GPUDrivenMaterialMaskMode.PackedMetallicOcclusionSmoothness
                    : (uint) GPUDrivenMaterialMaskMode.None,
            };
        }

        private static float4 GetTerrainLayerTilingOffset(
            VividTerrainData terrainData,
            in VividTerrainLayerData layer)
        {
            Vector4 tilingOffset = VividTerrainSurfaceUtility.GetLayerTilingOffset(
                terrainData.Size,
                layer.TileSize,
                layer.TileOffset);
            return new float4(tilingOffset.x, tilingOffset.y, tilingOffset.z, tilingOffset.w);
        }

        private static uint AppendTerrainControlBinding(
            VividGPUDrivenSceneData sceneData,
            IGPUDrivenTextureBackend textureBackend,
            VividTerrainData terrainData,
            int controlMapIndex)
        {
            if (controlMapIndex < 0 || controlMapIndex >= terrainData.ControlMaps.Count)
            {
                return VividSurfaceBindingData.InvalidResource;
            }

            Texture2D controlMap = terrainData.ControlMaps[controlMapIndex];
            if (controlMap == null)
            {
                return VividSurfaceBindingData.InvalidResource;
            }

            VividVirtualTextureAsset streamedAsset = controlMapIndex < terrainData.ControlVirtualTextures.Count
                ? terrainData.ControlVirtualTextures[controlMapIndex]
                : null;

            return AppendSurfaceBinding(
                sceneData,
                textureBackend,
                new GPUDrivenSurfaceTextureSet(
                    streamedAsset,
                    null,
                    null,
                    controlMap,
                    GPUDrivenMaterialMaskMode.PackedMetallicOcclusionSmoothness));
        }

        private static uint AppendSurfaceBinding(
            VividGPUDrivenSceneData sceneData,
            IGPUDrivenTextureBackend textureBackend,
            in GPUDrivenSurfaceTextureSet surfaceTextures)
        {
            uint surfaceBindingIndex = (uint) sceneData.SurfaceBindingCount;
            sceneData.MutableSurfaceBindings.Add(textureBackend.CreateSurfaceBinding(surfaceTextures));
            return surfaceBindingIndex;
        }

        private static GPUDrivenSurfaceTextureSet ExtractSurfaceTextures(
            GPUDrivenMaterialProxy materialProxy,
            IGPUDrivenTextureBackend textureBackend)
        {
            if (UsesVirtualTexturePayload(materialProxy, textureBackend))
            {
                return new GPUDrivenSurfaceTextureSet(
                    materialProxy.StreamedVirtualTexture,
                    null,
                    null,
                    null,
                    materialProxy.MaskMode);
            }

            if (materialProxy.TextureMode == GPUDrivenMaterialProxyTextureMode.VirtualTexture)
            {
                return ExtractSurfaceTextures(materialProxy.SourceMaterial);
            }

            return new GPUDrivenSurfaceTextureSet(
                null,
                materialProxy.BaseMap,
                materialProxy.BumpMap,
                materialProxy.MaskMap,
                materialProxy.MaskMode);
        }

        private static uint ComputeMaterialProxyRevision(
            GPUDrivenMaterialProxy materialProxy,
            IGPUDrivenTextureBackend textureBackend)
        {
            if (materialProxy == null)
                return 0u;

            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ materialProxy.Revision) * 16777619u;
                if (UsesVirtualTexturePayload(materialProxy, textureBackend))
                {
                    VividVirtualTextureAsset asset = materialProxy.StreamedVirtualTexture;
                    hash = (hash ^ GetObjectRevisionId(asset)) * 16777619u;
                    hash = (hash ^ (asset != null ? asset.ContentVersion : 0u)) * 16777619u;
                }
                else if (materialProxy.TextureMode == GPUDrivenMaterialProxyTextureMode.VirtualTexture)
                {
                    GPUDrivenSurfaceTextureSet fallbackTextures =
                        ExtractSurfaceTextures(materialProxy.SourceMaterial);
                    hash = (hash ^ GetObjectRevisionId(fallbackTextures.BaseColor)) * 16777619u;
                    hash = (hash ^ GetObjectRevisionId(fallbackTextures.Normal)) * 16777619u;
                    hash = (hash ^ GetObjectRevisionId(fallbackTextures.Mask)) * 16777619u;
                }
                return hash;
            }
        }

        private static bool UsesVirtualTexturePayload(
            GPUDrivenMaterialProxy materialProxy,
            IGPUDrivenTextureBackend textureBackend)
        {
            if (textureBackend is IGPUDrivenVirtualTextureBackend)
                return true;

            return materialProxy != null
                   && materialProxy.StreamedVirtualTexture != null
                   && textureBackend.CanUseStreamedVirtualTexture(materialProxy.StreamedVirtualTexture);
        }

        private static GPUDrivenSurfaceTextureSet ExtractSurfaceTextures(Material material)
        {
            Texture baseColor = GetTexture(material, s_BaseMapPropertyId) ?? GetTexture(material, s_MainTexPropertyId);
            Texture normal = GetTexture(material, s_BumpMapPropertyId);
            Texture mask = GetTexture(material, s_MaskMapPropertyId)
                           ?? GetTexture(material, s_RMOMapPropertyId)
                           ?? GetTexture(material, s_MetallicGlossMapPropertyId)
                           ?? GetTexture(material, s_RoughnessMapPropertyId);
            return new GPUDrivenSurfaceTextureSet(null, baseColor, normal, mask, GetMaskMode(material));
        }

        private static GPUDrivenMaterialMaskMode GetMaskMode(Material material)
        {
            if (GetTexture(material, s_MaskMapPropertyId) != null)
                return GPUDrivenMaterialMaskMode.PackedMetallicOcclusionSmoothness;
            if (GetTexture(material, s_RMOMapPropertyId) != null)
                return GPUDrivenMaterialMaskMode.RoughnessMetallicOcclusion;
            if (GetTexture(material, s_MetallicGlossMapPropertyId) != null)
                return GPUDrivenMaterialMaskMode.MetallicSmoothness;
            if (GetTexture(material, s_RoughnessMapPropertyId) != null)
                return GPUDrivenMaterialMaskMode.Roughness;

            return GPUDrivenMaterialMaskMode.None;
        }

        private static VividInstanceData CreateInstanceData(
            in VividMeshletRendererRenderData trackedData,
            int materialIndex,
            in MeshletAssetMetadata meshMetadata,
            in Bounds localBounds
        )
        {
            return new VividInstanceData
            {
                ObjectToWorldMatrix = ToFloat4x4(trackedData.objectToWorldMatrix),
                WorldToObjectMatrix = ToFloat4x4(trackedData.worldToObjectMatrix),
                AABBMin = ToFloat4(localBounds.min),
                AABBMax = ToFloat4(localBounds.max),
                TopMeshLODStartIndex = meshMetadata.TopMeshLODStartIndex,
                TotalMeshLODCount = meshMetadata.TotalMeshLODCount,
                MaterialIndex = (uint) materialIndex,
                MeshLODLevelCount = meshMetadata.MeshLODLevelCount,
                LODErrorScale = 1.0f,
                PassMask = ExtractPassMask(trackedData.shadowCastingMode),
                Flags = ExtractInstanceFlags(trackedData),
            };
        }

        private static VividInstancePassMask ExtractPassMask(ShadowCastingMode shadowCastingMode)
        {
            return shadowCastingMode switch
            {
                ShadowCastingMode.Off => VividInstancePassMask.Main,
                ShadowCastingMode.On => VividInstancePassMask.Main | VividInstancePassMask.Shadows,
                ShadowCastingMode.TwoSided => VividInstancePassMask.Main | VividInstancePassMask.Shadows,
                ShadowCastingMode.ShadowsOnly => VividInstancePassMask.Shadows,
                _ => VividInstancePassMask.Main,
            };
        }

        private static VividInstanceFlags ExtractInstanceFlags(in VividMeshletRendererRenderData trackedData)
        {
            VividInstanceFlags flags = VividInstanceFlags.None;
            VividMeshletRendererFlags rendererFlags = trackedData.flags;

            bool isDisabled = (rendererFlags & VividMeshletRendererFlags.ActiveInHierarchy) == 0
                || (rendererFlags & VividMeshletRendererFlags.Enabled) == 0
                || (rendererFlags & VividMeshletRendererFlags.SourceRendererEnabled) == 0;

            if (isDisabled)
            {
                flags |= VividInstanceFlags.Disabled;
            }

            if (trackedData.objectToWorldMatrix.determinant < 0.0f)
            {
                flags |= VividInstanceFlags.FlipWindingOrder;
            }

            return flags;
        }

        private static Material GetMaterialForSubMesh(Material[] sharedMaterials, int subMeshIndex)
        {
            if (sharedMaterials == null || sharedMaterials.Length == 0)
            {
                return null;
            }

            int materialIndex = Mathf.Clamp(subMeshIndex, 0, sharedMaterials.Length - 1);
            return sharedMaterials[materialIndex];
        }

        private static GPUDrivenMaterialProxy GetMaterialProxyForSubMesh(
            GPUDrivenMaterialProxy[] materialProxies,
            int subMeshIndex
        )
        {
            if (materialProxies == null || materialProxies.Length == 0)
            {
                return null;
            }

            int materialIndex = Mathf.Clamp(subMeshIndex, 0, materialProxies.Length - 1);
            return materialProxies[materialIndex];
        }

        private static Texture GetTexture(Material material, int propertyId)
        {
            return material != null && material.HasProperty(propertyId) ? material.GetTexture(propertyId) : null;
        }

        private static float4 GetColor(Material material, int propertyId, Color fallback)
        {
            Color color = material != null && material.HasProperty(propertyId)
                ? material.GetColor(propertyId)
                : fallback;
            return ConvertMaterialColorForGPU(color);
        }

        internal static float4 ConvertMaterialColorForGPU(Color color)
        {
            return GPUDrivenMaterialCompiler.ConvertMaterialColorForGPU(color);
        }

        private static float4 GetTilingOffset(Material material)
        {
            int texturePropertyId = material != null && material.HasProperty(s_BaseMapPropertyId)
                ? s_BaseMapPropertyId
                : s_MainTexPropertyId;

            if (material == null || !material.HasProperty(texturePropertyId))
            {
                return new float4(1.0f, 1.0f, 0.0f, 0.0f);
            }

            Vector2 scale = material.GetTextureScale(texturePropertyId);
            Vector2 offset = material.GetTextureOffset(texturePropertyId);
            return new float4(scale.x, scale.y, offset.x, offset.y);
        }

        private static float GetFloat(Material material, int propertyId, float fallback)
        {
            return material != null && material.HasProperty(propertyId) ? material.GetFloat(propertyId) : fallback;
        }

        private static float GetRoughness(Material material)
        {
            if (material == null)
            {
                return 1.0f;
            }

            if (material.HasProperty(s_SmoothnessPropertyId))
            {
                return 1.0f - Mathf.Clamp01(material.GetFloat(s_SmoothnessPropertyId));
            }

            return 1.0f;
        }

        private static VividMaterialFlags GetMaterialFlags(Material material)
        {
            if (IsSimpleForwardShader(material != null ? material.shader : null))
            {
                return VividMaterialFlags.Unlit;
            }

            return VividMaterialFlags.None;
        }

        private static bool IsSimpleForwardShader(Shader shader)
        {
            if (shader == null)
            {
                return false;
            }

            if (!s_SimpleForwardShaderResolved)
            {
                s_SimpleForwardShader = Shader.Find(SimpleForwardShaderName);
                s_SimpleForwardShaderResolved = true;
            }

            if (s_SimpleForwardShader != null)
            {
                return shader == s_SimpleForwardShader;
            }

            if (s_SimpleForwardShaderMatchCache.TryGetValue(shader, out bool isSimpleForwardShader))
            {
                return isSimpleForwardShader;
            }

            isSimpleForwardShader = string.Equals(shader.name, SimpleForwardShaderName, StringComparison.Ordinal);
            s_SimpleForwardShaderMatchCache.Add(shader, isSimpleForwardShader);
            return isSimpleForwardShader;
        }

        private static VividRendererListID GetRendererListId(Material material)
        {
            VividRendererListID rendererListId = VividRendererListID.Default;

            if (material != null)
            {
                int cullMode = material.HasProperty(s_CullPropertyId)
                    ? Mathf.RoundToInt(material.GetFloat(s_CullPropertyId))
                    : (int) CullMode.Back;

                if (cullMode == (int) CullMode.Front)
                {
                    rendererListId |= VividRendererListID.CullFront;
                }
                else if (cullMode == (int) CullMode.Off)
                {
                    rendererListId |= VividRendererListID.CullOff;
                }

                if (IsAlphaClipEnabled(material))
                {
                    rendererListId |= VividRendererListID.AlphaTest;
                }
            }

            return rendererListId;
        }

        private static bool IsAlphaClipEnabled(Material material)
        {
            return material != null &&
                   ((material.HasProperty(s_AlphaClipPropertyId) && material.GetFloat(s_AlphaClipPropertyId) > 0.5f) ||
                    material.IsKeywordEnabled("_ALPHATEST_ON"));
        }

        private static float GetAlphaClipThreshold(Material material)
        {
            return IsAlphaClipEnabled(material) && material.HasProperty(s_CutoffPropertyId)
                ? material.GetFloat(s_CutoffPropertyId)
                : 0.0f;
        }

        private static float4 ToFloat4(Vector3 value)
        {
            return new float4(value.x, value.y, value.z, 0.0f);
        }

        private static float4x4 ToFloat4x4(Matrix4x4 value)
        {
            return new float4x4(
                new float4(value.m00, value.m10, value.m20, value.m30),
                new float4(value.m01, value.m11, value.m21, value.m31),
                new float4(value.m02, value.m12, value.m22, value.m32),
                new float4(value.m03, value.m13, value.m23, value.m33)
            );
        }

        private readonly struct MeshletAssetMetadata
        {
            public MeshletAssetMetadata(
                uint topMeshLODStartIndex,
                uint totalMeshLODCount,
                uint meshLODLevelCount,
                uint assetVersion,
                int maxVisibleMeshletRenderRequestCount)
            {
                TopMeshLODStartIndex = topMeshLODStartIndex;
                TotalMeshLODCount = totalMeshLODCount;
                MeshLODLevelCount = meshLODLevelCount;
                AssetVersion = assetVersion;
                MaxVisibleMeshletRenderRequestCount = maxVisibleMeshletRenderRequestCount;
            }

            public uint TopMeshLODStartIndex { get; }

            public uint TotalMeshLODCount { get; }

            public uint MeshLODLevelCount { get; }

            public uint AssetVersion { get; }

            public int MaxVisibleMeshletRenderRequestCount { get; }
        }

        private readonly struct MaterialMetadata
        {
            public MaterialMetadata(int materialIndex, uint revision)
            {
                MaterialIndex = materialIndex;
                Revision = revision;
            }

            public int MaterialIndex { get; }

            public uint Revision { get; }
        }

        private sealed class EntityIdComparer : IEqualityComparer<EntityId>
        {
            public bool Equals(EntityId x, EntityId y)
            {
                return EntityId.ToULong(x) == EntityId.ToULong(y);
            }

            public int GetHashCode(EntityId obj)
            {
                return EntityId.ToULong(obj).GetHashCode();
            }
        }

        private sealed class EntityIdSubMeshIndexComparer : IEqualityComparer<(EntityId entityId, int subMeshIndex)>
        {
            public bool Equals(
                (EntityId entityId, int subMeshIndex) x,
                (EntityId entityId, int subMeshIndex) y)
            {
                return x.subMeshIndex == y.subMeshIndex
                       && EntityId.ToULong(x.entityId) == EntityId.ToULong(y.entityId);
            }

            public int GetHashCode((EntityId entityId, int subMeshIndex) obj)
            {
                unchecked
                {
                    return (EntityId.ToULong(obj.entityId).GetHashCode() * 397) ^ obj.subMeshIndex;
                }
            }
        }
    }
}
