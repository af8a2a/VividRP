# Meshlet fixture baseline

`VSMDynamicMeshletBaselineProbe.cs.txt` has been imported as the temporary `VSMDynamicBaselineProbe.cs` class and used for the completed smoke capture. It must not be imported alongside another copy. It supersedes the **unexecuted** plain-Renderer timing candidate. This diagnostic backend change did not modify Runtime code, production shaders or the scene graph.

The harness passes an offline compile of the current full Editor assembly with Unity's current references and the existing render-pass generator. Evidence is `dynamic-meshlet-compile/validation.json`; reproduce with `python Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/compile-dynamic-stage-timing.py --meshlet`. The live smoke run `Temp~/vsm-baseline/dynamic/20260912_075317_538/status.txt` is `complete`: thin_sweep, receiver_lift and deform each captured 32 adaptive frames with a 256-ray reference. The completed measurements are in `dynamic-smoke-jump01.json`.

## Why this backend

The production graph migration connects predepth to meshlet visibility depth, then routes the visibility GBuffer resolve to lighting and shadow receivers; it disconnects the legacy Unity GBuffer pass. A plain Renderer can therefore appear in receiver depth without a matching material/normal GBuffer. `VisibilityBufferGBufferResolve.shader` discards pixels with no meshlet visibility ID. This matches the observed plain receiver slab: valid elevated depth, zero decoded normal-Y and black material. Presence of StandardLit shader passes alone does not establish their use by this graph. This was a diagnostic fixture/backend mismatch with the existing graph, not a newly discovered VSM correctness regression.

Rigid fixtures now create a transient `VividMeshletCollectionAsset` with one LOD, capture the source Renderer state into `MeshletRenderer`, assign a transient StandardLit material proxy, and disable the source MeshRenderer. The proxy is opaque middle gray, nonmetallic, lit and two sided; the source material also sets ReceiveShadows=1, TransmissionWeight=0 and opaque state. The source state is captured while its Renderer is enabled, so the meshlet keeps `sourceRenderingEnabled=true` after the plain Renderer is disabled. The scene remains dynamic (`GameObject.isStatic=false`). No assets are saved.

The camera-begin callback updates the pose and explicitly calls the production database's `UpdateRendererTransformData` before culling. This avoids relying on an earlier `LateUpdate` to notice a transform that changed at camera begin.

## Deformation semantics

The `deform` scenario prebuilds one mesh, meshlet collection and bound child Renderer for every requested trajectory frame; all share one proxy. Exactly one child is active. Frame switching activates the corresponding prebound child and deactivates its predecessor. At the normal 32-frame setting this is a **prebaked frame sequence**, not live skinned deformation or a vertex-stream update. Registration/unregistration and resource transitions belong to its measured workload. No zero-GC claim is made for this dynamic registration path.

Quality warm-up cycles the prebaked sequence for 64 callbacks, then holds pose zero while waiting for `cameraFrame & 255 == 0`. The first measured pose is zero, followed by consecutive trajectory frames. Rigid and light quality scenarios retain their initial pose during warm-up. The timing branch cycles all trajectories during warm-up and observations.

## Independent reference

The active fixture meshlet is omitted from the initial background RTAS enumeration, then added separately through an explicit `RayTracingMeshInstanceConfig`: mask 2, `Enabled | ClosestHitOnly`, triangle culling off and `DynamicGeometryManualUpdate`. Every quality callback removes/re-adds this handle using **the same active mesh and world transform as the production meshlet**, calls `UpdateInstanceGeometry(handle)`, builds RTAS in the command buffer and dispatches the reference. This matches the GPU-validated mesh-config update route. It does not rely on an unverified `UpdateInstanceTransform(handle)` overload.

Other Unity Renderer references also use explicit enabled submesh flags. The previous `AddInstance(renderer, null, ...)` route passed an instance count check but did not produce ray hits in the route fixture. The reference route correction and changing the fixture to the graph's production Meshlet GBuffer backend are separate diagnostic changes; neither CPU triangle count nor instance count alone validates geometric hits.

## Cases and timing

Defaults are now nine scenarios: static, camera_slide, camera_turn, thin_sweep, deform, receiver_slide, receiver_lift, sun_rotate, sun_step. `sun_rotate` keeps its smooth total 3-degree motion. `sun_step` holds the original sun through frame 7, jumps 3 degrees at frame 8 and holds through frame 31; it provides a measurable onset for latency checks.

Quality defaults to fixed4/fixed8/adaptive, 64 warm callbacks plus phase alignment and 32 captured frames. `timingOnly=true` normalizes to all nine scenarios forward and reverse, adaptive4 only: **18 windows**, each with one metadata setup callback, 64 warm callbacks and 128 observations. The original 32-frame movement speed and cycle reset are preserved. No reference RTAS, reference output, GPU readback, screenshot or per-frame JSON work runs in this timing branch. GPU recorder values still describe the last completed profiling frame and are not paired with quality-frame BND phases.

## Completed smoke evidence and limits

- Thin sweep produced 2,848 eligible adjacent-frame floor visibility changes at the 0.1 threshold, after bias/surface continuity rejection. This establishes measurable shadow motion for the fixture. It does not make every candidate transition suitable for a latency measurement: reference reversal/surface loss and already-crossed output remain separately excluded.
- Receiver lift produced 137,240 valid top samples across all 32 frames. Of 137,263 geometric top candidates, 99.98324% also satisfied normal-Y > 0.9. The elevated surface therefore has matching depth and GBuffer normals. Its 0.001 m-bias reference MAE was 0.025651; this is a sampled quality measurement, not proof of material-point temporal response, since screen-grid samples do not track moving material points.
- The prebaked deform sequence produced 24 distinct geometry hashes and 123 nonzero adjacent floor-reference changes. The maximum reference change was 0.04296875, below the 0.1 threshold, so this case has **insufficient detectable events** for that latency test. Its animated geometry/reference path is exercised; it is not a zero-latency pass.

Timing must use this same accepted Meshlet backend. UnityCompatibilityRaster is expected to have zero executions in these windows; dynamic caster/raster measurements belong to the meshlet route. Keep the old plain-Renderer captures as invalid fixture evidence, not as equivalent dynamic receiver measurements. The full nine-case quality run and the separate 18-window timing run are evaluated in their own result reports.
