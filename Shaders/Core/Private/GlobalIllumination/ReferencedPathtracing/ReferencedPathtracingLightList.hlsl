#ifndef VIVIDRP_REFERENCED_PATH_TRACING_LIGHT_LIST_INCLUDED
#define VIVIDRP_REFERENCED_PATH_TRACING_LIGHT_LIST_INCLUDED

#define REFERENCED_LIGHT_LIST_VERSION 3u
#define REFERENCED_LIGHT_DISTRIBUTION_CDF 1u
#define REFERENCED_LIGHT_SPATIAL_INDEX_VERSION 1u
#define REFERENCED_LIGHT_SPATIAL_INDEX_AXIS_COUNT 3u
#define REFERENCED_LIGHT_SPATIAL_INDEX_HEADER_WORD_COUNT 24u
#define REFERENCED_LIGHT_SPATIAL_INDEX_CELL_HEADER_WORD_COUNT 2u
#define REFERENCED_LIGHT_SPATIAL_INDEX_STORAGE_BLOCK_WORD_COUNT 12u
#define REFERENCED_LIGHT_SPATIAL_INDEX_CELL_OVERFLOW_MASK (1u << 31u)
#define REFERENCED_LIGHT_SPATIAL_INDEX_CELL_COUNT_MASK 0x7fffffffu

#define REFERENCED_LIGHT_CONTEXT_SPATIAL_INDEXED (1u << 0u)
#define REFERENCED_LIGHT_CONTEXT_FULL_SCAN_FALLBACK (1u << 1u)
#define REFERENCED_LIGHT_CONTEXT_OUTSIDE_BOUNDS (1u << 2u)
#define REFERENCED_LIGHT_CONTEXT_CELL_OVERFLOW (1u << 3u)

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
    uint incompleteLocalProposalLightCount;
    uint reserved1;
};

StructuredBuffer<ReferencedPathTracingLightRecord> _ReferencedLightList;
StructuredBuffer<ReferencedPathTracingLightListParameters>
    _ReferencedLightListParameters;

uint ReferencedPathtracingLoadLightListStorageWord(uint wordIndex)
{
    uint blockIndex =
        1u
        + wordIndex
            / REFERENCED_LIGHT_SPATIAL_INDEX_STORAGE_BLOCK_WORD_COUNT;
    uint blockWord =
        wordIndex
        % REFERENCED_LIGHT_SPATIAL_INDEX_STORAGE_BLOCK_WORD_COUNT;
    ReferencedPathTracingLightListParameters block =
        _ReferencedLightListParameters[blockIndex];
    switch (blockWord)
    {
        case 0u: return block.lightCount;
        case 1u: return block.activeLightCount;
        case 2u: return block.unsupportedLightCount;
        case 3u: return block.unstableLightCount;
        case 4u: return asuint(block.totalSelectionWeight);
        case 5u: return asuint(block.inverseTotalSelectionWeight);
        case 6u: return block.signatureLow;
        case 7u: return block.signatureHigh;
        case 8u: return block.version;
        case 9u: return block.distributionMode;
        case 10u: return block.incompleteLocalProposalLightCount;
        case 11u: return block.reserved1;
    }

    return 0u;
}

struct ReferencedPathtracingLightSpatialIndexHeader
{
    uint version;
    uint resolution;
    uint cellCapacity;
    uint cellCount;
    uint cellHeaderWordOffset;
    uint lightIndexWordOffset;
    uint lightIndexCount;
    uint overflowCellCount;
    float3 boundsMin;
    float3 inverseBoundsExtent;
    uint unboundedLightOffset;
    uint unboundedLightCount;
    uint finiteLightCount;
    uint signatureLow;
    uint signatureHigh;
};

ReferencedPathtracingLightSpatialIndexHeader
ReferencedPathtracingLoadLightSpatialIndexHeader()
{
    ReferencedPathtracingLightSpatialIndexHeader header =
        (ReferencedPathtracingLightSpatialIndexHeader)0;
    header.version =
        ReferencedPathtracingLoadLightListStorageWord(0u);
    header.resolution =
        ReferencedPathtracingLoadLightListStorageWord(1u);
    header.cellCapacity =
        ReferencedPathtracingLoadLightListStorageWord(2u);
    header.cellCount =
        ReferencedPathtracingLoadLightListStorageWord(3u);
    header.cellHeaderWordOffset =
        ReferencedPathtracingLoadLightListStorageWord(4u);
    header.lightIndexWordOffset =
        ReferencedPathtracingLoadLightListStorageWord(5u);
    header.lightIndexCount =
        ReferencedPathtracingLoadLightListStorageWord(6u);
    header.overflowCellCount =
        ReferencedPathtracingLoadLightListStorageWord(7u);
    header.boundsMin = asfloat(uint3(
        ReferencedPathtracingLoadLightListStorageWord(8u),
        ReferencedPathtracingLoadLightListStorageWord(9u),
        ReferencedPathtracingLoadLightListStorageWord(10u)));
    header.inverseBoundsExtent = asfloat(uint3(
        ReferencedPathtracingLoadLightListStorageWord(11u),
        ReferencedPathtracingLoadLightListStorageWord(12u),
        ReferencedPathtracingLoadLightListStorageWord(13u)));
    header.unboundedLightOffset =
        ReferencedPathtracingLoadLightListStorageWord(14u);
    header.unboundedLightCount =
        ReferencedPathtracingLoadLightListStorageWord(15u);
    header.finiteLightCount =
        ReferencedPathtracingLoadLightListStorageWord(16u);
    header.signatureLow =
        ReferencedPathtracingLoadLightListStorageWord(17u);
    header.signatureHigh =
        ReferencedPathtracingLoadLightListStorageWord(18u);
    return header;
}

bool ReferencedPathtracingIsLightSpatialIndexHeaderValid(
    ReferencedPathtracingLightSpatialIndexHeader header)
{
    ReferencedPathTracingLightListParameters listParameters =
        _ReferencedLightListParameters[0];
    uint expectedCellCount =
        REFERENCED_LIGHT_SPATIAL_INDEX_AXIS_COUNT
        * header.resolution
        * header.resolution;
    return header.version == REFERENCED_LIGHT_SPATIAL_INDEX_VERSION
        && header.resolution > 0u
        && header.cellCapacity > 0u
        && header.cellCount == expectedCellCount
        && header.cellHeaderWordOffset
            >= REFERENCED_LIGHT_SPATIAL_INDEX_HEADER_WORD_COUNT
        && header.lightIndexWordOffset
            >= header.cellHeaderWordOffset
                + header.cellCount
                    * REFERENCED_LIGHT_SPATIAL_INDEX_CELL_HEADER_WORD_COUNT
        && header.unboundedLightOffset + header.unboundedLightCount
            <= header.lightIndexCount
        && header.signatureLow == listParameters.signatureLow
        && header.signatureHigh == listParameters.signatureHigh;
}

uint ReferencedPathtracingLoadSpatialLightIndex(
    ReferencedPathtracingLightSpatialIndexHeader header,
    uint indexOffset)
{
    if (indexOffset >= header.lightIndexCount)
        return 0xffffffffu;
    return ReferencedPathtracingLoadLightListStorageWord(
        header.lightIndexWordOffset + indexOffset);
}

ReferencedPathTracingLightListParameters
ReferencedPathtracingLoadReferenceLightListParameters()
{
    return _ReferencedLightListParameters[0];
}

bool ReferencedPathtracingHasReferenceLights()
{
    ReferencedPathTracingLightListParameters parameters =
        ReferencedPathtracingLoadReferenceLightListParameters();
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
        ReferencedPathtracingLoadReferenceLightListParameters();
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
    uint unboundedLightOffset;
    uint unboundedLightCount;
    uint spatialCandidateOffset;
    uint spatialCandidateCount;
    uint spatialAxis;
    uint spatialFlags;
};

void ReferencedPathtracingUseFullReferenceLightScan(
    inout ReferencedPathtracingLightSelectionContext context,
    uint extraFlags)
{
    ReferencedPathTracingLightListParameters parameters =
        ReferencedPathtracingLoadReferenceLightListParameters();
    context.unboundedLightOffset = 0u;
    context.unboundedLightCount = 0u;
    context.spatialCandidateOffset = 0u;
    context.spatialCandidateCount = parameters.lightCount;
    context.spatialAxis = 0xffffffffu;
    context.spatialFlags =
        REFERENCED_LIGHT_CONTEXT_FULL_SCAN_FALLBACK | extraFlags;
}

uint2 ReferencedPathtracingGetSpatialProjectionCoordinate(
    uint axis,
    uint3 gridCoordinate)
{
    if (axis == 0u)
        return gridCoordinate.yz;
    if (axis == 1u)
        return gridCoordinate.xz;
    return gridCoordinate.xy;
}

bool ReferencedPathtracingTryLoadSpatialCell(
    ReferencedPathtracingLightSpatialIndexHeader header,
    uint axis,
    uint3 gridCoordinate,
    out uint lightOffset,
    out uint lightCount,
    out bool overflow)
{
    lightOffset = 0u;
    lightCount = 0u;
    overflow = false;
    uint2 coordinate =
        ReferencedPathtracingGetSpatialProjectionCoordinate(
            axis,
            gridCoordinate);
    uint cellsPerAxis = header.resolution * header.resolution;
    uint cellIndex =
        axis * cellsPerAxis
        + coordinate.x
        + coordinate.y * header.resolution;
    if (cellIndex >= header.cellCount)
        return false;

    uint cellWordOffset =
        header.cellHeaderWordOffset
        + cellIndex
            * REFERENCED_LIGHT_SPATIAL_INDEX_CELL_HEADER_WORD_COUNT;
    lightOffset =
        ReferencedPathtracingLoadLightListStorageWord(cellWordOffset);
    uint countAndFlags =
        ReferencedPathtracingLoadLightListStorageWord(
            cellWordOffset + 1u);
    lightCount =
        countAndFlags
        & REFERENCED_LIGHT_SPATIAL_INDEX_CELL_COUNT_MASK;
    overflow =
        (countAndFlags
            & REFERENCED_LIGHT_SPATIAL_INDEX_CELL_OVERFLOW_MASK) != 0u;
    return lightOffset + lightCount <= header.lightIndexCount
        && lightCount <= header.cellCapacity;
}

void ReferencedPathtracingResolveLightSpatialCandidateSet(
    inout ReferencedPathtracingLightSelectionContext context)
{
    if (_ReferencedLightSpatialIndexEnabled == 0)
    {
        ReferencedPathtracingUseFullReferenceLightScan(context, 0u);
        return;
    }

    ReferencedPathTracingLightListParameters listParameters =
        ReferencedPathtracingLoadReferenceLightListParameters();
    if (listParameters.version != REFERENCED_LIGHT_LIST_VERSION)
    {
        ReferencedPathtracingUseFullReferenceLightScan(context, 0u);
        return;
    }

    ReferencedPathtracingLightSpatialIndexHeader header =
        ReferencedPathtracingLoadLightSpatialIndexHeader();
    if (!ReferencedPathtracingIsLightSpatialIndexHeaderValid(header))
    {
        ReferencedPathtracingUseFullReferenceLightScan(context, 0u);
        return;
    }

    context.unboundedLightOffset = header.unboundedLightOffset;
    context.unboundedLightCount = header.unboundedLightCount;
    context.spatialCandidateOffset = 0u;
    context.spatialCandidateCount = 0u;
    context.spatialAxis = 0xffffffffu;
    context.spatialFlags =
        REFERENCED_LIGHT_CONTEXT_SPATIAL_INDEXED;

    if (header.finiteLightCount == 0u)
        return;

    float3 normalizedPosition =
        (context.positionWS - header.boundsMin)
        * header.inverseBoundsExtent;
    if (any(isnan(normalizedPosition))
        || any(isinf(normalizedPosition)))
    {
        ReferencedPathtracingUseFullReferenceLightScan(context, 0u);
        return;
    }

    if (any(normalizedPosition < 0.0)
        || any(normalizedPosition > 1.0))
    {
        context.spatialFlags |=
            REFERENCED_LIGHT_CONTEXT_OUTSIDE_BOUNDS;
        return;
    }

    uint3 gridCoordinate = min(
        (uint3)floor(
            saturate(normalizedPosition)
            * (float)header.resolution),
        header.resolution - 1u);
    uint bestCount = 0xffffffffu;
    uint bestOffset = 0u;
    uint bestAxis = 0xffffffffu;
    bool sawOverflow = false;
    [unroll]
    for (uint axis = 0u;
         axis < REFERENCED_LIGHT_SPATIAL_INDEX_AXIS_COUNT;
         ++axis)
    {
        uint lightOffset;
        uint lightCount;
        bool overflow;
        if (!ReferencedPathtracingTryLoadSpatialCell(
                header,
                axis,
                gridCoordinate,
                lightOffset,
                lightCount,
                overflow))
        {
            ReferencedPathtracingUseFullReferenceLightScan(context, 0u);
            return;
        }

        sawOverflow = sawOverflow || overflow;
        if (!overflow && lightCount < bestCount)
        {
            bestCount = lightCount;
            bestOffset = lightOffset;
            bestAxis = axis;
        }
    }

    if (bestAxis == 0xffffffffu)
    {
        ReferencedPathtracingUseFullReferenceLightScan(
            context,
            REFERENCED_LIGHT_CONTEXT_CELL_OVERFLOW);
        return;
    }

    context.spatialCandidateOffset = bestOffset;
    context.spatialCandidateCount = bestCount;
    context.spatialAxis = bestAxis;
    if (sawOverflow)
    {
        context.spatialFlags |=
            REFERENCED_LIGHT_CONTEXT_CELL_OVERFLOW;
    }
}

uint ReferencedPathtracingGetContextLightCount(
    ReferencedPathtracingLightSelectionContext context)
{
    return context.unboundedLightCount
        + context.spatialCandidateCount;
}

uint ReferencedPathtracingGetContextLightIndex(
    ReferencedPathtracingLightSelectionContext context,
    uint contextLightIndex)
{
    if ((context.spatialFlags
            & REFERENCED_LIGHT_CONTEXT_FULL_SCAN_FALLBACK) != 0u)
    {
        return contextLightIndex;
    }

    ReferencedPathtracingLightSpatialIndexHeader header =
        ReferencedPathtracingLoadLightSpatialIndexHeader();
    uint indexOffset;
    if (contextLightIndex < context.unboundedLightCount)
    {
        indexOffset =
            context.unboundedLightOffset + contextLightIndex;
    }
    else
    {
        indexOffset =
            context.spatialCandidateOffset
            + contextLightIndex
            - context.unboundedLightCount;
    }

    return ReferencedPathtracingLoadSpatialLightIndex(
        header,
        indexOffset);
}

bool ReferencedPathtracingSpatialRangeContainsLight(
    ReferencedPathtracingLightSpatialIndexHeader header,
    uint lightOffset,
    uint lightCount,
    uint lightIndex)
{
    uint lower = 0u;
    uint upper = lightCount;
    while (lower < upper)
    {
        uint middle = lower + ((upper - lower) >> 1u);
        uint candidateIndex =
            ReferencedPathtracingLoadSpatialLightIndex(
                header,
                lightOffset + middle);
        if (candidateIndex < lightIndex)
            lower = middle + 1u;
        else
            upper = middle;
    }

    return lower < lightCount
        && ReferencedPathtracingLoadSpatialLightIndex(
            header,
            lightOffset + lower) == lightIndex;
}

bool ReferencedPathtracingContextContainsLight(
    ReferencedPathtracingLightSelectionContext context,
    uint lightIndex)
{
    if ((context.spatialFlags
            & REFERENCED_LIGHT_CONTEXT_FULL_SCAN_FALLBACK) != 0u)
    {
        return lightIndex < context.spatialCandidateCount;
    }

    ReferencedPathtracingLightSpatialIndexHeader header =
        ReferencedPathtracingLoadLightSpatialIndexHeader();
    return ReferencedPathtracingSpatialRangeContainsLight(
            header,
            context.unboundedLightOffset,
            context.unboundedLightCount,
            lightIndex)
        || ReferencedPathtracingSpatialRangeContainsLight(
            header,
            context.spatialCandidateOffset,
            context.spatialCandidateCount,
            lightIndex);
}

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
        ReferencedPathtracingLoadReferenceLightListParameters();
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

float ReferencedPathtracingGetLocalReferenceLightProposalWeight(
    ReferencedPathtracingLightSelectionContext context,
    uint lightIndex,
    ReferencedPathTracingLightRecord light)
{
    return ReferencedPathtracingContextContainsLight(
            context,
            lightIndex)
        ? ReferencedPathtracingGetLocalReferenceLightWeight(
            context,
            light)
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
    ReferencedPathtracingResolveLightSpatialCandidateSet(context);

    if (_ReferencedShadingPointLightSelectionEnabled != 0
        && context.globalTotalWeight > 0.0)
    {
        uint contextLightCount =
            ReferencedPathtracingGetContextLightCount(context);
        for (uint contextLightIndex = 0u;
             contextLightIndex < contextLightCount;
             ++contextLightIndex)
        {
            uint lightIndex =
                ReferencedPathtracingGetContextLightIndex(
                    context,
                    contextLightIndex);
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
        ReferencedPathTracingLightListParameters parameters =
            ReferencedPathtracingLoadReferenceLightListParameters();
        if (parameters.incompleteLocalProposalLightCount == 0u)
        {
            // Point/spot range and cone support are evaluated identically by
            // the local proposal and the NEE candidate. Mixing in the global
            // CDF here can select a different punctual light outside its
            // influence range, producing zero samples and visible dark bands
            // where punctual ranges overlap at one candidate per pixel.
            context.globalProposalProbability = 0.0;
        }
        else
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
    uint lightIndex,
    ReferencedPathTracingLightRecord light)
{
    if (!ReferencedPathtracingIsReferenceLightNeeEligible(light))
        return 0.0;

    return ReferencedPathtracingEvaluateMixtureSelectionPdf(
        context,
        light.selectionWeight,
        ReferencedPathtracingGetLocalReferenceLightProposalWeight(
            context,
            lightIndex,
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
        ReferencedPathtracingLoadReferenceLightListParameters();
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
    float accumulatedWeight = 0.0;
    uint lastEligibleLightIndex = 0xffffffffu;
    uint contextLightCount =
        ReferencedPathtracingGetContextLightCount(context);
    for (uint contextLightIndex = 0u;
         contextLightIndex < contextLightCount;
         ++contextLightIndex)
    {
        uint candidateIndex =
            ReferencedPathtracingGetContextLightIndex(
                context,
                contextLightIndex);
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
                lightIndex,
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
