#ifndef VIVID_LUMEN_DEBUG_VIEW_INCLUDED
#define VIVID_LUMEN_DEBUG_VIEW_INCLUDED

#include "../../Public/VividBentNormalPacking.hlsl"

// Modes 1–24 match UE 5.7 LumenVisualize.h and LumenDebugViewMode.
uint _LumenDebugMode;
RWTexture2D<float4> _LumenDebugOutput;
float4 _LumenDebugParameters; // Exposure multiplier, source available, frame, trace distance.
Texture2D<float4> _LumenDebugFeed;
Texture2D<uint> _LumenDebugPackedFeed;

bool LumenDebugPixel(uint2 pixel, out float2 uv)
{
    uint2 size;
    _LumenDebugOutput.GetDimensions(size.x, size.y);
    uv = (float2(pixel) + 0.5f) / float2(size);
    return all(pixel < size);
}

float3 LumenDebugUnavailable(uint2 pixel)
{
    return float3(0.09f, 0.1f, 0.13f) * ((((pixel.x + pixel.y) / 8u) & 1u) != 0u ? 1.0f : 0.5f);
}

float3 LumenDebugToneMap(float3 radiance)
{
    float3 color = max(radiance, 0.0f) * _LumenDebugParameters.x;
    return color / (1.0f + color);
}

void LumenDebugRay(float2 uv, out float3 origin, out float3 direction)
{
    float4 nearPoint = mul(_SdfDiffuseInverseVP, ComputeClipSpacePosition(uv, UNITY_NEAR_CLIP_VALUE));
    float4 farPoint = mul(_SdfDiffuseInverseVP, ComputeClipSpacePosition(uv, UNITY_RAW_FAR_CLIP_VALUE));
    origin = nearPoint.xyz / nearPoint.w;
    direction = SafeNormalize(farPoint.xyz - farPoint.w * origin);
}

uint2 LumenDebugPixelAt(float2 uv, uint2 size)
{
    return min(uint2(uv * size), size - 1u);
}

[numthreads(8, 8, 1)]
void LumenDebugTextureCS(uint2 pixel : SV_DispatchThreadID)
{
    float2 uv;
    if (!LumenDebugPixel(pixel, uv)) return;
    float3 color = LumenDebugUnavailable(pixel);
    if (_LumenDebugParameters.y > 0.0f)
    {
        uint2 size;
        _LumenDebugFeed.GetDimensions(size.x, size.y);
        if (_LumenDebugMode == 31u)
            size.y = min(size.y, 2048u);
        float4 value = _LumenDebugFeed.Load(int3(LumenDebugPixelAt(uv, size), 0));
        if (_LumenDebugMode == 23u || _LumenDebugMode == 24u)
        {
            // Same nibble visualization as LumenVisualizeScreenProbeGatherTemporalPS.
            uint packed = (uint)round(value.r * 255.0f);
            uint nibble = _LumenDebugMode == 23u ? packed >> 4u : packed & 15u;
            color = (nibble / 15.0f).xxx;
        }
        else if (_LumenDebugMode == 25u)
            color = value.rgb;
        else if (_LumenDebugMode == 28u)
            color = value.a.xxx;
        else if (_LumenDebugMode == 29u)
            color = value.a > 0.0f ? SafeNormalize(value.xyz) * 0.5f + 0.5f : 0.0f;
        else if (_LumenDebugMode == 32u)
        {
            uint2 slot = LumenDebugPixelAt(uv, uint2(64u, 64u));
            uint index = slot.x + slot.y * 64u;
            uint2 keyAddress = uint2(index & 2047u, 2048u + (index >> 11u));
            float4 key = _LumenDebugFeed.Load(int3(keyAddress, 0));
            float lastTrace = _LumenDebugFeed.Load(int3(keyAddress + uint2(0, 2), 0)).x;
            color = key.w <= 0.0f ? float3(0, 0, 0) : lastTrace <= 0.0f ? float3(1, 0.6, 0)
                : (1.0f - saturate((_LumenDebugParameters.z - lastTrace) / 64.0f)).xxx;
        }
        else if (_LumenDebugMode == 34u)
        {
            uint state = (uint)floor(value.a);
            uint source = state & 7u;
            if ((state & RayTraceIncomplete) != 0u) color = float3(1, 0.55, 0);
            else if (source == RayBackface || (state & RayMaterialBackface) != 0u) color = float3(0.45, 0.15, 0.6);
            else if (source == RayScreenHit) color = float3(0.15, 1, 0.25);
            else if (source == RaySdfHit) color = (state & RayGlobalCandidate) != 0u ? float3(0, 0.6, 1) : float3(1, 0.3, 0.1);
            else if (source == RayMissingSurface) color = float3(1, 0.15, 0.55);
            else if (source == RaySkyVisible) color = float3(0.5, 0.7, 1);
            else color = 0.0f;
        }
        else
            color = LumenDebugToneMap(value.rgb);
    }
    _LumenDebugOutput[pixel] = float4(color, 1.0f);
}

[numthreads(8, 8, 1)]
void LumenDebugBentNormalCS(uint2 pixel : SV_DispatchThreadID)
{
    float2 uv;
    if (!LumenDebugPixel(pixel, uv)) return;
    uint2 size;
    _LumenDebugPackedFeed.GetDimensions(size.x, size.y);
    float3 bent = UnpackScreenBentNormal(_LumenDebugPackedFeed.Load(int3(LumenDebugPixelAt(uv, size), 0)));
    float magnitude = length(bent);
    float3 color = _LumenDebugMode == 28u ? saturate(magnitude).xxx
        : bent / max(magnitude, 0.0001f) * 0.5f + 0.5f;
    _LumenDebugOutput[pixel] = float4(color, 1.0f);
}

[numthreads(8, 8, 1)]
void LumenDebugGlobalSdfCS(uint2 pixel : SV_DispatchThreadID)
{
    float2 uv;
    if (!LumenDebugPixel(pixel, uv)) return;
    float3 origin, direction;
    LumenDebugRay(uv, origin, direction);
    VividGlobalSdfHit hit;
    float3 color = 0;
    if (VividTraceGlobalSdf(origin, direction, 0, _LumenDebugParameters.w, hit, true, false))
        color = hit.normalWS * 0.5f + 0.5f;
    _LumenDebugOutput[pixel] = float4(color, 1);
}

#if defined(VIVID_LUMEN_DEBUG_SURFACE)
Texture2D<float4> _LumenDebugDirect;
Texture2D<float2> _LumenDebugCardFrames;
RWTexture2D<float2> _LumenDebugCardFramesOutput;
float4 _LumenDebugCardUpdates; // Direct and indirect begin/end.
float4 _LumenDebugFrameParameters; // Frame, atlas card count, clear, indirect order uses priority ranks.
#if defined(VIVID_SURFACE_CACHE_FEEDBACK)
uint _LumenDebugVisualizeFeedback;
#endif

[numthreads(64, 1, 1)]
void LumenDebugCardFramesCS(uint card : SV_DispatchThreadID)
{
    uint count = uint(_LumenDebugFrameParameters.y);
    if (card >= count) return;
    uint2 address = uint2(card & 63u, card >> 6u);
    uint rank = _SdfFinalLightingOrder[count + card];
    uint indirect = _LumenDebugFrameParameters.w != 0.0f ? rank : card;
    float2 frames = _LumenDebugFrameParameters.z != 0.0f ? 0.0f : _LumenDebugCardFramesOutput[address];
    if (rank >= _LumenDebugCardUpdates.x && rank < _LumenDebugCardUpdates.y)
        frames.x = _LumenDebugFrameParameters.x + 1.0f;
    if (indirect >= _LumenDebugCardUpdates.z && indirect < _LumenDebugCardUpdates.w)
        frames.y = _LumenDebugFrameParameters.x + 1.0f;
    _LumenDebugCardFramesOutput[address] = frames;
}

float3 LumenDebugSurface(uint mode, float2 uv, uint2 pixel)
{
    if (mode == 4u || _LumenDebugParameters.y <= 0.0f)
        return LumenDebugUnavailable(pixel);

    float3 origin, direction;
    LumenDebugRay(uv, origin, direction);
    VividMeshSdfHit hit;
    bool traced = VividTraceMeshSdfScene(origin, direction, 0.0f, _LumenDebugParameters.w, _SdfDiffuseLayerMask, hit);
    if (mode == 5u)
    {
        uint2 sourceSize = uint2(_SdfDiffuseScreenSize.xy);
        uint2 sourcePixel = LumenDebugPixelAt(uv, sourceSize);
        float depth = _SdfDiffuseDepth.Load(int3(sourcePixel, 0));
        if (depth != UNITY_RAW_FAR_CLIP_VALUE)
        {
            float3 raster = ComputeWorldSpacePosition(uv, depth, _SdfDiffuseInverseVP);
            // The raster surface may sit within a captured voxel shell.
            if (!traced || hit.distance > distance(origin, raster) + max(0.05f, 0.01f * hit.distance))
                return float3(1.0f, 0.8f, 0.0f);
        }
    }
    if (!traced) return 0.0f;
    if (mode == 6u) return hit.normalWS * 0.5f + 0.5f;

    float3 position = origin + direction * hit.distance;
    VividMeshSdfInstance instance = _MeshSdfInstances[hit.instanceIndex];
    DiffuseSurface surface = _SdfDiffuseSurfaces[hit.instanceIndex];
    if (mode == 15u || mode == 16u)
    {
        float4 p = float4(position, 1.0f);
        float3 local = float3(dot(instance.worldToLocal0, p), dot(instance.worldToLocal1, p), dot(instance.worldToLocal2, p));
        if (mode == 15u) return frac(local);
        if (instance.motionFrame != (uint)_LumenDebugParameters.z) return 0.0f;
        float4 localPoint = float4(local, 1.0f);
        float3 previous = float3(dot(instance.previousLocalToWorld0, localPoint), dot(instance.previousLocalToWorld1, localPoint),
            dot(instance.previousLocalToWorld2, localPoint));
        return saturate(abs(position - previous));
    }

    float3 normal, originalNormal;
    uint3 texel;
    float error;
    bool covered = VividTrySampleSdfSurface(hit, position, surface, normal, originalNormal, texel, error);
    if (mode == 11u) return covered ? 1.0f : 0.0f;
    if (!covered) return mode == 5u ? float3(1, 0.15, 0.55) : 0.0f;
    if (mode == 5u || mode == 3u)
    {
#if defined(VIVID_SURFACE_CACHE_FEEDBACK)
        if (_LumenDebugVisualizeFeedback != 0u)
        {
            if (!VividSdfMaterialSideVisible(surface.emission.w, originalNormal, -direction))
                return 0.0f;
            // Visualization permits high-res pages. Vivid currently maps this request to its resident card.
            return LumenDebugToneMap(LoadCardFinalLightingOrEmission(surface, texel, pixel, true));
        }
#endif
        return LumenDebugToneMap(LoadSdfSurfaceRadiance(originalNormal, direction, surface, texel));
    }
    if (mode == 8u) return surface.albedoAndValid.rgb;
    if (mode == 9u) return normal * 0.5f + 0.5f;
    if (mode == 10u) return LumenDebugToneMap(max(surface.emission.rgb, 0.0f));
    if (mode == 12u || mode == 22u)
    {
        uint slice, axis;
        float3 boundsMin, extent;
        VividGetSdfCardProjection(surface, texel.z, slice, axis, boundsMin, extent);
        if (mode == 22u)
        {
            uint hash = HashProbe(slice);
            return float3(hash & 255u, (hash >> 8u) & 255u, (hash >> 16u) & 255u) / 255.0f;
        }
        float3 tint;
        tint.x = (slice % 4u) / 3.0f;
        tint.y = ((slice / 4u) % 4u) / 3.0f;
        tint.z = saturate(1.0f - tint.x - tint.y);
        float2 grid = frac((texel.xy + 0.5f) / 8.0f);
        grid = smoothstep(abs(grid - 0.5f), 0.0f, 0.01f);
        return tint * lerp(0.25f, 1.0f, saturate(grid.x * grid.y));
    }
    if (mode == 14u)
        return LumenDebugToneMap(LoadCardIndirect(surface, hit, texel));

    uint card;
    if (!TryGetFinalLightingCard(surface, texel.z, card)) return 0.0f;
    if (mode == 13u)
        return LumenDebugToneMap(_LumenDebugDirect.Load(int3(FinalLightingCardOrigin(card) + texel.xy, 0)).rgb);
#if defined(VIVID_SURFACE_CACHE_FEEDBACK)
    if (mode == 19u || mode == 20u)
    {
        uint2 lastUsed = _SdfSurfaceCacheLastUsed[card];
        uint lastUsedFrame = mode == 19u ? lastUsed.x : lastUsed.y;
        if (lastUsedFrame == 0u) return 0.0f;
        uint FramesSinceLastUpdated = _SdfSurfaceCacheFeedbackFrame - lastUsedFrame;
        float3 VisColor = lerp(float3(1, 0, 0), float3(0, 0, 1), saturate(FramesSinceLastUpdated / 8.0f));
        if (FramesSinceLastUpdated < 1u)
            VisColor = float3(1, 1, 1);
        return VisColor;
    }
#endif
    float2 frames = _LumenDebugCardFrames.Load(int3(uint2(card & 63u, card >> 6u), 0));
    float lastUpdate = mode == 17u ? frames.x : frames.y;
    float scale = mode == 17u ? 8.0f : 16.0f;
    return lastUpdate > 0.0f ? (1.0f - saturate((_LumenDebugFrameParameters.x + 1.0f - lastUpdate) / scale)).xxx : 0.0f;
}

[numthreads(8, 8, 1)]
#if defined(VIVID_SURFACE_CACHE_FEEDBACK)
void LumenDebugSurfaceFeedbackCS(uint2 pixel : SV_DispatchThreadID)
#else
void LumenDebugSurfaceCS(uint2 pixel : SV_DispatchThreadID)
#endif
{
    float2 uv;
    if (!LumenDebugPixel(pixel, uv)) return;
    uint mode = _LumenDebugMode;
    if (mode == 1u)
    {
        uint tile = min(uint(uv.x * 3.0f), 2u);
        uv.x = frac(uv.x * 3.0f);
        mode = tile == 0u ? 6u : tile == 1u ? 4u : 5u;
    }
    _LumenDebugOutput[pixel] = float4(LumenDebugSurface(mode, uv, pixel), 1.0f);
}
#endif

#endif
