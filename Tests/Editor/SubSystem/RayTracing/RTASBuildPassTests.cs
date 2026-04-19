using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class RTASBuildPassTests
    {
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
    }
}
