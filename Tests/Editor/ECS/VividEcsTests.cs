using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VividRP.Runtime.ECS;

namespace VividRP.Editor.Tests
{
    public sealed class VividEcsTests
    {
        [Test]
        public void TypeManager_RegistersExplicitTypes_WithFlagsAndSoaMetadata()
        {
            VividEcsTypeIndex dataIndex = VividEcsTypeManager.RegisterComponent<TestData>();
            VividEcsTypeIndex sharedIndex = VividEcsTypeManager.RegisterShared<TestShared>();
            VividEcsTypeIndex tagIndex = VividEcsTypeManager.RegisterTag<TestTag>();
            VividEcsTypeIndex bitIndex = VividEcsTypeManager.RegisterBit<TestBit>();
            VividEcsTypeIndex soaIndex = VividEcsTypeManager.RegisterSoa<TestSoa>();
            VividEcsTypeInfo soaInfo = VividEcsTypeManager.GetTypeInfo(soaIndex);

            Assert.That(dataIndex.IsDataComponentType, Is.True);
            Assert.That(sharedIndex.IsSharedComponentType, Is.True);
            Assert.That(tagIndex.IsTagComponentType, Is.True);
            Assert.That(bitIndex.IsBitComponentType, Is.True);
            Assert.That(soaIndex.IsSoaComponentType, Is.True);
            Assert.That(soaInfo.SoaFieldCount, Is.EqualTo(TestSoa.FieldCountValue));
            Assert.That(soaInfo.SizeInPage, Is.EqualTo(TestSoa.TypeSizeInBytes));
            Assert.That(soaInfo.GetSoaFieldInfo(TestSoa.PositionFieldIndex).OffsetInPage, Is.EqualTo(TestSoa.PositionOffsetInPage));
            Assert.That(soaInfo.GetSoaFieldInfo(TestSoa.ColorFieldIndex).ElementSize, Is.EqualTo(sizeof(float) * 4));
            Assert.That(VividEcsTypeManager.RegisteredTypeCount, Is.GreaterThanOrEqualTo(5));
        }

        [Test]
        public void Constants_AlignToPage_UsesVividPageEntryCount()
        {
            Assert.That(VividEcsConstants.PageEntryCount, Is.EqualTo(256));
            Assert.That(VividEcsConstants.AlignToPage(0), Is.EqualTo(256));
            Assert.That(VividEcsConstants.AlignToPage(1), Is.EqualTo(256));
            Assert.That(VividEcsConstants.AlignToPage(256), Is.EqualTo(256));
            Assert.That(VividEcsConstants.AlignToPage(257), Is.EqualTo(512));
        }

        [Test]
        public void ArchetypeLine_AppendsAcrossPages_AndCompactsWithKeepMask()
        {
            VividEcsTypeIndex dataIndex = VividEcsTypeManager.RegisterComponent<TestData>();
            VividEcsTypeIndex bitIndex = VividEcsTypeManager.RegisterBit<TestBit>();
            using var line = new VividEcsArchetypeLine(7, dataIndex, bitIndex);
            line.EnsureCapacity(300);

            var dataColumn = line.GetColumn<VividEcsComponentColumn<TestData>>(dataIndex);
            var bitColumn = line.GetColumn<VividEcsBitColumn<TestBit>>(bitIndex);
            for (int index = 0; index < 260; index++)
            {
                Assert.That(line.Append(out int entryIndex), Is.True);
                dataColumn[entryIndex] = new TestData(index);
                bitColumn.Set(entryIndex, (index & 1) == 0);
            }

            Assert.That(line.capacity, Is.EqualTo(512));
            Assert.That(line.pageCount, Is.EqualTo(2));
            Assert.That(line.GetPageInfo(0).EntryCount, Is.EqualTo(256));
            Assert.That(line.GetPageInfo(1).EntryCount, Is.EqualTo(4));
            Assert.That(dataColumn[259].Value, Is.EqualTo(259));
            Assert.That(bitColumn.Get(258), Is.True);

            var keepMask = new NativeArray<byte>(VividEcsConstants.PageEntryCount, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            try
            {
                keepMask[0] = 1;
                keepMask[1] = 1;
                int removed = line.RemoveEntriesByPageKeepMask(1, keepMask);

                Assert.That(removed, Is.EqualTo(2));
                Assert.That(line.activeCount, Is.EqualTo(258));
                Assert.That(line.GetPageInfo(1).EntryCount, Is.EqualTo(2));
            }
            finally
            {
                keepMask.Dispose();
            }
        }

        [Test]
        public void World_QueryAndCommandBuffer_UpdateLineAndEntityCounts()
        {
            VividEcsTypeIndex dataIndex = VividEcsTypeManager.RegisterComponent<TestData>();
            VividEcsTypeIndex tagIndex = VividEcsTypeManager.RegisterTag<TestTag>();
            using var world = new VividEcsWorld();
            VividEcsArchetypeLine line = world.CreateArchetypeLine(8, dataIndex);
            using var commandBuffer = new VividEcsEntityCommandBuffer();

            commandBuffer.CreateEntity(line);
            commandBuffer.CreateEntity(line);
            commandBuffer.Playback(world);

            Assert.That(world.entityCount, Is.EqualTo(2));
            Assert.That(world.CreateQuery().WithAll(dataIndex).MatchingEntriesCount(), Is.EqualTo(2));
            Assert.That(world.CreateQuery().WithAll(tagIndex).MatchingLineCount(), Is.EqualTo(0));

            commandBuffer.AddComponentType(line, tagIndex);
            commandBuffer.Playback(world);

            Assert.That(world.CreateQuery().WithAll(dataIndex, tagIndex).MatchingLineCount(), Is.EqualTo(1));

            commandBuffer.RemoveComponentType(line, dataIndex);
            commandBuffer.Playback(world);

            Assert.That(world.CreateQuery().WithAll(dataIndex).MatchingLineCount(), Is.EqualTo(0));
            Assert.That(world.CreateQuery().WithAll(tagIndex).MatchingEntriesCount(), Is.EqualTo(2));
        }

        [Test]
        public void PageJob_ScheduleParallel_VisitsEachLivePage()
        {
            VividEcsTypeIndex dataIndex = VividEcsTypeManager.RegisterComponent<TestData>();
            using var line = new VividEcsArchetypeLine(9, dataIndex);
            line.EnsureCapacity(300);
            for (int index = 0; index < 300; index++)
                Assert.That(line.Append(out _), Is.True);

            using VividEcsPageGroup pageGroup = line.CreatePageGroup(Allocator.TempJob);
            var counts = new NativeArray<int>(pageGroup.pageCount, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            try
            {
                var job = new CapturePageCountsJob
                {
                    Counts = counts,
                };

                JobHandle handle = job.ScheduleParallel(pageGroup.pages);
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
        public void PageGroupJob_ScheduleParallel_VisitsEachGroup()
        {
            var pages = new NativeArray<VividEcsPageInfo>(3, Allocator.TempJob, NativeArrayOptions.UninitializedMemory)
            {
                [0] = new VividEcsPageInfo(1, 0, 0, 256),
                [1] = new VividEcsPageInfo(1, 1, 256, 12),
                [2] = new VividEcsPageInfo(2, 0, 0, 33),
            };
            var groups = new NativeArray<int2>(2, Allocator.TempJob, NativeArrayOptions.UninitializedMemory)
            {
                [0] = new int2(0, 2),
                [1] = new int2(2, 1),
            };
            var counts = new NativeArray<int>(2, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            try
            {
                var job = new CapturePageGroupCountsJob
                {
                    Counts = counts,
                };

                JobHandle handle = job.ScheduleParallel(pages, groups);
                handle.Complete();

                Assert.That(counts[0], Is.EqualTo(268));
                Assert.That(counts[1], Is.EqualTo(33));
            }
            finally
            {
                counts.Dispose();
                groups.Dispose();
                pages.Dispose();
            }
        }

        private readonly struct TestData : IVividEcsComponentData
        {
            public TestData(float value)
            {
                Value = value;
            }

            public readonly float Value;
        }

        private readonly struct TestShared : IVividEcsSharedComponentData
        {
            public readonly int Value;
        }

        private readonly struct TestTag : IVividEcsTagComponentData
        {
        }

        private readonly struct TestBit : IVividEcsBitComponentData
        {
        }

        private struct TestSoa : IVividEcsSoaComponentData
        {
            public const int PositionFieldIndex = 0;
            public const int ColorFieldIndex = 1;
            public const int FieldCountValue = 2;
            public const int PositionOffsetInPage = 0;
            public const int ColorOffsetInPage = PositionOffsetInPage + sizeof(float) * 3 * VividEcsConstants.PageEntryCount;
            public const int TypeSizeInBytes = ColorOffsetInPage + sizeof(float) * 4 * VividEcsConstants.PageEntryCount;

            public int FieldCount => FieldCountValue;

            public int TypeSize => TypeSizeInBytes;

            public VividEcsSoaFieldInfo GetFieldInfo(int index)
            {
                return index switch
                {
                    PositionFieldIndex => new VividEcsSoaFieldInfo(PositionOffsetInPage, sizeof(float) * 3),
                    ColorFieldIndex => new VividEcsSoaFieldInfo(ColorOffsetInPage, sizeof(float) * 4),
                    _ => throw new System.ArgumentOutOfRangeException(nameof(index)),
                };
            }
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

        [BurstCompile]
        private struct CapturePageGroupCountsJob : IVividEcsPageGroupJob
        {
            public NativeArray<int> Counts;

            public void Execute(VividEcsPageGroupInfo pageGroup)
            {
                int count = 0;
                for (int index = 0; index < pageGroup.PageCount; index++)
                    count += pageGroup[index].EntryCount;

                Counts[pageGroup.StartIndex == 0 ? 0 : 1] = count;
            }
        }
    }
}
