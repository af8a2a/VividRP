using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class VTAddressSpace : IDisposable
    {
        private readonly int[] m_MipOffsets;
        private readonly VTResidencyManager m_ResidencyManager;
        private readonly VTPageTableUpdater m_PageTableUpdater;
        private readonly VirtualTextureSpaceShaderParams m_ShaderParams;

        internal VTAddressSpace(int spaceId, in VirtualTextureSpaceDesc desc, VTProducer producer)
        {
            SpaceId = spaceId;
            Descriptor = desc;
            Producer = producer ?? VTNullProducer.Instance;
            m_MipOffsets = VirtualTextureSpaceUtility.BuildMipOffsets(desc.VirtualPageCountX, desc.VirtualPageCountY, desc.MipCount);
            TotalPageCount = VirtualTextureSpaceUtility.GetTotalPageCount(desc.VirtualPageCountX, desc.VirtualPageCountY, desc.MipCount);
            m_ShaderParams = new VirtualTextureSpaceShaderParams(spaceId, desc, TotalPageCount);
            m_ResidencyManager = new VTResidencyManager(desc.SpaceName, desc, TotalPageCount);
            m_PageTableUpdater = new VTPageTableUpdater(desc.SpaceName, TotalPageCount);
            m_PageTableUpdater.Rebuild(desc, m_MipOffsets, m_ResidencyManager);
            m_PageTableUpdater.RefreshBuffer();
        }

        internal int SpaceId { get; }

        internal VTProducer Producer { get; }

        internal VirtualTextureSpaceDesc Descriptor { get; }

        internal VTStackDesc StackDesc => Descriptor.StackDesc;

        internal int TotalPageCount { get; }

        internal int ResidentPageCount => m_ResidencyManager.ResidentPageCount;

        internal int FreePageCount => m_ResidencyManager.FreePageCount;

        internal int PendingRequestCount => m_ResidencyManager.PendingRequestCount;

        internal IReadOnlyList<VTRequest> PendingRequests => m_ResidencyManager.PendingRequests;

        internal int[] MipOffsets => m_MipOffsets;

        internal int ProcessRequests(IReadOnlyList<VirtualTextureAggregatedFeedbackRequest> requests, int frameIndex)
        {
            VTResidencyProcessResult result = m_ResidencyManager.ProcessRequests(
                Descriptor,
                m_MipOffsets,
                SpaceId,
                requests,
                frameIndex);

            if (result.PageTableChanged)
                m_PageTableUpdater.Rebuild(Descriptor, m_MipOffsets, m_ResidencyManager);

            return result.EvictionCount;
        }

        internal bool TryCommitRequest(in VTRequest request)
        {
            if (request.SpaceId != SpaceId)
                return false;

            if (!m_ResidencyManager.TryCommitRequest(Descriptor, m_MipOffsets, request))
                return false;

            m_PageTableUpdater.Rebuild(Descriptor, m_MipOffsets, m_ResidencyManager);
            return true;
        }

        internal bool TrySetPageLocked(in VirtualTexturePageCoord coord, bool locked)
        {
            if (!m_ResidencyManager.TrySetPageLocked(Descriptor, m_MipOffsets, coord, locked))
                return false;

            m_PageTableUpdater.Rebuild(Descriptor, m_MipOffsets, m_ResidencyManager);
            return true;
        }

        internal bool TryGetPageTableEntry(in VirtualTexturePageCoord coord, out VirtualTexturePageTableEntry entry)
        {
            return m_PageTableUpdater.TryGetEntry(Descriptor, m_MipOffsets, coord, out entry);
        }

        internal void RefreshPageTableBuffer()
        {
            m_PageTableUpdater.RefreshBuffer();
        }

        internal VirtualTextureSpaceBinding CreateBinding(
            GraphicsBuffer feedbackRequests,
            GraphicsBuffer feedbackCounter)
        {
            return new VirtualTextureSpaceBinding(
                SpaceId,
                Descriptor.SpaceName,
                m_PageTableUpdater.PageTableBuffer,
                m_ResidencyManager.PhysicalCache,
                feedbackRequests,
                feedbackCounter,
                m_ShaderParams,
                m_MipOffsets);
        }

        public void Dispose()
        {
            m_PageTableUpdater.Dispose();
            m_ResidencyManager.Dispose();
        }
    }
}
