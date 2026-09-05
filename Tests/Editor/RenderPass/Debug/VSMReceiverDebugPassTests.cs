using System;
using NUnit.Framework;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class VSMReceiverDebugPassTests
    {
        [Test]
        public void GeneratedRegistry_ExposesReceiverDebugNode()
        {
            var node = RenderPassNodeRegistry.GetNodeType(typeof(VSMReceiverDebugPass));
            Assert.That(node, Is.Not.Null);
            Assert.That(RenderPassNodeRegistry.GetPassType(node), Is.EqualTo(typeof(VSMReceiverDebugPass)));
        }

        [Test]
        public void Ports_RequireResolvedShadowAndExposeRawData()
        {
            var pass = new VSMReceiverDebugPass();
            try
            {
                var resources = ((IRenderPass)pass).Initialize();
                Assert.That(resources.Textures.Length, Is.EqualTo(5));
                Assert.That(Array.Exists(resources.Textures, x => x.Name == "DirectionalShadowTexture" && x.Access == AccessFlags.Read), Is.True);
                Assert.That(Array.Exists(resources.Textures, x => x.Name == "DiagnosticData" && x.Access == AccessFlags.Write), Is.True);
                Assert.That(pass.VisualizationMode, Is.EqualTo(VSMReceiverDebugMode.TexelFootprint));
                pass.VisualizationMode = (VSMReceiverDebugMode)999;
                Assert.That(pass.VisualizationMode, Is.EqualTo(VSMReceiverDebugMode.TexelFootprint));
            }
            finally { pass.Dispose(); }
        }

        [Test]
        public void Snapshot_RequiresBothRasterAndResolveForExactCameraFrame()
        {
            const ulong camera = 0x10000002aul;
            try
            {
                VirtualShadowMapPrototypeRuntime.BeginFrame();
                VirtualShadowMapPrototypeRuntime.MarkActive();
                VirtualShadowMapPrototypeRuntime.MarkPageDebugSnapshot(camera, 10);
                VirtualShadowMapPrototypeRuntime.MarkReceiverFeedbackProduced(camera, 9);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverDebugSnapshot(camera, 10), Is.False);
                VirtualShadowMapPrototypeRuntime.MarkReceiverFeedbackProduced(camera, 10);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverDebugSnapshot(camera, 10), Is.True);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverDebugSnapshot(0x20000002aul, 10), Is.False);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverDebugSnapshot(camera, 11), Is.False);
                VirtualShadowMapPrototypeRuntime.MarkFallback(VirtualShadowMapPrototypeFallbackReason.ReceiverFeedbackUnavailable);
                Assert.That(VirtualShadowMapPrototypeRuntime.HasReceiverDebugSnapshot(camera, 10), Is.False);
            }
            finally
            {
                VirtualShadowMapPrototypeRuntime.MarkReceiverFeedbackProduced(0, -1);
                VirtualShadowMapPrototypeRuntime.BeginFrame();
            }
        }

        [Test]
        public void StableDiagnosticsAndProfiling_AllocateZeroBytesAfterWarmup()
        {
            var pass = new VSMReceiverDebugPass();
            using var command = new CommandBuffer();
            try
            {
                for (int i = 0; i < 32; i++) Step(pass, command);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 256; i++) Step(pass, command);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.Zero);
            }
            finally { pass.Dispose(); }
        }

        private static void Step(VSMReceiverDebugPass pass, CommandBuffer command)
        {
            pass.ConfigureOutputSize(1920, 1080);
            pass.VisualizationMode = VSMReceiverDebugMode.SamplingWork;
            VirtualShadowMapPrototypeRuntime.HasReceiverDebugSnapshot(42, 10);
            command.Clear();
            using var scope = new ProfilingScope(command, VSMProfiling.Resolve);
            command.BeginSample("VSM.Allocate");
            command.EndSample("VSM.Allocate");
        }
    }
}
