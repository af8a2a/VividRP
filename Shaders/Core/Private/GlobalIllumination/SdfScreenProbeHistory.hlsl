#ifndef VIVID_SDF_SCREEN_PROBE_HISTORY_INCLUDED
#define VIVID_SDF_SCREEN_PROBE_HISTORY_INCLUDED

float ProbeReprojectionWeight(float4 geometry, float3 position, float3 normal, float eyeDepth, float radius)
{
    if (geometry.w < 0.0)
        return 0.0;
    float3 delta = geometry.xyz - position;
    return ProbeGeometryWeight(delta, normal, UnpackProbeNormal(geometry.w), eyeDepth)
        * saturate(1.0 - length(delta) / radius);
}

float4 AccumulateReprojectedProbe(float4 current, float3 position, float3 normal, float2 uv, inout float3x3 moments)
{
    uint2 grid = ProbeGridSize(uint2(_SdfDiffuseScreenSize.xy));
    float2 gridPosition = uv * _SdfDiffuseScreenSize.xy / ProbeSpacing - 0.5;
    int2 lower = int2(floor(gridPosition));
    float2 fraction = frac(gridPosition);
    float eyeDepth = dot(float4(position, 1.0), _SdfProbeCameraForward);
    float radius = clamp(0.04 * eyeDepth, 0.12, 0.40);
    float total = 0.0;
    float3 sum = 0.0;
    float3x3 sumMoments = (float3x3)0.0;
    [unroll]
    for (uint i = 0u; i < 4u; ++i)
    {
        int2 corner = int2(i & 1u, i >> 1u);
        int2 candidate = lower + corner;
        if (any(candidate < 0) || any(candidate >= int2(grid)))
            continue;

        uint2 tile = uint2(candidate);
        float weight = ProbeReprojectionWeight(_SdfDiffusePreviousGeometry.Load(int3(tile, 0)),
            position, normal, eyeDepth, radius);
        if (_SdfDiffuseAdaptiveProbes != 0u)
        {
            uint2 adaptive = tile + uint2(0u, grid.y);
            float alternate = ProbeReprojectionWeight(_SdfDiffusePreviousGeometry.Load(int3(adaptive, 0)),
                position, normal, eyeDepth, radius);
            if (alternate > weight)
            {
                tile = adaptive;
                weight = alternate;
            }
        }
        if (weight <= 0.0)
            continue;

        float4 history = _SdfDiffusePreviousProbes.Load(int3(tile, 0));
        float2 bilinear = lerp(1.0 - fraction, fraction, float2(corner));
        weight *= bilinear.x * bilinear.y * history.a;
        total += weight;
        sum += history.rgb * weight;
        if (_SdfDiffuseDirectionalProbes != 0u)
        {
            uint2 stride = ProbePlaneStride();
            sumMoments[0] += _SdfDiffusePreviousProbes.Load(int3(tile + stride, 0)).rgb * weight;
            sumMoments[1] += _SdfDiffusePreviousProbes.Load(int3(tile + 2u * stride, 0)).rgb * weight;
            sumMoments[2] += _SdfDiffusePreviousProbes.Load(int3(tile + 3u * stride, 0)).rgb * weight;
        }
    }
    if (total > 1e-5)
    {
        float blend = _SdfProbeHistoryBlend * saturate(total);
        current.rgb = lerp(current.rgb, sum / total, blend);
        moments = lerp(moments, sumMoments / total, blend);
    }
    return current;
}

float4 AccumulateProbe(float4 current, float3 position, float3 normal, inout float3x3 moments)
{
    if (_SdfDiffuseHasHistory == 0u || current.a == 0.0)
        return current;

    float4 clip = mul(_SdfDiffusePreviousVP, float4(position, 1.0));
    if (clip.w <= 0.0)
        return current;
    float2 uv = ScreenUv(clip);
    if (any(uv < 0.0) || any(uv >= 1.0))
        return current;
    if (_SdfProbeSpatialKernel != 0u)
        return AccumulateReprojectedProbe(current, position, normal, uv, moments);

    uint2 previousTile = uint2(uv * _SdfDiffuseScreenSize.xy) / ProbeSpacing;
    float4 geometry = _SdfDiffusePreviousGeometry.Load(int3(previousTile, 0));
    if (_SdfDiffuseAdaptiveProbes != 0u || _SdfDiffuseSpatialFilter != 0u)
    {
        float eyeDepth = dot(float4(position, 1.0), _SdfProbeCameraForward);
        float radius = max(0.08, 0.035 * eyeDepth);
        float weight = ProbeReprojectionWeight(geometry, position, normal, eyeDepth, radius);
        if (_SdfDiffuseAdaptiveProbes != 0u)
        {
            uint2 adaptive = previousTile + uint2(0u, ProbeGridSize(uint2(_SdfDiffuseScreenSize.xy)).y);
            float4 other = _SdfDiffusePreviousGeometry.Load(int3(adaptive, 0));
            float otherWeight = ProbeReprojectionWeight(other, position, normal, eyeDepth, radius);
            if (otherWeight > weight)
            {
                previousTile = adaptive;
                weight = otherWeight;
            }
        }
        if (weight < 0.16)
            return current;
    }
    else if (geometry.w < 0.0 || distance(geometry.xyz, position) > max(0.1, 0.04 * clip.w)
        || dot(UnpackProbeNormal(geometry.w), normal) < 0.9)
        return current;

    float4 history = _SdfDiffusePreviousProbes.Load(int3(previousTile, 0));
    if (history.a > 0.0)
    {
        float weight = 0.85 * history.a;
        current.rgb = lerp(current.rgb, history.rgb, weight);
        if (_SdfDiffuseDirectionalProbes != 0u)
        {
            uint2 stride = ProbePlaneStride();
            moments[0] = lerp(moments[0], _SdfDiffusePreviousProbes.Load(int3(previousTile + stride, 0)).rgb, weight);
            moments[1] = lerp(moments[1], _SdfDiffusePreviousProbes.Load(int3(previousTile + 2u * stride, 0)).rgb, weight);
            moments[2] = lerp(moments[2], _SdfDiffusePreviousProbes.Load(int3(previousTile + 3u * stride, 0)).rgb, weight);
        }
    }
    return current;
}

#endif
