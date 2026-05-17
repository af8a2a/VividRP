#ifndef VIVIDRP_REGIR_INCLUDED
#define VIVIDRP_REGIR_INCLUDED

#define VIVID_REGIR_LIGHT_TYPE_POINT     0u
#define VIVID_REGIR_LIGHT_TYPE_SPOT      1u
#define VIVID_REGIR_LIGHT_TYPE_TUBE      2u
#define VIVID_REGIR_LIGHT_TYPE_RECTANGLE 3u
#define VIVID_REGIR_INVALID_LIGHT_INDEX  0xffffffffu
#define VIVID_REGIR_MODE_GRID            1u
#define VIVID_REGIR_MODE_ONION           2u
#define VIVID_REGIR_SOURCE_SAMPLING_UNIFORM 0u
#define VIVID_REGIR_SOURCE_SAMPLING_POWER_RIS 1u
#define VIVID_REGIR_ONION_MAX_LAYER_GROUPS 8
#define VIVID_REGIR_ONION_MAX_RINGS 52
#define VIVID_REGIR_PI 3.14159265358979323846

struct VividReGIRLightData
{
    float3 positionWS;
    float range;
    float3 color;
    uint lightType;
    float3 directionWS;
    float angleScale;
    float3 rightWS;
    float angleOffset;
    float3 upWS;
    float shapeRadius;
    float2 areaSize;
    float power;
    uint renderingLayerMask;
};

struct VividReGIRParameters
{
    float3 centerWS;
    float cellSize;
    uint gridSizeX;
    uint gridSizeY;
    uint gridSizeZ;
    uint lightsPerCell;
    uint lightCount;
    uint slotCount;
    uint buildSamples;
    float samplingJitter;
    uint frameIndex;
    uint mode;
    uint sourceSamplingMode;
    uint lightPdfTextureWidth;
    uint lightPdfTextureHeight;
    uint lightPdfTextureMipCount;
    uint onionCellCount;
    uint onionLayerGroupCount;
    float onionCubicRootFactor;
    float onionLinearFactor;
    uint onionRingCount;
    uint pad0;

    float onionLayerInnerRadius[VIVID_REGIR_ONION_MAX_LAYER_GROUPS];
    float onionLayerOuterRadius[VIVID_REGIR_ONION_MAX_LAYER_GROUPS];
    float onionLayerInvLogLayerScale[VIVID_REGIR_ONION_MAX_LAYER_GROUPS];
    uint onionLayerCount[VIVID_REGIR_ONION_MAX_LAYER_GROUPS];
    float onionLayerInvEquatorialCellAngle[VIVID_REGIR_ONION_MAX_LAYER_GROUPS];
    uint onionLayerCellsPerLayer[VIVID_REGIR_ONION_MAX_LAYER_GROUPS];
    uint onionLayerRingOffset[VIVID_REGIR_ONION_MAX_LAYER_GROUPS];
    uint onionLayerRingCount[VIVID_REGIR_ONION_MAX_LAYER_GROUPS];
    float onionLayerEquatorialCellAngle[VIVID_REGIR_ONION_MAX_LAYER_GROUPS];
    float onionLayerScale[VIVID_REGIR_ONION_MAX_LAYER_GROUPS];
    uint onionLayerCellOffset[VIVID_REGIR_ONION_MAX_LAYER_GROUPS];
    uint onionLayerPad[VIVID_REGIR_ONION_MAX_LAYER_GROUPS];

    float onionRingCellAngle[VIVID_REGIR_ONION_MAX_RINGS];
    float onionRingInvCellAngle[VIVID_REGIR_ONION_MAX_RINGS];
    uint onionRingCellOffset[VIVID_REGIR_ONION_MAX_RINGS];
    uint onionRingCellCount[VIVID_REGIR_ONION_MAX_RINGS];
};

struct VividReGIRReservoir
{
    uint lightIndex;
    float weight;
    uint pad0;
    uint pad1;
};

uint VividReGIRGetCellCount(VividReGIRParameters parameters)
{
    if (parameters.mode == VIVID_REGIR_MODE_ONION)
        return max(parameters.onionCellCount, 1u);

    return max(parameters.gridSizeX, 1u)
        * max(parameters.gridSizeY, 1u)
        * max(parameters.gridSizeZ, 1u);
}

float3 VividReGIRSphericalToCartesian(float radius, float azimuth, float elevation)
{
    float cosElevation = cos(elevation);
    return float3(
        radius * cos(azimuth) * cosElevation,
        radius * sin(elevation),
        radius * sin(azimuth) * cosElevation);
}

void VividReGIRCartesianToSpherical(float3 position, out float radius, out float azimuth, out float elevation)
{
    radius = length(position);
    if (radius <= 1e-6)
    {
        azimuth = 0.0;
        elevation = 0.0;
        return;
    }

    azimuth = atan2(position.z, position.x);
    elevation = asin(clamp(position.y / radius, -1.0, 1.0));
}

float VividReGIRWrapAzimuth(float azimuth)
{
    azimuth = fmod(azimuth, 2.0 * VIVID_REGIR_PI);
    return azimuth < 0.0
        ? azimuth + 2.0 * VIVID_REGIR_PI
        : azimuth;
}

int VividReGIRGridWorldPosToCellIndex(VividReGIRParameters parameters, float3 worldPos)
{
    float cellSize = max(parameters.cellSize, 1e-5);
    uint3 gridSize = uint3(
        max(parameters.gridSizeX, 1u),
        max(parameters.gridSizeY, 1u),
        max(parameters.gridSizeZ, 1u));
    float3 gridSizeF = float3(gridSize);
    float3 gridOrigin = parameters.centerWS - gridSizeF * (cellSize * 0.5);
    int3 cellPosition = (int3)floor((worldPos - gridOrigin) / cellSize);

    if (any(cellPosition < 0) || any(cellPosition >= int3(gridSize)))
        return -1;

    return cellPosition.x
        + cellPosition.y * int(gridSize.x)
        + cellPosition.z * int(gridSize.x * gridSize.y);
}

int VividReGIROnionWorldPosToCellIndex(VividReGIRParameters parameters, float3 worldPos)
{
    if (parameters.onionLayerGroupCount == 0u || parameters.onionRingCount == 0u)
        return -1;

    float3 translatedPos = worldPos - parameters.centerWS;
    float radius;
    float azimuth;
    float elevation;
    VividReGIRCartesianToSpherical(translatedPos, radius, azimuth, elevation);
    azimuth = VividReGIRWrapAzimuth(azimuth + VIVID_REGIR_PI);

    if (radius <= parameters.onionLayerInnerRadius[0])
        return 0;

    uint layerGroupIndex;
    bool foundLayerGroup = false;
    [loop]
    for (layerGroupIndex = 0u; layerGroupIndex < parameters.onionLayerGroupCount; layerGroupIndex++)
    {
        if (radius <= parameters.onionLayerOuterRadius[layerGroupIndex])
        {
            foundLayerGroup = true;
            break;
        }
    }

    if (!foundLayerGroup)
        return -1;

    float layerInnerRadius = max(parameters.onionLayerInnerRadius[layerGroupIndex], 1e-5);
    uint layerIndex = (uint)floor(max(0.0, log(radius / layerInnerRadius) * parameters.onionLayerInvLogLayerScale[layerGroupIndex]));
    layerIndex = min(layerIndex, max(parameters.onionLayerCount[layerGroupIndex], 1u) - 1u);

    uint ringIndex = (uint)floor(abs(elevation) * parameters.onionLayerInvEquatorialCellAngle[layerGroupIndex] + 0.5);
    ringIndex = min(ringIndex, max(parameters.onionLayerRingCount[layerGroupIndex], 1u) - 1u);
    uint globalRingIndex = min(parameters.onionLayerRingOffset[layerGroupIndex] + ringIndex, parameters.onionRingCount - 1u);

    if ((layerIndex & 1u) != 0u)
        azimuth = VividReGIRWrapAzimuth(azimuth - parameters.onionRingCellAngle[globalRingIndex] * 0.5);

    uint ringCellCount = max(parameters.onionRingCellCount[globalRingIndex], 1u);
    int cellIndex = min(
        int(floor(azimuth * parameters.onionRingInvCellAngle[globalRingIndex])),
        int(ringCellCount) - 1);
    int ringCellOffset = int(parameters.onionRingCellOffset[globalRingIndex]);
    if (elevation < 0.0 && ringIndex > 0u)
        ringCellOffset += int(ringCellCount);

    return cellIndex
        + ringCellOffset
        + int(layerIndex * parameters.onionLayerCellsPerLayer[layerGroupIndex])
        + int(parameters.onionLayerCellOffset[layerGroupIndex]);
}

int VividReGIRWorldPosToCellIndex(VividReGIRParameters parameters, float3 worldPos)
{
    if (parameters.mode == VIVID_REGIR_MODE_ONION)
        return VividReGIROnionWorldPosToCellIndex(parameters, worldPos);

    return VividReGIRGridWorldPosToCellIndex(parameters, worldPos);
}

float VividReGIRGetJitterScale(VividReGIRParameters parameters, float3 worldPos)
{
    if (parameters.mode == VIVID_REGIR_MODE_ONION)
    {
        float distanceToCenter = length(worldPos - parameters.centerWS) / max(parameters.cellSize, 1e-5);
        float jitterScale = max(1.0, max(
            pow(distanceToCenter, 1.0 / 3.0) * parameters.onionCubicRootFactor,
            distanceToCenter * parameters.onionLinearFactor));
        return jitterScale * parameters.samplingJitter * parameters.cellSize;
    }

    return parameters.samplingJitter * parameters.cellSize;
}

float VividReGIRAverageDistanceToVolume(float distanceToCenter, float volumeRadius)
{
    const float nonlinearFactor = 1.1547;
    return distanceToCenter + volumeRadius * volumeRadius * volumeRadius
        / max((distanceToCenter + volumeRadius * nonlinearFactor) * (distanceToCenter + volumeRadius * nonlinearFactor), 1e-4);
}

bool VividReGIRGridCellIndexToWorldPos(VividReGIRParameters parameters, uint cellIndex, out float3 cellCenter, out float cellRadius)
{
    const uint cellsXY = max(parameters.gridSizeX, 1u) * max(parameters.gridSizeY, 1u);
    const uint cellCount = max(parameters.gridSizeX, 1u) * max(parameters.gridSizeY, 1u) * max(parameters.gridSizeZ, 1u);
    if (cellIndex >= cellCount)
    {
        cellCenter = 0.0;
        cellRadius = 0.0;
        return false;
    }

    uint3 cellPosition;
    cellPosition.z = cellIndex / cellsXY;
    uint cellInSlice = cellIndex - cellPosition.z * cellsXY;
    cellPosition.y = cellInSlice / max(parameters.gridSizeX, 1u);
    cellPosition.x = cellInSlice - cellPosition.y * max(parameters.gridSizeX, 1u);

    float3 gridSize = float3(parameters.gridSizeX, parameters.gridSizeY, parameters.gridSizeZ);
    float3 gridOrigin = parameters.centerWS - gridSize * (parameters.cellSize * 0.5);
    cellCenter = gridOrigin + (float3(cellPosition) + 0.5) * parameters.cellSize;
    cellRadius = parameters.cellSize * (0.5 * sqrt(3.0));
    return true;
}

bool VividReGIROnionCellIndexToWorldPos(VividReGIRParameters parameters, uint cellIndex, out float3 cellCenter, out float cellRadius)
{
    cellCenter = 0.0;
    cellRadius = 0.0;

    if (cellIndex >= max(parameters.onionCellCount, 1u) || parameters.onionLayerGroupCount == 0u)
        return false;

    if (cellIndex == 0u)
    {
        cellCenter = parameters.centerWS;
        cellRadius = parameters.onionLayerInnerRadius[0];
        return true;
    }

    uint localCellIndex = cellIndex - 1u;
    uint layerGroupIndex;
    bool foundLayerGroup = false;
    [loop]
    for (layerGroupIndex = 0u; layerGroupIndex < parameters.onionLayerGroupCount; layerGroupIndex++)
    {
        uint cellsPerGroup = parameters.onionLayerCellsPerLayer[layerGroupIndex]
            * parameters.onionLayerCount[layerGroupIndex];
        if (localCellIndex < cellsPerGroup)
        {
            foundLayerGroup = true;
            break;
        }

        localCellIndex -= cellsPerGroup;
    }

    if (!foundLayerGroup)
        return false;

    uint cellsPerLayer = max(parameters.onionLayerCellsPerLayer[layerGroupIndex], 1u);
    uint layerIndex = localCellIndex / cellsPerLayer;
    localCellIndex -= layerIndex * cellsPerLayer;

    uint ringIndex;
    bool foundRing = false;
    [loop]
    for (ringIndex = 0u; ringIndex < parameters.onionLayerRingCount[layerGroupIndex]; ringIndex++)
    {
        uint globalRingIndex = min(parameters.onionLayerRingOffset[layerGroupIndex] + ringIndex, parameters.onionRingCount - 1u);
        uint ringCellEnd = parameters.onionRingCellOffset[globalRingIndex]
            + parameters.onionRingCellCount[globalRingIndex] * (ringIndex > 0u ? 2u : 1u);
        if (localCellIndex < ringCellEnd)
        {
            foundRing = true;
            break;
        }
    }

    if (!foundRing)
        return false;

    uint resolvedRingIndex = min(parameters.onionLayerRingOffset[layerGroupIndex] + ringIndex, parameters.onionRingCount - 1u);
    localCellIndex -= parameters.onionRingCellOffset[resolvedRingIndex];

    float elevation = float(ringIndex) * parameters.onionLayerEquatorialCellAngle[layerGroupIndex];
    if (localCellIndex >= parameters.onionRingCellCount[resolvedRingIndex])
        elevation = -elevation;

    float azimuth = (float(localCellIndex) + 0.5) * parameters.onionRingCellAngle[resolvedRingIndex];
    if ((layerIndex & 1u) != 0u)
        azimuth += parameters.onionRingCellAngle[resolvedRingIndex] * 0.5;

    azimuth -= VIVID_REGIR_PI;

    float layerInnerRadius = parameters.onionLayerInnerRadius[layerGroupIndex]
        * pow(parameters.onionLayerScale[layerGroupIndex], layerIndex);
    float layerOuterRadius = layerInnerRadius * parameters.onionLayerScale[layerGroupIndex];
    float radius = (layerInnerRadius + layerOuterRadius) * 0.5;

    cellCenter = VividReGIRSphericalToCartesian(radius, azimuth, elevation);

    azimuth += parameters.onionRingCellAngle[resolvedRingIndex] * 0.5;
    elevation = elevation == 0.0
        ? parameters.onionLayerEquatorialCellAngle[layerGroupIndex] * 0.5
        : (abs(elevation) - parameters.onionLayerEquatorialCellAngle[layerGroupIndex] * 0.5) * sign(elevation);

    float3 cellCorner = VividReGIRSphericalToCartesian(layerOuterRadius, azimuth, elevation);
    cellRadius = length(cellCorner - cellCenter);
    cellCenter += parameters.centerWS;
    return true;
}

bool VividReGIRCellIndexToWorldPos(VividReGIRParameters parameters, uint cellIndex, out float3 cellCenter, out float cellRadius)
{
    if (parameters.mode == VIVID_REGIR_MODE_ONION)
        return VividReGIROnionCellIndexToWorldPos(parameters, cellIndex, cellCenter, cellRadius);

    return VividReGIRGridCellIndexToWorldPos(parameters, cellIndex, cellCenter, cellRadius);
}

float VividReGIREvaluateRangeWeight(VividReGIRLightData light, float3 volumeCenter, float volumeRadius)
{
    float distanceToCenter = length(volumeCenter - light.positionWS);
    if (distanceToCenter > light.range + volumeRadius)
        return 0.0;

    float averageDistance = VividReGIRAverageDistanceToVolume(distanceToCenter, volumeRadius);
    float rangeFade = saturate(1.0 - averageDistance / max(light.range, 1e-4));
    return light.power * rangeFade * rangeFade / max(averageDistance * averageDistance, 1e-4);
}

float VividReGIREvaluateSpotWeight(VividReGIRLightData light, float3 volumeCenter)
{
    float3 lightToVolume = volumeCenter - light.positionWS;
    float lengthSq = dot(lightToVolume, lightToVolume);
    if (lengthSq <= 1e-6)
        return 1.0;

    float3 directionToVolume = lightToVolume * rsqrt(lengthSq);
    float angleAttenuation = saturate(dot(normalize(light.directionWS), directionToVolume) * light.angleScale + light.angleOffset);
    return angleAttenuation * angleAttenuation;
}

float VividReGIREvaluateLightTargetWeight(VividReGIRLightData light, float3 volumeCenter, float volumeRadius)
{
    float weight = VividReGIREvaluateRangeWeight(light, volumeCenter, volumeRadius);
    if (weight <= 0.0)
        return 0.0;

    if (light.lightType == VIVID_REGIR_LIGHT_TYPE_SPOT)
        weight *= VividReGIREvaluateSpotWeight(light, volumeCenter);

    return max(weight, 0.0);
}

#endif
