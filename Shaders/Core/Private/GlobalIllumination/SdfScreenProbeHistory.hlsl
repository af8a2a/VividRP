#ifndef VIVID_SDF_SCREEN_PROBE_HISTORY_INCLUDED
#define VIVID_SDF_SCREEN_PROBE_HISTORY_INCLUDED

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

    uint2 previousTile = uint2(uv * _SdfDiffuseScreenSize.xy) / ProbeSpacing;
    float4 geometry = _SdfDiffusePreviousGeometry.Load(int3(previousTile, 0));
    if (_SdfDiffuseAdaptiveProbes != 0u || _SdfDiffuseSpatialFilter != 0u)
    {
        float eyeDepth = dot(float4(position, 1.0), _SdfProbeCameraForward);
        float radius = max(0.08, 0.035 * eyeDepth);
        float weight = geometry.w < 0.0 ? 0.0 : ProbeGeometryWeight(geometry.xyz - position, normal,
            UnpackProbeNormal(geometry.w), eyeDepth) * saturate(1.0 - distance(geometry.xyz, position) / radius);
        if (_SdfDiffuseAdaptiveProbes != 0u)
        {
            uint2 adaptive = previousTile + uint2(0u, ProbeGridSize(uint2(_SdfDiffuseScreenSize.xy)).y);
            float4 other = _SdfDiffusePreviousGeometry.Load(int3(adaptive, 0));
            float otherWeight = other.w < 0.0 ? 0.0 : ProbeGeometryWeight(other.xyz - position, normal,
                UnpackProbeNormal(other.w), eyeDepth) * saturate(1.0 - distance(other.xyz, position) / radius);
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
