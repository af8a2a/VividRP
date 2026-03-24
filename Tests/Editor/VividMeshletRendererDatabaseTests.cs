using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VividRP.Editor.GPUDriven;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Meshlets;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public class VividMeshletRendererDatabaseTests
    {
        private static readonly MethodInfo s_LateUpdateMethod =
            typeof(MeshletRenderer).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);

        [SetUp]
        public void SetUp()
        {
            VividMeshletRendererDatabase.instance.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            VividMeshletRendererDatabase.instance.Clear();
        }

        [Test]
        public void OnEnable_RegistersRendererData_WithoutCapturedSource()
        {
            Material material = null;
            Mesh mesh = null;
            GameObject gameObject = null;

            try
            {
                gameObject = CreateMeshRendererObject("MeshletRenderer_Database", out mesh, out material);
                var meshletRenderer = gameObject.AddComponent<MeshletRenderer>();

                Assert.That(VividMeshletRendererDatabase.instance.rendererCount, Is.EqualTo(1));
                Assert.That(VividMeshletRendererDatabase.instance.TryGetRendererData(meshletRenderer, out var trackedData), Is.True);
                Assert.That(trackedData.meshletRendererEntityId, Is.EqualTo(meshletRenderer.GetEntityId()));
                Assert.That(trackedData.sourceRendererEntityId, Is.EqualTo(EntityId.None));
                Assert.That(trackedData.sourceMeshEntityId, Is.EqualTo(EntityId.None));
                Assert.That(trackedData.subMeshCount, Is.Zero);
                Assert.That(trackedData.materialCount, Is.Zero);
                Assert.That((trackedData.flags & VividMeshletRendererFlags.Enabled) != 0, Is.True);
                Assert.That((trackedData.flags & VividMeshletRendererFlags.Valid) != 0, Is.False);
            }
            finally
            {
                DestroyTestObjects(gameObject, material, mesh);
            }
        }

        [Test]
        public void UpdateRendererData_CapturesResources_WhenSourceWasExplicitlyCaptured()
        {
            Material material = null;
            Mesh mesh = null;
            GameObject gameObject = null;
            VividMeshletCollectionAsset meshletCollection = null;
            GPUDrivenMaterialProxy materialProxy = null;

            try
            {
                gameObject = CreateMeshRendererObject("MeshletRenderer_ValidBinding", out mesh, out material);
                var meshletRenderer = gameObject.AddComponent<MeshletRenderer>();
                meshletRenderer.CaptureSourceFromRenderer(gameObject.GetComponent<MeshRenderer>());

                meshletCollection = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();
                materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
                meshletCollection.SourceSubmeshIndex = 0;

                meshletRenderer.SetMeshletCollections(new[] { meshletCollection });
                meshletRenderer.SetMaterialProxies(new[] { materialProxy });
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);

                Assert.That(VividMeshletRendererDatabase.instance.TryGetRendererData(meshletRenderer, out var trackedData), Is.True);
                Assert.That(trackedData.sourceRendererEntityId, Is.EqualTo(EntityId.None));
                Assert.That(trackedData.sourceMeshEntityId, Is.EqualTo(mesh.GetEntityId()));
                Assert.That((trackedData.flags & VividMeshletRendererFlags.Valid) != 0, Is.True);
                Assert.That(VividMeshletRendererDatabase.instance.TryGetRendererResources(meshletRenderer, out var trackedResources), Is.True);
                Assert.That(trackedResources.MeshletRenderer, Is.SameAs(meshletRenderer));
                Assert.That(trackedResources.SourceRenderer, Is.Null);
                Assert.That(trackedResources.SourceMesh, Is.SameAs(mesh));
                Assert.That(trackedResources.SharedMaterials, Has.Length.EqualTo(1));
                Assert.That(trackedResources.SharedMaterials[0], Is.SameAs(material));
                Assert.That(trackedResources.MeshletCollections, Has.Length.EqualTo(1));
                Assert.That(trackedResources.MeshletCollections[0], Is.SameAs(meshletCollection));
                Assert.That(trackedResources.MaterialProxies, Has.Length.EqualTo(1));
                Assert.That(trackedResources.MaterialProxies[0], Is.SameAs(materialProxy));
            }
            finally
            {
                if (materialProxy != null)
                {
                    Object.DestroyImmediate(materialProxy);
                }

                if (meshletCollection != null)
                {
                    Object.DestroyImmediate(meshletCollection);
                }

                DestroyTestObjects(gameObject, material, mesh);
            }
        }

        [Test]
        public void UpdateRendererData_PreservesSourceMaterials_WhenMeshRendererWasRemoved()
        {
            Material material = null;
            Mesh mesh = null;
            GameObject gameObject = null;
            VividMeshletCollectionAsset meshletCollection = null;
            GPUDrivenMaterialProxy materialProxy = null;

            try
            {
                gameObject = CreateMeshRendererObject("MeshletRenderer_RendererlessResources", out mesh, out material);
                var meshletRenderer = gameObject.AddComponent<MeshletRenderer>();
                meshletRenderer.CaptureSourceFromRenderer(gameObject.GetComponent<MeshRenderer>());

                meshletCollection = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();
                materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
                meshletCollection.SourceSubmeshIndex = 0;

                meshletRenderer.SetMeshletCollections(new[] { meshletCollection });
                meshletRenderer.SetMaterialProxies(new[] { materialProxy });
                MeshletRendererEditorUtility.TakeOverAndRemoveSourceMeshRenderer(meshletRenderer);
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);

                Assert.That(VividMeshletRendererDatabase.instance.TryGetRendererData(meshletRenderer, out var trackedData), Is.True);
                Assert.That(trackedData.sourceRendererEntityId, Is.EqualTo(EntityId.None));
                Assert.That((trackedData.flags & VividMeshletRendererFlags.SourceRendererEnabled) != 0, Is.True);
                Assert.That(VividMeshletRendererDatabase.instance.TryGetRendererResources(meshletRenderer, out var trackedResources), Is.True);
                Assert.That(trackedResources.SourceRenderer, Is.Null);
                Assert.That(trackedResources.SharedMaterials, Has.Length.EqualTo(1));
                Assert.That(trackedResources.SharedMaterials[0], Is.SameAs(material));
            }
            finally
            {
                if (materialProxy != null)
                {
                    Object.DestroyImmediate(materialProxy);
                }

                if (meshletCollection != null)
                {
                    Object.DestroyImmediate(meshletCollection);
                }

                DestroyTestObjects(gameObject, material, mesh);
            }
        }

        [Test]
        public void LateUpdate_RefreshesTrackedMatricesAndBounds_WhenTransformChanges()
        {
            Material material = null;
            Mesh mesh = null;
            GameObject gameObject = null;
            VividMeshletCollectionAsset meshletCollection = null;

            try
            {
                gameObject = CreateMeshRendererObject("MeshletRenderer_LateUpdate", out mesh, out material);
                var meshletRenderer = gameObject.AddComponent<MeshletRenderer>();
                meshletRenderer.CaptureSourceFromRenderer(gameObject.GetComponent<MeshRenderer>());

                meshletCollection = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();
                meshletCollection.SourceSubmeshIndex = 0;
                meshletRenderer.SetMeshletCollections(new[] { meshletCollection });
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);

                Assert.That(VividMeshletRendererDatabase.instance.TryGetRendererData(meshletRenderer, out var beforeData), Is.True);

                gameObject.transform.position = new Vector3(4.0f, 2.0f, -1.0f);
                InvokeLateUpdate(meshletRenderer);

                Assert.That(VividMeshletRendererDatabase.instance.TryGetRendererData(meshletRenderer, out var afterData), Is.True);
                Assert.That(afterData.objectToWorldMatrix.m03, Is.EqualTo(4.0f).Within(0.0001f));
                Assert.That(afterData.objectToWorldMatrix.m13, Is.EqualTo(2.0f).Within(0.0001f));
                Assert.That(afterData.objectToWorldMatrix.m23, Is.EqualTo(-1.0f).Within(0.0001f));
                Assert.That(afterData.worldBounds.center, Is.Not.EqualTo(beforeData.worldBounds.center));
                Assert.That(afterData.worldBounds.center.x, Is.EqualTo(4.5f).Within(0.0001f));
                Assert.That(afterData.worldBounds.center.y, Is.EqualTo(2.5f).Within(0.0001f));
                Assert.That(afterData.worldBounds.center.z, Is.EqualTo(-1.0f).Within(0.0001f));
            }
            finally
            {
                if (meshletCollection != null)
                {
                    Object.DestroyImmediate(meshletCollection);
                }

                DestroyTestObjects(gameObject, material, mesh);
            }
        }

        [Test]
        public void OnDisable_UnregistersRendererData_WhenMeshletRendererIsDisabled()
        {
            Material material = null;
            Mesh mesh = null;
            GameObject gameObject = null;

            try
            {
                gameObject = CreateMeshRendererObject("MeshletRenderer_Disable", out mesh, out material);
                var meshletRenderer = gameObject.AddComponent<MeshletRenderer>();

                Assert.That(VividMeshletRendererDatabase.instance.rendererCount, Is.EqualTo(1));

                meshletRenderer.enabled = false;

                Assert.That(VividMeshletRendererDatabase.instance.rendererCount, Is.Zero);
                Assert.That(VividMeshletRendererDatabase.instance.TryGetRendererData(meshletRenderer, out _), Is.False);
            }
            finally
            {
                DestroyTestObjects(gameObject, material, mesh);
            }
        }

        [Test]
        public void RegisterRenderer_DoesNotResolveChildRenderer_WhenSourceIsNotCaptured()
        {
            Material material = null;
            Mesh mesh = null;
            GameObject root = null;
            GameObject child = null;

            try
            {
                root = new GameObject("MeshletRenderer_Root");
                child = CreateMeshRendererObject("MeshletRenderer_Child", out mesh, out material);
                child.transform.SetParent(root.transform, false);

                var meshletRenderer = root.AddComponent<MeshletRenderer>();

                Assert.That(VividMeshletRendererDatabase.instance.TryGetRendererResources(meshletRenderer, out var trackedResources), Is.True);
                Assert.That(trackedResources.SourceRenderer, Is.Null);
                Assert.That(trackedResources.SourceMesh, Is.Null);
            }
            finally
            {
                if (root != null)
                {
                    Object.DestroyImmediate(root);
                }
                else if (child != null)
                {
                    Object.DestroyImmediate(child);
                }

                if (material != null)
                {
                    Object.DestroyImmediate(material);
                }

                if (mesh != null)
                {
                    Object.DestroyImmediate(mesh);
                }
            }
        }

        private static GameObject CreateMeshRendererObject(string name, out Mesh mesh, out Material material)
        {
            var gameObject = new GameObject(name);
            mesh = CreateSingleSubMeshMesh(name + "_Mesh");
            material = CreateTestMaterial();

            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            return gameObject;
        }

        private static Material CreateTestMaterial()
        {
            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null);
            return new Material(shader);
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
                    new Vector3(1.0f, 1.0f, 0.0f),
                },
                normals = new[]
                {
                    Vector3.forward,
                    Vector3.forward,
                    Vector3.forward,
                    Vector3.forward,
                },
                uv = new[]
                {
                    new Vector2(0.0f, 0.0f),
                    new Vector2(1.0f, 0.0f),
                    new Vector2(0.0f, 1.0f),
                    new Vector2(1.0f, 1.0f),
                },
            };

            mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void DestroyTestObjects(GameObject gameObject, Material material, Mesh mesh)
        {
            if (gameObject != null)
            {
                Object.DestroyImmediate(gameObject);
            }

            if (material != null)
            {
                Object.DestroyImmediate(material);
            }

            if (mesh != null)
            {
                Object.DestroyImmediate(mesh);
            }
        }

        private static void InvokeLateUpdate(MeshletRenderer meshletRenderer)
        {
            Assert.That(s_LateUpdateMethod, Is.Not.Null);
            s_LateUpdateMethod.Invoke(meshletRenderer, null);
        }
    }
}
