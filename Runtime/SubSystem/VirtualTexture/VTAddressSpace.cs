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
        private readonly IVTRuntimePageProducer m_RuntimeProducer;
        private readonly VTUploadScheduler m_UploadScheduler;

        internal VTAddressSpace(int spaceId, in VirtualTextureSpaceDesc desc, VTProducer producer)
        {
            SpaceId = spaceId;
            Descriptor = desc;
            Producer = producer ?? VTNullProducer.Instance;
            m_MipOffsets = VirtualTextureSpaceUtility.BuildMipOffsets(desc.VirtualPageCountX, desc.VirtualPageCountY, desc.MipCount);
            TotalPageCount = VirtualTextureSpaceUtility.GetTotalPageCount(desc.VirtualPageCountX, desc.VirtualPageCountY, desc.MipCount);
            m_ShaderParams = new VirtualTextureSpaceShaderParams(spaceId, desc, TotalPageCount);
            m_ResidencyManager = new VTResidencyManager(desc.SpaceName, desc, TotalPageCount, m_MipOffsets);
            m_PageTableUpdater = new VTPageTableUpdater(desc.SpaceName, TotalPageCount);
            m_RuntimeProducer = VTRuntimeProducerUtility.Resolve(Producer);
            m_UploadScheduler = new VTUploadScheduler(desc.SpaceName, desc, m_ResidencyManager.PhysicalCache, m_RuntimeProducer);
            BootstrapLowestMip();
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

        internal int ProcessRequests(IReadOnlyList<VirtualTextureAggregatedFeedbackRequest> requests, int frameIndex, CommandBuffer cmd)
        {
            bool pageTableChanged = m_UploadScheduler.CommitCompletedUploads(request => TryCommitRequestInternal(request, rebuildPageTable: false));

            VTResidencyProcessResult result = m_ResidencyManager.ProcessRequests(
                Descriptor,
                m_MipOffsets,
                SpaceId,
                requests,
                frameIndex);

            pageTableChanged |= result.PageTableChanged;
            if (pageTableChanged)
                m_PageTableUpdater.Rebuild(Descriptor, m_MipOffsets, m_ResidencyManager);

            m_UploadScheduler.SchedulePendingUploads(PendingRequests, cmd);
            return result.EvictionCount;
        }

        internal bool TryCommitRequest(in VTRequest request)
        {
            return TryCommitRequestInternal(request, rebuildPageTable: true);
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
            ComputeBuffer feedbackRequests,
            ComputeBuffer feedbackCounter)
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
            m_UploadScheduler.Dispose();
            m_PageTableUpdater.Dispose();
            m_ResidencyManager.Dispose();
        }

        private void BootstrapLowestMip()
        {
            int lowestMip = Descriptor.MipCount - 1;
            int pageCountX = VirtualTextureSpaceUtility.GetPageCountX(Descriptor.VirtualPageCountX, lowestMip);
            int pageCountY = VirtualTextureSpaceUtility.GetPageCountY(Descriptor.VirtualPageCountY, lowestMip);
            IVTRuntimePageProducer bootstrapProducer = m_RuntimeProducer ?? VTProceduralPageProducer.Instance;
            Texture2DArray bootstrapTexture = VTPageUploadUtility.CreateStagingTexture(
                Descriptor.SpaceName,
                Descriptor.PhysicalPageSize,
                1,
                "Bootstrap");
            var scratchPixels = new Color32[Descriptor.PhysicalPageSize * Descriptor.PhysicalPageSize];

            try
            {
                for (int y = 0; y < pageCountY; y++)
                {
                    for (int x = 0; x < pageCountX; x++)
                    {
                        var coord = new VirtualTexturePageCoord(x, y, lowestMip);
                        if (!m_ResidencyManager.TryAllocateResidentPage(
                                Descriptor,
                                m_MipOffsets,
                                SpaceId,
                                coord,
                                frameIndex: 0,
                                locked: true,
                                out VTRequest request))
                        {
                            throw new InvalidOperationException(
                                $"[VividRP] Failed to seed VT lowest mip page {coord} for space '{Descriptor.SpaceName}'.");
                        }

                        VTPageUploadUtility.WritePageToStagingTexture(
                            bootstrapTexture,
                            0,
                            scratchPixels,
                            bootstrapProducer,
                            Descriptor,
                            request);
                        bootstrapTexture.Apply(false, false);
                        Graphics.CopyTexture(bootstrapTexture, 0, 0, m_ResidencyManager.PhysicalCache, request.PhysicalPageId, 0);
                    }
                }
            }
            finally
            {
                CoreUtils.Destroy(bootstrapTexture);
            }
        }

        private bool TryCommitRequestInternal(in VTRequest request, bool rebuildPageTable)
        {
            if (request.SpaceId != SpaceId)
                return false;

            if (!m_ResidencyManager.TryCommitRequest(Descriptor, m_MipOffsets, request))
                return false;

            if (rebuildPageTable)
                m_PageTableUpdater.Rebuild(Descriptor, m_MipOffsets, m_ResidencyManager);

            return true;
        }
    }
}
