using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Runtime.GPUDriven
{
    [Flags]
    internal enum VividMeshletRendererChangeFlags : byte
    {
        None = 0,
        Added = 1 << 0,
        Removed = 1 << 1,
        Transform = 1 << 2,
        RenderState = 1 << 3,
        Resources = 1 << 4,
    }

    internal readonly struct VividMeshletRendererChange
    {
        internal VividMeshletRendererChange(EntityId entityId, VividMeshletRendererChangeFlags flags)
        {
            EntityId = entityId;
            Flags = flags;
        }

        internal EntityId EntityId { get; }

        internal VividMeshletRendererChangeFlags Flags { get; }
    }

    [Flags]
    public enum VividMeshletRendererFlags : uint
    {
        None = 0,
        ActiveInHierarchy = 1u << 0,
        Enabled = 1u << 1,
        Valid = 1u << 2,
        SourceRendererEnabled = 1u << 3,
        CastShadows = 1u << 4,
        ReceiveShadows = 1u << 5,
        Static = 1u << 6,
        Skinned = 1u << 7,
    }

    public struct VividMeshletRendererRenderData
    {
        public EntityId meshletRendererEntityId;
        public EntityId sourceRendererEntityId;
        public EntityId sourceMeshEntityId;
        public Matrix4x4 objectToWorldMatrix;
        public Matrix4x4 worldToObjectMatrix;
        public Bounds localBounds;
        public Bounds worldBounds;
        internal uint cameraLayerMask;
        public uint renderingLayerMask;
        public ShadowCastingMode shadowCastingMode;
        public MotionVectorGenerationMode motionVectorGenerationMode;
        public VividMeshletRendererFlags flags;
        public int subMeshCount;
        public int materialCount;
    }

    public readonly struct VividMeshletRendererResources
    {
        public VividMeshletRendererResources(
            MeshletRenderer meshletRenderer,
            Renderer sourceRenderer,
            Mesh sourceMesh,
            Material[] sourceMaterials,
            VividMeshletCollectionAsset[] meshletCollections,
            GPUDrivenMaterialProxy[] materialProxies
        )
        {
            MeshletRenderer = meshletRenderer;
            SourceRenderer = sourceRenderer;
            SourceMesh = sourceMesh;
            Terrain = null;
            TerrainData = null;
            SharedMaterials = sourceMaterials ?? Array.Empty<Material>();
            MeshletCollections = meshletCollections ?? Array.Empty<VividMeshletCollectionAsset>();
            MaterialProxies = materialProxies ?? Array.Empty<GPUDrivenMaterialProxy>();
            LocalBounds = Array.Empty<Bounds>();
        }

        public VividMeshletRendererResources(
            VividTerrain terrain,
            VividTerrainData terrainData,
            Material sourceMaterial,
            VividMeshletCollectionAsset[] meshletCollections,
            Bounds[] localBounds
        )
        {
            MeshletRenderer = null;
            SourceRenderer = null;
            SourceMesh = null;
            Terrain = terrain;
            TerrainData = terrainData;
            SharedMaterials = sourceMaterial != null
                ? new[] { sourceMaterial }
                : Array.Empty<Material>();
            MeshletCollections = meshletCollections ?? Array.Empty<VividMeshletCollectionAsset>();
            MaterialProxies = Array.Empty<GPUDrivenMaterialProxy>();
            LocalBounds = localBounds ?? Array.Empty<Bounds>();
        }

        public MeshletRenderer MeshletRenderer { get; }

        public Renderer SourceRenderer { get; }

        public Mesh SourceMesh { get; }

        public VividTerrain Terrain { get; }

        public VividTerrainData TerrainData { get; }

        public Material[] SharedMaterials { get; }

        public VividMeshletCollectionAsset[] MeshletCollections { get; }

        public GPUDrivenMaterialProxy[] MaterialProxies { get; }

        public Bounds[] LocalBounds { get; }

        public bool IsTerrain => Terrain != null;
    }

    public sealed class VividMeshletRendererDatabase
    {
        private readonly List<VividMeshletRendererRenderData> m_RendererData = new();
        private readonly List<VividMeshletRendererResources> m_RendererResources = new();
        private readonly Dictionary<EntityId, int> m_EntityIdToDataIndex = new();
        private readonly Dictionary<EntityId, VividMeshletRendererChangeFlags> m_PrimitiveChanges = new();
        private uint m_StructureRevision;
        private uint m_ResourceRevision;
        private uint m_InstanceRevision;
        private bool m_PrimitiveChangeJournalRequiresFullResync = true;

        private static readonly VividMeshletRendererDatabase s_Instance = new();

        public static VividMeshletRendererDatabase instance => s_Instance;

        public int rendererCount => m_RendererData.Count;

        public IReadOnlyList<VividMeshletRendererRenderData> rendererData => m_RendererData;

        public IReadOnlyList<VividMeshletRendererResources> rendererResources => m_RendererResources;

        internal uint StructureRevision => m_StructureRevision;

        internal uint ResourceRevision => m_ResourceRevision;

        internal uint InstanceRevision => m_InstanceRevision;

        internal VividMeshletRendererRenderData RegisterRenderer(MeshletRenderer meshletRenderer)
        {
            return UpdateRendererData(meshletRenderer);
        }

        internal VividMeshletRendererRenderData UpdateRendererData(MeshletRenderer meshletRenderer)
        {
            if (meshletRenderer == null)
            {
                return default;
            }

            VividMeshletRendererRenderData trackedData = CreateRendererData(meshletRenderer);
            VividMeshletRendererResources trackedResources = CreateRendererResources(meshletRenderer);
            bool added = StoreRendererData(trackedData, trackedResources);
            if (added)
                MarkStructureChanged();
            else
                MarkResourcesChanged();
            MarkPrimitiveChanged(
                trackedData.meshletRendererEntityId,
                (added ? VividMeshletRendererChangeFlags.Added : VividMeshletRendererChangeFlags.None)
                | VividMeshletRendererChangeFlags.Transform
                | VividMeshletRendererChangeFlags.RenderState
                | VividMeshletRendererChangeFlags.Resources);
            meshletRenderer.NotifyRendererDataSynchronized(resourcesUpdated: true);
            return trackedData;
        }

        internal VividMeshletRendererRenderData UpdateRendererRenderData(MeshletRenderer meshletRenderer)
        {
            if (meshletRenderer == null)
            {
                return default;
            }

            EntityId meshletRendererEntityId = meshletRenderer.GetEntityId();
            if (!TryGetRendererResources(meshletRendererEntityId, out VividMeshletRendererResources trackedResources))
            {
                return UpdateRendererData(meshletRenderer);
            }

            VividMeshletRendererRenderData trackedData = CreateRendererData(meshletRenderer);
            StoreRendererData(trackedData, trackedResources);
            MarkInstancesChanged();
            MarkPrimitiveChanged(
                trackedData.meshletRendererEntityId,
                VividMeshletRendererChangeFlags.Transform | VividMeshletRendererChangeFlags.RenderState);
            meshletRenderer.NotifyRendererDataSynchronized(resourcesUpdated: false);
            return trackedData;
        }

        internal VividMeshletRendererRenderData UpdateRendererTransformData(MeshletRenderer meshletRenderer)
        {
            if (meshletRenderer == null)
            {
                return default;
            }

            EntityId meshletRendererEntityId = meshletRenderer.GetEntityId();
            if (!TryGetRendererData(meshletRendererEntityId, out VividMeshletRendererRenderData trackedData)
                || !TryGetRendererResources(meshletRendererEntityId, out VividMeshletRendererResources trackedResources))
            {
                return UpdateRendererData(meshletRenderer);
            }

            VividMeshletRendererRenderData updatedTrackedData =
                CreateTransformOnlyRendererData(meshletRenderer, trackedData);
            StoreRendererData(updatedTrackedData, trackedResources);
            MarkInstancesChanged();
            MarkPrimitiveChanged(
                updatedTrackedData.meshletRendererEntityId,
                VividMeshletRendererChangeFlags.Transform);
            meshletRenderer.NotifyRendererDataSynchronized(resourcesUpdated: false);
            return updatedTrackedData;
        }

        internal VividMeshletRendererRenderData UpdateTerrainData(VividTerrain terrain)
        {
            if (terrain == null)
            {
                return default;
            }

            VividMeshletRendererRenderData trackedData = CreateTerrainData(terrain);
            VividMeshletRendererResources trackedResources = CreateTerrainResources(terrain);
            bool added = StoreRendererData(trackedData, trackedResources);
            if (added)
                MarkStructureChanged();
            else
                MarkResourcesChanged();
            MarkPrimitiveChanged(
                trackedData.meshletRendererEntityId,
                (added ? VividMeshletRendererChangeFlags.Added : VividMeshletRendererChangeFlags.None)
                | VividMeshletRendererChangeFlags.Transform
                | VividMeshletRendererChangeFlags.RenderState
                | VividMeshletRendererChangeFlags.Resources);
            terrain.NotifyTerrainDataSynchronized();
            return trackedData;
        }

        internal VividMeshletRendererRenderData UpdateTerrainTransformData(VividTerrain terrain)
        {
            if (terrain == null)
            {
                return default;
            }

            EntityId terrainEntityId = terrain.GetEntityId();
            if (!TryGetRendererData(terrainEntityId, out VividMeshletRendererRenderData trackedData)
                || !TryGetRendererResources(terrainEntityId, out VividMeshletRendererResources trackedResources))
            {
                return UpdateTerrainData(terrain);
            }

            VividMeshletRendererRenderData updatedTrackedData =
                CreateTerrainTransformOnlyData(terrain, trackedData);
            StoreRendererData(updatedTrackedData, trackedResources);
            MarkInstancesChanged();
            MarkPrimitiveChanged(
                updatedTrackedData.meshletRendererEntityId,
                VividMeshletRendererChangeFlags.Transform);
            terrain.NotifyTerrainDataSynchronized();
            return updatedTrackedData;
        }

        internal VividMeshletRendererRenderData UpdateTerrainRenderData(VividTerrain terrain)
        {
            if (terrain == null)
            {
                return default;
            }

            EntityId terrainEntityId = terrain.GetEntityId();
            if (!TryGetRendererResources(terrainEntityId, out VividMeshletRendererResources trackedResources))
            {
                return UpdateTerrainData(terrain);
            }

            VividMeshletRendererRenderData trackedData = CreateTerrainData(terrain);
            StoreRendererData(trackedData, trackedResources);
            MarkInstancesChanged();
            MarkPrimitiveChanged(
                trackedData.meshletRendererEntityId,
                VividMeshletRendererChangeFlags.Transform | VividMeshletRendererChangeFlags.RenderState);
            terrain.NotifyTerrainDataSynchronized();
            return trackedData;
        }

        internal bool TryGetRendererData(MeshletRenderer meshletRenderer, out VividMeshletRendererRenderData trackedData)
        {
            trackedData = default;

            if (meshletRenderer == null)
            {
                return false;
            }

            return TryGetRendererData(meshletRenderer.GetEntityId(), out trackedData);
        }

        internal bool TryGetRendererResources(MeshletRenderer meshletRenderer, out VividMeshletRendererResources trackedResources)
        {
            trackedResources = default;

            if (meshletRenderer == null)
            {
                return false;
            }

            return TryGetRendererResources(meshletRenderer.GetEntityId(), out trackedResources);
        }

        internal bool TryGetTerrainData(VividTerrain terrain, out VividMeshletRendererRenderData trackedData)
        {
            trackedData = default;
            return terrain != null && TryGetRendererData(terrain.GetEntityId(), out trackedData);
        }

        internal bool TryGetTerrainResources(VividTerrain terrain, out VividMeshletRendererResources trackedResources)
        {
            trackedResources = default;
            return terrain != null && TryGetRendererResources(terrain.GetEntityId(), out trackedResources);
        }

        internal bool TryGetRendererData(EntityId meshletRendererEntityId, out VividMeshletRendererRenderData trackedData)
        {
            trackedData = default;

            if (meshletRendererEntityId.Equals(EntityId.None))
            {
                return false;
            }

            return m_EntityIdToDataIndex.TryGetValue(meshletRendererEntityId, out int dataIndex)
                && TryGetRendererData(dataIndex, out trackedData);
        }

        internal bool TryGetRendererResources(EntityId meshletRendererEntityId, out VividMeshletRendererResources trackedResources)
        {
            trackedResources = default;

            if (meshletRendererEntityId.Equals(EntityId.None))
            {
                return false;
            }

            return m_EntityIdToDataIndex.TryGetValue(meshletRendererEntityId, out int dataIndex)
                && TryGetRendererResources(dataIndex, out trackedResources);
        }

        internal void UnregisterRenderer(MeshletRenderer meshletRenderer)
        {
            if (meshletRenderer == null)
            {
                return;
            }

            meshletRenderer.InvalidatePrimitiveHandle();

            EntityId meshletRendererEntityId = meshletRenderer.GetEntityId();
            if (meshletRendererEntityId.Equals(EntityId.None)
                || !m_EntityIdToDataIndex.TryGetValue(meshletRendererEntityId, out int removedIndex))
            {
                return;
            }

            MarkPrimitiveChanged(meshletRendererEntityId, VividMeshletRendererChangeFlags.Removed);
            RemoveRendererAt(removedIndex);
        }

        internal void UnregisterTerrain(VividTerrain terrain)
        {
            if (terrain == null)
            {
                return;
            }

            terrain.InvalidatePrimitiveHandle();

            EntityId terrainEntityId = terrain.GetEntityId();
            if (terrainEntityId.Equals(EntityId.None)
                || !m_EntityIdToDataIndex.TryGetValue(terrainEntityId, out int removedIndex))
            {
                return;
            }

            MarkPrimitiveChanged(terrainEntityId, VividMeshletRendererChangeFlags.Removed);
            RemoveRendererAt(removedIndex);
        }

        internal void Clear()
        {
            bool hadRenderers = m_RendererData.Count > 0
                || m_RendererResources.Count > 0
                || m_EntityIdToDataIndex.Count > 0;
            InvalidatePrimitiveHandles();
            m_RendererData.Clear();
            m_RendererResources.Clear();
            m_EntityIdToDataIndex.Clear();
            m_PrimitiveChanges.Clear();
            m_PrimitiveChangeJournalRequiresFullResync = true;
            if (hadRenderers)
                MarkStructureChanged();
        }

        internal void InvalidatePrimitiveHandles()
        {
            for (int index = 0; index < m_RendererResources.Count; index++)
            {
                VividMeshletRendererResources resources = m_RendererResources[index];
                if (resources.MeshletRenderer != null)
                    resources.MeshletRenderer.InvalidatePrimitiveHandle();
                if (resources.Terrain != null)
                    resources.Terrain.InvalidatePrimitiveHandle();
            }
        }

        internal void ConsumePrimitiveChanges(
            List<VividMeshletRendererChange> destination,
            out bool requiresFullResync)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            destination.Clear();
            foreach (KeyValuePair<EntityId, VividMeshletRendererChangeFlags> change in m_PrimitiveChanges)
                destination.Add(new VividMeshletRendererChange(change.Key, change.Value));
            m_PrimitiveChanges.Clear();
            requiresFullResync = m_PrimitiveChangeJournalRequiresFullResync;
            m_PrimitiveChangeJournalRequiresFullResync = false;
        }

        private bool TryGetRendererData(int dataIndex, out VividMeshletRendererRenderData trackedData)
        {
            trackedData = default;

            if (dataIndex < 0 || dataIndex >= m_RendererData.Count)
            {
                return false;
            }

            trackedData = m_RendererData[dataIndex];
            return true;
        }

        private bool TryGetRendererResources(int dataIndex, out VividMeshletRendererResources trackedResources)
        {
            trackedResources = default;

            if (dataIndex < 0 || dataIndex >= m_RendererResources.Count)
            {
                return false;
            }

            trackedResources = m_RendererResources[dataIndex];
            return true;
        }

        private bool StoreRendererData(
            in VividMeshletRendererRenderData trackedData,
            in VividMeshletRendererResources trackedResources
        )
        {
            if (trackedData.meshletRendererEntityId.Equals(EntityId.None))
            {
                return false;
            }

            if (m_EntityIdToDataIndex.TryGetValue(trackedData.meshletRendererEntityId, out int dataIndex))
            {
                m_RendererData[dataIndex] = trackedData;
                m_RendererResources[dataIndex] = trackedResources;
                return false;
            }

            dataIndex = m_RendererData.Count;
            m_RendererData.Add(trackedData);
            m_RendererResources.Add(trackedResources);
            m_EntityIdToDataIndex.Add(trackedData.meshletRendererEntityId, dataIndex);
            return true;
        }

        private void RemoveRendererAt(int removedIndex)
        {
            VividMeshletRendererRenderData removedRendererData = m_RendererData[removedIndex];
            m_EntityIdToDataIndex.Remove(removedRendererData.meshletRendererEntityId);

            int lastIndex = m_RendererData.Count - 1;
            if (removedIndex != lastIndex)
            {
                VividMeshletRendererRenderData lastRendererData = m_RendererData[lastIndex];
                VividMeshletRendererResources lastResources = m_RendererResources[lastIndex];

                m_RendererData[removedIndex] = lastRendererData;
                m_RendererResources[removedIndex] = lastResources;
                m_EntityIdToDataIndex[lastRendererData.meshletRendererEntityId] = removedIndex;
            }

            m_RendererData.RemoveAt(lastIndex);
            m_RendererResources.RemoveAt(lastIndex);
            MarkStructureChanged();
        }

        private void MarkStructureChanged()
        {
            m_StructureRevision = IncrementRevision(m_StructureRevision);
            m_ResourceRevision = IncrementRevision(m_ResourceRevision);
            m_InstanceRevision = IncrementRevision(m_InstanceRevision);
        }

        private void MarkResourcesChanged()
        {
            m_ResourceRevision = IncrementRevision(m_ResourceRevision);
            m_InstanceRevision = IncrementRevision(m_InstanceRevision);
        }

        private void MarkInstancesChanged()
        {
            m_InstanceRevision = IncrementRevision(m_InstanceRevision);
        }

        private void MarkPrimitiveChanged(EntityId entityId, VividMeshletRendererChangeFlags flags)
        {
            if (entityId.Equals(EntityId.None) || flags == VividMeshletRendererChangeFlags.None)
                return;

            if ((flags & VividMeshletRendererChangeFlags.Removed) != 0)
            {
                m_PrimitiveChanges[entityId] = VividMeshletRendererChangeFlags.Removed;
                return;
            }

            if (m_PrimitiveChanges.TryGetValue(entityId, out VividMeshletRendererChangeFlags existingFlags))
            {
                if ((existingFlags & VividMeshletRendererChangeFlags.Removed) != 0
                    && (flags & VividMeshletRendererChangeFlags.Added) != 0)
                {
                    m_PrimitiveChanges[entityId] = flags;
                    return;
                }
                flags |= existingFlags;
            }
            m_PrimitiveChanges[entityId] = flags;
        }

        private static uint IncrementRevision(uint revision)
        {
            return revision == uint.MaxValue ? 1u : revision + 1u;
        }

        private static VividMeshletRendererRenderData CreateRendererData(MeshletRenderer meshletRenderer)
        {
            Mesh sourceMesh = meshletRenderer.sourceMesh;
            bool isValid = meshletRenderer.TryValidateRuntimeBindings(out _);
            Matrix4x4 objectToWorldMatrix = meshletRenderer.transform.localToWorldMatrix;
            Matrix4x4 worldToObjectMatrix = meshletRenderer.transform.worldToLocalMatrix;
            Bounds localBounds = sourceMesh != null ? sourceMesh.bounds : default;
            Bounds worldBounds = TransformBounds(localBounds, objectToWorldMatrix);
            int materialCount = meshletRenderer.sourceMaterials.Count;

            return new VividMeshletRendererRenderData
            {
                meshletRendererEntityId = meshletRenderer.GetEntityId(),
                sourceRendererEntityId = EntityId.None,
                sourceMeshEntityId = sourceMesh != null ? sourceMesh.GetEntityId() : EntityId.None,
                objectToWorldMatrix = objectToWorldMatrix,
                worldToObjectMatrix = worldToObjectMatrix,
                localBounds = localBounds,
                worldBounds = worldBounds,
                cameraLayerMask = GetCameraLayerMask(meshletRenderer.gameObject),
                renderingLayerMask = meshletRenderer.renderingLayerMask,
                shadowCastingMode = meshletRenderer.shadowCastingMode,
                motionVectorGenerationMode = meshletRenderer.motionVectorGenerationMode,
                flags = BuildFlags(meshletRenderer, isValid),
                subMeshCount = sourceMesh != null ? Mathf.Max(1, sourceMesh.subMeshCount) : 0,
                materialCount = materialCount,
            };
        }

        private static VividMeshletRendererResources CreateRendererResources(MeshletRenderer meshletRenderer)
        {
            int sourceMaterialCount = meshletRenderer.sourceMaterials.Count;
            var sourceMaterials = new Material[sourceMaterialCount];
            for (int index = 0; index < sourceMaterialCount; index++)
            {
                sourceMaterials[index] = meshletRenderer.GetSourceMaterial(index);
            }

            int meshletCollectionCount = meshletRenderer.meshletCollections.Count;
            var meshletCollections = new VividMeshletCollectionAsset[meshletCollectionCount];
            for (int index = 0; index < meshletCollectionCount; index++)
            {
                meshletCollections[index] = meshletRenderer.GetMeshletCollection(index);
            }

            int materialProxyCount = meshletRenderer.materialProxies.Count;
            var materialProxies = new GPUDrivenMaterialProxy[materialProxyCount];
            for (int index = 0; index < materialProxyCount; index++)
            {
                materialProxies[index] = meshletRenderer.GetMaterialProxy(index);
            }

            return new VividMeshletRendererResources(
                meshletRenderer,
                null,
                meshletRenderer.sourceMesh,
                sourceMaterials,
                meshletCollections,
                materialProxies
            );
        }

        private static VividMeshletRendererRenderData CreateTransformOnlyRendererData(
            MeshletRenderer meshletRenderer,
            VividMeshletRendererRenderData trackedData
        )
        {
            Matrix4x4 objectToWorldMatrix = meshletRenderer.transform.localToWorldMatrix;
            Matrix4x4 worldToObjectMatrix = meshletRenderer.transform.worldToLocalMatrix;
            Bounds localBounds = trackedData.localBounds;

            trackedData.objectToWorldMatrix = objectToWorldMatrix;
            trackedData.worldToObjectMatrix = worldToObjectMatrix;
            trackedData.localBounds = localBounds;
            trackedData.worldBounds = TransformBounds(localBounds, objectToWorldMatrix);
            return trackedData;
        }

        private static VividMeshletRendererRenderData CreateTerrainData(VividTerrain terrain)
        {
            VividTerrainData terrainData = terrain.Data;
            Matrix4x4 objectToWorldMatrix = terrain.transform.localToWorldMatrix;
            Matrix4x4 worldToObjectMatrix = terrain.transform.worldToLocalMatrix;
            Bounds localBounds = ResolveTerrainLocalBounds(terrainData);
            int chunkCount = terrainData?.Chunks.Count ?? 0;

            return new VividMeshletRendererRenderData
            {
                meshletRendererEntityId = terrain.GetEntityId(),
                sourceRendererEntityId = EntityId.None,
                sourceMeshEntityId = terrainData != null ? terrainData.GetEntityId() : EntityId.None,
                objectToWorldMatrix = objectToWorldMatrix,
                worldToObjectMatrix = worldToObjectMatrix,
                localBounds = localBounds,
                worldBounds = TransformBounds(localBounds, objectToWorldMatrix),
                cameraLayerMask = GetCameraLayerMask(terrain.gameObject),
                renderingLayerMask = terrain.RenderingLayerMask,
                shadowCastingMode = terrain.ShadowCastingMode,
                motionVectorGenerationMode = MotionVectorGenerationMode.Camera,
                flags = BuildTerrainFlags(terrain),
                subMeshCount = chunkCount,
                materialCount = terrainData != null ? 1 : 0,
            };
        }

        private static Bounds ResolveTerrainLocalBounds(VividTerrainData terrainData)
        {
            IReadOnlyList<VividTerrainChunkData> chunks = terrainData?.Chunks;
            if (chunks == null || chunks.Count == 0)
                return terrainData != null ? terrainData.LocalBounds : default;

            Bounds localBounds = chunks[0].LocalBounds;
            for (int chunkIndex = 1; chunkIndex < chunks.Count; chunkIndex++)
                localBounds.Encapsulate(chunks[chunkIndex].LocalBounds);
            return localBounds;
        }

        private static VividMeshletRendererResources CreateTerrainResources(VividTerrain terrain)
        {
            VividTerrainData terrainData = terrain.Data;
            if (terrainData == null)
            {
                return new VividMeshletRendererResources(
                    terrain,
                    null,
                    null,
                    Array.Empty<VividMeshletCollectionAsset>(),
                    Array.Empty<Bounds>()
                );
            }

            IReadOnlyList<VividTerrainChunkData> chunks = terrainData.Chunks;
            var meshletCollections = new VividMeshletCollectionAsset[chunks.Count];
            var localBounds = new Bounds[chunks.Count];
            for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                meshletCollections[chunkIndex] = chunks[chunkIndex].MeshletCollection;
                localBounds[chunkIndex] = chunks[chunkIndex].LocalBounds;
            }

            return new VividMeshletRendererResources(
                terrain,
                terrainData,
                terrainData.SourceMaterial,
                meshletCollections,
                localBounds
            );
        }

        private static VividMeshletRendererRenderData CreateTerrainTransformOnlyData(
            VividTerrain terrain,
            VividMeshletRendererRenderData trackedData
        )
        {
            Matrix4x4 objectToWorldMatrix = terrain.transform.localToWorldMatrix;
            trackedData.objectToWorldMatrix = objectToWorldMatrix;
            trackedData.worldToObjectMatrix = terrain.transform.worldToLocalMatrix;
            trackedData.worldBounds = TransformBounds(trackedData.localBounds, objectToWorldMatrix);
            return trackedData;
        }

        private static VividMeshletRendererFlags BuildFlags(
            MeshletRenderer meshletRenderer,
            bool isValid
        )
        {
            VividMeshletRendererFlags flags = VividMeshletRendererFlags.None;
            GameObject targetGameObject = meshletRenderer.gameObject;

            if (targetGameObject.activeInHierarchy)
            {
                flags |= VividMeshletRendererFlags.ActiveInHierarchy;
            }

            if (meshletRenderer.enabled)
            {
                flags |= VividMeshletRendererFlags.Enabled;
            }

            if (isValid)
            {
                flags |= VividMeshletRendererFlags.Valid;
            }

            if (meshletRenderer.sourceRenderingEnabled)
            {
                flags |= VividMeshletRendererFlags.SourceRendererEnabled;
            }

            if (meshletRenderer.shadowCastingMode != ShadowCastingMode.Off)
            {
                flags |= VividMeshletRendererFlags.CastShadows;
            }

            if (meshletRenderer.receiveShadows)
            {
                flags |= VividMeshletRendererFlags.ReceiveShadows;
            }

            if (targetGameObject.isStatic)
            {
                flags |= VividMeshletRendererFlags.Static;
            }

            if (meshletRenderer.sourceWasSkinned)
            {
                flags |= VividMeshletRendererFlags.Skinned;
            }

            return flags;
        }

        private static uint GetCameraLayerMask(GameObject gameObject)
        {
            return 1u << gameObject.layer;
        }

        private static VividMeshletRendererFlags BuildTerrainFlags(VividTerrain terrain)
        {
            VividMeshletRendererFlags flags = VividMeshletRendererFlags.SourceRendererEnabled;
            GameObject targetGameObject = terrain.gameObject;

            if (targetGameObject.activeInHierarchy)
            {
                flags |= VividMeshletRendererFlags.ActiveInHierarchy;
            }

            if (terrain.enabled)
            {
                flags |= VividMeshletRendererFlags.Enabled;
            }

            if (terrain.HasBakedData)
            {
                flags |= VividMeshletRendererFlags.Valid;
            }

            if (terrain.ShadowCastingMode != ShadowCastingMode.Off)
            {
                flags |= VividMeshletRendererFlags.CastShadows;
            }

            if (terrain.ReceiveShadows)
            {
                flags |= VividMeshletRendererFlags.ReceiveShadows;
            }

            if (targetGameObject.isStatic)
            {
                flags |= VividMeshletRendererFlags.Static;
            }

            return flags;
        }

        private static Bounds TransformBounds(Bounds localBounds, Matrix4x4 objectToWorldMatrix)
        {
            Vector3 center = objectToWorldMatrix.MultiplyPoint3x4(localBounds.center);
            Vector3 extents = localBounds.extents;
            Vector3 axisX = objectToWorldMatrix.MultiplyVector(new Vector3(extents.x, 0.0f, 0.0f));
            Vector3 axisY = objectToWorldMatrix.MultiplyVector(new Vector3(0.0f, extents.y, 0.0f));
            Vector3 axisZ = objectToWorldMatrix.MultiplyVector(new Vector3(0.0f, 0.0f, extents.z));
            extents.x = Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x);
            extents.y = Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y);
            extents.z = Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z);
            return new Bounds(center, extents * 2.0f);
        }
    }
}
