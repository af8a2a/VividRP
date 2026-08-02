using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven.VirtualTexture
{
    internal sealed class GPUDrivenVirtualTextureProducer : IVTPageProducer
    {
        internal const int BaseColorLayerIndex = 0;
        internal const int NormalLayerIndex = 1;
        internal const int MaskLayerIndex = 2;
        internal const int LayerCount = 3;

        private sealed class AtlasEntry
        {
            internal AtlasEntry(
                RectInt pageRegion,
                int maxMip,
                Texture2D baseColor,
                Texture2D normal,
                Texture2D mask,
                int pageSize)
            {
                PageRegion = pageRegion;
                MaxMip = maxMip;
                Sources = new[]
                {
                    CreateSource(baseColor),
                    CreateSource(normal),
                    CreateSource(mask),
                };
                SourceMipOffsets = new[]
                {
                    ComputeSourceMipOffset(baseColor, pageRegion.width, pageSize),
                    ComputeSourceMipOffset(normal, pageRegion.width, pageSize),
                    ComputeSourceMipOffset(mask, pageRegion.width, pageSize),
                };
            }

            internal RectInt PageRegion { get; }

            internal int MaxMip { get; }

            internal VTTexture2DPageProducer[] Sources { get; }

            internal int[] SourceMipOffsets { get; }

            private static VTTexture2DPageProducer CreateSource(Texture2D texture)
            {
                return texture != null ? new VTTexture2DPageProducer(texture) : null;
            }

            private static int ComputeSourceMipOffset(Texture2D texture, int pageCount, int pageSize)
            {
                if (texture == null)
                    return 0;

                int virtualDimension = Mathf.Max(1, pageCount * pageSize);
                int sourceDimension = Mathf.Max(1, Mathf.Max(texture.width, texture.height));
                float ratio = (float) sourceDimension / virtualDimension;
                return Mathf.RoundToInt(Mathf.Log(ratio, 2.0f));
            }
        }

        private sealed class Finalizer : IVTMultiLayerPageFinalizer
        {
            private readonly GPUDrivenVirtualTextureProducer m_Producer;
            private readonly VirtualTextureSpaceDesc m_Desc;
            private readonly VTRequest m_Request;

            internal Finalizer(
                GPUDrivenVirtualTextureProducer producer,
                in VirtualTextureSpaceDesc desc,
                in VTRequest request)
            {
                m_Producer = producer;
                m_Desc = desc;
                m_Request = request;
            }

            public int LayerCount => GPUDrivenVirtualTextureProducer.LayerCount;

            public void FinalizeRender(CommandBuffer cmd)
            {
            }

            public void FinalizeUpload(Texture2DArray stagingTexture, int slice, Color32[] scratchPixels)
            {
                FinalizeUploadLayer(stagingTexture, slice, BaseColorLayerIndex, scratchPixels);
            }

            public void FinalizeUploadLayer(
                Texture2DArray stagingTexture,
                int slice,
                int layerIndex,
                Color32[] scratchPixels)
            {
                if (stagingTexture == null)
                    throw new ArgumentNullException(nameof(stagingTexture));
                if (scratchPixels == null)
                    throw new ArgumentNullException(nameof(scratchPixels));

                m_Producer.WritePageLayer(m_Desc, m_Request, layerIndex, scratchPixels);
                stagingTexture.SetPixels32(scratchPixels, slice, 0);
            }

            public void Dispose()
            {
            }
        }

        private readonly List<AtlasEntry> m_Entries = new();
        private readonly AtlasEntry[,] m_EntriesByBasePage;

        internal GPUDrivenVirtualTextureProducer(string name, in VirtualTextureSpaceDesc desc)
        {
            Name = !string.IsNullOrWhiteSpace(name)
                ? name
                : throw new ArgumentException("Producer name must be non-empty.", nameof(name));
            ProducerDesc = VTProducerDesc.FromSpaceDesc(Name, desc);
            m_EntriesByBasePage = new AtlasEntry[desc.VirtualPageCountX, desc.VirtualPageCountY];
        }

        public string Name { get; }

        public VTProducerDesc ProducerDesc { get; }

        internal int EntryCount => m_Entries.Count;

        internal void RegisterEntry(
            RectInt pageRegion,
            int maxMip,
            Texture2D baseColor,
            Texture2D normal,
            Texture2D mask,
            int pageSize)
        {
            if (pageRegion.width <= 0 || pageRegion.height != pageRegion.width)
                throw new ArgumentException("GPUDriven VT entries must be non-empty square page regions.", nameof(pageRegion));
            if (maxMip < 0)
                throw new ArgumentOutOfRangeException(nameof(maxMip));

            if (pageRegion.xMin < 0
                || pageRegion.yMin < 0
                || pageRegion.xMax > m_EntriesByBasePage.GetLength(0)
                || pageRegion.yMax > m_EntriesByBasePage.GetLength(1))
            {
                throw new ArgumentOutOfRangeException(nameof(pageRegion));
            }

            var entry = new AtlasEntry(
                pageRegion,
                maxMip,
                baseColor,
                normal,
                mask,
                pageSize);
            for (int pageY = pageRegion.yMin; pageY < pageRegion.yMax; pageY++)
            {
                for (int pageX = pageRegion.xMin; pageX < pageRegion.xMax; pageX++)
                {
                    if (m_EntriesByBasePage[pageX, pageY] != null)
                        throw new InvalidOperationException("GPUDriven VT atlas entries must not overlap.");
                }
            }

            for (int pageY = pageRegion.yMin; pageY < pageRegion.yMax; pageY++)
            {
                for (int pageX = pageRegion.xMin; pageX < pageRegion.xMax; pageX++)
                    m_EntriesByBasePage[pageX, pageY] = entry;
            }

            m_Entries.Add(entry);
        }

        public VTPageRequestStatus RequestPageData(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request)
        {
            return VirtualTextureSpaceUtility.IsCoordValid(desc, request.PageCoord)
                ? VTPageRequestStatus.Available
                : VTPageRequestStatus.Invalid;
        }

        public IVTPageFinalizer ProducePageData(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request)
        {
            if (!VirtualTextureSpaceUtility.IsCoordValid(desc, request.PageCoord))
                return null;

            return new Finalizer(this, desc, request);
        }

        public void GatherTasks(List<IVTPageProducerTask> tasks)
        {
        }

        public void CancelRequest(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request)
        {
        }

        private void WritePageLayer(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request,
            int layerIndex,
            Color32[] outputPixels)
        {
            if (layerIndex < 0 || layerIndex >= LayerCount)
                throw new ArgumentOutOfRangeException(nameof(layerIndex));

            int physicalPageSize = desc.PhysicalPageSize;
            int pixelCount = physicalPageSize * physicalPageSize;
            if (outputPixels.Length < pixelCount)
                throw new ArgumentException("Output pixel buffer is smaller than the physical VT page.", nameof(outputPixels));

            AtlasEntry entry = FindEntry(request.PageCoord);
            VTTexture2DPageProducer source = entry?.Sources[layerIndex];
            if (entry == null || source == null)
            {
                FillFallback(desc.StackDesc.GetLayer(layerIndex).FallbackColor, outputPixels, pixelCount);
                return;
            }

            VirtualTexturePageCoord coord = request.PageCoord;
            int entryPageXAtMip = entry.PageRegion.x >> coord.Mip;
            int entryPageYAtMip = entry.PageRegion.y >> coord.Mip;
            int entryPageCountAtMip = Mathf.Max(1, entry.PageRegion.width >> coord.Mip);
            int localPageX = coord.X - entryPageXAtMip;
            int localPageY = coord.Y - entryPageYAtMip;
            int logicalDimension = entryPageCountAtMip * desc.PageSize;
            int sourceMip = Mathf.Max(0, coord.Mip + entry.SourceMipOffsets[layerIndex]);

            for (int y = 0; y < physicalPageSize; y++)
            {
                int logicalY = localPageY * desc.PageSize + y - desc.BorderSize;
                float v = (logicalY + 0.5f) / logicalDimension;
                for (int x = 0; x < physicalPageSize; x++)
                {
                    int logicalX = localPageX * desc.PageSize + x - desc.BorderSize;
                    float u = (logicalX + 0.5f) / logicalDimension;
                    outputPixels[y * physicalPageSize + x] = source.SampleSource(sourceMip, u, v, true);
                }
            }
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
            return entry != null && coord.Mip <= entry.MaxMip ? entry : null;
        }

        private static void FillFallback(Color32 fallback, Color32[] outputPixels, int pixelCount)
        {
            for (int pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
                outputPixels[pixelIndex] = fallback;
        }
    }
}
