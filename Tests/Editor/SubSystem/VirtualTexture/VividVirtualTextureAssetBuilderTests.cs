using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using VividRP.Editor;
using VividRP.Runtime;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class VividVirtualTextureAssetBuilderTests
    {
        private const string TempFolder = "Assets/VividVirtualTextureImporterTests";
        private readonly List<string> m_TempFiles = new();

        [TearDown]
        public void TearDown()
        {
            VividVirtualTextureAssetProducer.ResetStreamReadHandlersForTesting();
            AssetDatabase.DeleteAsset(TempFolder);
            for (int index = 0; index < m_TempFiles.Count; index++)
            {
                string path = m_TempFiles[index];
                if (File.Exists(path))
                    File.Delete(path);
            }

            m_TempFiles.Clear();
        }

        [Test]
        public void Generate_BuildsTileChunkAndFallbackMetadata()
        {
            Texture2D sourceTexture = CreateSourceTexture(4, 4, readable: true);
            VividVirtualTextureAsset asset = ScriptableObject.CreateInstance<VividVirtualTextureAsset>();
            VividVirtualTextureBuiltData builtData = ScriptableObject.CreateInstance<VividVirtualTextureBuiltData>();
            var fallbackColor = new Color32(9, 8, 7, 255);

            try
            {
                asset.name = "BuiltMetadata";
                VividVirtualTextureAssetBuilder.Generate(asset, builtData, new VividVirtualTextureAssetBuilder.Parameters
                {
                    SourceTexture = sourceTexture,
                    SourceTextureGUID = "source-guid",
                    SourceTexturePath = "Assets/Source.png",
                    PageSize = 2,
                    BorderSize = 1,
                    MipCount = 2,
                    FallbackColor = fallbackColor,
                });

                Assert.That(asset.BuiltData, Is.SameAs(builtData));
                Assert.That(asset.SourceTextureGUID, Is.EqualTo("source-guid"));
                Assert.That(asset.SourceTexturePath, Is.EqualTo("Assets/Source.png"));
                Assert.That(asset.PageSize, Is.EqualTo(2));
                Assert.That(asset.BorderSize, Is.EqualTo(1));
                Assert.That(asset.MipCount, Is.EqualTo(2));
                Assert.That(asset.VirtualPageCountX, Is.EqualTo(2));
                Assert.That(asset.VirtualPageCountY, Is.EqualTo(2));
                Assert.That(asset.ChunkCount, Is.EqualTo(2));
                Assert.That(asset.TileCount, Is.EqualTo(5));
                Assert.That(builtData.FallbackColor, Is.EqualTo(fallbackColor));
                Assert.That(builtData.GraphicsFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));

                int tileByteSize = builtData.PhysicalPageSize * builtData.PhysicalPageSize * 4;
                Assert.That(builtData.RawDataByteSize, Is.EqualTo(asset.TileCount * tileByteSize));
                Assert.That(builtData.Chunks[0].FirstMip, Is.EqualTo(0));
                Assert.That(builtData.Chunks[0].ByteOffset, Is.EqualTo(0));
                Assert.That(builtData.Chunks[0].ByteSize, Is.EqualTo(4 * tileByteSize));
                Assert.That(builtData.Chunks[1].FirstMip, Is.EqualTo(1));
                Assert.That(builtData.Chunks[1].ByteOffset, Is.EqualTo(4 * tileByteSize));
                Assert.That(builtData.Chunks[1].ByteSize, Is.EqualTo(tileByteSize));

                Assert.That(builtData.TryGetTileDescriptor(
                    new VirtualTexturePageCoord(1, 0, 0),
                    out VividVirtualTextureTileDescriptor tile), Is.True);
                Assert.That(tile.Mip, Is.EqualTo(0));
                Assert.That(tile.X, Is.EqualTo(1));
                Assert.That(tile.Y, Is.EqualTo(0));
                Assert.That(tile.ChunkIndex, Is.EqualTo(0));
                Assert.That(tile.ByteOffset, Is.EqualTo(tileByteSize));
                Assert.That(tile.ByteSize, Is.EqualTo(tileByteSize));

                foreach (VividVirtualTextureTileDescriptor builtTile in builtData.Tiles)
                {
                    VividVirtualTextureChunkDescriptor chunk = builtData.Chunks[builtTile.ChunkIndex];
                    Assert.That(chunk.ContainsMip(builtTile.Mip), Is.True);
                    Assert.That(chunk.ContainsByteRange(builtTile.ByteOffset, builtTile.ByteSize), Is.True);
                }
            }
            finally
            {
                Object.DestroyImmediate(sourceTexture);
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(builtData);
            }
        }

        [Test]
        public void Generate_WritesStreamDataToExternalFile_WhenStreamPathProvided()
        {
            Texture2D sourceTexture = CreateSourceTexture(4, 4, readable: true);
            VividVirtualTextureAsset asset = ScriptableObject.CreateInstance<VividVirtualTextureAsset>();
            VividVirtualTextureBuiltData builtData = ScriptableObject.CreateInstance<VividVirtualTextureBuiltData>();
            string streamDataPath = CreateTempStreamDataPath();

            try
            {
                VividVirtualTextureAssetBuilder.Generate(asset, builtData, new VividVirtualTextureAssetBuilder.Parameters
                {
                    SourceTexture = sourceTexture,
                    SourceTextureGUID = "stream-source-guid",
                    SourceTexturePath = "Assets/StreamSource.png",
                    PageSize = 2,
                    BorderSize = 1,
                    MipCount = 2,
                    FallbackColor = new Color32(1, 2, 3, 255),
                    StreamDataPath = streamDataPath,
                });

                Assert.That(File.Exists(streamDataPath), Is.True);
                Assert.That(new FileInfo(streamDataPath).Length, Is.EqualTo(builtData.RawDataByteSize));
                Assert.That(builtData.HasStreamData, Is.True);
                Assert.That(builtData.HasInlineRawData, Is.False);
                Assert.That(builtData.StreamDataPath, Is.EqualTo(streamDataPath.Replace('\\', '/')));
                Assert.That(builtData.StreamDataByteSize, Is.EqualTo(builtData.RawDataByteSize));
                Assert.That(builtData.TryGetTilePayload(new VirtualTexturePageCoord(0, 0, 0), out _), Is.False);
                Assert.That(builtData.TryGetTilePayloadLocation(
                    new VirtualTexturePageCoord(1, 0, 0),
                    out VividVirtualTextureTilePayloadLocation location), Is.True);

                int tileByteSize = builtData.PhysicalPageSize * builtData.PhysicalPageSize * 4;
                Assert.That(location.ByteOffset, Is.EqualTo(tileByteSize));
                Assert.That(location.ByteSize, Is.EqualTo(tileByteSize));
            }
            finally
            {
                Object.DestroyImmediate(sourceTexture);
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(builtData);
            }
        }

        [Test]
        public void Generate_BuildsBaseColorAndNormalLayers()
        {
            Texture2D sourceTexture = CreateSourceTexture(4, 4, readable: true);
            Texture2D normalTexture = CreateOffsetSourceTexture(4, 4, readable: true, offset: 80);
            VividVirtualTextureAsset asset = ScriptableObject.CreateInstance<VividVirtualTextureAsset>();
            VividVirtualTextureBuiltData builtData = ScriptableObject.CreateInstance<VividVirtualTextureBuiltData>();
            var baseFallback = new Color32(9, 8, 7, 255);
            var normalFallback = new Color32(128, 128, 255, 255);
            var coord = new VirtualTexturePageCoord(1, 0, 0);

            try
            {
                VividVirtualTextureAssetBuilder.Generate(asset, builtData, new VividVirtualTextureAssetBuilder.Parameters
                {
                    SourceTexture = sourceTexture,
                    NormalTexture = normalTexture,
                    PageSize = 2,
                    BorderSize = 1,
                    MipCount = 2,
                    FallbackColor = baseFallback,
                    NormalFallbackColor = normalFallback,
                });

                Assert.That(builtData.LayerCount, Is.EqualTo(2));
                Assert.That(builtData.Layers[0].Semantic, Is.EqualTo(VTLayerSemantic.BaseColor));
                Assert.That(builtData.Layers[0].FallbackColor, Is.EqualTo(baseFallback));
                Assert.That(builtData.Layers[1].Semantic, Is.EqualTo(VTLayerSemantic.Normal));
                Assert.That(builtData.Layers[1].SRGB, Is.False);
                Assert.That(builtData.Layers[1].FallbackColor, Is.EqualTo(normalFallback));

                int layerByteSize = builtData.PhysicalPageSize * builtData.PhysicalPageSize * 4;
                Assert.That(builtData.RawDataByteSize, Is.EqualTo(asset.TileCount * layerByteSize * 2));
                Assert.That(builtData.TryGetTileDescriptor(coord, out VividVirtualTextureTileDescriptor tile), Is.True);
                Assert.That(tile.ByteSize, Is.EqualTo(layerByteSize * 2));

                VirtualTextureSpaceDesc desc = builtData.CreateSpaceDesc(
                    "LayeredRuntimeProducer",
                    cachePageCount: 2,
                    maxUploadsPerFrame: 1,
                    feedbackCapacity: 16);
                Assert.That(desc.StackDesc.LayerCount, Is.EqualTo(2));
                Assert.That(desc.StackDesc.TryGetLayerIndex(VTLayerSemantic.Normal, out int normalLayerIndex), Is.True);
                Assert.That(normalLayerIndex, Is.EqualTo(1));

                var request = new VTRequest(1, coord, 0, 1, 1, 0);
                var expectedBaseProducer = new VTTexture2DPageProducer(sourceTexture);
                var expectedNormalProducer = new VTTexture2DPageProducer(normalTexture);
                var expectedBasePixels = new Color32[desc.PhysicalPageSize * desc.PhysicalPageSize];
                var expectedNormalPixels = new Color32[expectedBasePixels.Length];
                expectedBaseProducer.WritePage(desc, request, expectedBasePixels);
                expectedNormalProducer.WritePage(desc, request, expectedNormalPixels);

                var producer = new VividVirtualTextureAssetProducer(asset);
                IVTPageFinalizer finalizer = producer.ProducePageData(desc, request);
                Assert.That(finalizer, Is.InstanceOf<IVTMultiLayerPageFinalizer>());
                var multiLayerFinalizer = (IVTMultiLayerPageFinalizer)finalizer;
                Assert.That(multiLayerFinalizer.LayerCount, Is.EqualTo(2));

                var stagingTexture = new Texture2DArray(
                    desc.PhysicalPageSize,
                    desc.PhysicalPageSize,
                    2,
                    desc.GraphicsFormat,
                    TextureCreationFlags.None);
                var scratchPixels = new Color32[expectedBasePixels.Length];

                try
                {
                    multiLayerFinalizer.FinalizeUploadLayer(stagingTexture, 0, 0, scratchPixels);
                    multiLayerFinalizer.FinalizeUploadLayer(stagingTexture, 1, 1, scratchPixels);
                    stagingTexture.Apply(false, false);

                    Assert.That(stagingTexture.GetPixels32(0, 0), Is.EqualTo(expectedBasePixels));
                    Assert.That(stagingTexture.GetPixels32(1, 0), Is.EqualTo(expectedNormalPixels));
                }
                finally
                {
                    finalizer.Dispose();
                    producer.Dispose();
                    Object.DestroyImmediate(stagingTexture);
                }
            }
            finally
            {
                Object.DestroyImmediate(sourceTexture);
                Object.DestroyImmediate(normalTexture);
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(builtData);
            }
        }

        [Test]
        public void AssetProducer_StreamsTilePayloadAsynchronously_AndDeduplicatesRequests()
        {
            Texture2D sourceTexture = CreateSourceTexture(4, 4, readable: true);
            VividVirtualTextureAsset asset = ScriptableObject.CreateInstance<VividVirtualTextureAsset>();
            VividVirtualTextureBuiltData builtData = ScriptableObject.CreateInstance<VividVirtualTextureBuiltData>();
            string streamDataPath = CreateTempStreamDataPath();
            var coord = new VirtualTexturePageCoord(1, 0, 0);

            try
            {
                VividVirtualTextureAssetBuilder.Generate(asset, builtData, new VividVirtualTextureAssetBuilder.Parameters
                {
                    SourceTexture = sourceTexture,
                    PageSize = 2,
                    BorderSize = 1,
                    MipCount = 2,
                    FallbackColor = new Color32(0, 0, 0, 255),
                    StreamDataPath = streamDataPath,
                });

                VirtualTextureSpaceDesc desc = builtData.CreateSpaceDesc(
                    "AsyncStreamProducer",
                    cachePageCount: 2,
                    maxUploadsPerFrame: 1,
                    feedbackCapacity: 16);
                var expectedProducer = new VTTexture2DPageProducer(sourceTexture);
                var request = new VTRequest(1, coord, 0, 1, 7, 0);
                var expectedPixels = new Color32[desc.PhysicalPageSize * desc.PhysicalPageSize];
                expectedProducer.WritePage(desc, request, expectedPixels);
                byte[] expectedBytes = ToRGBA32Bytes(expectedPixels);
                Object.DestroyImmediate(sourceTexture);
                sourceTexture = null;

                int readCount = 0;
                var completionSource = new TaskCompletionSource<byte[]>();
                VividVirtualTextureAssetProducer.SetStreamReadHandlersForTesting(
                    (path, byteOffset, byteSize, cancellationToken) =>
                    {
                        readCount += 1;
                        Assert.That(path, Is.EqualTo(Path.GetFullPath(streamDataPath)));
                        Assert.That(byteSize, Is.EqualTo(expectedBytes.Length));
                        cancellationToken.Register(() => completionSource.TrySetCanceled());
                        return completionSource.Task;
                    });

                var producer = new VividVirtualTextureAssetProducer(asset);
                Assert.That(producer.RequestPageData(desc, request), Is.EqualTo(VTPageRequestStatus.Pending));
                Assert.That(producer.RequestPageData(desc, request), Is.EqualTo(VTPageRequestStatus.Pending));
                Assert.That(readCount, Is.EqualTo(1));
                Assert.That(producer.PendingStreamTaskCountForTesting, Is.EqualTo(1));

                var tasks = new List<IVTPageProducerTask>();
                producer.GatherTasks(tasks);
                Assert.That(tasks, Has.Count.EqualTo(1));
                Assert.That(tasks[0].IsCompleted, Is.False);

                completionSource.SetResult(expectedBytes);
                Assert.That(producer.RequestPageData(desc, request), Is.EqualTo(VTPageRequestStatus.Available));
                IVTPageFinalizer finalizer = producer.ProducePageData(desc, request);
                Assert.That(finalizer, Is.Not.Null);
                Assert.That(producer.PendingStreamTaskCountForTesting, Is.EqualTo(0));

                var stagingTexture = new Texture2DArray(
                    desc.PhysicalPageSize,
                    desc.PhysicalPageSize,
                    1,
                    desc.GraphicsFormat,
                    TextureCreationFlags.None);
                var scratchPixels = new Color32[expectedPixels.Length];

                try
                {
                    finalizer.FinalizeUpload(stagingTexture, 0, scratchPixels);
                    stagingTexture.Apply(false, false);
                    Assert.That(stagingTexture.GetPixels32(0, 0), Is.EqualTo(expectedPixels));
                }
                finally
                {
                    finalizer.Dispose();
                    producer.Dispose();
                    Object.DestroyImmediate(stagingTexture);
                }
            }
            finally
            {
                if (sourceTexture != null)
                    Object.DestroyImmediate(sourceTexture);
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(builtData);
            }
        }

        [Test]
        public void AssetProducer_RetiresStaleStreamTasks()
        {
            Texture2D sourceTexture = CreateSourceTexture(4, 4, readable: true);
            VividVirtualTextureAsset asset = ScriptableObject.CreateInstance<VividVirtualTextureAsset>();
            VividVirtualTextureBuiltData builtData = ScriptableObject.CreateInstance<VividVirtualTextureBuiltData>();
            string streamDataPath = CreateTempStreamDataPath();

            try
            {
                VividVirtualTextureAssetBuilder.Generate(asset, builtData, new VividVirtualTextureAssetBuilder.Parameters
                {
                    SourceTexture = sourceTexture,
                    PageSize = 2,
                    BorderSize = 1,
                    MipCount = 2,
                    FallbackColor = new Color32(0, 0, 0, 255),
                    StreamDataPath = streamDataPath,
                });

                var pendingTasks = new List<TaskCompletionSource<byte[]>>();
                VividVirtualTextureAssetProducer.SetStreamReadHandlersForTesting(
                    (path, byteOffset, byteSize, cancellationToken) =>
                    {
                        var completionSource = new TaskCompletionSource<byte[]>();
                        cancellationToken.Register(() => completionSource.TrySetCanceled());
                        pendingTasks.Add(completionSource);
                        return completionSource.Task;
                    });

                VirtualTextureSpaceDesc desc = builtData.CreateSpaceDesc(
                    "RetireStreamProducer",
                    cachePageCount: 2,
                    maxUploadsPerFrame: 1,
                    feedbackCapacity: 16);
                var producer = new VividVirtualTextureAssetProducer(asset);
                var liveRequest = new VTRequest(1, new VirtualTexturePageCoord(0, 0, 0), 0, 1, 1, 0);
                var staleRequest = new VTRequest(1, new VirtualTexturePageCoord(1, 0, 0), 1, 2, 1, 0);

                try
                {
                    Assert.That(producer.RequestPageData(desc, liveRequest), Is.EqualTo(VTPageRequestStatus.Pending));
                    Assert.That(producer.RequestPageData(desc, staleRequest), Is.EqualTo(VTPageRequestStatus.Pending));
                    Assert.That(producer.PendingStreamTaskCountForTesting, Is.EqualTo(2));

                    producer.RetireRequests(new[] { liveRequest });

                    Assert.That(producer.PendingStreamTaskCountForTesting, Is.EqualTo(1));
                    Assert.That(pendingTasks[1].Task.IsCanceled, Is.True);
                }
                finally
                {
                    producer.Dispose();
                }
            }
            finally
            {
                Object.DestroyImmediate(sourceTexture);
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(builtData);
            }
        }

        [Test]
        public void AssetProducer_WritesBakedPage_WhenSourceTextureIsGone()
        {
            Texture2D sourceTexture = CreateSourceTexture(4, 4, readable: true);
            VividVirtualTextureAsset asset = ScriptableObject.CreateInstance<VividVirtualTextureAsset>();
            VividVirtualTextureBuiltData builtData = ScriptableObject.CreateInstance<VividVirtualTextureBuiltData>();
            var coord = new VirtualTexturePageCoord(1, 0, 0);

            try
            {
                asset.name = "RuntimeProducer";
                VividVirtualTextureAssetBuilder.Generate(asset, builtData, new VividVirtualTextureAssetBuilder.Parameters
                {
                    SourceTexture = sourceTexture,
                    PageSize = 2,
                    BorderSize = 1,
                    MipCount = 2,
                    FallbackColor = new Color32(0, 0, 0, 255),
                });

                VirtualTextureSpaceDesc desc = builtData.CreateSpaceDesc(
                    "RuntimeProducer",
                    cachePageCount: 2,
                    maxUploadsPerFrame: 1,
                    feedbackCapacity: 16);
                var expectedProducer = new VTTexture2DPageProducer(sourceTexture);
                var request = new VTRequest(1, coord, 0, 1, 1, 0);
                var expectedPixels = new Color32[desc.PhysicalPageSize * desc.PhysicalPageSize];
                expectedProducer.WritePage(desc, request, expectedPixels);

                Object.DestroyImmediate(sourceTexture);
                sourceTexture = null;

                var producer = new VividVirtualTextureAssetProducer(asset);
                Assert.That(producer.RequestPageData(desc, request), Is.EqualTo(VTPageRequestStatus.Available));

                IVTPageFinalizer finalizer = producer.ProducePageData(desc, request);
                Assert.That(finalizer, Is.Not.Null);

                var stagingTexture = new Texture2DArray(
                    desc.PhysicalPageSize,
                    desc.PhysicalPageSize,
                    1,
                    desc.GraphicsFormat,
                    TextureCreationFlags.None);
                var scratchPixels = new Color32[expectedPixels.Length];

                try
                {
                    finalizer.FinalizeUpload(stagingTexture, 0, scratchPixels);
                    stagingTexture.Apply(false, false);
                    Color32[] actualPixels = stagingTexture.GetPixels32(0, 0);
                    Assert.That(actualPixels, Is.EqualTo(expectedPixels));
                }
                finally
                {
                    finalizer.Dispose();
                    Object.DestroyImmediate(stagingTexture);
                }
            }
            finally
            {
                if (sourceTexture != null)
                    Object.DestroyImmediate(sourceTexture);
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(builtData);
            }
        }

        [Test]
        public void Generate_BakesFromNonReadableSourceTexture()
        {
            Texture2D sourceTexture = CreateSourceTexture(4, 4, readable: false);
            VividVirtualTextureAsset asset = ScriptableObject.CreateInstance<VividVirtualTextureAsset>();
            VividVirtualTextureBuiltData builtData = ScriptableObject.CreateInstance<VividVirtualTextureBuiltData>();

            try
            {
                Assert.That(sourceTexture.isReadable, Is.False);
                VividVirtualTextureAssetBuilder.Generate(asset, builtData, new VividVirtualTextureAssetBuilder.Parameters
                {
                    SourceTexture = sourceTexture,
                    PageSize = 2,
                    BorderSize = 1,
                    MipCount = 2,
                    FallbackColor = new Color32(0, 0, 0, 255),
                });

                Assert.That(asset.BuiltData, Is.SameAs(builtData));
                Assert.That(builtData.TileCount, Is.EqualTo(5));
                Assert.That(builtData.RawDataByteSize, Is.GreaterThan(0));
            }
            finally
            {
                Object.DestroyImmediate(sourceTexture);
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(builtData);
            }
        }

        [Test]
        public void Importer_BuildsVirtualTextureAsset_FromTextureAsset()
        {
            Directory.CreateDirectory(TempFolder);
            string texturePath = $"{TempFolder}/Source.png";
            Texture2D sourceTexture = CreateSourceTexture(4, 4, readable: true);
            File.WriteAllBytes(texturePath, sourceTexture.EncodeToPNG());
            Object.DestroyImmediate(sourceTexture);
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
            Texture2D importedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            Assert.That(importedTexture, Is.Not.Null);

            string vtAssetPath = VividVirtualTextureAssetImporter.CreateAssetForTexture(importedTexture);
            Assert.That(vtAssetPath, Is.Not.Empty);
            Assert.That(AssetImporter.GetAtPath(vtAssetPath), Is.TypeOf<VividVirtualTextureAssetImporter>());
            var importer = (VividVirtualTextureAssetImporter)AssetImporter.GetAtPath(vtAssetPath);
            importer.PageSize = 2;
            importer.BorderSize = 1;
            importer.MipCount = 2;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

            VividVirtualTextureAsset vtAsset = AssetDatabase.LoadAssetAtPath<VividVirtualTextureAsset>(vtAssetPath);
            Assert.That(vtAsset, Is.Not.Null);
            Assert.That(vtAsset.BuiltData, Is.Not.Null);
            Assert.That(vtAsset.SourceTexturePath, Is.EqualTo(texturePath));
            Assert.That(vtAsset.SourceTextureGUID, Is.EqualTo(AssetDatabase.AssetPathToGUID(texturePath)));
            Assert.That(vtAsset.PageSize, Is.EqualTo(2));
            Assert.That(vtAsset.BorderSize, Is.EqualTo(1));
            Assert.That(vtAsset.MipCount, Is.EqualTo(2));
            Assert.That(vtAsset.TileCount, Is.EqualTo(5));
            Assert.That(vtAsset.BuiltData.HasStreamData, Is.True);
            Assert.That(vtAsset.BuiltData.HasInlineRawData, Is.False);
            Assert.That(File.Exists(vtAsset.BuiltData.StreamDataPath), Is.True);
        }

        private static Texture2D CreateSourceTexture(int width, int height, bool readable)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: true);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = new Color32((byte)(16 + x * 40), (byte)(32 + y * 40), (byte)(x + y), 255);
            }

            texture.SetPixels32(pixels, 0);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: !readable);
            return texture;
        }

        private static Texture2D CreateOffsetSourceTexture(int width, int height, bool readable, byte offset)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: true);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    pixels[y * width + x] = new Color32(
                        (byte)(offset + x * 16),
                        (byte)(offset + y * 16),
                        (byte)(offset + x + y),
                        255);
                }
            }

            texture.SetPixels32(pixels, 0);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: !readable);
            return texture;
        }

        private string CreateTempStreamDataPath()
        {
            string path = Path.Combine(Path.GetTempPath(), $"VividVT_{Guid.NewGuid():N}.stream");
            m_TempFiles.Add(path);
            return path;
        }

        private static byte[] ToRGBA32Bytes(Color32[] pixels)
        {
            var bytes = new byte[pixels.Length * 4];
            for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
            {
                int byteIndex = pixelIndex * 4;
                Color32 pixel = pixels[pixelIndex];
                bytes[byteIndex] = pixel.r;
                bytes[byteIndex + 1] = pixel.g;
                bytes[byteIndex + 2] = pixel.b;
                bytes[byteIndex + 3] = pixel.a;
            }

            return bytes;
        }
    }
}
