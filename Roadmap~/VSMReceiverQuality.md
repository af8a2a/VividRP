# P5-B — Receiver quality independent of stable projections

Implemented 2026-09-05. Screen-density selection is opt-in; existing profiles
keep P4 coverage selection. This is a policy layer, not a new projection layout,
allocator, filter, or temporal stabilizer.

## Controls

The CSM Volume editor exposes these independent receiver settings:

- **Virtual Shadow Map Screen Density**: enable the new policy; default off.
- **Virtual Shadow Map Target Texel Pixels**: default 1, range 0.25–8. Smaller
  values request finer data. This is a quality target, not a guaranteed bound.
- **Virtual Shadow Map Resolution Lod Bias**: default 0, range -4–4. Effective
  target = Target Texel Pixels × 2^Bias; -1 requests twice the linear density,
  +1 half. This bias is not a shadow depth/normal bias.

First Level, virtual resolution and Max Distance remain resource/coverage
settings. Quality controls do not change their values, page alignment, depth
interval, projection generation, buffer identities or static-cache keys. Camera
FOV and output size affect receiver demand through the camera matrix/dimensions,
not the stable light projection. Changing virtual resolution or First Level
itself still changes the layout as before.

## Selection contract

1. Reconstruct the geometric receiver normal as in P4. Project the finest
   clipmap's virtual texel X/Y axes onto that receiver plane and then the screen.
   The same differential now serves P5-A diagnostics and production selection.
2. Compute continuous relative LOD = log2(effective target / largest axis
   footprint). Each coarser directional level doubles world texel size, so the
   differential is evaluated once, not once per level. Clamp to the configured
   level range and floor to the coarsest level within the target.
3. Apply actual projection coverage independently. Select the first covered
   level at or above that demand, including that level's normal offset and a
   1.5-texel map-edge guard for PCF. Unlike legacy half-radius selection, this can
   use fine data in the outer part of an existing projection without resizing it.
4. Blend near a density LOD boundary and a projection coverage boundary using
   the existing Transition control. The larger blend weight wins; at most one
   coarser transition sample is used. Density blending can briefly exceed the
   target near a LOD boundary; the target is not a strict maximum footprint.
5. Use the same preferred level for sampling and receiver feedback. Request its
   complete coarse fallback chain, retaining the existing terminal-coarse
   priority. Page residency does not affect the preferred choice. Missing/dirty
   pages still fall back as a whole; a fallback primary is not blended twice.

Degenerate receiver planes request the finest covered level and report an
invalid density ratio rather than a fabricated finite footprint. Beyond Max
Distance or all projection coverage, the terminal result remains lit. Axis
footprints are local estimates, not singular-value bounds; silhouettes, depth
discontinuities and grazing surfaces still need scene validation.

No quality-change cache flush is introduced. New fine requests arrive through
the existing next-frame feedback pipeline, so changing quality can temporarily
sample coarser resident data. This milestone does not add synchronous allocation,
extra physical pages, residency hysteresis or temporal anti-aliasing. Integer
page scrolling remains stable for cached depth, but changed coverage/LOD demand
can still produce temporal changes; that is not a claim of zero popping.

## Debug and comparison

Keep the P5-A node wiring. **PreferredLevel** now reports the selected policy's
pre-residency choice; **SampledLevel** and **FallbackLevels** retain their meaning.
New **QualityPolicy** mode (6) outputs raw RGBA:

| Channel | Meaning |
| --- | --- |
| R | Unclamped continuous density LOD relative to First Level; negative means finer than the configured finest level |
| G | Finest projection covering the biased receiver/filter guard, independent of density and residency; -1 if none |
| B | Preferred level after range/coverage constraints; -1 if none |
| A | Preferred level's largest-axis footprint / effective target; -1 when unavailable or degenerate |

Green means ratio ≤1; red reaches 4× target. This is **preferred**, not sampled
density: use TexelFootprint and SampledLevel to diagnose additional residency
degradation. Legacy policy produces (-1,-1,-1,-1) in QualityPolicy; sky retains
the (-2,-2,-2,-2) sentinel. All diagnostics remain feedback-write-free.

For a fixed camera, first compare Screen Density off/on with target=1, bias=0,
and unchanged resolution, First Level, page budget and biases. Record density
clamps and residency fallback separately. Then vary LOD bias, FOV and output
size while checking that cached projection generation does not change. Disable
debug replay when measuring ResolveAndFeedback cost: screen-density selection
adds receiver math even though it does not rebuild projections. GPU cost and
page pressure must be measured, not inferred from smaller shadow teeth.

## Verification and remaining gates

- Targeted Roslyn compilation: Runtime, Editor and Editor.Tests passed.
- DXC: all 33 resolve/sampling-test entry points compiled.
- An isolated compute-shader clone on the running Unity DX12 / RTX 5070 Ti
  executed receiver diagnostics with temporary buffers. With identical light
  projections, doubling screen dimensions changed center demand from level 1
  to 0. Doubling perspective camera distance changed continuous LOD from 0.2075
  to 1.2075. Receivers outside finer projection coverage clamped to levels 1/2;
  out-of-map and beyond-distance receivers remained unavailable. No scene,
  Volume, production shader instance or graph was changed by this probe.
  A separate disabled-policy probe confirmed legacy preferred levels; the
  QualityPolicy output explicitly gates on enable and returns (-1,-1,-1,-1)
  after switching off. The shader-import check emitted PipelineResources.asset
  importer-consistency warnings, but that generated asset has no file changes;
  this is not a clean whole-Editor import/memory audit.
- Added regression cases for defaults/clamps, quality-only uniforms preserving
  layout resources, zero managed allocation after warm-up, resolution/FOV/zoom,
  geometric slope, coverage, normal offset/PCF guards, LOD transitions and shared
  sampling/feedback behavior. Existing legacy sampling cases remain in place.
- **Unity Test Framework was not run:** interactive Editors were open. Manually
  run `VirtualShadowMapReceiverQualityTests`, `VirtualShadowMapSamplingTests`,
  `VirtualShadowMapClipmapTests`, `VSMReceiverDebugPassTests` and
  `CascadedShadowSettingsVolumeTests`. Compilation and the isolated GPU probe
  do not replace those tests or certify the zero-allocation assertion.

Scene-level Sponza comparison, full-frame/worker GC profiling, moving-camera
stability, budget-pressure recovery and the P5-A live timing sweep remain open.
No new default quality tier or performance improvement is claimed.
