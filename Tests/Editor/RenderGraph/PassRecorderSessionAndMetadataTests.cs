using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Serialization;
using VividRP.Runtime;

namespace VividRP.Tests.Editor.RenderGraphTests
{
    public sealed class PassRecorderSessionAndMetadataTests
    {
        [TearDown]
        public void TearDown()
        {
            PassRecorder.Dispose();
            SessionTrackingPassA.ResetCounters();
            SessionTrackingPassB.ResetCounters();
        }

        [Test]
        public void PassTypeMetadataCache_CompiledAccessors_ReadWriteAndResolveAliases()
        {
            var metadata = PassTypeMetadataCache.GetOrCreate(typeof(MetadataTestPass));
            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata.HasCameraHistoryWrite, Is.True);
            Assert.That(metadata.SlotAccesses.Length, Is.EqualTo(1));
            Assert.That(metadata.HistoryAccesses.Length, Is.EqualTo(1));

            Assert.That(metadata.TryGetResourceField("m_Source", out var sourceMeta), Is.True);
            Assert.That(metadata.TryGetResourceField("m_LegacySource", out var aliasMeta), Is.True);
            Assert.That(aliasMeta, Is.SameAs(sourceMeta));

            Assert.That(metadata.TryGetResourceField("m_Output", out var outputMeta), Is.True);
            Assert.That(outputMeta.BypassAttribute, Is.Not.Null);
            Assert.That(outputMeta.BypassAttribute.SourceFieldName, Is.EqualTo("m_Source"));

            Assert.That(metadata.TryGetResourceField("m_Scratch", out var scratchMeta), Is.True);
            Assert.That(scratchMeta.IsDeclaredTransient, Is.True);
            Assert.That(scratchMeta.IsTransient, Is.True);

            var pass = new MetadataTestPass();
            var replacement = new RenderGraphTexture();
            Assert.That(sourceMeta.Getter(pass), Is.SameAs(pass.Source));

            sourceMeta.Setter(pass, replacement);
            Assert.That(pass.Source, Is.SameAs(replacement));
            Assert.That(sourceMeta.Getter(pass), Is.SameAs(replacement));
        }

        [Test]
        public void PassResourceReferenceRefresh_SteadyState_AllocatesZeroBytes()
        {
            var pass = new MetadataTestPass();
            var texA = new RenderGraphTexture();
            var texB = new RenderGraphTexture();
            pass.SetSource(texA);

            var resources = PassResourceCollector.Collect(pass);
            Assert.That(resources, Is.Not.Null);

            // Warm up compiled getter & refresh path.
            for (var i = 0; i < 4; i++)
            {
                pass.SetSource((i & 1) == 0 ? texB : texA);
                Assert.That(pass.TryRefresh(resources), Is.True);
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 64; i++)
            {
                pass.SetSource((i & 1) == 0 ? texB : texA);
                pass.TryRefresh(resources);
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.EqualTo(0L));
            Assert.That(resources.Textures[0].Texture, Is.SameAs(texA));
        }

        [Test]
        public void EnsureCompiled_SwitchingBetweenGraphs_ReusesSessionsWithoutRecompileOrDisposal()
        {
            SessionTrackingPassA.ResetCounters();
            SessionTrackingPassB.ResetCounters();

            var graphA = CreateGraphAsset<SessionTrackingPassA>("GraphA", importVersion: 1);
            var graphB = CreateGraphAsset<SessionTrackingPassB>("GraphB", importVersion: 1);

            try
            {
                PassRecorder.EnsureCompiled(graphA);
                Assert.That(PassRecorder.CompiledSessionCount, Is.EqualTo(1));
                Assert.That(SessionTrackingPassA.CreateCount, Is.EqualTo(1));
                Assert.That(SessionTrackingPassA.DisposeCount, Is.EqualTo(0));
                var passA = PassRecorder.ActiveSession.RenderPasses[0];

                PassRecorder.EnsureCompiled(graphB);
                Assert.That(PassRecorder.CompiledSessionCount, Is.EqualTo(2));
                Assert.That(SessionTrackingPassB.CreateCount, Is.EqualTo(1));
                Assert.That(SessionTrackingPassA.DisposeCount, Is.EqualTo(0), "Switching graphs must not dispose the previous graph session.");
                var passB = PassRecorder.ActiveSession.RenderPasses[0];

                PassRecorder.EnsureCompiled(graphA);
                Assert.That(PassRecorder.CompiledSessionCount, Is.EqualTo(2));
                Assert.That(SessionTrackingPassA.CreateCount, Is.EqualTo(1), "Switching back to GraphA must reuse the compiled session.");
                Assert.That(SessionTrackingPassB.DisposeCount, Is.EqualTo(0));
                Assert.That(PassRecorder.ActiveSession.RenderPasses[0], Is.SameAs(passA));

                PassRecorder.EnsureCompiled(graphB);
                Assert.That(PassRecorder.ActiveSession.RenderPasses[0], Is.SameAs(passB));

                PassRecorder.Dispose();
                Assert.That(PassRecorder.CompiledSessionCount, Is.EqualTo(0));
                Assert.That(SessionTrackingPassA.DisposeCount, Is.EqualTo(1));
                Assert.That(SessionTrackingPassB.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graphA);
                UnityEngine.Object.DestroyImmediate(graphB);
            }
        }

        [Test]
        public void EnsureCompiled_SteadyStateGraphSwitching_AllocatesZeroBytes()
        {
            var graphA = CreateGraphAsset<SessionTrackingPassA>("GraphA", importVersion: 1);
            var graphB = CreateGraphAsset<SessionTrackingPassB>("GraphB", importVersion: 1);

            try
            {
                // Warm up both sessions.
                PassRecorder.EnsureCompiled(graphA);
                PassRecorder.EnsureCompiled(graphB);
                PassRecorder.EnsureCompiled(graphA);

                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 64; i++)
                {
                    PassRecorder.EnsureCompiled((i & 1) == 0 ? graphB : graphA);
                }
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(allocated, Is.EqualTo(0L));
            }
            finally
            {
                PassRecorder.Dispose();
                UnityEngine.Object.DestroyImmediate(graphA);
                UnityEngine.Object.DestroyImmediate(graphB);
            }
        }

        [Test]
        public void EnsureCompiled_ImportVersionBump_RecompilesOnlyModifiedGraph()
        {
            SessionTrackingPassA.ResetCounters();
            SessionTrackingPassB.ResetCounters();

            var graphA = CreateGraphAsset<SessionTrackingPassA>("GraphA", importVersion: 10);
            var graphB = CreateGraphAsset<SessionTrackingPassB>("GraphB", importVersion: 20);

            try
            {
                PassRecorder.EnsureCompiled(graphA);
                var originalPassA = PassRecorder.ActiveSession.RenderPasses[0];

                PassRecorder.EnsureCompiled(graphB);
                var originalPassB = PassRecorder.ActiveSession.RenderPasses[0];

                graphA.ImportVersion = 11;
                PassRecorder.EnsureCompiled(graphA);

                Assert.That(SessionTrackingPassA.DisposeCount, Is.EqualTo(1));
                Assert.That(SessionTrackingPassA.CreateCount, Is.EqualTo(2));
                Assert.That(PassRecorder.ActiveSession.RenderPasses[0], Is.Not.SameAs(originalPassA));
                Assert.That(SessionTrackingPassB.DisposeCount, Is.EqualTo(0));
                Assert.That(SessionTrackingPassB.CreateCount, Is.EqualTo(1));

                PassRecorder.EnsureCompiled(graphB);
                Assert.That(PassRecorder.ActiveSession.RenderPasses[0], Is.SameAs(originalPassB));
                Assert.That(SessionTrackingPassB.CreateCount, Is.EqualTo(1));
            }
            finally
            {
                PassRecorder.Dispose();
                UnityEngine.Object.DestroyImmediate(graphA);
                UnityEngine.Object.DestroyImmediate(graphB);
            }
        }

        private static RenderGraphData CreateGraphAsset<TPass>(string passName, long importVersion)
            where TPass : IRenderPass
        {
            var graph = ScriptableObject.CreateInstance<RenderGraphData>();
            graph.ImportVersion = importVersion;
            graph.Passes = new List<RenderGraphPassDefinition>
            {
                new RenderGraphPassDefinition
                {
                    PassName = passName,
                    PassType = typeof(TPass).AssemblyQualifiedName,
                }
            };
            return graph;
        }

        [FrameResourceSlot(FrameResourceSlots.SceneColorPrePostProcessName, AccessFlags.Read)]
        [CameraHistoryAccess("TestHistory", AccessFlags.Write)]
        private sealed class MetadataTestPass : ComputePass
        {
            [FormerlySerializedAs("m_LegacySource")]
            [RenderGraphResource(Access = AccessFlags.Read)]
            private RenderGraphTexture m_Source = new RenderGraphTexture();

            [RenderGraphResource(Access = AccessFlags.Write)]
            [PassBypass(nameof(m_Source))]
            private RenderGraphTexture m_Output = new RenderGraphTexture();

            [RenderGraphResource(Access = AccessFlags.ReadWrite)]
            [TransientResource]
            private RenderGraphBuffer m_Scratch = new RenderGraphBuffer();

            public RenderGraphTexture Source => m_Source;

            public void SetSource(RenderGraphTexture texture) => m_Source = texture;

            public override void Create() { }
            public override void Prepare(ContextContainer frameData) { }
            public override void Record(ComputePassContext ctx) { }
            public override void Dispose() { }
        }

        private sealed class SessionTrackingPassA : ComputePass
        {
            internal static int CreateCount;
            internal static int DisposeCount;

            internal static void ResetCounters()
            {
                CreateCount = 0;
                DisposeCount = 0;
            }

            public override void Create() => CreateCount++;
            public override void Prepare(ContextContainer frameData) { }
            public override void Record(ComputePassContext ctx) { }
            public override void Dispose() => DisposeCount++;
        }

        private sealed class SessionTrackingPassB : ComputePass
        {
            internal static int CreateCount;
            internal static int DisposeCount;

            internal static void ResetCounters()
            {
                CreateCount = 0;
                DisposeCount = 0;
            }

            public override void Create() => CreateCount++;
            public override void Prepare(ContextContainer frameData) { }
            public override void Record(ComputePassContext ctx) { }
            public override void Dispose() => DisposeCount++;
        }
    }
}
