using System.IO;
using System.Linq;
using System.Reflection;
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
        private const string GeneratedRoot = TempFolder + "/GPUDrivenGenerated";
        private const string MaterialProxyFolder = GeneratedRoot + "/MaterialProxy";
        private const string MeshletAssetFolder = GeneratedRoot + "/MeshletAsset";

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
        public void CaptureSourceFromRenderer_UsesMeshFilterMesh_WhenMeshRendererIsAttached()
        {
            var gameObject = new GameObject("MeshletRenderer_MeshFilter");
            Mesh mesh = CreateSingleSubMeshMesh("SourceMesh");
            var meshFilter = gameObject.AddComponent<MeshFilter>();
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            var meshletRenderer = gameObject.AddComponent<MeshletRenderer>();

            try
            {
                meshFilter.sharedMesh = mesh;

                Assert.That(meshletRenderer.CaptureSourceFromRenderer(meshRenderer), Is.True);
                Assert.That(meshletRenderer.sourceMesh, Is.SameAs(mesh));
                Assert.That(meshletRenderer.subMeshCount, Is.EqualTo(1));
                Assert.That(meshletRenderer.sourceMaterials.Count, Is.EqualTo(1));
                Assert.That(meshletRenderer.meshletCollections.Count, Is.EqualTo(1));
                Assert.That(meshletRenderer.materialProxies.Count, Is.EqualTo(1));
                Assert.That(meshletRenderer.GetSourceMaterial(0), Is.Null);
                Assert.That(meshletRenderer.GetMeshletCollection(0), Is.Null);
                Assert.That(meshletRenderer.GetMaterialProxy(0), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void CaptureSourceFromRenderer_UsesSkinnedMesh_WhenSkinnedMeshRendererIsAttached()
        {
            var gameObject = new GameObject("MeshletRenderer_Skinned");
            Mesh mesh = CreateSingleSubMeshMesh("SkinnedSource");
            var skinnedMeshRenderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            var meshletRenderer = gameObject.AddComponent<MeshletRenderer>();

            try
            {
                skinnedMeshRenderer.sharedMesh = mesh;

                Assert.That(meshletRenderer.CaptureSourceFromRenderer(skinnedMeshRenderer), Is.True);
                Assert.That(meshletRenderer.sourceMesh, Is.SameAs(mesh));
                Assert.That(meshletRenderer.subMeshCount, Is.EqualTo(1));
                Assert.That(meshletRenderer.materialProxies.Count, Is.EqualTo(1));
                Assert.That(meshletRenderer.sourceWasSkinned, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void RefreshSource_DoesNotAutoResolveGameObjectRenderer_WhenSourceIsNotCaptured()
        {
            var gameObject = new GameObject("MeshletRenderer_NoAutoResolve");
            Mesh mesh = CreateSingleSubMeshMesh("NoAutoResolveMesh");
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            gameObject.AddComponent<MeshRenderer>();
            var meshletRenderer = gameObject.AddComponent<MeshletRenderer>();

            try
            {
                meshletRenderer.RefreshSource();

                Assert.That(meshletRenderer.sourceMesh, Is.Null);
                Assert.That(meshletRenderer.subMeshCount, Is.Zero);
                Assert.That(meshletRenderer.sourceMaterials.Count, Is.Zero);
                Assert.That(meshletRenderer.meshletCollections.Count, Is.Zero);
                Assert.That(meshletRenderer.materialProxies.Count, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void GetMaterialSlotCount_ReturnsCapturedSubMeshCount_WhenSourceMeshExists()
        {
            var gameObject = new GameObject("MeshletRenderer_MaterialSlotCount");
            Mesh mesh = CreateTwoSubMeshMesh("MaterialSlotCountMesh");
            var meshFilter = gameObject.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
            var meshletRenderer = gameObject.AddComponent<MeshletRenderer>();

            try
            {
                meshFilter.sharedMesh = mesh;
                meshletRenderer.CaptureSourceFromRenderer(meshRenderer);

                Assert.That(MeshletRendererEditorUtility.GetMaterialSlotCount(meshletRenderer), Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void GetMaterialSlotCount_UsesStoredArrays_WhenSourceMeshIsMissing()
        {
            var gameObject = new GameObject("MeshletRenderer_LegacyMaterialSlots");
            var meshletRenderer = gameObject.AddComponent<MeshletRenderer>();
            var sourceMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
            var materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();

            try
            {
                typeof(MeshletRenderer)
                    .GetField("m_SourceMaterials", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(meshletRenderer, new Material[] { null, sourceMaterial });
                typeof(MeshletRenderer)
                    .GetField("m_MaterialProxies", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(meshletRenderer, new[] { materialProxy });

                Assert.That(MeshletRendererEditorUtility.GetMaterialSlotCount(meshletRenderer), Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(materialProxy);
                Object.DestroyImmediate(sourceMaterial);
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void TryValidate_ReturnsTrue_WhenEverySubmeshAssetIsAssignedAndTakeOverIsDisabled()
        {
            var gameObject = new GameObject("MeshletRenderer_Validation");
            Mesh mesh = CreateTwoSubMeshMesh("MultiSubMesh");
            var meshFilter = gameObject.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
            var meshletRenderer = gameObject.AddComponent<MeshletRenderer>();
            var subMesh0 = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();
            var subMesh1 = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();

            try
            {
                meshFilter.sharedMesh = mesh;
                meshletRenderer.CaptureSourceFromRenderer(meshRenderer);
                meshletRenderer.SetTakeOverSourceRenderer(false);

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
        public void TryValidate_RequiresMaterialProxies_WhenTakeOverIsEnabled()
        {
            var gameObject = new GameObject("MeshletRenderer_ProxyValidation");
            Mesh mesh = CreateSingleSubMeshMesh("ProxyValidationMesh");
            var meshFilter = gameObject.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
            var meshletRenderer = gameObject.AddComponent<MeshletRenderer>();
            var meshletCollection = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();
            var materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();

            try
            {
                meshFilter.sharedMesh = mesh;
                meshletRenderer.CaptureSourceFromRenderer(meshRenderer);
                meshletCollection.SourceSubmeshIndex = 0;
                meshletRenderer.SetMeshletCollections(new[] { meshletCollection });

                Assert.That(meshletRenderer.TryValidate(out string missingProxyMessage), Is.False);
                Assert.That(missingProxyMessage, Does.Contain("GPUDriven material proxy"));

                meshletRenderer.SetMaterialProxies(new[] { materialProxy });
                Assert.That(meshletRenderer.TryValidate(out string validationMessage), Is.True);
                Assert.That(validationMessage, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(materialProxy);
                Object.DestroyImmediate(meshletCollection);
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void CaptureSourceFromRenderer_ResizesMaterialProxyArray_WhenSubMeshCountChanges()
        {
            var gameObject = new GameObject("MeshletRenderer_ProxyResize");
            Mesh firstMesh = CreateSingleSubMeshMesh("ProxyResize_First");
            Mesh secondMesh = CreateTwoSubMeshMesh("ProxyResize_Second");
            var meshFilter = gameObject.AddComponent<MeshFilter>();
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            var meshletRenderer = gameObject.AddComponent<MeshletRenderer>();
            var materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();

            try
            {
                meshFilter.sharedMesh = firstMesh;
                meshletRenderer.CaptureSourceFromRenderer(meshRenderer);
                meshletRenderer.SetMaterialProxies(new[] { materialProxy });

                meshFilter.sharedMesh = secondMesh;
                meshletRenderer.CaptureSourceFromRenderer(meshRenderer);

                Assert.That(meshletRenderer.materialProxies.Count, Is.EqualTo(2));
                Assert.That(meshletRenderer.GetMaterialProxy(0), Is.SameAs(materialProxy));
                Assert.That(meshletRenderer.GetMaterialProxy(1), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(materialProxy);
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(firstMesh);
                Object.DestroyImmediate(secondMesh);
            }
        }

        [Test]
        public void RefreshSource_PreservesCapturedMeshAndMaterials_WhenMeshRendererWasRemoved()
        {
            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null);

            var gameObject = new GameObject("MeshletRenderer_RendererlessRefresh");
            Mesh mesh = CreateSingleSubMeshMesh("RendererlessRefreshMesh");
            var meshFilter = gameObject.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = new Material(shader);
            var meshletRenderer = gameObject.AddComponent<MeshletRenderer>();

            try
            {
                meshFilter.sharedMesh = mesh;
                meshletRenderer.CaptureSourceFromRenderer(meshRenderer);

                Assert.That(meshletRenderer.GetSourceMaterial(0), Is.SameAs(meshRenderer.sharedMaterial));

                Object.DestroyImmediate(meshRenderer);
                meshletRenderer.RefreshSource();

                Assert.That(meshletRenderer.sourceMesh, Is.SameAs(mesh));
                Assert.That(meshletRenderer.GetSourceMaterial(0), Is.Not.Null);
            }
            finally
            {
                if (meshletRenderer.GetSourceMaterial(0) != null)
                {
                    Object.DestroyImmediate(meshletRenderer.GetSourceMaterial(0));
                }

                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void RepairTakeOverBindings_CreatesMissingAssetsAndMaterialProxies_WhenSourceMeshIsPersistent()
        {
            EnsureSupportedPlatform();

            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null);

            string meshPath = TempFolder + "/RepairMesh.asset";
            Mesh mesh = CreateSingleSubMeshMesh("RepairMesh");
            AssetDatabase.CreateAsset(mesh, meshPath);
            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

            AssetDatabase.CreateFolder(TempFolder, "Materials");
            string materialPath = TempFolder + "/Materials/RepairMaterial.mat";
            Material material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
            material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            Assert.That(
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    material,
                    out string materialGuid,
                    out long materialLocalFileId),
                Is.True);
            string materialIdentifier = materialLocalFileId != 0L
                ? $"{materialGuid}_{unchecked((ulong)materialLocalFileId):X16}"
                : materialGuid;
            GameObject gameObject = null;

            try
            {
                gameObject = new GameObject("MeshletRenderer_RepairTakeOver");
                gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = material;
                MeshletRenderer meshletRenderer = gameObject.AddComponent<MeshletRenderer>();
                meshletRenderer.CaptureSourceFromRenderer(meshRenderer);

                Assert.That(meshletRenderer.TryValidate(out _), Is.False);

                MeshletRendererTakeOverRepairResult result =
                    MeshletRendererEditorUtility.RepairTakeOverBindings(meshletRenderer);

                Assert.That(result.Success, Is.True, result.ErrorMessage);
                Assert.That(result.CreatedMeshletAssetPaths, Has.Length.EqualTo(1));
                Assert.That(result.CreatedMaterialProxyAssetPaths, Has.Length.EqualTo(1));
                Assert.That(meshletRenderer.GetMeshletCollection(0), Is.Not.Null);
                Assert.That(meshletRenderer.GetMaterialProxy(0), Is.Not.Null);
                Assert.That(meshletRenderer.TryValidate(out string validationMessage), Is.True, validationMessage);
                Assert.That(
                    AssetDatabase.GetAssetPath(meshletRenderer.GetMeshletCollection(0)),
                    Does.StartWith(MeshletAssetFolder + "/RepairMesh_Meshlets"));
                Assert.That(
                    AssetDatabase.GetAssetPath(meshletRenderer.GetMaterialProxy(0)),
                    Is.EqualTo(
                        $"{MaterialProxyFolder}/RepairMaterial_{materialIdentifier}_GPUDriven.asset")
                );
                Assert.That(
                    AssetDatabase.IsValidFolder(TempFolder + "/Materials/GPUDrivenGenerated"),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void TakeOverAndRemoveSourceMeshRenderer_RemovesRendererButKeepsValidBindings()
        {
            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null);

            GameObject gameObject = null;
            Mesh mesh = null;
            Material material = null;
            VividMeshletCollectionAsset meshletCollection = null;
            GPUDrivenMaterialProxy materialProxy = null;

            try
            {
                gameObject = new GameObject("MeshletRenderer_RemoveMeshRenderer");
                mesh = CreateSingleSubMeshMesh("RemoveMeshRendererMesh");
                material = new Material(shader);
                meshletCollection = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();
                materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();

                gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = material;

                MeshletRenderer meshletRenderer = gameObject.AddComponent<MeshletRenderer>();
                meshletRenderer.CaptureSourceFromRenderer(meshRenderer);
                meshletCollection.SourceSubmeshIndex = 0;

                meshletRenderer.SetMeshletCollections(new[] { meshletCollection });
                meshletRenderer.SetMaterialProxies(new[] { materialProxy });

                MeshletRendererSourceRendererDetachResult result =
                    MeshletRendererEditorUtility.TakeOverAndRemoveSourceMeshRenderer(meshletRenderer);

                Assert.That(result.Success, Is.True, result.ErrorMessage);
                Assert.That(gameObject.GetComponent<MeshRenderer>(), Is.Null);
                Assert.That(meshletRenderer.sourceMesh, Is.SameAs(mesh));
                Assert.That(meshletRenderer.GetSourceMaterial(0), Is.SameAs(material));
                Assert.That(meshletRenderer.TryValidate(out string validationMessage), Is.True, validationMessage);
            }
            finally
            {
                Object.DestroyImmediate(materialProxy);
                Object.DestroyImmediate(meshletCollection);
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void TakeOverAndRemoveSourceMeshRenderersRecursively_ConvertsRootAndChildren()
        {
            EnsureSupportedPlatform();

            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null);

            Mesh mesh = CreateSingleSubMeshMesh("RecursiveMesh");
            AssetDatabase.CreateAsset(mesh, TempFolder + "/RecursiveMesh.asset");
            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(TempFolder + "/RecursiveMesh.asset");

            var material = new Material(shader);
            AssetDatabase.CreateAsset(material, TempFolder + "/RecursiveMaterial.mat");
            material = AssetDatabase.LoadAssetAtPath<Material>(TempFolder + "/RecursiveMaterial.mat");

            GameObject root = null;

            try
            {
                root = new GameObject("RecursiveRoot");
                GameObject child = new GameObject("Child");
                child.transform.SetParent(root.transform);

                GameObject inactiveChild = new GameObject("InactiveChild");
                inactiveChild.transform.SetParent(root.transform);
                inactiveChild.SetActive(false);

                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                root.AddComponent<MeshRenderer>().sharedMaterial = material;

                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                child.AddComponent<MeshRenderer>().sharedMaterial = material;

                inactiveChild.AddComponent<MeshFilter>().sharedMesh = mesh;
                inactiveChild.AddComponent<MeshRenderer>().sharedMaterial = material;

                MeshletRendererRecursiveConversionResult result =
                    MeshletRendererEditorUtility.TakeOverAndRemoveSourceMeshRenderersRecursively(root);

                Assert.That(result.Success, Is.True, result.ErrorMessage);
                Assert.That(result.ConvertedRendererCount, Is.EqualTo(3));
                Assert.That(result.AddedMeshletRendererCount, Is.EqualTo(3));
                Assert.That(result.FailedRendererCount, Is.Zero);
                Assert.That(result.SkippedRendererCount, Is.Zero);
                Assert.That(result.CreatedMeshletAssetPaths, Has.Length.EqualTo(1));
                Assert.That(result.CreatedMaterialProxyAssetPaths, Has.Length.EqualTo(1));

                AssertMeshletRendererConverted(root, mesh, material);
                AssertMeshletRendererConverted(child, mesh, material);
                AssertMeshletRendererConverted(inactiveChild, mesh, material);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TakeOverAndRemoveSourceMeshRenderersRecursively_SkipsInvalidMeshRenderersAndContinues()
        {
            EnsureSupportedPlatform();

            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null);

            Mesh mesh = CreateSingleSubMeshMesh("RecursiveValidMesh");
            AssetDatabase.CreateAsset(mesh, TempFolder + "/RecursiveValidMesh.asset");
            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(TempFolder + "/RecursiveValidMesh.asset");

            var material = new Material(shader);
            AssetDatabase.CreateAsset(material, TempFolder + "/RecursiveValidMaterial.mat");
            material = AssetDatabase.LoadAssetAtPath<Material>(TempFolder + "/RecursiveValidMaterial.mat");

            GameObject root = null;

            try
            {
                root = new GameObject("RecursiveMixedRoot");

                GameObject validChild = new GameObject("ValidChild");
                validChild.transform.SetParent(root.transform);
                validChild.AddComponent<MeshFilter>().sharedMesh = mesh;
                validChild.AddComponent<MeshRenderer>().sharedMaterial = material;

                GameObject invalidChild = new GameObject("InvalidChild");
                invalidChild.transform.SetParent(root.transform);
                invalidChild.AddComponent<MeshRenderer>();

                MeshletRendererRecursiveConversionResult result =
                    MeshletRendererEditorUtility.TakeOverAndRemoveSourceMeshRenderersRecursively(root);

                Assert.That(result.Success, Is.True, result.ErrorMessage);
                Assert.That(result.ConvertedRendererCount, Is.EqualTo(1));
                Assert.That(result.AddedMeshletRendererCount, Is.EqualTo(1));
                Assert.That(result.FailedRendererCount, Is.Zero);
                Assert.That(result.SkippedRendererCount, Is.EqualTo(1));

                AssertMeshletRendererConverted(validChild, mesh, material);
                Assert.That(invalidChild.GetComponent<MeshRenderer>(), Is.Not.Null);
                Assert.That(invalidChild.GetComponent<MeshletRenderer>(), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(root);
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
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            Assert.That(AssetDatabase.Contains(meshA), Is.True);
            Assert.That(AssetDatabase.Contains(meshB), Is.True);
            Assert.That(AssetDatabase.GetAssetPath(meshA), Is.EqualTo(assetPath));
            Assert.That(AssetDatabase.GetAssetPath(meshB), Is.EqualTo(assetPath));

            VividMeshletCollectionAssetImporter.CreateAssetsForSelection(new Object[] { meshA });
            VividMeshletCollectionAssetImporter.CreateAssetsForSelection(new Object[] { meshB });

            VividMeshletCollectionAsset[] collectionsA = MeshletRendererEditorUtility.CollectMeshletCollections(meshA);
            VividMeshletCollectionAsset[] collectionsB = MeshletRendererEditorUtility.CollectMeshletCollections(meshB);

            Assert.That(collectionsA, Has.Length.EqualTo(1));
            Assert.That(collectionsB, Has.Length.EqualTo(1));
            Assert.That(collectionsA[0], Is.Not.Null);
            Assert.That(collectionsB[0], Is.Not.Null);
            Assert.That(collectionsA[0], Is.Not.SameAs(collectionsB[0]));
            Assert.That(collectionsA[0].SourceMeshName, Is.Not.Empty);
            Assert.That(collectionsB[0].SourceMeshName, Is.Not.Empty);
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
            Assert.That(firstGeneration.All(path => path.StartsWith(MeshletAssetFolder + "/")), Is.True);
            Assert.That(meshletCollections, Has.Length.EqualTo(2));
            Assert.That(meshletCollections.All(collection => collection != null), Is.True);
        }

        [Test]
        public void GenerateMissingMeshletCollections_ReusesLegacyAdjacentAssets()
        {
            EnsureSupportedPlatform();

            Mesh mesh = CreateSingleSubMeshMesh("LegacyMeshletAsset");
            string meshPath = TempFolder + "/LegacyMeshletAsset.asset";
            AssetDatabase.CreateAsset(mesh, meshPath);
            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

            string[] legacyAssets = VividMeshletCollectionAssetImporter.CreateAssetsForSelection(
                new Object[] { mesh });
            string[] generatedAssets = MeshletRendererEditorUtility.GenerateMissingMeshletCollections(mesh);

            Assert.That(legacyAssets, Has.Length.EqualTo(1));
            Assert.That(legacyAssets[0], Does.StartWith(TempFolder + "/LegacyMeshletAsset_Meshlets"));
            Assert.That(generatedAssets, Is.Empty);
            Assert.That(AssetDatabase.IsValidFolder(GeneratedRoot), Is.False);
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

        private static void AssertMeshletRendererConverted(GameObject gameObject, Mesh mesh, Material material)
        {
            Assert.That(gameObject, Is.Not.Null);
            Assert.That(gameObject.GetComponent<MeshRenderer>(), Is.Null);

            MeshletRenderer meshletRenderer = gameObject.GetComponent<MeshletRenderer>();
            Assert.That(meshletRenderer, Is.Not.Null);
            Assert.That(meshletRenderer.sourceMesh, Is.SameAs(mesh));
            Assert.That(meshletRenderer.GetSourceMaterial(0), Is.SameAs(material));
            Assert.That(meshletRenderer.GetMeshletCollection(0), Is.Not.Null);
            Assert.That(meshletRenderer.GetMaterialProxy(0), Is.Not.Null);
            Assert.That(meshletRenderer.TryValidate(out string validationMessage), Is.True, validationMessage);
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
