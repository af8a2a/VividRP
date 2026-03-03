# AccelerationStructure Support for VividRP RenderGraph

## Overview

VividRP's RenderGraph now includes full support for ray tracing acceleration structures through the serializable descriptor system. This enables declarative configuration of ray tracing resources in the graph editor.

## New Components

### RenderGraphAccelerationStructureDesc
**Location:** `Runtime/RenderGraph/Resource/RenderGraphAccelerationStructureDesc.cs`

A serializable descriptor class that mirrors Unity's `RayTracingAccelerationStructureDesc` but can be fully serialized in assets.

**Properties:**
- `Name` - String identifier for debugging and profiling

**Methods:**
- `ToAccelerationStructureDesc()` - Converts to Unity's `RayTracingAccelerationStructureDesc`
- `FromAccelerationStructureDesc(desc)` - Creates from Unity's descriptor
- `Create(name)` - Static factory method for creating new descriptors

### Example Passes
**Location:** `Runtime/RenderGraph/RayTracingPassExample.cs`

Two example pass implementations demonstrating acceleration structure usage:

1. **RayTracingAccelerationStructurePass** - Builds a ray tracing acceleration structure from scene geometry
2. **RayTracingPass** - Uses an acceleration structure for ray queries in a compute shader

## Integration with Existing System

The acceleration structure descriptor integrates seamlessly with the existing descriptor system:

- Uses the same namespace (`VividRP.Runtime`) as other descriptors
- Follows the same naming convention (`RenderGraph[Type]Desc`)
- Provides conversion methods to/from Unity's native types
- Includes factory methods for common use cases

## Usage Patterns

### Building an Acceleration Structure

```csharp
// Create descriptor
var accelStructDesc = RenderGraphAccelerationStructureDesc.Create("SceneAccelerationStructure");

// Build in RenderGraph pass
using (var builder = renderGraph.AddComputePass<PassData>("Build RTAS", out var passData))
{
    var rtasHandle = renderGraph.ImportRayTracingAccelerationStructure(accelStruct);
    builder.UseAccelerationStructure(rtasHandle);

    builder.SetRenderFunc<PassData>((data, context) =>
    {
        context.cmd.BuildRayTracingAccelerationStructure(accelStruct);
    });
}
```

### Using an Acceleration Structure for Ray Tracing

```csharp
// Create output texture with random write enabled
var outputDesc = RenderGraphTextureDesc.CreateColorTarget(1920, 1080, GraphicsFormat.R16G16B16A16_SFloat);
outputDesc.EnableRandomWrite = true;

// Use in compute pass
using (var builder = renderGraph.AddComputePass<PassData>("Ray Tracing", out var passData))
{
    var outputTexture = renderGraph.CreateTexture(outputDesc.ToTextureDesc());
    var rtasHandle = renderGraph.ImportRayTracingAccelerationStructure(accelStruct);

    builder.UseTexture(outputTexture, AccessFlags.Write);
    builder.UseAccelerationStructure(rtasHandle);

    builder.SetRenderFunc<PassData>((data, context) =>
    {
        context.cmd.SetComputeTextureParam(shader, kernel, "_OutputTexture", outputTexture);
        context.cmd.SetComputeRayTracingAccelerationStructureParam(shader, kernel, "_AccelStruct", accelStruct);
        context.cmd.DispatchCompute(shader, kernel, width / 8, height / 8, 1);
    });
}
```

## Benefits

1. **Declarative Configuration** - Acceleration structure properties defined in assets
2. **Type Safety** - Compile-time checking of descriptor properties
3. **Serialization** - Full Unity serialization support
4. **Integration** - Works seamlessly with existing texture and buffer descriptors
5. **Ray Tracing Ready** - First-class support for modern ray tracing workflows

## Future Enhancements

1. **AccelerationStructureNodeData** - Dedicated node type for RTAS resources in the graph editor
2. **Ray Tracing Pass Node** - Specialized pass node with RTAS input ports
3. **RTAS Builder Node** - Node that builds acceleration structures from geometry
4. **Inline Ray Tracing** - Support for inline ray tracing in raster passes
5. **RTAS Validation** - Runtime validation of acceleration structure properties

## Hardware Requirements

Ray tracing acceleration structures require:
- DirectX 12 with DXR support (Windows)
- Vulkan with ray tracing extensions (Windows, Linux)
- Metal with ray tracing support (macOS, iOS)
- Hardware with ray tracing capabilities (NVIDIA RTX, AMD RDNA 2+, Intel Arc, Apple Silicon)

## Shader Integration

Example compute shader using acceleration structure:

```hlsl
#pragma kernel RayTraceMain

RWTexture2D<float4> _OutputTexture;
RaytracingAccelerationStructure _AccelStruct;

[numthreads(8, 8, 1)]
void RayTraceMain(uint3 id : SV_DispatchThreadID)
{
    // Setup ray
    RayDesc ray;
    ray.Origin = float3(0, 0, 0);
    ray.Direction = normalize(float3(id.xy, 1));
    ray.TMin = 0.001;
    ray.TMax = 1000.0;

    // Trace ray
    RayQuery<RAY_FLAG_NONE> query;
    query.TraceRayInline(_AccelStruct, RAY_FLAG_NONE, 0xFF, ray);
    query.Proceed();

    // Write result
    float4 color = query.CommittedStatus() == COMMITTED_TRIANGLE_HIT
        ? float4(1, 1, 1, 1)
        : float4(0, 0, 0, 1);
    _OutputTexture[id.xy] = color;
}
```

## See Also

- `RenderGraphTextureDesc.cs` - Texture descriptor implementation
- `RenderGraphBufferDesc.cs` - Buffer descriptor implementation
- `RenderGraphResourceDescriptors.md` - Complete descriptor system documentation
- Unity RenderGraph documentation - Official Unity RenderGraph API reference
