using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor
{
    internal static class DDGIProbePreviewRenderer
    {
        private static readonly Color ProbeFillColor = new(0.28f, 0.72f, 1.0f, 0.18f);
        private static readonly Color ProbeOutlineColor = new(0.28f, 0.72f, 1.0f, 0.95f);
        private static readonly Dictionary<EntityId, CachedPreviewData> s_CachedPreviewData = new();

        internal static void DrawSceneViewPreview(DDGIVolume volume)
        {
            if (volume == null || !volume.isActiveAndEnabled)
            {
                return;
            }

            Event currentEvent = Event.current;
            if (currentEvent == null || currentEvent.type != EventType.Repaint)
            {
                return;
            }

            CachedPreviewData previewData = GetOrCreatePreviewData(volume);
            if (previewData == null || previewData.WorldPositions.Length == 0)
            {
                return;
            }

            CompareFunction previousZTest = Handles.zTest;
            Color previousColor = Handles.color;
            Camera sceneCamera = SceneView.currentDrawingSceneView != null
                ? SceneView.currentDrawingSceneView.camera
                : Camera.current;
            Vector3 outlineNormal = sceneCamera != null ? sceneCamera.transform.forward : Vector3.forward;
            float probeDiameter = previewData.ProbeRadius * 2.0f;

            try
            {
                Handles.zTest = CompareFunction.Always;

                for (int index = 0; index < previewData.WorldPositions.Length; index++)
                {
                    Vector3 worldPosition = previewData.WorldPositions[index];

                    Handles.color = ProbeFillColor;
                    Handles.SphereHandleCap(0, worldPosition, Quaternion.identity, probeDiameter, EventType.Repaint);

                    Handles.color = ProbeOutlineColor;
                    Handles.DrawWireDisc(worldPosition, outlineNormal, previewData.ProbeRadius);
                }
            }
            finally
            {
                Handles.zTest = previousZTest;
                Handles.color = previousColor;
            }
        }

        private static CachedPreviewData GetOrCreatePreviewData(DDGIVolume volume)
        {
            EntityId volumeId = volume.GetEntityId();
            if (!s_CachedPreviewData.TryGetValue(volumeId, out CachedPreviewData previewData))
            {
                previewData = new CachedPreviewData();
                s_CachedPreviewData.Add(volumeId, previewData);
            }

            int previewHash = ComputePreviewHash(volume);
            if (previewData.PreviewHash != previewHash)
            {
                RebuildPreviewData(volume, previewData, previewHash);
            }

            return previewData;
        }

        private static void RebuildPreviewData(DDGIVolume volume, CachedPreviewData previewData, int previewHash)
        {
            float probeRadius = CalculateProbeRadius(volume);
            List<Vector3> worldPositions = BuildProbeWorldPositions(volume);

            previewData.WorldPositions = worldPositions.Count > 0 ? worldPositions.ToArray() : Array.Empty<Vector3>();
            previewData.ProbeRadius = probeRadius;
            previewData.PreviewHash = previewHash;
        }

        private static List<Vector3> BuildProbeWorldPositions(DDGIVolume volume)
        {
            Vector3Int probeCounts = volume.ProbeCounts;
            List<Vector3> worldPositions = new List<Vector3>(probeCounts.x * probeCounts.y * probeCounts.z);
            BoundProxyShape shape = volume.BoundProxyShape;
            Vector3 translation = volume.transform.position;

            for (int y = 0; y < probeCounts.y; y++)
            {
                for (int z = 0; z < probeCounts.z; z++)
                {
                    for (int x = 0; x < probeCounts.x; x++)
                    {
                        Vector3Int probeCoordinate = new Vector3Int(x, y, z);
                        Vector3 localPosition = volume.GetProbeLocalPosition(probeCoordinate);
                        if (!ShouldRenderProbe(shape, localPosition))
                        {
                            continue;
                        }

                        worldPositions.Add(translation + localPosition);
                    }
                }
            }

            return worldPositions;
        }

        private static bool ShouldRenderProbe(BoundProxyShape shape, Vector3 localPosition)
        {
            shape.Sanitize();
            if (shape.shape == BoundProxyShapeType.Sphere)
            {
                float radius = shape.GetSanitizedRadius();
                float radiusSq = radius * radius;
                return localPosition.sqrMagnitude <= radiusSq + 0.0001f;
            }

            Vector3 halfExtents = shape.GetSanitizedSize() * 0.5f;
            return Mathf.Abs(localPosition.x) <= halfExtents.x + 0.0001f
                && Mathf.Abs(localPosition.y) <= halfExtents.y + 0.0001f
                && Mathf.Abs(localPosition.z) <= halfExtents.z + 0.0001f;
        }

        private static float CalculateProbeRadius(DDGIVolume volume)
        {
            Vector3 spacing = volume.ProbeSpacing;
            float minSpacing = Mathf.Min(spacing.x, Mathf.Min(spacing.y, spacing.z));
            return Mathf.Max(minSpacing * 0.12f, 0.05f);
        }

        private static int ComputePreviewHash(DDGIVolume volume)
        {
            BoundProxyShape shape = volume.BoundProxyShape;
            Vector3Int probeCounts = volume.ProbeCounts;
            HashCode hash = new HashCode();
            hash.Add(volume.GetEntityId());
            hash.Add(volume.transform.position);
            hash.Add(shape.shape);
            hash.Add(shape.size);
            hash.Add(shape.radius);
            hash.Add(volume.ProbeSpacing);
            hash.Add(probeCounts.x);
            hash.Add(probeCounts.y);
            hash.Add(probeCounts.z);
            return hash.ToHashCode();
        }

        private sealed class CachedPreviewData
        {
            public Vector3[] WorldPositions = Array.Empty<Vector3>();
            public float ProbeRadius;
            public int PreviewHash;
        }
    }
}
