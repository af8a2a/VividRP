using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VividRP.Editor.GPUDriven;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Meshlets;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public class VividMeshletRendererDatabaseTests
    {
        private static readonly MethodInfo s_LateUpdateMethod =
            typeof(MeshletRenderer).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo s_TerrainLateUpdateMethod =
            typeof(VividTerrain).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);

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
        public void LateUpdate_PreservesTrackedResources_WhenOnlyTransformChanges()
        {
            Material material = null;
            Mesh mesh = null;
            GameObject gameObject = null;
            VividMeshletCollectionAsset meshletCollection = null;
            GPUDrivenMaterialProxy materialProxy = null;

            try
            {
                gameObject = CreateMeshRendererObject("MeshletRenderer_TransformOnlyResources", out mesh, out material);
                var meshletRenderer = gameObject.AddComponent<MeshletRenderer>();
                meshletRenderer.CaptureSourceFromRenderer(gameObject.GetComponent<MeshRenderer>());

                meshletCollection = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();
                meshletCollection.SourceSubmeshIndex = 0;
                materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
                meshletRenderer.SetMeshletCollections(new[] { meshletCollection });
                meshletRenderer.SetMaterialProxies(new[] { materialProxy });
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);

                Assert.That(VividMeshletRendererDatabase.instance.TryGetRendererResources(meshletRenderer, out var beforeResources), Is.True);

                gameObject.transform.position = new Vector3(3.0f, -2.0f, 5.0f);
                InvokeLateUpdate(meshletRenderer);

                Assert.That(VividMeshletRendererDatabase.instance.TryGetRendererResources(meshletRenderer, out var afterResources), Is.True);
                Assert.That(afterResources.SharedMaterials, Is.SameAs(beforeResources.SharedMaterials));
                Assert.That(afterResources.MeshletCollections, Is.SameAs(beforeResources.MeshletCollections));
                Assert.That(afterResources.MaterialProxies, Is.SameAs(beforeResources.MaterialProxies));
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
        public void LateUpdate_RefreshesTrackedResources_WhenBindingsChangeThroughMeshletRenderer()
        {
            Material material = null;
            Material replacementMaterial = null;
            Mesh mesh = null;
            GameObject gameObject = null;
            VividMeshletCollectionAsset firstMeshletCollection = null;
            VividMeshletCollectionAsset secondMeshletCollection = null;
            GPUDrivenMaterialProxy firstMaterialProxy = null;
            GPUDrivenMaterialProxy secondMaterialProxy = null;

            try
            {
                gameObject = CreateMeshRendererObject("MeshletRenderer_DirtyBindings", out mesh, out material);
                replacementMaterial = CreateTestMaterial();

                var meshletRenderer = gameObject.AddComponent<MeshletRenderer>();
                meshletRenderer.CaptureSourceFromRenderer(gameObject.GetComponent<MeshRenderer>());

                firstMeshletCollection = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();
                firstMeshletCollection.SourceSubmeshIndex = 0;
                firstMaterialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
                meshletRenderer.SetMeshletCollections(new[] { firstMeshletCollection });
                meshletRenderer.SetMaterialProxies(new[] { firstMaterialProxy });
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);

                Assert.That(VividMeshletRendererDatabase.instance.TryGetRendererResources(meshletRenderer, out var beforeResources), Is.True);

                secondMeshletCollection = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();
                secondMeshletCollection.SourceSubmeshIndex = 0;
                secondMaterialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();

                meshletRenderer.SetSourceMaterials(new[] { replacementMaterial });
                meshletRenderer.SetMeshletCollections(new[] { secondMeshletCollection });
                meshletRenderer.SetMaterialProxies(new[] { secondMaterialProxy });

                InvokeLateUpdate(meshletRenderer);

                Assert.That(VividMeshletRendererDatabase.instance.TryGetRendererResources(meshletRenderer, out var afterResources), Is.True);
                Assert.That(afterResources.SharedMaterials, Is.Not.SameAs(beforeResources.SharedMaterials));
                Assert.That(afterResources.MeshletCollections, Is.Not.SameAs(beforeResources.MeshletCollections));
                Assert.That(afterResources.MaterialProxies, Is.Not.SameAs(beforeResources.MaterialProxies));
                Assert.That(afterResources.SharedMaterials[0], Is.SameAs(replacementMaterial));
                Assert.That(afterResources.MeshletCollections[0], Is.SameAs(secondMeshletCollection));
                Assert.That(afterResources.MaterialProxies[0], Is.SameAs(secondMaterialProxy));
            }
            finally
            {
                if (secondMaterialProxy != null)
                {
                    Object.DestroyImmediate(secondMaterialProxy);
                }

                if (firstMaterialProxy != null)
                {
                    Object.DestroyImmediate(firstMaterialProxy);
                }

                if (secondMeshletCollection != null)
                {
                    Object.DestroyImmediate(secondMeshletCollection);
                }

                if (firstMeshletCollection != null)
                {
                    Object.DestroyImmediate(firstMeshletCollection);
                }

                if (replacementMaterial != null)
                {
                    Object.DestroyImmediate(replacementMaterial);
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

        [Test]
        public void VividTerrain_RegistersBakedChunksAndTracksTransformUntilDisabled()
        {
            GameObject gameObject = null;
            VividTerrainData terrainData = null;
            VividMeshletCollectionAsset meshletCollection = null;

            try
            {
                meshletCollection = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();
                meshletCollection.MeshLODLevelCount = VividTerrainData.SupportedChunkLODCount;
                var chunkBounds = new Bounds(new Vector3(8.0f, 2.0f, 8.0f), new Vector3(16.0f, 4.0f, 16.0f));
                terrainData = ScriptableObject.CreateInstance<VividTerrainData>();
                terrainData.Initialize(
                    string.Empty,
                    "DatabaseTerrain",
                    17,
                    new Vector3(16.0f, 4.0f, 16.0f),
                    chunkBounds,
                    Vector2Int.one,
                    VividTerrainBakeSettings.Default,
                    null,
                    System.Array.Empty<VividTerrainLayerData>(),
                    new[]
                    {
                        new VividTerrainChunkData(
                            Vector2Int.zero,
                            Vector2Int.zero,
                            new Vector2Int(16, 16),
                            chunkBounds,
                            meshletCollection
                        ),
                    }
                );

                gameObject = new GameObject("GPUDriven Terrain");
                VividTerrain terrain = gameObject.AddComponent<VividTerrain>();
                terrain.SetData(terrainData);

                Assert.That(VividMeshletRendererDatabase.instance.rendererCount, Is.EqualTo(1));
                Assert.That(VividMeshletRendererDatabase.instance.TryGetTerrainData(terrain, out var trackedData), Is.True);
                Assert.That((trackedData.flags & VividMeshletRendererFlags.Valid) != 0, Is.True);
                Assert.That(trackedData.subMeshCount, Is.EqualTo(1));
                Assert.That(trackedData.shadowCastingMode, Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.On));
                Assert.That(VividMeshletRendererDatabase.instance.TryGetTerrainResources(terrain, out var resources), Is.True);
                Assert.That(resources.IsTerrain, Is.True);
                Assert.That(resources.TerrainData, Is.SameAs(terrainData));
                Assert.That(resources.MeshletCollections, Is.EqualTo(new[] { meshletCollection }));
                Assert.That(resources.LocalBounds, Is.EqualTo(new[] { chunkBounds }));

                gameObject.transform.position = new Vector3(3.0f, 4.0f, 5.0f);
                InvokeTerrainLateUpdate(terrain);

                Assert.That(VividMeshletRendererDatabase.instance.TryGetTerrainData(terrain, out trackedData), Is.True);
                Assert.That(trackedData.worldBounds.center, Is.EqualTo(chunkBounds.center + gameObject.transform.position));

                terrain.enabled = false;
                Assert.That(VividMeshletRendererDatabase.instance.rendererCount, Is.Zero);
            }
            finally
            {
                if (gameObject != null)
                {
                    Object.DestroyImmediate(gameObject);
                }

                if (terrainData != null)
                {
                    Object.DestroyImmediate(terrainData);
                }

                if (meshletCollection != null)
                {
                    Object.DestroyImmediate(meshletCollection);
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

        private static void InvokeTerrainLateUpdate(VividTerrain terrain)
        {
            Assert.That(s_TerrainLateUpdateMethod, Is.Not.Null);
            s_TerrainLateUpdateMethod.Invoke(terrain, null);
        }
    }
}
