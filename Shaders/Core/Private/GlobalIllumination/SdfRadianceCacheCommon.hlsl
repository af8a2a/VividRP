#ifndef VIVID_SDF_RADIANCE_CACHE_COMMON_INCLUDED
#define VIVID_SDF_RADIANCE_CACHE_COMMON_INCLUDED

// The last atlas plane holds exact world-cell keys, so toroidal reuse
// never exposes an evicted probe. Keep the layout in sync with the pass.
static const uint FarGridSize = 16u;
static const uint FarOctSize = 8u;
static const uint FarDirections = FarOctSize * FarOctSize;
static const float FarCellSize = 2.5;
Texture2D<float4> _SdfFarRadiance;
// LumenRadianceCacheInterpolation.ush. A logical cell is relative to its
// clipmap corner; the physical atlas only contains allocated 32x32 probes.
static const uint WorldClipmaps = 4u;
static const uint WorldGrid = 48u;
static const uint WorldResolution = 32u;
static const uint WorldCapacity = 4096u;
static const uint WorldCells = WorldGrid * WorldGrid * WorldGrid * WorldClipmaps;
static const uint WorldBudget = 100u;

float WorldCellSize(uint level) { return (50.0 / 48.0) * float(1u << level); }
float WorldProbeTMin(uint level) { return WorldCellSize(level) * sqrt(3.0f); }
float WorldNearDistance(uint level) { return WorldProbeTMin(level) + WorldCellSize(level) * sqrt(3.0f); }
int3 WorldOrigin(uint level) { return int3(floor(_SdfFarWindow.xyz / WorldCellSize(level))) - 24; }

float3 GetRadianceProbeCoordFloat(float3 position, uint level)
{
    float cellSize = WorldCellSize(level);
    float3 corner = float3(WorldOrigin(level)) * cellSize - 0.5f * cellSize;
    return (position - corner) / cellSize;
}

uint GetRadianceProbeClipmap(float3 position, float dither)
{
    for (uint level = 0u; level < WorldClipmaps; ++level)
    {
        float3 coord = GetRadianceProbeCoordFloat(position, level);
        float3 bottom = saturate(coord - 0.5f);
        float3 top = saturate(float(WorldGrid) - 0.5f - coord);
        float fade = min(min(bottom.x, min(bottom.y, bottom.z)), min(top.x, min(top.y, top.z)));
        if (fade > dither) return level;
    }
    return WorldClipmaps;
}

uint WorldCellIndex(uint3 local, uint level)
{
    return local.x + WorldGrid * (local.y + WorldGrid * (local.z + WorldGrid * level));
}

uint2 WorldIndirection(uint index) { return uint2(index & 2047u, 2052u + (index >> 11u)); }
uint2 WorldKeyAddress(uint slot) { return uint2(slot & 2047u, 2048u + (slot >> 11u)); }
uint2 WorldAgeAddress(uint slot) { return WorldKeyAddress(slot) + uint2(0, 2); }
uint2 WorldTexelAddress(uint slot, uint2 xy)
{
    return uint2(slot & 63u, slot >> 6u) * WorldResolution + xy;
}
int3 WorldCorner(uint i) { return int3(i & 1u, (i >> 1u) & 1u, i >> 2u); }

uint GetWorldCoverage(float3 position)
{
    if (_SdfScreenProbeFullSphere == 0u || _SdfFarTraceDistance <= 0.0) return WorldClipmaps;
    uint level = GetRadianceProbeClipmap(position, 0.0f);
    if (level == WorldClipmaps || _SdfFarTraceDistance <= WorldNearDistance(level)) return WorldClipmaps;
    int3 lower = int3(floor(GetRadianceProbeCoordFloat(position, level) - 0.5f));
    [loop] for (uint i = 0u; i < 8u; ++i)
    {
        uint index = WorldCellIndex(lower + WorldCorner(i), level);
        if (_SdfFarRadiance.Load(int3(WorldIndirection(index), 0)).x <= 0.0f) return WorldClipmaps;
    }
    return level;
}

// The Epic gather cache uses one final radiance mip. Fold taps across the
// equal-area seam in lieu of a physically duplicated one-texel atlas border.
int2 WorldWrapTexel(int2 xy)
{
    int hi = int(WorldResolution) - 1;
    if (xy.x < 0 || xy.x > hi) xy = int2(clamp(xy.x, 0, hi), hi - xy.y);
    if (xy.y < 0 || xy.y > hi) xy = int2(hi - xy.x, clamp(xy.y, 0, hi));
    return xy;
}

bool SampleWorldCacheCorner(float3 position, float3 direction, uint level, int3 lower, float3 alpha,
    uint cornerIndex, uint slot, inout float4 accum)
{
    int3 corner = WorldCorner(cornerIndex);
    int3 coord = lower + corner;
    float cellSize = WorldCellSize(level);
    float3 probe = float3(WorldOrigin(level)) * cellSize - 0.5f * cellSize + (float3(coord) + 0.5f) * cellSize;
    float3 offset = position - probe;
    float radius = 1.5f * WorldProbeTMin(level);
    float a = dot(direction, direction), b = 2.0f * dot(direction, offset), c = dot(offset, offset) - radius * radius;
    float discriminant = b * b - 4.0f * a * c;
    float t = -1.0f;
    [flatten] if (discriminant >= 0.0f) t = (-b + sqrt(discriminant)) / (2.0f * a);
    float3 reprojected = (position + direction * t) - probe;
    float correction = t * t / (radius * dot(reprojected, direction));
    float2 uv = ProbeSphereUV(reprojected) * WorldResolution;
    uint2 pointXY = min(uint2(uv), WorldResolution - 1u);
    float depth = _SdfFarRadiance.Load(int3(WorldTexelAddress(slot, pointXY), 0)).w;
    if (depth <= 0.0f) return false;
    int2 base = int2(floor(uv - 0.5f));
    float2 f = frac(uv - 0.5f);
    float3 color = 0.0f;
    [unroll] for (uint j = 0u; j < 4u; ++j)
    {
        int2 d = int2(j & 1u, j >> 1u);
        float2 weight = lerp(1.0f - f, f, float2(d));
        if (weight.x * weight.y == 0.0f) continue;
        float4 tap = _SdfFarRadiance.Load(int3(WorldTexelAddress(slot, WorldWrapTexel(base + d)), 0));
        if (tap.w <= 0.0f) return false;
        color += (weight.x * weight.y) * tap.rgb;
    }
    float3 cornerAlpha = lerp(1.0f - alpha, alpha, float3(corner));
    accum += (cornerAlpha.x * cornerAlpha.y * cornerAlpha.z) * float4(color * correction, depth);
    return true;
}

bool SampleWorldCache(float3 position, float3 direction, uint level, out float3 radiance, out float hitDistance)
{
    float3 grid = GetRadianceProbeCoordFloat(position, level) - 0.5f;
    int3 lower = int3(floor(grid));
    float3 alpha = frac(grid);
    float4 result = 0.0;
    radiance = 0.0;
    hitDistance = 0.0;
    [loop] for (uint i = 0u; i < 8u; ++i)
    {
        int3 coord = lower + WorldCorner(i);
        uint slot = uint(_SdfFarRadiance.Load(int3(WorldIndirection(WorldCellIndex(coord, level)), 0)).x) - 1u;
        if (!SampleWorldCacheCorner(position, direction, level, lower, alpha, i, slot, result))
            return false;
    }
    radiance = result.rgb;
    hitDistance = result.w;
    return true;
}

// Native 36-page Global Distance Field lattice, 7 logical voxels per page.
StructuredBuffer<float4> _SdfObjectPageWork;
float ObjectGridVoxel(uint level) { return (50.0f / (36.0f * 7.0f)) * float(1u << level); }
uint ObjectGridMorton(uint3 cell)
{
    uint3 spread = (cell & 1u) | ((cell & 2u) << 2u);
    return spread.x | (spread.y << 1u) | (spread.z << 2u);
}

uint2 FarAtlasAddress(int3 cell, uint bin)
{
    uint3 slot = uint3(cell) & (FarGridSize - 1u);
    return uint2(slot.x + FarGridSize * slot.z, slot.y + FarGridSize * bin);
}

float3 FarProbePosition(int3 cell)
{
    return (float3(cell) + 0.5) * FarCellSize;
}

float3 FarProbeDirection(uint bin)
{
    return VividDecodeSurfaceSummaryNormal((float2(bin % FarOctSize, bin / FarOctSize) + 0.5) / FarOctSize);
}

bool HasFarProbe(int3 cell)
{
    if (any(uint3(cell - int3(_SdfFarWindow.xyz)) >= FarGridSize))
        return false;
    float4 key = _SdfFarRadiance.Load(int3(FarAtlasAddress(cell, FarDirections), 0));
    return key.w != 0.0 && all(key.xyz == float3(cell));
}

uint FarDirectionBin(float3 direction)
{
    uint2 oct = min(uint2(saturate(VividEncodeSurfaceSummaryNormal(direction)) * FarOctSize), FarOctSize - 1u);
    return oct.x + FarOctSize * oct.y;
}

#if defined(VIVID_SDF_SURFACE_CACHE)
StructuredBuffer<uint> _SdfFarHitIds;

float EncodeSurfaceCacheDepth(float Depth, bool bValid)
{
	// Reserve 1.0f for invalid pixels
	float MaxValidDepth = float(0xFFFF - 1 - 0.5f) / float(0xFFFF);
	Depth = min(Depth, MaxValidDepth);
	return bValid ? Depth : 1.0f;
}

bool IsSurfaceCacheDepthValid(float Depth)
{
	return Depth < 1.0f;
}

struct GridCardLighting
{
    float3 sum;
    float total;
    float maximumWeight;
    uint instance;
#if defined(VIVID_SURFACE_CACHE_FEEDBACK)
    float feedbackWeightSum;
    float feedbackMaxSampleWeight;
    uint feedbackCard;
#endif
};

void AccumulateGridCard(uint index, uint slot, uint face, uint slice, float3 cardMin, float3 sizeLS,
    float3 posLS, float3 unitsPerMeter, float normalWeight, float bias,
    float3 rayDirection, inout GridCardLighting accum)
{
    uint axis=face>>1u;
    uint2 tangent = axis==0u ? uint2(1,2) : axis==1u ? uint2(0,2) : uint2(0,1);
    float3 halfExtent=0.5f*sizeLS / unitsPerMeter;
    float3 centered=(posLS-(cardMin+0.5f*sizeLS))/unitsPerMeter;
    if(any(abs(centered)>halfExtent+0.5f*bias))return;
    float2 extentXY=float2(halfExtent[tangent.x],halfExtent[tangent.y]);
    if (any(extentXY <= 0.0f) || halfExtent[axis] <= 0.0f) return;
    float3 CardSpacePosition=float3(clamp(float2(centered[tangent.x],centered[tangent.y]),-extentXY,extentXY), (face&1u)?centered[axis]:-centered[axis]);
    float3 LocalExtent=float3(extentXY,halfExtent[axis]);
    float2 CardUV=min(CardSpacePosition.xy/(2.0f*LocalExtent.xy)+0.5f,0.999999f);
    uint width,height,slices;
    _SdfSurfaceAlbedoDepth.GetDimensions(width,height,slices);
    float2 atlasSize=float2(width,height);
    float SubTexelPrecisionBias=1.0f/512.0f;
    float2 CoordInPage=clamp(CardUV*atlasSize,0.5f,atlasSize-0.5f-SubTexelPrecisionBias);
    float2 FracUV=frac(CoordInPage+0.5f+SubTexelPrecisionBias);
    float4 TexelBilinearWeights;
    TexelBilinearWeights.x=(1.0f-FracUV.x)*(FracUV.y);
    TexelBilinearWeights.y=(FracUV.x)*(FracUV.y);
    TexelBilinearWeights.z=(FracUV.x)*(1-FracUV.y);
    TexelBilinearWeights.w=(1-FracUV.x)*(1-FracUV.y);
    // Point-sampler Gather on the native texture array uses the same four lanes as UE.
    float4 TexelDepths;
    float4 packedNormals[4];
    uint2 low=uint2(floor(CoordInPage-0.5f+SubTexelPrecisionBias));
    [unroll]
    for(uint k=0;k<4;k++){
        int4 texel=int4(min(low + uint2(k == 1u || k == 2u, k < 2u),uint2(width-1u,height-1u)),slice,0);
        float raw=_SdfSurfaceAlbedoDepth.Load(texel).w;
        packedNormals[k]=_SdfSurfaceNormalCoverage.Load(texel);
        TexelDepths[k]=EncodeSurfaceCacheDepth((face&1u)?1.0f-raw:raw,packedNormals[k].w>0);
    }
    const bool bHeightfield = false;
    float NormalWeight=normalWeight, SurfaceCacheBias=bias;
    float NormalizedHitDistance = -(CardSpacePosition.z / LocalExtent.z) * 0.5f + 0.5f;
    float BiasTreshold = SurfaceCacheBias / LocalExtent.z;
    float BiasFalloff = 0.25f * BiasTreshold;

    float4 TexelVisibility = 0.0f;
    for (uint TexelIndex = 0; TexelIndex < 4; ++TexelIndex)
    {
        // Skip invalid texels
        if (IsSurfaceCacheDepthValid(TexelDepths[TexelIndex]))
        {
            // No need to depth test heightfields
            if (bHeightfield)
            {
                TexelVisibility[TexelIndex] = 1.0f;
            }
            else
            {
                TexelVisibility[TexelIndex] = 1.0f - saturate((abs(NormalizedHitDistance - TexelDepths[TexelIndex]) - BiasTreshold) / BiasFalloff);
            }
        }
    }

    float4 TexelWeights = TexelBilinearWeights * TexelVisibility;

    float CardSampleWeight = NormalWeight * dot(TexelWeights, 1.0f);

    if (CardSampleWeight <= 0.0f) return;
    accum.total += CardSampleWeight;
    if (CardSampleWeight > accum.maximumWeight)
    {
        accum.maximumWeight = CardSampleWeight;
        accum.instance = index;
    }
    DiffuseSurface surface = _SdfDiffuseSurfaces[index];
    uint finalCard;
    bool ready = TryGetFinalLightingCard(surface, slot, finalCard);
    float3 emission = max(surface.emission.rgb, 0.0f);
    [branch]
    if (!ready && !any(emission > 0.0f)) return;

    // Culled material sides remain opaque and contribute black to the normalized result.
    float4 lightWeights = TexelWeights;
    if (surface.emission.w != 0.0f)
    {
        VividMeshSdfInstance inst = _MeshSdfInstances[index];
        float3 a = inst.worldToLocal0.xyz, b = inst.worldToLocal1.xyz, c = inst.worldToLocal2.xyz;
        [unroll] for (uint tap = 0u; tap < 4u; ++tap)
        {
            if (lightWeights[tap] <= 0.0f) continue;
            float3 normalLS = packedNormals[tap].xyz * 2.0f - 1.0f;
            float3 normal = SafeNormalize(a * normalLS.x + b * normalLS.y + c * normalLS.z);
            float3 originalNormal = packedNormals[tap].w < 0.75f ? -normal : normal;
            if (!VividSdfMaterialSideVisible(surface.emission.w, originalNormal, -rayDirection))
                lightWeights[tap] = 0.0f;
        }
    }

    [branch]
    if (!ready)
    {
        accum.sum += NormalWeight * dot(lightWeights, 1.0f) * emission;
        return;
    }

#if defined(VIVID_SURFACE_CACHE_FEEDBACK)
    // The feedback representative is the highest-weight page actually sampled.
    float feedbackWeight = NormalWeight * dot(lightWeights, 1.0f);
    accum.feedbackWeightSum += feedbackWeight;
    if (feedbackWeight > accum.feedbackMaxSampleWeight)
    {
        accum.feedbackMaxSampleWeight = feedbackWeight;
        accum.feedbackCard = finalCard;
    }
#endif
    uint2 atlasOrigin = FinalLightingCardOrigin(finalCard);
    if (all(lightWeights == TexelBilinearWeights))
    {
        uint atlasWidth, atlasHeight;
        _SdfFinalLightingAtlas.GetDimensions(atlasWidth, atlasHeight);
        float2 texel = clamp(float2(low) + FracUV + 0.5f, 0.5f, float2(width, height) - 0.5f);
        float2 atlasUV = (float2(atlasOrigin) + texel) / float2(atlasWidth, atlasHeight);
        accum.sum += CardSampleWeight * _SdfFinalLightingAtlas.SampleLevel(sampler_LinearClamp, atlasUV, 0.0f).rgb;
    }
    else
    {
        // Depth and material masks produce nonseparable weights at card edges.
        [unroll] for (uint tap = 0u; tap < 4u; ++tap)
        {
            float weight = NormalWeight * lightWeights[tap];
            if (weight <= 0.0f) continue;
            uint2 pixel = min(low + uint2(tap == 1u || tap == 2u, tap < 2u), uint2(width - 1u, height - 1u));
            accum.sum += weight * _SdfFinalLightingAtlas.Load(int3(atlasOrigin + pixel, 0)).rgb;
        }
    }
}

StructuredBuffer<uint> _SdfObjectPages;
StructuredBuffer<uint4> _SdfObjectGrid;

GridCardLighting EvaluateGlobalDistanceFieldHit(float3 pos, float3 normal, float3 direction, uint level)
{
    GridCardLighting result = (GridCardLighting)0;
    result.instance = 0xffffffffu;
    if (dot(normal, direction) >= 0.0f) return result;
    float halfVoxel = 0.5f * ObjectGridVoxel(level);
    float3 samplePosition = pos + normal * halfVoxel;
    float3 pageCoord = samplePosition / (ObjectGridVoxel(level) * 7.0f);
    int3 page = int3(floor(pageCoord));
    uint3 wrapped = uint3((page % 36 + 36) % 36);
    uint entry = _SdfObjectPages[wrapped.x + 36u * (wrapped.y + 36u * (wrapped.z + 36u * level))];
    if (entry == 0u) return result;
    if (any(_SdfObjectPageWork[entry-1u].xyz != float3(page))) return result;
    uint3 cell = uint3(frac(frac(pageCoord)) * 4.0f);
    uint4 objects = _SdfObjectGrid[(entry-1u) * 64u + ObjectGridMorton(cell)];
    for (uint j = 0u; j < 4u; ++j)
    {
        if (objects[j] == 0xffffffffu) break;
        uint index = objects[j] & 0xffffffu;
        float SurfaceCacheBias = 3.0f * (0.5f * _GlobalSdfLevels[min(level, VividGlobalSdfLevelCount - 1u)].w);
        if (_MeshSdfFields[_MeshSdfInstances[index].fieldIndex].mostlyTwoSided != 0u)
            SurfaceCacheBias += 0.50f;
        DiffuseSurface surface=_SdfDiffuseSurfaces[index];
        uint owner=uint(surface.cardMinAndFirst.w);
        if(owner==0u || surface.albedoAndValid.w == 0.0f) continue;
        VividMeshSdfInstance inst=_MeshSdfInstances[index];
        float3 units=float3(length(inst.worldToLocal0.xyz),length(inst.worldToLocal1.xyz),length(inst.worldToLocal2.xyz));
        float4 p=float4(pos,1.0f);
        float3 posLS=float3(dot(inst.worldToLocal0,p),dot(inst.worldToLocal1,p),dot(inst.worldToLocal2,p));
        float3 meshNormal=float3(dot(inst.worldToLocal0.xyz,normal),dot(inst.worldToLocal1.xyz,normal),dot(inst.worldToLocal2.xyz,normal))/units;
        float3 AxisWeights=meshNormal*meshNormal;
        uint2 local=(uint2)_SdfLocalCardTable[(owner-1u)/6u].xy;
        uint count=6u+(local.y-local.x)/2u;
        for(uint slot=0;slot<count;slot++) {
            uint face=slot,slice=owner-1u+slot;float3 lower=surface.cardMinAndFirst.xyz,size=surface.cardExtent.xyz;
            if(slot>=6u) {
                uint row=local.x+2u*(slot-6u);
                float4 a=_SdfLocalCardTable[row],b=_SdfLocalCardTable[row+1u];
                lower=a.xyz;size=b.xyz;slice=uint(a.w);face=uint(b.w);
            }
            uint axis=face>>1u;
            if(AxisWeights[axis]>0.0f && ((face&1u)!=0u)==(meshNormal[axis]>=0.0f))
                AccumulateGridCard(index,slot,face,slice,lower,size,posLS,units,AxisWeights[axis],SurfaceCacheBias,direction,result);
        }
        if(result.total>=0.9f)break;
    }
    if (result.total > 0.0f) result.sum /= result.total;
    return result;
}


#if defined(VIVID_SDF_FAR_UPDATE)
float4 TraceFarProbe(float3 origin, float3 direction, float start, out uint instance)
{
    instance = 0u;
    VividMeshSdfHit hit;
    bool advancedReceiver, incomplete;
#if defined(VIVID_SDF_WORLD)
    float end = _SdfFarTraceDistance;
#else
    float end = _SdfFarTraceDistance - _SdfDiffuseMaxDistance;
#endif
#if defined(VIVID_SDF_WORLD)
    if (_SdfUseGlobalClipmap == 2u)
    {
        VividGlobalSdfHit global;
        if (VividTraceGlobalSdf(origin, direction, start, end, global))
        {
            float cardDistance = global.distance + 0.5f * global.expandSurfaceAmount;
            GridCardLighting lighting = EvaluateGlobalDistanceFieldHit(origin + direction * cardDistance,
                global.normalWS, direction, global.level);
            instance = lighting.instance + 1u;
            return float4(lighting.sum, max(global.distance, 0.001f));
        }
        #if defined(VIVID_SDF_WORLD_SKY)
            if (ResolveSdfSkyMiss(origin, direction, start, false, 0u, (VividMeshSdfReceiver)0) == RaySkyVisible)
                return float4(SampleSkyTexture(direction, 0.0), end);
            return float4(0.0f, 0.0f, 0.0f, end);
        #else
            return float4(0,0,0,end);
        #endif
    }
#endif
    bool traced = VividTraceMeshSdfSceneInternal(origin, direction, start, end,
        _SdfDiffuseLayerMask, 0.0, false, (VividMeshSdfReceiver)0, hit, advancedReceiver, incomplete);
#if defined(VIVID_SDF_WORLD)
    if (!traced && !incomplete)
#if defined(VIVID_SDF_WORLD_SKY)
        return float4(SampleSkyTexture(direction, 0.0), end);
#else
        return float4(0.0, 0.0, 0.0, end);
#endif
    if (traced && !incomplete && dot(hit.normalWS, direction) >= -0.05)
        return float4(0.0, 0.0, 0.0, max(hit.distance, 0.001));
#endif
    if (!traced || incomplete || (start == 0.0 && hit.distance < 0.5 * FarCellSize) || dot(hit.normalWS, direction) >= -0.05)
        return 0.0;

    float3 position = origin + direction * hit.distance;
    DiffuseSurface surface = _SdfDiffuseSurfaces[hit.instanceIndex];
    float3 normal, originalNormal;
    uint3 texel;
    float error;
    if (surface.albedoAndValid.a == 0.0 || !VividTrySampleSdfSurface(hit, position, surface, normal, originalNormal, texel, error))
        return 0.0;
    float3 radiance = LoadSdfSurfaceRadiance(originalNormal, direction, surface, texel);
    instance = hit.instanceIndex + 1u;
    return float4(clamp(radiance, 0.0, 65000.0), hit.distance);
}

#endif

bool TryLoadFarRadiance(float3 origin, float3 direction, out float3 radiance, out float distance)
{
    radiance = 0.0;
    distance = 0.0;
    float3 start = origin + _SdfDiffuseMaxDistance * direction;
    int3 cell = int3(floor(start / FarCellSize));
    if (!HasFarProbe(cell))
        return false;

    uint bin = FarDirectionBin(direction);
    float4 cached = _SdfFarRadiance.Load(int3(FarAtlasAddress(cell, bin), 0));
    if (cached.a <= 0.0)
        return false;
    float3 cacheDirection = FarProbeDirection(bin);
    float3 end = FarProbePosition(cell) + cacheDirection * cached.a;
    float3 delta = end - start;
    float travel = length(delta);
    float3 reconnect = delta / max(travel, 1e-5);
    if (travel <= 0.5 * FarCellSize || travel + _SdfDiffuseMaxDistance > _SdfFarTraceDistance
        || dot(reconnect, direction) < 0.965)
        return false;

    // Match the cached endpoint after the near trace. An intervening hit or
    // an unfinished march leaves the far sample unresolved.
    float tolerance = clamp(0.5 * _SdfDiffuseRayBias, 0.01, 0.025);
    VividMeshSdfHit hit;
    bool advancedReceiver, incomplete;
    bool traced = VividTraceMeshSdfSceneInternal(start, reconnect, 0.0, travel + tolerance,
        _SdfDiffuseLayerMask, 0.0, false, (VividMeshSdfReceiver)0, hit, advancedReceiver, incomplete);
    if (!traced || incomplete || abs(hit.distance - travel) > tolerance
        || dot(hit.normalWS, reconnect) >= -0.05 || dot(hit.normalWS, cacheDirection) >= -0.05)
        return false;
    DiffuseSurface surface = _SdfDiffuseSurfaces[hit.instanceIndex];
    float3 normal, originalNormal;
    uint3 texel;
    float error;
    if (!VividTrySampleSdfSurface(hit, start + reconnect * hit.distance, surface,
        normal, originalNormal, texel, error))
        return false;
    radiance = VividSdfMaterialSideVisible(surface.emission.w, originalNormal, -reconnect) ? cached.rgb : 0.0;
    distance = travel + _SdfDiffuseMaxDistance;
    return true;
}

uint FarHitIndex(uint2 address)
{
    return address.x + address.y * (FarGridSize * FarGridSize);
}

float3 ReconstructFarRadiance(float3 start, float3 hitPosition, VividMeshSdfHit hit, out float coverage)
{
    float3 grid = start / FarCellSize - 0.5;
    int3 lower = int3(floor(grid));
    float3 fraction = frac(grid);
    float3 sum = 0.0;
    float total = 0.0;
    [loop]
    for (uint corner = 0u; corner < 8u; ++corner)
    {
        uint3 offset = uint3(corner & 1u, (corner >> 1u) & 1u, corner >> 2u);
        int3 cell = lower + int3(offset);
        if (any(uint3(cell - int3(_SdfFarWindow.xyz)) >= FarGridSize))
            continue;
        float4 key = _SdfFarRadiance.Load(int3(FarAtlasAddress(cell, FarDirections), 0));
        if (key.w == 0.0 || any(key.xyz != float3(cell)))
            continue;

        float3 delta = hitPosition - FarProbePosition(cell);
        float range = length(delta);
        if (range < 0.5 * FarCellSize || dot(delta / range, hit.normalWS) >= -0.05)
            continue;
        float3 spatial = lerp(1.0 - fraction, fraction, float3(offset));
        float2 oct = VividEncodeSurfaceSummaryNormal(delta / range) * FarOctSize - 0.5;
        int2 low = int2(floor(oct));
        float2 angularFraction = frac(oct);
        float radius = clamp(0.15 * range, 0.30, 1.25);
        [unroll]
        for (uint tap = 0u; tap < 4u; ++tap)
        {
            uint2 angularOffset = uint2(tap & 1u, tap >> 1u);
            uint2 binXY = uint2(clamp(low + int2(angularOffset), 0, int(FarOctSize) - 1));
            uint bin = binXY.x + FarOctSize * binXY.y;
            uint2 address = FarAtlasAddress(cell, bin);
            if (_SdfFarHitIds[FarHitIndex(address)] != hit.instanceIndex + 1u)
                continue;
            float4 cached = _SdfFarRadiance.Load(int3(address, 0));
            float3 sampleDirection = FarProbeDirection(bin);
            float3 error = cached.a * sampleDirection - delta;
            float depth = abs(dot(error, hit.normalWS));
            if (dot(sampleDirection, hit.normalWS) >= -0.05 || depth >= 0.10 || length(error) >= radius)
                continue;
            float2 angular = lerp(1.0 - angularFraction, angularFraction, float2(angularOffset));
            float weight = spatial.x * spatial.y * spatial.z * angular.x * angular.y
                * saturate(1.0 - depth / 0.10) * saturate(1.0 - length(error) / radius);
            sum += weight * cached.rgb;
            total += weight;
        }
    }
    // Fade to exact hit lighting where a probe is warming up or the local footprint is incomplete.
    coverage = smoothstep(0.05, 0.55, total);
    return sum / max(total, 1e-5);
}
#endif

#endif
