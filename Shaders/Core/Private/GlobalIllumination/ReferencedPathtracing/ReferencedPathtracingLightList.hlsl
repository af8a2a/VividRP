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

    // Rebuild the eligible domain when local NEE or the BSDF-only validation
    // strategy removes entries from the CPU-authored global distribution.
    float eligibleSelectionWeight = 0.0;
    for (uint lightIndex = 0u;
         lightIndex < parameters.lightCount;
         ++lightIndex)
    {
        ReferencedPathTracingLightRecord light =
            ReferencedPathtracingLoadReferenceLight(lightIndex);
        bool localLightAllowed =
            _ReferencedLocalLightNeeEnabled != 0
            || light.lightType == REFERENCED_LIGHT_TYPE_DIRECTIONAL;
        bool bsdfReachable =
            (light.flags & REFERENCED_LIGHT_FLAG_BSDF_REACHABLE) != 0u;
        if (localLightAllowed
            && ReferencedPathtracingIsLightNeeEligible(bsdfReachable))
        {
            eligibleSelectionWeight += max(light.selectionWeight, 0.0);
        }
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

    // The distribution stores mean scene-linear luminance over the sphere. Multiplying
    // by 4 PI gives an energy proxy in the same "power-like" role as analytic weights.
    float averageLuminance =
        _ReferencedEnvironmentImportanceDistribution[
            REFERENCED_ENVIRONMENT_AVERAGE_LUMINANCE_OFFSET];
    float selectionWeight =
        4.0 * kReferencedPathtracingPi * max(averageLuminance, 0.0);
    return !isnan(selectionWeight) && !isinf(selectionWeight)
        ? selectionWeight
        : 0.0;
}

float ReferencedPathtracingGetUnifiedLightSelectionWeight()
{
    return ReferencedPathtracingGetReferenceLightSelectionWeight()
        + ReferencedPathtracingGetEnvironmentSelectionWeight();
}

float ReferencedPathtracingGetUnifiedReferenceLightSelectionPdf(
    ReferencedPathTracingLightRecord light)
{
    bool localLightAllowed =
        _ReferencedLocalLightNeeEnabled != 0
        || light.lightType == REFERENCED_LIGHT_TYPE_DIRECTIONAL;
    bool bsdfReachable =
        (light.flags & REFERENCED_LIGHT_FLAG_BSDF_REACHABLE) != 0u;
    if (!localLightAllowed
        || !ReferencedPathtracingIsLightNeeEligible(bsdfReachable))
    {
        return 0.0;
    }

    float totalSelectionWeight =
        ReferencedPathtracingGetUnifiedLightSelectionWeight();
    return totalSelectionWeight > 0.0
        ? max(light.selectionWeight, 0.0) / totalSelectionWeight
        : 0.0;
}

float ReferencedPathtracingGetUnifiedEnvironmentSelectionPdf()
{
    float environmentWeight =
        ReferencedPathtracingGetEnvironmentSelectionWeight();
    float totalSelectionWeight =
        ReferencedPathtracingGetReferenceLightSelectionWeight()
        + environmentWeight;
    return totalSelectionWeight > 0.0
        ? environmentWeight / totalSelectionWeight
        : 0.0;
}

bool ReferencedPathtracingSampleUnifiedLightSource(
    float randomValue,
    out uint lightType,
    out uint lightIndex,
    out float selectionPdf)
{
    lightType = REFERENCED_LIGHT_TYPE_INVALID;
    lightIndex = 0xffffffffu;
    selectionPdf = 0.0;

    float analyticWeight =
        ReferencedPathtracingGetReferenceLightSelectionWeight();
    float environmentWeight =
        ReferencedPathtracingGetEnvironmentSelectionWeight();
    float totalSelectionWeight = analyticWeight + environmentWeight;
    if (totalSelectionWeight <= 0.0
        || isnan(totalSelectionWeight)
        || isinf(totalSelectionWeight))
    {
        return false;
    }

    float weightedSample =
        min(saturate(randomValue), 0.99999994) * totalSelectionWeight;
    if (weightedSample < analyticWeight)
    {
        if (_ReferencedLocalLightNeeEnabled != 0
            && _ReferencedTransportEstimatorMode
                != kReferencedTransportEstimatorBsdfOnly)
        {
            float conditionalSample = weightedSample / analyticWeight;
            float conditionalSelectionPdf;
            if (!ReferencedPathtracingSampleReferenceLightIndex(
                    conditionalSample,
                    lightIndex,
                    conditionalSelectionPdf))
            {
                return false;
            }

            ReferencedPathTracingLightRecord light =
                ReferencedPathtracingLoadReferenceLight(lightIndex);
            lightType = light.lightType;
            selectionPdf = conditionalSelectionPdf
                * analyticWeight
                / totalSelectionWeight;
        }
        else
        {
            ReferencedPathTracingLightListParameters parameters =
                _ReferencedLightListParameters[0];
            float accumulatedWeight = 0.0;
            for (uint candidateIndex = 0u;
                 candidateIndex < parameters.lightCount;
                 ++candidateIndex)
            {
                ReferencedPathTracingLightRecord light =
                    ReferencedPathtracingLoadReferenceLight(
                        candidateIndex);
                bool localLightAllowed =
                    _ReferencedLocalLightNeeEnabled != 0
                    || light.lightType
                        == REFERENCED_LIGHT_TYPE_DIRECTIONAL;
                bool bsdfReachable =
                    (light.flags
                        & REFERENCED_LIGHT_FLAG_BSDF_REACHABLE) != 0u;
                if (!localLightAllowed
                    || !ReferencedPathtracingIsLightNeeEligible(
                        bsdfReachable))
                {
                    continue;
                }

                accumulatedWeight += max(light.selectionWeight, 0.0);
                if (weightedSample < accumulatedWeight)
                {
                    lightIndex = candidateIndex;
                    lightType = light.lightType;
                    selectionPdf =
                        max(light.selectionWeight, 0.0)
                        / totalSelectionWeight;
                    break;
                }
            }
        }
    }
    else
    {
        lightType = REFERENCED_LIGHT_TYPE_ENVIRONMENT;
        selectionPdf = environmentWeight / totalSelectionWeight;
    }

    return selectionPdf > 0.0
        && !isnan(selectionPdf)
        && !isinf(selectionPdf);
}

float ReferencedPathtracingEvaluateUnifiedEnvironmentLightPdf(
    float3 directionWS)
{
    return ReferencedPathtracingGetUnifiedEnvironmentSelectionPdf()
        * ReferencedPathtracingEvaluateEnvironmentPdf(directionWS);
}

#endif
