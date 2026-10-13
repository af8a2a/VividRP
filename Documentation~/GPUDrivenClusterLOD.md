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

## Conservative runtime LOD selection

The compact parent bound uses the child's center and an upward-rounded radius
large enough to contain the original parent sphere. Its projected error must
therefore never be smaller than the original parent's error. Otherwise the
parent can request refinement while the child rejects that same refinement,
leaving missing triangles in the LOD cut.

`VividMeshLODError.hlsli` evaluates the local sphere using the full local-to-clip
matrix, including nonuniform scale and shear. The radius metric uses the nearest
clip W of the sphere with an outward FP32 allowance. An enclosing sphere has a
larger radius and a no-greater nearest W. Spheres touching or crossing W=0 return
the largest finite radius metric, requesting refinement for nonzero errors;
zero-error leaves remain selectable without `0 * infinity` producing NaN.
Orthographic views use the same calculation with constant W. This can select
finer LODs near large bounds; the error threshold, forced LOD behavior, buffer
layout and asset format are unchanged. Existing assets do not need a rebake.

The Sponza `Camera_1` roof regression came from projecting radius at the sphere's
center. Original parent nodes 2722/2723 measured about 1.05 times the threshold,
but recentered parent bounds for leaves 2727/2728 measured only 0.96/0.84 times
the threshold. Both sides were rejected. The spheres cross the eye plane; the
new metric consistently requests refinement. The missing outer roof strip was
verified restored in the live D3D12 GameView with automatic LOD, frustum, cone
and HZB culling enabled, at position `(6.181255, 18.638472, -10.503865)`, Euler
`(43.272297, 304.76556, 1.6177362)`, FOV 60 and clip planes 0.3/1000.

`Tools~/Validate-MeshletCulling.ps1` also compiles all six `MeshletListBuild`
kernels and runs `MeshLODSelectionProbe.hlsl` through the production selection
function on D3D12. Its 16,448 deterministic cases include the captured roof
bounds, perspective/orthographic views, recentered bfloat16 bounds/errors,
affine scale/shear/reflection, eye-plane crossings, zero-error leaves and forced
LOD controls. They passed on RTX 5070 Ti with no parent/child cut holes or metric
containment failures. Coarse and refined controls prevent always selecting the
finest level from passing. This does not measure frame-time performance.

## Existing assets

The `.vmeshletcollection` importer version is now 4. Unity regenerates imported
collections on the next applicable import. Existing serialized meshlet data
remains readable. Terrain collections baked as ordinary `.asset` files need an
explicit terrain rebake to use the new generation algorithm.

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

The blob format is now v5. Readers convert v1-v4 vertices on load, conservatively
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
