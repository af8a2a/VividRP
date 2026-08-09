using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualTexturePageTableScatterTests
    {
        [Test]
        public void ScatterUpdate_MatchesUint2ShaderLayout()
        {
            var update = new VTPageTableScatterUpdate(17, 0x8f00aa55u);

            Assert.That(Marshal.SizeOf<VTPageTableScatterUpdate>(), Is.EqualTo(sizeof(uint) * 2));
            Assert.That(update.DestinationIndex, Is.EqualTo(17u));
            Assert.That(update.PackedValue, Is.EqualTo(0x8f00aa55u));
        }

        [Test]
        public void ScatterCompute_WritesKnownPackedValuesToRequestedIndices()
        {
            Assume.That(SystemInfo.supportsComputeShaders, Is.True);
            ComputeShader shader = PipelineResourceManager.Get<VividRPCoreResources>()
                ?.VirtualTexturePageTableScatterCompute;
            Assert.That(shader, Is.Not.Null);

            int kernel = shader.FindKernel("ScatterPageTableUpdates");
            var updates = new[]
            {
                new VTPageTableScatterUpdate(5, 0x11111111u),
                new VTPageTableScatterUpdate(1, 0x89abcdefu),
                new VTPageTableScatterUpdate(6, 0xfedcba98u),
            };
            var destinationValues = new uint[8];
            using var updateBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                updates.Length,
                VTPageTableScatterUpdate.Stride);
            using var destinationBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                destinationValues.Length,
                sizeof(uint));
            updateBuffer.SetData(updates);
            destinationBuffer.SetData(destinationValues);

            shader.SetBuffer(kernel, "_VTPageTableUpdates", updateBuffer);
            shader.SetBuffer(kernel, "_VTPageTableDestination", destinationBuffer);
            shader.SetInt("_VTPageTableUpdateBase", 1);
            shader.SetInt("_VTPageTableUpdateCount", 2);
            shader.Dispatch(kernel, 1, 1, 1);
            destinationBuffer.GetData(destinationValues);

            Assert.That(destinationValues[1], Is.EqualTo(0x89abcdefu));
            Assert.That(destinationValues[6], Is.EqualTo(0xfedcba98u));
            Assert.That(destinationValues[5], Is.Zero);
        }

        [Test]
        public void PipelineResources_ResolvePageTableScatterCompute()
        {
            ComputeShader shader = PipelineResourceManager.Get<VividRPCoreResources>()
                ?.VirtualTexturePageTableScatterCompute;

            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.HasKernel("ScatterPageTableUpdates"), Is.True);
        }
    }
}
