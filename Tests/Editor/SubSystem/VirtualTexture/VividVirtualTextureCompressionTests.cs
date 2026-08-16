using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using VividRP.Editor;
using VividRP.Runtime;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class VividVirtualTextureCompressionTests
    {
        private sealed class RecordingIOBackend : IVTIOBackend
        {
            internal List<List<VTIOReadCommand>> Batches { get; } = new();

            internal List<long> PollOrder { get; } = new();

            public string Name => nameof(RecordingIOBackend);

            public bool IsAvailable => true;

            public IVTIOBatch CreateBatch(
                string path,
                IReadOnlyList<VTIOReadCommand> commands)
            {
                Batches.Add(new List<VTIOReadCommand>(commands));
                return new RecordingIOBatch(
                    commands.Count,
                    commands[0].FileOffset,
                    PollOrder);
            }

            public void Dispose()
            {
            }
        }

        private sealed class RecordingIOBatch : IVTIOBatch
        {
            private readonly long m_FirstFileOffset;
            private readonly List<long> m_PollOrder;

            internal RecordingIOBatch(
                int count,
                long firstFileOffset,
                List<long> pollOrder)
            {
                Count = count;
                m_FirstFileOffset = firstFileOffset;
                m_PollOrder = pollOrder;
            }

            public int Count { get; }

            public bool IsCompleted
            {
                get
                {
                    m_PollOrder.Add(m_FirstFileOffset);
                    return false;
                }
            }

            public bool Failed => false;

            public string Error => null;

            public bool TryGetResult(int commandIndex, out byte[] data)
            {
                data = null;
                return false;
            }

            public void Cancel()
            {
            }

            public void Dispose()
            {
            }
        }

        private sealed class CompletedIOBackend : IVTIOBackend
        {
            public string Name => nameof(CompletedIOBackend);

            public bool IsAvailable => true;

            public IVTIOBatch CreateBatch(
                string path,
                IReadOnlyList<VTIOReadCommand> commands)
            {
                return new CompletedIOBatch(commands);
            }

            public void Dispose()
            {
            }
        }

        private sealed class CompletedIOBatch : IVTIOBatch
        {
            private readonly byte[][] m_Results;

            internal CompletedIOBatch(IReadOnlyList<VTIOReadCommand> commands)
            {
                m_Results = new byte[commands.Count][];
                for (int commandIndex = 0; commandIndex < commands.Count; commandIndex++)
                    m_Results[commandIndex] = new byte[commands[commandIndex].ByteSize];
            }

            public int Count => m_Results.Length;

            public bool IsCompleted => true;

            public bool Failed => false;

            public string Error => null;

            public bool TryGetResult(int commandIndex, out byte[] data)
            {
                data = commandIndex >= 0 && commandIndex < m_Results.Length
                    ? m_Results[commandIndex]
                    : null;
                return data != null;
            }

            public void Cancel()
            {
            }

            public void Dispose()
            {
            }
        }

        private sealed class CapturingStorageEncoder : IVTGpuStorageEncoder
        {
            internal Color32[] NormalPage { get; private set; }

            public string Version => "CapturingStorageEncoder-v1";

            public bool TryEncodePages(
                GraphicsFormat destinationFormat,
                int physicalPageSize,
                System.Collections.Generic.IReadOnlyList<Color32[]> pages,
                VividVirtualTextureBCQuality quality,
                out byte[][] encodedPages,
                out string error)
            {
                if (destinationFormat == GraphicsFormat.RG_BC5_UNorm && pages.Count > 0)
                    NormalPage = (Color32[])pages[0].Clone();

                int pageByteSize = VTUnityBCnStorageEncoder.GetPageByteSize(
                    destinationFormat,
                    physicalPageSize);
                encodedPages = new byte[pages.Count][];
                for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
                    encodedPages[pageIndex] = new byte[pageByteSize];
                error = null;
                return true;
            }
        }

        [Test]
        public void ChunkDescriptorV2_Preserves64BitRangeCodecAndCrc()
        {
            const long offset = (long)int.MaxValue + 8192;
            var descriptor = new VividVirtualTextureChunkDescriptor(
                firstMip: 2,
                mipCount: 3,
                firstTile: 17,
                tileCount: 5,
                fileOffset: offset,
                storedByteSize: 1234,
                decodedByteSize: 4096,
                compression: VividVirtualTextureStreamCompression.Zstd,
                decodedPayloadCRC: 0x12345678,
                flags: VividVirtualTextureChunkFlags.MipTail);

            Assert.That(descriptor.UsesContainerSchemaV2, Is.True);
            Assert.That(descriptor.FileOffset, Is.EqualTo(offset));
            Assert.That(descriptor.StoredByteSize, Is.EqualTo(1234));
            Assert.That(descriptor.DecodedByteSize, Is.EqualTo(4096));
            Assert.That(descriptor.Compression, Is.EqualTo(VividVirtualTextureStreamCompression.Zstd));
            Assert.That(descriptor.DecodedPayloadCRC, Is.EqualTo(0x12345678));
            Assert.That(descriptor.FirstTile, Is.EqualTo(17));
            Assert.That(descriptor.TileCount, Is.EqualTo(5));
            Assert.That(descriptor.Flags, Is.EqualTo(VividVirtualTextureChunkFlags.MipTail));
            Assert.That(descriptor.ContainsByteRange(4000, 96), Is.True);
            Assert.That(descriptor.ContainsByteRange(4000, 97), Is.False);
        }

        [Test]
        public void DecodedPayloadCrc_MatchesStandardCrc32Vector()
        {
            byte[] data = System.Text.Encoding.ASCII.GetBytes("123456789");

            Assert.That(VTDecodedPayloadCRC.Compute(data), Is.EqualTo(0xcbf43926u));
        }

        [Test]
        public void ChunkIdentity_SharesV2ChunkButSeparatesLegacySyntheticTileRanges()
        {
            var firstV2Location = new VividVirtualTextureTilePayloadLocation(
                3, 4096, 256, 512, 0, 128,
                VividVirtualTextureStreamCompression.Zstd, 1,
                VividVirtualTextureChunkFlags.None);
            var secondV2Location = new VividVirtualTextureTilePayloadLocation(
                3, 4096, 256, 512, 128, 128,
                VividVirtualTextureStreamCompression.Zstd, 1,
                VividVirtualTextureChunkFlags.None);
            Assert.That(
                new VTStreamChunkManager.ChunkKey("asset.stream", 9, firstV2Location),
                Is.EqualTo(new VTStreamChunkManager.ChunkKey("asset.stream", 9, secondV2Location)));

            var firstLegacyLocation = new VividVirtualTextureTilePayloadLocation(
                0, 1024, 128, 128, 0, 128,
                VividVirtualTextureStreamCompression.None, 0,
                VividVirtualTextureChunkFlags.LegacySynthetic);
            var secondLegacyLocation = new VividVirtualTextureTilePayloadLocation(
                0, 1152, 128, 128, 0, 128,
                VividVirtualTextureStreamCompression.None, 0,
                VividVirtualTextureChunkFlags.LegacySynthetic);
            Assert.That(
                new VTStreamChunkManager.ChunkKey("asset.stream", 9, firstLegacyLocation),
                Is.Not.EqualTo(new VTStreamChunkManager.ChunkKey("asset.stream", 9, secondLegacyLocation)));
        }

        [Test]
        public void ResetSharedState_PreservesConfiguredDecodedCacheBudget()
        {
            VTStreamChunkManager.ResetShared();
            try
            {
                VTStreamChunkManager.Shared.Configure(
                    VividVirtualTextureIOBackendMode.AsyncReadManager,
                    maxInFlightChunkCount: 17,
                    decodeConcurrency: 3,
                    decodedCacheBudgetMiB: 19);

                VTStreamChunkManager.ResetSharedState();

                Assert.That(
                    VTStreamChunkManager.SharedDecodedCacheBudget,
                    Is.EqualTo(19L * 1024L * 1024L));
            }
            finally
            {
                VTStreamChunkManager.ResetShared();
            }
        }

        [Test]
        public void RequestPriorityKey_PreservesViewProducerMipAndIOTierOrdering()
        {
            var backgroundRequest = new VTRequest(
                1,
                new VirtualTexturePageCoord(0, 0, 0),
                0,
                1,
                priority: 8,
                requestFrame: 4,
                cameraPriority: 0,
                isActiveView: false);
            var activeRequest = new VTRequest(
                1,
                new VirtualTexturePageCoord(1, 0, 0),
                1,
                1,
                priority: 1,
                requestFrame: 4,
                cameraPriority: 0,
                isActiveView: true);
            VTRequestPriorityKey background = VTRequestPriorityKey.FromRequest(
                backgroundRequest,
                locked: false,
                producerPriority: 10);
            VTRequestPriorityKey active = VTRequestPriorityKey.FromRequest(
                activeRequest,
                locked: false,
                producerPriority: 0);

            Assert.That(VTRequestPriorityUtility.Compare(active, background), Is.LessThan(0));
            Assert.That(active.IOTier, Is.EqualTo(VTIOPriorityTier.High));
            Assert.That(background.IOTier, Is.EqualTo(VTIOPriorityTier.Normal));

            VTRequestPriorityKey lowProducer = VTRequestPriorityKey.FromRequest(
                backgroundRequest,
                locked: false,
                producerPriority: 0);
            VTRequestPriorityKey highProducer = VTRequestPriorityKey.FromRequest(
                backgroundRequest,
                locked: false,
                producerPriority: 2);
            Assert.That(VTRequestPriorityUtility.Compare(highProducer, lowProducer), Is.LessThan(0));

            var coarseRequest = new VTRequest(
                1,
                new VirtualTexturePageCoord(0, 0, 2),
                2,
                1,
                priority: 1,
                requestFrame: 4,
                cameraPriority: 0,
                isActiveView: false);
            VTRequestPriorityKey coarse = VTRequestPriorityKey.FromRequest(
                coarseRequest,
                locked: false,
                producerPriority: 0,
                mipTail: true);
            Assert.That(VTRequestPriorityUtility.CompareForIO(coarse, background), Is.LessThan(0));
            Assert.That(coarse.IOTier, Is.EqualTo(VTIOPriorityTier.Critical));
            Assert.That(
                VTRequestPriorityUtility.ComputeMipWeightedScore(int.MaxValue, int.MaxValue),
                Is.EqualTo((long)int.MaxValue * ((long)int.MaxValue + 1L)));
        }

        [Test]
        public void ChunkManager_SubmitsActiveViewBeforeBackgroundAndMapsIOTier()
        {
            VTStreamChunkManager.ResetShared();
            try
            {
                VTStreamChunkManager manager = VTStreamChunkManager.Shared;
                var backend = new RecordingIOBackend();
                manager.SetIOBackendForTesting(backend);
                var backgroundLocation = CreateRawLocation(chunkIndex: 0, fileOffset: 64);
                var activeLocation = CreateRawLocation(chunkIndex: 1, fileOffset: 0);
                var backgroundRequest = new VTRequest(
                    1,
                    new VirtualTexturePageCoord(0, 0, 0),
                    0,
                    1,
                    priority: 16,
                    requestFrame: 1,
                    cameraPriority: 0,
                    isActiveView: false);
                var activeRequest = new VTRequest(
                    1,
                    new VirtualTexturePageCoord(1, 0, 0),
                    1,
                    1,
                    priority: 1,
                    requestFrame: 1,
                    cameraPriority: 0,
                    isActiveView: true);

                using VTChunkLease backgroundLease = manager.Acquire(
                    "priority.stream",
                    1,
                    backgroundLocation,
                    VTRequestPriorityKey.FromRequest(
                        backgroundRequest,
                        locked: false,
                        producerPriority: 0));
                using VTChunkLease activeLease = manager.Acquire(
                    "priority.stream",
                    1,
                    activeLocation,
                    VTRequestPriorityKey.FromRequest(
                        activeRequest,
                        locked: false,
                        producerPriority: 0));

                manager.SubmitPendingReads();

                Assert.That(backend.Batches, Has.Count.EqualTo(2));
                Assert.That(backend.Batches[0], Has.Count.EqualTo(1));
                Assert.That(backend.Batches[0][0].FileOffset, Is.EqualTo(0));
                Assert.That(backend.Batches[0][0].HighPriority, Is.True);
                Assert.That(backend.Batches[1][0].FileOffset, Is.EqualTo(64));
                Assert.That(backend.Batches[1][0].HighPriority, Is.False);

                manager.PollProgress();
                Assert.That(backend.PollOrder, Is.EqualTo(new long[] { 0, 64 }));
            }
            finally
            {
                VTStreamChunkManager.ResetShared();
            }
        }

        [Test]
        public void ChunkManager_PollsLaterActiveBatchBeforeEarlierLazyBackgroundBatch()
        {
            VTStreamChunkManager.ResetShared();
            try
            {
                VTStreamChunkManager manager = VTStreamChunkManager.Shared;
                var backend = new RecordingIOBackend();
                manager.SetIOBackendForTesting(backend);
                var backgroundRequest = new VTRequest(
                    1,
                    new VirtualTexturePageCoord(0, 0, 0),
                    0,
                    1,
                    priority: 1,
                    requestFrame: 1,
                    cameraPriority: 1,
                    isActiveView: false);
                using VTChunkLease backgroundLease = manager.Acquire(
                    "lazy-priority.stream",
                    1,
                    CreateRawLocation(chunkIndex: 0, fileOffset: 64),
                    VTRequestPriorityKey.FromRequest(
                        backgroundRequest,
                        locked: false,
                        producerPriority: 0));
                manager.SubmitPendingReads();

                var activeRequest = new VTRequest(
                    1,
                    new VirtualTexturePageCoord(1, 0, 0),
                    1,
                    1,
                    priority: 1,
                    requestFrame: 2,
                    cameraPriority: 0,
                    isActiveView: true);
                using VTChunkLease activeLease = manager.Acquire(
                    "lazy-priority.stream",
                    1,
                    CreateRawLocation(chunkIndex: 1, fileOffset: 0),
                    VTRequestPriorityKey.FromRequest(
                        activeRequest,
                        locked: false,
                        producerPriority: 0));
                manager.SubmitPendingReads();

                Assert.That(backend.Batches, Has.Count.EqualTo(2));
                Assert.That(backend.Batches[0][0].FileOffset, Is.EqualTo(64));
                Assert.That(backend.Batches[1][0].FileOffset, Is.Zero);

                manager.PollProgress();

                Assert.That(backend.PollOrder, Is.EqualTo(new long[] { 0, 64 }));
            }
            finally
            {
                VTStreamChunkManager.ResetShared();
            }
        }

        [Test]
        public void ChunkManager_PromotesQueuedChunkBeforeSubmission()
        {
            VTStreamChunkManager.ResetShared();
            try
            {
                VTStreamChunkManager manager = VTStreamChunkManager.Shared;
                var backend = new RecordingIOBackend();
                manager.SetIOBackendForTesting(backend);
                VividVirtualTextureTilePayloadLocation location =
                    CreateRawLocation(chunkIndex: 0, fileOffset: 32);
                var backgroundRequest = new VTRequest(
                    1,
                    new VirtualTexturePageCoord(0, 0, 0),
                    0,
                    1,
                    priority: 1,
                    requestFrame: 1,
                    cameraPriority: 1,
                    isActiveView: false);
                var activeRequest = new VTRequest(
                    1,
                    backgroundRequest.PageCoord,
                    0,
                    1,
                    priority: 4,
                    requestFrame: 2,
                    cameraPriority: 0,
                    isActiveView: true);

                using VTChunkLease lease = manager.Acquire(
                    "promotion.stream",
                    1,
                    location,
                    VTRequestPriorityKey.FromRequest(
                        backgroundRequest,
                        locked: false,
                        producerPriority: 0));
                lease.PromotePriority(VTRequestPriorityKey.FromRequest(
                    activeRequest,
                    locked: false,
                    producerPriority: 0));

                manager.SubmitPendingReads();

                Assert.That(backend.Batches, Has.Count.EqualTo(1));
                Assert.That(backend.Batches[0], Has.Count.EqualTo(1));
                Assert.That(backend.Batches[0][0].HighPriority, Is.True);
            }
            finally
            {
                VTStreamChunkManager.ResetShared();
            }
        }

        [Test]
        public void ChunkManager_BoundsDecodeConcurrencyAndStartsHighestPriorityFirst()
        {
            using var firstDecodeStarted = new ManualResetEventSlim();
            using var releaseFirstDecode = new ManualResetEventSlim();
            var decodeOrder = new List<long>();
            var manager = new VTStreamChunkManager(state =>
            {
                var entry = (VTStreamChunkManager.ChunkEntry)state;
                lock (decodeOrder)
                    decodeOrder.Add(entry.Location.FileOffset);
                if (entry.Location.FileOffset == 0)
                {
                    firstDecodeStarted.Set();
                    releaseFirstDecode.Wait(TimeSpan.FromSeconds(5));
                }

                return new VTStreamChunkManager.DecodeResult(entry.StoredData, null);
            });
            try
            {
                manager.SetIOBackendForTesting(new CompletedIOBackend());
                manager.Configure(
                    VividVirtualTextureIOBackendMode.AsyncReadManager,
                    maxInFlightChunkCount: 4,
                    decodeConcurrency: 1,
                    decodedCacheBudgetMiB: 1);
                var backgroundRequest = new VTRequest(
                    1,
                    new VirtualTexturePageCoord(0, 0, 0),
                    0,
                    1,
                    priority: 16,
                    requestFrame: 1,
                    cameraPriority: 0,
                    isActiveView: false);
                var activeRequest = new VTRequest(
                    1,
                    new VirtualTexturePageCoord(1, 0, 0),
                    1,
                    1,
                    priority: 1,
                    requestFrame: 1,
                    cameraPriority: 0,
                    isActiveView: true);
                using VTChunkLease backgroundLease = manager.Acquire(
                    "decode-priority.stream",
                    1,
                    CreateRawLocation(chunkIndex: 0, fileOffset: 64),
                    VTRequestPriorityKey.FromRequest(
                        backgroundRequest,
                        locked: false,
                        producerPriority: 0));
                using VTChunkLease activeLease = manager.Acquire(
                    "decode-priority.stream",
                    1,
                    CreateRawLocation(chunkIndex: 1, fileOffset: 0),
                    VTRequestPriorityKey.FromRequest(
                        activeRequest,
                        locked: false,
                        producerPriority: 0));

                manager.SubmitPendingReads();
                manager.PollProgress();

                Assert.That(firstDecodeStarted.Wait(TimeSpan.FromSeconds(5)), Is.True);
                lock (decodeOrder)
                    Assert.That(decodeOrder, Is.EqualTo(new long[] { 0 }));
                Assert.That(manager.ActiveDecodeCount, Is.EqualTo(1));
                Assert.That(manager.PendingDecodeCount, Is.EqualTo(1));
                Assert.That(manager.LastDecodeSaturationCount, Is.EqualTo(1));
                manager.BeginFrame();
                Assert.That(manager.LastDecodeSaturationCount, Is.EqualTo(1));

                releaseFirstDecode.Set();
                WaitForLease(manager, activeLease);
                Assert.That(
                    SpinWait.SpinUntil(
                        () =>
                        {
                            lock (decodeOrder)
                                return decodeOrder.Count == 2;
                        },
                        TimeSpan.FromSeconds(5)),
                    Is.True);
                lock (decodeOrder)
                    Assert.That(decodeOrder, Is.EqualTo(new long[] { 0, 64 }));
                WaitForLease(manager, backgroundLease);
            }
            finally
            {
                releaseFirstDecode.Set();
                manager.Dispose();
            }
        }

        [Test]
        public void ChunkManager_ReleasesUnreferencedPendingDecodeWithoutStartingIt()
        {
            using var firstDecodeStarted = new ManualResetEventSlim();
            using var releaseFirstDecode = new ManualResetEventSlim();
            var decodeOrder = new List<long>();
            var manager = new VTStreamChunkManager(state =>
            {
                var entry = (VTStreamChunkManager.ChunkEntry)state;
                lock (decodeOrder)
                    decodeOrder.Add(entry.Location.FileOffset);
                if (entry.Location.FileOffset == 0)
                {
                    firstDecodeStarted.Set();
                    releaseFirstDecode.Wait(TimeSpan.FromSeconds(5));
                }

                return new VTStreamChunkManager.DecodeResult(entry.StoredData, null);
            });
            VTChunkLease activeLease = null;
            VTChunkLease backgroundLease = null;
            try
            {
                manager.SetIOBackendForTesting(new CompletedIOBackend());
                manager.Configure(
                    VividVirtualTextureIOBackendMode.AsyncReadManager,
                    maxInFlightChunkCount: 4,
                    decodeConcurrency: 1,
                    decodedCacheBudgetMiB: 1);
                var activeRequest = new VTRequest(
                    1,
                    new VirtualTexturePageCoord(0, 0, 0),
                    0,
                    1,
                    priority: 1,
                    requestFrame: 1,
                    cameraPriority: 0,
                    isActiveView: true);
                var backgroundRequest = new VTRequest(
                    1,
                    new VirtualTexturePageCoord(1, 0, 0),
                    1,
                    1,
                    priority: 1,
                    requestFrame: 1,
                    cameraPriority: 1,
                    isActiveView: false);
                activeLease = manager.Acquire(
                    "decode-release.stream",
                    1,
                    CreateRawLocation(chunkIndex: 0, fileOffset: 0),
                    VTRequestPriorityKey.FromRequest(
                        activeRequest,
                        locked: false,
                        producerPriority: 0));
                backgroundLease = manager.Acquire(
                    "decode-release.stream",
                    1,
                    CreateRawLocation(chunkIndex: 1, fileOffset: 64),
                    VTRequestPriorityKey.FromRequest(
                        backgroundRequest,
                        locked: false,
                        producerPriority: 0));

                manager.SubmitPendingReads();
                manager.PollProgress();

                Assert.That(firstDecodeStarted.Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(manager.ActiveDecodeCount, Is.EqualTo(1));
                Assert.That(manager.PendingDecodeCount, Is.EqualTo(1));
                Assert.That(manager.PendingChunkCount, Is.EqualTo(2));

                backgroundLease.Dispose();
                backgroundLease = null;

                Assert.That(manager.PendingDecodeCount, Is.Zero);
                Assert.That(manager.PendingChunkCount, Is.EqualTo(1));

                releaseFirstDecode.Set();
                WaitForLease(manager, activeLease);
                lock (decodeOrder)
                    Assert.That(decodeOrder, Is.EqualTo(new long[] { 0 }));
            }
            finally
            {
                releaseFirstDecode.Set();
                backgroundLease?.Dispose();
                activeLease?.Dispose();
                manager.Dispose();
            }
        }

        [Test]
        public void ChunkManager_SharesAsyncReadLeaseAndEvictsUnreferencedReadyData()
        {
            string streamPath = Path.Combine(Path.GetTempPath(), $"VividVT_{Guid.NewGuid():N}.stream");
            byte[] decoded = { 5, 7, 11, 13, 17, 19 };
            File.WriteAllBytes(streamPath, decoded);
            VTStreamChunkManager.ResetShared();
            try
            {
                VTStreamChunkManager manager = VTStreamChunkManager.Shared;
                manager.Configure(
                    VividVirtualTextureIOBackendMode.AsyncReadManager,
                    maxInFlightChunkCount: 4,
                    decodeConcurrency: 2,
                    decodedCacheBudgetMiB: 0);
                var location = new VividVirtualTextureTilePayloadLocation(
                    chunkIndex: 0,
                    fileOffset: 0,
                    storedByteSize: decoded.Length,
                    decodedByteSize: decoded.Length,
                    tileByteOffset: 0,
                    tileByteSize: decoded.Length,
                    compression: VividVirtualTextureStreamCompression.None,
                    decodedPayloadCRC: VTDecodedPayloadCRC.Compute(decoded),
                    flags: VividVirtualTextureChunkFlags.None);

                using VTChunkLease first = manager.Acquire(streamPath, 1, location, highPriority: false);
                using VTChunkLease second = manager.Acquire(streamPath, 1, location, highPriority: false);
                Assert.That(first, Is.Not.Null);
                Assert.That(second, Is.Not.Null);
                Assert.That(manager.PendingChunkCount, Is.EqualTo(1));
                manager.SubmitPendingReads();
                WaitForLease(manager, first);
                Assert.That(first.State, Is.EqualTo(VTStreamChunkState.Ready), first.Error);
                Assert.That(manager.PendingChunkCount, Is.Zero);
                Assert.That(first.TryGetTilePayload(location, out VividVirtualTextureTilePayload payload), Is.True);
                Assert.That(payload.Data, Is.EqualTo(decoded));

                first.Dispose();
                second.Dispose();
                using VTChunkLease afterEviction = manager.Acquire(streamPath, 1, location, highPriority: false);
                Assert.That(afterEviction.State, Is.EqualTo(VTStreamChunkState.Queued));
                Assert.That(manager.PendingChunkCount, Is.EqualTo(1));
            }
            finally
            {
                VTStreamChunkManager.ResetShared();
                File.Delete(streamPath);
            }
        }

        [Test]
        public void ChunkManager_CrcMismatchBecomesPermanentForSharedChunkIdentity()
        {
            string streamPath = Path.Combine(Path.GetTempPath(), $"VividVT_{Guid.NewGuid():N}.stream");
            byte[] decoded = { 1, 3, 3, 7 };
            File.WriteAllBytes(streamPath, decoded);
            VTStreamChunkManager.ResetShared();
            try
            {
                VTStreamChunkManager manager = VTStreamChunkManager.Shared;
                manager.Configure(
                    VividVirtualTextureIOBackendMode.AsyncReadManager,
                    maxInFlightChunkCount: 4,
                    decodeConcurrency: 2,
                    decodedCacheBudgetMiB: 1);
                var location = new VividVirtualTextureTilePayloadLocation(
                    chunkIndex: 0,
                    fileOffset: 0,
                    storedByteSize: decoded.Length,
                    decodedByteSize: decoded.Length,
                    tileByteOffset: 0,
                    tileByteSize: decoded.Length,
                    compression: VividVirtualTextureStreamCompression.None,
                    decodedPayloadCRC: 0x12345678,
                    flags: VividVirtualTextureChunkFlags.None);

                using VTChunkLease first = manager.Acquire(streamPath, 2, location, highPriority: false);
                manager.SubmitPendingReads();
                WaitForLease(manager, first);
                Assert.That(first.State, Is.EqualTo(VTStreamChunkState.Failed));
                Assert.That(first.Error, Does.Contain("CRC mismatch"));

                using VTChunkLease second = manager.Acquire(streamPath, 2, location, highPriority: false);
                Assert.That(second.State, Is.EqualTo(VTStreamChunkState.Failed));
                Assert.That(manager.PendingChunkCount, Is.Zero);
            }
            finally
            {
                VTStreamChunkManager.ResetShared();
                File.Delete(streamPath);
            }
        }

        [Test]
        public void ZstdCodec_RoundTripsIndependentFramesAtSupportedLevels()
        {
            var codec = new VTZstdStreamCodec();
            if (!codec.IsAvailable)
                Assert.Ignore("VividVTStreamingNative is not imported in this Editor session.");

            var decoded = new byte[256 * 1024];
            for (int index = 0; index < decoded.Length; index++)
                decoded[index] = (byte)(index % 19);

            for (int level = 1; level <= 3; level++)
            {
                Assert.That(
                    codec.TryEncode(decoded, level, out byte[] stored, out string encodeError),
                    Is.True,
                    $"level {level}: {encodeError}");
                Assert.That(stored.Length, Is.LessThan(decoded.Length));
                Assert.That(
                    codec.TryDecode(stored, decoded.Length, out byte[] roundTrip, out string decodeError),
                    Is.True,
                    $"level {level}: {decodeError}");
                Assert.That(roundTrip, Is.EqualTo(decoded));
            }
        }

        [Test]
        public void RawStreamCodec_TransfersReadBufferWithoutCopy()
        {
            var codec = new VTNoneStreamCodec();
            byte[] stored = { 1, 2, 3, 4 };

            Assert.That(codec.TryDecode(stored, stored.Length, out byte[] decoded, out string error), Is.True, error);
            Assert.That(decoded, Is.SameAs(stored));
        }

        [Test]
        public void RawStreamCodec_RejectsTruncationWithoutImplicitTranscode()
        {
            var codec = new VTNoneStreamCodec();
            byte[] stored = { 1, 2, 3, 4 };

            Assert.That(codec.TryDecode(stored, 5, out byte[] decoded, out string error), Is.False);
            Assert.That(decoded, Is.Null);
            Assert.That(error, Does.Contain("sizes differ"));
        }

        [Test]
        public void ConstantEncodedBlocks_UseValidBc4Bc5AndBc7Mode6Layouts()
        {
            byte[] bc4 = VTConstantEncodedPageFinalizer.EncodeBc4Block(73);
            Assert.That(bc4, Has.Length.EqualTo(8));
            Assert.That(bc4[0], Is.EqualTo(73));
            Assert.That(bc4[1], Is.EqualTo(73));

            byte[] bc5 = VTConstantEncodedPageFinalizer.EncodeBc5Block(31, 219);
            Assert.That(bc5, Has.Length.EqualTo(16));
            Assert.That(bc5[0], Is.EqualTo(31));
            Assert.That(bc5[1], Is.EqualTo(31));
            Assert.That(bc5[8], Is.EqualTo(219));
            Assert.That(bc5[9], Is.EqualTo(219));

            var source = new Color32(128, 64, 255, 32);
            byte[] bc7 = VTConstantEncodedPageFinalizer.EncodeBc7Mode6Block(source);
            Assert.That(bc7, Has.Length.EqualTo(16));
            int bitPosition = 0;
            Assert.That(ReadBits(bc7, ref bitPosition, 7), Is.EqualTo(1ul << 6));
            var endpoint0 = new ulong[4];
            var endpoint1 = new ulong[4];
            for (int channel = 0; channel < 4; channel++)
            {
                endpoint0[channel] = ReadBits(bc7, ref bitPosition, 7);
                endpoint1[channel] = ReadBits(bc7, ref bitPosition, 7);
            }

            ulong p0 = ReadBits(bc7, ref bitPosition, 1);
            ulong p1 = ReadBits(bc7, ref bitPosition, 1);
            byte[] expected = { source.r, source.g, source.b, source.a };
            for (int channel = 0; channel < expected.Length; channel++)
            {
                Assert.That(endpoint0[channel], Is.EqualTo(endpoint1[channel]));
                Assert.That(p0, Is.EqualTo(p1));
                int decoded = (int)(endpoint0[channel] * 2 + p0);
                Assert.That(decoded, Is.InRange(expected[channel] - 1, expected[channel] + 1));
            }

            Assert.That(ReadBits(bc7, ref bitPosition, 63), Is.Zero);
            Assert.That(bitPosition, Is.EqualTo(128));
        }

        [Test]
        public void PipelineAsset_UsesBoundedStreamingDefaultsAndClampsOverrides()
        {
            VividRenderPipelineAsset pipelineAsset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();
            try
            {
                Assert.That(pipelineAsset.VirtualTextureIOBackend, Is.EqualTo(VividVirtualTextureIOBackendMode.Auto));
                Assert.That(pipelineAsset.VirtualTextureMaxInFlightChunks, Is.EqualTo(64));
                Assert.That(pipelineAsset.VirtualTextureDecodeConcurrency, Is.InRange(2, 8));
                Assert.That(pipelineAsset.VirtualTextureDecodedCacheBudgetMiB, Is.EqualTo(32));

                pipelineAsset.VirtualTextureMaxInFlightChunks = 0;
                pipelineAsset.VirtualTextureDecodeConcurrency = 100;
                pipelineAsset.VirtualTextureDecodedCacheBudgetMiB = -1;
                Assert.That(pipelineAsset.VirtualTextureMaxInFlightChunks, Is.EqualTo(1));
                Assert.That(pipelineAsset.VirtualTextureDecodeConcurrency, Is.EqualTo(64));
                Assert.That(pipelineAsset.VirtualTextureDecodedCacheBudgetMiB, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(pipelineAsset);
            }
        }

        [Test]
        public void DesktopBcnBuild_WritesMortonChunksAndGpuReadyLayerFormats()
        {
            Texture2D baseColor = CreateTexture(512, 256, normal: false);
            Texture2D normal = CreateTexture(512, 256, normal: true);
            Texture2D mask = CreateTexture(512, 256, normal: false);
            VividVirtualTextureAsset asset = ScriptableObject.CreateInstance<VividVirtualTextureAsset>();
            VividVirtualTextureBuiltData builtData = ScriptableObject.CreateInstance<VividVirtualTextureBuiltData>();
            string streamPath = Path.Combine(
                Path.GetTempPath(),
                $"VividVT_{Guid.NewGuid():N}.stream");

            try
            {
                VividVirtualTextureAssetBuilder.Generate(asset, builtData, new VividVirtualTextureAssetBuilder.Parameters
                {
                    SourceTexture = baseColor,
                    NormalTexture = normal,
                    MaskTexture = mask,
                    StreamDataPath = streamPath,
                    RuntimeStreamDataPath = "VividRP/VirtualTextures/Test.stream",
                    BuildProfile = VividVirtualTextureBuildProfile.GPUDrivenSurface,
                    StorageProfile = VividVirtualTextureStorageProfile.DesktopBCn,
                    StreamCompression = VividVirtualTextureStreamCompression.None,
                    BCQuality = VividVirtualTextureBCQuality.Normal,
                    ChunkTargetKiB = 128,
                    ZstdLevel = 3,
                });

                Assert.That(builtData.ContainerSchemaVersion, Is.EqualTo(VividVirtualTextureBuiltData.CurrentContainerSchemaVersion));
                Assert.That(builtData.StorageProfile, Is.EqualTo(VividVirtualTextureStorageProfile.DesktopBCn));
                Assert.That(builtData.VirtualPageCountX, Is.EqualTo(4));
                Assert.That(builtData.VirtualPageCountY, Is.EqualTo(2));
                Assert.That(builtData.MipCount, Is.EqualTo(2));
                Assert.That(builtData.Layers[0].Format, Is.EqualTo(GraphicsFormat.RGBA_BC7_SRGB));
                Assert.That(builtData.Layers[0].PhysicalGroup, Is.EqualTo(0));
                Assert.That(builtData.Layers[1].Format, Is.EqualTo(GraphicsFormat.RG_BC5_UNorm));
                Assert.That(builtData.Layers[1].Encoding, Is.EqualTo(VTLayerDataEncoding.NormalRG));
                Assert.That(builtData.Layers[1].PhysicalGroup, Is.EqualTo(1));
                Assert.That(builtData.Layers[2].Format, Is.EqualTo(GraphicsFormat.RGBA_BC7_UNorm));
                Assert.That(builtData.Layers[2].PhysicalGroup, Is.EqualTo(2));
                Assert.That(builtData.Layers[3].Format, Is.EqualTo(GraphicsFormat.R_BC4_UNorm));
                Assert.That(builtData.Layers[3].PhysicalGroup, Is.EqualTo(3));
                VirtualTextureSpaceDesc spaceDesc = builtData.CreateSpaceDesc(
                    "CompressionTest",
                    cachePageCount: 8,
                    maxUploadsPerFrame: 4,
                    feedbackCapacity: 32);
                var shaderParams = new VirtualTextureSpaceShaderParams(
                    spaceId: 7,
                    spaceDesc,
                    spaceDesc.PageTableEntryCount);
                Assert.That(shaderParams.ToIntArray(), Has.Length.EqualTo(33));
                Assert.That(shaderParams.LayerEncodingWord, Is.EqualTo(200));

                Assert.That(builtData.Tiles[0].X, Is.EqualTo(0));
                Assert.That(builtData.Tiles[0].Y, Is.EqualTo(0));
                Assert.That(builtData.Tiles[1].X, Is.EqualTo(1));
                Assert.That(builtData.Tiles[1].Y, Is.EqualTo(0));
                Assert.That(builtData.Tiles[2].X, Is.EqualTo(0));
                Assert.That(builtData.Tiles[2].Y, Is.EqualTo(1));
                Assert.That(builtData.Tiles[3].X, Is.EqualTo(1));
                Assert.That(builtData.Tiles[3].Y, Is.EqualTo(1));
                Assert.That(builtData.TryGetTileDescriptor(
                    new VirtualTexturePageCoord(2, 0, 0),
                    out VividVirtualTextureTileDescriptor mortonTile), Is.True);
                Assert.That(mortonTile.X, Is.EqualTo(2));

                int expectedLayerBytes = 34 * 34 * 16;
                int expectedTileBytes = expectedLayerBytes * 3 + 34 * 34 * 8;
                Assert.That(builtData.Tiles[0].ByteSize, Is.EqualTo(expectedTileBytes));
                foreach (VividVirtualTextureChunkDescriptor chunk in builtData.Chunks)
                {
                    Assert.That(chunk.FileOffset % 4096, Is.Zero);
                    Assert.That(chunk.DecodedPayloadCRC, Is.Not.Zero);
                    Assert.That(chunk.StoredByteSize, Is.EqualTo(chunk.DecodedByteSize));
                    Assert.That(chunk.Compression, Is.EqualTo(VividVirtualTextureStreamCompression.None));
                }

                byte[] header = File.ReadAllBytes(streamPath);
                Assert.That(System.Text.Encoding.ASCII.GetString(header, 0, 8), Is.EqualTo("VIVIDVT2"));
                Assert.That(BitConverter.ToInt32(header, 8), Is.EqualTo(VividVirtualTextureBuiltData.CurrentContainerSchemaVersion));
                Assert.That(BitConverter.ToUInt32(header, 12), Is.EqualTo(builtData.ContentVersion));
                Assert.That(BitConverter.ToInt32(header, 16), Is.EqualTo(builtData.ChunkCount));
            }
            finally
            {
                Object.DestroyImmediate(baseColor);
                Object.DestroyImmediate(normal);
                Object.DestroyImmediate(mask);
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(builtData);
                if (File.Exists(streamPath))
                    File.Delete(streamPath);
            }
        }

        [Test]
        public void DesktopBcnSingleChannelMask_UsesBc4Encoding()
        {
            Texture2D source = CreateTexture(128, 128, normal: false);
            Texture2D mask = CreateTexture(128, 128, normal: false);
            VividVirtualTextureAsset asset = ScriptableObject.CreateInstance<VividVirtualTextureAsset>();
            VividVirtualTextureBuiltData builtData = ScriptableObject.CreateInstance<VividVirtualTextureBuiltData>();
            string streamPath = Path.Combine(Path.GetTempPath(), $"VividVT_{Guid.NewGuid():N}.stream");
            try
            {
                VividVirtualTextureAssetBuilder.Generate(asset, builtData, new VividVirtualTextureAssetBuilder.Parameters
                {
                    SourceTexture = source,
                    MaskTexture = mask,
                    StreamDataPath = streamPath,
                    BuildProfile = VividVirtualTextureBuildProfile.GPUDrivenSurface,
                    StorageProfile = VividVirtualTextureStorageProfile.DesktopBCn,
                    StreamCompression = VividVirtualTextureStreamCompression.None,
                    MaskStorage = VividVirtualTextureMaskStorage.SingleChannelR,
                    BCQuality = VividVirtualTextureBCQuality.Normal,
                    ChunkTargetKiB = 256,
                });

                Assert.That(builtData.MaskStorage, Is.EqualTo(VividVirtualTextureMaskStorage.SingleChannelR));
                Assert.That(builtData.Layers[3].Format, Is.EqualTo(GraphicsFormat.R_BC4_UNorm));
                Assert.That(builtData.Layers[3].Encoding, Is.EqualTo(VTLayerDataEncoding.SingleChannelR));
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(mask);
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(builtData);
                if (File.Exists(streamPath))
                    File.Delete(streamPath);
            }
        }

        [Test]
        public void DesktopBcnNormal_ReordersLegacyAgIntoCanonicalRgBeforeEncoding()
        {
            Texture2D baseColor = CreateTexture(128, 128, normal: false);
            Texture2D normal = CreateTexture(128, 128, normal: true);
            VividVirtualTextureAsset asset = ScriptableObject.CreateInstance<VividVirtualTextureAsset>();
            VividVirtualTextureBuiltData builtData = ScriptableObject.CreateInstance<VividVirtualTextureBuiltData>();
            string streamPath = Path.Combine(Path.GetTempPath(), $"VividVT_{Guid.NewGuid():N}.stream");
            var encoder = new CapturingStorageEncoder();
            VividVirtualTextureAssetBuilder.SetGpuStorageEncoderForTesting(encoder);
            try
            {
                VividVirtualTextureAssetBuilder.Generate(asset, builtData, new VividVirtualTextureAssetBuilder.Parameters
                {
                    SourceTexture = baseColor,
                    NormalTexture = normal,
                    StreamDataPath = streamPath,
                    BuildProfile = VividVirtualTextureBuildProfile.GPUDrivenSurface,
                    StorageProfile = VividVirtualTextureStorageProfile.DesktopBCn,
                    StreamCompression = VividVirtualTextureStreamCompression.None,
                    ChunkTargetKiB = 256,
                });

                Assert.That(encoder.NormalPage, Is.Not.Null);
                Assert.That(encoder.NormalPage, Has.All.Matches<Color32>(pixel => pixel.g == 128));
                Assert.That(encoder.NormalPage, Has.All.Matches<Color32>(pixel => pixel.b == 0 && pixel.a == 255));
                var uniqueRedValues = new System.Collections.Generic.HashSet<byte>();
                for (int pixelIndex = 0; pixelIndex < encoder.NormalPage.Length; pixelIndex++)
                    uniqueRedValues.Add(encoder.NormalPage[pixelIndex].r);
                Assert.That(uniqueRedValues.Count, Is.GreaterThan(1));
            }
            finally
            {
                VividVirtualTextureAssetBuilder.ResetGpuStorageEncoderForTesting();
                Object.DestroyImmediate(baseColor);
                Object.DestroyImmediate(normal);
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(builtData);
                if (File.Exists(streamPath))
                    File.Delete(streamPath);
            }
        }

        private static VividVirtualTextureTilePayloadLocation CreateRawLocation(
            int chunkIndex,
            long fileOffset)
        {
            return new VividVirtualTextureTilePayloadLocation(
                chunkIndex,
                fileOffset,
                storedByteSize: 16,
                decodedByteSize: 16,
                tileByteOffset: 0,
                tileByteSize: 16,
                compression: VividVirtualTextureStreamCompression.None,
                decodedPayloadCRC: 0,
                flags: VividVirtualTextureChunkFlags.None);
        }

        private static Texture2D CreateTexture(int width, int height, bool normal)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true, true);
            var pixels = new Color32[width * height];
            for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
            {
                pixels[pixelIndex] = normal
                    ? new Color32(128, 128, 255, (byte)(64 + pixelIndex % 128))
                    : new Color32((byte)pixelIndex, (byte)(pixelIndex >> 3), (byte)(pixelIndex >> 7), 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply(true, false);
            return texture;
        }

        private static ulong ReadBits(byte[] source, ref int bitPosition, int bitCount)
        {
            ulong value = 0;
            for (int bitIndex = 0; bitIndex < bitCount; bitIndex++, bitPosition++)
            {
                if ((source[bitPosition >> 3] & (1 << (bitPosition & 7))) != 0)
                    value |= 1ul << bitIndex;
            }

            return value;
        }

        private static void WaitForLease(VTStreamChunkManager manager, VTChunkLease lease)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (!lease.State.Equals(VTStreamChunkState.Ready)
                   && !lease.State.Equals(VTStreamChunkState.Failed)
                   && DateTime.UtcNow < deadline)
            {
                manager.BeginFrame();
                manager.SubmitPendingReads();
                System.Threading.Thread.Sleep(5);
            }
        }
    }
}
