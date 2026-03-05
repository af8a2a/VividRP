# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

VividRP is a Unity custom Scriptable Render Pipeline (SRP) package with a reflection-based, attribute-driven RenderGraph pass system. It targets Unity 6000.5+ and depends on `com.unity.render-pipelines.core` 17.5.0.

Package ID: `com.af8a2a.vividrp`

## Build & Development

This is a Unity package (not a standalone project). It lives under `Packages/` in a Unity project. There is no CLI build command — compilation happens inside the Unity Editor. Open the parent Unity project (`E:\VividRP_Reborn`) in Unity 6000.5+.

No tests exist yet (test framework dependency is declared but unused).

## Assembly Structure

- `VividRP.Runtime` — Runtime code. References `com.unity.render-pipelines.core`. Root namespace: `VividRP.Runtime`. Platforms: all.
- `VividRP.Editor` — Editor-only code. References both Runtime and Core RP. Root namespace: `VividRP.Editor`. Platform: Editor only.
- `VividRP.Shaders` — Shader assembly (Dummy.cs + shader files).

## Directory Layout

```
Runtime/
  RenderPipeline/          — SRP entry points (asset, pipeline, global settings)
  RenderGraph/
    Data/                  — RenderGraphData.cs (empty ScriptableObject stub)
    FrameContext/          — VividCameraData, VividRenderingData (ContextItem subclasses)
    Resource/              — Resource descriptor wrappers and attributes
    PassRecorder.cs        — Reflection-based pass recording (partial class)
    PassRecorder.Execution.cs — Execution logic for PassRecorder
    RenderGraphPass.cs     — Pass base classes (IRenderPass, ComputePass, RasterPass, UnsafePass)
  RenderPass/
    Core/                  — SetupPass.cs (stub)
    Example/               — FullScreenPass.cs (example raster pass)
  Utility/
    PipelineResource/      — Reflection-based resource loading
  Resources/               — PipelineResources.asset (loaded via PipelineResourceManager)
  Shaders/                 — Shader source files (Core/, FullScreenUV.shader)
Editor/
  PipelineResource/        — PipelineResourceUpdater.cs
  RenderGraph/             — RenderGraphEditor.cs (minimal GraphToolkit stub)
  RenderPipeline/          — VividGlobalSettingsPostprocessor.cs
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

**`RenderGraphAccelerationStructureDesc`** — ray tracing acceleration structure descriptor:
- `ToAccelerationStructureDesc()` — converts to Unity's type
- Static factory: `Create()`

**`PassResource`** — container for all resources collected from a pass:
- `PassResourceEntry[] Textures`, `PassResourceEntry[] Buffers`
- `IEnumerable<PassResourceEntry> AllEntries`

**`PassResourceEntry`** — metadata for a single resource field:
- `FieldInfo Field`, `string Name`, `AccessFlags Access`, `PassResourceType ResourceType`
- `object Descriptor`, `int AttachmentIndex`, `bool IsDepthAttachment`
- Typed accessors: `RenderGraphTexture Texture`, `RenderGraphBuffer Buffer`

### Frame Context (Runtime/RenderGraph/FrameContext/)

Both extend Unity's `ContextItem` and are stored in `ContextContainer`:

- `VividCameraData` — `Camera camera`, `actualWidth`, `actualHeight`, `pixelWidth`, `pixelHeight`
- `VividRenderingData` — `CullingResults cullingResults`, `ScriptableRenderContext context`

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

### Editor (Editor/)

- `RenderGraphEditor` — minimal stub using `Unity.GraphToolkit`, `[Graph("RenderGraph")]` attribute, single `Graph m_Graph` field. Not yet functional.
- `PipelineResourceUpdater` — `AssetPostprocessor` with `[InitializeOnLoadMethod]`; reflects on all `[PipelineResource]` classes, resolves assets, updates `PipelineResourcesContainer`
- `VividGlobalSettingsPostprocessor` — ensures global settings on asset import

### Example Pass (Runtime/RenderPass/Example/)

`FullScreenPass` extends `RasterPass`:
- `[RenderGraphResource(Access = AccessFlags.Write, AttachmentIndex = 0)] RenderGraphTexture texture` — output color attachment
- `Create()` — loads `FullScreenUVShader`, creates material
- `Prepare()` — updates texture dimensions from `VividCameraData`
- `Record()` — calls `Blitter.BlitTexture()` for full-screen quad

## Conventions

- Pass classes extend `ComputePass`, `RasterPass`, or `UnsafePass` and implement `IRenderPass`
- Resource fields on passes use `[RenderGraphResource]` attribute
- Resource classes use `[PipelineResource]` on the class and `[ResourcePath(path)]` on fields
- Private fields use `m_` prefix (Unity convention)
- `PassRecorder` is a `static partial class` split across two files
- Frame data accessed via `ContextContainer.Get<VividCameraData>()` / `ContextContainer.Get<VividRenderingData>()`
- `Blitter` is from Core RP, not this package
