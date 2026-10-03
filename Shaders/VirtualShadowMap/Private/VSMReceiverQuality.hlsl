// Receiver policy only. Stable power-of-two projections and page identities do
// not depend on these uniforms. Global pressure bias applies to requests only.

// Project virtual texel axes onto the geometric receiver plane, then the screen.
// These are local axis lengths, not a singular-value bound in every direction.
float2 VSMReceiverTexelFootprint(float3 positionWS, float3 normalWS, int level)
{
    if (level < 0) return -1.0;
    VividVSMProjection p = _VSMProjections[level];
    float3 axisX = normalize(p.worldToShadow[0].xyz);
    float3 axisY = normalize(p.worldToShadow[1].xyz);
    float3 axisZ = normalize(p.worldToShadow[2].xyz);
    float nZ = dot(normalWS, axisZ);
    if (abs(nZ) < 1e-4) return -1.0;
    float3 dx = (axisX - axisZ * (dot(normalWS, axisX) / nZ)) * p.parameters.x;
    float3 dy = (axisY - axisZ * (dot(normalWS, axisY) / nZ)) * p.parameters.x;
    float4 center = mul(_VSMReceiverViewProjection, float4(positionWS, 1));
    float4 deltaX = mul(_VSMReceiverViewProjection, float4(dx, 0));
    float4 deltaY = mul(_VSMReceiverViewProjection, float4(dy, 0));
    if (center.w <= 1e-6) return -1.0;
    float2 scale = 0.5 * float2(_CSMOutputWidth, _CSMOutputHeight) / (center.w * center.w);
    return float2(length((deltaX.xy * center.w - center.xy * deltaX.w) * scale),
                  length((deltaY.xy * center.w - center.xy * deltaY.w) * scale));
}

// UE uses floor(log2(distance) + ResolutionLodBias), with no upper clamp.
// Our stored radius is projection half-width: UE HalfLevelDim = 2^(Level+2).
float VSMBaseAbsoluteClipmapLevel()
{
    return round(log2(max(-_VSMProjections[0].selectionSphere.w * 100.0, 1e-20))) - 2.0;
}

int VSMClipmapLevelFromDistance(float distance, float firstAbsoluteLevel, float bias, int count)
{
    int index = max(0, (int)floor(log2(max(distance, 1e-20)) + bias) - (int)firstAbsoluteLevel);
    return index < count ? index : -1;
}

int SelectVSMClipmapLevel(float3 positionWS, bool marking)
{
    if (_VSMProjectionCount <= 0) return -1;
    // UE absolute levels are centimetre exponents; position buffers use metres.
    float distance = length(positionWS - _VSMProjections[0].selectionSphere.xyz) * 100.0;
    float bias = 0.0;
    float first = VSMBaseAbsoluteClipmapLevel();
#if defined(VIVID_VSM_PAGE_PRESSURE)
    // UE GetBiasedClipmapLevel adds GlobalResolutionLodBias to demand only.
    // Sampling starts at its desired level and uses resident parent mappings.
    if (marking && _VSMReceiverQuality.x > 1.5) bias += asfloat(_VSMPagePressure[0].x);
#endif
    // UE throttling first chooses a VSM without its resolution bias. Requests
    // include memory pressure at this stage; sampling intentionally does not.
    if (_VSMReceiverQuality.z > 0.5)
    {
        int unbiased = VSMClipmapLevelFromDistance(distance, first, bias, _VSMProjectionCount);
        bias += _VSMProjections[unbiased < 0 ? 0 : unbiased].parameters.z;
    }
    bias += _VSMReceiverQuality.y;
    int level = VSMClipmapLevelFromDistance(distance, first, bias, _VSMProjectionCount);
#if defined(VIVID_VSM_RECEIVER_DEBUG)
    g_VSMDebugQuality = float4(log2(max(distance, 1e-20)) + bias - first, first, level, bias);
#endif
    return level;
}

// Kept as the sampling-diagnostic entry point; fractional/coverage blend is gone.
int SelectVSMDensityLevelPrepared(float3 positionWS, float3 normal, bool smrt, out float blend,
    out VSMReceiverProjection prepared)
{
    blend = 0;
    prepared = (VSMReceiverProjection)0;
    int level = SelectVSMClipmapLevel(positionWS, false);
    if (level >= 0) prepared = PrepareVSMReceiverProjection(positionWS, normal, level);
    return level;
}

int SelectVSMDensityLevel(float3 positionWS, float3 normalWS, bool smrt, out float blend)
{
    blend = 0;
    return SelectVSMClipmapLevel(positionWS, false);
}

int SelectVSMDensityLevel(float3 positionWS, float3 normalWS, out float blend)
{
    return SelectVSMDensityLevel(positionWS, normalWS, UseVSMSMRT(), blend);
}
