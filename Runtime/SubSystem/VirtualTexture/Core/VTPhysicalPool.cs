using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal readonly struct VTPhysicalPoolLayerDesc : IEquatable<VTPhysicalPoolLayerDesc>
    {
        internal VTPhysicalPoolLayerDesc(
            VTLayerSemantic semantic,
            int physicalGroup,
            GraphicsFormat graphicsFormat,
            bool sRGB)
        {
            Semantic = semantic;
            PhysicalGroup = Mathf.Max(0, physicalGroup);
            GraphicsFormat = graphicsFormat;
            StorageFormat = VTPhysicalPoolDesc.ResolveStorageFormat(graphicsFormat);
            SRGB = sRGB;
        }

        internal VTLayerSemantic Semantic { get; }

        internal int PhysicalGroup { get; }

        internal GraphicsFormat GraphicsFormat { get; }

        internal GraphicsFormat StorageFormat { get; }

        internal bool SRGB { get; }

        internal static VTPhysicalPoolLayerDesc FromLayer(in VTLayerDesc layer)
        {
            return new VTPhysicalPoolLayerDesc(
                layer.Semantic,
                layer.PhysicalGroup,
                layer.GraphicsFormat,
                layer.SRGB);
        }

        public bool Equals(VTPhysicalPoolLayerDesc other)
        {
            return Semantic == other.Semantic
                   && PhysicalGroup == other.PhysicalGroup
                   && GraphicsFormat == other.GraphicsFormat
                   && StorageFormat == other.StorageFormat
                   && SRGB == other.SRGB;
        }

        public override bool Equals(object obj)
        {
            return obj is VTPhysicalPoolLayerDesc other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Semantic, PhysicalGroup, GraphicsFormat, StorageFormat, SRGB);
        }
    }

    internal readonly struct VTPhysicalPoolDesc : IEquatable<VTPhysicalPoolDesc>
    {
        internal VTPhysicalPoolDesc(
            int pageSize,
            int borderSize,
            int pageCount,
            IReadOnlyList<VTLayerDesc> layers)
        {
            if (layers == null || layers.Count == 0)
                throw new ArgumentException("Physical pool must contain at least one layer.", nameof(layers));

            PageSize = pageSize;
            BorderSize = borderSize;
            PhysicalPageSize = pageSize + borderSize * 2;
            PageCount = pageCount;
            LayerCount = Mathf.Max(1, layers.Count);
            m_Layers = new VTPhysicalPoolLayerDesc[LayerCount];
            m_GroupLayerCounts = new int[VTStackDesc.MaxLayerCount];
            m_GroupStorageFormats = new GraphicsFormat[VTStackDesc.MaxLayerCount];
            m_LayerPhysicalLayerIndices = new int[LayerCount];
            int maxPhysicalGroup = 0;
            for (int layerIndex = 0; layerIndex < LayerCount; layerIndex++)
            {
                m_Layers[layerIndex] = VTPhysicalPoolLayerDesc.FromLayer(layers[layerIndex]);
                if (m_Layers[layerIndex].PhysicalGroup >= VTStackDesc.MaxLayerCount)
                    throw new ArgumentOutOfRangeException(
                        nameof(layers),
                        $"Physical group index must be smaller than {VTStackDesc.MaxLayerCount}.");

                maxPhysicalGroup = Mathf.Max(maxPhysicalGroup, m_Layers[layerIndex].PhysicalGroup);
                GraphicsFormat groupFormat = m_GroupStorageFormats[m_Layers[layerIndex].PhysicalGroup];
                if (groupFormat == GraphicsFormat.None)
                {
                    m_GroupStorageFormats[m_Layers[layerIndex].PhysicalGroup] = m_Layers[layerIndex].StorageFormat;
                }
                else if (groupFormat != m_Layers[layerIndex].StorageFormat)
                {
                    throw new ArgumentException(
                        $"Physical group {m_Layers[layerIndex].PhysicalGroup} mixes layer storage formats. " +
                        "Use a separate physical group for layers with different formats.",
                        nameof(layers));
                }

                m_LayerPhysicalLayerIndices[layerIndex] = m_GroupLayerCounts[m_Layers[layerIndex].PhysicalGroup];
                m_GroupLayerCounts[m_Layers[layerIndex].PhysicalGroup] += 1;
            }

            GraphicsFormat = m_Layers[0].StorageFormat;
            PhysicalGroupCount = maxPhysicalGroup + 1;
            for (int groupIndex = 0; groupIndex < PhysicalGroupCount; groupIndex++)
            {
                if (m_GroupLayerCounts[groupIndex] <= 0)
                {
                    throw new ArgumentException(
                        "Physical group indices must be compact and start at zero.",
                        nameof(layers));
                }
            }

            LayerGroup = BuildLayerGroupKey(m_Layers);
        }

        private readonly VTPhysicalPoolLayerDesc[] m_Layers;
        private readonly int[] m_GroupLayerCounts;
        private readonly GraphicsFormat[] m_GroupStorageFormats;
        private readonly int[] m_LayerPhysicalLayerIndices;

        internal int PageSize { get; }

        internal int BorderSize { get; }

        internal int PhysicalPageSize { get; }

        internal int PageCount { get; }

        internal int LayerCount { get; }

        internal GraphicsFormat GraphicsFormat { get; }

        internal int PhysicalGroupCount { get; }

        internal string LayerGroup { get; }

        internal IReadOnlyList<VTPhysicalPoolLayerDesc> Layers => m_Layers ?? Array.Empty<VTPhysicalPoolLayerDesc>();

        internal int GetGroupLayerCount(int physicalGroup)
        {
            return m_GroupLayerCounts != null && physicalGroup >= 0 && physicalGroup < m_GroupLayerCounts.Length
                ? m_GroupLayerCounts[physicalGroup]
                : 0;
        }

        internal GraphicsFormat GetGroupStorageFormat(int physicalGroup)
        {
            return m_GroupStorageFormats != null
                   && physicalGroup >= 0
                   && physicalGroup < m_GroupStorageFormats.Length
                ? m_GroupStorageFormats[physicalGroup]
                : GraphicsFormat.None;
        }

        internal int GetLayerPhysicalGroup(int layerIndex)
        {
            if (m_Layers == null || layerIndex < 0 || layerIndex >= m_Layers.Length)
                return 0;

            return m_Layers[layerIndex].PhysicalGroup;
        }

        internal int GetLayerPhysicalLayerIndex(int layerIndex)
        {
            if (m_LayerPhysicalLayerIndices == null
                || layerIndex < 0
                || layerIndex >= m_LayerPhysicalLayerIndices.Length)
            {
                return 0;
            }

            return m_LayerPhysicalLayerIndices[layerIndex];
        }

        internal static VTPhysicalPoolDesc FromSpaceDesc(in VirtualTextureSpaceDesc desc)
        {
            return new VTPhysicalPoolDesc(
                desc.PageSize,
                desc.BorderSize,
                desc.CachePageCount,
                desc.StackDesc.Layers);
        }

        internal static GraphicsFormat ResolveStorageFormat(GraphicsFormat graphicsFormat)
        {
            return GraphicsFormatUtility.IsSRGBFormat(graphicsFormat)
                ? GraphicsFormatUtility.GetLinearFormat(graphicsFormat)
                : graphicsFormat;
        }

        public bool Equals(VTPhysicalPoolDesc other)
        {
            return PageSize == other.PageSize
                   && BorderSize == other.BorderSize
                   && PageCount == other.PageCount
                   && LayerCount == other.LayerCount
                   && GraphicsFormat == other.GraphicsFormat
                   && PhysicalGroupCount == other.PhysicalGroupCount
                   && LayersEqual(other)
                   && string.Equals(LayerGroup, other.LayerGroup, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is VTPhysicalPoolDesc other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hashCode = new HashCode();
            hashCode.Add(PageSize);
            hashCode.Add(BorderSize);
            hashCode.Add(PageCount);
            hashCode.Add(LayerCount);
            hashCode.Add(GraphicsFormat);
            hashCode.Add(PhysicalGroupCount);
            hashCode.Add(StringComparer.Ordinal.GetHashCode(LayerGroup ?? string.Empty));
            for (int layerIndex = 0; layerIndex < LayerCount; layerIndex++)
                hashCode.Add(m_Layers[layerIndex]);

            return hashCode.ToHashCode();
        }

        private bool LayersEqual(in VTPhysicalPoolDesc other)
        {
            if (m_Layers == null || other.m_Layers == null)
                return m_Layers == other.m_Layers;

            if (m_Layers.Length != other.m_Layers.Length)
                return false;

            for (int layerIndex = 0; layerIndex < m_Layers.Length; layerIndex++)
            {
                if (!m_Layers[layerIndex].Equals(other.m_Layers[layerIndex]))
                    return false;
            }

            return true;
        }

        private static string BuildLayerGroupKey(IReadOnlyList<VTPhysicalPoolLayerDesc> layers)
        {
            if (layers == null || layers.Count == 0)
                return "Default";

            var keyBuilder = new System.Text.StringBuilder();
            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                if (layerIndex > 0)
                    keyBuilder.Append('|');

                VTPhysicalPoolLayerDesc layer = layers[layerIndex];
                keyBuilder.Append((int)layer.Semantic);
                keyBuilder.Append(':');
                keyBuilder.Append(layer.PhysicalGroup);
                keyBuilder.Append(':');
                keyBuilder.Append((int)layer.GraphicsFormat);
                keyBuilder.Append(':');
                keyBuilder.Append(layer.SRGB ? 1 : 0);
            }

            return keyBuilder.ToString();
        }
    }

    internal readonly struct VTPhysicalPoolStats
    {
        internal VTPhysicalPoolStats(
            int poolCount,
            int residentPageCount,
            int freePageCount,
            int lockedPageCount,
            int evictedPageCount,
            long allocatedByteCount = 0,
            long residentByteCount = 0)
        {
            PoolCount = poolCount;
            ResidentPageCount = residentPageCount;
            FreePageCount = freePageCount;
            LockedPageCount = lockedPageCount;
            EvictedPageCount = evictedPageCount;
            AllocatedByteCount = Math.Max(0L, allocatedByteCount);
            ResidentByteCount = Math.Max(0L, residentByteCount);
        }

        internal int PoolCount { get; }

        internal int ResidentPageCount { get; }

        internal int FreePageCount { get; }

        internal int LockedPageCount { get; }

        internal int EvictedPageCount { get; }

        internal long AllocatedByteCount { get; }

        internal long ResidentByteCount { get; }
    }

    internal readonly struct VTPhysicalAtlasLayout : IEquatable<VTPhysicalAtlasLayout>
    {
        internal VTPhysicalAtlasLayout(int physicalPageSize, int tileCount, int maxTextureSize)
        {
            if (physicalPageSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(physicalPageSize));
            if (tileCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(tileCount));
            if (maxTextureSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxTextureSize));

            int maxTileCountPerDimension = maxTextureSize / physicalPageSize;
            if (maxTileCountPerDimension <= 0)
            {
                throw new InvalidOperationException(
                    $"VT physical page size {physicalPageSize} exceeds the active device's "
                    + $"maximum 2D texture size of {maxTextureSize}.");
            }

            int tileCountX = Mathf.CeilToInt(Mathf.Sqrt(tileCount));
            int tileCountY = (tileCount + tileCountX - 1) / tileCountX;
            if (tileCountX > maxTileCountPerDimension || tileCountY > maxTileCountPerDimension)
            {
                long atlasCapacity = (long)maxTileCountPerDimension * maxTileCountPerDimension;
                throw new InvalidOperationException(
                    $"VT physical cache requires {tileCount} atlas tiles, but the active device can fit at most "
                    + $"{atlasCapacity} {physicalPageSize}x{physicalPageSize} tiles in a "
                    + $"{maxTextureSize}x{maxTextureSize} 2D texture.");
            }

            PhysicalPageSize = physicalPageSize;
            TileCount = tileCount;
            TileCountX = tileCountX;
            TileCountY = tileCountY;
            Width = tileCountX * physicalPageSize;
            Height = tileCountY * physicalPageSize;
        }

        internal int PhysicalPageSize { get; }

        internal int TileCount { get; }

        internal int TileCountX { get; }

        internal int TileCountY { get; }

        internal int Width { get; }

        internal int Height { get; }

        internal RectInt GetTileRect(int tileIndex)
        {
            if (tileIndex < 0 || tileIndex >= TileCount)
                throw new ArgumentOutOfRangeException(nameof(tileIndex));

            return new RectInt(
                tileIndex % TileCountX * PhysicalPageSize,
                tileIndex / TileCountX * PhysicalPageSize,
                PhysicalPageSize,
                PhysicalPageSize);
        }

        public bool Equals(VTPhysicalAtlasLayout other)
        {
            return PhysicalPageSize == other.PhysicalPageSize
                   && TileCount == other.TileCount
                   && TileCountX == other.TileCountX
                   && TileCountY == other.TileCountY;
        }

        public override bool Equals(object obj)
        {
            return obj is VTPhysicalAtlasLayout other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(PhysicalPageSize, TileCount, TileCountX, TileCountY);
        }
    }

    internal readonly struct VTPhysicalPageIdentity : IEquatable<VTPhysicalPageIdentity>
    {
        internal VTPhysicalPageIdentity(
            VTProducerHandle producerHandle,
            string producerName,
            in VirtualTexturePageCoord pageCoord)
        {
            ProducerHandle = producerHandle;
            ProducerName = producerName;
            PageCoord = pageCoord;
        }

        internal VTProducerHandle ProducerHandle { get; }

        internal string ProducerName { get; }

        internal VirtualTexturePageCoord PageCoord { get; }

        public bool Equals(VTPhysicalPageIdentity other)
        {
            bool eitherHandleIsValid = ProducerHandle.IsValid || other.ProducerHandle.IsValid;
            bool sameProducer = eitherHandleIsValid
                ? ProducerHandle.IsValid
                  && other.ProducerHandle.IsValid
                  && ProducerHandle.Equals(other.ProducerHandle)
                : !string.IsNullOrEmpty(ProducerName)
                  && string.Equals(ProducerName, other.ProducerName, StringComparison.Ordinal);
            return sameProducer && PageCoord.Equals(other.PageCoord);
        }

        public override bool Equals(object obj)
        {
            return obj is VTPhysicalPageIdentity other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ProducerHandle.IsValid
                ? HashCode.Combine(ProducerHandle, PageCoord)
                : HashCode.Combine(ProducerName ?? string.Empty, PageCoord);
        }
    }

    internal interface IVTPhysicalPoolOwner
    {
        int SpaceId { get; }

        bool OnPhysicalPageInvalidated(int pageIndex, int generation);
    }

#if VT_DEBUG
    internal static class VTDebugLog
    {
        internal static void Trace(string message)
        {
            Write(LogType.Log, message);
        }

        internal static void Warning(string message)
        {
            Write(LogType.Warning, message);
        }

        internal static void Error(string message)
        {
            Write(LogType.Error, message);
        }

        private static void Write(LogType logType, string message)
        {
            // Structured VT messages already carry the event, frame and page sequence.
            // Per-message Unity stacks are identical and dominate captures during churn.
            Debug.LogFormat(logType, LogOption.NoStacktrace, null, "{0}", message);
        }
    }

    internal enum VTPageRequestKind : byte
    {
        Unknown = 0,
        Bootstrap = 1,
        Locked = 2,
        Demand = 3,
        Refinement = 4,
        Neighbor = 5,
    }

    internal readonly struct VTPageRequestDebugInfo
    {
        internal VTPageRequestDebugInfo(
            VTPageRequestKind requestKind,
            in VirtualTexturePageCoord sourceCoord,
            in VirtualTexturePageCoord effectiveCoord,
            int mipGap,
            long weightedScore)
        {
            RequestKind = requestKind;
            SourceCoord = sourceCoord;
            EffectiveCoord = effectiveCoord;
            MipGap = mipGap;
            WeightedScore = weightedScore;
        }

        internal VTPageRequestKind RequestKind { get; }

        internal VirtualTexturePageCoord SourceCoord { get; }

        internal VirtualTexturePageCoord EffectiveCoord { get; }

        internal int MipGap { get; }

        internal long WeightedScore { get; }
    }

    internal readonly struct VTDebugTransitionAncestor : IEquatable<VTDebugTransitionAncestor>
    {
        internal VTDebugTransitionAncestor(
            int pageIndex,
            int mip,
            int physicalPageId,
            int generation)
        {
            PageIndex = pageIndex;
            Mip = mip;
            PhysicalPageId = physicalPageId;
            Generation = generation;
        }

        internal static VTDebugTransitionAncestor Invalid => new(-1, -1, -1, 0);

        internal int PageIndex { get; }

        internal int Mip { get; }

        internal int PhysicalPageId { get; }

        internal int Generation { get; }

        internal bool IsValid => PageIndex >= 0 && PhysicalPageId >= 0 && Generation > 0;

        public bool Equals(VTDebugTransitionAncestor other)
        {
            return PageIndex == other.PageIndex
                   && Mip == other.Mip
                   && PhysicalPageId == other.PhysicalPageId
                   && Generation == other.Generation;
        }

        public override bool Equals(object obj)
        {
            return obj is VTDebugTransitionAncestor other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(PageIndex, Mip, PhysicalPageId, Generation);
        }
    }

    internal sealed class VTDebugPageTimelineDiagnostics
    {
        internal const int PendingCommitTimeoutFrames = 8;
        internal const int TransitionTimeoutGraceFrames = 2;
        internal const int CancelRetryWindowFrames = 4;
        internal const int CommitBurstThreshold = 8;
        internal const int TransitionBurstThreshold = 16;
        internal const int VisibilityWaveThreshold = 16;
        internal const int ActivitySummaryFrameCount = 30;
        internal const int NeighborChurnReportBackoffMultiplier = 4;
        internal const int NeighborChurnMaxReportWindowCount = 64;

        private enum PageStage : byte
        {
            Reserved,
            Resident,
            Transitioning,
            Stable,
            Replacing,
        }

        private readonly struct PageKey : IEquatable<PageKey>
        {
            internal PageKey(int spaceId, int pageIndex)
            {
                SpaceId = spaceId;
                PageIndex = pageIndex;
            }

            internal int SpaceId { get; }

            internal int PageIndex { get; }

            public bool Equals(PageKey other)
            {
                return SpaceId == other.SpaceId && PageIndex == other.PageIndex;
            }

            public override bool Equals(object obj)
            {
                return obj is PageKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(SpaceId, PageIndex);
            }
        }

        private sealed class PageTimeline
        {
            internal PageKey Key;
            internal int Mip;
            internal int Slot;
            internal int Generation;
            internal int ReserveFrame;
            internal int CommitFrame = -1;
            internal int TransitionFrame = -1;
            internal int LastTransitionObservationFrame = -1;
            internal byte LastTransitionPhase;
            internal PageStage Stage;
            internal bool Pending;
            internal bool Locked;
            internal bool PendingTimeoutReported;
            internal bool TransitionTimeoutReported;
            internal VTDebugTransitionAncestor TransitionAncestor;
            internal int TransitionCohortFrame = -1;
            internal VTPageRequestDebugInfo RequestDebugInfo;
        }

        private readonly struct CancelledReserve
        {
            internal CancelledReserve(
                int frame,
                int slot,
                int generation,
                int ageFrames,
                VTPageRequestKind requestKind)
            {
                Frame = frame;
                Slot = slot;
                Generation = generation;
                AgeFrames = ageFrames;
                RequestKind = requestKind;
            }

            internal int Frame { get; }

            internal int Slot { get; }

            internal int Generation { get; }

            internal int AgeFrames { get; }

            internal VTPageRequestKind RequestKind { get; }
        }

        private sealed class FrameWave
        {
            internal int CommitCount;
            internal int DemandCommitCount;
            internal int RefinementCommitCount;
            internal int NeighborCommitCount;
            internal int TransitionCount;
            internal int MinMip = int.MaxValue;
            internal int MaxMip = int.MinValue;
            internal long MaxWeightedScore;
        }

        private sealed class ActivityWindow
        {
            internal int FirstFrame = int.MaxValue;
            internal int LastFrame = int.MinValue;
            internal int ReserveCount;
            internal int CommitCount;
            internal int StableCount;
            internal int CancelCount;
            internal int RetryLoopCount;
            internal int BootstrapReserveCount;
            internal int LockedReserveCount;
            internal int DemandReserveCount;
            internal int RefinementReserveCount;
            internal int NeighborReserveCount;
            internal int UnknownReserveCount;
            internal int MinMip = int.MaxValue;
            internal int MaxMip = int.MinValue;
            internal long MaxWeightedScore;
            internal string FirstSample;
            internal string LastSample;
            internal string FirstRetrySample;
            internal string LastRetrySample;
        }

        private sealed class RepeatedChurnState
        {
            internal ActivityWindow PendingWindow = new();
            internal int PendingWindowCount;
            internal int TotalWindowCount;
            internal int ReportWindowCount = 1;
        }

        private readonly string m_PoolName;
        private readonly Action<string> m_ErrorReporter;
        private readonly Action<string> m_TraceReporter;
        private readonly Action<string> m_WarningReporter;
        private readonly Dictionary<PageKey, PageTimeline> m_Pages = new();
        private readonly Dictionary<int, int> m_SlotGenerations = new();
        private readonly Dictionary<PageKey, CancelledReserve> m_CancelledReserves = new();
        private readonly Dictionary<int, FrameWave> m_FrameWaves = new();
        private readonly Dictionary<int, ActivityWindow> m_ActivityWindows = new();
        private readonly Dictionary<int, RepeatedChurnState> m_RepeatedChurnStates = new();
        private readonly List<PageKey> m_KeysToRemove = new();
        private readonly List<int> m_SpaceIdsToRemove = new();
        private int m_CurrentFrame = -1;

        internal VTDebugPageTimelineDiagnostics(
            string poolName,
            Action<string> errorReporter = null,
            Action<string> traceReporter = null,
            Action<string> warningReporter = null)
        {
            m_PoolName = string.IsNullOrWhiteSpace(poolName) ? "Shared" : poolName;
            m_ErrorReporter = errorReporter ?? VTDebugLog.Error;
            m_TraceReporter = traceReporter ?? VTDebugLog.Trace;
            m_WarningReporter = warningReporter ?? VTDebugLog.Warning;
        }

        internal int CurrentFrame => m_CurrentFrame;

        internal void AdvanceFrame(int frameIndex)
        {
            if (frameIndex < 0)
                return;

            if (m_CurrentFrame >= 0 && frameIndex < m_CurrentFrame)
            {
                Report(
                    "FrameRegression",
                    frameIndex,
                    null,
                    $"previousFrame={m_CurrentFrame}>currentFrame={frameIndex}");
                return;
            }

            if (frameIndex == m_CurrentFrame)
                return;

            if (m_CurrentFrame >= 0)
            {
                FinalizeFrameWaves(m_CurrentFrame);
                FinalizeActivityWindows(frameIndex, force: false);
            }
            m_CurrentFrame = frameIndex;
            m_FrameWaves.Clear();
            CheckTimeouts(frameIndex);
            PruneCancelledReserves(frameIndex);
        }

        internal void OnReserve(
            int slot,
            int spaceId,
            int pageIndex,
            int mip,
            int generation,
            int frameIndex,
            bool pending,
            bool locked,
            in VTPageRequestDebugInfo requestDebugInfo)
        {
            ObserveFrame(frameIndex);
            var key = new PageKey(spaceId, pageIndex);
            if (m_SlotGenerations.TryGetValue(slot, out int activeGeneration)
                && activeGeneration != generation)
            {
                Report(
                    "SlotGenerationOverlap",
                    frameIndex,
                    null,
                    $"slot={slot} activeGeneration={activeGeneration} newGeneration={generation} "
                    + $"sequence=reserve(new) before release(old)");
            }

            if (m_Pages.TryGetValue(key, out PageTimeline existing)
                && (existing.Slot != slot || existing.Generation != generation))
            {
                Report(
                    "PageReservationOverlap",
                    frameIndex,
                    existing,
                    $"newSlot={slot} newGeneration={generation} sequence={FormatSequence(existing)}>reserve(new)");
            }

            if (m_CancelledReserves.TryGetValue(key, out CancelledReserve cancelled))
            {
                int retryAge = frameIndex - cancelled.Frame;
                if (retryAge >= 0 && retryAge <= CancelRetryWindowFrames)
                {
                    RecordCancelRetry(
                        spaceId,
                        pageIndex,
                        mip,
                        slot,
                        generation,
                        frameIndex,
                        cancelled,
                        retryAge,
                        requestDebugInfo.RequestKind);
                }

                m_CancelledReserves.Remove(key);
            }

            m_SlotGenerations[slot] = generation;
            var timeline = new PageTimeline
            {
                Key = key,
                Mip = mip,
                Slot = slot,
                Generation = generation,
                ReserveFrame = frameIndex,
                Stage = pending ? PageStage.Reserved : PageStage.Resident,
                Pending = pending,
                Locked = locked,
                RequestDebugInfo = requestDebugInfo,
            };
            m_Pages[key] = timeline;
            RecordActivityReserve(frameIndex, timeline);
        }

        internal void OnResidentAttach(
            int slot,
            int spaceId,
            int pageIndex,
            int mip,
            int generation,
            int frameIndex,
            bool locked)
        {
            ObserveFrame(frameIndex);
            var key = new PageKey(spaceId, pageIndex);
            m_SlotGenerations[slot] = generation;
            m_Pages[key] = new PageTimeline
            {
                Key = key,
                Mip = mip,
                Slot = slot,
                Generation = generation,
                ReserveFrame = frameIndex,
                CommitFrame = frameIndex,
                Stage = PageStage.Resident,
                Pending = false,
                Locked = locked,
            };
        }

        internal void OnResidentCommit(
            int slot,
            int spaceId,
            int pageIndex,
            int mip,
            int generation,
            int frameIndex,
            bool wasPending,
            bool wasResident,
            bool locked,
            in VTPageRequestDebugInfo requestDebugInfo)
        {
            ObserveFrame(frameIndex);
            var key = new PageKey(spaceId, pageIndex);
            if (!m_Pages.TryGetValue(key, out PageTimeline timeline))
            {
                Report(
                    "CommitWithoutReserve",
                    frameIndex,
                    null,
                    $"space={spaceId} pageIndex={pageIndex} mip={mip} slot={slot} "
                    + $"generation={generation} sequence=commit");
                timeline = new PageTimeline
                {
                    Key = key,
                    Mip = mip,
                    Slot = slot,
                    Generation = generation,
                    ReserveFrame = frameIndex,
                    RequestDebugInfo = requestDebugInfo,
                };
                m_Pages[key] = timeline;
            }
            else if (timeline.Slot != slot || timeline.Generation != generation)
            {
                Report(
                    "CommitGenerationMismatch",
                    frameIndex,
                    timeline,
                    $"commitSlot={slot} commitGeneration={generation} sequence={FormatSequence(timeline)}>commit(mismatch)");
            }
            else if (timeline.CommitFrame >= 0 || wasResident)
            {
                Report(
                    "DuplicateCommit",
                    frameIndex,
                    timeline,
                    $"wasPending={wasPending} wasResident={wasResident} sequence={FormatSequence(timeline)}>commit");
            }

            timeline.Mip = mip;
            timeline.Slot = slot;
            timeline.Generation = generation;
            timeline.CommitFrame = frameIndex;
            timeline.Stage = PageStage.Resident;
            timeline.Pending = false;
            timeline.Locked = locked;
            timeline.RequestDebugInfo = requestDebugInfo;
            RecordCommitWave(frameIndex, timeline);
            RecordActivityCommit(frameIndex, timeline);
            if (locked)
            {
                timeline.Stage = PageStage.Stable;
                RecordActivityStable(frameIndex, timeline);
                ReportLifecycle(
                    "resident",
                    frameIndex,
                    timeline,
                    $"commitPath={(wasPending ? "async" : "immediate")} locked=True");
            }
        }

        internal void OnLockChanged(
            int slot,
            int spaceId,
            int pageIndex,
            int generation,
            bool locked)
        {
            var key = new PageKey(spaceId, pageIndex);
            if (!m_Pages.TryGetValue(key, out PageTimeline timeline)
                || timeline.Slot != slot
                || timeline.Generation != generation)
            {
                return;
            }

            timeline.Locked = locked;
            if (!locked || timeline.Stage != PageStage.Transitioning)
                return;

            byte previousPhase = timeline.LastTransitionPhase;
            timeline.LastTransitionPhase = VirtualTexturePageTableEntry.MaxTransitionPhase;
            timeline.Stage = PageStage.Stable;
            m_TraceReporter(
                $"[VividRP][VT_DEBUG][PageTransitionForcedStable] pool={m_PoolName} "
                + $"frame={m_CurrentFrame} slot={slot} space={spaceId} pageIndex={pageIndex} "
                + $"mip={timeline.Mip} generation={generation} phase={previousPhase}->"
                + $"{VirtualTexturePageTableEntry.MaxTransitionPhase} reason=locked "
                + $"sequence={FormatSequence(timeline)}>forcedStable@{m_CurrentFrame}");
        }

        internal void OnTransitionBegin(
            int spaceId,
            int pageIndex,
            int mip,
            int slot,
            int generation,
            int frameIndex,
            VTDebugTransitionAncestor ancestor = default)
        {
            ObserveFrame(frameIndex);
            var key = new PageKey(spaceId, pageIndex);
            if (!m_Pages.TryGetValue(key, out PageTimeline timeline))
            {
                Report(
                    "TransitionBeforeCommit",
                    frameIndex,
                    null,
                    $"space={spaceId} pageIndex={pageIndex} mip={mip} slot={slot} "
                    + $"generation={generation} sequence=transitionBegin");
                return;
            }

            if (timeline.Slot != slot || timeline.Generation != generation)
            {
                Report(
                    "TransitionGenerationMismatch",
                    frameIndex,
                    timeline,
                    $"transitionSlot={slot} transitionGeneration={generation} "
                    + $"sequence={FormatSequence(timeline)}>transitionBegin(mismatch)");
                return;
            }

            if (timeline.Pending || timeline.CommitFrame < 0)
            {
                Report(
                    "TransitionBeforeCommit",
                    frameIndex,
                    timeline,
                    $"sequence={FormatSequence(timeline)}>transitionBegin");
            }
            else if (timeline.Stage == PageStage.Transitioning)
            {
                Report(
                    "DuplicateTransitionBegin",
                    frameIndex,
                    timeline,
                    $"sequence={FormatSequence(timeline)}>transitionBegin");
            }

            timeline.TransitionFrame = frameIndex;
            timeline.TransitionCohortFrame = frameIndex;
            timeline.LastTransitionObservationFrame = frameIndex;
            timeline.TransitionAncestor = ancestor.IsValid
                ? ancestor
                : VTDebugTransitionAncestor.Invalid;
            timeline.LastTransitionPhase = 0;
            timeline.Stage = PageStage.Transitioning;
        }

        internal void OnTransitionAncestorObserved(
            int spaceId,
            int pageIndex,
            int mip,
            int slot,
            int generation,
            int frameIndex,
            byte phase,
            in VTDebugTransitionAncestor ancestor)
        {
            ObserveFrame(frameIndex);
            var key = new PageKey(spaceId, pageIndex);
            if (!m_Pages.TryGetValue(key, out PageTimeline timeline)
                || timeline.TransitionFrame < 0)
            {
                Report(
                    "TransitionAncestorWithoutBegin",
                    frameIndex,
                    timeline,
                    $"child=(space:{spaceId},page:{pageIndex},mip:{mip},slot:{slot},generation:{generation}) "
                    + $"ancestor={FormatAncestor(in ancestor)} phase={phase} cohortFrame=unknown");
                return;
            }

            if (timeline.Slot != slot || timeline.Generation != generation)
            {
                Report(
                    "TransitionAncestorGenerationMismatch",
                    frameIndex,
                    timeline,
                    $"childSlot={slot} childGeneration={generation} ancestor={FormatAncestor(in ancestor)} "
                    + $"phase={phase} cohortFrame={timeline.TransitionCohortFrame}");
                return;
            }

            timeline.LastTransitionObservationFrame = frameIndex;
            if (timeline.TransitionAncestor.Equals(ancestor))
                return;

            VTDebugTransitionAncestor oldAncestor = timeline.TransitionAncestor;
            Report(
                "TransitionAncestorChanged",
                frameIndex,
                timeline,
                $"child=(space:{spaceId},page:{pageIndex},mip:{mip},slot:{slot},generation:{generation}) "
                + $"oldAncestor={FormatAncestor(in oldAncestor)} newAncestor={FormatAncestor(in ancestor)} "
                + $"phase={phase} cohortFrame={timeline.TransitionCohortFrame} "
                + $"sequence={FormatSequence(timeline)}>ancestorChanged");
            timeline.TransitionAncestor = ancestor;
        }

        internal void OnTransitionPhase(
            int spaceId,
            int pageIndex,
            int mip,
            int slot,
            int generation,
            int frameIndex,
            byte previousPhase,
            byte nextPhase)
        {
            ObserveFrame(frameIndex);
            var key = new PageKey(spaceId, pageIndex);
            if (!m_Pages.TryGetValue(key, out PageTimeline timeline)
                || timeline.TransitionFrame < 0)
            {
                Report(
                    "TransitionPhaseWithoutBegin",
                    frameIndex,
                    timeline,
                    $"space={spaceId} pageIndex={pageIndex} mip={mip} slot={slot} "
                    + $"generation={generation} phase={previousPhase}->{nextPhase} sequence=transitionPhase");
                return;
            }

            if (timeline.Slot != slot || timeline.Generation != generation)
            {
                Report(
                    "TransitionPhaseGenerationMismatch",
                    frameIndex,
                    timeline,
                    $"phaseSlot={slot} phaseGeneration={generation} phase={previousPhase}->{nextPhase} "
                    + $"sequence={FormatSequence(timeline)}>phase(mismatch)");
                return;
            }

            if (timeline.LastTransitionPhase != previousPhase)
            {
                Report(
                    "TransitionPhaseDiscontinuity",
                    frameIndex,
                    timeline,
                    $"trackedPhase={timeline.LastTransitionPhase} loggedPhase={previousPhase}->{nextPhase} "
                    + $"sequence={FormatSequence(timeline)}>phase(discontinuous)");
            }

            bool atomicReveal = previousPhase == 0
                                && nextPhase == VirtualTexturePageTableEntry.MaxTransitionPhase;
            if (nextPhase <= previousPhase
                || (!atomicReveal && nextPhase > previousPhase + 1))
            {
                Report(
                    "TransitionPhaseJump",
                    frameIndex,
                    timeline,
                    $"phase={previousPhase}->{nextPhase} ageFrames={frameIndex - timeline.TransitionFrame} "
                    + $"sequence={FormatSequence(timeline)}>phase(jump)");
            }

            timeline.LastTransitionPhase = nextPhase;
            timeline.LastTransitionObservationFrame = frameIndex;
            timeline.Stage = nextPhase >= VirtualTexturePageTableEntry.MaxTransitionPhase
                ? PageStage.Stable
                : PageStage.Transitioning;
            RecordTransitionWave(frameIndex, timeline);
            if (timeline.Stage == PageStage.Stable)
            {
                RecordActivityStable(frameIndex, timeline);
                ReportLifecycle(
                    "stable",
                    frameIndex,
                    timeline,
                    $"phase={previousPhase}->{nextPhase} "
                    + $"ancestor={FormatAncestor(in timeline.TransitionAncestor)} "
                    + $"revealAgeFrames={Math.Max(0, frameIndex - timeline.TransitionFrame)}");
            }
        }

        internal void OnReplacementBegin(
            int slot,
            int spaceId,
            int pageIndex,
            int generation,
            int frameIndex)
        {
            ObserveFrame(frameIndex);
            var key = new PageKey(spaceId, pageIndex);
            if (!m_Pages.TryGetValue(key, out PageTimeline timeline))
                return;

            if (timeline.Pending)
            {
                Report(
                    "ReplacePendingPage",
                    frameIndex,
                    timeline,
                    $"sequence={FormatSequence(timeline)}>replaceBegin");
            }
            else if (timeline.Stage == PageStage.Transitioning)
            {
                Report(
                    "ReplaceTransitioningPage",
                    frameIndex,
                    timeline,
                    $"phase={timeline.LastTransitionPhase} sequence={FormatSequence(timeline)}>replaceBegin");
            }

            timeline.Stage = PageStage.Replacing;
        }

        internal void OnReplacementInvalidation(
            int slot,
            int spaceId,
            int pageIndex,
            int generation,
            int frameIndex,
            bool accepted)
        {
            ObserveFrame(frameIndex);
            var key = new PageKey(spaceId, pageIndex);
            m_Pages.TryGetValue(key, out PageTimeline timeline);
            if (accepted)
            {
                if (timeline?.Stage == PageStage.Transitioning)
                {
                    Report(
                        "ReplaceTransitioningBinding",
                        frameIndex,
                        timeline,
                        $"phase={timeline.LastTransitionPhase} "
                        + $"sequence={FormatSequence(timeline)}>replaceInvalidate");
                }

                return;
            }

            Report(
                "ReplacementInvalidationRejected",
                frameIndex,
                timeline,
                $"space={spaceId} pageIndex={pageIndex} slot={slot} generation={generation} "
                + $"sequence={FormatSequence(timeline)}>replaceInvalidate(rejected)");
        }

        internal void OnReplacementCommit(
            int slot,
            int oldGeneration,
            int spaceId,
            int pageIndex,
            int mip,
            int newGeneration,
            int frameIndex,
            bool pending,
            bool locked,
            in VTPageRequestDebugInfo requestDebugInfo)
        {
            ObserveFrame(frameIndex);
            if (m_SlotGenerations.TryGetValue(slot, out int activeGeneration)
                && activeGeneration != oldGeneration)
            {
                Report(
                    "ReplacementCommitWithoutMatchingBegin",
                    frameIndex,
                    null,
                    $"slot={slot} expectedOldGeneration={oldGeneration} activeGeneration={activeGeneration} "
                    + $"newGeneration={newGeneration} sequence=replaceCommit");
            }

            OnReserve(
                slot,
                spaceId,
                pageIndex,
                mip,
                newGeneration,
                frameIndex,
                pending,
                locked,
                requestDebugInfo);
        }

        internal void OnSlotReleased(
            int slot,
            int generation,
            int frameIndex,
            bool releaseToFreeList)
        {
            ObserveFrame(frameIndex);
            m_KeysToRemove.Clear();
            foreach (KeyValuePair<PageKey, PageTimeline> pair in m_Pages)
            {
                PageTimeline timeline = pair.Value;
                if (timeline.Slot != slot || timeline.Generation != generation)
                    continue;

                if (timeline.Pending && releaseToFreeList)
                {
                    int ageFrames = Math.Max(0, frameIndex - timeline.ReserveFrame);
                    RecordActivityCancel(frameIndex, timeline, ageFrames);
                    m_CancelledReserves[timeline.Key] = new CancelledReserve(
                        frameIndex,
                        slot,
                        generation,
                        ageFrames,
                        timeline.RequestDebugInfo.RequestKind);

                    if (timeline.RequestDebugInfo.RequestKind == VTPageRequestKind.Bootstrap
                        || timeline.RequestDebugInfo.RequestKind == VTPageRequestKind.Demand
                        || timeline.RequestDebugInfo.RequestKind == VTPageRequestKind.Refinement
                        || timeline.RequestDebugInfo.RequestKind == VTPageRequestKind.Locked)
                    {
                        string code = timeline.RequestDebugInfo.RequestKind == VTPageRequestKind.Bootstrap
                            ? "PendingBootstrapCancelled"
                            : "PendingDemandCancelled";
                        Report(
                            code,
                            frameIndex,
                            timeline,
                            $"ageFrames={ageFrames} sequence={FormatSequence(timeline)}>cancel");
                    }
                }

                m_KeysToRemove.Add(pair.Key);
            }

            for (int keyIndex = 0; keyIndex < m_KeysToRemove.Count; keyIndex++)
                m_Pages.Remove(m_KeysToRemove[keyIndex]);

            if (m_SlotGenerations.TryGetValue(slot, out int activeGeneration)
                && activeGeneration == generation)
            {
                m_SlotGenerations.Remove(slot);
            }
        }

        internal void OnSharedBindingReleased(
            int slot,
            int spaceId,
            int pageIndex,
            int generation)
        {
            var key = new PageKey(spaceId, pageIndex);
            if (!m_Pages.TryGetValue(key, out PageTimeline timeline)
                || timeline.Slot != slot
                || timeline.Generation != generation)
            {
                return;
            }

            m_Pages.Remove(key);
        }

        internal void Reset()
        {
            if (m_CurrentFrame >= 0)
                FinalizeActivityWindows(m_CurrentFrame, force: true);
            m_Pages.Clear();
            m_SlotGenerations.Clear();
            m_CancelledReserves.Clear();
            m_FrameWaves.Clear();
            m_ActivityWindows.Clear();
            m_RepeatedChurnStates.Clear();
            m_KeysToRemove.Clear();
            m_SpaceIdsToRemove.Clear();
            m_CurrentFrame = -1;
        }

        private void ObserveFrame(int frameIndex)
        {
            if (frameIndex >= 0 && frameIndex != m_CurrentFrame)
                AdvanceFrame(frameIndex);
        }

        private void CheckTimeouts(int frameIndex)
        {
            foreach (PageTimeline timeline in m_Pages.Values)
            {
                if (timeline.Pending
                    && !timeline.PendingTimeoutReported
                    && frameIndex - timeline.ReserveFrame > PendingCommitTimeoutFrames)
                {
                    timeline.PendingTimeoutReported = true;
                    Report(
                        "CommitTimeout",
                        frameIndex,
                        timeline,
                        $"ageFrames={frameIndex - timeline.ReserveFrame} "
                        + $"timeoutFrames={PendingCommitTimeoutFrames} sequence={FormatSequence(timeline)}>timeout");
                }

                if (timeline.Stage == PageStage.Transitioning
                    && !timeline.TransitionTimeoutReported
                    && frameIndex - timeline.LastTransitionObservationFrame
                        > VTResidencyManager.PageTransitionFrameCount + TransitionTimeoutGraceFrames)
                {
                    timeline.TransitionTimeoutReported = true;
                    Report(
                        "TransitionTimeout",
                        frameIndex,
                        timeline,
                        $"phase={timeline.LastTransitionPhase} ageFrames={frameIndex - timeline.TransitionFrame} "
                        + $"unobservedFrames={frameIndex - timeline.LastTransitionObservationFrame} "
                        + $"sequence={FormatSequence(timeline)}>timeout");
                }
            }
        }

        private void PruneCancelledReserves(int frameIndex)
        {
            m_KeysToRemove.Clear();
            foreach (KeyValuePair<PageKey, CancelledReserve> pair in m_CancelledReserves)
            {
                if (frameIndex - pair.Value.Frame > CancelRetryWindowFrames)
                    m_KeysToRemove.Add(pair.Key);
            }

            for (int keyIndex = 0; keyIndex < m_KeysToRemove.Count; keyIndex++)
                m_CancelledReserves.Remove(m_KeysToRemove[keyIndex]);
        }

        private void RecordActivityReserve(int frameIndex, PageTimeline timeline)
        {
            ActivityWindow window = GetActivityWindow(timeline.Key.SpaceId);
            window.ReserveCount += 1;
            switch (timeline.RequestDebugInfo.RequestKind)
            {
                case VTPageRequestKind.Bootstrap:
                    window.BootstrapReserveCount += 1;
                    break;
                case VTPageRequestKind.Locked:
                    window.LockedReserveCount += 1;
                    break;
                case VTPageRequestKind.Demand:
                    window.DemandReserveCount += 1;
                    break;
                case VTPageRequestKind.Refinement:
                    window.RefinementReserveCount += 1;
                    break;
                case VTPageRequestKind.Neighbor:
                    window.NeighborReserveCount += 1;
                    break;
                default:
                    window.UnknownReserveCount += 1;
                    break;
            }

            AccumulateActivity(
                window,
                frameIndex,
                timeline.Mip,
                timeline.RequestDebugInfo.WeightedScore,
                FormatActivitySample(timeline, $"reserve@{frameIndex}"));
        }

        private void RecordActivityCommit(int frameIndex, PageTimeline timeline)
        {
            ActivityWindow window = GetActivityWindow(timeline.Key.SpaceId);
            window.CommitCount += 1;
            AccumulateActivity(
                window,
                frameIndex,
                timeline.Mip,
                timeline.RequestDebugInfo.WeightedScore,
                FormatActivitySample(timeline, FormatSequence(timeline)));
        }

        private void RecordActivityStable(int frameIndex, PageTimeline timeline)
        {
            ActivityWindow window = GetActivityWindow(timeline.Key.SpaceId);
            window.StableCount += 1;
            AccumulateActivity(
                window,
                frameIndex,
                timeline.Mip,
                timeline.RequestDebugInfo.WeightedScore,
                FormatActivitySample(
                    timeline,
                    $"{FormatSequence(timeline)}>stable@{frameIndex}"));
        }

        private void RecordActivityCancel(
            int frameIndex,
            PageTimeline timeline,
            int ageFrames)
        {
            ActivityWindow window = GetActivityWindow(timeline.Key.SpaceId);
            window.CancelCount += 1;
            AccumulateActivity(
                window,
                frameIndex,
                timeline.Mip,
                timeline.RequestDebugInfo.WeightedScore,
                FormatActivitySample(
                    timeline,
                    $"reserve@{timeline.ReserveFrame}>cancel@{frameIndex}(age:{ageFrames})"));
        }

        private void RecordCancelRetry(
            int spaceId,
            int pageIndex,
            int mip,
            int slot,
            int generation,
            int frameIndex,
            in CancelledReserve cancelled,
            int retryAge,
            VTPageRequestKind newRequestKind)
        {
            ActivityWindow window = GetActivityWindow(spaceId);
            window.RetryLoopCount += 1;
            int reserveFrame = cancelled.Frame - cancelled.AgeFrames;
            string sample =
                $"(page:{pageIndex},mip:{mip},oldSlot:{cancelled.Slot},newSlot:{slot},"
                + $"oldGeneration:{cancelled.Generation},newGeneration:{generation},"
                + $"oldRequest:{FormatRequestKind(cancelled.RequestKind)},"
                + $"newRequest:{FormatRequestKind(newRequestKind)},retryAge:{retryAge},"
                + $"sequence:reserve@{reserveFrame}>cancel@{cancelled.Frame}>reserve@{frameIndex})";
            window.FirstRetrySample ??= sample;
            window.LastRetrySample = sample;
            AccumulateActivity(window, frameIndex, mip, 0, sample);
        }

        private ActivityWindow GetActivityWindow(int spaceId)
        {
            if (!m_ActivityWindows.TryGetValue(spaceId, out ActivityWindow window))
            {
                window = new ActivityWindow();
                m_ActivityWindows.Add(spaceId, window);
            }

            return window;
        }

        private static void AccumulateActivity(
            ActivityWindow window,
            int frameIndex,
            int mip,
            long weightedScore,
            string sample)
        {
            window.FirstFrame = Math.Min(window.FirstFrame, frameIndex);
            window.LastFrame = Math.Max(window.LastFrame, frameIndex);
            window.MinMip = Math.Min(window.MinMip, mip);
            window.MaxMip = Math.Max(window.MaxMip, mip);
            window.MaxWeightedScore = Math.Max(window.MaxWeightedScore, weightedScore);
            window.FirstSample ??= sample;
            window.LastSample = sample;
        }

        private void FinalizeActivityWindows(int frameIndex, bool force)
        {
            m_SpaceIdsToRemove.Clear();
            foreach (KeyValuePair<int, ActivityWindow> pair in m_ActivityWindows)
            {
                ActivityWindow window = pair.Value;
                if (!force
                    && (window.FirstFrame == int.MaxValue
                        || frameIndex - window.FirstFrame < ActivitySummaryFrameCount))
                {
                    continue;
                }

                HandleCompletedActivityWindow(pair.Key, window, force);
                m_SpaceIdsToRemove.Add(pair.Key);
            }

            for (int index = 0; index < m_SpaceIdsToRemove.Count; index++)
                m_ActivityWindows.Remove(m_SpaceIdsToRemove[index]);

            if (force)
                FlushRepeatedChurnStates();
        }

        private void HandleCompletedActivityWindow(
            int spaceId,
            ActivityWindow window,
            bool force)
        {
            if (!IsPureNeighborChurn(window))
            {
                FlushRepeatedChurnState(spaceId, "neighbor-churn-exit", removeState: true);
                ReportActivityWindow(
                    spaceId,
                    window,
                    "activity",
                    windowCount: 1,
                    totalPatternWindowCount: 1,
                    emitRetryWarning: true);
                return;
            }

            if (!m_RepeatedChurnStates.TryGetValue(spaceId, out RepeatedChurnState state))
            {
                state = new RepeatedChurnState();
                m_RepeatedChurnStates.Add(spaceId, state);
            }

            MergeActivityWindow(state.PendingWindow, window);
            state.PendingWindowCount += 1;
            state.TotalWindowCount += 1;
            if (!force && state.PendingWindowCount < state.ReportWindowCount)
                return;

            ReportRepeatedChurn(spaceId, state, force ? "neighbor-churn-final" : "steady-neighbor-churn");
            state.ReportWindowCount = Math.Min(
                state.ReportWindowCount * NeighborChurnReportBackoffMultiplier,
                NeighborChurnMaxReportWindowCount);
        }

        private void FlushRepeatedChurnState(
            int spaceId,
            string mode,
            bool removeState)
        {
            if (!m_RepeatedChurnStates.TryGetValue(spaceId, out RepeatedChurnState state))
                return;

            if (state.PendingWindowCount > 0)
                ReportRepeatedChurn(spaceId, state, mode);
            if (removeState)
                m_RepeatedChurnStates.Remove(spaceId);
        }

        private void FlushRepeatedChurnStates()
        {
            m_SpaceIdsToRemove.Clear();
            foreach (KeyValuePair<int, RepeatedChurnState> pair in m_RepeatedChurnStates)
            {
                if (pair.Value.PendingWindowCount > 0)
                    ReportRepeatedChurn(pair.Key, pair.Value, "neighbor-churn-final");
                m_SpaceIdsToRemove.Add(pair.Key);
            }

            for (int index = 0; index < m_SpaceIdsToRemove.Count; index++)
                m_RepeatedChurnStates.Remove(m_SpaceIdsToRemove[index]);
        }

        private void ReportRepeatedChurn(
            int spaceId,
            RepeatedChurnState state,
            string mode)
        {
            ReportActivityWindow(
                spaceId,
                state.PendingWindow,
                mode,
                state.PendingWindowCount,
                state.TotalWindowCount,
                emitRetryWarning: false);
            state.PendingWindow = new ActivityWindow();
            state.PendingWindowCount = 0;
        }

        private static bool IsPureNeighborChurn(ActivityWindow window)
        {
            return window.ReserveCount > 0
                   && window.CommitCount == 0
                   && window.StableCount == 0
                   && window.CancelCount == window.ReserveCount
                   && window.NeighborReserveCount == window.ReserveCount
                   && window.BootstrapReserveCount == 0
                   && window.LockedReserveCount == 0
                   && window.DemandReserveCount == 0
                   && window.RefinementReserveCount == 0
                   && window.UnknownReserveCount == 0;
        }

        private static void MergeActivityWindow(ActivityWindow target, ActivityWindow source)
        {
            target.FirstFrame = Math.Min(target.FirstFrame, source.FirstFrame);
            target.LastFrame = Math.Max(target.LastFrame, source.LastFrame);
            target.ReserveCount += source.ReserveCount;
            target.CommitCount += source.CommitCount;
            target.StableCount += source.StableCount;
            target.CancelCount += source.CancelCount;
            target.RetryLoopCount += source.RetryLoopCount;
            target.BootstrapReserveCount += source.BootstrapReserveCount;
            target.LockedReserveCount += source.LockedReserveCount;
            target.DemandReserveCount += source.DemandReserveCount;
            target.RefinementReserveCount += source.RefinementReserveCount;
            target.NeighborReserveCount += source.NeighborReserveCount;
            target.UnknownReserveCount += source.UnknownReserveCount;
            target.MinMip = Math.Min(target.MinMip, source.MinMip);
            target.MaxMip = Math.Max(target.MaxMip, source.MaxMip);
            target.MaxWeightedScore = Math.Max(target.MaxWeightedScore, source.MaxWeightedScore);
            target.FirstSample ??= source.FirstSample;
            target.LastSample = source.LastSample ?? target.LastSample;
            target.FirstRetrySample ??= source.FirstRetrySample;
            target.LastRetrySample = source.LastRetrySample ?? target.LastRetrySample;
        }

        private void ReportActivityWindow(
            int spaceId,
            ActivityWindow window,
            string mode,
            int windowCount,
            int totalPatternWindowCount,
            bool emitRetryWarning)
        {
            string mipRange = window.MinMip == int.MaxValue
                ? "unknown"
                : $"{window.MinMip}-{window.MaxMip}";
            string details =
                $"pool={m_PoolName} mode={mode} frameRange={window.FirstFrame}-{window.LastFrame} "
                + $"space={spaceId} windows={windowCount} suppressedWindows={Math.Max(0, windowCount - 1)} "
                + $"totalPatternWindows={totalPatternWindowCount} "
                + $"reserves={window.ReserveCount} commits={window.CommitCount} "
                + $"stable={window.StableCount} cancels={window.CancelCount} "
                + $"retryLoops={window.RetryLoopCount} "
                + $"reserveKinds=(bootstrap:{window.BootstrapReserveCount},locked:{window.LockedReserveCount},"
                + $"demand:{window.DemandReserveCount},"
                + $"refinement:{window.RefinementReserveCount},neighbor:{window.NeighborReserveCount},"
                + $"unknown:{window.UnknownReserveCount}) "
                + $"mipRange={mipRange} maxWeightedScore={window.MaxWeightedScore} "
                + $"first={window.FirstSample ?? "none"} last={window.LastSample ?? "none"} "
                + $"retryFirst={window.FirstRetrySample ?? "none"} "
                + $"retryLast={window.LastRetrySample ?? "none"}";
            m_TraceReporter($"[VividRP][VT_DEBUG][TimelineSummary] {details}");
            if (emitRetryWarning && window.RetryLoopCount > 0)
            {
                m_WarningReporter(
                    $"[VividRP][VT_DEBUG][TimelineWarning] code=ReserveCancelRetryLoop "
                    + $"frame={window.LastFrame} {details} "
                    + $"sequence=reserve>cancel>reserve(window)");
            }
        }

        private static string FormatActivitySample(PageTimeline timeline, string sequence)
        {
            return $"(page:{timeline.Key.PageIndex},mip:{timeline.Mip},slot:{timeline.Slot},"
                   + $"generation:{timeline.Generation},request:{FormatRequestKind(timeline.RequestDebugInfo.RequestKind)},"
                   + $"sequence:{sequence})";
        }

        private void ReportLifecycle(
            string outcome,
            int frameIndex,
            PageTimeline timeline,
            string details)
        {
            m_TraceReporter(
                $"[VividRP][VT_DEBUG][PageTimeline] outcome={outcome} pool={m_PoolName} "
                + $"frame={frameIndex} space={timeline.Key.SpaceId} pageIndex={timeline.Key.PageIndex} "
                + $"mip={timeline.Mip} slot={timeline.Slot} generation={timeline.Generation} "
                + $"{FormatRequest(in timeline.RequestDebugInfo)} {details} "
                + $"sequence={FormatSequence(timeline)}>stable@{frameIndex}");
        }

        private void RecordCommitWave(int frameIndex, PageTimeline timeline)
        {
            VTPageRequestKind requestKind = timeline.RequestDebugInfo.RequestKind;
            if (timeline.Locked
                || requestKind == VTPageRequestKind.Bootstrap
                || requestKind == VTPageRequestKind.Locked)
            {
                return;
            }

            FrameWave wave = GetFrameWave(timeline.Key.SpaceId);
            wave.CommitCount += 1;
            AccumulateWaveDetails(wave, timeline);
            switch (requestKind)
            {
                case VTPageRequestKind.Demand:
                    wave.DemandCommitCount += 1;
                    break;
                case VTPageRequestKind.Refinement:
                    wave.RefinementCommitCount += 1;
                    break;
                case VTPageRequestKind.Neighbor:
                    wave.NeighborCommitCount += 1;
                    break;
            }

        }

        private void RecordTransitionWave(int frameIndex, PageTimeline timeline)
        {
            FrameWave wave = GetFrameWave(timeline.Key.SpaceId);
            wave.TransitionCount += 1;
            AccumulateWaveDetails(wave, timeline);
        }

        private FrameWave GetFrameWave(int spaceId)
        {
            if (!m_FrameWaves.TryGetValue(spaceId, out FrameWave wave))
            {
                wave = new FrameWave();
                m_FrameWaves.Add(spaceId, wave);
            }

            return wave;
        }

        private void FinalizeFrameWaves(int frameIndex)
        {
            foreach (KeyValuePair<int, FrameWave> pair in m_FrameWaves)
            {
                int spaceId = pair.Key;
                FrameWave wave = pair.Value;
                if (wave.CommitCount >= CommitBurstThreshold)
                    ReportWaveWarning("CommitBurst", frameIndex, spaceId, wave);
                if (wave.TransitionCount >= TransitionBurstThreshold)
                    ReportWaveWarning("TransitionBurst", frameIndex, spaceId, wave);
                if (wave.CommitCount > 0
                    && wave.TransitionCount > 0
                    && wave.CommitCount + wave.TransitionCount >= VisibilityWaveThreshold)
                {
                    ReportWaveWarning("VisibilityWaveOverlap", frameIndex, spaceId, wave);
                }
            }
        }

        private static void AccumulateWaveDetails(FrameWave wave, PageTimeline timeline)
        {
            wave.MinMip = Math.Min(wave.MinMip, timeline.Mip);
            wave.MaxMip = Math.Max(wave.MaxMip, timeline.Mip);
            wave.MaxWeightedScore = Math.Max(
                wave.MaxWeightedScore,
                timeline.RequestDebugInfo.WeightedScore);
        }

        private void ReportWaveWarning(string code, int frameIndex, int spaceId, FrameWave wave)
        {
            ReportWarning(
                code,
                frameIndex,
                null,
                $"space={spaceId} commits={wave.CommitCount} transitions={wave.TransitionCount} "
                + $"demandCommits={wave.DemandCommitCount} refinementCommits={wave.RefinementCommitCount} "
                + $"neighborCommits={wave.NeighborCommitCount} "
                + $"mipRange={FormatMipRange(wave)} maxWeightedScore={wave.MaxWeightedScore} "
                + $"sequence=frameWave(commit+transitionPhase)");
        }

        private static string FormatMipRange(FrameWave wave)
        {
            return wave.MinMip == int.MaxValue
                ? "unknown"
                : $"{wave.MinMip}-{wave.MaxMip}";
        }

        private void Report(string code, int frameIndex, PageTimeline timeline, string details)
        {
            string pageDetails = timeline == null
                ? string.Empty
                : $" space={timeline.Key.SpaceId} pageIndex={timeline.Key.PageIndex} mip={timeline.Mip} "
                  + $"slot={timeline.Slot} generation={timeline.Generation} "
                  + $"{FormatRequest(in timeline.RequestDebugInfo)}";
            m_ErrorReporter(
                $"[VividRP][VT_DEBUG][TimelineError] code={code} pool={m_PoolName} "
                + $"frame={frameIndex}{pageDetails} {details}");
        }

        private void ReportWarning(string code, int frameIndex, PageTimeline timeline, string details)
        {
            string pageDetails = timeline == null
                ? string.Empty
                : $" space={timeline.Key.SpaceId} pageIndex={timeline.Key.PageIndex} mip={timeline.Mip} "
                  + $"slot={timeline.Slot} generation={timeline.Generation} "
                  + $"{FormatRequest(in timeline.RequestDebugInfo)}";
            m_WarningReporter(
                $"[VividRP][VT_DEBUG][TimelineWarning] code={code} pool={m_PoolName} "
                + $"frame={frameIndex}{pageDetails} {details}");
        }

        private static string FormatAncestor(in VTDebugTransitionAncestor ancestor)
        {
            return ancestor.IsValid
                ? $"(page:{ancestor.PageIndex},mip:{ancestor.Mip},slot:{ancestor.PhysicalPageId},generation:{ancestor.Generation})"
                : "invalid";
        }

        private static string FormatSequence(PageTimeline timeline)
        {
            if (timeline == null)
                return "unknown";

            string sequence = $"reserve@{timeline.ReserveFrame}";
            if (timeline.CommitFrame >= 0)
                sequence += $">commit@{timeline.CommitFrame}";
            if (timeline.TransitionFrame >= 0)
                sequence += $">transition@{timeline.TransitionFrame}>phase{timeline.LastTransitionPhase}";
            return sequence;
        }

        private static string FormatRequest(in VTPageRequestDebugInfo debugInfo)
        {
            return $"requestKind={FormatRequestKind(debugInfo.RequestKind)} "
                   + $"sourceCoord={debugInfo.SourceCoord} effectiveCoord={debugInfo.EffectiveCoord} "
                   + $"mipGap={debugInfo.MipGap} weightedScore={debugInfo.WeightedScore}";
        }

        private static string FormatRequestKind(VTPageRequestKind requestKind)
        {
            return requestKind switch
            {
                VTPageRequestKind.Bootstrap => "bootstrap",
                VTPageRequestKind.Locked => "locked",
                VTPageRequestKind.Demand => "demand",
                VTPageRequestKind.Refinement => "refinement",
                VTPageRequestKind.Neighbor => "neighbor",
                _ => "unknown",
            };
        }
    }
#endif

    internal sealed class VTPhysicalPool : IDisposable
    {
        internal const int AsyncCommitEvictionProtectionFrames = 3;
        internal const int FeedbackEvictionProtectionFrames = 16;

        private struct PhysicalPageBinding
        {
            public IVTPhysicalPoolOwner Owner;
            public int SpaceId;
            public int VirtualPageIndex;
            public bool Locked;
            public bool VisibilityPending;
        }

        private struct PhysicalPageSlotState
        {
            public IVTPhysicalPoolOwner Owner;
            public int SpaceId;
            public int VirtualPageIndex;
            public int VirtualPageMip;
            public int Generation;
            public int LastAllocationFrame;
            public int LastAsyncCommitFrame;
            public VirtualTextureViewId AffinityViewId;
            public int LastAffinityFrame;
            public VTPhysicalPageIdentity Identity;
            public bool Resident;
            public bool PendingUpload;
            public bool Locked;
            public bool VisibilityPending;
#if VT_DEBUG
            public VTPageRequestDebugInfo RequestDebugInfo;
#endif
        }

        private readonly PhysicalPageSlotState[] m_Slots;
        private readonly Stack<int> m_FreePhysicalPages;
        private readonly LinkedList<int> m_LruPhysicalPages = new();
        private readonly LinkedListNode<int>[] m_LruNodes;
        private readonly int[] m_LastLruTouchFrames;
        private readonly int[] m_NextPhysicalPageWithSameIdentity;
        private readonly Dictionary<VTPhysicalPageIdentity, int> m_PhysicalPageLookup;
        private readonly List<PhysicalPageBinding>[] m_Bindings;
        private readonly Texture2D[] m_Textures;
        private readonly VTPhysicalAtlasLayout[] m_AtlasLayouts;
        private readonly string m_PoolName;
        private readonly long m_AllocatedByteCount;
        private readonly long m_BytesPerPhysicalPage;
#if VT_DEBUG
        private readonly string m_DebugName;
        private readonly VTDebugPageTimelineDiagnostics m_DebugTimeline;
#endif

        private int m_NextGeneration;
        private int m_RefCount;
        private int m_EvictedPageCount;
        private int m_LastTransitionStartFrame = int.MinValue;
        private int m_TransitionStartsThisFrame;

        internal VTPhysicalPool(string name, in VTPhysicalPoolDesc desc)
        {
            Desc = desc;
            string poolName = string.IsNullOrWhiteSpace(name) ? "Shared" : name;
            m_PoolName = poolName;
#if VT_DEBUG
            m_DebugName = poolName;
            m_DebugTimeline = new VTDebugPageTimelineDiagnostics(poolName);
#endif
            m_Slots = new PhysicalPageSlotState[Mathf.Max(1, desc.PageCount)];
            for (int slotIndex = 0; slotIndex < m_Slots.Length; slotIndex++)
            {
                m_Slots[slotIndex].VirtualPageIndex = -1;
                m_Slots[slotIndex].LastAsyncCommitFrame = -1;
                m_Slots[slotIndex].AffinityViewId = VirtualTextureViewId.Invalid;
                m_Slots[slotIndex].LastAffinityFrame = -1;
            }

            m_LruNodes = new LinkedListNode<int>[m_Slots.Length];
            m_LastLruTouchFrames = new int[m_Slots.Length];
            m_NextPhysicalPageWithSameIdentity = new int[m_Slots.Length];
            m_PhysicalPageLookup = new Dictionary<VTPhysicalPageIdentity, int>(m_Slots.Length);
            m_Bindings = new List<PhysicalPageBinding>[m_Slots.Length];
            for (int slotIndex = 0; slotIndex < m_Bindings.Length; slotIndex++)
            {
                m_LastLruTouchFrames[slotIndex] = int.MinValue;
                m_NextPhysicalPageWithSameIdentity[slotIndex] = -1;
                m_Bindings[slotIndex] = new List<PhysicalPageBinding>(1);
            }

            m_FreePhysicalPages = new Stack<int>(m_Slots.Length);
            for (int slotIndex = m_Slots.Length - 1; slotIndex >= 0; slotIndex--)
                m_FreePhysicalPages.Push(slotIndex);

            m_Textures = new Texture2D[Mathf.Max(1, desc.PhysicalGroupCount)];
            m_AtlasLayouts = new VTPhysicalAtlasLayout[m_Textures.Length];
            long allocatedByteCount = 0;
            long bytesPerPhysicalPage = 0;
            try
            {
                for (int groupIndex = 0; groupIndex < m_Textures.Length; groupIndex++)
                {
                    GraphicsFormat storageFormat = desc.GetGroupStorageFormat(groupIndex);
                    CopyTextureSupport requiredCopySupport =
                        CopyTextureSupport.Basic | CopyTextureSupport.DifferentTypes;
                    if (GraphicsFormatUtility.IsCompressedFormat(storageFormat)
                        && (!SystemInfo.IsFormatSupported(storageFormat, GraphicsFormatUsage.Sample)
                            || (SystemInfo.copyTextureSupport & requiredCopySupport) != requiredCopySupport))
                    {
                        throw new InvalidOperationException(
                            $"The active graphics device cannot sample and CopyTexture the compressed VT format "
                            + $"{storageFormat} used by physical group {groupIndex}.");
                    }

                    int groupLayerCount = Mathf.Max(1, desc.GetGroupLayerCount(groupIndex));
                    int tileCount = checked(m_Slots.Length * groupLayerCount);
                    m_AtlasLayouts[groupIndex] = new VTPhysicalAtlasLayout(
                        desc.PhysicalPageSize,
                        tileCount,
                        SystemInfo.maxTextureSize);
                    m_Textures[groupIndex] = CreatePhysicalTexture(
                        poolName,
                        desc,
                        groupIndex,
                        m_AtlasLayouts[groupIndex]);
                    allocatedByteCount = checked(
                        allocatedByteCount
                        + GetTextureByteCount(
                            storageFormat,
                            m_AtlasLayouts[groupIndex].Width,
                            m_AtlasLayouts[groupIndex].Height));
                    bytesPerPhysicalPage = checked(
                        bytesPerPhysicalPage
                        + GetTextureByteCount(
                            storageFormat,
                            desc.PhysicalPageSize,
                            desc.PhysicalPageSize)
                        * groupLayerCount);
                }
            }
            catch
            {
                DestroyTextures(m_Textures);
                throw;
            }

            m_AllocatedByteCount = allocatedByteCount;
            m_BytesPerPhysicalPage = bytesPerPhysicalPage;
        }

        internal VTPhysicalPoolDesc Desc { get; }

        internal Texture2D Texture => GetTextureForGroup(0);

        internal IReadOnlyList<Texture2D> Textures => m_Textures ?? Array.Empty<Texture2D>();

        internal Texture2D GetTextureForGroup(int physicalGroup)
        {
            if (m_Textures == null || physicalGroup < 0 || physicalGroup >= m_Textures.Length)
                return null;

            return m_Textures[physicalGroup];
        }

        internal VTPhysicalAtlasLayout GetAtlasLayoutForGroup(int physicalGroup)
        {
            if (m_AtlasLayouts == null || physicalGroup < 0 || physicalGroup >= m_AtlasLayouts.Length)
                throw new ArgumentOutOfRangeException(nameof(physicalGroup));

            return m_AtlasLayouts[physicalGroup];
        }

        internal RectInt GetPhysicalTileRect(int physicalGroup, int physicalPageId, int physicalLayerIndex)
        {
            if (physicalPageId < 0 || physicalPageId >= m_Slots.Length)
                throw new ArgumentOutOfRangeException(nameof(physicalPageId));

            int groupLayerCount = Mathf.Max(1, GetGroupLayerCount(physicalGroup));
            if (physicalLayerIndex < 0 || physicalLayerIndex >= groupLayerCount)
                throw new ArgumentOutOfRangeException(nameof(physicalLayerIndex));

            int tileIndex = checked(physicalPageId * groupLayerCount + physicalLayerIndex);
            return GetAtlasLayoutForGroup(physicalGroup).GetTileRect(tileIndex);
        }

        internal int GetGroupLayerCount(int physicalGroup)
        {
            return Desc.GetGroupLayerCount(physicalGroup);
        }

        internal int GetLayerPhysicalGroup(int layerIndex)
        {
            return Desc.GetLayerPhysicalGroup(layerIndex);
        }

        internal int GetLayerPhysicalLayerIndex(int layerIndex)
        {
            return Desc.GetLayerPhysicalLayerIndex(layerIndex);
        }

        internal int RefCount => m_RefCount;

        internal int FreePageCount => m_FreePhysicalPages.Count;

        internal int ResidentPageCount
        {
            get
            {
                int count = 0;
                for (int pageIndex = 0; pageIndex < m_Slots.Length; pageIndex++)
                {
                    if (IsOccupied(m_Slots[pageIndex]) && m_Slots[pageIndex].Resident)
                        count += 1;
                }

                return count;
            }
        }

        internal int LockedPageCount
        {
            get
            {
                int count = 0;
                for (int pageIndex = 0; pageIndex < m_Slots.Length; pageIndex++)
                {
                    if (IsOccupied(m_Slots[pageIndex]) && m_Slots[pageIndex].Locked)
                        count += 1;
                }

                return count;
            }
        }

        internal int EvictedPageCount => m_EvictedPageCount;

        internal void ResetRuntimeState()
        {
            m_EvictedPageCount = 0;
            m_LastTransitionStartFrame = int.MinValue;
            m_TransitionStartsThisFrame = 0;
#if VT_DEBUG
            m_DebugTimeline.Reset();
#endif
            RecreatePhysicalTextures();
        }

#if VT_DEBUG
        internal void DebugAdvanceTimelineFrame(int frameIndex)
        {
            m_DebugTimeline.AdvanceFrame(frameIndex);
        }

        internal void DebugResetTimeline()
        {
            m_DebugTimeline.Reset();
        }

        internal void DebugNotifyPageTransitionBegin(
            int spaceId,
            int pageIndex,
            int mip,
            int physicalPageId,
            int generation,
            int frameIndex,
            in VTDebugTransitionAncestor ancestor)
        {
            m_DebugTimeline.OnTransitionBegin(
                spaceId,
                pageIndex,
                mip,
                physicalPageId,
                generation,
                frameIndex,
                ancestor);
        }

        internal void DebugValidatePageTransitionAncestor(
            int spaceId,
            int pageIndex,
            int mip,
            int physicalPageId,
            int generation,
            int frameIndex,
            byte phase,
            in VTDebugTransitionAncestor ancestor)
        {
            m_DebugTimeline.OnTransitionAncestorObserved(
                spaceId,
                pageIndex,
                mip,
                physicalPageId,
                generation,
                frameIndex,
                phase,
                ancestor);
        }

        internal void DebugNotifyPageTransitionPhase(
            int spaceId,
            int pageIndex,
            int mip,
            int physicalPageId,
            int generation,
            int frameIndex,
            byte previousPhase,
            byte nextPhase)
        {
            m_DebugTimeline.OnTransitionPhase(
                spaceId,
                pageIndex,
                mip,
                physicalPageId,
                generation,
                frameIndex,
                previousPhase,
                nextPhase);
        }
#endif

        internal long AllocatedByteCount => m_AllocatedByteCount;

        internal long ResidentByteCount => checked((long)ResidentPageCount * m_BytesPerPhysicalPage);

        internal bool TryAcquireTransitionStart(int frameIndex, int maxStartsPerFrame)
        {
            if (frameIndex < 0 || maxStartsPerFrame <= 0)
                return false;

            if (m_LastTransitionStartFrame != frameIndex)
            {
                m_LastTransitionStartFrame = frameIndex;
                m_TransitionStartsThisFrame = 0;
            }

            if (m_TransitionStartsThisFrame >= maxStartsPerFrame)
                return false;

            m_TransitionStartsThisFrame += 1;
            return true;
        }

        internal void AddRef()
        {
            m_RefCount += 1;
        }

        internal int ReleaseRef()
        {
            m_RefCount = Mathf.Max(0, m_RefCount - 1);
            return m_RefCount;
        }

        internal bool TryAllocatePage(
            IVTPhysicalPoolOwner owner,
            VTProducerHandle producerHandle,
            string producerName,
            int pageIndex,
            int pageMip,
            in VirtualTexturePageCoord pageCoord,
            VirtualTextureViewId activeViewId,
            VirtualTextureViewId allocationViewId,
            bool updateAffinity,
            int frameIndex,
            bool locked,
            bool pendingUpload,
#if VT_DEBUG
            in VTPageRequestDebugInfo requestDebugInfo,
#endif
            out int physicalPageId,
            out int generation,
            out bool evicted)
        {
            physicalPageId = -1;
            generation = 0;
            evicted = false;
#if VT_DEBUG
            bool allocatedFromFreeList = false;
            PhysicalPageSlotState replacedSlotState = default;
            int replacedBindingCount = 0;
#endif
            if (owner == null)
                return false;

            if (m_FreePhysicalPages.Count > 0)
            {
                physicalPageId = m_FreePhysicalPages.Pop();
#if VT_DEBUG
                allocatedFromFreeList = true;
#endif
            }
            else
            {
                physicalPageId = FindEvictionCandidate(frameIndex, activeViewId);
                if (physicalPageId < 0)
                    return false;

#if VT_DEBUG
                replacedSlotState = m_Slots[physicalPageId];
                replacedBindingCount = m_Bindings[physicalPageId].Count;
                LogPageReplacementBegin(
                    physicalPageId,
                    in replacedSlotState,
                    replacedBindingCount,
                    owner,
                    producerHandle,
                    producerName,
                    pageIndex,
                    pageMip,
                    in pageCoord,
                    activeViewId,
                    allocationViewId,
                    updateAffinity,
                    frameIndex,
                    locked,
                    pendingUpload,
                    in requestDebugInfo);
#endif
                evicted = EvictPhysicalPageForReuse(physicalPageId, frameIndex);
            }

            generation = ++m_NextGeneration;
            PhysicalPageSlotState slotState = m_Slots[physicalPageId];
            slotState.Owner = owner;
            slotState.SpaceId = owner.SpaceId;
            slotState.VirtualPageIndex = pageIndex;
            slotState.VirtualPageMip = pageMip;
            slotState.Generation = generation;
            slotState.LastAllocationFrame = frameIndex;
            slotState.LastAsyncCommitFrame = -1;
            slotState.Identity = new VTPhysicalPageIdentity(producerHandle, producerName, pageCoord);
            slotState.Resident = !pendingUpload;
            slotState.PendingUpload = pendingUpload;
            slotState.Locked = locked;
            slotState.VisibilityPending = false;
#if VT_DEBUG
            slotState.RequestDebugInfo = requestDebugInfo;
#endif
            slotState.AffinityViewId = VirtualTextureViewId.Invalid;
            slotState.LastAffinityFrame = -1;
            m_Slots[physicalPageId] = slotState;
            AddPhysicalPageLookup(physicalPageId, slotState.Identity);
            m_Bindings[physicalPageId].Clear();
            AddBinding(physicalPageId, owner, pageIndex, locked);
            Touch(physicalPageId, allocationViewId, frameIndex, updateAffinity);
#if VT_DEBUG
            if (evicted)
            {
                PhysicalPageSlotState committedSlotState = m_Slots[physicalPageId];
                LogPageReplacementCommit(
                    physicalPageId,
                    in replacedSlotState,
                    replacedBindingCount,
                    in committedSlotState,
                    frameIndex);
            }
            else if (allocatedFromFreeList)
            {
                PhysicalPageSlotState reservedSlotState = m_Slots[physicalPageId];
                RecordPageReserve(
                    physicalPageId,
                    in reservedSlotState,
                    frameIndex);
            }

            if (!pendingUpload)
            {
                PhysicalPageSlotState residentSlotState = m_Slots[physicalPageId];
                RecordPageResidentCommit(
                    physicalPageId,
                    in residentSlotState,
                    frameIndex,
                    commitFrameIndex: -1,
                    wasPendingUpload: false,
                    wasResident: false);
            }
#endif
            return true;
        }

        internal bool TryAttachResidentPage(
            IVTPhysicalPoolOwner owner,
            VTProducerHandle producerHandle,
            string producerName,
            int pageIndex,
            in VirtualTexturePageCoord pageCoord,
            VirtualTextureViewId viewId,
            int frameIndex,
            bool locked,
            out int physicalPageId,
            out int generation)
        {
            physicalPageId = -1;
            generation = 0;
            if (owner == null)
                return false;

            if (!TryFindPhysicalPage(producerHandle, producerName, pageCoord, out physicalPageId, out generation))
                return false;

            PhysicalPageSlotState slotState = m_Slots[physicalPageId];
            if (!slotState.Resident || slotState.PendingUpload)
            {
                physicalPageId = -1;
                generation = 0;
                return false;
            }

            AddBinding(physicalPageId, owner, pageIndex, locked);
            Touch(physicalPageId, viewId, frameIndex, HasViewAffinity(viewId));
#if VT_DEBUG
            LogPageResidentAttach(
                physicalPageId,
                owner.SpaceId,
                pageIndex,
                pageCoord.Mip,
                generation,
                frameIndex,
                locked);
#endif
            return true;
        }

        internal bool TryFindPhysicalPage(
            VTProducerHandle producerHandle,
            string producerName,
            in VirtualTexturePageCoord pageCoord,
            out int physicalPageId,
            out int generation)
        {
            var identity = new VTPhysicalPageIdentity(producerHandle, producerName, pageCoord);
            if (m_PhysicalPageLookup.TryGetValue(identity, out int slotIndex))
            {
                PhysicalPageSlotState slotState = m_Slots[slotIndex];
                if (IsOccupied(slotState) && slotState.Identity.Equals(identity))
                {
                    physicalPageId = slotIndex;
                    generation = slotState.Generation;
                    return true;
                }
            }

            physicalPageId = -1;
            generation = 0;
            return false;
        }

        internal bool TryCommitPage(
            int physicalPageId,
            int generation,
            int commitFrameIndex = -1)
        {
            if (!TryGetSlot(physicalPageId, generation, out PhysicalPageSlotState slotState))
                return false;

#if VT_DEBUG
            bool wasPendingUpload = slotState.PendingUpload;
            bool wasResident = slotState.Resident;
            int allocationFrameIndex = slotState.LastAllocationFrame;
#endif
            slotState.PendingUpload = false;
            slotState.Resident = true;
            if (commitFrameIndex >= 0)
            {
                slotState.LastAllocationFrame = Mathf.Max(
                    slotState.LastAllocationFrame,
                    commitFrameIndex);
                slotState.LastAsyncCommitFrame = commitFrameIndex;
            }
            m_Slots[physicalPageId] = slotState;
#if VT_DEBUG
            RecordPageResidentCommit(
                physicalPageId,
                in slotState,
                allocationFrameIndex,
                commitFrameIndex,
                wasPendingUpload,
                wasResident);
#endif
            return true;
        }

        internal bool TrySetLocked(
            int physicalPageId,
            int generation,
            IVTPhysicalPoolOwner owner,
            int pageIndex,
            bool locked)
        {
            if (!TryGetSlot(physicalPageId, generation, out PhysicalPageSlotState slotState))
                return false;

            if (!TrySetBindingLocked(physicalPageId, owner, pageIndex, locked))
                return false;

            slotState.Locked = IsAnyBindingLocked(physicalPageId);
            m_Slots[physicalPageId] = slotState;
#if VT_DEBUG
            m_DebugTimeline.OnLockChanged(
                physicalPageId,
                owner?.SpaceId ?? slotState.SpaceId,
                pageIndex,
                generation,
                locked);
#endif
            return true;
        }

        internal bool TrySetVisibilityPending(
            int physicalPageId,
            int generation,
            IVTPhysicalPoolOwner owner,
            int pageIndex,
            bool visibilityPending)
        {
            if (!TryGetSlot(physicalPageId, generation, out PhysicalPageSlotState slotState))
                return false;

            List<PhysicalPageBinding> bindings = m_Bindings[physicalPageId];
            bool found = false;
            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                PhysicalPageBinding binding = bindings[bindingIndex];
                if (!ReferenceEquals(binding.Owner, owner) || binding.VirtualPageIndex != pageIndex)
                    continue;

                binding.VisibilityPending = visibilityPending;
                bindings[bindingIndex] = binding;
                found = true;
                break;
            }

            if (!found)
                return false;

            slotState.VisibilityPending = IsAnyBindingVisibilityPending(physicalPageId);
            m_Slots[physicalPageId] = slotState;
            return true;
        }

        internal void Touch(
            int physicalPageId,
            VirtualTextureViewId viewId,
            int frameIndex,
            bool updateAffinity)
        {
            if (physicalPageId < 0 || physicalPageId >= m_Slots.Length)
                return;

            if (updateAffinity && HasViewAffinity(viewId))
            {
                PhysicalPageSlotState slotState = m_Slots[physicalPageId];
                slotState.AffinityViewId = viewId;
                slotState.LastAffinityFrame = frameIndex;
                m_Slots[physicalPageId] = slotState;
            }

            if (m_LastLruTouchFrames[physicalPageId] == frameIndex)
                return;

            m_LastLruTouchFrames[physicalPageId] = frameIndex;

            LinkedListNode<int> node = m_LruNodes[physicalPageId];
            if (node == null)
            {
                node = new LinkedListNode<int>(physicalPageId);
                m_LruNodes[physicalPageId] = node;
                m_LruPhysicalPages.AddLast(node);
                return;
            }

            if (node.List != null && node != m_LruPhysicalPages.Last)
            {
                m_LruPhysicalPages.Remove(node);
                m_LruPhysicalPages.AddLast(node);
            }
            else if (node.List == null)
            {
                m_LruPhysicalPages.AddLast(node);
            }
        }

        internal int FlushProducer(VTProducerHandle producerHandle, string producerName)
        {
            int flushedCount = 0;
            for (int slotIndex = m_Slots.Length - 1; slotIndex >= 0; slotIndex--)
            {
                PhysicalPageSlotState slotState = m_Slots[slotIndex];
                if (!IsOccupied(slotState) || !IsSameProducer(slotState.Identity, producerHandle, producerName))
                    continue;

                FlushPhysicalPage(slotIndex);
                flushedCount += 1;
            }

            return flushedCount;
        }

        internal int FlushRegion(
            int spaceId,
            int mip,
            RectInt pageRegion)
        {
            int flushedCount = 0;
            for (int slotIndex = m_Slots.Length - 1; slotIndex >= 0; slotIndex--)
            {
                PhysicalPageSlotState slotState = m_Slots[slotIndex];
                if (!IsOccupied(slotState)
                    || !HasBindingForSpace(slotIndex, spaceId)
                    || slotState.Identity.PageCoord.Mip != mip
                    || !pageRegion.Contains(new Vector2Int(slotState.Identity.PageCoord.X, slotState.Identity.PageCoord.Y)))
                {
                    continue;
                }

                flushedCount += FlushBindings(
                    slotIndex,
                    binding => binding.SpaceId == spaceId);
            }

            return flushedCount;
        }

        internal int FlushOwner(IVTPhysicalPoolOwner owner)
        {
            if (owner == null)
                return 0;

            int flushedCount = 0;
            for (int slotIndex = m_Slots.Length - 1; slotIndex >= 0; slotIndex--)
            {
                flushedCount += FlushBindings(
                    slotIndex,
                    binding => ReferenceEquals(binding.Owner, owner));
            }

            return flushedCount;
        }

        public void Dispose()
        {
#if VT_DEBUG
            m_DebugTimeline.Reset();
#endif
            m_LruPhysicalPages.Clear();
            m_PhysicalPageLookup.Clear();
            m_FreePhysicalPages.Clear();
            for (int slotIndex = 0; slotIndex < m_Bindings.Length; slotIndex++)
                m_Bindings[slotIndex].Clear();

            if (m_Textures == null)
                return;

            DestroyTextures(m_Textures);
        }

        private static void DestroyTextures(IReadOnlyList<Texture2D> textures)
        {
            if (textures == null)
                return;

            for (int textureIndex = 0; textureIndex < textures.Count; textureIndex++)
            {
                if (textures[textureIndex] != null)
                    CoreUtils.Destroy(textures[textureIndex]);
            }
        }

        private void RecreatePhysicalTextures()
        {
            var replacements = new Texture2D[m_Textures.Length];
            try
            {
                for (int physicalGroup = 0; physicalGroup < replacements.Length; physicalGroup++)
                {
                    replacements[physicalGroup] = CreatePhysicalTexture(
                        m_PoolName,
                        Desc,
                        physicalGroup,
                        m_AtlasLayouts[physicalGroup]);
                }
            }
            catch
            {
                DestroyTextures(replacements);
                throw;
            }

            for (int physicalGroup = 0; physicalGroup < replacements.Length; physicalGroup++)
            {
                Texture2D previous = m_Textures[physicalGroup];
                m_Textures[physicalGroup] = replacements[physicalGroup];
                if (previous != null)
                    CoreUtils.Destroy(previous);
            }
        }

        private static Texture2D CreatePhysicalTexture(
            string poolName,
            in VTPhysicalPoolDesc desc,
            int physicalGroup,
            in VTPhysicalAtlasLayout layout)
        {
            GraphicsFormat storageFormat = desc.GetGroupStorageFormat(physicalGroup);
            if (storageFormat == GraphicsFormat.None)
                storageFormat = desc.GraphicsFormat;

            var texture = new Texture2D(
                layout.Width,
                layout.Height,
                storageFormat,
                TextureCreationFlags.None)
            {
                name = physicalGroup == 0
                    ? $"VividVT_{poolName}_PhysicalAtlas"
                    : $"VividVT_{poolName}_PhysicalAtlas_Group{physicalGroup}",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            NativeArray<byte> rawTextureData = texture.GetRawTextureData<byte>();
            unsafe
            {
                UnsafeUtility.MemClear(rawTextureData.GetUnsafePtr(), rawTextureData.Length);
            }
            texture.Apply(false, true);
            return texture;
        }

        private bool TryGetSlot(int physicalPageId, int generation, out PhysicalPageSlotState slotState)
        {
            slotState = default;
            if (physicalPageId < 0 || physicalPageId >= m_Slots.Length)
                return false;

            slotState = m_Slots[physicalPageId];
            return IsOccupied(slotState) && slotState.Generation == generation;
        }

        private bool EvictPhysicalPageForReuse(int physicalPageId, int frameIndex)
        {
            PhysicalPageSlotState slotState = m_Slots[physicalPageId];
            if (!IsOccupied(slotState))
                return false;

#if VT_DEBUG
            InvalidateBindingsForReplacement(physicalPageId, frameIndex);
#else
            InvalidateBindings(physicalPageId);
#endif
            ClearPhysicalPage(physicalPageId, releaseToFreeList: false);
            m_EvictedPageCount += 1;
            return true;
        }

        private void FlushPhysicalPage(int physicalPageId)
        {
            PhysicalPageSlotState slotState = m_Slots[physicalPageId];
            if (!IsOccupied(slotState))
                return;

            InvalidateBindings(physicalPageId);
            ClearPhysicalPage(physicalPageId, releaseToFreeList: true);
        }

        private void AddBinding(
            int physicalPageId,
            IVTPhysicalPoolOwner owner,
            int pageIndex,
            bool locked)
        {
            List<PhysicalPageBinding> bindings = m_Bindings[physicalPageId];
            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                PhysicalPageBinding binding = bindings[bindingIndex];
                if (!ReferenceEquals(binding.Owner, owner) || binding.VirtualPageIndex != pageIndex)
                    continue;

                binding.Locked |= locked;
                bindings[bindingIndex] = binding;
                if (locked)
                    SetSlotLocked(physicalPageId, true);
                return;
            }

            bindings.Add(new PhysicalPageBinding
            {
                Owner = owner,
                SpaceId = owner.SpaceId,
                VirtualPageIndex = pageIndex,
                Locked = locked,
                VisibilityPending = false,
            });

            PhysicalPageSlotState slotState = m_Slots[physicalPageId];
            if (slotState.Owner == null)
            {
                slotState.Owner = owner;
                slotState.SpaceId = owner.SpaceId;
                slotState.VirtualPageIndex = pageIndex;
            }

            if (locked)
                slotState.Locked = true;

            m_Slots[physicalPageId] = slotState;
        }

        private bool TrySetBindingLocked(
            int physicalPageId,
            IVTPhysicalPoolOwner owner,
            int pageIndex,
            bool locked)
        {
            List<PhysicalPageBinding> bindings = m_Bindings[physicalPageId];
            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                PhysicalPageBinding binding = bindings[bindingIndex];
                if (!ReferenceEquals(binding.Owner, owner) || binding.VirtualPageIndex != pageIndex)
                    continue;

                binding.Locked = locked;
                bindings[bindingIndex] = binding;
                return true;
            }

            return false;
        }

        private bool IsAnyBindingLocked(int physicalPageId)
        {
            List<PhysicalPageBinding> bindings = m_Bindings[physicalPageId];
            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                if (bindings[bindingIndex].Locked)
                    return true;
            }

            return false;
        }

        private bool IsAnyBindingVisibilityPending(int physicalPageId)
        {
            List<PhysicalPageBinding> bindings = m_Bindings[physicalPageId];
            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                if (bindings[bindingIndex].VisibilityPending)
                    return true;
            }

            return false;
        }

        private bool HasBindingForSpace(int physicalPageId, int spaceId)
        {
            List<PhysicalPageBinding> bindings = m_Bindings[physicalPageId];
            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                if (bindings[bindingIndex].SpaceId == spaceId)
                    return true;
            }

            return false;
        }

        private void SetSlotLocked(int physicalPageId, bool locked)
        {
            PhysicalPageSlotState slotState = m_Slots[physicalPageId];
            slotState.Locked = locked;
            m_Slots[physicalPageId] = slotState;
        }

        private int FlushBindings(
            int physicalPageId,
            Predicate<PhysicalPageBinding> predicate)
        {
            if (predicate == null || physicalPageId < 0 || physicalPageId >= m_Bindings.Length)
                return 0;

            List<PhysicalPageBinding> bindings = m_Bindings[physicalPageId];
            if (bindings.Count == 0)
                return 0;

            int flushedCount = 0;
            int generation = m_Slots[physicalPageId].Generation;
            for (int bindingIndex = bindings.Count - 1; bindingIndex >= 0; bindingIndex--)
            {
                PhysicalPageBinding binding = bindings[bindingIndex];
                if (!predicate(binding))
                    continue;

                binding.Owner?.OnPhysicalPageInvalidated(binding.VirtualPageIndex, generation);
#if VT_DEBUG
                if (bindings.Count > 1)
                {
                    m_DebugTimeline.OnSharedBindingReleased(
                        physicalPageId,
                        binding.SpaceId,
                        binding.VirtualPageIndex,
                        generation);
                }
#endif
                bindings.RemoveAt(bindingIndex);
                flushedCount += 1;
            }

            if (flushedCount <= 0)
                return 0;

            if (bindings.Count == 0)
                ClearPhysicalPage(physicalPageId, releaseToFreeList: true);
            else
                PromotePrimaryBinding(physicalPageId);

            return flushedCount;
        }

        private void InvalidateBindings(int physicalPageId)
        {
            List<PhysicalPageBinding> bindings = m_Bindings[physicalPageId];
            int generation = m_Slots[physicalPageId].Generation;
            for (int bindingIndex = bindings.Count - 1; bindingIndex >= 0; bindingIndex--)
            {
                PhysicalPageBinding binding = bindings[bindingIndex];
                binding.Owner?.OnPhysicalPageInvalidated(binding.VirtualPageIndex, generation);
            }
        }

#if VT_DEBUG
        private void InvalidateBindingsForReplacement(int physicalPageId, int frameIndex)
        {
            List<PhysicalPageBinding> bindings = m_Bindings[physicalPageId];
            int generation = m_Slots[physicalPageId].Generation;
            for (int bindingIndex = bindings.Count - 1; bindingIndex >= 0; bindingIndex--)
            {
                PhysicalPageBinding binding = bindings[bindingIndex];
                bool invalidated = binding.Owner?.OnPhysicalPageInvalidated(
                    binding.VirtualPageIndex,
                    generation) ?? false;
                string ownerType = binding.Owner != null
                    ? binding.Owner.GetType().Name
                    : "<null>";
                string message =
                    $"[VividRP][VT_DEBUG][PageReplaceInvalidate] pool={m_DebugName} frame={frameIndex} "
                    + $"slot={physicalPageId} generation={generation} "
                    + $"binding={bindingIndex + 1}/{bindings.Count} space={binding.SpaceId} "
                    + $"pageIndex={binding.VirtualPageIndex} locked={binding.Locked} "
                    + $"owner={ownerType} accepted={invalidated}";
                if (invalidated)
                    VTDebugLog.Trace(message);
                else
                    VTDebugLog.Warning(message);
                m_DebugTimeline.OnReplacementInvalidation(
                    physicalPageId,
                    binding.SpaceId,
                    binding.VirtualPageIndex,
                    generation,
                    frameIndex,
                    invalidated);
            }
        }

        private void LogPageReplacementBegin(
            int physicalPageId,
            in PhysicalPageSlotState oldSlot,
            int oldBindingCount,
            IVTPhysicalPoolOwner newOwner,
            VTProducerHandle newProducerHandle,
            string newProducerName,
            int newPageIndex,
            int newPageMip,
            in VirtualTexturePageCoord newPageCoord,
            VirtualTextureViewId activeViewId,
            VirtualTextureViewId allocationViewId,
            bool updateAffinity,
            int frameIndex,
            bool newLocked,
            bool newPendingUpload,
            in VTPageRequestDebugInfo requestDebugInfo)
        {
            m_DebugTimeline.OnReplacementBegin(
                physicalPageId,
                oldSlot.SpaceId,
                oldSlot.VirtualPageIndex,
                oldSlot.Generation,
                frameIndex);
            VTDebugLog.Trace(
                $"[VividRP][VT_DEBUG][PageReplaceBegin] pool={m_DebugName} frame={frameIndex} slot={physicalPageId} "
                + $"old=(space:{oldSlot.SpaceId},pageIndex:{oldSlot.VirtualPageIndex},mip:{oldSlot.VirtualPageMip},"
                + $"coord:{oldSlot.Identity.PageCoord},producer:{FormatProducer(oldSlot.Identity.ProducerHandle, oldSlot.Identity.ProducerName)},"
                + $"generation:{oldSlot.Generation},resident:{oldSlot.Resident},pending:{oldSlot.PendingUpload},"
                + $"locked:{oldSlot.Locked},allocatedFrame:{oldSlot.LastAllocationFrame},"
                + $"asyncCommitFrame:{oldSlot.LastAsyncCommitFrame},lastTouchFrame:{m_LastLruTouchFrames[physicalPageId]},"
                + $"affinity:{oldSlot.AffinityViewId},affinityFrame:{oldSlot.LastAffinityFrame},bindings:{oldBindingCount}) "
                + $"new=(space:{newOwner.SpaceId},pageIndex:{newPageIndex},mip:{newPageMip},coord:{newPageCoord},"
                + $"producer:{FormatProducer(newProducerHandle, newProducerName)},locked:{newLocked},pending:{newPendingUpload}) "
                + $"activeView={activeViewId} allocationView={allocationViewId} updateAffinity={updateAffinity} "
                + $"{FormatRequestDebug(in requestDebugInfo)} "
                + $"evictionCountBefore={m_EvictedPageCount}");
        }

        private void LogPageReplacementCommit(
            int physicalPageId,
            in PhysicalPageSlotState oldSlot,
            int oldBindingCount,
            in PhysicalPageSlotState newSlot,
            int frameIndex)
        {
            m_DebugTimeline.OnReplacementCommit(
                physicalPageId,
                oldSlot.Generation,
                newSlot.SpaceId,
                newSlot.VirtualPageIndex,
                newSlot.VirtualPageMip,
                newSlot.Generation,
                frameIndex,
                newSlot.PendingUpload,
                newSlot.Locked,
                newSlot.RequestDebugInfo);
            VTDebugLog.Trace(
                $"[VividRP][VT_DEBUG][PageReplaceCommit] pool={m_DebugName} frame={frameIndex} slot={physicalPageId} "
                + $"old=(space:{oldSlot.SpaceId},pageIndex:{oldSlot.VirtualPageIndex},mip:{oldSlot.VirtualPageMip},"
                + $"coord:{oldSlot.Identity.PageCoord},generation:{oldSlot.Generation},bindings:{oldBindingCount}) "
                + $"new=(space:{newSlot.SpaceId},pageIndex:{newSlot.VirtualPageIndex},mip:{newSlot.VirtualPageMip},"
                + $"coord:{newSlot.Identity.PageCoord},producer:{FormatProducer(newSlot.Identity.ProducerHandle, newSlot.Identity.ProducerName)},"
                + $"generation:{newSlot.Generation},resident:{newSlot.Resident},pending:{newSlot.PendingUpload},"
                + $"locked:{newSlot.Locked},lastTouchFrame:{m_LastLruTouchFrames[physicalPageId]},"
                + $"affinity:{newSlot.AffinityViewId},affinityFrame:{newSlot.LastAffinityFrame}) "
                + $"{FormatRequestDebug(in newSlot.RequestDebugInfo)} "
                + $"evictionCountAfter={m_EvictedPageCount}");
        }

        private void RecordPageReserve(
            int physicalPageId,
            in PhysicalPageSlotState slot,
            int frameIndex)
        {
            m_DebugTimeline.OnReserve(
                physicalPageId,
                slot.SpaceId,
                slot.VirtualPageIndex,
                slot.VirtualPageMip,
                slot.Generation,
                frameIndex,
                slot.PendingUpload,
                slot.Locked,
                slot.RequestDebugInfo);
        }

        private void RecordPageResidentCommit(
            int physicalPageId,
            in PhysicalPageSlotState slot,
            int allocationFrameIndex,
            int commitFrameIndex,
            bool wasPendingUpload,
            bool wasResident)
        {
            int resolvedCommitFrameIndex = commitFrameIndex >= 0
                ? commitFrameIndex
                : allocationFrameIndex;
            m_DebugTimeline.OnResidentCommit(
                physicalPageId,
                slot.SpaceId,
                slot.VirtualPageIndex,
                slot.VirtualPageMip,
                slot.Generation,
                resolvedCommitFrameIndex,
                wasPendingUpload,
                wasResident,
                slot.Locked,
                slot.RequestDebugInfo);
        }

        private static string FormatRequestDebug(in VTPageRequestDebugInfo debugInfo)
        {
            string requestKind = debugInfo.RequestKind switch
            {
                VTPageRequestKind.Bootstrap => "bootstrap",
                VTPageRequestKind.Locked => "locked",
                VTPageRequestKind.Demand => "demand",
                VTPageRequestKind.Refinement => "refinement",
                VTPageRequestKind.Neighbor => "neighbor",
                _ => "unknown",
            };
            return $"requestKind={requestKind} sourceCoord={debugInfo.SourceCoord} "
                   + $"effectiveCoord={debugInfo.EffectiveCoord} mipGap={debugInfo.MipGap} "
                   + $"weightedScore={debugInfo.WeightedScore}";
        }

        private void LogPageResidentAttach(
            int physicalPageId,
            int spaceId,
            int pageIndex,
            int mip,
            int generation,
            int frameIndex,
            bool locked)
        {
            m_DebugTimeline.OnResidentAttach(
                physicalPageId,
                spaceId,
                pageIndex,
                mip,
                generation,
                frameIndex,
                locked);
            VTDebugLog.Trace(
                $"[VividRP][VT_DEBUG][PageResidentAttach] pool={m_DebugName} frame={frameIndex} "
                + $"slot={physicalPageId} space={spaceId} pageIndex={pageIndex} mip={mip} "
                + $"generation={generation} locked={locked}");
        }

        private static string FormatProducer(VTProducerHandle producerHandle, string producerName)
        {
            string name = string.IsNullOrEmpty(producerName) ? "<unnamed>" : producerName;
            return $"{producerHandle}/{name}";
        }
#endif

        private void PromotePrimaryBinding(int physicalPageId)
        {
            List<PhysicalPageBinding> bindings = m_Bindings[physicalPageId];
            if (bindings.Count == 0)
                return;

            PhysicalPageBinding primary = bindings[0];
            bool locked = false;
            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
                locked |= bindings[bindingIndex].Locked;

            PhysicalPageSlotState slotState = m_Slots[physicalPageId];
            slotState.Owner = primary.Owner;
            slotState.SpaceId = primary.SpaceId;
            slotState.VirtualPageIndex = primary.VirtualPageIndex;
            slotState.Locked = locked;
            slotState.VisibilityPending = IsAnyBindingVisibilityPending(physicalPageId);
            m_Slots[physicalPageId] = slotState;
        }

        private void ClearPhysicalPage(int physicalPageId, bool releaseToFreeList)
        {
            PhysicalPageSlotState slotState = m_Slots[physicalPageId];
#if VT_DEBUG
            int diagnosticFrameIndex = m_DebugTimeline.CurrentFrame >= 0
                ? m_DebugTimeline.CurrentFrame
                : slotState.LastAllocationFrame;
            m_DebugTimeline.OnSlotReleased(
                physicalPageId,
                slotState.Generation,
                diagnosticFrameIndex,
                releaseToFreeList);
#endif
            RemovePhysicalPageLookup(physicalPageId, slotState.Identity);
            slotState.Owner = null;
            slotState.SpaceId = 0;
            slotState.VirtualPageIndex = -1;
            slotState.VirtualPageMip = 0;
            slotState.Generation = 0;
            slotState.LastAllocationFrame = -1;
            slotState.LastAsyncCommitFrame = -1;
            slotState.AffinityViewId = VirtualTextureViewId.Invalid;
            slotState.LastAffinityFrame = -1;
            slotState.Identity = default;
            slotState.Resident = false;
            slotState.PendingUpload = false;
            slotState.Locked = false;
            slotState.VisibilityPending = false;
#if VT_DEBUG
            slotState.RequestDebugInfo = default;
#endif
            m_Slots[physicalPageId] = slotState;
            m_LastLruTouchFrames[physicalPageId] = int.MinValue;
            m_Bindings[physicalPageId].Clear();

            if (!releaseToFreeList)
                return;

            LinkedListNode<int> node = m_LruNodes[physicalPageId];
            if (node?.List != null)
                m_LruPhysicalPages.Remove(node);

            m_FreePhysicalPages.Push(physicalPageId);
        }

        private static long GetTextureByteCount(GraphicsFormat format, int width, int height)
        {
            long blockWidth = Math.Max(1u, GraphicsFormatUtility.GetBlockWidth(format));
            long blockHeight = Math.Max(1u, GraphicsFormatUtility.GetBlockHeight(format));
            long blockSize = Math.Max(1u, GraphicsFormatUtility.GetBlockSize(format));
            long blocksX = (Math.Max(1, width) + blockWidth - 1) / blockWidth;
            long blocksY = (Math.Max(1, height) + blockHeight - 1) / blockHeight;
            return checked(blocksX * blocksY * blockSize);
        }

        private void AddPhysicalPageLookup(int physicalPageId, in VTPhysicalPageIdentity identity)
        {
            m_NextPhysicalPageWithSameIdentity[physicalPageId] = -1;
            if (!m_PhysicalPageLookup.TryGetValue(identity, out int firstPhysicalPageId))
            {
                m_PhysicalPageLookup.Add(identity, physicalPageId);
                return;
            }

            if (physicalPageId < firstPhysicalPageId)
            {
                m_NextPhysicalPageWithSameIdentity[physicalPageId] = firstPhysicalPageId;
                m_PhysicalPageLookup[identity] = physicalPageId;
                return;
            }

            int previousPhysicalPageId = firstPhysicalPageId;
            int nextPhysicalPageId = m_NextPhysicalPageWithSameIdentity[previousPhysicalPageId];
            while (nextPhysicalPageId >= 0 && nextPhysicalPageId < physicalPageId)
            {
                previousPhysicalPageId = nextPhysicalPageId;
                nextPhysicalPageId = m_NextPhysicalPageWithSameIdentity[previousPhysicalPageId];
            }

            m_NextPhysicalPageWithSameIdentity[physicalPageId] = nextPhysicalPageId;
            m_NextPhysicalPageWithSameIdentity[previousPhysicalPageId] = physicalPageId;
        }

        private void RemovePhysicalPageLookup(int physicalPageId, in VTPhysicalPageIdentity identity)
        {
            if (!m_PhysicalPageLookup.TryGetValue(identity, out int firstPhysicalPageId))
                return;

            int nextPhysicalPageId = m_NextPhysicalPageWithSameIdentity[physicalPageId];
            if (firstPhysicalPageId == physicalPageId)
            {
                if (nextPhysicalPageId >= 0)
                    m_PhysicalPageLookup[identity] = nextPhysicalPageId;
                else
                    m_PhysicalPageLookup.Remove(identity);

                m_NextPhysicalPageWithSameIdentity[physicalPageId] = -1;
                return;
            }

            int previousPhysicalPageId = firstPhysicalPageId;
            while (previousPhysicalPageId >= 0)
            {
                if (m_NextPhysicalPageWithSameIdentity[previousPhysicalPageId] == physicalPageId)
                {
                    m_NextPhysicalPageWithSameIdentity[previousPhysicalPageId] = nextPhysicalPageId;
                    break;
                }

                previousPhysicalPageId = m_NextPhysicalPageWithSameIdentity[previousPhysicalPageId];
            }

            m_NextPhysicalPageWithSameIdentity[physicalPageId] = -1;
        }

        private int FindEvictionCandidate(int frameIndex, VirtualTextureViewId activeViewId)
        {
            int candidatePhysicalPageId = -1;
            int fallbackPhysicalPageId = -1;

            LinkedListNode<int> node = m_LruPhysicalPages.First;
            while (node != null)
            {
                int physicalPageId = node.Value;
                if (!CanEvict(physicalPageId, frameIndex))
                {
                    node = node.Next;
                    continue;
                }

                if (IsBetterEvictionCandidate(physicalPageId, fallbackPhysicalPageId))
                    fallbackPhysicalPageId = physicalPageId;

                if (IsProtectedByActiveViewAffinity(physicalPageId, activeViewId))
                {
                    node = node.Next;
                    continue;
                }

                if (IsBetterEvictionCandidate(physicalPageId, candidatePhysicalPageId))
                    candidatePhysicalPageId = physicalPageId;

                node = node.Next;
            }

            return candidatePhysicalPageId >= 0 ? candidatePhysicalPageId : fallbackPhysicalPageId;
        }

        private bool IsBetterEvictionCandidate(int physicalPageId, int currentPhysicalPageId)
        {
            if (currentPhysicalPageId < 0)
                return true;

            // Match the runtime VT age key: age is authoritative and mip only
            // differentiates pages observed in the same frame.
            int lastTouchFrame = m_LastLruTouchFrames[physicalPageId];
            int currentLastTouchFrame = m_LastLruTouchFrames[currentPhysicalPageId];
            if (lastTouchFrame != currentLastTouchFrame)
                return lastTouchFrame < currentLastTouchFrame;

            int pageMip = m_Slots[physicalPageId].VirtualPageMip;
            int currentPageMip = m_Slots[currentPhysicalPageId].VirtualPageMip;
            if (pageMip != currentPageMip)
                return pageMip < currentPageMip;

            return physicalPageId < currentPhysicalPageId;
        }

        private bool CanEvict(int physicalPageId, int frameIndex)
        {
            if (physicalPageId < 0 || physicalPageId >= m_Slots.Length)
                return false;

            PhysicalPageSlotState slotState = m_Slots[physicalPageId];
            int lastTouchFrame = m_LastLruTouchFrames[physicalPageId];
            bool outsideFeedbackProtectionWindow = lastTouchFrame == int.MinValue
                                                   || frameIndex < lastTouchFrame
                                                   || frameIndex - lastTouchFrame
                                                   >= FeedbackEvictionProtectionFrames;
            return IsOccupied(slotState)
                   && slotState.LastAllocationFrame != frameIndex
                   && outsideFeedbackProtectionWindow
                   && (slotState.LastAsyncCommitFrame < 0
                       || frameIndex - slotState.LastAsyncCommitFrame
                       >= AsyncCommitEvictionProtectionFrames)
                   && !slotState.PendingUpload
                   && !slotState.VisibilityPending
                   && !slotState.Locked;
        }

        private bool IsProtectedByActiveViewAffinity(
            int physicalPageId,
            VirtualTextureViewId activeViewId)
        {
            if ((!activeViewId.IsValid && !activeViewId.IsCameraTypeOnly)
                || physicalPageId < 0
                || physicalPageId >= m_Slots.Length)
            {
                return false;
            }

            PhysicalPageSlotState slotState = m_Slots[physicalPageId];
            if (slotState.LastAffinityFrame < 0)
                return false;

            return activeViewId.IsValid
                ? slotState.AffinityViewId.Equals(activeViewId)
                : slotState.AffinityViewId.CameraType == activeViewId.CameraType;
        }

        private static bool IsOccupied(in PhysicalPageSlotState slotState)
        {
            return slotState.Owner != null && slotState.VirtualPageIndex >= 0;
        }

        private static bool HasViewAffinity(VirtualTextureViewId viewId)
        {
            return viewId.IsValid || viewId.IsCameraTypeOnly;
        }

        private static bool IsSameProducer(
            in VTPhysicalPageIdentity identity,
            VTProducerHandle producerHandle,
            string producerName)
        {
            if (producerHandle.IsValid
                && identity.ProducerHandle.IsValid
                && identity.ProducerHandle.Equals(producerHandle))
            {
                return true;
            }

            if (string.IsNullOrEmpty(identity.ProducerName) || string.IsNullOrEmpty(producerName))
                return false;

            return string.Equals(identity.ProducerName, producerName, StringComparison.Ordinal);
        }
    }
}
