# VividRP

English | [简体中文](README.md)

VividRP is an experimental Scriptable Render Pipeline (SRP) package for Unity 6. It centers on a visual, data-driven RenderGraph workflow and explores high-end rendering systems including deferred rendering, GPU Driven, Bindless, Ray Tracing, reference path tracing, volumetrics, virtual texturing, and temporal upscaling.

> [!IMPORTANT]
> - The current development baseline is Unity `6000.7.0a3` (commit `b08e599c`). `package.json` still declares `6000.6.0a3` as the minimum compatibility version; use `6000.7.0a3` for the current feature set.
> - Windows + DirectX 12 is the most reliable runtime environment. Bindless and Ray Tracing workflows require DX12 / DXR capabilities.
> - VividRP is under active development. Some subsystems are experimental and are not guaranteed to be cross-platform, production-ready, or backward-compatible.
> - This repository no longer maps one-to-one to the feature list of the legacy [VividRP](https://github.com/af8a2a/VividRP). Treat the current source, this README, and `Documentation~` as the source of truth.

## Core workflow

VividRP content is primarily authored as `.vrdg` graph assets:

1. In the Project window, choose `Assets/Create/VividRP/Standard Render Graph` for a runnable template, or `Assets/Create/VividRP/Render Graph` for an empty graph.
2. Double-click the `.vrdg` asset and configure passes and resource connections in the editor built on `com.unity.graphtoolkit`.
3. The importer compiles the graph into runtime `RenderGraphData`.
4. Assign the generated `RenderGraphData` to the **Render Graph Asset** field of a `VividRenderPipelineAsset`.

Pass ports are generated from fields marked with `[RenderGraphResource]`. At runtime, `PassRecorder` schedules three standard pass types:

- `RasterPass` for raster rendering;
- `ComputePass` for Compute Shader dispatch;
- `UnsafePass` when native `CommandBuffer` access is required.

The graph editor supports Texture, Buffer, Render List, History, Preview, and Acceleration Structure resources, as well as Local Subgraph-based SubSystems. The compile pipeline derives resource bindings and dependencies, performs pass culling, and validates graphs.

## Included capabilities

The modules below have implementations in the current package. Availability still depends on project configuration, platform, and hardware support.

| Area | Included systems |
| --- | --- |
| Core rendering | Pre-depth, GBuffer, deferred lighting, motion vectors, HZB, general object drawing, and color pyramids |
| Shadows and lighting | CSM / PCSS, clustered lighting, directional DXR shadows with SIGMA denoising, sky, and atmospheric scattering |
| Post processing | Auto exposure, bloom with mip-scattering and FFT-convolution modes, color grading, depth of field, GTAO, lens flares, SSR with ReBlur, local exposure, and final compositing |
| Anti-aliasing and upscaling | CMAA2, TAA, TSR, and FSR3; DLSS Super Resolution / Ray Reconstruction require additional plugin integration |
| Volumetrics | Global and local volumetric fog, VBuffer, volumetric lighting, and Max-Z generation |
| GPU Driven | Meshlet import and rendering, Visibility Buffer, object dispatch, debug overlay, and bindless descriptor support |
| Ray Tracing | Serializable RTAS descriptors, RTAS construction, directional ray-traced shadows, and related debug passes |
| Reference path tracing | OpenPBR-based multi-bounce DXR prototype, accumulation, ray-tracing G-buffer, NRD REBLUR preview denoising, and a Unity Open Image Denoise backend |
| Resources and subsystems | Virtual Texturing with SVT, DBuffer Decals, LTC area lights, reflection-probe atlas, ReGIR, and sky management |
| Per-Object Buffer | Per-renderer shader data without `MaterialPropertyBlock`, centralized generated HLSL layouts, a color example, and an MPB CPU benchmark |
| Experimental particles | ECS paged-storage simulation, culling, sorting, Billboard / Mesh / Stretch rendering, trails, collisions, and sub-emitters |
| Editor and tests | RenderGraph editing, importing, compilation, validation, node-registry generation, resource drawers, and EditMode tests |

The package includes `StandardLit`, `SimpleLit`, `SimpleForward`, and `StandardLayeredLit` shaders, plus URP Lit and HDRP Lit material conversion tools. Cameras, lights, reflection probes, and most Volume settings have VividRP companion components or custom Inspectors.

## Quick start

1. Open the project with Unity `6000.7.0a3`, or a compatible `6000.7` release.
2. Add the package through Package Manager as an embedded or local package.
3. Create a `VividRenderPipelineAsset` through `Assets/Create/VividRP/Vivid Render Pipeline`.
4. Set that asset as the active render pipeline in **Project Settings > Graphics**.
5. Create and edit a **Standard Render Graph**, then assign its generated `RenderGraphData` to the pipeline asset.
6. For post-processing or global rendering settings, initialize **Default Volume** in the pipeline asset Inspector, or create a scene Volume Profile.
7. To enable temporal anti-aliasing, add `VividAdditionalCameraData` to a camera and select the desired mode.

### Optional: GPU Driven, Bindless, and Ray Tracing

- Enable **GPU Driven** in the pipeline asset Inspector.
- Bindless requires running the following script from the project root, then restarting Unity:

```powershell
powershell -ExecutionPolicy Bypass -File .\Packages\VividRP\Setup-Bindless.ps1
```

- Ray Tracing requires DXR-compatible hardware and DX12. Add `VividRP/Ray Tracing/Settings` to a Volume Profile and configure it for the project.
- DLSS requires the NVIDIA dependencies and the `DLSS_PLUGIN_INTEGRATE` scripting define symbol.
- Reference path tracing uses a dedicated `.vrdg` asset and pipeline asset for controlled-scene validation; it is not intended for real-time use or complete ground truth. Current support is primarily `StandardLit` opaque / alpha-tested materials. The OIDN backend is enabled only in the Editor or 64-bit Standalone builds when `com.unity.rendering.denoising` is available.

## Directory guide

- `Runtime/RenderPipeline`: pipeline, pipeline asset, global settings, and Volume integration.
- `Runtime/RenderGraph`: runtime graph data, resource descriptors, History / Preview, Pass Recorder, and Frame Context.
- `Runtime/RenderPass`: Core, Debug, and Example passes.
- `Runtime/SubSystem`: GPU Driven, Virtual Texture, Volumetric, Sky, Decal, Reflection, DLSS, and other subsystems.
- `Runtime/ComponentData`: companion camera, light, and reflection-probe data.
- `Runtime/PerObjectBuffer`: per-renderer shader-data system and examples.
- `Runtime/Experiment/Particle`: experimental ECS particle system.
- `Editor/RenderGraph`: graph editor, importer, compiler, validation, and node-registry generation.
- `Editor/PipelineResource`: resource collection and synchronization for `[PipelineResource]` declarations.
- `Shaders`: package shaders and their assembly.
- `Documentation~`: workflow, constraints, and design notes.
- `Tests/Editor`: EditMode tests.

## Resource synchronization and development notes

- `.vrdg` files are the RenderGraph source of truth. Do not manually maintain imported `RenderGraphData` contents.
- Do not manually edit `Editor/RenderGraph/GeneratedRenderPassNodes.g.cs` or `Runtime/Resources/PipelineResources.asset`. The former is generated by the node-registry generator; the latter should be updated through the resource-collection workflow.
- After changing `[PipelineResource]` or `[ResourcePath]` declarations, select `Runtime/Resources/PipelineResources.asset` and run **Recollect Engine Resources** in the Inspector.
- RenderGraph resource field names are contracts for ports, previews, and compiled bindings. Review graph assets and tests when changing them.

## Documentation and roadmap

- [RenderGraph Editor](Documentation~/RenderGraphEditor.md)
- [RenderGraph SubSystem](Documentation~/RenderGraphSubSystem.md)
- [RenderGraph Resource Descriptors](Documentation~/RenderGraphResourceDescriptors.md)
- [Acceleration Structure Support](Documentation~/AccelerationStructureSupport.md)
- [Bindless Setup](Documentation~/Bindless.md)
- [Local Exposure](Documentation~/LocalExposure.md)
- [Virtual Texture Architecture](Documentation~/VirtualTextureCoreArchitecture.md)
- [Per-Object Buffer](Documentation~/PerObjectBuffer.md)
- Roadmaps: [Shadow](Roadmap~/Shadow.md), [Sky](Roadmap~/Sky.md), [Virtual Texture](Roadmap~/VirtualTextureSystem.md), [SVT](Roadmap~/SVTRoadmap.md), and [Reference Path Tracing](Roadmap~/ReferencePathTracingRoadmap.md)
