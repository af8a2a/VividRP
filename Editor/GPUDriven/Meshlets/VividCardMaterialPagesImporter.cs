using System;
using System.IO;
using Unity.Mathematics;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Editor.GPUDriven.Meshlets
{
    [ScriptedImporter(1, "vcardpages")]
    internal sealed class VividCardMaterialPagesImporter : ScriptedImporter
    {
        private struct Page
        {
            internal int Id, Level;
            internal int2 Source, Size;
            internal VividCardMipMap Mip;
        }

        private static int ReadCount(BinaryReader reader, int limit)
        {
            int n = reader.ReadInt32();
            return n > 0 && n <= limit ? n : throw new InvalidDataException("Invalid page snapshot dimensions.");
        }

        private static byte[] Read(BinaryReader reader, int size)
        {
            byte[] bytes = reader.ReadBytes(size);
            return bytes.Length == size ? bytes : throw new EndOfStreamException();
        }

        private static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidDataException(reason);
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            using var stream = File.OpenRead(ctx.assetPath);
            using var reader = new BinaryReader(stream);
            Require(reader.ReadUInt32() == 0x50434d56 && reader.ReadUInt32() == 1, "Unsupported material page snapshot.");
            var sourceSize = new int2(ReadCount(reader, 8192), ReadCount(reader, 8192));
            Require(math.all(sourceSize % 128 == 0), "Surface Cache atlas is not page aligned.");
            int cards = ReadCount(reader, 1 << 20), meshes = ReadCount(reader, 1 << 20);
            int tableSize = ReadCount(reader, 1 << 20), resident = ReadCount(reader, tableSize);
            int blocks = ReadCount(reader, sourceSize.x * sourceSize.y / 16);
            Require(stream.Length - stream.Position == (long)resident * 24 + cards * 160L + meshes * 96L
                + tableSize * 88L + blocks * 96L, "Material page snapshot size does not match the declared layout.");
            var pages = new Page[resident];
            var mapped = new bool[tableSize];
            int expectedBlocks = 0;
            for (int i = 0; i < pages.Length; ++i)
            {
                ref var p = ref pages[i];
                p.Id = reader.ReadInt32();
                p.Level = reader.ReadInt32();
                p.Source = new int2(reader.ReadInt32(), reader.ReadInt32());
                p.Size = new int2(reader.ReadInt32(), reader.ReadInt32());
                Require((uint)p.Id < tableSize && !mapped[p.Id] && p.Level >= 3 && p.Level <= 11
                    && math.all(p.Size >= 8 & p.Size <= 128) && math.all(p.Size % 8 == 0)
                    && math.all(p.Source >= 0 & p.Source <= sourceSize - p.Size), "Invalid resident card page.");
                mapped[p.Id] = true;
                expectedBlocks = checked(expectedBlocks + p.Size.x * p.Size.y / 16);
            }
            Require(expectedBlocks == blocks, "Inconsistent resident block count.");
            byte[] cardData = Read(reader, cards * 160);
            byte[] meshData = Read(reader, meshes * 96);
            byte[] table = Read(reader, tableSize * 8);
            byte[] pageData = Read(reader, tableSize * 80);
            var allocator = new VividSurfaceCachePageAllocator(sourceSize / 128);
            int2 used = 0;
            for (int i = 0; i < pages.Length; ++i)
            {
                ref var p = ref pages[i];
                int pageOffset = p.Id * 80;
                int card = BitConverter.ToInt32(pageData, pageOffset);
                Require((uint)card < cards, "Resident page has no source card.");
                uint bias = BitConverter.ToUInt32(cardData, card * 160 + 76);
                int xBias = (int)(bias & 255), yBias = (int)(bias >> 8 & 255);
                Require(xBias <= 8 && yBias <= 8 && (xBias == 0 || yBias == 0), "Invalid card aspect bias.");
                var mip = new VividCardMipMap(p.Level, (float)(1 << yBias) / (1 << xBias));
                p.Mip = mip;
                Require(math.all(mip.PageResolution == p.Size), "Card mip resolution differs from its material page.");
                uint2 srcEntry = mip.GetPageTableEntry(p.Source, (uint)p.Id);
                Require(BitConverter.ToUInt32(table, p.Id * 8) == srcEntry.x
                    && BitConverter.ToUInt32(table, p.Id * 8 + 4) == srcEntry.y, "Mapped page table entry differs from the source.");
                Require(allocator.TryReserve(mip, p.Source), "Overlapping or misaligned resident material pages.");
                used = math.max(used, p.Source + p.Size);
            }
            int2 size = math.ceilpow2(used);
            int pixels = checked(size.x * size.y);
            var albedo = new byte[pixels];
            var normals = new byte[pixels];
            var emissive = new byte[pixels];
            var depth = new byte[pixels * 2];
            var opacity = new byte[pixels];
            Array.Fill(depth, (byte)255);
            for (int i = 0; i < tableSize; ++i)
            {
                uint entry = BitConverter.ToUInt32(table, i * 8);
                if (entry == 0) continue;
                uint target = BitConverter.ToUInt32(table, i * 8 + 4);
                Require(target < tableSize && mapped[target], "Virtual page refers to a missing physical page.");
            }
            var block = new byte[96];
            foreach (var p in pages)
            {
                int pageOffset = p.Id * 80;
                uint card = BitConverter.ToUInt32(pageData, pageOffset);
                uint firstPage = BitConverter.ToUInt32(pageData, pageOffset + 4);
                int localPage = p.Id - checked((int)firstPage);
                Require(localPage >= 0 && localPage < p.Mip.SizeInPages.x * p.Mip.SizeInPages.y, "Invalid card page span.");
                var coord = new int2(localPage % p.Mip.SizeInPages.x, localPage / p.Mip.SizeInPages.x);
                var srcData = VividCardPageData.Create(card, firstPage, p.Mip, coord, p.Source, sourceSize);
                var dstData = VividCardPageData.Create(card, firstPage, p.Mip, coord, p.Source, size);
                for (int c = 0; c < 4; ++c)
                {
                    Require(BitConverter.ToUInt32(pageData, pageOffset + 4*c) == math.asuint(srcData.CardAndSize[c])
                        && BitConverter.ToUInt32(pageData, pageOffset + 16 + 4*c) == math.asuint(srcData.CardUVRect[c])
                        && BitConverter.ToUInt32(pageData, pageOffset + 32 + 4*c) == math.asuint(srcData.AtlasUVRect[c])
                        && BitConverter.ToUInt32(pageData, pageOffset + 48 + 4*c) == math.asuint(srcData.TexelSizeAndTiles[c]),
                        "Card page geometry differs from its source projection.");
                    BitConverter.TryWriteBytes(pageData.AsSpan(pageOffset + 32 + 4*c), dstData.AtlasUVRect[c]);
                }
                Array.Clear(pageData, p.Id * 80 + 64, 16);
                for (int y = 0; y < p.Size.y; y += 4)
                    for (int x = 0; x < p.Size.x; x += 4)
                    {
                        int2 xy = p.Source + new int2(x, y);
                        int offset = ((xy.y / 4) * (size.x / 4) + xy.x / 4) * 16;
                        Require(reader.Read(block, 0, block.Length) == block.Length, "Incomplete material block.");
                        Buffer.BlockCopy(block, 0, albedo, offset, 16);
                        Buffer.BlockCopy(block, 16, normals, offset, 16);
                        Buffer.BlockCopy(block, 32, emissive, offset, 16);
                        for (int dy = 0; dy < 4; ++dy)
                        {
                            int pixel = (xy.y + dy) * size.x + xy.x;
                            Buffer.BlockCopy(block, 48 + dy * 8, depth, pixel * 2, 8);
                            Buffer.BlockCopy(block, 80 + dy * 4, opacity, pixel, 4);
                        }
                    }
            }
            Texture2D Layer(string name, GraphicsFormat format, byte[] bytes)
            {
                var texture = new Texture2D(size.x, size.y, format, TextureCreationFlags.None)
                    { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                texture.SetPixelData(bytes, 0);
                texture.Apply(false, true);
                ctx.AddObjectToAsset(name, texture);
                return texture;
            }
            var asset = ScriptableObject.CreateInstance<VividCardMaterialPages>();
            asset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            asset.ResidentPages = resident;
            asset.Cards = cardData;
            asset.MeshCards = meshData;
            asset.PageTable = table;
            asset.CardPages = pageData;
            asset.Albedo = Layer("Albedo", GraphicsFormat.RGBA_BC7_UNorm, albedo);
            asset.Normal = Layer("Normal", GraphicsFormat.RG_BC5_UNorm, normals);
            asset.Emissive = Layer("Emissive", GraphicsFormat.RGB_BC6H_UFloat, emissive);
            asset.Depth = Layer("Depth", GraphicsFormat.R16_UNorm, depth);
            asset.Opacity = Layer("Opacity", GraphicsFormat.R8_UNorm, opacity);
            ctx.AddObjectToAsset("CardMaterialPages", asset);
            ctx.SetMainObject(asset);
        }
    }
}
