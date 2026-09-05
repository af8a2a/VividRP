# Virtual Shadow Map Roadmap

> Status date: 2026-09-03
>
> Scope: main directional-light hard shadows, with `MeshletRenderer` as the primary path and Unity `MeshRenderer` as a small compatibility path.

## 1. Goal

Build a production-oriented Virtual Shadow Map system that:

- gives the dominant `MeshletRenderer` path sparse page allocation, GPU per-page culling, and page-level caching;
- keeps a small number of GPUDriven-incompatible Unity `MeshRenderer` objects functional through their original `ShadowCaster` shaders;
- lets both renderer paths write the same virtual shadow address space and participate in the same screen-space resolve;
- preserves the existing CSM path as a fail-closed fallback until VSM readiness is deterministic;
- performs no recurring managed allocation in render-loop code after warm-up.

The roadmap intentionally optimizes the Meshlet path first. It does not attempt to reproduce Unreal's full Non-Nanite per-page draw-command pipeline for the small Unity Renderer compatibility set.

## 2. Confirmed Content Constraints

These constraints are part of the design, not temporary implementation details:

1. A source primitive is owned by exactly one renderer backend.
   - An unconverted object keeps its Unity `MeshRenderer` or `SkinnedMeshRenderer`.
   - A converted object completes takeover and retains only `MeshletRenderer` rendering data.
   - A primitive is never submitted by both paths in the same pass.
2. Most shadow-casting geometry uses `MeshletRenderer`.
3. Unity Renderers are a small compatibility set, so full-cascade shadow raster cost is acceptable for them.
4. Every Unity Renderer shader that casts into VSM may add an explicit VSM `ShadowCaster` variant.
5. The current prototype platform contract remains Direct3D 12 or Vulkan, reverse-Z, compute support, and `R32_UInt` load/store support.
6. Hard-shadow correctness, cache stability, and failure behavior take priority over filtering quality.

## 3. Explicit Non-Goals

The following work is out of scope unless profiling later proves it necessary:

- retaining a live source `MeshRenderer` beside a takeover `MeshletRenderer`;
- runtime deduplication between the two renderer backends;
- per-page Unity `RendererList` generation;
- a Unity Renderer GPU Scene or BatchRendererGroup replacement;
- automatic support for arbitrary third-party shaders that do not implement the VSM caster ABI;
- soft-shadow algorithms before sparse allocation and cache invalidation are stable;
- local-light VSMs before the main directional-light path is production ready.

## 4. Current Baseline

The repository already contains the following prototype pieces:

- a platform capability gate for reverse-Z Direct3D 12/Vulkan and `R32_UInt` storage;
- receiver-driven 128 x 128 virtual-page requests with one-frame feedback latency;
- an explicitly unmapped sparse page table and a fixed 256-page physical budget;
- static/dynamic physical layers sharing one allocated physical-page address;
- Meshlet hard-shadow raster that writes `R32_UInt` pages with `InterlockedMax`;
- hard-shadow resolve through the VSM page table;
- a static-pool cache key plus independent static-hit/static-refresh/dynamic-refresh counters;
- `VSMDebugPass` views for static, dynamic, or combined device depth, occupancy, and a depth heat map;
- a conventional CSM path in which Unity Renderer and Meshlet casters already share one cascade depth target.

The current VSM path caches static Meshlet caster pages, refreshes dynamic Meshlet plus compatible Unity Renderer caster pages every frame, and allocates only pages requested by visible receivers. It requires GPUDriven readiness for Meshlet content. Conventional CSM is now recorded only when VSM is disabled, not ready, is collecting initial receiver feedback, or fails before completing the current frame.

## 5. Target Architecture

```text
                         Shared virtual address space
                    Page table + page metadata + allocator
                                      |
                   +------------------+------------------+
                   |                                     |
        MeshletRenderer primary path          Unity Renderer compatibility path
        GPU page/instance/meshlet cull        Unity ShadowRendererList
        Dirty-page raster                     Full-cascade raster
                   |                                     |
                   +---------- direct UAV writes --------+
                                      |
                         Static pool + Dynamic pool
                                      |
                       max(staticDepth, dynamicDepth)
                                      |
                         screen-space shadow resolve
```

### 5.1 Renderer Responsibilities

| Responsibility | MeshletRenderer | Unity Renderer |
| --- | --- | --- |
| Ownership | Takeover-only | Compatibility-only |
| Shadow shader | GPUDriven Meshlet caster | Original material `ShadowCaster` with VSM variant |
| Initial raster scope | Full cascade | Full cascade |
| Final raster scope | Dirty physical pages | Full cascade with unmapped-page rejection |
| Fine culling | Instance, LOD, Meshlet, page | Unity shadow culling only |
| Static cache | Supported and prioritized | Not required initially |
| Dynamic handling | Dynamic pool | Always dynamic pool initially |
| Per-page invalidation | Required | Not required initially |

### 5.2 Physical Depth Convention

The platform contract is reverse-Z:

- physical pages clear to `0u`;
- greater device depth is closer to the light;
- both caster paths write `asuint(saturate(positionCS.z))` through `InterlockedMax`;
- static and dynamic depths combine with `max`;
- an unmapped page produces a fully lit result.

This convention must remain centralized in shared shader code. Caster and resolve shaders must not duplicate independent depth or page-address rules.

## 6. Shared VSM Caster Shader ABI

Create one shared HLSL include for all supported shadow casters, for example:

```text
Shaders/Core/Public/Shadow/VividVirtualShadowMapCaster.hlsl
```

The ABI owns:

- virtual texel to virtual page conversion;
- page-table indexing by cascade;
- encoded physical page decoding;
- physical texel calculation;
- unmapped-page rejection;
- reverse-Z depth packing and atomic write.

The minimal public shader interface is:

```hlsl
// V2 Unity compatibility entry point: tile-local SV_Position is restored to
// virtual texels by the shared writer; projection index comes from the pass.
void VividWriteVSMDepth(float4 rasterPositionCS);
```

The existing `ShadowCaster` pass remains the selected Unity light mode. Supported shaders compile a global runtime variant:

```hlsl
#pragma multi_compile_fragment _ VIVID_VSM_CASTER
```

Rules:

1. Use a global `multi_compile` variant, not a material-local `shader_feature`.
2. Execute material alpha, terrain-hole, and coverage rejection before `VividWriteVSMDepth`.
3. Preserve each shader's original vertex path, instancing, skinning, deformation, and culling behavior.
4. Bind the physical page UAV at the ABI's fixed slot only while executing VSM caster draws.
5. Cache shader IDs and keyword handles; do not construct names or keyword objects in the render loop.

Initial shader coverage:

- StandardLit;
- ExperimentalStandardLit;
- StandardLayeredLit;
- Unlit;
- TerrainLit and TerrainLit Basemap;
- project-specific shadow-casting compatibility shaders.

## 7. Milestone Roadmap

## P2.1 - Direct Mixed-Caster Hard Shadows

> Implementation status: code, shared caster ABI, and supported shader-variant validation are complete. Interactive mixed-scene visual and graphics-API validation remain before the milestone is accepted.

### Objective

Allow Unity Renderer and MeshletRenderer caster sets to write one fully resident VSM physical pool directly.

### Work

1. Extract the existing Meshlet page-address and atomic-write logic into the shared VSM caster ABI.
2. Add `VIVID_VSM_CASTER` variants to the initial supported Unity shaders.
3. Make VSM preparation active when either Unity casters or Meshlet casters are available.
4. Remove the global dependency on Meshlet draw readiness from the VSM refresh path.
5. For every cascade refresh:
   - clear one shared raster depth slice;
   - bind the physical page UAV;
   - enable the global Unity VSM caster keyword;
   - draw the Unity `ShadowRendererList`;
   - disable the global keyword;
   - draw available Meshlet opaque and compatible alpha-test buckets;
   - clear random-write targets.
6. Keep Unity and Meshlet availability independent:
   - Unity casters do not require GPUDriven or virtual-texture readiness;
   - Meshlet opaque casters do not require virtual-texture sampling;
   - only Meshlet alpha-test buckets may be skipped when their texture backend is unavailable.
7. Keep conventional CSM enabled as a transition fallback.

### Verification

- Unity-only, Meshlet-only, and mixed scenes all produce VSM hard shadows.
- Spatially overlapping casters resolve to the nearest reverse-Z depth regardless of draw order.
- StandardLit alpha clip, Unlit alpha clip, Terrain holes, two-sided shadows, and `ShadowsOnly` behavior are correct.
- A missing GPUDriven system does not disable Unity VSM casters.
- Missing Meshlet virtual-texture data does not disable Unity or Meshlet opaque casters.
- `VSMDebugPass` displays nonzero physical depth from both paths.
- Stable `Prepare` and `Record` support code allocates zero managed bytes after warm-up.

### Exit Criteria

- Mixed-caster correctness matches conventional CSM in representative scenes.
- No graphics API validation error is produced by DSV/UAV binding or global keyword transitions.
- All supported VSM caster variants import and compile successfully.

## P2-B (P2.2) - Shader Compatibility Validation and Fail-Closed Fallback

> Implementation status: code and compiler validation complete. Targeted Unity EditMode tests are added; scene/build acceptance remains pending while an Editor session is active.

### Objective

Prevent a Unity Renderer with an unsupported shader from silently disappearing from VSM.

### Work

1. Give every supported shader a stable VSM capability marker or registered metadata entry.
2. Add cold-path editor/build validation for every shadow-casting Unity Renderer material.
3. Cache validation results by the actual shader/material inputs and invalidate only when those inputs change.
4. Report the exact GameObject, Renderer, material slot, Material, and Shader for unsupported casters.
5. Make runtime readiness fail closed:
   - if required caster support cannot be guaranteed, disable VSM for that light/camera;
   - continue with conventional CSM;
   - never expose a partially populated VSM as active.
6. Ensure shader build stripping retains the global VSM caster variants.

The implemented shader contract requires both declarations on every participating Unity caster pass:

```shaderlab
Tags { "LightMode" = "ShadowCaster" "VividVSMCaster" = "2" }
#pragma multi_compile_fragment _ VIVID_VSM_CASTER
```

Runtime validation is event-invalidated through Renderer, Terrain, Material, and Shader object changes. Stable frames read the cached result without rescanning scene casters or formatting diagnostics.

### Verification

- Removing a VSM variant from any required shader causes deterministic validation failure.
- Unsupported content produces CSM output rather than partial VSM shadows.
- Validation does not scan scene Renderers, build strings, or allocate collections every frame.
- Repeated stable validation checks allocate zero managed bytes after warm-up.

### Exit Criteria

- All production scene casters are either validated VSM casters or explicitly non-shadow-casting.
- VSM activation and debug state agree with the validation result.

## P2.3 - Static and Dynamic Physical Pools

### Status

Implementation complete on 2026-09-02. Focused C# compilation and the active Unity Editor script/shader reload pass without persistent compile or shader errors. Unity Test Framework execution and static/dynamic graphics validation remain pending because an interactive Editor session is active.

The implementation uses the existing Meshlet static flag to build aggregate, static-only, and dynamic-only shadow DrawSets. Static Meshlet changes advance a dedicated cache revision, while dynamic-only transform changes leave it stable. Both pools share the same page table and descriptor; resolve combines reverse-Z depths with `max`, and `VSMDebugPass` exposes Combined, Static, and Dynamic pool selection.

### Objective

Preserve cached Meshlet shadow depth while allowing the small Unity Renderer compatibility set to redraw every frame.

### Default Classification

- Static pool: static MeshletRenderer casters only.
- Dynamic pool: all Unity Renderer casters and all dynamic MeshletRenderer casters.
- Unity Renderers are deliberately treated as dynamic even when their GameObjects are marked static.

This avoids Unity transform, material, animation, skinning, and property-block revision tracking. Optional static Unity Renderer caching is deferred until profiling demonstrates a need.

### Work

1. Allocate static and dynamic physical depth pools with the same physical-page coordinate space.
2. Share one page table between both pools.
3. Render or refresh the static pool only when the static cache key is invalid.
4. Clear and redraw the dynamic pool every frame.
5. Split Meshlet shadow culling by the existing static instance flag.
6. Draw all Unity `ShadowRendererList` casters into the dynamic pool.
7. Change resolve and debug sampling to combine:

```hlsl
uint finalRawDepth = max(staticRawDepth, dynamicRawDepth);
```

8. Track static-cache and dynamic-refresh counters without per-frame diagnostic string generation.

### Verification

- Moving, animating, enabling, disabling, or changing a Unity Renderer never leaves stale depth.
- Dynamic Meshlets never leave trails after moving or disappearing.
- Stable static Meshlet content is not rerasterized merely because Unity Renderers are present.
- Static and dynamic overlaps resolve to the correct closest depth.
- Debug views can inspect static, dynamic, and combined occupancy/depth.

### Exit Criteria

- Static Meshlet cache hits coexist with per-frame Unity Renderer redraws.
- Static and dynamic pool resource descriptors remain stable and are not recreated per frame.

## P2.4 - Deterministic VSM/CSM State Machine

### Status

Implementation complete on 2026-09-02. VSM readiness is resolved during `Prepare`, successful VSM recording transitions to `Active`, and conventional CSM recording is gated by that success. Resource, compatibility, GPUDriven, DrawSet, virtual-texture, cache-key, and record-time failures retain an allocation-free fallback reason and route the same frame through conventional CSM. Focused C# compilation passes; Unity Test Framework and RenderDoc/graphics validation remain pending while an interactive Editor session is active.

### Objective

Remove duplicate conventional CSM raster when VSM is guaranteed to be usable, while retaining a reliable fallback.

### States

```text
Disabled -> Prepared -> Refreshing -> Active
                    \-> Cached -----/
                    \-> Fallback
```

### Work

1. Compute readiness before choosing whether conventional CSM must raster.
2. Treat resource, shader compatibility, page-table, and required texture-backend failures as fallback reasons.
3. Skip conventional CSM when:
   - a valid VSM cache can be used; or
   - the current frame is guaranteed to complete all required VSM writes.
4. Keep conventional CSM when VSM readiness is uncertain.
5. Expose the current state and last fallback reason through allocation-free runtime data; format user-facing text only on demand.

### Verification

- A VSM cache-hit frame contains no conventional CSM caster draws.
- Every forced VSM failure produces valid conventional CSM in the same frame.
- Toggling VSM, changing resolution, changing cascade count, and losing GPUDriven readiness does not create a shadowless frame.

### Exit Criteria

- Conventional CSM functions as a fallback rather than a permanent duplicate cost.

## P3.1 - Receiver Page Marking and Physical Allocation

### P3-A Implementation Status

Implemented on 2026-09-03:

- receiver page marking in both full-screen and tiled CSM resolve paths;
- one-frame-latency bootstrap that keeps conventional CSM until feedback exists;
- an unmapped page table plus requested, allocated, dirty, cached, static, dynamic, and last-requested-frame metadata;
- deterministic ascending-index allocation into a fixed 256-page physical pool;
- shared static/dynamic physical addresses, selective static dirty-page clear, and allocated-page dynamic clear;
- deterministic overflow accounting without overwriting an existing mapping.

Allocator reclamation added on 2026-09-04:

- fill unused slots first, then evict the least recently requested resident absent from the current feedback; equal ages choose the lowest physical slot;
- protect all current-feedback pages, including requests already processed and pages allocated earlier in the same dispatch;
- revoke the old virtual mapping and allocation/cache flags before reassigning its physical slot; new owners are dirty and not cached so both depth layers are cleared before redraw;
- retain unrequested cached pages when there is no budget pressure; overflow now means current-feedback demand exceeds capacity, not that historical allocations exhausted the pool;
- keep the four-counter layout: resident count, current request count, new assignments (including reused slots), and overflow. No CPU readback, new buffers, or render-loop managed allocations are introduced.

Validation: the original saturated-pool GPU reproduction now maps the new page and unmaps its victim without disturbing a requested resident. An isolated 13-frame GPU probe passed mapping/flag checks and 3,328 static/dynamic pool pixel checks. A 40-frame probe with 1,024 virtual pages and the real 256-page budget passed 3,123 assignments, including over-budget feedback and subsequent recovery. The compute shader imported without messages and the new multi-frame regression test compiled. Unity Test Framework execution and scene-level visual/Profiler acceptance remain manual while the Editor is active.

Receiver feedback ownership corrected on 2026-09-04:

- record the producer's full 64-bit camera EntityId together with its frame, and consume feedback only for that camera's immediately following frame; resource release clears both fields;
- clear shared GPU request bits and request timestamps before either resolve path when the producer changes or a frame repeats/rewinds. Preserve page mappings, depth ownership, and dirty/cached flags;
- keep resident request bits until allocation completes, so eviction protection requires actual current demand rather than a coincident reset timestamp (including frame zero);
- retain the shared single-pool architecture: camera switches bootstrap through conventional CSM. Multiple cameras continuously alternating feedback can remain on CSM; this is safe fallback, not independent multi-camera VSM caching.

Validation: the source-extracted ownership predicates passed 440 checks and 4,096 warm iterations with zero current-thread managed allocation. GPU checks passed producer-switch/rewind cases at feedback frames 0 and 10; the 40-frame 256-page allocator probe and 13-frame static/dynamic pool probe also remained clean. Unity Test Framework and scene-level multi-camera/Profiler acceptance remain manual while the Editor is active.

Page-state debug views added on 2026-09-04:

- `VSMDebugPass.VisualizationMode` now offers `Page States`, `Requested`, `Allocated`, `Dirty / Redrawn`, `Cached`, `Unmapped`, `Evicted`, and `Overflow`, in addition to the existing depth views;
- page views display the shared virtual map in four quadrants (`C0/C1` above `C2/C3`); inactive cascades remain blank. Allocation is read from the page table/metadata, independently of whether any depth texel is nonzero;
- metadata's existing fourth word stores the current allocation/static-render submission snapshot. Requested bits survive allocator consumption, refreshed pages remain visibly dirty after finalization, and eviction/overflow events last until the next allocation. Subsequent receiver feedback writes cannot change the snapshot;
- the snapshot is presented only for its producing camera and frame after successful VSM submission. Disabled VSM, bootstrap/fallback, a different camera, or a Debug Pass placed before CSM shows amber/dark hatching instead of stale page states;
- the GPU-only header displays `RES` (resident / physical budget), `REQ` (consumed receiver requests), `NEW` (assignments, including reused slots), and `OVF` (overflow), with budget-relative bars. No CPU readback or extra page-state buffer is used.

Usage: place `VSMDebugPass` after `CSMShadowPass`, connect `OutputTexture` to the desired debug output, and choose `Page States` or an individual mask. The legend uses `MAP` green for allocated, `REQ` yellow for requested, `DRT` orange for dirty/refreshed, `CCH` blue for cached without redraw, `UNM` gray for unmapped, `EVI` purple for evicted, and `OVF` red for allocation overflow. The combined view prioritizes overflow, eviction, dirty, cached, allocated, then unmapped; individual masks expose overlapping states (for example, an overflow page is also requested and unmapped). These are the last **consumed** requests, normally produced in the preceding frame, not the next feedback being collected by resolve. `Pool` and `Exposure` affect depth views only; page views share allocation and show the static cache's refresh/cache state. The old `Occupancy` value is preserved for serialization and relabeled `Depth Coverage (Occupancy)` to distinguish written texels from allocated pages.

P3.2 performs Meshlet page-aware submission after the existing cascade culling stages. Scene-level reference-image and GPU Profiler acceptance remain separate from the page-debug implementation.

Validation: the focused D3D12 batch run passed 45 of 46 tests, including actual pixel/color and GPU-counter checks for all eight page modes, empty-but-allocated pages, snapshot-versus-next-feedback separation, inactive cascades, camera/frame ownership, zero warm managed allocation, 13-frame allocator/eviction events, and local-invalidation snapshots. The remaining existing graph-serialization fixture had an unconnected output and was culled; it now connects to FinalBlit. A no-snapshot hatching regression was also added. Both final test edits compile with the current Unity 6000.7.0a6 references, but were not rerun because the interactive Editor reopened. Run `VSMDebugPassTests` manually to finish those two checks; no scene-level or all-thread Profiler acceptance is implied.

### Objective

Replace the fully resident page table with demand-driven virtual pages under a fixed physical memory budget.

### Work

1. Mark requested virtual pages from visible shadow receivers.
2. Add page metadata for requested, allocated, dirty, cached, static, dynamic, and last-used state.
3. Add a bounded physical-page allocator (dense unused-slot tail plus in-place eviction; no separate free list is needed while slots are never released without reassignment).
4. Represent unmapped pages explicitly in the page table.
5. Allocate one physical address shared by the static and dynamic depth layers.
6. Add deterministic eviction and overflow handling.
7. Clear only newly allocated or dirty physical pages.
8. Extend debug visualization with requested, allocated, dirty, cached, unmapped, and evicted modes.

### Unity Renderer Compatibility Behavior

Unity Renderers continue to raster the full cascade. Their shader:

- calculates the virtual page from `SV_Position`;
- reads the shared page table;
- rejects unmapped pages;
- writes only mapped dynamic physical pages.

The unused vertex and raster work is accepted because the compatibility set is small.

### Verification

- Only receiver-requested pages become allocated.
- Unmapped pages never write an arbitrary physical address.
- Allocation overflow is visible and deterministic rather than corrupting existing pages.
- Unity compatibility casters continue to work without per-page RendererLists.

### Exit Criteria

- Physical memory use is bounded independently of virtual resolution.
- The fully resident fallback can be disabled in normal development configurations.

## P3.2 - Meshlet Per-Page Culling and Dirty-Page Raster

### P3-B Implementation Status

Implemented on 2026-09-03:

- preserved the existing four-cascade instance, pass-mask, main-camera LOD, and fine Meshlet culling stages;
- added a GPU page-expansion stage that projects each visible Meshlet sphere into virtual-page bounds and rejects unmapped pages plus clean static pages;
- emits compact `instance / Meshlet / virtual page / cascade` requests; the bounded large-Meshlet path below adds a second indirect command per RendererList;
- routes all page requests through one array-target draw using `SV_RenderTargetArrayIndex`, with per-page clip distances so triangles cannot raster outside their assigned page;
- keeps static and dynamic culling streams sequential and independent while reusing the same grow-only scratch buffers;
- bounds normal expansion to four page requests per input Meshlet; large Meshlets now use page-instanced, page-clipped draws instead of a full-cascade sentinel (see the 2026-09-04 correction below);
- leaves the small Unity Renderer compatibility set on its existing full-cascade dynamic-pool path.

The page-aware raster still uses the virtual-resolution cascade depth array as transient depth storage because the physical pool is written directly through the shared UAV ABI. A later physical-page depth-array conversion is optional; it is not required for page-granular Meshlet submission or cache filtering.

Strict clean-page raster exclusion added on 2026-09-04:

- compact a GPU raster-page list from resident physical owners before each pool's page culling: static includes only allocated dirty pages, dynamic includes allocated pages;
- keep small Meshlets on the existing direct page-request path; store one bounds record for each relevant large Meshlet and instance it over the compact raster-page list through a second indirect command per RendererList;
- reject other cascades and pages outside that record's bounds in the vertex shader, before pulling geometry; every surviving instance uses the same concrete page clip distances as a small request. No unbounded/full-cascade sentinel remains;
- preserve the existing four-record-per-source scratch capacity: small requests grow from each RendererList range's front, large records from its back. Each source emits either at most four small records or one large record, so the ranges cannot collide. Only a fixed 257-word page list and a second indirect-argument set are added;
- all-clean static pools produce zero indirect instances in both paths. Large Meshlets can still incur vertex invocations for relevant pages rejected by the bounds/cascade checks; this is bounded vertex overhead, not rasterization of clean pages. Transient full-resolution depth storage/clears and overall GPU performance acceptance remain unchanged.

Validation: after the interactive Editor exited, the focused D3D12 Unity batch run passed all 8 tests (0 failed/skipped). GPU request regressions cover all-clean, one-dirty, sparse dirty, all-256-dirty and dynamic-resident cases across four cascades and two RendererLists, including mixed small/large Meshlets, non-contiguous physical ownership, exact page coverage and scratch guards. The warm resource test covers reuse/release of the new page list and zero managed allocation; the raster shader compiles synchronously for opaque/alpha-tested and bindless/virtual-texture page-caster variants. Independent Runtime and Editor test assembly compilation also passed. Scene-level reference-image and GPU Profiler acceptance remain separate from these checks.

### Objective

Move the dominant Meshlet path from full-cascade raster to page-granular GPU work.

### Work

1. Build compact page views for allocated dirty pages.
2. Cull Meshlet instances against relevant cascade/page bounds.
3. Retain the existing instance, pass-mask, LOD, and fine Meshlet culling stages.
4. Emit page-aware render requests containing the page-view identity and Meshlet request.
5. Transform Meshlet vertices into page-local clip space.
6. Raster only dirty or uncached pages.
7. Keep static and dynamic request streams separate.
8. Size scratch buffers from bounded metadata and use grow-only capacity changes outside stable frames.

### Verification

- Meshlet VSM output matches the full-cascade reference path.
- Cached clean pages generate no Meshlet raster work.
- Page frustum and LOD decisions allow false positives but no false negatives.
- Culling, list building, and indirect draw preparation allocate zero managed bytes after warm-up.

### Exit Criteria

- Meshlet raster cost scales with dirty requested pages rather than full virtual cascade area.
- Unity compatibility raster remains a small, separately measurable cost.

## P3.3 - Meshlet Page-Level Cache Invalidation

### P3-C Implementation Status

Implemented on 2026-09-04:

- added a persistent PrimitiveScene invalidation journal that retains old and new world-space AABBs until the VSM cache acknowledges the matching static-shadow revision;
- records old/new coverage for static movement and state transitions, old coverage for removal, and current coverage for explicit geometry/material resource changes;
- projects each invalidation AABB through every active cascade on the GPU and dirties only already allocated pages intersecting the resulting page rectangle;
- separates localized static-content revisions from cache-address/configuration changes, preserving page-table entries and physical addresses for unrelated pages;
- keeps full-static-pool invalidation for first synchronization, missing journals, journal overflow, non-finite bounds, and untracked material/geometry changes;
- classifies skinned Meshlets as dynamic-pool casters even when their GameObject carries a static flag;
- leaves Unity Renderer compatibility casters exclusively in the always-refreshed dynamic pool.

The invalidation journal is bounded to 1024 AABBs. Overflow deliberately discards the partial journal and requests a conservative full refresh rather than risking stale static depth.

Static-culling cache-key coverage corrected on 2026-09-04:

- include the camera layer mask, the complete consumed main-camera LOD context (view-projection, position, up/right vectors, and scaled pixel dimensions), and each active cascade's consumed culling fields (view/projection, position, receiver sphere, frustum planes, pass mask, and projection mode);
- build LOD/cascade contexts during `Prepare` once, copy their values into the key, and submit the same snapshot during `Record`; mutable arrays, scratch offsets, padding, and inactive cascades do not affect key identity;
- reject non-finite culling inputs and fully invalidate static pages when culling configuration changes, while retaining localized invalidation for static-content revisions. Camera/LOD changes, including jitter if present in the consumed projection, can therefore reduce cache hits; no quantization or LOD stabilization is introduced here.

Focused validation: Runtime and Editor test assemblies contain the updated key and regression methods. An independent C# probe compiled the source-extracted key, frustum accessor, context types, and refresh predicates against Unity's managed math types: 64 checks passed, with zero current-thread managed bytes across 4,096 warm construction/hash/cache-check iterations. Regression coverage includes snapshot ownership and inactive/dispatch-only state. Unity Test Framework, scene-level visuals, and all-thread Profiler acceptance remain manual while the Editor is active.

Mixed tracked/untracked change coverage corrected on 2026-09-04:

- replace the frame-wide "some resource journal/content change exists" fallback suppression with entity-scoped journal coverage;
- retain owned snapshots of actual emitted primitive/section/geometry/material identities, so a caster becoming unrenderable, reappearing, or changing resource identity cannot disappear from change tracking when current-resource validation skips it;
- require a full static refresh for any source change without that same primitive's added/removed/resources journal entry, independently of localized changes elsewhere. First builds and texture-backend identity/binding-revision changes also require conservative refresh;
- preserve localized content invalidation and journal-covered removals/resource changes. Source ordering and database compaction do not count as identity changes; reusable dictionaries/sets avoid steady-state managed allocation.

Validation: the pre-fix D3D12 integration run reproduced both failures (A stops drawing while B has either tracked content changes or a resource journal); the three local/journal-covered controls passed. The updated Runtime and Editor test assemblies compile independently with Unity 6000.7.0a6 references. An isolated probe using the production snapshot/invalidation methods and managed stand-ins for Unity resources passed 481 checks across source/journal/global-change combinations, with zero managed bytes over 4,096 warmed iterations. Five integration cases and a snapshot/reordering/zero-allocation regression are present in `VividGPUDrivenSceneDataBuilderTests`; their post-fix Unity run remains manual because the interactive Editor reopened. Scene-level and all-thread Profiler acceptance is still pending.

Validation completed: Unity rebuilt the Runtime and Editor test assemblies, the compute shader imported without errors, and a temporary GPU probe verified two cascades across 32 pages (old/new bounds, overlapping invalidations, out-of-range bounds, and preservation of unallocated/unaffected pages). Journal lifetime, overflow fallback, cache-key separation, and warm-path allocation regression tests were added. Unity Test Framework execution and scene-level visual/Profiler acceptance remain manual while the Editor is active.

VT alpha-coverage cache invalidation completed on 2026-09-05:

- snapshot the GPU-driven VT allocation's page-table version into frame bindings, including arrival, eviction and transition/reveal changes. Binding reindexing and upload acknowledgement retain that identity;
- also track deferred mip-tail uploads that replace bootstrap pixels in-place without changing the page table;
- before building the VSM cache key, compare the sampling revision, page-table resource identity and the actual space-resolved adaptive mip bias used by the caster shader. Binding loss/recovery also invalidates coverage;
- append bounds for static alpha-tested Meshlets to the existing P3-C journal, once per primitive even with multiple alpha sections. Preserve other pending changes and full-refresh fallback; dynamic, disabled, non-shadow and opaque-only primitives are excluded;
- this is conservative at the VT allocation level, not per texture/page dependency tracking: any sampling change in the GPU-driven allocation invalidates all its static alpha casters. Overlapping opaque shadow pages can consequently redraw, and journal overflow still requests a full static refresh. Stable sampling does not scan casters or dirty pages; Unity Renderer casters remain in the dynamic pool.

Validation: Runtime and Editor test assemblies compiled independently against Unity 6000.7.0a6; the Editor console returned no errors. A source-extracted CPU probe using managed stand-ins passed 84 checks and measured zero managed bytes over 4,096 warmed sampling-check/invalidation iterations. Added regressions for arrival/reveal/eviction, atomic and in-place refresh, binding snapshots, bias resolution, static-alpha selection, journal preservation and warmed allocation. Unity Test Framework, streamed alpha-caster scene/GPU acceptance and all-thread Profiler checks remain manual while the Editor is active.

### Objective

Invalidate only pages affected by Meshlet scene changes.

### Work

1. Project both old and new primitive bounds into relevant cascade page ranges.
2. Dirty the union of old and new covered pages for movement, enable/disable, and removal.
3. Dirty covered pages for geometry, material coverage, LOD policy, and shadow-state changes.
4. Treat deforming or explicitly dynamic Meshlets as dynamic-pool casters.
5. Keep a conservative whole-static-pool invalidation fallback for unknown changes.
6. Preserve page-table and physical-address validity across unrelated primitive changes.

### Unity Renderer Policy

Unity Renderers remain dynamic-pool casters. No Unity Renderer page-level invalidation system is required in this milestone.

### Verification

- Moving one static Meshlet invalidates only its old and new page coverage.
- Removing an occluder clears its old shadow without invalidating unrelated pages.
- Material alpha-coverage changes invalidate affected pages.
- Unknown invalidation causes fall back to conservative refresh rather than stale shadows.

### Exit Criteria

- Stable regions retain cached static pages while localized Meshlet changes occur elsewhere.

## P4 - Directional Clipmaps, Filtering, and Quality

### Objective

Improve spatial stability and shadow quality only after allocation and invalidation are trustworthy.

### P4-A - Projection and Resource Decoupling

**Status (2026-09-04): implementation and independent compilation complete; Unity GPU/visual acceptance pending.**

- Added a VSM projection ABI: a structured buffer of world-to-clip/world-to-shadow
  matrices, selection spheres, world-units-per-virtual-texel, receiver bias, border,
  and maximum distance. Page culling, local invalidation, Meshlet rasterization,
  hard-shadow resolve, and receiver feedback consume this ABI. VSM page-table
  sizing no longer clamps the projection count to four.
- At the P4-A checkpoint the producer was deliberately a CSM adapter (superseded
  by P4-B below). CPU broad-phase culling,
  source draw sets, and cache-key matrices still use the existing four-cascade
  producer. This is not stable directional clipmaps; replacing that producer and
  its cache identity belongs to P4-B. No claim of stable XY/Z or camera remapping
  is made here.
- Added the Volume field **Virtual Shadow Map Resolution**: 0 follows the light's
  CSM resolution; nonzero values are rounded up to 128 texels, up to 16384.
  This does not resize the conventional CSM atlas or raise the physical-page
  budget. The default therefore retains the existing resolution.
- Meshlet page requests now rasterize into **128 x 128 x physical-capacity D32**.
  The request's virtual page selects a physical DSV layer; the fragment writer
  uses the same page identity plus local pixel coordinates to address the pool.
  Pixel density, alpha derivatives, depth encoding, and existing page clipping
  are preserved.
- The minority Unity Renderer path reuses a **maximum 2048 x 2048 D32** target
  across coarse virtual-map tiles. One renderer list is built per projection
  and reused; there are no CPU draws per physical page. The explicit tradeoff is
  additional compatibility draws: per projection, 2K/4K/8K/16K use 1/4/16/64
  tile submissions, including tiles without allocated pages. At four 16K
  projections this is 256 renderer-list submissions, not 256 per-page commands.
  This path needs profiling with the actual minority-caster count.
- Maximum nominal raster-depth storage is **16 MiB Meshlet + 16 MiB Unity =
  32 MiB**, instead of 4 GiB for four dense 16K D32 layers. The two existing
  physical pools remain at most 32 MiB combined; page tables/metadata and CSM
  fallback resources are additional. Only bounded raster textures are created,
  even at 16K virtual resolution.
- Bootstrap and Record-time CSM fallback still request pages using the VSM
  projections and VSM receiver bias. Request collection uses full-screen resolve
  so fully-lit CSM tiles cannot suppress feedback.
- The caster capability tag is now **VividVSMCaster=2**. Old True-tagged custom
  shaders fail closed to CSM. A V2 shader must preserve its deformation/coverage
  path, use VividRP Core's shadow TransformWorldToHClip (which selects
  _VSMRasterViewProjection under _VSMUnityRasterEnabled), and call the shared
  one-argument VividWriteVSMDepth with tile-local SV_Position. Custom projection
  code must implement that runtime selection itself. Fragment-only
  VIVID_VSM_CASTER variants remain supported; changing the tag alone is not a
  valid migration. All six built-in compatibility shaders were updated.
- Existing reverse-Z uint depth, static/dynamic composition, fixed comparison
  bias and hard-shadow reference remain. No PCF, SMRT, stable clipmap generation,
  coarser-level missing-page fallback, or expanded residency budget was added.

Verification:

- Independent Roslyn compilation: Runtime, Editor, and Editor.Tests passed.
- DXC DXIL compilation: full resolve, eight VSM management kernels, CSM tile
  classify/resolve, alpha-tested Meshlet vertex/fragment, and the Unity caster
  shared ABI vertex/fragment passed. This does not substitute for Unity's
  material import/variant pipeline or a Vulkan device run.
- Standalone probe using the shipping RasterTransform method and Unity's managed
  matrix implementation: 402,240 pixel/depth checks passed (including partial
  coarse tiles and both UV origins); 4,096 warmed transforms allocated 0 B.
  DXC reflection confirms matrix/vector offsets 0/64/128/144 and a 160-byte stride.
  This is not a measurement of the complete Unity render loop.
- Added regression coverage for the 160-byte CPU/GPU ABI, projection counts above
  four, independent/page-aligned resolution, page/coarse-tile pixel-center and
  depth equivalence with both UV origins, 16K bounded raster descriptors, resource
  reuse, and warmed adapter/upload allocation. Existing GPU cull/invalidation
  fixtures now bind the VSM projection buffer.
- Unity Editor is open: do not start batchmode or UI-driven tests. Manually run
  CascadedShadowSettingsVolumeTests and VividRenderPipelineAssetGpuDrivenTests;
  verify VSM debug output at 2K/4K/16K, opaque/alpha-tested/terrain-hole casters,
  tile/page boundaries, static-cache reuse, moving dynamic casters, camera
  switching, version-1 custom-shader fallback, and VSM disabled. Measure managed
  allocation and compatibility-tile GPU/CPU cost after warmup.

### P4-B - Stable Directional Clipmaps

**Status (2026-09-04): implementation and independent compilation complete; Unity GPU/visual acceptance pending.**

- Replaced the CSM adapter with an independent directional clipmap producer.
  **Virtual Shadow Map First Level** is the base-2 radius exponent (default 2:
  radius 4 world units). Levels double until the outer radius covers twice
  Max Distance; at the default 150 distance this produces eight levels, radii
  4 through 512. There are at most 16 levels; extreme distance raises the
  effective first level if necessary. Effective resolution is at least 512,
  page-aligned, at most 16384; 0 still follows the light's CSM resolution.
- Raster XY origins snap to whole virtual pages in light space. CPU origins are
  signed 64-bit page coordinates. Receiver selection follows the unsnapped
  camera, uses the inner half-radius of each level, blends over its outer 20%,
  and retains the existing maximum-distance fade. Camera rotation, FOV and
  viewport changes do not change the VSM projection or its LOD basis.
- A power-of-two, padded light-space Z interval persists while it contains both
  the caster bounds and receiver range. Bounds shrinking or moving within it
  does not refresh cached depth. This first implementation shares one Z range
  across levels, unlike Unreal's per-level range optimization: large scene
  bounds can reduce near-level depth precision. Bias tuning remains P4-D.
- The recorded projection snapshot owns camera/light identity, exact light
  rotation, effective resolution, level layout and Z range. Preparation alone
  never advances that snapshot. Integer XY scrolling remaps the page table
  **and all receiver-feedback metadata**, preserving physical slot/depth identity.
  Outgoing pages free their slots; entering pages start unmapped. The allocator
  reuses owner-array holes before eviction, without compacting cached depth.
- Camera/light/basis/Z/layout changes clear old coordinate mappings and use CSM
  for one feedback-bootstrap frame. A full-window jump similarly reboots
  feedback. Skipped layout Record cannot publish valid feedback. The shared
  runtime still has one camera owner, not simultaneous per-camera caches.
- Meshlet VSM culling uses independent clipmap prisms, no receiver-centered
  culling sphere, and orthographic shadow-texel LOD error instead of observer
  distance/FOV/viewport. CPU candidate selection conservatively includes the
  outer clipmap plus all conventional CSM fallback frusta. Thus cached pages
  are not silently tied to changing camera-LOD or CSM broad-phase inputs.
  Camera masks, forced LOD, error threshold, slope bias, content/texture versions
  and the P3 invalidation journal still participate in refresh decisions.
- Unity compatibility casters use the same clipmap projections and their own
  shadow split data. CSM cascade settings remain relevant to fallback only.
  Valid VSM frames still avoid conventional CSM rasterization; fallback retains
  the original CSM path. The V2 custom-caster ABI is unchanged.
- Debug page-state views now lay out all clipmap levels, rather than four fixed
  quadrants. Physical atlas debug modes are unchanged.
- Physical residency remains capped at 256 pages. Bounded Meshlet/Unity raster
  depth targets from P4-A are unchanged. Scratch table/metadata storage adds
  20 bytes per virtual page (5 MiB at 16 levels of 16K). Unity compatibility
  raster still submits coarse tiles per level: eight 16K levels imply 512
  renderer-list tile submissions. This is explicitly a profiling risk, not a
  claim that increasing levels or resolution is free.
- No coarser-level missing-page fallback, cross-page filtering, SMRT or expanded
  residency budget is included. Newly visible/unmapped receivers can still
  expose the existing one-frame feedback delay; that policy belongs to P4-C.

Verification:

- Independent Roslyn compilation of Runtime, Editor and Editor.Tests passed.
- DXC DXIL compilation passed for every CSMShadowResolve compute kernel,
  including both new remap kernels, plus MeshletListBuild, alpha-tested Meshlet
  raster and the adaptive debug page shader. Vulkan/device validation is pending.
- Standalone CPU probe: 2,904,010 depth-coverage/page-remap-model checks passed;
  4,096 warmed calls to the shipping FitDepthInterval method allocated 0 B.
  This does not execute the GPU remap or measure the Unity render loop.
- Added VirtualShadowMapClipmapTests for page alignment/negative coordinates,
  retained-page texel/depth equivalence, stable Z/escape, ownership and skipped
  Record, generation-based cache identity, eight-level GPU remapping, feedback
  preservation, full jumps/basis reset and hole reuse. Updated the warmed
  layout/resource/upload GC regression and actual-culling source contracts.
- Unity Editor is open, so these Unity tests have **not been run**. Manually run
  VirtualShadowMapClipmapTests, CascadedShadowSettingsVolumeTests,
  VividRenderPipelineAssetGpuDrivenTests and VSMDebugPassTests.
  On D3D12 and Vulkan, verify slow/diagonal translation, negative world positions,
  camera rotation/FOV changes, light rotation, Z-range escape, camera switches,
  512/2K/16K resolutions, mixed alpha-tested casters, static invalidation, empty
  entering pages, full-window jumps and VSM disabled. Inspect all-level debug
  output and profile warmed managed allocations plus compatibility-tile cost.

### P4-C - Cross-Page Sampling and Missing-Page Degradation

**Status (2026-09-04): implementation and independent validation complete; Unity GPU/visual acceptance pending.**

- Defined an explicit no-border-duplication sampling contract: each signed offset
  tap addresses a virtual texel and performs its own page-table lookup before
  loading the static/dynamic physical pools. Physical atlas adjacency is never
  used as virtual adjacency. Out-of-map taps are unavailable, not clamped/wrapped.
  Conservative bounds/caster addressing remains unchanged, including its upper
  endpoint coverage rule; receiver sampling uses the half-open virtual domain.
- The reference shadow remains one point comparison. A one-texel request halo
  covers neighboring hard taps (at most four distinct pages per level at a page
  corner), preparing the request footprint for P4-D without adding PCF here.
  Filters with a larger radius must explicitly expand this footprint; the
  current halo is not a blanket guarantee for arbitrary future filter kernels.
- Resolve requests the preferred level and its entire coarser chain, including
  during CSM bootstrap and while fine depth is already resident. Each level uses
  the original world receiver, its own projection, depth and normal-bias scale;
  fine UV/depth values are never reused for a coarser clipmap.
- An unmapped, dirty or inconsistent page is unavailable. A completed allocated
  page with zero depth is a valid lit sample and does not trigger fallback.
  Missing fine coverage walks toward the nearest available coarser level.
  Missing transition coverage retains the valid primary sample; an already
  degraded primary is not blended a second time. A real lit coarse sample may
  still participate in the ordinary level transition.
- The last clipmap's feedback carries a live coarse-priority bit (metadata.x bit
  8, alongside Requested). Allocation processes that demand first and may evict
  requested fine detail to establish fallback coverage. Other requests run from
  coarse to fine, preserving page order within each level. A requested resident
  is protected against equal/finer demand; coarser demand can evict finer detail.
  LRU still chooses among eligible victims. Evicted fine demand is preserved
  long enough to account for its eventual allocation or overflow. Priority and
  request bits are cleared together, including on camera-feedback reset; debug
  Requested/Evicted/Overflow snapshots remain consistent with the counters.
- If every level is unavailable, the terminal policy is **lit**. This reduces
  missing-detail artifacts when coarse coverage is resident; it does not promise
  shadow coverage when even the coarsest footprint exceeds the physical budget.
  The 256-page cap, one-frame feedback latency, shared camera ownership, static/
  dynamic pools and CSM bootstrap/failure fallback are unchanged. There is no
  new per-frame CSM raster fallback, new runtime resource, caster ABI revision,
  quality toggle, PCF or soft-shadow mode.
- Tradeoff: extra requests and priority can reduce fine-page residency; the
  allocator scans virtual entries twice. Profile request pressure, overflow,
  coarse coverage and resolve/allocator GPU time at 2K and 16K before increasing
  resolution. No managed render-loop code changed in this step.

2026-09-05 transition fix: a live 4096-resolution snapshot requested 1,022 pages
against the 256-page budget. The old allocator stayed at per-level residency
`[254,0,0,0,0,0,0,0,0,2]` across three isolated GPU dispatches, starving every
intermediate transition level. The same snapshot now recovers in one dispatch
to `[0,0,73,118,37,14,6,4,2,2]`, with zero new allocations on the next two
dispatches. The budget is unchanged; continuous fallback coverage takes priority
over finest detail. This does not add temporal residency blending or guarantee
seam-free transitions under arbitrary page pressure. Added cold-pool and
multi-frame saturation regressions. All 33 compute entry points and the Runtime,
Editor and Editor.Tests assemblies compiled. Unity Test Framework remains manual
while the Editor is open: run VirtualShadowMapSamplingTests,
VirtualShadowMapClipmapTests and CascadedShadowSettingsVolumeTests. Importing the
shader reported the existing PipelineResources.asset importer-consistency warning;
that generated asset was not changed. Live viewpoints changed during validation,
so the scene readbacks are not a fixed-camera before/after acceptance comparison.

Verification:

- Independent Roslyn compilation: Runtime, Editor and Editor.Tests passed.
- DXC DXIL compilation: all 21 CSMShadowResolve kernels (including the Bend
  variants with their defines) and all three sampling-test kernels passed.
- The shipping allocator body was syntax-translated from HLSL into a standalone
  C# probe: 10,000 randomized frames and 1,000,000 ownership, priority and overflow
  checks passed, including arbitrary physical-slot holes. This is CPU validation,
  not a GPU execution or synchronization test.
- An independent addressing/footprint model passed 153,609 checks, including map
  boundaries, page corners, non-power-of-two resolution and unrelated physical
  slots. The Unity GPU tests exercise the actual production shader functions,
  rather than duplicating those functions in a test shader.
- Added VirtualShadowMapSamplingTests covering both directions of every page
  edge/corner, out-of-map rejection, empty/dirty pages, world/depth/bias
  reprojection, missing and valid-lit transition levels, all-missing/bootstrap
  feedback, map-edge request clipping, priority takeover, coarse oversubscription
  and priority reset. Updated the affected addressing/feedback source contracts.
- Unity Editor remains open: Unity Test Framework tests were **not run**.
  Manually run VirtualShadowMapSamplingTests, VirtualShadowMapClipmapTests,
  CascadedShadowSettingsVolumeTests and VividRenderPipelineAssetGpuDrivenTests.
  On D3D12 and Vulkan inspect translation across page/level boundaries, newly
  exposed receivers, fine-page eviction, full coarse-budget exhaustion, static
  invalidation, moving dynamic/alpha-tested casters, camera switching and VSM
  disabled. Check Requested/Allocated/Evicted/Overflow debug views and GPU cost.
  Continue the existing warmed Unity Profiler/GC regression checks.

### P4-D - Bias, PCF, and Transitions

**Status (2026-09-04): implementation and independent compilation complete; Unity GPU/visual and performance acceptance pending.**

- Added Volume controls **Virtual Shadow Map PCF** (default off) and **Virtual
  Shadow Map Transition** (default 0.2, range 0..0.5). PCF off retains the
  single-point hard-shadow reference. Transition is the fraction of each
  level's selection radius used for blending; zero disables it. Both controls
  are exposed by the custom Volume editor and are independent of CSM quality.
- PCF is a normalized 3x3 tent of explicit virtual taps, not a hardware sampler
  applied to the physical atlas. Per-axis weight is
  `max(1.5 - abs(offset - (frac(uv * resolution) - 0.5)), 0)` for offsets -1..1.
  Normalization preserves constant shadow values and the footprint is continuous
  across texel/page boundaries, including unrelated physical slots. Each tap
  composes static/dynamic depth and performs its own comparison.
- Every positive-weight tap must be valid. If one is missing or dirty, the
  entire kernel retries at a coarser level, reprojecting the original world
  receiver and rebuilding that level's bias. There is no partial-footprint
  renormalization or implicit lit sample at a hole. The P4-C one-texel request
  halo already covers this kernel; whole-chain requests, coarse allocation
  priority, terminal-lit policy and the 256-page budget are unchanged.
- VSM pools store unbiased depth for both Unity and Meshlet casters. VSM raster
  depth bias is explicitly zero; conventional CSM fallback retains its original
  raster bias and resolve behavior. Custom V2 casters must likewise preserve
  raw depth rather than add a private vertex/depth offset or ShaderLab Offset
  in their VSM variant. No caster ABI or projection-buffer stride change is
  required (V2 and 160 bytes remain).
- Reused the directional light's **Depth Bias**, **Normal Bias**, and
  **Slope-Scale Depth Bias**, with their VSM units documented in the editor:
  - Constant comparison bias is `Depth Bias * worldTexelSize * depthScale`.
    Positive Depth Bias has a device-depth precision floor of 2^-23; zero
    disables that floor. This replaces P4-C's fixed 1/65536 VSM compare offset.
  - Normal offset is `Normal Bias * worldTexelSize * sqrt(1 - NdotZ^2)` along
    the normalized receiver normal. It vanishes on a light-facing flat plane.
  - Receiver-plane depth gradient is derived from the projection's axes and
    receiver normal, scaled to device depth per virtual texel. Each gradient
    axis is bounded to four world texels of light-axis depth. Slope comparison
    bias is the light's slope multiplier times the maximum axis gradient,
    capped at the same four-texel depth. The compare sign follows reversed Z.
  - Both Hard and PCF correct each tap to the estimated receiver plane at the
    actual texel center before comparison. Hard remains a single depth compare,
    not a filter. Bias is rebuilt per fallback and transition level, respecting
    its texel size and projection depth range.
- These receiver-only controls are not static depth-cache inputs: quality
  changes do not remap pages or increment projection generation, and slope
  bias no longer invalidates VSM caster depth. Changing normal bias can still
  change the requested footprint and therefore residency work. Existing scenes
  may need bias retuning: Hard mode with the same numeric settings is not
  bit-identical to P4-C; all three light biases can be zeroed for the unbiased
  point reference.
- Level blending now uses `t*t*(3-2*t)` with smooth endpoints. An unavailable
  transition footprint preserves the primary sample; an already degraded
  primary is not blended again. Existing maximum-distance fade remains.

Verification and limits:

- Independent Roslyn compilation: Runtime, Editor and Editor.Tests passed.
  DXC DXIL compilation: all 21 production kernels/variants and all eight sampling
  test kernels passed, including the Bend variants with their defines.
- The shipping bias, tent-weight and transition functions were syntax-translated
  to a standalone C# numerical probe: 316,787 checks passed for depth/texel
  scaling, rotated projection axes, slope bounds, precision floor, weight
  normalization, boundary continuity and transition endpoints. This is CPU
  numerical validation, not GPU execution or backend conformance.
- Added production-function GPU regression tests for shuffled cross-page PCF,
  fractional weights, page-boundary continuity, whole-kernel fallback,
  unavailable transitions, sloped-plane correction, bias scaling/caps and
  level-transition endpoints. Added Volume defaults/clamps, unchanged cache
  geometry and receiver binding contracts; extended the warmed upload/GC test
  with the quality constant upload.
- Acne follow-up: the initial P4-D implementation incorrectly used the GBuffer
  shading-normal slope for geometric bias, and Hard omitted the receiver-to-texel
  center correction. Both could self-shadow a sloped surface even without PCF.
  VSM now reconstructs a geometric receiver normal from depth, choosing the
  nearest-depth one-sided derivative per axis and rejecting sky/duplicate edge
  samples. The shading normal only selects its hemisphere; it is the fallback
  when no nondegenerate depth plane can be reconstructed. No new texture or
  render-graph input is needed; this adds four depth loads and two world-position
  reconstructions per VSM receiver, not per level. Conventional CSM is unchanged.
- Added GPU regressions for Hard/PCF coplanar taps, normal-mapped sloped receivers
  with and without a real occluder, screen edges, depth discontinuities and
  isolated pixels. A standalone probe syntax-translating the production normal,
  bias and filter functions passed 30,016 checks using synthetic orthographic
  and perspective depth/world mappings. It reproduces Hard=0 / PCF=0.42857152
  with the wrong shading normal, then both=1 with reconstructed geometry and
  both=0 with an added occluder. This is not a GPU test or scene visual acceptance.
- Depth-neighborhood reconstruction remains approximate at thin geometry or
  boundaries where neither neighboring surface matches the center; extreme
  grazing angles are still slope-limited. Validate these cases and acne versus
  peter-panning in the actual scene rather than assuming a universal bias value.
- There is no temporal filtering: page availability/eviction can still cause
  visible quality changes. PCF costs up to nine virtual taps per attempted
  level, with additional coarse retries and transition sampling. Measure this
  bandwidth cost; no 5x5 filter, PCSS, SMRT or larger residency budget was added.
- Unity Editor was open, so Unity Test Framework tests were **not run**. The
  Unity console connector was unavailable; independent compilation does not
  establish successful Editor import. Manually run VirtualShadowMapSamplingTests,
  VirtualShadowMapClipmapTests, CascadedShadowSettingsVolumeTests and
  VividRenderPipelineAssetGpuDrivenTests. On D3D12 and Vulkan compare Hard/PCF
  for Unity/Meshlet opaque and alpha-tested casters, sloped and normal-mapped
  receivers, thin geometry, page/level crossings, missing/evicted pages, bias
  edits, depth-range changes and static-cache hits. Verify VSM-disabled and
  incompatible-shader CSM fallback, then run warmed GC/Profiler and GPU timing
  checks. Compilation alone does not close these quality/performance gates.

Next: close P4-A through P4-D GPU/visual acceptance and bias tuning before
evaluating higher-quality soft shadows or declaring the P4 production baseline.

### Work

1. Replace CSM-style virtual cascades with stable directional clipmap levels where appropriate.
2. Add page borders or a defined cross-page sampling policy.
3. Add cross-page hard-shadow taps, then small-kernel PCF.
4. Revisit caster slope bias, receiver normal bias, and fixed compare bias as one ABI.
5. Stabilize clipmap/page origins under camera translation.
6. Add level transition blending and maximum-distance fade.
7. Evaluate higher-quality soft-shadow methods only after hard-shadow output remains the reference mode.

### Verification

- Camera translation does not cause avoidable page or shadow-edge swimming.
- Filtering across a page boundary matches filtering inside a page.
- Bias behavior is consistent between Unity and Meshlet caster paths.

### Exit Criteria

- Hard-shadow VSM is the stable production baseline and quality modes are optional layers above it.

## P5 - Production Hardening and Performance Gates

### P5-A — Quality and Performance Baseline

Implemented instrumentation and a fixed measurement protocol on 2026-09-05.
See [VSMQualityBaseline.md](VSMQualityBaseline.md) and the six-configuration
[measurement sheet](VSMQualityBaseline.csv). Only the existing 2K Hard capture's
quality row is populated; the live GPU timing / 2K–8K Hard–PCF sweep is pending.

- Added opt-in VSMReceiverDebugPass after the completed directional shadow resolve,
  exposing preferred/actual/transition levels, fallback, geometric texel screen
  footprint, attempted taps/comparisons/levels and missing-page reasons. A raw
  RGBA32F output accompanies visualization for pixel inspection/export.
- Reuses production sampling functions through a diagnostic-only kernel define.
  Feedback writes are compiled out of diagnostic replay; default resolve has no
  instrumentation, new output binding or diagnostic texture. Exact camera/frame
  raster+resolve completion gates the snapshot, including record-time fallback.
- Added cached/literal GPU profiling scopes for layout/remap, allocation,
  invalidation, clears, static/dynamic culling/raster, compatibility raster,
  finalization and combined resolve/feedback. No per-frame timing readback or
  diagnostic string construction is introduced. Page culling is a nested scope.
- P5-A does not alter resolution, selection, PCF, cache policy or page capacity.
  Screen-density-driven selection, allocator scaling, sparse raster optimization
  and runtime quality stabilization belong to later P5 steps.

Validation results and remaining scene/API gates are recorded with the baseline.

### P5-B — Receiver Quality Independent of Stable Projections

Implemented an opt-in screen-density policy on 2026-09-05. See
[VSMReceiverQuality.md](VSMReceiverQuality.md) for controls, selection semantics,
debug channel definitions and validation limits.

- Target screen pixels per virtual texel plus receiver Resolution LOD Bias are
  independent uniforms, not inputs to clipmap layout or static-cache identity.
- Shared geometric screen footprint drives demand; actual biased projection
  coverage constrains it. Sampling and feedback start at the same preferred
  level and retain the coarse fallback chain and page-budget limits.
- Added density/coverage transitions and QualityPolicy diagnostics; retained
  legacy selection by default for P5-A comparisons. No projection fitting,
  allocator/budget change, new filter or temporal history is introduced.
- Roslyn/DXC compilation and an isolated DX12 selection probe passed. Added
  regression tests await manual execution because interactive Editors were open;
  scene quality/performance and full render-thread allocation gates remain open.

### Objective

Make the VSM path observable, bounded, and safe to enable by default on supported platforms.

### Required Counters

- requested, allocated, dirty, cached, and evicted pages;
- static and dynamic page occupancy;
- static cache hit and refresh counts;
- Unity Renderer caster draw count;
- Meshlet candidate, visible, and raster request counts;
- allocation overflow and fallback counts;
- VSM, Unity compatibility raster, Meshlet culling/raster, and resolve GPU timings.

### Managed-Allocation Gates

For every new stable hot path:

1. warm initialization and resource creation;
2. call the stable path repeatedly;
3. assert zero `GC.GetAllocatedBytesForCurrentThread()` growth;
4. verify relevant worker/render threads in the Unity Profiler;
5. keep formatting, diagnostic dumps, validation scans, and resource naming outside the frame loop.

### Performance Decisions

- Optimize Meshlet page culling and dirty-page raster before optimizing Unity compatibility raster.
- Do not add Unity per-page draw commands unless measured Unity compatibility cost is material.
- Do not cache Unity Renderers statically unless dynamic-pool raster is shown to be a bottleneck.
- Do not reduce conservative bounds or buffer capacities without tests proving no false negatives or overflow.

### Exit Criteria

- No recurring managed allocations after warm-up.
- All GPU and memory budgets are bounded and externally observable.
- Overflow and unsupported-content cases fall back or degrade deterministically.

## 8. Critical Path

```text
Shared caster ABI
    -> direct Unity + Meshlet physical writes
    -> shader compatibility validation
    -> static/dynamic physical pools
    -> deterministic CSM fallback removal
    -> receiver page marking and allocation
    -> Meshlet per-page culling
    -> Meshlet page-level invalidation
    -> clipmap stability and filtering
```

P3 must not begin by adding per-page Meshlet draws before page requests, allocation state, and debug visibility are trustworthy. P4 filtering must not hide hard-shadow address or cache bugs.

## 9. Validation Matrix

| Scenario | Expected result |
| --- | --- |
| Unity Renderer only | Direct dynamic-pool VSM or deterministic CSM fallback |
| MeshletRenderer only | Static/dynamic VSM according to instance state |
| Mixed renderer scene | Correct closest depth in one virtual address space |
| Overlapping backends | Draw order does not affect final shadow depth |
| Alpha-clipped Unity material | Coverage is clipped before physical write |
| Alpha-clipped Meshlet material | Coverage matches its material proxy/backend |
| Skinned Unity Renderer | Redrawn in dynamic pool every frame |
| Moving dynamic Meshlet | Redrawn in dynamic pool without static-cache invalidation |
| Stable static Meshlet | Static page remains cached |
| Unsupported Unity shader | Whole VSM path fails closed to CSM |
| Missing Meshlet VT data | Unity and opaque Meshlet casters remain valid |
| Page allocation overflow | Deterministic debug-visible degradation, no alias corruption |
| Cascade or resolution change | Required resources/cache are recreated or invalidated once |
| VSM toggle | No shadowless transition frame |

## 10. Primary Code Touchpoints

Expected implementation areas include:

- `Runtime/RenderPass/Core/CSMShadowPass.cs`
- `Runtime/RenderPass/Core/CSMShadowResolvePass.cs`
- `Runtime/RenderPass/Debug/VSMDebugPass.cs`
- `Runtime/RenderGraph/FrameContext/VividShadowData.cs`
- `Runtime/SubSystem/GPUDriven/VividGPUDrivenSystem.cs`
- `Runtime/SubSystem/GPUDriven/Component/VividMeshletRendererDatabase.cs`
- `Shaders/Core/Private/CSMShadowResolve.compute`
- `Shaders/Core/Private/GPUDriven/VisibilityBufferShadowCasterPass.shader`
- `Shaders/Material/ShaderPass/VividShaderPassShadowCaster.hlsl`
- `Shaders/Material/Unlit/UnlitPass.hlsl`
- `Shaders/Material/TerrainLit/TerrainLitPass.hlsl`
- supported material `.shader` files containing `ShadowCaster` passes
- focused Editor tests under `Tests/Editor/RenderPass/Shadows` and `Tests/Editor/SubSystem/GPUDriven`

New compute shaders or runtime resources must be registered through the normal pipeline resource synchronization path. Generated source-generator binaries, synchronized `PipelineResources.asset`, and Unity `.meta` files must not be edited manually.

## 11. Definition of Done

The directional VSM project is production ready when:

1. MeshletRenderer is the measured primary caster path and scales with dirty requested pages.
2. The small Unity Renderer compatibility set casts correct shadows through explicit shader variants without per-page command generation.
3. Static Meshlet pages remain cached while all Unity Renderer casters safely redraw in the dynamic pool.
4. Unsupported shaders and resource failures deterministically fall back to conventional CSM.
5. Conventional CSM is not rendered on valid active/cache-hit VSM frames.
6. Page allocation, eviction, invalidation, raster, resolve, and debug behavior are deterministic and testable.
7. Stable render-loop execution performs zero recurring managed allocation after warm-up.
