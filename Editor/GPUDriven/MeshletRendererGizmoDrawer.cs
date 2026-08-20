using UnityEditor;
using UnityEngine;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Editor.GPUDriven
{
    internal static class MeshletRendererGizmoDrawer
    {
        internal const GizmoType DrawOptions = GizmoType.NonSelected | GizmoType.Pickable;

        [DrawGizmo(DrawOptions)]
        private static void DrawMeshletRendererGizmos(MeshletRenderer meshletRenderer, GizmoType _)
        {
            if (meshletRenderer == null || !meshletRenderer.isActiveAndEnabled)
            {
                return;
            }

            if (!MeshletRendererGizmoUtility.TryGetLocalSelectionBounds(meshletRenderer, out Bounds localBounds))
            {
                return;
            }

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;

            try
            {
                Gizmos.matrix = meshletRenderer.transform.localToWorldMatrix;
                Gizmos.color = Color.clear;
                Gizmos.DrawCube(localBounds.center, localBounds.size);
            }
            finally
            {
                Gizmos.matrix = previousMatrix;
                Gizmos.color = previousColor;
            }
        }
    }

    internal static class MeshletRendererGizmoUtility
    {
        internal static bool TryGetLocalSelectionBounds(MeshletRenderer meshletRenderer, out Bounds bounds)
        {
            bounds = default;
            if (meshletRenderer == null)
            {
                return false;
            }

            if (meshletRenderer.sourceMesh != null)
            {
                bounds = meshletRenderer.localBounds;
                return true;
            }

            bool hasBounds = false;
            int subMeshCount = meshletRenderer.meshletCollections?.Count ?? 0;
            for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
            {
                VividMeshletCollectionAsset meshletCollection = meshletRenderer.GetMeshletCollection(subMeshIndex);
                if (meshletCollection == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = meshletCollection.Bounds;
                    hasBounds = true;
                    continue;
                }

                bounds.Encapsulate(meshletCollection.Bounds);
            }

            return hasBounds;
        }
    }
}
