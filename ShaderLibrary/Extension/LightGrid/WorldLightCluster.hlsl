#ifndef UNIVERSAL_WORLD_LIGHT_CLUSTER_INCLUDED
#define UNIVERSAL_WORLD_LIGHT_CLUSTER_INCLUDED

// World Light Cluster - GPU-side light queries for path tracing GI
// Uses the same GPULightData format as cluster lighting for easy migration
// Supports punctual lights (Point/Spot) and area lights (Rectangle)

#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/LightCullingSystem/GPULights.cs.hlsl"

// Light type constants (encoded in lightFlags bits 16-19)
#define GPULIGHTTYPE_POINT 0
#define GPULIGHTTYPE_SPOT 1
#define GPULIGHTTYPE_TUBE 5
#define GPULIGHTTYPE_RECTANGLE 6

// Shader resources
StructuredBuffer<GPULightData> _WorldLightData;
StructuredBuffer<uint2> _WorldLightGridCells;   // (offset, count) per cell
StructuredBuffer<uint> _WorldLightIndices;      // Light indices per cell

// Shader constants
int _WorldLightCount;
int _WorldLightGridResolution;
float3 _WorldLightGridMin;
float2 _WorldLightGridCellSize; // x = cellSize, y = invCellSize

// Maximum lights to iterate per query (to avoid infinite loops)
#define WORLD_LIGHT_MAX_ITERATION 64

// Convert world position to grid cell coordinate
int3 WorldToGridCell(float3 positionWS)
{
    float3 localPos = positionWS - _WorldLightGridMin;
    return int3(localPos * _WorldLightGridCellSize.y);
}

// Get flat cell index from 3D coordinates
int GetCellIndex(int3 cellCoord)
{
    if (any(cellCoord < 0) || any(cellCoord >= _WorldLightGridResolution))
        return -1;
    
    return cellCoord.x 
         + cellCoord.y * _WorldLightGridResolution 
         + cellCoord.z * _WorldLightGridResolution * _WorldLightGridResolution;
}

// Get light count at a cell
uint GetCellLightCount(int cellIndex)
{
    if (cellIndex < 0)
        return 0;
    return _WorldLightGridCells[cellIndex].y;
}

// Get light index from cell
uint GetCellLightIndex(int cellIndex, uint localIndex)
{
    uint2 cellData = _WorldLightGridCells[cellIndex];
    return _WorldLightIndices[cellData.x + localIndex];
}

// Get GPULightData by index
GPULightData GetWorldLight(uint lightIndex)
{
    return _WorldLightData[lightIndex];
}

// ============================================================================
// Light Type Helpers
// ============================================================================

// Get light type from lightFlags (bits 16-19)
uint GetWorldLightType(GPULightData light)
{
    return (light.lightFlags >> 16) & 0xF;
}

// Check if light is a rectangle area light
bool IsRectangleLight(GPULightData light)
{
    return GetWorldLightType(light) == GPULIGHTTYPE_RECTANGLE;
}

// Check if light is a spot light
bool IsSpotLight(GPULightData light)
{
    return light.lightAttenuation.z > 0;
}

// ============================================================================
// Rectangle Light Range and Attenuation
// ============================================================================

// Check if position is in rectangle light range
bool IsInRectangleLightRange(float3 positionWS, GPULightData light)
{
    float3 lightToPoint = positionWS - light.positionWS;

    // Rectangle emits in -forward direction, so check if point is in front
    float forwardDist = dot(lightToPoint, light.forward);
    if (forwardDist < 0.0)
        return false;

    // Calculate range from rangeAttenuationScale
    // rangeAttenuationScale = sqrtHuge / (range * range), sqrtHuge = 4096
    float rangeSq = 4096.0 / max(light.rangeAttenuationScale, 0.0001);

    float distSq = dot(lightToPoint, lightToPoint);
    return distSq <= rangeSq;
}

// Calculate rectangle light attenuation
float GetRectangleLightAttenuation(float3 positionWS, GPULightData light)
{
    float3 lightToPoint = positionWS - light.positionWS;
    float distSq = dot(lightToPoint, lightToPoint);

    // Range attenuation using rangeAttenuationScale
    float rangeSq = 4096.0 / max(light.rangeAttenuationScale, 0.0001);
    float distAtten = saturate(1.0 - distSq / rangeSq);
    distAtten *= distAtten;

    // One-sided emission: smooth fade at light plane
    float forwardDist = dot(lightToPoint, light.forward);
    float sideFade = saturate(forwardDist * 10.0);

    return distAtten * sideFade;
}

// ============================================================================
// Light Range and Attenuation
// ============================================================================

// Check if position is within light's influence range
bool IsInLightRange(float3 positionWS, GPULightData light)
{
    // Handle rectangle lights separately
    if (IsRectangleLight(light))
    {
        return IsInRectangleLightRange(positionWS, light);
    }

    // Punctual lights (Point/Spot)
    float3 toLight = light.positionWS - positionWS;
    float distSq = dot(toLight, toLight);

    // lightAttenuation.x = 1 / (range * range)
    float rangeSq = 1.0 / max(light.lightAttenuation.x, 0.0001);

    return distSq <= rangeSq;
}

// Calculate light attenuation at position (same as cluster lighting)
float GetWorldLightAttenuation(float3 positionWS, GPULightData light)
{
    // Handle rectangle lights separately
    if (IsRectangleLight(light))
    {
        return GetRectangleLightAttenuation(positionWS, light);
    }

    // Punctual lights (Point/Spot)
    float3 lightVector = light.positionWS - positionWS;
    float distanceSqr = max(dot(lightVector, lightVector), 0.0001);

    // Distance attenuation
    float lightAtten = rcp(distanceSqr);
    float factor = distanceSqr * light.lightAttenuation.x;
    float smoothFactor = saturate(1.0 - factor * factor);
    smoothFactor *= smoothFactor;
    lightAtten *= smoothFactor;

    // Spot attenuation
    if (IsSpotLight(light))
    {
        float3 lightDir = normalize(lightVector);
        float SdotL = dot(float3(light.dir), lightDir);
        float atten = saturate(SdotL * light.lightAttenuation.z + light.lightAttenuation.w);
        atten *= atten;
        lightAtten *= atten;
    }

    return lightAtten;
}

// Iterator structure for world light queries
struct WorldLightIterator
{
    int3 cellCoord;
    int cellIndex;
    uint lightIndexInCell;
    uint cellLightCount;
    uint iterationCount;
    bool isValid;
};

// Initialize iterator at world position
WorldLightIterator WorldLightIteratorInit(float3 positionWS)
{
    WorldLightIterator iter;
    iter.cellCoord = WorldToGridCell(positionWS);
    iter.cellIndex = GetCellIndex(iter.cellCoord);
    iter.lightIndexInCell = 0;
    iter.cellLightCount = GetCellLightCount(iter.cellIndex);
    iter.iterationCount = 0;
    iter.isValid = iter.cellIndex >= 0 && iter.cellLightCount > 0;
    return iter;
}

// Get next light index, returns false when done
bool WorldLightIteratorNext(inout WorldLightIterator iter, out uint lightIndex)
{
    lightIndex = 0;
    
    if (!iter.isValid || iter.iterationCount >= WORLD_LIGHT_MAX_ITERATION)
        return false;
    
    if (iter.lightIndexInCell >= iter.cellLightCount)
        return false;
    
    lightIndex = GetCellLightIndex(iter.cellIndex, iter.lightIndexInCell);
    iter.lightIndexInCell++;
    iter.iterationCount++;
    
    return true;
}

// Simple query: iterate all lights in a cell
// Usage:
//   WorldLightIterator iter = WorldLightIteratorInit(positionWS);
//   uint lightIdx;
//   while (WorldLightIteratorNext(iter, lightIdx)) {
//       GPULightData light = GetWorldLight(lightIdx);
//       if (IsInLightRange(positionWS, light)) {
//           // Process light
//       }
//   }

// Query lights in a radius (searches neighboring cells)
// Returns number of lights found, fills lightIndices array
uint QueryWorldLightsInRadius(float3 positionWS, float radius, out uint lightIndices[WORLD_LIGHT_MAX_ITERATION])
{
    uint foundCount = 0;
    
    float invCellSize = _WorldLightGridCellSize.y;
    int searchRadius = int(ceil(radius * invCellSize));
    searchRadius = min(searchRadius, 2); // Limit search to 2 cells in each direction
    
    int3 centerCell = WorldToGridCell(positionWS);
    
    [loop]
    for (int dz = -searchRadius; dz <= searchRadius && foundCount < WORLD_LIGHT_MAX_ITERATION; dz++)
    {
        [loop]
        for (int dy = -searchRadius; dy <= searchRadius && foundCount < WORLD_LIGHT_MAX_ITERATION; dy++)
        {
            [loop]
            for (int dx = -searchRadius; dx <= searchRadius && foundCount < WORLD_LIGHT_MAX_ITERATION; dx++)
            {
                int3 cellCoord = centerCell + int3(dx, dy, dz);
                int cellIndex = GetCellIndex(cellCoord);
                
                if (cellIndex < 0)
                    continue;
                
                uint2 cellData = _WorldLightGridCells[cellIndex];
                uint offset = cellData.x;
                uint count = cellData.y;
                
                [loop]
                for (uint i = 0; i < count && foundCount < WORLD_LIGHT_MAX_ITERATION; i++)
                {
                    uint lightIdx = _WorldLightIndices[offset + i];
                    
                    // Check for duplicates (light might be in multiple cells)
                    bool isDuplicate = false;
                    [loop]
                    for (uint j = 0; j < foundCount; j++)
                    {
                        if (lightIndices[j] == lightIdx)
                        {
                            isDuplicate = true;
                            break;
                        }
                    }
                    
                    if (!isDuplicate)
                    {
                        // Check if actually in range
                        GPULightData light = _WorldLightData[lightIdx];
                        if (IsInLightRange(positionWS, light))
                        {
                            lightIndices[foundCount++] = lightIdx;
                        }
                    }
                }
            }
        }
    }
    
    return foundCount;
}

// Evaluate direct lighting from world lights at a position
// Simple diffuse-only version for GI bounce
half3 EvaluateWorldLightsDiffuse(float3 positionWS, half3 normalWS)
{
    half3 totalLight = half3(0, 0, 0);
    
    int3 cellCoord = WorldToGridCell(positionWS);
    int cellIndex = GetCellIndex(cellCoord);
    
    if (cellIndex < 0)
        return totalLight;
    
    uint2 cellData = _WorldLightGridCells[cellIndex];
    uint offset = cellData.x;
    uint count = min(cellData.y, WORLD_LIGHT_MAX_ITERATION);
    
    [loop]
    for (uint i = 0; i < count; i++)
    {
        uint lightIdx = _WorldLightIndices[offset + i];
        GPULightData light = _WorldLightData[lightIdx];
        
        float3 lightVector = light.positionWS - positionWS;
        float distanceSqr = max(dot(lightVector, lightVector), 0.0001);
        half3 lightDir = half3(normalize(lightVector));
        
        // Distance attenuation
        float atten = GetWorldLightAttenuation(positionWS, light);
        
        // NdotL
        half NdotL = saturate(dot(normalWS, lightDir));
        
        totalLight += half3(light.color) * atten * NdotL;
    }
    
    return totalLight;
}

#endif // UNIVERSAL_WORLD_LIGHT_CLUSTER_INCLUDED
