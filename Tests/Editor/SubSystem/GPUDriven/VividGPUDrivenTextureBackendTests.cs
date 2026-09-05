using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.PrimitiveScene;

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

        [TestCase(false)]
        [TestCase(true)]
        public void VirtualTextureShadows_TrackSamplingAndResolvedBiasWithoutStableFrameAllocations(bool usesVirtualTexture)
        {
            var source = new GameObject("VT Shadow Caster");
            try
            {
                IGPUDrivenTextureBackend backend = usesVirtualTexture
                    ? new FakeVirtualTextureBackend()
                    : new FakeGPUDrivenTextureBackend();
                using var system = new VividGPUDrivenSystem(backend);
                VividPrimitiveScene scene = system.PrimitiveScene;
                EntityId sourceId = source.GetEntityId();
                var geometry = new VividPrimitiveResourceKey(VividPrimitiveResourceDomain.MeshletGeometry, sourceId, EntityId.None, -1);
                var material = new VividPrimitiveResourceKey(VividPrimitiveResourceDomain.MaterialProxy, sourceId, EntityId.None, -1);
                var sections = new[] { new VividPrimitiveDrawSectionDescriptor(0, geometry, material, VividPrimitiveDrawSectionFlags.Valid) };
                scene.RegisterOrUpdate(new VividPrimitiveSourceDescriptor(sourceId, Matrix4x4.identity, Matrix4x4.identity,
                    new Bounds(Vector3.zero, Vector3.one), uint.MaxValue, VividInstancePassMask.Shadows,
                    VividPrimitiveFlags.Valid | VividPrimitiveFlags.Static, sections));
                scene.UpdateMaterialPayload(material, 0u, new VividMaterialData { RendererListID = VividRendererListID.AlphaTest });
                scene.AcknowledgeStaticShadowInvalidations(scene.StaticShadowRevision);
                var desc = new VirtualTextureSpaceDesc("Shadow VT", 16, 1, 4, 4, 3, 4,
                    GraphicsFormat.R8G8B8A8_UNorm, 4, 32);
                int spaceId = VirtualTextureSystem.RegisterSpace(desc);
                Assert.That(VirtualTextureSystem.TryGetSpaceBinding(spaceId, out var binding), Is.True);
                var frameData = new VividVirtualTextureFrameData();
                frameData.AddBinding(binding);
                uint revision = scene.StaticShadowRevision;
                system.UpdateVirtualTextureShadowInvalidations(frameData);
                Assert.That(scene.StaticShadowRevision == revision, Is.EqualTo(!usesVirtualTexture));
                scene.AcknowledgeStaticShadowInvalidations(scene.StaticShadowRevision);
                revision = scene.StaticShadowRevision;
                system.UpdateVirtualTextureShadowInvalidations(frameData);
                Assert.That(scene.StaticShadowRevision, Is.EqualTo(revision));

                Assert.That(VirtualTextureSystem.TryMakePageResident(spaceId, new VirtualTexturePageCoord(0, 0, 0), false, 1), Is.True);
                Assert.That(VirtualTextureSystem.TryGetSpaceBinding(spaceId, out binding), Is.True);
                frameData.Reset();
                frameData.AddBinding(binding);
                system.UpdateVirtualTextureShadowInvalidations(frameData);
                Assert.That(scene.StaticShadowRevision == revision, Is.EqualTo(!usesVirtualTexture));
                Assert.That(scene.PendingStaticShadowInvalidationBounds.Length, Is.EqualTo(usesVirtualTexture ? 1 : 0));
                Assert.That(scene.StaticShadowInvalidationRequiresFullRefresh, Is.False);
                scene.AcknowledgeStaticShadowInvalidations(scene.StaticShadowRevision);

                revision = scene.StaticShadowRevision;
                frameData.AdaptiveMipBias = 1.25f;
                system.UpdateVirtualTextureShadowInvalidations(frameData);
                Assert.That(scene.StaticShadowRevision == revision, Is.EqualTo(!usesVirtualTexture));
                scene.AcknowledgeStaticShadowInvalidations(scene.StaticShadowRevision);
                VirtualTextureSystem.SetAdaptiveMipBiasEnabled(spaceId, false); // Resolved space bias now overrides the frame value.
                revision = scene.StaticShadowRevision;
                system.UpdateVirtualTextureShadowInvalidations(frameData);
                Assert.That(scene.StaticShadowRevision == revision, Is.EqualTo(!usesVirtualTexture));
                scene.AcknowledgeStaticShadowInvalidations(scene.StaticShadowRevision);
                revision = scene.StaticShadowRevision;
                frameData.AdaptiveMipBias = 2.5f;
                system.UpdateVirtualTextureShadowInvalidations(frameData);
                Assert.That(scene.StaticShadowRevision, Is.EqualTo(revision));

                // A different VT space must not invalidate this allocation.
                int otherSpaceId = VirtualTextureSystem.RegisterSpace(new VirtualTextureSpaceDesc(
                    "Other Shadow VT", 32, 1, 4, 4, 3, 4, GraphicsFormat.R8G8B8A8_UNorm, 4, 32));
                Assert.That(VirtualTextureSystem.TryMakePageResident(otherSpaceId, new VirtualTexturePageCoord(1, 1, 0), false, 2), Is.True);
                system.UpdateVirtualTextureShadowInvalidations(frameData);
                Assert.That(scene.StaticShadowRevision, Is.EqualTo(revision));
                for (int i = 0; i < 8; i++)
                    system.UpdateVirtualTextureShadowInvalidations(frameData);
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 128; i++)
                    system.UpdateVirtualTextureShadowInvalidations(frameData);
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.Zero);

                system.UpdateVirtualTextureShadowInvalidations(null);
                Assert.That(scene.StaticShadowRevision == revision, Is.EqualTo(!usesVirtualTexture));
                scene.AcknowledgeStaticShadowInvalidations(scene.StaticShadowRevision);
                revision = scene.StaticShadowRevision;
                system.UpdateVirtualTextureShadowInvalidations(frameData);
                Assert.That(scene.StaticShadowRevision == revision, Is.EqualTo(!usesVirtualTexture));
            }
            finally
            {
                Object.DestroyImmediate(source);
                VirtualTextureSystem.Deinitialize();
            }
        }

        private sealed class FakeVirtualTextureBackend : FakeGPUDrivenTextureBackend, IGPUDrivenVirtualTextureBackend
        {
            public int VirtualTextureAllocationId => 0;
            public int VirtualTextureSpaceId => 0;
        }

        private class FakeGPUDrivenTextureBackend : IGPUDrivenTextureBackend
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

            public bool CanUseStreamedVirtualTexture(VividVirtualTextureAsset asset)
            {
                return false;
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
