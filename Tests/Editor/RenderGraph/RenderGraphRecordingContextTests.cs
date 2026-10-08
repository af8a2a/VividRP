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
        private sealed class AccelerationStructurePassData { }

        [TestCase(false, AccessFlags.Read)]
        [TestCase(false, AccessFlags.Write)]
        [TestCase(false, AccessFlags.ReadWrite)]
        [TestCase(true, AccessFlags.Read)]
        [TestCase(true, AccessFlags.Write)]
        [TestCase(true, AccessFlags.ReadWrite)]
        public void AccelerationStructureBinding_PreservesDependenciesWithoutAllocating(bool unsafePass, AccessFlags access)
        {
            var graph = new UnityRenderGraph("RTAS binding allocation check");
            var resources = new RenderGraphResourceRegistry(new RenderGraphDebugParams());
            var builder = new RenderGraphBuilders();
            RenderGraphPass pass;
            if (unsafePass)
            {
                var typedPass = new UnsafeRenderGraphPass<AccelerationStructurePassData>();
                typedPass.Initialize(0, new AccelerationStructurePassData(), "RTAS", RenderGraphPassType.Unsafe, null);
                pass = typedPass;
            }
            else
            {
                var typedPass = new ComputeRenderGraphPass<AccelerationStructurePassData>();
                typedPass.Initialize(0, new AccelerationStructurePassData(), "RTAS", RenderGraphPassType.Compute, null);
                pass = typedPass;
            }

            try
            {
                resources.BeginRenderGraph(1);
                // Dependency registration only: no native RTAS or GPU execution is required.
                RayTracingAccelerationStructure native = null;
                var handle = resources.ImportRayTracingAccelerationStructure(in native, "RTAS");
                builder.Setup(pass, resources, graph, null);
                for (var i = 0; i < 32; i++)
                {
                    pass.Clear();
                    PassRecorder.UseAccelerationStructure(builder, handle, access);
                }
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 256; i++)
                {
                    pass.Clear();
                    PassRecorder.UseAccelerationStructure(builder, handle, access);
                }
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                var resourceType = (int)RenderGraphResourceType.AccelerationStructure;
                Assert.That(allocated, Is.Zero);
                Assert.That(pass.resourceReadLists[resourceType].Count, Is.EqualTo((access & AccessFlags.Read) != 0 ? 1 : 0));
                Assert.That(pass.resourceWriteLists[resourceType].Count, Is.EqualTo((access & AccessFlags.Write) != 0 ? 1 : 0));
                Assert.That(pass.implicitReadsList.Count, Is.Zero);
            }
            finally
            {
                builder.Dispose();
                resources.Clear(false);
                resources.Cleanup();
                graph.Cleanup();
            }
        }

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
