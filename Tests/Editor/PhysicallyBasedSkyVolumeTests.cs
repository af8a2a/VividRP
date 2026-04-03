using NUnit.Framework;
using UnityEngine;
using System.Reflection;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class PhysicallyBasedSkyVolumeTests
    {
        [Test]
        public void IsActive_ReturnsTrue_WhenAtmosphereContainsScattering()
        {
            var volume = ScriptableObject.CreateInstance<PhysicallyBasedSkyVolume>();

            try
            {
                volume.airMaximumAltitude.value = 10000.0f;
                volume.airDensityR.value = 0.25f;

                Assert.That(volume.IsActive(), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void GetAirExtinctionCoefficient_UsesCustomDensity_WhenTypeIsCustom()
        {
            var volume = ScriptableObject.CreateInstance<PhysicallyBasedSkyVolume>();

            try
            {
                volume.type.value = PhysicallyBasedSkyModel.Custom;
                volume.airMaximumAltitude.value = 12000.0f;
                volume.airDensityR.value = 0.5f;
                volume.airDensityG.value = 0.25f;
                volume.airDensityB.value = 0.125f;

                var expectedScaleHeight = PhysicallyBasedSkyVolume.ScaleHeightFromLayerDepth(12000.0f);
                var extinction = volume.GetAirExtinctionCoefficient();

                Assert.That(extinction.x, Is.EqualTo(PhysicallyBasedSkyVolume.ExtinctionFromZenithOpacityAndScaleHeight(0.5f, expectedScaleHeight)).Within(1e-5f));
                Assert.That(extinction.y, Is.EqualTo(PhysicallyBasedSkyVolume.ExtinctionFromZenithOpacityAndScaleHeight(0.25f, expectedScaleHeight)).Within(1e-5f));
                Assert.That(extinction.z, Is.EqualTo(PhysicallyBasedSkyVolume.ExtinctionFromZenithOpacityAndScaleHeight(0.125f, expectedScaleHeight)).Within(1e-5f));
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void IsHeightFogActive_ReturnsTrue_WhenEnabledWithPositiveDensityAndDistance()
        {
            var volume = ScriptableObject.CreateInstance<PhysicallyBasedSkyVolume>();

            try
            {
                volume.enableHeightFog.value = true;
                volume.fogDensity.value = 0.05f;
                volume.fogMaxDistance.value = 1500.0f;

                Assert.That(volume.IsHeightFogActive(), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void ExposureField_IsHidden_BecauseAutoExposureOwnsSkyBrightness()
        {
            var field = typeof(PhysicallyBasedSkyVolume).GetField(nameof(PhysicallyBasedSkyVolume.exposure));

            Assert.That(field, Is.Not.Null);
            Assert.That(field.GetCustomAttribute<HideInInspector>(), Is.Not.Null);
        }

        [Test]
        public void GetHashCode_DoesNotChange_WhenDeprecatedExposureChanges()
        {
            var volume = ScriptableObject.CreateInstance<PhysicallyBasedSkyVolume>();

            try
            {
                var initialHash = volume.GetHashCode();
                volume.exposure.value = 4.0f;

                Assert.That(volume.GetHashCode(), Is.EqualTo(initialHash));
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void GetHashCode_Changes_WhenFogSettingsChange()
        {
            var volume = ScriptableObject.CreateInstance<PhysicallyBasedSkyVolume>();

            try
            {
                var initialHash = volume.GetHashCode();
                volume.enableHeightFog.value = true;
                volume.fogDensity.value = 0.125f;

                Assert.That(volume.GetHashCode(), Is.Not.EqualTo(initialHash));
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }
    }
}
