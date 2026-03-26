using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Editor.GPUDriven;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Bindless;
using VividRP.Runtime.GPUDriven.Meshlets;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public class VividGPUDrivenSceneDataBuilderTests
    {
        [SetUp]
        public void SetUp()
        {
            VividMeshletRendererDatabase.instance.Clear();
            VividGPUDrivenSystem.Shutdown();
        }

        [TearDown]
        public void TearDown()
        {
            VividMeshletRendererDatabase.instance.Clear();
            VividGPUDrivenSystem.Shutdown();
        }

        [Test]
        public void Build_DeduplicatesSharedMaterialAndMeshletData_WhenTwoRenderersReferenceSameAssets()
        {
            GameObject first = null;
            GameObject second = null;
            Mesh mesh = null;
            Material material = null;
            VividMeshletCollectionAsset meshletCollection = null;

            try
            {
                mesh = CreateSingleSubMeshMesh("SharedMesh");
                material = CreateTestMaterial();
                meshletCollection = CreateMeshletCollectionAsset(
                    "SharedCollection",
                    0,
                    1,
                    new[] { CreateMeshLODNode(0, 1, 0) },
                    new[] { CreateMeshlet(0, 0, 3, 1) },
                    new[] { CreateVertex(0.0f, 0.0f, 0.0f), CreateVertex(1.0f, 0.0f, 0.0f), CreateVertex(0.0f, 1.0f, 0.0f) },
                    new byte[] { 0, 1, 2 }
                );

                first = CreateMeshletRendererObject("Renderer_A", mesh, new[] { material }, out MeshletRenderer firstRenderer);
                second = CreateMeshletRendererObject("Renderer_B", mesh, new[] { material }, out MeshletRenderer secondRenderer);
                firstRenderer.SetMeshletCollections(new[] { meshletCollection });
                secondRenderer.SetMeshletCollections(new[] { meshletCollection });
                VividMeshletRendererDatabase.instance.UpdateRendererData(firstRenderer);
                VividMeshletRendererDatabase.instance.UpdateRendererData(secondRenderer);

                var sceneData = new VividGPUDrivenSceneData();
                var builder = new VividGPUDrivenSceneDataBuilder();
                using var bindlessTextureContainer = new BindlessTextureContainer(new FakeBindlessTextureDescriptorAllocator(16));

                builder.Build(sceneData, VividMeshletRendererDatabase.instance, bindlessTextureContainer);

                Assert.That(sceneData.InstanceCount, Is.EqualTo(2));
                Assert.That(sceneData.MaterialCount, Is.EqualTo(1));
                Assert.That(sceneData.MeshLODNodeCount, Is.EqualTo(1));
                Assert.That(sceneData.MeshletCount, Is.EqualTo(1));
                Assert.That(sceneData.VertexCount, Is.EqualTo(3));
                Assert.That(sceneData.IndexCount, Is.EqualTo(3));
                Assert.That(sceneData.Instances[0].MaterialIndex, Is.EqualTo(sceneData.Instances[1].MaterialIndex));
                Assert.That(sceneData.Instances[0].TopMeshLODStartIndex, Is.EqualTo(sceneData.Instances[1].TopMeshLODStartIndex));
            }
            finally
            {
                DestroyTestObjects(first, second, material, mesh, meshletCollection);
            }
        }

        [Test]
        public void Build_ReusesStaticMeshletData_WhenSceneIsRebuiltWithoutAssetChanges()
        {
            GameObject gameObject = null;
            Mesh mesh = null;
            Material material = null;
            VividMeshletCollectionAsset meshletCollection = null;

            try
            {
                mesh = CreateSingleSubMeshMesh("StaticReuseMesh");
                material = CreateTestMaterial();
                meshletCollection = CreateMeshletCollectionAsset(
                    "StaticReuseCollection",
                    0,
                    1,
                    new[] { CreateMeshLODNode(0, 1, 0) },
                    new[] { CreateMeshlet(0, 0, 3, 1) },
                    new[] { CreateVertex(0.0f, 0.0f, 0.0f), CreateVertex(1.0f, 0.0f, 0.0f), CreateVertex(0.0f, 1.0f, 0.0f) },
                    new byte[] { 0, 1, 2 }
                );

                gameObject = CreateMeshletRendererObject("Renderer_StaticReuse", mesh, new[] { material }, out MeshletRenderer meshletRenderer);
                meshletRenderer.SetMeshletCollections(new[] { meshletCollection });
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);

                var sceneData = new VividGPUDrivenSceneData();
                var builder = new VividGPUDrivenSceneDataBuilder();
                using var bindlessTextureContainer = new BindlessTextureContainer(new FakeBindlessTextureDescriptorAllocator(16));

                bool firstStaticDataChanged = builder.Build(sceneData, VividMeshletRendererDatabase.instance, bindlessTextureContainer);
                Assert.That(firstStaticDataChanged, Is.True);
                Assert.That(sceneData.InstanceCount, Is.EqualTo(1));
                Assert.That(sceneData.MeshLODNodeCount, Is.EqualTo(1));
                Assert.That(sceneData.MeshletCount, Is.EqualTo(1));
                Assert.That(sceneData.VertexCount, Is.EqualTo(3));
                Assert.That(sceneData.IndexCount, Is.EqualTo(3));

                bool secondStaticDataChanged = builder.Build(sceneData, VividMeshletRendererDatabase.instance, bindlessTextureContainer);
                Assert.That(secondStaticDataChanged, Is.False);
                Assert.That(sceneData.InstanceCount, Is.EqualTo(1));
                Assert.That(sceneData.MeshLODNodeCount, Is.EqualTo(1));
                Assert.That(sceneData.MeshletCount, Is.EqualTo(1));
                Assert.That(sceneData.VertexCount, Is.EqualTo(3));
                Assert.That(sceneData.IndexCount, Is.EqualTo(3));
            }
            finally
            {
                DestroyTestObjects(gameObject, null, material, mesh, meshletCollection);
            }
        }

        [Test]
        public void Build_ReusesMaterialData_WhenProxyRevisionIsUnchanged()
        {
            GameObject gameObject = null;
            Mesh mesh = null;
            Material material = null;
            VividMeshletCollectionAsset meshletCollection = null;
            GPUDrivenMaterialProxy materialProxy = null;

            try
            {
                mesh = CreateSingleSubMeshMesh("MaterialReuseMesh");
                material = CreateTestMaterial();
                meshletCollection = CreateMeshletCollectionAsset(
                    "MaterialReuseCollection",
                    0,
                    1,
                    new[] { CreateMeshLODNode(0, 1, 0) },
                    new[] { CreateMeshlet(0, 0, 3, 1) },
                    new[] { CreateVertex(0.0f, 0.0f, 0.0f), CreateVertex(1.0f, 0.0f, 0.0f), CreateVertex(0.0f, 1.0f, 0.0f) },
                    new byte[] { 0, 1, 2 }
                );
                materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
                materialProxy.SourceMaterial = material;
                materialProxy.BaseColor = new Color(0.2f, 0.4f, 0.6f, 1.0f);

                gameObject = CreateMeshletRendererObject("Renderer_MaterialReuse", mesh, new[] { material }, out MeshletRenderer meshletRenderer);
                meshletRenderer.SetMeshletCollections(new[] { meshletCollection });
                meshletRenderer.SetMaterialProxies(new[] { materialProxy });
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);

                var sceneData = new VividGPUDrivenSceneData();
                var builder = new VividGPUDrivenSceneDataBuilder();
                using var bindlessTextureContainer = new BindlessTextureContainer(new FakeBindlessTextureDescriptorAllocator(16));

                bool firstStaticDataChanged = builder.Build(
                    sceneData,
                    VividMeshletRendererDatabase.instance,
                    bindlessTextureContainer,
                    out bool firstMaterialDataChanged
                );
                Assert.That(firstStaticDataChanged, Is.True);
                Assert.That(firstMaterialDataChanged, Is.True);

                bool secondStaticDataChanged = builder.Build(
                    sceneData,
                    VividMeshletRendererDatabase.instance,
                    bindlessTextureContainer,
                    out bool secondMaterialDataChanged
                );
                Assert.That(secondStaticDataChanged, Is.False);
                Assert.That(secondMaterialDataChanged, Is.False);
                Assert.That(sceneData.MaterialCount, Is.EqualTo(1));
            }
            finally
            {
                DestroyTestObjects(gameObject, null, material, mesh, meshletCollection, materialProxy);
            }
        }

        [Test]
        public void Build_RebuildsMaterialData_WhenProxyRevisionChanges()
        {
            GameObject gameObject = null;
            Mesh mesh = null;
            Material material = null;
            VividMeshletCollectionAsset meshletCollection = null;
            GPUDrivenMaterialProxy materialProxy = null;

            try
            {
                mesh = CreateSingleSubMeshMesh("MaterialChangeMesh");
                material = CreateTestMaterial();
                meshletCollection = CreateMeshletCollectionAsset(
                    "MaterialChangeCollection",
                    0,
                    1,
                    new[] { CreateMeshLODNode(0, 1, 0) },
                    new[] { CreateMeshlet(0, 0, 3, 1) },
                    new[] { CreateVertex(0.0f, 0.0f, 0.0f), CreateVertex(1.0f, 0.0f, 0.0f), CreateVertex(0.0f, 1.0f, 0.0f) },
                    new byte[] { 0, 1, 2 }
                );
                materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
                materialProxy.SourceMaterial = material;
                materialProxy.BaseColor = new Color(0.1f, 0.2f, 0.3f, 1.0f);

                gameObject = CreateMeshletRendererObject("Renderer_MaterialChange", mesh, new[] { material }, out MeshletRenderer meshletRenderer);
                meshletRenderer.SetMeshletCollections(new[] { meshletCollection });
                meshletRenderer.SetMaterialProxies(new[] { materialProxy });
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);

                var sceneData = new VividGPUDrivenSceneData();
                var builder = new VividGPUDrivenSceneDataBuilder();
                using var bindlessTextureContainer = new BindlessTextureContainer(new FakeBindlessTextureDescriptorAllocator(16));

                builder.Build(sceneData, VividMeshletRendererDatabase.instance, bindlessTextureContainer, out _);
                materialProxy.BaseColor = new Color(0.8f, 0.7f, 0.6f, 1.0f);

                bool staticDataChanged = builder.Build(
                    sceneData,
                    VividMeshletRendererDatabase.instance,
                    bindlessTextureContainer,
                    out bool materialDataChanged
                );
                Assert.That(staticDataChanged, Is.False);
                Assert.That(materialDataChanged, Is.True);
                Assert.That(sceneData.Materials[0].AlbedoColor.x, Is.EqualTo(0.8f).Within(0.0001f));
                Assert.That(sceneData.Materials[0].AlbedoColor.y, Is.EqualTo(0.7f).Within(0.0001f));
                Assert.That(sceneData.Materials[0].AlbedoColor.z, Is.EqualTo(0.6f).Within(0.0001f));
            }
            finally
            {
                DestroyTestObjects(gameObject, null, material, mesh, meshletCollection, materialProxy);
            }
        }

        [Test]
        public void Build_RebuildsStaticMeshletData_WhenTrackedMeshletAssetChanges()
        {
            GameObject gameObject = null;
            Mesh mesh = null;
            Material material = null;
            VividMeshletCollectionAsset meshletCollection = null;

            try
            {
                mesh = CreateSingleSubMeshMesh("TrackedAssetMesh");
                material = CreateTestMaterial();
                meshletCollection = CreateMeshletCollectionAsset(
                    "TrackedAssetCollection",
                    0,
                    1,
                    new[] { CreateMeshLODNode(0, 1, 0) },
                    new[] { CreateMeshlet(0, 0, 3, 1) },
                    new[] { CreateVertex(0.0f, 0.0f, 0.0f), CreateVertex(1.0f, 0.0f, 0.0f), CreateVertex(0.0f, 1.0f, 0.0f) },
                    new byte[] { 0, 1, 2 }
                );

                gameObject = CreateMeshletRendererObject("Renderer_TrackedAsset", mesh, new[] { material }, out MeshletRenderer meshletRenderer);
                meshletRenderer.SetMeshletCollections(new[] { meshletCollection });
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);

                var sceneData = new VividGPUDrivenSceneData();
                var builder = new VividGPUDrivenSceneDataBuilder();
                using var bindlessTextureContainer = new BindlessTextureContainer(new FakeBindlessTextureDescriptorAllocator(16));

                bool firstStaticDataChanged = builder.Build(sceneData, VividMeshletRendererDatabase.instance, bindlessTextureContainer);
                Assert.That(firstStaticDataChanged, Is.True);
                Assert.That(sceneData.VertexCount, Is.EqualTo(3));
                Assert.That(sceneData.IndexCount, Is.EqualTo(3));

                meshletCollection.Meshlets = new[] { CreateMeshlet(0, 0, 6, 2) };
                meshletCollection.MeshLODNodes = new[] { CreateMeshLODNode(0, 1, 0) };
                meshletCollection.VertexBuffer = new[]
                {
                    CreateVertex(0.0f, 0.0f, 0.0f),
                    CreateVertex(1.0f, 0.0f, 0.0f),
                    CreateVertex(0.0f, 1.0f, 0.0f),
                    CreateVertex(1.0f, 1.0f, 0.0f),
                    CreateVertex(2.0f, 0.0f, 0.0f),
                    CreateVertex(2.0f, 1.0f, 0.0f),
                };
                meshletCollection.IndexBuffer = new byte[] { 0, 1, 2, 3, 4, 5 };
                meshletCollection.MarkChanged();

                bool secondStaticDataChanged = builder.Build(sceneData, VividMeshletRendererDatabase.instance, bindlessTextureContainer);
                Assert.That(secondStaticDataChanged, Is.True);
                Assert.That(sceneData.VertexCount, Is.EqualTo(6));
                Assert.That(sceneData.IndexCount, Is.EqualTo(6));
            }
            finally
            {
                DestroyTestObjects(gameObject, null, material, mesh, meshletCollection);
            }
        }

        [Test]
        public void Build_CreatesOneInstancePerSubmeshAndPatchesOffsets_WhenMeshletAssetsAreUnique()
        {
            GameObject gameObject = null;
            Mesh mesh = null;
            Material material0 = null;
            Material material1 = null;
            VividMeshletCollectionAsset meshletCollection0 = null;
            VividMeshletCollectionAsset meshletCollection1 = null;

            try
            {
                mesh = CreateTwoSubMeshMesh("MultiSubMesh");
                material0 = CreateTestMaterial();
                material1 = CreateTestMaterial();
                meshletCollection0 = CreateMeshletCollectionAsset(
                    "Collection_0",
                    0,
                    1,
                    new[] { CreateMeshLODNode(0, 1, 0) },
                    new[] { CreateMeshlet(0, 0, 3, 1) },
                    new[] { CreateVertex(0.0f, 0.0f, 0.0f), CreateVertex(1.0f, 0.0f, 0.0f), CreateVertex(0.0f, 1.0f, 0.0f) },
                    new byte[] { 0, 1, 2 }
                );
                meshletCollection1 = CreateMeshletCollectionAsset(
                    "Collection_1",
                    1,
                    2,
                    new[]
                    {
                        CreateMeshLODNode(0, 1, 0),
                        CreateMeshLODNode(1, 1, 1),
                    },
                    new[]
                    {
                        CreateMeshlet(0, 0, 3, 1),
                        CreateMeshlet(3, 3, 3, 1),
                    },
                    new[]
                    {
                        CreateVertex(2.0f, 0.0f, 0.0f),
                        CreateVertex(2.0f, 1.0f, 0.0f),
                        CreateVertex(3.0f, 0.0f, 0.0f),
                        CreateVertex(3.0f, 1.0f, 0.0f),
                        CreateVertex(4.0f, 0.0f, 0.0f),
                        CreateVertex(4.0f, 1.0f, 0.0f),
                    },
                    new byte[] { 0, 1, 2, 3, 4, 5 }
                );

                gameObject = CreateMeshletRendererObject(
                    "Renderer_MultiSubMesh",
                    mesh,
                    new[] { material0, material1 },
                    out MeshletRenderer meshletRenderer
                );
                meshletRenderer.SetMeshletCollections(new[] { meshletCollection0, meshletCollection1 });
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);

                var sceneData = new VividGPUDrivenSceneData();
                var builder = new VividGPUDrivenSceneDataBuilder();
                using var bindlessTextureContainer = new BindlessTextureContainer(new FakeBindlessTextureDescriptorAllocator(16));

                builder.Build(sceneData, VividMeshletRendererDatabase.instance, bindlessTextureContainer);

                Assert.That(sceneData.InstanceCount, Is.EqualTo(2));
                Assert.That(sceneData.MaterialCount, Is.EqualTo(2));
                Assert.That(sceneData.MeshLODNodeCount, Is.EqualTo(3));
                Assert.That(sceneData.MeshletCount, Is.EqualTo(3));
                Assert.That(sceneData.VertexCount, Is.EqualTo(9));
                Assert.That(sceneData.IndexCount, Is.EqualTo(9));
                Assert.That(sceneData.Instances[0].TopMeshLODStartIndex, Is.EqualTo(0u));
                Assert.That(sceneData.Instances[0].TotalMeshLODCount, Is.EqualTo(1u));
                Assert.That(sceneData.Instances[1].TopMeshLODStartIndex, Is.EqualTo(1u));
                Assert.That(sceneData.Instances[1].TotalMeshLODCount, Is.EqualTo(2u));
                Assert.That(sceneData.MeshLODNodes[1].MeshletStartIndex, Is.EqualTo(1u));
                Assert.That(sceneData.MeshLODNodes[2].MeshletStartIndex, Is.EqualTo(2u));
                Assert.That(sceneData.Meshlets[1].VertexOffset, Is.EqualTo(3u));
                Assert.That(sceneData.Meshlets[1].TriangleOffset, Is.EqualTo(3u));
                Assert.That(sceneData.Meshlets[2].VertexOffset, Is.EqualTo(6u));
                Assert.That(sceneData.Meshlets[2].TriangleOffset, Is.EqualTo(6u));
            }
            finally
            {
                DestroyTestObjects(gameObject, null, material0, material1, mesh, meshletCollection0, meshletCollection1);
            }
        }

        [Test]
        public void Build_UsesMaterialProxyData_WhenProxyIsAssigned()
        {
            GameObject gameObject = null;
            Mesh mesh = null;
            Material material = null;
            Texture2D baseMap = null;
            Texture2D bumpMap = null;
            VividMeshletCollectionAsset meshletCollection = null;
            GPUDrivenMaterialProxy materialProxy = null;

            try
            {
                mesh = CreateSingleSubMeshMesh("ProxyMaterialMesh");
                material = CreateTestMaterial();
                baseMap = new Texture2D(1, 1);
                bumpMap = new Texture2D(1, 1);
                materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
                materialProxy.SourceMaterial = material;
                materialProxy.BaseMap = baseMap;
                materialProxy.BaseColor = new Color(0.8f, 0.6f, 0.4f, 1.0f);
                materialProxy.TextureTilingOffset = new Vector4(4.0f, 5.0f, 0.25f, 0.5f);
                materialProxy.BumpMap = bumpMap;
                materialProxy.BumpScale = 0.4f;
                materialProxy.Metallic = 0.75f;
                materialProxy.Roughness = 0.35f;
                materialProxy.EmissionColor = new Color(0.1f, 0.2f, 0.3f, 1.0f);
                materialProxy.AlphaClip = true;
                materialProxy.Cutoff = 0.4f;
                materialProxy.CullMode = CullMode.Off;
                materialProxy.DisableLighting = true;

                meshletCollection = CreateMeshletCollectionAsset(
                    "ProxyMaterialCollection",
                    0,
                    1,
                    new[] { CreateMeshLODNode(0, 1, 0) },
                    new[] { CreateMeshlet(0, 0, 3, 1) },
                    new[] { CreateVertex(0.0f, 0.0f, 0.0f), CreateVertex(1.0f, 0.0f, 0.0f), CreateVertex(0.0f, 1.0f, 0.0f) },
                    new byte[] { 0, 1, 2 }
                );

                gameObject = CreateMeshletRendererObject("Renderer_ProxyMaterial", mesh, new[] { material }, out MeshletRenderer meshletRenderer);
                meshletRenderer.SetMeshletCollections(new[] { meshletCollection });
                meshletRenderer.SetMaterialProxies(new[] { materialProxy });
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);

                var sceneData = new VividGPUDrivenSceneData();
                var builder = new VividGPUDrivenSceneDataBuilder();
                using var bindlessTextureContainer = new BindlessTextureContainer(new FakeBindlessTextureDescriptorAllocator(16));

                builder.Build(sceneData, VividMeshletRendererDatabase.instance, bindlessTextureContainer);

                Assert.That(sceneData.MaterialCount, Is.EqualTo(1));
                VividMaterialData materialData = sceneData.Materials[0];
                Assert.That(materialData.AlbedoColor.x, Is.EqualTo(0.8f).Within(0.0001f));
                Assert.That(materialData.TextureTilingOffset.y, Is.EqualTo(5.0f).Within(0.0001f));
                Assert.That(materialData.AlbedoIndex, Is.Not.EqualTo(VividMaterialData.NoTextureIndex));
                Assert.That(materialData.NormalsIndex, Is.Not.EqualTo(VividMaterialData.NoTextureIndex));
                Assert.That(materialData.MasksIndex, Is.EqualTo(VividMaterialData.NoTextureIndex));
                Assert.That(materialData.NormalsStrength, Is.EqualTo(0.4f).Within(0.0001f));
                Assert.That(materialData.Metallic, Is.EqualTo(0.75f).Within(0.0001f));
                Assert.That(materialData.Roughness, Is.EqualTo(0.35f).Within(0.0001f));
                Assert.That(materialData.MaterialFlags, Is.EqualTo(VividMaterialFlags.Unlit));
                Assert.That(materialData.RendererListID, Is.EqualTo(VividRendererListID.CullOff | VividRendererListID.AlphaTest));
                Assert.That(materialData.AlphaClipThreshold, Is.EqualTo(0.4f).Within(0.0001f));
            }
            finally
            {
                DestroyTestObjects(gameObject, null, material, mesh, meshletCollection, materialProxy);

                if (baseMap != null)
                {
                    Object.DestroyImmediate(baseMap);
                }

                if (bumpMap != null)
                {
                    Object.DestroyImmediate(bumpMap);
                }
            }
        }

        [Test]
        public void Build_UsesMaterialProxyData_WhenMeshRendererWasRemoved()
        {
            Shader shader = Shader.Find("VividRP/Material/StandardLit");
            if (shader == null)
            {
                Assert.Ignore("VividRP/Material/StandardLit shader is not available.");
            }

            GameObject gameObject = null;
            Mesh mesh = null;
            Material material = null;
            Texture2D baseMap = null;
            VividMeshletCollectionAsset meshletCollection = null;
            GPUDrivenMaterialProxy materialProxy = null;

            try
            {
                mesh = CreateSingleSubMeshMesh("RendererlessProxyMesh");
                material = new Material(shader);
                baseMap = new Texture2D(1, 1);
                material.SetColor("_BaseColor", new Color(0.3f, 0.4f, 0.5f, 1.0f));
                material.SetTexture("_BaseMap", baseMap);
                materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
                materialProxy.SourceMaterial = material;

                meshletCollection = CreateMeshletCollectionAsset(
                    "RendererlessProxyCollection",
                    0,
                    1,
                    new[] { CreateMeshLODNode(0, 1, 0) },
                    new[] { CreateMeshlet(0, 0, 3, 1) },
                    new[] { CreateVertex(0.0f, 0.0f, 0.0f), CreateVertex(1.0f, 0.0f, 0.0f), CreateVertex(0.0f, 1.0f, 0.0f) },
                    new byte[] { 0, 1, 2 }
                );

                gameObject = CreateMeshletRendererObject("Renderer_RendererlessProxy", mesh, new[] { material }, out MeshletRenderer meshletRenderer);
                meshletRenderer.SetMeshletCollections(new[] { meshletCollection });
                meshletRenderer.SetMaterialProxies(new[] { materialProxy });
                MeshletRendererEditorUtility.TakeOverAndRemoveSourceMeshRenderer(meshletRenderer);
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);
                GPUDrivenMaterialProxy syncedProxy = meshletRenderer.GetMaterialProxy(0);

                var sceneData = new VividGPUDrivenSceneData();
                var builder = new VividGPUDrivenSceneDataBuilder();
                using var bindlessTextureContainer = new BindlessTextureContainer(new FakeBindlessTextureDescriptorAllocator(16));

                builder.Build(sceneData, VividMeshletRendererDatabase.instance, bindlessTextureContainer);

                Assert.That(gameObject.GetComponent<MeshRenderer>(), Is.Null);
                Assert.That(sceneData.InstanceCount, Is.EqualTo(1));
                Assert.That(sceneData.MaterialCount, Is.EqualTo(1));
                Assert.That(sceneData.Materials[0].AlbedoColor.x, Is.EqualTo(syncedProxy.BaseColor.r).Within(0.0001f));
                Assert.That(sceneData.Materials[0].AlbedoIndex, Is.Not.EqualTo(VividMaterialData.NoTextureIndex));
            }
            finally
            {
                DestroyTestObjects(gameObject, null, material, mesh, meshletCollection, materialProxy);

                if (baseMap != null)
                {
                    Object.DestroyImmediate(baseMap);
                }
            }
        }

        [Test]
        public void PrepareFrame_ExtractsVividMaterialProperties_WhenGpuDrivenSystemBuildsSceneData()
        {
            Shader shader = Shader.Find("VividRP/Material/StandardLit");
            if (shader == null)
            {
                Assert.Ignore("VividRP/Material/StandardLit shader is not available.");
            }

            GameObject gameObject = null;
            Mesh mesh = null;
            Material material = null;
            Texture2D baseMap = null;
            Texture2D normalMap = null;
            Texture2D metallicMap = null;
            VividMeshletCollectionAsset meshletCollection = null;

            try
            {
                mesh = CreateSingleSubMeshMesh("MaterialMesh");
                baseMap = new Texture2D(1, 1);
                normalMap = new Texture2D(1, 1);
                metallicMap = new Texture2D(1, 1);
                material = new Material(shader);
                material.SetColor("_BaseColor", new Color(0.25f, 0.5f, 0.75f, 1.0f));
                material.SetTexture("_BaseMap", baseMap);
                material.SetTextureScale("_BaseMap", new Vector2(2.0f, 3.0f));
                material.SetTextureOffset("_BaseMap", new Vector2(0.1f, 0.2f));
                material.SetTexture("_BumpMap", normalMap);
                material.SetFloat("_BumpScale", 0.75f);
                material.SetTexture("_MetallicGlossMap", metallicMap);
                material.SetFloat("_Metallic", 0.4f);
                material.SetFloat("_Smoothness", 0.2f);
                material.SetColor("_EmissionColor", new Color(1.0f, 0.5f, 0.0f, 0.25f));
                material.SetFloat("_AlphaClip", 1.0f);
                material.SetFloat("_Cutoff", 0.33f);
                material.SetFloat("_Cull", 0.0f);

                meshletCollection = CreateMeshletCollectionAsset(
                    "MaterialCollection",
                    0,
                    1,
                    new[] { CreateMeshLODNode(0, 1, 0) },
                    new[] { CreateMeshlet(0, 0, 3, 1) },
                    new[] { CreateVertex(0.0f, 0.0f, 0.0f), CreateVertex(1.0f, 0.0f, 0.0f), CreateVertex(0.0f, 1.0f, 0.0f) },
                    new byte[] { 0, 1, 2 }
                );

                gameObject = CreateMeshletRendererObject("Renderer_Material", mesh, new[] { material }, out MeshletRenderer meshletRenderer);
                meshletRenderer.SetMeshletCollections(new[] { meshletCollection });
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);

                using var system = new VividGPUDrivenSystem(new FakeBindlessTextureDescriptorAllocator(16));
                system.PrepareFrame();

                Assert.That(system.SceneData.MaterialCount, Is.EqualTo(1));
                Assert.That(system.BufferSet.InstanceCount, Is.EqualTo(1));
                Assert.That(system.BufferSet.MaterialCount, Is.EqualTo(1));
                Assert.That(system.BufferSet.MeshLODNodeCount, Is.EqualTo(1));
                Assert.That(system.BufferSet.MeshletCount, Is.EqualTo(1));
                Assert.That(system.BufferSet.SharedVertexCount, Is.EqualTo(3));
                Assert.That(system.BufferSet.SharedIndexCount, Is.EqualTo(3));
                VividMaterialData materialData = system.SceneData.Materials[0];
                Assert.That(materialData.AlbedoColor.x, Is.EqualTo(0.25f).Within(0.0001f));
                Assert.That(materialData.AlbedoColor.y, Is.EqualTo(0.5f).Within(0.0001f));
                Assert.That(materialData.AlbedoColor.z, Is.EqualTo(0.75f).Within(0.0001f));
                Assert.That(materialData.TextureTilingOffset.x, Is.EqualTo(2.0f).Within(0.0001f));
                Assert.That(materialData.TextureTilingOffset.y, Is.EqualTo(3.0f).Within(0.0001f));
                Assert.That(materialData.TextureTilingOffset.z, Is.EqualTo(0.1f).Within(0.0001f));
                Assert.That(materialData.TextureTilingOffset.w, Is.EqualTo(0.2f).Within(0.0001f));
                Assert.That(materialData.AlbedoIndex, Is.Not.EqualTo(VividMaterialData.NoTextureIndex));
                Assert.That(materialData.NormalsIndex, Is.Not.EqualTo(VividMaterialData.NoTextureIndex));
                Assert.That(materialData.MasksIndex, Is.Not.EqualTo(VividMaterialData.NoTextureIndex));
                Assert.That(materialData.NormalsStrength, Is.EqualTo(0.75f).Within(0.0001f));
                Assert.That(materialData.Metallic, Is.EqualTo(0.4f).Within(0.0001f));
                Assert.That(materialData.Roughness, Is.EqualTo(0.8f).Within(0.0001f));
                Assert.That(materialData.Emission.x, Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(materialData.RendererListID, Is.EqualTo(VividRendererListID.CullOff | VividRendererListID.AlphaTest));
                Assert.That(materialData.AlphaClipThreshold, Is.EqualTo(0.33f).Within(0.0001f));
            }
            finally
            {
                DestroyTestObjects(gameObject, null, material, mesh, meshletCollection);

                if (baseMap != null)
                {
                    Object.DestroyImmediate(baseMap);
                }

                if (normalMap != null)
                {
                    Object.DestroyImmediate(normalMap);
                }

                if (metallicMap != null)
                {
                    Object.DestroyImmediate(metallicMap);
                }
            }
        }

        private static GameObject CreateMeshletRendererObject(
            string name,
            Mesh mesh,
            Material[] materials,
            out MeshletRenderer meshletRenderer
        )
        {
            var gameObject = new GameObject(name);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterials = materials;
            meshletRenderer = gameObject.AddComponent<MeshletRenderer>();
            meshletRenderer.CaptureSourceFromRenderer(meshRenderer);
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
                tangents = new[]
                {
                    new Vector4(1.0f, 0.0f, 0.0f, 1.0f),
                    new Vector4(1.0f, 0.0f, 0.0f, 1.0f),
                    new Vector4(1.0f, 0.0f, 0.0f, 1.0f),
                    new Vector4(1.0f, 0.0f, 0.0f, 1.0f),
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

        private static Mesh CreateTwoSubMeshMesh(string meshName)
        {
            var mesh = CreateSingleSubMeshMesh(meshName);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new[] { 0, 2, 1 }, 0, true);
            mesh.SetTriangles(new[] { 1, 2, 3 }, 1, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static VividMeshletCollectionAsset CreateMeshletCollectionAsset(
            string name,
            int sourceSubmeshIndex,
            int meshLODLevelCount,
            VividMeshLODNode[] meshLODNodes,
            VividMeshlet[] meshlets,
            VividMeshletVertex[] vertices,
            byte[] indices
        )
        {
            var asset = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();
            var meshLODLevelNodeCounts = new int[Mathf.Max(1, meshLODLevelCount)];
            for (int nodeIndex = 0; nodeIndex < meshLODNodes.Length; nodeIndex++)
            {
                int levelIndex = Mathf.Clamp((int) meshLODNodes[nodeIndex].LevelIndex, 0, meshLODLevelNodeCounts.Length - 1);
                meshLODLevelNodeCounts[levelIndex]++;
            }

            asset.name = name;
            asset.SourceSubmeshIndex = sourceSubmeshIndex;
            asset.Bounds = new Bounds(Vector3.zero, Vector3.one);
            asset.MeshLODLevelCount = meshLODLevelCount;
            asset.LeafMeshletCount = meshlets.Length;
            asset.MeshLODLevelNodeCounts = meshLODLevelNodeCounts;
            asset.MeshLODNodes = meshLODNodes;
            asset.Meshlets = meshlets;
            asset.VertexBuffer = vertices;
            asset.IndexBuffer = indices;
            return asset;
        }

        private static VividMeshLODNode CreateMeshLODNode(uint meshletStartIndex, uint meshletCount, uint levelIndex)
        {
            return new VividMeshLODNode
            {
                MeshletStartIndex = meshletStartIndex,
                MeshletCount = meshletCount,
                LevelIndex = levelIndex,
                Bounds = new float4(0.0f, 0.0f, 0.0f, 1.0f),
                ParentBounds = new float4(0.0f, 0.0f, 0.0f, 1.0f),
                ParentError = -1.0f,
                Error = 0.0f,
            };
        }

        private static VividMeshlet CreateMeshlet(uint vertexOffset, uint triangleOffset, uint vertexCount, uint triangleCount)
        {
            return new VividMeshlet
            {
                VertexOffset = vertexOffset,
                TriangleOffset = triangleOffset,
                VertexCount = vertexCount,
                TriangleCount = triangleCount,
                BoundingSphere = new float4(0.0f, 0.0f, 0.0f, 1.0f),
                ConeApexCutoff = new float4(0.0f, 0.0f, 1.0f, 0.0f),
                ConeAxis = new float4(0.0f, 0.0f, 1.0f, 0.0f),
            };
        }

        private static VividMeshletVertex CreateVertex(float x, float y, float z)
        {
            return new VividMeshletVertex
            {
                Position = new float4(x, y, z, 1.0f),
                Normal = new float4(0.0f, 0.0f, 1.0f, 0.0f),
                Tangent = new float4(1.0f, 0.0f, 0.0f, 1.0f),
                UV = new float4(x, y, 0.0f, 0.0f),
            };
        }

        private static void DestroyTestObjects(
            GameObject firstGameObject,
            GameObject secondGameObject,
            params Object[] objects
        )
        {
            if (firstGameObject != null)
            {
                Object.DestroyImmediate(firstGameObject);
            }

            if (secondGameObject != null)
            {
                Object.DestroyImmediate(secondGameObject);
            }

            foreach (Object instance in objects)
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        private sealed class FakeBindlessTextureDescriptorAllocator : IBindlessTextureDescriptorAllocator
        {
            public FakeBindlessTextureDescriptorAllocator(uint descriptorHeapCount)
            {
                DescriptorHeapCount = descriptorHeapCount;
                DescriptorCapacity = descriptorHeapCount;
            }

            public bool IsAvailable => true;

            public uint DescriptorHeapCount { get; }
            public uint DescriptorStartIndex { get; }
            public uint DescriptorCapacity { get; }

            public string UnavailableReason => string.Empty;

            public uint CreateSRVDescriptorCallCountThisFrame { get; private set; }

            public void ResetPerFrameStats()
            {
                CreateSRVDescriptorCallCountThisFrame = 0;
            }

            public bool TryCreateTextureDescriptor(Texture texture, uint index)
            {
                CreateSRVDescriptorCallCountThisFrame++;
                return true;
            }
        }

    }
}
