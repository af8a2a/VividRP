using System;
using System.Collections.Generic;
using UnityEngine;
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

        public Renderer sourceRenderer => m_SourceRenderer;

        public Mesh sourceMesh => m_SourceMesh;

        public IReadOnlyList<VividMeshletCollectionAsset> meshletCollections => m_MeshletCollections;

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

        public VividMeshletCollectionAsset GetMeshletCollection(int subMeshIndex)
        {
            if (subMeshIndex < 0 || m_MeshletCollections == null || subMeshIndex >= m_MeshletCollections.Length)
            {
                return null;
            }

            return m_MeshletCollections[subMeshIndex];
        }

        public bool TryValidate(out string validationMessage)
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

        private void OnValidate()
        {
            RefreshSource();
        }

        private bool SetSource(Renderer renderer, Mesh mesh)
        {
            bool rendererChanged = m_SourceRenderer != renderer;
            bool meshChanged = m_SourceMesh != mesh;

            m_SourceRenderer = renderer;
            m_SourceMesh = mesh;

            bool collectionsChanged = EnsureMeshletCollectionArraySize();
            return rendererChanged || meshChanged || collectionsChanged;
        }

        private bool ClearSource()
        {
            bool changed = m_SourceRenderer != null || m_SourceMesh != null || (m_MeshletCollections?.Length ?? 0) > 0;
            m_SourceRenderer = null;
            m_SourceMesh = null;
            m_MeshletCollections = Array.Empty<VividMeshletCollectionAsset>();
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
    }
}
