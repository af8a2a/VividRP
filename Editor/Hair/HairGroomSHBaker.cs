using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor
{
    /// <summary>
    /// GPU compute baker for IJGC SIGGRAPH 2025 Biased L1 Spherical Harmonics
    /// hair self-shadowing (voxelized strand optical depth + optional opaque head/core
    /// mesh occluder + 64-ray Fibonacci sphere raymarch with negative miss bias).
    /// Persists baked SH directly into the canonical .vividhair binary asset.
    /// </summary>
    public static class HairGroomSHBaker
    {
        private const string BakerShaderPath =
            "Packages/com.vivid.render-pipelines/Shaders/Core/Private/Hair/StrandHairSHBaker.compute";

        public struct BakeSettings
        {
            public int GridResolution;
            public int RayCount;
            public float RadiusScale;
            public float DensityScale;

            public static BakeSettings Default => new BakeSettings
            {
                GridResolution = 96,
                RayCount = 64,
                RadiusScale = 1.0f,
                DensityScale = 1.0f,
            };
        }

        [MenuItem("VividRP/Hair/Bake Biased L1 SH for Scene Grooms")]
        public static void BakeAllSceneGrooms()
        {
            var renderers = UnityEngine.Object.FindObjectsByType<HairGroomRenderer>(FindObjectsInactive.Exclude);
            if (renderers == null || renderers.Length == 0)
            {
                Debug.LogWarning("[HairGroomSHBaker] No active HairGroomRenderer found in the open scene.");
                return;
            }

            var bakedAssets = new HashSet<HairGroomAsset>();
            int bakedCount = 0;
            foreach (var renderer in renderers)
            {
                if (renderer == null || renderer.GroomAsset == null || !bakedAssets.Add(renderer.GroomAsset))
                    continue;

                if (BakeAndPersistForRenderer(renderer, BakeSettings.Default))
                    bakedCount++;
            }

            AssetDatabase.Refresh();
            Debug.Log($"[HairGroomSHBaker] Baked Biased L1 SH for {bakedCount} groom asset(s).");
        }

        /// <summary>
        /// Bakes Biased L1 SH for a HairGroomRenderer (automatically gathering sibling/child
        /// opaque MeshFilters such as the mannequin head or emitter core sphere as occluders)
        /// and writes the updated .vividhair file back to disk.
        /// </summary>
        public static bool BakeAndPersistForRenderer(HairGroomRenderer renderer, BakeSettings settings)
        {
            if (renderer == null || renderer.GroomAsset == null)
                return false;

            var occluderTris = CollectLocalOccluderTriangles(renderer);
            var sh = BakeVisibilitySH(renderer.GroomAsset, occluderTris, settings);
            if (sh == null || sh.Length != renderer.GroomAsset.VertexCount)
                return false;

            byte[] updatedBytes = renderer.GroomAsset.UpdateBakedVisibilitySH(sh);

            string assetPath = AssetDatabase.GetAssetPath(renderer.GroomAsset);
            if (!string.IsNullOrEmpty(assetPath) && assetPath.EndsWith(".vividhair", StringComparison.OrdinalIgnoreCase))
            {
                string fullPath = Path.GetFullPath(assetPath);
                File.WriteAllBytes(fullPath, updatedBytes);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                Debug.Log(
                    $"[HairGroomSHBaker] Saved baked Biased L1 SH ({sh.Length:N0} verts, {occluderTris.Count / 3:N0} occluder tris) -> {assetPath}");
            }

            return true;
        }

        /// <summary>
        /// Runs the GPU Biased L1 SH baker for an arbitrary HairGroomAsset and optional
        /// object-space occluder triangles (3 Vector4 vertices per triangle).
        /// </summary>
        public static HairGroomVisibilitySH[] BakeVisibilitySH(
            HairGroomAsset asset,
            IReadOnlyList<Vector4> occluderTriVertsOS,
            BakeSettings settings)
        {
            if (asset == null || !asset.HasBinaryData || asset.VertexCount <= 0 || asset.SegmentCount <= 0)
                throw new ArgumentException("Valid HairGroomAsset with binary data is required.", nameof(asset));

            var bakerCs = AssetDatabase.LoadAssetAtPath<ComputeShader>(BakerShaderPath);
            if (bakerCs == null)
                throw new FileNotFoundException($"Could not load StrandHairSHBaker compute shader at '{BakerShaderPath}'.");

            var gpuRes = asset.GetOrCreateGpuResource();
            if (gpuRes == null || !gpuRes.IsValid)
                throw new InvalidOperationException($"Failed to build GPU resource for '{asset.name}'.");

            int gridRes = Mathf.Clamp(settings.GridResolution, 32, 128);
            int rayCount = Mathf.Clamp(settings.RayCount, 16, 256);
            float radiusScale = Mathf.Max(settings.RadiusScale, 0.05f);
            float densityScale = Mathf.Max(settings.DensityScale, 0.05f);

            // Pad groom bounds uniformly into a cube so voxels are isotropic
            Bounds b = asset.Bounds;
            float maxExtent = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            float paddedSize = Mathf.Max(maxExtent * 1.12f, 0.02f);
            float voxelSize = paddedSize / gridRes;
            Vector3 half = Vector3.one * (paddedSize * 0.5f);
            Vector3 boundsMin = b.center - half;
            Vector3 boundsInvSize = Vector3.one / paddedSize;

            int totalVoxels = gridRes * gridRes * gridRes;
            int occluderTriCount = occluderTriVertsOS != null ? occluderTriVertsOS.Count / 3 : 0;

            GraphicsBuffer densityGrid = null;
            GraphicsBuffer occluderGrid = null;
            GraphicsBuffer occluderVertsBuf = null;
            GraphicsBuffer outputShBuf = null;
            CommandBuffer cmd = null;

            try
            {
                densityGrid = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVoxels, sizeof(uint));
                occluderGrid = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVoxels, sizeof(uint));
                outputShBuf = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    gpuRes.VertexCount,
                    HairGroomVisibilitySH.Stride);

                int dummyCount = Mathf.Max(3, occluderTriCount * 3);
                occluderVertsBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, dummyCount, sizeof(float) * 4);
                if (occluderTriCount > 0)
                {
                    var triArray = new Vector4[occluderTriCount * 3];
                    for (int i = 0; i < triArray.Length; i++)
                        triArray[i] = occluderTriVertsOS[i];
                    occluderVertsBuf.SetData(triArray);
                }

                int kClear = bakerCs.FindKernel("KClearDensityGrid");
                int kVoxHair = bakerCs.FindKernel("KVoxelizeHairSegments");
                int kVoxOcc = bakerCs.FindKernel("KVoxelizeOccluderTriangles");
                int kBake = bakerCs.FindKernel("KBakeVertexL1SH");

                cmd = new CommandBuffer { name = "StrandHair_BakeBiasedL1SH" };
                cmd.SetComputeVectorParam(bakerCs, "_BakerBoundsMin", boundsMin);
                cmd.SetComputeVectorParam(bakerCs, "_BakerBoundsInvSize", boundsInvSize);
                cmd.SetComputeFloatParam(bakerCs, "_BakerVoxelSize", voxelSize);
                cmd.SetComputeIntParam(bakerCs, "_BakerGridRes", gridRes);
                cmd.SetComputeIntParam(bakerCs, "_BakerVertexCount", gpuRes.VertexCount);
                cmd.SetComputeIntParam(bakerCs, "_BakerSegmentCount", gpuRes.SegmentCount);
                cmd.SetComputeIntParam(bakerCs, "_BakerOccluderTriCount", occluderTriCount);
                cmd.SetComputeIntParam(bakerCs, "_BakerRayCount", rayCount);
                cmd.SetComputeFloatParam(bakerCs, "_BakerRadiusScale", radiusScale);
                cmd.SetComputeFloatParam(bakerCs, "_BakerDensityScale", densityScale);

                // 1. Clear voxel grids
                cmd.SetComputeBufferParam(bakerCs, kClear, "_BakerDensityGrid", densityGrid);
                cmd.SetComputeBufferParam(bakerCs, kClear, "_BakerOccluderGrid", occluderGrid);
                cmd.DispatchCompute(bakerCs, kClear, (totalVoxels + 63) / 64, 1, 1);

                // 2. Voxelize hair segments into optical-depth grid
                cmd.SetComputeBufferParam(bakerCs, kVoxHair, "_HairRestVertices", gpuRes.RestVertexBuffer);
                cmd.SetComputeBufferParam(bakerCs, kVoxHair, "_HairSegmentIndices", gpuRes.SegmentIndexBuffer);
                cmd.SetComputeBufferParam(bakerCs, kVoxHair, "_BakerDensityGrid", densityGrid);
                cmd.DispatchCompute(bakerCs, kVoxHair, (gpuRes.SegmentCount + 63) / 64, 1, 1);

                // 3. Voxelize opaque occluder triangles (mannequin head / emitter core)
                if (occluderTriCount > 0)
                {
                    cmd.SetComputeBufferParam(bakerCs, kVoxOcc, "_BakerOccluderTriVerts", occluderVertsBuf);
                    cmd.SetComputeBufferParam(bakerCs, kVoxOcc, "_BakerOccluderGrid", occluderGrid);
                    cmd.DispatchCompute(bakerCs, kVoxOcc, (occluderTriCount + 63) / 64, 1, 1);
                }

                // 4. Raymarch 64 Fibonacci directions per vertex -> Biased L1 SH
                cmd.SetComputeBufferParam(bakerCs, kBake, "_HairRestVertices", gpuRes.RestVertexBuffer);
                cmd.SetComputeBufferParam(bakerCs, kBake, "_BakerDensityGrid", densityGrid);
                cmd.SetComputeBufferParam(bakerCs, kBake, "_BakerOccluderGrid", occluderGrid);
                cmd.SetComputeBufferParam(bakerCs, kBake, "_BakerOutputSH", outputShBuf);
                cmd.DispatchCompute(bakerCs, kBake, (gpuRes.VertexCount + 63) / 64, 1, 1);

                Graphics.ExecuteCommandBuffer(cmd);

                var result = new HairGroomVisibilitySH[gpuRes.VertexCount];
                outputShBuf.GetData(result);
                return result;
            }
            finally
            {
                cmd?.Release();
                densityGrid?.Dispose();
                occluderGrid?.Dispose();
                occluderVertsBuf?.Dispose();
                outputShBuf?.Dispose();
            }
        }

        private static List<Vector4> CollectLocalOccluderTriangles(HairGroomRenderer renderer)
        {
            var tris = new List<Vector4>();
            Matrix4x4 worldToGroomLocal = renderer.transform.worldToLocalMatrix;

            var filters = renderer.GetComponentsInChildren<MeshFilter>(includeInactive: false);
            foreach (var mf in filters)
            {
                if (mf == null || mf.sharedMesh == null)
                    continue;

                // Skip runtime DOTS hair proxy and inverted shadow proxy meshes
                if (mf.gameObject.name.Contains("HairDotsPathTracingProxy")
                    || mf.gameObject.name.Contains("ShadowProxy"))
                {
                    continue;
                }

                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled)
                    continue;

                Mesh mesh = mf.sharedMesh;
                Vector3[] verts = mesh.vertices;
                int[] indices = mesh.triangles;
                if (verts == null || indices == null || indices.Length < 3)
                    continue;

                Matrix4x4 meshToGroomLocal = worldToGroomLocal * mf.transform.localToWorldMatrix;
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    Vector3 a = meshToGroomLocal.MultiplyPoint3x4(verts[indices[i + 0]]);
                    Vector3 b = meshToGroomLocal.MultiplyPoint3x4(verts[indices[i + 1]]);
                    Vector3 c = meshToGroomLocal.MultiplyPoint3x4(verts[indices[i + 2]]);
                    tris.Add(new Vector4(a.x, a.y, a.z, 1.0f));
                    tris.Add(new Vector4(b.x, b.y, b.z, 1.0f));
                    tris.Add(new Vector4(c.x, c.y, c.z, 1.0f));
                }
            }

            return tris;
        }
    }
}
