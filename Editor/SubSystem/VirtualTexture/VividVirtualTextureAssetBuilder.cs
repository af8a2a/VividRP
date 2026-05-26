using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor
{
    internal static class VividVirtualTextureAssetBuilder
    {
        internal struct Parameters
        {
            public Texture2D SourceTexture;
            public Texture2D NormalTexture;
            public Texture2D MaskTexture;
            public string SourceTextureGUID;
            public string SourceTexturePath;
            public int PageSize;
            public int BorderSize;
            public int MipCount;
            public Color32 FallbackColor;
            public Color32 NormalFallbackColor;
            public Color32 MaskFallbackColor;
            public string StreamDataPath;
            public Action<string> LogErrorHandler;
        }

        internal static void Generate(
            VividVirtualTextureAsset asset,
            VividVirtualTextureBuiltData builtData,
            in Parameters parameters)
        {
            if (asset == null)
                throw new ArgumentNullException(nameof(asset));
            if (builtData == null)
                throw new ArgumentNullException(nameof(builtData));
            if (parameters.SourceTexture == null)
                throw new ArgumentNullException(nameof(parameters.SourceTexture));

            int pageSize = Mathf.Max(1, parameters.PageSize);
            int borderSize = Mathf.Max(0, parameters.BorderSize);
            int virtualPageCountX = Mathf.Max(1, Mathf.CeilToInt(parameters.SourceTexture.width / (float)pageSize));
            int virtualPageCountY = Mathf.Max(1, Mathf.CeilToInt(parameters.SourceTexture.height / (float)pageSize));
            int mipCount = parameters.MipCount > 0
                ? Mathf.Clamp(parameters.MipCount, 1, VirtualTextureFeedbackProcessor.MaxMipCount)
                : ComputeMipCount(virtualPageCountX, virtualPageCountY);
            VividVirtualTextureLayerDescriptor[] layers = CreateLayerDescriptors(parameters);
            VTLayerDesc[] stackLayers = CreateStackLayers(layers);

            var desc = new VirtualTextureSpaceDesc(
                ResolveSpaceName(asset, parameters),
                virtualPageCountX,
                virtualPageCountY,
                mipCount,
                new VTStackDesc(
                    pageSize,
                    borderSize,
                    cachePageCount: 2,
                    layers: stackLayers,
                    maxUploadsPerFrame: 1,
                    feedbackCapacity: 16));

            int totalTileCount = VirtualTextureSpaceUtility.GetTotalPageCount(virtualPageCountX, virtualPageCountY, mipCount);
            var tiles = new List<VividVirtualTextureTileDescriptor>(totalTileCount);
            var chunks = new List<VividVirtualTextureChunkDescriptor>(mipCount);
            var rawData = new List<byte>(totalTileCount * desc.PhysicalPageSize * desc.PhysicalPageSize * 4 * layers.Length);
            var mipTileOffsets = new int[mipCount];
            var pagePixels = new Color32[desc.PhysicalPageSize * desc.PhysicalPageSize];
            VTTexture2DPageProducer[] producers = CreateLayerProducers(parameters);

            for (int mip = 0; mip < mipCount; mip++)
            {
                mipTileOffsets[mip] = tiles.Count;
                int chunkIndex = chunks.Count;
                int chunkByteOffset = rawData.Count;
                int pageCountX = VirtualTextureSpaceUtility.GetPageCountX(virtualPageCountX, mip);
                int pageCountY = VirtualTextureSpaceUtility.GetPageCountY(virtualPageCountY, mip);

                for (int y = 0; y < pageCountY; y++)
                {
                    for (int x = 0; x < pageCountX; x++)
                    {
                        int tileByteOffset = rawData.Count - chunkByteOffset;
                        var coord = new VirtualTexturePageCoord(x, y, mip);
                        var request = new VTRequest(
                            0,
                            coord,
                            physicalPageId: 0,
                            generation: 0,
                            priority: 0,
                            requestFrame: 0);

                        for (int layerIndex = 0; layerIndex < producers.Length; layerIndex++)
                        {
                            producers[layerIndex].WritePage(desc, request, pagePixels);
                            AppendRGBA32(pagePixels, rawData);
                        }

                        tiles.Add(new VividVirtualTextureTileDescriptor(
                            mip,
                            x,
                            y,
                            chunkIndex,
                            tileByteOffset,
                            pagePixels.Length * 4 * producers.Length));
                    }
                }

                chunks.Add(new VividVirtualTextureChunkDescriptor(
                    firstMip: mip,
                    mipCount: 1,
                    byteOffset: chunkByteOffset,
                    byteSize: rawData.Count - chunkByteOffset,
                    codec: VividVirtualTextureCodec.RawRGBA32));
            }

            byte[] rawBytes = rawData.ToArray();
            byte[] inlineRawData = rawBytes;
            string streamDataPath = string.Empty;
            int streamDataByteSize = 0;
            if (!string.IsNullOrWhiteSpace(parameters.StreamDataPath))
            {
                streamDataPath = parameters.StreamDataPath.Replace('\\', '/');
                string directory = Path.GetDirectoryName(streamDataPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllBytes(streamDataPath, rawBytes);
                inlineRawData = Array.Empty<byte>();
                streamDataByteSize = rawBytes.Length;
            }

            builtData.Initialize(
                parameters.SourceTextureGUID,
                parameters.SourceTexturePath,
                pageSize,
                borderSize,
                virtualPageCountX,
                virtualPageCountY,
                mipCount,
                layers,
                chunks.ToArray(),
                tiles.ToArray(),
                mipTileOffsets,
                inlineRawData,
                streamDataPath,
                streamDataByteSize);
            asset.Initialize(builtData);
        }

        private static VividVirtualTextureLayerDescriptor[] CreateLayerDescriptors(in Parameters parameters)
        {
            var layers = new List<VividVirtualTextureLayerDescriptor>
            {
                new(
                    VTLayerSemantic.BaseColor,
                    GraphicsFormat.R8G8B8A8_UNorm,
                    GraphicsFormatUtility.IsSRGBFormat(parameters.SourceTexture.graphicsFormat),
                    parameters.FallbackColor,
                    physicalGroup: 0),
            };

            if (parameters.NormalTexture != null)
            {
                layers.Add(new VividVirtualTextureLayerDescriptor(
                    VTLayerSemantic.Normal,
                    GraphicsFormat.R8G8B8A8_UNorm,
                    sRGB: false,
                    ResolveFallback(parameters.NormalFallbackColor, new Color32(128, 128, 255, 255)),
                    physicalGroup: 0));
            }

            if (parameters.MaskTexture != null)
            {
                layers.Add(new VividVirtualTextureLayerDescriptor(
                    VTLayerSemantic.Mask,
                    GraphicsFormat.R8G8B8A8_UNorm,
                    sRGB: false,
                    ResolveFallback(parameters.MaskFallbackColor, new Color32(255, 255, 255, 255)),
                    physicalGroup: 0));
            }

            return layers.ToArray();
        }

        private static VTLayerDesc[] CreateStackLayers(VividVirtualTextureLayerDescriptor[] layerDescriptors)
        {
            var stackLayers = new VTLayerDesc[layerDescriptors.Length];
            for (int layerIndex = 0; layerIndex < layerDescriptors.Length; layerIndex++)
            {
                VividVirtualTextureLayerDescriptor layer = layerDescriptors[layerIndex];
                stackLayers[layerIndex] = new VTLayerDesc(
                    layer.Semantic,
                    layer.Format,
                    layer.SRGB,
                    layer.FallbackColor,
                    layer.PhysicalGroup);
            }

            return stackLayers;
        }

        private static VTTexture2DPageProducer[] CreateLayerProducers(in Parameters parameters)
        {
            var producers = new List<VTTexture2DPageProducer>
            {
                new(parameters.SourceTexture),
            };

            if (parameters.NormalTexture != null)
                producers.Add(new VTTexture2DPageProducer(parameters.NormalTexture));
            if (parameters.MaskTexture != null)
                producers.Add(new VTTexture2DPageProducer(parameters.MaskTexture));

            return producers.ToArray();
        }

        private static Color32 ResolveFallback(Color32 configured, Color32 defaultColor)
        {
            return configured.a == 0 && configured.r == 0 && configured.g == 0 && configured.b == 0
                ? defaultColor
                : configured;
        }

        private static string ResolveSpaceName(
            VividVirtualTextureAsset asset,
            in Parameters parameters)
        {
            if (!string.IsNullOrWhiteSpace(asset.name))
                return asset.name;

            if (!string.IsNullOrWhiteSpace(parameters.SourceTexture.name))
                return parameters.SourceTexture.name;

            if (!string.IsNullOrWhiteSpace(parameters.SourceTexturePath))
                return Path.GetFileNameWithoutExtension(parameters.SourceTexturePath);

            return nameof(VividVirtualTextureAsset);
        }

        internal static int ComputeMipCount(int virtualPageCountX, int virtualPageCountY)
        {
            int maxPageCount = Mathf.Max(1, Mathf.Max(virtualPageCountX, virtualPageCountY));
            int mipCount = 1;
            while ((maxPageCount >>= 1) > 0 && mipCount < VirtualTextureFeedbackProcessor.MaxMipCount)
                mipCount += 1;

            return mipCount;
        }

        private static void AppendRGBA32(Color32[] pixels, List<byte> output)
        {
            for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
            {
                Color32 pixel = pixels[pixelIndex];
                output.Add(pixel.r);
                output.Add(pixel.g);
                output.Add(pixel.b);
                output.Add(pixel.a);
            }
        }
    }
}
