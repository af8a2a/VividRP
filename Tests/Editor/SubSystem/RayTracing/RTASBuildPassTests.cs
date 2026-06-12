using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Meshlets;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class RTASBuildPassTests
    {
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
        public void ResolveSettings_UsesDescriptorDefaults_WhenVolumeHasNoOverrides()
        {
            var descriptor = RenderGraphAccelerationStructureDesc.Create("SceneRTAS");
            descriptor.LayerMask = 1 << 7;
            descriptor.RayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.DynamicGeometry;
            descriptor.BuildFlagsStaticGeometries = RayTracingAccelerationStructureBuildFlags.PreferFastTrace;
            descriptor.BuildFlagsDynamicGeometries = RayTracingAccelerationStructureBuildFlags.PreferFastBuild;
            descriptor.EnableCompaction = true;

            var settings = RTASBuildPass.ResolveSettings(null, descriptor);

            Assert.That(settings.BuildMode, Is.EqualTo(VividRTASBuildMode.Automatic));
            Assert.That(settings.CullingMode, Is.EqualTo(VividRTASCullingMode.ExtendedFrustum));
            Assert.That(settings.CullingDistance, Is.EqualTo(RTASBuildPass.DefaultSphereCullingDistance));
            Assert.That(settings.MinSolidAngle, Is.EqualTo(RTASBuildPass.DefaultMinSolidAngle));
            Assert.That((int)settings.LayerMask, Is.EqualTo(1 << 7));
            Assert.That(
                settings.RayTracingModeMask,
                Is.EqualTo(RayTracingAccelerationStructure.RayTracingModeMask.DynamicGeometry));
            Assert.That(
                settings.BuildFlagsStaticGeometries,
                Is.EqualTo(RayTracingAccelerationStructureBuildFlags.PreferFastTrace));
            Assert.That(
                settings.BuildFlagsDynamicGeometries,
                Is.EqualTo(RayTracingAccelerationStructureBuildFlags.PreferFastBuild));
            Assert.That(settings.EnableCompaction, Is.True);
        }

        [Test]
        public void ResolveSettings_UsesVolumeOverrides_WhenOverrideStateEnabled()
        {
            var volume = ScriptableObject.CreateInstance<RayTracingSettingsVolume>();

            try
            {
                volume.active = true;
                volume.rayBias.overrideState = true;
                volume.rayBias.value = 0.01f;
                volume.distantRayBias.overrideState = true;
                volume.distantRayBias.value = 0.1f;
                volume.extendShadowCulling.overrideState = true;
                volume.extendShadowCulling.value = true;
                volume.extendCameraCulling.overrideState = true;
                volume.extendCameraCulling.value = true;
                volume.buildMode.overrideState = true;
                volume.buildMode.value = VividRTASBuildMode.Manual;
                volume.cullingMode.overrideState = true;
                volume.cullingMode.value = VividRTASCullingMode.Sphere;
                volume.cullingDistance.overrideState = true;
                volume.cullingDistance.value = 321f;
                volume.minSolidAngle.overrideState = true;
                volume.minSolidAngle.value = 12f;
                volume.layerMask.overrideState = true;
                volume.layerMask.value = 1 << 4;
                volume.rayTracingModeMask.overrideState = true;
                volume.rayTracingModeMask.value = RayTracingAccelerationStructure.RayTracingModeMask.DynamicGeometry;
                volume.buildFlagsStaticGeometries.overrideState = true;
                volume.buildFlagsStaticGeometries.value = RayTracingAccelerationStructureBuildFlags.PreferFastTrace;
                volume.buildFlagsDynamicGeometries.overrideState = true;
                volume.buildFlagsDynamicGeometries.value = RayTracingAccelerationStructureBuildFlags.PreferFastBuild;
                volume.enableCompaction.overrideState = true;
                volume.enableCompaction.value = true;

                var settings = RTASBuildPass.ResolveSettings(volume);
                var descriptor = RenderGraphAccelerationStructureDesc.Create("SceneRTAS");

                RTASBuildPass.ApplyResolvedSettings(descriptor, in settings);

                Assert.That(settings.BuildMode, Is.EqualTo(VividRTASBuildMode.Manual));
                Assert.That(settings.CullingMode, Is.EqualTo(VividRTASCullingMode.Sphere));
                Assert.That(settings.CullingDistance, Is.EqualTo(321f));
                Assert.That(settings.MinSolidAngle, Is.EqualTo(12f));
                Assert.That(settings.ExtendShadowCulling, Is.True);
                Assert.That(settings.ExtendCameraCulling, Is.True);
                Assert.That(settings.RayBias, Is.EqualTo(0.01f));
                Assert.That(settings.DistantRayBias, Is.EqualTo(0.1f));
                Assert.That((int)settings.LayerMask, Is.EqualTo(1 << 4));
                Assert.That(
                    settings.RayTracingModeMask,
                    Is.EqualTo(RayTracingAccelerationStructure.RayTracingModeMask.DynamicGeometry));
                Assert.That(
                    settings.BuildFlagsStaticGeometries,
                    Is.EqualTo(RayTracingAccelerationStructureBuildFlags.PreferFastTrace));
                Assert.That(
                    settings.BuildFlagsDynamicGeometries,
                    Is.EqualTo(RayTracingAccelerationStructureBuildFlags.PreferFastBuild));
                Assert.That(settings.EnableCompaction, Is.True);
                Assert.That(descriptor.ManagementMode, Is.EqualTo(RayTracingAccelerationStructure.ManagementMode.Manual));
                Assert.That((int)descriptor.LayerMask, Is.EqualTo(1 << 4));
                Assert.That(descriptor.EnableCompaction, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void CreateCullingConfig_UsesSphereCulling_AndVividRenderPipelineFilter()
        {
            var cameraObject = new GameObject("RTAS Sphere Camera");
            var camera = cameraObject.AddComponent<Camera>();

            try
            {
                camera.transform.position = new Vector3(1f, 2f, 3f);

                var settings = new RTASBuildPass.ResolvedRayTracingSettings(
                    VividRTASBuildMode.Automatic,
                    VividRTASCullingMode.Sphere,
                    42f,
                    RTASBuildPass.DefaultMinSolidAngle,
                    false,
                    false,
                    RTASBuildPass.DefaultRayBias,
                    RTASBuildPass.DefaultDistantRayBias,
                    1 << 2,
                    RayTracingAccelerationStructure.RayTracingModeMask.Everything,
                    RayTracingAccelerationStructureBuildFlags.None,
                    RayTracingAccelerationStructureBuildFlags.None,
                    false);

                var cullingConfig = RTASBuildPass.CreateCullingConfig(camera, in settings);

                Assert.That((cullingConfig.flags & RayTracingInstanceCullingFlags.EnableSphereCulling) != 0, Is.True);
                Assert.That(cullingConfig.sphereCenter, Is.EqualTo(camera.transform.position));
                Assert.That(cullingConfig.sphereRadius, Is.EqualTo(42f));
                Assert.That(cullingConfig.instanceTests, Has.Length.EqualTo(1));
                Assert.That(cullingConfig.instanceTests[0].layerMask, Is.EqualTo(1 << 2));
                Assert.That(cullingConfig.instanceTests[0].instanceMask, Is.EqualTo(0xFFu));
                Assert.That(cullingConfig.materialTest.requiredShaderTags, Has.Length.EqualTo(1));
                Assert.That(
                    cullingConfig.materialTest.requiredShaderTags[0].tagId,
                    Is.EqualTo(new ShaderTagId("RenderPipeline")));
                Assert.That(
                    cullingConfig.materialTest.requiredShaderTags[0].tagValueId,
                    Is.EqualTo(new ShaderTagId("VividRenderPipeline")));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void CreateCullingConfig_UsesSolidAngleCulling_WhenModeIsSelected()
        {
            var cameraObject = new GameObject("RTAS Solid Angle Camera");
            var camera = cameraObject.AddComponent<Camera>();

            try
            {
                var settings = new RTASBuildPass.ResolvedRayTracingSettings(
                    VividRTASBuildMode.Automatic,
                    VividRTASCullingMode.SolidAngle,
                    RTASBuildPass.DefaultSphereCullingDistance,
                    7.5f,
                    false,
                    false,
                    RTASBuildPass.DefaultRayBias,
                    RTASBuildPass.DefaultDistantRayBias,
                    ~0,
                    RayTracingAccelerationStructure.RayTracingModeMask.Everything,
                    RayTracingAccelerationStructureBuildFlags.None,
                    RayTracingAccelerationStructureBuildFlags.None,
                    false);

                var cullingConfig = RTASBuildPass.CreateCullingConfig(camera, in settings);

                Assert.That((cullingConfig.flags & RayTracingInstanceCullingFlags.EnableSolidAngleCulling) != 0, Is.True);
                Assert.That(cullingConfig.minSolidAngle, Is.EqualTo(7.5f));
                Assert.That((cullingConfig.flags & RayTracingInstanceCullingFlags.EnablePlaneCulling) == 0, Is.True);
                Assert.That((cullingConfig.flags & RayTracingInstanceCullingFlags.EnableSphereCulling) == 0, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void CountMeshletCandidateInstances_CountsPerSubMesh_WhenFallbackFilterIsUsed()
        {
            Material firstMaterial = null;
            Material secondMaterial = null;
            Mesh mesh = null;
            GameObject gameObject = null;
            VividMeshletCollectionAsset firstCollection = null;
            VividMeshletCollectionAsset secondCollection = null;

            try
            {
                mesh = CreateTwoSubMeshMesh("RTAS_MeshletFallback_Mesh");
                firstMaterial = CreateTestMaterial();
                secondMaterial = CreateTestMaterial();
                var meshletRenderer = CreateMeshletRenderer(
                    "RTAS_MeshletFallback",
                    mesh,
                    new[] { firstMaterial, secondMaterial },
                    out gameObject);

                firstCollection = CreateMeshletCollection(0);
                secondCollection = CreateMeshletCollection(1);
                meshletRenderer.SetMeshletCollections(new[] { firstCollection, secondCollection });

                RemoveAttachedSourceRenderer(gameObject);
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);

                Assert.That(
                    RTASBuildPass.CountMeshletCandidateInstances(
                        VividMeshletRendererDatabase.instance,
                        ~0,
                        RayTracingAccelerationStructure.RayTracingModeMask.Everything,
                        true),
                    Is.Zero);
                Assert.That(
                    RTASBuildPass.CountMeshletCandidateInstances(
                        VividMeshletRendererDatabase.instance,
                        ~0,
                        RayTracingAccelerationStructure.RayTracingModeMask.Everything,
                        false),
                    Is.EqualTo(2));
            }
            finally
            {
                DestroyTestObjects(gameObject, mesh, firstMaterial, secondMaterial, firstCollection, secondCollection);
            }
        }

        [Test]
        public void CountMeshletCandidateInstances_UsesProxySourceMaterial_WhenSharedMaterialIsMissing()
        {
            Material sourceMaterial = null;
            Mesh mesh = null;
            GameObject gameObject = null;
            VividMeshletCollectionAsset meshletCollection = null;
            GPUDrivenMaterialProxy materialProxy = null;

            try
            {
                mesh = CreateSingleSubMeshMesh("RTAS_MeshletProxyMaterial_Mesh");
                sourceMaterial = CreateTestMaterial();
                var meshletRenderer = CreateMeshletRenderer(
                    "RTAS_MeshletProxyMaterial",
                    mesh,
                    new[] { sourceMaterial },
                    out gameObject);

                meshletCollection = CreateMeshletCollection(0);
                materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
                materialProxy.SourceMaterial = sourceMaterial;

                meshletRenderer.SetSourceMaterials(new Material[] { null });
                meshletRenderer.SetMeshletCollections(new[] { meshletCollection });
                meshletRenderer.SetMaterialProxies(new[] { materialProxy });

                RemoveAttachedSourceRenderer(gameObject);
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);

                Assert.That(VividMeshletRendererDatabase.instance.TryGetRendererResources(meshletRenderer, out var resources), Is.True);
                Assert.That(RTASBuildPass.TryResolveMeshletMaterial(resources, 0, out var resolvedMaterial), Is.True);
                Assert.That(resolvedMaterial, Is.SameAs(sourceMaterial));
                Assert.That(
                    RTASBuildPass.CountMeshletCandidateInstances(
                        VividMeshletRendererDatabase.instance,
                        ~0,
                        RayTracingAccelerationStructure.RayTracingModeMask.Everything,
                        false),
                    Is.EqualTo(1));
            }
            finally
            {
                DestroyTestObjects(gameObject, mesh, sourceMaterial, meshletCollection, materialProxy);
            }
        }

        [Test]
        public void CountMeshletCandidateInstances_SkipsMeshletRenderer_WhenAttachedSourceRendererIsStillActive()
        {
            Material material = null;
            Mesh mesh = null;
            GameObject gameObject = null;
            VividMeshletCollectionAsset meshletCollection = null;

            try
            {
                mesh = CreateSingleSubMeshMesh("RTAS_MeshletDuplicateGuard_Mesh");
                material = CreateTestMaterial();
                var meshletRenderer = CreateMeshletRenderer(
                    "RTAS_MeshletDuplicateGuard",
                    mesh,
                    new[] { material },
                    out gameObject);

                meshletCollection = CreateMeshletCollection(0);
                meshletRenderer.SetMeshletCollections(new[] { meshletCollection });
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);

                Assert.That(
                    RTASBuildPass.CountMeshletCandidateInstances(
                        VividMeshletRendererDatabase.instance,
                        ~0,
                        RayTracingAccelerationStructure.RayTracingModeMask.Everything,
                        false),
                    Is.Zero);
            }
            finally
            {
                DestroyTestObjects(gameObject, mesh, material, meshletCollection);
            }
        }

        [Test]
        public void CountMeshletCandidateInstances_RespectsDynamicTransformMode_ForMeshletRenderer()
        {
            Material material = null;
            Mesh mesh = null;
            GameObject gameObject = null;
            VividMeshletCollectionAsset meshletCollection = null;

            try
            {
                mesh = CreateSingleSubMeshMesh("RTAS_MeshletDynamicMask_Mesh");
                material = CreateTestMaterial();
                var meshletRenderer = CreateMeshletRenderer(
                    "RTAS_MeshletDynamicMask",
                    mesh,
                    new[] { material },
                    out gameObject);

                meshletCollection = CreateMeshletCollection(0);
                meshletRenderer.SetMeshletCollections(new[] { meshletCollection });

                RemoveAttachedSourceRenderer(gameObject);
                VividMeshletRendererDatabase.instance.UpdateRendererData(meshletRenderer);

                Assert.That(VividMeshletRendererDatabase.instance.TryGetRendererData(meshletRenderer, out var trackedData), Is.True);
                Assert.That(RTASBuildPass.GetMeshletRayTracingMode(trackedData.flags), Is.EqualTo(RayTracingMode.DynamicTransform));
                Assert.That(
                    RTASBuildPass.CountMeshletCandidateInstances(
                        VividMeshletRendererDatabase.instance,
                        ~0,
                        RayTracingAccelerationStructure.RayTracingModeMask.Static,
                        false),
                    Is.Zero);
                Assert.That(
                    RTASBuildPass.CountMeshletCandidateInstances(
                        VividMeshletRendererDatabase.instance,
                        ~0,
                        RayTracingAccelerationStructure.RayTracingModeMask.DynamicTransform,
                        false),
                    Is.EqualTo(1));
            }
            finally
            {
                DestroyTestObjects(gameObject, mesh, material, meshletCollection);
            }
        }

        [Test]
        public void CollectMeshletRendererInstanceBatches_GroupsCompatibleMeshletInstances_ForAddInstances()
        {
            Material material = null;
            Mesh mesh = null;
            GameObject firstObject = null;
            GameObject secondObject = null;
            VividMeshletCollectionAsset meshletCollection = null;

            try
            {
                mesh = CreateSingleSubMeshMesh("RTAS_MeshletBatch_Mesh");
                material = CreateTestMaterial();
                material.enableInstancing = true;

                var firstRenderer = CreateMeshletRenderer(
                    "RTAS_MeshletBatch_First",
                    mesh,
                    new[] { material },
                    out firstObject);
                var secondRenderer = CreateMeshletRenderer(
                    "RTAS_MeshletBatch_Second",
                    mesh,
                    new[] { material },
                    out secondObject);

                firstObject.transform.position = new Vector3(1f, 2f, 3f);
                secondObject.transform.position = new Vector3(4f, 5f, 6f);

                meshletCollection = CreateMeshletCollection(0);
                firstRenderer.SetMeshletCollections(new[] { meshletCollection });
                secondRenderer.SetMeshletCollections(new[] { meshletCollection });

                RemoveAttachedSourceRenderer(firstObject);
                RemoveAttachedSourceRenderer(secondObject);
                VividMeshletRendererDatabase.instance.UpdateRendererData(firstRenderer);
                VividMeshletRendererDatabase.instance.UpdateRendererData(secondRenderer);

                var settings = CreateDefaultResolvedSettings();
                var batches = RTASBuildPass.CollectMeshletRendererInstanceBatches(
                    VividMeshletRendererDatabase.instance,
                    null,
                    in settings,
                    false);

                Assert.That(batches, Has.Count.EqualTo(1));
                Assert.That(batches[0].Config.mesh, Is.SameAs(mesh));
                Assert.That(batches[0].Config.material, Is.SameAs(material));
                Assert.That(batches[0].Config.subMeshIndex, Is.EqualTo(0u));
                Assert.That(batches[0].ObjectToWorldMatrices, Has.Count.EqualTo(2));
                Assert.That(batches[0].ObjectToWorldMatrices[0], Is.EqualTo(firstObject.transform.localToWorldMatrix));
                Assert.That(batches[0].ObjectToWorldMatrices[1], Is.EqualTo(secondObject.transform.localToWorldMatrix));
                Assert.That(RTASBuildPass.CanUseAddInstances(batches[0].Config.material, batches[0].ObjectToWorldMatrices.Count), Is.True);
            }
            finally
            {
                if (firstObject != null)
                    Object.DestroyImmediate(firstObject);

                if (secondObject != null)
                    Object.DestroyImmediate(secondObject);

                if (mesh != null)
                    Object.DestroyImmediate(mesh);

                if (material != null)
                    Object.DestroyImmediate(material);

                if (meshletCollection != null)
                    Object.DestroyImmediate(meshletCollection);
            }
        }

        [Test]
        public void CanUseAddInstances_RequiresMaterialAndPositiveInstanceCount()
        {
            var material = CreateTestMaterial();

            try
            {
                Assert.That(RTASBuildPass.CanUseAddInstances(null, 2), Is.False);
                Assert.That(RTASBuildPass.CanUseAddInstances(material, 0), Is.False);
                Assert.That(RTASBuildPass.CanUseAddInstances(material, 1), Is.True);
                Assert.That(RTASBuildPass.CanUseAddInstances(material, 2), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void ShouldUseAutomaticSceneRendererCulling_DisablesCullInstances_WhenBuildModeIsManual()
        {
            var automaticSettings = CreateDefaultResolvedSettings();
            var manualSettings = CreateResolvedSettings(VividRTASBuildMode.Manual);

            Assert.That(RTASBuildPass.ShouldUseAutomaticSceneRendererCulling(in automaticSettings), Is.True);
            Assert.That(RTASBuildPass.ShouldUseAutomaticSceneRendererCulling(in manualSettings), Is.False);
        }

        [Test]
        public void Prepare_WritesResolvedSettingsIntoFrameContext()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var cameraObject = new GameObject("RTAS Prepare Camera");

            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                var component = profile.Add<RayTracingSettingsVolume>(false);
                component.active = true;
                component.rayBias.overrideState = true;
                component.rayBias.value = 0.03f;
                component.minSolidAngle.overrideState = true;
                component.minSolidAngle.value = 9f;
                component.buildMode.overrideState = true;
                component.buildMode.value = VividRTASBuildMode.Manual;

                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                VolumeManager.instance.Initialize(profile);
                VolumeManager.instance.Update(camera.transform, ~0);

                var pass = new RTASBuildPass();
                pass.Create();

                var frameData = new ContextContainer();
                frameData.GetOrCreate<VividCameraData>().camera = camera;

                pass.Prepare(frameData);

                var rayTracingData = frameData.GetOrCreate<VividRayTracingSettingsData>();
                Assert.That(rayTracingData.buildMode, Is.EqualTo(VividRTASBuildMode.Manual));
                Assert.That(rayTracingData.rayBias, Is.EqualTo(0.03f));
                Assert.That(rayTracingData.minSolidAngle, Is.EqualTo(9f));
                Assert.That(rayTracingData.cullingMode, Is.EqualTo(VividRTASCullingMode.ExtendedFrustum));
            }
            finally
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ShaderVariablesRayTracingUtility_Create_UsesRayTracingSettingsData()
        {
            var settings = new VividRayTracingSettingsData
            {
                rayBias = 0.02f,
                distantRayBias = 0.08f,
                minSolidAngle = 6f,
            };

            var shaderVariables = ShaderVariablesRayTracingUtility.Create(settings);

            Assert.That(shaderVariables._RayTracingRayBias, Is.EqualTo(0.02f));
            Assert.That(shaderVariables._RayTracingDistantRayBias, Is.EqualTo(0.08f));
            Assert.That(shaderVariables._RayTracingMinSolidAngle, Is.EqualTo(6f));
        }

        [Test]
        public void ShaderVariablesRayTracingUtility_OverrideBiases_ReplacesBiasValues()
        {
            var shaderVariables = new ShaderVariablesRayTracing
            {
                _RayTracingRayBias = 0.01f,
                _RayTracingDistantRayBias = 0.04f,
                _RayTracingMinSolidAngle = 3f,
            };

            ShaderVariablesRayTracingUtility.OverrideBiases(ref shaderVariables, 0.03f, 0.09f);

            Assert.That(shaderVariables._RayTracingRayBias, Is.EqualTo(0.03f));
            Assert.That(shaderVariables._RayTracingDistantRayBias, Is.EqualTo(0.09f));
            Assert.That(shaderVariables._RayTracingMinSolidAngle, Is.EqualTo(3f));
        }

        private static MeshletRenderer CreateMeshletRenderer(
            string name,
            Mesh mesh,
            Material[] materials,
            out GameObject gameObject)
        {
            gameObject = new GameObject(name);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;

            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterials = materials;

            var meshletRenderer = gameObject.AddComponent<MeshletRenderer>();
            meshletRenderer.CaptureSourceFromRenderer(meshRenderer);
            return meshletRenderer;
        }

        private static RTASBuildPass.ResolvedRayTracingSettings CreateDefaultResolvedSettings()
        {
            return CreateResolvedSettings(VividRTASBuildMode.Automatic);
        }

        private static RTASBuildPass.ResolvedRayTracingSettings CreateResolvedSettings(VividRTASBuildMode buildMode)
        {
            return new RTASBuildPass.ResolvedRayTracingSettings(
                buildMode,
                VividRTASCullingMode.ExtendedFrustum,
                RTASBuildPass.DefaultSphereCullingDistance,
                RTASBuildPass.DefaultMinSolidAngle,
                false,
                false,
                RTASBuildPass.DefaultRayBias,
                RTASBuildPass.DefaultDistantRayBias,
                ~0,
                RayTracingAccelerationStructure.RayTracingModeMask.Everything,
                RayTracingAccelerationStructureBuildFlags.None,
                RayTracingAccelerationStructureBuildFlags.None,
                false);
        }

        private static VividMeshletCollectionAsset CreateMeshletCollection(int subMeshIndex)
        {
            var meshletCollection = ScriptableObject.CreateInstance<VividMeshletCollectionAsset>();
            meshletCollection.SourceSubmeshIndex = subMeshIndex;
            return meshletCollection;
        }

        private static void RemoveAttachedSourceRenderer(GameObject gameObject)
        {
            if (gameObject == null)
                return;

            var meshRenderer = gameObject.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
                Object.DestroyImmediate(meshRenderer);

            var meshFilter = gameObject.GetComponent<MeshFilter>();
            if (meshFilter != null)
                Object.DestroyImmediate(meshFilter);
        }

        private static Material CreateTestMaterial()
        {
            var shader = Shader.Find("Hidden/InternalErrorShader");
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
                    new Vector3(0f, 0f, 0f),
                    new Vector3(1f, 0f, 0f),
                    new Vector3(0f, 1f, 0f),
                    new Vector3(1f, 1f, 0f),
                },
            };

            mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateTwoSubMeshMesh(string meshName)
        {
            var mesh = new Mesh
            {
                name = meshName,
                vertices = new[]
                {
                    new Vector3(0f, 0f, 0f),
                    new Vector3(1f, 0f, 0f),
                    new Vector3(0f, 1f, 0f),
                    new Vector3(1f, 1f, 0f),
                    new Vector3(2f, 0f, 0f),
                    new Vector3(2f, 1f, 0f),
                },
                subMeshCount = 2,
            };

            mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0, true);
            mesh.SetTriangles(new[] { 1, 3, 4, 4, 3, 5 }, 1, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void DestroyTestObjects(GameObject gameObject, Mesh mesh, params Object[] objects)
        {
            if (gameObject != null)
                Object.DestroyImmediate(gameObject);

            if (mesh != null)
                Object.DestroyImmediate(mesh);

            if (objects == null)
                return;

            for (var index = 0; index < objects.Length; index++)
            {
                if (objects[index] != null)
                    Object.DestroyImmediate(objects[index]);
            }
        }
    }
}
