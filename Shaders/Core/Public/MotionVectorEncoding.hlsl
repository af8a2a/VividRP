#ifndef VIVIDRP_MOTION_VECTOR_ENCODING_INCLUDED
#define VIVIDRP_MOTION_VECTOR_ENCODING_INCLUDED

// UE 5.7.4 Common.ush, EncodeVelocityToTexture / DecodeVelocityFromTexture,
// desktop gamma encoding with velocity depth. XY is NDC, Z is device depth.
// Camera and object motion share this target. The spare depth bit marks an object override.
float4 VividEncodeVelocity(float3 V, uint objectVelocity)
{
    V.xy = sign(V.xy) * sqrt(abs(V.xy)) * (2.0 / sqrt(2.0));
    float4 encoded;
    encoded.xy = V.xy * (0.499f * 0.5f) + 32767.0f / 65535.0f;
    uint Vz = asuint(V.z);
    encoded.z = saturate(float((Vz >> 16) & 0xFFFFu) * rcp(65535.0f) + (0.1 / 65535.0f));
    encoded.w = saturate(float((Vz & 0xFFFEu) | (objectVelocity & 1u)) * rcp(65535.0f) + (0.1 / 65535.0f));
    return encoded;
}

// Sampling an older two-channel signed UV target supplies B=0 and A=1.
bool VividHasLegacyMotionUV(float4 encoded)
{
    return encoded.z == 0.0 && encoded.w == 1.0;
}

bool PixelSetAsNoMotionVectors(float4 encoded)
{
    return VividHasLegacyMotionUV(encoded) ? encoded.x > 1.0 : encoded.x == 0.0;
}

bool VividHasObjectVelocity(float4 encoded)
{
    return !VividHasLegacyMotionUV(encoded) && !PixelSetAsNoMotionVectors(encoded) && (uint(round(encoded.w * 65535.0f)) & 1u) != 0u;
}

float2 VividDecodeMotionUV(float4 encoded)
{
    if (VividHasLegacyMotionUV(encoded))
        return encoded.x > 1.0 ? 0.0 : encoded.xy;
    if (encoded.x == 0.0)
        return 0.0;
    const float InvDiv = 1.0f / (0.499f * 0.5f);
    float2 V = encoded.xy * InvDiv - 32767.0f / 65535.0f * InvDiv;
    V = (V * abs(V)) * 0.5;
    return V * 0.5; // Vivid consumers use UV displacement, including the render-target Y sign.
}

float VividDecodeMotionDeviceZ(float4 encoded)
{
    return asfloat((uint(round(encoded.z * 65535.0f)) << 16) | (uint(round(encoded.w * 65535.0f)) & 0xFFFEu));
}

float VividPreviousDeviceZ(float4 encoded, float deviceZ, float previousCameraDeviceZ)
{
    return VividHasObjectVelocity(encoded) ? deviceZ - VividDecodeMotionDeviceZ(encoded) : previousCameraDeviceZ;
}

void DecodeMotionVector(float4 encoded, out float2 motionVector)
{
    motionVector = VividDecodeMotionUV(encoded);
}

#endif
