#ifndef VIVIDRP_REFERENCED_PATH_TRACING_LIGHT_LIST_INCLUDED
#define VIVIDRP_REFERENCED_PATH_TRACING_LIGHT_LIST_INCLUDED

#define REFERENCED_LIGHT_LIST_VERSION 1u
#define REFERENCED_LIGHT_DISTRIBUTION_CDF 1u

#define REFERENCED_LIGHT_TYPE_INVALID 0u
#define REFERENCED_LIGHT_TYPE_DIRECTIONAL 1u
#define REFERENCED_LIGHT_TYPE_POINT 2u
#define REFERENCED_LIGHT_TYPE_SPOT 3u
#define REFERENCED_LIGHT_TYPE_RECTANGLE 4u
#define REFERENCED_LIGHT_TYPE_TUBE 5u
#define REFERENCED_LIGHT_TYPE_DISC 6u
#define REFERENCED_LIGHT_TYPE_ENVIRONMENT 7u
#define REFERENCED_LIGHT_TYPE_EMISSIVE_TRIANGLE 8u

#define REFERENCED_LIGHT_FLAG_SINGULAR (1u << 0)
#define REFERENCED_LIGHT_FLAG_INFINITE (1u << 1)
#define REFERENCED_LIGHT_FLAG_BSDF_REACHABLE (1u << 2)
#define REFERENCED_LIGHT_FLAG_ONE_SIDED (1u << 3)
#define REFERENCED_LIGHT_FLAG_CASTS_SHADOWS (1u << 4)
#define REFERENCED_LIGHT_FLAG_HAS_STABLE_ID (1u << 5)
#define REFERENCED_LIGHT_FLAG_USES_AREA_MEASURE (1u << 6)
#define REFERENCED_LIGHT_FLAG_USES_LINE_MEASURE (1u << 7)

// CPU ABI: ReferencedPathTracingLightRecord, 144 bytes.
struct ReferencedPathTracingLightRecord
{
    float3 positionWS;
    float range;

    float3 forwardWS;
    float angularDiameter;

    float3 rightWS;
    float shapeRadius;

    float3 upWS;
    float barnDoorCosAngle;

    float3 radiometricColor;
    float selectionWeight;

    float2 areaSize;
    float2 spotAngleParameters;

    float2 rangeAttenuation;
    float barnDoorLength;
    float shadowStrength;

    float selectionPdf;
    float cdf;
    uint renderingLayerMask;
    uint shadowRenderingLayerMask;

    uint stableIdLow;
    uint stableIdHigh;
    uint lightType;
    uint flags;
};

// CPU ABI: ReferencedPathTracingLightListParameters, 48 bytes.
struct ReferencedPathTracingLightListParameters
{
    uint lightCount;
    uint activeLightCount;
    uint unsupportedLightCount;
    uint unstableLightCount;

    float totalSelectionWeight;
    float inverseTotalSelectionWeight;
    uint signatureLow;
    uint signatureHigh;

    uint version;
    uint distributionMode;
    uint reserved0;
    uint reserved1;
};

StructuredBuffer<ReferencedPathTracingLightRecord> _ReferencedLightList;
StructuredBuffer<ReferencedPathTracingLightListParameters>
    _ReferencedLightListParameters;

bool ReferencedPathtracingHasReferenceLights()
{
    ReferencedPathTracingLightListParameters parameters =
        _ReferencedLightListParameters[0];
    return parameters.version == REFERENCED_LIGHT_LIST_VERSION
        && parameters.distributionMode == REFERENCED_LIGHT_DISTRIBUTION_CDF
        && parameters.activeLightCount > 0u
        && parameters.totalSelectionWeight > 0.0;
}

ReferencedPathTracingLightRecord ReferencedPathtracingLoadReferenceLight(
    uint lightIndex)
{
    return _ReferencedLightList[lightIndex];
}

bool ReferencedPathtracingSampleReferenceLightIndex(
    float sample,
    out uint lightIndex,
    out float selectionPdf)
{
    lightIndex = 0u;
    selectionPdf = 0.0;

    ReferencedPathTracingLightListParameters parameters =
        _ReferencedLightListParameters[0];
    if (parameters.version != REFERENCED_LIGHT_LIST_VERSION
        || parameters.distributionMode != REFERENCED_LIGHT_DISTRIBUTION_CDF
        || parameters.lightCount == 0u
        || parameters.activeLightCount == 0u
        || parameters.totalSelectionWeight <= 0.0)
    {
        return false;
    }

    float target = min(saturate(sample), 0.99999994);
    uint lower = 0u;
    uint upper = parameters.lightCount;
    while (lower < upper)
    {
        uint middle = lower + ((upper - lower) >> 1u);
        if (target < _ReferencedLightList[middle].cdf)
            upper = middle;
        else
            lower = middle + 1u;
    }

    if (lower >= parameters.lightCount)
        return false;

    ReferencedPathTracingLightRecord light = _ReferencedLightList[lower];
    if (light.selectionPdf <= 0.0)
        return false;

    lightIndex = lower;
    selectionPdf = light.selectionPdf;
    return true;
}

struct ReferencedPathtracingLightSelectionContext
{
    float3 positionWS;
    float globalAnalyticWeight;
    float3 normalWS;
    float globalEnvironmentWeight;
    float localAnalyticWeight;
    float localEnvironmentWeight;
    float globalTotalWeight;
    float localTotalWeight;
    float globalProposalProbability;
};

bool ReferencedPathtracingIsReferenceLightNeeEligible(
    ReferencedPathTracingLightRecord light)
{
    bool localLightAllowed =
        _ReferencedLocalLightNeeEnabled != 0
        || light.lightType == REFERENCED_LIGHT_TYPE_DIRECTIONAL;
    bool bsdfReachable =
        (light.flags & REFERENCED_LIGHT_FLAG_BSDF_REACHABLE) != 0u;
    return localLightAllowed
        && ReferencedPathtracingIsLightNeeEligible(bsdfReachable);
}

float ReferencedPathtracingGetReferenceLightSelectionWeight()
{
    ReferencedPathTracingLightListParameters parameters =
        _ReferencedLightListParameters[0];
    if (parameters.version != REFERENCED_LIGHT_LIST_VERSION
        || parameters.distributionMode != REFERENCED_LIGHT_DISTRIBUTION_CDF
        || parameters.activeLightCount == 0u
        || parameters.totalSelectionWeight <= 0.0
        || isnan(parameters.totalSelectionWeight)
        || isinf(parameters.totalSelectionWeight))
    {
        return 0.0;
    }

    if (_ReferencedLocalLightNeeEnabled != 0
        && _ReferencedTransportEstimatorMode
            != kReferencedTransportEstimatorBsdfOnly)
    {
        return parameters.totalSelectionWeight;
    }

    float eligibleSelectionWeight = 0.0;
    for (uint lightIndex = 0u;
         lightIndex < parameters.lightCount;
         ++lightIndex)
    {
        ReferencedPathTracingLightRecord light =
            ReferencedPathtracingLoadReferenceLight(lightIndex);
        if (ReferencedPathtracingIsReferenceLightNeeEligible(light))
            eligibleSelectionWeight += max(light.selectionWeight, 0.0);
    }

    return eligibleSelectionWeight;
}

float ReferencedPathtracingGetEnvironmentSelectionWeight()
{
    if (_ReferencedEnvironmentNeeEnabled == 0
        || _ReferencedEnvironmentLightingEnabled == 0
        || !ReferencedPathtracingIsLightNeeEligible(true)
        || _ReferencedEnvironmentSamplingMode
            == kReferencedEnvironmentSamplingBsdfOnly
        || !ReferencedPathtracingHasEnvironment()
        || !ReferencedPathtracingHasEnvironmentDistributionEnergy())
    {
        return 0.0;
    }

    float averageLuminance =
        _ReferencedEnvironmentImportanceDistribution[
            REFERENCED_ENVIRONMENT_AVERAGE_LUMINANCE_OFFSET];
    float selectionWeight =
        4.0 * kReferencedPathtracingPi * max(averageLuminance, 0.0);
    return !isnan(selectionWeight) && !isinf(selectionWeight)
        ? selectionWeight
        : 0.0;
}

float ReferencedPathtracingEvaluateLocalRangeWindow(
    float distanceSquared,
    float2 rangeAttenuation)
{
    float scaledDistanceSquared =
        distanceSquared * max(rangeAttenuation.x, 0.0);
    float window = saturate(
        max(rangeAttenuation.y, 0.0)
        - scaledDistanceSquared * scaledDistanceSquared);
    return window * window;
}

float ReferencedPathtracingGetLocalReferenceLightWeight(
    ReferencedPathtracingLightSelectionContext context,
    ReferencedPathTracingLightRecord light)
{
    float baseWeight = max(light.selectionWeight, 0.0);
    if (baseWeight <= 0.0
        || !ReferencedPathtracingIsReferenceLightNeeEligible(light))
    {
        return 0.0;
    }

    float normalLengthSquared = dot(context.normalWS, context.normalWS);
    bool hasNormal = normalLengthSquared > 1e-8
        && !isnan(normalLengthSquared)
        && !isinf(normalLengthSquared);
    float3 normalWS = hasNormal
        ? context.normalWS * rsqrt(normalLengthSquared)
        : 0.0;

    float importance = 1.0;
    if (light.lightType == REFERENCED_LIGHT_TYPE_DIRECTIONAL)
    {
        float directionLengthSquared = dot(light.forwardWS, light.forwardWS);
        if (directionLengthSquared <= 1e-8)
            return 0.0;
        float3 directionToLight =
            -light.forwardWS * rsqrt(directionLengthSquared);
        importance = hasNormal
            ? saturate(dot(normalWS, directionToLight))
            : 1.0;
    }
    else
    {
        float3 surfaceToLight = light.positionWS - context.positionWS;
        float distanceSquared = dot(surfaceToLight, surfaceToLight);
        if (distanceSquared <= 1e-8
            || isnan(distanceSquared)
            || isinf(distanceSquared))
        {
            return 0.0;
        }

        float3 directionToLight =
            surfaceToLight * rsqrt(distanceSquared);
        float surfaceCosine = hasNormal
            ? saturate(dot(normalWS, directionToLight))
            : 1.0;
        float rangeWindow =
            ReferencedPathtracingEvaluateLocalRangeWindow(
                distanceSquared,
                light.rangeAttenuation);
        float distanceFactor = rcp(max(
            distanceSquared
                + max(light.shapeRadius * light.shapeRadius, 0.0),
            1e-4));
        importance = surfaceCosine * rangeWindow * distanceFactor;

        if (light.lightType == REFERENCED_LIGHT_TYPE_SPOT)
        {
            float forwardLengthSquared =
                dot(light.forwardWS, light.forwardWS);
            if (forwardLengthSquared <= 1e-8)
                return 0.0;
            float3 forwardWS =
                light.forwardWS * rsqrt(forwardLengthSquared);
            float spotAttenuation = saturate(
                dot(forwardWS, -directionToLight)
                    * light.spotAngleParameters.x
                + light.spotAngleParameters.y);
            importance *= spotAttenuation * spotAttenuation;
        }
        else if ((light.flags & REFERENCED_LIGHT_FLAG_ONE_SIDED) != 0u)
        {
            float forwardLengthSquared =
                dot(light.forwardWS, light.forwardWS);
            if (forwardLengthSquared <= 1e-8)
                return 0.0;
            float3 forwardWS =
                light.forwardWS * rsqrt(forwardLengthSquared);
            importance *= saturate(dot(-directionToLight, forwardWS));
        }
        else if (light.lightType == REFERENCED_LIGHT_TYPE_TUBE)
        {
            float rightLengthSquared = dot(light.rightWS, light.rightWS);
            if (rightLengthSquared <= 1e-8)
                return 0.0;
            float3 rightWS = light.rightWS * rsqrt(rightLengthSquared);
            float axialCosine = abs(dot(directionToLight, rightWS));
            importance *= sqrt(saturate(
                1.0 - axialCosine * axialCosine));
        }
    }

    float localWeight = baseWeight * max(importance, 0.0);
    return !isnan(localWeight) && !isinf(localWeight)
        ? min(localWeight, 1e30)
        : 0.0;
}

ReferencedPathtracingLightSelectionContext
ReferencedPathtracingCreateLightSelectionContext(
    float3 positionWS,
    float3 normalWS)
{
    ReferencedPathtracingLightSelectionContext context =
        (ReferencedPathtracingLightSelectionContext)0;
    context.positionWS = positionWS;
    context.normalWS = normalWS;
    context.globalAnalyticWeight =
        ReferencedPathtracingGetReferenceLightSelectionWeight();
    context.globalEnvironmentWeight =
        ReferencedPathtracingGetEnvironmentSelectionWeight();
    context.globalTotalWeight =
        context.globalAnalyticWeight
        + context.globalEnvironmentWeight;

    if (_ReferencedShadingPointLightSelectionEnabled != 0
        && context.globalTotalWeight > 0.0)
    {
        ReferencedPathTracingLightListParameters parameters =
            _ReferencedLightListParameters[0];
        for (uint lightIndex = 0u;
             lightIndex < parameters.lightCount;
             ++lightIndex)
        {
            ReferencedPathTracingLightRecord light =
                ReferencedPathtracingLoadReferenceLight(lightIndex);
            context.localAnalyticWeight +=
                ReferencedPathtracingGetLocalReferenceLightWeight(
                    context,
                    light);
        }

        // Environment remains present in the local proposal. Its directional
        // conditional distribution already carries the HDRI importance.
        context.localEnvironmentWeight =
            context.globalEnvironmentWeight;
        context.localTotalWeight =
            context.localAnalyticWeight
            + context.localEnvironmentWeight;
    }

    context.globalProposalProbability = 1.0;
    if (context.globalTotalWeight <= 0.0
        && context.localTotalWeight > 0.0)
    {
        context.globalProposalProbability = 0.0;
    }
    else if (context.globalTotalWeight > 0.0
        && context.localTotalWeight > 0.0
        && _ReferencedShadingPointLightSelectionEnabled != 0)
    {
        float configuredProbability =
            _ReferencedGlobalLightProposalProbability;
        if (isnan(configuredProbability)
            || isinf(configuredProbability))
        {
            configuredProbability = 0.25;
        }
        context.globalProposalProbability =
            clamp(configuredProbability, 0.05, 1.0);
    }

    return context;
}

float ReferencedPathtracingEvaluateMixtureSelectionPdf(
    ReferencedPathtracingLightSelectionContext context,
    float globalWeight,
    float localWeight)
{
    float globalPdf = context.globalTotalWeight > 0.0
        ? max(globalWeight, 0.0) / context.globalTotalWeight
        : 0.0;
    float localPdf = context.localTotalWeight > 0.0
        ? max(localWeight, 0.0) / context.localTotalWeight
        : 0.0;
    float selectionPdf =
        context.globalProposalProbability * globalPdf
        + (1.0 - context.globalProposalProbability) * localPdf;
    return !isnan(selectionPdf) && !isinf(selectionPdf)
        ? max(selectionPdf, 0.0)
        : 0.0;
}

float ReferencedPathtracingGetUnifiedReferenceLightSelectionPdf(
    ReferencedPathtracingLightSelectionContext context,
    ReferencedPathTracingLightRecord light)
{
    if (!ReferencedPathtracingIsReferenceLightNeeEligible(light))
        return 0.0;

    return ReferencedPathtracingEvaluateMixtureSelectionPdf(
        context,
        light.selectionWeight,
        ReferencedPathtracingGetLocalReferenceLightWeight(
            context,
            light));
}

float ReferencedPathtracingGetUnifiedEnvironmentSelectionPdf(
    ReferencedPathtracingLightSelectionContext context)
{
    return ReferencedPathtracingEvaluateMixtureSelectionPdf(
        context,
        context.globalEnvironmentWeight,
        context.localEnvironmentWeight);
}

bool ReferencedPathtracingSampleGlobalAnalyticLight(
    ReferencedPathtracingLightSelectionContext context,
    float weightedSample,
    out uint lightIndex)
{
    lightIndex = 0xffffffffu;
    if (context.globalAnalyticWeight <= 0.0)
        return false;

    if (_ReferencedLocalLightNeeEnabled != 0
        && _ReferencedTransportEstimatorMode
            != kReferencedTransportEstimatorBsdfOnly)
    {
        float conditionalSelectionPdf;
        return ReferencedPathtracingSampleReferenceLightIndex(
            weightedSample / context.globalAnalyticWeight,
            lightIndex,
            conditionalSelectionPdf);
    }

    ReferencedPathTracingLightListParameters parameters =
        _ReferencedLightListParameters[0];
    float accumulatedWeight = 0.0;
    uint lastEligibleLightIndex = 0xffffffffu;
    for (uint candidateIndex = 0u;
         candidateIndex < parameters.lightCount;
         ++candidateIndex)
    {
        ReferencedPathTracingLightRecord light =
            ReferencedPathtracingLoadReferenceLight(candidateIndex);
        if (!ReferencedPathtracingIsReferenceLightNeeEligible(light))
            continue;

        if (light.selectionWeight > 0.0)
            lastEligibleLightIndex = candidateIndex;
        accumulatedWeight += max(light.selectionWeight, 0.0);
        if (weightedSample < accumulatedWeight)
        {
            lightIndex = candidateIndex;
            return true;
        }
    }

    lightIndex = lastEligibleLightIndex;
    return lightIndex != 0xffffffffu;
}

bool ReferencedPathtracingSampleLocalAnalyticLight(
    ReferencedPathtracingLightSelectionContext context,
    float weightedSample,
    out uint lightIndex)
{
    lightIndex = 0xffffffffu;
    ReferencedPathTracingLightListParameters parameters =
        _ReferencedLightListParameters[0];
    float accumulatedWeight = 0.0;
    uint lastEligibleLightIndex = 0xffffffffu;
    for (uint candidateIndex = 0u;
         candidateIndex < parameters.lightCount;
         ++candidateIndex)
    {
        ReferencedPathTracingLightRecord light =
            ReferencedPathtracingLoadReferenceLight(candidateIndex);
        float localWeight =
            ReferencedPathtracingGetLocalReferenceLightWeight(
                context,
                light);
        if (localWeight > 0.0)
            lastEligibleLightIndex = candidateIndex;
        accumulatedWeight += localWeight;
        if (localWeight > 0.0
            && weightedSample < accumulatedWeight)
        {
            lightIndex = candidateIndex;
            return true;
        }
    }

    lightIndex = lastEligibleLightIndex;
    return lightIndex != 0xffffffffu;
}

bool ReferencedPathtracingSampleUnifiedLightSource(
    ReferencedPathtracingLightSelectionContext context,
    float randomValue,
    out uint lightType,
    out uint lightIndex,
    out float selectionPdf)
{
    lightType = REFERENCED_LIGHT_TYPE_INVALID;
    lightIndex = 0xffffffffu;
    selectionPdf = 0.0;
    if (context.globalTotalWeight <= 0.0
        && context.localTotalWeight <= 0.0)
    {
        return false;
    }

    float sample = min(saturate(randomValue), 0.99999994);
    bool sampleGlobal =
        context.globalProposalProbability >= 1.0
        || sample < context.globalProposalProbability;
    float proposalSample = sampleGlobal
        ? sample / max(context.globalProposalProbability, 1e-8)
        : (sample - context.globalProposalProbability)
            / max(1.0 - context.globalProposalProbability, 1e-8);
    proposalSample = min(saturate(proposalSample), 0.99999994);

    float analyticWeight = sampleGlobal
        ? context.globalAnalyticWeight
        : context.localAnalyticWeight;
    float environmentWeight = sampleGlobal
        ? context.globalEnvironmentWeight
        : context.localEnvironmentWeight;
    float totalWeight = analyticWeight + environmentWeight;
    if (totalWeight <= 0.0
        || isnan(totalWeight)
        || isinf(totalWeight))
    {
        return false;
    }

    float weightedSample = proposalSample * totalWeight;
    if (environmentWeight <= 0.0
        || weightedSample < analyticWeight)
    {
        bool sampled = sampleGlobal
            ? ReferencedPathtracingSampleGlobalAnalyticLight(
                context,
                weightedSample,
                lightIndex)
            : ReferencedPathtracingSampleLocalAnalyticLight(
                context,
                weightedSample,
                lightIndex);
        if (!sampled)
            return false;

        ReferencedPathTracingLightRecord light =
            ReferencedPathtracingLoadReferenceLight(lightIndex);
        lightType = light.lightType;
        selectionPdf =
            ReferencedPathtracingGetUnifiedReferenceLightSelectionPdf(
                context,
                light);
    }
    else
    {
        lightType = REFERENCED_LIGHT_TYPE_ENVIRONMENT;
        selectionPdf =
            ReferencedPathtracingGetUnifiedEnvironmentSelectionPdf(
                context);
    }

    return selectionPdf > 0.0
        && !isnan(selectionPdf)
        && !isinf(selectionPdf);
}

float ReferencedPathtracingEvaluateUnifiedEnvironmentLightPdf(
    ReferencedPathtracingLightSelectionContext context,
    float3 directionWS)
{
    return ReferencedPathtracingGetUnifiedEnvironmentSelectionPdf(
            context)
        * ReferencedPathtracingEvaluateEnvironmentPdf(directionWS);
}

#endif
