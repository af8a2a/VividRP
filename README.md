# VividRP

[English](README_EN.md) | 简体中文

VividRP 是一个面向 Unity 6 的自定义 Scriptable Render Pipeline 包。它延续了 legacy [VividRP](https://github.com/af8a2a/VividRP) 的方向，但当前代码库已经不再是旧版 README 那份"大而全功能清单"的直接同步版本。现阶段的重点是先把一个可运行、可扩展、可测试的 RenderGraph-first 渲染管线基础搭起来，再逐步把 GPU Driven、Bindless、Ray Tracing 等能力接回到统一工作流里。

> [!IMPORTANT]
> - 当前开发目标为 Unity `6000.5.0a7` / `6000.5`
> - 当前最可靠的运行路径是 Windows + DirectX 12
> - Bindless 与 Ray Tracing 相关流程默认按 DX12 / DXR 能力设计
> - legacy README 中列出的功能不应直接视为当前包已全部实现，请以本文档和当前源码为准

## 当前定位

与 legacy README 把 VividRP 描述成"自定义 URP 变体"不同，当前包更适合被理解为一个独立的自定义 SRP 包，核心由以下部分组成：

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
- 三种 Pass 基类：`RasterPass`（光栅化渲染）、`ComputePass`（Compute Shader 调度）、`UnsafePass`（原生 CommandBuffer 访问）
- 资源绑定模式：`External`（图外部连接）、`PassOwnedOverrideable`（Pass 创建默认值，可被覆盖）；Pass 内部临时资源使用 `[TransientResource]`
- 当前支持 `Texture`、`Buffer`、`RenderList`、`History`、`Preview`、`Acceleration Structure` 节点
- 支持基于 Graph Toolkit Local Subgraph 的 `SubSystem`
- 运行时包含 history、preview、frame context 和资源绑定管理
- 支持 `[Range]` float 参数和 enum 参数反射，可在图上直接配置
- `IAsyncComputeSupportedPass` 标记接口支持异步计算
- `IDynamicPassResourceLayout` 接口支持运行时动态资源布局
- `IAllowGlobalStateModificationPass` 标记全局状态修改类 Pass

### 核心渲染与调试

**基础 Pass：**
- `PreDepthPass` — 不透明物体预深度（"VividPreDepth" shader tag）
- `GBufferPass` — 延迟 GBuffer 生成（GBuffer0~4，支持 Albedo/Normal/Material/Emission/BakedGI）
- `CopyDepthPass` — 深度拷贝到 R32_SFloat 颜色纹理
- `GenerateViewZPass` — 从深度图生成线性视空间深度
- `DrawObjectPass` — 通用物体绘制，支持最多 8 个颜色目标和动态重配置
- `MotionVectorPass` — 物体和相机运动矢量渲染
- `HZBGeneratePass` — SPD 分层深度缓冲（HZB），支持原子计数
- `CMAA2Pass` — Conservative Morphological Anti-Aliasing 2（Edge Detection → Deferred Blend → Combine）
- `TemporalAAPass` — 时域抗锯齿（TAA），支持 Halton 抖动、历史混合、运动衰减、抗闪烁

**阴影系统：**
- `CSMShadowPass` — Cascade Shadow Map 渲染（2x2 Atlas，最多 4 级 Cascade）
- `CSMShadowResolvePass` — CSM 阴影解析，PCSS 柔和阴影（Percentage Closer Soft Shadows）
- `ShadowClassifyPass` — 像素分类标记需要光线追踪阴影的区域
- `DirectionalRayTracedShadowPass` — 方向光 DXR 光线追踪阴影
- `SIGMAShadowDenoisePass` — SIGMA 阴影降噪（ClassifyTiles → SmoothTiles → Blur → TemporalStabilization）

**光照系统：**
- `DeferredLightingPass` — 主延迟光照计算，支持材质分类（Standard / Fabric / ClearCoat）
- `DeferredDirectionalLightingPass` — 仅方向光的延迟光照变体
- `LightGridPass` / `LightGridGlobalPass` — 簇状光照剔除（FPTL big-tile + layered light list）
- `PreIntegratedFGDPreparePass` — Pre-Integrated FGD（Filament Specular BRDF）LUT 预计算
- `ClassificationPass` — 基于 Tile 的材质分类，为 DeferredLightingPass 生成 Indirect Dispatch 参数
- `HDRISky` / `PhysicallyBasedSky` — HDRI 天空和物理天空系统
- `AtmosphericScatteringPass` — 大气散射与雾效
- `SkyInjectionPass` — 天空注入渲染

**后处理：**
- `AutoExposurePass` — 自动曝光（支持 Unreal 和 HDRP 两种实现路径）
- `BloomPass` — 泛光效果
- `ColorGradingPass` — 颜色分级（Channel Mixer / Color Adjustments / Curves / LiftGammaGain / ShadowsMidtonesHighlights / Split Toning / Tonemapping / WhiteBalance）
- `DepthOfFieldPass` — 景深
- `DiffusionPass` — 屏幕空间扩散/模糊
- `GTAOPass` — Ground Truth Ambient Occlusion
- `DataDrivenLensFlarePass` / `ScreenSpaceLensFlarePass` — 镜头光晕
- `FinalBlitPass` — 最终 Blit 到相机 Backbuffer（含 Color Grading LUT、自动曝光、Film Grain、Bloom 合成）
- `StopNaNPass` — 消除颜色缓冲中的 NaN/Inf 像素
- `FilmGrain`、`Tonemapping`（ACES / Neutral / Custom）等 Volume 组件

**调试 Pass：**
- `ClusterDebugPass` — 簇状光照可视化（Opaque / Slice 模式）
- `TileDebugPass` — Tile 级别调试
- `ExposureDebugPass` — 自动曝光计量区可视化
- `SliderDebugPass` — 调试滑动条
- `RTASInstanceDebugPass` — RTAS 实例可视化
- `OverlayDebugPass` — 调试叠加层
- `VirtualTextureDemoPass` — Virtual Texture 演示（Residency / MipBias / PhysicalPageId 模式）
- `VirtualTextureVisualizationPass` — Virtual Texture 可视化

### GPU Driven / Bindless

- `GPU Driven` 与 `GPU Driven Debug Overlay` 开关已经集成到管线资产 Inspector
- `GPUDrivenSettingsVolume` — Global Volume 中的 GPU Driven 配置（强制 LOD 深度、误差阈值）
- 已包含 meshlet collection 导入/构建、`MeshletRenderer` 组件、scene data builder
- `VisibilityBufferPass` — GPU Driven 可见性缓冲渲染
- `VisibilityBufferGBufferResolvePass` — 可见性缓冲解析到 GBuffer
- `VisibilityBufferResolvePass` — 可见性缓冲解析到颜色目标
- `ObjectDispatcherService` — GPU Driven 对象调度服务，基于 `ObjectTracker` 批量处理 Unity Object 变更
- 已包含 bindless 纹理容器和 native descriptor allocator
- 提供 `Setup-Bindless.ps1`，用于把 `UnityBindless.dll` 复制到 `Assets/Plugins/...` 以满足早加载要求

### Ray Tracing

- `RayTracingSettingsVolume` — Ray Tracing 配置（Build Mode / Culling Mode / Ray Bias / Build Flags 等）
- 已包含可序列化的 `RenderGraphAccelerationStructureDesc`
- 编辑器中可直接 author Acceleration Structure resource node
- `RTASBuildPass` — 构建场景 Ray Tracing Acceleration Structure，支持 Shader Tag Fallback、双面/AlphaTest/透明关键词、RTAS Instance Batching、Frustum/Sphere/SolidAngle Culling
- 已实现 directional ray traced shadow 与 SIGMA shadow denoise pass
- 包内已包含 SIGMA shadow denoise 相关 shader 资源

### 材质、Shader 与后处理

- 当前包内提供 `StandardLit`、`SimpleLit`、`SimpleForward` 等材质 shader
- 已包含 URP Lit 材质转换与自定义 Shader GUI（`StandardLitShaderGUI`）
- 支持 Metallic 工作流、Alpha Test、Clear Coat、Normal Map、Emission、Roughness Map、Occlusion Map
- 已包含 Color Grading、White Balance、Channel Mixer、Split Toning、Film Grain、Tonemapping 等后处理组件
- `ThirdParty/LWGUI` 已随包集成，供材质 Inspector 使用

### 自定义组件数据与 Volume 系统

**组件数据：**
- `VividAdditionalCameraData` — 相机附加数据
  - Render Type（Base / Overlay）、Clear Depth、Volume Layer Mask
  - Stop NaNs、Dithering 开关
  - Antialiasing Mode（None / CMAA2 / TemporalAntiAliasing）
  - TAA Settings（Jitter Spread、Sample Count、Blend Factor、Motion Weight Decay、Anti-Flicker）
  - 相机矩阵扩展（View / Projection / Jitter / GPU 投影等）
- `VividAdditionalLightData` — 灯光附加数据
  - Light Render Database (`VividLightRenderDatabase`) 全局灯光追踪
  - Shadow Settings（CSM 质量、Ray Traced Shadow、PCSS 参数）
  - Light category flags、cast shadows、rendering layer masks

**Volume 组件（运行时）：**

| 类别 | Volume 组件 |
|---|---|
| **阴影** | `CascadedShadowSettingsVolume`（CSM 级联数/距离/分割比/偏移）、`RayTracingSettingsVolume`（DXR 构建/剔除/偏置）、`GPUDrivenSettingsVolume`（LOD 深度/误差阈值） |
| **天空** | `HDRISky`、`PhysicallyBasedSky`、`SkySettings` |
| **曝光** | `AutoExposure`（测光模式/HDRP 与 Unreal 实现/直方图/适应速度） |
| **后处理** | `Bloom`、`ColorAdjustments`、`ColorCurves`、`ChannelMixer`、`LiftGammaGain`、`ShadowsMidtonesHighlights`、`SplitToning`、`Tonemapping`、`WhiteBalance`、`DepthOfField`、`Diffusion`、`GTAO`、`FilmGrain`、`ScreenSpaceLensFlare` |

### 帧上下文系统

- `VividCameraData` — 相机状态（像素尺寸、矩阵、Shader Globals、时序索引）
- `VividRenderingData` — 帧渲染状态（剔除结果、GPU-Driven 标记）
- `VividLightData` — 可见光集合（方向光 / 点光 / 面光，与 `VividLightRenderDatabase` 集成）
- `VividShadowData` — 阴影状态（CSM Cascade 数据、Atlas 参数、World Texel Size 等）
- `VividTemporalData` — 帧间数据（View/Projection 矩阵、Jitter、首帧标记）
- `VividSkyData` — 天空系统状态（HDRI / PhysicallyBased / 环境光照模式）
- `VividClusteredLightingData` — 簇状光照配置（Tile 大小、Slice 数量、对数分布、光源列表 Buffer）
- `VividRayTracingSettingsData` — Ray Tracing 配置数据
- `VividVirtualTextureFrameData` — Virtual Texture 逐帧数据
- `FrameContextSystem` — CameraRelative 系统，驱动时序状态推进 / Shader Globals 设置

### 编辑器工具与测试

- `PipelineResourceUpdater` 与 `PipelineResourcesContainer` Inspector 用于统一回收 `[PipelineResource]` / `[ResourcePath]` 声明
- 完整的 `.vrdg` RenderGraph 编辑器（`RenderGraphEditorGraph`），支持 SubSystem（`.vrdgsub`）、图验证、trim graph、双击导航
- 导入链路：`.vrdg` → `ScriptedImporter` → `RenderGraphCompiler` → `RenderGraphData`
- 节点注册与代码生成：`RenderPassNodeRegistry` / `RenderPassNodeRegistryGenerator` → `GeneratedRenderPassNodes.g.cs`
- Graph Toolkit 节点数据模型：`RenderPassNodeData`、`TextureResourceNodeData`、`BufferResourceNodeData`、`RenderListResourceNodeData`、`AccelerationStructureResourceNodeData`、`HistoryResourceNodeData`
- 资源描述符 Property Drawer（Texture / Buffer / RenderList）
- 图编译实用工具：Pass 排序与依赖推导、SubSystem 展开、Pass Culling 分析
- 图编辑器验证：Pass 类型解析、资源合约校验、Async Compute 资格检查、循环依赖检测、连接冲突检测
- `VividRenderPipelineAssetEditor` — 管线资产 Inspector（Render Graph Asset / Color Grading Space / Auto Exposure 实现 / Async Compute / GPU Driven / SRP Batcher / Adaptive Probe Volumes / Default Volume Profile）
- `VividCameraEditor` / `VividLightEditor` — 相机/灯光自定义 Inspector
- `StandardLitShaderGUI`（LWGUI 框架）与 `URPLitMaterialConverter` — 材质编辑器与 URP Lit 材质转换
- Volume 组件自定义 Inspector（CascadedShadow / RayTracing / GPUDriven / Sky / AutoExposure / ColorGrading / Diffusion / ScreenSpaceLensFlare）
- `BoundProxy` 编辑器工具 — 代理体形编辑与管理
- `Tests/Editor` 已覆盖 RenderGraph、Pass、Drawer、GPU Driven、Ray Tracing Volume、材质导入、组件数据、FrameContext、Shadow Data、SubSystem 等大量 EditMode 场景

## 快速开始

1. 使用 Unity `6000.5.0a7` 或兼容的 `6000.5` 打开包含该包的 Unity 项目根目录
2. 通过 `Assets/Create/VividRP/Vivid Render Pipeline` 创建 `VividRenderPipelineAsset`
3. 在 `Project Settings > Graphics` 中把该资产设为当前 SRP
4. 通过 `Assets/Create/VividRP/Render Graph` 创建 `.vrdg`
5. 双击 `.vrdg` 打开 RenderGraph 编辑器，添加资源节点和 Pass 节点
6. 将该 `.vrdg` 生成的主对象 `RenderGraphData` 赋给管线资产的 `Render Graph Asset`
7. 如需默认 Volume，直接在管线资产 Inspector 的 `Default Volume` 折叠区初始化或编辑
8. 如需抗锯齿，在相机 `VividAdditionalCameraData` 中选择 `CMAA2` 或 `TemporalAntiAliasing`
9. 如资源路径或 `[PipelineResource]` 声明发生变化，打开 `Runtime/Resources/PipelineResources.asset` 并点击 `Recollect Engine Resources`
10. 如需 GPU Driven，启用管线资产上的 `GPU Driven`
11. 如需 Bindless，在项目根目录运行以下命令，然后重启 Unity：

```powershell
powershell -ExecutionPolicy Bypass -File .\Packages\VividRP\Setup-Bindless.ps1
```

12. 如需 Ray Tracing，请使用支持 DXR 的硬件与 DX12 环境，并在 Volume Profile 中添加 `VividRP/Ray Tracing/Settings`

## 当前没有从 legacy README 恢复的内容

legacy README 中提到的大量高级特性目前并不是当前包的已交付状态，至少不应在这个包里被默认认为"已经可用"。当前仓库中尚未看到完整落地或仅保留占位入口的方向包括：

- Super Resolution 栈，例如 `DLSS`、`TAAU`、`FSR*`
- legacy README 里的大规模 GI / SSR / Path Tracing / ReSTIR 系列能力
- `Decal`、`Terrain`、`Foliage`、`Volumetric` 等子系统的完整实现
- 生产可用的 samples / demo scene 工作流

如果你是在对照旧版 README 寻找某个特性，建议先检查当前源码、`Documentation/` 和 `Tests/Editor/` 是否真的存在对应实现。

## 包结构速览

- `Runtime/RenderPipeline` — 管线资产、全局设置、Volume 组件
- `Runtime/RenderGraph` — 运行时图数据、资源描述符、History/Preview、Pass Recorder、帧上下文系统
- `Runtime/RenderGraph/FrameContext` — 逐帧上下文数据（Camera / Light / Shadow / Sky / Temporal / ClusteredLighting / RayTracing / VirtualTexture）
- `Runtime/RenderPass/Core` — 核心渲染 Pass（PreDepth / GBuffer / DeferredLighting / MotionVector / Shadow / Sky / PostProcessing / GPUDriven 等）
- `Runtime/RenderPass/Debug` — 调试 Pass（Cluster / Tile / Exposure / RTAS / VirtualTexture）
- `Runtime/RenderPass/Example` — 示例 Pass
- `Runtime/SubSystem/GPUDriven` — GPU Driven、Meshlet、Bindless、Native 集成
- `Runtime/ComponentData` — 自定义组件数据（VividAdditionalCameraData / VividAdditionalLightData）
- `Runtime/Utility` — 运行时工具集（PipelineResource / BoundProxy / Camera Utility / BlueNoise / HaltonJitter）
- `Runtime/Extension/CoreRP` — Core RP 扩展（UnsafeGraphContext 扩展、ObjectDispatcherService）
- `Editor/RenderGraph` — Graph Toolkit 编辑器、验证、导入、编译、节点注册生成
- `Editor/PipelineResource` — 资源回收与同步工具
- `Editor/ComponentEditor` — 相机/灯光自定义 Inspector
- `Editor/Material` / `Editor/Shader` — 材质 Shader GUI、URP Lit 材质转换、编辑器 Shader
- `Editor/VolumeEditor` — Volume 组件自定义 Inspector
- `Shaders` — 包内 shader（StandardLit / SimpleLit / SimpleForward / DeferredLit / 编辑器 Shader）
- `Documentation` — 当前工作流和子系统文档
- `Tests/Editor` — EditMode 测试

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
