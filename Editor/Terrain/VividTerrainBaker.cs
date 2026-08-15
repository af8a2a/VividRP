using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Editor.GPUDriven;
using VividRP.Editor.GPUDriven.Meshlets;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Meshlets;
using Object = UnityEngine.Object;

namespace VividRP.Editor.TerrainTools
{
    internal static class VividTerrainBaker
    {
        internal struct Parameters
        {
            public TerrainData SourceTerrainData;
            public Material SourceMaterial;
            public string SourceTerrainDataGUID;
            public long SourceTerrainDataLocalFileID;
            public VividTerrainBakeSettings Settings;
            public int CompositeMaxResolution;
            public Action<float, string> ProgressHandler;
            public Action<string> LogErrorHandler;
        }

        internal static VividTerrainData BakeToAsset(string assetPath, in Parameters parameters)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                throw new ArgumentException("A terrain asset path is required.", nameof(assetPath));
            }

            assetPath = assetPath.Replace('\\', '/');
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
            {
                throw new InvalidOperationException($"An asset already exists at '{assetPath}'.");
            }

            var data = ScriptableObject.CreateInstance<VividTerrainData>();
            data.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            bool mainAssetCreated = false;
            var createdVirtualTexturePaths = new List<string>();

            try
            {
                Parameters generateParameters = parameters;
                Action<float, string> progressHandler = parameters.ProgressHandler;
                generateParameters.ProgressHandler = (progress, message) =>
                    progressHandler?.Invoke(progress * 0.6f, message);
                Generate(data, generateParameters);
                progressHandler?.Invoke(0.6f, "Building terrain streamed virtual textures");
                BuildStreamedVirtualTextures(
                    data,
                    parameters.SourceTerrainData,
                    parameters.SourceTerrainDataGUID,
                    assetPath,
                    parameters.CompositeMaxResolution,
                    progressHandler,
                    createdVirtualTexturePaths);
                AssetDatabase.CreateAsset(data, assetPath);
                mainAssetCreated = true;

                if (data.NormalizedHeightTexture != null)
                    AssetDatabase.AddObjectToAsset(data.NormalizedHeightTexture, data);

                for (int chunkIndex = 0; chunkIndex < data.Chunks.Count; chunkIndex++)
                {
                    VividMeshletCollectionAsset meshlets = data.Chunks[chunkIndex].MeshletCollection;
                    if (meshlets != null)
                    {
                        AssetDatabase.AddObjectToAsset(meshlets, data);
                    }
                }

                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssetIfDirty(data);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                return AssetDatabase.LoadAssetAtPath<VividTerrainData>(assetPath);
            }
            catch
            {
                if (mainAssetCreated)
                {
                    AssetDatabase.DeleteAsset(assetPath);
                }
                else
                {
                    DestroyGeneratedData(data);
                }

                DeleteCreatedAssets(createdVirtualTexturePaths);

                throw;
            }
        }

        internal static void Generate(VividTerrainData destination, in Parameters parameters)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            TerrainData terrainData = parameters.SourceTerrainData;
            if (terrainData == null)
            {
                throw new ArgumentNullException(nameof(parameters.SourceTerrainData));
            }

            int heightmapResolution = terrainData.heightmapResolution;
            if (heightmapResolution < 2)
            {
                throw new InvalidOperationException(
                    $"TerrainData '{terrainData.name}' has invalid heightmap resolution {heightmapResolution}."
                );
            }

            VividTerrainBakeSettings settings = new(
                parameters.Settings.HeightSampleStride,
                parameters.Settings.ChunkQuadCount,
                parameters.Settings.OptimizeVertexCache,
                parameters.Settings.MaxMeshLODLevelCount
            );
            int sampleStride = settings.HeightSampleStride;
            int sampledQuadCount = Mathf.CeilToInt((heightmapResolution - 1) / (float) sampleStride);
            int chunkGridSizeX = Mathf.CeilToInt(sampledQuadCount / (float) settings.ChunkQuadCount);
            int chunkGridSizeY = Mathf.CeilToInt(sampledQuadCount / (float) settings.ChunkQuadCount);
            var chunkGridSize = new Vector2Int(chunkGridSizeX, chunkGridSizeY);

            float[,] heights = terrainData.GetHeights(0, 0, heightmapResolution, heightmapResolution);
            bool[,] holes = ReadHoles(terrainData);
            Vector3 terrainSize = terrainData.size;
            var chunks = new List<VividTerrainChunkData>(chunkGridSizeX * chunkGridSizeY);

            try
            {
                int totalChunkCount = chunkGridSizeX * chunkGridSizeY;
                int completedChunkCount = 0;
                for (int chunkY = 0; chunkY < chunkGridSizeY; chunkY++)
                {
                    for (int chunkX = 0; chunkX < chunkGridSizeX; chunkX++)
                    {
                        int sampleQuadStartX = chunkX * settings.ChunkQuadCount;
                        int sampleQuadStartY = chunkY * settings.ChunkQuadCount;
                        int quadCountX = Mathf.Min(settings.ChunkQuadCount, sampledQuadCount - sampleQuadStartX);
                        int quadCountY = Mathf.Min(settings.ChunkQuadCount, sampledQuadCount - sampleQuadStartY);
                        int heightmapStartX = ToHeightmapSample(sampleQuadStartX, sampleStride, heightmapResolution);
                        int heightmapStartY = ToHeightmapSample(sampleQuadStartY, sampleStride, heightmapResolution);
                        int heightmapEndX = ToHeightmapSample(sampleQuadStartX + quadCountX, sampleStride, heightmapResolution);
                        int heightmapEndY = ToHeightmapSample(sampleQuadStartY + quadCountY, sampleStride, heightmapResolution);

                        parameters.ProgressHandler?.Invoke(
                            completedChunkCount / (float) totalChunkCount,
                            $"Baking terrain chunk ({chunkX}, {chunkY})"
                        );

                        Mesh mesh = CreateChunkMesh(
                            terrainData.name,
                            heights,
                            holes,
                            terrainSize,
                            heightmapResolution,
                            sampleStride,
                            sampleQuadStartX,
                            sampleQuadStartY,
                            quadCountX,
                            quadCountY,
                            chunkX,
                            chunkY
                        );

                        VividMeshletCollectionAsset meshletCollection = null;
                        Bounds chunkBounds = mesh.bounds;
                        try
                        {
                            if (mesh.GetIndexCount(0) > 0)
                            {
                                meshletCollection = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();
                                meshletCollection.name = $"Chunk_{chunkX}_{chunkY}_Meshlets";
                                VividMeshletCollectionBuilder.Generate(
                                    meshletCollection,
                                    new VividMeshletCollectionBuilder.Parameters
                                    {
                                        Mesh = mesh,
                                        SourceMeshGUID = parameters.SourceTerrainDataGUID,
                                        SourceMeshLocalFileID = parameters.SourceTerrainDataLocalFileID,
                                        SubMeshIndex = 0,
                                        OptimizeVertexCache = settings.OptimizeVertexCache,
                                        MaxMeshLODLevelCount = settings.MaxMeshLODLevelCount,
                                        TargetError = 0.01f,
                                        TargetErrorSloppy = 0.001f,
                                        MinTriangleReductionPerStep = 0.8f,
                                        DisableSloppySimplification = true,
                                        PreserveFinestLODLevels = true,
                                        LogErrorHandler = parameters.LogErrorHandler,
                                    }
                                );
                            }
                        }
                        catch
                        {
                            if (meshletCollection != null)
                            {
                                Object.DestroyImmediate(meshletCollection);
                            }

                            throw;
                        }
                        finally
                        {
                            Object.DestroyImmediate(mesh);
                        }

                        chunks.Add(new VividTerrainChunkData(
                            new Vector2Int(chunkX, chunkY),
                            new Vector2Int(heightmapStartX, heightmapStartY),
                            new Vector2Int(heightmapEndX, heightmapEndY),
                            chunkBounds,
                            meshletCollection
                        ));
                        completedChunkCount++;
                    }
                }

                parameters.ProgressHandler?.Invoke(1.0f, "Terrain bake complete");

                Bounds terrainBounds = CalculateTerrainBounds(chunks, terrainSize);
                destination.Initialize(
                    parameters.SourceTerrainDataGUID,
                    terrainData.name,
                    heightmapResolution,
                    terrainSize,
                    terrainBounds,
                    chunkGridSize,
                    settings,
                    parameters.SourceMaterial,
                    CaptureLayers(terrainData.terrainLayers),
                    chunks.ToArray(),
                    CaptureControlMaps(terrainData),
                    CreateNormalizedHeightTexture(terrainData.name, heights)
                );
            }
            catch
            {
                for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
                {
                    VividMeshletCollectionAsset meshlets = chunks[chunkIndex].MeshletCollection;
                    if (meshlets != null)
                    {
                        Object.DestroyImmediate(meshlets);
                    }
                }

                throw;
            }
        }

        private static Mesh CreateChunkMesh(
            string terrainName,
            float[,] heights,
            bool[,] holes,
            Vector3 terrainSize,
            int heightmapResolution,
            int sampleStride,
            int sampleQuadStartX,
            int sampleQuadStartY,
            int quadCountX,
            int quadCountY,
            int chunkX,
            int chunkY)
        {
            int vertexCountX = quadCountX + 1;
            int vertexCountY = quadCountY + 1;
            var vertices = new Vector3[vertexCountX * vertexCountY];
            var normals = new Vector3[vertices.Length];
            var tangents = new Vector4[vertices.Length];
            var uv = new Vector2[vertices.Length];

            for (int localY = 0; localY < vertexCountY; localY++)
            {
                int heightmapY = ToHeightmapSample(sampleQuadStartY + localY, sampleStride, heightmapResolution);
                for (int localX = 0; localX < vertexCountX; localX++)
                {
                    int heightmapX = ToHeightmapSample(sampleQuadStartX + localX, sampleStride, heightmapResolution);
                    int vertexIndex = localY * vertexCountX + localX;
                    float normalizedX = heightmapX / (float) (heightmapResolution - 1);
                    float normalizedY = heightmapY / (float) (heightmapResolution - 1);
                    Vector3 normal = CalculateNormal(
                        heights,
                        terrainSize,
                        heightmapResolution,
                        heightmapX,
                        heightmapY,
                        out float heightSlopeX
                    );
                    Vector3 tangent = new Vector3(1.0f, heightSlopeX, 0.0f).normalized;

                    vertices[vertexIndex] = new Vector3(
                        normalizedX * terrainSize.x,
                        heights[heightmapY, heightmapX] * terrainSize.y,
                        normalizedY * terrainSize.z
                    );
                    normals[vertexIndex] = normal;
                    tangents[vertexIndex] = new Vector4(tangent.x, tangent.y, tangent.z, 1.0f);
                    uv[vertexIndex] = new Vector2(normalizedX, normalizedY);
                }
            }

            var indices = new List<int>(quadCountX * quadCountY * 6);
            for (int localY = 0; localY < quadCountY; localY++)
            {
                int heightmapY0 = ToHeightmapSample(sampleQuadStartY + localY, sampleStride, heightmapResolution);
                int heightmapY1 = ToHeightmapSample(sampleQuadStartY + localY + 1, sampleStride, heightmapResolution);
                for (int localX = 0; localX < quadCountX; localX++)
                {
                    int heightmapX0 = ToHeightmapSample(sampleQuadStartX + localX, sampleStride, heightmapResolution);
                    int heightmapX1 = ToHeightmapSample(sampleQuadStartX + localX + 1, sampleStride, heightmapResolution);
                    if (!ShouldEmitQuad(
                            holes,
                            heightmapResolution,
                            heightmapX0,
                            heightmapY0,
                            heightmapX1,
                            heightmapY1))
                    {
                        continue;
                    }

                    int bottomLeft = localY * vertexCountX + localX;
                    int bottomRight = bottomLeft + 1;
                    int topLeft = bottomLeft + vertexCountX;
                    int topRight = topLeft + 1;

                    indices.Add(bottomLeft);
                    indices.Add(topLeft);
                    indices.Add(bottomRight);
                    indices.Add(bottomRight);
                    indices.Add(topLeft);
                    indices.Add(topRight);
                }
            }

            var mesh = new Mesh
            {
                name = $"{terrainName}_Chunk_{chunkX}_{chunkY}",
                indexFormat = vertices.Length > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16,
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(indices, 0, calculateBounds: true);
            mesh.bounds = CalculateBounds(vertices);
            return mesh;
        }

        private static Bounds CalculateBounds(Vector3[] vertices)
        {
            if (vertices == null || vertices.Length == 0)
            {
                return default;
            }

            var bounds = new Bounds(vertices[0], Vector3.zero);
            for (int vertexIndex = 1; vertexIndex < vertices.Length; vertexIndex++)
            {
                bounds.Encapsulate(vertices[vertexIndex]);
            }

            return bounds;
        }

        private static Vector3 CalculateNormal(
            float[,] heights,
            Vector3 terrainSize,
            int heightmapResolution,
            int sampleX,
            int sampleY,
            out float heightSlopeX)
        {
            int minX = Mathf.Max(0, sampleX - 1);
            int maxX = Mathf.Min(heightmapResolution - 1, sampleX + 1);
            int minY = Mathf.Max(0, sampleY - 1);
            int maxY = Mathf.Min(heightmapResolution - 1, sampleY + 1);
            float distanceX = (maxX - minX) * terrainSize.x / (heightmapResolution - 1);
            float distanceY = (maxY - minY) * terrainSize.z / (heightmapResolution - 1);
            heightSlopeX = distanceX > 0.0f
                ? (heights[sampleY, maxX] - heights[sampleY, minX]) * terrainSize.y / distanceX
                : 0.0f;
            float heightSlopeY = distanceY > 0.0f
                ? (heights[maxY, sampleX] - heights[minY, sampleX]) * terrainSize.y / distanceY
                : 0.0f;
            return new Vector3(-heightSlopeX, 1.0f, -heightSlopeY).normalized;
        }

        private static bool[,] ReadHoles(TerrainData terrainData)
        {
            int holesResolution = terrainData.holesResolution;
            return holesResolution > 0
                ? terrainData.GetHoles(0, 0, holesResolution, holesResolution)
                : null;
        }

        private static bool ShouldEmitQuad(
            bool[,] holes,
            int heightmapResolution,
            int heightmapX0,
            int heightmapY0,
            int heightmapX1,
            int heightmapY1)
        {
            if (holes == null)
            {
                return true;
            }

            int holesResolutionY = holes.GetLength(0);
            int holesResolutionX = holes.GetLength(1);
            int heightmapQuadCount = heightmapResolution - 1;
            int holeStartX = Mathf.Clamp(
                Mathf.FloorToInt(heightmapX0 / (float) heightmapQuadCount * holesResolutionX),
                0,
                holesResolutionX - 1
            );
            int holeStartY = Mathf.Clamp(
                Mathf.FloorToInt(heightmapY0 / (float) heightmapQuadCount * holesResolutionY),
                0,
                holesResolutionY - 1
            );
            int holeEndX = Mathf.Clamp(
                Mathf.CeilToInt(heightmapX1 / (float) heightmapQuadCount * holesResolutionX),
                holeStartX + 1,
                holesResolutionX
            );
            int holeEndY = Mathf.Clamp(
                Mathf.CeilToInt(heightmapY1 / (float) heightmapQuadCount * holesResolutionY),
                holeStartY + 1,
                holesResolutionY
            );

            for (int holeY = holeStartY; holeY < holeEndY; holeY++)
            {
                for (int holeX = holeStartX; holeX < holeEndX; holeX++)
                {
                    if (!holes[holeY, holeX])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static int ToHeightmapSample(int sampledGridCoordinate, int sampleStride, int heightmapResolution)
        {
            return Mathf.Min(sampledGridCoordinate * sampleStride, heightmapResolution - 1);
        }

        private static Bounds CalculateTerrainBounds(List<VividTerrainChunkData> chunks, Vector3 terrainSize)
        {
            if (chunks.Count == 0)
            {
                return new Bounds(terrainSize * 0.5f, terrainSize);
            }

            Bounds bounds = chunks[0].LocalBounds;
            for (int chunkIndex = 1; chunkIndex < chunks.Count; chunkIndex++)
            {
                bounds.Encapsulate(chunks[chunkIndex].LocalBounds);
            }

            return bounds;
        }

        private static VividTerrainLayerData[] CaptureLayers(TerrainLayer[] terrainLayers)
        {
            if (terrainLayers == null || terrainLayers.Length == 0)
            {
                return Array.Empty<VividTerrainLayerData>();
            }

            var layers = new VividTerrainLayerData[terrainLayers.Length];
            for (int layerIndex = 0; layerIndex < terrainLayers.Length; layerIndex++)
            {
                TerrainLayer layer = terrainLayers[layerIndex];
                if (layer == null)
                {
                    continue;
                }

                layers[layerIndex] = new VividTerrainLayerData(
                    layer.diffuseTexture,
                    layer.normalMapTexture,
                    layer.maskMapTexture,
                    layer.tileSize,
                    layer.tileOffset,
                    layer.specular,
                    layer.metallic,
                    layer.smoothness,
                    layer.normalScale
                );
            }

            return layers;
        }

        private static Texture2D[] CaptureControlMaps(TerrainData terrainData)
        {
            Texture2D[] sourceControlMaps = terrainData != null
                ? terrainData.alphamapTextures
                : null;
            if (sourceControlMaps == null || sourceControlMaps.Length == 0)
            {
                return Array.Empty<Texture2D>();
            }

            int controlMapCount = Mathf.Min(
                sourceControlMaps.Length,
                VividTerrainData.MaximumControlMapCount
            );
            var controlMaps = new Texture2D[controlMapCount];
            Array.Copy(sourceControlMaps, controlMaps, controlMapCount);
            return controlMaps;
        }

        private static void BuildStreamedVirtualTextures(
            VividTerrainData terrainData,
            TerrainData sourceTerrainData,
            string sourceTerrainDataGUID,
            string terrainAssetPath,
            int compositeMaxResolution,
            Action<float, string> progressHandler,
            List<string> createdAssetPaths)
        {
            var layerVirtualTextures = new VividVirtualTextureAsset[terrainData.Layers.Count];
            int supportedLayerCount = terrainData.SupportedSurfaceLayerCount;
            for (int layerIndex = 0; layerIndex < supportedLayerCount; layerIndex++)
            {
                progressHandler?.Invoke(
                    Mathf.Lerp(0.6f, 0.7f, layerIndex / (float)Mathf.Max(1, supportedLayerCount)),
                    $"Building terrain layer VT {layerIndex + 1}/{supportedLayerCount}");
                VividTerrainLayerData layer = terrainData.Layers[layerIndex];
                if (layer.DiffuseTexture == null
                    && layer.NormalMapTexture == null
                    && layer.MaskMapTexture == null)
                {
                    continue;
                }

                string virtualTexturePath = CreateVirtualTextureAssetPath(
                    terrainAssetPath,
                    $"Layer{layerIndex}_Surface");
                bool success = GPUDrivenMaterialProxyEditorUtility.BuildOrRefreshStreamedVirtualTexture(
                    virtualTexturePath,
                    layer.DiffuseTexture,
                    layer.NormalMapTexture,
                    layer.MaskMapTexture,
                    GPUDrivenMaterialMaskMode.PackedMetallicOcclusionSmoothness,
                    VividVirtualTextureAddressMode.Repeat,
                    out VividVirtualTextureAsset streamedAsset,
                    out bool wasCreated,
                    out string errorMessage);
                if (wasCreated)
                    createdAssetPaths.Add(virtualTexturePath);
                if (!success)
                {
                    throw new InvalidOperationException(
                        $"Failed to build streamed VT for terrain layer {layerIndex}: {errorMessage}");
                }

                layerVirtualTextures[layerIndex] = streamedAsset;
            }

            int controlMapCount = Mathf.Min(
                terrainData.RequiredControlMapCount,
                VividTerrainData.MaximumControlMapCount);
            var persistentControlMaps = new Texture2D[controlMapCount];
            var controlVirtualTextures = new VividVirtualTextureAsset[controlMapCount];
            for (int controlMapIndex = 0; controlMapIndex < controlMapCount; controlMapIndex++)
            {
                progressHandler?.Invoke(
                    Mathf.Lerp(0.7f, 0.8f, controlMapIndex / (float)Mathf.Max(1, controlMapCount)),
                    $"Building terrain control VT {controlMapIndex + 1}/{controlMapCount}");
                string controlMapPath = CreateSiblingAssetPath(
                    terrainAssetPath,
                    $"Control{controlMapIndex}_Source",
                    "asset");
                Texture2D controlMap = CreatePersistentControlMap(
                    sourceTerrainData,
                    controlMapIndex,
                    controlMapPath);
                persistentControlMaps[controlMapIndex] = controlMap;
                createdAssetPaths.Add(controlMapPath);

                string virtualTexturePath = CreateVirtualTextureAssetPath(
                    terrainAssetPath,
                    $"Control{controlMapIndex}");
                bool success = GPUDrivenMaterialProxyEditorUtility.BuildOrRefreshStreamedVirtualTexture(
                    virtualTexturePath,
                    null,
                    null,
                    controlMap,
                    GPUDrivenMaterialMaskMode.PackedMetallicOcclusionSmoothness,
                    VividVirtualTextureAddressMode.Clamp,
                    out VividVirtualTextureAsset streamedAsset,
                    out bool wasCreated,
                    out string errorMessage);
                if (wasCreated)
                    createdAssetPaths.Add(virtualTexturePath);
                if (!success)
                {
                    throw new InvalidOperationException(
                        $"Failed to build streamed VT for terrain control map {controlMapIndex}: {errorMessage}");
                }

                controlVirtualTextures[controlMapIndex] = streamedAsset;
            }

            terrainData.SetControlMaps(persistentControlMaps);
            terrainData.SetStreamedVirtualTextures(layerVirtualTextures, controlVirtualTextures);

            if (supportedLayerCount <= 1)
            {
                terrainData.SetCompositeVirtualTexture(null);
                progressHandler?.Invoke(1.0f, "Terrain streamed virtual textures complete");
                return;
            }

            var compositeLayers = new VividTerrainCompositeLayerSource[supportedLayerCount];
            for (int layerIndex = 0; layerIndex < supportedLayerCount; layerIndex++)
            {
                compositeLayers[layerIndex] = new VividTerrainCompositeLayerSource(
                    terrainData.Layers[layerIndex],
                    terrainData.Size);
            }

            string resolvedSourceGUID = sourceTerrainDataGUID;
            if (string.IsNullOrWhiteSpace(resolvedSourceGUID))
            {
                string sourcePath = AssetDatabase.GetAssetPath(sourceTerrainData);
                resolvedSourceGUID = AssetDatabase.AssetPathToGUID(sourcePath);
            }

            var compositeSource = new VividTerrainCompositeSource(
                resolvedSourceGUID,
                terrainData.Size,
                compositeMaxResolution,
                compositeLayers,
                persistentControlMaps);
            string compositeVirtualTexturePath = CreateVirtualTextureAssetPath(
                terrainAssetPath,
                "CompositeSurface");
            bool compositeSuccess = VividTerrainCompositeVirtualTextureAssetUtility.BuildOrRefresh(
                compositeVirtualTexturePath,
                compositeSource,
                (progress, message) => progressHandler?.Invoke(
                    Mathf.Lerp(0.8f, 1.0f, progress),
                    message),
                out VividVirtualTextureAsset compositeVirtualTexture,
                out bool compositeWasCreated,
                out string compositeErrorMessage);
            if (compositeWasCreated)
                createdAssetPaths.Add(compositeVirtualTexturePath);
            if (!compositeSuccess)
            {
                throw new InvalidOperationException(
                    $"Failed to build terrain composite streamed VT: {compositeErrorMessage}");
            }

            terrainData.SetCompositeVirtualTexture(compositeVirtualTexture);
        }

        private static Texture2D CreateNormalizedHeightTexture(string terrainName, float[,] heights)
        {
            int height = heights.GetLength(0);
            int width = heights.GetLength(1);
            var samples = new ushort[checked(width * height)];
            for (int y = 0; y < height; y++)
            {
                int rowStart = y * width;
                for (int x = 0; x < width; x++)
                {
                    samples[rowStart + x] = (ushort)Mathf.RoundToInt(
                        Mathf.Clamp01(heights[y, x]) * ushort.MaxValue);
                }
            }

            var texture = new Texture2D(
                width,
                height,
                GraphicsFormat.R16_UNorm,
                TextureCreationFlags.MipChain)
            {
                name = $"{terrainName}_NormalizedHeight",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixelData(samples, 0);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: false);
            return texture;
        }

        private static string CreateVirtualTextureAssetPath(string terrainAssetPath, string suffix)
        {
            return CreateSiblingAssetPath(
                terrainAssetPath,
                suffix,
                VividVirtualTextureAssetImporter.Extension);
        }

        private static string CreateSiblingAssetPath(
            string terrainAssetPath,
            string suffix,
            string extension)
        {
            string directory = Path.GetDirectoryName(terrainAssetPath)?.Replace('\\', '/') ?? "Assets";
            string baseName = Path.GetFileNameWithoutExtension(terrainAssetPath);
            string candidate = Path.Combine(directory, $"{baseName}_{suffix}.{extension}")
                .Replace('\\', '/');
            return AssetDatabase.GenerateUniqueAssetPath(candidate);
        }

        private static Texture2D CreatePersistentControlMap(
            TerrainData sourceTerrainData,
            int controlMapIndex,
            string assetPath)
        {
            if (sourceTerrainData == null)
                throw new ArgumentNullException(nameof(sourceTerrainData));

            int width = Mathf.Max(1, sourceTerrainData.alphamapWidth);
            int height = Mathf.Max(1, sourceTerrainData.alphamapHeight);
            int layerCount = Mathf.Max(0, sourceTerrainData.alphamapLayers);
            int firstLayer = checked(controlMapIndex * 4);
            var pixels = new Color32[checked(width * height)];
            const int rowBatchSize = 64;
            for (int firstRow = 0; firstRow < height; firstRow += rowBatchSize)
            {
                int rowCount = Mathf.Min(rowBatchSize, height - firstRow);
                float[,,] weights = sourceTerrainData.GetAlphamaps(0, firstRow, width, rowCount);
                for (int localY = 0; localY < rowCount; localY++)
                {
                    int destinationRow = (firstRow + localY) * width;
                    for (int x = 0; x < width; x++)
                    {
                        pixels[destinationRow + x] = new Color32(
                            EncodeControlWeight(weights, localY, x, firstLayer, layerCount),
                            EncodeControlWeight(weights, localY, x, firstLayer + 1, layerCount),
                            EncodeControlWeight(weights, localY, x, firstLayer + 2, layerCount),
                            EncodeControlWeight(weights, localY, x, firstLayer + 3, layerCount));
                    }
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: true, linear: true)
            {
                name = $"TerrainControl{controlMapIndex}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: false);
            AssetDatabase.CreateAsset(texture, assetPath);
            Texture2D persistentTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (persistentTexture == null)
            {
                throw new InvalidOperationException(
                    $"Failed to reload persistent terrain control map '{assetPath}'.");
            }

            return persistentTexture;
        }

        private static byte EncodeControlWeight(
            float[,,] weights,
            int y,
            int x,
            int layerIndex,
            int layerCount)
        {
            if (layerIndex < 0 || layerIndex >= layerCount || layerIndex >= weights.GetLength(2))
                return 0;

            return (byte)Mathf.RoundToInt(Mathf.Clamp01(weights[y, x, layerIndex]) * byte.MaxValue);
        }

        private static void DeleteCreatedAssets(List<string> createdAssetPaths)
        {
            for (int assetIndex = 0; assetIndex < createdAssetPaths.Count; assetIndex++)
            {
                string assetPath = createdAssetPaths[assetIndex];
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.DeleteAsset(assetPath + ".stream");
            }
        }

        private static void DestroyGeneratedData(VividTerrainData data)
        {
            if (data == null)
            {
                return;
            }

            for (int chunkIndex = 0; chunkIndex < data.Chunks.Count; chunkIndex++)
            {
                VividMeshletCollectionAsset meshlets = data.Chunks[chunkIndex].MeshletCollection;
                if (meshlets != null)
                {
                    Object.DestroyImmediate(meshlets);
                }
            }

            if (data.NormalizedHeightTexture != null)
                Object.DestroyImmediate(data.NormalizedHeightTexture);

            Object.DestroyImmediate(data);
        }
    }
}
