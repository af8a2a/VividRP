# VividTerrain chunk LOD path

`VividTerrain` converts a Unity `Terrain` into package-owned terrain metadata and meshlet geometry. Each non-empty chunk enters GPUDriven as an independent instance and reuses the meshlet instance culling, meshlet culling, visibility-buffer resolve, shadow, and active texture-backend paths.

## Conversion workflow

1. Select a GameObject with a Unity `Terrain` component.
2. Choose **GameObject > VividRP > GPUDriven > Create VividTerrain Copy**, or use **Create VividTerrain Copy** from the Terrain component context menu.
3. VividRP creates a `VividTerrainData` asset beside the source `TerrainData` asset when it lives under `Assets`; otherwise the output is created under `Assets`.
4. VividRP duplicates the source Terrain GameObject as a sibling, removes the `Terrain` renderer only from the duplicate, and adds `VividTerrain` to that copy.

The source GameObject, its `Terrain` component, and its `TerrainData` asset are never modified. The duplicate keeps the other copied components, children, and any `TerrainCollider`. The duplicate is rendered by GPUDriven when that subsystem is active. The original Terrain remains enabled, so disable it when comparing or replacing the source rendering to avoid overlapping geometry. Undo removes the generated scene copy, while the generated data asset remains in the project and can be removed manually if it is no longer needed.

## Baked data

The baker reads the source heightmap once and creates a regular grid in Terrain-local space. Each vertex contains position, a finite-difference normal, tangent, and full-terrain UV. The grid is divided into independently bounded chunks with one shared border row/column between adjacent chunks. The last source heightmap edge is always sampled even when the sample stride does not divide the source resolution evenly.

Terrain holes are respected. A sampled quad that covers any source hole texel is omitted, which deliberately avoids bridging a hole when baking with a coarse sample stride.

Surface-layer prototype data copies the diffuse, normal, and mask textures together with tiling and core material scalar values from each `TerrainLayer`. Alphamap/control texture baking is not part of this stage.

## Meshlet storage

Each non-empty chunk is passed through the existing `VividMeshOptimizer` meshoptimizer binding. New bakes request up to four mesh LOD levels by default and retain the finest generated level when the graph must be truncated. The builder may stop earlier when preserving the chunk boundary prevents a useful triangle reduction. The regular meshoptimizer simplifier runs with locked borders, and Terrain disables the sloppy fallback because that fallback cannot preserve borders. This keeps shared heightmap-edge vertices stable when adjacent chunks select different LOD depths. Empty chunks produced by terrain holes remain valid and do not create GPU instances.

LOD nodes are uploaded through the existing GPUDriven scene buffers. Automatic selection uses the shared screen-space error threshold, while the GPUDriven forced-depth debug control can inspect individual levels. Chunks with fewer generated levels remain on their leaf when a deeper level is forced.

No intermediate Unity `Mesh` asset is persisted. A chunk owns a `VividMeshletCollectionAsset` sub-asset whose node, meshlet, vertex, and local-index arrays are serialized into the existing versioned binary blob. Version 2 blobs use platform-independent LZ4 block compression, so repetitive terrain vertex streams and index data do not expand into large YAML arrays. Version 1 GZip blobs remain readable and are rewritten as LZ4 after their data is loaded and the asset is saved again.

Current defaults are:

- Height sample stride: 1 (use every heightmap sample)
- Chunk size: 64 x 64 quads
- Maximum chunk LOD levels: 4
- meshoptimizer vertex-cache optimization: enabled

The runtime-facing asset exposes source identity, terrain size, local bounds, row-major chunk grid coordinates, heightmap sample ranges, the configured LOD limit, actual per-chunk LOD counts, surface layers, and compressed meshlet collections. Assets baked before Terrain LOD was introduced deserialize the missing LOD setting as one and remain valid without rebaking. Multiple terrain layers are preserved in the asset, but this stage renders only the first layer through the shared Base/Normal/Mask surface binding. Control-map blending, geomorphing, and chunk streaming are intentionally deferred.
