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

#endif
