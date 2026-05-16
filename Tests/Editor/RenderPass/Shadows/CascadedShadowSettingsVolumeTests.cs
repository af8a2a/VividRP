using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class CascadedShadowSettingsVolumeTests
    {
        [Test]
        public void GetCascadeBorderRatios_ConvertsInterCascadeRangesToSquaredDistanceFade()
        {
            var volume = ScriptableObject.CreateInstance<CascadedShadowSettingsVolume>();

            try
            {
                volume.cascadeCount.value = 4;
                volume.cascadeSplit1.value = 2.0f / 70.0f;
                volume.cascadeSplit2.value = 6.0f / 70.0f;
                volume.cascadeSplit3.value = 22.0f / 70.0f;
                volume.cascadeBorder1.value = 0.26794f / 2.0f;
                volume.cascadeBorder2.value = 1.17174f / 4.0f;
                volume.cascadeBorder3.value = 3.35086f / 16.0f;
                volume.cascadeBorder4.value = 0.0f;

                var borders = volume.GetCascadeBorderRatios();

                Assert.That(borders.x, Is.EqualTo(ConvertInterCascadeBorder(volume.cascadeBorder1.value, 0.0f, volume.cascadeSplit1.value)).Within(1e-6f));
                Assert.That(borders.y, Is.EqualTo(ConvertInterCascadeBorder(volume.cascadeBorder2.value, volume.cascadeSplit1.value, volume.cascadeSplit2.value)).Within(1e-6f));
                Assert.That(borders.z, Is.EqualTo(ConvertInterCascadeBorder(volume.cascadeBorder3.value, volume.cascadeSplit2.value, volume.cascadeSplit3.value)).Within(1e-6f));
                Assert.That(borders.w, Is.EqualTo(ConvertInterCascadeBorder(volume.cascadeBorder4.value, volume.cascadeSplit3.value, 1.0f)).Within(1e-6f));
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        private static float ConvertInterCascadeBorder(float interCascadeBorder, float previousCascadeRelativeRange, float cascadeRelativeRange)
        {
            float rangeBorder = cascadeRelativeRange > 0.0f
                ? (cascadeRelativeRange - previousCascadeRelativeRange) * interCascadeBorder / cascadeRelativeRange
                : 0.0f;

            return 1.0f - (1.0f - rangeBorder) * (1.0f - rangeBorder);
        }

        [Test]
        public void CascadedShadowSettingsVolume_ExposesScreenSpaceShadowDenoiseToggle()
        {
            var volume = ScriptableObject.CreateInstance<CascadedShadowSettingsVolume>();

            try
            {
                Assert.That(volume.screenSpaceShadowDenoise, Is.Not.Null);
                Assert.That(volume.screenSpaceShadowDenoise.value, Is.False);

                volume.screenSpaceShadowDenoise.value = true;

                Assert.That(volume.screenSpaceShadowDenoise.value, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }
    }
}
