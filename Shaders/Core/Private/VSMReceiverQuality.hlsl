// Receiver policy only. Stable power-of-two projections and page identities do
// not depend on these uniforms. Resolve, feedback and debug use this same choice.
float4 _VSMReceiverQuality; // enabled, target pixels per texel including LOD bias, reserved
float4x4 _VSMReceiverViewProjection;

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

int SelectVSMDensityLevel(float3 positionWS, float3 normalWS, out float blend)
{
    blend = 0;
    if (_VSMProjectionCount <= 0) return -1;
    VividVSMProjection first = _VSMProjections[0];
    if (length(positionWS - first.selectionSphere.xyz) >= first.parameters.w) return -1;

    float3 normal = normalWS * rsqrt(max(dot(normalWS, normalWS), 1e-8));
    // All directional levels share axes and double texel size. Evaluate the
    // camera/receiver differential once, not once per projection.
    float2 footprint = VSMReceiverTexelFootprint(positionWS, normal, 0);
    float worst = max(footprint.x, footprint.y);
    float target = max(_VSMReceiverQuality.y, 1e-4);
    // Degenerate planes conservatively request the finest covered level.
    float desiredLOD = worst > 0 ? log2(target / worst) : 0;
    float lod = clamp(desiredLOD, 0, _VSMProjectionCount - 1);
    int desired = (int)floor(lod);
#if defined(VIVID_VSM_RECEIVER_DEBUG)
    g_VSMDebugQuality = float4(desiredLOD, -1, -1, -1);
#endif
    float guard = _VSMReceiverParameters.x >= 0.5 ? 1.5 / _VSMPrototypeVirtualResolution : 0;
    for (int level = 0; level < _VSMProjectionCount; level++)
    {
        VividVSMProjection p = _VSMProjections[level];
        float4 bias = BuildVSMReceiverBias(p, normal);
        float3 coord = mul(p.worldToShadow, float4(positionWS + normal * bias.w, 1)).xyz;
        // Coverage, including normal offset and PCF map-edge guard, is a hard
        // constraint independent of requested density and current residency.
        if (any(coord.xy < guard) || any(coord.xy >= 1 - guard) || coord.z < 0 || coord.z > 1)
            continue;
#if defined(VIVID_VSM_RECEIVER_DEBUG)
        if (g_VSMDebugQuality.y < 0) g_VSMDebugQuality.y = level;
#endif
        if (level < desired) continue;
        float edge = max(abs(coord.x - 0.5), abs(coord.y - 0.5));
        blend = VSMTransitionWeight(edge + guard, p.parameters.z);
        if (level == desired)
            blend = max(blend, VSMTransitionWeight(frac(lod) * 0.5, p.parameters.z));
#if defined(VIVID_VSM_RECEIVER_DEBUG)
        g_VSMDebugQuality.zw = float2(level, worst >= 0 ? worst * exp2((float)level) / target : -1);
#endif
        return level;
    }
    return -1;
}
