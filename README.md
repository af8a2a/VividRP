# VividRP

[English](README_EN.md) | 简体中文

VividRP 是一个面向 Unity 6 的自定义 Scriptable Render Pipeline 包。它延续了 legacy [VividRP](https://github.com/af8a2a/VividRP) 的方向，但当前代码库已经不再是旧版 README 那份“大而全功能清单”的直接同步版本。现阶段的重点是先把一个可运行、可扩展、可测试的 RenderGraph-first 渲染管线基础搭起来，再逐步把 GPU Driven、Bindless、Ray Tracing 等能力接回到统一工作流里。

> [!IMPORTANT]
> - 当前开发目标为 Unity `6000.5.0a7` / `6000.5`
> - 当前最可靠的运行路径是 Windows + DirectX 12
> - Bindless 与 Ray Tracing 相关流程默认按 DX12 / DXR 能力设计
> - legacy README 中列出的功能不应直接视为当前包已全部实现，请以本文档和当前源码为准

## 当前定位

与 legacy README 把 VividRP 描述成“自定义 URP 变体”不同，当前包更适合被理解为一个独立的自定义 SRP 包，核心由以下部分组成：

- `VividRenderPipeline` / `VividRenderPipelineAsset` / `VividRenderPipelineGlobalSettings`
- 基于 `com.unity.graphtoolkit` 的 `.vrdg` RenderGraph 编辑器
- 将 `.vrdg` 编译为运行时 `RenderGraphData` 的导入链路
- 通过 `PassRecorder` 执行的反射驱动 RenderGraph 运行时
- 围绕 GPU Driven、Bindless、Ray Tracing 构建的实验性子系统

## 当前已落地的能力

### RenderGraph 工作流

- 使用 `Assets/Create/VividRP/Render Graph` 创建 `.vrdg`
- `ScriptedImporter` 在导入时把 `.vrdg` 编译为运行时 `RenderGraphData`
- Pass 端口由 `[RenderGraphResource]` 字段反射生成
- 当前支持 `Texture`、`Buffer`、`RenderList`、`History`、`Preview`、`Acceleration Structure` 节点
- 支持基于 Graph Toolkit Local Subgraph 的 `SubSystem`
- 支持 pass-owned resource 的 override / hidden binding 模式
- 运行时包含 history、preview、frame context 和资源绑定管理

### 核心渲染与调试

- 自定义相机 / 灯光附加数据与 Inspector
- `PreDepth`、`GBuffer`、`CopyDepth`、`MotionVector`、`DeferredLighting`、`FinalBlit` 等核心 Pass
- `HDRISky`、聚类光照、Realtime Area Light LUT 预计算
- `ClusterDebug`、`TileDebug`、`SliderDebug`、`OverlayDebug`、`RTASInstanceDebug` 等调试链路
- 默认 Volume Profile 由 VividRP Global Settings 统一管理

### GPU Driven / Bindless

- `GPU Driven` 与 `GPU Driven Debug Overlay` 开关已经集成到管线资产 Inspector
- 已包含 meshlet collection 导入/构建、`MeshletRenderer` 组件、scene data builder、visibility buffer 相关 Pass 与 shader
- 已包含 bindless 纹理容器和 native descriptor allocator
- 提供 `Setup-Bindless.ps1`，用于把 `UnityBindless.dll` 复制到 `Assets/Plugins/...` 以满足早加载要求

### Ray Tracing

- 已包含可序列化的 `RenderGraphAccelerationStructureDesc`
- 编辑器中可直接 author acceleration structure resource node
- 已实现 `RTASBuildPass`
- 已实现 directional ray traced shadow 与 denoise pass
- `RayTracingSettingsVolume` 提供 RTAS build / culling / bias 相关配置
- 包内已包含 SIGMA shadow denoise 相关 shader 资源

### 材质、Shader 与后处理

- 当前包内提供 `StandardLit`、`SimpleLit`、`SimpleForward` 等材质 shader
- 已包含 URP Lit 材质转换与自定义 Shader GUI
- 已包含 Color Grading、White Balance、Channel Mixer、Split Toning、Film Grain、Tonemapping 等后处理组件
- `ThirdParty/LWGUI` 已随包集成，供材质 Inspector 使用

### 编辑器工具与测试

- `PipelineResourceUpdater` 与 `PipelineResourcesContainer` Inspector 用于统一回收 `[PipelineResource]` / `[ResourcePath]` 声明
- `Editor/RenderGraph/GeneratedRenderPassNodes.g.cs` 由生成器维护，不需要手工维护节点注册
- `Tests/Editor` 已覆盖 RenderGraph、Pass、Drawer、GPU Driven、Ray Tracing Volume、材质导入、组件数据等大量 EditMode 场景

## 快速开始

1. 使用 Unity `6000.5.0a7` 或兼容的 `6000.5` 打开包含该包的 Unity 项目根目录
2. 通过 `Assets/Create/VividRP/Vivid Render Pipeline` 创建 `VividRenderPipelineAsset`
3. 在 `Project Settings > Graphics` 中把该资产设为当前 SRP
4. 通过 `Assets/Create/VividRP/Render Graph` 创建 `.vrdg`
5. 双击 `.vrdg` 打开 RenderGraph 编辑器，添加资源节点和 Pass 节点
6. 将该 `.vrdg` 生成的主对象 `RenderGraphData` 赋给管线资产的 `Render Graph Asset`
7. 如需默认 Volume，直接在管线资产 Inspector 的 `Default Volume` 折叠区初始化或编辑
8. 如资源路径或 `[PipelineResource]` 声明发生变化，打开 `Runtime/Resources/PipelineResources.asset` 并点击 `Recollect Engine Resources`
9. 如需 GPU Driven，启用管线资产上的 `GPU Driven`
10. 如需 Bindless，在项目根目录运行以下命令，然后重启 Unity：

```powershell
powershell -ExecutionPolicy Bypass -File .\Packages\VividRP\Setup-Bindless.ps1
```

11. 如需 Ray Tracing，请使用支持 DXR 的硬件与 DX12 环境，并在 Volume Profile 中添加 `VividRP/Ray Tracing/Settings`

## 当前没有从 legacy README 恢复的内容

legacy README 中提到的大量高级特性目前并不是当前包的已交付状态，至少不应在这个包里被默认认为“已经可用”。当前仓库中尚未看到完整落地或仅保留占位入口的方向包括：

- Super Resolution 栈，例如 `DLSS`、`TAAU`、`FSR*`
- legacy README 里的大规模 GI / SSR / Path Tracing / ReSTIR 系列能力
- `Decal`、`Terrain`、`Foliage`、`Volumetric` 等子系统的完整实现
- 生产可用的 samples / demo scene 工作流

如果你是在对照旧版 README 寻找某个特性，建议先检查当前源码、`Documentation/` 和 `Tests/Editor/` 是否真的存在对应实现。

## 包结构速览

- `Runtime/RenderPipeline`：管线资产、全局设置、Volume 组件
- `Runtime/RenderGraph`：运行时图数据、资源包装、preview/history、pass recorder
- `Runtime/RenderPass/Core`：当前主要核心 Pass
- `Runtime/SubSystem/GPUDriven`：GPU Driven、meshlet、bindless、native 集成
- `Editor/RenderGraph`：Graph Toolkit 编辑器、校验、导入、编译、节点注册生成
- `Editor/PipelineResource`：资源回收与同步工具
- `Shaders`：包内 shader 与公共 HLSL
- `Documentation`：当前工作流和子系统文档
- `Tests/Editor`：EditMode 测试

## 文档

- [RenderGraph Editor](Documentation/RenderGraphEditor.md)
- [RenderGraph SubSystem](Documentation/RenderGraphSubSystem.md)
- [RenderGraph Resource Descriptors](Documentation/RenderGraphResourceDescriptors.md)
- [Acceleration Structure Support](Documentation/AccelerationStructureSupport.md)
- [Bindless Setup](Documentation/Bindless.md)
- [LWGUI Notes](Documentation/LWGUI.md)

## 测试

从项目根目录运行当前 EditMode 测试：

```powershell
Unity.exe -batchmode -projectPath "<project root>" -runTests -testPlatform EditMode -testResults Logs/editmode-results.xml -quit -logFile Logs/editmode.log
```

当前包里还没有提交 PlayMode 测试。
