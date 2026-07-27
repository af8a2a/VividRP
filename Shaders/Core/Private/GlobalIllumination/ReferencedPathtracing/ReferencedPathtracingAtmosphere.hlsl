#ifndef VIVIDRP_REFERENCED_PATH_TRACING_ATMOSPHERE_INCLUDED
#define VIVIDRP_REFERENCED_PATH_TRACING_ATMOSPHERE_INCLUDED

#define REFERENCED_ATMOSPHERE_MEDIUM_EVENT_NONE 0u
#define REFERENCED_ATMOSPHERE_MEDIUM_EVENT_SCATTER 1u
#define REFERENCED_ATMOSPHERE_MEDIUM_EVENT_ABSORB 2u
#define REFERENCED_ATMOSPHERE_MEDIUM_EVENT_TRACKING_OVERFLOW 3u

static const uint kReferencedAtmosphereMaximumTrackingSteps = 1024u;
static const float kReferencedAtmosphereTrackingEpsilon = 1e-7;

struct ReferencedPathtracingAtmosphereMediumSample
{
    float3 positionWS;
    float distance;
    // For NONE this is the no-collision RGB/hero transmittance ratio.
    // For SCATTER it is the RGB/hero transmittance ratio to the event.
    float3 transmittanceRatio;
    float3 rayleighScattering;
    float heroScattering;
    float3 mieScattering;
    float boundaryDistance;
    uint heroChannel;
    uint eventType;
    uint hitsGround;
    uint trackingStepCount;
};

void ReferencedPathtracingInitializeAtmosphereMediumSample(
    out ReferencedPathtracingAtmosphereMediumSample mediumSample)
{
    mediumSample =
        (ReferencedPathtracingAtmosphereMediumSample)0;
    mediumSample.transmittanceRatio = 1.0;
    mediumSample.eventType =
        REFERENCED_ATMOSPHERE_MEDIUM_EVENT_NONE;
}

float ReferencedPathtracingGetAtmosphereChannel(
    float3 value,
    uint channel)
{
    return channel == 0u
        ? value.x
        : (channel == 1u ? value.y : value.z);
}

float ReferencedPathtracingGetAtmosphereTrackingRandom(
    float4 randomValue,
    uint trackingStep,
    uint stream)
{
    uint randomBits =
        asuint(randomValue.y)
        ^ ReferencedPathtracingHash(
            asuint(randomValue.x) + 0x9e3779b9u)
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

void ReferencedPathtracingEvaluateAtmosphereScatteringComponents(
    float radialDistance,
    out float3 rayleighScattering,
    out float3 mieScattering,
    out float3 extinction)
{
    float3 density =
        ReferencedPathtracingEvaluateAtmosphereDensity(
            radialDistance);
    extinction =
        ReferencedPathtracingEvaluateAtmosphereExtinction(
            radialDistance);
    rayleighScattering =
        density.x
        * max(_ReferencedAtmosphereRayleighScattering.rgb, 0.0);
    mieScattering =
        density.y
        * max(_ReferencedAtmosphereMieScattering.rgb, 0.0);

    // Keep malformed authoring data energy safe. Physical profiles already
    // satisfy scattering <= extinction, so this does not alter valid inputs.
    float3 totalScattering =
        rayleighScattering + mieScattering;
    float3 scatteringScale = min(
        extinction / max(totalScattering, 1e-20),
        1.0);
    rayleighScattering *= scatteringScale;
    mieScattering *= scatteringScale;
}

float3 ReferencedPathtracingGetAtmosphereTransmittanceRatio(
    float3 transmittance,
    uint heroChannel)
{
    float heroTransmittance = max(
        ReferencedPathtracingGetAtmosphereChannel(
            transmittance,
            heroChannel),
        1e-30);
    float3 ratio =
        max(transmittance, 0.0) / heroTransmittance;
    return !any(isnan(ratio)) && !any(isinf(ratio))
        ? max(ratio, 0.0)
        : 0.0;
}

bool ReferencedPathtracingSampleAtmosphereMedium(
    float3 rayOriginWS,
    float3 rayDirectionWS,
    float maximumDistance,
    float4 randomValue,
    out ReferencedPathtracingAtmosphereMediumSample mediumSample)
{
    ReferencedPathtracingInitializeAtmosphereMediumSample(
        mediumSample);
    if (!ReferencedPathtracingHasReferenceAtmosphere())
        return false;

    float directionLengthSquared =
        dot(rayDirectionWS, rayDirectionWS);
    if (directionLengthSquared <= 1e-12)
        return false;

    float3 direction =
        rayDirectionWS * rsqrt(directionLengthSquared);
    ReferencedPathtracingAtmosphereRayInterval interval;
    if (!ReferencedPathtracingIntersectAtmosphere(
            rayOriginWS,
            direction,
            maximumDistance,
            interval))
    {
        return false;
    }

    mediumSample.boundaryDistance = interval.exitDistance;
    mediumSample.hitsGround = interval.hitsGround;
    mediumSample.heroChannel = min(
        (uint)(saturate(randomValue.x) * 3.0),
        2u);

    float3 seaLevelMajorant =
        max(_ReferencedAtmosphereRayleighExtinction.rgb, 0.0)
        + max(_ReferencedAtmosphereMieExtinction.rgb, 0.0)
        + max(_ReferencedAtmosphereOzoneExtinction.rgb, 0.0);
    float heroMajorant =
        ReferencedPathtracingGetAtmosphereChannel(
            seaLevelMajorant,
            mediumSample.heroChannel);
    if (heroMajorant <= 1e-12)
    {
        float3 transmittance =
            ReferencedPathtracingEvaluateAtmosphereTransmittance(
                rayOriginWS,
                direction,
                interval.exitDistance);
        mediumSample.transmittanceRatio =
            ReferencedPathtracingGetAtmosphereTransmittanceRatio(
                transmittance,
                mediumSample.heroChannel);
        return true;
    }

    float candidateDistance = interval.entryDistance;
    [loop]
    for (uint trackingStep = 0u;
        trackingStep < kReferencedAtmosphereMaximumTrackingSteps;
        ++trackingStep)
    {
        mediumSample.trackingStepCount = trackingStep + 1u;
        float freeFlightRandom =
            ReferencedPathtracingGetAtmosphereTrackingRandom(
                randomValue,
                trackingStep,
                0u);
        candidateDistance +=
            -log(max(1.0 - freeFlightRandom, 1e-7))
            / heroMajorant;
        if (candidateDistance >= interval.exitDistance)
        {
            float3 transmittance =
                ReferencedPathtracingEvaluateAtmosphereTransmittance(
                    rayOriginWS,
                    direction,
                    interval.exitDistance);
            mediumSample.transmittanceRatio =
                ReferencedPathtracingGetAtmosphereTransmittanceRatio(
                    transmittance,
                    mediumSample.heroChannel);
            return true;
        }

        float3 candidatePositionWS =
            rayOriginWS + direction * candidateDistance;
        float radialDistance = length(
            candidatePositionWS
            - _ReferencedAtmospherePlanetCenterBottomRadius.xyz);
        float3 rayleighScattering;
        float3 mieScattering;
        float3 extinction;
        ReferencedPathtracingEvaluateAtmosphereScatteringComponents(
            radialDistance,
            rayleighScattering,
            mieScattering,
            extinction);
        float heroExtinction =
            ReferencedPathtracingGetAtmosphereChannel(
                extinction,
                mediumSample.heroChannel);
        float acceptanceProbability = saturate(
            heroExtinction / heroMajorant);
        float acceptanceRandom =
            ReferencedPathtracingGetAtmosphereTrackingRandom(
                randomValue,
                trackingStep,
                1u);
        if (acceptanceRandom >= acceptanceProbability)
            continue;

        float3 totalScattering =
            rayleighScattering + mieScattering;
        float heroScattering =
            ReferencedPathtracingGetAtmosphereChannel(
                totalScattering,
                mediumSample.heroChannel);
        float scatteringProbability =
            heroExtinction > 0.0
                ? saturate(heroScattering / heroExtinction)
                : 0.0;
        float scatteringRandom =
            ReferencedPathtracingGetAtmosphereTrackingRandom(
                randomValue,
                trackingStep,
                2u);
        mediumSample.positionWS = candidatePositionWS;
        mediumSample.distance = candidateDistance;
        if (scatteringRandom >= scatteringProbability
            || heroScattering <= 1e-20)
        {
            mediumSample.eventType =
                REFERENCED_ATMOSPHERE_MEDIUM_EVENT_ABSORB;
            return true;
        }

        float3 transmittance =
            ReferencedPathtracingEvaluateAtmosphereTransmittance(
                rayOriginWS,
                direction,
                candidateDistance);
        mediumSample.transmittanceRatio =
            ReferencedPathtracingGetAtmosphereTransmittanceRatio(
                transmittance,
                mediumSample.heroChannel);
        mediumSample.rayleighScattering =
            rayleighScattering;
        mediumSample.mieScattering = mieScattering;
        mediumSample.heroScattering = heroScattering;
        mediumSample.eventType =
            REFERENCED_ATMOSPHERE_MEDIUM_EVENT_SCATTER;
        return true;
    }

    mediumSample.eventType =
        REFERENCED_ATMOSPHERE_MEDIUM_EVENT_TRACKING_OVERFLOW;
    return true;
}

float ReferencedPathtracingEvaluateRayleighPhase(
    float cosineTheta)
{
    cosineTheta = clamp(cosineTheta, -1.0, 1.0);
    return (3.0 / (16.0 * kReferencedPathtracingPi))
        * (1.0 + cosineTheta * cosineTheta);
}

float ReferencedPathtracingEvaluateMiePhase(
    float cosineTheta)
{
    cosineTheta = clamp(cosineTheta, -1.0, 1.0);
    float anisotropy = clamp(
        _ReferencedAtmosphereTopRadiusMieAnisotropy.y,
        -0.99,
        0.99);
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

float ReferencedPathtracingSignedCubeRoot(float value)
{
    return value >= 0.0
        ? pow(value, 1.0 / 3.0)
        : -pow(-value, 1.0 / 3.0);
}

float ReferencedPathtracingSampleRayleighCosine(
    float randomValue)
{
    float cubicOffset =
        4.0 * saturate(randomValue) - 2.0;
    float discriminantRoot =
        sqrt(cubicOffset * cubicOffset + 1.0);
    return clamp(
        ReferencedPathtracingSignedCubeRoot(
            cubicOffset + discriminantRoot)
        + ReferencedPathtracingSignedCubeRoot(
            cubicOffset - discriminantRoot),
        -1.0,
        1.0);
}

float ReferencedPathtracingSampleMieCosine(
    float randomValue)
{
    float anisotropy = clamp(
        _ReferencedAtmosphereTopRadiusMieAnisotropy.y,
        -0.99,
        0.99);
    if (abs(anisotropy) < 1e-3)
        return 1.0 - 2.0 * saturate(randomValue);

    float numerator =
        1.0 - anisotropy * anisotropy;
    float denominator = max(
        1.0 - anisotropy
            + 2.0 * anisotropy * saturate(randomValue),
        1e-6);
    float ratio = numerator / denominator;
    return clamp(
        (1.0 + anisotropy * anisotropy - ratio * ratio)
            / (2.0 * anisotropy),
        -1.0,
        1.0);
}

float3 ReferencedPathtracingEvaluateAtmospherePhaseScattering(
    ReferencedPathtracingAtmosphereMediumSample mediumSample,
    float cosineTheta)
{
    return mediumSample.rayleighScattering
            * ReferencedPathtracingEvaluateRayleighPhase(
                cosineTheta)
        + mediumSample.mieScattering
            * ReferencedPathtracingEvaluateMiePhase(
                cosineTheta);
}

float3 ReferencedPathtracingEvaluateAtmosphereDirectWeight(
    ReferencedPathtracingAtmosphereMediumSample mediumSample,
    float3 currentDirectionWS,
    float3 lightDirectionWS)
{
    float cosineTheta = dot(
        normalize(currentDirectionWS),
        normalize(lightDirectionWS));
    float3 phaseScattering =
        ReferencedPathtracingEvaluateAtmospherePhaseScattering(
            mediumSample,
            cosineTheta);
    return mediumSample.transmittanceRatio
        * phaseScattering
        / max(mediumSample.heroScattering, 1e-20);
}

bool ReferencedPathtracingSampleAtmospherePhase(
    ReferencedPathtracingAtmosphereMediumSample mediumSample,
    float3 currentDirectionWS,
    float2 randomValue,
    out float3 sampledDirectionWS,
    out float3 throughputWeight,
    out float phasePdf)
{
    sampledDirectionWS = 0.0;
    throughputWeight = 0.0;
    phasePdf = 0.0;
    if (mediumSample.eventType
            != REFERENCED_ATMOSPHERE_MEDIUM_EVENT_SCATTER
        || mediumSample.heroScattering <= 1e-20)
    {
        return false;
    }

    float heroRayleigh =
        ReferencedPathtracingGetAtmosphereChannel(
            mediumSample.rayleighScattering,
            mediumSample.heroChannel);
    float rayleighProbability = saturate(
        heroRayleigh / mediumSample.heroScattering);
    float lobeRandom = min(
        saturate(randomValue.x),
        0.99999994);
    float cosineTheta;
    if (lobeRandom < rayleighProbability
        && rayleighProbability > 0.0)
    {
        cosineTheta =
            ReferencedPathtracingSampleRayleighCosine(
                lobeRandom / rayleighProbability);
    }
    else
    {
        float mieProbability =
            max(1.0 - rayleighProbability, 1e-8);
        cosineTheta =
            ReferencedPathtracingSampleMieCosine(
                (lobeRandom - rayleighProbability)
                / mieProbability);
    }

    float sineTheta = sqrt(
        saturate(1.0 - cosineTheta * cosineTheta));
    float phi =
        2.0
        * kReferencedPathtracingPi
        * saturate(randomValue.y);
    float sinePhi;
    float cosinePhi;
    sincos(phi, sinePhi, cosinePhi);
    float3 forward = normalize(currentDirectionWS);
    float3 basisX;
    float3 basisY;
    ReferencedPathtracingBuildDirectionalBasis(
        forward,
        basisX,
        basisY);
    sampledDirectionWS = normalize(
        basisX * (sineTheta * cosinePhi)
        + basisY * (sineTheta * sinePhi)
        + forward * cosineTheta);

    float3 phaseScattering =
        ReferencedPathtracingEvaluateAtmospherePhaseScattering(
            mediumSample,
            cosineTheta);
    float heroPhaseScattering =
        ReferencedPathtracingGetAtmosphereChannel(
            phaseScattering,
            mediumSample.heroChannel);
    if (heroPhaseScattering <= 1e-20)
        return false;

    phasePdf =
        heroPhaseScattering
        / mediumSample.heroScattering;
    throughputWeight =
        mediumSample.transmittanceRatio
        * phaseScattering
        / heroPhaseScattering;
    return phasePdf > 0.0
        && !isnan(phasePdf)
        && !isinf(phasePdf)
        && !any(isnan(throughputWeight))
        && !any(isinf(throughputWeight))
        && any(throughputWeight > 0.0);
}

bool ReferencedPathtracingHasAtmosphereSun()
{
    return ReferencedPathtracingHasReferenceAtmosphere()
        && (_ReferencedAtmosphereFlags
            & kReferencedAtmosphereFlagLightingEnabled) != 0
        && _ReferencedAtmosphereHasSun != 0
        && dot(
            _ReferencedAtmosphereSunDirection.xyz,
            _ReferencedAtmosphereSunDirection.xyz) > 1e-8
        && any(_ReferencedAtmosphereSunIlluminance.rgb > 0.0);
}

#endif
