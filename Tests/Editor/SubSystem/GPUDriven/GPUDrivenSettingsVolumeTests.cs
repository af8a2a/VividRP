using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class GPUDrivenSettingsVolumeTests
    {
        [Test]
        public void GPUDrivenSettingsVolume_UsesGPUDrivenDefaults_WhenCreated()
        {
            var volume = ScriptableObject.CreateInstance<GPUDrivenSettingsVolume>();

            try
            {
                Assert.That(
                    volume.forcedMeshLODNodeDepth.value,
                    Is.EqualTo(VividGPUDrivenCullingContextUtility.DefaultForcedMeshLODNodeDepth));
                Assert.That(
                    volume.meshLODErrorThreshold.value,
                    Is.EqualTo(VividGPUDrivenCullingContextUtility.DefaultMeshLODErrorThreshold));
                Assert.That(volume.IsActive(), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void ResolveSettings_UsesVolumeOverrides_WhenOverrideStateEnabled()
        {
            var volume = ScriptableObject.CreateInstance<GPUDrivenSettingsVolume>();

            try
            {
                volume.active = true;
                volume.forcedMeshLODNodeDepth.overrideState = true;
                volume.forcedMeshLODNodeDepth.value = 3;
                volume.meshLODErrorThreshold.overrideState = true;
                volume.meshLODErrorThreshold.value = 12.5f;

                GPUDrivenSettingsVolume.GPUDrivenSettingsData settings = GPUDrivenSettingsVolume.ResolveSettings(volume);

                Assert.That(settings.forcedMeshLODNodeDepth, Is.EqualTo(3));
                Assert.That(settings.meshLODErrorThreshold, Is.EqualTo(12.5f));
                Assert.That(volume.IsActive(), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void GetGPUDrivenSettingsVolume_ReturnsStackComponent_WhenVolumeManagerIsInitialized()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var cameraObject = new GameObject("GPUDriven Volume Camera");

            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                GPUDrivenSettingsVolume component = profile.Add<GPUDrivenSettingsVolume>(false);
                component.active = true;
                component.meshLODErrorThreshold.overrideState = true;
                component.meshLODErrorThreshold.value = 9.5f;

                if (VolumeManager.instance.isInitialized)
                {
                    VolumeManager.instance.Deinitialize();
                }

                VolumeManager.instance.Initialize(profile);
                VolumeManager.instance.Update(camera.transform, ~0);

                GPUDrivenSettingsVolume resolvedVolume = VividVolumeManagerUtility.GetGPUDrivenSettingsVolume();

                Assert.That(resolvedVolume, Is.Not.Null);
                Assert.That(resolvedVolume.meshLODErrorThreshold.value, Is.EqualTo(9.5f));
            }
            finally
            {
                if (VolumeManager.instance.isInitialized)
                {
                    VolumeManager.instance.Deinitialize();
                }

                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(profile);
            }
        }
    }
}
