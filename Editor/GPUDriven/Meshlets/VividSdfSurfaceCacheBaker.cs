using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Meshlets;
using Object = UnityEngine.Object;

namespace VividRP.Editor.GPUDriven.Meshlets
{
    public static class VividSdfSurfaceCacheBaker
    {
        private const GraphicsFormat AlbedoDepthFormat = GraphicsFormat.R16G16B16A16_SFloat;
        private const GraphicsFormat NormalFormat = GraphicsFormat.R8G8B8A8_UNorm;
        private static readonly int BaseMap = Shader.PropertyToID("_CardBaseMap");
        private static readonly int BaseMapST = Shader.PropertyToID("_CardBaseMapST");
        private static readonly int BaseColor = Shader.PropertyToID("_CardBaseColor");
        private static readonly int MetallicMap = Shader.PropertyToID("_CardMetallicMap");
        private static readonly int Metallic = Shader.PropertyToID("_CardMetallic");
        private static readonly int MinAndFace = Shader.PropertyToID("_CardMinAndFace");
        private static readonly int InverseExtent = Shader.PropertyToID("_CardInverseExtent");

        [MenuItem("Assets/Create/VividRP/SDF Surface Cache from Selection")]
        private static void BakeSelection()
        {
            var renderers = Selection.gameObjects.SelectMany(root => root.GetComponentsInChildren<MeshletRenderer>()).ToArray();
            if (renderers.Length == 0)
                throw new InvalidOperationException("Select GameObjects with MeshletRenderer and baked Mesh SDFs.");
            var path = EditorUtility.SaveFilePanelInProject("Bake SDF Surface Cache", "SDF Surface Cache", "asset", "Choose a new asset path.");
            if (!string.IsNullOrEmpty(path))
                Selection.activeObject = Bake(renderers, path);
        }

        public static VividSdfSurfaceCache Bake(IReadOnlyList<MeshletRenderer> renderers, string path, int resolution = 64)
        {
            if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) || AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new ArgumentException("Choose an unused .asset path for the surface cache.", nameof(path));

            var shader = Shader.Find("Hidden/VividRP/SDF Surface Card Capture");
            if (shader == null || !shader.isSupported || !SystemInfo.supports2DArrayTextures || !SystemInfo.supportsAsyncGPUReadback
                || SystemInfo.supportedRenderTargetCount < 2)
                throw new InvalidOperationException("Surface cards require MRT raster capture and texture array readback.");

            var entries = CollectEntries(renderers);
            int sliceCount = entries.Count * VividSdfSurfaceCache.CardsPerMesh;
            if (sliceCount == 0 || sliceCount > SystemInfo.maxTextureArraySlices)
                throw new InvalidOperationException($"Surface cache needs a supported number of array slices, found {sliceCount}.");

            resolution = Mathf.Clamp(resolution, 16, 128);
            var albedo = new Texture2DArray(resolution, resolution, sliceCount, AlbedoDepthFormat, TextureCreationFlags.None)
                { name = "Surface Card Albedo and Depth", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var normal = new Texture2DArray(resolution, resolution, sliceCount, NormalFormat, TextureCreationFlags.None)
                { name = "Surface Card Normal and Coverage", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var cache = ScriptableObject.CreateInstance<VividSdfSurfaceCache>();
            try
            {
                Capture(entries, shader, resolution, albedo, normal);
                cache.Initialize(entries.ToArray(), albedo, normal);
                AssetDatabase.CreateAsset(cache, path);
                AssetDatabase.AddObjectToAsset(albedo, cache);
                AssetDatabase.AddObjectToAsset(normal, cache);
                AssetDatabase.SaveAssetIfDirty(cache);
                return cache;
            }
            catch
            {
                if (AssetDatabase.Contains(cache))
                    AssetDatabase.DeleteAsset(path);
                if (albedo != null)
                    Object.DestroyImmediate(albedo);
                if (normal != null)
                    Object.DestroyImmediate(normal);
                if (cache != null)
                    Object.DestroyImmediate(cache);
                throw;
            }
        }

        private static List<VividSdfSurfaceCache.Entry> CollectEntries(IReadOnlyList<MeshletRenderer> renderers)
        {
            var seen = new HashSet<(Mesh, Material)>();
            var entries = new List<VividSdfSurfaceCache.Entry>();
            for (int i = 0; i < renderers.Count; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || renderer.meshSdf == null || renderer.sourceMaterials.Count != 1)
                    continue;
                var mesh = renderer.sourceMesh;
                var material = renderer.sourceMaterials[0];
                if (material == null || material.shader.name != "VividRP/Material/StandardLit"
                    || material.GetFloat("_Surface") != 0f || material.GetFloat("_AlphaClip") != 0f
                    || !seen.Add((mesh, material)))
                    continue;

                var bounds = mesh.bounds;
                bounds.Expand(0.002f);
                entries.Add(new VividSdfSurfaceCache.Entry { Mesh = mesh, Material = material, Bounds = bounds });
            }
            return entries;
        }

        private static void Capture(List<VividSdfSurfaceCache.Entry> entries, Shader shader, int resolution,
            Texture2DArray albedo, Texture2DArray normal)
        {
            var albedoTarget = RenderTexture.GetTemporary(new RenderTextureDescriptor(resolution, resolution, AlbedoDepthFormat, 24));
            var normalTarget = RenderTexture.GetTemporary(new RenderTextureDescriptor(resolution, resolution, NormalFormat, 0));
            var capture = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            using var cmd = new CommandBuffer { name = "Bake SDF Surface Cards" };
            var targets = new RenderTargetIdentifier[] { albedoTarget, normalTarget };
            var properties = new MaterialPropertyBlock();
            try
            {
                for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                {
                    var entry = entries[entryIndex];
                    SetMaterialProperties(entry.Material, properties);
                    var size = entry.Bounds.size;
                    var min = entry.Bounds.min;
                    properties.SetVector(InverseExtent, new Vector4(1f / size.x, 1f / size.y, 1f / size.z, 0f));
                    for (int face = 0; face < VividSdfSurfaceCache.CardsPerMesh; face++)
                    {
                        properties.SetVector(MinAndFace, new Vector4(min.x, min.y, min.z, face));
                        cmd.SetRenderTarget(targets, albedoTarget);
                        cmd.SetViewport(new Rect(0f, 0f, resolution, resolution));
                        cmd.DisableScissorRect();
                        cmd.ClearRenderTarget(true, true, Color.clear);
                        cmd.DrawMesh(entry.Mesh, Matrix4x4.identity, capture, 0, 0, properties);
                        int slice = entryIndex * VividSdfSurfaceCache.CardsPerMesh + face;
                        cmd.CopyTexture(albedoTarget, 0, 0, albedo, slice, 0);
                        cmd.CopyTexture(normalTarget, 0, 0, normal, slice, 0);
                    }
                }
                Graphics.ExecuteCommandBuffer(cmd);
                var albedoReadback = AsyncGPUReadback.Request(albedo, 0);
                var normalReadback = AsyncGPUReadback.Request(normal, 0);
                albedoReadback.WaitForCompletion();
                normalReadback.WaitForCompletion();
                if (albedoReadback.hasError || normalReadback.hasError
                    || albedoReadback.layerCount != albedo.depth || normalReadback.layerCount != normal.depth)
                    throw new InvalidOperationException("Could not read back all captured surface cards.");

                for (int slice = 0; slice < albedo.depth; slice++)
                {
                    albedo.SetPixelData(albedoReadback.GetData<ushort>(slice), 0, slice);
                    normal.SetPixelData(normalReadback.GetData<Color32>(slice), 0, slice);
                }
                albedo.Apply(false, false);
                normal.Apply(false, false);
            }
            finally
            {
                Object.DestroyImmediate(capture);
                RenderTexture.ReleaseTemporary(albedoTarget);
                RenderTexture.ReleaseTemporary(normalTarget);
            }
        }

        private static void SetMaterialProperties(Material material, MaterialPropertyBlock properties)
        {
            var tiling = material.GetTextureScale("_BaseMap");
            var offset = material.GetTextureOffset("_BaseMap");
            var color = GPUDrivenMaterialCompiler.ConvertMaterialColorForGPU(material.GetColor("_BaseColor"));
            properties.SetVector(BaseColor, new Vector4(color.x, color.y, color.z, color.w));
            properties.SetVector(BaseMapST, new Vector4(tiling.x, tiling.y, offset.x, offset.y));
            properties.SetTexture(BaseMap, material.GetTexture("_BaseMap") ?? Texture2D.whiteTexture);

            bool rmo = material.IsKeywordEnabled("_RMOMAP");
            bool legacy = material.IsKeywordEnabled("_METALLICSPECGLOSSMAP");
            var map = rmo ? material.GetTexture("_RMOMap") : legacy ? material.GetTexture("_MetallicGlossMap") : null;
            properties.SetTexture(MetallicMap, map ?? Texture2D.whiteTexture);
            properties.SetVector(Metallic, new Vector4(material.GetFloat("_Metallic"),
                material.GetFloat("_MetallicRemapMin"), material.GetFloat("_MetallicRemapMax"),
                rmo ? 1f : legacy ? 2f : 0f));
        }
    }
}
