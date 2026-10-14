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
linear object-space error. All resulting coarse clusters copy these exact values
into their own Bounds/Error; all source children copy the same values into their
ParentBounds/ParentError in the CPU authoring view. The GPU stores each distinct
pair once as FP32, without recentering or independently rounding the parent.
This follows the group-data
contract in UE 5.7.4 `NaniteBuilder/Private/ClusterDAG.cpp` (`FClusterDAG::ReduceGroup`), while
keeping meshoptimizer as the builder.

The sequential GPU `VividMeshLODNodePacked` is **16 bytes**:

| Offset | Field |
| --- | --- |
| 0 | GroupIndex (uint) |
| 4 | ParentGroupIndex (uint; 0xffffffff for roots) |
| 8 | MeshletStartIndex (uint) |
| 12 | PackedMeshletCountLevel (uint) |

`_MeshLODGroups` contains sequential **20-byte** `VividMeshLODGroup` records:
Bounds (float4) at offset 0 and Error (float) at offset 16. A child's parent index
and the corresponding coarse nodes' own indices refer to the same record.
`VividMeshLODGroupTable` deduplicates by the exact bits of all five floats, so
one-ULP differences and signed zeros are not silently merged. Scene uploads build
the table after rebasing all assets' meshlet offsets; asset-local group indices
never leak into the scene. Identical object-space data can also be shared across
assets without sharing their geometry offsets or instance transforms.

The existing 48-byte `VividMeshLODNode` remains the CPU authoring/API view for
meshoptimizer generation, scene assembly and CPU VSM hierarchy construction.
It is expanded when loading a blob and is no longer uploaded to the GPU.
Serialization and GPU uploads use the same table builder. The buffer set owns
and reuses its lists/dictionary, binds both buffers to the production paths,
and releases both GPU buffers on disposal.

LOD metadata uses `16 * nodeCount + 20 * groupCount` bytes instead of
`48 * nodeCount`; savings depend on the actual sharing ratio. The extra group
lookups preserve the existing FP32 cut calculation. This is a GPU metadata
storage change, not a CPU memory or frame-time improvement claim.

The shader selects a node when its parent needs refinement and the node itself
does not. Both sides evaluate identical group data with complementary `>` / `<=`
comparisons; equality selects the coarse side. Roots have no parent constraint,
and the finest retained level always remains drawable, including truncated
hierarchies and a zero pixel budget. Bounds contain child bounds and accumulated
errors increase toward coarser levels, preserving a complete cut across levels.

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

The blob format is now **v7** and `.vmeshletcollection` importer version is **6**.
Unity rebuilds imported collections from their existing source settings. Vertices
remain 20 bytes and meshlets remain 32 bytes. The LZ4 payload stores level counts,
the shared group array, indexed nodes, meshlets, vertices and indices in that
order. Both own and parent group indices are checked before expanding nodes.

v6 already preserves exact group data and loads without rebaking or falling back
to the finest cut. Its nodes are interned on GPU upload just like v7; saving a
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
  All 34 pure managed checks pass, covering v1-v7 decoding, exact shared-data round
  trips, invalid group indices, table rebuild/empty state, distinct float bits,
  allocation-free warmed table builds, v4/v5 finest-cut migration, all node/group
  offsets and strides, and vertex quantization.
- DXC compiles all 6 MeshletListBuild, 8 GPUInstanceCulling and 12
  GPUMeshletCulling kernels. The production selection and VSM hierarchy predicate
  run on D3D12 (RTX 5070 Ti) in `Tools~/MeshLODSelectionProbe.hlsl`.
- 16,448 cases pass with zero holes/overlap for main-view, shadow and three-level
  cuts, including exact equality +/- one float ULP, zero error/budget, viewport,
  FOV, instance bias, affine transforms and the captured Camera_1 roof bounds.
  An FP64 oracle checks projected error. VSM branches never reject a selected
  contained node or the truncated finest level. The probe loads the production
  16-byte nodes and 20-byte groups from actual D3D12 structured buffers, with
  different offsets per case. All 1,052,672 result bytes match the previous
  expanded-node implementation exactly. Existing 24,640 meshlet and
  41,024 instance-frustum cases also pass in native16 and FP32 modes.
- The running Sponza scene has 217 instances, 2,888 distinct LOD nodes and 2,014
  shared groups. GPU readback matches all CPU geometry offsets and all own/parent
  float bits (2,468 non-root nodes), with no invalid indices or duplicate groups.
  Both metadata buffers total **86,488 bytes**, down from **138,624 bytes**:
  **52,136 bytes / 37.61% saved**. This excludes vertices, meshlets and other scene
  resources. Sixteen full static uploads after warm-up allocate zero managed
  bytes on the calling thread and reuse both GPU buffers. Readback reports 994
  visible meshlets and 16 nonzero indirect argument records at the current view.
  Auto LOD uses a 1-pixel budget. Live Editor compilation and console errors are
  zero; production compute shaders have no errors (existing warnings remain).
- Before extracting the shared table, the original roof camera was captured at
  `(6.181255, 18.638472, -10.503865)`,
  Euler `(43.272297, 304.76556, 1.6177362)`, with GPUDriven/occlusion enabled and
  automatic LOD. The previously missing outer roof strip is present in the
  captured scene output. The user's camera pose was then restored. Capture:
  `Temp~/lod-pixel-budget/roof.png`. This is a geometry check, not material or
  frame-time validation.

Unity Test Framework tests were not run in the active interactive Editor.
Run `VividMeshletCollectionBuilderTests`, `GPUDrivenSettingsVolumeTests`, and
GPU buffer/serializer fixtures manually for the full Editor integration suite.

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
