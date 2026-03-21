using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using VividRP.Editor.GPUDriven;
using VividRP.Editor.GPUDriven.Meshlets;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Editor.Tests
{
    public class MeshletRendererTests
    {
        private const string TempFolder = "Assets/VividRP_Temp_MeshletRendererTests";

        [SetUp]
        public void SetUp()
        {
            DeleteTempFolder();
            EnsureTempFolderExists();
        }

        [TearDown]
        public void TearDown()
        {
            DeleteTempFolder();
        }

        [Test]
        public void RefreshSource_UsesMeshFilterMesh_WhenMeshRendererIsAttached()
        {
            var gameObject = new GameObject("MeshletRenderer_MeshFilter");
            Mesh mesh = CreateSingleSubMeshMesh("SourceMesh");
            var meshFilter = gameObject.AddComponent<MeshFilter>();
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            var meshletRenderer = gameObject.AddComponent<MeshletRenderer>();

            try
            {
                meshFilter.sharedMesh = mesh;
                meshletRenderer.RefreshSource();

                Assert.That(meshletRenderer.sourceRenderer, Is.SameAs(meshRenderer));
                Assert.That(meshletRenderer.sourceMesh, Is.SameAs(mesh));
                Assert.That(meshletRenderer.subMeshCount, Is.EqualTo(1));
                Assert.That(meshletRenderer.meshletCollections.Count, Is.EqualTo(1));
                Assert.That(meshletRenderer.GetMeshletCollection(0), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void RefreshSource_UsesSkinnedMesh_WhenSkinnedMeshRendererIsAttached()
        {
            var gameObject = new GameObject("MeshletRenderer_Skinned");
            Mesh mesh = CreateSingleSubMeshMesh("SkinnedSource");
            var skinnedMeshRenderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            var meshletRenderer = gameObject.AddComponent<MeshletRenderer>();

            try
            {
                skinnedMeshRenderer.sharedMesh = mesh;
                meshletRenderer.RefreshSource();

                Assert.That(meshletRenderer.sourceRenderer, Is.SameAs(skinnedMeshRenderer));
                Assert.That(meshletRenderer.sourceMesh, Is.SameAs(mesh));
                Assert.That(meshletRenderer.subMeshCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void TryValidate_ReturnsTrue_WhenEverySubmeshAssetIsAssigned()
        {
            var gameObject = new GameObject("MeshletRenderer_Validation");
            Mesh mesh = CreateTwoSubMeshMesh("MultiSubMesh");
            var meshFilter = gameObject.AddComponent<MeshFilter>();
            gameObject.AddComponent<MeshRenderer>();
            var meshletRenderer = gameObject.AddComponent<MeshletRenderer>();
            var subMesh0 = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();
            var subMesh1 = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();

            try
            {
                meshFilter.sharedMesh = mesh;
                meshletRenderer.RefreshSource();

                subMesh0.SourceSubmeshIndex = 0;
                subMesh1.SourceSubmeshIndex = 1;

                meshletRenderer.SetMeshletCollections(new[] { subMesh0 });
                Assert.That(meshletRenderer.TryValidate(out string missingBindingMessage), Is.False);
                Assert.That(missingBindingMessage, Does.Contain("submesh 1"));

                meshletRenderer.SetMeshletCollections(new[] { subMesh0, subMesh1 });
                Assert.That(meshletRenderer.TryValidate(out string validationMessage), Is.True);
                Assert.That(validationMessage, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(subMesh0);
                Object.DestroyImmediate(subMesh1);
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void CollectMeshletCollections_DistinguishesMeshesThatShareAssetPath()
        {
            EnsureSupportedPlatform();

            Mesh meshA = CreateSingleSubMeshMesh("SharedMeshA");
            Mesh meshB = CreateSingleSubMeshMesh("SharedMeshB");
            string assetPath = TempFolder + "/SharedMeshes.asset";

            AssetDatabase.CreateAsset(meshA, assetPath);
            AssetDatabase.AddObjectToAsset(meshB, assetPath);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            Mesh[] meshes = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Mesh>().ToArray();
            meshA = meshes.Single(mesh => mesh.name == "SharedMeshA");
            meshB = meshes.Single(mesh => mesh.name == "SharedMeshB");

            VividMeshletCollectionAssetImporter.CreateAssetsForSelection(new Object[] { meshA });
            VividMeshletCollectionAssetImporter.CreateAssetsForSelection(new Object[] { meshB });

            VividMeshletCollectionAsset[] collectionsA = MeshletRendererEditorUtility.CollectMeshletCollections(meshA);
            VividMeshletCollectionAsset[] collectionsB = MeshletRendererEditorUtility.CollectMeshletCollections(meshB);

            Assert.That(collectionsA, Has.Length.EqualTo(1));
            Assert.That(collectionsB, Has.Length.EqualTo(1));
            Assert.That(collectionsA[0], Is.Not.Null);
            Assert.That(collectionsB[0], Is.Not.Null);
            Assert.That(collectionsA[0], Is.Not.SameAs(collectionsB[0]));
            Assert.That(collectionsA[0].SourceMeshName, Is.EqualTo("SharedMeshA"));
            Assert.That(collectionsB[0].SourceMeshName, Is.EqualTo("SharedMeshB"));
            Assert.That(collectionsA[0].SourceMeshLocalFileID, Is.Not.EqualTo(0L));
            Assert.That(collectionsB[0].SourceMeshLocalFileID, Is.Not.EqualTo(0L));
            Assert.That(collectionsA[0].SourceMeshLocalFileID, Is.Not.EqualTo(collectionsB[0].SourceMeshLocalFileID));
        }

        [Test]
        public void GenerateMissingMeshletCollections_SkipsAssetsThatAlreadyExist()
        {
            EnsureSupportedPlatform();

            Mesh mesh = CreateTwoSubMeshMesh("GeneratedMesh");
            string meshPath = TempFolder + "/GeneratedMesh.asset";

            AssetDatabase.CreateAsset(mesh, meshPath);
            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

            string[] firstGeneration = MeshletRendererEditorUtility.GenerateMissingMeshletCollections(mesh);
            string[] secondGeneration = MeshletRendererEditorUtility.GenerateMissingMeshletCollections(mesh);
            VividMeshletCollectionAsset[] meshletCollections = MeshletRendererEditorUtility.CollectMeshletCollections(mesh);

            Assert.That(firstGeneration, Has.Length.EqualTo(2));
            Assert.That(secondGeneration, Is.Empty);
            Assert.That(meshletCollections, Has.Length.EqualTo(2));
            Assert.That(meshletCollections.All(collection => collection != null), Is.True);
        }

        private static Mesh CreateSingleSubMeshMesh(string meshName)
        {
            var mesh = new Mesh
            {
                name = meshName,
            };

            mesh.vertices = new[]
            {
                new Vector3(0.0f, 0.0f, 0.0f),
                new Vector3(1.0f, 0.0f, 0.0f),
                new Vector3(0.0f, 1.0f, 0.0f),
                new Vector3(1.0f, 1.0f, 0.0f),
            };
            mesh.normals = Enumerable.Repeat(Vector3.forward, 4).ToArray();
            mesh.uv = new[]
            {
                new Vector2(0.0f, 0.0f),
                new Vector2(1.0f, 0.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(1.0f, 1.0f),
            };
            mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0, true);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        private static Mesh CreateTwoSubMeshMesh(string meshName)
        {
            var mesh = new Mesh
            {
                name = meshName,
                subMeshCount = 2,
            };

            mesh.vertices = new[]
            {
                new Vector3(0.0f, 0.0f, 0.0f),
                new Vector3(1.0f, 0.0f, 0.0f),
                new Vector3(0.0f, 1.0f, 0.0f),
                new Vector3(1.0f, 1.0f, 0.0f),
                new Vector3(2.0f, 0.0f, 0.0f),
                new Vector3(2.0f, 1.0f, 0.0f),
            };
            mesh.normals = Enumerable.Repeat(Vector3.forward, 6).ToArray();
            mesh.uv = new[]
            {
                new Vector2(0.0f, 0.0f),
                new Vector2(1.0f, 0.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(1.0f, 1.0f),
                new Vector2(2.0f, 0.0f),
                new Vector2(2.0f, 1.0f),
            };
            mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0, true);
            mesh.SetTriangles(new[] { 1, 3, 4, 4, 3, 5 }, 1, true);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
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
    }
}
