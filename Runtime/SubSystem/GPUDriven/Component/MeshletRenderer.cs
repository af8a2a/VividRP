using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Runtime.GPUDriven
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class MeshletRenderer : MonoBehaviour
    {
        [SerializeField]
        private Renderer m_SourceRenderer;

        [SerializeField]
        [HideInInspector]
        private Mesh m_SourceMesh;

        [SerializeField]
        private VividMeshletCollectionAsset[] m_MeshletCollections = Array.Empty<VividMeshletCollectionAsset>();

        [SerializeField]
        private GPUDrivenMaterialProxy[] m_MaterialProxies = Array.Empty<GPUDrivenMaterialProxy>();

        [SerializeField]
        private bool m_TakeOverSourceRenderer = true;

        [NonSerialized]
        private Renderer m_TakenOverSourceRenderer;

        [NonSerialized]
        private bool m_CachedSourceRendererForceRenderingOff;

        [NonSerialized]
        private bool m_HasCachedSourceRendererForceRenderingOff;

        public Renderer sourceRenderer => m_SourceRenderer;

        public Mesh sourceMesh => m_SourceMesh;

        public IReadOnlyList<VividMeshletCollectionAsset> meshletCollections => m_MeshletCollections;

        public IReadOnlyList<GPUDrivenMaterialProxy> materialProxies => m_MaterialProxies;

        public bool takeOverSourceRenderer => m_TakeOverSourceRenderer;

        public int subMeshCount => m_SourceMesh != null ? Mathf.Max(1, m_SourceMesh.subMeshCount) : 0;

        public Bounds localBounds => m_SourceMesh != null ? m_SourceMesh.bounds : default;

        public bool RefreshSource()
        {
            if (m_SourceRenderer != null && TryExtractMesh(m_SourceRenderer, out Mesh mesh))
            {
                return SetSource(m_SourceRenderer, mesh);
            }

            if (TryFindPreferredRenderer(out Renderer renderer) && TryExtractMesh(renderer, out mesh))
            {
                return SetSource(renderer, mesh);
            }

            return ClearSource();
        }

        public bool SetSourceRenderer(Renderer renderer)
        {
            if (renderer == null)
            {
                return ClearSource();
            }

            if (!TryExtractMesh(renderer, out Mesh mesh))
            {
                return false;
            }

            return SetSource(renderer, mesh);
        }

        public bool SetMeshletCollections(VividMeshletCollectionAsset[] meshletCollections)
        {
            int expectedCount = subMeshCount;
            if (expectedCount <= 0)
            {
                bool cleared = m_MeshletCollections is { Length: > 0 };
                m_MeshletCollections = Array.Empty<VividMeshletCollectionAsset>();
                return cleared;
            }

            var sanitizedCollections = new VividMeshletCollectionAsset[expectedCount];
            if (meshletCollections != null)
            {
                Array.Copy(meshletCollections, sanitizedCollections, Mathf.Min(expectedCount, meshletCollections.Length));
            }

            if (AreCollectionsEqual(m_MeshletCollections, sanitizedCollections))
            {
                return false;
            }

            m_MeshletCollections = sanitizedCollections;
            return true;
        }

        public bool SetMaterialProxies(GPUDrivenMaterialProxy[] materialProxies)
        {
            int expectedCount = subMeshCount;
            if (expectedCount <= 0)
            {
                bool cleared = m_MaterialProxies is { Length: > 0 };
                m_MaterialProxies = Array.Empty<GPUDrivenMaterialProxy>();
                return cleared;
            }

            var sanitizedProxies = new GPUDrivenMaterialProxy[expectedCount];
            if (materialProxies != null)
            {
                Array.Copy(materialProxies, sanitizedProxies, Mathf.Min(expectedCount, materialProxies.Length));
            }

            if (AreMaterialProxiesEqual(m_MaterialProxies, sanitizedProxies))
            {
                return false;
            }

            m_MaterialProxies = sanitizedProxies;
            return true;
        }

        public bool SetTakeOverSourceRenderer(bool takeOverSourceRenderer)
        {
            if (m_TakeOverSourceRenderer == takeOverSourceRenderer)
            {
                return false;
            }

            m_TakeOverSourceRenderer = takeOverSourceRenderer;
            SyncSourceRendererTakeoverState();
            return true;
        }

        public VividMeshletCollectionAsset GetMeshletCollection(int subMeshIndex)
        {
            if (subMeshIndex < 0 || m_MeshletCollections == null || subMeshIndex >= m_MeshletCollections.Length)
            {
                return null;
            }

            return m_MeshletCollections[subMeshIndex];
        }

        public GPUDrivenMaterialProxy GetMaterialProxy(int subMeshIndex)
        {
            if (subMeshIndex < 0 || m_MaterialProxies == null || subMeshIndex >= m_MaterialProxies.Length)
            {
                return null;
            }

            return m_MaterialProxies[subMeshIndex];
        }

        public bool TryValidate(out string validationMessage)
        {
            if (!TryValidateRuntimeBindings(out validationMessage))
            {
                return false;
            }

            if (!m_TakeOverSourceRenderer)
            {
                validationMessage = string.Empty;
                return true;
            }

            int expectedCount = Mathf.Max(1, m_SourceMesh.subMeshCount);
            int actualCount = m_MaterialProxies?.Length ?? 0;
            if (actualCount != expectedCount)
            {
                validationMessage =
                    $"Expected {expectedCount} GPUDriven material proxies for '{m_SourceMesh.name}', but found {actualCount}.";
                return false;
            }

            for (int subMeshIndex = 0; subMeshIndex < m_MaterialProxies.Length; subMeshIndex++)
            {
                if (m_MaterialProxies[subMeshIndex] != null)
                {
                    continue;
                }

                validationMessage =
                    $"Missing GPUDriven material proxy for submesh {subMeshIndex} on '{m_SourceMesh.name}'.";
                return false;
            }

            validationMessage = string.Empty;
            return true;
        }

        internal bool TryValidateRuntimeBindings(out string validationMessage)
        {
            if (m_SourceMesh == null)
            {
                validationMessage =
                    "Source Mesh is not resolved. Attach a MeshRenderer or SkinnedMeshRenderer, or assign a compatible Renderer.";
                return false;
            }

            int expectedCount = Mathf.Max(1, m_SourceMesh.subMeshCount);
            int actualCount = m_MeshletCollections?.Length ?? 0;
            if (actualCount != expectedCount)
            {
                validationMessage = $"Expected {expectedCount} meshlet assets for '{m_SourceMesh.name}', but found {actualCount}.";
                return false;
            }

            for (int subMeshIndex = 0; subMeshIndex < m_MeshletCollections.Length; subMeshIndex++)
            {
                VividMeshletCollectionAsset meshletCollection = m_MeshletCollections[subMeshIndex];
                if (meshletCollection == null)
                {
                    validationMessage = $"Missing meshlet asset for submesh {subMeshIndex} on '{m_SourceMesh.name}'.";
                    return false;
                }

                if (meshletCollection.SourceSubmeshIndex != subMeshIndex)
                {
                    validationMessage =
                        $"Meshlet asset '{meshletCollection.name}' targets submesh {meshletCollection.SourceSubmeshIndex}, expected {subMeshIndex}.";
                    return false;
                }
            }

            validationMessage = string.Empty;
            return true;
        }

        internal static bool TryExtractMesh(Renderer renderer, out Mesh mesh)
        {
            switch (renderer)
            {
                case SkinnedMeshRenderer skinnedMeshRenderer:
                    mesh = skinnedMeshRenderer.sharedMesh;
                    return mesh != null;
                case MeshRenderer meshRenderer when meshRenderer.TryGetComponent(out MeshFilter meshFilter):
                    mesh = meshFilter.sharedMesh;
                    return mesh != null;
                default:
                    mesh = null;
                    return false;
            }
        }

        private void Reset()
        {
            RefreshSource();
        }

        private void OnEnable()
        {
            RefreshSource();
            SyncDatabaseRegistration();
        }

        private void LateUpdate()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            SyncSourceRendererTakeoverState();
            VividMeshletRendererDatabase.instance.UpdateRendererData(this);
        }

        private void OnDisable()
        {
            RestoreSourceRendererTakeover();
            VividMeshletRendererDatabase.instance.UnregisterRenderer(this);
        }

        private void OnDestroy()
        {
            RestoreSourceRendererTakeover();
            VividMeshletRendererDatabase.instance.UnregisterRenderer(this);
        }

        private void OnValidate()
        {
            RefreshSource();
            SyncDatabaseRegistration();
        }

        private bool SetSource(Renderer renderer, Mesh mesh)
        {
            bool rendererChanged = m_SourceRenderer != renderer;
            bool meshChanged = m_SourceMesh != mesh;

            m_SourceRenderer = renderer;
            m_SourceMesh = mesh;

            bool collectionsChanged = EnsureMeshletCollectionArraySize();
            bool proxiesChanged = EnsureMaterialProxyArraySize();
            return rendererChanged || meshChanged || collectionsChanged || proxiesChanged;
        }

        private bool ClearSource()
        {
            bool changed = m_SourceRenderer != null
                || m_SourceMesh != null
                || (m_MeshletCollections?.Length ?? 0) > 0
                || (m_MaterialProxies?.Length ?? 0) > 0;
            m_SourceRenderer = null;
            m_SourceMesh = null;
            m_MeshletCollections = Array.Empty<VividMeshletCollectionAsset>();
            m_MaterialProxies = Array.Empty<GPUDrivenMaterialProxy>();
            return changed;
        }

        private bool EnsureMeshletCollectionArraySize()
        {
            int expectedCount = subMeshCount;
            if (expectedCount <= 0)
            {
                bool cleared = m_MeshletCollections is { Length: > 0 };
                m_MeshletCollections = Array.Empty<VividMeshletCollectionAsset>();
                return cleared;
            }

            if (m_MeshletCollections != null && m_MeshletCollections.Length == expectedCount)
            {
                return false;
            }

            var resizedCollections = new VividMeshletCollectionAsset[expectedCount];
            if (m_MeshletCollections != null)
            {
                Array.Copy(m_MeshletCollections, resizedCollections, Mathf.Min(expectedCount, m_MeshletCollections.Length));
            }

            m_MeshletCollections = resizedCollections;
            return true;
        }

        private bool EnsureMaterialProxyArraySize()
        {
            int expectedCount = subMeshCount;
            if (expectedCount <= 0)
            {
                bool cleared = m_MaterialProxies is { Length: > 0 };
                m_MaterialProxies = Array.Empty<GPUDrivenMaterialProxy>();
                return cleared;
            }

            if (m_MaterialProxies != null && m_MaterialProxies.Length == expectedCount)
            {
                return false;
            }

            var resizedMaterialProxies = new GPUDrivenMaterialProxy[expectedCount];
            if (m_MaterialProxies != null)
            {
                Array.Copy(m_MaterialProxies, resizedMaterialProxies, Mathf.Min(expectedCount, m_MaterialProxies.Length));
            }

            m_MaterialProxies = resizedMaterialProxies;
            return true;
        }

        private bool TryFindPreferredRenderer(out Renderer renderer)
        {
            if (TryGetComponent(out SkinnedMeshRenderer skinnedMeshRenderer) && TryExtractMesh(skinnedMeshRenderer, out _))
            {
                renderer = skinnedMeshRenderer;
                return true;
            }

            if (TryGetComponent(out MeshRenderer meshRenderer) && TryExtractMesh(meshRenderer, out _))
            {
                renderer = meshRenderer;
                return true;
            }

            Renderer candidate = null;
            foreach (Renderer childRenderer in GetComponentsInChildren<Renderer>(true))
            {
                if (!TryExtractMesh(childRenderer, out _))
                {
                    continue;
                }

                if (candidate != null && candidate != childRenderer)
                {
                    renderer = null;
                    return false;
                }

                candidate = childRenderer;
            }

            renderer = candidate;
            return renderer != null;
        }

        private static bool AreCollectionsEqual(
            VividMeshletCollectionAsset[] lhs,
            VividMeshletCollectionAsset[] rhs
        )
        {
            if (ReferenceEquals(lhs, rhs))
            {
                return true;
            }

            if (lhs == null || rhs == null || lhs.Length != rhs.Length)
            {
                return false;
            }

            for (int index = 0; index < lhs.Length; index++)
            {
                if (lhs[index] != rhs[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AreMaterialProxiesEqual(
            GPUDrivenMaterialProxy[] lhs,
            GPUDrivenMaterialProxy[] rhs
        )
        {
            if (ReferenceEquals(lhs, rhs))
            {
                return true;
            }

            if (lhs == null || rhs == null || lhs.Length != rhs.Length)
            {
                return false;
            }

            for (int index = 0; index < lhs.Length; index++)
            {
                if (lhs[index] != rhs[index])
                {
                    return false;
                }
            }

            return true;
        }

        private void SyncDatabaseRegistration()
        {
            SyncSourceRendererTakeoverState();

            if (isActiveAndEnabled)
            {
                VividMeshletRendererDatabase.instance.UpdateRendererData(this);
                return;
            }

            VividMeshletRendererDatabase.instance.UnregisterRenderer(this);
        }

        private void SyncSourceRendererTakeoverState()
        {
            Renderer currentSourceRenderer = m_SourceRenderer;
            if (m_TakenOverSourceRenderer != null && m_TakenOverSourceRenderer != currentSourceRenderer)
            {
                RestoreSourceRendererTakeover();
            }

            if (!ShouldTakeOverSourceRenderer())
            {
                RestoreSourceRendererTakeover();
                return;
            }

            if (currentSourceRenderer == null)
            {
                RestoreSourceRendererTakeover();
                return;
            }

            if (m_TakenOverSourceRenderer != currentSourceRenderer || !m_HasCachedSourceRendererForceRenderingOff)
            {
                m_TakenOverSourceRenderer = currentSourceRenderer;
                m_CachedSourceRendererForceRenderingOff = currentSourceRenderer.forceRenderingOff;
                m_HasCachedSourceRendererForceRenderingOff = true;
            }

            currentSourceRenderer.forceRenderingOff = true;
        }

        private void RestoreSourceRendererTakeover()
        {
            if (m_TakenOverSourceRenderer != null && m_HasCachedSourceRendererForceRenderingOff)
            {
                m_TakenOverSourceRenderer.forceRenderingOff = m_CachedSourceRendererForceRenderingOff;
            }

            m_TakenOverSourceRenderer = null;
            m_HasCachedSourceRendererForceRenderingOff = false;
            m_CachedSourceRendererForceRenderingOff = false;
        }

        private bool ShouldTakeOverSourceRenderer()
        {
            if (!m_TakeOverSourceRenderer || !isActiveAndEnabled || m_SourceRenderer == null)
            {
                return false;
            }

            if (!TryValidate(out _))
            {
                return false;
            }

            return IsGPUDrivenPipelineEnabled();
        }

        private static bool IsGPUDrivenPipelineEnabled()
        {
            RenderPipelineAsset currentRenderPipeline = GraphicsSettings.currentRenderPipeline;
            if (currentRenderPipeline == null)
            {
                return false;
            }

            Type pipelineAssetType = currentRenderPipeline.GetType();
            if (!string.Equals(pipelineAssetType.FullName, "VividRP.Runtime.VividRenderPipelineAsset", StringComparison.Ordinal))
            {
                return false;
            }

            var property = pipelineAssetType.GetProperty("EnableGPUDriven");
            if (property == null || property.PropertyType != typeof(bool))
            {
                return false;
            }

            return property.GetValue(currentRenderPipeline) is bool enableGPUDriven && enableGPUDriven;
        }
    }
}
