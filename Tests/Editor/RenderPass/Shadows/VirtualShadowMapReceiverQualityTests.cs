using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualShadowMapReceiverQualityTests
    {
        [Test]
        public void Volume_DefaultsToLegacyAndClampsIndependentQualityInputs()
        {
            var settings = ScriptableObject.CreateInstance<CascadedShadowSettingsVolume>();
            try
            {
                Assert.That(settings.virtualShadowMapScreenDensity.value, Is.False);
                Assert.That(settings.virtualShadowMapTargetTexelPixels.value, Is.EqualTo(1));
                Assert.That(settings.virtualShadowMapResolutionLodBias.value, Is.Zero);
                Assert.That(VirtualShadowMapReceiverQuality.BuildParameters(settings), Is.EqualTo(new Vector4(0, 1, 0, 0)));
                settings.virtualShadowMapTargetTexelPixels.value = 0;
                settings.virtualShadowMapResolutionLodBias.value = -100;
                Assert.That(settings.virtualShadowMapTargetTexelPixels.value, Is.EqualTo(0.25f));
                Assert.That(settings.virtualShadowMapResolutionLodBias.value, Is.EqualTo(-4));
                settings.virtualShadowMapTargetTexelPixels.value = 100;
                settings.virtualShadowMapResolutionLodBias.value = 100;
                Assert.That(settings.virtualShadowMapTargetTexelPixels.value, Is.EqualTo(8));
                Assert.That(settings.virtualShadowMapResolutionLodBias.value, Is.EqualTo(4));
                Assert.That(VirtualShadowMapReceiverQuality.BuildParameters(true, 1, -1).y, Is.EqualTo(0.5f));
                Assert.That(VirtualShadowMapReceiverQuality.BuildParameters(true, 1, 1).y, Is.EqualTo(2));
            }
            finally { UnityEngine.Object.DestroyImmediate(settings); }
        }

        [Test]
        public void QualityUniformChanges_KeepProjectionBuffersPageOriginsAndGeneration()
        {
            var layout = new VirtualShadowMapClipmapLayout();
            using var projections = new VirtualShadowMapProjectionSet();
            using var cmd = new CommandBuffer();
            var bounds = new Bounds(Vector3.zero, Vector3.one * 100);
            layout.Update(Vector3.zero, Quaternion.identity, bounds, 150, 2048, 1, 1, 1, 2);
            projections.PrepareClipmaps(layout);
            projections.CommitRecordedLayout();
            var buffer = projections.Buffer;
            var generation = projections.Generation;
            var matrix = layout.Projections[0] * layout.Views[0];
            var origin = layout.OriginX[0];
            for (int i = -4; i <= 4; i++)
            {
                // The only quality input to rendering is a receiver uniform;
                // camera FOV/output-size matrices never enter the layout producer.
                var quality = VirtualShadowMapReceiverQuality.BuildParameters(true, 1, i);
                cmd.SetGlobalVector(VirtualShadowMapReceiverQuality.ParametersId, quality);
                layout.Update(Vector3.zero, Quaternion.identity, bounds, 150, 2048, 1, 1, 1, 2);
                projections.PrepareClipmaps(layout);
                Assert.That(projections.Buffer, Is.SameAs(buffer));
                Assert.That(projections.Generation, Is.EqualTo(generation));
                Assert.That(projections.RequiresRemap || projections.RequiresFeedbackReset, Is.False);
                Assert.That(layout.Projections[0] * layout.Views[0], Is.EqualTo(matrix));
                Assert.That(layout.OriginX[0], Is.EqualTo(origin));
            }
        }

        [Test]
        public void StableQualityParameterPreparation_AllocatesZeroBytes()
        {
            using var cmd = new CommandBuffer();
            for (int i = 0; i < 32; i++) RecordQuality(cmd);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++) RecordQuality(cmd);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        private static void RecordQuality(CommandBuffer cmd)
        {
            cmd.Clear();
            cmd.SetGlobalVector(VirtualShadowMapReceiverQuality.ParametersId,
                VirtualShadowMapReceiverQuality.BuildParameters(true, 1, -1));
            cmd.SetGlobalMatrix(VirtualShadowMapReceiverQuality.ViewProjectionId, Matrix4x4.identity);
        }
    }
}
