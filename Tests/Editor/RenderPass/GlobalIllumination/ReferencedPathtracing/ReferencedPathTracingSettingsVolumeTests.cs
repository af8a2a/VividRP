using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class ReferencedPathTracingSettingsVolumeTests
    {
        [Test]
        public void Defaults_EnableHdriLightingVisibilityImportanceSamplingAndMis()
        {
            var volume = ScriptableObject.CreateInstance<ReferencedPathTracingSettingsVolume>();

            try
            {
                Assert.That(volume.environmentLighting.value, Is.True);
                Assert.That(volume.environmentCameraVisible.value, Is.True);
                Assert.That(
                    volume.environmentSamplingMode.value,
                    Is.EqualTo(
                        ReferencedPathTracingEnvironmentSamplingMode.ImportanceSampling));
                Assert.That(
                    volume.environmentEstimatorMode.value,
                    Is.EqualTo(ReferencedPathTracingEnvironmentEstimatorMode.Mis));
                Assert.That(
                    volume.environmentDebugMode.value,
                    Is.EqualTo(ReferencedPathTracingEnvironmentDebugMode.Combined));
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }
    }
}
