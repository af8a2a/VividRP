# WorldLightCluster System

## Overview

GPU-based world-space light culling system for path tracing and multi-bounce global illumination. 
Provides efficient light queries at arbitrary world positions via 3D spatial grid, not limited to camera-visible lights.

**Key Features:**
- GPU-side light queries (shader-based)
- Reuses `GPULightData` format for easy integration with existing lighting loop
- Only punctual lights (Point/Spot)
- StructuredBuffer storage for shader access

## Architecture

### CPU Side (WorldLightCluster.cs)

1. **Light Collection**: Collects all punctual lights from scene
2. **Spatial Grid Building**: Builds 3D grid structure mapping world positions to lights
3. **GPU Buffer Management**: Uploads data to StructuredBuffers

### GPU Side (WorldLightCluster.hlsl)

1. **Grid Query**: Convert world position to grid cell
2. **Light Iteration**: Iterate lights in cell(s)
3. **Light Evaluation**: Evaluate lighting using GPULightData

## GPU Buffers

| Buffer | Type | Description |
|--------|------|-------------|
| `_WorldLightData` | `StructuredBuffer<GPULightData>` | All punctual light data |
| `_WorldLightGridCells` | `StructuredBuffer<uint2>` | (offset, count) per grid cell |
| `_WorldLightIndices` | `StructuredBuffer<uint>` | Light indices per cell |

## Shader Constants

| Constant | Type | Description |
|----------|------|-------------|
| `_WorldLightCount` | `int` | Total number of lights |
| `_WorldLightGridResolution` | `int` | Grid resolution per axis |
| `_WorldLightGridMin` | `float3` | World-space minimum of grid |
| `_WorldLightGridCellSize` | `float2` | (cellSize, invCellSize) |

## Usage

### C# Setup

```csharp
// Initialize
var worldLightCluster = new WorldLightCluster();
worldLightCluster.Initialize(maxLights: 512, gridResolution: 32, cellSize: 10.0f);

// Set world bounds (call once or when scene changes)
worldLightCluster.SetWorldBounds(sceneBounds);

// Update each frame
worldLightCluster.UpdateCluster();

// Bind to shaders
worldLightCluster.BindGlobalShaderResources(cmd);
```

### Shader Usage

```hlsl
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/WorldLightCluster.hlsl"

// Simple iteration at a position
void ProcessLightsAtPosition(float3 positionWS, half3 normalWS)
{
    WorldLightIterator iter = WorldLightIteratorInit(positionWS);
    uint lightIdx;
    
    while (WorldLightIteratorNext(iter, lightIdx))
    {
        GPULightData light = GetWorldLight(lightIdx);
        
        if (IsInLightRange(positionWS, light))
        {
            float atten = GetWorldLightAttenuation(positionWS, light);
            // Use light data...
        }
    }
}

// Or use helper function for diffuse GI
half3 bounceLight = EvaluateWorldLightsDiffuse(hitPositionWS, hitNormalWS);

// Or query with radius
uint lightIndices[WORLD_LIGHT_MAX_ITERATION];
uint lightCount = QueryWorldLightsInRadius(positionWS, searchRadius, lightIndices);
for (uint i = 0; i < lightCount; i++)
{
    GPULightData light = GetWorldLight(lightIndices[i]);
    // Process light...
}
```

## Integration with Path Tracing

For multi-bounce GI, when a ray hits a surface outside camera view:

```hlsl
// In ray tracing shader
[shader("closesthit")]
void ClosestHit(inout RayPayload payload, in BuiltInTriangleIntersectionAttributes attr)
{
    float3 hitPos = WorldRayOrigin() + WorldRayDirection() * RayTCurrent();
    float3 hitNormal = GetHitNormal(attr);
    
    // Evaluate direct lighting from world lights at hit point
    half3 directLight = EvaluateWorldLightsDiffuse(hitPos, hitNormal);
    
    // Add to GI contribution
    payload.color += directLight * payload.throughput;
}
```

## GPULightData Format (Reused from ClusterLighting)

```hlsl
struct GPULightData
{
    float3 positionWS;
    uint lightLayerMask;
    
    float3 color;
    int lightFlags;
    
    float4 lightAttenuation;  // Distance and spot attenuation params
    
    float3 dir;               // Spot direction
    int shadowLightIndex;
    
    float4 lightOcclusionProbInfo;
    
    int cookieLightIndex;
    int shadowType;
    float baseContribution;
    float minRoughness;
    
    float4 size;
    
    float3 forward;
    float rangeAttenuationScale;
    float3 up;
    float rangeAttenuationBias;
    float3 right;
    float volumetricLightDimmer;
};
```

## Performance Notes

- Grid resolution trades memory for query speed
- `k_MaxLightsPerCell = 32` limits lights per cell
- `WORLD_LIGHT_MAX_ITERATION = 64` limits shader iterations
- Consider cell size based on typical light range in scene
