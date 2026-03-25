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
        [HideInInspector]
        private Mesh m_SourceMesh;

        [SerializeField]
        private Material[] m_SourceMaterials = Array.Empty<Material>();

        [SerializeField]
        private bool m_SourceRenderingEnabled = true;

        [SerializeField]
        private ShadowCastingMode m_ShadowCastingMode = ShadowCastingMode.On;

        [SerializeField]
        private bool m_ReceiveShadows = true;

        [SerializeField]
        private MotionVectorGenerationMode m_MotionVectorGenerationMode = MotionVectorGenerationMode.Object;

        [SerializeField]
        private uint m_RenderingLayerMask = 1u;

        [SerializeField]
        private bool m_SourceWasSkinned;

        [SerializeField]
        private VividMeshletCollectionAsset[] m_MeshletCollections = Array.Empty<VividMeshletCollectionAsset>();

        [SerializeField]
        private GPUDrivenMaterialProxy[] m_MaterialProxies = Array.Empty<GPUDrivenMaterialProxy>();

        [SerializeField]
        private bool m_TakeOverSourceRenderer = true;

        public Renderer sourceRenderer => null;

        public Mesh sourceMesh => m_SourceMesh;

        public IReadOnlyList<Material> sourceMaterials => m_SourceMaterials;

        public bool sourceRenderingEnabled => m_SourceRenderingEnabled;

        public ShadowCastingMode shadowCastingMode => m_ShadowCastingMode;

        public bool receiveShadows => m_ReceiveShadows;

        public MotionVectorGenerationMode motionVectorGenerationMode => m_MotionVectorGenerationMode;

        public uint renderingLayerMask => m_RenderingLayerMask;

        public bool sourceWasSkinned => m_SourceWasSkinned;

        public IReadOnlyList<VividMeshletCollectionAsset> meshletCollections => m_MeshletCollections;

        public IReadOnlyList<GPUDrivenMaterialProxy> materialProxies => m_MaterialProxies;

        public bool takeOverSourceRenderer => m_TakeOverSourceRenderer;

        public int subMeshCount => m_SourceMesh != null ? Mathf.Max(1, m_SourceMesh.subMeshCount) : 0;

        public Bounds localBounds => m_SourceMesh != null ? m_SourceMesh.bounds : default;

        public bool RefreshSource()
        {
            bool materialsChanged = EnsureSourceMaterialArraySize();
            bool collectionsChanged = EnsureMeshletCollectionArraySize();
            bool proxiesChanged = EnsureMaterialProxyArraySize();
            return materialsChanged || collectionsChanged || proxiesChanged;
        }

        public bool CaptureSourceFromGameObject()
        {
            if (!TryGetAttachedRenderer(out Renderer renderer))
            {
                return false;
            }

            return CaptureSourceFromRenderer(renderer);
        }

        public bool CaptureSourceFromRenderer(Renderer renderer)
        {
            if (renderer == null || !TryExtractMesh(renderer, out Mesh mesh))
            {
                return false;
            }

            bool meshChanged = m_SourceMesh != mesh;
            m_SourceMesh = mesh;

            bool materialsChanged = SetSourceMaterials(renderer.sharedMaterials);
            bool stateChanged = CaptureRendererState(renderer);
            bool collectionsChanged = EnsureMeshletCollectionArraySize();
            bool proxiesChanged = EnsureMaterialProxyArraySize();
            return meshChanged || materialsChanged || stateChanged || collectionsChanged || proxiesChanged;
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

        public bool SetSourceMaterials(Material[] sourceMaterials)
        {
            int expectedCount = subMeshCount;
            if (expectedCount <= 0)
            {
                bool cleared = m_SourceMaterials is { Length: > 0 };
                m_SourceMaterials = Array.Empty<Material>();
                return cleared;
            }

            var sanitizedMaterials = new Material[expectedCount];
            if (sourceMaterials != null)
            {
                Array.Copy(sourceMaterials, sanitizedMaterials, Mathf.Min(expectedCount, sourceMaterials.Length));
            }

            if (AreMaterialsEqual(m_SourceMaterials, sanitizedMaterials))
            {
                return false;
            }

            m_SourceMaterials = sanitizedMaterials;
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

        public Material GetSourceMaterial(int subMeshIndex)
        {
            if (subMeshIndex < 0 || m_SourceMaterials == null || subMeshIndex >= m_SourceMaterials.Length)
            {
                return null;
            }

            return m_SourceMaterials[subMeshIndex];
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
                    "Source Mesh is not assigned or captured. Capture source data from an attached Renderer before enabling GPUDriven rendering.";
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

            VividMeshletRendererDatabase.instance.UpdateRendererData(this);
        }

        private void OnDisable()
        {
            VividMeshletRendererDatabase.instance.UnregisterRenderer(this);
        }

        private void OnDestroy()
        {
            VividMeshletRendererDatabase.instance.UnregisterRenderer(this);
        }
#if UNITY_EDITOR
        private void OnValidate()
        {
            RefreshSource();
            SyncDatabaseRegistration();
        }
#endif
        private bool EnsureSourceMaterialArraySize()
        {
            int expectedCount = subMeshCount;
            if (expectedCount <= 0)
            {
                bool cleared = m_SourceMaterials is { Length: > 0 };
                m_SourceMaterials = Array.Empty<Material>();
                return cleared;
            }

            if (m_SourceMaterials != null && m_SourceMaterials.Length == expectedCount)
            {
                return false;
            }

            var resizedMaterials = new Material[expectedCount];
            if (m_SourceMaterials != null)
            {
                Array.Copy(m_SourceMaterials, resizedMaterials, Mathf.Min(expectedCount, m_SourceMaterials.Length));
            }

            m_SourceMaterials = resizedMaterials;
            return true;
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

        private bool TryGetAttachedRenderer(out Renderer renderer)
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

            renderer = null;
            return false;
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

        private static bool AreMaterialsEqual(
            Material[] lhs,
            Material[] rhs
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

        private bool CaptureRendererState(Renderer renderer)
        {
            bool sourceRenderingEnabled = renderer.enabled;
            ShadowCastingMode shadowCastingMode = renderer.shadowCastingMode;
            bool receiveShadows = renderer.receiveShadows;
            MotionVectorGenerationMode motionVectorGenerationMode = renderer.motionVectorGenerationMode;
            uint renderingLayerMask = (uint) renderer.renderingLayerMask;
            bool sourceWasSkinned = renderer is SkinnedMeshRenderer;

            bool changed = m_SourceRenderingEnabled != sourceRenderingEnabled
                || m_ShadowCastingMode != shadowCastingMode
                || m_ReceiveShadows != receiveShadows
                || m_MotionVectorGenerationMode != motionVectorGenerationMode
                || m_RenderingLayerMask != renderingLayerMask
                || m_SourceWasSkinned != sourceWasSkinned;

            m_SourceRenderingEnabled = sourceRenderingEnabled;
            m_ShadowCastingMode = shadowCastingMode;
            m_ReceiveShadows = receiveShadows;
            m_MotionVectorGenerationMode = motionVectorGenerationMode;
            m_RenderingLayerMask = renderingLayerMask;
            m_SourceWasSkinned = sourceWasSkinned;
            return changed;
        }

        private void SyncDatabaseRegistration()
        {
            if (isActiveAndEnabled)
            {
                VividMeshletRendererDatabase.instance.UpdateRendererData(this);
                return;
            }

            VividMeshletRendererDatabase.instance.UnregisterRenderer(this);
        }

        internal static bool TryExtractMesh(MeshFilter meshFilter, out Mesh mesh)
        {
            mesh = meshFilter != null ? meshFilter.sharedMesh : null;
            return mesh != null;
        }
    }
}
