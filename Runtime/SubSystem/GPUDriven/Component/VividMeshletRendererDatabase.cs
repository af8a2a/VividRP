using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Runtime.GPUDriven
{
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
            Material[] sharedMaterials,
            VividMeshletCollectionAsset[] meshletCollections
        )
        {
            MeshletRenderer = meshletRenderer;
            SourceRenderer = sourceRenderer;
            SourceMesh = sourceMesh;
            SharedMaterials = sharedMaterials ?? Array.Empty<Material>();
            MeshletCollections = meshletCollections ?? Array.Empty<VividMeshletCollectionAsset>();
        }

        public MeshletRenderer MeshletRenderer { get; }

        public Renderer SourceRenderer { get; }

        public Mesh SourceMesh { get; }

        public Material[] SharedMaterials { get; }

        public VividMeshletCollectionAsset[] MeshletCollections { get; }
    }

    public sealed class VividMeshletRendererDatabase
    {
        private readonly List<VividMeshletRendererRenderData> m_RendererData = new();
        private readonly List<VividMeshletRendererResources> m_RendererResources = new();
        private readonly Dictionary<EntityId, int> m_EntityIdToDataIndex = new();

        public static VividMeshletRendererDatabase instance => Singleton<VividMeshletRendererDatabase>.instance;

        public int rendererCount => m_RendererData.Count;

        public IReadOnlyList<VividMeshletRendererRenderData> rendererData => m_RendererData;

        public IReadOnlyList<VividMeshletRendererResources> rendererResources => m_RendererResources;

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
            StoreRendererData(trackedData, trackedResources);
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

            EntityId meshletRendererEntityId = meshletRenderer.GetEntityId();
            if (meshletRendererEntityId.Equals(EntityId.None)
                || !m_EntityIdToDataIndex.TryGetValue(meshletRendererEntityId, out int removedIndex))
            {
                return;
            }

            RemoveRendererAt(removedIndex);
        }

        internal void Clear()
        {
            m_RendererData.Clear();
            m_RendererResources.Clear();
            m_EntityIdToDataIndex.Clear();
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

        private void StoreRendererData(
            in VividMeshletRendererRenderData trackedData,
            in VividMeshletRendererResources trackedResources
        )
        {
            if (trackedData.meshletRendererEntityId.Equals(EntityId.None))
            {
                return;
            }

            if (m_EntityIdToDataIndex.TryGetValue(trackedData.meshletRendererEntityId, out int dataIndex))
            {
                m_RendererData[dataIndex] = trackedData;
                m_RendererResources[dataIndex] = trackedResources;
                return;
            }

            dataIndex = m_RendererData.Count;
            m_RendererData.Add(trackedData);
            m_RendererResources.Add(trackedResources);
            m_EntityIdToDataIndex.Add(trackedData.meshletRendererEntityId, dataIndex);
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
        }

        private static VividMeshletRendererRenderData CreateRendererData(MeshletRenderer meshletRenderer)
        {
            meshletRenderer.RefreshSource();

            Renderer sourceRenderer = meshletRenderer.sourceRenderer;
            Mesh sourceMesh = meshletRenderer.sourceMesh;
            bool isValid = meshletRenderer.TryValidate(out _);
            Matrix4x4 objectToWorldMatrix = sourceRenderer != null
                ? sourceRenderer.localToWorldMatrix
                : meshletRenderer.transform.localToWorldMatrix;
            Matrix4x4 worldToObjectMatrix = sourceRenderer != null
                ? sourceRenderer.worldToLocalMatrix
                : meshletRenderer.transform.worldToLocalMatrix;
            Bounds localBounds = sourceMesh != null ? sourceMesh.bounds : default;
            Bounds worldBounds = sourceRenderer != null
                ? sourceRenderer.bounds
                : TransformBounds(localBounds, objectToWorldMatrix);
            Material[] sharedMaterials = sourceRenderer != null ? sourceRenderer.sharedMaterials : Array.Empty<Material>();

            return new VividMeshletRendererRenderData
            {
                meshletRendererEntityId = meshletRenderer.GetEntityId(),
                sourceRendererEntityId = sourceRenderer != null ? sourceRenderer.GetEntityId() : EntityId.None,
                sourceMeshEntityId = sourceMesh != null ? sourceMesh.GetEntityId() : EntityId.None,
                objectToWorldMatrix = objectToWorldMatrix,
                worldToObjectMatrix = worldToObjectMatrix,
                localBounds = localBounds,
                worldBounds = worldBounds,
                renderingLayerMask = sourceRenderer != null ? (uint) sourceRenderer.renderingLayerMask : 0u,
                shadowCastingMode = sourceRenderer != null ? sourceRenderer.shadowCastingMode : ShadowCastingMode.Off,
                motionVectorGenerationMode = sourceRenderer != null
                    ? sourceRenderer.motionVectorGenerationMode
                    : MotionVectorGenerationMode.Camera,
                flags = BuildFlags(meshletRenderer, sourceRenderer, isValid),
                subMeshCount = sourceMesh != null ? Mathf.Max(1, sourceMesh.subMeshCount) : 0,
                materialCount = sharedMaterials.Length,
            };
        }

        private static VividMeshletRendererResources CreateRendererResources(MeshletRenderer meshletRenderer)
        {
            Renderer sourceRenderer = meshletRenderer.sourceRenderer;
            Material[] sharedMaterials = sourceRenderer != null ? sourceRenderer.sharedMaterials : Array.Empty<Material>();

            int meshletCollectionCount = meshletRenderer.meshletCollections.Count;
            var meshletCollections = new VividMeshletCollectionAsset[meshletCollectionCount];
            for (int index = 0; index < meshletCollectionCount; index++)
            {
                meshletCollections[index] = meshletRenderer.GetMeshletCollection(index);
            }

            return new VividMeshletRendererResources(
                meshletRenderer,
                sourceRenderer,
                meshletRenderer.sourceMesh,
                sharedMaterials,
                meshletCollections
            );
        }

        private static VividMeshletRendererFlags BuildFlags(
            MeshletRenderer meshletRenderer,
            Renderer sourceRenderer,
            bool isValid
        )
        {
            VividMeshletRendererFlags flags = VividMeshletRendererFlags.None;
            GameObject targetGameObject = sourceRenderer != null ? sourceRenderer.gameObject : meshletRenderer.gameObject;

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

            if (sourceRenderer != null && sourceRenderer.enabled)
            {
                flags |= VividMeshletRendererFlags.SourceRendererEnabled;
            }

            if (sourceRenderer != null && sourceRenderer.shadowCastingMode != ShadowCastingMode.Off)
            {
                flags |= VividMeshletRendererFlags.CastShadows;
            }

            if (sourceRenderer != null && sourceRenderer.receiveShadows)
            {
                flags |= VividMeshletRendererFlags.ReceiveShadows;
            }

            if (targetGameObject.isStatic)
            {
                flags |= VividMeshletRendererFlags.Static;
            }

            if (sourceRenderer is SkinnedMeshRenderer)
            {
                flags |= VividMeshletRendererFlags.Skinned;
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
