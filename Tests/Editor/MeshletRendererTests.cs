using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using VividRP.Editor.GPUDriven;
using VividRP.Editor.GPUDriven.Meshlets;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Editor.Tests
{
    public class MeshletRendererTests
    {
        private const string TempFolder = "Assets/VividRP_Temp_MeshletRendererTests";
        private static readonly MethodInfo s_LateUpdateMethod =
            typeof(MeshletRenderer).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);

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
                Assert.That(meshletRenderer.materialProxies.Count, Is.EqualTo(1));
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
                Assert.That(meshletRenderer.materialProxies.Count, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void TryValidate_ReturnsTrue_WhenEverySubmeshAssetIsAssignedAndTakeOverIsDisabled()
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
            gameObject.AddComponent<MeshRenderer>();
            var meshletRenderer = gameObject.AddComponent<MeshletRenderer>();
            var meshletCollection = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();
            var materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();

            try
            {
                meshFilter.sharedMesh = mesh;
                meshletRenderer.RefreshSource();
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
        public void RefreshSource_ResizesMaterialProxyArray_WhenSubMeshCountChanges()
        {
            var gameObject = new GameObject("MeshletRenderer_ProxyResize");
            Mesh firstMesh = CreateSingleSubMeshMesh("ProxyResize_First");
            Mesh secondMesh = CreateTwoSubMeshMesh("ProxyResize_Second");
            var meshFilter = gameObject.AddComponent<MeshFilter>();
            gameObject.AddComponent<MeshRenderer>();
            var meshletRenderer = gameObject.AddComponent<MeshletRenderer>();
            var materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();

            try
            {
                meshFilter.sharedMesh = firstMesh;
                meshletRenderer.RefreshSource();
                meshletRenderer.SetMaterialProxies(new[] { materialProxy });

                meshFilter.sharedMesh = secondMesh;
                meshletRenderer.RefreshSource();

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
        public void LateUpdate_TogglesForceRenderingOff_WhenTakeOverConditionsChange()
        {
            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null);

            RenderPipelineAsset previousGraphicsPipeline = GraphicsSettings.defaultRenderPipeline;
            RenderPipelineAsset previousQualityPipeline = QualitySettings.renderPipeline;
            VividRenderPipelineAsset pipelineAsset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();
            pipelineAsset.EnableGPUDriven = true;

            var gameObject = new GameObject("MeshletRenderer_TakeOver");
            Mesh mesh = CreateSingleSubMeshMesh("TakeOverMesh");
            var meshFilter = gameObject.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = new Material(shader);
            var meshletRenderer = gameObject.AddComponent<MeshletRenderer>();
            var meshletCollection = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();
            var materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();

            try
            {
                GraphicsSettings.defaultRenderPipeline = pipelineAsset;
                QualitySettings.renderPipeline = pipelineAsset;

                meshFilter.sharedMesh = mesh;
                meshletRenderer.RefreshSource();
                meshletCollection.SourceSubmeshIndex = 0;
                materialProxy.SourceMaterial = meshRenderer.sharedMaterial;
                meshletRenderer.SetMeshletCollections(new[] { meshletCollection });
                meshletRenderer.SetMaterialProxies(new[] { materialProxy });

                Assert.That(meshRenderer.forceRenderingOff, Is.False);

                InvokeLateUpdate(meshletRenderer);
                Assert.That(meshRenderer.forceRenderingOff, Is.True);

                meshletRenderer.SetTakeOverSourceRenderer(false);
                InvokeLateUpdate(meshletRenderer);
                Assert.That(meshRenderer.forceRenderingOff, Is.False);
            }
            finally
            {
                GraphicsSettings.defaultRenderPipeline = previousGraphicsPipeline;
                QualitySettings.renderPipeline = previousQualityPipeline;

                Object.DestroyImmediate(materialProxy);
                Object.DestroyImmediate(meshletCollection);
                Object.DestroyImmediate(meshRenderer.sharedMaterial);
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(pipelineAsset);
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

        private static void InvokeLateUpdate(MeshletRenderer meshletRenderer)
        {
            Assert.That(s_LateUpdateMethod, Is.Not.Null);
            s_LateUpdateMethod.Invoke(meshletRenderer, null);
        }
    }
}
