using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Editor.TerrainTools;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Meshlets;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class VividTerrainBakerTests
    {
        private const string TempFolder = "Assets/VividTerrainBakerTests";
        private readonly List<GameObject> m_GameObjects = new();

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Assets", nameof(VividTerrainBakerTests));
            }
        }

        [TearDown]
        public void TearDown()
        {
            for (int index = 0; index < m_GameObjects.Count; index++)
            {
                if (m_GameObjects[index] != null)
                {
                    Object.DestroyImmediate(m_GameObjects[index]);
                }
            }

            m_GameObjects.Clear();
            AssetDatabase.DeleteAsset(TempFolder);
        }

        [Test]
        public void TerrainData_RejectsPreviousBakeVersion()
        {
            var data = ScriptableObject.CreateInstance<VividTerrainData>();

            try
            {
                FieldInfo bakeVersionField = typeof(VividTerrainData).GetField(
                    "m_BakeVersion",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                Assert.That(bakeVersionField, Is.Not.Null);
                bakeVersionField.SetValue(data, VividTerrainData.CurrentBakeVersion - 1u);

                Assert.That(data.TryValidate(out string reason), Is.False);
                Assert.That(reason, Does.Contain("Bake version 2 is not supported"));
                Assert.That(reason, Does.Contain($"expected {VividTerrainData.CurrentBakeVersion}"));
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void BakeToAsset_SamplesHeightmapAndPersistsCompressedMeshletSubAssets()
        {
            EnsureSupportedPlatform();
            TerrainData source = CreateTerrainData("HeightSource", out float expectedMaximumHeight);
            string sourcePath = TempFolder + "/HeightSource.asset";
            string bakedPath = TempFolder + "/HeightSource_VividTerrain.asset";
            AssetDatabase.CreateAsset(source, sourcePath);
            string sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath);

            var progressValues = new List<float>();
            VividTerrainData baked = VividTerrainBaker.BakeToAsset(
                bakedPath,
                new VividTerrainBaker.Parameters
                {
                    SourceTerrainData = source,
                    SourceTerrainDataGUID = sourceGuid,
                    Settings = new VividTerrainBakeSettings(
                        1,
                        16,
                        optimizeVertexCache: true,
                        maxMeshLODLevelCount: 4
                    ),
                    ProgressHandler = (progress, _) => progressValues.Add(progress),
                    LogErrorHandler = Assert.Fail,
                }
            );

            Assert.That(baked, Is.Not.Null);
            Assert.That(baked.IsValid, Is.True);
            Assert.That(baked.SourceTerrainDataGUID, Is.EqualTo(sourceGuid));
            Assert.That(baked.SourceHeightmapResolution, Is.EqualTo(33));
            Assert.That(baked.Size, Is.EqualTo(new Vector3(32.0f, 10.0f, 32.0f)));
            Assert.That(baked.ChunkGridSize, Is.EqualTo(new Vector2Int(2, 2)));
            Assert.That(baked.Chunks, Has.Count.EqualTo(4));
            Assert.That(baked.GeometryChunkCount, Is.EqualTo(4));
            Assert.That(baked.BakeSettings.MaxMeshLODLevelCount, Is.EqualTo(4));
            Assert.That(baked.GeometryChunkLODRange.x, Is.GreaterThanOrEqualTo(1));
            Assert.That(baked.GeometryChunkLODRange.y, Is.LessThanOrEqualTo(4));
            Assert.That(baked.LocalBounds.min.y, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(baked.LocalBounds.max.y, Is.EqualTo(expectedMaximumHeight).Within(0.0001f));
            Assert.That(progressValues, Is.Not.Empty);
            Assert.That(progressValues[^1], Is.EqualTo(1.0f));

            VividTerrainChunkData firstChunk = baked.Chunks[0];
            Assert.That(firstChunk.Coordinate, Is.EqualTo(Vector2Int.zero));
            Assert.That(firstChunk.HeightmapSampleMin, Is.EqualTo(Vector2Int.zero));
            Assert.That(firstChunk.HeightmapSampleMax, Is.EqualTo(new Vector2Int(16, 16)));

            foreach (VividTerrainChunkData chunk in baked.Chunks)
            {
                Assert.That(chunk.MeshletCollection, Is.Not.Null);
                Assert.That(chunk.LODCount, Is.InRange(1, baked.BakeSettings.MaxMeshLODLevelCount));
                Assert.That(chunk.UsesSupportedLODCount, Is.True);
                Assert.That(chunk.MeshletCollection.Meshlets, Is.Not.Empty);
                Assert.That(chunk.MeshletCollection.VertexBuffer, Is.Not.Empty);
                Assert.That(chunk.MeshletCollection.IndexBuffer, Is.Not.Empty);
                Assert.That(ReadMeshletBlob(chunk.MeshletCollection), Is.Not.Empty);
            }

            VividMeshletCollectionAsset[] persistedMeshlets = AssetDatabase.LoadAllAssetsAtPath(bakedPath)
                .OfType<VividMeshletCollectionAsset>()
                .ToArray();
            Assert.That(persistedMeshlets, Has.Length.EqualTo(4));

            VividTerrainData reloaded = AssetDatabase.LoadAssetAtPath<VividTerrainData>(bakedPath);
            Assert.That(reloaded, Is.Not.Null);
            Assert.That(reloaded.Chunks, Has.Count.EqualTo(4));
            Assert.That(reloaded.Chunks.All(chunk => chunk.HasGeometry), Is.True);
        }

        [Test]
        public void BakeToAsset_PersistsTerrainControlMapBeforeBuildingStreamedVirtualTexture()
        {
            EnsureSupportedPlatform();
            TerrainData source = CreateTerrainData("StreamedControlSource", out _);
            string sourcePath = TempFolder + "/StreamedControlSource.asset";
            string bakedPath = TempFolder + "/StreamedControlSource_VividTerrain.asset";
            string firstLayerPath = TempFolder + "/StreamedControlLayer0.terrainlayer";
            string secondLayerPath = TempFolder + "/StreamedControlLayer1.terrainlayer";
            AssetDatabase.CreateAsset(source, sourcePath);

            var firstLayer = new TerrainLayer { name = "Streamed Control Layer 0" };
            var secondLayer = new TerrainLayer { name = "Streamed Control Layer 1" };
            AssetDatabase.CreateAsset(firstLayer, firstLayerPath);
            AssetDatabase.CreateAsset(secondLayer, secondLayerPath);
            source.terrainLayers = new[] { firstLayer, secondLayer };
            source.alphamapResolution = 8;

            var alphamaps = new float[8, 8, 2];
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    float secondLayerWeight = x / 7.0f;
                    alphamaps[y, x, 0] = 1.0f - secondLayerWeight;
                    alphamaps[y, x, 1] = secondLayerWeight;
                }
            }

            source.SetAlphamaps(0, 0, alphamaps);
            EditorUtility.SetDirty(source);
            AssetDatabase.SaveAssets();

            VividTerrainData baked = VividTerrainBaker.BakeToAsset(
                bakedPath,
                new VividTerrainBaker.Parameters
                {
                    SourceTerrainData = source,
                    SourceTerrainDataGUID = AssetDatabase.AssetPathToGUID(sourcePath),
                    Settings = new VividTerrainBakeSettings(1, 32, optimizeVertexCache: false),
                    LogErrorHandler = Assert.Fail,
                });

            Assert.That(baked.ControlMaps, Has.Count.EqualTo(1));
            Texture2D controlMap = baked.ControlMaps[0];
            Assert.That(controlMap, Is.Not.Null);
            Assert.That(EditorUtility.IsPersistent(controlMap), Is.True);
            Assert.That(AssetDatabase.GetAssetPath(controlMap), Does.EndWith("_Control0_Source.asset"));
            Assert.That(controlMap, Is.Not.SameAs(source.alphamapTextures[0]));
            Assert.That(baked.ControlVirtualTextures, Has.Count.EqualTo(1));
            Assert.That(baked.ControlVirtualTextures[0], Is.Not.Null);

            VividTerrainData reloaded = AssetDatabase.LoadAssetAtPath<VividTerrainData>(bakedPath);
            Assert.That(reloaded.ControlMaps, Has.Count.EqualTo(1));
            Assert.That(reloaded.ControlMaps[0], Is.Not.Null);
            Assert.That(reloaded.ControlVirtualTextures, Has.Count.EqualTo(1));
            Assert.That(reloaded.ControlVirtualTextures[0], Is.Not.Null);
        }

        [Test]
        public void Generate_BuildsMultipleLODLevelsAndPreservesChunkBorders()
        {
            EnsureSupportedPlatform();
            TerrainData source = CreateTerrainData("LODSource", out _);
            var baked = ScriptableObject.CreateInstance<VividTerrainData>();

            try
            {
                VividTerrainBaker.Generate(
                    baked,
                    new VividTerrainBaker.Parameters
                    {
                        SourceTerrainData = source,
                        Settings = new VividTerrainBakeSettings(
                            1,
                            32,
                            optimizeVertexCache: true,
                            maxMeshLODLevelCount: 4
                        ),
                        LogErrorHandler = Assert.Fail,
                    }
                );

                Assert.That(baked.ChunkGridSize, Is.EqualTo(Vector2Int.one));
                Assert.That(baked.GeometryChunkLODRange.x, Is.GreaterThan(1));
                Assert.That(baked.GeometryChunkLODRange.y, Is.LessThanOrEqualTo(4));
                VividTerrainChunkData chunk = baked.Chunks[0];
                Assert.That(
                    chunk.MeshletCollection.MeshLODLevelNodeCounts[^1],
                    Is.EqualTo(chunk.MeshletCollection.LeafMeshletCount)
                );
                AssertChunkBordersArePreservedAcrossLODs(chunk, expectedQuadCount: 32);
            }
            finally
            {
                DestroyGeneratedData(baked);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void Generate_CapturesTerrainLayersAndControlMapsForGPUDrivenBlending()
        {
            EnsureSupportedPlatform();
            TerrainData source = CreateTerrainData("ControlMapSource", out _);
            var firstLayer = new TerrainLayer { name = "Control Layer 0" };
            var secondLayer = new TerrainLayer { name = "Control Layer 1" };
            var baked = ScriptableObject.CreateInstance<VividTerrainData>();

            try
            {
                source.terrainLayers = new[] { firstLayer, secondLayer };
                source.alphamapResolution = 8;
                var alphamaps = new float[8, 8, 2];
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        float secondLayerWeight = x / 7.0f;
                        alphamaps[y, x, 0] = 1.0f - secondLayerWeight;
                        alphamaps[y, x, 1] = secondLayerWeight;
                    }
                }
                source.SetAlphamaps(0, 0, alphamaps);

                VividTerrainBaker.Generate(
                    baked,
                    new VividTerrainBaker.Parameters
                    {
                        SourceTerrainData = source,
                        Settings = new VividTerrainBakeSettings(1, 32, optimizeVertexCache: false),
                        LogErrorHandler = Assert.Fail,
                    }
                );

                Assert.That(baked.Layers, Has.Count.EqualTo(2));
                Assert.That(baked.ControlMaps, Has.Count.EqualTo(1));
                Assert.That(baked.ControlMaps[0], Is.SameAs(source.alphamapTextures[0]));
                Assert.That(baked.SupportedSurfaceLayerCount, Is.EqualTo(2));
                Assert.That(baked.RequiredControlMapCount, Is.EqualTo(1));
                Assert.That(baked.HasCompleteControlMapData, Is.True);
            }
            finally
            {
                DestroyGeneratedData(baked);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(firstLayer);
                Object.DestroyImmediate(secondLayer);
            }
        }

        [Test]
        public void Generate_UsesSampleStrideAndRetainsLastHeightmapEdge()
        {
            EnsureSupportedPlatform();
            TerrainData source = CreateTerrainData("StrideSource", out _);
            var baked = ScriptableObject.CreateInstance<VividTerrainData>();

            try
            {
                VividTerrainBaker.Generate(
                    baked,
                    new VividTerrainBaker.Parameters
                    {
                        SourceTerrainData = source,
                        Settings = new VividTerrainBakeSettings(6, 4, optimizeVertexCache: false),
                        LogErrorHandler = Assert.Fail,
                    }
                );

                Assert.That(baked.ChunkGridSize, Is.EqualTo(new Vector2Int(2, 2)));
                Assert.That(baked.Chunks[^1].HeightmapSampleMax, Is.EqualTo(new Vector2Int(32, 32)));
                Assert.That(baked.Chunks[^1].LocalBounds.max.x, Is.EqualTo(source.size.x).Within(0.0001f));
                Assert.That(baked.Chunks[^1].LocalBounds.max.z, Is.EqualTo(source.size.z).Within(0.0001f));
            }
            finally
            {
                DestroyGeneratedData(baked);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void Generate_DoesNotCreateMeshletCollectionsForFullyHoledTerrain()
        {
            EnsureSupportedPlatform();
            TerrainData source = CreateTerrainData("HoledSource", out _);
            var holes = new bool[source.holesResolution, source.holesResolution];
            source.SetHoles(0, 0, holes);
            var baked = ScriptableObject.CreateInstance<VividTerrainData>();

            try
            {
                VividTerrainBaker.Generate(
                    baked,
                    new VividTerrainBaker.Parameters
                    {
                        SourceTerrainData = source,
                        Settings = new VividTerrainBakeSettings(1, 16, optimizeVertexCache: true),
                        LogErrorHandler = Assert.Fail,
                    }
                );

                Assert.That(baked.IsValid, Is.True);
                Assert.That(baked.Chunks, Has.Count.EqualTo(4));
                Assert.That(baked.GeometryChunkCount, Is.Zero);
                Assert.That(baked.Chunks.All(chunk => chunk.MeshletCollection == null), Is.True);
            }
            finally
            {
                DestroyGeneratedData(baked);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void TerrainData_RejectsChunkExceedingBakedLODLimit()
        {
            VividTerrainData data = null;
            VividMeshletCollectionAsset meshlets = null;

            try
            {
                meshlets = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();
                meshlets.MeshLODLevelCount = 3;
                data = ScriptableObject.CreateInstance<VividTerrainData>();
                data.Initialize(
                    string.Empty,
                    "UnsupportedLOD",
                    17,
                    new Vector3(16.0f, 4.0f, 16.0f),
                    new Bounds(new Vector3(8.0f, 2.0f, 8.0f), new Vector3(16.0f, 4.0f, 16.0f)),
                    Vector2Int.one,
                    new VividTerrainBakeSettings(
                        1,
                        16,
                        optimizeVertexCache: true,
                        maxMeshLODLevelCount: 2
                    ),
                    null,
                    Array.Empty<VividTerrainLayerData>(),
                    new[]
                    {
                        new VividTerrainChunkData(
                            Vector2Int.zero,
                            Vector2Int.zero,
                            new Vector2Int(16, 16),
                            new Bounds(new Vector3(8.0f, 2.0f, 8.0f), new Vector3(16.0f, 4.0f, 16.0f)),
                            meshlets
                        ),
                    }
                );

                Assert.That(data.IsValid, Is.False);
                Assert.That(data.TryValidate(out string reason), Is.False);
                Assert.That(reason, Does.Contain("exceeding its baked limit of 2"));
                Assert.That(data.Chunks[0].UsesSupportedLODCount, Is.True);
            }
            finally
            {
                if (data != null)
                {
                    Object.DestroyImmediate(data);
                }

                if (meshlets != null)
                {
                    Object.DestroyImmediate(meshlets);
                }
            }
        }

        [Test]
        public void BakeSettings_PreserveLegacySingleLODAndDefaultNewBakesToFourLODs()
        {
            Assert.That(
                default(VividTerrainBakeSettings).MaxMeshLODLevelCount,
                Is.EqualTo(VividTerrainBakeSettings.LegacyMaxMeshLODLevelCount)
            );
            Assert.That(
                VividTerrainBakeSettings.Default.MaxMeshLODLevelCount,
                Is.EqualTo(VividTerrainBakeSettings.DefaultMaxMeshLODLevelCount)
            );
            Assert.That(
                new VividTerrainBakeSettings(1, 64, maxMeshLODLevelCount: 3).MaxMeshLODLevelCount,
                Is.EqualTo(3)
            );
        }

        [Test]
        public void CreateCopy_CreatesVividTerrainWithoutChangingSourceTerrain()
        {
            EnsureSupportedPlatform();
            TerrainData source = CreateTerrainData("ConversionSource", out _);
            string sourcePath = TempFolder + "/ConversionSource.asset";
            string bakedPath = TempFolder + "/ConversionSource_VividTerrain.asset";
            AssetDatabase.CreateAsset(source, sourcePath);

            var gameObject = new GameObject("Terrain To Convert");
            m_GameObjects.Add(gameObject);
            gameObject.transform.SetLocalPositionAndRotation(
                new Vector3(3.0f, 4.0f, 5.0f),
                Quaternion.Euler(0.0f, 25.0f, 0.0f)
            );
            gameObject.transform.localScale = new Vector3(2.0f, 1.0f, 2.0f);
            UnityEngine.Terrain terrain = gameObject.AddComponent<UnityEngine.Terrain>();
            terrain.terrainData = source;
            TerrainCollider sourceCollider = gameObject.AddComponent<TerrainCollider>();
            sourceCollider.terrainData = source;
            float sourceHeightBeforeConversion = source.GetHeight(16, 16);

            VividTerrainConversionUtility.Result result = VividTerrainConversionUtility.CreateCopy(
                terrain,
                bakedPath,
                new VividTerrainBakeSettings(1, 32, optimizeVertexCache: true)
            );

            GameObject convertedObject = result.Component.gameObject;
            m_GameObjects.Add(convertedObject);

            Assert.That(convertedObject, Is.Not.SameAs(gameObject));
            Assert.That(convertedObject.name, Is.EqualTo(gameObject.name + " VividTerrain"));
            Assert.That(gameObject.GetComponent<UnityEngine.Terrain>(), Is.SameAs(terrain));
            Assert.That(terrain.terrainData, Is.SameAs(source));
            Assert.That(gameObject.GetComponent<TerrainCollider>(), Is.SameAs(sourceCollider));
            Assert.That(sourceCollider.terrainData, Is.SameAs(source));
            Assert.That(source.GetHeight(16, 16), Is.EqualTo(sourceHeightBeforeConversion));
            Assert.That(gameObject.GetComponent<VividTerrain>(), Is.Null);
            Assert.That(convertedObject.GetComponent<UnityEngine.Terrain>(), Is.Null);
            Assert.That(convertedObject.GetComponent<TerrainCollider>().terrainData, Is.SameAs(source));
            Assert.That(convertedObject.GetComponent<VividTerrain>(), Is.SameAs(result.Component));
            Assert.That(convertedObject.transform.position, Is.EqualTo(gameObject.transform.position));
            Assert.That(convertedObject.transform.rotation, Is.EqualTo(gameObject.transform.rotation));
            Assert.That(convertedObject.transform.localScale, Is.EqualTo(gameObject.transform.localScale));
            Assert.That(result.Component.Data, Is.SameAs(result.Data));
            Assert.That(result.Component.HasBakedData, Is.True);
            Assert.That(result.Data.SourceTerrainDataGUID, Is.EqualTo(AssetDatabase.AssetPathToGUID(sourcePath)));
            Assert.That(AssetDatabase.LoadAssetAtPath<VividTerrainData>(bakedPath), Is.SameAs(result.Data));
        }

        private static TerrainData CreateTerrainData(string name, out float expectedMaximumHeight)
        {
            const int resolution = 33;
            var terrainData = new TerrainData
            {
                name = name,
                heightmapResolution = resolution,
                size = new Vector3(32.0f, 10.0f, 32.0f),
            };
            var heights = new float[resolution, resolution];
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    heights[y, x] = (x + y) / (float) ((resolution - 1) * 2) * 0.5f;
                }
            }

            terrainData.SetHeights(0, 0, heights);
            expectedMaximumHeight = 5.0f;
            return terrainData;
        }

        private static void AssertChunkBordersArePreservedAcrossLODs(
            in VividTerrainChunkData chunk,
            int expectedQuadCount)
        {
            VividMeshletCollectionAsset collection = chunk.MeshletCollection;
            Assert.That(collection, Is.Not.Null);
            Assert.That(collection.MeshLODLevelCount, Is.GreaterThan(1));

            Bounds bounds = chunk.LocalBounds;
            float stepX = bounds.size.x / expectedQuadCount;
            float stepZ = bounds.size.z / expectedQuadCount;
            for (int levelIndex = 0; levelIndex < collection.MeshLODLevelCount; levelIndex++)
            {
                var borderCoordinates = new HashSet<Vector2Int>();
                foreach (var node in collection.MeshLODNodes)
                {
                    if (node.LevelIndex != (uint) levelIndex)
                    {
                        continue;
                    }

                    for (uint meshletOffset = 0; meshletOffset < node.MeshletCount; meshletOffset++)
                    {
                        VividMeshlet meshlet = collection.Meshlets[(int) (node.MeshletStartIndex + meshletOffset)];
                        for (uint vertexOffset = 0; vertexOffset < meshlet.VertexCount; vertexOffset++)
                        {
                            var position = collection.VertexBuffer[(int) (meshlet.VertexOffset + vertexOffset)].Position;
                            int x = Mathf.RoundToInt((position.x - bounds.min.x) / stepX);
                            int y = Mathf.RoundToInt((position.z - bounds.min.z) / stepZ);
                            if (x == 0 || y == 0 || x == expectedQuadCount || y == expectedQuadCount)
                            {
                                borderCoordinates.Add(new Vector2Int(x, y));
                            }
                        }
                    }
                }

                for (int coordinate = 0; coordinate <= expectedQuadCount; coordinate++)
                {
                    Assert.That(
                        borderCoordinates,
                        Does.Contain(new Vector2Int(coordinate, 0)),
                        $"LOD {levelIndex} removed a bottom border vertex."
                    );
                    Assert.That(
                        borderCoordinates,
                        Does.Contain(new Vector2Int(coordinate, expectedQuadCount)),
                        $"LOD {levelIndex} removed a top border vertex."
                    );
                    Assert.That(
                        borderCoordinates,
                        Does.Contain(new Vector2Int(0, coordinate)),
                        $"LOD {levelIndex} removed a left border vertex."
                    );
                    Assert.That(
                        borderCoordinates,
                        Does.Contain(new Vector2Int(expectedQuadCount, coordinate)),
                        $"LOD {levelIndex} removed a right border vertex."
                    );
                }
            }
        }

        private static byte[] ReadMeshletBlob(VividMeshletCollectionAsset meshlets)
        {
            FieldInfo blobField = typeof(VividMeshletCollectionAsset).GetField(
                "m_MeshDataBlob",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.That(blobField, Is.Not.Null);
            return (byte[]) blobField.GetValue(meshlets);
        }

        private static void DestroyGeneratedData(VividTerrainData data)
        {
            if (data == null)
            {
                return;
            }

            foreach (VividTerrainChunkData chunk in data.Chunks)
            {
                if (chunk.MeshletCollection != null)
                {
                    Object.DestroyImmediate(chunk.MeshletCollection);
                }
            }

            Object.DestroyImmediate(data);
        }

        private static void EnsureSupportedPlatform()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                Assert.Ignore("Meshoptimizer native plugins are currently configured for Windows Editor only.");
            }
        }
    }
}
