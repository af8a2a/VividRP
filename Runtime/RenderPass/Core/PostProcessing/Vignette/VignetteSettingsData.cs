using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal struct VignetteSettingsData
    {
        private static readonly VignetteSettingsData s_Default = new()
        {
            enabled = false,
            mode = VignetteMode.Procedural,
            color = Color.black,
            center = new Vector2(0.5f, 0.5f),
            intensity = 0f,
            smoothness = 0.2f,
            roundness = 1f,
            rounded = false,
            mask = null,
            opacity = 1f
        };

        public bool enabled;
        public VignetteMode mode;
        public Color color;
        public Vector2 center;
        public float intensity;
        public float smoothness;
        public float roundness;
        public bool rounded;
        public Texture mask;
        public float opacity;

        public bool IsProcedural => mode == VignetteMode.Procedural;

        public static VignetteSettingsData CreateDefault()
        {
            return s_Default;
        }
    }

    internal static class VignetteRuntimeUtility
    {
        internal const float HdrpIntensityMultiplier = 3f;
        internal const float HdrpSmoothnessMultiplier = 5f;

        internal static Vector4 CreateParams1(VignetteSettingsData settings)
        {
            return settings.IsProcedural
                ? new Vector4(settings.center.x, settings.center.y, 0f, 0f)
                : new Vector4(0f, 0f, 1f, 0f);
        }

        internal static Vector4 CreateParams2(VignetteSettingsData settings)
        {
            if (!settings.IsProcedural)
                return Vector4.zero;

            return new Vector4(
                settings.intensity * HdrpIntensityMultiplier,
                settings.smoothness * HdrpSmoothnessMultiplier,
                CreateHdrpRoundness(settings.roundness),
                settings.rounded ? 1f : 0f);
        }

        internal static Vector4 CreateColor(VignetteSettingsData settings)
        {
            var color = settings.color;
            color.a = settings.IsProcedural ? color.a : Mathf.Clamp01(settings.opacity);
            return color;
        }

        internal static Vector4 CreateScreenParams(Rect viewport)
        {
            var width = viewport.width > 0f ? viewport.width : Screen.width;
            var height = viewport.height > 0f ? viewport.height : Screen.height;
            width = Mathf.Max(1f, width);
            height = Mathf.Max(1f, height);
            return new Vector4(width, height, 1f / width, 1f / height);
        }

        internal static float CreateHdrpRoundness(float roundness)
        {
            return (1f - roundness) * 6f + roundness;
        }
    }

    internal static class VignetteSettingsResolver
    {
        internal static VignetteSettingsData Resolve()
        {
            var settings = VignetteSettingsData.CreateDefault();
            var stack = VolumeManager.instance.stack;
            if (stack == null)
                return settings;

            var vignette = stack.GetComponent<Vignette>();
            if (vignette == null || !vignette.IsActive())
                return settings;

            settings.enabled = true;
            settings.mode = vignette.mode.value;
            settings.color = vignette.color.value;
            settings.center = vignette.center.value;
            settings.intensity = vignette.intensity.value;
            settings.smoothness = vignette.smoothness.value;
            settings.roundness = vignette.roundness.value;
            settings.rounded = vignette.rounded.value;
            settings.mask = vignette.mask.value;
            settings.opacity = vignette.opacity.value;
            return settings;
        }
    }
}
