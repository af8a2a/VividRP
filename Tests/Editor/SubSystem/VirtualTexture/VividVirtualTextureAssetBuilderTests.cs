using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using VividRP.Editor;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class VividVirtualTextureAssetBuilderTests
    {
        private const string TempFolder = "Assets/VividVirtualTextureImporterTests";

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TempFolder);
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
    }
}
