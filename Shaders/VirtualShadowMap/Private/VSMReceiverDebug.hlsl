// Included only by the opt-in VSMReceiverDebug kernel. No feedback or pool writes.
#include "../Debug/VSMPageDebug.hlsl"
RWTexture2D<float4> _VSMReceiverDebugOutput;
RWTexture2D<float4> _VSMReceiverDebugData;
Texture2D<float> _VSMReceiverDebugShadow;
int _VSMReceiverDebugMode;

float3 VSMReceiverLevelColor(int level)
{
    if (level < 0) return float3(1, 0, 1);
    return 0.25 + 0.75 * frac(float3(0.37, 0.61, 0.83) * (level + 1));
}

[numthreads(8, 8, 1)]
void VSMReceiverDebug(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)_CSMOutputWidth || id.y >= (uint)_CSMOutputHeight) return;
    uint2 pixel = id.xy;
    // Show the actual production mask without re-running filtering/SMRT.
    if (_VSMReceiverDebugMode == 10)
    {
        float mask = _VSMReceiverDebugShadow.Load(int3(pixel, 0));
        _VSMReceiverDebugData[pixel] = float4(mask, 0, 0, 1);
        _VSMReceiverDebugOutput[pixel] = float4(mask.xxx, 1);
        return;
    }
    g_VSMDebugLevels = -1;
    g_VSMDebugBlend = 0;
    g_VSMDebugWork = 0;
    g_VSMDebugMissing = 0;
    g_VSMDebugQuality = -1;
    g_VSMDebugSMRT = 0;
    float depth = _DepthTexture.Load(int3(pixel, 0));
    // -2 is sky; -1 is unavailable/outside selection. Neither is valid lit depth.
    if (IsSkyPixel(depth))
    {
        _VSMReceiverDebugData[pixel] = -2.0;
        _VSMReceiverDebugOutput[pixel] = float4(0, 0, 0, 1);
        return;
    }
    float3 position = ReconstructWorldPosition(pixel, depth);
    float3 normal = DecodeVividNormalOct(_GBuffer1.Load(int3(pixel, 0)).xy);
    normal = ReconstructVSMReceiverNormal(pixel, depth, position, normal);
    float shadow = ResolveVSMReceiver(position, normal, pixel);
    float4 data = float4(g_VSMDebugLevels, g_VSMDebugBlend);
    float3 color = VSMReceiverLevelColor(g_VSMDebugLevels.x);
    if (_VSMReceiverDebugMode == 1)
        color = VSMReceiverLevelColor(g_VSMDebugLevels.y);
    else if (_VSMReceiverDebugMode == 2)
    {
        int fallback = g_VSMDebugLevels.y - g_VSMDebugLevels.x;
        color = g_VSMDebugLevels.y < 0 ? float3(1, 0, 1)
            : lerp(float3(0, 0.7, 0), float3(1, 0, 0), saturate(fallback / 4.0));
    }
    else if (_VSMReceiverDebugMode == 3)
    {
        int level = g_VSMDebugLevels.y;
        float2 footprint = VSMReceiverTexelFootprint(position, normal, level);
        data = float4(footprint, level >= 0 ? _VSMProjections[level].parameters.x : 0,
            all(footprint >= 0) ? 1 : 0);
        float worst = max(footprint.x, footprint.y);
        color = worst < 0 ? float3(1, 0, 1)
            : lerp(float3(0, 0.7, 0), float3(1, 0, 0), saturate((worst - 1) / 3));
    }
    else if (_VSMReceiverDebugMode == 4)
    {
        data = float4(g_VSMDebugWork, g_VSMDebugLevels.z >= 0 ? 1 : 0);
        color = lerp(float3(0, 0.15, 0.5), float3(1, 0.1, 0), saturate(data.x / 36));
    }
    else if (_VSMReceiverDebugMode == 5)
    {
        float source = _VSMReceiverDebugShadow.Load(int3(pixel, 0));
        data = float4(g_VSMDebugMissing, shadow, source, abs(shadow - source));
        color = g_VSMDebugLevels.x < 0 ? float3(0.3, 0.3, 0.3)
            : g_VSMDebugLevels.y < 0 ? float3(1, 0, 1)
            : g_VSMDebugMissing != 0 ? float3(1, 0.6, 0) : float3(0, 0.7, 0);
    }
    else if (_VSMReceiverDebugMode == 6)
    {
        data = _VSMReceiverQuality.x > 0 ? g_VSMDebugQuality : -1;
        color = data.w < 0 ? float3(1, 0, 1)
            : lerp(float3(0, 0.7, 0), float3(1, 0, 0), saturate((data.w - 1) / 3));
    }
    if (_VSMReceiverDebugMode == 7)
    {
        data = float4(g_VSMDebugSMRT);
        color = float3(saturate(data.y / max(data.x, 1)), saturate(data.z), saturate(data.w));
    }
    if (_VSMReceiverDebugMode == 8 || _VSMReceiverDebugMode == 9)
    {
        // Inspect intended demand, not the coarser successful fallback which would
        // hide deferred or missing fine pages. This is the central biased texel;
        // a filter/SMRT footprint can touch additional pages.
        int level = g_VSMDebugLevels.x;
        data = -1;
        color = float3(1, 0, 1);
        if (level >= 0 && level < _VSMProjectionCount)
        {
            float3 unitNormal = normal * rsqrt(max(dot(normal, normal), 1e-8));
            VSMReceiverProjection prepared = PrepareVSMReceiverProjection(position, unitNormal, level);
            int2 texel;
            if (VividVSMTryOffsetVirtualTexel(prepared.coord.xy, int2(0, 0),
                (uint)_VSMPrototypeVirtualResolution, texel))
            {
                uint2 page = (uint2)texel / (uint)_VSMPrototypePageSize;
                uint axis = (uint)_VSMPrototypePagesPerAxis;
                uint index = (uint)level * axis * axis + page.y * axis + page.x;
                uint4 metadata = _VSMPrototypePageMetadata[index];
                uint mapping = _VSMPrototypePageTable[index];
                bool allocated = mapping != 0u && (metadata.x & kVSMPageAllocated) != 0u;
                if (_VSMReceiverDebugMode == 8)
                {
                    // Preserve the exported cache/request snapshot without storing
                    // transient request roles in the resident metadata buffer.
                    data = float4(metadata.x, metadata.w | _VSMPageRequestFlags[index], mapping, level);
                    color = VSMDebugCacheColor(metadata, allocated, 0u);
                }
                else
                {
                    data = float4(page, level, mapping);
                    color = VSMDebugIndexColor(index);
                    if (any((uint2)texel % (uint)_VSMPrototypePageSize < 1u)) color *= 0.3;
                }
            }
        }
    }
    _VSMReceiverDebugData[pixel] = data;
    _VSMReceiverDebugOutput[pixel] = float4(color, 1);
}
