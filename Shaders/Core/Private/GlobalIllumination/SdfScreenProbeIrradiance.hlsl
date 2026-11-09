#ifndef VIVID_SDF_SCREEN_PROBE_IRRADIANCE_INCLUDED
#define VIVID_SDF_SCREEN_PROBE_IRRADIANCE_INCLUDED

// Three cosine-convolved SH bands packed as the Cartesian quadratic form.
struct SdfProbeIrradiance
{
    float4 r0, g0, b0;
    float4 r1, g1, b1;
    float3 c;
};

void ProjectSdfProbeIrradiance(inout SdfProbeIrradiance sh, float3 radiance, float3 d)
{
    // The three-band expansion of max(0, n.d) is
    // 0.09375 + 0.5 * (n.d) + 0.46875 * (n.d)^2.
    float xy = 0.234375 * dot(d.xy, d.xy);
    float4 l01 = float4(0.5 * d, 0.09375 + xy);
    float4 l2 = float4(0.9375 * d.x * d.y, 0.9375 * d.y * d.z,
        0.46875 * d.z * d.z - xy, 0.9375 * d.z * d.x);
    sh.r0 += radiance.r * l01;
    sh.g0 += radiance.g * l01;
    sh.b0 += radiance.b * l01;
    sh.r1 += radiance.r * l2;
    sh.g1 += radiance.g * l2;
    sh.b1 += radiance.b * l2;
    sh.c += radiance * (0.234375 * (d.x * d.x - d.y * d.y));
}

float3 EvaluateSdfProbeIrradiance(SdfProbeIrradiance sh, float3 normal)
{
    float4 n = float4(normal, 1.0);
    float4 products = normal.xyzz * normal.yzzx;
    return float3(dot(sh.r0, n), dot(sh.g0, n), dot(sh.b0, n))
        + float3(dot(sh.r1, products), dot(sh.g1, products), dot(sh.b1, products))
        + sh.c * (normal.x * normal.x - normal.y * normal.y);
}

#endif

