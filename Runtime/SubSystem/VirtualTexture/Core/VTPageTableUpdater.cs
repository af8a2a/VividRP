using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class VTPageTableUpdater : IDisposable
    {
        private readonly int[] m_BestPhysicalPageIds;
        private readonly int[] m_BestResolvedMips;
        private readonly VirtualTexturePageTableEntry[] m_PageTableEntries;
        private readonly bool[] m_RecomputeMask;
        private readonly bool[] m_UploadMask;
        private readonly List<int> m_RecomputeIndices = new();
        private readonly List<int> m_UploadIndices = new();
        private readonly GraphicsBuffer m_PageTableBuffer;

        private bool m_HasBuiltPageTable;
        private bool m_FullBufferUploadRequired;
        private bool m_PageTableDirty;
        private int m_RebuildCount;
        private int m_LastRecomputedEntryCount;
        private int m_LastUploadedEntryCount;
        private int m_SparseUploadCount;
        private int m_FullUploadCount;

        internal VTPageTableUpdater(string spaceName, int totalPageCount)
        {
            ValidateBufferCapacity(totalPageCount);
            m_BestPhysicalPageIds = new int[totalPageCount];
            Array.Fill(m_BestPhysicalPageIds, -1);
            m_BestResolvedMips = new int[totalPageCount];
            m_PageTableEntries = new VirtualTexturePageTableEntry[totalPageCount];
            m_RecomputeMask = new bool[totalPageCount];
            m_UploadMask = new bool[totalPageCount];
            for (int pageIndex = 0; pageIndex < m_PageTableEntries.Length; pageIndex++)
                m_PageTableEntries[pageIndex] = VirtualTexturePageTableEntry.Invalid();

            m_PageTableBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalPageCount, sizeof(uint))
            {
                name = $"VividVT_{spaceName}_PageTable"
            };
        }

        private static void ValidateBufferCapacity(int totalPageCount)
        {
            if (totalPageCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(totalPageCount));

            long requiredByteSize = (long)totalPageCount * VirtualTextureSpaceUtility.PageTableEntryStride;
            long maxGraphicsBufferSize = SystemInfo.maxGraphicsBufferSize;
            if (maxGraphicsBufferSize > 0 && requiredByteSize > maxGraphicsBufferSize)
            {
                throw new InvalidOperationException(
                    $"Virtual texture page table requires a {requiredByteSize}-byte graphics buffer, "
                    + $"but the active device supports at most {maxGraphicsBufferSize} bytes.");
            }
        }

        internal GraphicsBuffer PageTableBuffer => m_PageTableBuffer;

        internal int RebuildCount => m_RebuildCount;

        internal int LastRecomputedEntryCount => m_LastRecomputedEntryCount;

        internal int LastUploadedEntryCount => m_LastUploadedEntryCount;

        internal int SparseUploadCount => m_SparseUploadCount;

        internal int FullUploadCount => m_FullUploadCount;

        internal void Rebuild(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            VTResidencyManager residencyManager)
        {
            // Some mutation paths rebuild immediately while the frame-end path consumes the
            // flag first. Consuming here as well prevents an immediate rebuild from leaving a
            // stale flag that would schedule a second no-op pass at frame end.
            residencyManager.ConsumePageTableDirtyFlag();
            m_RebuildCount += 1;
            m_LastRecomputedEntryCount = 0;

            if (!m_HasBuiltPageTable)
            {
                for (int pageIndex = m_PageTableEntries.Length - 1; pageIndex >= 0; pageIndex--)
                    RecomputeEntry(desc, mipOffsets, residencyManager, pageIndex);

                m_HasBuiltPageTable = true;
                m_FullBufferUploadRequired = true;
                m_PageTableDirty = true;
                m_LastRecomputedEntryCount = m_PageTableEntries.Length;
                return;
            }

            IReadOnlyList<int> dirtyPageUpdates = residencyManager.DirtyPageTableUpdates;
            for (int dirtyIndex = 0; dirtyIndex < dirtyPageUpdates.Count; dirtyIndex++)
                MarkDirtySubtree(desc, mipOffsets, dirtyPageUpdates[dirtyIndex]);

            if (m_RecomputeIndices.Count == 0)
                return;

            // Mip offsets are laid out from the finest mip to the coarsest mip. Descending
            // flat indices therefore guarantee that a fallback parent is updated first.
            m_RecomputeIndices.Sort(static (left, right) => right.CompareTo(left));
            for (int dirtyIndex = 0; dirtyIndex < m_RecomputeIndices.Count; dirtyIndex++)
            {
                int pageIndex = m_RecomputeIndices[dirtyIndex];
                uint previousValue = m_PageTableEntries[pageIndex].PackedValue;
                RecomputeEntry(desc, mipOffsets, residencyManager, pageIndex);
                if (m_PageTableEntries[pageIndex].PackedValue != previousValue)
                {
                    MarkUploadDirty(pageIndex);
                }

                m_RecomputeMask[pageIndex] = false;
            }

            m_LastRecomputedEntryCount = m_RecomputeIndices.Count;
            m_RecomputeIndices.Clear();
            m_PageTableDirty |= m_UploadIndices.Count > 0;
        }

        internal bool TryGetEntry(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            in VirtualTexturePageCoord coord,
            out VirtualTexturePageTableEntry entry)
        {
            if (!VirtualTextureSpaceUtility.IsCoordValid(desc, coord))
            {
                entry = default;
                return false;
            }

            int pageIndex = VirtualTextureSpaceUtility.GetFlatIndex(desc, mipOffsets, coord);
            entry = m_PageTableEntries[pageIndex];
            return true;
        }

        internal void RefreshBuffer()
        {
            if (!m_PageTableDirty)
                return;

            m_LastUploadedEntryCount = m_UploadIndices.Count;
            int fullUploadThreshold = m_PageTableEntries.Length / 2 + m_PageTableEntries.Length % 2;
            bool useFullUpload = m_FullBufferUploadRequired
                                 || m_UploadIndices.Count >= fullUploadThreshold;
            if (useFullUpload)
            {
                m_PageTableBuffer.SetData(m_PageTableEntries);
                m_LastUploadedEntryCount = m_PageTableEntries.Length;
                m_FullUploadCount += 1;
            }
            else
            {
                m_UploadIndices.Sort();
                int rangeStart = m_UploadIndices[0];
                int previousIndex = rangeStart;
                for (int dirtyIndex = 1; dirtyIndex <= m_UploadIndices.Count; dirtyIndex++)
                {
                    bool endOfList = dirtyIndex == m_UploadIndices.Count;
                    int pageIndex = endOfList ? -1 : m_UploadIndices[dirtyIndex];
                    if (!endOfList && pageIndex == previousIndex + 1)
                    {
                        previousIndex = pageIndex;
                        continue;
                    }

                    int rangeCount = previousIndex - rangeStart + 1;
                    m_PageTableBuffer.SetData(
                        m_PageTableEntries,
                        rangeStart,
                        rangeStart,
                        rangeCount);
                    if (!endOfList)
                    {
                        rangeStart = pageIndex;
                        previousIndex = pageIndex;
                    }
                }

                m_SparseUploadCount += 1;
            }

            for (int dirtyIndex = 0; dirtyIndex < m_UploadIndices.Count; dirtyIndex++)
                m_UploadMask[m_UploadIndices[dirtyIndex]] = false;
            m_UploadIndices.Clear();
            m_FullBufferUploadRequired = false;
            m_PageTableDirty = false;
        }

        private void MarkDirtySubtree(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            int pageIndex)
        {
            if (!TryGetPageCoord(desc, mipOffsets, pageIndex, out VirtualTexturePageCoord coord))
                return;

            for (int mip = coord.Mip; mip >= 0; mip--)
            {
                int mipDelta = coord.Mip - mip;
                int startX = coord.X << mipDelta;
                int startY = coord.Y << mipDelta;
                int endX = Mathf.Min(
                    VirtualTextureSpaceUtility.GetPageCountX(desc.VirtualPageCountX, mip),
                    (coord.X + 1) << mipDelta);
                int endY = Mathf.Min(
                    VirtualTextureSpaceUtility.GetPageCountY(desc.VirtualPageCountY, mip),
                    (coord.Y + 1) << mipDelta);
                int mipWidth = VirtualTextureSpaceUtility.GetPageCountX(desc.VirtualPageCountX, mip);
                int mipOffset = mipOffsets[mip];
                for (int y = startY; y < endY; y++)
                {
                    for (int x = startX; x < endX; x++)
                        MarkRecomputeDirty(mipOffset + y * mipWidth + x);
                }
            }
        }

        private void MarkRecomputeDirty(int pageIndex)
        {
            if (m_RecomputeMask[pageIndex])
                return;

            m_RecomputeMask[pageIndex] = true;
            m_RecomputeIndices.Add(pageIndex);
        }

        private void MarkUploadDirty(int pageIndex)
        {
            if (m_UploadMask[pageIndex])
                return;

            m_UploadMask[pageIndex] = true;
            m_UploadIndices.Add(pageIndex);
        }

        private void RecomputeEntry(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            VTResidencyManager residencyManager,
            int pageIndex)
        {
            if (!TryGetPageCoord(desc, mipOffsets, pageIndex, out VirtualTexturePageCoord coord))
                return;

            VTPageResidencyState pageState = residencyManager.GetPageState(pageIndex);
            bool hasBestMapping = false;
            int bestPhysicalPageId = -1;
            int bestResolvedMip = 0;
            VirtualTexturePageTableEntry entry;

            if (pageState.Resident)
            {
                hasBestMapping = true;
                bestPhysicalPageId = pageState.PhysicalPageId;
                bestResolvedMip = coord.Mip;
                entry = new VirtualTexturePageTableEntry(
                    bestPhysicalPageId,
                    bestResolvedMip,
                    true,
                    false,
                    false,
                    pageState.Locked);
            }
            else
            {
                if (coord.Mip < desc.MipCount - 1)
                {
                    int parentMip = coord.Mip + 1;
                    int parentWidth = VirtualTextureSpaceUtility.GetPageCountX(desc.VirtualPageCountX, parentMip);
                    int parentIndex = mipOffsets[parentMip]
                                      + (coord.Y >> 1) * parentWidth
                                      + (coord.X >> 1);
                    if (m_BestPhysicalPageIds[parentIndex] >= 0)
                    {
                        hasBestMapping = true;
                        bestPhysicalPageId = m_BestPhysicalPageIds[parentIndex];
                        bestResolvedMip = m_BestResolvedMips[parentIndex];
                    }
                }

                entry = hasBestMapping
                    ? new VirtualTexturePageTableEntry(
                        bestPhysicalPageId,
                        bestResolvedMip,
                        false,
                        true,
                        pageState.PendingUpload,
                        pageState.Locked)
                    : VirtualTexturePageTableEntry.Invalid(pageState.PendingUpload, pageState.Locked);
            }

            m_PageTableEntries[pageIndex] = entry;
            m_BestPhysicalPageIds[pageIndex] = hasBestMapping ? bestPhysicalPageId : -1;
            m_BestResolvedMips[pageIndex] = hasBestMapping ? bestResolvedMip : 0;
        }

        private static bool TryGetPageCoord(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            int pageIndex,
            out VirtualTexturePageCoord coord)
        {
            if (pageIndex < 0)
            {
                coord = default;
                return false;
            }

            for (int mip = desc.MipCount - 1; mip >= 0; mip--)
            {
                int mipOffset = mipOffsets[mip];
                if (pageIndex < mipOffset)
                    continue;

                int mipWidth = VirtualTextureSpaceUtility.GetPageCountX(desc.VirtualPageCountX, mip);
                int mipHeight = VirtualTextureSpaceUtility.GetPageCountY(desc.VirtualPageCountY, mip);
                int localIndex = pageIndex - mipOffset;
                if (localIndex >= mipWidth * mipHeight)
                    continue;

                coord = new VirtualTexturePageCoord(
                    localIndex % mipWidth,
                    localIndex / mipWidth,
                    mip);
                return true;
            }

            coord = default;
            return false;
        }

        public void Dispose()
        {
            m_PageTableBuffer?.Dispose();
        }
    }
}
