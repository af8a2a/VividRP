using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class VTPageTableUpdater : IDisposable
    {
        private readonly int[] m_BestPhysicalPageIds;
        private readonly int[] m_BestResolvedMips;
        private readonly VirtualTexturePageTableEntry[] m_PageTableEntries;
        private readonly GraphicsBuffer m_PageTableBuffer;

        private bool m_PageTableDirty;

        internal VTPageTableUpdater(string spaceName, int totalPageCount)
        {
            m_BestPhysicalPageIds = new int[totalPageCount];
            Array.Fill(m_BestPhysicalPageIds, -1);
            m_BestResolvedMips = new int[totalPageCount];
            m_PageTableEntries = new VirtualTexturePageTableEntry[totalPageCount];
            for (int pageIndex = 0; pageIndex < m_PageTableEntries.Length; pageIndex++)
                m_PageTableEntries[pageIndex] = VirtualTexturePageTableEntry.Invalid();

            m_PageTableBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalPageCount, sizeof(uint))
            {
                name = $"VividVT_{spaceName}_PageTable"
            };
        }

        internal GraphicsBuffer PageTableBuffer => m_PageTableBuffer;

        internal void Rebuild(
            in VirtualTextureSpaceDesc desc,
            int[] mipOffsets,
            VTResidencyManager residencyManager)
        {
            for (int mip = desc.MipCount - 1; mip >= 0; mip--)
            {
                int mipWidth = VirtualTextureSpaceUtility.GetPageCountX(desc.VirtualPageCountX, mip);
                int mipHeight = VirtualTextureSpaceUtility.GetPageCountY(desc.VirtualPageCountY, mip);
                int mipOffset = mipOffsets[mip];
                int parentWidth = mip < desc.MipCount - 1
                    ? VirtualTextureSpaceUtility.GetPageCountX(desc.VirtualPageCountX, mip + 1)
                    : 0;
                int parentOffset = mip < desc.MipCount - 1 ? mipOffsets[mip + 1] : 0;

                for (int y = 0; y < mipHeight; y++)
                {
                    for (int x = 0; x < mipWidth; x++)
                    {
                        int pageIndex = mipOffset + y * mipWidth + x;
                        VTPageResidencyState pageState = residencyManager.GetPageState(pageIndex);

                        bool hasBestMapping = false;
                        int bestPhysicalPageId = -1;
                        int bestResolvedMip = 0;
                        VirtualTexturePageTableEntry entry;

                        if (pageState.Resident)
                        {
                            hasBestMapping = true;
                            bestPhysicalPageId = pageState.PhysicalPageId;
                            bestResolvedMip = mip;
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
                            if (mip < desc.MipCount - 1)
                            {
                                int parentIndex = parentOffset + (y >> 1) * parentWidth + (x >> 1);
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
                }
            }

            m_PageTableDirty = true;
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

            m_PageTableBuffer.SetData(m_PageTableEntries);
            m_PageTableDirty = false;
        }

        public void Dispose()
        {
            m_PageTableBuffer?.Dispose();
        }
    }
}
