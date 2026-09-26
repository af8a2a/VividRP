// Demand generation is independent of residency, depth sampling and ray phase.
// A zero request denotes an empty footprint (including a disabled filter path).
struct VSMReceiverPageFootprint
{
    uint2 minPage, maxPage;
    uint2 minTexel, maxTexel;
    uint request;
};

VSMReceiverPageFootprint BuildVSMReceiverPageFootprint(float2 uv, int level, uint role, int halo)
{
    VSMReceiverPageFootprint footprint = (VSMReceiverPageFootprint)0;
    int2 texel;
    uint resolution = (uint)_VSMPrototypeVirtualResolution;
    if (_VSMPrototypeRequestEnabled == 0 || level < 0 || level >= _VSMProjectionCount
        || !VividVSMTryOffsetVirtualTexel(uv, int2(0, 0), resolution, texel)) return footprint;
    uint pageSize = (uint)_VSMPrototypePageSize;
    footprint.minTexel = (uint2)max(texel - halo, 0);
    footprint.maxTexel = (uint2)min(texel + halo, (int)resolution - 1);
    footprint.minPage = footprint.minTexel / pageSize;
    footprint.maxPage = footprint.maxTexel / pageSize;
    footprint.request = kVSMPageRequested | role;
    if (level == _VSMProjectionCount - 1) footprint.request |= kVSMPageCoarseRequested;
    return footprint;
}

bool VSMFootprintContains(VSMReceiverPageFootprint footprint, uint2 page)
{
    return footprint.request != 0u && all(page >= footprint.minPage) && all(page <= footprint.maxPage);
}

uint2 VSMFootprintPageMask(VSMReceiverPageFootprint footprint, uint2 page)
{
    if (!VSMFootprintContains(footprint, page)) return 0u;
    uint size = (uint)_VSMPrototypePageSize;
    uint2 origin = page * size;
    return VividVSMReceiverMaskRect(max(footprint.minTexel, origin) - origin,
        min(footprint.maxTexel, origin + size - 1u) - origin, size);
}

void EmitVSMReceiverPage(uint2 coord, int level, uint request, uint2 receiverMask)
{
#if !defined(VIVID_VSM_RECEIVER_DEBUG) && !defined(VIVID_VSM_RESOLVE_RECEIVERS)
    uint axis = (uint)_VSMPrototypePagesPerAxis;
    uint page = (uint)level * axis * axis + coord.y * axis + coord.x;
    // Terminal pages are a complete safety net, including fallback ray origins.
    if ((request & kVSMPageCoarseRequested) != 0u) receiverMask = 0xffffffffu;
#if defined(VIVID_VSM_MARK_RECEIVERS)
    if (WaveActiveAllEqual(page))
    {
        request = WaveActiveBitOr(request);
        receiverMask = WaveActiveBitOr(receiverMask);
        if (!WaveIsFirstLane()) return;
    }
#endif
    InterlockedOr(_VSMPageRequestFlags[page], request);
    if (_VSMReceiverMaskEnabled != 0)
    {
        InterlockedOr(_VSMPageReceiverMasks[page].x, receiverMask.x);
        InterlockedOr(_VSMPageReceiverMasks[page].y, receiverMask.y);
    }
#endif
}

void EmitVSMReceiverFootprints(int level, VSMReceiverPageFootprint a, VSMReceiverPageFootprint b)
{
    // Emit A with the intersection's combined roles, then only B minus A.
    // A bounding rectangle or OR-ing roles across the whole union would promote
    // unrelated pages and change the allocator's priorities.
    if (a.request != 0u)
        for (uint y = a.minPage.y; y <= a.maxPage.y; y++)
            for (uint x = a.minPage.x; x <= a.maxPage.x; x++)
            {
                uint2 page = uint2(x, y);
                EmitVSMReceiverPage(page, level, a.request | (VSMFootprintContains(b, page) ? b.request : 0u),
                    VSMFootprintPageMask(a, page) | VSMFootprintPageMask(b, page));
            }
    // PCF is usually wholly enclosed by the conservative SMRT footprint.
    if (b.request != 0u && !(VSMFootprintContains(a, b.minPage) && VSMFootprintContains(a, b.maxPage)))
        for (uint y = b.minPage.y; y <= b.maxPage.y; y++)
            for (uint x = b.minPage.x; x <= b.maxPage.x; x++)
            {
                uint2 page = uint2(x, y);
                if (!VSMFootprintContains(a, page)) EmitVSMReceiverPage(page, level, b.request, VSMFootprintPageMask(b, page));
            }
}

// UE's independent terminal coarse coverage: up to four pages surrounding the
// clipmap origin, even with no visible receivers. OR preserves all pixel roles.
// Start with the terminal level; intermediate coarse ranges are a separate policy.
[numthreads(1, 1, 1)]
void VSMMarkCoarsePages(uint3 id : SV_DispatchThreadID)
{
    if (_VSMProjectionCount <= 0 || _VSMPrototypeRequestEnabled == 0) return;
    int level = _VSMProjectionCount - 1;
    VividVSMProjection p = _VSMProjections[level];
    float2 uv = mul(p.worldToShadow, float4(p.selectionSphere.xyz, 1)).xy;
    float2 center = uv * _VSMPrototypePagesPerAxis - 0.5;
    int2 low = max((int2)floor(center), 0);
    int2 high = min((int2)ceil(center), _VSMPrototypePagesPerAxis - 1);
    for (int y = low.y; y <= high.y; y++)
        for (int x = low.x; x <= high.x; x++)
            EmitVSMReceiverPage(uint2(x, y), level,
                kVSMPageRequested | kVSMPageCoarseRequested, 0xffffffffu);
}

void MarkVSMReceiverPage(float2 uv, int level, uint role, int halo)
{
    EmitVSMReceiverFootprints(level, BuildVSMReceiverPageFootprint(uv, level, role, halo),
        (VSMReceiverPageFootprint)0);
}

void MarkVSMReceiverPage(float2 uv, int level, uint role)
{
    MarkVSMReceiverPage(uv, level, role, 1);
}

// Select each filter independently: its map-edge guard can select a different
// preferred level and transition. Share the production density/coverage policy.
int SelectVSMMarkingStart(float3 position, float3 normal, bool smrt, out float blend,
    out VSMReceiverProjection selected, out bool allowFallback)
{
    blend = 0;
    selected = (VSMReceiverProjection)0;
    allowFallback = true;
    bool density = _VSMReceiverQuality.x > 0;
    int first = 0;
    if (density) first = SelectVSMDensityLevelPrepared(position, normal, smrt, blend, selected);
    if (first < 0) return -1;
    for (int level = first; level < _VSMProjectionCount; level++)
    {
        VividVSMProjection p = _VSMProjections[level];
        float2 relative = mul(p.worldToShadow, float4(position - p.selectionSphere.xyz, 0)).xy * 2;
        float edge = max(abs(relative.x), abs(relative.y));
        if (!density && edge >= 0.5) continue;
        if (length(position - p.selectionSphere.xyz) >= p.parameters.w)
        {
            // Match the resolve's terminal distance fade: it does not retry PCF.
            allowFallback = false;
            return -1;
        }
        if (!density) blend = VSMTransitionWeight(edge, p.parameters.z);
        return level;
    }
    return -1;
}

VSMReceiverPageFootprint BuildVSMSMRTPageFootprint(float3 position, VividVSMProjection p,
    int level, uint role)
{
    float2 center = mul(p.worldToShadow, float4(position, 1)).xy;
    float radius = VSMSMRTRayLength(level) * _VSMSMRTParameters.w / p.parameters.x;
    float originGuard = (_VSMReceiverParameters.x >= 0.5 ? 1.5 : 0) + abs(p.parameters.y);
    int halo = (int)ceil(radius + originGuard + 0.001);
    float guard = (float)halo / _VSMPrototypeVirtualResolution;
    if (!all(center >= -guard) || !all(center < 1 + guard)) return (VSMReceiverPageFootprint)0;
    // Include continuations whose finer, normal-biased origin lies inside even
    // when this level's unbiased receiver or biased depth is outside the map.
    float halfTexel = 0.5 / _VSMPrototypeVirtualResolution;
    return BuildVSMReceiverPageFootprint(clamp(center, halfTexel, 1 - halfTexel), level, role, halo);
}

uint AdvanceVSMMarkingRole(uint role, float blend)
{
    return role == kVSMPagePrimaryRequested
        ? kVSMPageParentRequested | (blend > 0 ? kVSMPageTransitionRequested : 0u) : 0u;
}

void MarkVSMReceiver(float3 position, float3 normalWS)
{
    if (_VSMPrototypeRequestEnabled == 0 || _VSMProjectionCount <= 0) return;
    float3 normal = normalWS * rsqrt(max(dot(normalWS, normalWS), 1e-8));
    float smrtBlend = 0, pcfBlend = 0;
    VSMReceiverProjection smrtSelected = (VSMReceiverProjection)0, pcfSelected = (VSMReceiverProjection)0;
    bool allowPCF = true, unused;
    int smrtFirst = -1, pcfFirst = -1;
    if (UseVSMSMRT()) smrtFirst = SelectVSMMarkingStart(position, normal, true, smrtBlend, smrtSelected, allowPCF);
    if (allowPCF) pcfFirst = SelectVSMMarkingStart(position, normal, false, pcfBlend, pcfSelected, unused);
    int first = smrtFirst < 0 ? pcfFirst : pcfFirst < 0 ? smrtFirst : min(smrtFirst, pcfFirst);
    if (first < 0) return;
    uint smrtRole = kVSMPagePrimaryRequested, pcfRole = kVSMPagePrimaryRequested;
    for (int level = first; level < _VSMProjectionCount; level++)
    {
        VSMReceiverProjection prepared;
        if (_VSMReceiverQuality.x > 0 && level == smrtFirst) prepared = smrtSelected;
        else if (_VSMReceiverQuality.x > 0 && level == pcfFirst) prepared = pcfSelected;
        else prepared = PrepareVSMReceiverProjection(position, normal, level);
        bool covered = all(prepared.coord >= 0) && all(prepared.coord <= 1);
        VSMReceiverPageFootprint soft = (VSMReceiverPageFootprint)0, hard = (VSMReceiverPageFootprint)0;
        if (smrtFirst >= 0 && level >= smrtFirst)
        {
            uint role = smrtRole;
            if (level > smrtFirst && VSMSMRTRayLength(level - 1) < _VSMSMRTParameters.z)
                role |= kVSMPagePrimaryRequested;
            soft = BuildVSMSMRTPageFootprint(position, prepared.projection, level, role);
            if (covered) smrtRole = AdvanceVSMMarkingRole(smrtRole, smrtBlend);
        }
        if (pcfFirst >= 0 && level >= pcfFirst && covered)
        {
            hard = BuildVSMReceiverPageFootprint(prepared.coord.xy, level, pcfRole, 1);
            pcfRole = AdvanceVSMMarkingRole(pcfRole, pcfBlend);
        }
        EmitVSMReceiverFootprints(level, soft, hard);
    }
}

#if defined(VIVID_VSM_MARK_RECEIVERS)
[numthreads(8, 8, 1)]
void VSMMarkReceiverPages(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)_CSMOutputWidth || id.y >= (uint)_CSMOutputHeight) return;
    float depth = _DepthTexture.Load(int3(id.xy, 0));
    if (IsSkyPixel(depth)) return;
    float3 position = ReconstructWorldPosition(id.xy, depth);
    float3 normal = DecodeVividNormalOct(_GBuffer1.Load(int3(id.xy, 0)).xy);
    normal = ReconstructVSMReceiverNormal(id.xy, depth, position, normal);
    MarkVSMReceiver(position, normal);
}
#endif
