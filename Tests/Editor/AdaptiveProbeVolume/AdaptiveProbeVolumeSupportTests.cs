using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class AdaptiveProbeVolumeSupportTests
    {
        [Test]
        public void PipelineAsset_ImplementsProbeVolumeEnabledInterface_WithExpectedDefaults()
        {
            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();

            try
            {
                var probeVolumeAsset = asset as IProbeVolumeEnabledRenderPipeline;

                Assert.That(probeVolumeAsset, Is.Not.Null);
                Assert.That(probeVolumeAsset.supportProbeVolume, Is.False);
                Assert.That(probeVolumeAsset.maxSHBands, Is.EqualTo(ProbeVolumeSHBands.SphericalHarmonicsL2));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}
