"""Install the bounded shader-only variants over the archived turn baseline."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
BACKUP = ROOT / "Temp~/vsm-depth-variants/before"


def baseline(path):
    return (BACKUP / Path(path).name).read_text(encoding="utf-8-sig")


def save(path, text):
    (ROOT / path).write_text(text, encoding="utf-8")


address = "Shaders/Core/Public/Shadow/VividVirtualShadowMapAddressing.hlsl"
s = baseline(address)
s = s.replace("#define VIVID_VSM_DEPTH_LAYER_COUNT 16", """#define VIVID_VSM_DEPTH_LAYER_COUNT 16

// Diagnostic layout A/B: a bijection within each 2x2 texel / four-layer block.
int _VSMDepthLayoutInterleaved;
uint3 VividVSMDepthAddress(uint2 texel, uint layer)
{
    if (_VSMDepthLayoutInterleaved == 0) return uint3(texel, layer);
    return uint3((texel & ~1u) | uint2(layer & 1u, (layer >> 1u) & 1u),
        (layer & ~3u) | (texel.x & 1u) | ((texel.y & 1u) << 1u));
}
int4 VividVSMDepthLoadAddress(uint2 texel, uint layer)
{
    return int4(VividVSMDepthAddress(texel, layer), 0);
}""")
save(address, s)
caster = "Shaders/Core/Public/Shadow/VividVirtualShadowMapCaster.hlsl"
save(caster, baseline(caster).replace("[uint3(texel, layer)]", "[VividVSMDepthAddress(texel, layer)]"))

resolve = "Shaders/Core/Private/CSMShadowResolve.compute"
s = baseline(resolve).replace("int      _VSMPageOccupancySkipDisabled;", "int      _VSMPageOccupancySkipDisabled;\nint _VSMDepthExperiment; // bit 0: page layer bounds; bit 1: last front-depth reuse")
s = s.replace("[uint3(physicalTexel, layer)]", "[VividVSMDepthAddress(physicalTexel, layer)]")
s = s.replace("int4(origin + uint2(texel % size, texel / size), 0, 0)", "VividVSMDepthLoadAddress(origin + uint2(texel % size, texel / size), 0u)")
s = s.replace(".Load(int4(physicalTexel, layer, 0))", ".Load(VividVSMDepthLoadAddress(physicalTexel, layer))")
s = s.replace("groupshared uint g_VSMPageNonempty;", """groupshared uint g_VSMPageNonempty;
groupshared uint2 g_VSMPageLayerCount;
static const uint kVSMPageLayerRangeKnown = 1u << 26;
static const uint kVSMPageLayerRangeMask = (1023u << 16) | kVSMPageLayerRangeKnown;

uint VSMExtendDepthCount(Texture2DArray<uint> pool, uint2 texel, uint count)
{
    if (count == 16u || pool.Load(VividVSMDepthLoadAddress(texel, count)) == 0u) return count;
    uint low = count + 1u, high = 16u;
    while (low < high)
    {
        uint mid = (low + high) >> 1u;
        if (pool.Load(VividVSMDepthLoadAddress(texel, mid)) == 0u) high = mid;
        else low = mid + 1u;
    }
    return low;
}""")
needle = "    if (lane == 0u) g_VSMPageNonempty = 0u;"
s = s.replace(needle, """    if ((_VSMDepthExperiment & 1) != 0)
    {
        scanStatic = scanStatic || (flags & kVSMPageLayerRangeKnown) == 0u;
        uint2 counts = uint2(scanStatic ? 0u : (flags >> 16u) & 31u, 0u);
        if (lane == 0u) g_VSMPageLayerCount = 0u;
        GroupMemoryBarrierWithGroupSync();
        for (uint texel = lane; texel < size * size; texel += 64u)
        {
            uint2 p = origin + uint2(texel % size, texel / size);
            if (scanStatic) counts.x = VSMExtendDepthCount(_VSMPrototypeStaticPhysicalPage, p, counts.x);
            counts.y = VSMExtendDepthCount(_VSMPrototypeDynamicPhysicalPage, p, counts.y);
            if (counts.y == 16u && (!scanStatic || counts.x == 16u)) break;
        }
        InterlockedMax(g_VSMPageLayerCount.x, counts.x);
        InterlockedMax(g_VSMPageLayerCount.y, counts.y);
        GroupMemoryBarrierWithGroupSync();
        if (lane == 0u)
        {
            flags &= ~(kVSMPageStaticEmpty | kVSMPageDynamicEmpty | kVSMPageLayerRangeMask);
            flags |= kVSMPageStaticOccupancyKnown | kVSMPageLayerRangeKnown
                | (g_VSMPageLayerCount.x << 16u) | (g_VSMPageLayerCount.y << 21u);
            if (g_VSMPageLayerCount.x == 0u) flags |= kVSMPageStaticEmpty;
            if (g_VSMPageLayerCount.y == 0u) flags |= kVSMPageDynamicEmpty;
            _VSMPrototypePageMetadata[page].x = flags;
        }
        return;
    }
    flags &= ~kVSMPageLayerRangeKnown;
""" + needle)
s = s.replace("uint2 LoadVSMDepthLayer(int2 physicalTexel, uint layer, uint pageFlags)\n{", """static uint4 g_VSMFrontCache = uint4(0xffffffffu, 0xffffffffu, 0u, 0u);
uint2 LoadVSMDepthLayer(int2 physicalTexel, uint layer, uint pageFlags)
{
    bool reuse = (_VSMDepthExperiment & 2) != 0 && layer == 0u;
    if (reuse && all(g_VSMFrontCache.xy == (uint2)physicalTexel)) return g_VSMFrontCache.zw;""")
s = s.replace("    return depths;\n}\n\nuint LoadCombinedVSMDepth", "    if (reuse) g_VSMFrontCache = uint4(physicalTexel, depths);\n    return depths;\n}\n\nuint LoadCombinedVSMDepth")
save(resolve, s)

smrt = "Shaders/Core/Private/VSMSMRT.hlsl"
s = baseline(smrt).replace("float originDepth, float depthPerWorld, float enter, float exitTime, float thickness)\n{", "float originDepth, float depthPerWorld, float enter, float exitTime, float thickness, uint layerCount)\n{", 1)
s = s.replace("uint low = 1u, high = VIVID_VSM_DEPTH_LAYER_COUNT;", "uint low = 1u, high = layerCount;")
s = s.replace("pool.Load(int4(texel, layer, 0))", "pool.Load(VividVSMDepthLoadAddress(texel, layer))")
needle = "bool TryTraceVSMSMRTRay(float3 origin, float2 texelsPerWorld, float depthPerWorld,"
s = s.replace(needle, """bool VSMSMRTHasDepthInInterval(Texture2DArray<uint> pool, int2 texel, uint frontDepth,
    float originDepth, float depthPerWorld, float enter, float exitTime, float thickness)
{
    return VSMSMRTHasDepthInInterval(pool, texel, frontDepth,
        originDepth, depthPerWorld, enter, exitTime, thickness, 16u);
}

""" + needle, 1)
s = s.replace("                hit = VSMSMRTHasDepthInInterval(_VSMPrototypeStaticPhysicalPage,", "                bool bounded = (_VSMDepthExperiment & 1) != 0 && (pageFlags & kVSMPageLayerRangeKnown) != 0u;\n                hit = VSMSMRTHasDepthInInterval(_VSMPrototypeStaticPhysicalPage,")
s = s.replace("physical, frontDepths.x, origin.z, depthPerWorld, enter, exitTime, thickness);", "physical, frontDepths.x, origin.z, depthPerWorld, enter, exitTime, thickness,\n                    bounded ? (pageFlags >> 16u) & 31u : 16u);")
s = s.replace("physical, frontDepths.y, origin.z, depthPerWorld, enter, exitTime, thickness);", "physical, frontDepths.y, origin.z, depthPerWorld, enter, exitTime, thickness,\n                        bounded ? (pageFlags >> 21u) & 31u : 16u);")
save(smrt, s)

tests = "Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.compute"
s = baseline(tests)
s = s.replace("texel, front.x, ray.x, ray.y, input.z, input.w, ray.z);", "texel, front.x, ray.x, ray.y, input.z, input.w, ray.z,\n        (_VSMDepthExperiment & 1) != 0 ? VSMExtendDepthCount(_VSMPrototypeStaticPhysicalPage, texel, 0u) : 16u);")
s = s.replace("texel, front.y, ray.x, ray.y, input.z, input.w, ray.z);", "texel, front.y, ray.x, ray.y, input.z, input.w, ray.z,\n        (_VSMDepthExperiment & 1) != 0 ? VSMExtendDepthCount(_VSMPrototypeDynamicPhysicalPage, texel, 0u) : 16u);")
s = s.replace("_TestStaticPool[id]", "_TestStaticPool[VividVSMDepthAddress(id.xy, id.z)]").replace("_TestDynamicPool[id]", "_TestDynamicPool[VividVSMDepthAddress(id.xy, id.z)]")
s = "#pragma kernel PermuteDepthLayout\n" + s
s += """
// The layout permutation is an involution. Swap each pair once, within a page.
[numthreads(8, 8, 1)]
void PermuteDepthLayout(uint3 id : SV_DispatchThreadID)
{
    uint owner = _VSMPrototypePhysicalPageOwners[id.z];
    if (owner == 0u) return;
    uint2 p = uint2(id.z % (uint)_VSMPrototypePhysicalPagesPerRow,
        id.z / (uint)_VSMPrototypePhysicalPagesPerRow) * (uint)_VSMPrototypePageSize + id.xy;
    for (uint layer = 0u; layer < 16u; layer++)
    {
        uint3 a = uint3(p, layer), b = VividVSMDepthAddress(p, layer);
        // Local bijection order; no pair crosses a 2x2 texel block.
        uint ia = ((p.y & 1u) * 2u + (p.x & 1u)) * 16u + layer;
        uint ib = ((b.y & 1u) * 2u + (b.x & 1u)) * 16u + b.z;
        if (ia >= ib) continue;
        uint x = _VSMPrototypeStaticPhysicalPageRW[a], y = _VSMPrototypeStaticPhysicalPageRW[b];
        _VSMPrototypeStaticPhysicalPageRW[a] = y; _VSMPrototypeStaticPhysicalPageRW[b] = x;
        x = _VSMPrototypeDynamicPhysicalPageRW[a]; y = _VSMPrototypeDynamicPhysicalPageRW[b];
        _VSMPrototypeDynamicPhysicalPageRW[a] = y; _VSMPrototypeDynamicPhysicalPageRW[b] = x;
    }
}
"""
save(tests, s)
