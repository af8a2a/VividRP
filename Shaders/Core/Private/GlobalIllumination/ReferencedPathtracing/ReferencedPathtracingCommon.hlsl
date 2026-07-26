#ifndef VIVIDRP_REFERENCED_PATH_TRACING_COMMON_INCLUDED
#define VIVIDRP_REFERENCED_PATH_TRACING_COMMON_INCLUDED

int _ReferencedLocalLightNeeEnabled;

// Lower-resolution lighting cubemap shared by BSDF miss, distribution build, and NEE.
TextureCube<float4> _ReferencedEnvironmentTexture;
SamplerState sampler_ReferencedEnvironmentTexture;
// Raw source cubemap retained at asset resolution for primary camera background evaluation.
TextureCube<float4> _ReferencedEnvironmentBackgroundTexture;
SamplerState sampler_ReferencedEnvironmentBackgroundTexture;
float4 _ReferencedEnvironmentTint;
// x: scene-linear intensity multiplier, y: rotation in degrees,
// z: maximum available mip, w: valid HDRI source.
float4 _ReferencedEnvironmentParameters;
int _ReferencedEnvironmentLightingEnabled;
int _ReferencedEnvironmentCameraVisible;
int _ReferencedEnvironmentImportanceSamplingEnabled;
int _ReferencedEnvironmentNeeEnabled;
int _ReferencedEnvironmentSamplingMode;
int _ReferencedEnvironmentEstimatorMode;
int _ReferencedEnvironmentDebugMode;
float4 _ReferencedCameraClearColor;
int _ReferencedCameraSkyEnabled;

static const int kReferencedEnvironmentSamplingBsdfOnly = 0;
static const int kReferencedEnvironmentSamplingImportance = 1;
static const int kReferencedEnvironmentSamplingUniformSphere = 2;
static const int kReferencedEnvironmentEstimatorMis = 0;
static const int kReferencedEnvironmentEstimatorLightOnly = 1;
static const int kReferencedEnvironmentEstimatorBsdfOnly = 2;
static const int kReferencedEnvironmentDebugCombined = 0;
static const int kReferencedEnvironmentDebugEnvironmentOnly = 1;
static const int kReferencedEnvironmentDebugPrimaryBackgroundOnly = 2;
static const int kReferencedEnvironmentDebugIndirectMissOnly = 3;
static const float kReferencedPathtracingPi = 3.14159265358979323846;

#define REFERENCED_ENVIRONMENT_DISTRIBUTION_VERSION 1
#define REFERENCED_ENVIRONMENT_HEADER_ELEMENT_COUNT 4u
#define REFERENCED_ENVIRONMENT_PDF_NORMALIZATION_OFFSET 0u
#define REFERENCED_ENVIRONMENT_AVERAGE_LUMINANCE_OFFSET 1u
#define REFERENCED_ENVIRONMENT_VALID_OFFSET 2u
#define REFERENCED_ENVIRONMENT_VERSION_OFFSET 3u
#define REFERENCED_ENVIRONMENT_MARGINAL_RESOLUTION 64u
#define REFERENCED_ENVIRONMENT_CONDITIONAL_RESOLUTION 128u
#define REFERENCED_ENVIRONMENT_MARGINAL_OFFSET \
    REFERENCED_ENVIRONMENT_HEADER_ELEMENT_COUNT
#define REFERENCED_ENVIRONMENT_CONDITIONAL_OFFSET \
    (REFERENCED_ENVIRONMENT_MARGINAL_OFFSET \
        + REFERENCED_ENVIRONMENT_MARGINAL_RESOLUTION)
#define REFERENCED_ENVIRONMENT_MIN_LUMINANCE 1e-12

#if defined(REFERENCED_ENVIRONMENT_DISTRIBUTION_BUILD)
RWStructuredBuffer<float> _ReferencedEnvironmentImportanceDistribution;
#else
StructuredBuffer<float> _ReferencedEnvironmentImportanceDistribution;
#endif

bool ReferencedPathtracingHasEnvironment()
{
    return _ReferencedEnvironmentParameters.w > 0.5;
}

float ReferencedPathtracingEnvironmentLuminance(float3 radiance)
{
    return max(dot(max(radiance, 0.0), float3(0.2126, 0.7152, 0.0722)), 0.0);
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

float3 ReferencedPathtracingSampleBackgroundEnvironment(float3 directionWS)
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
    float3 radiance = _ReferencedEnvironmentBackgroundTexture.SampleLevel(
        sampler_ReferencedEnvironmentBackgroundTexture,
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

float2 ReferencedPathtracingMapDirectionToEquiarealUV(float3 directionWS)
{
    float directionLengthSquared = dot(directionWS, directionWS);
    if (directionLengthSquared <= 1e-8
        || isnan(directionLengthSquared)
        || isinf(directionLengthSquared))
    {
        return 0.5;
    }

    float3 direction = directionWS * rsqrt(directionLengthSquared);
    float phi = atan2(-direction.z, -direction.x);
    float u = frac(0.5 - phi * (0.5 / kReferencedPathtracingPi));
    float v = saturate((direction.y + 1.0) * 0.5);
    return float2(u, v);
}

float3 ReferencedPathtracingMapEquiarealUVToDirection(float2 uv)
{
    uv = saturate(uv);
    float phi = 2.0 * kReferencedPathtracingPi * (1.0 - uv.x);
    float cosineTheta = clamp(2.0 * uv.y - 1.0, -1.0, 1.0);
    float sineTheta = sqrt(saturate(1.0 - cosineTheta * cosineTheta));
    float sinePhi;
    float cosinePhi;
    sincos(phi, sinePhi, cosinePhi);
    return float3(cosinePhi * sineTheta, cosineTheta, sinePhi * sineTheta);
}

float ReferencedPathtracingReadEnvironmentCDF(
    uint bufferOffset,
    uint index)
{
    return _ReferencedEnvironmentImportanceDistribution[bufferOffset + index];
}

float ReferencedPathtracingSampleEnvironmentCDF(
    uint bufferOffset,
    uint elementCount,
    float randomValue)
{
    float sampleValue = min(saturate(randomValue), 0.99999994);
    uint lowerIndex = 0u;
    uint upperIndex = elementCount;
    while (lowerIndex + 1u < upperIndex)
    {
        uint middleIndex = (lowerIndex + upperIndex) >> 1u;
        float middleCDF = ReferencedPathtracingReadEnvironmentCDF(
            bufferOffset,
            middleIndex);
        if (middleCDF <= sampleValue)
            lowerIndex = middleIndex;
        else
            upperIndex = middleIndex;
    }

    float lowerCDF = ReferencedPathtracingReadEnvironmentCDF(
        bufferOffset,
        lowerIndex);
    float upperCDF =
        lowerIndex + 1u < elementCount
            ? ReferencedPathtracingReadEnvironmentCDF(
                bufferOffset,
                lowerIndex + 1u)
            : 1.0;
    float interval = max(upperCDF - lowerCDF, 1e-8);
    float fraction = saturate((sampleValue - lowerCDF) / interval);
    return (lowerIndex + fraction) / elementCount;
}

bool ReferencedPathtracingHasEnvironmentDistributionEnergy()
{
    float valid = _ReferencedEnvironmentImportanceDistribution[
        REFERENCED_ENVIRONMENT_VALID_OFFSET];
    float version = _ReferencedEnvironmentImportanceDistribution[
        REFERENCED_ENVIRONMENT_VERSION_OFFSET];
    float normalization = _ReferencedEnvironmentImportanceDistribution[
        REFERENCED_ENVIRONMENT_PDF_NORMALIZATION_OFFSET];
    return valid > 0.5
        && abs(version - REFERENCED_ENVIRONMENT_DISTRIBUTION_VERSION) < 0.5
        && normalization > 0.0
        && !isnan(normalization)
        && !isinf(normalization);
}

float ReferencedPathtracingEvaluateEnvironmentPdf(float3 directionWS)
{
    if (_ReferencedEnvironmentLightingEnabled == 0
        || !ReferencedPathtracingHasEnvironment()
        || !ReferencedPathtracingHasEnvironmentDistributionEnergy())
    {
        return 0.0;
    }

    if (_ReferencedEnvironmentSamplingMode
        == kReferencedEnvironmentSamplingUniformSphere)
    {
        return 0.25 / kReferencedPathtracingPi;
    }

    if (_ReferencedEnvironmentImportanceSamplingEnabled == 0
        || _ReferencedEnvironmentSamplingMode
            != kReferencedEnvironmentSamplingImportance)
    {
        return 0.0;
    }

    float normalization = _ReferencedEnvironmentImportanceDistribution[
        REFERENCED_ENVIRONMENT_PDF_NORMALIZATION_OFFSET];
    float3 radiance =
        ReferencedPathtracingEvaluateLightingEnvironment(directionWS);
    float pdf =
        ReferencedPathtracingEnvironmentLuminance(radiance) * normalization;
    return !isnan(pdf) && !isinf(pdf) ? max(pdf, 0.0) : 0.0;
}

bool ReferencedPathtracingSampleEnvironment(
    float2 randomValue,
    out float3 directionWS,
    out float3 radiance,
    out float pdf)
{
    directionWS = 0.0;
    radiance = 0.0;
    pdf = 0.0;

    if (_ReferencedEnvironmentLightingEnabled == 0
        || !ReferencedPathtracingHasEnvironment()
        || !ReferencedPathtracingHasEnvironmentDistributionEnergy())
    {
        return false;
    }

    float2 uv;
    if (_ReferencedEnvironmentSamplingMode
        == kReferencedEnvironmentSamplingUniformSphere)
    {
        uv = min(saturate(randomValue), 0.99999994);
    }
    else
    {
        if (_ReferencedEnvironmentImportanceSamplingEnabled == 0
            || _ReferencedEnvironmentSamplingMode
                != kReferencedEnvironmentSamplingImportance)
        {
            return false;
        }

        float v = ReferencedPathtracingSampleEnvironmentCDF(
            REFERENCED_ENVIRONMENT_MARGINAL_OFFSET,
            REFERENCED_ENVIRONMENT_MARGINAL_RESOLUTION,
            randomValue.x);
        uint rowIndex = min(
            (uint)(v * REFERENCED_ENVIRONMENT_MARGINAL_RESOLUTION),
            REFERENCED_ENVIRONMENT_MARGINAL_RESOLUTION - 1u);
        float u = ReferencedPathtracingSampleEnvironmentCDF(
            REFERENCED_ENVIRONMENT_CONDITIONAL_OFFSET
                + rowIndex * REFERENCED_ENVIRONMENT_CONDITIONAL_RESOLUTION,
            REFERENCED_ENVIRONMENT_CONDITIONAL_RESOLUTION,
            randomValue.y);
        uv = float2(u, v);
    }

    directionWS = ReferencedPathtracingMapEquiarealUVToDirection(uv);
    radiance = ReferencedPathtracingEvaluateLightingEnvironment(directionWS);
    pdf = ReferencedPathtracingEvaluateEnvironmentPdf(directionWS);
    return pdf > 0.0;
}

float ReferencedPathtracingPowerHeuristic(float pdfA, float pdfB)
{
    pdfA = !isnan(pdfA) && !isinf(pdfA) ? max(pdfA, 0.0) : 0.0;
    pdfB = !isnan(pdfB) && !isinf(pdfB) ? max(pdfB, 0.0) : 0.0;
    float maximumPdf = max(pdfA, pdfB);
    if (maximumPdf <= 0.0)
        return 0.0;

    // Normalize before squaring so a very sharp glossy PDF cannot overflow.
    float normalizedA = pdfA / maximumPdf;
    float normalizedB = pdfB / maximumPdf;
    float squaredA = normalizedA * normalizedA;
    float squaredB = normalizedB * normalizedB;
    return squaredA / max(squaredA + squaredB, 1e-20);
}

float ReferencedPathtracingOneMinusCosFromSinSquared(float sinThetaSquared)
{
    sinThetaSquared = saturate(sinThetaSquared);
    return sinThetaSquared < 0.01
        ? sinThetaSquared * (0.5 + 0.125 * sinThetaSquared)
        : 1.0 - sqrt(max(1.0 - sinThetaSquared, 0.0));
}

void ReferencedPathtracingBuildDirectionalBasis(
    float3 directionWS,
    out float3 basisX,
    out float3 basisY)
{
    float signZ = directionWS.z >= 0.0 ? 1.0 : -1.0;
    float a = -rcp(signZ + directionWS.z);
    float b = directionWS.x * directionWS.y * a;
    basisX = float3(
        1.0 + signZ * directionWS.x * directionWS.x * a,
        signZ * b,
        -signZ * directionWS.x);
    basisY = float3(
        b,
        signZ + directionWS.y * directionWS.y * a,
        -directionWS.y);
}

bool ReferencedPathtracingGetDirectionalLightSolidAnglePdf(
    float angularDiameter,
    out float cosThetaMax,
    out float lightPdf)
{
    cosThetaMax = 1.0;
    lightPdf = 0.0;

    float halfAngularDiameter = 0.5
        * clamp(
            angularDiameter,
            0.0,
            0.5 * kReferencedPathtracingPi);
    float sinThetaMax = sin(halfAngularDiameter);
    float sinThetaMaxSquared = sinThetaMax * sinThetaMax;
    if (sinThetaMaxSquared <= 1e-12)
        return false;

    float oneMinusCosThetaMax =
        ReferencedPathtracingOneMinusCosFromSinSquared(
            sinThetaMaxSquared);
    float solidAngle =
        2.0 * kReferencedPathtracingPi * oneMinusCosThetaMax;
    if (solidAngle <= 0.0 || isnan(solidAngle) || isinf(solidAngle))
        return false;

    cosThetaMax = 1.0 - oneMinusCosThetaMax;
    lightPdf = rcp(solidAngle);
    return !isnan(lightPdf) && !isinf(lightPdf);
}

void ReferencedPathtracingSampleDirectionalLight(
    float3 centerDirectionWS,
    float angularDiameter,
    float2 randomSample,
    out float3 sampledDirectionWS,
    out float lightPdf,
    out uint isDelta)
{
    sampledDirectionWS = centerDirectionWS;
    lightPdf = 0.0;
    isDelta = 1u;

    float cosThetaMax;
    if (!ReferencedPathtracingGetDirectionalLightSolidAnglePdf(
            angularDiameter,
            cosThetaMax,
            lightPdf))
    {
        return;
    }

    float cosTheta = lerp(
        1.0,
        cosThetaMax,
        saturate(randomSample.y));
    float sinTheta = sqrt(max(1.0 - cosTheta * cosTheta, 0.0));
    float phi =
        2.0 * kReferencedPathtracingPi * saturate(randomSample.x);
    float sinPhi;
    float cosPhi;
    sincos(phi, sinPhi, cosPhi);

    float3 basisX;
    float3 basisY;
    ReferencedPathtracingBuildDirectionalBasis(
        centerDirectionWS,
        basisX,
        basisY);
    sampledDirectionWS = normalize(
        basisX * (sinTheta * cosPhi)
        + basisY * (sinTheta * sinPhi)
        + centerDirectionWS * cosTheta);
    isDelta = 0u;
}

bool ReferencedPathtracingEvaluateDirectionalLightPdf(
    float3 centerDirectionWS,
    float angularDiameter,
    float3 directionWS,
    out float lightPdf)
{
    float cosThetaMax;
    if (!ReferencedPathtracingGetDirectionalLightSolidAnglePdf(
            angularDiameter,
            cosThetaMax,
            lightPdf))
    {
        return false;
    }

    float directionLengthSquared = dot(directionWS, directionWS);
    float centerLengthSquared = dot(
        centerDirectionWS,
        centerDirectionWS);
    if (directionLengthSquared <= 1e-8
        || centerLengthSquared <= 1e-8
        || isnan(directionLengthSquared)
        || isinf(directionLengthSquared)
        || isnan(centerLengthSquared)
        || isinf(centerLengthSquared))
    {
        lightPdf = 0.0;
        return false;
    }

    float3 direction = directionWS * rsqrt(directionLengthSquared);
    float3 centerDirection = centerDirectionWS
        * rsqrt(centerLengthSquared);
    return dot(direction, centerDirection) >= cosThetaMax;
}

float ReferencedPathtracingGetEnvironmentLightEstimatorWeight(
    float lightPdf,
    float bsdfPdf)
{
    if (_ReferencedEnvironmentEstimatorMode
        == kReferencedEnvironmentEstimatorBsdfOnly)
    {
        return 0.0;
    }

    if (_ReferencedEnvironmentEstimatorMode
        == kReferencedEnvironmentEstimatorLightOnly)
    {
        return 1.0;
    }

    return ReferencedPathtracingPowerHeuristic(lightPdf, bsdfPdf);
}

float ReferencedPathtracingGetEnvironmentBsdfEstimatorWeight(
    float bsdfPdf,
    float lightPdf,
    bool sampledDeltaEvent)
{
    // Delta directions cannot be generated by the continuous environment proposal.
    if (sampledDeltaEvent)
        return 1.0;

    // BSDF-only sampling is the mandatory fallback when NEE has no support.
    if (_ReferencedEnvironmentNeeEnabled == 0
        || _ReferencedEnvironmentSamplingMode
            == kReferencedEnvironmentSamplingBsdfOnly
        || _ReferencedEnvironmentEstimatorMode
            == kReferencedEnvironmentEstimatorBsdfOnly)
    {
        return 1.0;
    }

    // A light proposal with zero density cannot compete for this direction.
    if (lightPdf <= 0.0 || isnan(lightPdf) || isinf(lightPdf))
        return 1.0;

    if (_ReferencedEnvironmentEstimatorMode
        == kReferencedEnvironmentEstimatorLightOnly)
    {
        return 0.0;
    }

    return ReferencedPathtracingPowerHeuristic(bsdfPdf, lightPdf);
}

float4 ReferencedPathtracingEvaluateCameraBackground(float3 directionWS)
{
    if (_ReferencedCameraSkyEnabled != 0
        && _ReferencedEnvironmentCameraVisible != 0
        && ReferencedPathtracingHasEnvironment())
    {
        return float4(
            ReferencedPathtracingSampleBackgroundEnvironment(directionWS),
            1.0);
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
    float3 neeDiffuseRadiance;
    float3 neeSpecularRadiance;
    float3 neeDirectionWS;
    float neeDistance;
    // Keep discrete and conditional PDFs separate for future proposal mixtures.
    float neeSelectionPdf;
    float neeSolidAnglePdf;
    float neeBsdfPdf;
    float neeShadowStrength;
    uint neeLightIndex;
    uint neeLightType;
    uint neeFlags;
    uint neeValid;
    float3 nextDirectionWS;
    float3 nextThroughputWeight;
    float nextPdf;
    float linearRoughness;
    float hitDistance;
    uint nextLobeClass;
    uint nextLobeIsDelta;
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
    payload.neeDiffuseRadiance = 0.0;
    payload.neeSpecularRadiance = 0.0;
    payload.neeDirectionWS = 0.0;
    payload.neeDistance = 0.0;
    payload.neeSelectionPdf = 0.0;
    payload.neeSolidAnglePdf = 0.0;
    payload.neeBsdfPdf = 0.0;
    payload.neeShadowStrength = 0.0;
    payload.neeLightIndex = 0xffffffffu;
    payload.neeLightType = 0u;
    payload.neeFlags = 0u;
    payload.neeValid = 0u;
    payload.nextDirectionWS = 0.0;
    payload.nextThroughputWeight = 0.0;
    payload.nextPdf = 0.0;
    payload.linearRoughness = 1.0;
    payload.hitDistance = 0.0;
    payload.nextLobeClass = 0u;
    payload.nextLobeIsDelta = 0u;
    payload.hit = 0u;
}

#endif
