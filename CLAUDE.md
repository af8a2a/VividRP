# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

VividRP is a Unity custom Scriptable Render Pipeline (SRP) package with a reflection-based, attribute-driven RenderGraph pass system. It targets Unity 6000.5+ and depends on `com.unity.render-pipelines.core` 17.5.0.

Package ID: `com.af8a2a.vividrp`

## Build & Development

This is a Unity package (not a standalone project). It lives under `Packages/` in a Unity project. There is no CLI build command — compilation happens inside the Unity Editor. Open the parent Unity project (`E:\VividRP_Reborn`) in Unity 6000.5+.

### Testing

Run EditMode tests with Unity Test Framework:
```bash
Unity.exe -batchmode -projectPath "E:\VividRP_Reborn" -runTests -testPlatform EditMode -testResults Logs/editmode-results.xml -quit -logFile Logs/editmode.log
```

Tests are located in `Tests/Editor/` under the `VividRP.Editor.Tests` assembly. No committed PlayMode tests exist yet — add the relevant test assembly before documenting or relying on a PlayMode batch command.

## Assembly Structure

- `VividRP.Runtime` — Runtime code. References `com.unity.render-pipelines.core`. Root namespace: `VividRP.Runtime`. Platforms: all.
- `VividRP.Editor` — Editor-only code. References both Runtime and Core RP. Root namespace: `VividRP.Editor`. Platform: Editor only.
- `VividRP.Shaders` — Shader assembly (Dummy.cs + shader files).

## Directory Layout

```
Runtime/
  RenderPipeline/          — SRP entry points (asset, pipeline, global settings, volume components)
  RenderGraph/
    Data/                  — RenderGraphData.cs (ScriptableObject for graph assets)
    FrameContext/          — VividCameraData, VividRenderingData, VividLightData (ContextItem subclasses)
    Resource/              — Resource descriptor wrappers and attributes
    PassRecorder.cs        — Reflection-based pass recording (partial class)
    PassRecorder.Execution.cs — Execution logic for PassRecorder
    RenderGraphPass.cs     — Pass base classes (IRenderPass, ComputePass, RasterPass, UnsafePass)
    RenderGraphPreviewRegistry.cs — Preview texture management for editor
    RenderGraphHistoryRegistry.cs — Temporal resource management
  RenderPass/
    Core/                  — Core passes (GBuffer, DrawObject, CopyDepth, HDRISky, FinalBlit, etc.)
    Core/PostProcessing/   — Post-processing passes (ColorGrading, etc.)
    Example/               — FullScreenPass.cs (example raster pass)
  ComponentData/           — VividAdditionalCameraData, VividAdditionalLightData
  Utility/
    PipelineResource/      — Reflection-based resource loading
  Resources/               — PipelineResources.asset (loaded via PipelineResourceManager)
  Extension/CoreRP/        — Core RP extension glue plus VividRP.CoreRP.Runtime.asmref
Shaders/                   — Shader source files (top-level package folder, not under Runtime/)
Editor/
  PipelineResource/        — PipelineResourceUpdater.cs
  RenderGraph/             — GraphToolkit-based RenderGraph editor, validators, importers, node types; .vrdg files imported via RenderGraphImporter.cs
  RenderGraph/Nodes/       — Node data types (RenderPassNodeData, TextureResourceNodeData, etc.)
  RenderPipeline/          — Global settings, asset editor, volume profile utilities
  ComponentEditor/         — Camera and light component editors
  Material/                — Shader GUI implementations
  Shader/                  — Editor-only shader assets
  VolumeEditor/            — Volume component custom inspectors
Tests/Editor/              — EditMode test suite
Tests/Runtime/             — Runtime/PlayMode tests (add assembly before relying on batch command)
Documentation/             — Package documentation
```

## Architecture

### SRP Entry Point

- `VividRenderPipelineAsset` (ScriptableObject, `CreateAssetMenu`) — creates `VividRenderPipeline`
- `VividRenderPipeline` — implements `IRenderGraphEnabledRenderPipeline`. Constructor initializes `PipelineResourceManager` and `Blitter` (from Core RP). `Render()` calls `RenderCamera()` per camera then `m_RenderGraph.EndFrame()`. `RenderCamera()` initializes frame context and calls `PassRecorder.RecordRenderGraph()`.
- `VividRenderPipelineGlobalSettings` — extends `RenderPipelineGlobalSettings<VividRenderPipelineGlobalSettings, VividRenderPipeline>`

### Pass System (Runtime/RenderGraph/)

Passes are C# classes that declare their resources via `[RenderGraphResource]` attributes. `PassRecorder` discovers and records them automatically.

**`IRenderPass` (interface)** — implemented by all pass classes:
- `PassResource Initialize()` — reflects on `[RenderGraphResource]` fields to collect resource requirements
- `void Prepare(ContextContainer frameData)` — called each frame to update dynamic resource descriptors
- `void Create()` — one-time init (load shaders, create materials)

**Pass base classes** (in `RenderGraphPass.cs`):
- `ComputePass` — `abstract void Record(ComputeGraphContext context)`
- `RasterPass` — `abstract void Record(RasterGraphContext context)`
- `UnsafePass` — `abstract void Record(UnsafeGraphContext context)`

**`PassRecorder` (static partial class)**:
- `Compile()` — one-time: instantiates all pass types, calls `Create()` and `Initialize()`
- `RecordRenderGraph(RenderGraph, ScriptableRenderContext)` — main entry point each frame
- `InitializeContext()` — populates `ContextContainer` with `VividCameraData` and `VividRenderingData`
- Type-specific methods: `RecordComputePass()`, `RecordRasterPass()`, `RecordUnsafePass()`
- Resource setup: `SetupComputeResources()`, `SetupRasterResources()`, `SetupUnsafeResources()`
- Static state: `_renderPasses`, `m_passResources`, `m_frameData`

### Resource Descriptors (Runtime/RenderGraph/Resource/)

**`[RenderGraphResource]` attribute** — marks fields on pass classes:
- `string Name` — optional display name
- `AccessFlags Access` — read/write flags
- `int AttachmentIndex` — color attachment slot for raster passes (0–7; -1 = not an attachment)
- `bool IsDepthAttachment` — marks as depth attachment

**`RenderGraphTexture`** — serializable texture descriptor wrapper:
- `RenderGraphTextureDesc desc` — serializable descriptor (Width, Height, ColorFormat, DepthBufferBits, MSAASamples, etc.)
- `internal TextureHandle innerHandle` — set by PassRecorder
- Implicit conversion to `TextureHandle`
- Static factories: `CreateColorTarget()`, `CreateDepthTarget()`

**`RenderGraphBuffer`** — serializable buffer descriptor wrapper:
- `RenderGraphBufferDesc desc` — serializable descriptor (Count, Stride, Target, Name)
- `internal BufferHandle innerHandle` — set by PassRecorder
- Implicit conversion to `BufferHandle`
- Static factories: `CreateStructured()`, `CreateAppend()`, `CreateIndirectArguments()`

**`RenderGraphRenderList`** — serializable renderer list descriptor wrapper:
- `RenderGraphRenderListDesc desc` — descriptor with ShaderTagNames, RenderQueueRange, SortingCriteria, LayerMask, etc.
- `internal RendererListHandle innerHandle` — set by PassRecorder
- Implicit conversion to `RendererListHandle` and `RendererList`
- Static factories: `CreateOpaque()`, `CreateTransparent()`
- Default shader tags: `"VividForward"`, `"SRPDefaultUnlit"`

**`RenderGraphAccelerationStructureDesc`** — ray tracing acceleration structure descriptor:
- `ToAccelerationStructureDesc()` — converts to Unity's type
- Static factory: `Create()`

**`PassResource`** — container for all resources collected from a pass:
- `PassResourceEntry[] Textures`, `PassResourceEntry[] Buffers`, `PassResourceEntry[] RenderLists`
- `IEnumerable<PassResourceEntry> AllEntries`

**`PassResourceEntry`** — metadata for a single resource field:
- `FieldInfo Field`, `string Name`, `AccessFlags Access`, `PassResourceType ResourceType`
- `object Descriptor`, `int AttachmentIndex`, `bool IsDepthAttachment`
- Typed accessors: `RenderGraphTexture Texture`, `RenderGraphBuffer Buffer`, `RenderGraphRenderList RenderList`

### Frame Context (Runtime/RenderGraph/FrameContext/)

All extend Unity's `ContextItem` and are stored in `ContextContainer`:

- `VividCameraData` — `Camera camera`, `actualWidth`, `actualHeight`, `pixelWidth`, `pixelHeight`, plus extension methods for additional camera data access
- `VividRenderingData` — `CullingResults cullingResults`, `ScriptableRenderContext context`
- `VividLightData` — Light data for the current frame

### Preview & History Systems (Runtime/RenderGraph/)

**`RenderGraphPreviewRegistry`** — manages preview textures for editor visualization:
- `TryGetPreview(Type passType, string fieldName, out Texture texture)` — retrieves preview texture for a pass resource
- `TryGetSinglePreview(out Type passType, out string fieldName, out Texture texture)` — gets the single active preview
- `SetPreview(Type passType, string fieldName, Texture texture)` — sets external preview texture
- `GetOrCreatePreviewTarget(...)` — creates RTHandle for preview rendering
- Only available in Editor or Development builds (controlled by `IsAvailable`)

**`RenderGraphHistoryRegistry`** — manages temporal resources across frames:
- `GetOrCreateHistoryTarget(Camera, RenderGraphData, int historyIndex, RenderGraphTextureDesc, CommandBuffer)` — gets or creates history texture
- `TryGetHistoryTarget(Camera, RenderGraphData, int historyIndex, out RTHandle, out bool hasValidData)` — retrieves existing history
- `MarkHistoryValid(Camera, RenderGraphData, int historyIndex, bool valid)` — marks history data as valid/invalid
- History textures are keyed by camera instance, graph asset, and history index

### Pipeline Resource System (Runtime/Utility/PipelineResource/)

`PipelineResourceManager` (static utility) loads shader/asset references via reflection:
- `Initialize()` — loads `PipelineResourcesContainer` from `Resources/PipelineResources`
- `Get<T>()` — returns cached instance of resource class T
- `BuildInstance<T>()` — reflects on `[ResourcePath]` fields, populates from container
- `Cleanup()` — clears cache on pipeline disposal

`PipelineResourcesContainer` (ScriptableObject) holds `List<ResourceEntry>` mapping TypeName + FieldName → Asset.

Resource classes use `[PipelineResource]` on the class and `[ResourcePath("path")]` on fields. Example: `VividRPCoreResources` with `BlitShader`, `CoreBlitShader`, `CoreBlitColorAndDepthShader`, `FullScreenUVShader`.

### Blitter

`Blitter` is from `com.unity.render-pipelines.core` (not defined in this package):
- `Initialize(coreBlitShader, coreBlitColorAndDepthShader)` — called in `VividRenderPipeline` constructor
- `BlitTexture(cmd, source, scaleBias, material, pass)` — used by pass implementations
- `Cleanup()` — called on pipeline disposal

### Component Data (Runtime/ComponentData/)

**`VividAdditionalCameraData`** — extends camera with VividRP-specific settings:
- `VividCameraRenderType renderType` — Base or Overlay
- `bool clearDepth` — whether to clear depth
- `LayerMask volumeLayerMask` — volume layer mask for post-processing
- `bool stopNaNs`, `bool dithering` — post-processing options
- Internal matrix storage for view/projection/jitter matrices
- Extension method: `Camera.GetVividAdditionalCameraData()`

**`VividAdditionalLightData`** — extends light with VividRP-specific settings:
- `bool usePipelineSettings` — use pipeline-wide settings
- `bool customShadowLayers` — enable custom shadow rendering layers
- `RenderingLayerMask shadowRenderingLayers` — custom shadow layer mask
- `RenderingLayerMask effectiveShadowRenderingLayers` — resolved shadow layers
- Extension method: `Light.GetVividAdditionalLightData()`

### Volume System (Runtime/RenderPipeline/)

**`HDRISkyVolume`** — HDRI sky volume component:
- `NoInterpCubemapParameter skyCubemap` — sky cubemap texture
- `ColorParameter tint` — sky tint color (HDR)
- `MinFloatParameter exposure` — sky exposure
- `ClampedFloatParameter rotation` — sky rotation (-180 to 180)

**Post-Processing Volume Components** (Runtime/RenderPass/Core/PostProcessing/):
- `WhiteBalance` — temperature and tint adjustments
- `ColorAdjustments` — post-exposure, contrast, color filter, hue shift, saturation
- `ChannelMixer` — per-channel color mixing (RGB output channels)
- `SplitToning` — shadows/highlights toning with balance
- `LiftGammaGain` — lift, gamma, gain color grading
- `ShadowsMidtonesHighlights` — shadows, midtones, highlights with range controls
- `ColorCurves` — YRGB curves and HSV curves (hue vs hue, hue vs sat, sat vs sat, lum vs sat)
- `Tonemapping` — tonemapping mode selection

All post-processing components implement `IPostProcessComponent` with `IsActive()` method.

### Editor (Editor/)

**RenderGraph Editor** (Editor/RenderGraph/):
- `RenderGraphEditor` — GraphToolkit-based visual graph editor with `[Graph("RenderGraph")]` attribute
- `RenderGraphImporter` — imports `.rendergraph` assets as `RenderGraphData`
- `RenderGraphEditorValidator` — validates graph structure and connections
- `RenderPassNodeRegistryGenerator` — generates `GeneratedRenderPassNodes.g.cs` from runtime pass types
- `RenderPassNodeRegistryBuilder` — builds registry of pass types for node generation
- `RenderGraphPassCompilationUtility` — compiles graph assets into pass execution order

**Node Data Types** (Editor/RenderGraph/Nodes/):
- `RenderGraphNodeData` — base class for all graph nodes
- `RenderPassNodeData` — represents a render pass in the graph
- `TextureResourceNodeData` — texture resource node
- `BufferResourceNodeData` — buffer resource node
- `RenderListResourceNodeData` — renderer list resource node
- `HistoryResourceNodeData` — temporal history resource node
- `ClassificationResourceNodeData` — classification resource node
- `PreviewNodeData` — preview output node
- `RenderPassPortUtility` — generates input/output ports for pass nodes based on `[RenderGraphResource]` fields

**Property Drawers** (Editor/RenderGraph/):
- `RenderGraphTextureDescDrawer` — custom drawer for `RenderGraphTextureDesc`
- `RenderGraphBufferDescDrawer` — custom drawer for `RenderGraphBufferDesc`
- `RenderGraphRenderListDescDrawer` — custom drawer for `RenderGraphRenderListDesc`
- `TexturePreviewValueDrawer` — drawer for texture preview values

**Component Editors** (Editor/ComponentEditor/):
- `VividCameraEditor` — custom editor for cameras with `VividAdditionalCameraData`
- `VividSerializedCamera` — serialized property wrapper for camera data
- `VividLightEditor` — custom editor for lights with `VividAdditionalLightData`
- `VividSerializedLight` — serialized property wrapper for light data

**Pipeline Editors** (Editor/RenderPipeline/):
- `VividRenderPipelineAssetEditor` — custom editor for pipeline asset
- `VividDefaultVolumeProfileSettingsPropertyDrawer` — drawer for default volume profile settings
- `VividDefaultVolumeProfileEditorUtility` — utilities for volume profile management
- `VividGlobalSettingsPostprocessor` — ensures global settings on asset import
- `PipelineResourceUpdater` — `AssetPostprocessor` with `[InitializeOnLoadMethod]`; reflects on all `[PipelineResource]` classes, resolves assets, updates `PipelineResourcesContainer`

**Material Editors** (Editor/Material/):
- `StandardLitShaderGUI` — shader GUI for standard lit materials

### Example Pass (Runtime/RenderPass/Example/)

`FullScreenPass` extends `RasterPass`:
- `[RenderGraphResource(Access = AccessFlags.Write, AttachmentIndex = 0)] RenderGraphTexture texture` — output color attachment
- `Create()` — loads `FullScreenUVShader`, creates material
- `Prepare()` — updates texture dimensions from `VividCameraData`
- `Record()` — calls `Blitter.BlitTexture()` for full-screen quad

### Core Passes (Runtime/RenderPass/Core/)

**`GBufferPass`** — deferred rendering G-Buffer generation:
- Outputs: GBuffer0 (R8G8B8A8), GBuffer1 (R16G16_SFloat), GBuffer2 (R8G8B8A8), GBuffer3 (B10G11R11_UFloatPack32), Depth (Depth32)
- Uses `RenderGraphRenderList` with `"VividGBuffer"` shader tag
- Renders opaque geometry to multiple render targets

**`DrawObjectPass`** — generic object drawing pass:
- Configurable render list with shader tags and queue range
- Supports color and depth attachments

**`CopyDepthPass`** — depth buffer copy:
- Copies depth texture using Core RP utilities

**`HDRISkyPass`** — HDRI sky rendering:
- Reads `HDRISkyVolume` from volume stack
- Renders cubemap-based sky with rotation support
- Outputs to color target with depth testing

**`FinalBlitPass`** — final blit to camera target:
- Blits processed image to camera's backbuffer
- Handles post-processing output

**`ColorGradingPass`** — color grading post-processing:
- Reads all color grading volume components (WhiteBalance, ColorAdjustments, etc.)
- Generates LUT texture for color grading
- Applies tonemapping
- Supports StopNaNs and dithering options

**`SetupPass`**, **`ClassificationPass`** — infrastructure passes for frame setup and light classification

## Conventions

- Pass classes extend `ComputePass`, `RasterPass`, or `UnsafePass` and implement `IRenderPass`
- Resource fields on passes use `[RenderGraphResource]` attribute — put it on fields, not properties; the collector reflects instance fields across the inheritance chain and ignores null resource values
- Initialize pass resource descriptors before `Initialize()` runs; a null `RenderGraphTexture`, `RenderGraphBuffer`, or `RenderGraphRenderList` field is skipped and will not get ports or runtime setup
- Resource classes use `[PipelineResource]` on the class and `[ResourcePath(path)]` on fields; expose resource bindings as `public` instance fields — the updater does not populate private fields
- Private fields use `m_` prefix (Unity convention)
- `PassRecorder` is a `static partial class` split across two files
- Frame data accessed via `ContextContainer.Get<VividCameraData>()` / `ContextContainer.Get<VividRenderingData>()`
- `Blitter` is from Core RP, not this package
- Shader files are in top-level `Shaders/` folder, not under `Runtime/`
- Generated files use `.g.cs` suffix and should not be manually edited
- Node data classes end with `NodeData` suffix
- Test files end with `Tests.cs` suffix
- For read/write resources, input port uses `<FieldName>_In`, output port uses `<FieldName>_Out`
- Preserve existing resource field names when changing pass APIs — read/write ports, preview keys, compiled graph bindings, and tests rely on those exact names

## RenderGraph Rules

- `.vrdg` files are the source of truth for graph authoring; do not manually maintain derived `RenderGraphData` contents — let the importer/compiler regenerate them
- New passes should implement `Create()`, `Prepare(...)`, `Record(...)`, and `Dispose()` coherently; `Prepare(...)` is where per-frame descriptor sizing/imports should happen
- Use the appropriate resource wrapper type for graph integration: `RenderGraphTexture`, `RenderGraphBuffer`, `RenderGraphRenderList`, and `RenderGraphAccelerationStructureDesc` where supported by the runtime/editor flow
- History resource nodes expose `PrevOut` and `CurrOut`; if you change history binding semantics, update importer, compilation ordering, runtime history handling, and tests together
- Pass ordering is compiler-driven; when changing binding semantics or connection rules, verify `RenderGraphPassCompilationUtility` still derives the right dependencies and cycle fallback behavior
- If a pass changes its exposed resource layout dynamically, keep `IDynamicPassResourceLayout` behavior and the related editor/runtime tests in sync
- If a pass supports async compute or global state modification, express that through the existing marker interfaces instead of ad hoc flags
- When adding a runtime pass type, verify the generated node registry, navigation helpers, compilation utility, and pass-node tests still reflect the new pass correctly
- Preview nodes are texture-focused; if you add new preview behavior for buffers, render lists, or history resources, update both validator/drawer logic and tests in the same change

## Coding Style

- Use 4-space indentation, braces on new lines, and small focused methods
- Match namespaces to area: `VividRP.Runtime`, `VividRP.Runtime.RenderPass.Core`, `VividRP.Editor.RenderGraph`, `VividRP.Editor.Tests`
- Preserve reflection-driven contracts: `[RenderGraphResource]` fields are discovered by PassRecorder and used for editor port generation
- Preserve serialized field names in authoring/runtime data models unless you also add an explicit migration path — `RenderGraphData`, `RenderGraphPassDefinition`, and `PipelineResourcesContainer` are serialized assets that survive importer/editor updates
- Keep GraphToolkit naming consistent: node models end with `NodeData`, generated files use `.g.cs`
- Serialized fields often use `m_` prefix, but match the style of the file you're editing
- Use `Undo.RecordObject(...)` before mutating serialized assets in editor code; also persist with `EditorUtility.SetDirty(...)` and `AssetDatabase.SaveAssetIfDirty(...)` when following existing sync/generation patterns
- Prefer minimal visibility (`internal`, `internal sealed`) for editor helpers
- Do not hand-edit generated files like `GeneratedRenderPassNodes.g.cs` or `PipelineResources.asset`

## Resource Workflow

- Use `PipelineResourceUpdater` or the custom inspector on `PipelineResourcesContainer` to recollect engine resources; do not hand-maintain `Runtime/Resources/PipelineResources.asset` entry-by-entry
- Resource container entries are normalized and sorted during recollection; if you change resource-key generation, preserve deterministic ordering and update related tests
- Keep `ResourceEntry` serialization compatibility in mind when renaming fields; `ResourceObject` already carries a migration attribute from the older `Asset` name

## Testing

- Use Unity Test Framework with NUnit under `Tests/Editor/`
- Follow naming pattern: `MethodName_ExpectedBehavior_WhenCondition`
- Add focused EditMode tests with each fix or feature, especially around pass-port generation, descriptor drawers, preview metadata, registry generation, reflection-based pass/resource behavior, render-list/history resources, and custom editor utilities
- If a change introduces runtime-only behavior that cannot be validated meaningfully in current EditMode tests, add the appropriate `Tests/Runtime/` or PlayMode coverage in the same change
- Prefer self-contained tests using dummy pass types or temporary ScriptableObjects

## Commit Guidelines

- Prefer short imperative commit titles
- Use Conventional Commit prefixes when practical: `feat:`, `fix:`, `test:`, `refactor:`
- Include generated/synchronized outputs in the same commit: `GeneratedRenderPassNodes.g.cs`, `PipelineResources.asset`, `.meta` files
- PRs should summarize purpose, key changes, package-path assumptions, and EditMode test evidence
- Include screenshots or GIFs for RenderGraph editor UI changes and note shader-visible behavior changes when touching passes, shaders, or pipeline resources

## Important Notes

- Unity `.meta` files are auto-generated; do not manually create or edit them
- Do not hand-edit generated or synchronized artifacts such as `Editor/RenderGraph/GeneratedRenderPassNodes.g.cs` or `Runtime/Resources/PipelineResources.asset` unless you are intentionally fixing their generator/sync pipeline
- Unity `.meta` files, generated assets, and package-relative paths must stay in sync when moving or renaming files
- The repository currently uses both `Packages/com.af8a2a.vividrp/...` and `Packages/VividRP/...` path constants; do not “fix” only one side during refactors — audit all package-relative paths together
- Package path changes require updates to both `Editor/PipelineResource/PipelineResourceUpdater.cs` and `Editor/RenderGraph/RenderPassNodeRegistryGenerator.cs`
- Quick searches:
  - Pass/resource search: `rg "IRenderPass|RenderGraphResource|PipelineResource|ResourcePath" Runtime Editor Tests`
  - Editor/codegen search: `rg "GeneratedRenderPassNodes|BuildRegistrations|RegisteredPassTypeName" Editor Runtime Tests`
  - Package path audit: `rg "Packages/VividRP|Packages/com.af8a2a.vividrp|com.af8a2a.vividrp" Runtime Editor Tests package.json`
