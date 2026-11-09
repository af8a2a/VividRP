#ifndef VIVID_SDF_SHORT_RANGE_AOCOMMON_INCLUDED
#define VIVID_SDF_SHORT_RANGE_AOCOMMON_INCLUDED

float3 LoadContactDelta(int2 pixel, float3 origin)
{
    if (any(pixel < 0) || any(pixel >= int2(_SdfDiffuseScreenSize.xy)))
        return 0.0;
    float depth = _SdfDiffuseDepth.Load(int3(pixel, 0));
    if (depth == UNITY_RAW_FAR_CLIP_VALUE)
        return 0.0;
    float2 uv = (float2(pixel) + 0.5) * _SdfDiffuseScreenSize.zw;
    return ComputeWorldSpacePosition(uv, depth, _SdfDiffuseInverseVP) - origin;
}

float3 ContactTangent(int2 pixel, int2 axis, float3 position)
{
    float3 ahead = LoadContactDelta(pixel + axis, position);
    float3 behind = -LoadContactDelta(pixel - axis, position);
    float a = dot(ahead, ahead);
    float b = dot(behind, behind);
    return (b == 0.0 || (a > 0.0 && a < b)) ? ahead : behind;
}

float4 EvaluateNearDiffuseVisibility(uint2 receiverPixel, float3 position, float3 shadingNormal, bool directional)
{
    float radius = _SdfNearOcclusion.x;
    int2 pixel = int2(receiverPixel);
    float3 dx = ContactTangent(pixel, int2(1, 0), position);
    float3 dy = ContactTangent(pixel, int2(0, 1), position);
    float3 geometricNormal = SafeNormalize(cross(dx, dy));
    float agreement = dot(geometricNormal, shadingNormal);
    float3 normal = shadingNormal;
    if (abs(agreement) > 0.25 && max(dot(dx, dx), dot(dy, dy)) < radius * radius * 0.25)
        normal = geometricNormal * (agreement < 0.0 ? -1.0 : 1.0);

    float eyeW = mul(_SdfDiffuseVP, float4(position, 1.0)).w;
    float pixelRadius = clamp(0.5 * radius * _SdfDiffuseScreenSize.y * length(_SdfDiffuseVP[1].xyz)
        / max(eyeW, 1e-3), 1.0, 32.0);
    const float2 directions[8] = { float2(1, 0), float2(-1, 0), float2(0, 1), float2(0, -1),
        float2(0.7071, 0.7071), float2(-0.7071, 0.7071), float2(0.7071, -0.7071), float2(-0.7071, -0.7071) };
    float occlusion = 0.0;
    float3 blocked = 0.0;
    [unroll]
    for (uint ray = 0u; ray < 8u; ray++)
    {
        float horizon = 0.0;
        float3 blocker = 0.0;
        [unroll]
        for (uint tap = 1u; tap <= 2u; tap++)
        {
            float2 offset = directions[ray] * pixelRadius * (tap * 0.45);
            float3 delta = LoadContactDelta(pixel + int2(round(offset)), position);
            float distance = length(delta);
            float elevation = (dot(delta, normal) - 0.015) / max(distance, 1e-5);
            float occluder = saturate(elevation) * saturate(1.0 - distance / radius);
            if (directional && occluder > horizon)
                blocker = delta / max(distance, 1e-5);
            horizon = max(horizon, occluder);
        }
        occlusion += horizon;
        blocked += horizon * blocker;
    }
    float visibility = 1.0 - _SdfNearOcclusion.y * saturate(0.25 * occlusion);
    float3 towardOpen = -0.25 * _SdfNearOcclusion.y * (blocked - shadingNormal * dot(blocked, shadingNormal));
    // A bounded tangent term keeps directional visibility in [0, 1].
    // Its cosine-weighted hemisphere mean is zero about the material normal.
    towardOpen *= min(1.0, min(visibility, 1.0 - visibility) / max(length(towardOpen), 1e-5));
    return float4(towardOpen, visibility);
}

float NearDiffuseDirectionVisibility(float4 visibility, float3 direction)
{
    return saturate(visibility.w + dot(visibility.xyz, direction));
}

#endif
