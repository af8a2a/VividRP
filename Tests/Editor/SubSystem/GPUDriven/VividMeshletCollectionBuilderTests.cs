using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using Unity.Collections;
using Unity.Mathematics;
using VividRP.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;
using VividRP.Editor.GPUDriven.Meshlets;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Editor.Tests
{
    public class VividMeshletCollectionBuilderTests
    {
        private const string TempFolder = "Assets/VividRP_Temp_MeshletTests";

        [SetUp]
        public void SetUp()
        {
            EnsureSupportedPlatform();
            DeleteTempFolder();
            EnsureTempFolderExists();
        }

        [TearDown]
        public void TearDown()
        {
            DeleteTempFolder();
        }

        [Test]
        public void Generate_ProducesStableMeshletData_WhenCalledTwiceForSameMesh()
        {
            EnsureSupportedPlatform();

            Mesh mesh = CreateGridMesh(20, 20);
            var first = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();
            var second = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();

            try
            {
                VividMeshletCollectionBuilder.Generate(first, CreateParameters(mesh));
                VividMeshletCollectionBuilder.Generate(second, CreateParameters(mesh));

                Assert.That(first.Meshlets, Is.Not.Empty);
                Assert.That(first.VertexBuffer, Is.Not.Empty);
                Assert.That(first.IndexBuffer, Is.Not.Empty);
                Assert.That(second.LeafMeshletCount, Is.EqualTo(first.LeafMeshletCount));
                Assert.That(second.MeshLODLevelCount, Is.EqualTo(first.MeshLODLevelCount));
                Assert.That(second.SourceMeshLocalFileID, Is.EqualTo(first.SourceMeshLocalFileID));
                Assert.That(second.SourceSubmeshIndex, Is.EqualTo(first.SourceSubmeshIndex));
                Assert.That(second.Bounds.center, Is.EqualTo(first.Bounds.center));
                Assert.That(second.Bounds.size, Is.EqualTo(first.Bounds.size));
                CollectionAssert.AreEqual(first.MeshLODLevelNodeCounts, second.MeshLODLevelNodeCounts);
                CollectionAssert.AreEqual(first.MeshLODNodes, second.MeshLODNodes);
                CollectionAssert.AreEqual(first.MeshLODGroups, second.MeshLODGroups);
                CollectionAssert.AreEqual(first.Meshlets, second.Meshlets);
                CollectionAssert.AreEqual(first.VertexBuffer, second.VertexBuffer);
                CollectionAssert.AreEqual(first.IndexBuffer, second.IndexBuffer);
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Generate_QuantizesBeforeClusteringAndKeepsBoundsConservative()
        {
            Mesh mesh = CreateGridMesh(20, 20);
            var asset = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();
            try
            {
                Vector3[] positions = mesh.vertices;
                var quantized = new System.Collections.Generic.HashSet<Vector3>();
                for (int i = 0; i < positions.Length; i++)
                {
                    positions[i] = positions[i] * 0.1234567f + new Vector3(123.4567f, -23.4567f, 34.5678f);
                    quantized.Add(VividMeshletVertexPacking.QuantizePosition(positions[i]));
                }
                mesh.vertices = positions;
                mesh.RecalculateBounds();
                VividMeshletCollectionBuilder.Generate(asset, CreateParameters(mesh));
                Assert.That(asset.Meshlets, Is.Not.Empty);
                foreach (VividMeshlet meshlet in asset.Meshlets)
                {
                    for (uint i = 0; i < meshlet.VertexCount; i++)
                    {
                        Unity.Mathematics.float3 p = asset.VertexBuffer[meshlet.VertexOffset + i].Position;
                        Assert.That(quantized.Contains(p), Is.True, "LOD output must retain the shared quantized position grid.");
                        Assert.That(asset.Bounds.Contains(p), Is.True);
                        Assert.That(Unity.Mathematics.math.distance(p, meshlet.BoundingSphere.xyz),
                            Is.LessThanOrEqualTo(meshlet.BoundingSphere.w + 1e-5f));
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Generate_PreserveFinestLODLevels_KeepsOriginalLeafMeshletsWhenTruncated()
        {
            Mesh mesh = CreateGridMesh(64, 64);
            var asset = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();

            try
            {
                VividMeshletCollectionBuilder.Parameters parameters = CreateParameters(mesh);
                parameters.MaxMeshLODLevelCount = 2;
                parameters.DisableSloppySimplification = true;
                parameters.PreserveFinestLODLevels = true;

                VividMeshletCollectionBuilder.Generate(asset, parameters);

                Assert.That(asset.MeshLODLevelCount, Is.InRange(1, 2));
                Assert.That(asset.MeshLODLevelNodeCounts[^1], Is.EqualTo(asset.LeafMeshletCount));
            }
            finally
            {
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Importer_CreatesMeshletAsset_WhenMeshIsAssigned()
        {
            EnsureSupportedPlatform();

            Mesh mesh = CreateGridMesh(20, 20);
            try
            {
                string meshPath = TempFolder + "/TestMesh.asset";
                string collectionPath = TempFolder + "/TestMesh." + VividMeshletCollectionAssetImporter.Extension;

                AssetDatabase.CreateAsset(mesh, meshPath);
                File.WriteAllText(GetAbsolutePath(collectionPath), string.Empty);
                AssetDatabase.ImportAsset(collectionPath, ImportAssetOptions.ForceSynchronousImport);

                var importer = AssetImporter.GetAtPath(collectionPath) as VividMeshletCollectionAssetImporter;
                Assert.That(importer, Is.Not.Null);

                importer.Mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                importer.SubMeshIndex = 0;
                importer.OptimizeVertexCache = true;
                importer.MaxMeshLODLevelCount = 4;
                importer.TargetError = 0.01f;
                importer.TargetErrorSloppy = 0.001f;
                importer.MinTriangleReductionPerStep = 0.8f;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();

                VividMeshletCollectionAsset asset = AssetDatabase.LoadAssetAtPath<VividMeshletCollectionAsset>(collectionPath);
                Assert.That(asset, Is.Not.Null);
                Assert.That(asset.SourceMeshName, Is.EqualTo("TestMesh"));
                Assert.That(asset.SourceMeshLocalFileID, Is.Not.EqualTo(0L));
                Assert.That(asset.SourceSubmeshIndex, Is.EqualTo(0));
                Assert.That(asset.Meshlets, Is.Not.Empty);
                Assert.That(asset.VertexBuffer, Is.Not.Empty);
                Assert.That(asset.IndexBuffer, Is.Not.Empty);
                Assert.That(asset.MeshLODNodes, Is.Not.Empty);
            }
            finally
            {
                if (mesh != null)
                {
                    Object.DestroyImmediate(mesh, true);
                }
            }
        }

        [Test]
        public void AssetSerialization_RoundTripsMeshletDataThroughBinaryBlob_WhenSavedAndReloaded()
        {
            var asset = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();

            try
            {
                asset.SourceMeshName = "SerializedMesh";
                asset.SourceMeshLocalFileID = 42L;
                asset.SourceSubmeshIndex = 1;
                asset.Bounds = new Bounds(new Vector3(1.0f, 2.0f, 3.0f), new Vector3(4.0f, 5.0f, 6.0f));
                asset.LeafMeshletCount = 1;
                asset.MeshLODLevelCount = 1;
                asset.MeshLODLevelNodeCounts = new[] { 1 };
                asset.SetMeshData(asset.MeshLODLevelNodeCounts, new[]
                {
                    new VividMeshLODNode
                    {
                        MeshletStartIndex = 0,
                        MeshletCount = 1,
                        LevelIndex = 0,
                    },
                }, asset.Meshlets, asset.VertexBuffer, asset.IndexBuffer);
                asset.Meshlets = new[]
                {
                    new VividMeshlet
                    {
                        VertexOffset = 0,
                        TriangleOffset = 0,
                        VertexCount = 3,
                        TriangleCount = 1,
                    },
                };
                asset.VertexBuffer = new[]
                {
                    VividMeshletVertexPacking.Pack(
                        new Unity.Mathematics.float3(0.0f, 0.0f, 0.0f),
                        new Unity.Mathematics.float3(0.0f, 0.0f, 1.0f),
                        new Unity.Mathematics.float4(1.0f, 0.0f, 0.0f, 1.0f),
                        new Unity.Mathematics.float2(0.0f, 0.0f)),
                    VividMeshletVertexPacking.Pack(
                        new Unity.Mathematics.float3(1.0f, 0.0f, 0.0f),
                        new Unity.Mathematics.float3(0.0f, 0.0f, 1.0f),
                        new Unity.Mathematics.float4(1.0f, 0.0f, 0.0f, 1.0f),
                        new Unity.Mathematics.float2(1.0f, 0.0f)),
                    VividMeshletVertexPacking.Pack(
                        new Unity.Mathematics.float3(0.0f, 1.0f, 0.0f),
                        new Unity.Mathematics.float3(0.0f, 0.0f, 1.0f),
                        new Unity.Mathematics.float4(1.0f, 0.0f, 0.0f, 1.0f),
                        new Unity.Mathematics.float2(0.0f, 1.0f)),
                };
                asset.IndexBuffer = new byte[] { 0, 1, 2 };
                asset.MarkChanged();

                string assetPath = TempFolder + "/SerializedMeshlets.asset";
                AssetDatabase.CreateAsset(asset, assetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

                VividMeshletCollectionAsset reloaded = AssetDatabase.LoadAssetAtPath<VividMeshletCollectionAsset>(assetPath);
                Assert.That(reloaded, Is.Not.Null);
                Assert.That(reloaded.SourceMeshName, Is.EqualTo(asset.SourceMeshName));
                Assert.That(reloaded.SourceMeshLocalFileID, Is.EqualTo(asset.SourceMeshLocalFileID));
                Assert.That(reloaded.SourceSubmeshIndex, Is.EqualTo(asset.SourceSubmeshIndex));
                Assert.That(reloaded.LeafMeshletCount, Is.EqualTo(asset.LeafMeshletCount));
                Assert.That(reloaded.MeshLODLevelCount, Is.EqualTo(asset.MeshLODLevelCount));
                Assert.That(reloaded.Bounds.center, Is.EqualTo(asset.Bounds.center));
                Assert.That(reloaded.Bounds.size, Is.EqualTo(asset.Bounds.size));
                CollectionAssert.AreEqual(asset.MeshLODLevelNodeCounts, reloaded.MeshLODLevelNodeCounts);
                CollectionAssert.AreEqual(asset.MeshLODNodes, reloaded.MeshLODNodes);
                CollectionAssert.AreEqual(asset.MeshLODGroups, reloaded.MeshLODGroups);
                CollectionAssert.AreEqual(asset.Meshlets, reloaded.Meshlets);
                CollectionAssert.AreEqual(asset.VertexBuffer, reloaded.VertexBuffer);
                CollectionAssert.AreEqual(asset.IndexBuffer, reloaded.IndexBuffer);

                FieldInfo blobField = typeof(VividMeshletCollectionAsset).GetField("m_MeshDataBlob", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(blobField, Is.Not.Null);
                var blob = blobField.GetValue(reloaded) as byte[];
                Assert.That(blob, Is.Not.Null.And.Not.Empty);
            }
            finally
            {
                if (asset != null)
                {
                    Object.DestroyImmediate(asset, true);
                }
            }
        }

        [Test]
        public void CreateAssetsForSelection_CreatesMeshletAsset_WhenModelAssetIsSelected()
        {
            EnsureSupportedPlatform();

            Mesh mesh = CreateGridMesh(20, 20);
            GameObject prefabRoot = null;

            try
            {
                string meshPath = TempFolder + "/ModelMesh.asset";
                string prefabPath = TempFolder + "/ImportedModel.prefab";

                AssetDatabase.CreateAsset(mesh, meshPath);

                prefabRoot = new GameObject("ImportedModel");
                GameObject child = new GameObject("MeshNode");
                child.transform.SetParent(prefabRoot.transform, false);
                child.AddComponent<MeshRenderer>();
                child.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                Object.DestroyImmediate(prefabRoot);
                prefabRoot = null;

                GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                string[] createdAssets = VividMeshletCollectionAssetImporter.CreateAssetsForSelection(new Object[] { modelAsset });

                Assert.That(createdAssets, Has.Length.EqualTo(1));
                Assert.That(createdAssets[0], Does.StartWith(TempFolder + "/GPUDrivenGenerated/MeshletAsset/TestMesh_Meshlets"));

                VividMeshletCollectionAsset asset = AssetDatabase.LoadAssetAtPath<VividMeshletCollectionAsset>(createdAssets[0]);
                Assert.That(asset, Is.Not.Null);
                Assert.That(asset.SourceMeshName, Is.EqualTo("TestMesh"));
                Assert.That(asset.SourceMeshLocalFileID, Is.Not.EqualTo(0L));
                Assert.That(asset.SourceSubmeshIndex, Is.EqualTo(0));
                Assert.That(asset.Meshlets, Is.Not.Empty);
                Assert.That(asset.VertexBuffer, Is.Not.Empty);
                Assert.That(asset.IndexBuffer, Is.Not.Empty);
            }
            finally
            {
                if (prefabRoot != null)
                {
                    Object.DestroyImmediate(prefabRoot);
                }

                if (mesh != null)
                {
                    Object.DestroyImmediate(mesh, true);
                }
            }
        }

        [Test]
        public void Generate_ClusterHierarchy_PreservesLeafCoverageAndConservativeBounds()
        {
            Mesh mesh = CreateGridMesh(64, 64);
            var asset = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();
            try
            {
                var parameters = CreateParameters(mesh);
                parameters.MaxMeshLODLevelCount = 0;
                parameters.DisableSloppySimplification = true;
                VividMeshletCollectionBuilder.Generate(asset, parameters);
                Assert.That(asset.MeshLODLevelCount, Is.GreaterThan(1));
                uint leafTriangles = 0;
                foreach (var packed in asset.MeshLODNodes)
                {
                    var node = VividGPUDrivenBufferSetTests.ExpandMeshLODNode(packed, asset.MeshLODGroups);
                    Assert.That(float.IsNaN(node.Error) || float.IsInfinity(node.Error), Is.False);
                    Assert.That(node.Error, Is.GreaterThanOrEqualTo(0));
                    if (node.LevelIndex == 0)
                        Assert.That(node.ParentError, Is.LessThan(0));
                    else
                    {
                        Assert.That(node.ParentError, Is.GreaterThanOrEqualTo(node.Error));
                        Assert.That(node.ParentBounds.w + 1e-4f, Is.GreaterThanOrEqualTo(
                            math.distance(node.Bounds.xyz, node.ParentBounds.xyz) + node.Bounds.w));
                        bool sharedTransitionFound = false;
                        foreach (var parent in asset.MeshLODNodes)
                        {
                            if (parent.LevelIndex + 1 != node.LevelIndex) continue;
                            if (parent.GroupIndex == packed.ParentGroupIndex)
                                sharedTransitionFound = true;
                        }
                        Assert.That(sharedTransitionFound, Is.True, "Parent and child must share exact group Bounds/Error.");
                    }
                    Assert.That(node.MeshletCount, Is.EqualTo(1));
                    VividMeshlet meshlet = asset.Meshlets[node.MeshletStartIndex];
                    Assert.That(meshlet.VertexCount, Is.InRange(1u, VividMeshletConfiguration.MaxMeshletVertices));
                    Assert.That(meshlet.TriangleCount, Is.InRange(1u, VividMeshletConfiguration.MaxMeshletTriangles));
                    for (uint i = 0; i < meshlet.TriangleCount * 3; i++)
                        Assert.That(asset.IndexBuffer[meshlet.TriangleOffset + i], Is.LessThan(meshlet.VertexCount));
                    if (node.LevelIndex == asset.MeshLODLevelCount - 1)
                        leafTriangles += meshlet.TriangleCount;
                }
                Assert.That(leafTriangles, Is.EqualTo(64 * 64 * 2));
            }
            finally
            {
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(mesh);
            }
        }

        [TestCase(VividMeshOptimizer.SimplifyMode.Normal)]
        [TestCase(VividMeshOptimizer.SimplifyMode.Sloppy)]
        public void SimplifyMeshlets_LockedVertices_RetainsAllTriangles(VividMeshOptimizer.SimplifyMode mode)
        {
            Mesh mesh = CreateGridMesh(20, 20);
            try
            {
                using var positions = CopyPositions(mesh, 1.0f);
                using var indices = new NativeArray<uint>(System.Array.ConvertAll(mesh.triangles, i => (uint) i), Allocator.TempJob);
                var locks = new NativeArray<byte>(mesh.vertexCount, Allocator.TempJob);
                for (int i = 0; i < locks.Length; i++)
                    locks[i] = (byte) meshopt_SimplifyVertexFlags.Lock;
                var settings = VividMeshletCollectionAsset.MeshletGenerationParams;
                using var source = VividMeshOptimizer.BuildMeshlets(Allocator.TempJob, positions, 0, 12, indices, settings);
                var groups = new NativeArray<VividMeshOptimizer.MeshletBuildResults>(1, Allocator.Temp);
                groups[0] = source;
                try
                {
                    var layout = new VividMeshOptimizer.VertexLayout { Vertices = positions, PositionStride = 12 };
                    using var result = VividMeshOptimizer.SimplifyMeshlets(Allocator.TempJob, groups, layout, settings,
                        mode, 1.0f, out float error, locks);
                    Assert.That(CountTriangles(result), Is.EqualTo(indices.Length / 3));
                    Assert.That(error, Is.EqualTo(0.0f));
                }
                finally
                {
                    groups.Dispose();
                    locks.Dispose();
                }
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void SimplifyMeshlets_MergesSharedVertexIds_AndReturnsObjectSpaceError()
        {
            Mesh mesh = CreateGridMesh(20, 20);
            try
            {
                Vector3[] vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++)
                    vertices[i].z = Mathf.Sin(vertices[i].x * 0.7f) * Mathf.Cos(vertices[i].y * 0.6f);
                mesh.vertices = vertices;
                float unitError = SimplifyAtScale(mesh, 1, out int unitTriangles);
                float scaledError = SimplifyAtScale(mesh, 8, out int scaledTriangles);
                Assert.That(unitTriangles, Is.LessThan(mesh.triangles.Length / 3 * 0.8f));
                Assert.That(scaledTriangles, Is.EqualTo(unitTriangles));
                Assert.That(unitError, Is.GreaterThan(0));
                Assert.That(scaledError, Is.EqualTo(unitError * 8).Within(unitError * 0.001f));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void SimplifyMeshlets_EmptySloppyResult_KeepsSourceGeometry()
        {
            using var positions = new NativeArray<float>(new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f }, Allocator.TempJob);
            using var indices = new NativeArray<uint>(new[] { 0u, 1u, 2u }, Allocator.TempJob);
            var settings = VividMeshletCollectionAsset.MeshletGenerationParams;
            using var source = VividMeshOptimizer.BuildMeshlets(Allocator.TempJob, positions, 0, 12, indices, settings);
            var groups = new NativeArray<VividMeshOptimizer.MeshletBuildResults>(1, Allocator.Temp);
            groups[0] = source;
            try
            {
                var layout = new VividMeshOptimizer.VertexLayout { Vertices = positions, PositionStride = 12 };
                using var result = VividMeshOptimizer.SimplifyMeshlets(Allocator.TempJob, groups, layout, settings,
                    VividMeshOptimizer.SimplifyMode.Sloppy, 1.0f, out float error);
                Assert.That(CountTriangles(result), Is.EqualTo(1));
                Assert.That(error, Is.EqualTo(0));
                CollectionAssert.AreEquivalent(source.Vertices.ToArray(), result.Vertices.ToArray());
            }
            finally { groups.Dispose(); }
        }

        private static float SimplifyAtScale(Mesh mesh, float scale, out int triangleCount)
        {
            using var positions = CopyPositions(mesh, scale);
            using var indices = new NativeArray<uint>(System.Array.ConvertAll(mesh.triangles, i => (uint) i), Allocator.TempJob);
            var settings = VividMeshletCollectionAsset.MeshletGenerationParams;
            using var source = VividMeshOptimizer.BuildMeshlets(Allocator.TempJob, positions, 0, 12, indices, settings);
            Assert.That(source.Meshlets.Length, Is.GreaterThan(1));
            var groups = new NativeArray<VividMeshOptimizer.MeshletBuildResults>(1, Allocator.Temp);
            groups[0] = source;
            try
            {
                var layout = new VividMeshOptimizer.VertexLayout { Vertices = positions, PositionStride = 12 };
                using var result = VividMeshOptimizer.SimplifyMeshlets(Allocator.TempJob, groups, layout, settings,
                    VividMeshOptimizer.SimplifyMode.Normal, 0.1f, out float error);
                triangleCount = CountTriangles(result);
                return error;
            }
            finally { groups.Dispose(); }
        }

        private static NativeArray<float> CopyPositions(Mesh mesh, float scale)
        {
            Vector3[] vertices = mesh.vertices;
            var result = new NativeArray<float>(vertices.Length * 3, Allocator.TempJob);
            for (int i = 0; i < vertices.Length; i++)
            {
                result[i * 3] = vertices[i].x * scale;
                result[i * 3 + 1] = vertices[i].y * scale;
                result[i * 3 + 2] = vertices[i].z * scale;
            }
            return result;
        }

        private static int CountTriangles(VividMeshOptimizer.MeshletBuildResults build)
        {
            int count = 0;
            foreach (meshopt_Meshlet meshlet in build.Meshlets)
                count += (int) meshlet.TriangleCount;
            return count;
        }

        private static VividMeshletCollectionBuilder.Parameters CreateParameters(Mesh mesh)
        {
            return new VividMeshletCollectionBuilder.Parameters
            {
                Mesh = mesh,
                SourceMeshGUID = "test-guid",
                SourceMeshLocalFileID = 12345L,
                SubMeshIndex = 0,
                OptimizeVertexCache = true,
                MaxMeshLODLevelCount = 4,
                TargetError = 0.01f,
                TargetErrorSloppy = 0.001f,
                MinTriangleReductionPerStep = 0.8f,
                LogErrorHandler = message => Assert.Fail(message),
            };
        }

        private static Mesh CreateGridMesh(int columns, int rows)
        {
            var mesh = new Mesh
            {
                name = "TestMesh",
            };

            int vertexColumns = columns + 1;
            int vertexRows = rows + 1;
            var vertices = new Vector3[vertexColumns * vertexRows];
            var normals = new Vector3[vertices.Length];
            var tangents = new Vector4[vertices.Length];
            var uv = new Vector2[vertices.Length];

            for (int row = 0; row < vertexRows; row++)
            {
                for (int column = 0; column < vertexColumns; column++)
                {
                    int vertexIndex = row * vertexColumns + column;
                    vertices[vertexIndex] = new Vector3(column, row, 0.0f);
                    normals[vertexIndex] = Vector3.forward;
                    tangents[vertexIndex] = new Vector4(1.0f, 0.0f, 0.0f, 1.0f);
                    uv[vertexIndex] = new Vector2(column / (float) columns, row / (float) rows);
                }
            }

            var indices = new int[columns * rows * 6];
            int writeIndex = 0;
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int bottomLeft = row * vertexColumns + column;
                    int bottomRight = bottomLeft + 1;
                    int topLeft = bottomLeft + vertexColumns;
                    int topRight = topLeft + 1;

                    indices[writeIndex++] = bottomLeft;
                    indices[writeIndex++] = topLeft;
                    indices[writeIndex++] = bottomRight;
                    indices[writeIndex++] = bottomRight;
                    indices[writeIndex++] = topLeft;
                    indices[writeIndex++] = topRight;
                }
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.tangents = tangents;
            mesh.uv = uv;
            mesh.triangles = indices;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void EnsureSupportedPlatform()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                Assert.Ignore("Meshlet native plugins are currently configured for Windows Editor only.");
            }
        }

        private static void EnsureTempFolderExists()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(TempFolder));
            }
        }

        private static void DeleteTempFolder()
        {
            if (AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.DeleteAsset(TempFolder);
            }
        }

        private static string GetAbsolutePath(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot ?? string.Empty, assetPath);
        }
    }
}
