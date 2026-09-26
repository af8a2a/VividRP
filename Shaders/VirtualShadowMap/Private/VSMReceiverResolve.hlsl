float4 BuildVSMReceiverBias(VividVSMProjection projection, float3 normalWS)
{
    float3 rowX = projection.worldToShadow[0].xyz;
    float3 rowY = projection.worldToShadow[1].xyz;
    float3 rowZ = projection.worldToShadow[2].xyz;
    float depthScale = length(rowZ);
    float3 axisZ = rowZ / max(depthScale, 1e-8);
    float nZ = dot(normalWS, axisZ);
    float texelDepth = projection.parameters.x * depthScale;
    float2 nXY = float2(dot(normalWS, rowX / max(length(rowX), 1e-8)),
        dot(normalWS, rowY / max(length(rowY), 1e-8)));
    float denominator = nZ < 0.0 ? min(nZ, -1e-3) : max(nZ, 1e-3);
    // Each tap already follows the receiver plane. Bias only the slope left
    // uncorrected by the grazing clamp; adding the full slope again detaches
    // contact shadows by several texels, increasingly at coarser levels.
    float2 planeSlope = -nXY / denominator;
    float2 correctedSlope = clamp(planeSlope, -4.0, 4.0);
    float2 gradient = correctedSlope * texelDepth;
    float2 residualSlope = abs(planeSlope - correctedSlope);
    float slope = min(max(residualSlope.x, residualSlope.y) * _VSMReceiverParameters.z,
        4.0) * texelDepth;
    float constant = max(_VSMReceiverParameters.y * texelDepth,
        _VSMReceiverParameters.y > 0.0 ? 1.0 / 8388608.0 : 0.0);
    float comparisonBias = constant + slope;
#if !UNITY_REVERSED_Z
    comparisonBias = -comparisonBias;
#endif
    float normalOffset = projection.parameters.x * projection.parameters.y
        * sqrt(saturate(1.0 - nZ * nZ));
    return float4(gradient, comparisonBias, normalOffset);
}

// Prepared for one candidate level. Density selection hands its accepted
// projection to resolve so normal bias and world-to-shadow are not repeated.
struct VSMReceiverProjection
{
    VividVSMProjection projection;
    float4 bias;
    float3 coord;
};

VSMReceiverProjection PrepareVSMReceiverProjection(float3 positionWS, float3 normal, int index)
{
    VSMReceiverProjection result;
    result.projection = _VSMProjections[index];
    result.bias = BuildVSMReceiverBias(result.projection, normal);
    float3 biasedPosition = positionWS + normal * result.bias.w;
    result.coord = mul(result.projection.worldToShadow, float4(biasedPosition, 1.0)).xyz;
    return result;
}

float VSMAreaWeight(float offsetFromCenter)
{
    // Overlap of a unit texel cell with a receiver-centered, two-texel-wide box.
    // The three axis weights sum to two at every sub-texel phase.
    return saturate(1.5 - abs(offsetFromCenter));
}

uint VSMStochasticHash(uint value)
{
    value ^= value >> 16;
    value *= 0x7feb352du;
    value ^= value >> 15;
    value *= 0x846ca68bu;
    return value ^ (value >> 16);
}

uint GetVSMStochasticSeed(uint2 pixel, uint frameIndex)
{
    return VSMStochasticHash(pixel.x ^ VSMStochasticHash(pixel.y + 0x9e3779b9u)
        ^ VSMStochasticHash(frameIndex + 0x68bc21ebu));
}

float2 VSMStochasticDiskSample(uint seed, uint sampleIndex)
{
    // Three equal-area radial bands by three angular sectors: nine strata.
    // Hash jitter is deterministic for a pixel/frame, not a blue-noise sequence.
    uint random = VSMStochasticHash(seed + sampleIndex * 0x9e3779b9u);
    float2 jitter = float2(random >> 8, VSMStochasticHash(random + 0x68bc21ebu) >> 8)
        * (1.0 / 16777216.0);
    float radius = sqrt(((float)(sampleIndex / 3u) + jitter.x) * (1.0 / 3.0));
    float angle = ((float)(sampleIndex % 3u) + jitter.y) * (kPCSSTwoPi / 3.0);
    float sine, cosine;
    sincos(angle, sine, cosine);
    return radius * float2(cosine, sine);
}

int2 GetVSMStochasticTexelOffset(float2 centerOffset, float2 sampleOffset)
{
    // Keep the unit-radius support when a sum just below 2 rounds up to 2.
    // This bounds the sample offset, never clamps a virtual map or page edge.
    return clamp(int2(floor(centerOffset + 0.5 + sampleOffset)), -1, 1);
}

bool HasVSMStochasticFootprint(float2 shadowUV, int index)
{
    int2 minTexel, maxTexel;
    if (!VividVSMTryOffsetVirtualTexel(shadowUV, int2(-1, -1),
            (uint)_VSMPrototypeVirtualResolution, minTexel)
        || !VividVSMTryOffsetVirtualTexel(shadowUV, int2(1, 1),
            (uint)_VSMPrototypeVirtualResolution, maxTexel))
    {
#if defined(VIVID_VSM_RECEIVER_DEBUG)
        g_VSMDebugMissing |= 8u;
#endif
        return false;
    }

    // Validate the union of all phases before sampling. Radius <= one texel
    // stays inside the existing feedback halo, spanning at most four pages.
    int2 minPage = minTexel / _VSMPrototypePageSize;
    int2 maxPage = maxTexel / _VSMPrototypePageSize;
    for (int y = minPage.y; y <= maxPage.y; y++)
    {
        for (int x = minPage.x; x <= maxPage.x; x++)
        {
            int2 physicalTexel;
            if (!TryResolveVSMPhysicalTexel(int2(x, y) * _VSMPrototypePageSize,
                    index, physicalTexel))
                return false;
        }
    }
    return true;
}

bool TryFilterVSMProjection(float3 coord, float4 bias, int index, uint2 pixel, out float shadow)
{
    shadow = 1.0;
    float2 centerOffset = frac(coord.xy * (float)_VSMPrototypeVirtualResolution) - 0.5;
    if (_VSMReceiverParameters.x < 0.5)
    {
        // A point lookup still samples the texel center, not the receiver UV.
        // Correct that sub-texel displacement on sloped planes in Hard mode too.
        float depth = saturate(coord.z + bias.z - dot(bias.xy, centerOffset));
        return TrySampleVSMVirtualTap(coord.xy, int2(0, 0), depth, index, shadow);
    }

    if (_VSMReceiverParameters.w >= 0.5)
    {
        if (!HasVSMStochasticFootprint(coord.xy, index))
            return false;
        uint seed = GetVSMStochasticSeed(pixel, (uint)_CSMFrameIndex);
        float stochasticSum = 0.0;
        [unroll]
        for (uint sampleIndex = 0u; sampleIndex < 9u; sampleIndex++)
        {
            float2 sampleOffset = VSMStochasticDiskSample(seed, sampleIndex);
            int2 texelOffset = GetVSMStochasticTexelOffset(centerOffset, sampleOffset);
            // Depth follows the actual integer texel center relative to the
            // ORIGINAL receiver, not the random continuous sample position.
            float2 tapOffset = float2(texelOffset) - centerOffset;
            float depth = saturate(coord.z + bias.z + dot(bias.xy, tapOffset));
            float tapShadow;
            if (!TrySampleVSMVirtualTap(coord.xy, texelOffset, depth, index, tapShadow))
                return false;
            stochasticSum += tapShadow;
        }
        shadow = stochasticSum * (1.0 / 9.0);
        return true;
    }

    float sum = 0.0;
    float weightSum = 0.0;
    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            float2 tapOffset = float2(x, y) - centerOffset;
            float weight = VSMAreaWeight(tapOffset.x) * VSMAreaWeight(tapOffset.y);
            if (weight <= 0.0)
                continue;
            // Compare at the tap's receiver-plane depth, not at the center's
            // depth. The full positive-weight footprint must be available;
            // partial kernels are not brightened or renormalized around holes.
            float tapShadow;
            float depth = saturate(coord.z + bias.z + dot(bias.xy, tapOffset));
            if (!TrySampleVSMVirtualTap(coord.xy, int2(x, y), depth, index, tapShadow))
                return false;
            sum += weight * tapShadow;
            weightSum += weight;
        }
    }
    shadow = sum / weightSum;
    return true;
}

bool TryFilterVSMProjection(float3 coord, float4 bias, int index, out float shadow)
{
    return TryFilterVSMProjection(coord, bias, index, uint2(0, 0), shadow);
}

float VSMTransitionWeight(float edge, float border)
{
    if (border <= 0.0)
        return 0.0;
    float t = saturate((edge - (0.5 - border)) / border);
    return t * t * (3.0 - 2.0 * t);
}

#include "VSMSMRT.hlsl"

// Reproject the original world receiver (including THIS level's normal bias)
// at every fallback level. Reusing a fine UV/depth or shifting a physical texel
// would sample unrelated data after clipmap scrolling.
bool TryEvaluateVSMProjection(VSMReceiverProjection prepared, int index,
    bool sampleDepth, uint2 pixel, bool smrt,
    inout VSMSMRTReceiverSamples samples, out float shadow)
{
    shadow = 1.0;
    if (!sampleDepth) return false;
    VSM_COST_ADD(1, smrt ? 1u : 0u);
    VSM_COST_ADD(29, smrt ? 0u : 1u);
#if defined(VIVID_VSM_RECEIVER_DEBUG)
    g_VSMDebugWork.z++;
#endif
    VividVSMProjection projection = prepared.projection;
    float4 bias = prepared.bias;
    float3 coord = prepared.coord;
    if (!all(coord >= 0.0) || !all(coord <= 1.0))
    {
#if defined(VIVID_VSM_RECEIVER_DEBUG)
        g_VSMDebugMissing |= 8u;
#endif
        return false;
    }
    if (_VSMPrototypeEnabled == 0) return false;
    // HLSL conditional expressions may evaluate both operands and overwrite the
    // shared out parameter. Keep the two filters behind an actual branch.
    [branch]
    if (smrt) return TryFilterVSMSMRT(coord, bias, index, pixel,
        PrepareVSMSMRTProjection(projection), samples, shadow);
    return TryFilterVSMProjection(coord, bias, index, pixel, shadow);
}

#include "VSMReceiverQuality.hlsl"
#include "VSMPageMarking.hlsl"

float ResolveVSMReceiverMode(float3 positionWS, float3 normalWS, uint2 pixel, bool smrt, out bool unavailable)
{
    unavailable = false;
    bool densityPolicy = _VSMReceiverQuality.x > 0;
    float densityBlend = 0;
    int firstLevel = 0;
    float3 normal = normalWS * rsqrt(max(dot(normalWS, normalWS), 1e-8));
    VSMReceiverProjection selected = (VSMReceiverProjection)0;
    VSMSMRTReceiverSamples samples = (VSMSMRTReceiverSamples)0;
    [branch]
    if (densityPolicy)
        firstLevel = SelectVSMDensityLevelPrepared(positionWS, normal, smrt, densityBlend, selected);
    if (firstLevel < 0) { unavailable = true; return 1.0; }
    for (int index = firstLevel; index < _VSMProjectionCount; index++)
    {
        VividVSMProjection projection = _VSMProjections[index];
        float2 relative = mul(projection.worldToShadow,
            float4(positionWS - projection.selectionSphere.xyz, 0.0)).xy * 2;
        float edge = max(abs(relative.x), abs(relative.y));
        if (!densityPolicy && edge >= 0.5)
            continue;
        float maxDistance = projection.parameters.w;
        float distance = length(positionWS - projection.selectionSphere.xyz);
        if (distance >= maxDistance)
            return 1.0;
        float border = projection.parameters.z;
        float blend = densityPolicy ? densityBlend : VSMTransitionWeight(edge, border);
#if defined(VIVID_VSM_RECEIVER_DEBUG)
        g_VSMDebugLevels.x = index;
        g_VSMDebugBlend = blend;
#endif
        float shadow = 1.0;
        float transition = 1.0;
        int sampledLevel = -1;
        bool hasTransition = false;
        for (int level = index; level < _VSMProjectionCount; level++)
        {
            bool needSample = sampledLevel < 0 || (sampledLevel == index && blend > 0.0 && !hasTransition);
            float sampleShadow;
            VSMReceiverProjection prepared = selected;
            // Only prepare projections that still need a depth estimate.
            if (needSample)
            {
                if (!densityPolicy || level != firstLevel)
                    prepared = PrepareVSMReceiverProjection(positionWS, normal, level);
            }
            bool sampled = TryEvaluateVSMProjection(prepared, level, needSample, pixel,
                smrt, samples, sampleShadow);
            VSM_COST_ADD(2, smrt && needSample && sampledLevel < 0 && level > index ? 1u : 0u);
            VSM_COST_ADD(3, smrt && needSample && sampledLevel >= 0 ? 1u : 0u);
            if (!sampled) continue;
            if (sampledLevel < 0)
            {
                sampledLevel = level;
                shadow = sampleShadow;
#if defined(VIVID_VSM_RECEIVER_DEBUG)
                g_VSMDebugLevels.y = level;
#endif
            }
            else
            {
                transition = sampleShadow;
                hasTransition = true;
                VSM_COST_ADD(25, smrt ? 1u : 0u);
#if defined(VIVID_VSM_RECEIVER_DEBUG)
                g_VSMDebugLevels.z = level;
#endif
            }
        }
        // Missing transition coverage must not brighten a valid primary sample.
        // If the primary already fell back, it must not be blended a second time.
        if (sampledLevel == index && hasTransition)
            shadow = lerp(shadow, transition, blend);
        unavailable = sampledLevel < 0;
        float fade = saturate((maxDistance - distance) / max(maxDistance * 0.2, 1e-5));
        return lerp(1.0, shadow, fade);
    }
    // No complete level covers the receiver: explicit terminal lit policy.
    unavailable = true;
    return 1.0;
}

float ResolveVSMReceiver(float3 positionWS, float3 normalWS, uint2 pixel)
{
    bool smrt = UseVSMSMRT();
    bool unavailable;
    float shadow = ResolveVSMReceiverMode(positionWS, normalWS, pixel, smrt, unavailable);
    if (smrt && unavailable)
    {
        VSM_COST_ADD(22, 1u);
#if defined(VIVID_VSM_RECEIVER_DEBUG)
        g_VSMDebugSMRT.w++;
#endif
        // Retry the complete existing PCF hierarchy only after all soft estimates
        // failed. Never clamp a valid penumbra against a hard central reference.
        shadow = ResolveVSMReceiverMode(positionWS, normalWS, pixel, false, unavailable);
    }
    VSM_COST_ADD(24, unavailable ? 1u : 0u);
    return shadow;
}

float ResolveVSMReceiver(float3 positionWS, float3 normalWS)
{
    return ResolveVSMReceiver(positionWS, normalWS, uint2(0, 0));
}

#if defined(VIVID_VSM_RECEIVER_DEBUG)
#include "VSMReceiverDebug.hlsl"
#endif

#if defined(VIVID_VSM_SMRT_COST)
RWStructuredBuffer<uint4> _VSMSMRTCostOutput;
Texture2D<float> _VSMSMRTCostReference;

// Replay the production receiver function using the same inputs and raw shadow
// BEFORE denoising. No scene/history/page writes and no diagnostic wave votes.
[numthreads(8, 8, 1)]
void VSMReceiverCost(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)_CSMOutputWidth || id.y >= (uint)_CSMOutputHeight) return;
    [unroll] for (uint c = 0u; c < 8u; c++) g_VSMSMRTCost[c] = 0u;
    float depth = _DepthTexture.Load(int3(id.xy, 0));
    float shadow = 1.0;
    if (!IsSkyPixel(depth))
    {
        VSM_COST_ADD(0, 1u);
        float3 position = ReconstructWorldPosition(id.xy, depth);
        float3 normal = DecodeVividNormalOct(_GBuffer1.Load(int3(id.xy, 0)).xy);
        normal = ReconstructVSMReceiverNormal(id.xy, depth, position, normal);
        shadow = ResolveVSMReceiver(position, normal, id.xy);
    }
    float reference = _VSMSMRTCostReference.Load(int3(id.xy, 0));
    // The production raw visibility target is R16_SFloat. Compare at that
    // storage precision, not an unquantized float against its half-float store.
    shadow = f16tof32(f32tof16(shadow));
    VSM_COST_ADD(30, shadow != reference ? 1u : 0u);
    VSM_COST_ADD(31, !isfinite(shadow) || !isfinite(reference) || abs(shadow - reference) > 1e-6 ? 1u : 0u);
    uint pixel = id.y * (uint)_CSMOutputWidth + id.x;
    [unroll] for (uint c = 0u; c < 8u; c++) _VSMSMRTCostOutput[pixel * 8u + c] = g_VSMSMRTCost[c];
}
#endif
