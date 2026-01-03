# Global Illumination Volume Component - Path Tracing Settings

Complete guide to configuring Referenced Path Tracing through the Volume Component system.

## Overview

The **Global Illumination** volume component provides comprehensive control over path tracing settings. Settings can be configured globally or per-volume for artistic control.

## Accessing the Volume Component

1. Create a **Global Volume** or **Local Volume** in your scene
2. Add **Override** → **Lighting** → **Global Illumination**
3. Enable desired parameter overrides (checkboxes)
4. Configure settings

## Volume Component Structure

```csharp
[VolumeComponentMenu("Lighting/Global Illumination")]
public sealed partial class GlobalIllumination : VolumeComponent
```

## Settings Categories

### 1. General Settings

#### Enable Path Tracing
- **Property**: `enablePathTracing`
- **Type**: `BoolParameter`
- **Default**: `false`
- **Description**: Master switch for path tracing global illumination

```csharp
var gi = VolumeManager.instance.stack.GetComponent<GlobalIllumination>();
if (gi.IsPathTracingActive()) { /* ... */ }
```

#### Technique
- **Property**: `technique`
- **Type**: `GlobalIlluminationTechniqueParameter`
- **Values**: `Disabled`, `ReferencedPathTracing`
- **Default**: `Disabled`
- **Description**: Selects GI technique

#### Path Tracing Quality
- **Property**: `pathTracingQuality`
- **Type**: `PathTracingQualityParameter`
- **Values**: 
  - `Low` - 1 bounce, 1 SPP (fast, real-time)
  - `Medium` - 2 bounces, 1 SPP (balanced)
  - `High` - 4 bounces, 2 SPP (high quality)
  - `Ultra` - 8 bounces, 4 SPP (maximum quality)
  - `Custom` - Manual control
- **Default**: `Medium`
- **Description**: Quality preset that automatically configures bounces and samples

#### Path Tracing Intensity
- **Property**: `pathTracingIntensity`
- **Type**: `ClampedFloatParameter`
- **Range**: 0.0 - 10.0
- **Default**: `1.0`
- **Description**: Global intensity multiplier for indirect lighting

**Usage Example**:
```csharp
// Boost GI contribution for a dark scene
gi.pathTracingIntensity.value = 2.0f;
```

---

### 2. Ray Tracing Settings

#### Max Bounces
- **Property**: `maxBounces`
- **Type**: `ClampedIntParameter`
- **Range**: 1 - 8
- **Default**: `4`
- **Description**: Maximum ray bounces per path
- **Performance Impact**: High - Each bounce multiplies cost
- **Quality Impact**: High - More bounces = more accurate indirect lighting

**Guidelines**:
- **1 bounce**: Direct lighting only (AO-like)
- **2 bounces**: Single indirect bounce (good for real-time)
- **4 bounces**: Full global illumination (balanced)
- **8+ bounces**: Reference quality (slow)

#### Samples Per Pixel
- **Property**: `samplesPerPixel`
- **Type**: `ClampedIntParameter`
- **Range**: 1 - 16
- **Default**: `1`
- **Description**: Samples per pixel per frame
- **Performance Impact**: Very High - Linear cost scaling
- **Quality Impact**: High - More samples = less noise

**Strategy**:
- Use `1 SPP` + temporal accumulation for progressive refinement
- Use `2-4 SPP` for faster convergence with less accumulation
- Use `8+ SPP` for static scenes with minimal temporal artifacts

#### Ray Length
- **Property**: `rayLength`
- **Type**: `MinFloatParameter`
- **Range**: 0.1 - ∞
- **Default**: `100.0`
- **Description**: Maximum ray travel distance in world units

#### Layer Mask
- **Property**: `layerMask`
- **Type**: `LayerMaskParameter`
- **Default**: `-1` (all layers)
- **Description**: Layers to include in path tracing
- **Use Case**: Exclude specific objects (e.g., UI, particles)

---

### 3. Quality Settings

#### Use Russian Roulette
- **Property**: `useRussianRoulette`
- **Type**: `BoolParameter`
- **Default**: `true`
- **Description**: Enable probabilistic path termination
- **Performance Impact**: Medium - 15-30% speedup
- **Quality Impact**: Minimal - Unbiased when implemented correctly

#### Russian Roulette Start Bounce
- **Property**: `russianRouletteStartBounce`
- **Type**: `ClampedIntParameter`
- **Range**: 1 - 8
- **Default**: `3`
- **Description**: Minimum bounces before RR can terminate paths

#### Firefly Clamp
- **Property**: `fireflyClamp`
- **Type**: `ClampedFloatParameter`
- **Range**: 0.0 - 100.0
- **Default**: `10.0`
- **Description**: Maximum radiance to prevent bright noise pixels
- **Quality Impact**: Can darken very bright surfaces

**Tuning**:
- **Low values (1-5)**: Aggressive firefly reduction, may darken specular
- **Medium values (10-20)**: Balanced
- **High values (50+)**: Minimal clamping, more noise

#### Use NVIDIA SER
- **Property**: `useNVSER`
- **Type**: `BoolParameter`
- **Default**: `true`
- **Description**: Enable Shader Execution Reordering on RTX GPUs
- **Performance Impact**: 20-40% speedup on RTX 3000/4000 series
- **Compatibility**: Automatic fallback on non-RTX hardware

#### Texture LOD Bias
- **Property**: `textureLODBias`
- **Type**: `ClampedFloatParameter`
- **Range**: 0.0 - 4.0
- **Default**: `0.5`
- **Description**: LOD bias for material texture sampling
- **Performance Impact**: Medium - Reduces bandwidth
- **Quality Impact**: May blur textures at distance

---

### 4. Temporal Accumulation

#### Temporal Accumulation
- **Property**: `temporalAccumulation`
- **Type**: `BoolParameter`
- **Default**: `true`
- **Description**: Accumulate samples across frames for progressive refinement

#### Max Accumulated Frames
- **Property**: `maxAccumulatedFrames`
- **Type**: `ClampedIntParameter`
- **Range**: 1 - 1024
- **Default**: `64`
- **Description**: Maximum frames to accumulate
- **Quality Impact**: Higher values = cleaner but slower convergence

**Convergence Time**:
```
Time to converge = maxAccumulatedFrames × samplesPerPixel / FPS
Example: 64 frames × 1 SPP @ 60 FPS = ~1 second
```

#### Reset On Camera Move
- **Property**: `resetOnCameraMove`
- **Type**: `BoolParameter`
- **Default**: `true`
- **Description**: Reset accumulation when camera moves
- **Use Case**: Prevents ghosting but causes flicker during movement

#### Camera Movement Threshold
- **Property**: `cameraMovementThreshold`
- **Type**: `ClampedFloatParameter`
- **Range**: 0.0 - 1.0
- **Default**: `0.01`
- **Description**: Minimum camera movement (world units) to trigger reset

---

### 5. Denoising

#### Denoise Mode
- **Property**: `denoiseMode`
- **Type**: `PathTracingDenoiseModeParameter`
- **Values**:
  - `None` - Raw path tracing output
  - `Temporal` - Accumulation only (default)
  - `SpatialTemporal` - Bilateral filter + accumulation
  - `NRD` - NVIDIA Real-time Denoisers (requires integration)
- **Default**: `Temporal`

#### Denoise Radius
- **Property**: `denoiseRadius`
- **Type**: `ClampedFloatParameter`
- **Range**: 1.0 - 32.0
- **Default**: `8.0`
- **Description**: Spatial filter radius (for SpatialTemporal mode)

#### Use NRD
- **Property**: `useNRD`
- **Type**: `BoolParameter`
- **Default**: `false`
- **Description**: Use NVIDIA Real-time Denoisers
- **Requires**: NRD integration

---

### 6. Advanced Settings

#### Environment Intensity
- **Property**: `environmentIntensity`
- **Type**: `ClampedFloatParameter`
- **Range**: 0.0 - 10.0
- **Default**: `1.0`
- **Description**: Intensity multiplier for sky/environment lighting

#### Include Emissive
- **Property**: `includeEmissive`
- **Type**: `BoolParameter`
- **Default**: `true`
- **Description**: Include emissive materials as light sources

#### Include Direct Lighting
- **Property**: `includeDirectLighting`
- **Type**: `BoolParameter`
- **Default**: `true`
- **Description**: Evaluate direct lighting from WorldLightCluster
- **Use Case**: Disable to see only indirect (bounced) light

#### Receiver Motion Rejection
- **Property**: `receiverMotionRejection`
- **Type**: `BoolParameter`
- **Default**: `true`
- **Description**: Reject accumulated samples for moving objects
- **Quality Impact**: Reduces ghosting but increases noise

---

### 7. Debug Settings

#### Debug Visualize Bounce
- **Property**: `debugVisualizeBounce`
- **Type**: `ClampedIntParameter`
- **Range**: 0 - 8
- **Default**: `0` (all bounces)
- **Description**: Show only specific bounce number
- **Use Cases**:
  - `0` - All bounces (normal)
  - `1` - First bounce only (direct lighting)
  - `2` - Second bounce only (first indirect)
  - `3+` - Higher order bounces

#### Debug Show Path Tracing Only
- **Property**: `debugShowPathTracingOnly`
- **Type**: `BoolParameter`
- **Default**: `false`
- **Description**: Show path tracing output without compositing

---

## API Reference

### Helper Methods

#### IsPathTracingActive()
```csharp
public bool IsPathTracingActive()
```
Returns `true` if path tracing is enabled and active.

**Example**:
```csharp
var gi = VolumeManager.instance.stack.GetComponent<GlobalIllumination>();
if (gi.IsPathTracingActive())
{
    // Path tracing is running
}
```

#### GetMaxBounces()
```csharp
public int GetMaxBounces()
```
Returns actual max bounces based on quality preset or custom value.

#### GetSamplesPerPixel()
```csharp
public int GetSamplesPerPixel()
```
Returns actual samples per pixel based on quality preset or custom value.

---

## Quality Presets Breakdown

| Preset | Bounces | SPP | Use Case | Target FPS |
|--------|---------|-----|----------|------------|
| **Low** | 1 | 1 | Real-time, fast preview | 60+ |
| **Medium** | 2 | 1 | Balanced quality/performance | 30-60 |
| **High** | 4 | 2 | High quality, cinematics | 15-30 |
| **Ultra** | 8 | 4 | Reference, static scenes | < 15 |
| **Custom** | User | User | Full control | Varies |

---

## Usage Examples

### Example 1: Real-time Preview Setup
```csharp
var gi = VolumeManager.instance.stack.GetComponent<GlobalIllumination>();
gi.technique.value = GlobalIlluminationTechnique.ReferencedPathTracing;
gi.enablePathTracing.value = true;
gi.pathTracingQuality.value = PathTracingQuality.Low;
gi.temporalAccumulation.value = true;
gi.maxAccumulatedFrames.value = 32;
```

### Example 2: High Quality Cinematic
```csharp
var gi = VolumeManager.instance.stack.GetComponent<GlobalIllumination>();
gi.technique.value = GlobalIlluminationTechnique.ReferencedPathTracing;
gi.enablePathTracing.value = true;
gi.pathTracingQuality.value = PathTracingQuality.Ultra;
gi.temporalAccumulation.value = true;
gi.maxAccumulatedFrames.value = 256;
gi.fireflyClamp.value = 20.0f;
gi.useNVSER.value = true;
```

### Example 3: Custom Configuration
```csharp
var gi = VolumeManager.instance.stack.GetComponent<GlobalIllumination>();
gi.technique.value = GlobalIlluminationTechnique.ReferencedPathTracing;
gi.enablePathTracing.value = true;
gi.pathTracingQuality.value = PathTracingQuality.Custom;
gi.maxBounces.value = 3;
gi.samplesPerPixel.value = 2;
gi.fireflyClamp.value = 15.0f;
gi.pathTracingIntensity.value = 1.5f;
```

### Example 4: Debug First Bounce Only
```csharp
var gi = VolumeManager.instance.stack.GetComponent<GlobalIllumination>();
gi.debugVisualizeBounce.value = 1; // Show only direct lighting
gi.debugShowPathTracingOnly.value = true;
```

---

## Performance Tuning Guide

### Target: 60 FPS @ 1080p
```
Quality: Low
Bounces: 1-2
SPP: 1
Accumulation: 16-32 frames
SER: Enabled
```

### Target: 30 FPS @ 1080p
```
Quality: Medium
Bounces: 2-3
SPP: 1
Accumulation: 32-64 frames
SER: Enabled
```

### Target: Reference Quality (< 15 FPS)
```
Quality: Ultra
Bounces: 6-8
SPP: 4
Accumulation: 128-256 frames
SER: Enabled
Firefly Clamp: High (20-50)
```

---

## Integration with ReferencedPathTracingPass

The volume settings automatically guide the `ReferencedPathTracingPass`:

```csharp
public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
{
    // Get volume settings
    var gi = VolumeManager.instance.stack.GetComponent<GlobalIllumination>();
    
    // Check if enabled
    if (!gi.IsPathTracingActive())
        return;
    
    // Settings are automatically applied via ApplyVolumeSettings()
    ApplyVolumeSettings(gi);
    
    // Pass parameters are set from volume in InitializePassData()
    // ...
}
```

All volume parameters are automatically bound to shaders through `ExecutePathTracing()`.

---

## Troubleshooting

### Issue: No GI visible
- Check `enablePathTracing` is `true`
- Check `technique` is `ReferencedPathTracing`
- Verify ray tracing is supported (DXR capable GPU)
- Check `pathTracingIntensity` is not 0

### Issue: Too noisy
- Increase `maxAccumulatedFrames`
- Increase `samplesPerPixel`
- Lower `fireflyClamp`
- Enable `denoiseMode` = `SpatialTemporal`

### Issue: Too dark
- Increase `pathTracingIntensity`
- Increase `environmentIntensity`
- Check `fireflyClamp` is not too low
- Verify lights are in WorldLightCluster grid

### Issue: Poor performance
- Lower `pathTracingQuality` to `Low` or `Medium`
- Reduce `maxBounces`
- Use `samplesPerPixel` = 1 with accumulation
- Enable `useNVSER` on RTX GPUs
- Increase `textureLODBias`

### Issue: Ghosting during movement
- Enable `resetOnCameraMove`
- Lower `cameraMovementThreshold`
- Reduce `maxAccumulatedFrames`
- Enable `receiverMotionRejection`

---

## See Also

- [ReferencedPathTracingPass.cs](./ReferencedPathTracingPass.cs) - Pass implementation
- [README.md](./README.md) - System overview
- [WorldLightCluster](../../LightCullingSystem/WorldLightCluster/README.md) - Light query system


