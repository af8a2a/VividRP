using System.Collections.Generic;
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
        public void TileAllocator_ReusesAndMergesFreedRanges()
        {
            var allocator = new VividEcsTileAllocator();

            VividEcsTileRange first = allocator.AllocateEntries(300);
            VividEcsTileRange second = allocator.AllocateTiles(1);

            Assert.That(first.StartTile, Is.EqualTo(0));
            Assert.That(first.TileCount, Is.EqualTo(2));
            Assert.That(first.EntryCapacity, Is.EqualTo(512));
            Assert.That(second.StartTile, Is.EqualTo(2));
            Assert.That(allocator.liveTileCount, Is.EqualTo(3));
            Assert.That(allocator.highWatermarkTileCount, Is.EqualTo(3));

            allocator.Free(first);
            VividEcsTileRange reused = allocator.AllocateEntries(128);

            Assert.That(reused.StartTile, Is.EqualTo(0));
            Assert.That(reused.TileCount, Is.EqualTo(1));
            Assert.That(allocator.freeRangeCount, Is.EqualTo(1));

            allocator.Free(reused);
            allocator.Free(second);

            Assert.That(allocator.liveTileCount, Is.EqualTo(0));
            Assert.That(allocator.freeRangeCount, Is.EqualTo(1));
            Assert.That(allocator.highWatermarkTileCount, Is.EqualTo(3));
        }

        [Test]
        public void SparseTable_SetRemoveAndDenseAccess_UsesSwapBack()
        {
            var table = new VividEcsSparseTable<int>();

            table.Set(12, 120);
            table.Set(3, 30);
            table.Set(12, 121);

            Assert.That(table.count, Is.EqualTo(2));
            Assert.That(table.TryGetValue(12, out int value), Is.True);
            Assert.That(value, Is.EqualTo(121));
            Assert.That(table.ContainsKey(3), Is.True);

            Assert.That(table.Remove(12), Is.True);

            Assert.That(table.count, Is.EqualTo(1));
            Assert.That(table.ContainsKey(12), Is.False);
            Assert.That(table.GetKeyAtDenseIndex(0), Is.EqualTo(3));
            Assert.That(table.GetValueAtDenseIndex(0), Is.EqualTo(30));
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
        public void World_AllocatesArchetypeLineTiles_FromSharedAllocator()
        {
            VividEcsTypeIndex dataIndex = VividEcsTypeManager.RegisterComponent<TestData>();
            using var world = new VividEcsWorld();

            VividEcsArchetypeLine first = world.CreateArchetypeLine(300, dataIndex);
            VividEcsArchetypeLine second = world.CreateArchetypeLine(1, dataIndex);

            Assert.That(first.tileRange.StartTile, Is.EqualTo(0));
            Assert.That(first.tileRange.TileCount, Is.EqualTo(2));
            Assert.That(second.tileRange.StartTile, Is.EqualTo(2));
            Assert.That(world.tileAllocator.liveTileCount, Is.EqualTo(3));

            first.EnsureCapacity(1);

            Assert.That(first.tileRange.TileCount, Is.EqualTo(1));
            Assert.That(world.tileAllocator.liveTileCount, Is.EqualTo(2));
            Assert.That(world.tileAllocator.freeRangeCount, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void World_QueryAndLineGroups_FilterBySharedComponent()
        {
            VividEcsTypeIndex dataIndex = VividEcsTypeManager.RegisterComponent<TestData>();
            using var world = new VividEcsWorld();
            VividEcsArchetypeLine first = world.CreateArchetypeLine(8, dataIndex);
            VividEcsArchetypeLine second = world.CreateArchetypeLine(8, dataIndex);
            VividEcsArchetypeLine third = world.CreateArchetypeLine(8, dataIndex);
            first.SetSharedComponent(new TestShared(1));
            second.SetSharedComponent(new TestShared(1));
            third.SetSharedComponent(new TestShared(2));

            world.CreateEntity(first);
            world.CreateEntity(second);
            world.CreateEntity(third);

            VividEcsQuery allData = world.CreateQuery().WithAll(dataIndex);
            VividEcsQuery sharedOne = world.CreateQuery().WithAll(dataIndex).WithShared(new TestShared(1));
            List<VividEcsArchetypeLineGroup> groups = world.CreateArchetypeLineGroups(allData);

            Assert.That(sharedOne.MatchingLineCount(), Is.EqualTo(2));
            Assert.That(sharedOne.MatchingEntriesCount(), Is.EqualTo(2));
            Assert.That(groups.Count, Is.EqualTo(2));
            Assert.That(groups[0].SharedKey.Equals(groups[1].SharedKey), Is.False);
            Assert.That(groups[0].activeCount + groups[1].activeCount, Is.EqualTo(3));
        }

        [Test]
        public void World_LineGroups_CanGroupBySelectedSharedComponentTypes()
        {
            VividEcsTypeIndex dataIndex = VividEcsTypeManager.RegisterComponent<TestData>();
            VividEcsTypeIndex sharedIndex = VividEcsTypeManager.RegisterShared<TestShared>();
            VividEcsTypeManager.RegisterShared<TestSharedB>();
            using var world = new VividEcsWorld();
            VividEcsArchetypeLine first = world.CreateArchetypeLine(8, dataIndex);
            VividEcsArchetypeLine second = world.CreateArchetypeLine(8, dataIndex);
            VividEcsArchetypeLine third = world.CreateArchetypeLine(8, dataIndex);
            first.SetSharedComponent(new TestShared(1));
            first.SetSharedComponent(new TestSharedB(10));
            second.SetSharedComponent(new TestShared(1));
            second.SetSharedComponent(new TestSharedB(20));
            third.SetSharedComponent(new TestShared(2));
            third.SetSharedComponent(new TestSharedB(10));

            world.CreateEntity(first);
            world.CreateEntity(second);
            world.CreateEntity(third);

            VividEcsQuery allData = world.CreateQuery().WithAll(dataIndex);
            List<VividEcsArchetypeLineGroup> fullGroups = world.CreateArchetypeLineGroups(allData);
            List<VividEcsArchetypeLineGroup> selectedGroups =
                world.CreateArchetypeLineGroups(allData, sharedIndex);

            Assert.That(fullGroups, Has.Count.EqualTo(3));
            Assert.That(selectedGroups, Has.Count.EqualTo(2));
            Assert.That(ContainsGroupWithActiveCount(selectedGroups, 2), Is.True);
            Assert.That(ContainsGroupWithActiveCount(selectedGroups, 1), Is.True);
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

        [Test]
        public void ManagerJobRegistry_SchedulesEnabledJobsInOrder()
        {
            var values = new NativeArray<int>(3, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            try
            {
                var registry = new VividEcsManagerJobRegistry<RegistryContext>();
                registry.Register(
                    "late",
                    10,
                    (context, dependency) => new WriteRegistryValueJob
                    {
                        Values = context.Values,
                        Index = 1,
                        Value = 2,
                    }.Schedule(dependency));
                registry.Register(
                    "disabled",
                    5,
                    (context, dependency) => new WriteRegistryValueJob
                    {
                        Values = context.Values,
                        Index = 2,
                        Value = 99,
                    }.Schedule(dependency),
                    context => context.DisabledJobsEnabled);
                registry.Register(
                    "early",
                    0,
                    (context, dependency) => new WriteRegistryValueJob
                    {
                        Values = context.Values,
                        Index = 0,
                        Value = 1,
                    }.Schedule(dependency));

                var context = new RegistryContext
                {
                    Values = values,
                    DisabledJobsEnabled = false,
                };
                JobHandle handle = registry.ScheduleEnabled(context);
                handle.Complete();

                Assert.That(registry.count, Is.EqualTo(3));
                Assert.That(registry.EnabledCount(context), Is.EqualTo(2));
                Assert.That(values[0], Is.EqualTo(1));
                Assert.That(values[1], Is.EqualTo(2));
                Assert.That(values[2], Is.EqualTo(0));
            }
            finally
            {
                values.Dispose();
            }
        }

        [Test]
        public void ManagerJobRegistry_UsesModuleFlagsToEnableJobs()
        {
            var values = new NativeArray<int>(3, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            try
            {
                var registry = new VividEcsManagerJobRegistry<ModuleRegistryContext>();
                registry.RegisterModule(
                    "a",
                    0,
                    ModuleRegistryContext.ModuleA,
                    (context, dependency) => new WriteRegistryValueJob
                    {
                        Values = context.Values,
                        Index = 0,
                        Value = 10,
                    }.Schedule(dependency));
                registry.RegisterModule(
                    "b",
                    1,
                    ModuleRegistryContext.ModuleB,
                    (context, dependency) => new WriteRegistryValueJob
                    {
                        Values = context.Values,
                        Index = 1,
                        Value = 20,
                    }.Schedule(dependency));

                var context = new ModuleRegistryContext
                {
                    Values = values,
                    EnabledModuleFlags = ModuleRegistryContext.ModuleB,
                };
                JobHandle handle = registry.ScheduleEnabled(context);
                handle.Complete();

                Assert.That(registry.EnabledCount(context), Is.EqualTo(1));
                Assert.That(values[0], Is.EqualTo(0));
                Assert.That(values[1], Is.EqualTo(20));
            }
            finally
            {
                values.Dispose();
            }
        }

        [Test]
        public void ManagerJobRegistry_CanScheduleEnabledJobsInParallel()
        {
            var values = new NativeArray<int>(4, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            try
            {
                var registry = new VividEcsManagerJobRegistry<ModuleRegistryContext>();
                registry.RegisterModule(
                    "a",
                    10,
                    ModuleRegistryContext.ModuleA,
                    (context, dependency) => new WriteRegistryValueJob
                    {
                        Values = context.Values,
                        Index = 0,
                        Value = 11,
                    }.Schedule(dependency));
                registry.RegisterModule(
                    "b",
                    0,
                    ModuleRegistryContext.ModuleB,
                    (context, dependency) => new WriteRegistryValueJob
                    {
                        Values = context.Values,
                        Index = 1,
                        Value = 22,
                    }.Schedule(dependency));
                registry.RegisterModule(
                    "disabled",
                    5,
                    ModuleRegistryContext.ModuleC,
                    (context, dependency) => new WriteRegistryValueJob
                    {
                        Values = context.Values,
                        Index = 2,
                        Value = 33,
                    }.Schedule(dependency));

                var context = new ModuleRegistryContext
                {
                    Values = values,
                    EnabledModuleFlags = ModuleRegistryContext.ModuleA | ModuleRegistryContext.ModuleB,
                };
                JobHandle handle = registry.ScheduleEnabledParallel(context);
                handle.Complete();

                Assert.That(registry.EnabledCount(context), Is.EqualTo(2));
                Assert.That(values[0], Is.EqualTo(11));
                Assert.That(values[1], Is.EqualTo(22));
                Assert.That(values[2], Is.EqualTo(0));
            }
            finally
            {
                values.Dispose();
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
            public TestShared(int value)
            {
                Value = value;
            }

            public readonly int Value;
        }

        private readonly struct TestSharedB : IVividEcsSharedComponentData
        {
            public TestSharedB(int value)
            {
                Value = value;
            }

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

        private struct RegistryContext
        {
            public NativeArray<int> Values;
            public bool DisabledJobsEnabled;
        }

        private struct ModuleRegistryContext : IVividEcsManagerJobModuleFlags
        {
            public const uint ModuleA = 1u << 0;
            public const uint ModuleB = 1u << 1;
            public const uint ModuleC = 1u << 2;

            public NativeArray<int> Values;
            public uint EnabledModuleFlags { get; set; }
        }

        private struct WriteRegistryValueJob : IJob
        {
            public NativeArray<int> Values;
            public int Index;
            public int Value;

            public void Execute()
            {
                Values[Index] = Value;
            }
        }

        private static bool ContainsGroupWithActiveCount(
            List<VividEcsArchetypeLineGroup> groups,
            int activeCount)
        {
            for (int index = 0; index < groups.Count; index++)
            {
                if (groups[index].activeCount == activeCount)
                    return true;
            }

            return false;
        }
    }
}
