using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using UnityRenderGraph = UnityEngine.Rendering.RenderGraphModule.RenderGraph;

namespace VividRP.Editor.Tests
{
    public sealed class FrameResourceTableTests
    {
        private const string TestSlotName = "FrameResourceTableTests.DeclarativeSlot";

        private static readonly FrameResourceSlotId s_TestTextureSlot =
            FrameResourceSlotId.Create("FrameResourceTableTests.Texture");

        private static readonly FrameResourceSlotId s_TestBufferSlot =
            FrameResourceSlotId.Create("FrameResourceTableTests.Buffer");

        private static readonly FrameResourceSlotId s_TestExtractionSlot =
            FrameResourceSlotId.Create("FrameResourceTableTests.Extraction");

        private sealed class DummyComputePass : ComputePass
        {
            public override void Create() { }
            public override void Prepare(ContextContainer frameData) { }
            public override void Record(ComputePassContext context) { }
            public override void Dispose() { }
        }

        [FrameResourceSlot(TestSlotName, AccessFlags.Write)]
        private sealed class SlotWriterPass : ComputePass
        {
            [RenderGraphResource(Access = AccessFlags.Write)]
            private RenderGraphTexture m_UnusedOutput = new();

            public override void Create() { }
            public override void Prepare(ContextContainer frameData) { }
            public override void Record(ComputePassContext context) { }
            public override void Dispose() { }
        }

        [FrameResourceSlot(TestSlotName, AccessFlags.ReadWrite)]
        private sealed class SlotModifierPass : ComputePass
        {
            [RenderGraphResource(Access = AccessFlags.Write)]
            private RenderGraphTexture m_UnusedOutput = new();

            public override void Create() { }
            public override void Prepare(ContextContainer frameData) { }
            public override void Record(ComputePassContext context) { }
            public override void Dispose() { }
        }

        [FrameResourceSlot(TestSlotName, AccessFlags.Read)]
        private sealed class SlotReaderSinkPass : ComputePass
        {
            public override void Create() { }
            public override void Prepare(ContextContainer frameData) { }
            public override void Record(ComputePassContext context) { }
            public override void Dispose() { }
        }

        [CameraHistoryAccess("TestHistory", AccessFlags.Write)]
        private sealed class HistoryWriterPass : ComputePass
        {
            [RenderGraphResource(Access = AccessFlags.Write)]
            private RenderGraphTexture m_UnusedOutput = new();

            public override void Create() { }
            public override void Prepare(ContextContainer frameData) { }
            public override void Record(ComputePassContext context) { }
            public override void Dispose() { }
        }

        private sealed class RecordingExtractor : IFrameTextureExtractor
        {
            public int CallCount;
            public bool Accept = true;
            public TextureHandle LastSourceHandle;

            public bool TryExtractTexture(
                UnityRenderGraph renderGraph,
                FrameResourceSlotId slot,
                RenderGraphTexture sourceTexture,
                TextureHandle sourceHandle,
                in FrameResourceExtractionContext context)
            {
                CallCount++;
                LastSourceHandle = sourceHandle;
                return Accept;
            }
        }

        [SetUp]
        public void SetUp()
        {
            CameraHistorySystem.Dispose();
            PassRecorder.AbortFrame();
        }

        [TearDown]
        public void TearDown()
        {
            PassRecorder.AbortFrame();
            CameraHistorySystem.Dispose();
        }

        [Test]
        public void CreateAndUseTextureSlot_CreatesCoreRpFrameResource_AndMergesPassAccess()
        {
            var renderGraph = new UnityRenderGraph("FrameResourceTable Texture Test");
            var producer = new DummyComputePass();
            var consumer = new DummyComputePass();
            var frameData = new ContextContainer();

            try
            {
                SetCurrentRenderGraph(renderGraph);
                var table = FrameResourceTable.GetOrCreate(frameData);
                var desc = new TextureDesc(32, 16)
                {
                    colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    enableRandomWrite = true,
                    name = "TestFrameSlotTexture",
                };

                var created = table.CreateAndPublishTextureForPass(
                    producer,
                    s_TestTextureSlot,
                    in desc,
                    AccessFlags.Write);
                Assert.That(created.IsValid(), Is.True);
                Assert.That(table.TextureCount, Is.EqualTo(1));

                // Re-registering the same handle on the same pass merges AccessFlags without duplicating entries.
                Assert.That(
                    table.UseTextureSlotForPass(producer, s_TestTextureSlot, AccessFlags.Read, out var merged),
                    Is.True);
                Assert.That(merged, Is.EqualTo(created));
                Assert.That(GetRegisteredPassHandleCount(producer), Is.EqualTo(1));
                Assert.That(GetRegisteredPassHandleAccess(producer, 0), Is.EqualTo(AccessFlags.ReadWrite));

                // Consumer pass resolves the exact same TextureHandle.
                Assert.That(
                    table.UseTextureSlotForPass(consumer, s_TestTextureSlot, AccessFlags.Read, out var consumed),
                    Is.True);
                Assert.That(consumed, Is.EqualTo(created));
                Assert.That(GetRegisteredPassHandleCount(consumer), Is.EqualTo(1));
                Assert.That(GetRegisteredPassHandleAccess(consumer, 0), Is.EqualTo(AccessFlags.Read));

                table.Reset();
                Assert.That(table.TextureCount, Is.EqualTo(0));
                Assert.That(table.TryGetTexture(s_TestTextureSlot, out _), Is.False);
            }
            finally
            {
                PassRecorder.AbortFrame();
                renderGraph.Cleanup();
            }
        }

        [Test]
        public void CreateAndUseBufferSlot_SharesCoreRpFrameBufferAcrossPasses()
        {
            var renderGraph = new UnityRenderGraph("FrameResourceTable Buffer Test");
            var producer = new DummyComputePass();
            var consumer = new DummyComputePass();
            var frameData = new ContextContainer();

            try
            {
                SetCurrentRenderGraph(renderGraph);
                var table = FrameResourceTable.GetOrCreate(frameData);
                var desc = new BufferDesc(16, sizeof(uint), GraphicsBuffer.Target.Structured)
                {
                    name = "TestFrameSlotBuffer",
                };

                var created = table.CreateAndPublishBufferForPass(
                    producer,
                    s_TestBufferSlot,
                    in desc,
                    AccessFlags.Write);
                Assert.That(created.IsValid(), Is.True);
                Assert.That(table.BufferCount, Is.EqualTo(1));

                Assert.That(
                    table.UseBufferSlotForPass(consumer, s_TestBufferSlot, AccessFlags.Read, out var consumed),
                    Is.True);
                Assert.That(PassRecorder.AreImportedBufferHandlesEqual(consumed, created), Is.True);
                Assert.That(GetRegisteredPassHandleCount(consumer), Is.EqualTo(1));
            }
            finally
            {
                PassRecorder.AbortFrame();
                renderGraph.Cleanup();
            }
        }

        [Test]
        public void VividSubstrateMaterialFrameData_UsesFrameResourceTable_WithoutCameraHistoryAllocation()
        {
            using var cameraScope = new CameraScope("SubstrateFrameTableCamera");
            var renderGraph = new UnityRenderGraph("Substrate FrameResourceTable Test");
            var writer1 = new DummyComputePass();
            var writer2 = new DummyComputePass();
            var reader = new DummyComputePass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.SetCamera(cameraScope.Camera);

            var history = cameraScope.Camera.GetVividCameraHistory();
            history.BeginFrame(64, 32);

            try
            {
                SetCurrentRenderGraph(renderGraph);

                bool firstOk = VividSubstrateMaterialFrameData.EnsureAllocatedForWrite(
                    writer1,
                    frameData,
                    cameraData,
                    64,
                    32,
                    16,
                    out var substrate1,
                    out var handle1,
                    out bool isFirst1);

                Assert.That(firstOk, Is.True);
                Assert.That(isFirst1, Is.True);
                Assert.That(handle1.IsValid(), Is.True);
                Assert.That(substrate1.IsValid, Is.True);
                Assert.That(substrate1.WriterCount, Is.EqualTo(1));
                Assert.That(history.TextureCount, Is.EqualTo(0),
                    "Same-frame Substrate Closure Buffer must not allocate persistent CameraHistory VRAM.");

                bool secondOk = VividSubstrateMaterialFrameData.EnsureAllocatedForWrite(
                    writer2,
                    frameData,
                    cameraData,
                    64,
                    32,
                    16,
                    out var substrate2,
                    out var handle2,
                    out bool isFirst2);

                Assert.That(secondOk, Is.True);
                Assert.That(isFirst2, Is.False);
                Assert.That(substrate2, Is.SameAs(substrate1));
                Assert.That(handle2, Is.EqualTo(handle1));
                Assert.That(substrate2.WriterCount, Is.EqualTo(2));

                bool readerHasContent = VividSubstrateMaterialFrameData.ImportClosureBufferOrDummyForPass(
                    reader,
                    frameData,
                    64,
                    32,
                    out var readHandle);

                Assert.That(readerHasContent, Is.True);
                Assert.That(readHandle, Is.EqualTo(handle1));
                Assert.That(history.TextureCount, Is.EqualTo(0));
            }
            finally
            {
                PassRecorder.AbortFrame();
                history.AbortFrame();
                renderGraph.Cleanup();
            }
        }

        [Test]
        public void FrameResourceTable_SteadyStatePublishLookupReset_AllocatesZeroManagedBytes()
        {
            var renderGraph = new UnityRenderGraph("FrameResourceTable ZeroGC Test");
            var frameData = new ContextContainer();

            try
            {
                SetCurrentRenderGraph(renderGraph);
                var table = FrameResourceTable.GetOrCreate(frameData);
                var desc = new TextureDesc(16, 16)
                {
                    colorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                    name = "ZeroGcSlot",
                };
                var handle = PassRecorder.CreateTextureHandle(in desc);

                // Warmup
                for (int i = 0; i < 8; i++)
                {
                    table.Reset();
                    table.PublishTexture(s_TestTextureSlot, handle);
                    table.TryGetTexture(s_TestTextureSlot, out _);
                }

                long before = GC.GetAllocatedBytesForCurrentThread();
                bool ok = true;
                for (int i = 0; i < 64; i++)
                {
                    table.Reset();
                    table.PublishTexture(s_TestTextureSlot, handle);
                    ok &= table.TryGetTexture(s_TestTextureSlot, out _);
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(ok, Is.True);
                Assert.That(allocated, Is.Zero, "Steady-state FrameResourceTable publish/lookup/reset must allocate zero managed memory.");
            }
            finally
            {
                PassRecorder.AbortFrame();
                renderGraph.Cleanup();
            }
        }

        [Test]
        public void TextureExtractor_RunsOnceWhenAccepted_AndZeroAllocatesInSteadyState()
        {
            var renderGraph = new UnityRenderGraph("FrameResourceTable Extractor Test");
            var frameData = new ContextContainer();
            var extractor = new RecordingExtractor();
            var sourceTexture = RenderGraphTexture.CreateColorTarget("SceneColor", GraphicsFormat.R16G16B16A16_SFloat);
            var textureCache = new Dictionary<RenderGraphTexture, TextureHandle>(4);

            try
            {
                SetCurrentRenderGraph(renderGraph);
                var table = FrameResourceTable.GetOrCreate(frameData);
                var desc = new TextureDesc(16, 16)
                {
                    colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    name = "SceneColor",
                };
                var handle = PassRecorder.CreateTextureHandle(in desc);
                textureCache[sourceTexture] = handle;

                table.RegisterTextureExtractor(s_TestExtractionSlot, extractor);
                Assert.That(table.HasPendingTextureExtractors, Is.True);

                var context = new FrameResourceExtractionContext(renderGraph, textureCache);
                extractor.Accept = false;
                Assert.That(
                    table.TryRunTextureExtractors(s_TestExtractionSlot, renderGraph, sourceTexture, handle, in context),
                    Is.EqualTo(0));
                Assert.That(table.HasPendingTextureExtractors, Is.True);
                Assert.That(extractor.CallCount, Is.EqualTo(1));

                extractor.Accept = true;
                Assert.That(
                    table.TryRunTextureExtractors(s_TestExtractionSlot, renderGraph, sourceTexture, handle, in context),
                    Is.EqualTo(1));
                Assert.That(table.HasPendingTextureExtractors, Is.False);
                Assert.That(extractor.CallCount, Is.EqualTo(2));
                Assert.That(extractor.LastSourceHandle, Is.EqualTo(handle));

                // Second call after consumption must be a no-op.
                Assert.That(
                    table.TryRunTextureExtractors(s_TestExtractionSlot, renderGraph, sourceTexture, handle, in context),
                    Is.EqualTo(0));
                Assert.That(extractor.CallCount, Is.EqualTo(2));

                // Warmup steady-state reset + register + run
                for (int i = 0; i < 8; i++)
                {
                    table.Reset();
                    table.RegisterTextureExtractor(s_TestExtractionSlot, extractor);
                    table.TryRunTextureExtractors(s_TestExtractionSlot, renderGraph, sourceTexture, handle, in context);
                }

                long before = GC.GetAllocatedBytesForCurrentThread();
                int total = 0;
                for (int i = 0; i < 64; i++)
                {
                    table.Reset();
                    table.RegisterTextureExtractor(s_TestExtractionSlot, extractor);
                    total += table.TryRunTextureExtractors(s_TestExtractionSlot, renderGraph, sourceTexture, handle, in context);
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(total, Is.EqualTo(64));
                Assert.That(allocated, Is.Zero, "Steady-state extractor register/run/reset must allocate zero managed memory.");
            }
            finally
            {
                PassRecorder.AbortFrame();
                renderGraph.Cleanup();
            }
        }

        [Test]
        public void AutoMarkWritten_MarksRegisteredHistory_OnlyWhenPassExecutes()
        {
            using var cameraScope = new CameraScope("AutoMarkHistoryCamera");
            var renderGraph = new UnityRenderGraph("AutoMarkHistory Test");
            var pass = new DummyComputePass();
            var inactivePass = new DummyComputePass();
            var history = cameraScope.Camera.GetVividCameraHistory();
            var activeId = CameraHistoryId.Create("FrameResourceTableTests.AutoMarkActive");
            var inactiveId = CameraHistoryId.Create("FrameResourceTableTests.AutoMarkInactive");
            var desc = new CameraHistoryTextureDescriptor(16, 16, GraphicsFormat.R8G8B8A8_UNorm, enableRandomWrite: true);

            history.BeginFrame(16, 16);
            try
            {
                SetCurrentRenderGraph(renderGraph);
                var activeTex = history.GetOrCreateTexture(activeId, 2, desc);
                var inactiveTex = history.GetOrCreateTexture(inactiveId, 2, desc);

                var activeHandle = CameraHistoryRenderGraphBridge.ImportForPass(
                    pass, activeTex, 0, AccessFlags.ReadWrite, autoMarkWritten: true);
                var inactiveHandle = CameraHistoryRenderGraphBridge.ImportForPass(
                    inactivePass, inactiveTex, 0, AccessFlags.ReadWrite, autoMarkWritten: true);

                Assert.That(activeHandle.IsValid(), Is.True);
                Assert.That(inactiveHandle.IsValid(), Is.True);

                // Only execute `pass`; `inactivePass` never runs.
                PassRecorder.MarkRegisteredHistoriesWritten(pass);
                history.CommitFrame();

                history.BeginFrame(16, 16);
                Assert.That(activeTex.IsValid(1), Is.True, "Executed pass must automatically commit its registered history write.");
                Assert.That(inactiveTex.IsValid(1), Is.False, "Unexecuted pass must not mark its registered history as written.");
                history.AbortFrame();
            }
            finally
            {
                PassRecorder.AbortFrame();
                history.AbortFrame();
                renderGraph.Cleanup();
            }
        }

        [Test]
        public void FrameResourceSlotAttribute_OrdersWriteBeforeReadWriteBeforeRead_AndPreservesLiveness()
        {
            var passDefinitions = new List<RenderGraphPassDefinition>
            {
                new() { PassType = typeof(SlotReaderSinkPass).AssemblyQualifiedName },
                new() { PassType = typeof(SlotModifierPass).AssemblyQualifiedName },
                new() { PassType = typeof(SlotWriterPass).AssemblyQualifiedName },
            };

            var ordered = RenderGraphPassCompilationUtility.OrderPassDefinitions(passDefinitions);
            Assert.That(ordered.Count, Is.EqualTo(3));
            Assert.That(ordered[0].PassType, Is.EqualTo(typeof(SlotWriterPass).AssemblyQualifiedName));
            Assert.That(ordered[1].PassType, Is.EqualTo(typeof(SlotModifierPass).AssemblyQualifiedName));
            Assert.That(ordered[2].PassType, Is.EqualTo(typeof(SlotReaderSinkPass).AssemblyQualifiedName));

            var live = ordered.GetLivePassIndices();
            Assert.That(live, Is.EqualTo(new[] { 0, 1, 2 }),
                "Live slot consumer must keep both Write and ReadWrite slot producers alive without .vrdg wires.");
        }

        [Test]
        public void CameraHistoryAccessAttribute_MarksWriterAsLiveRoot_WithoutSideEffectInterface()
        {
            var passDefinitions = new List<RenderGraphPassDefinition>
            {
                new() { PassType = typeof(HistoryWriterPass).AssemblyQualifiedName },
            };

            var live = passDefinitions.GetLivePassIndices();
            Assert.That(live, Is.EqualTo(new[] { 0 }),
                "Pass declaring CameraHistoryAccess Write must be treated as a live root during static culling.");
        }

        private static void SetCurrentRenderGraph(UnityRenderGraph renderGraph)
        {
            var field = typeof(PassRecorder).GetField(
                "s_CurrentRenderGraph",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null);
            field.SetValue(null, renderGraph);
        }

        private static int GetRegisteredPassHandleCount(IRenderPass pass)
        {
            var dict = GetPassImportedHandlesDictionary();
            return dict.Contains(pass) && dict[pass] is IList list ? list.Count : 0;
        }

        private static AccessFlags GetRegisteredPassHandleAccess(IRenderPass pass, int index)
        {
            var dict = GetPassImportedHandlesDictionary();
            var list = (IList)dict[pass];
            var entry = list[index];
            var prop = entry.GetType().GetProperty("Access", BindingFlags.Public | BindingFlags.Instance);
            Assert.That(prop, Is.Not.Null);
            return (AccessFlags)prop.GetValue(entry);
        }

        private static IDictionary GetPassImportedHandlesDictionary()
        {
            var field = typeof(PassRecorder).GetField(
                "s_PassImportedHandles",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null);
            return (IDictionary)field.GetValue(null);
        }

        private sealed class CameraScope : IDisposable
        {
            private readonly GameObject m_GameObject;

            internal CameraScope(string name)
            {
                m_GameObject = new GameObject(name);
                Camera = m_GameObject.AddComponent<Camera>();
            }

            internal Camera Camera { get; }

            public void Dispose()
            {
                if (m_GameObject != null)
                    UnityEngine.Object.DestroyImmediate(m_GameObject);
            }
        }
    }
}
