// using NUnit.Framework;
// using UnityEngine;
// using UnityEngine.Rendering;
// using VividRP.Runtime;
// using VividRP.Runtime.RenderPass.Core;
//
// namespace VividRP.Editor.Tests
// {
//     public class RayTracingSettingsVolumeTests
//     {
//         [Test]
//         public void RayTracingSettingsVolume_UsesHdrpLikeDefaults_WhenCreated()
//         {
//             var volume = ScriptableObject.CreateInstance<RayTracingSettingsVolume>();
//
//             try
//             {
//                 Assert.That(volume.rayBias.value, Is.EqualTo(RTASBuildPass.DefaultRayBias));
//                 Assert.That(volume.distantRayBias.value, Is.EqualTo(RTASBuildPass.DefaultDistantRayBias));
//                 Assert.That(volume.extendShadowCulling.value, Is.False);
//                 Assert.That(volume.extendCameraCulling.value, Is.False);
//                 Assert.That(volume.buildMode.value, Is.EqualTo(VividRTASBuildMode.Automatic));
//                 Assert.That(volume.cullingMode.value, Is.EqualTo(VividRTASCullingMode.ExtendedFrustum));
//                 Assert.That(volume.cullingDistance.value, Is.EqualTo(RTASBuildPass.DefaultSphereCullingDistance));
//                 Assert.That((int)volume.layerMask.value, Is.EqualTo(~0));
//                 Assert.That(volume.rayTracingModeMask.value, Is.EqualTo(RayTracingAccelerationStructure.RayTracingModeMask.Everything));
//                 Assert.That(volume.enableCompaction.value, Is.False);
//                 Assert.That(volume.IsActive(), Is.False);
//             }
//             finally
//             {
//                 Object.DestroyImmediate(volume);
//             }
//         }
//
//         [Test]
//         public void ResolveSettings_UsesVolumeOverrides_WhenOverrideStateEnabled()
//         {
//             var volume = ScriptableObject.CreateInstance<RayTracingSettingsVolume>();
//
//             try
//             {
//                 volume.active = true;
//                 volume.rayBias.overrideState = true;
//                 volume.rayBias.value = 0.01f;
//                 volume.distantRayBias.overrideState = true;
//                 volume.distantRayBias.value = 0.1f;
//                 volume.extendShadowCulling.overrideState = true;
//                 volume.extendShadowCulling.value = true;
//                 volume.extendCameraCulling.overrideState = true;
//                 volume.extendCameraCulling.value = true;
//                 volume.buildMode.overrideState = true;
//                 volume.buildMode.value = VividRTASBuildMode.Manual;
//                 volume.cullingMode.overrideState = true;
//                 volume.cullingMode.value = VividRTASCullingMode.Sphere;
//                 volume.cullingDistance.overrideState = true;
//                 volume.cullingDistance.value = 321f;
//                 volume.layerMask.overrideState = true;
//                 volume.layerMask.value = 1 << 4;
//                 volume.rayTracingModeMask.overrideState = true;
//                 volume.rayTracingModeMask.value = RayTracingAccelerationStructure.RayTracingModeMask.DynamicGeometry;
//                 volume.buildFlagsStaticGeometries.overrideState = true;
//                 volume.buildFlagsStaticGeometries.value = RayTracingAccelerationStructureBuildFlags.PreferFastTrace;
//                 volume.buildFlagsDynamicGeometries.overrideState = true;
//                 volume.buildFlagsDynamicGeometries.value = RayTracingAccelerationStructureBuildFlags.PreferFastBuild;
//                 volume.enableCompaction.overrideState = true;
//                 volume.enableCompaction.value = true;
//
//                 var settings = RTASBuildPass.ResolveSettings(volume);
//                 var desc = RenderGraphAccelerationStructureDesc.Create("SceneRTAS");
//                 RTASBuildPass.ApplyResolvedSettings(desc, in settings);
//
//                 Assert.That(settings.buildMode, Is.EqualTo(VividRTASBuildMode.Manual));
//                 Assert.That(settings.cullingMode, Is.EqualTo(VividRTASCullingMode.Sphere));
//                 Assert.That(settings.cullingDistance, Is.EqualTo(321f));
//                 Assert.That(settings.extendShadowCulling, Is.True);
//                 Assert.That(settings.extendCameraCulling, Is.True);
//                 Assert.That(settings.rayBias, Is.EqualTo(0.01f));
//                 Assert.That(settings.distantRayBias, Is.EqualTo(0.1f));
//                 Assert.That(settings.layerMask.value, Is.EqualTo(1 << 4));
//                 Assert.That(settings.rayTracingModeMask, Is.EqualTo(RayTracingAccelerationStructure.RayTracingModeMask.DynamicGeometry));
//                 Assert.That(settings.buildFlagsStaticGeometries, Is.EqualTo(RayTracingAccelerationStructureBuildFlags.PreferFastTrace));
//                 Assert.That(settings.buildFlagsDynamicGeometries, Is.EqualTo(RayTracingAccelerationStructureBuildFlags.PreferFastBuild));
//                 Assert.That(settings.enableCompaction, Is.True);
//                 Assert.That(desc.ManagementMode, Is.EqualTo(RayTracingAccelerationStructure.ManagementMode.Manual));
//                 Assert.That(desc.LayerMask.value, Is.EqualTo(1 << 4));
//                 Assert.That(desc.EnableCompaction, Is.True);
//             }
//             finally
//             {
//                 Object.DestroyImmediate(volume);
//             }
//         }
//
//         [Test]
//         public void CreateCullingConfig_UsesSphereCulling_WhenSphereModeIsSelected()
//         {
//             var cameraObject = new GameObject("RTAS Sphere Camera");
//             var camera = cameraObject.AddComponent<Camera>();
//
//             try
//             {
//                 camera.transform.position = new Vector3(1f, 2f, 3f);
//
//                 var settings = new RTASBuildPass.ResolvedRayTracingSettings(
//                     VividRTASBuildMode.Automatic,
//                     VividRTASCullingMode.Sphere,
//                     42f,
//                     false,
//                     false,
//                     RTASBuildPass.DefaultRayBias,
//                     RTASBuildPass.DefaultDistantRayBias,
//                     1 << 2,
//                     RayTracingAccelerationStructure.RayTracingModeMask.Everything,
//                     RayTracingAccelerationStructureBuildFlags.None,
//                     RayTracingAccelerationStructureBuildFlags.None,
//                     false);
//
//                 var cullingConfig = RTASBuildPass.CreateCullingConfig(camera, in settings);
//
//                 Assert.That((cullingConfig.flags & RayTracingInstanceCullingFlags.EnableSphereCulling) != 0, Is.True);
//                 Assert.That(cullingConfig.sphereCenter, Is.EqualTo(camera.transform.position));
//                 Assert.That(cullingConfig.sphereRadius, Is.EqualTo(42f));
//                 Assert.That(cullingConfig.instanceTests, Has.Length.EqualTo(1));
//                 Assert.That(cullingConfig.instanceTests[0].layerMask, Is.EqualTo(1 << 2));
//             }
//             finally
//             {
//                 Object.DestroyImmediate(cameraObject);
//             }
//         }
//
//         [Test]
//         public void CreateCullingConfig_UsesExtendedPlanes_WhenExtendedCullingIsEnabled()
//         {
//             var cameraObject = new GameObject("RTAS Frustum Camera");
//             var camera = cameraObject.AddComponent<Camera>();
//
//             try
//             {
//                 camera.orthographic = false;
//                 camera.fieldOfView = 60f;
//                 camera.aspect = 16f / 9f;
//                 camera.nearClipPlane = 0.3f;
//                 camera.farClipPlane = 100f;
//
//                 var settings = new RTASBuildPass.ResolvedRayTracingSettings(
//                     VividRTASBuildMode.Automatic,
//                     VividRTASCullingMode.ExtendedFrustum,
//                     RTASBuildPass.DefaultSphereCullingDistance,
//                     false,
//                     true,
//                     RTASBuildPass.DefaultRayBias,
//                     RTASBuildPass.DefaultDistantRayBias,
//                     ~0,
//                     RayTracingAccelerationStructure.RayTracingModeMask.Everything,
//                     RayTracingAccelerationStructureBuildFlags.None,
//                     RayTracingAccelerationStructureBuildFlags.None,
//                     false);
//
//                 var cullingConfig = RTASBuildPass.CreateCullingConfig(camera, in settings);
//
//                 Assert.That((cullingConfig.flags & RayTracingInstanceCullingFlags.EnablePlaneCulling) != 0, Is.True);
//                 Assert.That(cullingConfig.planes, Has.Length.EqualTo(6));
//                 Assert.That(cullingConfig.sphereRadius, Is.EqualTo(0f));
//                 Assert.That(
//                     GeometryUtility.TestPlanesAABB(
//                         cullingConfig.planes,
//                         new Bounds(camera.transform.position + camera.transform.forward * 50f, Vector3.one)),
//                     Is.True);
//                 Assert.That(
//                     GeometryUtility.TestPlanesAABB(
//                         cullingConfig.planes,
//                         new Bounds(camera.transform.position - camera.transform.forward * 5f, Vector3.one)),
//                     Is.False);
//             }
//             finally
//             {
//                 Object.DestroyImmediate(cameraObject);
//             }
//         }
//
//         [Test]
//         public void GetRayTracingSettingsVolume_ReturnsStackComponent_WhenVolumeManagerIsInitialized()
//         {
//             var profile = ScriptableObject.CreateInstance<VolumeProfile>();
//             var cameraObject = new GameObject("RTAS Volume Camera");
//
//             try
//             {
//                 var camera = cameraObject.AddComponent<Camera>();
//                 var component = profile.Add<RayTracingSettingsVolume>(false);
//                 component.active = true;
//                 component.rayBias.overrideState = true;
//                 component.rayBias.value = 0.02f;
//
//                 if (VolumeManager.instance.isInitialized)
//                     VolumeManager.instance.Deinitialize();
//
//                 VolumeManager.instance.Initialize(profile);
//                 VolumeManager.instance.Update(camera.transform, ~0);
//
//                 var resolvedVolume = VividVolumeManagerUtility.GetRayTracingSettingsVolume();
//
//                 Assert.That(resolvedVolume, Is.Not.Null);
//                 Assert.That(resolvedVolume.rayBias.value, Is.EqualTo(0.02f));
//             }
//             finally
//             {
//                 if (VolumeManager.instance.isInitialized)
//                     VolumeManager.instance.Deinitialize();
//
//                 Object.DestroyImmediate(cameraObject);
//                 Object.DestroyImmediate(profile);
//             }
//         }
//
//         [Test]
//         public void Prepare_WritesResolvedSettingsIntoFrameContext()
//         {
//             var profile = ScriptableObject.CreateInstance<VolumeProfile>();
//             var cameraObject = new GameObject("RTAS Prepare Camera");
//
//             try
//             {
//                 var camera = cameraObject.AddComponent<Camera>();
//                 var component = profile.Add<RayTracingSettingsVolume>(false);
//                 component.active = true;
//                 component.rayBias.overrideState = true;
//                 component.rayBias.value = 0.03f;
//                 component.buildMode.overrideState = true;
//                 component.buildMode.value = VividRTASBuildMode.Manual;
//
//                 if (VolumeManager.instance.isInitialized)
//                     VolumeManager.instance.Deinitialize();
//
//                 VolumeManager.instance.Initialize(profile);
//                 VolumeManager.instance.Update(camera.transform, ~0);
//
//                 var pass = new RTASBuildPass();
//                 var frameData = new ContextContainer();
//                 frameData.GetOrCreate<VividCameraData>().camera = camera;
//
//                 pass.Prepare(frameData);
//
//                 var rayTracingData = frameData.GetOrCreate<VividRayTracingSettingsData>();
//                 Assert.That(rayTracingData.buildMode, Is.EqualTo(VividRTASBuildMode.Manual));
//                 Assert.That(rayTracingData.rayBias, Is.EqualTo(0.03f));
//                 Assert.That(rayTracingData.cullingMode, Is.EqualTo(VividRTASCullingMode.ExtendedFrustum));
//             }
//             finally
//             {
//                 if (VolumeManager.instance.isInitialized)
//                     VolumeManager.instance.Deinitialize();
//
//                 Object.DestroyImmediate(cameraObject);
//                 Object.DestroyImmediate(profile);
//             }
//         }
//     }
// }
