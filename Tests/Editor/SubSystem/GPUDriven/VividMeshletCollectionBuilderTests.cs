using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using VividRP.Editor.GPUDriven.Meshlets;
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
                Assert.That(createdAssets[0], Does.StartWith(TempFolder + "/TestMesh_Meshlets"));

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
