#pragma max_recursion_depth 1

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/RaytracingGBufferCommon.hlsl"

RaytracingAccelerationStructure _AccelerationStructure;

RWTexture2D<float> _RaytracingGBufferViewZ;
RWTexture2D<float4> _RaytracingGBufferMotionVectors;
RWTexture2D<float2> _RaytracingGBufferDlssMotionVectors;
RWTexture2D<float4> _RaytracingGBufferNrdNormalRoughness;
RWTexture2D<float4> _RaytracingGBufferBaseColorMetalness;
RWTexture2D<float4> _RaytracingGBufferDlssNormalRoughness;
RWTexture2D<float4> _RaytracingGBufferDiffuseAlbedo;
RWTexture2D<float4> _RaytracingGBufferSpecularAlbedo;
RWTexture2D<float4> _RaytracingGBufferNrdDiffuseMaterialFactor;
RWTexture2D<float4> _RaytracingGBufferNrdSpecularMaterialFactor;
RWTexture2D<float> _RaytracingGBufferDlssDepth;

float4 _RaytracingGBufferCameraPositionWS;
float4x4 _RaytracingGBufferPixelCoordToViewDirWS;
float4x4 _RaytracingGBufferWorldToView;
float4x4 _RaytracingGBufferWorldToClip;
float4x4 _RaytracingGBufferWorldToViewPrevious;
float4x4 _RaytracingGBufferWorldToClipPrevious;
float2 _RaytracingGBufferScreenSize;
float _RaytracingGBufferRayMinDistance;
float _RaytracingGBufferRayMaxDistance;

static const float kRaytracingGBufferNrdInf = 1000000.0;

float3 GetRaytracingGBufferPrimaryRayDirectionWS(float2 pixelCoord)
{
    float4 viewDirectionWS = mul(float4(pixelCoord, 1.0, 1.0), _RaytracingGBufferPixelCoordToViewDirWS);
    return -normalize(viewDirectionWS.xyz);
}

float2 GetRaytracingGBufferScreenUv(float4x4 worldToClip, float3 positionWS)
{
    float4 clip = mul(worldToClip, float4(positionWS, 1.0));
    return (clip.xy / max(abs(clip.w), 1e-6)) * float2(0.5, -0.5) + 0.5;
}

float2 EncodeRaytracingGBufferOctNormal(float3 normalWS)
{
    normalWS /= max(abs(normalWS.x) + abs(normalWS.y) + abs(normalWS.z), 1e-6);
    float2 wrapped = (1.0 - abs(normalWS.yx)) * (step(0.0, normalWS.xy) * 2.0 - 1.0);
    float2 encoded = normalWS.z >= 0.0 ? normalWS.xy : wrapped;
    return encoded * 0.5 + 0.5;
}

float4 PackRaytracingGBufferNrdNormalRoughness(float3 normalWS, float linearRoughness)
{
    return float4(
        EncodeRaytracingGBufferOctNormal(normalize(normalWS)),
        saturate(linearRoughness),
        0.0);
}

float3 GetRaytracingGBufferSpecularAlbedo(
    float3 baseColor,
    float metalness,
    float perceptualRoughness,
    float3 normalWS,
    float3 viewDirectionWS)
{
    float3 reflectance0 = lerp(0.04.xxx, saturate(baseColor), saturate(metalness));
    float noV = saturate(dot(normalize(normalWS), normalize(viewDirectionWS)));
    // OpenPBR's specular_roughness is perceptual roughness. The split-sum
    // environment term consumes alpha roughness instead.
    float alphaRoughness = perceptualRoughness * perceptualRoughness;
    float4 c0 = float4(-1.0, -0.0275, -0.572, 0.022);
    float4 c1 = float4(1.0, 0.0425, 1.04, -0.04);
    float4 r = saturate(alphaRoughness) * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * noV)) * r.x + r.y;
    float2 ab = float2(-1.04, 1.04) * a004 + r.zw;
    return saturate(reflectance0 * ab.x + ab.y);
}

[shader("raygeneration")]
void RayGenRaytracingGBuffer()
{
    uint2 launchIndex = DispatchRaysIndex().xy;
    uint2 launchDimensions = DispatchRaysDimensions().xy;
    uint2 pixelCoord = uint2(launchIndex.x, launchDimensions.y - launchIndex.y - 1u);

    float2 pixelCenter = (float2)pixelCoord + 0.5;
    float3 rayDirectionWS = GetRaytracingGBufferPrimaryRayDirectionWS(pixelCenter);
    float3 rayDirectionDx = GetRaytracingGBufferPrimaryRayDirectionWS(pixelCenter + float2(1.0, 0.0));
    float3 rayDirectionDy = GetRaytracingGBufferPrimaryRayDirectionWS(pixelCenter + float2(0.0, 1.0));

    RaytracingGBufferPayload payload;
    InitializeRaytracingGBufferPayload(payload);
    payload.rayConeSpreadAngle = max(
        length(rayDirectionDx - rayDirectionWS),
        length(rayDirectionDy - rayDirectionWS));

    RayDesc ray;
    ray.Origin = _RaytracingGBufferCameraPositionWS.xyz;
    ray.Direction = rayDirectionWS;
    ray.TMin = _RaytracingGBufferRayMinDistance;
    ray.TMax = _RaytracingGBufferRayMaxDistance;
    TraceRay(
        _AccelerationStructure,
        RAY_FLAG_NONE,
        0xFF,
        0,
        1,
        0,
        ray,
        payload);

    if (payload.hit == 0u)
    {
        _RaytracingGBufferViewZ[pixelCoord] = kRaytracingGBufferNrdInf;
        _RaytracingGBufferMotionVectors[pixelCoord] = 0.0;
        _RaytracingGBufferDlssMotionVectors[pixelCoord] = 0.0;
        _RaytracingGBufferNrdNormalRoughness[pixelCoord] = 0.0;
        _RaytracingGBufferBaseColorMetalness[pixelCoord] = 0.0;
        _RaytracingGBufferDlssNormalRoughness[pixelCoord] = 0.0;
        _RaytracingGBufferDiffuseAlbedo[pixelCoord] = 0.0;
        _RaytracingGBufferSpecularAlbedo[pixelCoord] = 0.0;
        _RaytracingGBufferNrdDiffuseMaterialFactor[pixelCoord] = 1.0;
        _RaytracingGBufferNrdSpecularMaterialFactor[pixelCoord] = 1.0;
        _RaytracingGBufferDlssDepth[pixelCoord] = 0.0;
        return;
    }

    float viewZ = abs(mul(_RaytracingGBufferWorldToView, float4(payload.positionWS, 1.0)).z);
    float viewZPrevious = abs(mul(
        _RaytracingGBufferWorldToViewPrevious,
        float4(payload.positionWS, 1.0)).z);
    float2 currentUv = GetRaytracingGBufferScreenUv(
        _RaytracingGBufferWorldToClip,
        payload.positionWS);
    float2 previousUv = GetRaytracingGBufferScreenUv(
        _RaytracingGBufferWorldToClipPrevious,
        payload.positionWS);
    float3 motion = float3(
        (previousUv - currentUv) * _RaytracingGBufferScreenSize,
        viewZPrevious - viewZ);

    float3 normalWS = normalize(payload.shadingNormalWS);
    float linearRoughness = saturate(payload.linearRoughness);
    float metalness = saturate(payload.metalness);
    float3 baseColor = saturate(payload.baseColor);
    float3 diffuseAlbedo = baseColor * (1.0 - metalness);
    float3 specularAlbedo = GetRaytracingGBufferSpecularAlbedo(
        baseColor,
        metalness,
        linearRoughness,
        normalWS,
        -rayDirectionWS);

    float4 clip = mul(_RaytracingGBufferWorldToClip, float4(payload.positionWS, 1.0));
    float hardwareDepth = saturate(clip.z / max(abs(clip.w), 1e-6));

    _RaytracingGBufferViewZ[pixelCoord] = viewZ;
    _RaytracingGBufferMotionVectors[pixelCoord] = float4(motion, viewZ);
    _RaytracingGBufferDlssMotionVectors[pixelCoord] = motion.xy;
    _RaytracingGBufferNrdNormalRoughness[pixelCoord] =
        PackRaytracingGBufferNrdNormalRoughness(normalWS, linearRoughness);
    _RaytracingGBufferBaseColorMetalness[pixelCoord] = float4(baseColor, metalness);
    // DLSS-RR consumes sqrt(alpha roughness). For the StandardLit/OpenPBR
    // mapping alpha = perceptualRoughness^2, so this is perceptual roughness.
    _RaytracingGBufferDlssNormalRoughness[pixelCoord] =
        float4(normalWS, linearRoughness);
    _RaytracingGBufferDiffuseAlbedo[pixelCoord] = float4(diffuseAlbedo, 1.0);
    _RaytracingGBufferSpecularAlbedo[pixelCoord] = float4(specularAlbedo, 1.0);
    _RaytracingGBufferNrdDiffuseMaterialFactor[pixelCoord] =
        float4(payload.nrdDiffuseMaterialFactor, 1.0);
    _RaytracingGBufferNrdSpecularMaterialFactor[pixelCoord] =
        float4(payload.nrdSpecularMaterialFactor, 1.0);
    _RaytracingGBufferDlssDepth[pixelCoord] = hardwareDepth;
}

[shader("miss")]
void MissRaytracingGBuffer(inout RaytracingGBufferPayload payload : SV_RayPayload)
{
    InitializeRaytracingGBufferPayload(payload);
}
