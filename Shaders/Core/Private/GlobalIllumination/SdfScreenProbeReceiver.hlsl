#ifndef VIVID_SDF_SCREEN_PROBE_RECEIVER_INCLUDED
#define VIVID_SDF_SCREEN_PROBE_RECEIVER_INCLUDED

#if defined(VIVID_SDF_RECEIVER)
Texture2D<uint2> _SdfReceiverVisibility;
StructuredBuffer<uint4> _SdfReceiverEntities; // Entity ID in XY, SDF instance index in Z.
uint _SdfReceiverCount;
uint _SdfCorrectReceiverSelfHits;

bool LoadReceiverTangent(int2 pixel, int2 axis, uint2 visibility, float3 position, out float3 tangent)
{
    tangent = 0.0;
    [unroll]
    for (int side = -1; side <= 1; side += 2)
    {
        int2 neighbor = pixel + side * axis;
        if (any(neighbor < 0) || any(neighbor >= int2(_SdfDiffuseScreenSize.xy))
            || any(_SdfReceiverVisibility.Load(int3(neighbor, 0)) != visibility))
            continue;
        float depth = _SdfDiffuseDepth.Load(int3(neighbor, 0));
        if (depth == UNITY_RAW_FAR_CLIP_VALUE)
            continue;
        float2 uv = (float2(neighbor) + 0.5) * _SdfDiffuseScreenSize.zw;
        float3 neighborPosition = ComputeWorldSpacePosition(uv, depth, _SdfDiffuseInverseVP);
        tangent = neighborPosition - position;
        return true;
    }
    return false;
}

bool LoadContinuousReceiverTangent(int2 pixel, int2 axis, uint instance, float3 position, float3 normal,
    out float3 tangent)
{
    tangent = 0.0;
    float3 before, after, beforeNormal, afterNormal;
    int2 a = pixel - axis;
    int2 b = pixel + axis;
    if (any(a < 0) || any(b >= int2(_SdfDiffuseScreenSize.xy)))
        return false;
    if (_SdfReceiverVisibility.Load(int3(a, 0)).x != instance
        || _SdfReceiverVisibility.Load(int3(b, 0)).x != instance
        || !LoadReceiver(uint2(a), before, beforeNormal)
        || !LoadReceiver(uint2(b), after, afterNormal))
        return false;
    if (min(dot(beforeNormal, normal), dot(afterNormal, normal)) < 0.98)
        return false;

    float3 left = position - before;
    float3 right = after - position;
    float leftLength = length(left);
    float rightLength = length(right);
    // A shared instance can contain folds or disconnected surfaces. Require depth continuity on both sides.
    if (min(leftLength, rightLength) <= 1e-6 || max(leftLength, rightLength) > 1.35 * min(leftLength, rightLength)
        || dot(left, right) < 0.999 * leftLength * rightLength)
        return false;
    tangent = after - before;
    return true;
}

VividMeshSdfReceiver LoadSdfReceiver(uint2 pixel, float3 position, float3 shadingNormal)
{
    VividMeshSdfReceiver receiver = (VividMeshSdfReceiver)0;
    uint2 visibility = _SdfReceiverVisibility.Load(int3(pixel, 0));
    uint index = visibility.x - VIVID_VISIBILITY_BUFFER_INSTANCE_ID_BIAS;
    if (index >= _SdfReceiverCount)
        return receiver;

    float3 dx, dy;
    bool exactX = LoadReceiverTangent(int2(pixel), int2(1, 0), visibility, position, dx);
    bool exactY = LoadReceiverTangent(int2(pixel), int2(0, 1), visibility, position, dy);
    bool reconstructed = !exactX || !exactY;
    if (reconstructed)
    {
        // Reconstruct from a continuous patch on both axes when a triangle-local derivative is missing.
        if (!LoadContinuousReceiverTangent(int2(pixel), int2(1, 0), visibility.x, position, shadingNormal, dx))
            return receiver;
        if (!LoadContinuousReceiverTangent(int2(pixel), int2(0, 1), visibility.x, position, shadingNormal, dy))
            return receiver;
    }
    float3 normal = SafeNormalize(cross(dx, dy));
    if (dot(normal, normal) < 0.5)
        return receiver;
    float3 towardsCamera = unity_OrthoParams.w != 0.0 ? -_SdfProbeCameraForward.xyz : _WorldSpaceCameraPos - position;
    receiver.normalWS = dot(normal, towardsCamera) < 0.0 ? -normal : normal;
    if (reconstructed && dot(receiver.normalWS, shadingNormal) < 0.94)
        return (VividMeshSdfReceiver)0;
    receiver.entityId = _SdfReceiverEntities[index].xy;
    receiver.positionWS = position;
    return receiver;
}
#endif

#endif
