#ifndef VIVIDRP_REGIR_INCLUDED
#define VIVIDRP_REGIR_INCLUDED

#define VIVID_REGIR_LIGHT_TYPE_POINT     0u
#define VIVID_REGIR_LIGHT_TYPE_SPOT      1u
#define VIVID_REGIR_LIGHT_TYPE_TUBE      2u
#define VIVID_REGIR_LIGHT_TYPE_RECTANGLE 3u
#define VIVID_REGIR_INVALID_LIGHT_INDEX  0xffffffffu

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
    uint pad0;
    uint pad1;
    uint pad2;
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
    return max(parameters.gridSizeX, 1u)
        * max(parameters.gridSizeY, 1u)
        * max(parameters.gridSizeZ, 1u);
}

int VividReGIRWorldPosToCellIndex(VividReGIRParameters parameters, float3 worldPos)
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

float VividReGIRAverageDistanceToVolume(float distanceToCenter, float volumeRadius)
{
    const float nonlinearFactor = 1.1547;
    return distanceToCenter + volumeRadius * volumeRadius * volumeRadius
        / max((distanceToCenter + volumeRadius * nonlinearFactor) * (distanceToCenter + volumeRadius * nonlinearFactor), 1e-4);
}

bool VividReGIRCellIndexToWorldPos(VividReGIRParameters parameters, uint cellIndex, out float3 cellCenter, out float cellRadius)
{
    const uint cellsXY = max(parameters.gridSizeX, 1u) * max(parameters.gridSizeY, 1u);
    const uint cellCount = VividReGIRGetCellCount(parameters);
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
