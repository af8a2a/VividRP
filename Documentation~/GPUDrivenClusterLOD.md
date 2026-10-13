# GPUDriven cluster LOD generation

The meshlet collection builder uses the vendored meshoptimizer v1.1 DLL for
clustering, partitioning, simplification, and reclustering. METIS is no longer
called by GPUDriven. Its legacy public wrapper is obsolete; its source, binary,
and license notices remain for compatibility. `PluginSource~/Build-GeometryPlugins.ps1`
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
groups. Import settings remain relative error tolerances. GPU-shared layouts,
packed metadata, binary serialization, shaders, and LOD selection stay unchanged.

The builder also retains owning native allocations separately from bounded
meshlet views, releases retry partitions and list storage, and completes vertex
write jobs before unpinning managed output buffers.

## Existing assets

The `.vmeshletcollection` importer version is now 3. Unity regenerates imported
collections on the next applicable import. Existing serialized meshlet data
remains readable. Terrain collections baked as ordinary `.asset` files need an
explicit terrain rebake to use the new generation algorithm.

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

For visual acceptance, regenerate a detailed mesh and rebake terrain, then
move the camera across LOD transitions and inspect silhouette, seams, cluster
debug views, and visible meshlet counts. Static C# compilation and the native
probe do not establish this runtime result.
