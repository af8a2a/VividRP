# RenderGraph Serializable Resource Descriptors

## Overview

VividRP's RenderGraph now includes serializable resource descriptor classes that mirror Unity's `TextureDesc` and `BufferDesc` but can be fully serialized in assets. This allows pass nodes to define resource properties declaratively.

## Core Classes

### RenderGraphTextureDesc
Location: `Runtime/RenderGraph/Data/RenderGraphTextureDesc.cs`

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
Location: `Runtime/RenderGraph/Data/RenderGraphBufferDesc.cs`

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

## Port Integration

### RenderGraphPortData
Location: `Runtime/RenderGraph/Data/RenderGraphPortData.cs`

Ports now optionally store resource descriptors:
- `TextureDesc` - For texture ports
- `BufferDesc` - For buffer ports

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

## Benefits

1. **Declarative Configuration** - Resource properties are defined in the asset, not hardcoded
2. **Editor Integration** - Descriptors can be exposed in custom inspectors for visual editing
3. **Reusability** - Descriptors can be shared between ports and nodes
4. **Type Safety** - Compile-time checking of descriptor properties
5. **Extensibility** - Easy to add new descriptor types for future resource types
6. **Serialization** - Full Unity serialization support with `[SerializeReference]`

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
bufferNode.BufferDesc.Target = ComputeBufferType.Append;

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
