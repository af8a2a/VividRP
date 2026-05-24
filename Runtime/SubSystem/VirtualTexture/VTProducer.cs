using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public interface VTProducer
    {
        string Name { get; }
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
        internal static IVTRuntimePageProducer Resolve(VTProducer producer)
        {
            return producer as IVTRuntimePageProducer;
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
                int logicalY = Mathf.Clamp(pageOriginY + y - desc.BorderSize, 0, logicalHeight - 1);
                float v = (logicalY + 0.5f) / logicalHeight;
                for (int x = 0; x < physicalPageSize; x++)
                {
                    int logicalX = Mathf.Clamp(pageOriginX + x - desc.BorderSize, 0, logicalWidth - 1);
                    float u = (logicalX + 0.5f) / logicalWidth;
                    outputPixels[y * physicalPageSize + x] = SampleSource(coord.Mip, u, v);
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
                (clampedY + 0.5f) / logicalHeight);
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

        private Color32 SampleSource(int mip, float u, float v)
        {
            int sampleMip = Mathf.Clamp(mip, 0, m_MipPixels.Length - 1);
            Color32[] pixels = m_MipPixels[sampleMip];
            Vector2Int size = m_MipSizes[sampleMip];
            float sourceX = Mathf.Clamp01(u) * size.x - 0.5f;
            float sourceY = Mathf.Clamp01(v) * size.y - 0.5f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(sourceX), 0, size.x - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(sourceY), 0, size.y - 1);
            int x1 = Mathf.Min(x0 + 1, size.x - 1);
            int y1 = Mathf.Min(y0 + 1, size.y - 1);
            float tx = Mathf.Clamp01(sourceX - x0);
            float ty = Mathf.Clamp01(sourceY - y0);

            Color32 c00 = pixels[y0 * size.x + x0];
            Color32 c10 = pixels[y0 * size.x + x1];
            Color32 c01 = pixels[y1 * size.x + x0];
            Color32 c11 = pixels[y1 * size.x + x1];
            return Bilinear(c00, c10, c01, c11, tx, ty);
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
