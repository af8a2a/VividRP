using System;
using UnityEngine;

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

    internal sealed class VTNullProducer : VTProducer
    {
        internal static readonly VTNullProducer Instance = new();

        private VTNullProducer()
        {
        }

        public string Name => nameof(VTNullProducer);
    }
}
