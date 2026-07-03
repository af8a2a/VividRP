using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal static class VividCameraUtility
    {
        internal static bool ShouldUseGameCameraAdditionalData(Camera camera)
        {
            return camera != null
                   && camera.cameraType == CameraType.Game
                   && !IsEditorPreviewCamera(camera);
        }

        internal static bool IsEditorPreviewCamera(Camera camera)
        {
            if (camera == null)
                return false;

            if (camera.cameraType == CameraType.Preview)
                return true;

#if UNITY_EDITOR
            // Some inspector previews enter SRP as hidden Game cameras instead of CameraType.Preview.
            return camera.cameraType == CameraType.Game
                   && HasPreviewCameraName(camera)
                   && (HasEditorTransientHideFlags(camera.hideFlags)
                       || HasEditorTransientHideFlags(camera.gameObject.hideFlags));
#else
            return false;
#endif
        }

#if UNITY_EDITOR
        private static bool HasPreviewCameraName(Camera camera)
        {
            return ContainsIgnoreCase(camera.name, "Preview")
                   || ContainsIgnoreCase(camera.gameObject.name, "Preview");
        }

        private static bool HasEditorTransientHideFlags(HideFlags hideFlags)
        {
            return (hideFlags & HideFlags.DontSave) != 0;
        }

        private static bool ContainsIgnoreCase(string value, string fragment)
        {
            return !string.IsNullOrEmpty(value)
                   && value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
        }
#endif
    }
}
