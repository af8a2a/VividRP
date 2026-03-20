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

### RenderGraphAccelerationStructure
**Location:** `Runtime/RenderGraph/Resource/RenderGraphAccelerationStructureDesc.cs`

A serializable RenderGraph resource wrapper around Unity's `RayTracingAccelerationStructure`.
It can lazily create a native acceleration structure from the serialized descriptor or wrap an externally managed one.

### AccelerationStructureResourceNodeData
**Location:** `Editor/RenderGraph/Nodes/AccelerationStructureResourceNodeData.cs`

A dedicated resource node for authoring RTAS descriptors directly in the RenderGraph editor.

### RTASBuildPass
**Location:** `Runtime/RenderPass/Core/RTASBuildPass.cs`

A compute pass that builds a scene RTAS into a RenderGraph resource without relying on
`SetGlobalRayTracingAccelerationStructure`, so the resource can flow explicitly to downstream passes.

## Integration with Existing System

The acceleration structure descriptor integrates seamlessly with the existing descriptor system:

- Uses the same namespace (`VividRP.Runtime`) as other descriptors
- Follows the same naming convention (`RenderGraph[Type]Desc`)
- Provides conversion methods to/from Unity's native types
- Includes factory methods for common use cases

## Usage Patterns

### Building an Acceleration Structure

Connect an `AccelerationStructureResourceNodeData` node to `RTASBuildPass` when you want an authored RTAS descriptor,
or use the pass-owned default RTAS output directly and forward it to downstream ray tracing passes.

### Using an Acceleration Structure for Ray Tracing

In a downstream pass, declare a `[RenderGraphResource] RenderGraphAccelerationStructure` field with `AccessFlags.Read`
and bind it from `RTASBuildPass` through the graph. At record time, use the wrapped native object with
`SetRayTracingAccelerationStructure(...)` or other ray tracing command buffer APIs.

## Benefits

1. **Explicit Dependencies** - RTAS lifetime and ordering stay inside RenderGraph instead of hidden global state
2. **Async Compute Friendly** - RTAS build and consumer passes can use explicit resource edges
3. **Serialization** - Descriptor and node data are stored in graph assets
4. **Integration** - Works alongside existing texture, buffer, history, and render-list resources
5. **Ray Tracing Ready** - Downstream passes can consume the same RTAS resource directly

## Future Enhancements

1. **Scene Culling Controls** - More authoring-time control over RTAS build scope and filtering
2. **Ray Tracing Pass Templates** - Prebuilt nodes for common ray tracing workflows
3. **Inline Ray Tracing** - Support for inline ray tracing in raster passes
4. **RTAS Validation** - Additional editor/runtime validation for unsupported hardware or pass layouts

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
