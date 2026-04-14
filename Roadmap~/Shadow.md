# CSM (Cascaded Shadow Maps) Implementation Plan

## Context

VividRP currently only supports ray-traced directional shadows (`DirectionalRayTracedShadowPass`). This requires RT-capable hardware. We need a raster-based CSM fallback that works on all hardware, referencing HDRP's architecture but significantly simplified for V1.

The existing pipeline already consumes a screen-space `DirectionalShadowTexture` (R16_SFloat, 0=shadowed, 1=lit) in `DeferredLit.compute:75-78`. The CSM system writes to the same resource name, so the deferred lighting path needs no changes.

## Key Integration Points

- `VividLightData.mainLightIndex` — index into `cullingResults.visibleLights` for the main directional light (already exists)
- `CullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives()` — Unity API for cascade matrix computation
- `DirectionalShadowTexture` resource name — consumed by `DeferredLightingPass` and `SkyInjectionPass`
- RT shadow pass writes R16_SFloat with HALF_MAX=65504 for "fully lit"; deferred shader does `saturate()`, so writing 1.0 for lit works too

## New Files

### 1. `Runtime/RenderPipeline/CascadedShadowSettingsVolume.cs`
Volume component for CSM settings:
- `BoolParameter enableCSM` (default false)
- `ClampedIntParameter cascadeCount` (1–4, default 4)
- `MinFloatParameter maxShadowDistance` (default 150)
- `ClampedFloatParameter cascadeSplit1/2/3` (default 0.067, 0.2, 0.467)
- `ClampedIntParameter shadowResolution` (512–4096, default 2048 per cascade)
- `ClampedFloatParameter depthBias` / `normalBias`

### 2. `Runtime/RenderGraph/FrameContext/VividShadowData.cs`
New `ContextItem` carrying per-frame CSM state:
- `bool isCSMActive`
- `int cascadeCount`, `int atlasResolution`, `int cascadeResolution`
- `Matrix4x4[] viewMatrices[4]`, `Matrix4x4[] projMatrices[4]`
- `Vector4[] cascadeSpheres[4]` (xyz=center, w=radiusSq)
- `Vector4[] cascadeAtlasScaleOffsets[4]` (xy=scale, zw=offset)
- `float depthBias`, `normalBias`, `maxShadowDistance`

### 3. `Runtime/RenderPass/Core/CSMShadowPass.cs` — UnsafePass
Renders shadow casters into a depth atlas. UnsafePass because we need per-cascade viewport control.

**Constructor**: Creates `m_ShadowAtlas` as depth texture (D16_UNorm or D32_SFloat), creates `m_ShadowCasterRenderList`.

**Prepare**:
1. Read `CascadedShadowSettingsVolume` from volume stack
2. Read `VividLightData` → get `mainLightIndex` for the visible light index
3. For each cascade, call `cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(mainLightIndex, cascadeIndex, cascadeCount, splitRatios, resolution, ...)` → get view/proj matrices, splitData
4. Store matrices and cascade spheres into `VividShadowData` (via `frameData.GetOrCreate<VividShadowData>()`)
5. Size atlas: 2x2 grid layout → `atlasResolution = cascadeResolution * 2`
6. Configure `ShadowDrawingSettings`

**Record**:
1. For each cascade: set viewport to tile region, set view/proj matrices, draw renderer list
2. Restore camera matrices

Atlas layout (2x2):
```
+------+------+
| C0   | C1   |
+------+------+
| C2   | C3   |
+------+------+
```

### 4. `Runtime/RenderPass/Core/CSMShadowResolvePass.cs` — ComputePass
Screen-space resolve: reads depth + shadow atlas → writes `DirectionalShadowTexture`.

Resources:
- Read: `Depth`, `GBuffer1` (normals for normal bias), `CSMShadowAtlas`
- Write: `DirectionalShadowTexture` (R16_SFloat, same name as RT shadow output)

**Record**: Dispatch compute shader with cascade data as constants.

### 5. `Shaders/Core/Private/CSMShadowResolve.compute`
Compute shader (8x8 thread groups):
1. Reconstruct world position from depth + inverse VP
2. Distance check → early out if beyond `maxShadowDistance`
3. Cascade selection: test `distance²(posWS, sphere.xyz) < sphere.w` for each cascade
4. Transform to shadow clip space via `viewProj[cascadeIndex]`
5. Remap to atlas UV via scale/offset
6. Apply depth bias + normal bias
7. Sample with hardware PCF (SamplerComparisonState) + 3x3 tent filter
8. Write shadow factor to output

### 6. `Shaders/Core/Private/CSMShadowCaster.shader` (optional fallback)
Minimal depth-only shader with `LightMode = "ShadowCaster"` tag. Only needed if existing materials lack a ShadowCaster pass.

## Modified Files

### `Runtime/Utility/PipelineResource/VividRPCoreResources.cs`
Add:
```csharp
[ResourcePath("Shaders/Core/Private/CSMShadowResolve")]
public ComputeShader CSMShadowResolveCompute;
```

### `Runtime/RenderGraph/FrameContext/VividLightData.cs`
Expose `mainDirectionalVisibleLightIndex` — the index into `cullingResults.visibleLights` for the main directional light. Currently `mainLightIndex` already stores this. Verify it's the correct index for `ComputeDirectionalShadowMatricesAndCullingPrimitives`.

## Pass Ordering

```
GBuffer → [CSMShadowPass → CSMShadowResolvePass | DirectionalRayTracedShadowPass → SIGMA] → DeferredLighting
```

CSM and RT shadow paths are mutually exclusive — both write `DirectionalShadowTexture`. Selection: if `CascadedShadowSettingsVolume.enableCSM` is active and main directional light exists, use CSM. RT shadow pass already checks `isRayTracedShadowActive` on the light component.

## Phased Roadmap

### Phase 1: Shadow Atlas Rendering
- `CascadedShadowSettingsVolume.cs`
- `VividShadowData.cs`
- `CSMShadowPass.cs` (UnsafePass, renders depth atlas)
- Resource registration
- Validate atlas in Frame Debugger / RenderDoc

### Phase 2: Shadow Resolve
- `CSMShadowResolve.compute` (hard shadows first, no PCF)
- `CSMShadowResolvePass.cs`
- Verify shadows appear in deferred lighting output

### Phase 3: Filtering & Bias
- Add 3x3 tent PCF
- Depth bias + normal bias
- Cascade border blending
- Distance fade at maxShadowDistance

### Phase 4: Polish
- Cascade debug visualization (color-coded)
- Mutual exclusion with RT shadow path
- Edge cases (no light, orthographic camera, cascade count < 4)
- Performance profiling

## Verification

1. Add `CascadedShadowSettingsVolume` to a Volume profile, enable CSM
2. Place a directional light with shadows enabled
3. Frame Debugger: inspect shadow atlas — each quadrant shows scene from light perspective
4. Scene view: shadows visible on all opaque geometry
5. Cascade visualization: temporarily color-code cascades to verify split distances
6. Walk camera to verify cascade transitions and distance fade
7. RenderDoc: verify PCF sampling pattern and bias behavior
