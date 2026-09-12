# Independent baseline review

Reviewed the archived `VSMDynamicBaselineProbe.cs.txt`, production receiver-capture placement, pipeline camera callbacks, independent reference shader, dynamic parser and stage-timing report. This review does not execute Unity or certify a capture that has not completed.

## Capture validity

- The pipeline invokes `beginCameraRendering` before `ScheduleCullForCamera`; the probe applies each trajectory pose there. Production camera/culling state therefore sees the intended pose before rendering.
- `EditorReceiverCapture` runs after fullscreen resolve, spatial/temporal filtering and history writes. The reference uses that callback's actual depth, GBuffer, filtered shadow and current written history, with that frame's GPU inverse view-projection. The 18-case GPU fixture already validates its ROI, stride, platform Y convention, sky/invalid handling, visibility and copied signal/world channels; see `reference-gpu-result.txt`.
- Each mode waits at least 64 frames and starts at `frameIndex & 255 == 0`. The probe rejects non-consecutive receiver frames and records camera/light/fixture pose, phase, matrix and deformation hash. Fixed 4, fixed 8 and adaptive can therefore be compared at matching trajectory steps and production noise phases. Check the completed data's pair validation rather than relying only on planned settings.
- Fixture transform/geometry updates must occur before the TLAS build and reference dispatch in the same command buffer. Explicit-flags Renderer and remove/re-add mesh-config paths now have independent actual-GPU evidence: `reference-update-gpu-command.json` / `reference-update-gpu-result.txt` passed 18 route/mode/pose tests, including blocked → clear → blocked with both transform movement and changed mesh vertices. The Renderer routes passed with InternalErrorShader and StandardLit. The mesh-config route removes the old handle, re-adds current geometry/current transform in manual-update mode and calls `UpdateInstanceGeometry(handle)`.

Four invalid fixture assumptions were found while validating the capture harness. First, `HideAndDontSave` excluded the fixture from scene-caster discovery; use `HideInHierarchy`. Second, Renderer ray-tracing update mode was not explicit; use `DynamicTransform` or `DynamicGeometryManualUpdate`. Third, on the tested Unity 6000.7.0a6 build, `AddInstance(renderer, null, ...)` reported an instance but yielded no geometry hits; explicit per-submesh `Enabled | ClosestHitOnly` flags fixed this. Fourth, the active graph has a compatibility PreDepth pass but no ordinary GBuffer pass, so the plain Renderer fixture wrote depth while its normal was invalid. The independent RTAS and renderer count could not certify receiver validity.

Trial `20260912_073232_016` and subsequent ordinary-Renderer trajectories with empty RTAS or missing normal data must not contribute a dynamic-quality pass. The root workflow is replacing those fixtures with the actual meshlet GBuffer path and re-capturing. This review does not certify the replacement runs in advance. Require a known shadow transition and adequate world/normal coverage before accepting a run; zero eligible events and eight top-surface samples across a trajectory indicate inadequate evidence, not stability.

The graph observation is source-grounded: `Assets/New Vivid Render Pipeline Asset 1.asset` references `Assets/_Recovery/Standard Vivid Render Graph 1.vrdg`, which contains `VisibilityBufferGBufferResolvePass` and `PreDepthPass` but no ordinary `GBufferPass`. The default material itself is StandardLit and does contain a VividGBuffer shader pass. The missing graph stage, rather than an absent material pass, explains the normal validity problem in that configuration.

## Dynamic quality boundaries

`receiver_slide` is a nearly coplanar thin plate. At fixed camera and unchanged lighting, interior points are almost identical world-space shadow queries while the object translates. This primarily tests near-coplanar replacement and boundaries; it cannot alone validate larger disocclusion or object motion reprojection. The subsequently added `receiver_lift` supplies a stronger depth-change case. Its results need the parser's matching lift plan and raised top-surface ROI, not the floor-only mask.

The parser associates adjacent samples by world-position tolerance, not object identity or full motion-vector reprojection. Camera slide/turn may have few eligible stationary world samples. Zero eligible events is insufficient evidence, not a zero-lag pass. Detection-delay statistics must retain censored/reversing/undetected events and report detected-event percentiles as such. A filtered signal can differ from raw geometric visibility within spatial filter support without being a ray-intersection error.

The independent reference forces triangle opacity, disables face culling, uses source meshes and omits automatic terrain/procedural/skinning/WPO evaluation. Use opaque interior regions, show the 1 mm / 10 mm normal-bias sensitivity and 256 / 1024 convergence evidence, and do not certify foliage alpha accuracy. Only the explicitly controlled fixture is updated dynamically; other scene geometry is assumed frozen during a run. CPU deformation hash agreement alone is not GPU BLAS-update evidence.

## Stage cost boundaries

The current timing report correctly keeps six forward/reverse windows separate, uses the parent `VSM.Resolve` directly, excludes nested PageCull from sums, and labels recorder observations rather than claiming exact GPU-frame/camera identity. Trace occupies approximately 97.66%–98.77% of the measured Resolve interval; it contains selection, retries, history lookup and tracing together, so this does not isolate 16-layer bandwidth or prove a particular hardware bottleneck.

Static warm-cache windows have no UnityCompatibilityRaster or InvalidateStatic samples and negligible static raster work. They cannot predict dynamic invalidation/raster peaks or the cost of many deforming compatibility Renderers. Non-nested marker sums are useful interval diagnostics, not a complete VSM critical-path or whole-frame duration. Resolve improvements should be quoted within matching directional windows, not against old captures made under different editor load.

## Profiling allocation check

`VSMProfilingGCProbe.cs.txt` is a normal temporary Editor menu that directly uses the production cached samplers. It measures four single scopes, spatial/temporal nested layouts and null fallback after 128 warmups, with 4096 repetitions each. Reflection and the rejected command approach are removed. It records through the native CommandBuffer overload, which calls the same sampler Begin/End methods as the production BaseCommandBuffer overload, without mutating shared wrappers.

The result covers current-thread managed allocation in warmed marker recording only. It does not replace all-thread Unity Profiler verification or prove the rest of Prepare/Record is allocation-free. No GPU commands are submitted and no scene is changed by this check.
