static const uint kVSMPageRequested = 1u << 0;
static const uint kVSMPageAllocated = 1u << 1;
static const uint kVSMPageDirty = 1u << 2;
static const uint kVSMPageCached = 1u << 3;
static const uint kVSMPageStatic = 1u << 4;
static const uint kVSMPageDynamic = 1u << 5;
// Debug-only events in metadata.w; never participate in rendering decisions.
static const uint kVSMPageDebugEvicted = 1u << 6;
static const uint kVSMPageDebugOverflow = 1u << 7;
// Live feedback priority, cleared together with Requested after allocation.
static const uint kVSMPageCoarseRequested = 1u << 8;
static const uint kVSMPagePrimaryRequested = 1u << 9;
static const uint kVSMPageTransitionRequested = 1u << 10;
static const uint kVSMPageParentRequested = 1u << 11;
static const uint kVSMPageStaticEmpty = 1u << 12;
static const uint kVSMPageDynamicEmpty = 1u << 13;
static const uint kVSMPageStaticOccupancyKnown = 1u << 14;
static const uint kVSMPageDynamicDirty = 1u << 15;
static const uint kVSMPageDynamicOccupancyKnown = 1u << 16;
// Dirty data is retained but unavailable until this page is selected and fully rebuilt.
static const uint kVSMPageDeferred = 1u << 17;
static const uint kVSMPageRequestMask = kVSMPageRequested | kVSMPageCoarseRequested
    | kVSMPagePrimaryRequested | kVSMPageTransitionRequested | kVSMPageParentRequested;

// A page can serve multiple receivers/roles. Atomic OR preserves the strongest
// demand; the terminal level remains the safety net for any incomplete footprint.
uint VSMPageRequestPriority(uint flags)
{
    if ((flags & (kVSMPageCoarseRequested | kVSMPageParentRequested)) != 0u) return 0u;
    if ((flags & kVSMPagePrimaryRequested) != 0u) return 1u;
    if ((flags & kVSMPageTransitionRequested) != 0u) return 2u;
    return 3u;
}
static const uint kVSMMaxPagesPerMeshletRequest = 4u;

// Scalar work counters, compiled only for the explicit cost replay. ABI and
// names are documented by SMRTCostCapture; production has no counter storage.
#if defined(VIVID_VSM_SMRT_COST)
static uint4 g_VSMSMRTCost[8];
#define VSM_COST_ADD(counter, count) g_VSMSMRTCost[(counter) / 4][(counter) % 4] += (count)
#else
#define VSM_COST_ADD(counter, count)
#endif

#if defined(VIVID_VSM_RECEIVER_DEBUG)
// Thread-local instrumentation exists only in the opt-in diagnostic kernel.
static int3 g_VSMDebugLevels;
static float g_VSMDebugBlend;
// Depth lookups count paired front/linear reads or individual ordered-search probes, not scalar pool loads.
static uint3 g_VSMDebugWork; // attempted queries, completed depth lookups, attempted levels
static uint g_VSMDebugMissing; // unmapped=1, dirty=2, inconsistent=4, outside=8
static float4 g_VSMDebugQuality; // desired LOD, minimum covered, selected, footprint/target
static uint4 g_VSMDebugSMRT; // rays, unoccluded parallel tails, failed footprints, final PCF fallback
#endif

