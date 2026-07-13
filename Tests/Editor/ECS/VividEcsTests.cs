using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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
            int initialVersion = table.version;

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
            Assert.That(table.version, Is.GreaterThan(initialVersion));
        }

        [Test]
        public void World_LineAttachments_UseSparseLineBindings_WithoutInvalidatingQueries()
        {
            VividEcsTypeIndex dataIndex = VividEcsTypeManager.RegisterComponent<TestData>();
            using var world = new VividEcsWorld();
            VividEcsArchetypeLine line = world.CreateArchetypeLine(4, dataIndex);
            world.CreateEntity(line);
            VividEcsQuery query = world.CreateQuery().WithAll(dataIndex);

            Assert.That(query.PrepareMatchingLines(), Is.EqualTo(1));
            int cacheRevision = query.cacheRevision;

            world.SetLineAttachment(line, new TestLineAttachment(7));
            Assert.That(world.TryGetLineAttachment(line, out TestLineAttachment first), Is.True);
            Assert.That(first.Value, Is.EqualTo(7));
            Assert.That(world.GetLineAttachmentCount<TestLineAttachment>(), Is.EqualTo(1));
            Assert.That(query.PrepareMatchingLines(), Is.EqualTo(1));
            Assert.That(query.cacheRevision, Is.EqualTo(cacheRevision));
            Assert.That(query.lastSourceScanCount, Is.EqualTo(0));

            world.SetLineAttachment(line, new TestLineAttachment(11));
            Assert.That(world.TryGetLineAttachment(line, out TestLineAttachment replaced), Is.True);
            Assert.That(replaced.Value, Is.EqualTo(11));
            Assert.That(world.GetLineAttachmentCount<TestLineAttachment>(), Is.EqualTo(1));

            Assert.That(world.DestroyArchetypeLine(line), Is.True);
            Assert.That(world.GetLineAttachmentCount<TestLineAttachment>(), Is.EqualTo(0));
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

            var matchedLines = new List<VividEcsArchetypeLine>();
            foreach (VividEcsArchetypeLine matchedLine in world.CreateQuery().WithAll(tagIndex).MatchLines())
                matchedLines.Add(matchedLine);

            Assert.That(matchedLines, Has.Count.EqualTo(1));
            Assert.That(matchedLines[0], Is.SameAs(line));
        }

        [Test]
        public void World_QueryCache_ReusesMatchingLinesUntilStructureChanges()
        {
            VividEcsTypeIndex dataIndex = VividEcsTypeManager.RegisterComponent<TestData>();
            VividEcsTypeIndex tagIndex = VividEcsTypeManager.RegisterTag<TestTag>();
            using var world = new VividEcsWorld();
            VividEcsArchetypeLine first = world.CreateArchetypeLine(8, dataIndex);
            VividEcsArchetypeLine second = world.CreateArchetypeLine(8, dataIndex);
            first.SetSharedComponent(new TestShared(1));
            second.SetSharedComponent(new TestShared(2));
            VividEcsQuery query = world.CreateQuery().WithAll(dataIndex);

            Assert.That(query.MatchingLineCount(), Is.EqualTo(2));
            Assert.That(query.cacheRebuildCount, Is.EqualTo(1));
            Assert.That(query.cacheHitCount, Is.EqualTo(0));
            Assert.That(query.lastSourceScanCount, Is.EqualTo(2));

            first.SetSharedComponent(new TestShared(3));

            Assert.That(query.MatchingLineCount(), Is.EqualTo(2));
            Assert.That(query.cacheRebuildCount, Is.EqualTo(1));
            Assert.That(query.cacheHitCount, Is.EqualTo(1));
            Assert.That(query.lastSourceScanCount, Is.EqualTo(0));

            world.AddComponentType(first, tagIndex);

            Assert.That(query.MatchingLineCount(), Is.EqualTo(2));
            Assert.That(query.cacheRebuildCount, Is.EqualTo(2));
            Assert.That(query.lastSourceScanCount, Is.EqualTo(2));

            Assert.That(world.DestroyArchetypeLine(second), Is.True);
            Assert.That(query.MatchingLineCount(), Is.EqualTo(1));
            Assert.That(query.cacheRebuildCount, Is.EqualTo(3));
            Assert.That(query.lastSourceScanCount, Is.EqualTo(1));
        }

        [Test]
        public void World_QueryCache_SharedFilterTracksOnlyRelevantSharedType()
        {
            VividEcsTypeIndex dataIndex = VividEcsTypeManager.RegisterComponent<TestData>();
            using var world = new VividEcsWorld();
            VividEcsArchetypeLine line = world.CreateArchetypeLine(8, dataIndex);
            line.SetSharedComponent(new TestShared(1));
            line.SetSharedComponent(new TestSharedB(10));
            VividEcsQuery query = world.CreateQuery()
                .WithAll(dataIndex)
                .WithShared(new TestShared(1));

            Assert.That(query.MatchingLineCount(), Is.EqualTo(1));
            Assert.That(query.cacheRebuildCount, Is.EqualTo(1));

            line.SetSharedComponent(new TestSharedB(20));

            Assert.That(query.MatchingLineCount(), Is.EqualTo(1));
            Assert.That(query.cacheRebuildCount, Is.EqualTo(1));
            Assert.That(query.cacheHitCount, Is.EqualTo(1));
            Assert.That(query.lastSourceScanCount, Is.EqualTo(0));

            line.SetSharedComponent(new TestShared(2));

            Assert.That(query.MatchingLineCount(), Is.EqualTo(0));
            Assert.That(query.cacheRebuildCount, Is.EqualTo(2));
            Assert.That(query.lastSourceScanCount, Is.EqualTo(1));
        }

        [Test]
        public void World_LineGroupCache_ReusesGroupsUntilSelectedSharedKeyChanges()
        {
            VividEcsTypeIndex dataIndex = VividEcsTypeManager.RegisterComponent<TestData>();
            VividEcsTypeIndex sharedIndex = VividEcsTypeManager.RegisterShared<TestShared>();
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
            VividEcsQuery query = world.CreateQuery().WithAll(dataIndex);
            var cache = new VividEcsArchetypeLineGroupCache(query, sharedIndex);

            Assert.That(cache.Prepare(), Is.True);
            Assert.That(cache.groupCount, Is.EqualTo(2));
            Assert.That(cache.cacheRebuildCount, Is.EqualTo(1));
            Assert.That(cache.cacheHitCount, Is.EqualTo(0));
            Assert.That(cache.lastSourceLineScanCount, Is.EqualTo(3));
            VividEcsSharedComponentKey sharedOneKey = first.GetSharedComponentKey(sharedIndex);
            VividEcsArchetypeLineGroup sharedOneGroup = FindLineGroup(cache.groups, sharedOneKey);
            Assert.That(sharedOneGroup, Is.Not.Null);
            Assert.That(sharedOneGroup.lineCount, Is.EqualTo(2));
            Assert.That(sharedOneGroup.TryGetSharedComponent(out TestShared sharedValue), Is.True);
            Assert.That(sharedValue.Value, Is.EqualTo(1));

            first.SetSharedComponent(new TestSharedB(30));

            Assert.That(cache.Prepare(), Is.False);
            Assert.That(cache.cacheRebuildCount, Is.EqualTo(1));
            Assert.That(cache.cacheHitCount, Is.EqualTo(1));
            Assert.That(cache.lastSourceLineScanCount, Is.EqualTo(0));
            Assert.That(FindLineGroup(cache.groups, sharedOneKey), Is.SameAs(sharedOneGroup));

            first.SetSharedComponent(new TestShared(2));

            Assert.That(cache.Prepare(), Is.True);
            Assert.That(cache.cacheRebuildCount, Is.EqualTo(2));
            Assert.That(cache.lastSourceLineScanCount, Is.EqualTo(3));
            Assert.That(FindLineGroup(cache.groups, sharedOneKey), Is.SameAs(sharedOneGroup));
            Assert.That(sharedOneGroup.lineCount, Is.EqualTo(1));

            world.RemoveComponentType(second, sharedIndex);

            Assert.That(cache.Prepare(), Is.True);
            Assert.That(cache.cacheRebuildCount, Is.EqualTo(3));
            Assert.That(FindLineGroup(cache.groups, sharedOneKey), Is.Null);
            Assert.That(sharedOneGroup.lineCount, Is.EqualTo(0));

            second.SetSharedComponent(new TestShared(1));

            Assert.That(cache.Prepare(), Is.True);
            Assert.That(cache.cacheRebuildCount, Is.EqualTo(4));
            Assert.That(FindLineGroup(cache.groups, sharedOneKey), Is.SameAs(sharedOneGroup));
            Assert.That(sharedOneGroup.lineCount, Is.EqualTo(1));
        }

        [Test]
        public void World_LineGroupNativeAttachmentCache_InvalidatesOnlyForGroupsOrAttachments()
        {
            VividEcsTypeIndex dataIndex = VividEcsTypeManager.RegisterComponent<TestData>();
            VividEcsTypeIndex sharedIndex = VividEcsTypeManager.RegisterShared<TestShared>();
            using var world = new VividEcsWorld();
            VividEcsArchetypeLine first = world.CreateArchetypeLine(8, dataIndex, sharedIndex);
            VividEcsArchetypeLine second = world.CreateArchetypeLine(8, dataIndex, sharedIndex);
            first.SetSharedComponent(new TestShared(1));
            second.SetSharedComponent(new TestShared(1));
            world.SetLineAttachment(first, new TestLineAttachment(10));
            world.SetLineAttachment(second, new TestLineAttachment(20));

            VividEcsQuery query = world.CreateQuery().WithAll(dataIndex);
            var groupCache = new VividEcsArchetypeLineGroupCache(query, sharedIndex);
            using var attachmentCache =
                new VividEcsArchetypeLineGroupNativeAttachmentCache<TestLineAttachment>(
                    world,
                    groupCache);

            Assert.That(attachmentCache.Prepare(), Is.True);
            Assert.That(attachmentCache.groupCount, Is.EqualTo(1));
            Assert.That(attachmentCache.attachmentCount, Is.EqualTo(2));
            Assert.That(attachmentCache.ranges[0].Start, Is.EqualTo(0));
            Assert.That(attachmentCache.ranges[0].Count, Is.EqualTo(2));
            Assert.That(attachmentCache.attachments[0].LineId, Is.EqualTo(first.ArchetypeLineId));
            Assert.That(attachmentCache.attachments[0].Value.Value, Is.EqualTo(10));

            Assert.That(attachmentCache.Prepare(), Is.False);
            Assert.That(attachmentCache.lastSourceLineScanCount, Is.EqualTo(0));

            world.SetLineAttachment(first, new TestLineAttachment(30));

            Assert.That(attachmentCache.Prepare(), Is.True);
            Assert.That(groupCache.lastSourceLineScanCount, Is.EqualTo(0));
            Assert.That(attachmentCache.lastSourceLineScanCount, Is.EqualTo(2));
            Assert.That(attachmentCache.attachments[0].Value.Value, Is.EqualTo(30));

            Assert.That(world.RemoveLineAttachment<TestLineAttachment>(second), Is.True);
            Assert.That(attachmentCache.Prepare(), Is.True);
            Assert.That(attachmentCache.attachmentCount, Is.EqualTo(1));
            Assert.That(attachmentCache.ranges[0].Count, Is.EqualTo(1));
        }

        [Test]
        public void World_QueryNativeAttachmentCache_RebuildsOnlyForMembershipOrAttachmentChanges()
        {
            VividEcsTypeIndex dataIndex = VividEcsTypeManager.RegisterComponent<TestData>();
            VividEcsTypeIndex tagIndex = VividEcsTypeManager.RegisterTag<TestTag>();
            using var world = new VividEcsWorld();
            VividEcsArchetypeLine first = world.CreateArchetypeLine(8, dataIndex, tagIndex);
            VividEcsArchetypeLine second = world.CreateArchetypeLine(8, dataIndex);
            world.SetLineAttachment(first, new TestLineAttachment(10));
            world.SetLineAttachment(second, new TestLineAttachment(20));
            VividEcsQuery query = world.CreateQuery().WithAll(dataIndex, tagIndex);
            using var cache = new VividEcsQueryNativeAttachmentCache<TestLineAttachment>(world, query);

            Assert.That(cache.Prepare(), Is.True);
            Assert.That(cache.attachmentCount, Is.EqualTo(1));
            Assert.That(cache.attachments[0].LineId, Is.EqualTo(first.ArchetypeLineId));
            Assert.That(cache.attachments[0].Value.Value, Is.EqualTo(10));

            Assert.That(cache.Prepare(), Is.False);
            Assert.That(cache.lastSourceLineScanCount, Is.EqualTo(0));

            world.SetLineAttachment(first, new TestLineAttachment(30));

            Assert.That(cache.Prepare(), Is.True);
            Assert.That(query.lastSourceScanCount, Is.EqualTo(0));
            Assert.That(cache.lastSourceLineScanCount, Is.EqualTo(1));
            Assert.That(cache.attachments[0].Value.Value, Is.EqualTo(30));

            world.AddComponentType(second, tagIndex);

            Assert.That(cache.Prepare(), Is.True);
            Assert.That(cache.attachmentCount, Is.EqualTo(2));
            Assert.That(cache.attachments[1].LineId, Is.EqualTo(second.ArchetypeLineId));
            Assert.That(cache.attachments[1].Value.Value, Is.EqualTo(20));

            Assert.That(world.RemoveLineAttachment<TestLineAttachment>(first), Is.True);
            Assert.That(cache.Prepare(), Is.True);
            Assert.That(cache.attachmentCount, Is.EqualTo(1));
            Assert.That(cache.attachments[0].LineId, Is.EqualTo(second.ArchetypeLineId));
        }

        [Test]
        public void World_LineGroupNativeSharedAttachmentCache_CachesSharedKeysAndRanges()
        {
            VividEcsTypeIndex dataIndex = VividEcsTypeManager.RegisterComponent<TestData>();
            VividEcsTypeIndex sharedIndex = VividEcsTypeManager.RegisterShared<TestShared>();
            using var world = new VividEcsWorld();
            VividEcsArchetypeLine first = world.CreateArchetypeLine(8, dataIndex, sharedIndex);
            VividEcsArchetypeLine second = world.CreateArchetypeLine(8, dataIndex, sharedIndex);
            VividEcsArchetypeLine third = world.CreateArchetypeLine(8, dataIndex, sharedIndex);
            first.SetSharedComponent(new TestShared(1));
            second.SetSharedComponent(new TestShared(1));
            third.SetSharedComponent(new TestShared(2));
            world.SetLineAttachment(first, new TestLineAttachment(10));
            world.SetLineAttachment(second, new TestLineAttachment(20));
            world.SetLineAttachment(third, new TestLineAttachment(30));

            VividEcsQuery query = world.CreateQuery().WithAll(dataIndex);
            var groupCache = new VividEcsArchetypeLineGroupCache(query, sharedIndex);
            using var nativeCache =
                new VividEcsArchetypeLineGroupNativeSharedAttachmentCache<
                    TestShared,
                    TestLineAttachment>(world, groupCache);

            Assert.That(nativeCache.Prepare(), Is.True);
            Assert.That(nativeCache.groupCount, Is.EqualTo(2));
            Assert.That(nativeCache.attachmentCount, Is.EqualTo(3));
            Assert.That(nativeCache.groups[0].HasSharedComponent, Is.EqualTo(1));
            Assert.That(nativeCache.groups[0].SharedComponent.Value, Is.EqualTo(1));
            Assert.That(nativeCache.groups[0].Start, Is.EqualTo(0));
            Assert.That(nativeCache.groups[0].Count, Is.EqualTo(2));
            Assert.That(nativeCache.groups[1].SharedComponent.Value, Is.EqualTo(2));
            Assert.That(nativeCache.groups[1].Start, Is.EqualTo(2));
            Assert.That(nativeCache.groups[1].Count, Is.EqualTo(1));
            Assert.That(nativeCache.attachments[2].Value.Value, Is.EqualTo(30));

            Assert.That(nativeCache.Prepare(), Is.False);
            Assert.That(nativeCache.lastSourceLineScanCount, Is.EqualTo(0));

            world.SetLineAttachment(first, new TestLineAttachment(40));

            Assert.That(nativeCache.Prepare(), Is.True);
            Assert.That(groupCache.lastSourceLineScanCount, Is.EqualTo(0));
            Assert.That(nativeCache.attachments[0].Value.Value, Is.EqualTo(40));

            second.SetSharedComponent(new TestShared(2));

            Assert.That(nativeCache.Prepare(), Is.True);
            Assert.That(nativeCache.groups[0].SharedComponent.Value, Is.EqualTo(1));
            Assert.That(nativeCache.groups[0].Count, Is.EqualTo(1));
            Assert.That(nativeCache.groups[1].SharedComponent.Value, Is.EqualTo(2));
            Assert.That(nativeCache.groups[1].Count, Is.EqualTo(2));
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
        public void SharedComponentKey_SingleInlineKey_EqualsArrayKey_AndEmptyKeysMatch()
        {
            VividEcsTypeIndex sharedIndex = VividEcsTypeManager.RegisterShared<TestShared>();
            var inlineKey = new VividEcsSharedComponentKey(sharedIndex, new TestShared(7));
            var arrayKey = new VividEcsSharedComponentKey(
                new[] { sharedIndex },
                new object[] { new TestShared(7) });
            var emptyArrayKey = new VividEcsSharedComponentKey(
                System.Array.Empty<VividEcsTypeIndex>(),
                System.Array.Empty<object>());
            VividEcsSharedComponentKey defaultEmptyKey = default;
            var dictionary = new Dictionary<VividEcsSharedComponentKey, int>();

            dictionary.Add(emptyArrayKey, 1);
            dictionary[defaultEmptyKey] = 2;

            Assert.That(inlineKey.Equals(arrayKey), Is.True);
            Assert.That(inlineKey.GetHashCode(), Is.EqualTo(arrayKey.GetHashCode()));
            Assert.That(inlineKey.TryGet(out TestShared inlineValue), Is.True);
            Assert.That(inlineValue, Is.EqualTo(new TestShared(7)));
            Assert.That(arrayKey.TryGet(out TestShared arrayValue), Is.True);
            Assert.That(arrayValue, Is.EqualTo(new TestShared(7)));
            Assert.That(defaultEmptyKey.TryGet(out TestShared _), Is.False);
            Assert.That(defaultEmptyKey.Equals(emptyArrayKey), Is.True);
            Assert.That(defaultEmptyKey.GetHashCode(), Is.EqualTo(emptyArrayKey.GetHashCode()));
            Assert.That(dictionary, Has.Count.EqualTo(1));
            Assert.That(dictionary[emptyArrayKey], Is.EqualTo(2));
        }

        [Test]
        public void World_LineGroups_ReusesScratchCollections_AndSkipsEmptyScratchGroups()
        {
            VividEcsTypeIndex dataIndex = VividEcsTypeManager.RegisterComponent<TestData>();
            VividEcsTypeIndex sharedIndex = VividEcsTypeManager.RegisterShared<TestShared>();
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
            var groups = new List<VividEcsArchetypeLineGroup>();
            var scratchGroups = new Dictionary<VividEcsSharedComponentKey, List<VividEcsArchetypeLine>>();

            world.CreateArchetypeLineGroups(allData, groups, scratchGroups, sharedIndex);

            Assert.That(groups, Has.Count.EqualTo(2));
            Assert.That(scratchGroups, Has.Count.EqualTo(2));
            var firstPassLists = new List<IReadOnlyList<VividEcsArchetypeLine>>();
            for (int index = 0; index < groups.Count; index++)
                firstPassLists.Add(groups[index].lines);

            int mapGroupCount = world.CreateArchetypeLineGroupMap(sharedOne, scratchGroups, sharedIndex);

            Assert.That(mapGroupCount, Is.EqualTo(1));
            Assert.That(scratchGroups, Has.Count.EqualTo(2));
            Assert.That(TryGetSingleNonEmptyLineList(scratchGroups, out List<VividEcsArchetypeLine> mapLines), Is.True);
            Assert.That(mapLines, Has.Count.EqualTo(2));
            Assert.That(ContainsLineListReference(firstPassLists, mapLines), Is.True);

            world.CreateArchetypeLineGroups(sharedOne, groups, scratchGroups, sharedIndex);

            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups[0].activeCount, Is.EqualTo(2));
            Assert.That(scratchGroups, Has.Count.EqualTo(2));
            Assert.That(ContainsLineListReference(firstPassLists, groups[0].lines), Is.True);
        }

        [Test]
        public void World_LineGroupMap_SingleSharedTypeOverload_MatchesParamsPath()
        {
            VividEcsTypeIndex dataIndex = VividEcsTypeManager.RegisterComponent<TestData>();
            VividEcsTypeIndex sharedIndex = VividEcsTypeManager.RegisterShared<TestShared>();
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
            var inlineGroups = new Dictionary<VividEcsSharedComponentKey, List<VividEcsArchetypeLine>>();
            var paramsGroups = new Dictionary<VividEcsSharedComponentKey, List<VividEcsArchetypeLine>>();

            int inlineGroupCount = world.CreateArchetypeLineGroupMap(allData, inlineGroups, sharedIndex);
            int paramsGroupCount = world.CreateArchetypeLineGroupMap(allData, paramsGroups, new[] { sharedIndex });

            Assert.That(first.GetSharedComponentKey(sharedIndex), Is.EqualTo(first.GetSharedComponentKey(new[] { sharedIndex })));
            Assert.That(inlineGroupCount, Is.EqualTo(paramsGroupCount));
            Assert.That(inlineGroups.Keys, Is.EquivalentTo(paramsGroups.Keys));
            foreach (KeyValuePair<VividEcsSharedComponentKey, List<VividEcsArchetypeLine>> pair in inlineGroups)
            {
                Assert.That(paramsGroups.TryGetValue(pair.Key, out List<VividEcsArchetypeLine> lines), Is.True);
                Assert.That(pair.Value, Is.EquivalentTo(lines));
            }
        }

        [Test]
        public void World_PageGroups_FlattenMatchingLinePages()
        {
            VividEcsTypeIndex dataIndex = VividEcsTypeManager.RegisterComponent<TestData>();
            VividEcsTypeIndex sharedIndex = VividEcsTypeManager.RegisterShared<TestShared>();
            using var world = new VividEcsWorld();
            VividEcsArchetypeLine first = world.CreateArchetypeLine(8, dataIndex);
            VividEcsArchetypeLine second = world.CreateArchetypeLine(8, dataIndex);
            first.SetSharedComponent(new TestShared(1));
            second.SetSharedComponent(new TestShared(1));
            first.EnsureCapacity(300);
            second.EnsureCapacity(10);
            first.SetActiveCount(300);
            second.SetActiveCount(10);

            VividEcsQuery allData = world.CreateQuery().WithAll(dataIndex);
            using VividEcsPageGroup worldPages = world.CreatePageGroup(allData, Allocator.TempJob);
            List<VividEcsArchetypeLineGroup> groups = world.CreateArchetypeLineGroups(allData, sharedIndex);
            using VividEcsPageGroup groupedPages = groups[0].CreatePageGroup(Allocator.TempJob);

            Assert.That(worldPages.pageCount, Is.EqualTo(3));
            Assert.That(worldPages[0].ArchetypeLineId, Is.EqualTo(first.ArchetypeLineId));
            Assert.That(worldPages[0].EntryCount, Is.EqualTo(256));
            Assert.That(worldPages[1].ArchetypeLineId, Is.EqualTo(first.ArchetypeLineId));
            Assert.That(worldPages[1].EntryCount, Is.EqualTo(44));
            Assert.That(worldPages[2].ArchetypeLineId, Is.EqualTo(second.ArchetypeLineId));
            Assert.That(worldPages[2].EntryCount, Is.EqualTo(10));
            Assert.That(groupedPages.pageCount, Is.EqualTo(worldPages.pageCount));
            Assert.That(groupedPages[0].EntryCount, Is.EqualTo(worldPages[0].EntryCount));
            Assert.That(groupedPages[2].EntryCount, Is.EqualTo(worldPages[2].EntryCount));
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

                foreach (VividEcsPageDispatchMode dispatchMode in new[]
                         {
                             VividEcsPageDispatchMode.Dynamic,
                             VividEcsPageDispatchMode.Average,
                         })
                {
                    counts[0] = 0;
                    counts[1] = 0;
                    JobHandle handle = job.ScheduleParallel(
                        pageGroup.pages,
                        dispatchMode: dispatchMode);
                    handle.Complete();

                    Assert.That(counts[0], Is.EqualTo(256));
                    Assert.That(counts[1], Is.EqualTo(44));
                }
            }
            finally
            {
                counts.Dispose();
            }
        }

        [Test]
        public void PageJob_CustomProducer_RespectsDependency()
        {
            var pages = new NativeArray<VividEcsPageInfo>(2, Allocator.TempJob, NativeArrayOptions.UninitializedMemory)
            {
                [0] = new VividEcsPageInfo(7, 0, 0, 256),
                [1] = new VividEcsPageInfo(7, 1, 256, 31),
            };
            var gate = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            var counts = new NativeArray<int>(2, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            try
            {
                JobHandle dependency = new WriteRegistryValueJob
                {
                    Values = gate,
                    Index = 0,
                    Value = 9,
                }.Schedule();
                var job = new CapturePageDependencyJob
                {
                    Gate = gate,
                    Counts = counts,
                };

                JobHandle handle = job.Schedule(pages, dependency);
                handle.Complete();

                Assert.That(counts[0], Is.EqualTo(265));
                Assert.That(counts[1], Is.EqualTo(40));
            }
            finally
            {
                counts.Dispose();
                gate.Dispose();
                pages.Dispose();
            }
        }

        [Test]
        public void PageJob_EmbeddedPages_UseWorkStrideWithoutCopy()
        {
            var works = new NativeArray<EmbeddedPageWork>(3, Allocator.TempJob, NativeArrayOptions.UninitializedMemory)
            {
                [0] = new EmbeddedPageWork(new VividEcsPageInfo(4, 0, 0, 256), 3),
                [1] = new EmbeddedPageWork(new VividEcsPageInfo(4, 1, 256, 17), 5),
                [2] = new EmbeddedPageWork(new VividEcsPageInfo(8, 0, 0, 9), 7),
            };
            var values = new NativeArray<int>(3, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            try
            {
                var job = new CaptureEmbeddedPageWorkJob
                {
                    Works = works,
                    Values = values,
                };

                JobHandle handle = job.ScheduleParallelEmbedded(
                    works,
                    pageInfoByteOffset: 0,
                    dispatchMode: VividEcsPageDispatchMode.Dynamic);
                handle.Complete();

                Assert.That(values[0], Is.EqualTo(259));
                Assert.That(values[1], Is.EqualTo(22));
                Assert.That(values[2], Is.EqualTo(16));
            }
            finally
            {
                values.Dispose();
                works.Dispose();
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

                foreach (VividEcsPageDispatchMode dispatchMode in new[]
                         {
                             VividEcsPageDispatchMode.Dynamic,
                             VividEcsPageDispatchMode.Average,
                         })
                {
                    counts[0] = 0;
                    counts[1] = 0;
                    JobHandle handle = job.ScheduleParallel(
                        pages,
                        groups,
                        dispatchMode: dispatchMode);
                    handle.Complete();

                    Assert.That(counts[0], Is.EqualTo(268));
                    Assert.That(counts[1], Is.EqualTo(33));
                }
            }
            finally
            {
                counts.Dispose();
                groups.Dispose();
                pages.Dispose();
            }
        }

        [Test]
        public void PageGroupJob_CustomProducer_RespectsDependency()
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
            var gate = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            var counts = new NativeArray<int>(2, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            try
            {
                JobHandle dependency = new WriteRegistryValueJob
                {
                    Values = gate,
                    Index = 0,
                    Value = 9,
                }.Schedule();
                var job = new CapturePageGroupDependencyJob
                {
                    Gate = gate,
                    Counts = counts,
                };

                JobHandle handle = job.Schedule(pages, groups, dependency);
                handle.Complete();

                Assert.That(counts[0], Is.EqualTo(277));
                Assert.That(counts[1], Is.EqualTo(42));
            }
            finally
            {
                counts.Dispose();
                gate.Dispose();
                groups.Dispose();
                pages.Dispose();
            }
        }

        [Test]
        public void PageGroupJob_EmbeddedPages_UseWorkStrideWithoutCopy()
        {
            var works = new NativeArray<EmbeddedPageWork>(3, Allocator.TempJob, NativeArrayOptions.UninitializedMemory)
            {
                [0] = new EmbeddedPageWork(new VividEcsPageInfo(4, 0, 0, 256), 3),
                [1] = new EmbeddedPageWork(new VividEcsPageInfo(4, 1, 256, 17), 5),
                [2] = new EmbeddedPageWork(new VividEcsPageInfo(8, 2, 0, 9), 7),
            };
            var groups = new NativeArray<int2>(2, Allocator.TempJob, NativeArrayOptions.UninitializedMemory)
            {
                [0] = new int2(0, 2),
                [1] = new int2(2, 1),
            };
            var values = new NativeArray<int>(2, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            try
            {
                NativeSlice<VividEcsPageInfo> pages =
                    VividEcsPageJobExtensions.CreateEmbeddedPageSlice(works, pageInfoByteOffset: 0);
                var job = new CaptureEmbeddedPageGroupWorkJob
                {
                    Works = works,
                    Values = values,
                };

                JobHandle handle = job.ScheduleParallelSlice(
                    pages,
                    groups,
                    dispatchMode: VividEcsPageDispatchMode.Dynamic);
                handle.Complete();

                Assert.That(values[0], Is.EqualTo(281));
                Assert.That(values[1], Is.EqualTo(16));
            }
            finally
            {
                values.Dispose();
                groups.Dispose();
                works.Dispose();
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
                    (context, dependency) => new WriteParallelRegistryValueJob
                    {
                        Values = context.Values,
                        Index = 0,
                        Value = 11,
                    }.Schedule(dependency));
                registry.RegisterModule(
                    "b",
                    0,
                    ModuleRegistryContext.ModuleB,
                    (context, dependency) => new WriteParallelRegistryValueJob
                    {
                        Values = context.Values,
                        Index = 1,
                        Value = 22,
                    }.Schedule(dependency));
                registry.RegisterModule(
                    "disabled",
                    5,
                    ModuleRegistryContext.ModuleC,
                    (context, dependency) => new WriteParallelRegistryValueJob
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

        private readonly struct TestLineAttachment : IVividEcsLineAttachmentData
        {
            public TestLineAttachment(int value)
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

            public void Execute(VividEcsPageInfo page, int pageIndex)
            {
                Counts[pageIndex] = page.EntryCount;
            }
        }

        [BurstCompile]
        private struct CapturePageDependencyJob : IVividEcsPageJob
        {
            [ReadOnly]
            public NativeArray<int> Gate;

            public NativeArray<int> Counts;

            public void Execute(VividEcsPageInfo page, int pageIndex)
            {
                Counts[pageIndex] = Gate[0] + page.EntryCount;
            }
        }

        private readonly struct EmbeddedPageWork
        {
            public EmbeddedPageWork(VividEcsPageInfo page, int value)
            {
                Page = page;
                Value = value;
            }

            public readonly VividEcsPageInfo Page;

            public readonly int Value;
        }

        [BurstCompile]
        private struct CaptureEmbeddedPageWorkJob : IVividEcsPageJob
        {
            [ReadOnly]
            public NativeArray<EmbeddedPageWork> Works;

            public NativeArray<int> Values;

            public void Execute(VividEcsPageInfo page, int pageIndex)
            {
                Values[pageIndex] = page.EntryCount + Works[pageIndex].Value;
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

        [BurstCompile]
        private struct CapturePageGroupDependencyJob : IVividEcsPageGroupJob
        {
            [ReadOnly]
            public NativeArray<int> Gate;

            public NativeArray<int> Counts;

            public void Execute(VividEcsPageGroupInfo pageGroup)
            {
                int count = Gate[0];
                for (int index = 0; index < pageGroup.PageCount; index++)
                    count += pageGroup[index].EntryCount;

                Counts[pageGroup.StartIndex == 0 ? 0 : 1] = count;
            }
        }

        [BurstCompile]
        private struct CaptureEmbeddedPageGroupWorkJob : IVividEcsPageGroupJob
        {
            [ReadOnly]
            public NativeArray<EmbeddedPageWork> Works;

            public NativeArray<int> Values;

            public void Execute(VividEcsPageGroupInfo pageGroup)
            {
                int value = 0;
                for (int index = 0; index < pageGroup.PageCount; index++)
                {
                    VividEcsPageInfo page = pageGroup[index];
                    value += page.EntryCount + Works[page.PageIndex].Value;
                }

                Values[pageGroup.StartIndex == 0 ? 0 : 1] = value;
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

        private struct WriteParallelRegistryValueJob : IJob
        {
            [NativeDisableContainerSafetyRestriction]
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

        private static VividEcsArchetypeLineGroup FindLineGroup(
            IReadOnlyList<VividEcsArchetypeLineGroup> groups,
            VividEcsSharedComponentKey sharedKey)
        {
            for (int index = 0; index < groups.Count; index++)
            {
                if (groups[index].SharedKey.Equals(sharedKey))
                    return groups[index];
            }

            return null;
        }

        private static bool ContainsLineListReference(
            List<IReadOnlyList<VividEcsArchetypeLine>> lists,
            IReadOnlyList<VividEcsArchetypeLine> target)
        {
            for (int index = 0; index < lists.Count; index++)
            {
                if (ReferenceEquals(lists[index], target))
                    return true;
            }

            return false;
        }

        private static bool TryGetSingleNonEmptyLineList(
            Dictionary<VividEcsSharedComponentKey, List<VividEcsArchetypeLine>> groups,
            out List<VividEcsArchetypeLine> result)
        {
            result = null;
            foreach (KeyValuePair<VividEcsSharedComponentKey, List<VividEcsArchetypeLine>> pair in groups)
            {
                if (pair.Value.Count == 0)
                    continue;

                if (result != null)
                    return false;

                result = pair.Value;
            }

            return result != null;
        }
    }
}
