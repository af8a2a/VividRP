# RenderGraph Serializable Resource Descriptors

## Overview

VividRP's RenderGraph now includes serializable resource descriptor classes that mirror Unity's `TextureDesc`, `BufferDesc`, and `RayTracingAccelerationStructureDesc` but can be fully serialized in assets. This allows pass nodes to define resource properties declaratively.

## Core Classes

### RenderGraphTextureDesc
Location: `Runtime/RenderGraph/Resource/RenderGraphTextureDesc.cs`

Serializable texture descriptor with full support for:
- Dimensions (width, height, slices, dimension)
- Format (color format, depth bits)
- Sampling (MSAA, filter mode, wrap mode, aniso level, mip bias)
- Mip maps (enable, auto-generate, count)
- Clear settings (clear buffer, clear color)
- Flags (random write, bind MS, dynamic scale)
- Metadata (name, fallback texture)

**Key Methods:**
- `ToTextureDesc()` - Converts to Unity's `TextureDesc`
- `FromTextureDesc(TextureDesc)` - Creates from Unity's descriptor
- `CreateColorTarget(width, height, format)` - Factory for color targets
- `CreateDepthTarget(width, height, depthBits)` - Factory for depth targets

### RenderGraphBufferDesc
Location: `Runtime/RenderGraph/Resource/RenderGraphBufferDesc.cs`

Serializable buffer descriptor with support for:
- Count and stride
- Buffer target type (Default, Structured, Append, IndirectArguments, etc.)
- Name metadata

**Key Methods:**
- `ToBufferDesc()` - Converts to Unity's `BufferDesc`
- `FromBufferDesc(BufferDesc)` - Creates from Unity's descriptor
- `CreateStructured(count, stride)` - Factory for structured buffers
- `CreateAppend(count, stride)` - Factory for append/consume buffers
- `CreateIndirectArguments(count)` - Factory for indirect args buffers

### RenderGraphAccelerationStructureDesc
Location: `Runtime/RenderGraph/Resource/RenderGraphAccelerationStructureDesc.cs`

Serializable acceleration structure descriptor for ray tracing:
- Name metadata for debugging and profiling

**Key Methods:**
- `ToAccelerationStructureDesc()` - Converts to Unity's `RayTracingAccelerationStructureDesc`
- `FromAccelerationStructureDesc(RayTracingAccelerationStructureDesc)` - Creates from Unity's descriptor
- `Create(name)` - Factory for acceleration structures

## Port Integration

### RenderGraphPortData
Location: `Runtime/RenderGraph/Data/RenderGraphPortData.cs`

Ports now optionally store resource descriptors:
- `TextureDesc` - For texture ports
- `BufferDesc` - For buffer ports
- `AccelerationStructureDesc` - For acceleration structure ports (ray tracing)

**Key Methods:**
- `GetDescriptor()` - Returns the appropriate descriptor based on port type
- `SetDescriptor(object)` - Sets the descriptor and clears the other type

## Node Updates

All resource and pass nodes have been updated to use the new descriptor system:

### Resource Nodes

**TextureNodeData**
- Now uses `RenderGraphTextureDesc` instead of individual fields
- Descriptor is initialized with `CreateColorTarget(1920, 1080)`
- Port stores reference to the descriptor

**BufferNodeData**
- Now uses `RenderGraphBufferDesc` instead of Count/Stride fields
- Descriptor is initialized with `CreateStructured(1, 4)`
- Port stores reference to the descriptor

**HistoryTextureNodeData**
- Uses `RenderGraphTextureDesc` with `TextureSizeMode` support
- Both Current and History ports reference the same descriptor
- Resolves size based on camera or explicit dimensions

**HistoryBufferNodeData**
- Uses `RenderGraphBufferDesc`
- Both Current and History ports reference the same descriptor

### Pass Nodes

**FullScreenPassNodeData**
- Now uses `RenderGraphTextureDesc` for output texture
- Descriptor is initialized with `CreateColorTarget(1920, 1080)`
- Output port stores reference to the descriptor

**RasterPassNodeData**
- Uses data-driven approach with reflection-based compilation
- Creates transient attachments using existing serialized fields
- Can be extended to use descriptors per-attachment in the future

## Ray Tracing Support

VividRP now includes full support for ray tracing acceleration structures through the descriptor system.

1. **Authoring RTAS Resources** - `Editor/RenderGraph/Nodes/AccelerationStructureResourceNodeData.cs` exposes RTAS descriptors in the graph editor
2. **Building Scene RTAS** - `Runtime/RenderPass/Core/RTASBuildPass.cs` builds a scene RTAS into a RenderGraph resource
3. **Passing RTAS Between Passes** - downstream passes can consume `RenderGraphAccelerationStructure` fields through explicit graph edges

### Ray Tracing Usage Example

```csharp
// Producer pass
[RenderGraphResource(Name = "SceneRTAS", Access = AccessFlags.Write)]
private RenderGraphAccelerationStructure m_SceneAccelerationStructure = new();

// Consumer pass
[RenderGraphResource(Name = "SceneRTAS", Access = AccessFlags.Read)]
private RenderGraphAccelerationStructure m_SceneAccelerationStructure = new();
```

## Benefits

1. **Declarative Configuration** - Resource properties are defined in the asset, not hardcoded
2. **Editor Integration** - Descriptors can be exposed in custom inspectors for visual editing
3. **Reusability** - Descriptors can be shared between ports and nodes
4. **Type Safety** - Compile-time checking of descriptor properties
5. **Extensibility** - Easy to add new descriptor types for future resource types
6. **Serialization** - Full Unity serialization support with `[SerializeReference]`
7. **Ray Tracing Ready** - First-class support for acceleration structures

## Usage Example

```csharp
// Create a texture node with custom descriptor
var textureNode = new TextureNodeData();
textureNode.TextureDesc = RenderGraphTextureDesc.CreateColorTarget(2048, 2048, GraphicsFormat.R16G16B16A16_SFloat);
textureNode.TextureDesc.UseMipMap = true;
textureNode.TextureDesc.AutoGenerateMips = true;

// Create a buffer node with custom descriptor
var bufferNode = new BufferNodeData();
bufferNode.BufferDesc = RenderGraphBufferDesc.CreateStructured(1024, 16);
bufferNode.BufferDesc.Target = GraphicsBuffer.Target.Append;

// Create an acceleration structure descriptor
var accelStructDesc = RenderGraphAccelerationStructureDesc.Create("MyAccelerationStructure");

// Access descriptor from port
var port = textureNode.Ports[0];
var desc = port.TextureDesc; // Returns RenderGraphTextureDesc
```

## Future Enhancements

1. **Editor UI** - Custom property drawers for descriptors in the graph editor
2. **Descriptor Presets** - Library of common descriptor configurations
3. **Validation** - Runtime validation of descriptor properties
4. **Per-Attachment Descriptors** - RasterPassNodeData could use descriptors per color/depth attachment
5. **Descriptor Inheritance** - Ports could inherit descriptors from connected ports
6. **Ray Tracing Node** - Dedicated node type for ray tracing passes with RTAS inputs
