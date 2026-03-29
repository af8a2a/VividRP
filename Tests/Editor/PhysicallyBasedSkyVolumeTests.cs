using NUnit.Framework;
using UnityEngine;
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
    }
}
