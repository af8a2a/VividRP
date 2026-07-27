using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal struct DepthOfFieldSettingsData
    {
        public bool enabled;
        public bool physicallyBased;
        public DepthOfFieldMode focusMode;
        public FocusDistanceMode focusDistanceMode;
        public float focusDistance;
        public float nearFocusStart;
        public float nearFocusEnd;
        public float farFocusStart;
        public float farFocusEnd;
        public int nearSampleCount;
        public int farSampleCount;
        public float nearMaxBlur;
        public float farMaxBlur;
        public bool highQualityFiltering;
        public float adaptiveSamplingWeight;
        public bool limitManualRangeNearBlur;
        public bool coCStabilization;
        public DepthOfFieldResolution resolution;

        public static DepthOfFieldSettingsData CreateDefault()
        {
            return new DepthOfFieldSettingsData
            {
                enabled = false,
                physicallyBased = false,
                focusMode = DepthOfFieldMode.Off,
                focusDistanceMode = FocusDistanceMode.Volume,
                focusDistance = 10f,
                nearFocusStart = 0f,
                nearFocusEnd = 4f,
                farFocusStart = 10f,
                farFocusEnd = 20f,
                nearSampleCount = 5,
                farSampleCount = 7,
                nearMaxBlur = 4f,
                farMaxBlur = 8f,
                highQualityFiltering = true,
                adaptiveSamplingWeight = 0.75f,
                limitManualRangeNearBlur = false,
                coCStabilization = true,
                resolution = DepthOfFieldResolution.Half
            };
        }
    }

    internal static class DepthOfFieldSettingsResolver
    {
        internal static DepthOfFieldSettingsData Resolve()
        {
            var stack = VolumeManager.instance.stack;
            if (stack == null)
                return DepthOfFieldSettingsData.CreateDefault();

            var depthOfField = stack.GetComponent<DepthOfField>();
            if (depthOfField == null || !depthOfField.IsActive())
                return DepthOfFieldSettingsData.CreateDefault();

            return Resolve(depthOfField);
        }

        internal static DepthOfFieldSettingsData
            ResolveForReferencePathTracing()
        {
            var stack = VolumeManager.instance.stack;
            if (stack == null)
                return DepthOfFieldSettingsData.CreateDefault();

            var depthOfField = stack.GetComponent<DepthOfField>();
            if (depthOfField == null
                || depthOfField.focusMode.value
                    != DepthOfFieldMode.UsePhysicalCamera)
            {
                return DepthOfFieldSettingsData.CreateDefault();
            }

            // A physical camera integrates over the lens aperture itself. The
            // post-process near/far blur-radius switches therefore must not
            // silently disable thin-lens transport.
            return Resolve(depthOfField);
        }

        private static DepthOfFieldSettingsData Resolve(
            DepthOfField depthOfField)
        {
            var settings = DepthOfFieldSettingsData.CreateDefault();
            settings.enabled = true;
            settings.physicallyBased = depthOfField.physicallyBased;
            settings.focusMode = depthOfField.focusMode.value;
            settings.focusDistanceMode = depthOfField.focusDistanceMode.value;
            settings.focusDistance = depthOfField.focusDistance.value;
            settings.nearFocusStart = depthOfField.nearFocusStart.value;
            settings.nearFocusEnd = depthOfField.nearFocusEnd.value;
            settings.farFocusStart = depthOfField.farFocusStart.value;
            settings.farFocusEnd = depthOfField.farFocusEnd.value;
            settings.nearSampleCount = depthOfField.nearSampleCount;
            settings.farSampleCount = depthOfField.farSampleCount;
            settings.nearMaxBlur = depthOfField.nearMaxBlur;
            settings.farMaxBlur = depthOfField.farMaxBlur;
            settings.highQualityFiltering = depthOfField.highQualityFiltering;
            settings.adaptiveSamplingWeight = depthOfField.adaptiveSamplingWeight;
            settings.limitManualRangeNearBlur = depthOfField.limitManualRangeNearBlur;
            settings.coCStabilization = depthOfField.coCStabilization.value;
            settings.resolution = depthOfField.resolution;
            return settings;
        }
    }
}
