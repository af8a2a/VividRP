using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class VividVirtualTextureAssetProducer : IVTPageProducer
    {
        private sealed class Finalizer : IVTPageFinalizer
        {
            private readonly VividVirtualTextureTilePayload m_Payload;
            private readonly int m_ExpectedPixelCount;

            internal Finalizer(in VividVirtualTextureTilePayload payload, int expectedPixelCount)
            {
                m_Payload = payload;
                m_ExpectedPixelCount = expectedPixelCount;
            }

            public void FinalizeRender(CommandBuffer cmd)
            {
            }

            public void FinalizeUpload(Texture2DArray stagingTexture, int slice, Color32[] scratchPixels)
            {
                if (stagingTexture == null)
                    throw new ArgumentNullException(nameof(stagingTexture));
                if (scratchPixels == null)
                    throw new ArgumentNullException(nameof(scratchPixels));
                if (!m_Payload.IsValid)
                    throw new InvalidOperationException("[VividRP] Invalid virtual texture tile payload.");

                int pixelCount = Mathf.Min(m_ExpectedPixelCount, scratchPixels.Length);
                if (m_Payload.ByteSize < pixelCount * 4)
                    throw new InvalidOperationException("[VividRP] Virtual texture tile payload is smaller than the target page.");

                byte[] data = m_Payload.Data;
                int byteOffset = m_Payload.ByteOffset;
                for (int pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
                {
                    int sourceIndex = byteOffset + pixelIndex * 4;
                    scratchPixels[pixelIndex] = new Color32(
                        data[sourceIndex],
                        data[sourceIndex + 1],
                        data[sourceIndex + 2],
                        data[sourceIndex + 3]);
                }

                stagingTexture.SetPixels32(scratchPixels, slice, 0);
            }

            public void Dispose()
            {
            }
        }

        private readonly VividVirtualTextureAsset m_Asset;
        private readonly VividVirtualTextureBuiltData m_BuiltData;

        internal VividVirtualTextureAssetProducer(VividVirtualTextureAsset asset)
        {
            m_Asset = asset != null
                ? asset
                : throw new ArgumentNullException(nameof(asset));
            m_BuiltData = asset.BuiltData != null
                ? asset.BuiltData
                : throw new ArgumentException("Virtual texture asset must contain built data.", nameof(asset));

            string producerName = string.IsNullOrWhiteSpace(asset.name)
                ? nameof(VividVirtualTextureAsset)
                : asset.name;
            ProducerDesc = new VTProducerDesc(
                producerName,
                m_BuiltData.PageSize,
                m_BuiltData.BorderSize,
                m_BuiltData.VirtualPageCountX,
                m_BuiltData.VirtualPageCountY,
                m_BuiltData.MipCount,
                Mathf.Max(1, m_BuiltData.LayerCount),
                m_BuiltData.GraphicsFormat,
                m_BuiltData.LayerCount > 0 && m_BuiltData.Layers[0].SRGB,
                m_BuiltData.FallbackColor,
                producerPriority: 0,
                continuousUpdate: false,
                persistentLowestMip: true);
        }

        public string Name => $"{nameof(VividVirtualTextureAssetProducer)}({m_Asset.name})";

        public VTProducerDesc ProducerDesc { get; }

        public VTPageRequestStatus RequestPageData(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request)
        {
            if (!m_BuiltData.Matches(desc))
                return VTPageRequestStatus.Invalid;

            return m_BuiltData.TryGetTilePayload(request.PageCoord, out _)
                ? VTPageRequestStatus.Available
                : VTPageRequestStatus.Invalid;
        }

        public IVTPageFinalizer ProducePageData(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request)
        {
            if (!m_BuiltData.Matches(desc)
                || !m_BuiltData.TryGetTilePayload(request.PageCoord, out VividVirtualTextureTilePayload payload))
            {
                return null;
            }

            int pixelCount = desc.PhysicalPageSize * desc.PhysicalPageSize;
            return new Finalizer(payload, pixelCount);
        }

        public void GatherTasks(List<IVTPageProducerTask> tasks)
        {
        }

        public void CancelRequest(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request)
        {
        }
    }
}
