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

            VolumeManager.instance.Initialize(GetDefaultVolumeProfile());
        }

        internal static void Update(Camera camera)
        {
            if (!VolumeManager.instance.isInitialized || camera == null)
                return;

            var additionalCameraData = camera.GetComponent<VividAdditionalCameraData>();
            if (additionalCameraData == null && camera.cameraType == CameraType.Game)
                additionalCameraData = camera.GetVividAdditionalCameraData();

            var volumeLayerMask = additionalCameraData != null
                ? additionalCameraData.volumeLayerMask
                : (LayerMask)camera.cullingMask;

            if (additionalCameraData == null && camera.cameraType == CameraType.Preview)
                volumeLayerMask = s_DefaultVolumeLayerMask;

            VolumeManager.instance.Update(camera.transform, volumeLayerMask);
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

        internal static HDRISkyVolume GetHDRISkyVolume()
        {
            return VolumeManager.instance.stack?.GetComponent<HDRISkyVolume>();
        }

        internal static SliderDebugVolume GetSliderDebugVolume()
        {
            return VolumeManager.instance.stack?.GetComponent<SliderDebugVolume>();
        }
    }
}
