using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven.VirtualTexture
{
    internal sealed class GPUDrivenVirtualTextureProducer :
        IVTPageProducer,
        IVTPrioritizedPageProducer,
        IVTPageRequestRetirement,
        IDisposable
    {
        internal const int BaseColorLayerIndex = 0;
        internal const int NormalLayerIndex = 1;
        internal const int MaskLayerIndex = 2;
        internal const int ScalarMaskLayerIndex = 3;
        internal const int LayerCount = 3;

        private const int ThreadGroupSize = 8;

        private static readonly int s_BaseColorTextureId = Shader.PropertyToID("_BaseColorTexture");
        private static readonly int s_NormalTextureId = Shader.PropertyToID("_NormalTexture");
        private static readonly int s_MaskTextureId = Shader.PropertyToID("_MaskTexture");
        private static readonly int s_OutputPagesId = Shader.PropertyToID("_OutputPages");
        private static readonly int s_PageCoordId = Shader.PropertyToID("_VTPageCoord");
        private static readonly int s_EntryPageRegionId = Shader.PropertyToID("_VTEntryPageRegion");
        private static readonly int s_PageLayoutId = Shader.PropertyToID("_VTPageLayout");
        private static readonly int s_SourceMipOffsetsId = Shader.PropertyToID("_VTSourceMipOffsets");
        private static readonly int s_BaseColorFallbackId = Shader.PropertyToID("_VTBaseColorFallback");
        private static readonly int s_NormalFallbackId = Shader.PropertyToID("_VTNormalFallback");
        private static readonly int s_MaskFallbackId = Shader.PropertyToID("_VTMaskFallback");

        private sealed class AtlasEntry : IDisposable
        {
            internal AtlasEntry(
                RectInt pageRegion,
                int maxMip,
                Texture2D baseColor,
                Texture2D normal,
                Texture2D mask,
                int pageSize,
                bool repeat)
            {
                PageRegion = pageRegion;
                MaxMip = maxMip;
                Repeat = repeat;
                Sources = new[] { baseColor, normal, mask };
                SourceMipOffsets = new[]
                {
                    ComputeSourceMipOffset(baseColor, pageRegion.width, pageRegion.height, pageSize),
                    ComputeSourceMipOffset(normal, pageRegion.width, pageRegion.height, pageSize),
                    ComputeSourceMipOffset(mask, pageRegion.width, pageRegion.height, pageSize),
                };
                PresenceMask = (baseColor != null ? 1 : 0)
                               | (normal != null ? 2 : 0)
                               | (mask != null ? 4 : 0);
            }

            internal AtlasEntry(
                RectInt pageRegion,
                VividVirtualTextureAsset asset,
                in VirtualTextureSpaceDesc localDesc)
            {
                PageRegion = pageRegion;
                MaxMip = asset.MipCount - 1;
                Repeat = true;
                Sources = Array.Empty<Texture2D>();
                SourceMipOffsets = Array.Empty<int>();
                PresenceMask = asset.ContentLayerMask;
                LocalDesc = localDesc;
                StreamedProducer = new VividVirtualTextureAssetProducer(asset);
            }

            internal RectInt PageRegion { get; }

            internal int MaxMip { get; }

            internal bool Repeat { get; }

            internal Texture2D[] Sources { get; }

            internal int[] SourceMipOffsets { get; }

            internal int PresenceMask { get; }

            internal bool IsStreamed => StreamedProducer != null;

            internal VirtualTextureSpaceDesc LocalDesc { get; }

            internal VividVirtualTextureAssetProducer StreamedProducer { get; }

            public void Dispose()
            {
                StreamedProducer?.Dispose();
            }
        }

        private sealed class Finalizer : IVTGpuPageFinalizer
        {
            private readonly GPUDrivenVirtualTextureProducer m_Producer;
            private readonly AtlasEntry m_Entry;
            private readonly VirtualTextureSpaceDesc m_Desc;
            private readonly VTRequest m_Request;

            internal Finalizer(
                GPUDrivenVirtualTextureProducer producer,
                AtlasEntry entry,
                in VirtualTextureSpaceDesc desc,
                in VTRequest request)
            {
                m_Producer = producer;
                m_Entry = entry;
                m_Desc = desc;
                m_Request = request;
            }

            public int LayerCount => GPUDrivenVirtualTextureProducer.LayerCount;

            public void RecordGpuUpload(CommandBuffer cmd, RenderTexture stagingTexture, int baseSlice)
            {
                m_Producer.RecordPageUpload(cmd, stagingTexture, baseSlice, m_Entry, m_Desc, m_Request);
            }

            public void Dispose()
            {
            }
        }

        private readonly List<AtlasEntry> m_Entries = new();
        private readonly AtlasEntry[,] m_EntriesByBasePage;
        private readonly ComputeShader m_ComputeShader;
        private readonly int m_Kernel;
        private readonly List<VTRequest> m_RetirementScratch = new();
        private bool m_IsDisposed;

        internal GPUDrivenVirtualTextureProducer(
            string name,
            in VirtualTextureSpaceDesc desc,
            ComputeShader computeShader)
        {
            Name = !string.IsNullOrWhiteSpace(name)
                ? name
                : throw new ArgumentException("Producer name must be non-empty.", nameof(name));
            m_ComputeShader = computeShader != null
                ? computeShader
                : throw new ArgumentNullException(nameof(computeShader));
            if (!m_ComputeShader.HasKernel("CS"))
                throw new ArgumentException("GPUDriven VT page producer compute shader is missing the 'CS' kernel.", nameof(computeShader));

            m_Kernel = m_ComputeShader.FindKernel("CS");
            ProducerDesc = VTProducerDesc.FromSpaceDesc(Name, desc);
            m_EntriesByBasePage = new AtlasEntry[desc.VirtualPageCountX, desc.VirtualPageCountY];
        }

        public string Name { get; }

        public VTProducerDesc ProducerDesc { get; }

        internal int EntryCount => m_Entries.Count;

        internal int StreamedEntryCount
        {
            get
            {
                int count = 0;
                for (int entryIndex = 0; entryIndex < m_Entries.Count; entryIndex++)
                {
                    if (m_Entries[entryIndex].IsStreamed)
                        count += 1;
                }

                return count;
            }
        }

        internal int PendingStreamTaskCount
        {
            get
            {
                int count = 0;
                for (int entryIndex = 0; entryIndex < m_Entries.Count; entryIndex++)
                {
                    VividVirtualTextureAssetProducer producer = m_Entries[entryIndex].StreamedProducer;
                    if (producer != null)
                        count += producer.PendingStreamTaskCountForTesting;
                }

                return count;
            }
        }

        internal bool HasPermanentStreamFailure(RectInt pageRegion)
        {
            if (pageRegion.width <= 0
                || pageRegion.xMin < 0
                || pageRegion.yMin < 0
                || pageRegion.xMax > m_EntriesByBasePage.GetLength(0)
                || pageRegion.yMax > m_EntriesByBasePage.GetLength(1))
            {
                return false;
            }

            AtlasEntry entry = m_EntriesByBasePage[pageRegion.xMin, pageRegion.yMin];
            return entry != null
                   && entry.PageRegion.Equals(pageRegion)
                   && entry.StreamedProducer?.HasPermanentFailure == true;
        }

        internal void RegisterEntry(
            RectInt pageRegion,
            int maxMip,
            Texture2D baseColor,
            Texture2D normal,
            Texture2D mask,
            int pageSize,
            bool repeat)
        {
            ValidatePageRegion(pageRegion, maxMip);
            ValidateAndReservePageRegion(pageRegion);

            var entry = new AtlasEntry(
                pageRegion,
                maxMip,
                baseColor,
                normal,
                mask,
                pageSize,
                repeat);
            StoreEntry(entry);
        }

        internal void RegisterStreamedEntry(RectInt pageRegion, VividVirtualTextureAsset asset)
        {
            if (asset == null)
                throw new ArgumentNullException(nameof(asset));
            if (asset.VirtualPageCountX != pageRegion.width || asset.VirtualPageCountY != pageRegion.height)
                throw new ArgumentException("The streamed VT asset dimensions must match its atlas page region.", nameof(asset));

            int maxMip = asset.MipCount - 1;
            ValidatePageRegion(pageRegion, maxMip);

            ValidateAndReservePageRegion(pageRegion);
            VirtualTextureSpaceDesc localDesc = asset.CreateSpaceDesc(
                $"{Name}.{asset.name}",
                cachePageCount: 2,
                maxUploadsPerFrame: 1,
                feedbackCapacity: 16);
            var entry = new AtlasEntry(pageRegion, asset, localDesc);
            StoreEntry(entry);
        }

        internal bool UnregisterEntry(RectInt pageRegion)
        {
            if (pageRegion.width <= 0
                || pageRegion.xMin < 0
                || pageRegion.yMin < 0
                || pageRegion.xMax > m_EntriesByBasePage.GetLength(0)
                || pageRegion.yMax > m_EntriesByBasePage.GetLength(1))
            {
                return false;
            }

            AtlasEntry entry = m_EntriesByBasePage[pageRegion.xMin, pageRegion.yMin];
            if (entry == null || !entry.PageRegion.Equals(pageRegion))
                return false;

            for (int pageY = pageRegion.yMin; pageY < pageRegion.yMax; pageY++)
            {
                for (int pageX = pageRegion.xMin; pageX < pageRegion.xMax; pageX++)
                {
                    if (ReferenceEquals(m_EntriesByBasePage[pageX, pageY], entry))
                        m_EntriesByBasePage[pageX, pageY] = null;
                }
            }

            bool removed = m_Entries.Remove(entry);
            if (removed)
                entry.Dispose();
            return removed;
        }

        public VTPageRequestStatus RequestPageData(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request)
        {
            VTRequestPriorityKey priorityKey = VTRequestPriorityKey.FromRequest(
                request,
                locked: false,
                producerPriority: ProducerDesc.ProducerPriority);
            return RequestPageData(desc, request, priorityKey);
        }

        public VTPageRequestStatus RequestPageData(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request,
            in VTRequestPriorityKey priorityKey)
        {
            if (!VirtualTextureSpaceUtility.IsCoordValid(desc, request.PageCoord))
                return VTPageRequestStatus.Invalid;

            AtlasEntry entry = FindEntry(request.PageCoord);
            if (entry == null)
                return VTPageRequestStatus.Invalid;

            if (!entry.IsStreamed)
                return VTPageRequestStatus.Available;

            VTRequest localRequest = TranslateRequest(entry, request);
            VTRequestPriorityKey localPriorityKey = VTRequestPriorityKey.FromRequest(
                localRequest,
                priorityKey.Locked,
                entry.StreamedProducer.ProducerDesc.ProducerPriority);
            localPriorityKey = VTRequestPriorityUtility.SelectHigher(
                priorityKey,
                localPriorityKey);
            return entry.StreamedProducer.RequestPageData(
                entry.LocalDesc,
                localRequest,
                localPriorityKey);
        }

        public IVTPageUploadFinalizer ProducePageData(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request)
        {
            if (!VirtualTextureSpaceUtility.IsCoordValid(desc, request.PageCoord))
                return null;

            AtlasEntry entry = FindEntry(request.PageCoord);
            if (entry == null)
                return null;

            if (!entry.IsStreamed)
                return new Finalizer(this, entry, desc, request);

            VTRequest localRequest = TranslateRequest(entry, request);
            return entry.StreamedProducer.ProducePageData(entry.LocalDesc, localRequest);
        }

        public void GatherTasks(List<IVTPageProducerTask> tasks)
        {
            for (int entryIndex = 0; entryIndex < m_Entries.Count; entryIndex++)
                m_Entries[entryIndex].StreamedProducer?.GatherTasks(tasks);
        }

        public void CancelRequest(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request)
        {
            AtlasEntry entry = FindEntry(request.PageCoord);
            if (entry == null || !entry.IsStreamed)
                return;

            VTRequest localRequest = TranslateRequest(entry, request);
            entry.StreamedProducer.CancelRequest(entry.LocalDesc, localRequest);
        }

        public void RetireRequests(IReadOnlyList<VTRequest> liveRequests)
        {
            for (int entryIndex = 0; entryIndex < m_Entries.Count; entryIndex++)
            {
                AtlasEntry entry = m_Entries[entryIndex];
                if (!entry.IsStreamed)
                    continue;

                m_RetirementScratch.Clear();
                if (liveRequests != null)
                {
                    for (int requestIndex = 0; requestIndex < liveRequests.Count; requestIndex++)
                    {
                        VTRequest request = liveRequests[requestIndex];
                        if (ReferenceEquals(FindEntry(request.PageCoord), entry))
                            m_RetirementScratch.Add(TranslateRequest(entry, request));
                    }
                }

                entry.StreamedProducer.RetireRequests(m_RetirementScratch);
            }

            m_RetirementScratch.Clear();
        }

        public void Dispose()
        {
            if (m_IsDisposed)
                return;

            for (int entryIndex = 0; entryIndex < m_Entries.Count; entryIndex++)
                m_Entries[entryIndex].Dispose();

            m_Entries.Clear();
            Array.Clear(m_EntriesByBasePage, 0, m_EntriesByBasePage.Length);
            m_RetirementScratch.Clear();
            m_IsDisposed = true;
        }

        internal static int ComputeSourceMipOffset(
            Texture2D texture,
            int pageCountX,
            int pageCountY,
            int pageSize)
        {
            if (texture == null)
                return 0;

            int virtualWidth = Mathf.Max(1, pageCountX * pageSize);
            int virtualHeight = Mathf.Max(1, pageCountY * pageSize);
            float ratioX = texture.width / (float) virtualWidth;
            float ratioY = texture.height / (float) virtualHeight;
            float ratio = Mathf.Max(ratioX, ratioY);
            return Mathf.RoundToInt(Mathf.Log(ratio, 2.0f));
        }

        private void RecordPageUpload(
            CommandBuffer cmd,
            RenderTexture stagingTexture,
            int baseSlice,
            AtlasEntry entry,
            in VirtualTextureSpaceDesc desc,
            in VTRequest request)
        {
            if (cmd == null)
                throw new ArgumentNullException(nameof(cmd));
            if (stagingTexture == null)
                throw new ArgumentNullException(nameof(stagingTexture));

            Texture2D baseColor = entry.Sources[BaseColorLayerIndex];
            Texture2D normal = entry.Sources[NormalLayerIndex];
            Texture2D mask = entry.Sources[MaskLayerIndex];
            int presenceMask = (baseColor != null ? 1 : 0)
                               | (normal != null ? 2 : 0)
                               | (mask != null ? 4 : 0);
            Texture fallbackTexture = Texture2D.whiteTexture;
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_Kernel,
                s_BaseColorTextureId,
                baseColor != null ? baseColor : fallbackTexture);
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_Kernel,
                s_NormalTextureId,
                normal != null ? normal : fallbackTexture);
            cmd.SetComputeTextureParam(
                m_ComputeShader,
                m_Kernel,
                s_MaskTextureId,
                mask != null ? mask : fallbackTexture);
            cmd.SetComputeTextureParam(m_ComputeShader, m_Kernel, s_OutputPagesId, stagingTexture);

            VirtualTexturePageCoord coord = request.PageCoord;
            cmd.SetComputeVectorParam(
                m_ComputeShader,
                s_PageCoordId,
                new Vector4(coord.X, coord.Y, coord.Mip, baseSlice));
            cmd.SetComputeVectorParam(
                m_ComputeShader,
                s_EntryPageRegionId,
                new Vector4(
                    entry.PageRegion.x,
                    entry.PageRegion.y,
                    entry.PageRegion.width,
                    entry.PageRegion.height));
            cmd.SetComputeVectorParam(
                m_ComputeShader,
                s_PageLayoutId,
                new Vector4(desc.PageSize, desc.BorderSize, desc.PhysicalPageSize, entry.Repeat ? 1.0f : 0.0f));
            cmd.SetComputeVectorParam(
                m_ComputeShader,
                s_SourceMipOffsetsId,
                new Vector4(
                    entry.SourceMipOffsets[BaseColorLayerIndex],
                    entry.SourceMipOffsets[NormalLayerIndex],
                    entry.SourceMipOffsets[MaskLayerIndex],
                    presenceMask));
            cmd.SetComputeVectorParam(
                m_ComputeShader,
                s_BaseColorFallbackId,
                ToVector(desc.StackDesc.GetLayer(BaseColorLayerIndex).FallbackColor));
            cmd.SetComputeVectorParam(
                m_ComputeShader,
                s_NormalFallbackId,
                ToVector(desc.StackDesc.GetLayer(NormalLayerIndex).FallbackColor));
            cmd.SetComputeVectorParam(
                m_ComputeShader,
                s_MaskFallbackId,
                ToVector(desc.StackDesc.GetLayer(MaskLayerIndex).FallbackColor));

            int groupCount = Mathf.CeilToInt(desc.PhysicalPageSize / (float)ThreadGroupSize);
            cmd.DispatchCompute(m_ComputeShader, m_Kernel, groupCount, groupCount, 1);
        }

        private AtlasEntry FindEntry(in VirtualTexturePageCoord coord)
        {
            int basePageX = coord.X << coord.Mip;
            int basePageY = coord.Y << coord.Mip;
            if (basePageX < 0
                || basePageY < 0
                || basePageX >= m_EntriesByBasePage.GetLength(0)
                || basePageY >= m_EntriesByBasePage.GetLength(1))
            {
                return null;
            }

            AtlasEntry entry = m_EntriesByBasePage[basePageX, basePageY];
            if (entry == null || coord.Mip > entry.MaxMip)
                return null;

            int entryX = entry.PageRegion.x >> coord.Mip;
            int entryY = entry.PageRegion.y >> coord.Mip;
            int entryWidth = Mathf.Max(1, entry.PageRegion.width >> coord.Mip);
            int entryHeight = Mathf.Max(1, entry.PageRegion.height >> coord.Mip);
            return coord.X >= entryX
                   && coord.Y >= entryY
                   && coord.X < entryX + entryWidth
                   && coord.Y < entryY + entryHeight
                ? entry
                : null;
        }

        private static void ValidatePageRegion(RectInt pageRegion, int maxMip)
        {
            if (pageRegion.width <= 0 || pageRegion.height <= 0)
                throw new ArgumentException("GPUDriven VT entries must be non-empty page regions.", nameof(pageRegion));
            if (!Mathf.IsPowerOfTwo(pageRegion.width) || !Mathf.IsPowerOfTwo(pageRegion.height))
                throw new ArgumentException("GPUDriven VT entry dimensions must be powers of two.", nameof(pageRegion));
            if (maxMip < 0 || maxMip != ComputeMaxMip(pageRegion.width, pageRegion.height))
                throw new ArgumentOutOfRangeException(nameof(maxMip));

            int alignment = 1 << maxMip;
            if ((pageRegion.x & (alignment - 1)) != 0 || (pageRegion.y & (alignment - 1)) != 0)
                throw new ArgumentException("GPUDriven VT entries must be aligned to their maximum mip.", nameof(pageRegion));
        }

        private static int ComputeMaxMip(int pageCountX, int pageCountY)
        {
            int pageCount = Mathf.Max(1, Mathf.Min(pageCountX, pageCountY));
            int maxMip = 0;
            while ((pageCount >>= 1) > 0)
                maxMip += 1;
            return maxMip;
        }

        private void ValidateAndReservePageRegion(RectInt pageRegion)
        {
            if (pageRegion.xMin < 0
                || pageRegion.yMin < 0
                || pageRegion.xMax > m_EntriesByBasePage.GetLength(0)
                || pageRegion.yMax > m_EntriesByBasePage.GetLength(1))
            {
                throw new ArgumentOutOfRangeException(nameof(pageRegion));
            }

            for (int pageY = pageRegion.yMin; pageY < pageRegion.yMax; pageY++)
            {
                for (int pageX = pageRegion.xMin; pageX < pageRegion.xMax; pageX++)
                {
                    if (m_EntriesByBasePage[pageX, pageY] != null)
                        throw new InvalidOperationException("GPUDriven VT atlas entries must not overlap.");
                }
            }
        }

        private void StoreEntry(AtlasEntry entry)
        {
            RectInt pageRegion = entry.PageRegion;
            for (int pageY = pageRegion.yMin; pageY < pageRegion.yMax; pageY++)
            {
                for (int pageX = pageRegion.xMin; pageX < pageRegion.xMax; pageX++)
                    m_EntriesByBasePage[pageX, pageY] = entry;
            }

            m_Entries.Add(entry);
        }

        private static VTRequest TranslateRequest(AtlasEntry entry, in VTRequest request)
        {
            int mip = request.PageCoord.Mip;
            var localCoord = new VirtualTexturePageCoord(
                request.PageCoord.X - (entry.PageRegion.x >> mip),
                request.PageCoord.Y - (entry.PageRegion.y >> mip),
                mip);
            return new VTRequest(
                request.SpaceId,
                localCoord,
                request.PhysicalPageId,
                request.Generation,
                request.Priority,
                request.RequestFrame,
                request.CameraPriority,
                request.IsActiveView);
        }

        private static Vector4 ToVector(Color32 color)
        {
            const float inverseByte = 1.0f / 255.0f;
            return new Vector4(
                color.r * inverseByte,
                color.g * inverseByte,
                color.b * inverseByte,
                color.a * inverseByte);
        }
    }
}
