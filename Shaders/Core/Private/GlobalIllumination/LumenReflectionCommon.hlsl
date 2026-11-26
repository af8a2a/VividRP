#ifndef VIVID_LUMEN_REFLECTION_COMMON_INCLUDED
#define VIVID_LUMEN_REFLECTION_COMMON_INCLUDED

// Verbatim port of UE5 LumenReflectionCommon.ush, LumenReflectionDenoiserCommon.ush,
// and required math helpers from MonteCarlo.ush, BRDF.ush, Common.ush, RandomPCG.ush,
// RandomInterleavedGradientNoise.ush, and FastMathThirdParty.ush.

#include "../../Public/VividLumenReflectionsCombine.hlsl"

#define REFLECTION_THREADGROUP_SIZE_2D 8u
#define REFLECTION_THREADGROUP_SIZE_1D 64u
#define LUMEN_TILE_BITMASK_REFLECTIONS 0x08u

#define LUMEN_FLOAT_32_MAX float(+3.402823466e+38f)
#define LUMEN_MAX_HALF_FLOAT 65504.0f
// Float R11G11B10 max (LumenReflectionDenoiserCommon.ush)
#define INVALID_LIGHTING float3(65024.0f, 65024.0f, 64512.0f)

// Must be larger than FLOAT16_MIN (LumenReflectionCommon.ush)
static const float MinReflectionConeAngle = 0.00001f;

float4 _SdfProbeCameraPosition;

uint2 _LumenReflectionTracingBufferSize;
float2 _LumenReflectionTracingBufferInvSize;
uint2 _LumenReflectionDownsampleFactorXY;
uint2 _LumenReflectionTracingViewSize;
float _LumenReflectionMaxRayIntensity;
uint _LumenReflectionStateFrameIndex;
uint _LumenReflectionStateFrameIndexMod8;
uint _LumenReflectionRayDirectionFrameIndex;
float _LumenReflectionGGXSamplingBias;
float _LumenReflectionMaxTraceDistance;
float _LumenReflectionMaxMeshSdfTraceDistance;
float _LumenReflectionDenoiserOneOverTonemapRange;
uint _LumenReflectionHasBlueNoise;
uint _LumenReflectionHasDualSlabSidecar;
uint _LumenReflectionUseWorldCache;
uint _LumenReflectionUseGlobalSdf;

Texture2D<float2> _LumenReflectionBlueNoiseVec2;
Texture2D<float> _LumenReflectionBlueNoiseScalar;
Texture2D<float4> _LumenReflectionLayerAux0;
Texture2D<float4> _LumenReflectionLayerAux1;

// UE5 Common.ush / PackUnpack.ush helpers
uint2 LumenZOrder2D(uint Index, const uint SizeLog2)
{
    uint2 Coord = 0u;
    [unroll]
    for (uint i = 0u; i < SizeLog2; i++)
    {
        Coord.x |= ((Index >> (2u * i + 0u)) & 0x1u) << i;
        Coord.y |= ((Index >> (2u * i + 1u)) & 0x1u) << i;
    }
    return Coord;
}

uint LumenPackTileCoord12bits(uint2 In)
{
    return (In.x & 0xFFFu) | ((In.y & 0xFFFu) << 12u);
}

uint2 LumenUnpackTileCoord12bits(uint In)
{
    return uint2(In & 0xFFFu, (In >> 12u) & 0xFFFu);
}

uint LumenPackTileCoord14bits(uint2 In)
{
    return (In.x & 0x3FFFu) | ((In.y & 0x3FFFu) << 14u);
}

uint2 LumenUnpackTileCoord14bits(uint In)
{
    return uint2(In & 0x3FFFu, (In >> 14u) & 0x3FFFu);
}

// UE5 RandomPCG.ush
uint3 LumenRand3DPCG16(int3 p)
{
    uint3 v = uint3(p);
    v = v * 1664525u + 1013904223u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    return v >> 16u;
}

float2 LumenRand16ToFloat(uint2 Rand16Bits)
{
    return float2(Rand16Bits) * (1.0f / 65536.0f);
}

// UE5 RandomInterleavedGradientNoise.ush
float LumenInterleavedGradientNoise(float2 uv, float FrameId)
{
    uv += FrameId * (float2(47.0f, 17.0f) * 0.695f);
    const float3 magic = float3(0.06711056f, 0.00583715f, 52.9829189f);
    return frac(magic.z * frac(dot(uv, magic.xy)));
}

// UE5 FastMathThirdParty.ush
float LumenAcosFast(float inX)
{
    float x = abs(inX);
    float res = -0.156583f * x + (0.5f * PI);
    res *= sqrt(saturate(1.0f - x));
    return (inX >= 0.0f) ? res : PI - res;
}

float LumenAtanFastPos(float x)
{
    float t0 = (x < 1.0f) ? x : 1.0f / x;
    float t1 = t0 * t0;
    float poly = 0.0872929f;
    poly = -0.301895f + poly * t1;
    poly = 1.0f + poly * t1;
    poly = poly * t0;
    return (x < 1.0f) ? poly : (0.5f * PI) - poly;
}

float LumenAtanFast(float x)
{
    float t0 = LumenAtanFastPos(abs(x));
    return (x < 0.0f) ? -t0 : t0;
}

// UE5 MonteCarlo.ush
float3x3 LumenGetTangentBasis(float3 TangentZ)
{
    const float Sign = TangentZ.z >= 0.0f ? 1.0f : -1.0f;
    const float a = -rcp(Sign + TangentZ.z);
    const float b = TangentZ.x * TangentZ.y * a;

    float3 TangentX = float3(1.0f + Sign * a * (TangentZ.x * TangentZ.x), Sign * b, -Sign * TangentZ.x);
    float3 TangentY = float3(b, Sign + a * (TangentZ.y * TangentZ.y), -TangentZ.y);

    return float3x3(TangentX, TangentY, TangentZ);
}

float2 LumenHammersley16(uint Index, uint NumSamples, uint2 Random)
{
    float E1 = frac((float)Index / NumSamples + float(Random.x) * (1.0f / 65536.0f));
    float E2 = float((reversebits(Index) >> 16u) ^ Random.y) * (1.0f / 65536.0f);
    return float2(E1, E2);
}

float3 LumenConcentricDiskSamplingHelper(float2 E)
{
    float2 p = 2.0f * E - 0.99999994f;
    float2 a = abs(p);
    float Lo = min(a.x, a.y);
    float Hi = max(a.x, a.y);
    float Epsilon = 5.42101086243e-20f;
    float Phi = (PI / 4.0f) * (Lo / (Hi + Epsilon) + 2.0f * float(a.y >= a.x));
    float Radius = Hi;
    const uint SignMask = 0x80000000u;
    float2 Disk = asfloat((asuint(float2(cos(Phi), sin(Phi))) & ~SignMask) | (asuint(p) & SignMask));
    return float3(Disk, Radius);
}

float2 LumenUniformSampleDiskConcentric(float2 E)
{
    float3 Result = LumenConcentricDiskSamplingHelper(E);
    return Result.xy * Result.z;
}

// UE5 MonteCarlo.ush::VisibleGGXPDF_aniso & ImportanceSampleVisibleGGX (with bounded VNDF)
float LumenVisibleGGXPDF_aniso(float3 V, float3 H, float2 Alpha, bool bLimitVDNFToReflection = true)
{
    float NoV = V.z;
    float NoH = H.z;
    float VoH = dot(V, H);
    float a2 = Alpha.x * Alpha.y;
    float3 Hs = float3(Alpha.y * H.x, Alpha.x * H.y, a2 * NoH);
    float S = dot(Hs, Hs);
    float ratio = a2 / max(S, 1e-20f);
    float D = (1.0f / PI) * a2 * (ratio * ratio);
    float LenV = length(float3(V.x * Alpha.x, V.y * Alpha.y, NoV));
    float k = 1.0f;
    if (bLimitVDNFToReflection)
    {
        float a = saturate(min(Alpha.x, Alpha.y));
        float s = 1.0f + length(V.xy);
        float ka2 = a * a, s2 = s * s;
        k = (s2 - ka2 * s2) / max(s2 + ka2 * V.z * V.z, 1e-20f);
    }
    return (2.0f * D * VoH) / max(k * NoV + LenV, 1e-20f);
}

float4 LumenImportanceSampleVisibleGGX(float2 E, float2 Alpha, float3 V, bool bLimitVDNFToReflection = true)
{
    float3 Vh = normalize(float3(Alpha * V.xy, max(V.z, 1e-4f)));

    float Phi = (2.0f * PI) * E.x;
    float k = 1.0f;
    if (bLimitVDNFToReflection)
    {
        float a = saturate(min(Alpha.x, Alpha.y));
        float s = 1.0f + length(V.xy);
        float a2 = a * a, s2 = s * s;
        k = (s2 - a2 * s2) / max(s2 + a2 * V.z * V.z, 1e-20f);
    }
    float Z = lerp(1.0f, -k * Vh.z, E.y);
    float SinTheta = sqrt(saturate(1.0f - Z * Z));
    float X = SinTheta * cos(Phi);
    float Y = SinTheta * sin(Phi);
    float3 H = float3(X, Y, Z) + Vh;

    H = normalize(float3(Alpha * H.xy, max(0.0f, H.z)));
    return float4(H, LumenVisibleGGXPDF_aniso(V, H, Alpha, bLimitVDNFToReflection));
}

// UE5 BRDF.ush::D_GGX (high precision variant to avoid NaN)
float LumenD_GGX(float a2, float NoH)
{
    float NoH2 = NoH * NoH;
    float safeA2 = max(a2, 1e-7f);
    float d = NoH2 + (1.0f - NoH2) / safeA2;
    return 1.0f / max(PI * safeA2 * d * d, 1e-20f);
}

// UE5 BlueNoise.ush adapter using VividRP's 128x8192 STBN atlas (Y-flipped on Unity import)
uint3 LumenBlueNoiseCoord(uint2 ScreenCoord, uint FrameIndex)
{
    uint3 Wrapped = uint3(ScreenCoord, FrameIndex) & uint3(127u, 127u, 63u);
    return uint3(Wrapped.x, 8191u - (Wrapped.z * 128u + Wrapped.y), 0u);
}

float2 LumenBlueNoiseVec2(uint2 ScreenCoord, uint FrameIndex)
{
    if (_LumenReflectionHasBlueNoise != 0u)
    {
        return _LumenReflectionBlueNoiseVec2.Load(LumenBlueNoiseCoord(ScreenCoord, FrameIndex)).xy;
    }
    uint2 RandomSeed = LumenRand3DPCG16(int3(ScreenCoord, FrameIndex & 7u)).xy;
    return LumenRand16ToFloat(RandomSeed);
}

// UE5 LumenReflectionCommon.ush
float2 GetScreenTileJitter(uint2 DownsampledScreenCoord)
{
    if (_LumenReflectionDownsampleFactorXY.x > 1u)
    {
        if (_LumenReflectionDownsampleFactorXY.y > 1u)
        {
            uint2 CellIndex = DownsampledScreenCoord % 2u;
            uint LinearIndex = CellIndex.x + CellIndex.y * 2u;
            LinearIndex = (LinearIndex + _LumenReflectionStateFrameIndex) % 4u;

            // 4 rooks sampling pattern
            uint2 Jitter;
            Jitter.x = (LinearIndex & 0x02u) != 0u ? 1u : 0u;
            Jitter.y = (LinearIndex & 0x01u) != 0u ? 0u : 1u;
            return float2(Jitter);
        }
        else
        {
            return float2((DownsampledScreenCoord.y + _LumenReflectionStateFrameIndex) % 2u, 0u);
        }
    }
    return 0.0f;
}

uint2 GetScreenCoordFromReflectionTracingCoord(uint2 ReflectionTracingCoord)
{
    float2 ScreenJitter = GetScreenTileJitter(ReflectionTracingCoord);
    uint2 ScreenSize = uint2(_SdfDiffuseScreenSize.xy);
    return min(ReflectionTracingCoord * _LumenReflectionDownsampleFactorXY + uint2(ScreenJitter + 0.5f), ScreenSize - 1u);
}

float2 GetScreenUVFromReflectionTracingCoord(uint2 ReflectionTracingCoord)
{
    float2 ScreenCoord = float2(ReflectionTracingCoord * _LumenReflectionDownsampleFactorXY) + GetScreenTileJitter(ReflectionTracingCoord) + 0.5f;
    ScreenCoord = min(ScreenCoord, _SdfDiffuseScreenSize.xy - 0.5f);
    return ScreenCoord * _SdfDiffuseScreenSize.zw;
}

float GetMaxHitDistance()
{
    return LUMEN_MAX_HALF_FLOAT;
}

float EncodeRayDistance(float HitDistance, bool bHit)
{
    HitDistance = max(HitDistance, 0.0f);
    return HitDistance * (bHit ? -1.0f : 1.0f);
}

float DecodeRayDistance(float Encoded, out bool bHit)
{
    bHit = (asuint(Encoded) & 0x80000000u) != 0u;
    return abs(Encoded);
}

struct FRayData
{
    float3 Direction;
    float PDF;
    float ConeHalfAngle;
};

FRayData UnpackRayData(float4 PackedRayData)
{
    FRayData RayData;
    RayData.Direction = PackedRayData.xyz;
    RayData.ConeHalfAngle = abs(PackedRayData.w);
    RayData.PDF = 1.0f / max(abs(PackedRayData.w), MinReflectionConeAngle);
    return RayData;
}

uint EncodeTraceTexel(uint2 ReflectionTracingCoord)
{
    return LumenPackTileCoord14bits(ReflectionTracingCoord);
}

uint2 DecodeTraceTexel(uint TraceTexelData)
{
    return LumenUnpackTileCoord14bits(TraceTexelData);
}

// UE5 LumenReflectionDenoiserCommon.ush
bool IsLightingValid(float3 V)
{
    return !all(V >= INVALID_LIGHTING - 1.0f);
}

float3 LumenRGBToYCoCg(float3 RGB)
{
    float Y  = dot(RGB, float3( 0.25f, 0.5f,  0.25f));
    float Co = dot(RGB, float3( 0.5f,  0.0f, -0.5f));
    float Cg = dot(RGB, float3(-0.25f, 0.5f, -0.25f));
    return float3(Y, Co, Cg);
}

float3 LumenYCoCgToRGB(float3 YCoCg)
{
    float Y  = YCoCg.x;
    float Co = YCoCg.y;
    float Cg = YCoCg.z;
    float R = Y + Co - Cg;
    float G = Y + Cg;
    float B = Y - Co - Cg;
    return float3(R, G, B);
}

float GetSpecularDominantDirFactor(float Roughness)
{
    float S = saturate(1.0f - Roughness);
    return S * (sqrt(S) + Roughness);
}

float GetSpecularLobeHalfAngle(float Roughness)
{
    float E = 0.75f;
    return LumenAtanFast(((Roughness * Roughness) * E) / (1.0f - E));
}

float3 RGBToDenoiserSpace(float3 V)
{
    if (IsLightingValid(V))
    {
        float Weight = 1.0f / (1.0f + Luminance(V) * _LumenReflectionDenoiserOneOverTonemapRange);
        V = V * Weight;
    }
    return V;
}

float4 RGBAToDenoiserSpace(float4 V)
{
    V.xyz = RGBToDenoiserSpace(V.xyz);
    return V;
}

float3 DenoiserSpaceToRGB(float3 V)
{
    if (IsLightingValid(V))
    {
        float InvWeight = 1.0f / max(1.0f - Luminance(V) * _LumenReflectionDenoiserOneOverTonemapRange, 1e-4f);
        V = V * InvWeight;
    }
    return V;
}

float PackNumFramesAccumulated(float X)
{
    return X / 32.0f + 0.5f / 255.0f;
}

float UnpackNumFramesAccumulated(float X)
{
    return X * 32.0f;
}

float GetMaxFramesAccumulated(float MaxFrames, float Roughness)
{
    return lerp(2.0f, MaxFrames, saturate(Roughness / 0.05f));
}

float3 LumenMakeFinite(float3 V)
{
    return isfinite(V) ? V : 0.0f;
}

float LumenMakeFinite(float V)
{
    return isfinite(V) ? V : 0.0f;
}

// Thin VividRP Surface Summary / DualSlab GBuffer adapter -> FLumenReflectionMaterial
struct FLumenReflectionMaterial
{
    bool bIsValid;
    bool bHasBackfaceDiffuse;
    bool bIsClearCoat;
    float DeviceZ;
    float SceneDepth;
    float3 WorldNormal;
    float Roughness;
    float TopLayerRoughness;
    float3 DiffuseAlbedo;
};

float LumenReflectionEyeDepth(float deviceZ)
{
    return unity_OrthoParams.w != 0.0f
        ? lerp(_ProjectionParams.z, _ProjectionParams.y, deviceZ)
        : LinearEyeDepth(deviceZ, _ZBufferParams);
}

float2 LumenNdcToUvScale()
{
#if UNITY_UV_STARTS_AT_TOP
    return float2(0.5f, -0.5f);
#else
    return float2(0.5f, 0.5f);
#endif
}

FLumenReflectionMaterial ReadLumenReflectionMaterial(uint2 ScreenCoord, bool bApplySmoothBias)
{
    FLumenReflectionMaterial Mat = (FLumenReflectionMaterial)0;
    float deviceZ = _SdfDiffuseDepth.Load(int3(ScreenCoord, 0));
    Mat.DeviceZ = deviceZ;
    if (deviceZ == UNITY_RAW_FAR_CLIP_VALUE)
    {
        Mat.SceneDepth = 1e7f;
        return Mat;
    }

    float4 rt1 = _SdfDiffuseGBuffer1.Load(int3(ScreenCoord, 0));
    if (!VividIsSurfaceSummaryGBufferABIValid(rt1.a))
    {
        Mat.SceneDepth = LumenReflectionEyeDepth(deviceZ);
        return Mat;
    }

    float4 rt0 = _SdfDiffuseGBuffer0.Load(int3(ScreenCoord, 0));
    uint header = VividSanitizeDeferredExportHeader((uint)round(saturate(rt0.a) * 255.0f));
    uint exportClass = VividGetDeferredExportClass(header);
    if (exportClass == VIVID_DEFERRED_EXPORT_CLASS_EMPTY || exportClass == VIVID_DEFERRED_EXPORT_CLASS_UNLIT)
    {
        Mat.SceneDepth = LumenReflectionEyeDepth(deviceZ);
        return Mat;
    }

    Mat.bIsValid = true;
    Mat.bHasBackfaceDiffuse = (exportClass == VIVID_DEFERRED_EXPORT_CLASS_SUBSURFACE);
    Mat.SceneDepth = LumenReflectionEyeDepth(deviceZ);
    Mat.WorldNormal = VividDecodeSurfaceSummaryNormal(rt1.xy);
    Mat.Roughness = saturate(rt1.z);
    Mat.TopLayerRoughness = Mat.Roughness;
    Mat.DiffuseAlbedo = VividDecodeSurfaceSummaryDiffuseAlbedo(rt0.rgb);

    if (exportClass == VIVID_DEFERRED_EXPORT_CLASS_DUAL_SLAB && _LumenReflectionHasDualSlabSidecar != 0u)
    {
        float4 aux0 = _LumenReflectionLayerAux0.Load(int3(ScreenCoord, 0));
        if (VividIsDualSlabLayerSidecarValid(aux0))
        {
            float4 aux1 = _LumenReflectionLayerAux1.Load(int3(ScreenCoord, 0));
            VividDualSlabLayerData topLayer = VividUnpackDualSlabLayerSidecar(aux0, aux1);
            if (topLayer.layerWeight > VIVID_DUAL_SLAB_LAYER_SIDECAR_MIN_WEIGHT)
            {
                Mat.bIsClearCoat = true;
                Mat.TopLayerRoughness = min(Mat.Roughness, topLayer.perceptualRoughness);
            }
        }
    }

    if (bApplySmoothBias)
    {
        Mat.TopLayerRoughness = ApplyLumenReflectionSmoothBias(Mat.TopLayerRoughness);
    }

    return Mat;
}

bool NeedLumenReflectionRay(const FLumenReflectionMaterial Mat)
{
    return Mat.bIsValid && NeedsLumenReflections(Mat.Roughness, Mat.TopLayerRoughness, Mat.bHasBackfaceDiffuse, Mat.bIsClearCoat);
}

float3 LumenWorldPositionFromScreenUV(float2 ScreenUV, float DeviceZ)
{
    return ComputeWorldSpacePosition(ScreenUV, DeviceZ, _SdfDiffuseInverseVP);
}

#endif
