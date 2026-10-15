# GPUDriven cluster LOD generation

The meshlet collection builder uses the vendored meshoptimizer v1.1 DLL for
clustering, partitioning, simplification, and reclustering.
`PluginSource~/Build-GeometryPlugins.ps1`
builds and deploys only meshoptimizer.

## Algorithm and compatibility

The partitioning and shared-boundary strategy follows `src/meshopt_clusterlod.h`
in the local `E:/vk_lod_clusters` reference, based on meshoptimizer's
[clusterlod example](https://github.com/zeux/meshoptimizer/blob/73583c335e541c139821d0de2bf5f12960a04941/demo/clusterlod.h).

1. Build the finest meshlets with the existing vertex/triangle limits.
2. Generate a position-only remap so duplicated UV/normal seam vertices count
   as connected during `meshopt_partitionClusters`. Supply positions for spatial
   grouping of disconnected components. The existing target is four meshlets
   per group; the native API's returned partition count determines allocation.
3. Lock every position shared by multiple groups, propagating the lock to all
   original vertex IDs at that position.
4. Gather each group's triangles and compact vertices by original ID. This
   merges meshlet-local duplicates while retaining attribute seams. Simplify
   with `meshopt_simplifyWithAttributes` (no attribute weights, border locking)
   and the shared-position locks, then rebuild meshlets.
5. Retain the existing optional sloppy fallback and level reduction threshold.
   Both simplifiers receive boundary locks. Sloppy mode can merge coincident
   seam IDs and degrade attributes; terrain continues to disable it.
6. Convert relative simplification errors into object-space distances with
   `meshopt_simplifyScale`, then conservatively accumulate child errors and
   bounds. An empty simplification result retains its source geometry.

This adapts the reference to VividRP's existing level-based representation;
it does not adopt the reference's asynchronous iteration or independent terminal
groups. Import settings remain relative error tolerances. Vertex quantization
and its serialization changes are described below. Conservative runtime LOD
selection is described in the next section.

The builder also retains owning native allocations separately from bounded
meshlet views, releases retry partitions and list storage, and completes vertex
write jobs before unpinning managed output buffers.

## Shared group transitions and pixel budgets

Each simplification group produces one canonical sphere and one accumulated
linear object-space error. All resulting coarse clusters and their source
children reference the same group record, as own and parent data respectively.
CPU and GPU use the same canonical decoded Bounds/Error, without recentering or
independently rounding the parent. Source assets retain FP32 data; resident
radius/error storage uses upward-rounded FP16 with an FP32 overflow table.
This follows the group-data
contract in UE 5.7.4 `NaniteBuilder/Private/ClusterDAG.cpp` (`FClusterDAG::ReduceGroup`), while
keeping meshoptimizer as the builder.

The sequential CPU/GPU `VividMeshLODNodePacked` is **16 bytes**:

| Offset | Field |
| --- | --- |
| 0 | GroupIndex (uint) |
| 4 | ParentGroupIndex (uint; 0xffffffff for roots) |
| 8 | MeshletStartIndex (uint) |
| 12 | PackedMeshletCountLevel (uint) |

Assets contain **20-byte** `VividMeshLODGroup` records: Bounds (float4) at offset 0
and Error (float) at offset 16. `_MeshLODGroups` instead contains sequential
**16-byte** `VividMeshLODGroupPacked` records: three FP32 center coordinates at
offsets 0/4/8 and packed radius/error at offset 12. `_MeshLODGroupOverflow` has
8-byte FP32 radius/error pairs. A child's parent index and the corresponding
coarse nodes' own indices refer to the same canonical record.
`VividMeshLODGroupTable` interns source records when building an asset or
converting old metadata, by the exact bits of all five floats. One-ULP differences
and signed zeros are not silently merged. v7 loads preserve both arrays and
their indices directly, including unused or duplicate records, without a
48-byte intermediate array or a deduplication dictionary.

Scene assembly interns groups across assets by the exact bits of all five floats.
A reusable asset-local-to-scene index array remaps both own and parent references;
meshlet offsets are rebased as before. The root sentinel is preserved and source
arrays are not modified. Multiple instances of one asset reuse its scene range.
The scene-owned dictionary and remap storage retain capacity between rebuilds.
They are populated only on the initial static build or when the referenced asset
set or a referenced asset's content version changes. Instance/material-only
updates and changes to unrelated assets reuse the existing groups and node IDs.
After assembling all assets, `VividMeshLODGroupQuantizer` rounds scalars upward,
repairs every child-to-parent containment/error constraint, interns the final
decoded values and remaps node indices. This includes dependencies introduced
by cross-asset sharing. Its work storage also retains capacity. GPU upload copies
the prepared packed groups, overflow records and nodes directly, without encoding,
interning or index remapping during upload.

`VividMeshletCollectionAsset.MeshLODNodes` and `VividGPUDrivenSceneData.MeshLODNodes`
now expose indexed nodes; `MeshLODGroups` exposes the shared records. Use
`SetMeshData(levelCounts, nodes, groups, meshlets, vertices, indices)` to replace
an asset's data together, then `MarkChanged()` after editing. The old five-argument
`SetMeshData` overload accepts 48-byte `VividMeshLODNode` input and converts it
once for source compatibility. That struct otherwise serves old blob conversion;
assets and scenes never retain an expanded node array. The meshoptimizer builder
also emits shared records directly. The scene's `MeshLODGroups` is a 20-byte
per-group CPU cache decoded from the final packed records and overflow table.
CPU VSM hierarchy construction reads this cache; `PackedMeshLODGroups` and
`MeshLODGroupOverflow` are the directly uploaded arrays. No node expansion occurs.

Resident GPU LOD metadata uses `16 * nodeCount + 16 * groupCount + 8 * overflowCount`
bytes, plus one 8-byte dummy record when the overflow table is empty. Savings
depend on sharing and overflow frequency. CPU memory additionally retains the
decoded 20-byte group cache and reusable build storage; this change reduces GPU
payload, not CPU memory. The extra lookups preserve FP32 cut calculations. These
figures exclude other rendering resources; no frame-time improvement is claimed.

The shader selects a node when its parent needs refinement and the node itself
does not. Both sides evaluate identical group data with complementary `>` / `<=`
comparisons; equality selects the coarse side. Roots have no parent constraint,
and the finest retained level always remains drawable, including truncated
hierarchies and a zero pixel budget. Bounds contain child bounds and accumulated
errors increase toward coarser levels, preserving a complete cut across levels.

## Main-view hierarchy traversal and resident leaves

Main-view culling now uses three GPU stages:

1. `CSMainHierarchyEnqueue` rejects instances and appends instance/view tasks.
2. `CSMainHierarchyTraverse` traverses the cached binary hierarchy and emits
   disjoint candidate ranges of at most 32 LOD records through indirect dispatch.
   It rejects conservatively by transformed local AABB and the maximum projected
   parent error. Near-plane, nonfinite and equality ambiguity retain the range.
3. `MeshletListBuild.CS` applies the exact shared-group parent/own predicates and
   expands only selected records into meshlet requests. Meshlet frustum, cone and
   occlusion culling continue afterward.

This follows the separation between `ShouldVisitChildInternal` and
`SmallEnoughToDraw` / `NANITE_CLUSTER_FLAG_STREAMING_LEAF` in the local UE source
`Engine/Shaders/Private/Nanite/NaniteClusterCulling.usf`. Unlike Nanite's packed
hierarchy, each range here can contain several groups, so final selection still
checks its exact parent predicate; an interval result cannot replace that test.
Native `float16_t` box support tests are reused. Projected error and all shared
cut decisions remain FP32. The platform continues to assume reversed Z.

Main and VSM views share the existing 80-byte auxiliary hierarchy. Its final
word (`ErrorMagnitude.w`, byte 76, interpreted as uint bits) stores a resident
leaf mask in range leaves and an any-leaf flag in internal nodes. The existing
16-byte task's fourth uint carries the mask into final selection. No serialized
v7 changes, expanded LOD nodes, extra per-node GPU flags, or second hierarchy
buffer are needed. Queue storage is eight bytes per possible instance/view task
plus 16 bytes of indirect arguments and is reused at stable capacity.

Residency is derived per asset range and transition level from nonempty child
records referencing a coarse group's index. Every member of that group therefore
gets the same terminal state. A resident terminal group may exceed the pixel
budget, but still draws only when its parent refines into it. Forced deeper levels
also retain terminal groups. VSM uses the same mask and excludes these groups
from minimum-own-error range rejection. Geometry/content invalidation rebuilds
the masks; camera, instance-transform and material updates reuse the table.

The current asset system exposes complete resident refinements, not Nanite page
requests, priorities or asynchronous page installation. This handles fully absent
finer refinements and truncated hierarchies. A future geometry streamer must
install/evict a complete refinement atomically and invalidate the hierarchy;
partially publishing some children is not a supported residency contract.

Validation on 2026-10-15, Unity 7000.0.0a9 / D3D12 / RTX 5070 Ti:

- All instance, meshlet and selection kernels compiled with DXC, including native
  FP16 instructions in main hierarchy traversal. Runtime, Editor and Editor.Tests
  compiled against current Bee references; 40 standalone managed checks passed,
  including hierarchy stride/field offsets. Unity Test Framework was not run
  because the interactive Editor was open.
- The production staged GPU path matched a flat GPU selection reference in 603
  comparisons: all 399 poses of the roof route plus forced-depth, shadow-texel
  and synthetic absent-finest-refinement cases. No duplicate meshlets, overlapping
  jobs, residency-mask mismatches or capacity overruns occurred. Synthetic data
  exercised 1,323 terminal instance/node pairs above the globally finest level.
- These selection comparisons deliberately use nonrestrictive frustum planes to
  isolate LOD correctness. Along the route, jobs decreased from 296 to 224–260
  with identical final selection. This is not a new image comparison or a GPU
  frame-time result. Evidence is under `Temp~/main-lod-traversal/`.
- A further 399 comparisons enabled the actual roof-route frustum. All 6,222
  additional meshlet rejections were independently checked in FP64 against their
  indexed, quantized triangle vertices: each rejected meshlet lay entirely outside
  at least one clip plane. No additional LOD selections were introduced.
- Native D3D12 predicates passed 111,680 LOD cases (including projected range
  upper bounds and resident-leaf main/shadow/forced-depth selection), plus 41,024
  instance and 24,640 meshlet cases per precision mode. A live Editor frame used
  106 traversal tasks, 142 jobs, 1,017 candidates and 908 drawn meshlets.
- Repeated stable scene upload/hierarchy update allocated 0 managed bytes over
  128 calls and retained the hierarchy buffer. Camera transform and Play Mode were
  unchanged. No scene was saved; the Editor acquired a dirty-scene flag during
  validation, which was left intact. One CLI evaluation exceeded Pipeline's five-second response timeout;
  its completed result was recovered from disk. No shader or rendering error was
  reported by the final console inspection.

Like Nanite's view pixel scale / MaxPixelsPerEdge budget, the runtime compares
**linear projected error in pixels** against a budget, default **1 pixel**. The
new Volume field is `meshLODPixelError` (Inspector: Mesh LOD Pixel Error).
The previous heuristic threshold, commonly 50, has no view-independent numeric
conversion: old serialized overrides are deliberately not mapped to this field.
Set a new pixel override to retune quality. The existing C# system property and
shader uniform named `MeshLODErrorThreshold` retain their names but now use pixel
units; the old Volume API is an obsolete forwarding property.

`VividMeshLODError.hlsli` uses the full local-to-clip matrix, viewport dimensions,
and the shared sphere's nearest W. A bound on the projection derivative includes
the off-axis W contribution and supports nonuniform scale, shear, perspective,
and orthographic cameras. Error is no longer multiplied by sphere radius squared
or compared with instance-center distance squared. Eye-plane crossings request
refinement for nonzero errors; zero error remains zero. The implementation is a
conservative projection bound, not an exact port of Nanite's tangent-angle and
near-plane-clamped formula; large spheres crossing the eye plane can select
more detail. Critical cut math remains FP32, while existing strict float16_t
frustum culling is unchanged.

VSM clipmap mode compares linear object-space error times shadow texels per
object-space unit against the same budget. Its hierarchy interval rejection uses
the same scale as the leaf shader and preserves ambiguous boundary cases and
ranges containing the finest retained level. Forced-depth behavior is retained.

## Existing assets

The blob format remains **v7** and `.vmeshletcollection` importer version **6**;
this resident-only change requires no asset reimport or rebake. Vertices remain 20 bytes
and meshlets remain 32 bytes. The LZ4 payload stores level counts,
the shared group array, indexed nodes, meshlets, vertices and indices in that
order. Both own and parent group indices are validated before accepting data.

v6 already preserves exact group data and loads without rebaking or falling back
to the finest cut. Its nodes are interned once during loading; saving a
loaded collection serializes the shared format. Existing ordinary v6 `.asset`
collections therefore also receive the GPU storage reduction without a rebake.

v1-v3 retain full parent metadata and convert losslessly to the new node layout.
v4/v5 discarded the original parent centers and rounded parent errors, so an
exact transition cannot be recovered reliably. These assets load the complete
finest cut until reimported/rebaked; `RequiresLODRebuild` exposes this state. Their
original serialized blob is retained during serialization so loading does not
silently replace the old hierarchy with the fallback. Ordinary `.asset` terrain
collections require an explicit terrain rebake to regain automatic LOD. Source
FBX/glTF files are unchanged.

## LOD validation (2026-10-14)

- Runtime, Editor and Editor.Tests compile against Unity 7000.0.0a9 references.
  All 40 pure managed checks pass outside the Unity Test Framework. They cover
  v1-v7 serialization, indexed ownership, all field offsets/strides, every finite
  nonnegative half boundary and adjacent FP32 values, subnormal ceilings,
  overflow/invalid inputs, near-tangent containment repair, tiny displacements
  beside very large radii, unbounded repairs, final interning/remapping, repeated
  build stability, empty rebuilds and warmed zero-allocation quantization.
- DXC compiles all 6 MeshletListBuild, 8 GPUInstanceCulling and 12
  GPUMeshletCulling kernels. D3D12 execution on an RTX 5070 Ti passes **111,680**
  LOD cases with production C# encoded 16-byte nodes/groups and 8-byte overflow
  buffers. The corpus contains **6,613 overflow records** and covers every
  finite nonnegative FP16 code, adjacent FP32 values, large coordinates, affine
  transforms, eye-plane cases and the captured Camera_1 roof bounds.
  CPU/GPU decode is bit-identical, source bounds/errors never decrease, main,
  shadow and three-level cuts have zero holes/overlap, and an FP64 pixel-error
  oracle agrees. VSM spatial projections retain at least the original coverage;
  frustum and VSM hierarchy checks never newly reject a selected case.
  Separate meshlet (24,640 cases per mode) and instance-frustum (41,024 per mode)
  probes pass in both native16 and FP32 modes.
- Live Sponza readback contains 217 instances, 2,888 distinct LOD nodes and 2,014
  groups with **zero overflow records**. Node/group/overflow strides are 16/16/8.
  All CPU/GPU decoded bits match, centers and geometry offsets match the original
  FP32 capture, radii/errors never decrease, and all hierarchy containment checks
  pass. VSM intervals match the decoded cache; VSM spatial topology, roots and
  geometry-bound buffers remain unchanged.
  GPU node/group payload decreases from **86,488 to 78,432 bytes**; including the
  empty side table's dummy record gives **78,440 bytes**, a net **8,048-byte
  (9.31%)** reduction in this metadata. Group records alone are 20% smaller.
  These figures exclude vertices, meshlets, CPU caches, reserved capacity and
  other scene resources. Final readback reports 1,088 visible meshlets and 16
  nonzero indirect argument records at the current view. Live Editor compilation
  and console errors are zero; existing warnings remain.
- Sixteen warmed full static uploads allocate zero managed bytes on the calling
  thread and reuse node/group/overflow buffers. Sixty-four warmed stable scene
  builds allocate zero managed bytes and perform zero static rebuilds. Both the
  source interning dictionary and packed-group list remain untouched. An
  unrelated asset revision leaves them untouched; a referenced asset revision
  rebuilds them once, reproduces identical data, and the next build reuses them.
  Only in-memory revisions were changed; no asset content was edited or saved.
- Evidence is in `Temp~/lod-group16/`: `managed-validation.log`,
  `gpu-validation.log`, `final-lod-check.log`, `instance-validation.log`,
  `readback.json` and `cache.json`. Reproduce the managed/D3D12 checks with
  `Tools~/Validate-MeshletQuantization.ps1`, `Tools~/Validate-MeshletCulling.ps1`
  and `Tools~/Validate-InstanceFrustumCulling.ps1`. The fixed-camera visual/GPU
  follow-up is below. The earlier roof capture at
  `Temp~/lod-pixel-budget/roof.png` predates this resident codec change.

Unity Test Framework tests were not run in the active interactive Editor.
Run `VividMeshletCollectionBuilderTests`, `VividGPUDrivenSceneDataBuilderTests`,
`VirtualShadowMapGeometryBoundsTests`, `VirtualShadowMapPageHierarchyTests`, and
GPU buffer/serializer fixtures manually for the full Editor integration suite.
These include cross-asset group remapping, root-sentinel preservation and
same-count group edits refreshing VSM error intervals.

### Roof camera A/B follow-up (2026-10-14)

Report: `Temp~/lod-group16-ab/report/report.html`; reproducible analysis/plots:
`Temp~/lod-group16-ab/analyze.py`, followed by `render_report.py` in that directory.
Raw GPU readbacks, receiver depth/normal/shadow, screenshots, timing CSVs, settings
snapshots and source hashes remain beside the report.

- Fixed Camera_1 at `(6.181255, 18.638472, -10.503865)`, Euler
  `(43.272297, 304.76556, 1.6177362)`, 1920x1080, vertical FOV 60, near/far
  0.3/1000. GPUDriven, occlusion and automatic 1-pixel LOD remain enabled.
  VSM/SMRT settings are unchanged. Game time is paused during the controlled
  Play Mode comparison; the original camera/time/Play Mode state is restored.
- A uses the original 20-byte FP32 groups without resident quantization or
  hierarchy repair. B uses the production 16-byte path. Both GPU layouts and
  CPU VSM caches change together; shader variants select the layout at compile
  time. The baseline group bit-pattern set matches the pre-quantization capture.
  All temporary source instrumentation is restored byte-for-byte and the probe
  shader is removed through AssetDatabase.
- Valid depth is identical at every one of 2,073,600 pixels. The main
  view's 4,548 instance/node GPU decisions and actual draw lists match: 1,284
  candidates, 912 visible meshlets, no recovered draws or duplicates. Across
  the main view and 17 shadow views, each captured state checks 96,318 complete
  leaf-to-root paths; six captures have zero holes, overlaps or unresolved
  parent references. Shadow view 3 replaces two coarse nodes with four finer
  nodes; the other views agree. This is conservative refinement, not a broken cut.
- Final RGB is not bit-identical: A/B MAE is 0.07717/255 versus 0.05736/255 and
  0.05128/255 for same-version repeats. Shadow MAE is 0.001598 on a 0-1 scale,
  within the repeated-frame differences 0.001799 and 0.001611. SMRT frame noise
  and shadow LOD changes affect lighting; not every RGB difference is attributed
  to noise. Depth and draw/cut evidence exclude new missing or duplicate
  geometry in this view.
- Six alternating pairs use 192 warm-up camera frames and 384 valid samples per
  window (2,304 per version). Readbacks and diagnostic dispatches run outside
  timing windows. The GPU `VividLODAB.CameraGPU` range averages **1.571474 ms A**
  versus **1.568078 ms B**. Paired B-A is **-3.396 us** (-0.216%), with a 95% t interval of
  **[-13.519, +6.728] us** across six window pairs. No stable speedup or regression
  is established. Clocks are not locked; telemetry and per-window dispersion are
  retained. The temporary ProfilingSampler/ProfilingScope covers pre-graph
  GPUDriven culling, the render graph, post-graph commands and submitted gizmos,
  excluding Editor UI; nested ranges are never added together. Earlier graph-only
  measurements (1.520192 ms A / 1.518290 ms B) are retained separately; they omit
  the pre-graph main-view LOD build and are not the primary cost metric.
- FrameTimingManager/FrameTime.GPU reports roughly 0.012 ms here, smaller than
  individual passes, so it is excluded as a camera-cost metric. The first pilot
  also lost Play Mode during compilation and produced no valid samples; it is
  excluded. A plain-string marker pilot lacked SampleGPU registration and is
  likewise excluded. Camera pose, frame extent, sample counts and compilation state are
  checked before accepting the final windows. This validates the specified fixed
  view, not arbitrary camera motion or a whole-application performance claim.
- The packed normal readback is excluded: Unity rejects the diagnostic tool's
  A2B10G10R10_UNormPack32-to-RGBAFloat conversion (formats 75 -> 52), returning
  zeros without setting the readback error flag. Zero-filled normal equality is
  not validation. Depth uses an explicit compute copy, contains 1,216,311 nonzero
  finite pixels, and remains identical after restoring the production source.

### Moving roof camera A/B follow-up (2026-10-15)

Report and synchronized video: `Temp~/lod-group16-dynamic/report/report.html`
and `roof-route-ab.mp4` in that directory. `route.json` defines 399 rendered poses:
distance 6 to 100 and back along the original roof view axis, two oscillation
segments around actual roof LOD thresholds (42.295 and 98.353), four stationary
holds, and continuous connecting segments. FOV, 1920x1080 resolution, 1-pixel LOD
budget, reversed-Z, GPUDriven/HZB and VSM/SMRT settings are preserved.

- Four complete A/B/B/A captures contain 1,596 frames. Each frame checks the
  main view and 17 shadow views, totaling **153,723,528 leaf-to-root path checks**.
  Holes, overlaps, duplicate main-view draws and draws from unselected nodes are
  all zero. Every run records 858 roof-node selection changes (including both
  sides of transitions, not 858 distinct thresholds).
- Both A/B comparisons differ in node selection at 38 poses and in depth at
  12 poses. The largest depth difference covers **2,498 pixels (0.1205%)**;
  lost and gained coverage are both zero. Differences occur only when the LOD
  cut changes; equal cuts have equal draw sets. The quantized version retains
  finer detail slightly longer, as confirmed by GPU error/budget measurements.
  These expected threshold shifts are not classified as false culling.
- A/A and B/B depth are identical at every pose. All four hold segments keep
  depth and LOD selection unchanged; the outbound and return paths make identical
  LOD decisions at identical positions. No new abnormal geometry flicker is
  observed on this route. Final RGB is not identical: mean A/B differences are
  0.03667 and 0.03669 /255, versus 0.07052 and 0.03598 /255 for same-version
  controls. Frame-dependent lighting noise remains; the result is not a claim
  of flicker-free rendering on every camera route.
- Timing excludes screenshots, readbacks and diagnostic dispatches. Each window
  has 192 stationary warm-up frames, one 399-frame moving warm-up cycle, and two
  measured cycles. Six paired windows provide **4,788 valid samples per mode**.
  The full camera GPU range, including pre-graph GPUDriven work, averages
  **2.151886 ms A / 2.170838 ms B**. Paired B-A is **+18.952 us (+0.881%)**;
  its 95% t interval is **[-15.884, +53.787] us**. Stable speedup or regression
  is not established. Clocks are unlocked. Recorder observations can lag the
  current pose, so timing is compared over complete route cycles, not attributed
  to exact threshold frames.
- The original sixth pair was interrupted by an Editor restart: TA6 is unpaired
  and TB6 incomplete, so both are excluded. TA7/TB7 replace that pair after
  verifying the backend, dimensions and VSM settings. The report retains separate
  session means and all excluded data. The comparison video plays at 30 fps;
  this playback rate is not a measured rendering rate.
- All six instrumented production files are restored to their pre-experiment
  hashes. The temporary Editor probe is removed through AssetDatabase and its
  source is archived beside the data. This cleanup also removes the experimental
  files included in the intervening commit. The final live check confirms the
  production 16-byte group layout, no temporary switch, the original camera and
  time settings, and an unmodified scene. The Editor remains stopped, preserving
  its state after the restart. No Unity Test Framework run was started.

## Shared-group quantization design (implemented, 2026-10-14)

The selected 16-byte format is now the resident format. The comparison below
records the preceding investigation; source assets remain 20-byte FP32 groups.

The local UE **5.7.4** source provides three relevant contracts:

- `E:/UnrealEngine/Engine/Source/Developer/NaniteBuilder/Private/ClusterDAG.cpp:1072`
  assigns one group's LOD bounds/error to all dependent coarse clusters.
- `Encode/NaniteEncode.cpp:153` retains the cluster's FP32 LOD sphere, but packs
  LOD error and edge length as two FP16 values. `NaniteDataDecode.ush:519` loads
  the sphere with `asfloat` and decodes the scalars with `f16tof32`.
- `Encode/NaniteEncodeHierarchy.cpp:488` and `:506` retain FP32 hierarchy LOD
  spheres, round the minimum error downward, and pack it with the parent error.
  Ordinary `FFloat16::Set` uses round-to-nearest-even; Nanite does **not** round
  every error upward. Its interval lower bound serves a different purpose from
  VividRP's shared transition error.

Thus compressing the LOD sphere is a VividRP extension, not a direct Nanite port.
Epic's [vertex precision documentation](https://dev.epicgames.com/documentation/en-us/unreal-engine/nanite-technical-details)
describes object-space, power-of-two **vertex position** quantization; that should
not be mistaken for a compressed resident LOD sphere format.

### Candidate formats and measured input distribution

The offline experiment reads the preceding Sponza GPU capture in
`Temp~/lod-scene-dedup/before-MeshLODGroupsBuffer.bin` and its node buffer,
reproduces exact cross-asset interning (2,022 -> 2,014 groups), and examines
2,025 distinct own-to-parent group edges across 2,888 nodes. This is captured
scene data, not a new live render or GPU codec test. Reproduce with
`python Temp~/lod-group-quantization/analyze.py`; results are in `result.json`
beside that script.

| Format | Group payload | Node + group bytes in this capture | Reduction from FP32 metadata |
| --- | ---: | ---: | ---: |
| Previous FP32 center/radius/error | 20 B | 86,488 | -- |
| FP32 sphere + separate packed FP16 error stream | 18 B average | 82,460 | 4.66% |
| **FP32 center + upward FP16 radius/error** | **16 B** | **78,432** | **9.31%** |
| FP16 center/radius/error + reserved bits | 12 B | 70,376 | 18.63% |

The 18-byte option needs a separate error stream (two FP16 errors per uint);
putting one packed error uint after a float4 still occupies 20 bytes. The
16/12-byte figures exclude any overflow table or quantization-domain metadata
and conservatively retain all 2,014 group IDs, without claiming extra merging
from quantization. These are payload savings, not whole-scene VRAM or timing.

In this capture, maximum absolute center coordinate is 16.20624 object units,
radii range from 0.01621 to 199.10589, and positive errors range from 0.00022149
to 0.660371. Both half-based candidates fit without overflow records. With
upward scalar rounding, the 16-byte candidate increases radius by at most
**0.09357%** and error by at most **0.09127%**. It preserves centers exactly.
The 12-byte candidate rounds centers to nearest FP16 and compensates radius
for their displacement: maximum center shift is **0.005044 object units** and
maximum radius increase is **0.73920%**. Pixel-error inflation is not bounded
by those percentages near the eye plane; larger bounds can request much more
detail there. No GPU time or visual-quality improvement is established.

### Resident implementation: 16 bytes

Keep three FP32 center coordinates at offsets 0/4/8 and a `uint RadiusError` at
offset 12. C# declares the coordinates as scalar fields because CoreRP's HLSL
generator promotes a `float3` field to `float4`, even with exact packing.
Pack nonnegative radius/error in its low/high 16 bits with explicit upward
rounding. Decode to **FP32** before projection, shadow error evaluation and
comparisons. Existing strict `float16_t` frustum acceleration is independent
of this storage encoding; do not move critical LOD comparisons into FP16.

For a finite nonnegative scalar, encode the nearest half, decode it, and advance
to the next half code if the decoded value is smaller than the source. Preserve
exact zero; never flush a positive error to zero. Values above 65,504 must not
be clamped down. An escape uses bit 31 of `RadiusError` (the unused
sign bit of a valid nonnegative error) and the remaining bits as an index into
an FP32 `float2(radius,error)` table. Either scalar exceeding 65,504 creates an
escape; any representable partner scalar still uses its upward half ceiling.
Positive tiny values round up to the minimum half subnormal; they do not escape.
Storage is **16*G + 8*F** group bytes for G groups and F escapes (plus an 8-byte
dummy when F is zero); it only beats 20*G when overflow is sufficiently rare.
Invalid spatial bounds use an unbounded radius and maximum finite error; a nonfinite
center is replaced with zero only in this unbounded fallback. Invalid errors
use the maximum finite FP32 error. A radius that exceeds FP32 during hierarchy
repair remains infinite in the side table. VSM projection retains the full
viewport for nonfinite projected bounds; it never converts them to texel indices.

The 12-byte candidate is a later experiment. Absolute half centers lose too much
precision for small bounds far from the object origin and overflow on large
coordinates. A shared origin plus power-of-two grid could improve it, but adds
a decode-domain ID/header and reduces cross-asset sharing when domains differ.
Identical packed words under different domains are not identical groups.

### Correctness requirements

1. **One canonical decoded record per shared transition.** Coarse own and fine
   parent references must resolve to identical decoded bits, including escapes.
   Preserve the root index sentinel and complementary `>` / `<=` decisions,
   zero-error behavior, forced depth and the finest retained level.
2. **Outward spatial rounding.** If the center changes from c to c', require
   `r' >= r + length(c' - c)`, including encode/decode arithmetic error. The
   16-byte candidate avoids center error, but still needs `r' >= r`.
3. **Hierarchy closure after quantization.** For every child-to-parent group
   edge, require `rParent' >= distance(cParent', cChild') + rChild'` and
   `errorParent' >= errorChild'`. Propagate increases toward coarser groups,
   round outward again, and include every dependency of a shared group. Apply
   the same rule when an escape changes the precision of either endpoint.
   Bounds/error inflation is based on source asset values on each static rebuild.
   Repair uses FP64 distance plus addition margins, upward FP32 rounding and a
   second half ceiling. Identical centers need no margin. A fixed-point pass
   includes cross-asset aliases and rejects nonconvergent growing cycles.
4. **Shared indices alone are insufficient for a multilevel cut.** They ensure
   matching decisions at one transition, while nested bounds and monotone error
   prevent a coarse ancestor and a finer descendant being selected together.
   Independent outward rounding can break nesting: a parent at x=0 with r=2
   contains a child at x=0.9996000528 with r=1.0003999472. Rounding each radius
   upward to half leaves the parent at 2 but moves the child radius to
   1.0009765625, which no longer fits. The captured scene has no such failures
   for either candidate, but this explicit counterexample requires a regression.
5. **VSM uses decoded values.** Build its `min own error` / `max parent error`
   intervals from the same packed scene groups consumed by the GPU. Using the
   original FP32 parent error can reject descendants that the quantized GPU
   error would select. If hierarchy intervals are later compressed separately,
   round interval minima down and maxima up. Keep existing geometric bounds
   derived from actual vertices; sphere fallbacks use the decoded outer bounds.
6. **Only static scene changes perform encoding and interning.** Keep v7 asset
   group data at source FP32 precision. During the cached scene build,
   quantize each source group once, repair hierarchy constraints, then intern
   final canonical decoded/encoded values and remap node indices. Resolve escape
   indices after canonicalization; do not treat different escape indices as
   different values. The CPU scene table caches the decoder output, and VSM
   reads it without expanding nodes to 48 bytes. GPU upload remains a direct
   copy of prepared buffers. No reimport or disk format bump is necessary.

The original offline FP64 experiment established the nesting counterexample.
The production C# encoder and D3D12 decoder now exercise these requirements in
the validation above, including escapes and the three-level near-tangent chain.
A small increase in refinement is expected; bit-identical LOD selections to
the original FP32 inputs are not the acceptance criterion. The fixed roof view
is covered above; other camera routes and streaming workloads remain unmeasured.

## Resident vertex quantization

Following `scene_gltf.cpp` and `shaders/attribute_encoding.h` in the local
`E:/vk_lod_clusters` reference, positions and UVs retain the sign, exponent and
16 mantissa bits. The 25-bit values are packed directly into the resident vertex
buffer. Normals use precise octahedral 11+11-bit encoding; tangents use a 9-bit
angle around the decoded normal and one handedness bit. This projects valid
tangents into the normal plane. A standalone tangent without a valid normal
retains the legacy oct15 encoding. Missing directions retain explicit flags.

The resulting `VividMeshletVertex` stride is **20 bytes**, down from 32 bytes
(37.5% less vertex-buffer storage; not a whole-scene VRAM or frame-time claim).
CPU field offsets and HLSL definitions agree on five sequential uints:

| Byte offset | Field | Bit payload, low to high |
| --- | --- | --- |
| 0 | PositionXY | X:25, Y low:7 |
| 4 | PositionYZ | Y high:18, Z low:14 |
| 8 | PositionZUV | Z high:11, U low:21 |
| 12 | UVAndFlags | U high:4, V:25, normal valid:1, tangent valid:1, reserved:1 |
| 16 | TangentFrame | normal:22, tangent angle and sign:10 |

Quantization rounds values, flushes denormals/signed zero and preserves finite
extremes, infinities and NaNs. Precision scales with object-space coordinate
magnitude. Positions are quantized **before** meshoptimizer clustering,
simplification and bounds/cone calculation, with an identical grid for all
clusters and LODs. Renderer and terrain bounds also include quantized geometry.
The binary payload retains LZ4 compression; the reference's per-cluster arithmetic
bitstream is not needed for this fixed-width resident format.

Vertex quantization was introduced in v5 and is retained in v7. Readers convert
v1-v4 vertices on load, conservatively
expand legacy bounds/errors and disable stale cones when positions moved.
Reimporting models or rebaking terrain produces bounds and cones directly from
quantized geometry. Source FBX/glTF files are not modified. Generated assets
continue to use the `GPUDrivenGenerated/MeshletAsset` generation rule.

The shared HLSL decoder serves visibility, shadow, debug, native mesh shader and
GBuffer resolve paths. Regenerate its struct declarations with CoreRP's C# to HLSL
generator whenever changing the C# layout; do not edit the generated include.

## Validation

Run the native integration probe from the package directory:

```powershell
python Tools~/Validate-ClusterLOD.py
```

It loads the shipped DLL and checks partition determinism, disconnected seam
IDs, shared boundary positions in both simplification modes, all-locked geometry,
uniform-scale error conversion, empty-result preservation, and iterative group
simplification. It does not execute the C# builder or validate GPU rendering.

The focused Unity tests are in `VividMeshletCollectionBuilderTests`, including
determinism, hierarchy metadata/leaf coverage, locked vertices, error scaling,
empty simplification, importer behavior, and binary serialization. Run these
manually when an interactive Editor is open; automated Unity Test Framework
runs are only allowed when the Editor is closed.

For quantization, serialization compatibility, field offsets and stable-call
allocation checks, using the containing project's imported Bee references:

```powershell
./Tools~/Validate-MeshletQuantization.ps1
# Optional actual D3D12 execution; requires DXC, clang-cl and the Windows SDK:
./Tools~/Validate-MeshletQuantization.ps1 -Gpu
```

This compiles Runtime, Editor and Editor.Tests, invokes only pure managed test
methods outside Unity Test Framework, regenerates the GPU struct include through
CoreRP, and exports production C# vectors. `-Gpu` compiles the shared decoder and
compares 4096 decoded vertices against C# on the GPU (including missing attributes
and normals near the tangent-basis singularity). It does not import assets or
run the Unity builder integration test. Outputs are under
`Temp~/meshlet-quantization` by default.

For visual acceptance, regenerate a detailed mesh and rebake terrain, then
move the camera across LOD transitions and inspect silhouette, seams, cluster
debug views, and visible meshlet counts. Static C# compilation and the native
probe do not establish this runtime result.
