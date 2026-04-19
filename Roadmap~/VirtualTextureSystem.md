## Single-Space VT Runtime MVP

### Summary
Implement a runnable VT MVP for exactly one 2D address space, one stack, and one physical cache, with a dedicated demo rendering path rather than touching `StandardLit`. The runtime will guarantee a resident locked lowest mip, automatic page uploads with fence-backed double buffering, deterministic cache pressure behavior, and a debug surface inside the existing VividRP debug panel so fallback and eviction are directly visible in-scene.

### Key Changes
- **VT core in `Runtime/SubSystem/VirtualTexture/`**
    - Keep the existing page-table and residency foundation, but add an internal upload scheduler that owns:
        - a queued upload list,
        - two staging upload batches,
        - one fence per in-flight batch,
        - auto-commit when a fence passes.
    - Seed the lowest mip immediately at space bootstrap, mark it locked, and rebuild the page table before normal requests so every miss always has an ancestor path.
    - Preserve the current public/test seam (`TryGetPendingUploadRequests` / `CommitUpload`) for tests, but route normal runtime behavior through the internal uploader.
    - Add deterministic eviction bias on top of LRU:
        - never evict the locked lowest mip,
        - prefer evicting finer pages before coarser fallback pages,
        - never evict same-frame or in-flight uploads.
    - Keep one-space scope explicit: runtime consumes the first/only registered VT binding and does not add multi-space selection in this iteration.
    - Add an internal producer sub-interface under the existing `VTProducer` marker so the public surface is not broken; implement a procedural producer that generates mip/page-coded tiles.

- **Demo render path and sample hookup**
    - Add a dedicated `VirtualTextureDemoPass` under the render-pass tree, with its own render-list tag, instead of modifying the main material path.
    - Add a `VirtualTextureDemo` material shader under `Shaders/Material/` that:
        - computes requested mip from UV derivatives,
        - resolves VT addresses from `_VTPageTable`,
        - samples `_VTPhysicalCache`,
        - emits feedback for unresolved/fallback/pending pages,
        - supports debug shading modes.
    - Bind VT resources in the demo pass from `VividVirtualTextureFrameData`, and bind feedback UAVs explicitly for the draw scope so feedback only exists on the demo path.
    - Add a public scene component, `VirtualTextureDemoController`, that registers the single VT space and procedural producer with sensible defaults.
    - Update the example assets to make the feature directly runnable:
        - `Assets/Vivid Render Graph.vrdg`: insert the demo pass into the active graph.
        - `Assets/Scenes/SampleScene.unity`: add a minimal VT demo object/controller path for validation.

- **Debug view in existing debug panel**
    - Extend `Runtime/RenderPipeline/VividRenderingDebugDisplaySettings.cs` with a new VT foldout and a `VirtualTextureDebugMode` enum.
    - Expose at least these modes:
        - `None`: normal shaded VT output.
        - `Residency`: exact resident vs fallback vs pending coloring.
        - `MipBias`: visualize `requestedMip - resolvedMip`.
        - `PhysicalPageId`: hash-color by physical page id to make eviction reuse obvious.
    - Add read-only VT counters in the panel from the stats registry:
        - resident pages,
        - free pages,
        - pending uploads,
        - evictions,
        - faults.
    - The debug view should make cache pressure observable without needing logs.

### Public Interfaces / Contract Changes
- Add public `VirtualTextureDemoController` for the sample/demo path.
- Add `VirtualTextureDebugMode` and corresponding debug-settings fields.
- Add public-facing HLSL helper(s) in `Shaders/Core/Public/VirtualTexture/VirtualTexture.hlsl` for requested-mip computation and stable VT sampling on the demo path.
- Keep `VTProducer` source-compatible; add a new internal producer capability interface rather than changing the existing public marker.

### Test Plan
- **EditMode VT core tests**
    - Lowest mip bootstrap is resident, locked, and provides valid fallback before any dynamic request.
    - In-flight upload batches auto-commit only after simulated fence completion.
    - Upload double buffering stalls cleanly when both batches are busy instead of overwriting staging state.
    - Eviction keeps the coarse fallback page(s) and prefers finer pages under pressure.
    - Cache-full behavior produces deterministic page reuse, not random allocation order.

- **EditMode render/debug tests**
    - `VirtualTextureDemoPass` node generation/compilation works and exposes expected ports/inspector parameters.
    - Debug settings defaults and VT foldout values resolve correctly.
    - Shader contract tests cover new VT demo/debug entry points and explicit feedback binding declarations.

- **Manual acceptance**
    - Open `Assets/Scenes/SampleScene.unity`.
    - Use the active Vivid pipeline asset with `Assets/Vivid Render Graph.vrdg`.
    - Rotate the camera quickly around the VT demo object:
        - no whole-screen corruption,
        - misses fall back to ancestor mip instead of invalid output,
        - with a deliberately undersized cache, debug modes show stable eviction/bias rather than random flicker.

### Assumptions
- Scope stays at one 2D address space, one stack, one physical cache.
- Tile content is procedural in this iteration; no real texture paging or multi-stack content upload.
- Example assets under `Assets/` are allowed to change for a directly runnable demo.
- Existing manual upload commit APIs stay as test hooks and are not removed.
- Unity batch outputs should be written under the project `Logs/` directory, not `Packages/Custom_URP/Logs/`, to avoid import-loop issues.
