#ifndef VIVIDRP_REFERENCED_PATH_TRACING_COMMON_INCLUDED
#define VIVIDRP_REFERENCED_PATH_TRACING_COMMON_INCLUDED

float4 _ReferencedMainLightDirectionWS;
float4 _ReferencedMainLightColor;
int _ReferencedReGIREnabled;

// E0 environment contract, consumed by the E1 camera and BSDF miss paths.
// E2 will add the importance-distribution textures used by the sampling mode below.
TextureCube<float4> _ReferencedEnvironmentTexture;
SamplerState sampler_ReferencedEnvironmentTexture;
float4 _ReferencedEnvironmentTint;
// x: scene-linear intensity multiplier, y: rotation in degrees,
// z: maximum available mip, w: valid HDRI source.
float4 _ReferencedEnvironmentParameters;
int _ReferencedEnvironmentLightingEnabled;
int _ReferencedEnvironmentCameraVisible;
int _ReferencedEnvironmentImportanceSamplingEnabled;
int _ReferencedEnvironmentSamplingMode;
int _ReferencedEnvironmentDebugMode;
float4 _ReferencedCameraClearColor;
int _ReferencedCameraSkyEnabled;

static const int kReferencedEnvironmentDebugCombined = 0;
static const int kReferencedEnvironmentDebugEnvironmentOnly = 1;
static const int kReferencedEnvironmentDebugPrimaryBackgroundOnly = 2;
static const int kReferencedEnvironmentDebugIndirectMissOnly = 3;

bool ReferencedPathtracingHasEnvironment()
{
    return _ReferencedEnvironmentParameters.w > 0.5;
}

float3 ReferencedPathtracingRotateEnvironmentDirection(float3 directionWS)
{
    float rotationRadians = radians(_ReferencedEnvironmentParameters.y);
    float sine;
    float cosine;
    sincos(rotationRadians, sine, cosine);

    return float3(
        cosine * directionWS.x - sine * directionWS.z,
        directionWS.y,
        sine * directionWS.x + cosine * directionWS.z);
}

float3 ReferencedPathtracingSampleEnvironment(float3 directionWS)
{
    float directionLengthSquared = dot(directionWS, directionWS);
    if (!ReferencedPathtracingHasEnvironment()
        || directionLengthSquared <= 1e-8
        || isnan(directionLengthSquared)
        || isinf(directionLengthSquared))
    {
        return 0.0;
    }

    float3 rotatedDirectionWS = ReferencedPathtracingRotateEnvironmentDirection(
        directionWS * rsqrt(directionLengthSquared));
    float3 radiance = _ReferencedEnvironmentTexture.SampleLevel(
        sampler_ReferencedEnvironmentTexture,
        rotatedDirectionWS,
        0.0).rgb;
    radiance *= max(_ReferencedEnvironmentTint.rgb, 0.0)
        * max(_ReferencedEnvironmentParameters.x, 0.0);
    return !any(isnan(radiance)) && !any(isinf(radiance))
        ? max(radiance, 0.0)
        : 0.0;
}

float3 ReferencedPathtracingEvaluateLightingEnvironment(float3 directionWS)
{
    return _ReferencedEnvironmentLightingEnabled != 0
        ? ReferencedPathtracingSampleEnvironment(directionWS)
        : 0.0;
}

float4 ReferencedPathtracingEvaluateCameraBackground(float3 directionWS)
{
    if (_ReferencedCameraSkyEnabled != 0
        && _ReferencedEnvironmentCameraVisible != 0
        && ReferencedPathtracingHasEnvironment())
    {
        return float4(ReferencedPathtracingSampleEnvironment(directionWS), 1.0);
    }

    return float4(
        max(_ReferencedCameraClearColor.rgb, 0.0),
        saturate(_ReferencedCameraClearColor.a));
}

struct ReferencedPathtracingPayload
{
    // Raygen inputs consumed by closest-hit.
    float3 pathThroughput;
    float3 bsdfRandom;
    float3 directLightRandom;
    float rayConeWidth;
    float rayConeSpreadAngle;

    // Compact closest-hit outputs consumed by the iterative path loop.
    float3 positionWS;
    float3 faceNormalWS;
    float3 emission;
    float3 mainLightDiffuseBsdf;
    float3 mainLightSpecularBsdf;
    float3 reGIRLocalDiffuseRadiance;
    float3 reGIRLocalSpecularRadiance;
    float3 reGIRLocalDirectionWS;
    float reGIRLocalDistance;
    float3 nextDirectionWS;
    float3 nextThroughputWeight;
    float nextPdf;
    float linearRoughness;
    float hitDistance;
    uint nextLobeClass;
    uint hit;
};

void InitializeReferencedPathtracingPayload(out ReferencedPathtracingPayload payload)
{
    payload.pathThroughput = 1.0;
    payload.bsdfRandom = 0.0;
    payload.directLightRandom = 0.0;
    payload.rayConeWidth = 0.0;
    payload.rayConeSpreadAngle = 0.0;
    payload.positionWS = 0.0;
    payload.faceNormalWS = 0.0;
    payload.emission = 0.0;
    payload.mainLightDiffuseBsdf = 0.0;
    payload.mainLightSpecularBsdf = 0.0;
    payload.reGIRLocalDiffuseRadiance = 0.0;
    payload.reGIRLocalSpecularRadiance = 0.0;
    payload.reGIRLocalDirectionWS = 0.0;
    payload.reGIRLocalDistance = 0.0;
    payload.nextDirectionWS = 0.0;
    payload.nextThroughputWeight = 0.0;
    payload.nextPdf = 0.0;
    payload.linearRoughness = 1.0;
    payload.hitDistance = 0.0;
    payload.nextLobeClass = 0u;
    payload.hit = 0u;
}

#endif
