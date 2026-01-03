# Referenced Path Tracing System

Multi-bounce physically-based path tracing global illumination for Unity URP.

## Overview

The Referenced Path Tracing system provides ground-truth global illumination using hardware-accelerated ray tracing (DXR). It integrates with the WorldLightCluster for efficient light queries at arbitrary world positions.

## Architecture

### Components

1. **ReferencedPathTracingPass.cs** - RenderGraph-based render pass
2. **ReferencedPathTracing.hlsl** - Ray generation and miss shaders
3. **ReferencedPathTracing.raytrace** - Ray tracing shader entry points
4. **RayTracingShaderPassPathTracing.hlsl** - Closest hit shader for Lit materials
5. **LitInputPathTracing.hlsl** - LOD-aware texture sampling for ray tracing
6. **VividRuntimeShader.GlobalIllumination.cs** - Shader resource management

### Data Flow

```
ReferencedPathTracingPass (CPU)
    ↓ RecordRenderGraph
    ↓ Initialize PassData
    ↓ ExecutePathTracing (GPU)
        ↓ RayGenPathTracing (primary rays)
            ↓ TraceRay
                ↓ ClosestHitPathTracing (Lit.shader)
                    ↓ Sample material (LitInputPathTracing)
                    ↓ Query lights (WorldLightCluster)
                    ↓ Evaluate BRDF
                    ↓ Sample next bounce direction
                ↓ MissShaderPathTracing (environment)
            ↓ Accumulate radiance
            ↓ Russian Roulette termination
        ↓ Output accumulated radiance
```

## Key Features

### Ray Tracing Integration
- **Hardware Ray Tracing**: Uses DXR for acceleration
- **RenderGraph Integration**: Modern URP rendering architecture
- **Acceleration Structure**: Automatic RTAS management via RayTracingSystem
- **Shader Pass**: "PathTracingDXR" pass in Lit shader

### Material System
- **Full Lit Shader Support**: All material properties work in path tracing
- **LOD-Aware Sampling**: `SAMPLE_TEXTURE2D_LOD` for ray tracing compatibility
- **Distance-Based LOD**: Automatic mip level calculation based on ray distance
- **Bounce-Based LOD**: Progressive LOD increase for secondary bounces
- **Alpha Testing**: Proper any-hit shader for foliage/cutouts

### Lighting System
- **WorldLightCluster Integration**: Out-of-view light queries
- **Direct Lighting**: Per-bounce light evaluation
- **BRDF**: Cook-Torrance for specular, Lambert for diffuse
- **Environment Lighting**: Sky/cubemap sampling in miss shader
- **Emission**: Self-emissive materials

### Path Tracing Algorithms
- **Multi-Bounce GI**: Configurable bounce depth (default: 4)
- **Importance Sampling**: Cosine-weighted hemisphere for diffuse
- **Russian Roulette**: Path termination for efficiency
- **Firefly Clamping**: Noise reduction for high-radiance samples
- **Temporal Accumulation**: Progressive refinement across frames
- **NVIDIA SER**: Shader Execution Reordering for ray coherence (optional)

### Performance Features
- **Blue Noise Sampling**: Dithered texture set for low-discrepancy samples
- **Frame Index**: Temporal variation for progressive sampling
- **History Buffers**: Ping-pong buffers for accumulation
- **SER Support**: Automatic detection of NVIDIA hardware extension

## Usage

### Basic Setup

```csharp
// In your renderer feature or pass setup
var pathTracingPass = new ReferencedPathTracingPass();
pathTracingPass.Setup(
    maxBounces: 4,           // Maximum path depth
    samplesPerPixel: 1,      // Samples per frame
    fireflyClamp: 10.0f,     // Radiance clamp threshold
    useNVSER: true,          // Enable NVIDIA SER
    accumulate: true         // Temporal accumulation
);
renderer.EnqueuePass(pathTracingPass);
```

### Shader Resources

Shader resources are automatically loaded from `VividRuntimeShader`:

```csharp
var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<VividRuntimeShader>();
var pathTracingShader = runtimeShaders.referencedPathTracingRTShader;
```

### Shader Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `_PathTracingMaxBounces` | `int` | Maximum ray bounces (1-8) |
| `_PathTracingSamplesPerPixel` | `int` | Samples per pixel per frame |
| `_PathTracingFireflyClamp` | `float` | Max radiance to prevent fireflies |
| `_PathTracingFrameIndex` | `int` | Frame counter for temporal sampling |
| `_UseNVSER` | `float` | Enable NVIDIA SER (1.0 = on) |
| `_PathTracingAccumulate` | `int` | Enable temporal accumulation |
| `_PathTracingOutput` | `RWTexture2D<float4>` | Output radiance buffer |
| `_PathTracingHistory` | `Texture2D<float4>` | Previous frame accumulation |

### Input Textures

| Texture | Usage |
|---------|-------|
| `_GBuffer0` | Albedo (RGB) + AO (A) |
| `_GBuffer1` | Specular/Metallic (RGB) + Roughness (A) |
| `_GBuffer2` | World space normal (RGB) |
| `_CameraDepthTexture` | Scene depth |
| `_SkyTexture` | Environment cubemap |

## Implementation Details

### RenderGraph Pass Structure

```csharp
public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
{
    // 1. Validate ray tracing support
    // 2. Create output texture
    // 3. Setup history buffer
    // 4. Add compute pass with PassData
    // 5. Bind all resources
    // 6. Execute path tracing
    // 7. Increment frame index
}
```

### PassData Structure

```csharp
class PassData
{
    // Input textures (GBuffer, depth)
    // Output texture
    // History texture
    // Ray tracing resources (shader, RTAS)
    // Constant buffers
    // Parameters (bounces, samples, etc.)
}
```

### Execution Flow

1. **Setup Phase** (RecordRenderGraph):
   - Initialize ray tracing resources
   - Allocate output and history textures
   - Setup constant buffers
   - Register texture dependencies

2. **Execution Phase** (ExecutePathTracing):
   - Set ray tracing shader pass
   - Bind acceleration structure
   - Bind input/output textures
   - Set shader parameters
   - Dispatch rays

3. **Ray Generation** (RayGenPathTracing):
   - Generate primary rays from camera
   - Initialize payload (radiance, throughput, seed)
   - Trace ray
   - Accumulate with history

4. **Closest Hit** (ClosestHitPathTracing):
   - Sample material with LOD
   - Query WorldLightCluster for lights
   - Evaluate BRDF
   - Sample next bounce direction
   - Update payload

5. **Miss Shader** (MissShaderPathTracing):
   - Sample environment/sky
   - Return background radiance

### LOD Calculation

```hlsl
// Distance-based LOD
float textureLOD = ComputeTextureLODFromDistance(hitDistance, 1.0);

// Add LOD bias for secondary bounces
textureLOD += payload.bounceCount * 0.5;

// Sample with explicit LOD
InitializeStandardLitSurfaceDataRT(uv, textureLOD, surfaceData);
```

### Light Query Integration

```hlsl
// Query lights from WorldLightCluster
WorldLightIterator iter = WorldLightIteratorInit(hitPositionWS);
uint lightIdx;

while (WorldLightIteratorNext(iter, lightIdx))
{
    GPULightData light = GetWorldLight(lightIdx);
    
    // Evaluate direct lighting
    float3 directLight = EvaluateLighting(light, hitPositionWS, normalWS, ...);
    payload.radiance += payload.throughput * directLight;
}
```

### NVIDIA SER Integration

```hlsl
UNITY_BRANCH
if (_UseNVSER)
{
    NvHitObject hitObject;
    NvTraceRayHitObject(_RaytracingAccelerationStructure, ...);
    NvReorderThread(hitObject); // Coherence hint
    NvInvokeHitObject(_RaytracingAccelerationStructure, hitObject);
}
else
{
    TraceRay(_RaytracingAccelerationStructure, ...);
}
```

## Performance Considerations

### Optimization Strategies

1. **LOD Management**:
   - Use distance-based LOD to reduce bandwidth
   - Increase LOD for secondary bounces
   - Keep LOD 0 for alpha testing (accuracy)

2. **Bounce Count**:
   - 1-2 bounces: Fast, suitable for real-time
   - 3-4 bounces: Balanced quality/performance
   - 5+ bounces: High quality, slower

3. **Samples Per Pixel**:
   - 1 SPP + accumulation: Progressive refinement
   - 2-4 SPP: Better convergence per frame
   - 8+ SPP: High quality, expensive

4. **Russian Roulette**:
   - Starts after bounce 3
   - Survival probability based on throughput
   - Reduces unnecessary rays

5. **NVIDIA SER**:
   - ~20-40% performance improvement on RTX GPUs
   - Automatic detection and fallback
   - Reorders divergent rays for coherence

### Memory Usage

- **Output Buffer**: Width × Height × 16 bytes (RGBA16F)
- **History Buffer**: Width × Height × 16 bytes × 2 (ping-pong)
- **RTAS**: Scene geometry dependent
- **Shader Constants**: ~1KB

## Limitations

1. **Platform Support**: DXR-capable GPUs (DX12, Vulkan)
2. **Transparency**: Limited to alpha-tested materials
3. **Denoising**: Not yet implemented (raw path tracing output)
4. **Compositing**: Manual integration with main rendering required
5. **Sky Texture**: Currently defaults to black (needs integration)

## Future Work

- [ ] Temporal denoising (NRD integration)
- [ ] Spatial denoising for low sample counts
- [ ] Automatic compositing with forward/deferred rendering
- [ ] Sky texture binding from environment settings
- [ ] GGX importance sampling for specular
- [ ] Multiple importance sampling (MIS)
- [ ] Translucent material support (refraction)
- [ ] Light sampling (next event estimation)
- [ ] Volumetric scattering
- [ ] Adaptive sampling

## References

- [Ray Tracing Gems](http://www.realtimerendering.com/raytracinggems/)
- [Physically Based Rendering (PBR Book)](https://www.pbr-book.org/)
- [NVIDIA Ray Tracing Documentation](https://developer.nvidia.com/rtx)
- [Unity DXR Documentation](https://docs.unity3d.com/Manual/com.unity.render-pipelines.high-definition-raytracing.html)


