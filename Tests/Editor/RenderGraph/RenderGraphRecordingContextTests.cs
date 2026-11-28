using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using UnityRenderGraph = UnityEngine.Rendering.RenderGraphModule.RenderGraph;

namespace VividRP.Editor.Tests
{
    public sealed class RenderGraphRecordingContextTests
    {
        private sealed class AccelerationStructurePassData { }

        private sealed class PureProducerComputePass : ComputePass
        {
            [RenderGraphResource(Name = "Output", Access = AccessFlags.Write)]
            internal RenderGraphTexture Output = RenderGraphTexture.CreateOutput("PureProducerOutput", GraphicsFormat.R8G8B8A8_UNorm);

            public int RecordCallCount;

            public override void Create() { }
            public override void Prepare(ContextContainer frameData) { }
            public override void Record(ComputePassContext context) => RecordCallCount++;
            public override void Dispose() { }
        }

        private sealed class SideEffectComputePass : ComputePass, IRenderGraphSideEffectPass
        {
            [RenderGraphResource(Name = "Input", Access = AccessFlags.Read)]
            internal RenderGraphTexture Input;

            [RenderGraphResource(Name = "Output", Access = AccessFlags.Write)]
            internal RenderGraphTexture Output = RenderGraphTexture.CreateOutput("SideEffectOutput", GraphicsFormat.R8G8B8A8_UNorm);

            public int RecordCallCount;

            public override void Create() { }
            public override void Prepare(ContextContainer frameData) { }
            public override void Record(ComputePassContext context) => RecordCallCount++;
            public override void Dispose() { }
        }

        private sealed class GlobalStateComputePass : ComputePass, IAllowGlobalStateModificationPass
        {
            [RenderGraphResource(Name = "Output", Access = AccessFlags.Write)]
            internal RenderGraphTexture Output = RenderGraphTexture.CreateOutput("GlobalStateOutput", GraphicsFormat.R8G8B8A8_UNorm);

            public override void Create() { }
            public override void Prepare(ContextContainer frameData) { }
            public override void Record(ComputePassContext context) { }
            public override void Dispose() { }
        }

        [CameraHistoryAccess("TestHistory", AccessFlags.Write)]
        private sealed class HistoryWriterComputePass : ComputePass
        {
            [RenderGraphResource(Name = "Output", Access = AccessFlags.Write)]
            internal RenderGraphTexture Output = RenderGraphTexture.CreateOutput("HistoryOutput", GraphicsFormat.R8G8B8A8_UNorm);

            public override void Create() { }
            public override void Prepare(ContextContainer frameData) { }
            public override void Record(ComputePassContext context) { }
            public override void Dispose() { }
        }

        private sealed class MultiSubPassRecordingPass : ComputePass, IRenderGraphRecordingPass, IRenderGraphSideEffectPass
        {
            private sealed class ChildPassData
            {
                public MultiSubPassRecordingPass Owner;
            }

            private static readonly BaseRenderFunc<ChildPassData, ComputeGraphContext> s_ChildRenderFunc = ExecuteChild;

            public readonly List<string> ExecutionLog = new();

            public override void Create() { }
            public override void Prepare(ContextContainer frameData) { }
            public override void Record(ComputePassContext context) => ExecutionLog.Add("Primary");
            public override void Dispose() { }

            public void RecordGraph(RenderGraphRecordingContext context)
            {
                context.RecordDefaultComputePass(this, "PrimarySubPass");
                using var builder = context.RenderGraph.AddComputePass<ChildPassData>("ChildSubPass", out var data);
                data.Owner = this;
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(s_ChildRenderFunc);
            }

            private static void ExecuteChild(ChildPassData data, ComputeGraphContext context)
            {
                data.Owner.ExecutionLog.Add("Child");
            }
        }

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

        [Test]
        public void ShouldAllowPassCulling_ReturnsTrueForPureProducers_AndFalseForSideEffectHistoryAndSinks_ZeroGC()
        {
            var pureProducer = new PureProducerComputePass();
            var pureResources = ((IRenderPass)pureProducer).Initialize();
            var sideEffect = new SideEffectComputePass();
            var sideEffectResources = ((IRenderPass)sideEffect).Initialize();
            var globalState = new GlobalStateComputePass();
            var globalStateResources = ((IRenderPass)globalState).Initialize();
            var historyWriter = new HistoryWriterComputePass();
            var historyResources = ((IRenderPass)historyWriter).Initialize();
            var finalBlit = new FinalBlitPass();
            var finalBlitResources = ((IRenderPass)finalBlit).Initialize();

            Assert.That(PassRecorder.ShouldAllowPassCulling(pureProducer, pureResources), Is.True);
            Assert.That(PassRecorder.ShouldAllowPassCulling(sideEffect, sideEffectResources), Is.False);
            Assert.That(PassRecorder.ShouldAllowPassCulling(globalState, globalStateResources), Is.False);
            Assert.That(PassRecorder.ShouldAllowPassCulling(historyWriter, historyResources), Is.False);
            Assert.That(PassRecorder.ShouldAllowPassCulling(finalBlit, finalBlitResources), Is.False);

            // Warmup type cache then verify zero managed allocation in steady state.
            for (int i = 0; i < 8; i++)
            {
                PassRecorder.ShouldAllowPassCulling(pureProducer, pureResources);
                PassRecorder.ShouldAllowPassCulling(historyWriter, historyResources);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            bool result = true;
            for (int i = 0; i < 64; i++)
            {
                result &= PassRecorder.ShouldAllowPassCulling(pureProducer, pureResources);
                result &= !PassRecorder.ShouldAllowPassCulling(historyWriter, historyResources);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(result, Is.True);
            Assert.That(allocated, Is.Zero, "ShouldAllowPassCulling must allocate zero managed memory after warmup.");
        }

        [Test]
        public void RecordComputePass_SetsCoreRpAllowPassCulling_BasedOnPassSideEffects()
        {
            var renderGraph = new UnityRenderGraph("VividRP Dynamic Pass Culling Flag Test");
            var cmd = new CommandBuffer();
            var textureCache = new Dictionary<RenderGraphTexture, TextureHandle>(4);
            var bufferCache = new Dictionary<RenderGraphBuffer, BufferHandle>(4);
            var renderListCache = new Dictionary<RenderGraphRenderList, RendererListHandle>(4);
            var rtasCache = new Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle>(4);

            var pureProducer = new PureProducerComputePass();
            var pureResources = ((IRenderPass)pureProducer).Initialize();
            var sideEffect = new SideEffectComputePass { Input = pureProducer.Output };
            var sideEffectResources = ((IRenderPass)sideEffect).Initialize();

            try
            {
                renderGraph.BeginRecording(new RenderGraphParameters
                {
                    commandBuffer = cmd,
                    currentFrameIndex = 1,
                });

                PassRecorder.RecordComputePass(
                    renderGraph, pureProducer, pureResources, null, false,
                    textureCache, bufferCache, renderListCache, rtasCache, "PureProducer");
                PassRecorder.RecordComputePass(
                    renderGraph, sideEffect, sideEffectResources, null, false,
                    textureCache, bufferCache, renderListCache, rtasCache, "SideEffectConsumer");

                var passes = GetRecordedRenderGraphPasses(renderGraph);
                Assert.That(passes.Count, Is.EqualTo(2));
                Assert.That(passes[0].allowPassCulling, Is.True, "Pure graph producer must allow CoreRP dynamic pass culling.");
                Assert.That(passes[1].allowPassCulling, Is.False, "Side-effect pass must disable CoreRP pass culling.");
            }
            finally
            {
                cmd.Dispose();
                renderGraph.Cleanup();
            }
        }

        [Test]
        public void MultiSubPassRecording_RecordsPrimaryAndChildSubPassesInOrder()
        {
            var renderGraph = new UnityRenderGraph("VividRP Multi SubPass Test");
            var cmd = new CommandBuffer();
            var frameData = new ContextContainer();
            var textureCache = new Dictionary<RenderGraphTexture, TextureHandle>(4);
            var bufferCache = new Dictionary<RenderGraphBuffer, BufferHandle>(4);
            var renderListCache = new Dictionary<RenderGraphRenderList, RendererListHandle>(4);
            var rtasCache = new Dictionary<RenderGraphAccelerationStructure, RayTracingAccelerationStructureHandle>(4);

            var pass = new MultiSubPassRecordingPass();
            var resources = ((IRenderPass)pass).Initialize();

            try
            {
                renderGraph.BeginRecording(new RenderGraphParameters
                {
                    commandBuffer = cmd,
                    currentFrameIndex = 1,
                });

                var ctx = new RenderGraphRecordingContext(
                    renderGraph,
                    frameData,
                    null,
                    false,
                    textureCache,
                    bufferCache,
                    renderListCache,
                    rtasCache,
                    resources,
                    "PrimarySubPass");
                pass.RecordGraph(ctx);

                var passes = GetRecordedRenderGraphPasses(renderGraph);
                Assert.That(passes.Count, Is.EqualTo(2));
                Assert.That(passes[0].name, Is.EqualTo("PrimarySubPass"));
                Assert.That(passes[1].name, Is.EqualTo("ChildSubPass"));
            }
            finally
            {
                cmd.Dispose();
                renderGraph.Cleanup();
            }
        }

        private static List<RenderGraphPass> GetRecordedRenderGraphPasses(UnityRenderGraph renderGraph)
        {
            var field = typeof(UnityRenderGraph).GetField(
                "m_RenderPasses",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.That(field, Is.Not.Null);
            return (List<RenderGraphPass>)field.GetValue(renderGraph);
        }
    }
}
