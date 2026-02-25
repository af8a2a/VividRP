# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

VividRP is a Unity custom Scriptable Render Pipeline (SRP) package with a data-driven, node-based RenderGraph editor. It targets Unity 6000.5+ and depends on `com.unity.render-pipelines.core` 17.5.0.

Package ID: `com.af8a2a.vividrp`

## Build & Development

This is a Unity package (not a standalone project). It lives under `Packages/` in a Unity project. There is no CLI build command — compilation happens inside the Unity Editor. Open the parent Unity project (`E:\VividRP_Reborn`) in Unity 6000.5+.

No tests exist yet (test framework dependency is declared but unused).

## Assembly Structure

- `VividRP.Runtime` — Runtime code. References `com.unity.render-pipelines.core`. Root namespace: `VividRP.Runtime`
- `VividRP.Editor` — Editor-only code. References both Runtime and Core RP. Root namespace: `VividRP.Editor`. Platform: Editor only.

## Architecture

### SRP Entry Point

`VividRenderPipelineAsset` (ScriptableObject, `CreateAssetMenu`) creates `VividRenderPipeline`. The asset holds a reference to a `RenderGraphAsset`. The pipeline uses Unity's `RenderGraph` API — it calls `BeginRecording`, delegates to `RenderGraphExecutor`, then `EndRecordingAndExecute`.

### Data Model (Runtime/RenderGraph/Data/)

The graph is serialized as a `RenderGraphAsset` (ScriptableObject) containing:
- `List<RenderGraphNodeData>` (uses `[SerializeReference]` for polymorphism)
- `List<RenderGraphEdgeData>` (output port → input port connections)

Node types inherit from `RenderGraphNodeData`:
- **Resource nodes**: `TextureNodeData`, `BufferNodeData` — produce resource handles, have output ports only
- **Pass nodes**: `RasterPassNodeData`, `ComputePassNodeData`, `UnsafePassNodeData` — consume and produce resources via input/output ports

Each node has a GUID, position, name, and a list of `RenderGraphPortData`. Ports have an ID, display name, `PortType` (Texture/Buffer/RendererList), and direction (input/output).

Edges connect `OutputNodeGuid:OutputPortId` → `InputNodeGuid:InputPortId`.

### Graph Validation

`RenderGraphAsset.Validate()` enforces DAG constraints using Kahn's algorithm, returning topological order or cycle errors. `WouldCreateCycle()` does a DFS check before edge creation.

### Execution (Runtime/RenderGraph/RenderGraphExecutor.cs)

`RenderGraphExecutor.Execute()` walks the validated topological order and:
1. Creates `TextureHandle`/`BufferHandle` for resource nodes
2. Records `AddRasterRenderPass`, `AddComputeRenderPass`, or `AddUnsafeRenderPass` for pass nodes
3. Resolves input ports to handles via the edge map, and uses `UseTexture`/`UseBuffer` to declare dependencies
4. Pass-through propagation: output ports inherit handles from matching input ports (naming convention: "Output X" ↔ "Input X")

### Editor (Editor/RenderGraph/)

Built on `UnityEditor.Experimental.GraphView`:
- `RenderGraphEditorWindow` — main window, opened via `VividRP/Render Graph Editor` menu or double-clicking a `RenderGraphAsset`
- `RenderGraphView` — the GraphView subclass handling node/edge CRUD with Undo support and cycle prevention on edge creation
- `RenderGraphSearchWindow` — right-click node creation menu (Pass group + Resource group)
- `RenderGraphNodeView` — base node view; subclasses (`RasterPassNodeView`, `ComputePassNodeView`, `UnsafePassNodeView`, `TextureNodeView`, `BufferNodeView`) add inline property editors and color-coded title bars
- `RenderGraphAssetEditor` — custom Inspector with "Open in Graph Editor" button
- Styles in `Editor/RenderGraph/Styles/RenderGraphEditor.uss`, loaded via package path `Packages/com.af8a2a.vividrp/...`

## Conventions

- Node data classes are `[Serializable]` and use `[SerializeReference]` on the asset's node list for polymorphic serialization
- GUIDs are `System.Guid.NewGuid().ToString()` strings
- Editor code uses Undo recording (`Undo.RecordObject`) before mutating the asset
- Private fields use `m_` prefix (Unity convention)
- USS stylesheets are referenced by package path, not asset path
