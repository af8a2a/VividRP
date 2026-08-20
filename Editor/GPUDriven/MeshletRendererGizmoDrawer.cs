using UnityEditor;
using UnityEngine;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Editor.GPUDriven
{
    internal static class MeshletRendererGizmoDrawer
    {
        internal const GizmoType DrawOptions =
            GizmoType.Selected | GizmoType.NonSelected | GizmoType.InSelectionHierarchy | GizmoType.Pickable;

        private static readonly Color NonSelectedBoundsFillColor = new(0.14f, 0.82f, 1.0f, 0.04f);
        private static readonly Color SelectedBoundsFillColor = new(0.14f, 0.82f, 1.0f, 0.08f);
        private static readonly Color SelectedBoundsWireColor = new(0.14f, 0.82f, 1.0f, 0.95f);

        [DrawGizmo(DrawOptions)]
        private static void DrawMeshletRendererGizmos(MeshletRenderer meshletRenderer, GizmoType gizmoType)
        {
            if (meshletRenderer == null || !meshletRenderer.isActiveAndEnabled)
            {
                return;
            }

            if (!MeshletRendererGizmoUtility.TryGetLocalSelectionBounds(meshletRenderer, out Bounds localBounds))
            {
                return;
            }

            bool showSelectionDetails = ShouldDrawSelectionDetails(gizmoType);
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;

            try
            {
                Gizmos.matrix = meshletRenderer.transform.localToWorldMatrix;
                DrawRendererBounds(localBounds, showSelectionDetails);

                if (showSelectionDetails)
                {
                    DrawMeshletBounds(meshletRenderer);
                }
            }
            finally
            {
                Gizmos.matrix = previousMatrix;
                Gizmos.color = previousColor;
            }
        }

        internal static bool ShouldDrawSelectionDetails(GizmoType gizmoType)
        {
            return (gizmoType & (GizmoType.Selected | GizmoType.InSelectionHierarchy)) != 0;
        }

        private static void DrawRendererBounds(Bounds localBounds, bool isSelected)
        {
            Gizmos.color = isSelected ? SelectedBoundsFillColor : NonSelectedBoundsFillColor;
            Gizmos.DrawCube(localBounds.center, localBounds.size);

            if (!isSelected)
            {
                return;
            }

            Gizmos.color = SelectedBoundsWireColor;
            Gizmos.DrawWireCube(localBounds.center, localBounds.size);
        }

        private static void DrawMeshletBounds(MeshletRenderer meshletRenderer)
        {
            int subMeshCount = meshletRenderer.meshletCollections?.Count ?? 0;
            for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
            {
                VividMeshletCollectionAsset meshletCollection = meshletRenderer.GetMeshletCollection(subMeshIndex);
                if (meshletCollection?.Meshlets == null)
                {
                    continue;
                }

                Color subMeshColor = GetSubMeshColor(subMeshIndex);
                subMeshColor.a = 0.9f;
                Gizmos.color = subMeshColor;

                for (int meshletIndex = 0; meshletIndex < meshletCollection.Meshlets.Length; meshletIndex++)
                {
                    Bounds meshletBounds = MeshletRendererGizmoUtility.GetLocalMeshletBounds(meshletCollection.Meshlets[meshletIndex]);
                    Gizmos.DrawWireSphere(meshletBounds.center, meshletBounds.extents.x);
                }
            }
        }

        private static Color GetSubMeshColor(int subMeshIndex)
        {
            float hue = Mathf.Repeat(0.11f + subMeshIndex * 0.173f, 1.0f);
            return Color.HSVToRGB(hue, 0.7f, 1.0f);
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

        internal static Bounds GetLocalMeshletBounds(in VividMeshlet meshlet)
        {
            Vector3 center = new(meshlet.BoundingSphere.x, meshlet.BoundingSphere.y, meshlet.BoundingSphere.z);
            float radius = Mathf.Max(0.0f, meshlet.BoundingSphere.w);
            return new Bounds(center, Vector3.one * (radius * 2.0f));
        }
    }
}
