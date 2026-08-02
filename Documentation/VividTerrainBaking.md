# VividTerrain baking prototype

`VividTerrain` is a data-only GPUDriven terrain prototype. This stage converts a Unity `Terrain` into package-owned terrain metadata and meshlet geometry; it does not render the baked data yet.

## Conversion workflow

1. Select a GameObject with a Unity `Terrain` component.
2. Choose **GameObject > VividRP > GPUDriven > Create VividTerrain Copy**, or use **Create VividTerrain Copy** from the Terrain component context menu.
3. VividRP creates a `VividTerrainData` asset beside the source `TerrainData` asset when it lives under `Assets`; otherwise the output is created under `Assets`.
4. VividRP duplicates the source Terrain GameObject as a sibling, removes the `Terrain` renderer only from the duplicate, and adds `VividTerrain` to that copy.

The source GameObject, its `Terrain` component, and its `TerrainData` asset are never modified. The duplicate keeps the other copied components, children, and any `TerrainCollider`. Because rendering is outside this prototype, the new VividTerrain copy is not visible yet; the original Terrain continues rendering normally. Undo removes the generated scene copy, while the generated data asset remains in the project and can be removed manually if it is no longer needed.

## Baked data

The baker reads the source heightmap once and creates a regular grid in Terrain-local space. Each vertex contains position, a finite-difference normal, tangent, and full-terrain UV. The grid is divided into independently bounded chunks with one shared border row/column between adjacent chunks. The last source heightmap edge is always sampled even when the sample stride does not divide the source resolution evenly.

Terrain holes are respected. A sampled quad that covers any source hole texel is omitted, which deliberately avoids bridging a hole when baking with a coarse sample stride.

Surface-layer prototype data copies the diffuse, normal, and mask textures together with tiling and core material scalar values from each `TerrainLayer`. Alphamap/control texture baking is not part of this stage.

## Meshlet storage

Each non-empty chunk is passed through the existing `VividMeshOptimizer` meshoptimizer binding. The bake requests exactly one mesh LOD level; terrain LOD generation and selection are deferred.

No intermediate Unity `Mesh` asset is persisted. A chunk owns a `VividMeshletCollectionAsset` sub-asset whose node, meshlet, vertex, and local-index arrays are serialized into the existing versioned binary blob. Version 2 blobs use platform-independent LZ4 block compression, so repetitive terrain vertex streams and index data do not expand into large YAML arrays. Version 1 GZip blobs remain readable and are rewritten as LZ4 after their data is loaded and the asset is saved again.

Current defaults are:

- Height sample stride: 1 (use every heightmap sample)
- Chunk size: 64 x 64 quads
- meshoptimizer vertex-cache optimization: enabled

The runtime-facing asset already exposes source identity, terrain size, local bounds, chunk grid coordinates, heightmap sample ranges, surface layers, and compressed meshlet collections. GPU buffer upload, rendering, LOD, streaming, and culling integration are intentionally deferred.
