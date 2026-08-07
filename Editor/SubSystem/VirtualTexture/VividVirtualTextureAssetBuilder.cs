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
        private const int GPUDrivenPageSize = 128;
        private const int GPUDrivenBorderSize = 4;
        private const int GPUDrivenMaxPageCount = 64;

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
            public VividVirtualTextureBuildProfile BuildProfile;
            public VividVirtualTextureAddressMode AddressMode;
            public string RuntimeStreamDataPath;
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
            Texture2D primaryTexture = ResolvePrimaryTexture(parameters);
            if (primaryTexture == null)
                throw new ArgumentException("At least one source texture is required.", nameof(parameters));
            if (parameters.BuildProfile == VividVirtualTextureBuildProfile.Generic && parameters.SourceTexture == null)
                throw new ArgumentNullException(nameof(parameters.SourceTexture));

            bool gpuDrivenSurface = parameters.BuildProfile == VividVirtualTextureBuildProfile.GPUDrivenSurface;
            int pageSize = gpuDrivenSurface ? GPUDrivenPageSize : Mathf.Max(1, parameters.PageSize);
            int borderSize = gpuDrivenSurface ? GPUDrivenBorderSize : Mathf.Max(0, parameters.BorderSize);
            ResolveVirtualPageCounts(
                parameters,
                primaryTexture,
                pageSize,
                out int virtualPageCountX,
                out int virtualPageCountY);
            int mipCount = !gpuDrivenSurface && parameters.MipCount > 0
                ? Mathf.Clamp(parameters.MipCount, 1, VirtualTextureFeedbackProcessor.MaxMipCount)
                : gpuDrivenSurface
                    ? ComputeGPUDrivenMipCount(virtualPageCountX, virtualPageCountY)
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
            bool writeStreamData = !string.IsNullOrWhiteSpace(parameters.StreamDataPath);
            var rawData = writeStreamData
                ? null
                : new List<byte>(totalTileCount * desc.PhysicalPageSize * desc.PhysicalPageSize * 4 * layers.Length);
            var mipTileOffsets = new int[mipCount];
            var pagePixels = new Color32[desc.PhysicalPageSize * desc.PhysicalPageSize];
            var pageBytes = new byte[pagePixels.Length * 4];
            VTTexture2DPageProducer[] producers = CreateLayerProducers(parameters);
            int[] sourceMipOffsets = CreateSourceMipOffsets(
                producers,
                gpuDrivenSurface,
                virtualPageCountX,
                virtualPageCountY,
                pageSize);
            string streamDataPath = writeStreamData
                ? parameters.StreamDataPath.Replace('\\', '/')
                : string.Empty;
            FileStream stream = null;

            try
            {
                if (writeStreamData)
                {
                    string directory = Path.GetDirectoryName(streamDataPath);
                    if (!string.IsNullOrWhiteSpace(directory))
                        Directory.CreateDirectory(directory);
                    stream = new FileStream(
                        streamDataPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.Read,
                        bufferSize: 1024 * 1024,
                        FileOptions.SequentialScan);
                }

                for (int mip = 0; mip < mipCount; mip++)
                {
                    mipTileOffsets[mip] = tiles.Count;
                    int chunkIndex = chunks.Count;
                    int chunkByteOffset = GetDataByteCount(rawData, stream);
                    int pageCountX = VirtualTextureSpaceUtility.GetPageCountX(virtualPageCountX, mip);
                    int pageCountY = VirtualTextureSpaceUtility.GetPageCountY(virtualPageCountY, mip);

                    for (int y = 0; y < pageCountY; y++)
                    {
                        for (int x = 0; x < pageCountX; x++)
                        {
                            int tileByteOffset = GetDataByteCount(rawData, stream) - chunkByteOffset;
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
                                if (producers[layerIndex] != null)
                                {
                                    bool repeat = gpuDrivenSurface
                                                  && parameters.AddressMode == VividVirtualTextureAddressMode.Repeat;
                                    producers[layerIndex].WritePage(
                                        desc,
                                        request,
                                        pagePixels,
                                        repeat,
                                        sourceMipOffsets[layerIndex]);
                                }
                                else
                                {
                                    Fill(pagePixels, layers[layerIndex].FallbackColor);
                                }

                                AppendRGBA32(pagePixels, pageBytes, rawData, stream);
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
                        byteSize: GetDataByteCount(rawData, stream) - chunkByteOffset,
                        codec: VividVirtualTextureCodec.RawRGBA32));
                }
            }
            finally
            {
                stream?.Dispose();
            }

            byte[] inlineRawData = rawData != null ? rawData.ToArray() : Array.Empty<byte>();
            int streamDataByteSize = writeStreamData
                ? checked((int) new FileInfo(streamDataPath).Length)
                : 0;

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
                streamDataByteSize,
                parameters.BuildProfile,
                ComputeContentLayerMask(parameters),
                ComputeContentVersion(parameters, pageSize, borderSize, virtualPageCountX, virtualPageCountY, mipCount),
                parameters.AddressMode,
                parameters.RuntimeStreamDataPath);
            asset.Initialize(builtData);
        }

        private static VividVirtualTextureLayerDescriptor[] CreateLayerDescriptors(in Parameters parameters)
        {
            if (parameters.BuildProfile == VividVirtualTextureBuildProfile.GPUDrivenSurface)
            {
                return new[]
                {
                    new VividVirtualTextureLayerDescriptor(
                        VTLayerSemantic.BaseColor,
                        GraphicsFormat.R8G8B8A8_SRGB,
                        sRGB: true,
                        new Color32(255, 255, 255, 255),
                        physicalGroup: 0),
                    new VividVirtualTextureLayerDescriptor(
                        VTLayerSemantic.Normal,
                        GraphicsFormat.R8G8B8A8_UNorm,
                        sRGB: false,
                        new Color32(128, 128, 255, 128),
                        physicalGroup: 0),
                    new VividVirtualTextureLayerDescriptor(
                        VTLayerSemantic.Mask,
                        GraphicsFormat.R8G8B8A8_UNorm,
                        sRGB: false,
                        new Color32(255, 255, 255, 255),
                        physicalGroup: 0),
                };
            }

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
            if (parameters.BuildProfile == VividVirtualTextureBuildProfile.GPUDrivenSurface)
            {
                return new[]
                {
                    CreateProducer(parameters.SourceTexture),
                    CreateProducer(parameters.NormalTexture),
                    CreateProducer(parameters.MaskTexture),
                };
            }

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

        private static VTTexture2DPageProducer CreateProducer(Texture2D texture)
        {
            return texture != null ? new VTTexture2DPageProducer(texture) : null;
        }

        private static int[] CreateSourceMipOffsets(
            VTTexture2DPageProducer[] producers,
            bool gpuDrivenSurface,
            int virtualPageCountX,
            int virtualPageCountY,
            int pageSize)
        {
            var offsets = new int[producers.Length];
            if (!gpuDrivenSurface)
                return offsets;

            int virtualWidth = Mathf.Max(1, virtualPageCountX * pageSize);
            int virtualHeight = Mathf.Max(1, virtualPageCountY * pageSize);
            for (int layerIndex = 0; layerIndex < producers.Length; layerIndex++)
            {
                Texture2D texture = producers[layerIndex]?.SourceTexture;
                if (texture == null)
                    continue;

                float ratioX = texture.width / (float) virtualWidth;
                float ratioY = texture.height / (float) virtualHeight;
                offsets[layerIndex] = Mathf.RoundToInt(Mathf.Log(Mathf.Max(ratioX, ratioY), 2.0f));
            }

            return offsets;
        }

        private static Texture2D ResolvePrimaryTexture(in Parameters parameters)
        {
            return parameters.SourceTexture ?? parameters.NormalTexture ?? parameters.MaskTexture;
        }

        private static void ResolveVirtualPageCounts(
            in Parameters parameters,
            Texture2D primaryTexture,
            int pageSize,
            out int pageCountX,
            out int pageCountY)
        {
            if (parameters.BuildProfile != VividVirtualTextureBuildProfile.GPUDrivenSurface)
            {
                pageCountX = Mathf.Max(1, Mathf.CeilToInt(primaryTexture.width / (float) pageSize));
                pageCountY = Mathf.Max(1, Mathf.CeilToInt(primaryTexture.height / (float) pageSize));
                return;
            }

            int maxWidth = 1;
            int maxHeight = 1;
            ResolveMaxDimensions(parameters.SourceTexture, ref maxWidth, ref maxHeight);
            ResolveMaxDimensions(parameters.NormalTexture, ref maxWidth, ref maxHeight);
            ResolveMaxDimensions(parameters.MaskTexture, ref maxWidth, ref maxHeight);
            int requiredPageCountX = Mathf.Max(1, Mathf.CeilToInt(maxWidth / (float) pageSize));
            int requiredPageCountY = Mathf.Max(1, Mathf.CeilToInt(maxHeight / (float) pageSize));
            pageCountX = Mathf.Min(GPUDrivenMaxPageCount, Mathf.NextPowerOfTwo(requiredPageCountX));
            pageCountY = Mathf.Min(GPUDrivenMaxPageCount, Mathf.NextPowerOfTwo(requiredPageCountY));
        }

        private static void ResolveMaxDimensions(
            Texture2D texture,
            ref int maxWidth,
            ref int maxHeight)
        {
            if (texture == null)
                return;

            maxWidth = Mathf.Max(maxWidth, texture.width);
            maxHeight = Mathf.Max(maxHeight, texture.height);
        }

        private static int ComputeContentLayerMask(in Parameters parameters)
        {
            return (parameters.SourceTexture != null ? 1 : 0)
                   | (parameters.NormalTexture != null ? 2 : 0)
                   | (parameters.MaskTexture != null ? 4 : 0);
        }

        private static uint ComputeContentVersion(
            in Parameters parameters,
            int pageSize,
            int borderSize,
            int pageCountX,
            int pageCountY,
            int mipCount)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = AppendHash(hash, (int) parameters.BuildProfile);
                hash = AppendHash(hash, pageSize);
                hash = AppendHash(hash, borderSize);
                hash = AppendHash(hash, pageCountX);
                hash = AppendHash(hash, pageCountY);
                hash = AppendHash(hash, mipCount);
                hash = AppendHash(hash, (int) parameters.AddressMode);
                hash = AppendTextureHash(hash, parameters.SourceTexture);
                hash = AppendTextureHash(hash, parameters.NormalTexture);
                hash = AppendTextureHash(hash, parameters.MaskTexture);
                return hash != 0 ? hash : 1u;
            }
        }

        private static uint AppendTextureHash(uint hash, Texture2D texture)
        {
            if (texture == null)
                return AppendHash(hash, 0);

            hash = AppendHash(hash, texture.width);
            hash = AppendHash(hash, texture.height);
            hash = AppendHash(hash, (int) texture.graphicsFormat);
            return AppendHash(hash, texture.imageContentsHash.GetHashCode());
        }

        private static uint AppendHash(uint hash, int value)
        {
            unchecked
            {
                return (hash ^ (uint) value) * 16777619u;
            }
        }

        private static void Fill(Color32[] pixels, Color32 color)
        {
            for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
                pixels[pixelIndex] = color;
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

            Texture2D primaryTexture = ResolvePrimaryTexture(parameters);
            if (primaryTexture != null && !string.IsNullOrWhiteSpace(primaryTexture.name))
                return primaryTexture.name;

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

        internal static int ComputeGPUDrivenMipCount(int virtualPageCountX, int virtualPageCountY)
        {
            int minPageCount = Mathf.Max(1, Mathf.Min(virtualPageCountX, virtualPageCountY));
            int mipCount = 1;
            while ((minPageCount >>= 1) > 0 && mipCount < VirtualTextureFeedbackProcessor.MaxMipCount)
                mipCount += 1;

            return mipCount;
        }

        private static int GetDataByteCount(List<byte> rawData, FileStream stream)
        {
            return rawData != null ? rawData.Count : checked((int) stream.Position);
        }

        private static void AppendRGBA32(
            Color32[] pixels,
            byte[] pageBytes,
            List<byte> output,
            FileStream stream)
        {
            for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
            {
                int byteIndex = pixelIndex * 4;
                Color32 pixel = pixels[pixelIndex];
                pageBytes[byteIndex] = pixel.r;
                pageBytes[byteIndex + 1] = pixel.g;
                pageBytes[byteIndex + 2] = pixel.b;
                pageBytes[byteIndex + 3] = pixel.a;
            }

            if (stream != null)
                stream.Write(pageBytes, 0, pageBytes.Length);
            else
                output.AddRange(pageBytes);
        }
    }
}
