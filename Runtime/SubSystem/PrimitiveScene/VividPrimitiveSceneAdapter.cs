using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Runtime.PrimitiveScene
{
    internal sealed class VividPrimitiveSceneAdapter
    {
        private static readonly ProfilerMarker s_SyncMarker = new("VividRP.PrimitiveScene.Sync");
        private static readonly ProfilerMarker s_RebuildLegacyBridgeMarker = new("VividRP.PrimitiveScene.RebuildLegacyBridge");

        private readonly List<VividMeshletRendererChange> m_Changes = new();
        private readonly List<VividPrimitiveDrawSectionDescriptor> m_SectionDescriptors = new();
        private readonly HashSet<EntityId> m_CurrentPrimitiveIds = new(new EntityIdComparer());
        private readonly List<EntityId> m_RemovedPrimitiveIds = new();
        private bool m_RequiresFullResync = true;

        internal void Synchronize(
            VividPrimitiveScene primitiveScene,
            VividMeshletRendererDatabase database,
            VividGPUDrivenSceneData legacySceneData,
            bool staticDataChanged,
            bool materialDataChanged,
            int frameIndex)
        {
            if (primitiveScene == null)
                throw new ArgumentNullException(nameof(primitiveScene));
            if (database == null)
                throw new ArgumentNullException(nameof(database));
            if (legacySceneData == null)
                throw new ArgumentNullException(nameof(legacySceneData));

            using (s_SyncMarker.Auto())
            {
                primitiveScene.BeginFrame(frameIndex);
                database.ConsumePrimitiveChanges(m_Changes, out bool journalRequiresFullResync);
                bool fullResync = m_RequiresFullResync || journalRequiresFullResync;
                bool rebuildLegacyBridge = fullResync || staticDataChanged || materialDataChanged;
                try
                {
                    if (fullResync)
                    {
                        ReconcileAll(primitiveScene, database);
                        primitiveScene.RecordFullResync();
                    }
                    else
                    {
                        for (int index = 0; index < m_Changes.Count; index++)
                        {
                            VividMeshletRendererChange change = m_Changes[index];
                            if ((change.Flags & (VividMeshletRendererChangeFlags.Added
                                | VividMeshletRendererChangeFlags.Removed
                                | VividMeshletRendererChangeFlags.RenderState
                                | VividMeshletRendererChangeFlags.Resources)) != 0)
                            {
                                rebuildLegacyBridge = true;
                            }
                            ApplyChange(primitiveScene, database, change);
                        }
                    }

                    if (rebuildLegacyBridge)
                        RebuildLegacyBridge(primitiveScene, legacySceneData);
                    m_RequiresFullResync = false;
                }
                catch
                {
                    m_RequiresFullResync = true;
                    throw;
                }
            }
        }

        private void ReconcileAll(
            VividPrimitiveScene primitiveScene,
            VividMeshletRendererDatabase database)
        {
            primitiveScene.CollectSourceEntityIds(m_RemovedPrimitiveIds);
            m_CurrentPrimitiveIds.Clear();
            IReadOnlyList<VividMeshletRendererRenderData> rendererData = database.rendererData;
            IReadOnlyList<VividMeshletRendererResources> rendererResources = database.rendererResources;
            int rendererCount = Mathf.Min(rendererData.Count, rendererResources.Count);
            for (int rendererIndex = 0; rendererIndex < rendererCount; rendererIndex++)
            {
                VividMeshletRendererRenderData trackedData = rendererData[rendererIndex];
                EntityId entityId = trackedData.meshletRendererEntityId;
                if (entityId.Equals(EntityId.None))
                    continue;

                RegisterOrUpdate(primitiveScene, trackedData, rendererResources[rendererIndex]);
                m_CurrentPrimitiveIds.Add(entityId);
            }

            for (int index = 0; index < m_RemovedPrimitiveIds.Count; index++)
            {
                EntityId entityId = m_RemovedPrimitiveIds[index];
                if (!m_CurrentPrimitiveIds.Contains(entityId))
                    primitiveScene.Remove(entityId);
            }
        }

        private void ApplyChange(
            VividPrimitiveScene primitiveScene,
            VividMeshletRendererDatabase database,
            in VividMeshletRendererChange change)
        {
            if ((change.Flags & VividMeshletRendererChangeFlags.Removed) != 0
                || !database.TryGetRendererData(change.EntityId, out VividMeshletRendererRenderData trackedData)
                || !database.TryGetRendererResources(change.EntityId, out VividMeshletRendererResources trackedResources))
            {
                primitiveScene.Remove(change.EntityId);
                return;
            }

            RegisterOrUpdate(primitiveScene, trackedData, trackedResources);
        }

        private void RegisterOrUpdate(
            VividPrimitiveScene primitiveScene,
            in VividMeshletRendererRenderData trackedData,
            in VividMeshletRendererResources trackedResources)
        {
            m_SectionDescriptors.Clear();
            int sectionCount = Mathf.Max(0, trackedData.subMeshCount);
            for (int sourceSectionIndex = 0; sourceSectionIndex < sectionCount; sourceSectionIndex++)
            {
                VividMeshletCollectionAsset geometry = GetArrayValue(
                    trackedResources.MeshletCollections,
                    sourceSectionIndex);
                GPUDrivenMaterialProxy materialProxy = GetClampedArrayValue(
                    trackedResources.MaterialProxies,
                    sourceSectionIndex);
                Material material = GetClampedArrayValue(
                    trackedResources.SharedMaterials,
                    sourceSectionIndex);
                VividPrimitiveResourceKey geometryKey = CreateGeometryKey(
                    geometry,
                    trackedResources.IsTerrain);
                VividPrimitiveResourceKey materialKey = CreateMaterialKey(
                    trackedData.meshletRendererEntityId,
                    trackedResources,
                    materialProxy,
                    material,
                    sourceSectionIndex);
                VividPrimitiveDrawSectionFlags sectionFlags = geometryKey.IsValid
                    ? VividPrimitiveDrawSectionFlags.Valid
                    : VividPrimitiveDrawSectionFlags.None;
                if (trackedResources.IsTerrain)
                    sectionFlags |= VividPrimitiveDrawSectionFlags.Terrain;
                m_SectionDescriptors.Add(new VividPrimitiveDrawSectionDescriptor(
                    sourceSectionIndex,
                    geometryKey,
                    materialKey,
                    sectionFlags));
            }

            VividPrimitiveHandle candidateHandle = trackedResources.MeshletRenderer != null
                ? trackedResources.MeshletRenderer.primitiveHandle
                : trackedResources.Terrain != null
                    ? trackedResources.Terrain.primitiveHandle
                    : VividPrimitiveHandle.Invalid;
            VividPrimitiveHandle handle = primitiveScene.RegisterOrUpdate(
                candidateHandle,
                new VividPrimitiveSourceDescriptor(
                    trackedData.meshletRendererEntityId,
                    trackedData.objectToWorldMatrix,
                    trackedData.worldToObjectMatrix,
                    trackedData.worldBounds,
                    trackedData.renderingLayerMask,
                    trackedData.cameraLayerMask,
                    ExtractPassMask(trackedData.shadowCastingMode),
                    ExtractPrimitiveFlags(trackedData, trackedResources.IsTerrain),
                    m_SectionDescriptors));
            if (trackedResources.MeshletRenderer != null)
                trackedResources.MeshletRenderer.NotifyPrimitiveHandleAssigned(handle);
            if (trackedResources.Terrain != null)
                trackedResources.Terrain.NotifyPrimitiveHandleAssigned(handle);
        }

        internal static void RebuildLegacyBridge(
            VividPrimitiveScene primitiveScene,
            VividGPUDrivenSceneData legacySceneData)
        {
            using (s_RebuildLegacyBridgeMarker.Auto())
            {
                primitiveScene.InvalidateLegacyResourcePayloads();
                primitiveScene.ResizeLegacyInstanceMappings(legacySceneData.InstanceCount);
                primitiveScene.BeginDrawSetSourceRebuild();
                IReadOnlyList<VividGPUDrivenInstanceSourceData> sources = legacySceneData.InstanceSources;
                int sourceCount = Mathf.Min(legacySceneData.InstanceCount, sources.Count);
                for (int instanceIndex = 0; instanceIndex < legacySceneData.InstanceCount; instanceIndex++)
                {
                    VividLegacyInstanceMappingData mapping = CreateInvalidLegacyMapping();
                    if (instanceIndex < sourceCount)
                    {
                        VividGPUDrivenInstanceSourceData source = sources[instanceIndex];
                        VividInstanceData legacyInstance = legacySceneData.Instances[instanceIndex];
                        VividPrimitiveResourceKey geometryKey = CreateGeometryKey(source);
                        VividPrimitiveResourceKey materialKey = CreateMaterialKey(source);
                        primitiveScene.UpdateGeometryPayload(geometryKey, legacyInstance);
                        bool hasLegacyMaterial = legacyInstance.MaterialIndex < (uint) legacySceneData.MaterialCount;
                        VividMaterialData legacyMaterial = hasLegacyMaterial
                            ? legacySceneData.Materials[(int) legacyInstance.MaterialIndex]
                            : default;
                        if (hasLegacyMaterial)
                        {
                            primitiveScene.UpdateMaterialPayload(
                                materialKey,
                                legacyInstance.MaterialIndex,
                                legacyMaterial);
                        }

                        if (primitiveScene.TryGetAbsoluteDrawSectionIndex(
                            source.PrimitiveEntityId,
                            source.SourceSectionIndex,
                            out VividPrimitiveHandle primitiveHandle,
                            out int drawSectionIndex))
                        {
                            mapping = new VividLegacyInstanceMappingData
                            {
                                PrimitiveIndex = (uint) primitiveHandle.Index,
                                PrimitiveGeneration = primitiveHandle.Generation,
                                DrawSectionIndex = (uint) drawSectionIndex,
                                Flags = 1u,
                            };

                            if (hasLegacyMaterial
                                && primitiveScene.IsDrawSectionRenderable(drawSectionIndex))
                            {
                                primitiveScene.SetDrawSetSource(
                                    drawSectionIndex,
                                    new VividPrimitiveDrawSourceData
                                    {
                                        PrimitiveHandle = primitiveHandle,
                                        AbsoluteDrawSectionIndex = (uint) drawSectionIndex,
                                        LegacyInstanceIndex = (uint) instanceIndex,
                                        RendererListID = legacyMaterial.RendererListID,
                                        Flags = VividPrimitiveDrawSourceFlags.Valid,
                                    });
                            }
                        }
                    }
                    primitiveScene.SetLegacyInstanceMapping(instanceIndex, mapping);
                }
            }
        }

        private static VividPrimitiveFlags ExtractPrimitiveFlags(
            in VividMeshletRendererRenderData trackedData,
            bool isTerrain)
        {
            VividMeshletRendererFlags rendererFlags = trackedData.flags;
            VividPrimitiveFlags flags = VividPrimitiveFlags.None;
            if ((rendererFlags & VividMeshletRendererFlags.Valid) != 0)
                flags |= VividPrimitiveFlags.Valid;
            if ((rendererFlags & VividMeshletRendererFlags.Static) != 0)
                flags |= VividPrimitiveFlags.Static;
            if ((rendererFlags & VividMeshletRendererFlags.Skinned) != 0)
                flags |= VividPrimitiveFlags.Skinned;
            if ((rendererFlags & VividMeshletRendererFlags.ReceiveShadows) != 0)
                flags |= VividPrimitiveFlags.ReceiveShadows;
            if (trackedData.shadowCastingMode == ShadowCastingMode.TwoSided)
                flags |= VividPrimitiveFlags.TwoSidedShadows;
            if (isTerrain)
                flags |= VividPrimitiveFlags.Terrain;

            bool disabled = (rendererFlags & VividMeshletRendererFlags.ActiveInHierarchy) == 0
                || (rendererFlags & VividMeshletRendererFlags.Enabled) == 0
                || (rendererFlags & VividMeshletRendererFlags.SourceRendererEnabled) == 0;
            if (disabled)
                flags |= VividPrimitiveFlags.Disabled;
            if (trackedData.objectToWorldMatrix.determinant < 0.0f)
                flags |= VividPrimitiveFlags.FlipWindingOrder;
            return flags;
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

        private static VividPrimitiveResourceKey CreateGeometryKey(
            VividMeshletCollectionAsset geometry,
            bool isTerrain)
        {
            if (geometry == null)
                return VividPrimitiveResourceKey.Invalid;
            return new VividPrimitiveResourceKey(
                isTerrain
                    ? VividPrimitiveResourceDomain.TerrainGeometry
                    : VividPrimitiveResourceDomain.MeshletGeometry,
                geometry.GetEntityId(),
                EntityId.None,
                -1);
        }

        private static VividPrimitiveResourceKey CreateMaterialKey(
            EntityId primitiveEntityId,
            in VividMeshletRendererResources resources,
            GPUDrivenMaterialProxy materialProxy,
            Material material,
            int sourceSectionIndex)
        {
            if (resources.IsTerrain)
            {
                return new VividPrimitiveResourceKey(
                    VividPrimitiveResourceDomain.TerrainMaterial,
                    primitiveEntityId,
                    EntityId.None,
                    -1);
            }
            if (materialProxy != null)
            {
                return new VividPrimitiveResourceKey(
                    VividPrimitiveResourceDomain.MaterialProxy,
                    materialProxy.GetEntityId(),
                    EntityId.None,
                    -1);
            }
            if (material != null)
            {
                return new VividPrimitiveResourceKey(
                    VividPrimitiveResourceDomain.UnityMaterial,
                    material.GetEntityId(),
                    EntityId.None,
                    -1);
            }
            return new VividPrimitiveResourceKey(
                VividPrimitiveResourceDomain.MissingMaterial,
                EntityId.None,
                primitiveEntityId,
                sourceSectionIndex);
        }

        private static VividPrimitiveResourceKey CreateGeometryKey(
            in VividGPUDrivenInstanceSourceData source)
        {
            if (source.GeometryEntityId.Equals(EntityId.None))
                return VividPrimitiveResourceKey.Invalid;
            return new VividPrimitiveResourceKey(
                (source.Flags & VividGPUDrivenInstanceSourceFlags.TerrainGeometry) != 0
                    ? VividPrimitiveResourceDomain.TerrainGeometry
                    : VividPrimitiveResourceDomain.MeshletGeometry,
                source.GeometryEntityId,
                EntityId.None,
                -1);
        }

        private static VividPrimitiveResourceKey CreateMaterialKey(
            in VividGPUDrivenInstanceSourceData source)
        {
            if ((source.Flags & VividGPUDrivenInstanceSourceFlags.TerrainMaterial) != 0
                && !source.MaterialEntityId.Equals(EntityId.None))
            {
                return new VividPrimitiveResourceKey(
                    VividPrimitiveResourceDomain.TerrainMaterial,
                    source.MaterialEntityId,
                    EntityId.None,
                    -1);
            }
            if ((source.Flags & VividGPUDrivenInstanceSourceFlags.MaterialProxy) != 0
                && !source.MaterialEntityId.Equals(EntityId.None))
            {
                return new VividPrimitiveResourceKey(
                    VividPrimitiveResourceDomain.MaterialProxy,
                    source.MaterialEntityId,
                    EntityId.None,
                    -1);
            }
            if ((source.Flags & VividGPUDrivenInstanceSourceFlags.MissingMaterial) == 0
                && !source.MaterialEntityId.Equals(EntityId.None))
            {
                return new VividPrimitiveResourceKey(
                    VividPrimitiveResourceDomain.UnityMaterial,
                    source.MaterialEntityId,
                    EntityId.None,
                    -1);
            }
            return new VividPrimitiveResourceKey(
                VividPrimitiveResourceDomain.MissingMaterial,
                EntityId.None,
                source.PrimitiveEntityId,
                source.SourceSectionIndex);
        }

        private static VividLegacyInstanceMappingData CreateInvalidLegacyMapping()
        {
            return new VividLegacyInstanceMappingData
            {
                PrimitiveIndex = VividPrimitiveScene.InvalidIndex,
                PrimitiveGeneration = 0u,
                DrawSectionIndex = VividPrimitiveScene.InvalidIndex,
                Flags = 0u,
            };
        }

        private static T GetArrayValue<T>(T[] values, int index)
            where T : UnityEngine.Object
        {
            return values != null && (uint) index < (uint) values.Length ? values[index] : null;
        }

        private static T GetClampedArrayValue<T>(T[] values, int index)
            where T : UnityEngine.Object
        {
            if (values == null || values.Length == 0)
                return null;
            return values[Mathf.Clamp(index, 0, values.Length - 1)];
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
    }
}
