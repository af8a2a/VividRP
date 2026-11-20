using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace VividRP.Runtime.GPUDriven.Meshlets
{
    // FLumenCard::GetMipMapDesc and FLumenSceneData::ReallocVirtualSurface.
    internal readonly struct VividCardMipMap
    {
        internal const int MinLevel = 3, MaxLevel = 11, PageLevel = 7;
        internal const int PageSize = 1 << PageLevel, VirtualPageSize = PageSize - 1;
        internal readonly int2 Levels, SizeInPages, Resolution, PageResolution;
        internal readonly bool SubAllocation;

        internal VividCardMipMap(int resLevel, float cardAspect)
        {
            if (resLevel < MinLevel || resLevel > MaxLevel || !(cardAspect > 0f) || !math.isfinite(cardAspect))
                throw new ArgumentOutOfRangeException();
            int2 bias = 0;
            if (cardAspect >= 1f)
                bias.y = AspectBias(cardAspect);
            else
                bias.x = AspectBias(1f / cardAspect);
            int2 levels = math.clamp(resLevel - bias, MinLevel, MaxLevel);
            SubAllocation = math.all(levels <= PageLevel);
            if (SubAllocation)
            {
                SizeInPages = new int2(1);
                PageResolution = new int2(1 << levels.x, 1 << levels.y);
                Resolution = PageResolution;
            }
            else
            {
                levels = math.max(levels, PageLevel);
                SizeInPages = new int2(1 << (levels.x - PageLevel), 1 << (levels.y - PageLevel));
                Resolution = SizeInPages * VirtualPageSize;
                PageResolution = new int2(PageSize);
            }
            Levels = levels;
        }

        // Positive FMath::RoundToInt rounds half up. Clamp before conversion for very narrow cards.
        private static int AspectBias(float ratio) =>
            31 - math.lzcnt((uint)math.floor(math.min(ratio, 1 << (MaxLevel - MinLevel)) + 0.5f));

        internal float4 GetCardUVRect(int2 page)
        {
            float2 lo = (float2)page / SizeInPages;
            float2 hi = (float2)(page + 1) / SizeInPages;
            float2 border = 0.5f * (PageSize - VirtualPageSize) * (hi - lo) / PageSize;
            return new float4(math.select(lo, lo - border, page > 0),
                math.select(hi, hi + border, page + 1 < SizeInPages));
        }

        // FUpdateSurfaceCacheMeshCardsTask. The caller supplies OBB distance and half extent in cm.
        internal static int GetDesiredLockedLevel(float distanceCm, float maxExtentCm, float resolutionScale,
            float texelDensityScale, float maxTexelDensity, int maxResolution, int minResolution, float maxDistanceCm)
        {
            float viewerDistance = math.max(distanceCm, 100f);
            float projected = math.min(texelDensityScale * maxExtentCm * resolutionScale / viewerDistance,
                maxTexelDensity * maxExtentCm);
            uint resolution = (uint)math.min((int)projected, maxResolution);
            int snapped = 1 << (32 - math.lzcnt(resolution > 0 ? resolution - 1u : 0u));
            return viewerDistance < maxDistanceCm && snapped >= minResolution
                ? 31 - math.lzcnt((uint)math.max(snapped, 1 << MinLevel)) : -1;
        }

        internal uint2 GetPageTableEntry(int2 physicalTexelMin, uint samplePageIndex)
        {
            uint2 bias = (uint2)physicalTexelMin >> MinLevel;
            return new uint2((bias.x & 0xfffu) | ((bias.y & 0xfffu) << 12)
                | ((uint)Levels.x << 24) | ((uint)Levels.y << 28), samplePageIndex);
        }
    }

    // FLumenCardPageGPUData::FillData. Frame counters are subsequently owned by the page-update shader.
    [StructLayout(LayoutKind.Sequential)]
    internal struct VividCardPageData
    {
        internal float4 CardAndSize, CardUVRect, AtlasUVRect, TexelSizeAndTiles, UpdateFrames;

        internal static VividCardPageData Create(uint card, uint firstPage, VividCardMipMap mip, int2 localPage,
            int2 physicalMin, int2 atlasTexels)
        {
            float4 uv = mip.GetCardUVRect(localPage);
            int2 size = mip.PageResolution;
            float2 invAtlas = 1f / (float2)atlasTexels;
            int2 tileCount = mip.SubAllocation ? size / 8 : mip.SizeInPages * (VividCardMipMap.PageSize / 8);
            return new VividCardPageData
            {
                CardAndSize = new float4(math.asfloat(card), math.asfloat(firstPage), size.x, size.y),
                CardUVRect = uv,
                AtlasUVRect = new float4(physicalMin * invAtlas, (physicalMin + size) * invAtlas),
                TexelSizeAndTiles = new float4((uv.zw - uv.xy) / size, math.asfloat(tileCount)),
            };
        }
    }

    // FLumenSurfaceCacheAllocator. Each bin holds a single (width,height) of suballocation.
    internal sealed class VividSurfaceCachePageAllocator
    {
        private struct PhysicalPage
        {
            internal int Bin;
            internal int Available;
            internal uint4 Low, High;

            internal uint GetWord(int word) => word < 4 ? Low[word] : High[word - 4];
            internal void SetWord(int word, uint value)
            {
                if (word < 4) Low[word] = value;
                else High[word - 4] = value;
            }
        }

        private readonly int m_PagesAcross;
        private readonly PhysicalPage[] m_Pages;
        private readonly List<int>[] m_Bins = new List<int>[25];
        private readonly uint[] m_PhysicalBits;
        private int m_FreeCount;

        internal VividSurfaceCachePageAllocator(int2 atlasSizeInPages)
        {
            if (math.any(atlasSizeInPages <= 0) || math.any(atlasSizeInPages > 256))
                throw new ArgumentOutOfRangeException(nameof(atlasSizeInPages));
            m_PagesAcross = atlasSizeInPages.x;
            m_Pages = new PhysicalPage[atlasSizeInPages.x * atlasSizeInPages.y];
            m_PhysicalBits = new uint[(m_Pages.Length + 31) / 32];
            m_FreeCount = m_Pages.Length;
        }

        private static int Bin(VividCardMipMap mip) => mip.Levels.x - VividCardMipMap.MinLevel
            + 5 * (mip.Levels.y - VividCardMipMap.MinLevel);

        internal bool IsSpaceAvailable(VividCardMipMap mip, bool singlePage)
        {
            if (m_FreeCount >= (singlePage ? 1 : mip.SizeInPages.x * mip.SizeInPages.y))
                return true;
            if (mip.SubAllocation && m_Bins[Bin(mip)] is { } list)
                for (int i = 0; i < list.Count; ++i)
                    if (m_Pages[list[i]].Available > 0)
                        return true;
            return false;
        }

        private int AllocatePhysicalPage()
        {
            for (int i = 0; i < m_PhysicalBits.Length; ++i)
            {
                uint free = ~m_PhysicalBits[i];
                if (free == 0) continue;
                int bit = math.tzcnt(free);
                int page = i * 32 + bit;
                if (page >= m_Pages.Length) break;
                m_PhysicalBits[i] |= 1u << bit;
                --m_FreeCount;
                return page;
            }
            return -1;
        }

        internal bool TryAllocate(VividCardMipMap mip, out int2 physicalTexelMin)
        {
            physicalTexelMin = -1;
            int page = -1;
            int bin = mip.SubAllocation ? Bin(mip) : -1;
            List<int> list = null;
            int2 elements = VividCardMipMap.PageSize / mip.PageResolution;
            if (mip.SubAllocation)
            {
                list = m_Bins[bin] ??= new List<int>();
                for (int i = 0; i < list.Count; ++i)
                    if (m_Pages[list[i]].Available > 0) { page = list[i]; break; }
            }
            if (page < 0)
            {
                page = AllocatePhysicalPage();
                if (page < 0) return false;
                m_Pages[page] = new PhysicalPage { Bin = bin, Available = elements.x * elements.y };
                list?.Add(page);
            }
            physicalTexelMin = new int2(page % m_PagesAcross, page / m_PagesAcross) * VividCardMipMap.PageSize;
            if (mip.SubAllocation)
            {
                ref var physical = ref m_Pages[page];
                for (int word = 0; word < 8; ++word)
                {
                    uint bits = physical.GetWord(word);
                    if (bits == uint.MaxValue) continue;
                    int bit = math.tzcnt(~bits);
                    physical.SetWord(word, bits | (1u << bit));
                    --physical.Available;
                    int index = word * 32 + bit;
                    physicalTexelMin += new int2(index % elements.x, index / elements.x) * mip.PageResolution;
                    break;
                }
            }
            return true;
        }

        // Restore the captured address. Lumen also uses physical texel coordinates to index blue noise.
        internal bool TryReserve(VividCardMipMap mip, int2 min)
        {
            int2 coord = min / VividCardMipMap.PageSize;
            if (math.any(min < 0) || coord.x >= m_PagesAcross || coord.y >= m_Pages.Length / m_PagesAcross
                || math.any(min % mip.PageResolution != 0)) return false;
            int index = coord.y * m_PagesAcross + coord.x;
            uint pageBit = 1u << (index & 31);
            int bin = mip.SubAllocation ? Bin(mip) : -1;
            int2 elements = VividCardMipMap.PageSize / mip.PageResolution;
            ref var page = ref m_Pages[index];
            if ((m_PhysicalBits[index >> 5] & pageBit) == 0)
            {
                m_PhysicalBits[index >> 5] |= pageBit;
                --m_FreeCount;
                page = new PhysicalPage { Bin = bin, Available = elements.x * elements.y };
                if (mip.SubAllocation)
                    (m_Bins[bin] ??= new List<int>()).Add(index);
            }
            else if (!mip.SubAllocation || page.Bin != bin)
                return false;
            if (mip.SubAllocation)
            {
                int2 cell = (min % VividCardMipMap.PageSize) / mip.PageResolution;
                int slot = cell.y * elements.x + cell.x, word = slot / 32;
                uint mask = 1u << (slot & 31);
                uint bits = page.GetWord(word);
                if ((bits & mask) != 0) return false;
                page.SetWord(word, bits | mask);
                --page.Available;
            }
            return true;
        }

        internal void Free(VividCardMipMap mip, int2 min)
        {
            int2 coord = min / VividCardMipMap.PageSize;
            int page = coord.x + m_PagesAcross * coord.y;
            uint pageBit = 1u << (page & 31);
            if (math.any(min < 0) || coord.x >= m_PagesAcross || (uint)page >= m_Pages.Length
                || (m_PhysicalBits[page >> 5] & pageBit) == 0)
                throw new InvalidOperationException("Unmapped Surface Cache page.");
            ref var physical = ref m_Pages[page];
            if (physical.Bin != (mip.SubAllocation ? Bin(mip) : -1) || math.any(min % mip.PageResolution != 0))
                throw new InvalidOperationException("Surface Cache page allocation mismatch.");
            if (mip.SubAllocation)
            {
                int2 cell = (min % VividCardMipMap.PageSize) / mip.PageResolution;
                int2 elements = VividCardMipMap.PageSize / mip.PageResolution;
                int slot = cell.x + cell.y * elements.x;
                int word = slot / 32;
                uint mask = 1u << (slot & 31);
                uint bits = physical.GetWord(word);
                if ((bits & mask) == 0) throw new InvalidOperationException("Unmapped Surface Cache suballocation.");
                physical.SetWord(word, bits & ~mask);
                if (++physical.Available != elements.x * elements.y) return;
                var list = m_Bins[physical.Bin];
                int index = list.IndexOf(page);
                list[index] = list[list.Count - 1];
                list.RemoveAt(list.Count - 1);
            }
            m_PhysicalBits[page >> 5] &= ~pageBit;
            ++m_FreeCount;
        }
    }
}
