#ifndef VIVID_BENT_NORMAL_PACKING_INCLUDED
#define VIVID_BENT_NORMAL_PACKING_INCLUDED

// LumenScreenSpaceBentNormal.ush and PackUnpack.ush, using the existing
// packed axis convention for Vivid's world-space bent normals.
uint PackScreenBentNormal(float3 BentNormal)
{
    float3 rgb = BentNormal.zxy * 0.5f + 0.5f;
    uint r = uint(floor(saturate(rgb.r) * 2047.0 + 0.5));
    uint g = uint(floor(saturate(rgb.g) * 2047.0 + 0.5));
    uint b = uint(floor(saturate(rgb.b) * 1023.0 + 0.5));
    return (r << 21) | (g << 10) | b;
}

float3 UnpackScreenBentNormal(uint PackedBentNormal)
{
    float r = float(PackedBentNormal >> 21) * (1.0 / 2047.0);
    float g = float((PackedBentNormal >> 10) & 2047) * (1.0 / 2047.0);
    float b = float(PackedBentNormal & 1023) * (1.0 / 1023.0);
    return float3(g, b, r) * 2.0f - 1.0f;
}

float VividBentNormalAcosFast(float inX)
{
    float x = abs(inX);
    float res = -0.156583f * x + (0.5 * PI);
    res *= sqrt(1.0f - x);
    return (inX >= 0) ? res : PI - res;
}

#endif
