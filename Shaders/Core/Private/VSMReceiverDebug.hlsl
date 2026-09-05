// Included only by the opt-in VSMReceiverDebug kernel. No feedback or pool writes.
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
    g_VSMDebugLevels = -1;
    g_VSMDebugBlend = 0;
    g_VSMDebugWork = 0;
    g_VSMDebugMissing = 0;
    g_VSMDebugQuality = -1;
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
    float shadow = ResolveVSMReceiver(position, normal);
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
    _VSMReceiverDebugData[pixel] = data;
    _VSMReceiverDebugOutput[pixel] = float4(color, 1);
}
