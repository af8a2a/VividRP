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

// SHCommon.ush::EvaluateSHIrradiance. Apply each zonal factor to the corresponding
// cosine-convolved band. The packed constant also carries the band-2 Z term.
float3 EvaluateSHIrradiance(float3 Direction, float AO, SdfProbeIrradiance SH)
{
    float CosA2 = saturate(1.0f - AO);
    float CosA = sqrt(CosA2);
    float Z0 = 1.0f - CosA2;
    float Z1 = 1.0f - CosA2 * CosA;
    float Z2 = Z0 * (1.0f + 3.0f * CosA2);

    float3 Constant = float3(SH.r0.w, SH.g0.w, SH.b0.w);
    float3 Band0 = Constant + float3(SH.r1.z, SH.g1.z, SH.b1.z) / 3.0f;
    float3 Band1 = float3(dot(SH.r0.xyz, Direction), dot(SH.g0.xyz, Direction), dot(SH.b0.xyz, Direction));
    float4 Products = Direction.xyzz * Direction.yzzx;
    float3 Band2 = Constant - Band0
        + float3(dot(SH.r1, Products), dot(SH.g1, Products), dot(SH.b1, Products))
        + SH.c * (Direction.x * Direction.x - Direction.y * Direction.y);
    return max(float3(0, 0, 0), Band0 * Z0 + Band1 * Z1 + Band2 * Z2);
}

#endif

