using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Editor.GPUDriven;
using VividRP.Runtime;
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
        public void AddInstance_ClassifiesActiveRendererBatchKeysByPass()
        {
            var sceneData = new VividGPUDrivenSceneData();
            sceneData.MutableMaterials.Add(new VividMaterialData
            {
                RendererListID = VividRendererListID.Default,
            });
            sceneData.MutableMaterials.Add(new VividMaterialData
            {
                RendererListID = VividRendererListID.CullOff | VividRendererListID.AlphaTest,
            });
            sceneData.MutableMaterials.Add(new VividMaterialData
            {
                RendererListID = VividRendererListID.AlphaTest,
            });
            sceneData.MutableMaterials.Add(new VividMaterialData
            {
                RendererListID = VividRendererListID.CullOff,
            });

            sceneData.AddInstance(
                new VividInstanceData
                {
                    MaterialIndex = 0,
                    PassMask = VividInstancePassMask.Main,
                    Flags = VividInstanceFlags.FlipWindingOrder,
                },
                maxVisibleMeshletRenderRequestCount: 1);
            sceneData.AddInstance(
                new VividInstanceData
                {
                    MaterialIndex = 1,
                    PassMask = VividInstancePassMask.Main,
                    Flags = VividInstanceFlags.FlipWindingOrder,
                },
                maxVisibleMeshletRenderRequestCount: 1);
            sceneData.AddInstance(
                new VividInstanceData
                {
                    MaterialIndex = 3,
                    PassMask = VividInstancePassMask.Main | VividInstancePassMask.Shadows,
                },
                maxVisibleMeshletRenderRequestCount: 1);
            sceneData.AddInstance(
                new VividInstanceData
                {
                    MaterialIndex = 2,
                    PassMask = VividInstancePassMask.Shadows,
                    Flags = VividInstanceFlags.FlipWindingOrder,
                },
                maxVisibleMeshletRenderRequestCount: 1);
            sceneData.AddInstance(
                new VividInstanceData
                {
                    MaterialIndex = 2,
                    PassMask = VividInstancePassMask.Main,
                    Flags = VividInstanceFlags.Disabled,
                },
                maxVisibleMeshletRenderRequestCount: 1);

            Assert.That(sceneData.IsMainViewRendererBatchActive(VividRendererListID.CullFront), Is.True);
            Assert.That(
                sceneData.IsMainViewRendererBatchActive(VividRendererListID.CullOff | VividRendererListID.AlphaTest),
                Is.True);
            Assert.That(sceneData.IsMainViewRendererBatchActive(VividRendererListID.CullOff), Is.True);
            Assert.That(sceneData.IsMainViewRendererBatchActive(VividRendererListID.Default), Is.False);
            Assert.That(sceneData.IsMainViewRendererBatchActive(VividRendererListID.AlphaTest), Is.False);
            Assert.That(
                sceneData.IsShadowRendererBatchActive(VividRendererListID.CullFront | VividRendererListID.AlphaTest),
                Is.True);
            Assert.That(sceneData.IsShadowRendererBatchActive(VividRendererListID.CullOff), Is.True);
            Assert.That(sceneData.IsShadowRendererBatchActive(VividRendererListID.CullFront), Is.False);
            Assert.That(
                sceneData.IsShadowRendererBatchActive(VividRendererListID.CullOff | VividRendererListID.AlphaTest),
                Is.False);

            sceneData.ClearInstances();

            Assert.That(sceneData.IsMainViewRendererBatchActive(VividRendererListID.CullFront), Is.False);
            Assert.That(
                sceneData.IsMainViewRendererBatchActive(VividRendererListID.CullOff | VividRendererListID.AlphaTest),
                Is.False);
            Assert.That(
                sceneData.IsShadowRendererBatchActive(VividRendererListID.CullFront | VividRendererListID.AlphaTest),
                Is.False);
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
                using var bindlessTextureContainer = new BindlessGPUDrivenTextureBackend(new FakeBindlessTextureDescriptorAllocator(16));

                builder.Build(sceneData, VividMeshletRendererDatabase.instance, bindlessTextureContainer);

                Assert.That(sceneData.InstanceCount, Is.EqualTo(2));
                Assert.That(sceneData.MaterialCount, Is.EqualTo(1));
                Assert.That(sceneData.SurfaceBindingCount, Is.EqualTo(1));
                Assert.That(sceneData.MeshLODNodeCount, Is.EqualTo(1));
                Assert.That(sceneData.MeshletCount, Is.EqualTo(1));
                Assert.That(sceneData.VertexCount, Is.EqualTo(3));
                Assert.That(sceneData.IndexCount, Is.EqualTo(3));
                Assert.That(sceneData.MaxMeshletListBuildJobCount, Is.EqualTo(2));
                Assert.That(sceneData.MaxVisibleMeshletRenderRequestCount, Is.EqualTo(2));
                Assert.That(sceneData.Instances[0].MaterialIndex, Is.EqualTo(sceneData.Instances[1].MaterialIndex));
                Assert.That(sceneData.Instances[0].TopMeshLODStartIndex, Is.EqualTo(sceneData.Instances[1].TopMeshLODStartIndex));
                Assert.That(sceneData.IsMainViewRendererBatchActive(VividRendererListID.Default), Is.True);
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
                using var bindlessTextureContainer = new BindlessGPUDrivenTextureBackend(new FakeBindlessTextureDescriptorAllocator(16));

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
                using var bindlessTextureContainer = new BindlessGPUDrivenTextureBackend(new FakeBindlessTextureDescriptorAllocator(16));

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
        public void Build_ReusesInstanceData_WhenRendererDataIsUnchanged()
        {
            GameObject gameObject = null;
            Mesh mesh = null;
            Material material = null;
            VividMeshletCollectionAsset meshletCollection = null;
            GPUDrivenMaterialProxy materialProxy = null;

            try
            {
                mesh = CreateSingleSubMeshMesh("InstanceReuseMesh");
                material = CreateTestMaterial();
                meshletCollection = CreateMeshletCollectionAsset(
                    "InstanceReuseCollection",
                    0,
                    1,
                    new[] { CreateMeshLODNode(0, 1, 0) },
                    new[] { CreateMeshlet(0, 0, 3, 1) },
                    new[] { CreateVertex(0.0f, 0.0f, 0.0f), CreateVertex(1.0f, 0.0f, 0.0f), CreateVertex(0.0f, 1.0f, 0.0f) },
                    new byte[] { 0, 1, 2 }
                );
                materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
                materialProxy.SourceMaterial = material;

                gameObject = CreateMeshletRendererObject("Renderer_InstanceReuse", mesh, new[] { material }, out MeshletRenderer meshletRenderer);
                meshletRenderer.SetMeshletCollections(new[] { meshletCollection });
                meshletRenderer.SetMaterialProxies(new[] { materialProxy });
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);

                var sceneData = new VividGPUDrivenSceneData();
                var builder = new VividGPUDrivenSceneDataBuilder();
                using var bindlessTextureContainer = new BindlessGPUDrivenTextureBackend(new FakeBindlessTextureDescriptorAllocator(16));

                builder.Build(
                    sceneData,
                    VividMeshletRendererDatabase.instance,
                    bindlessTextureContainer,
                    out bool firstMaterialDataChanged,
                    out bool firstInstanceDataChanged
                );
                Assert.That(firstMaterialDataChanged, Is.True);
                Assert.That(firstInstanceDataChanged, Is.True);
                Assert.That(sceneData.MaxMeshletListBuildJobCount, Is.EqualTo(1));
                Assert.That(sceneData.MaxVisibleMeshletRenderRequestCount, Is.EqualTo(1));

                VividInstanceData retainedInstanceData = sceneData.MutableInstances[0];
                retainedInstanceData.Padding0 = 123u;
                sceneData.MutableInstances[0] = retainedInstanceData;

                bool staticDataChanged = builder.Build(
                    sceneData,
                    VividMeshletRendererDatabase.instance,
                    bindlessTextureContainer,
                    out bool materialDataChanged,
                    out bool instanceDataChanged
                );

                Assert.That(staticDataChanged, Is.False);
                Assert.That(materialDataChanged, Is.False);
                Assert.That(instanceDataChanged, Is.False);
                Assert.That(sceneData.MaxMeshletListBuildJobCount, Is.EqualTo(1));
                Assert.That(sceneData.MaxVisibleMeshletRenderRequestCount, Is.EqualTo(1));
                Assert.That(sceneData.Instances[0].Padding0, Is.EqualTo(123u));
            }
            finally
            {
                DestroyTestObjects(gameObject, null, material, mesh, meshletCollection, materialProxy);
            }
        }

        [Test]
        public void Build_RebuildsInstanceData_WhenRendererTransformChanges()
        {
            GameObject gameObject = null;
            Mesh mesh = null;
            Material material = null;
            VividMeshletCollectionAsset meshletCollection = null;
            GPUDrivenMaterialProxy materialProxy = null;

            try
            {
                mesh = CreateSingleSubMeshMesh("InstanceChangeMesh");
                material = CreateTestMaterial();
                meshletCollection = CreateMeshletCollectionAsset(
                    "InstanceChangeCollection",
                    0,
                    1,
                    new[] { CreateMeshLODNode(0, 1, 0) },
                    new[] { CreateMeshlet(0, 0, 3, 1) },
                    new[] { CreateVertex(0.0f, 0.0f, 0.0f), CreateVertex(1.0f, 0.0f, 0.0f), CreateVertex(0.0f, 1.0f, 0.0f) },
                    new byte[] { 0, 1, 2 }
                );
                materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
                materialProxy.SourceMaterial = material;

                gameObject = CreateMeshletRendererObject("Renderer_InstanceChange", mesh, new[] { material }, out MeshletRenderer meshletRenderer);
                meshletRenderer.SetMeshletCollections(new[] { meshletCollection });
                meshletRenderer.SetMaterialProxies(new[] { materialProxy });
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);

                var sceneData = new VividGPUDrivenSceneData();
                var builder = new VividGPUDrivenSceneDataBuilder();
                using var bindlessTextureContainer = new BindlessGPUDrivenTextureBackend(new FakeBindlessTextureDescriptorAllocator(16));

                builder.Build(
                    sceneData,
                    VividMeshletRendererDatabase.instance,
                    bindlessTextureContainer,
                    out _,
                    out _
                );

                gameObject.transform.position = new Vector3(4.0f, 2.0f, -1.0f);
                VividMeshletRendererDatabase.instance.UpdateRendererTransformData(meshletRenderer);

                bool staticDataChanged = builder.Build(
                    sceneData,
                    VividMeshletRendererDatabase.instance,
                    bindlessTextureContainer,
                    out bool materialDataChanged,
                    out bool instanceDataChanged
                );

                Assert.That(staticDataChanged, Is.False);
                Assert.That(materialDataChanged, Is.False);
                Assert.That(instanceDataChanged, Is.True);
                Assert.That(sceneData.Instances[0].ObjectToWorldMatrix.c3.x, Is.EqualTo(4.0f).Within(0.0001f));
                Assert.That(sceneData.Instances[0].ObjectToWorldMatrix.c3.y, Is.EqualTo(2.0f).Within(0.0001f));
                Assert.That(sceneData.Instances[0].ObjectToWorldMatrix.c3.z, Is.EqualTo(-1.0f).Within(0.0001f));
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
                using var bindlessTextureContainer = new BindlessGPUDrivenTextureBackend(new FakeBindlessTextureDescriptorAllocator(16));

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
        public void Build_RebuildsMaterialData_WhenBindlessTextureBindingRevisionChanges()
        {
            GameObject gameObject = null;
            Mesh mesh = null;
            Material material = null;
            VividMeshletCollectionAsset meshletCollection = null;
            GPUDrivenMaterialProxy materialProxy = null;
            Texture2D texture = null;

            try
            {
                mesh = CreateSingleSubMeshMesh("MaterialTextureBindingChangeMesh");
                material = CreateTestMaterial();
                texture = new Texture2D(1, 1);
                meshletCollection = CreateMeshletCollectionAsset(
                    "MaterialTextureBindingChangeCollection",
                    0,
                    1,
                    new[] { CreateMeshLODNode(0, 1, 0) },
                    new[] { CreateMeshlet(0, 0, 3, 1) },
                    new[] { CreateVertex(0.0f, 0.0f, 0.0f), CreateVertex(1.0f, 0.0f, 0.0f), CreateVertex(0.0f, 1.0f, 0.0f) },
                    new byte[] { 0, 1, 2 }
                );
                materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
                materialProxy.SourceMaterial = material;
                materialProxy.BaseMap = texture;

                gameObject = CreateMeshletRendererObject("Renderer_MaterialTextureBindingChange", mesh, new[] { material }, out MeshletRenderer meshletRenderer);
                meshletRenderer.SetMeshletCollections(new[] { meshletCollection });
                meshletRenderer.SetMaterialProxies(new[] { materialProxy });
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);

                var sceneData = new VividGPUDrivenSceneData();
                var builder = new VividGPUDrivenSceneDataBuilder();
                using var bindlessTextureContainer = new BindlessGPUDrivenTextureBackend(new FakeBindlessTextureDescriptorAllocator(16));
                EntityId textureId = texture.GetEntityId();

                builder.Build(sceneData, VividMeshletRendererDatabase.instance, bindlessTextureContainer, out _);
                Object.DestroyImmediate(texture);
                texture = null;

                bindlessTextureContainer.TextureContainer.MarkTextureDestroyed(textureId);
                bindlessTextureContainer.PrepareFrame();

                bool staticDataChanged = builder.Build(
                    sceneData,
                    VividMeshletRendererDatabase.instance,
                    bindlessTextureContainer,
                    out bool materialDataChanged
                );

                Assert.That(staticDataChanged, Is.False);
                Assert.That(materialDataChanged, Is.True);
                Assert.That(sceneData.SurfaceBindingCount, Is.EqualTo(1));
                Assert.That(sceneData.Materials[0].SurfaceBindingIndex, Is.EqualTo(0u));
                Assert.That(sceneData.SurfaceBindings[0].BaseColorResource, Is.EqualTo(VividSurfaceBindingData.InvalidResource));
                Assert.That(sceneData.SurfaceBindings[0].NormalResource, Is.EqualTo(VividSurfaceBindingData.InvalidResource));
                Assert.That(sceneData.SurfaceBindings[0].MaskResource, Is.EqualTo(VividSurfaceBindingData.InvalidResource));
                Assert.That(sceneData.SurfaceBindings[0].Flags & VividSurfaceBindingFlags.BaseColor, Is.EqualTo(VividSurfaceBindingFlags.None));
                Assert.That(sceneData.SurfaceBindings[0].UVScaleBias, Is.EqualTo(new float4(1.0f, 1.0f, 0.0f, 0.0f)));
            }
            finally
            {
                DestroyTestObjects(gameObject, null, material, mesh, meshletCollection, materialProxy, texture);
            }
        }

        [Test]
        public void Build_RebuildsStaticMeshletData_WhenTrackedMeshletAssetChanges()
        {
            GameObject gameObject = null;
            Mesh mesh = null;
            Material material = null;
            VividMeshletCollectionAsset meshletCollection = null;
            GPUDrivenMaterialProxy materialProxy = null;

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
                materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
                materialProxy.SourceMaterial = material;

                gameObject = CreateMeshletRendererObject("Renderer_TrackedAsset", mesh, new[] { material }, out MeshletRenderer meshletRenderer);
                meshletRenderer.SetMeshletCollections(new[] { meshletCollection });
                meshletRenderer.SetMaterialProxies(new[] { materialProxy });
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);

                var sceneData = new VividGPUDrivenSceneData();
                var builder = new VividGPUDrivenSceneDataBuilder();
                using var bindlessTextureContainer = new BindlessGPUDrivenTextureBackend(new FakeBindlessTextureDescriptorAllocator(16));

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
                DestroyTestObjects(gameObject, null, material, mesh, meshletCollection, materialProxy);
            }
        }

        [Test]
        public void Build_SkipsSceneRebuild_WhenUntrackedMeshletAssetChanges()
        {
            GameObject gameObject = null;
            Mesh mesh = null;
            Material material = null;
            VividMeshletCollectionAsset trackedMeshletCollection = null;
            VividMeshletCollectionAsset untrackedMeshletCollection = null;
            GPUDrivenMaterialProxy materialProxy = null;

            try
            {
                mesh = CreateSingleSubMeshMesh("UntrackedAssetChangeMesh");
                material = CreateTestMaterial();
                trackedMeshletCollection = CreateMeshletCollectionAsset(
                    "TrackedCollection",
                    0,
                    1,
                    new[] { CreateMeshLODNode(0, 1, 0) },
                    new[] { CreateMeshlet(0, 0, 3, 1) },
                    new[] { CreateVertex(0.0f, 0.0f, 0.0f), CreateVertex(1.0f, 0.0f, 0.0f), CreateVertex(0.0f, 1.0f, 0.0f) },
                    new byte[] { 0, 1, 2 }
                );
                materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
                materialProxy.SourceMaterial = material;

                gameObject = CreateMeshletRendererObject("Renderer_UntrackedAssetChange", mesh, new[] { material }, out MeshletRenderer meshletRenderer);
                meshletRenderer.SetMeshletCollections(new[] { trackedMeshletCollection });
                meshletRenderer.SetMaterialProxies(new[] { materialProxy });
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);

                var sceneData = new VividGPUDrivenSceneData();
                var builder = new VividGPUDrivenSceneDataBuilder();
                using var bindlessTextureContainer = new BindlessGPUDrivenTextureBackend(new FakeBindlessTextureDescriptorAllocator(16));

                builder.Build(sceneData, VividMeshletRendererDatabase.instance, bindlessTextureContainer);
                VividInstanceData retainedInstanceData = sceneData.MutableInstances[0];
                retainedInstanceData.Padding0 = 123u;
                sceneData.MutableInstances[0] = retainedInstanceData;

                untrackedMeshletCollection = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();
                untrackedMeshletCollection.MarkChanged();

                bool staticDataChanged = builder.Build(
                    sceneData,
                    VividMeshletRendererDatabase.instance,
                    bindlessTextureContainer,
                    out bool materialDataChanged,
                    out bool instanceDataChanged
                );

                Assert.That(staticDataChanged, Is.False);
                Assert.That(materialDataChanged, Is.False);
                Assert.That(instanceDataChanged, Is.False);
                Assert.That(sceneData.Instances[0].Padding0, Is.EqualTo(123u));
            }
            finally
            {
                DestroyTestObjects(
                    gameObject,
                    null,
                    material,
                    mesh,
                    trackedMeshletCollection,
                    untrackedMeshletCollection,
                    materialProxy);
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
                using var bindlessTextureContainer = new BindlessGPUDrivenTextureBackend(new FakeBindlessTextureDescriptorAllocator(16));

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
                Assert.That(sceneData.MaxMeshletListBuildJobCount, Is.EqualTo(2));
                Assert.That(sceneData.MaxVisibleMeshletRenderRequestCount, Is.EqualTo(3));
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
                using var bindlessTextureContainer = new BindlessGPUDrivenTextureBackend(new FakeBindlessTextureDescriptorAllocator(16));

                builder.Build(sceneData, VividMeshletRendererDatabase.instance, bindlessTextureContainer);

                Assert.That(sceneData.MaterialCount, Is.EqualTo(1));
                Assert.That(sceneData.SurfaceBindingCount, Is.EqualTo(1));
                VividMaterialData materialData = sceneData.Materials[0];
                VividSurfaceBindingData surfaceBindingData = sceneData.SurfaceBindings[(int) materialData.SurfaceBindingIndex];
                Assert.That(materialData.AlbedoColor.x, Is.EqualTo(0.8f).Within(0.0001f));
                Assert.That(materialData.TextureTilingOffset.y, Is.EqualTo(5.0f).Within(0.0001f));
                Assert.That(surfaceBindingData.Flags & VividSurfaceBindingFlags.BaseColor, Is.Not.EqualTo(VividSurfaceBindingFlags.None));
                Assert.That(surfaceBindingData.Flags & VividSurfaceBindingFlags.Normal, Is.Not.EqualTo(VividSurfaceBindingFlags.None));
                Assert.That(surfaceBindingData.Flags & VividSurfaceBindingFlags.Mask, Is.EqualTo(VividSurfaceBindingFlags.None));
                Assert.That(surfaceBindingData.BaseColorResource, Is.EqualTo(15u));
                Assert.That(surfaceBindingData.NormalResource, Is.EqualTo(14u));
                Assert.That(surfaceBindingData.MaskResource, Is.EqualTo(VividSurfaceBindingData.InvalidResource));
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
                using var bindlessTextureContainer = new BindlessGPUDrivenTextureBackend(new FakeBindlessTextureDescriptorAllocator(16));

                builder.Build(sceneData, VividMeshletRendererDatabase.instance, bindlessTextureContainer);

                Assert.That(gameObject.GetComponent<MeshRenderer>(), Is.Null);
                Assert.That(sceneData.InstanceCount, Is.EqualTo(1));
                Assert.That(sceneData.MaterialCount, Is.EqualTo(1));
                Assert.That(sceneData.Materials[0].AlbedoColor.x, Is.EqualTo(syncedProxy.BaseColor.r).Within(0.0001f));
                VividSurfaceBindingData surfaceBindingData = sceneData.SurfaceBindings[(int) sceneData.Materials[0].SurfaceBindingIndex];
                Assert.That(surfaceBindingData.Flags & VividSurfaceBindingFlags.BaseColor, Is.Not.EqualTo(VividSurfaceBindingFlags.None));
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
                Assert.That(system.BufferSet.SurfaceBindingCount, Is.EqualTo(1));
                Assert.That(system.BufferSet.MeshLODNodeCount, Is.EqualTo(1));
                Assert.That(system.BufferSet.MeshletCount, Is.EqualTo(1));
                Assert.That(system.BufferSet.SharedVertexCount, Is.EqualTo(3));
                Assert.That(system.BufferSet.SharedIndexCount, Is.EqualTo(3));
                VividMaterialData materialData = system.SceneData.Materials[0];
                VividSurfaceBindingData surfaceBindingData = system.SceneData.SurfaceBindings[(int) materialData.SurfaceBindingIndex];
                Assert.That(materialData.AlbedoColor.x, Is.EqualTo(0.25f).Within(0.0001f));
                Assert.That(materialData.AlbedoColor.y, Is.EqualTo(0.5f).Within(0.0001f));
                Assert.That(materialData.AlbedoColor.z, Is.EqualTo(0.75f).Within(0.0001f));
                Assert.That(materialData.TextureTilingOffset.x, Is.EqualTo(2.0f).Within(0.0001f));
                Assert.That(materialData.TextureTilingOffset.y, Is.EqualTo(3.0f).Within(0.0001f));
                Assert.That(materialData.TextureTilingOffset.z, Is.EqualTo(0.1f).Within(0.0001f));
                Assert.That(materialData.TextureTilingOffset.w, Is.EqualTo(0.2f).Within(0.0001f));
                Assert.That(surfaceBindingData.Flags, Is.EqualTo(
                    VividSurfaceBindingFlags.BaseColor | VividSurfaceBindingFlags.Normal | VividSurfaceBindingFlags.Mask));
                Assert.That(surfaceBindingData.BaseColorResource, Is.EqualTo(15u));
                Assert.That(surfaceBindingData.NormalResource, Is.EqualTo(14u));
                Assert.That(surfaceBindingData.MaskResource, Is.EqualTo(13u));
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

        [Test]
        public void Build_AppendsTerrainChunksWithSharedFirstLayerSurfaceBinding()
        {
            GameObject gameObject = null;
            VividTerrainData terrainData = null;
            VividMeshletCollectionAsset firstCollection = null;
            VividMeshletCollectionAsset secondCollection = null;
            Texture2D baseMap = null;
            Texture2D normalMap = null;
            Texture2D maskMap = null;

            try
            {
                firstCollection = CreateMeshletCollectionAsset(
                    "TerrainChunk0",
                    0,
                    1,
                    new[] { CreateMeshLODNode(0, 1, 0) },
                    new[] { CreateMeshlet(0, 0, 3, 1) },
                    new[] { CreateVertex(0.0f, 0.0f, 0.0f), CreateVertex(1.0f, 0.0f, 0.0f), CreateVertex(0.0f, 1.0f, 0.0f) },
                    new byte[] { 0, 1, 2 }
                );
                secondCollection = CreateMeshletCollectionAsset(
                    "TerrainChunk1",
                    0,
                    1,
                    new[] { CreateMeshLODNode(0, 1, 0) },
                    new[] { CreateMeshlet(0, 0, 3, 1) },
                    new[] { CreateVertex(8.0f, 0.0f, 0.0f), CreateVertex(9.0f, 0.0f, 0.0f), CreateVertex(8.0f, 1.0f, 0.0f) },
                    new byte[] { 0, 1, 2 }
                );

                baseMap = new Texture2D(4, 4) { name = "Terrain Base" };
                normalMap = new Texture2D(4, 4) { name = "Terrain Normal" };
                maskMap = new Texture2D(4, 4) { name = "Terrain Mask" };
                var firstLayer = new VividTerrainLayerData(
                    baseMap,
                    normalMap,
                    maskMap,
                    new Vector2(4.0f, 8.0f),
                    new Vector2(1.0f, 2.0f),
                    Color.white,
                    0.35f,
                    0.75f,
                    0.6f
                );
                var firstBounds = new Bounds(new Vector3(4.0f, 1.0f, 16.0f), new Vector3(8.0f, 2.0f, 32.0f));
                var secondBounds = new Bounds(new Vector3(12.0f, 1.0f, 16.0f), new Vector3(8.0f, 2.0f, 32.0f));
                terrainData = ScriptableObject.CreateInstance<VividTerrainData>();
                terrainData.Initialize(
                    string.Empty,
                    "SceneBuilderTerrain",
                    33,
                    new Vector3(16.0f, 4.0f, 32.0f),
                    new Bounds(new Vector3(8.0f, 1.0f, 16.0f), new Vector3(16.0f, 2.0f, 32.0f)),
                    new Vector2Int(2, 1),
                    VividTerrainBakeSettings.Default,
                    null,
                    new[] { firstLayer },
                    new[]
                    {
                        new VividTerrainChunkData(Vector2Int.zero, Vector2Int.zero, new Vector2Int(16, 32), firstBounds, firstCollection),
                        new VividTerrainChunkData(new Vector2Int(1, 0), new Vector2Int(16, 0), new Vector2Int(32, 32), secondBounds, secondCollection),
                    }
                );

                gameObject = new GameObject("Terrain Scene Builder");
                VividTerrain terrain = gameObject.AddComponent<VividTerrain>();
                terrain.SetData(terrainData);

                var sceneData = new VividGPUDrivenSceneData();
                var builder = new VividGPUDrivenSceneDataBuilder();
                using var textureBackend = new BindlessGPUDrivenTextureBackend(new FakeBindlessTextureDescriptorAllocator(16));
                bool staticDataChanged = builder.Build(
                    sceneData,
                    VividMeshletRendererDatabase.instance,
                    textureBackend,
                    out bool materialDataChanged,
                    out bool instanceDataChanged
                );

                Assert.That(staticDataChanged, Is.True);
                Assert.That(materialDataChanged, Is.True);
                Assert.That(instanceDataChanged, Is.True);
                Assert.That(sceneData.InstanceCount, Is.EqualTo(2));
                Assert.That(sceneData.MaterialCount, Is.EqualTo(1));
                Assert.That(sceneData.SurfaceBindingCount, Is.EqualTo(1));
                Assert.That(sceneData.MeshletCount, Is.EqualTo(2));
                Assert.That(sceneData.Instances[0].MaterialIndex, Is.EqualTo(sceneData.Instances[1].MaterialIndex));
                Assert.That(sceneData.Instances[0].AABBMin.x, Is.EqualTo(firstBounds.min.x).Within(0.0001f));
                Assert.That(sceneData.Instances[1].AABBMin.x, Is.EqualTo(secondBounds.min.x).Within(0.0001f));
                Assert.That(sceneData.Instances[0].PassMask,
                    Is.EqualTo(VividInstancePassMask.Main | VividInstancePassMask.Shadows));

                VividMaterialData materialData = sceneData.Materials[0];
                Assert.That(materialData.TextureTilingOffset.x, Is.EqualTo(4.0f).Within(0.0001f));
                Assert.That(materialData.TextureTilingOffset.y, Is.EqualTo(4.0f).Within(0.0001f));
                Assert.That(materialData.TextureTilingOffset.z, Is.EqualTo(-0.25f).Within(0.0001f));
                Assert.That(materialData.TextureTilingOffset.w, Is.EqualTo(-0.25f).Within(0.0001f));
                Assert.That(materialData.NormalsStrength, Is.EqualTo(0.6f).Within(0.0001f));
                Assert.That(materialData.Metallic, Is.EqualTo(0.35f).Within(0.0001f));
                Assert.That(materialData.Roughness, Is.EqualTo(0.25f).Within(0.0001f));
                Assert.That(materialData.Padding0,
                    Is.EqualTo((uint) GPUDrivenMaterialMaskMode.PackedMetallicOcclusionSmoothness));

                VividSurfaceBindingData binding = sceneData.SurfaceBindings[(int) materialData.SurfaceBindingIndex];
                Assert.That(binding.Flags, Is.EqualTo(
                    VividSurfaceBindingFlags.BaseColor | VividSurfaceBindingFlags.Normal | VividSurfaceBindingFlags.Mask));
                Assert.That(binding.BaseColorResource, Is.Not.EqualTo(VividSurfaceBindingData.InvalidResource));
                Assert.That(binding.NormalResource, Is.Not.EqualTo(VividSurfaceBindingData.InvalidResource));
                Assert.That(binding.MaskResource, Is.Not.EqualTo(VividSurfaceBindingData.InvalidResource));

                staticDataChanged = builder.Build(
                    sceneData,
                    VividMeshletRendererDatabase.instance,
                    textureBackend,
                    out materialDataChanged,
                    out instanceDataChanged
                );
                Assert.That(staticDataChanged, Is.False);
                Assert.That(materialDataChanged, Is.False);
                Assert.That(instanceDataChanged, Is.False);
                Assert.That(sceneData.InstanceCount, Is.EqualTo(2));
                Assert.That(sceneData.MaterialCount, Is.EqualTo(1));
                Assert.That(sceneData.SurfaceBindingCount, Is.EqualTo(1));
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

                if (firstCollection != null)
                {
                    Object.DestroyImmediate(firstCollection);
                }

                if (secondCollection != null)
                {
                    Object.DestroyImmediate(secondCollection);
                }

                if (baseMap != null)
                {
                    Object.DestroyImmediate(baseMap);
                }

                if (normalMap != null)
                {
                    Object.DestroyImmediate(normalMap);
                }

                if (maskMap != null)
                {
                    Object.DestroyImmediate(maskMap);
                }
            }
        }

        [Test]
        public void Build_AppendsTerrainLayerAndControlBindingsForMultiLayerTerrain()
        {
            GameObject gameObject = null;
            VividTerrainData terrainData = null;
            VividVirtualTextureAsset compositeVirtualTexture = null;
            VividVirtualTextureBuiltData compositeBuiltData = null;
            VividMeshletCollectionAsset meshletCollection = null;
            var textures = new List<Texture2D>();

            try
            {
                meshletCollection = CreateMeshletCollectionAsset(
                    "TerrainControlBlendChunk",
                    0,
                    1,
                    new[] { CreateMeshLODNode(0, 1, 0) },
                    new[] { CreateMeshlet(0, 0, 3, 1) },
                    new[]
                    {
                        CreateVertex(0.0f, 0.0f, 0.0f),
                        CreateVertex(1.0f, 0.0f, 0.0f),
                        CreateVertex(0.0f, 1.0f, 0.0f),
                    },
                    new byte[] { 0, 1, 2 }
                );

                Texture2D baseMap0 = CreateTerrainTexture("Terrain Base 0", textures);
                Texture2D normalMap0 = CreateTerrainTexture("Terrain Normal 0", textures);
                Texture2D maskMap0 = CreateTerrainTexture("Terrain Mask 0", textures);
                Texture2D baseMap1 = CreateTerrainTexture("Terrain Base 1", textures);
                Texture2D normalMap1 = CreateTerrainTexture("Terrain Normal 1", textures);
                Texture2D controlMap = CreateTerrainTexture("Terrain Control 0", textures);
                controlMap.wrapMode = TextureWrapMode.Clamp;

                var layers = new[]
                {
                    new VividTerrainLayerData(
                        baseMap0,
                        normalMap0,
                        maskMap0,
                        new Vector2(4.0f, 8.0f),
                        Vector2.zero,
                        Color.white,
                        0.25f,
                        0.7f,
                        0.5f),
                    new VividTerrainLayerData(
                        baseMap1,
                        normalMap1,
                        null,
                        new Vector2(8.0f, 16.0f),
                        new Vector2(2.0f, 4.0f),
                        Color.white,
                        0.6f,
                        0.2f,
                        0.8f),
                };
                var chunkBounds = new Bounds(
                    new Vector3(8.0f, 2.0f, 16.0f),
                    new Vector3(16.0f, 4.0f, 32.0f));
                terrainData = ScriptableObject.CreateInstance<VividTerrainData>();
                terrainData.Initialize(
                    string.Empty,
                    "TerrainControlBlend",
                    33,
                    new Vector3(16.0f, 4.0f, 32.0f),
                    chunkBounds,
                    Vector2Int.one,
                    VividTerrainBakeSettings.Default,
                    null,
                    layers,
                    new[]
                    {
                        new VividTerrainChunkData(
                            Vector2Int.zero,
                            Vector2Int.zero,
                            new Vector2Int(32, 32),
                            chunkBounds,
                            meshletCollection),
                    },
                    new[] { controlMap }
                );
                compositeVirtualTexture = ScriptableObject.CreateInstance<VividVirtualTextureAsset>();
                compositeVirtualTexture.name = "Terrain Composite Surface";
                compositeBuiltData = ScriptableObject.CreateInstance<VividVirtualTextureBuiltData>();
                InitializeCompositeBuiltData(compositeBuiltData, contentVersion: 1u);
                compositeVirtualTexture.Initialize(compositeBuiltData);
                terrainData.SetCompositeVirtualTexture(compositeVirtualTexture);

                gameObject = new GameObject("Terrain Control Blend Scene Builder");
                VividTerrain terrain = gameObject.AddComponent<VividTerrain>();
                terrain.SetData(terrainData);

                var sceneData = new VividGPUDrivenSceneData();
                var builder = new VividGPUDrivenSceneDataBuilder();
                using var textureBackend = new BindlessGPUDrivenTextureBackend(
                    new FakeBindlessTextureDescriptorAllocator(16));

                builder.Build(sceneData, VividMeshletRendererDatabase.instance, textureBackend);

                Assert.That(sceneData.MaterialCount, Is.EqualTo(1));
                Assert.That(sceneData.SurfaceBindingCount, Is.EqualTo(3));
                Assert.That(sceneData.TerrainMaterialCount, Is.EqualTo(1));
                Assert.That(sceneData.TerrainLayerCount, Is.EqualTo(2));

                VividMaterialData materialData = sceneData.Materials[0];
                Assert.That(
                    materialData.MaterialFlags & VividMaterialFlags.Terrain,
                    Is.EqualTo(VividMaterialFlags.Terrain));
                Assert.That(materialData.SurfaceBindingIndex, Is.EqualTo(0u));
                Assert.That(materialData.Padding1, Is.EqualTo(0u));

                VividTerrainMaterialData terrainMaterialData = sceneData.TerrainMaterials[0];
                Assert.That(terrainMaterialData.LayerStartIndex, Is.EqualTo(0u));
                Assert.That(terrainMaterialData.LayerCount, Is.EqualTo(2u));
                Assert.That(terrainMaterialData.ControlBindingIndex0, Is.EqualTo(2u));
                Assert.That(
                    terrainMaterialData.ControlBindingIndex1,
                    Is.EqualTo(VividSurfaceBindingData.InvalidResource));

                Assert.That(sceneData.TerrainLayers[0].SurfaceBindingIndex, Is.EqualTo(0u));
                Assert.That(sceneData.TerrainLayers[1].SurfaceBindingIndex, Is.EqualTo(1u));
                Assert.That(sceneData.TerrainLayers[1].TextureTilingOffset.x, Is.EqualTo(2.0f).Within(0.0001f));
                Assert.That(sceneData.TerrainLayers[1].TextureTilingOffset.y, Is.EqualTo(2.0f).Within(0.0001f));
                Assert.That(sceneData.TerrainLayers[1].TextureTilingOffset.z, Is.EqualTo(-0.25f).Within(0.0001f));
                Assert.That(sceneData.TerrainLayers[1].TextureTilingOffset.w, Is.EqualTo(-0.25f).Within(0.0001f));

                VividSurfaceBindingData controlBinding = sceneData.SurfaceBindings[2];
                Assert.That(controlBinding.Flags, Is.EqualTo(VividSurfaceBindingFlags.Mask));
                Assert.That(controlBinding.BaseColorResource, Is.EqualTo(VividSurfaceBindingData.InvalidResource));
                Assert.That(controlBinding.NormalResource, Is.EqualTo(VividSurfaceBindingData.InvalidResource));
                Assert.That(controlBinding.MaskResource, Is.Not.EqualTo(VividSurfaceBindingData.InvalidResource));
                Assert.That(controlBinding.UVScaleBias.x, Is.LessThan(0.0f));

                var compositeSceneData = new VividGPUDrivenSceneData();
                var compositeBuilder = new VividGPUDrivenSceneDataBuilder();
                using var compositeBackend = new CompositeCapableTextureBackend(compositeVirtualTexture);
                compositeBuilder.Build(
                    compositeSceneData,
                    VividMeshletRendererDatabase.instance,
                    compositeBackend);

                Assert.That(compositeSceneData.MaterialCount, Is.EqualTo(1));
                Assert.That(compositeSceneData.SurfaceBindingCount, Is.EqualTo(1));
                Assert.That(compositeSceneData.TerrainMaterialCount, Is.EqualTo(0));
                Assert.That(compositeSceneData.TerrainLayerCount, Is.EqualTo(0));
                VividMaterialData compositeMaterial = compositeSceneData.Materials[0];
                Assert.That(
                    compositeMaterial.MaterialFlags & VividMaterialFlags.Terrain,
                    Is.EqualTo(VividMaterialFlags.None));
                Assert.That(compositeMaterial.TextureTilingOffset, Is.EqualTo(new float4(1.0f, 1.0f, 0.0f, 0.0f)));
                Assert.That(compositeMaterial.NormalsStrength, Is.EqualTo(1.0f));
                Assert.That(
                    compositeMaterial.Padding0,
                    Is.EqualTo((uint) GPUDrivenMaterialMaskMode.PackedMetallicOcclusionSmoothness));

                var terrainRVTSceneData = new VividGPUDrivenSceneData();
                var terrainRVTBuilder = new VividGPUDrivenSceneDataBuilder();
                using var terrainRVTBackend = new CompositeCapableTextureBackend(
                    compositeVirtualTexture,
                    terrainRVTRecordIndex: 17u);
                terrainRVTBuilder.Build(
                    terrainRVTSceneData,
                    VividMeshletRendererDatabase.instance,
                    terrainRVTBackend);

                Assert.That(terrainRVTSceneData.SurfaceBindingCount, Is.EqualTo(1));
                Assert.That(terrainRVTSceneData.TerrainMaterialCount, Is.Zero);
                Assert.That(terrainRVTSceneData.TerrainLayerCount, Is.Zero);
                VividMaterialData terrainRVTMaterial = terrainRVTSceneData.Materials[0];
                Assert.That(
                    terrainRVTMaterial.MaterialFlags & VividMaterialFlags.TerrainRuntimeVirtualTexture,
                    Is.EqualTo(VividMaterialFlags.TerrainRuntimeVirtualTexture));
                Assert.That(
                    terrainRVTMaterial.MaterialFlags & VividMaterialFlags.Terrain,
                    Is.EqualTo(VividMaterialFlags.None));
                Assert.That(terrainRVTMaterial.Padding1, Is.EqualTo(17u));

                compositeBuilder.Build(
                    compositeSceneData,
                    VividMeshletRendererDatabase.instance,
                    compositeBackend,
                    out bool unchangedMaterialData);
                Assert.That(unchangedMaterialData, Is.False);

                InitializeCompositeBuiltData(compositeBuiltData, contentVersion: 2u);
                compositeBuilder.Build(
                    compositeSceneData,
                    VividMeshletRendererDatabase.instance,
                    compositeBackend,
                    out bool changedMaterialData);
                Assert.That(changedMaterialData, Is.True);
            }
            finally
            {
                if (gameObject != null)
                    Object.DestroyImmediate(gameObject);
                if (terrainData != null)
                    Object.DestroyImmediate(terrainData);
                if (compositeVirtualTexture != null)
                    Object.DestroyImmediate(compositeVirtualTexture);
                if (compositeBuiltData != null)
                    Object.DestroyImmediate(compositeBuiltData);
                if (meshletCollection != null)
                    Object.DestroyImmediate(meshletCollection);
                for (int textureIndex = 0; textureIndex < textures.Count; textureIndex++)
                {
                    if (textures[textureIndex] != null)
                        Object.DestroyImmediate(textures[textureIndex]);
                }
            }
        }

        private static void InitializeCompositeBuiltData(
            VividVirtualTextureBuiltData builtData,
            uint contentVersion)
        {
            builtData.Initialize(
                string.Empty,
                string.Empty,
                pageSize: 128,
                borderSize: 4,
                virtualPageCountX: 1,
                virtualPageCountY: 1,
                mipCount: 1,
                Array.Empty<VividVirtualTextureLayerDescriptor>(),
                Array.Empty<VividVirtualTextureChunkDescriptor>(),
                Array.Empty<VividVirtualTextureTileDescriptor>(),
                new[] { 0 },
                new byte[] { 0 },
                buildProfile: VividVirtualTextureBuildProfile.GPUDrivenSurface,
                contentLayerMask: 7,
                contentVersion: contentVersion,
                addressMode: VividVirtualTextureAddressMode.Clamp);
        }

        private sealed class CompositeCapableTextureBackend :
            IGPUDrivenTextureBackend,
            IGPUDrivenTerrainRuntimeVirtualTextureBackend
        {
            private readonly VividVirtualTextureAsset m_CompositeAsset;
            private readonly uint m_TerrainRVTRecordIndex;

            internal CompositeCapableTextureBackend(
                VividVirtualTextureAsset compositeAsset,
                uint terrainRVTRecordIndex = VividSurfaceBindingData.InvalidResource)
            {
                m_CompositeAsset = compositeAsset;
                m_TerrainRVTRecordIndex = terrainRVTRecordIndex;
            }

            public string DisplayName => "Composite Test";

            public bool IsAvailable => true;

            public string UnavailableReason => string.Empty;

            public uint BindingRevision => 0u;

            public bool TerrainRuntimeVirtualTextureEnabled =>
                m_TerrainRVTRecordIndex != VividSurfaceBindingData.InvalidResource;

            public void PrepareFrame()
            {
            }

            public void ResetPerFrameStats()
            {
            }

            public bool CanUseStreamedVirtualTexture(VividVirtualTextureAsset asset)
            {
                return asset == m_CompositeAsset;
            }

            public bool TryGetOrCreateTerrainRuntimeVirtualTexture(
                VividTerrain terrain,
                VividTerrainData terrainData,
                uint revision,
                out uint recordIndex)
            {
                recordIndex = m_TerrainRVTRecordIndex;
                return TerrainRuntimeVirtualTextureEnabled;
            }

            public void UpdateTerrainRuntimeVirtualTextures(Camera renderingCamera, int frameIndex)
            {
            }

            public void BindTerrainRuntimeVirtualTextureGlobals(CommandBuffer cmd)
            {
            }

            public VividSurfaceBindingData CreateSurfaceBinding(in GPUDrivenSurfaceTextureSet textures)
            {
                bool composite = textures.StreamedVirtualTexture == m_CompositeAsset;
                return new VividSurfaceBindingData
                {
                    BaseColorResource = composite ? 1u : VividSurfaceBindingData.InvalidResource,
                    NormalResource = composite ? 2u : VividSurfaceBindingData.InvalidResource,
                    MaskResource = composite ? 3u : VividSurfaceBindingData.InvalidResource,
                    Flags = composite
                        ? VividSurfaceBindingFlags.BaseColor
                          | VividSurfaceBindingFlags.Normal
                          | VividSurfaceBindingFlags.Mask
                        : VividSurfaceBindingFlags.None,
                    UVScaleBias = new float4(-1.0f, -1.0f, 0.0f, 0.0f),
                };
            }

            public GPUDrivenTextureBackendStats GetStats()
            {
                return default;
            }

            public void Dispose()
            {
            }
        }

        [Test]
        public void Build_RejectsTerrainWhenChunkChangesToUnsupportedLODCount()
        {
            GameObject gameObject = null;
            VividTerrainData terrainData = null;
            VividMeshletCollectionAsset meshletCollection = null;

            try
            {
                meshletCollection = CreateMeshletCollectionAsset(
                    "TerrainChunk",
                    0,
                    VividTerrainData.MinimumChunkLODCount,
                    new[] { CreateMeshLODNode(0, 1, 0) },
                    new[] { CreateMeshlet(0, 0, 3, 1) },
                    new[]
                    {
                        CreateVertex(0.0f, 0.0f, 0.0f),
                        CreateVertex(1.0f, 0.0f, 0.0f),
                        CreateVertex(0.0f, 1.0f, 0.0f),
                    },
                    new byte[] { 0, 1, 2 }
                );
                var chunkBounds = new Bounds(
                    new Vector3(8.0f, 2.0f, 8.0f),
                    new Vector3(16.0f, 4.0f, 16.0f)
                );
                terrainData = ScriptableObject.CreateInstance<VividTerrainData>();
                terrainData.Initialize(
                    string.Empty,
                    "RuntimeLODMutation",
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

                gameObject = new GameObject("Terrain Unsupported LOD");
                VividTerrain terrain = gameObject.AddComponent<VividTerrain>();
                terrain.SetData(terrainData);
                Assert.That(
                    VividMeshletRendererDatabase.instance.TryGetTerrainData(terrain, out var trackedData),
                    Is.True
                );
                Assert.That((trackedData.flags & VividMeshletRendererFlags.Valid) != 0, Is.True);

                meshletCollection.MeshLODLevelCount = terrainData.BakeSettings.MaxMeshLODLevelCount + 1;

                var sceneData = new VividGPUDrivenSceneData();
                var builder = new VividGPUDrivenSceneDataBuilder();
                using var textureBackend = new BindlessGPUDrivenTextureBackend(
                    new FakeBindlessTextureDescriptorAllocator(16)
                );
                builder.Build(sceneData, VividMeshletRendererDatabase.instance, textureBackend);

                Assert.That(terrainData.TryValidate(out string reason), Is.False);
                Assert.That(reason, Does.Contain("exceeding its baked limit"));
                Assert.That(sceneData.InstanceCount, Is.Zero);
                Assert.That(sceneData.MeshletCount, Is.Zero);
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

        [Test]
        public void Build_AppendsTerrainChunkLODHierarchyForGPUSelection()
        {
            GameObject gameObject = null;
            VividTerrainData terrainData = null;
            VividMeshletCollectionAsset meshletCollection = null;

            try
            {
                meshletCollection = CreateMeshletCollectionAsset(
                    "TerrainChunkLODHierarchy",
                    0,
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
                        CreateVertex(0.0f, 0.0f, 0.0f),
                        CreateVertex(1.0f, 0.0f, 0.0f),
                        CreateVertex(0.0f, 1.0f, 0.0f),
                        CreateVertex(0.0f, 0.0f, 0.0f),
                        CreateVertex(1.0f, 0.0f, 0.0f),
                        CreateVertex(0.0f, 1.0f, 0.0f),
                    },
                    new byte[] { 0, 1, 2, 0, 1, 2 }
                );
                var chunkBounds = new Bounds(
                    new Vector3(8.0f, 2.0f, 8.0f),
                    new Vector3(16.0f, 4.0f, 16.0f)
                );
                terrainData = ScriptableObject.CreateInstance<VividTerrainData>();
                terrainData.Initialize(
                    string.Empty,
                    "TerrainLODHierarchy",
                    17,
                    new Vector3(16.0f, 4.0f, 16.0f),
                    chunkBounds,
                    Vector2Int.one,
                    new VividTerrainBakeSettings(
                        1,
                        16,
                        optimizeVertexCache: true,
                        maxMeshLODLevelCount: 2
                    ),
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

                gameObject = new GameObject("Terrain LOD Hierarchy");
                VividTerrain terrain = gameObject.AddComponent<VividTerrain>();
                terrain.SetData(terrainData);

                var sceneData = new VividGPUDrivenSceneData();
                var builder = new VividGPUDrivenSceneDataBuilder();
                using var textureBackend = new BindlessGPUDrivenTextureBackend(
                    new FakeBindlessTextureDescriptorAllocator(16)
                );
                builder.Build(sceneData, VividMeshletRendererDatabase.instance, textureBackend);

                Assert.That(terrainData.IsValid, Is.True);
                Assert.That(terrainData.GeometryChunkLODRange, Is.EqualTo(new Vector2Int(2, 2)));
                Assert.That(sceneData.InstanceCount, Is.EqualTo(1));
                Assert.That(sceneData.MeshLODNodeCount, Is.EqualTo(2));
                Assert.That(sceneData.MeshletCount, Is.EqualTo(2));
                Assert.That(sceneData.Instances[0].MeshLODLevelCount, Is.EqualTo(2u));
                Assert.That(sceneData.Instances[0].TotalMeshLODCount, Is.EqualTo(2u));
                Assert.That(sceneData.MeshLODNodes[0].LevelIndex, Is.EqualTo(0u));
                Assert.That(sceneData.MeshLODNodes[1].LevelIndex, Is.EqualTo(1u));
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

        [Test]
        public void PrepareFrame_DoesNotAllocate_WhenFallbackMaterialSceneIsStable()
        {
            GameObject gameObject = null;
            Mesh mesh = null;
            Material material = null;
            VividMeshletCollectionAsset meshletCollection = null;

            try
            {
                mesh = CreateSingleSubMeshMesh("StablePrepareFrameMesh");
                material = CreateTestMaterial();
                meshletCollection = CreateMeshletCollectionAsset(
                    "StablePrepareFrameCollection",
                    0,
                    1,
                    new[] { CreateMeshLODNode(0, 1, 0) },
                    new[] { CreateMeshlet(0, 0, 3, 1) },
                    new[] { CreateVertex(0.0f, 0.0f, 0.0f), CreateVertex(1.0f, 0.0f, 0.0f), CreateVertex(0.0f, 1.0f, 0.0f) },
                    new byte[] { 0, 1, 2 }
                );

                gameObject = CreateMeshletRendererObject("Renderer_StablePrepareFrame", mesh, new[] { material }, out MeshletRenderer meshletRenderer);
                meshletRenderer.SetMeshletCollections(new[] { meshletCollection });
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);

                using var system = new VividGPUDrivenSystem(new FakeBindlessTextureDescriptorAllocator(16));
                system.PrepareFrame(reportStats: false);
                system.PrepareFrame(reportStats: false);

                var allocatedBefore = global::System.GC.GetAllocatedBytesForCurrentThread();
                for (int index = 0; index < 32; index++)
                {
                    system.PrepareFrame(reportStats: false);
                }

                var allocatedBytes = global::System.GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                Assert.That(allocatedBytes, Is.Zero);
            }
            finally
            {
                DestroyTestObjects(gameObject, null, material, mesh, meshletCollection);
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

        private static Texture2D CreateTerrainTexture(string name, List<Texture2D> textures)
        {
            var texture = new Texture2D(4, 4)
            {
                name = name,
            };
            textures.Add(texture);
            return texture;
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
            return VividMeshletVertexPacking.Pack(
                new float3(x, y, z),
                new float3(0.0f, 0.0f, 1.0f),
                new float4(1.0f, 0.0f, 0.0f, 1.0f),
                new float2(x, y)
            );
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

            public ulong CompletedFrameFenceValue => 0ul;

            public ulong PendingFrameFenceValue => 1ul;

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
