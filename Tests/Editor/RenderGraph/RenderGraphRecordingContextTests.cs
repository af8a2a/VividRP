using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using UnityRenderGraph = UnityEngine.Rendering.RenderGraphModule.RenderGraph;

namespace VividRP.Editor.Tests
{
    public sealed class RenderGraphRecordingContextTests
    {
        [Test]
        public void Constructor_DoesNotAllocate()
        {
            var renderGraph = new UnityRenderGraph("VividRP RenderGraphRecordingContext Allocation Test");
            var frameData = new ContextContainer();
            var textureCache = new Dictionary<RenderGraphTexture, TextureHandle>(1);
            var bufferCache = new Dictionary<RenderGraphBuffer, BufferHandle>(1);
            var renderListCache = new Dictionary<RenderGraphRenderList, RendererListHandle>(1);
            var accelerationStructureCache =
                new Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle>(1);

            try
            {
                _ = new RenderGraphRecordingContext(
                    renderGraph,
                    frameData,
                    null,
                    false,
                    textureCache,
                    bufferCache,
                    renderListCache,
                    accelerationStructureCache);

                GC.Collect();
                var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                for (var index = 0; index < 32; index++)
                {
                    _ = new RenderGraphRecordingContext(
                        renderGraph,
                        frameData,
                        null,
                        false,
                        textureCache,
                        bufferCache,
                        renderListCache,
                        accelerationStructureCache);
                }

                var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                Assert.That(allocatedBytes, Is.Zero);
            }
            finally
            {
                renderGraph.Cleanup();
            }
        }
    }
}
