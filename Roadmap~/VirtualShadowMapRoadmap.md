# Virtual Shadow Map Roadmap

> Status date: 2026-09-02
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
- a fully resident 128 x 128 page table;
- a four-cascade physical page layout;
- Meshlet hard-shadow raster that writes `R32_UInt` pages with `InterlockedMax`;
- hard-shadow resolve through the VSM page table;
- a whole-pool cache key and cache hit/refresh state;
- `VSMDebugPass` views for device depth, occupancy, and a depth heat map;
- a conventional CSM path in which Unity Renderer and Meshlet casters already share one cascade depth target.

The current VSM refresh remains Meshlet-only, requires GPUDriven readiness, and still renders conventional CSM every frame. The current whole-pool cache also has no safe invalidation source for arbitrary Unity Renderers.

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
bool VividTryResolveVSMPhysicalTexel(
    float4 positionCS,
    uint cascadeIndex,
    out uint2 physicalTexel);

void VividWriteVSMDepth(
    float4 positionCS,
    uint cascadeIndex);
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
Tags { "LightMode" = "ShadowCaster" "VividVSMCaster" = "True" }
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

### Objective

Replace the fully resident page table with demand-driven virtual pages under a fixed physical memory budget.

### Work

1. Mark requested virtual pages from visible shadow receivers.
2. Add page metadata for requested, allocated, dirty, cached, static, dynamic, and last-used state.
3. Add a bounded physical-page free list and allocator.
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
