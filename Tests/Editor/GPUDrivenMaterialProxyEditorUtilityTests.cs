using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Editor.GPUDriven;
using VividRP.Runtime.GPUDriven;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public class GPUDrivenMaterialProxyEditorUtilityTests
    {
        private const string TempFolder = "Assets/VividRP_Temp_GPUDrivenMaterialProxyEditorUtilityTests";

        [SetUp]
        public void SetUp()
        {
            DeleteTempFolder();
            EnsureTempFolderExists();
            VividMeshletRendererDatabase.instance.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            VividMeshletRendererDatabase.instance.Clear();
            DeleteTempFolder();
        }

        [Test]
        public void CreateOrBindMaterialProxies_CreatesAssetNextToPersistentMaterial_WhenMaterialAssetExists()
        {
            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null);

            string meshPath = TempFolder + "/PersistentMesh.asset";
            string materialPath = TempFolder + "/PersistentMaterial.mat";

            Mesh mesh = CreateSingleSubMeshMesh("PersistentMesh");
            AssetDatabase.CreateAsset(mesh, meshPath);
            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

            var persistentMaterial = new Material(shader);
            AssetDatabase.CreateAsset(persistentMaterial, materialPath);
            persistentMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            GameObject gameObject = CreateMeshletRendererObject("PersistentMaterialRenderer", mesh, persistentMaterial, out MeshletRenderer meshletRenderer);

            try
            {
                GPUDrivenMaterialProxyBindingResult result = GPUDrivenMaterialProxyEditorUtility.CreateOrBindMaterialProxies(meshletRenderer);

                Assert.That(result.Success, Is.True);
                Assert.That(meshletRenderer.GetMaterialProxy(0), Is.Not.Null);
                Assert.That(
                    AssetDatabase.GetAssetPath(meshletRenderer.GetMaterialProxy(0)),
                    Is.EqualTo($"{TempFolder}/PersistentMaterial_GPUDriven.asset")
                );
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void CreateOrBindMaterialProxies_CreatesAssetNextToPersistentMesh_WhenMaterialIsNonPersistent()
        {
            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null);

            string meshPath = TempFolder + "/FallbackMesh.asset";

            Mesh mesh = CreateSingleSubMeshMesh("FallbackMesh");
            AssetDatabase.CreateAsset(mesh, meshPath);
            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

            Material nonPersistentMaterial = new Material(shader);
            GameObject gameObject = CreateMeshletRendererObject("FallbackMaterialRenderer", mesh, nonPersistentMaterial, out MeshletRenderer meshletRenderer);

            try
            {
                GPUDrivenMaterialProxyBindingResult result = GPUDrivenMaterialProxyEditorUtility.CreateOrBindMaterialProxies(meshletRenderer);

                Assert.That(result.Success, Is.True);
                Assert.That(meshletRenderer.GetMaterialProxy(0), Is.Not.Null);
                Assert.That(
                    AssetDatabase.GetAssetPath(meshletRenderer.GetMaterialProxy(0)),
                    Is.EqualTo($"{TempFolder}/FallbackMesh_SubMesh0_GPUDriven.asset")
                );
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(nonPersistentMaterial);
            }
        }

        private static GameObject CreateMeshletRendererObject(
            string name,
            Mesh mesh,
            Material material,
            out MeshletRenderer meshletRenderer
        )
        {
            var gameObject = new GameObject(name);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshletRenderer = gameObject.AddComponent<MeshletRenderer>();
            return gameObject;
        }

        private static Mesh CreateSingleSubMeshMesh(string meshName)
        {
            var mesh = new Mesh
            {
                name = meshName,
                vertices = new[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f),
                    new Vector3(1.0f, 0.0f, 0.0f),
                    new Vector3(0.0f, 1.0f, 0.0f),
                },
                normals = new[]
                {
                    Vector3.forward,
                    Vector3.forward,
                    Vector3.forward,
                },
                uv = new[]
                {
                    new Vector2(0.0f, 0.0f),
                    new Vector2(1.0f, 0.0f),
                    new Vector2(0.0f, 1.0f),
                },
            };

            mesh.SetTriangles(new[] { 0, 2, 1 }, 0);
            mesh.RecalculateBounds();
            return mesh;
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
