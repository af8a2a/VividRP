using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public interface VTProducer
    {
        string Name { get; }
    }

    public readonly struct VTProducerDesc : IEquatable<VTProducerDesc>
    {
        public VTProducerDesc(
            string name,
            int tileSize,
            int borderSize,
            int virtualPageCountX,
            int virtualPageCountY,
            int mipCount,
            int layerCount,
            GraphicsFormat format,
            bool sRGB,
            Color32 fallbackColor,
            int producerPriority,
            bool continuousUpdate,
            bool persistentLowestMip)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Producer name must be non-empty.", nameof(name));
            if (tileSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(tileSize));
            if (borderSize < 0)
                throw new ArgumentOutOfRangeException(nameof(borderSize));
            if (virtualPageCountX <= 0)
                throw new ArgumentOutOfRangeException(nameof(virtualPageCountX));
            if (virtualPageCountY <= 0)
                throw new ArgumentOutOfRangeException(nameof(virtualPageCountY));
            if (mipCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(mipCount));
            if (layerCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(layerCount));

            Name = name;
            TileSize = tileSize;
            BorderSize = borderSize;
            VirtualPageCountX = virtualPageCountX;
            VirtualPageCountY = virtualPageCountY;
            MipCount = mipCount;
            LayerCount = layerCount;
            Format = format;
            SRGB = sRGB;
            FallbackColor = fallbackColor;
            ProducerPriority = producerPriority;
            ContinuousUpdate = continuousUpdate;
            PersistentLowestMip = persistentLowestMip;
        }

        public string Name { get; }

        public int TileSize { get; }

        public int BorderSize { get; }

        public int VirtualPageCountX { get; }

        public int VirtualPageCountY { get; }

        public int MipCount { get; }

        public int LayerCount { get; }

        public GraphicsFormat Format { get; }

        public bool SRGB { get; }

        public Color32 FallbackColor { get; }

        public int ProducerPriority { get; }

        public bool ContinuousUpdate { get; }

        public bool PersistentLowestMip { get; }

        internal static VTProducerDesc FromSpaceDesc(string producerName, in VirtualTextureSpaceDesc desc)
        {
            return new VTProducerDesc(
                producerName,
                desc.PageSize,
                desc.BorderSize,
                desc.VirtualPageCountX,
                desc.VirtualPageCountY,
                desc.MipCount,
                desc.StackDesc.LayerCount,
                desc.GraphicsFormat,
                desc.StackDesc.SRGB,
                desc.StackDesc.FallbackColor,
                producerPriority: 0,
                continuousUpdate: false,
                persistentLowestMip: true);
        }

        public bool Equals(VTProducerDesc other)
        {
            return string.Equals(Name, other.Name, StringComparison.Ordinal)
                   && TileSize == other.TileSize
                   && BorderSize == other.BorderSize
                   && VirtualPageCountX == other.VirtualPageCountX
                   && VirtualPageCountY == other.VirtualPageCountY
                   && MipCount == other.MipCount
                   && LayerCount == other.LayerCount
                   && Format == other.Format
                   && SRGB == other.SRGB
                   && FallbackColor.Equals(other.FallbackColor)
                   && ProducerPriority == other.ProducerPriority
                   && ContinuousUpdate == other.ContinuousUpdate
                   && PersistentLowestMip == other.PersistentLowestMip;
        }

        public override bool Equals(object obj)
        {
            return obj is VTProducerDesc other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = StringComparer.Ordinal.GetHashCode(Name);
                hashCode = (hashCode * 397) ^ TileSize;
                hashCode = (hashCode * 397) ^ BorderSize;
                hashCode = (hashCode * 397) ^ VirtualPageCountX;
                hashCode = (hashCode * 397) ^ VirtualPageCountY;
                hashCode = (hashCode * 397) ^ MipCount;
                hashCode = (hashCode * 397) ^ LayerCount;
                hashCode = (hashCode * 397) ^ (int)Format;
                hashCode = (hashCode * 397) ^ SRGB.GetHashCode();
                hashCode = (hashCode * 397) ^ FallbackColor.GetHashCode();
                hashCode = (hashCode * 397) ^ ProducerPriority;
                hashCode = (hashCode * 397) ^ ContinuousUpdate.GetHashCode();
                hashCode = (hashCode * 397) ^ PersistentLowestMip.GetHashCode();
                return hashCode;
            }
        }
    }

    public enum VTPageRequestStatus
    {
        Invalid,
        Saturated,
        Pending,
        Available,
    }

    internal interface IVTPageProducerTask
    {
        bool IsCompleted { get; }
    }

    internal interface IVTPageRequestRetirement
    {
        void RetireRequests(IReadOnlyList<VTRequest> liveRequests);
    }

    internal interface IVTPageUploadFinalizer : IDisposable
    {
    }

    internal interface IVTPageFinalizer : IVTPageUploadFinalizer
    {
        void FinalizeRender(CommandBuffer cmd);

        void FinalizeUpload(Texture2DArray stagingTexture, int slice, Color32[] scratchPixels);
    }

    internal interface IVTMultiLayerPageFinalizer : IVTPageFinalizer
    {
        int LayerCount { get; }

        void FinalizeUploadLayer(
            Texture2DArray stagingTexture,
            int slice,
            int layerIndex,
            Color32[] scratchPixels);
    }

    internal interface IVTGpuPageFinalizer : IVTPageUploadFinalizer
    {
        int LayerCount { get; }

        void RecordGpuUpload(CommandBuffer cmd, RenderTexture stagingTexture, int baseSlice);
    }

    internal interface IVTEncodedPageFinalizer : IVTPageUploadFinalizer
    {
        int LayerCount { get; }

        void FinalizeEncodedUploadLayer(Texture2DArray stagingTexture, int slice, int layerIndex);
    }

    internal readonly struct VTPageUploadPayload
    {
        internal VTPageUploadPayload(in VTRequest request, IVTPageUploadFinalizer finalizer)
        {
            Request = request;
            Finalizer = finalizer ?? throw new ArgumentNullException(nameof(finalizer));
        }

        internal VTRequest Request { get; }

        internal IVTPageUploadFinalizer Finalizer { get; }

        internal bool IsValid => Finalizer is IVTPageFinalizer or IVTGpuPageFinalizer or IVTEncodedPageFinalizer;
    }

    internal interface IVTPageProducer : VTProducer
    {
        VTProducerDesc ProducerDesc { get; }

        VTPageRequestStatus RequestPageData(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request);

        IVTPageUploadFinalizer ProducePageData(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request);

        void GatherTasks(List<IVTPageProducerTask> tasks);

        void CancelRequest(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request);
    }

    internal interface IVTRuntimePageProducer : VTProducer
    {
        void WritePage(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request,
            Color32[] outputPixels);
    }

    internal static class VTRuntimeProducerUtility
    {
        internal static IVTPageProducer Resolve(VTProducer producer, in VirtualTextureSpaceDesc desc)
        {
            if (producer is IVTPageProducer pageProducer)
                return pageProducer;

            if (producer is VividVirtualTextureAsset virtualTextureAsset)
                return virtualTextureAsset.BuiltData != null
                    ? new VividVirtualTextureAssetProducer(virtualTextureAsset)
                    : null;

            return producer is IVTRuntimePageProducer runtimeProducer
                ? CreateAdapter(runtimeProducer, desc)
                : null;
        }

        internal static IVTPageProducer CreateAdapter(
            IVTRuntimePageProducer runtimeProducer,
            in VirtualTextureSpaceDesc desc)
        {
            return runtimeProducer != null
                ? new VTSynchronousPageProducerAdapter(runtimeProducer, desc)
                : null;
        }
    }

    internal sealed class VTSynchronousPageProducerAdapter : IVTPageProducer
    {
        private sealed class Finalizer : IVTMultiLayerPageFinalizer
        {
            private readonly IVTRuntimePageProducer m_Producer;
            private readonly VirtualTextureSpaceDesc m_Desc;
            private readonly VTRequest m_Request;

            internal Finalizer(
                IVTRuntimePageProducer producer,
                in VirtualTextureSpaceDesc desc,
                in VTRequest request)
            {
                m_Producer = producer ?? throw new ArgumentNullException(nameof(producer));
                m_Desc = desc;
                m_Request = request;
            }

            public void FinalizeRender(CommandBuffer cmd)
            {
            }

            public int LayerCount => Mathf.Max(1, m_Desc.StackDesc.LayerCount);

            public void FinalizeUpload(Texture2DArray stagingTexture, int slice, Color32[] scratchPixels)
            {
                FinalizeUploadLayer(stagingTexture, slice, 0, scratchPixels);
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

                if (layerIndex == 0)
                    m_Producer.WritePage(m_Desc, m_Request, scratchPixels);
                else
                    FillLayerFallback(layerIndex, scratchPixels);

                stagingTexture.SetPixels32(scratchPixels, slice, 0);
            }

            public void Dispose()
            {
            }

            private void FillLayerFallback(int layerIndex, Color32[] scratchPixels)
            {
                Color32 fallbackColor = m_Desc.StackDesc.GetLayer(layerIndex).FallbackColor;
                for (int pixelIndex = 0; pixelIndex < scratchPixels.Length; pixelIndex++)
                    scratchPixels[pixelIndex] = fallbackColor;
            }
        }

        private readonly IVTRuntimePageProducer m_RuntimeProducer;

        internal VTSynchronousPageProducerAdapter(
            IVTRuntimePageProducer runtimeProducer,
            in VirtualTextureSpaceDesc desc)
        {
            m_RuntimeProducer = runtimeProducer ?? throw new ArgumentNullException(nameof(runtimeProducer));
            ProducerDesc = VTProducerDesc.FromSpaceDesc(m_RuntimeProducer.Name, desc);
        }

        public string Name => m_RuntimeProducer.Name;

        public VTProducerDesc ProducerDesc { get; }

        public VTPageRequestStatus RequestPageData(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request)
        {
            return VirtualTextureSpaceUtility.IsCoordValid(desc, request.PageCoord)
                ? VTPageRequestStatus.Available
                : VTPageRequestStatus.Invalid;
        }

        public IVTPageUploadFinalizer ProducePageData(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request)
        {
            if (!VirtualTextureSpaceUtility.IsCoordValid(desc, request.PageCoord))
                return null;

            return new Finalizer(m_RuntimeProducer, desc, request);
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

    internal sealed class VTProceduralPageProducer : IVTRuntimePageProducer
    {
        internal static readonly VTProceduralPageProducer Instance = new();

        private VTProceduralPageProducer()
        {
        }

        public string Name => nameof(VTProceduralPageProducer);

        public void WritePage(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request,
            Color32[] outputPixels)
        {
            if (outputPixels == null)
                throw new ArgumentNullException(nameof(outputPixels));

            int physicalPageSize = desc.PhysicalPageSize;
            int pixelCount = physicalPageSize * physicalPageSize;
            if (outputPixels.Length < pixelCount)
                throw new ArgumentException("Output pixel buffer is smaller than the physical VT page.", nameof(outputPixels));

            VirtualTexturePageCoord coord = request.PageCoord;
            Color32 primaryColor = HashColor(coord.X, coord.Y, coord.Mip, request.PhysicalPageId, 0);
            Color32 secondaryColor = HashColor(coord.X, coord.Y, coord.Mip, request.PhysicalPageId, 1);
            Color32 accentColor = HashColor(coord.X, coord.Y, coord.Mip, request.PhysicalPageId, 2);
            int borderSize = Mathf.Clamp(desc.BorderSize, 0, physicalPageSize / 2);
            int interiorMin = borderSize;
            int interiorMax = Mathf.Max(interiorMin, physicalPageSize - borderSize);
            int cellSize = Mathf.Max(4, desc.PageSize / 8);

            for (int y = 0; y < physicalPageSize; y++)
            {
                for (int x = 0; x < physicalPageSize; x++)
                {
                    int pixelIndex = y * physicalPageSize + x;
                    bool borderPixel = x < borderSize
                                       || y < borderSize
                                       || x >= physicalPageSize - borderSize
                                       || y >= physicalPageSize - borderSize;
                    if (borderPixel)
                    {
                        outputPixels[pixelIndex] = new Color32(255, 255, 255, 255);
                        continue;
                    }

                    int interiorX = x - interiorMin;
                    int interiorY = y - interiorMin;
                    bool checker = ((interiorX / cellSize) + (interiorY / cellSize) + coord.Mip) % 2 == 0;
                    bool guideLine = interiorX % cellSize == 0 || interiorY % cellSize == 0;
                    bool diagonal = interiorX == interiorY || interiorX + interiorY == Mathf.Max(0, interiorMax - interiorMin - 1);

                    Color32 color = checker ? primaryColor : secondaryColor;
                    if (guideLine)
                        color = Blend(color, accentColor, 96);
                    if (diagonal)
                        color = Blend(color, new Color32(255, 255, 255, 255), 96);

                    outputPixels[pixelIndex] = color;
                }
            }
        }

        private static Color32 HashColor(int x, int y, int mip, int physicalPageId, int salt)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)(x + 1 + salt * 17)) * 16777619u;
                hash = (hash ^ (uint)(y + 1 + salt * 31)) * 16777619u;
                hash = (hash ^ (uint)(mip + 1 + salt * 47)) * 16777619u;
                hash = (hash ^ (uint)(physicalPageId + 1 + salt * 61)) * 16777619u;

                byte r = (byte)(64 + (hash & 0x7Fu));
                byte g = (byte)(64 + ((hash >> 8) & 0x7Fu));
                byte b = (byte)(64 + ((hash >> 16) & 0x7Fu));
                return new Color32(r, g, b, 255);
            }
        }

        private static Color32 Blend(Color32 left, Color32 right, byte rightWeight)
        {
            int leftWeight = 255 - rightWeight;
            return new Color32(
                (byte)((left.r * leftWeight + right.r * rightWeight) / 255),
                (byte)((left.g * leftWeight + right.g * rightWeight) / 255),
                (byte)((left.b * leftWeight + right.b * rightWeight) / 255),
                255);
        }
    }

    internal sealed class VTCheckerSourcePageProducer : IVTRuntimePageProducer
    {
        internal static readonly VTCheckerSourcePageProducer Instance = new();

        private VTCheckerSourcePageProducer()
        {
        }

        public string Name => nameof(VTCheckerSourcePageProducer);

        public void WritePage(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request,
            Color32[] outputPixels)
        {
            if (outputPixels == null)
                throw new ArgumentNullException(nameof(outputPixels));

            int physicalPageSize = desc.PhysicalPageSize;
            int pixelCount = physicalPageSize * physicalPageSize;
            if (outputPixels.Length < pixelCount)
                throw new ArgumentException("Output pixel buffer is smaller than the physical VT page.", nameof(outputPixels));

            VirtualTexturePageCoord coord = request.PageCoord;
            int pageOriginX = coord.X * desc.PageSize;
            int pageOriginY = coord.Y * desc.PageSize;
            for (int y = 0; y < physicalPageSize; y++)
            {
                int sourceY = pageOriginY + y - desc.BorderSize;
                for (int x = 0; x < physicalPageSize; x++)
                {
                    int sourceX = pageOriginX + x - desc.BorderSize;
                    outputPixels[y * physicalPageSize + x] = EvaluateSourceTexel(desc, coord.Mip, sourceX, sourceY);
                }
            }
        }

        internal static Color32 EvaluateSourceTexel(
            in VirtualTextureSpaceDesc desc,
            int mip,
            int sourceX,
            int sourceY)
        {
            int sourceWidth = VirtualTextureSpaceUtility.GetPageCountX(desc.VirtualPageCountX, mip) * desc.PageSize;
            int sourceHeight = VirtualTextureSpaceUtility.GetPageCountY(desc.VirtualPageCountY, mip) * desc.PageSize;
            int clampedX = Mathf.Clamp(sourceX, 0, Mathf.Max(0, sourceWidth - 1));
            int clampedY = Mathf.Clamp(sourceY, 0, Mathf.Max(0, sourceHeight - 1));
            int checkerSize = Mathf.Max(1, desc.PageSize / 4);
            bool checker = ((clampedX / checkerSize) + (clampedY / checkerSize) + mip) % 2 == 0;
            byte baseValue = checker ? (byte)210 : (byte)52;
            byte red = (byte)Mathf.Clamp(baseValue + mip * 9, 0, 255);
            byte green = (byte)(32 + ((clampedX * 11 + mip * 23) & 0x7F));
            byte blue = (byte)(32 + ((clampedY * 13 + mip * 29) & 0x7F));
            return new Color32(red, green, blue, 255);
        }
    }

    internal sealed class VTTexture2DPageProducer : IVTRuntimePageProducer
    {
        private readonly Texture2D m_SourceTexture;
        private Color32[][] m_MipPixels;
        private Vector2Int[] m_MipSizes;
        private bool m_SourceDataBuilt;

        internal VTTexture2DPageProducer(Texture2D sourceTexture)
        {
            m_SourceTexture = sourceTexture != null
                ? sourceTexture
                : throw new ArgumentNullException(nameof(sourceTexture));
        }

        public string Name => $"{nameof(VTTexture2DPageProducer)}({m_SourceTexture.name})";

        internal Texture2D SourceTexture => m_SourceTexture;

        public void WritePage(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request,
            Color32[] outputPixels)
        {
            WritePage(desc, request, outputPixels, repeat: false);
        }

        internal void WritePage(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request,
            Color32[] outputPixels,
            bool repeat)
        {
            WritePage(desc, request, outputPixels, repeat, sourceMipOffset: 0);
        }

        internal void WritePage(
            in VirtualTextureSpaceDesc desc,
            in VTRequest request,
            Color32[] outputPixels,
            bool repeat,
            int sourceMipOffset)
        {
            if (outputPixels == null)
                throw new ArgumentNullException(nameof(outputPixels));

            int physicalPageSize = desc.PhysicalPageSize;
            int pixelCount = physicalPageSize * physicalPageSize;
            if (outputPixels.Length < pixelCount)
                throw new ArgumentException("Output pixel buffer is smaller than the physical VT page.", nameof(outputPixels));

            EnsureSourceData();

            VirtualTexturePageCoord coord = request.PageCoord;
            int pageCountX = VirtualTextureSpaceUtility.GetPageCountX(desc.VirtualPageCountX, coord.Mip);
            int pageCountY = VirtualTextureSpaceUtility.GetPageCountY(desc.VirtualPageCountY, coord.Mip);
            int logicalWidth = Mathf.Max(1, pageCountX * desc.PageSize);
            int logicalHeight = Mathf.Max(1, pageCountY * desc.PageSize);
            int pageOriginX = coord.X * desc.PageSize;
            int pageOriginY = coord.Y * desc.PageSize;

            for (int y = 0; y < physicalPageSize; y++)
            {
                int logicalY = pageOriginY + y - desc.BorderSize;
                if (!repeat)
                    logicalY = Mathf.Clamp(logicalY, 0, logicalHeight - 1);
                float v = (logicalY + 0.5f) / logicalHeight;
                for (int x = 0; x < physicalPageSize; x++)
                {
                    int logicalX = pageOriginX + x - desc.BorderSize;
                    if (!repeat)
                        logicalX = Mathf.Clamp(logicalX, 0, logicalWidth - 1);
                    float u = (logicalX + 0.5f) / logicalWidth;
                    outputPixels[y * physicalPageSize + x] = SampleSource(
                        coord.Mip + sourceMipOffset,
                        u,
                        v,
                        repeat);
                }
            }
        }

        internal Color32 EvaluateSourceTexel(
            in VirtualTextureSpaceDesc desc,
            int mip,
            int sourceX,
            int sourceY)
        {
            EnsureSourceData();

            int pageCountX = VirtualTextureSpaceUtility.GetPageCountX(desc.VirtualPageCountX, mip);
            int pageCountY = VirtualTextureSpaceUtility.GetPageCountY(desc.VirtualPageCountY, mip);
            int logicalWidth = Mathf.Max(1, pageCountX * desc.PageSize);
            int logicalHeight = Mathf.Max(1, pageCountY * desc.PageSize);
            int clampedX = Mathf.Clamp(sourceX, 0, logicalWidth - 1);
            int clampedY = Mathf.Clamp(sourceY, 0, logicalHeight - 1);
            return SampleSource(
                mip,
                (clampedX + 0.5f) / logicalWidth,
                (clampedY + 0.5f) / logicalHeight,
                false);
        }

        private void EnsureSourceData()
        {
            if (m_SourceDataBuilt)
                return;

            if (m_SourceTexture.isReadable && TryBuildMipPixels(m_SourceTexture))
            {
                m_SourceDataBuilt = true;
                return;
            }

            Texture2D readableCopy = CreateReadableCopy(m_SourceTexture);
            try
            {
                if (!TryBuildMipPixels(readableCopy))
                    throw new InvalidOperationException($"[VividRP] Failed to read VT source texture '{m_SourceTexture.name}'.");
            }
            finally
            {
                CoreUtils.Destroy(readableCopy);
            }

            m_SourceDataBuilt = true;
        }

        private bool TryBuildMipPixels(Texture2D readableTexture)
        {
            try
            {
                int mipCount = Mathf.Max(1, readableTexture.mipmapCount);
                m_MipPixels = new Color32[mipCount][];
                m_MipSizes = new Vector2Int[mipCount];

                for (int mip = 0; mip < mipCount; mip++)
                {
                    int mipWidth = Mathf.Max(1, readableTexture.width >> mip);
                    int mipHeight = Mathf.Max(1, readableTexture.height >> mip);
                    m_MipPixels[mip] = readableTexture.GetPixels32(mip);
                    m_MipSizes[mip] = new Vector2Int(mipWidth, mipHeight);
                }

                return true;
            }
            catch (UnityException)
            {
                m_MipPixels = null;
                m_MipSizes = null;
                return false;
            }
        }

        private static Texture2D CreateReadableCopy(Texture2D sourceTexture)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture temporary = RenderTexture.GetTemporary(
                sourceTexture.width,
                sourceTexture.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);

            try
            {
                Graphics.Blit(sourceTexture, temporary);
                RenderTexture.active = temporary;

                var readableTexture = new Texture2D(
                    sourceTexture.width,
                    sourceTexture.height,
                    TextureFormat.RGBA32,
                    true,
                    false)
                {
                    name = $"{sourceTexture.name}_VividVT_Readable",
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };

                readableTexture.ReadPixels(
                    new Rect(0f, 0f, sourceTexture.width, sourceTexture.height),
                    0,
                    0,
                    false);
                readableTexture.Apply(true, false);
                return readableTexture;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        internal Color32 SampleSource(int mip, float u, float v, bool repeat)
        {
            EnsureSourceData();

            int sampleMip = Mathf.Clamp(mip, 0, m_MipPixels.Length - 1);
            Color32[] pixels = m_MipPixels[sampleMip];
            Vector2Int size = m_MipSizes[sampleMip];
            float sampleU = repeat ? Repeat01(u) : Mathf.Clamp01(u);
            float sampleV = repeat ? Repeat01(v) : Mathf.Clamp01(v);
            float sourceX = sampleU * size.x - 0.5f;
            float sourceY = sampleV * size.y - 0.5f;
            int unwrappedX0 = Mathf.FloorToInt(sourceX);
            int unwrappedY0 = Mathf.FloorToInt(sourceY);
            int x0 = repeat ? PositiveModulo(unwrappedX0, size.x) : Mathf.Clamp(unwrappedX0, 0, size.x - 1);
            int y0 = repeat ? PositiveModulo(unwrappedY0, size.y) : Mathf.Clamp(unwrappedY0, 0, size.y - 1);
            int x1 = repeat ? PositiveModulo(unwrappedX0 + 1, size.x) : Mathf.Min(x0 + 1, size.x - 1);
            int y1 = repeat ? PositiveModulo(unwrappedY0 + 1, size.y) : Mathf.Min(y0 + 1, size.y - 1);
            float tx = Mathf.Clamp01(sourceX - x0);
            float ty = Mathf.Clamp01(sourceY - y0);

            if (repeat)
            {
                tx = Mathf.Clamp01(sourceX - unwrappedX0);
                ty = Mathf.Clamp01(sourceY - unwrappedY0);
            }

            Color32 c00 = pixels[y0 * size.x + x0];
            Color32 c10 = pixels[y0 * size.x + x1];
            Color32 c01 = pixels[y1 * size.x + x0];
            Color32 c11 = pixels[y1 * size.x + x1];
            return Bilinear(c00, c10, c01, c11, tx, ty);
        }

        private static float Repeat01(float value)
        {
            return value - Mathf.Floor(value);
        }

        private static int PositiveModulo(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        private static Color32 Bilinear(
            Color32 c00,
            Color32 c10,
            Color32 c01,
            Color32 c11,
            float tx,
            float ty)
        {
            float inverseTx = 1f - tx;
            float inverseTy = 1f - ty;
            float w00 = inverseTx * inverseTy;
            float w10 = tx * inverseTy;
            float w01 = inverseTx * ty;
            float w11 = tx * ty;

            return new Color32(
                (byte)Mathf.RoundToInt(c00.r * w00 + c10.r * w10 + c01.r * w01 + c11.r * w11),
                (byte)Mathf.RoundToInt(c00.g * w00 + c10.g * w10 + c01.g * w01 + c11.g * w11),
                (byte)Mathf.RoundToInt(c00.b * w00 + c10.b * w10 + c01.b * w01 + c11.b * w11),
                (byte)Mathf.RoundToInt(c00.a * w00 + c10.a * w10 + c01.a * w01 + c11.a * w11));
        }
    }

    internal sealed class VTNullProducer : VTProducer
    {
        internal static readonly VTNullProducer Instance = new();

        private VTNullProducer()
        {
        }

        public string Name => nameof(VTNullProducer);
    }
}
