# Light Culling Optimization Roadmap

VividRP maintains two clustered light culling implementations:

| System | Files | Status |
|---|---|---|
| **VividRP Native** | `Shaders/Material/ClusteredLightCull.compute` | Custom 5-kernel pipeline with big tile coarse cull + per-cluster fine cull |
| **HDRP-derived** | `Shaders/Core/Private/Lighting/lightlistbuild-{clearatomic,bigtile,clustered}.compute` | Ported from HDRP with OBB convex hull, multi-pass cull, adaptive log depth |

---

## HDRP-derived (`lightlistbuild-*.compute`)

### High Priority

- [x] **Parallel compaction after spherical intersection tests (clustered)**
  - Current (`lightlistbuild-clustered.compute:555-564`): Thread 0 serially compacts `coarseList` after sphere test marks rejected entries as `UINT_MAX`. NR_THREADS-1 threads idle.
  - Target: `WaveActiveBallot` + `WavePrefixCountBits` for parallel stream compaction, or groupshared atomic double-buffer.
  - Impact: Eliminates thread-0 bottleneck for up to 128 coarse entries.
  - Done: Implemented via `WavePrefixCountBits` / `WaveActiveCountBits` with cross-wave prefix sum through `ldsTilePassList` scratch and in-place compacted write to `coarseList`. Requires `#pragma use_dxc`.

- [x] **Group-level prefix sum for global allocation (clustered)**
  - Current (`lightlistbuild-clustered.compute:453`): Each thread (=each cluster) does `InterlockedAdd(g_LayeredSingleIdxBuffer[0], iSpaceAvail, start)`.
  - Target: `groupshared` prefix sum across nrClusters threads, single group-level `InterlockedAdd`, local offset distribution.
  - Impact: Global atomic count reduced from O(nrClusters x numTiles) to O(numTiles).
  - Done: Implemented via `WavePrefixSum` / `WaveActiveSum` with cross-wave scan through `ldsTilePassList` scratch and single `InterlockedAdd(g_LayeredSingleIdxBuffer[0], acc, groupAllocationBase)` per group.

- [x] **Pre-compute cluster corner vertices for `CheckIntersection`**
  - Current (`lightlistbuild-clustered.compute:193-215`): Each `CheckIntersection` call recomputes 8 corner vertices via `GetViewPosFromLinDepth` (involves matrix multiply). 6 planes x 8 corners x numLights = massive redundancy.
  - Target: Compute the 8 corners once per cluster (per thread) before the light loop, store in registers.
  - Impact: Eliminates O(48 x numCoarseLights) matrix multiplications per cluster.
  - Done: Pre-compute `float4 clusterVerts[8]` (with w=1.0) from `ClusterIdxToZ` + `GetViewPosFromLinDepth` once before the light loop. `CheckIntersection` signature changed to `(int l, int k, float4 clusterVerts[8])`, inner loop reduced to pure `dot(plane, clusterVerts[vi])`.

### Medium Priority

- [x] **ExactEdgeTests: wave intrinsics instead of per-light barriers**
  - Current (`lightlistbuild-clustered.compute:618-688` / `lightlistbuild-bigtile.compute:341-408`): Outer loop iterates lights serially, 2x `GroupMemoryBarrierWithGroupSync` per light. 100 lights = 200 barriers.
  - Target: Use `WaveActiveAnyTrue(bFoundSepPlane)` to replace `InterlockedOr(ldsIsLightInvisible, 1)` + barrier pair. Batch multiple lights where possible.
  - Impact: Dramatically reduces synchronization overhead for edge tests.
  - Done (clustered): Replaced `InterlockedOr(ldsIsLightInvisible, 1)` + clear/barrier per light with `WaveActiveAnyTrue(threadFoundSep)` + cross-wave reduce via `ldsTilePassList` scratch. Eliminates all per-edge-pair atomics; single-wave groups need 0 barriers per light, multi-wave groups need 2 (same count but no atomic contention). bigtile variant not yet updated.

- [ ] **HiZ-based tile max depth (replace per-pixel depth sampling)**
  - Current (`lightlistbuild-clustered.compute:282-321`, `ENABLE_DEPTH_TEXTURE_BACKPLANE`): 32x32=1024 depth fetches per tile to find max depth for adaptive log base.
  - Target: Read from hierarchical-Z mip (mip5 = 1 texel for 32x32, or 4 texels from mip4). Or output per-tile max depth from a prior pass.
  - Impact: 1024 -> 1-4 texture fetches per tile.

- [ ] **Z-binning pass between big tile and clustered**
  - Current: `clusterIdxs` provides per-light depth range (min/max cluster index), but the clustered kernel still iterates all coarse lights per cluster.
  - Target: After big-tile, sort lights by view-space depth per tile, build Z-bin ranges. Clustered kernel uses binary search to iterate only the relevant depth window.
  - Impact: Major iteration reduction for high light density.

- [ ] **Replace bitonic sort with parallel compaction (big tile)**
  - Current (`lightlistbuild-bigtile.compute:181` / `SortingComputeUtils.hlsl`): SORTLIST macro runs bitonic sort on `MAX_NR_BIG_TILE_LIGHTS_PLUS_ONE=512` capacity. That is `log2(512)*(log2(512)+1)/2 = 45` barrier rounds regardless of actual light count.
  - Target: If strict ordering is not required (often true), replace with parallel compaction. If ordering is needed, use radix sort or leverage wave-level shuffle for small lists.
  - Impact: Eliminates 45 GroupMemoryBarrier rounds for big tile.

### Low Priority

- [ ] **`SFiniteLightBound` float4 alignment**
  - Current (`LightLoop.cs.hlsl:97-106`): Uses `float3` members (12-byte aligned), may cause cross-cache-line reads on some GPUs. `LightVolumeData` already uses `float3+uint` interleave (16-byte).
  - Target: Pack `scaleXY` and `radius` into `w` components of existing `float4` vectors.
  - Impact: Better cache line utilization.

- [ ] **Merge ClearAtomic dispatch**
  - Same as VividRP native: single `[1,1,1]` dispatch for zeroing one uint.
  - Target: Fold into clustered kernel init or use CPU-side clear.
  - Impact: -1 dispatch.

- [ ] **Volumetric big tile parallel write**
  - Current (`lightlistbuild-bigtile.compute:237-273`): `GENERATE_VOLUMETRIC_BIGTILE` path uses thread 0 serial loop for compaction + 16-bit packing.
  - Target: Parallel compaction of volumetric-affecting lights into groupshared, then parallel packed write.
  - Impact: Only matters when volumetric fog is enabled.

- [ ] **Increase convex hull plane batch size**
  - Current (`lightlistbuild-clustered.compute:464-468`): Processes 4 lights per batch, loads 6 planes x 4 lights = 24 `float4` into `groupshared lightPlanes`. Only 24 of NR_THREADS threads participate in fetch.
  - Target: Increase to 6 or 8 lights per batch (36 or 48 groupshared float4). Better utilization of threads during fetch phase and fewer outer loop iterations.
  - Impact: Modest; reduces barrier count in the main light loop.

---

## Cross-cutting / Shared Concerns

- [ ] **Wave intrinsics availability audit**
  - Many optimizations above rely on `WaveActiveBallot`, `WavePrefixCountBits`, `WaveActiveAnyTrue`. Verify availability on all target platforms (`d3d11`, `vulkan`, `metal`, `playstation`, `xboxone`, `xboxseries`, `switch`, `switch2`). Provide scalar fallbacks where needed.

- [ ] **Unified light culling strategy decision**
  - VividRP currently maintains two independent culling systems. Long-term, decide whether to converge on one implementation (likely the HDRP-derived one, with VividRP-native optimizations folded in) or keep both for different use cases (e.g., forward vs deferred).

- [ ] **Profiling harness**
  - Before implementing optimizations, establish GPU timing markers around each kernel dispatch (using `CommandBuffer.BeginSample` / `EndSample` or RenderDoc / Nsight captures) to measure baseline and validate improvements. Track: dispatch count, total light cull time, per-kernel time, global atomic contention, occupancy.
