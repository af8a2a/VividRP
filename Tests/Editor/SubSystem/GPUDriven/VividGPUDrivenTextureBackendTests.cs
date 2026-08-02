using NUnit.Framework;
using Unity.Mathematics;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests
{
    public sealed class VividGPUDrivenTextureBackendTests
    {
        [SetUp]
        public void SetUp()
        {
            VividMeshletRendererDatabase.instance.Clear();
            VividGPUDrivenStatsRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            VividMeshletRendererDatabase.instance.Clear();
            VividGPUDrivenStatsRegistry.Clear();
        }

        [Test]
        public void System_UsesInjectedTextureBackend_ForLifecycleAvailabilityRevisionAndStats()
        {
            var backend = new FakeGPUDrivenTextureBackend
            {
                IsAvailableValue = false,
                UnavailableReasonValue = "Fake backend unavailable.",
                BindingRevisionValue = 17u,
                Stats = new GPUDrivenTextureBackendStats(3u, 256u, 41u, 5u, 19),
            };
            var system = new VividGPUDrivenSystem(backend);

            Assert.That(system.BindlessTextureContainer, Is.Null);
            Assert.That(system.IsAvailable, Is.False);
            Assert.That(system.UnavailableReason, Is.EqualTo("Fake backend unavailable."));

            system.PrepareFrame();

            VividGPUDrivenStats stats = VividGPUDrivenStatsRegistry.LastStats;
            Assert.That(backend.ResetPerFrameStatsCallCount, Is.EqualTo(1));
            Assert.That(backend.PrepareFrameCallCount, Is.EqualTo(1));
            Assert.That(backend.BindingRevisionReadCount, Is.GreaterThan(0));
            Assert.That(stats.TextureBackendName, Is.EqualTo("Fake"));
            Assert.That(stats.TextureBackendAvailable, Is.False);
            Assert.That(stats.StatusMessage, Is.EqualTo("Fake backend unavailable."));
            Assert.That(stats.BackendPoolCount, Is.EqualTo(3u));
            Assert.That(stats.BackendResourceCapacity, Is.EqualTo(256u));
            Assert.That(stats.AllocatedBackendResourceCount, Is.EqualTo(41u));
            Assert.That(stats.CreateBackendResourceCallCountThisFrame, Is.EqualTo(5u));
            Assert.That(stats.RegisteredBackendResourceCount, Is.EqualTo(19));

            system.Dispose();

            Assert.That(backend.DisposeCallCount, Is.EqualTo(1));
        }

        private sealed class FakeGPUDrivenTextureBackend : IGPUDrivenTextureBackend
        {
            public string DisplayName => "Fake";

            public bool IsAvailable => IsAvailableValue;

            public string UnavailableReason => UnavailableReasonValue;

            public uint BindingRevision
            {
                get
                {
                    BindingRevisionReadCount++;
                    return BindingRevisionValue;
                }
            }

            internal bool IsAvailableValue { get; set; }

            internal string UnavailableReasonValue { get; set; } = string.Empty;

            internal uint BindingRevisionValue { get; set; }

            internal GPUDrivenTextureBackendStats Stats { get; set; }

            internal int BindingRevisionReadCount { get; private set; }

            internal int PrepareFrameCallCount { get; private set; }

            internal int ResetPerFrameStatsCallCount { get; private set; }

            internal int DisposeCallCount { get; private set; }

            public void PrepareFrame()
            {
                PrepareFrameCallCount++;
            }

            public void ResetPerFrameStats()
            {
                ResetPerFrameStatsCallCount++;
            }

            public VividSurfaceBindingData CreateSurfaceBinding(in GPUDrivenSurfaceTextureSet textures)
            {
                return new VividSurfaceBindingData
                {
                    BaseColorResource = VividSurfaceBindingData.InvalidResource,
                    NormalResource = VividSurfaceBindingData.InvalidResource,
                    MaskResource = VividSurfaceBindingData.InvalidResource,
                    Flags = VividSurfaceBindingFlags.None,
                    UVScaleBias = new float4(1.0f, 1.0f, 0.0f, 0.0f),
                };
            }

            public GPUDrivenTextureBackendStats GetStats()
            {
                return Stats;
            }

            public void Dispose()
            {
                DisposeCallCount++;
            }
        }
    }
}
