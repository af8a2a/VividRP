#ifndef VIVIDRP_VSM_VIEW_COMPACTION_INCLUDED
#define VIVIDRP_VSM_VIEW_COMPACTION_INCLUDED

uint _VSMActiveViewOffset;
#if defined(VIVID_VSM_COMPACT_VIEWS)
StructuredBuffer<uint> _VSMActiveViews;
uint VividVSMActiveViewCount() { return _VSMActiveViews[_VSMActiveViewOffset]; }
bool VividVSMResolveActiveView(uint dispatchView, out uint projectionIndex)
{
    projectionIndex = 0u;
    if (dispatchView >= VividVSMActiveViewCount()) return false;
    projectionIndex = _VSMActiveViews[_VSMActiveViewOffset + 1u + dispatchView];
    return true;
}
#endif

#if defined(VIVID_VSM_BUILD_COMPACT_VIEWS)
RWStructuredBuffer<uint> _VSMActiveViewsRW;
RWStructuredBuffer<uint> _VSMInstanceDispatchArgsRW;
uint _VSMSourceInstanceCount;

// One short ordered scan per caster layer. Preserve projection IDs and all
// existing per-projection queue offsets; only the dispatch domain is compacted.
void VividVSMCompactViews(uint projectionCount, uint layer, out uint activeCount)
{
    activeCount = 0u;
    for (uint level = 0u; level < projectionCount; level++)
    {
        _VSMActiveViewsRW[_VSMActiveViewOffset + 1u + level] = 0xffffffffu;
        uint4 bounds = _VSMUncachedPageRectBounds[level * 2u + layer];
        if (all(bounds.xy <= bounds.zw))
            _VSMActiveViewsRW[_VSMActiveViewOffset + 1u + activeCount++] = level;
    }
    _VSMActiveViewsRW[_VSMActiveViewOffset] = activeCount;
    uint argsOffset = layer * 3u;
    _VSMInstanceDispatchArgsRW[argsOffset] = activeCount == 0u ? 0u : (_VSMSourceInstanceCount + 31u) / 32u;
    _VSMInstanceDispatchArgsRW[argsOffset + 1u] = activeCount;
    _VSMInstanceDispatchArgsRW[argsOffset + 2u] = 1u;
}
#endif
#endif
