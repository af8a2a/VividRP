using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor
{
    internal static class VividVirtualTextureAssetBuilder
    {
        private const int GPUDrivenPageSize = 128;
        private const int GPUDrivenDefaultBorderSize = 4;
        private const int GPUDrivenHighQualityBorderSize = 8;
        private const int GPUDrivenMaxPageCount = 64;
        private const int StreamAlignment = 4096;
        private const int StreamHeaderByteSize = 32;
        private const string DesktopContentEncodingVersion = "DesktopContent-NormalRG-LinearCopy-v3";
        private static readonly byte[] s_StreamMagic = Encoding.ASCII.GetBytes("VIVIDVT2");
        private static IVTGpuStorageEncoder s_GpuStorageEncoder = new VTUnityBCnStorageEncoder();

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
            public VividVirtualTextureStorageProfile StorageProfile;
            public VividVirtualTextureStreamCompression StreamCompression;
            public VividVirtualTextureMaskStorage MaskStorage;
            public VividVirtualTextureBCQuality BCQuality;
            public int ZstdLevel;
            public int ChunkTargetKiB;
            public Action<string> LogWarningHandler;
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

            if (parameters.StorageProfile == VividVirtualTextureStorageProfile.DesktopBCn)
            {
                GenerateDesktopBCn(asset, builtData, parameters);
                return;
            }

            bool gpuDrivenSurface = parameters.BuildProfile == VividVirtualTextureBuildProfile.GPUDrivenSurface;
            int pageSize = gpuDrivenSurface ? GPUDrivenPageSize : Mathf.Max(1, parameters.PageSize);
            int borderSize = gpuDrivenSurface
                ? ResolveGPUDrivenBorderSize(parameters.BorderSize)
                : Mathf.Max(0, parameters.BorderSize);
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
                parameters.RuntimeStreamDataPath,
                containerSchemaVersion: 0,
                storageProfile: VividVirtualTextureStorageProfile.LegacyRGBA32);
            asset.Initialize(builtData);
        }

        internal static void SetGpuStorageEncoderForTesting(IVTGpuStorageEncoder encoder)
        {
            s_GpuStorageEncoder = encoder ?? new VTUnityBCnStorageEncoder();
        }

        internal static void ResetGpuStorageEncoderForTesting()
        {
            s_GpuStorageEncoder = new VTUnityBCnStorageEncoder();
        }

        private readonly struct DesktopTileBuildRecord
        {
            internal DesktopTileBuildRecord(int mip, int x, int y, int descriptorIndex)
            {
                Mip = mip;
                X = x;
                Y = y;
                DescriptorIndex = descriptorIndex;
            }

            internal int Mip { get; }

            internal int X { get; }

            internal int Y { get; }

            internal int DescriptorIndex { get; }
        }

        private sealed class DesktopChunkBuildPlan
        {
            internal readonly List<DesktopTileBuildRecord> Tiles = new();

            internal VividVirtualTextureChunkFlags Flags;

            internal int FirstMip => Tiles.Count > 0 ? Tiles[0].Mip : 0;

            internal int LastMip => Tiles.Count > 0 ? Tiles[^1].Mip : FirstMip;
        }

        private static void GenerateDesktopBCn(
            VividVirtualTextureAsset asset,
            VividVirtualTextureBuiltData builtData,
            in Parameters parameters)
        {
            if (string.IsNullOrWhiteSpace(parameters.StreamDataPath))
            {
                throw new ArgumentException(
                    "DesktopBCn virtual textures require an external stream data path.",
                    nameof(parameters));
            }

            bool gpuDrivenSurface = parameters.BuildProfile == VividVirtualTextureBuildProfile.GPUDrivenSurface;
            int pageSize = gpuDrivenSurface ? GPUDrivenPageSize : Mathf.Max(1, parameters.PageSize);
            int borderSize = gpuDrivenSurface
                ? ResolveGPUDrivenBorderSize(parameters.BorderSize)
                : Mathf.Max(0, parameters.BorderSize);
            int physicalPageSize = checked(pageSize + borderSize * 2);
            if ((physicalPageSize & 3) != 0)
                throw new ArgumentException("DesktopBCn physical page size must be 4x4 block aligned.", nameof(parameters));

            Texture2D primaryTexture = ResolvePrimaryTexture(parameters);
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
            VividVirtualTextureLayerDescriptor[] layers = CreateDesktopLayerDescriptors(parameters);
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

            VTTexture2DPageProducer[] producers = CreateDesktopLayerProducers(parameters);
            int[] sourceMipOffsets = CreateSourceMipOffsets(
                producers,
                gpuDrivenSurface,
                virtualPageCountX,
                virtualPageCountY,
                pageSize);
            int totalTileCount = VirtualTextureSpaceUtility.GetTotalPageCount(
                virtualPageCountX,
                virtualPageCountY,
                mipCount);
            int[] mipTileOffsets = new int[mipCount];
            int[] tileLookup = new int[totalTileCount];
            List<DesktopTileBuildRecord>[] recordsByMip = CreateMortonTileRecords(
                virtualPageCountX,
                virtualPageCountY,
                mipCount,
                mipTileOffsets,
                tileLookup);
            int tileDecodedByteSize = GetEncodedTileByteSize(layers, physicalPageSize);
            int chunkTargetBytes = Mathf.Clamp(
                parameters.ChunkTargetKiB > 0 ? parameters.ChunkTargetKiB : 256,
                128,
                256) * 1024;
            List<DesktopChunkBuildPlan> chunkPlans = CreateChunkPlans(
                recordsByMip,
                tileDecodedByteSize,
                chunkTargetBytes);
            uint contentVersion = ComputeContentVersion(
                parameters,
                pageSize,
                borderSize,
                virtualPageCountX,
                virtualPageCountY,
                mipCount);
            var tiles = new VividVirtualTextureTileDescriptor[totalTileCount];
            var chunks = new VividVirtualTextureChunkDescriptor[chunkPlans.Count];
            string streamDataPath = parameters.StreamDataPath.Replace('\\', '/');
            string directory = Path.GetDirectoryName(streamDataPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            using (var stream = new FileStream(
                       streamDataPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.Read,
                       bufferSize: 1024 * 1024,
                       FileOptions.SequentialScan))
            {
                WriteStreamHeader(stream, contentVersion, chunkPlans.Count);
                bool compressionWarningLogged = false;
                for (int chunkIndex = 0; chunkIndex < chunkPlans.Count; chunkIndex++)
                {
                    DesktopChunkBuildPlan plan = chunkPlans[chunkIndex];
                    byte[] decodedData = BuildEncodedChunk(
                        desc,
                        parameters,
                        layers,
                        producers,
                        sourceMipOffsets,
                        plan,
                        out int[] tileByteOffsets,
                        out int[] tileByteSizes);
                    uint crc = VTDecodedPayloadCRC.Compute(decodedData);
                    byte[] storedData = decodedData;
                    VividVirtualTextureStreamCompression storedCompression = VividVirtualTextureStreamCompression.None;
                    VividVirtualTextureStreamCompression requestedCompression = parameters.StreamCompression;
                    if (requestedCompression == VividVirtualTextureStreamCompression.Zstd)
                    {
                        IVTStreamCodec codec = VTStreamCodecRegistry.Get(requestedCompression);
                        string codecError = null;
                        if (codec != null
                            && codec.IsAvailable
                            && codec.TryEncode(
                                decodedData,
                                Mathf.Clamp(parameters.ZstdLevel > 0 ? parameters.ZstdLevel : 3, 1, 3),
                                out byte[] compressedData,
                                out codecError))
                        {
                            if (compressedData.Length < decodedData.Length)
                            {
                                storedData = compressedData;
                                storedCompression = requestedCompression;
                            }
                        }
                        else
                        {
                            if (!compressionWarningLogged)
                            {
                                compressionWarningLogged = true;
                                parameters.LogWarningHandler?.Invoke(
                                    codecError ?? "Zstd 1.5.7 is unavailable; storing BCn chunks without stream compression.");
                            }
                        }
                    }
                    else if (requestedCompression != VividVirtualTextureStreamCompression.None)
                    {
                        if (!compressionWarningLogged)
                        {
                            compressionWarningLogged = true;
                            parameters.LogWarningHandler?.Invoke(
                                $"Streaming codec {requestedCompression} is reserved but not implemented; storing raw BC blocks.");
                        }
                    }

                    AlignStream(stream, StreamAlignment);
                    long fileOffset = stream.Position;
                    stream.Write(storedData, 0, storedData.Length);
                    chunks[chunkIndex] = new VividVirtualTextureChunkDescriptor(
                        plan.FirstMip,
                        plan.LastMip - plan.FirstMip + 1,
                        plan.Tiles[0].DescriptorIndex,
                        plan.Tiles.Count,
                        fileOffset,
                        storedData.Length,
                        decodedData.Length,
                        storedCompression,
                        crc,
                        plan.Flags);

                    for (int tileIndex = 0; tileIndex < plan.Tiles.Count; tileIndex++)
                    {
                        DesktopTileBuildRecord record = plan.Tiles[tileIndex];
                        tiles[record.DescriptorIndex] = new VividVirtualTextureTileDescriptor(
                            record.Mip,
                            record.X,
                            record.Y,
                            chunkIndex,
                            tileByteOffsets[tileIndex],
                            tileByteSizes[tileIndex]);
                    }
                }
            }

            long streamDataByteSize64 = new FileInfo(streamDataPath).Length;
            int streamDataByteSize = streamDataByteSize64 >= int.MaxValue
                ? int.MaxValue
                : (int)streamDataByteSize64;
            builtData.Initialize(
                parameters.SourceTextureGUID,
                parameters.SourceTexturePath,
                pageSize,
                borderSize,
                virtualPageCountX,
                virtualPageCountY,
                mipCount,
                layers,
                chunks,
                tiles,
                mipTileOffsets,
                Array.Empty<byte>(),
                streamDataPath,
                streamDataByteSize,
                parameters.BuildProfile,
                ComputeContentLayerMask(parameters),
                contentVersion,
                parameters.AddressMode,
                parameters.RuntimeStreamDataPath,
                VividVirtualTextureBuiltData.CurrentContainerSchemaVersion,
                VividVirtualTextureStorageProfile.DesktopBCn,
                tileLookup,
                streamDataByteSize64,
                parameters.MaskStorage);
            asset.Initialize(builtData);
        }

        private static List<DesktopTileBuildRecord>[] CreateMortonTileRecords(
            int virtualPageCountX,
            int virtualPageCountY,
            int mipCount,
            int[] mipTileOffsets,
            int[] tileLookup)
        {
            var recordsByMip = new List<DesktopTileBuildRecord>[mipCount];
            int descriptorIndex = 0;
            int linearBase = 0;
            for (int mip = 0; mip < mipCount; mip++)
            {
                mipTileOffsets[mip] = linearBase;
                int pageCountX = VirtualTextureSpaceUtility.GetPageCountX(virtualPageCountX, mip);
                int pageCountY = VirtualTextureSpaceUtility.GetPageCountY(virtualPageCountY, mip);
                var coords = new List<Vector2Int>(pageCountX * pageCountY);
                for (int y = 0; y < pageCountY; y++)
                {
                    for (int x = 0; x < pageCountX; x++)
                        coords.Add(new Vector2Int(x, y));
                }

                coords.Sort((left, right) =>
                {
                    int mortonCompare = ComputeMortonCode(left.x, left.y).CompareTo(ComputeMortonCode(right.x, right.y));
                    if (mortonCompare != 0)
                        return mortonCompare;
                    int yCompare = left.y.CompareTo(right.y);
                    return yCompare != 0 ? yCompare : left.x.CompareTo(right.x);
                });

                var records = new List<DesktopTileBuildRecord>(coords.Count);
                for (int coordIndex = 0; coordIndex < coords.Count; coordIndex++)
                {
                    Vector2Int coord = coords[coordIndex];
                    records.Add(new DesktopTileBuildRecord(mip, coord.x, coord.y, descriptorIndex));
                    tileLookup[linearBase + coord.y * pageCountX + coord.x] = descriptorIndex;
                    descriptorIndex += 1;
                }

                recordsByMip[mip] = records;
                linearBase += coords.Count;
            }

            return recordsByMip;
        }

        private static List<DesktopChunkBuildPlan> CreateChunkPlans(
            IReadOnlyList<List<DesktopTileBuildRecord>> recordsByMip,
            int tileByteSize,
            int targetByteSize)
        {
            int tailStartMip = Mathf.Max(0, recordsByMip.Count - 1);
            long tailByteSize = recordsByMip.Count > 0
                ? (long)recordsByMip[tailStartMip].Count * tileByteSize
                : 0;
            for (int mip = recordsByMip.Count - 2; mip >= 0; mip--)
            {
                long candidateByteSize = tailByteSize + (long)recordsByMip[mip].Count * tileByteSize;
                if (candidateByteSize > targetByteSize)
                    break;

                tailByteSize = candidateByteSize;
                tailStartMip = mip;
            }

            var plans = new List<DesktopChunkBuildPlan>();
            int maxTilesPerChunk = Mathf.Max(1, targetByteSize / Mathf.Max(1, tileByteSize));
            for (int mip = 0; mip < tailStartMip; mip++)
                AppendChunkPlans(recordsByMip[mip], maxTilesPerChunk, VividVirtualTextureChunkFlags.None, plans);

            if (recordsByMip.Count > 0)
            {
                var tailRecords = new List<DesktopTileBuildRecord>();
                for (int mip = tailStartMip; mip < recordsByMip.Count; mip++)
                    tailRecords.AddRange(recordsByMip[mip]);
                AppendChunkPlans(tailRecords, maxTilesPerChunk, VividVirtualTextureChunkFlags.MipTail, plans);
            }

            return plans;
        }

        private static void AppendChunkPlans(
            IReadOnlyList<DesktopTileBuildRecord> records,
            int maxTilesPerChunk,
            VividVirtualTextureChunkFlags flags,
            ICollection<DesktopChunkBuildPlan> output)
        {
            for (int firstTile = 0; firstTile < records.Count; firstTile += maxTilesPerChunk)
            {
                var plan = new DesktopChunkBuildPlan { Flags = flags };
                int tileCount = Mathf.Min(maxTilesPerChunk, records.Count - firstTile);
                for (int tileIndex = 0; tileIndex < tileCount; tileIndex++)
                    plan.Tiles.Add(records[firstTile + tileIndex]);
                output.Add(plan);
            }
        }

        private static byte[] BuildEncodedChunk(
            in VirtualTextureSpaceDesc desc,
            in Parameters parameters,
            VividVirtualTextureLayerDescriptor[] layers,
            VTTexture2DPageProducer[] producers,
            int[] sourceMipOffsets,
            DesktopChunkBuildPlan plan,
            out int[] tileByteOffsets,
            out int[] tileByteSizes)
        {
            if (producers == null || producers.Length != layers.Length)
            {
                throw new InvalidOperationException(
                    $"DesktopBCn VT layer/producer mismatch: {layers.Length} layers and "
                    + $"{producers?.Length ?? 0} producers.");
            }

            if (sourceMipOffsets == null || sourceMipOffsets.Length != layers.Length)
            {
                throw new InvalidOperationException(
                    $"DesktopBCn VT layer/source-mip mismatch: {layers.Length} layers and "
                    + $"{sourceMipOffsets?.Length ?? 0} source mip offsets.");
            }

            int pixelCount = checked(desc.PhysicalPageSize * desc.PhysicalPageSize);
            var encodedLayers = new byte[layers.Length][][];
            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                var pages = new List<Color32[]>(plan.Tiles.Count);
                for (int tileIndex = 0; tileIndex < plan.Tiles.Count; tileIndex++)
                {
                    DesktopTileBuildRecord record = plan.Tiles[tileIndex];
                    var pixels = new Color32[pixelCount];
                    if (producers[layerIndex] != null)
                    {
                        var request = new VTRequest(
                            0,
                            new VirtualTexturePageCoord(record.X, record.Y, record.Mip),
                            physicalPageId: 0,
                            generation: 0,
                            priority: 0,
                            requestFrame: 0);
                        bool repeat = parameters.BuildProfile == VividVirtualTextureBuildProfile.GPUDrivenSurface
                                      && parameters.AddressMode == VividVirtualTextureAddressMode.Repeat;
                        producers[layerIndex].WritePage(
                            desc,
                            request,
                            pixels,
                            repeat,
                            sourceMipOffsets[layerIndex]);
                        ConvertLayerEncoding(pixels, layers[layerIndex].Encoding);
                    }
                    else
                    {
                        Fill(pixels, layers[layerIndex].FallbackColor);
                    }
                    pages.Add(pixels);
                }

                if (!s_GpuStorageEncoder.TryEncodePages(
                        layers[layerIndex].Format,
                        desc.PhysicalPageSize,
                        pages,
                        parameters.BCQuality,
                        out encodedLayers[layerIndex],
                        out string error))
                {
                    throw new InvalidOperationException($"Failed to encode VT layer {layerIndex}: {error}");
                }

                if (encodedLayers[layerIndex] == null
                    || encodedLayers[layerIndex].Length != plan.Tiles.Count)
                {
                    throw new InvalidOperationException(
                        $"VT storage encoder returned {encodedLayers[layerIndex]?.Length ?? 0} pages for "
                        + $"layer {layerIndex}; expected {plan.Tiles.Count}.");
                }
            }

            int tileByteSize = 0;
            for (int layerIndex = 0; layerIndex < encodedLayers.Length; layerIndex++)
                tileByteSize = checked(tileByteSize + encodedLayers[layerIndex][0].Length);
            var decodedData = new byte[checked(tileByteSize * plan.Tiles.Count)];
            tileByteOffsets = new int[plan.Tiles.Count];
            tileByteSizes = new int[plan.Tiles.Count];
            int destinationOffset = 0;
            for (int tileIndex = 0; tileIndex < plan.Tiles.Count; tileIndex++)
            {
                tileByteOffsets[tileIndex] = destinationOffset;
                for (int layerIndex = 0; layerIndex < encodedLayers.Length; layerIndex++)
                {
                    byte[] encodedPage = encodedLayers[layerIndex][tileIndex];
                    Buffer.BlockCopy(encodedPage, 0, decodedData, destinationOffset, encodedPage.Length);
                    destinationOffset += encodedPage.Length;
                }

                tileByteSizes[tileIndex] = tileByteSize;
            }

            return decodedData;
        }

        private static void ConvertLayerEncoding(Color32[] pixels, VTLayerDataEncoding encoding)
        {
            if (encoding == VTLayerDataEncoding.NormalRG)
            {
                for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
                    pixels[pixelIndex] = ConvertNormalToCanonicalRG(pixels[pixelIndex]);
            }
            else if (encoding == VTLayerDataEncoding.SingleChannelR)
            {
                for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
                {
                    byte value = pixels[pixelIndex].r;
                    pixels[pixelIndex] = new Color32(value, value, value, 255);
                }
            }
        }

        internal static Color32 ConvertNormalToCanonicalRG(Color32 source)
        {
            // Match Unity's UnpackNormalmapRGorAG contract: BC5 stores X in R
            // with A=1, while legacy DXT5nm stores X in A with R=1.
            byte normalX = (byte)((source.r * source.a + 127) / 255);
            return new Color32(normalX, source.g, 0, 255);
        }

        private static int GetEncodedTileByteSize(
            IReadOnlyList<VividVirtualTextureLayerDescriptor> layers,
            int physicalPageSize)
        {
            int byteSize = 0;
            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
                byteSize = checked(byteSize + VTUnityBCnStorageEncoder.GetPageByteSize(layers[layerIndex].Format, physicalPageSize));
            return byteSize;
        }

        private static uint ComputeMortonCode(int x, int y)
        {
            uint morton = 0;
            for (int bit = 0; bit < 16; bit++)
            {
                morton |= ((uint)x >> bit & 1u) << (bit * 2);
                morton |= ((uint)y >> bit & 1u) << (bit * 2 + 1);
            }

            return morton;
        }

        private static void WriteStreamHeader(Stream stream, uint contentVersion, int chunkCount)
        {
            stream.Write(s_StreamMagic, 0, s_StreamMagic.Length);
            WriteInt32(stream, VividVirtualTextureBuiltData.CurrentContainerSchemaVersion);
            WriteUInt32(stream, contentVersion);
            WriteInt32(stream, chunkCount);
            WriteInt32(stream, 0);
            WriteInt32(stream, 0);
            WriteInt32(stream, 0);
            if (stream.Position != StreamHeaderByteSize)
                throw new InvalidOperationException("VT stream header layout changed unexpectedly.");
        }

        private static void WriteInt32(Stream stream, int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteUInt32(Stream stream, uint value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void AlignStream(Stream stream, int alignment)
        {
            int padding = checked((int)((alignment - stream.Position % alignment) % alignment));
            if (padding > 0)
                stream.Write(new byte[padding], 0, padding);
        }

        private static VividVirtualTextureLayerDescriptor[] CreateDesktopLayerDescriptors(in Parameters parameters)
        {
            bool gpuDrivenSurface = parameters.BuildProfile == VividVirtualTextureBuildProfile.GPUDrivenSurface;
            var layers = new List<VividVirtualTextureLayerDescriptor>();
            if (gpuDrivenSurface)
            {
                layers.Add(new VividVirtualTextureLayerDescriptor(
                    VTLayerSemantic.BaseColor,
                    GraphicsFormat.RGBA_BC7_SRGB,
                    sRGB: true,
                    new Color32(255, 255, 255, 255),
                    physicalGroup: 0,
                    VTLayerDataEncoding.RGBA));
                layers.Add(new VividVirtualTextureLayerDescriptor(
                    VTLayerSemantic.Normal,
                    GraphicsFormat.RG_BC5_UNorm,
                    sRGB: false,
                    new Color32(128, 128, 255, 128),
                    physicalGroup: 1,
                    VTLayerDataEncoding.NormalRG));
                layers.Add(new VividVirtualTextureLayerDescriptor(
                    VTLayerSemantic.Mask,
                    GraphicsFormat.RGBA_BC7_UNorm,
                    sRGB: false,
                    new Color32(255, 255, 255, 255),
                    physicalGroup: 2,
                    VTLayerDataEncoding.RGBA));
                layers.Add(new VividVirtualTextureLayerDescriptor(
                    VTLayerSemantic.Height,
                    GraphicsFormat.R_BC4_UNorm,
                    sRGB: false,
                    new Color32(255, 255, 255, 255),
                    physicalGroup: 3,
                    VTLayerDataEncoding.SingleChannelR));
                return layers.ToArray();
            }

            if (gpuDrivenSurface || parameters.SourceTexture != null)
            {
                layers.Add(new VividVirtualTextureLayerDescriptor(
                    VTLayerSemantic.BaseColor,
                    GraphicsFormat.RGBA_BC7_SRGB,
                    sRGB: true,
                    gpuDrivenSurface ? new Color32(255, 255, 255, 255) : parameters.FallbackColor,
                    physicalGroup: 0,
                    VTLayerDataEncoding.RGBA));
            }

            if (gpuDrivenSurface || parameters.NormalTexture != null)
            {
                layers.Add(new VividVirtualTextureLayerDescriptor(
                    VTLayerSemantic.Normal,
                    GraphicsFormat.RG_BC5_UNorm,
                    sRGB: false,
                    ResolveFallback(parameters.NormalFallbackColor, new Color32(128, 128, 255, 128)),
                    physicalGroup: 1,
                    VTLayerDataEncoding.NormalRG));
            }

            if (gpuDrivenSurface || parameters.MaskTexture != null)
            {
                bool scalarMask = parameters.MaskStorage == VividVirtualTextureMaskStorage.SingleChannelR;
                layers.Add(new VividVirtualTextureLayerDescriptor(
                    VTLayerSemantic.Mask,
                    scalarMask ? GraphicsFormat.R_BC4_UNorm : GraphicsFormat.RGBA_BC7_UNorm,
                    sRGB: false,
                    ResolveFallback(parameters.MaskFallbackColor, new Color32(255, 255, 255, 255)),
                    physicalGroup: 2,
                    scalarMask ? VTLayerDataEncoding.SingleChannelR : VTLayerDataEncoding.RGBA));
            }

            return layers.ToArray();
        }

        private static VTTexture2DPageProducer[] CreateDesktopLayerProducers(in Parameters parameters)
        {
            if (parameters.BuildProfile != VividVirtualTextureBuildProfile.GPUDrivenSurface)
                return CreateLayerProducers(parameters);

            bool scalarMask = parameters.MaskStorage == VividVirtualTextureMaskStorage.SingleChannelR;
            return new[]
            {
                CreateProducer(parameters.SourceTexture),
                CreateProducer(parameters.NormalTexture),
                scalarMask ? null : CreateProducer(parameters.MaskTexture),
                scalarMask ? CreateProducer(parameters.MaskTexture) : null,
            };
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
                        physicalGroup: 0,
                        VTLayerDataEncoding.RGBA),
                    new VividVirtualTextureLayerDescriptor(
                        VTLayerSemantic.Normal,
                        GraphicsFormat.R8G8B8A8_UNorm,
                        sRGB: false,
                        new Color32(128, 128, 255, 128),
                        physicalGroup: 0,
                        VTLayerDataEncoding.LegacyNormalAG),
                    new VividVirtualTextureLayerDescriptor(
                        VTLayerSemantic.Mask,
                        GraphicsFormat.R8G8B8A8_UNorm,
                        sRGB: false,
                        new Color32(255, 255, 255, 255),
                        physicalGroup: 0,
                        VTLayerDataEncoding.RGBA),
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
                    physicalGroup: 0,
                    VTLayerDataEncoding.LegacyNormalAG));
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
                    layer.PhysicalGroup,
                    layer.Encoding);
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
            // UnityEngine.Object overloads == so a missing serialized reference can be
            // Unity-null while its managed wrapper is still non-null. The C# ?? operator
            // only checks the managed reference and would select that invalid object ahead
            // of a valid normal or mask texture (notably for mask-only terrain control VTs).
            if (parameters.SourceTexture != null)
                return parameters.SourceTexture;
            if (parameters.NormalTexture != null)
                return parameters.NormalTexture;
            return parameters.MaskTexture != null ? parameters.MaskTexture : null;
        }

        private static int ResolveGPUDrivenBorderSize(int requestedBorderSize)
        {
            return requestedBorderSize >= GPUDrivenHighQualityBorderSize
                ? GPUDrivenHighQualityBorderSize
                : GPUDrivenDefaultBorderSize;
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
                hash = AppendHash(hash, (int) parameters.StorageProfile);
                hash = AppendHash(hash, (int) parameters.MaskStorage);
                hash = AppendHash(hash, (int) parameters.BCQuality);
                hash = AppendHash(hash, (int) parameters.StreamCompression);
                if (parameters.StreamCompression == VividVirtualTextureStreamCompression.Zstd)
                    hash = AppendStringHash(hash, "Zstd-1.5.7");
                hash = AppendHash(hash, Mathf.Clamp(parameters.ZstdLevel > 0 ? parameters.ZstdLevel : 3, 1, 3));
                hash = AppendHash(hash, Mathf.Clamp(parameters.ChunkTargetKiB > 0 ? parameters.ChunkTargetKiB : 256, 128, 256));
                if (parameters.StorageProfile == VividVirtualTextureStorageProfile.DesktopBCn)
                {
                    hash = AppendStringHash(hash, DesktopContentEncodingVersion);
                    VividVirtualTextureLayerDescriptor[] contentLayers = CreateDesktopLayerDescriptors(parameters);
                    hash = AppendHash(hash, contentLayers.Length);
                    for (int layerIndex = 0; layerIndex < contentLayers.Length; layerIndex++)
                    {
                        VividVirtualTextureLayerDescriptor layer = contentLayers[layerIndex];
                        hash = AppendHash(hash, (int)layer.Semantic);
                        hash = AppendHash(hash, (int)layer.Format);
                        hash = AppendHash(hash, layer.SRGB ? 1 : 0);
                        hash = AppendHash(hash, layer.PhysicalGroup);
                        hash = AppendHash(hash, (int)layer.Encoding);
                    }

                    hash = AppendStringHash(hash, s_GpuStorageEncoder?.Version);
                }
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

        private static uint AppendStringHash(uint hash, string value)
        {
            if (string.IsNullOrEmpty(value))
                return AppendHash(hash, 0);

            hash = AppendHash(hash, value.Length);
            for (int index = 0; index < value.Length; index++)
                hash = AppendHash(hash, value[index]);
            return hash;
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
