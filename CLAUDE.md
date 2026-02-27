# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

VividRP is a Unity custom Scriptable Render Pipeline (SRP) package with a data-driven, node-based RenderGraph editor. It targets Unity 6000.5+ and depends on `com.unity.render-pipelines.core` 17.5.0.

Package ID: `com.af8a2a.vividrp`

## Build & Development

This is a Unity package (not a standalone project). It lives under `Packages/` in a Unity project. There is no CLI build command — compilation happens inside the Unity Editor. Open the parent Unity project (`E:\VividRP_Reborn`) in Unity 6000.5+.

No tests exist yet (test framework dependency is declared but unused).

## Assembly Structure

- `VividRP.Runtime` — Runtime code. References `com.unity.render-pipelines.core`. Root namespace: `VividRP.Runtime`. Platforms: all.
- `VividRP.Editor` — Editor-only code. References both Runtime and Core RP. Root namespace: `VividRP.Editor`. Platform: Editor only.

## Directory Layout

```
Runtime/
  RenderPipeline/          — SRP entry points (asset, pipeline, global settings)
  RenderGraph/
    Data/                  — Serializable graph model
      Nodes/               — Node data classes (10 types)
      Enums/               — PassType, PortType, ResourceType, TextureSizeMode
    Passes/                — Pass execution infrastructure
    Resource/              — Resource creation & history management
  Utility/
    PipelineResource/      — Reflection-based resource loading
  Resources/               — Shader assets (loaded via PipelineResourceManager)
  Shaders/                 — Shader source files
Editor/
  RenderGraph/
    Nodes/                 — Node view classes (10 types, one per node)
    Styles/                — USS stylesheets
```

## Architecture

### SRP Entry Point

- `VividRenderPipelineAsset` (ScriptableObject, `CreateAssetMenu`) — creates `VividRenderPipeline`, holds a reference to a `RenderGraphAsset`
- `VividRenderPipeline` — implements `IRenderGraphEnabledRenderPipeline`, initializes Blitter and PipelineResourceManager in constructor, calls `BeginRecording` → `RenderGraphExecutor` → `EndRecordingAndExecute`, disposes resources on cleanup
- `VividRenderPipelineGlobalSettings` — extends `RenderPipelineGlobalSettings<VividRenderPipelineGlobalSettings, VividRenderPipeline>` for Unity's global settings integration

### Data Model (Runtime/RenderGraph/Data/)

The graph is serialized as a `RenderGraphAsset` (ScriptableObject) containing:
- `List<RenderGraphNodeData>` (uses `[SerializeReference]` for polymorphism)
- `List<RenderGraphEdgeData>` (output port → input port connections)

Base classes:
- `RenderGraphNodeData` — GUID, position, name, list of `RenderGraphPortData`
- `ResourceNodeData` (abstract) — base for resource-producing nodes
- `RenderPassNodeData` (abstract) — base for pass nodes

Node types (10 total):

**Resource nodes** (output ports only):
- `TextureNodeData` — explicit-dimension textures
- `BufferNodeData` — graphics buffers
- `HistoryTextureNodeData` — double-buffered texture with Current/History ports, supports `TextureSizeMode` (Explicit/CameraRelative) and scaling; implements `IHistoryResourceNode`
- `HistoryBufferNodeData` — double-buffered buffer with Current/History ports; implements `IHistoryResourceNode`

**Pass nodes** (consume and produce resources):
- `RasterPassNodeData` — standard raster rendering
- `ComputePassNodeData` — compute shader dispatch
- `UnsafePassNodeData` — low-level pass with direct command buffer access
- `FullScreenPassNodeData` — full-screen quad rendering (creates its own output texture)
- `FinalBlitPassNodeData` — blits to backbuffer, handles scene view vs game view
- `PreviewPassNodeData` — generates preview textures for editor visualization

Ports have an ID, display name, `PortType` (Texture/Buffer/RendererList), direction (input/output), and `AccessFlags`.

Edges connect `OutputNodeGuid:OutputPortId` → `InputNodeGuid:InputPortId`.

Enums: `PassType` (Raster/Compute/Unsafe), `PortType` (Texture/Buffer/RendererList), `ResourceType` (Texture/Buffer), `TextureSizeMode` (Explicit/CameraRelative).

### Graph Validation

`RenderGraphAsset.Validate()` enforces DAG constraints using Kahn's algorithm, returning topological order or cycle errors. `WouldCreateCycle()` does a DFS check before edge creation.

### Execution (Runtime/RenderGraph/RenderGraphExecutor.cs)

`RenderGraphExecutor.Execute()` walks the validated topological order and:
1. Creates resources via `ResourceNodeData.CreateResource()` (passing `ResourceCreationContext` with RenderGraph, Camera, HistoryManager)
2. Records passes via `RenderPassNodeData.Record()` (passing `PassExecutionContext` with Camera, CullingResults, resolved input slots, output storage)
3. Resolves input ports to `ResourceSlot` (wraps `TextureHandle`/`BufferHandle`) via the edge map
4. Pass-through propagation: output ports inherit handles from matching input ports (naming convention: "Output X" ↔ "Input X")

Key context types:
- `PassExecutionContext` — Camera, CullingResults, resolved input ResourceSlots, output storage
- `ResourceCreationContext` — RenderGraph, Camera, HistoryResourceManager
- `ResourceSlot` — wraps TextureHandle or BufferHandle with validity check

### History Resource System (Runtime/RenderGraph/Resource/)

`HistoryResourceManager` manages double-buffered textures and buffers for temporal effects:
- `GetOrAllocate()` — allocates or reuses RTHandles with Current/History pair
- `GetCurrentHandle()` / `GetHistoryHandle()` — retrieves current or previous frame's resource
- `SwapBuffers()` — called each frame to rotate buffers
- `ReleaseAll()` — cleanup on pipeline disposal

`IHistoryResourceNode` interface (implemented by `HistoryTextureNodeData`, `HistoryBufferNodeData`):
- `CreateHistorySlot()` — creates ResourceSlot for the history output port
- `HistoryPortId` — identifies the history output port

### Pipeline Resource System (Runtime/Utility/PipelineResource/)

`PipelineResourceManager` (static utility) loads shader/asset references via reflection:
- `Initialize()` — loads `PipelineResourcesContainer` from `Resources/PipelineResources`
- `Get<T>()` — returns cached instance of resource class T
- `BuildInstance<T>()` — reflects on fields with `[ResourcePath]` attribute, populates from container
- `Cleanup()` — clears cache on pipeline disposal

`PipelineResourcesContainer` (ScriptableObject) holds `List<ResourceEntry>` mapping TypeName + FieldName → Asset.

Resource classes are marked with `[PipelineResource]`; fields use `[ResourcePath("path")]` (e.g., `VividRPCoreResources` with BlitShader, CoreBlitShader, FullScreenUVShader, etc.).

### Blitter (Runtime utility)

`Blitter` is a static utility for texture blitting operations:
- `Initialize(coreBlitShader, coreBlitColorAndDepthShader)` — called in `VividRenderPipeline` constructor
- `BlitTexture(cmd, source, scaleBias, material, pass)` — used by `FinalBlitPassNodeData` and `PreviewPassNodeData`
- `Cleanup()` — called on pipeline disposal

### Editor (Editor/RenderGraph/)

Built on `UnityEditor.Experimental.GraphView`:
- `RenderGraphEditorWindow` — main window, opened via `VividRP/Render Graph Editor` menu or double-clicking a `RenderGraphAsset`
- `RenderGraphView` — the GraphView subclass handling node/edge CRUD with Undo support and cycle prevention on edge creation
- `RenderGraphSearchWindow` — right-click node creation menu (Pass group + Resource group)
- `RenderNodeRegistry` — node type registry for the editor
- `NodeViewFactory` — creates the appropriate NodeView for each node data type
- `RenderGraphNodeView` — base node view; uses `[NodeEditor]` attribute for registration
- Node views (one per node type): `TextureNodeView`, `BufferNodeView`, `HistoryTextureNodeView`, `HistoryBufferNodeView`, `RasterPassNodeView`, `ComputePassNodeView`, `UnsafePassNodeView`, `FullScreenPassNodeView`, `FinalBlitPassNodeView`, `PreviewPassNodeView`
- `RenderGraphAssetEditor` — custom Inspector with "Open in Graph Editor" button
- `PipelineResourceUpdater` — updates `PipelineResourcesContainer` asset
- `VividGlobalSettingsPostprocessor` — handles global settings initialization
- Styles in `Editor/RenderGraph/Styles/RenderGraphEditor.uss`, loaded via package path `Packages/com.af8a2a.vividrp/...`

## Conventions

- Node data classes are `[Serializable]` and use `[SerializeReference]` on the asset's node list for polymorphic serialization
- Resource nodes are marked with `[ResourceNode(displayName)]`; pass nodes with `[RenderPass(displayName, passType)]`
- Resource classes use `[PipelineResource]` on the class and `[ResourcePath(path)]` on fields
- Node views use `[NodeEditor]` attribute for registration with `NodeViewFactory`
- GUIDs are `System.Guid.NewGuid().ToString()` strings
- Editor code uses Undo recording (`Undo.RecordObject`) before mutating the asset
- Private fields use `m_` prefix (Unity convention)
- USS stylesheets are referenced by package path, not asset path
