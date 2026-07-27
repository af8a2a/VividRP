#ifndef VIVIDRP_REFERENCED_PATH_TRACING_CLOUD_INCLUDED
#define VIVIDRP_REFERENCED_PATH_TRACING_CLOUD_INCLUDED

#define REFERENCED_CLOUD_EVENT_NONE 0u
#define REFERENCED_CLOUD_EVENT_SCATTER 1u
#define REFERENCED_CLOUD_EVENT_ABSORB 2u
#define REFERENCED_CLOUD_EVENT_TRACKING_OVERFLOW 3u

static const uint kReferencedCloudMaximumTrackingSteps = 1024u;
static const float kReferencedCloudTrackingEpsilon = 1e-7;

struct ReferencedPathtracingCloudRayInterval
{
    float entryDistance;
    float exitDistance;
    uint intersectsCloud;
};

// PT-only cloud material boundary. A future authored/callable cloud material can
// replace this evaluator without changing the path-loop event contract.
struct ReferencedPathtracingCloudMaterialSample
{
    float density;
    float extinction;
    float3 scattering;
};

struct ReferencedPathtracingCloudMediumSample
{
    float3 positionWS;
    float distance;
    float3 scatteringRatio;
    float density;
    float boundaryDistance;
    uint heroChannel;
    uint eventType;
    uint trackingStepCount;
};

void ReferencedPathtracingInitializeCloudMediumSample(
    out ReferencedPathtracingCloudMediumSample cloudSample)
{
    cloudSample = (ReferencedPathtracingCloudMediumSample)0;
    cloudSample.scatteringRatio = 1.0;
    cloudSample.eventType = REFERENCED_CLOUD_EVENT_NONE;
}

bool ReferencedPathtracingHasCloudAcceleration()
{
    float valid =
        _ReferencedEnvironmentImportanceDistribution[
            REFERENCED_CLOUD_ACCELERATION_VALID_OFFSET];
    float version =
        _ReferencedEnvironmentImportanceDistribution[
            REFERENCED_CLOUD_ACCELERATION_VERSION_OFFSET];
    float resolution =
        _ReferencedEnvironmentImportanceDistribution[
            REFERENCED_CLOUD_ACCELERATION_RESOLUTION_OFFSET];
    return ReferencedPathtracingHasReferenceClouds()
        && valid > 0.5
        && abs(
            version
            - REFERENCED_CLOUD_ACCELERATION_VERSION) < 0.5
        && abs(
            resolution
            - REFERENCED_CLOUD_ACCELERATION_RADIAL_RESOLUTION) < 0.5;
}

float ReferencedPathtracingSampleCloudRadialMajorant(
    float normalizedHeight)
{
    if (!ReferencedPathtracingHasCloudAcceleration())
    {
        return ReferencedPathtracingEvaluateCloudHeightEnvelope(
            normalizedHeight);
    }

    float coordinate =
        saturate(normalizedHeight)
        * REFERENCED_CLOUD_ACCELERATION_RADIAL_RESOLUTION
        - 0.5;
    int lowerIndex = clamp(
        (int)floor(coordinate),
        0,
        (int)REFERENCED_CLOUD_ACCELERATION_RADIAL_RESOLUTION - 1);
    int upperIndex = min(
        lowerIndex + 1,
        (int)REFERENCED_CLOUD_ACCELERATION_RADIAL_RESOLUTION - 1);
    // Max instead of interpolation preserves the conservative per-cell bound.
    return max(
        _ReferencedEnvironmentImportanceDistribution[
            REFERENCED_CLOUD_ACCELERATION_DATA_OFFSET
            + (uint)lowerIndex],
        _ReferencedEnvironmentImportanceDistribution[
            REFERENCED_CLOUD_ACCELERATION_DATA_OFFSET
            + (uint)upperIndex]);
}

bool ReferencedPathtracingIntersectCloudShell(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float maximumDistance,
    out ReferencedPathtracingCloudRayInterval interval)
{
    interval = (ReferencedPathtracingCloudRayInterval)0;
    if (!ReferencedPathtracingHasReferenceClouds())
        return false;

    float directionLengthSquared =
        dot(rayDirectionWS, rayDirectionWS);
    if (directionLengthSquared <= 1e-12)
        return false;

    float3 direction =
        rayDirectionWS * rsqrt(directionLengthSquared);
    float3 originPS =
        rayOriginWS
        - _ReferencedAtmospherePlanetCenterBottomRadius.xyz;
    float radialDistance = length(originPS);
    float innerRadius = _ReferencedCloudLayerParameters.x;
    float outerRadius = _ReferencedCloudLayerParameters.y;
    float2 outerIntersection;
    if (!ReferencedPathtracingIntersectAtmosphereSphere(
            originPS,
            direction,
            outerRadius,
            outerIntersection)
        || outerIntersection.y < 0.0)
    {
        return false;
    }

    float entryDistance = max(outerIntersection.x, 0.0);
    float exitDistance = min(
        outerIntersection.y,
        max(maximumDistance, 0.0));
    float2 innerIntersection;
    bool intersectsInner =
        ReferencedPathtracingIntersectAtmosphereSphere(
            originPS,
            direction,
            innerRadius,
            innerIntersection);
    float boundaryTolerance = max(innerRadius * 1e-7, 0.01);
    if (radialDistance < innerRadius - boundaryTolerance)
    {
        if (!intersectsInner || innerIntersection.y < entryDistance)
            return false;
        entryDistance = max(entryDistance, innerIntersection.y);
    }
    else if (intersectsInner
        && innerIntersection.x > entryDistance
        && innerIntersection.x < exitDistance)
    {
        exitDistance = innerIntersection.x;
    }

    if (exitDistance <= entryDistance)
        return false;

    interval.entryDistance = entryDistance;
    interval.exitDistance = exitDistance;
    interval.intersectsCloud = 1u;
    return true;
}

uint ReferencedPathtracingCloudHash3(uint3 coordinate, uint seed)
{
    uint value = coordinate.x * 0x8da6b343u;
    value ^= coordinate.y * 0xd8163841u;
    value ^= coordinate.z * 0xcb1ab31fu;
    value ^= seed * 0x165667b1u;
    return ReferencedPathtracingHash(value);
}

float ReferencedPathtracingCloudValueNoise(
    float3 position,
    uint seed)
{
    int3 cell = (int3)floor(position);
    float3 interpolation = frac(position);
    interpolation =
        interpolation
        * interpolation
        * interpolation
        * (interpolation * (interpolation * 6.0 - 15.0) + 10.0);

    float corners[8];
    [unroll]
    for (uint cornerIndex = 0u; cornerIndex < 8u; ++cornerIndex)
    {
        uint3 offset = uint3(
            cornerIndex & 1u,
            (cornerIndex >> 1u) & 1u,
            (cornerIndex >> 2u) & 1u);
        int3 corner = cell + (int3)offset;
        corners[cornerIndex] =
            ReferencedPathtracingHashToUnitFloat(
                ReferencedPathtracingCloudHash3(
                    asuint(corner),
                    seed));
    }

    float lowerY = lerp(
        lerp(corners[0], corners[1], interpolation.x),
        lerp(corners[2], corners[3], interpolation.x),
        interpolation.y);
    float upperY = lerp(
        lerp(corners[4], corners[5], interpolation.x),
        lerp(corners[6], corners[7], interpolation.x),
        interpolation.y);
    return lerp(lowerY, upperY, interpolation.z);
}

float ReferencedPathtracingEvaluateCloudNoise(float3 positionPS)
{
    float noiseScale = max(_ReferencedCloudNoiseParameters.x, 1.0);
    uint seed = (uint)max(_ReferencedCloudNoiseParameters.y, 0.0);
    float3 coordinate =
        positionPS / noiseScale
        + float3(17.0, 43.0, 71.0);
    float noise = 0.0;
    float weight = 0.5333333;
    [unroll]
    for (uint octave = 0u; octave < 4u; ++octave)
    {
        noise +=
            weight
            * ReferencedPathtracingCloudValueNoise(
                coordinate,
                seed + octave * 1013u);
        coordinate =
            coordinate * 2.031
            + float3(11.0, 7.0, 5.0);
        weight *= 0.5;
    }
    return saturate(noise);
}

void ReferencedPathtracingEvaluateCloudMaterial(
    float3 positionWS,
    out ReferencedPathtracingCloudMaterialSample materialSample)
{
    materialSample = (ReferencedPathtracingCloudMaterialSample)0;
    if (!ReferencedPathtracingHasReferenceClouds())
        return;

    float3 positionPS =
        positionWS
        - _ReferencedAtmospherePlanetCenterBottomRadius.xyz;
    float radialDistance = length(positionPS);
    float layerThickness = max(
        _ReferencedCloudLayerParameters.y
        - _ReferencedCloudLayerParameters.x,
        1.0);
    float normalizedHeight =
        (radialDistance - _ReferencedCloudLayerParameters.x)
        / layerThickness;
    if (normalizedHeight <= 0.0 || normalizedHeight >= 1.0)
        return;

    float radialMajorant =
        ReferencedPathtracingSampleCloudRadialMajorant(
            normalizedHeight);
    if (radialMajorant <= 0.0)
        return;

    float coverage = saturate(_ReferencedCloudLayerParameters.z);
    float noise = ReferencedPathtracingEvaluateCloudNoise(positionPS);
    float coverageDensity = saturate(
        (noise - (1.0 - coverage))
        / max(coverage, 1e-4));
    float heightEnvelope =
        ReferencedPathtracingEvaluateCloudHeightEnvelope(
            normalizedHeight);
    materialSample.density =
        saturate(coverageDensity * heightEnvelope);
    materialSample.extinction =
        materialSample.density
        * max(_ReferencedCloudLayerParameters.w, 0.0);
    materialSample.scattering =
        materialSample.extinction
        * saturate(_ReferencedCloudMaterialParameters.rgb);
}

float ReferencedPathtracingGetCloudTrackingRandom(
    float4 randomValue,
    uint trackingStep,
    uint stream)
{
    uint randomBits =
        asuint(randomValue.x)
        ^ ReferencedPathtracingHash(
            asuint(randomValue.y) + 0x9e3779b9u)
        ^ ReferencedPathtracingHash(
            asuint(randomValue.z) + 0x85ebca6bu)
        ^ ReferencedPathtracingHash(
            asuint(randomValue.w) + 0xc2b2ae35u)
        ^ ReferencedPathtracingHash(
            trackingStep * 0x27d4eb2du
            + stream * 0x165667b1u);
    return min(
        ReferencedPathtracingHashToUnitFloat(randomBits),
        0.99999994);
}

bool ReferencedPathtracingSampleCloudMedium(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float maximumDistance,
    float4 randomValue,
    out ReferencedPathtracingCloudMediumSample cloudSample)
{
    ReferencedPathtracingInitializeCloudMediumSample(cloudSample);
    ReferencedPathtracingCloudRayInterval interval;
    if (!ReferencedPathtracingIntersectCloudShell(
            rayOriginWS,
            rayDirectionWS,
            maximumDistance,
            interval))
    {
        return false;
    }

    cloudSample.boundaryDistance = interval.exitDistance;
    cloudSample.heroChannel = min(
        (uint)(saturate(randomValue.x) * 3.0),
        2u);
    float extinctionMajorant =
        max(_ReferencedCloudLayerParameters.w, 0.0);
    if (extinctionMajorant <= 1e-12)
        return true;

    float3 direction = normalize(rayDirectionWS);
    float candidateDistance = interval.entryDistance;
    [loop]
    for (uint trackingStep = 0u;
        trackingStep < kReferencedCloudMaximumTrackingSteps;
        ++trackingStep)
    {
        cloudSample.trackingStepCount = trackingStep + 1u;
        float freeFlightRandom =
            ReferencedPathtracingGetCloudTrackingRandom(
                randomValue,
                trackingStep,
                0u);
        candidateDistance +=
            -log(max(1.0 - freeFlightRandom, kReferencedCloudTrackingEpsilon))
            / extinctionMajorant;
        if (candidateDistance >= interval.exitDistance)
            return true;

        float3 candidatePositionWS =
            rayOriginWS + direction * candidateDistance;
        ReferencedPathtracingCloudMaterialSample materialSample;
        ReferencedPathtracingEvaluateCloudMaterial(
            candidatePositionWS,
            materialSample);
        float acceptanceProbability = saturate(
            materialSample.extinction
            / extinctionMajorant);
        float acceptanceRandom =
            ReferencedPathtracingGetCloudTrackingRandom(
                randomValue,
                trackingStep,
                1u);
        if (acceptanceRandom >= acceptanceProbability)
            continue;

        float heroAlbedo =
            ReferencedPathtracingGetAtmosphereChannel(
                saturate(_ReferencedCloudMaterialParameters.rgb),
                cloudSample.heroChannel);
        float scatteringRandom =
            ReferencedPathtracingGetCloudTrackingRandom(
                randomValue,
                trackingStep,
                2u);
        cloudSample.positionWS = candidatePositionWS;
        cloudSample.distance = candidateDistance;
        cloudSample.density = materialSample.density;
        if (scatteringRandom >= heroAlbedo
            || heroAlbedo <= 1e-20)
        {
            cloudSample.eventType = REFERENCED_CLOUD_EVENT_ABSORB;
            return true;
        }

        cloudSample.scatteringRatio =
            saturate(_ReferencedCloudMaterialParameters.rgb)
            / heroAlbedo;
        cloudSample.eventType = REFERENCED_CLOUD_EVENT_SCATTER;
        return true;
    }

    cloudSample.eventType =
        REFERENCED_CLOUD_EVENT_TRACKING_OVERFLOW;
    return true;
}

float ReferencedPathtracingEvaluateCloudPhase(float cosineTheta)
{
    cosineTheta = clamp(cosineTheta, -1.0, 1.0);
    float anisotropy = clamp(
        _ReferencedCloudMaterialParameters.w,
        -0.95,
        0.95);
    float anisotropySquared = anisotropy * anisotropy;
    float denominator = max(
        1.0 + anisotropySquared
            - 2.0 * anisotropy * cosineTheta,
        1e-6);
    return (1.0 - anisotropySquared)
        / (4.0
            * kReferencedPathtracingPi
            * denominator
            * sqrt(denominator));
}

float ReferencedPathtracingEvaluateCloudPhasePdf(
    float3 currentDirectionWS,
    float3 sampledDirectionWS)
{
    return ReferencedPathtracingEvaluateCloudPhase(
        dot(
            normalize(currentDirectionWS),
            normalize(sampledDirectionWS)));
}

float3 ReferencedPathtracingEvaluateCloudDirectWeight(
    ReferencedPathtracingCloudMediumSample cloudSample,
    float3 currentDirectionWS,
    float3 lightDirectionWS)
{
    float phase =
        ReferencedPathtracingEvaluateCloudPhasePdf(
            currentDirectionWS,
            lightDirectionWS);
    return cloudSample.scatteringRatio * phase;
}

bool ReferencedPathtracingSampleCloudPhase(
    ReferencedPathtracingCloudMediumSample cloudSample,
    float3 currentDirectionWS,
    float2 randomValue,
    out float3 sampledDirectionWS,
    out float3 throughputWeight,
    out float phasePdf)
{
    sampledDirectionWS = 0.0;
    throughputWeight = 0.0;
    phasePdf = 0.0;
    if (cloudSample.eventType != REFERENCED_CLOUD_EVENT_SCATTER)
        return false;

    float anisotropy = clamp(
        _ReferencedCloudMaterialParameters.w,
        -0.95,
        0.95);
    float cosineTheta;
    if (abs(anisotropy) < 1e-3)
    {
        cosineTheta = 1.0 - 2.0 * saturate(randomValue.x);
    }
    else
    {
        float numerator = 1.0 - anisotropy * anisotropy;
        float denominator = max(
            1.0 - anisotropy
                + 2.0 * anisotropy * saturate(randomValue.x),
            1e-6);
        float ratio = numerator / denominator;
        cosineTheta = clamp(
            (1.0 + anisotropy * anisotropy - ratio * ratio)
                / (2.0 * anisotropy),
            -1.0,
            1.0);
    }

    float sineTheta = sqrt(
        saturate(1.0 - cosineTheta * cosineTheta));
    float azimuth =
        2.0
        * kReferencedPathtracingPi
        * saturate(randomValue.y);
    float sineAzimuth;
    float cosineAzimuth;
    sincos(azimuth, sineAzimuth, cosineAzimuth);
    float3 forward = normalize(currentDirectionWS);
    float3 basisX;
    float3 basisY;
    ReferencedPathtracingBuildDirectionalBasis(
        forward,
        basisX,
        basisY);
    sampledDirectionWS = normalize(
        basisX * (sineTheta * cosineAzimuth)
        + basisY * (sineTheta * sineAzimuth)
        + forward * cosineTheta);
    phasePdf =
        ReferencedPathtracingEvaluateCloudPhase(cosineTheta);
    throughputWeight = cloudSample.scatteringRatio;
    return phasePdf > 0.0
        && !isnan(phasePdf)
        && !isinf(phasePdf)
        && !any(isnan(throughputWeight))
        && !any(isinf(throughputWeight));
}

float3 ReferencedPathtracingEvaluateCloudTransmittance(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float maximumDistance)
{
    ReferencedPathtracingCloudRayInterval interval;
    if (!ReferencedPathtracingIntersectCloudShell(
            rayOriginWS,
            rayDirectionWS,
            maximumDistance,
            interval))
    {
        return 1.0;
    }

    float3 direction = normalize(rayDirectionWS);
    float segmentLength =
        interval.exitDistance - interval.entryDistance;
    float stepLength =
        segmentLength
        / REFERENCED_CLOUD_SHADOW_REFERENCE_SAMPLE_COUNT;
    float opticalDepth = 0.0;
    [loop]
    for (uint sampleIndex = 0u;
        sampleIndex < REFERENCED_CLOUD_SHADOW_REFERENCE_SAMPLE_COUNT;
        ++sampleIndex)
    {
        float distance =
            interval.entryDistance
            + (sampleIndex + 0.5) * stepLength;
        ReferencedPathtracingCloudMaterialSample materialSample;
        ReferencedPathtracingEvaluateCloudMaterial(
            rayOriginWS + direction * distance,
            materialSample);
        opticalDepth += materialSample.extinction * stepLength;
    }
    float transmittance = exp(-min(max(opticalDepth, 0.0), 80.0));
    return transmittance.xxx;
}

float3 ReferencedPathtracingEvaluateCloudMultipleScatteringWeight(
    ReferencedPathtracingCloudMediumSample cloudSample)
{
    if ((int)_ReferencedCloudNoiseParameters.z == 0)
        return 1.0;

    float strength = max(_ReferencedCloudNoiseParameters.w, 0.0);
    return 1.0
        + strength
            * saturate(cloudSample.density)
            * saturate(_ReferencedCloudMaterialParameters.rgb);
}

#endif
