using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal static class VividVolumeManagerUtility
    {
        private static readonly LayerMask s_DefaultVolumeLayerMask = 1;

        internal static void Initialize()
        {
            if (VolumeManager.instance.isInitialized)
                VolumeManager.instance.Deinitialize();

            var defaultVolumeProfile = GetDefaultVolumeProfile();
            UpgradeDefaultVolumeProfileValues(defaultVolumeProfile);
            VolumeManager.instance.Initialize(defaultVolumeProfile);
        }

        internal static void Update(Camera camera)
        {
            if (!VolumeManager.instance.isInitialized || camera == null)
                return;

            var additionalCameraData = camera.GetComponent<VividAdditionalCameraData>();
            if (additionalCameraData == null && camera.cameraType == CameraType.Game)
                additionalCameraData = camera.GetVividAdditionalCameraData();

            var volumeLayerMask = ResolveVolumeLayerMask(camera, additionalCameraData);

            VolumeManager.instance.Update(camera.transform, volumeLayerMask);
        }

        internal static LayerMask ResolveVolumeLayerMask(
            Camera camera,
            VividAdditionalCameraData additionalCameraData = null)
        {
            if (additionalCameraData != null)
                return additionalCameraData.volumeLayerMask;

            if (camera == null)
                return s_DefaultVolumeLayerMask;

            if (camera.cameraType == CameraType.SceneView)
            {
                var mainCamera = Camera.main;
                if (mainCamera != null
                    && mainCamera.TryGetComponent<VividAdditionalCameraData>(out var mainCameraData))
                {
                    return mainCameraData.volumeLayerMask;
                }

                return s_DefaultVolumeLayerMask;
            }

            if (camera.cameraType == CameraType.Preview)
                return s_DefaultVolumeLayerMask;

            return camera.cullingMask;
        }

        internal static void Deinitialize()
        {
            if (VolumeManager.instance.isInitialized)
                VolumeManager.instance.Deinitialize();
        }

        internal static VolumeProfile GetDefaultVolumeProfile()
        {
            return VividRenderPipelineGlobalSettings.instance?
                .GetSettings<VividDefaultVolumeProfileSettings>()?
                .volumeProfile;
        }

        internal static bool UpgradeDefaultVolumeProfileValues(VolumeProfile profile)
        {
            return profile != null
                && profile.TryGet<AutoExposure>(out var autoExposure)
                && autoExposure.UpgradeUnrealDefaultProfileValuesIfNeeded();
        }

        internal static HDRISkyVolume GetHDRISkyVolume()
        {
            return VolumeManager.instance.stack?.GetComponent<HDRISkyVolume>();
        }

        internal static SkySettingsVolume GetSkySettingsVolume()
        {
            return VolumeManager.instance.stack?.GetComponent<SkySettingsVolume>();
        }

        internal static PhysicallyBasedSkyVolume GetPhysicallyBasedSkyVolume()
        {
            return VolumeManager.instance.stack?.GetComponent<PhysicallyBasedSkyVolume>();
        }

        internal static RayTracingSettingsVolume GetRayTracingSettingsVolume()
        {
            return VolumeManager.instance.stack?.GetComponent<RayTracingSettingsVolume>();
        }

        internal static ReferencedPathTracingSettingsVolume GetReferencedPathTracingSettingsVolume()
        {
            return VolumeManager.instance.stack?.GetComponent<ReferencedPathTracingSettingsVolume>();
        }

        internal static CascadedShadowSettingsVolume GetCascadedShadowSettingsVolume()
        {
            return VolumeManager.instance.stack?.GetComponent<CascadedShadowSettingsVolume>();
        }

        internal static GPUDrivenSettingsVolume GetGPUDrivenSettingsVolume()
        {
            return VolumeManager.instance.stack?.GetComponent<GPUDrivenSettingsVolume>();
        }

        internal static ProbeVolumesOptions GetProbeVolumesOptions()
        {
            return VolumeManager.instance.stack?.GetComponent<ProbeVolumesOptions>();
        }

        internal static VividVolumetricFogVolume GetVolumetricFogVolume()
        {
            return VolumeManager.instance.stack?.GetComponent<VividVolumetricFogVolume>();
        }
    }
}
