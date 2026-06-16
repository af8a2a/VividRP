using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal static class VividHDROutputUtility
    {
        internal static bool HDROutputForMainDisplayIsActive()
        {
            return SystemInfo.hdrDisplaySupportFlags.HasFlag(HDRDisplaySupportFlags.Supported)
                && HDROutputSettings.main.available
                && HDROutputSettings.main.active;
        }

        internal static bool HDROutputActiveForCameraType(CameraType cameraType)
        {
            return cameraType == CameraType.Game && HDROutputForMainDisplayIsActive();
        }

        internal static bool HDROutputActiveForCamera(Camera camera)
        {
            return camera != null && HDROutputActiveForCameraType(camera.cameraType);
        }

        internal static HDROutputUtils.HDRDisplayInformation HDRDisplayInformationForCamera(Camera camera)
        {
            var displaySettings = HDROutputSettings.main;
            return new HDROutputUtils.HDRDisplayInformation(
                displaySettings.maxFullFrameToneMapLuminance,
                displaySettings.maxToneMapLuminance,
                displaySettings.minToneMapLuminance,
                displaySettings.paperWhiteNits);
        }

        internal static ColorGamut HDRDisplayColorGamutForCamera(Camera camera)
        {
            return HDROutputSettings.main.displayColorGamut;
        }

        internal static void SetHDRState(Camera camera, ref bool enableHdrOnce)
        {
            if (camera == null || camera.cameraType == CameraType.Reflection)
                return;

            var displaySettings = HDROutputSettings.main;

#if UNITY_EDITOR
            var hdrEnabledInPlayerSettings = UnityEditor.PlayerSettings.useHDRDisplay;
#else
            var hdrEnabledInPlayerSettings = true;
#endif
            var supportsSwitchingHDR =
                SystemInfo.hdrDisplaySupportFlags.HasFlag(HDRDisplaySupportFlags.RuntimeSwitchable);
            var hdrOutputAvailable = displaySettings.available;
            var hdrOutputActive = displaySettings.active;

            if (hdrEnabledInPlayerSettings && supportsSwitchingHDR && hdrOutputAvailable)
            {
                if (camera.cameraType != CameraType.Game)
                {
                    if (hdrOutputActive)
                        displaySettings.RequestHDRModeChange(false);
                }
                else if (enableHdrOnce)
                {
                    displaySettings.RequestHDRModeChange(true);
                    enableHdrOnce = false;
                }
            }

            if (displaySettings.active)
                displaySettings.automaticHDRTonemapping = false;
        }
    }
}
