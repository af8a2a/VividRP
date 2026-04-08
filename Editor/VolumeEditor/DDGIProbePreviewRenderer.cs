using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using Object = UnityEngine.Object;

namespace VividRP.Editor
{
    [InitializeOnLoad]
    internal static class DDGIProbePreviewRenderer
    {
        private const string PreviewShaderName = "Hidden/VividRP/Editor/DDGIProbePreview";
        private const int MaxInstancesPerBatch = 1023;

        private static readonly int ProbeColorId = Shader.PropertyToID("_ProbeColor");
        private static readonly Color ProbeColor = new(0.28f, 0.72f, 1.0f, 0.32f);
        private static readonly Dictionary<EntityId, CachedPreviewData> s_CachedPreviewData = new();

        private static Material s_PreviewMaterial;
        private static Mesh s_SphereMesh;

        static DDGIProbePreviewRenderer()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            Selection.selectionChanged += SceneView.RepaintAll;
            AssemblyReloadEvents.beforeAssemblyReload += DisposeAll;
            EditorApplication.quitting += DisposeAll;
        }

        private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null || camera.cameraType != CameraType.SceneView)
            {
                return;
            }

            EnsureSharedResources();
            if (s_PreviewMaterial == null || s_SphereMesh == null)
            {
                CleanupUnusedPreviewData(new HashSet<EntityId>());
                return;
            }

            GameObject[] selectedObjects = Selection.gameObjects;
            HashSet<EntityId> activePreviewIds = new HashSet<EntityId>();

            for (int index = 0; index < selectedObjects.Length; index++)
            {
                GameObject selectedObject = selectedObjects[index];
                DDGIVolume volume = selectedObject != null ? selectedObject.GetComponent<DDGIVolume>() : null;
                if (volume == null
                    || !volume.isActiveAndEnabled
                    || !volume.gameObject.scene.IsValid()
                    || !volume.gameObject.scene.isLoaded)
                {
                    continue;
                }

                EntityId volumeId = volume.GetEntityId();
                if (!activePreviewIds.Add(volumeId))
                {
                    continue;
                }

                DrawVolumePreview(volume, camera);
            }

            CleanupUnusedPreviewData(activePreviewIds);
        }

        private static void DrawVolumePreview(DDGIVolume volume, Camera camera)
        {
            if (volume == null || camera == null)
            {
                return;
            }

            CachedPreviewData previewData = GetOrCreatePreviewData(volume);
            if (previewData == null || previewData.InstanceCount <= 0)
            {
                return;
            }

            previewData.PropertyBlock.Clear();
            previewData.PropertyBlock.SetColor(ProbeColorId, ProbeColor);

            Matrix4x4[] matrices = previewData.Matrices;
            int remaining = previewData.InstanceCount;
            int offset = 0;

            while (remaining > 0)
            {
                int batchCount = Mathf.Min(remaining, MaxInstancesPerBatch);

                if (offset == 0 && batchCount == matrices.Length)
                {
                    Graphics.DrawMeshInstanced(
                        s_SphereMesh,
                        0,
                        s_PreviewMaterial,
                        matrices,
                        batchCount,
                        previewData.PropertyBlock,
                        ShadowCastingMode.Off,
                        false,
                        volume.gameObject.layer,
                        camera,
                        LightProbeUsage.Off);
                }
                else
                {
                    Matrix4x4[] batch = new Matrix4x4[batchCount];
                    Array.Copy(matrices, offset, batch, 0, batchCount);
                    Graphics.DrawMeshInstanced(
                        s_SphereMesh,
                        0,
                        s_PreviewMaterial,
                        batch,
                        batchCount,
                        previewData.PropertyBlock,
                        ShadowCastingMode.Off,
                        false,
                        volume.gameObject.layer,
                        camera,
                        LightProbeUsage.Off);
                }

                offset += batchCount;
                remaining -= batchCount;
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
            List<Matrix4x4> probeMatrices = BuildProbeMatrices(volume, probeRadius);

            previewData.Matrices = probeMatrices.Count > 0 ? probeMatrices.ToArray() : Array.Empty<Matrix4x4>();
            previewData.InstanceCount = probeMatrices.Count;
            previewData.PreviewHash = previewHash;
        }

        private static List<Matrix4x4> BuildProbeMatrices(DDGIVolume volume, float probeRadius)
        {
            Vector3Int probeCounts = volume.ProbeCounts;
            List<Matrix4x4> probeMatrices = new List<Matrix4x4>(probeCounts.x * probeCounts.y * probeCounts.z);
            Vector3 probeScale = Vector3.one * (probeRadius * 2.0f);
            BoundProxyShape shape = volume.BoundProxyShape;

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

                        Vector3 worldPosition = volume.transform.position + localPosition;
                        probeMatrices.Add(Matrix4x4.TRS(worldPosition, Quaternion.identity, probeScale));
                    }
                }
            }

            return probeMatrices;
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
            return Mathf.Max(minSpacing * 0.08f, 0.0025f);
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

        private static void EnsureSharedResources()
        {
            if (s_PreviewMaterial == null)
            {
                Shader shader = Shader.Find(PreviewShaderName);
                if (shader != null)
                {
                    s_PreviewMaterial = new Material(shader)
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                        enableInstancing = true,
                    };
                }
                else
                {
                    Debug.LogWarning($"[DDGIProbePreview] Shader not found: {PreviewShaderName}");
                }
            }

            s_SphereMesh ??= CreateSphereMesh();
        }

        private static Mesh CreateSphereMesh()
        {
            GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            try
            {
                MeshFilter meshFilter = primitive.GetComponent<MeshFilter>();
                return meshFilter != null ? meshFilter.sharedMesh : null;
            }
            finally
            {
                Object.DestroyImmediate(primitive);
            }
        }

        private static void CleanupUnusedPreviewData(HashSet<EntityId> activePreviewIds)
        {
            List<EntityId> staleIds = null;

            foreach (KeyValuePair<EntityId, CachedPreviewData> pair in s_CachedPreviewData)
            {
                if (activePreviewIds.Contains(pair.Key))
                {
                    continue;
                }

                staleIds ??= new List<EntityId>();
                staleIds.Add(pair.Key);
            }

            if (staleIds == null)
            {
                return;
            }

            for (int index = 0; index < staleIds.Count; index++)
            {
                s_CachedPreviewData.Remove(staleIds[index]);
            }
        }

        private static void DisposeAll()
        {
            s_CachedPreviewData.Clear();

            if (s_PreviewMaterial != null)
            {
                Object.DestroyImmediate(s_PreviewMaterial);
                s_PreviewMaterial = null;
            }

            s_SphereMesh = null;
        }

        private sealed class CachedPreviewData
        {
            public readonly MaterialPropertyBlock PropertyBlock = new MaterialPropertyBlock();
            public Matrix4x4[] Matrices = Array.Empty<Matrix4x4>();
            public int InstanceCount;
            public int PreviewHash;
        }
    }
}
