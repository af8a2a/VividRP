# WorldLightCluster System

## Overview

The `WorldLightCluster` system provides world-space light culling for path tracing and multi-bounce global illumination. Unlike the standard `ClusterLighting` system which only culls camera-visible lights, this system maintains all lights in the scene and allows efficient queries at arbitrary world positions.

## Architecture

### Core Components

1. **WorldLightCluster** - Main entry point and public interface
   - Manages initialization and lifecycle
   - Provides query interface for lights at world positions
   - Handles GPU buffer management

2. **WorldLightClusterData** - Stores all light data
   - Maintains a list of all lights in the scene
   - Converts Unity Light data to shader-friendly format
   - Manages GPU buffer for shader access

3. **WorldLightSpatialGrid** - 3D spatial acceleration structure
   - Uses a uniform grid for efficient spatial queries
   - Maps world positions to grid cells
   - Supports range-based queries

4. **WorldLightData** - Data structures
   - `WorldLightData`: CPU-side light data structure
   - `WorldLightDataGPU`: GPU-side light data structure (matches shader)

5. **IWorldLightQuery** - Query interface
   - Standardized interface for light queries
   - Supports both List and NativeList result types

## Usage

### Basic Setup

```csharp
// Initialize the cluster
var worldLightCluster = new WorldLightCluster();
worldLightCluster.Initialize(maxLights: 1024, gridResolution: 32, cellSize: 10.0f);

// Update each frame with current scene lights
var worldBounds = CalculateSceneBounds(); // Your scene bounds calculation
worldLightCluster.UpdateCluster(lightData, worldBounds);
```

### Querying Lights

```csharp
// Query lights at a world position
var positionWS = new float3(10.0f, 5.0f, 0.0f);
var maxDistance = 50.0f;
var lightIndices = new List<int>();

int lightCount = worldLightCluster.QueryLights(positionWS, maxDistance, lightIndices);

// Access light data
foreach (var lightIndex in lightIndices)
{
    var lightData = worldLightCluster.GetLightData(lightIndex);
    if (lightData.HasValue)
    {
        // Use light data for path tracing
        var lightPos = lightData.Value.positionWS;
        var lightColor = lightData.Value.color;
        // ...
    }
}
```

### GPU Access

```csharp
// Get GPU buffer for shader access
var gpuBuffer = worldLightCluster.GetGPULightDataBuffer();
if (gpuBuffer != null)
{
    cmd.SetGlobalBuffer("_WorldLightData", gpuBuffer);
    cmd.SetGlobalInt("_WorldLightCount", worldLightCluster.ClusterData.LightCount);
}
```

## Data Structures

### WorldLightData (CPU)

- `positionWS`: World-space position
- `directionWS`: Light direction (normalized)
- `color`: Light color (linear, pre-multiplied by intensity)
- `range`: Light range
- `lightType`: Type of light (Point, Spot, Directional, etc.)
- `spotAngle`: Spot angle (for spot lights)
- `areaSize`: Area light dimensions
- `boundingSphere`: Bounding sphere for culling

### WorldLightDataGPU (GPU/Shader)

GPU-optimized version with proper alignment and precomputed values:
- Precomputed `rangeSquared` for distance calculations
- Precomputed `spotAngleCos/Sin` for spot light calculations
- Proper padding for GPU alignment

## Spatial Grid

The spatial grid uses a uniform 3D grid structure:
- **Grid Resolution**: Number of cells per axis (default: 32)
- **Cell Size**: Size of each cell in world units (default: 10.0f)
- **Total Cells**: `gridResolution^3`

Lights are added to all grid cells that intersect their influence volume (position ± range).

## Integration with Path Tracing

For path tracing, use this system when rays hit surfaces outside the camera view:

```csharp
// In path tracing shader/compute shader
// When ray hits a surface at positionWS:

// Query lights (CPU side)
var lightIndices = new List<int>();
worldLightCluster.QueryLights(hitPositionWS, maxLightDistance, lightIndices);

// Or use GPU buffer directly in shader
// Sample lights from _WorldLightData buffer
```

## Performance Considerations

- **Grid Resolution**: Higher resolution = more memory, faster queries
- **Cell Size**: Should match typical light range in your scene
- **Max Lights**: Set based on expected scene complexity
- **Update Frequency**: Update cluster when lights change, not every frame if possible

## Future Enhancements

- BVH or octree for better spatial partitioning
- GPU-side spatial grid queries
- Hierarchical light clustering (similar to screen-space clustering)
- Support for directional lights (currently focuses on point/spot/area lights)
- Light importance sampling based on intensity/distance

