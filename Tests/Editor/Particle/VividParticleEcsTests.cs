using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using VividRP.Runtime.ECS;
using VividRP.Runtime.Particle;
using VividRP.Runtime.Particle.ECS;

namespace VividRP.Editor.Tests
{
    public sealed class VividParticleEcsTests
    {
        [Test]
        public void TypeManager_RegistersParticleTypes_WithStableSoaOffsets()
        {
            VividParticleEcsBootstrap.RegisterTypes();
            VividEcsTypeIndex commonIndex = VividEcsTypeManager.GetTypeIndex<VividParticleCommon>();
            VividEcsTypeIndex systemIdIndex = VividEcsTypeManager.GetTypeIndex<VividParticleSystemId>();
            VividEcsTypeIndex rendererKeyIndex = VividEcsTypeManager.GetTypeIndex<VividParticleRendererSharedKey>();
            VividEcsTypeInfo commonType = VividEcsTypeManager.GetTypeInfo(commonIndex);

            Assert.That(commonIndex.IsValid, Is.True);
            Assert.That(systemIdIndex.IsValid, Is.True);
            Assert.That(rendererKeyIndex.IsValid, Is.True);
            Assert.That(systemIdIndex.Value, Is.Not.EqualTo(commonIndex.Value));
            Assert.That(systemIdIndex.IsSharedComponentType, Is.True);
            Assert.That(rendererKeyIndex.IsSharedComponentType, Is.True);
            Assert.That(VividEcsTypeManager.RegisteredTypeCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(commonType.IsSoa, Is.True);
            Assert.That(commonType.SoaFieldCount, Is.EqualTo(VividParticleCommon.FieldCountValue));
            Assert.That(commonType.SizeInPage, Is.EqualTo(VividParticleCommon.TypeSizeInBytes));
            Assert.That(commonType.GetSoaFieldInfo(VividParticleCommon.PositionFieldIndex).OffsetInPage, Is.EqualTo(VividParticleCommon.PositionOffsetInPage));
            Assert.That(commonType.GetSoaFieldInfo(VividParticleCommon.PositionFieldIndex).ElementSize, Is.EqualTo(VividParticleCommon.Float3SizeInBytes));
            Assert.That(commonType.GetSoaFieldInfo(VividParticleCommon.VelocityFieldIndex).OffsetInPage, Is.EqualTo(VividParticleCommon.VelocityOffsetInPage));
            Assert.That(commonType.GetSoaFieldInfo(VividParticleCommon.StartColorFieldIndex).ElementSize, Is.EqualTo(VividParticleCommon.Float4SizeInBytes));
            Assert.That(commonType.GetSoaFieldInfo(VividParticleCommon.SizeFieldIndex).OffsetInPage, Is.EqualTo(VividParticleCommon.SizeOffsetInPage));
        }

        [Test]
        public void Storage_UsesPageCapacity_AndClampsActiveCountWhenShrunk()
        {
            using var storage = new VividParticleEcsStorage();
            storage.systemId = new VividParticleSystemId(17);
            storage.EnsureCapacity(300);

            Assert.That(VividEcsConstants.PageEntryCount, Is.EqualTo(VividParticleStorage.PageSize));
            Assert.That(storage.capacity, Is.EqualTo(512));
            Assert.That(storage.pageCount, Is.EqualTo(2));
            Assert.That(storage.tileStart, Is.EqualTo(0));
            Assert.That(storage.tileCount, Is.EqualTo(2));
            Assert.That(storage.allocatorLiveTileCount, Is.EqualTo(2));

            for (int index = 0; index < 300; index++)
                Assert.That(AddParticle(storage, index), Is.True);

            Assert.That(storage.activeCount, Is.EqualTo(300));

            storage.EnsureCapacity(3);

            Assert.That(storage.capacity, Is.EqualTo(256));
            Assert.That(storage.pageCount, Is.EqualTo(1));
            Assert.That(storage.activeCount, Is.EqualTo(3));
            Assert.That(storage.tileCount, Is.EqualTo(1));
            Assert.That(storage.allocatorLiveTileCount, Is.EqualTo(1));
            Assert.That(storage.allocatorHighWatermarkTileCount, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void Constants_AlignToPage_UsesFixedPageEntryCount()
        {
            Assert.That(VividEcsConstants.PageEntryCount, Is.EqualTo(256));
            Assert.That(VividEcsConstants.AlignToPage(0), Is.EqualTo(256));
            Assert.That(VividEcsConstants.AlignToPage(1), Is.EqualTo(256));
            Assert.That(VividEcsConstants.AlignToPage(256), Is.EqualTo(256));
            Assert.That(VividEcsConstants.AlignToPage(257), Is.EqualTo(512));
        }

        [Test]
        public void Storage_AddWritesAndReadsAcrossPages()
        {
            using var storage = new VividParticleEcsStorage();
            storage.systemId = new VividParticleSystemId(17);
            storage.EnsureCapacity(300);

            for (int index = 0; index < 260; index++)
                Assert.That(AddParticle(storage, index), Is.True);

            Assert.That(storage.activeCount, Is.EqualTo(260));
            Assert.That(storage.capacity, Is.EqualTo(512));
            AssertVector3(new Vector3(0.0f, 1.0f, 2.0f), storage.GetPosition(0));
            AssertVector3(new Vector3(259.0f, 260.0f, 261.0f), storage.GetPosition(259));
            AssertVector3(new Vector3(259.5f, 260.5f, 261.5f), storage.GetVelocity(259));
            Assert.That(storage.GetStartLifetime(259), Is.EqualTo(10.0f));
            Assert.That(storage.GetRemainingLifetime(259), Is.EqualTo(10.0f));
            Assert.That(storage.GetSize(259), Is.EqualTo(260.0f));
            AssertColor(new Color(0.25f, 0.5f, 0.75f, 1.0f), storage.GetColor(259));

            using VividEcsPageGroup pageGroup = storage.CreatePageGroup(Allocator.TempJob);
            Assert.That(pageGroup.pageCount, Is.EqualTo(2));
            Assert.That(pageGroup[0].EntryCount, Is.EqualTo(256));
            Assert.That(pageGroup[1].EntryCount, Is.EqualTo(4));
            Assert.That(pageGroup[1].StartIndex, Is.EqualTo(256));
            Assert.That(storage.systemId, Is.EqualTo(new VividParticleSystemId(17)));
        }

        [Test]
        public void Storage_QueryLineGroups_UseSharedRendererKey()
        {
            using var storage = new VividParticleEcsStorage();
            storage.systemId = new VividParticleSystemId(17);
            var rendererKey = new VividParticleRendererSharedKey(
                materialId: 1,
                meshId: 2,
                renderMode: (int)VividParticleRenderMode.Billboard,
                layer: 3,
                gpuDataLayoutHash: 4,
                dataPerSharpBits: 5u,
                shadowCastingMode: 0,
                receiveShadows: false);
            storage.rendererSharedKey = rendererKey;
            storage.EnsureCapacity(4);
            Assert.That(AddParticle(storage, 0), Is.True);

            using VividEcsPageGroup pageGroup = storage.CreateSimulationPageGroup(Allocator.TempJob);
            var groups = storage.CreateLineGroups();

            Assert.That(pageGroup.pageCount, Is.EqualTo(1));
            Assert.That(storage.queryLineGroupCount, Is.EqualTo(1));
            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups[0].activeCount, Is.EqualTo(1));
            Assert.That(storage.rendererSharedKey, Is.EqualTo(rendererKey));
        }

        [Test]
        public void PageJob_SchedulesForEachLivePage()
        {
            using var storage = new VividParticleEcsStorage();
            storage.EnsureCapacity(300);
            for (int index = 0; index < 300; index++)
                Assert.That(AddParticle(storage, index), Is.True);

            using VividEcsPageGroup pageGroup = storage.CreatePageGroup(Allocator.TempJob);
            var counts = new NativeArray<int>(pageGroup.pageCount, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            try
            {
                var job = new CapturePageCountsJob
                {
                    Counts = counts,
                };

                JobHandle handle = job.Schedule(pageGroup.pages);
                handle.Complete();

                Assert.That(counts[0], Is.EqualTo(256));
                Assert.That(counts[1], Is.EqualTo(44));
            }
            finally
            {
                counts.Dispose();
            }
        }

        [Test]
        public void Storage_ScheduleIntegrate_MatchesExistingStorage()
        {
            using var legacyStorage = new VividParticleStorage();
            using var ecsStorage = new VividParticleEcsStorage();
            legacyStorage.EnsureCapacity(8);
            ecsStorage.EnsureCapacity(8);

            for (int index = 0; index < 3; index++)
            {
                Vector3 position = new(index, index + 2.0f, index + 4.0f);
                Vector3 velocity = new(index + 0.25f, index + 0.5f, index + 0.75f);
                Color color = new(0.1f * index, 0.2f, 0.3f, 1.0f);
                Assert.That(legacyStorage.Add(position, velocity, 5.0f, 5.0f, index + 1.0f, color), Is.True);
                Assert.That(ecsStorage.Add(position, velocity, 5.0f, 5.0f, index + 1.0f, color), Is.True);
            }

            Vector3 gravity = new(0.0f, -9.81f, 0.0f);
            Assert.That(legacyStorage.ScheduleIntegrate(0.125f, gravity, out JobHandle legacyHandle), Is.True);
            Assert.That(ecsStorage.ScheduleIntegrate(0.125f, gravity, out JobHandle ecsHandle), Is.True);

            legacyHandle.Complete();
            ecsHandle.Complete();
            legacyStorage.ApplyScheduledIntegrateResult();
            ecsStorage.ApplyScheduledIntegrateResult();

            Assert.That(ecsStorage.activeCount, Is.EqualTo(legacyStorage.activeCount));
            for (int index = 0; index < legacyStorage.activeCount; index++)
            {
                AssertVector3(legacyStorage.GetPosition(index), ecsStorage.GetPosition(index));
                AssertVector3(legacyStorage.GetVelocity(index), ecsStorage.GetVelocity(index));
                Assert.That(ecsStorage.GetStartLifetime(index), Is.EqualTo(legacyStorage.GetStartLifetime(index)));
                Assert.That(ecsStorage.GetRemainingLifetime(index), Is.EqualTo(legacyStorage.GetRemainingLifetime(index)));
                Assert.That(ecsStorage.GetSize(index), Is.EqualTo(legacyStorage.GetSize(index)));
                AssertColor(legacyStorage.GetColor(index), ecsStorage.GetColor(index));
            }
        }

        [Test]
        public void Storage_ScheduleIntegrate_CompactsExpiredParticlesWithSwapBack()
        {
            using var storage = new VividParticleEcsStorage();
            storage.EnsureCapacity(3);
            Assert.That(storage.Add(Vector3.zero, Vector3.zero, 0.05f, 0.05f, 1.0f, Color.red), Is.True);
            Assert.That(storage.Add(Vector3.one, Vector3.zero, 5.0f, 5.0f, 2.0f, Color.green), Is.True);
            Assert.That(storage.Add(Vector3.right, Vector3.zero, 6.0f, 6.0f, 3.0f, Color.blue), Is.True);

            Assert.That(storage.ScheduleIntegrate(0.1f, Vector3.zero, out JobHandle handle), Is.True);
            handle.Complete();
            storage.ApplyScheduledIntegrateResult();

            Assert.That(storage.activeCount, Is.EqualTo(2));
            AssertColor(Color.blue, storage.GetColor(0));
            AssertColor(Color.green, storage.GetColor(1));
            Assert.That(storage.GetRemainingLifetime(0), Is.EqualTo(5.9f).Within(0.0001f));
        }

        [Test]
        public void Storage_ReserveInitializeParticles_WritesPointParticlesInBurst()
        {
            using var storage = new VividParticleEcsStorage();
            using var works = new NativeList<VividParticleEcsInitializeParticlesWork>(1, Allocator.TempJob);
            storage.EnsureCapacity(4);
            VividParticleSystemFrameSnapshot snapshot = CreatePointSnapshot();

            Assert.That(storage.ReserveInitializeParticles(3, snapshot, 123u, works, out int firstIndex, out int count), Is.True);
            Assert.That(firstIndex, Is.EqualTo(0));
            Assert.That(count, Is.EqualTo(3));
            Assert.That(storage.activeCount, Is.EqualTo(3));

            var job = new VividParticleEcsInitializeParticlesJob
            {
                Works = works.AsArray(),
            };
            job.Schedule(works.Length, innerloopBatchCount: 1).Complete();

            for (int index = 0; index < 3; index++)
            {
                AssertVector3(Vector3.zero, storage.GetPosition(index));
                AssertVector3(new Vector3(0.0f, 0.0f, 2.5f), storage.GetVelocity(index));
                Assert.That(storage.GetStartLifetime(index), Is.EqualTo(3.0f));
                Assert.That(storage.GetRemainingLifetime(index), Is.EqualTo(3.0f));
                Assert.That(storage.GetSize(index), Is.EqualTo(0.75f));
                AssertColor(new Color(0.2f, 0.4f, 0.6f, 0.8f), storage.GetColor(index));
            }
        }

        [Test]
        public void Storage_DisposeCanRepeat_WithoutThrowing()
        {
            var storage = new VividParticleEcsStorage();
            storage.EnsureCapacity(1);

            Assert.DoesNotThrow(() => storage.Dispose());
            Assert.DoesNotThrow(() => storage.Dispose());
        }

        private static bool AddParticle(VividParticleEcsStorage storage, int index)
        {
            return storage.Add(
                new Vector3(index, index + 1.0f, index + 2.0f),
                new Vector3(index + 0.5f, index + 1.5f, index + 2.5f),
                10.0f,
                10.0f,
                index + 1.0f,
                new Color(0.25f, 0.5f, 0.75f, 1.0f));
        }

        private static VividParticleSystemFrameSnapshot CreatePointSnapshot()
        {
            return new VividParticleSystemFrameSnapshot(
                0.0f,
                true,
                true,
                false,
                false,
                5.0f,
                true,
                3.0f,
                2.5f,
                0.75f,
                new Color(0.2f, 0.4f, 0.6f, 0.8f),
                0.0f,
                VividParticleSystemSimulationSpace.Local,
                4,
                1u,
                false,
                true,
                0.0f,
                null,
                true,
                VividParticleShapeType.Point,
                1.0f,
                Vector3.one,
                25.0f,
                true,
                VividParticleRenderMode.Billboard,
                null,
                null,
                Color.white,
                1.0f,
                2.0f,
                0.0f,
                0,
                0,
                Vector3.zero,
                Matrix4x4.identity,
                Quaternion.identity,
                1);
        }

        private static void AssertVector3(Vector3 expected, Vector3 actual)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.0001f));
        }

        private static void AssertColor(Color expected, Color actual)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.0001f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.0001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.0001f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.0001f));
        }

        [BurstCompile]
        private struct CapturePageCountsJob : IVividEcsPageJob
        {
            public NativeArray<int> Counts;

            public void Execute(VividEcsPageInfo page)
            {
                Counts[page.PageIndex] = page.EntryCount;
            }
        }
    }
}
