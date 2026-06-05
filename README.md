# VividRP

[English](README_EN.md) | 简体中文

VividRP 是一个面向 Unity 6 的自定义 Scriptable Render Pipeline 包。它延续了 legacy [VividRP](https://github.com/af8a2a/VividRP) 的方向，但当前代码库已经不再是旧版 README 那份"大而全功能清单"的直接同步版本。现阶段的重点是先把一个可运行、可扩展、可测试的 RenderGraph-first 渲染管线基础搭起来，再逐步把 GPU Driven、Bindless、Ray Tracing 等能力接回到统一工作流里。

> [!IMPORTANT]
> - 当前开发目标为 Unity `6000.6.0a5` / `6000.6`
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

- 使用 `Assets/Create/VividRP/Standard Render Graph` 创建基于内置模板的可运行 `.vrdg`，或用 `Assets/Create/VividRP/Render Graph` 创建空图
- `ScriptedImporter` 在导入时把 `.vrdg` 编译为运行时 `RenderGraphData`
- Pass 端口由 `[RenderGraphResource]` 字段反射生成
- 三种 Pass 基类：`RasterPass`（光栅化渲染）、`ComputePass`（Compute Shader 调度）、`UnsafePass`（原生 CommandBuffer 访问）
- 资源绑定模式：`External`（图外部连接）、`PassOwnedOverrideable`（Pass 创建默认值，可被覆盖）；Pass 内部临时资源使用 `[TransientResource]`
- 当前支持 `Texture`、`Buffer`、`RenderList`、`History`、`Preview`、`Acceleration Structure` 节点
- 支持基于 Graph Toolkit Local Subgraph 的 `SubSystem`
- 运行时包含 history、preview、frame context 和资源绑定管理
- 支持 `[Range]` float 参数和 enum 参数反射，可在图上直接配置
- `IAsyncComputeSupportedPass` 标记接口支持异步计算
- `IDynamicPassResourceLayout` 接口支持运行时动态资源布局；`IStablePassResourceLayout` 子接口用于字段固定但描述符实例可变的 Pass
- `IAllowGlobalStateModificationPass` 标记全局状态修改类 Pass
- `IBlueNoiseConsumerPass` 标记采样共享蓝噪声资源的 Pass
- `IRenderGraphSideEffectPass` 标记具有跨帧副作用的 Pass（历史更新、回读、导入资源更新等）
- `IRenderGraphPreparePass` 提供在所有 Pass 完成 Prepare() 后刷新资源的钩子
- `IRenderGraphRecordingPass` 用于抗锯齿类 Pass 的图录制回调
- `IRenderGizmoPrePostProcessBoundaryPass` 标记后处理中的 Gizmo 渲染边界

### 核心渲染与调试

**基础 Pass：**
- `PreDepthPass` — 不透明物体预深度（"VividPreDepth" shader tag）
- `GBufferPass` — 延迟 GBuffer 生成（GBuffer0~4，支持 Albedo/Normal/Material/Emission/BakedGI）
- `CopyDepthPass` — 深度拷贝到 R32_SFloat 颜色纹理
- `GenerateViewZPass` — 从深度图生成线性视空间深度
- `DrawObjectPass` — 通用物体绘制，支持最多 8 个颜色目标和动态重配置
- `MotionVectorPass` — 物体和相机运动矢量渲染
- `HZBGeneratePass` — SPD 分层深度缓冲（HZB），支持原子计数
- `HDRPHZBPass` — HDRP 兼容的 HZB 生成 Pass，带稳定资源布局
- `AntialiasingPass` — 统一抗锯齿 Pass，根据 `VividAdditionalCameraData` 配置自动路由到 CMAA2、TAA、FSR3、TSR 或 DLSS
  - `CMAA2Pass` — 保守形态抗锯齿（Compute Pass）
  - `TemporalAAPass` — 时域抗锯齿（Compute Pass）
  - `TSRUpscalerPass` — Unreal Engine TSR 超分辨率
  - `FSR3UpscalerPass` — AMD FidelityFX Super Resolution 3
  - `DLSSPass` — NVIDIA DLSS Super Resolution / Ray Reconstruction

**阴影系统：**
- `CSMShadowPass` — Cascade Shadow Map 渲染（2x2 Atlas，最多 4 级 Cascade）
- `MeshletShadowPass` — GPU Driven Meshlet 阴影投射（Visibility Buffer Shadow Caster）
- `CSMShadowResolvePass` — CSM 阴影解析，PCSS 柔和阴影（Percentage Closer Soft Shadows），支持屏幕空间阴影 Tile 分类与 Bend SSS 参数控制
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

**反射探针系统：**
- `VividReflectionProbeAtlasSystem` — 反射探针图集系统（VividSubsystem），将探针 Cubemap 打包到共享纹理图集中，支持 LRU 淘汰、GGX BSDF 卷积与 HDR 解码
- `VividAdditionalReflectionData` — 反射探针附加数据，支持 HDRP 风格的 Proxy Volume 模式（Infinite / InfluenceVolume / Box）、Blend Distance、Side Fade、Multiplier、Weight 与 Importance
- `VividReflectionProbeAtlasSettings` — 图集配置（分辨率 / 格式 / 最大探针数 / Mip 级别）
- 反射探针剔除已集成到 `LightGridPass` 的簇状光照剔除管线中

**ReGIR（Reservoir-based Global Illumination Ray Tracing）：**
- `ReGIRGridBuildPass` — ReGIR 空间网格构建（Compute Pass），管理 Reservoir 缓冲区
- `ReGIRDebugPass` — ReGIR 调试可视化（Cells / ReservoirOccupancy / ReservoirWeight 模式）
- `VividReGIRData` — ReGIR 逐帧上下文数据

**体积雾系统：**
- `VolumetricDensityPass` — 体积雾密度累积（UnsafePass，支持全局状态修改）
- `VolumetricLightingPass` — 体积雾光照计算（Compute Pass，支持 Async Compute）
- `VolumetricMaxZPass` — VBuffer 分辨率下的最大深度 Pass（Compute Pass，支持 Async Compute）
- `VividVolumetricFogVolume` — 全局体积雾 Volume 组件
- `VividLocalVolumetricFog` — 局部体积雾组件（含管理器与体素化 Shader）
- 已同步 HDRP 体积光照内核、各向异性支持、投影与历史处理

**后处理：**
- `AutoExposurePass` — 自动曝光（支持 Unreal 和 HDRP 两种实现路径）
- `BloomPass` — 泛光效果
- `ColorGradingPass` — 颜色分级（Channel Mixer / Color Adjustments / Curves / LiftGammaGain / ShadowsMidtonesHighlights / Split Toning / Tonemapping / WhiteBalance）
- `DepthOfFieldPass` — 景深
- `DiffusionPass` — 屏幕空间扩散/模糊
- `GTAOPass` — Ground Truth Ambient Occlusion
- `DataDrivenLensFlarePass` / `ScreenSpaceLensFlarePass` — 镜头光晕
- `FinalBlitPass` — 最终 Blit 到相机 Backbuffer（含 Color Grading LUT、自动曝光、Film Grain、Bloom 合成）
- `StopNaNPass` — 消除颜色缓冲中的 NaN/Inf 像素（Raster Pass，旁路注入已显式化）
- `ScreenSpaceReflectionPass` — 屏幕空间反射，支持 Vivid / HDRP / Hybrid / RayTracing 四种执行路径，内置 ReBlur 降噪器与时域累积
- `LocalExposurePass` — 局部曝光（Bilateral Grid 64×64×32），含双边网格构建、对数亮度、可分离模糊与最终应用
- `ColorPyramidPass` — 颜色金字塔（Mip Chain）生成（Compute Pass，带历史追踪与 Side Effect 标记）
- `Vignette` — 渐晕效果
- `FilmGrain`、`Tonemapping`（ACES / Neutral / Custom）等 Volume 组件

**超分辨率 / Upscaling：**
- `AntialiasingPass` 统一入口，根据相机配置自动选择：
  - **TSR**（Temporal Super Resolution）— 基于 Unreal Engine TSR 的时域超分辨率，支持空间抗锯齿、速度膨胀与深度感知边缘拒绝、历史钳制与迟滞、薄几何覆盖检测、亮度不稳定性预处理、持久历史复活、可选 Wave Ops 与 16-bit VALU 加速
  - **FSR3**（AMD FidelityFX Super Resolution 3）— 包含 Reactivity 预处理、Shading Change 检测、Luma Instability、Luma Pyramid、RCAS 锐化
  - **DLSS**（NVIDIA Deep Learning Super Sampling）— Super Resolution 与 Ray Reconstruction（需 `DLSS_PLUGIN_INTEGRATE` 脚本定义符号）

**Decal 系统：**
- `DecalSystem` — 基于 DBuffer 的 Decal 渲染系统
- `DecalProjector` — Decal 投影器组件，支持逐 Decal 的 Base Color、Opacity、Metallic、Roughness 属性
- HDRP 兼容的 Decal 投影与采样
- GPU Driven Decal GBuffer 绑定

**调试 Pass：**
- `ClusterDebugPass` — 簇状光照可视化（Opaque / Slice 模式）
- `TileDebugPass` — Tile 级别调试
- `ExposureDebugPass` — 自动曝光计量区可视化
- `SliderDebugPass` — 调试滑动条
- `RTASInstanceDebugPass` — RTAS 实例可视化
- `OverlayDebugPass` — 调试叠加层
- `VisibilityBufferDebugPass` — GPU Driven 可见性缓冲调试（Raster Pass）
- `VirtualTextureDemoPass` — Virtual Texture 演示（Residency / MipBias / PhysicalPageId 模式）
- `VirtualTextureVisualizationPass` — Virtual Texture 可视化
- `MaterialDebugPass` — GBuffer 材质通道可视化（24 种模式：BaseColor / Normal / Roughness / Metallic / AO / SpecularOcclusion / Coat / MaterialFeatures / Emissive / BakedGI / Depth 等）
- `ReflectionProbeAtlasDebugPass` — 反射探针图集调试（Atlas / Slot 模式，可调 Mip / Slice / Exposure）
- `ReGIRDebugPass` — ReGIR Reservoir 调试可视化（Cells / ReservoirOccupancy / ReservoirWeight）

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

### 子系统框架（VividSubsystem）

- 基于 CRTP 的 `VividSubsystem<TSelf>` 泛型单例基类，统一管理子系统生命周期
- 自动订阅 `FrameContextSystem.SubsystemPreRender`，每帧通过 `OnUpdate(ContextContainer, CommandBuffer)` 调度
- `Initialize()` / `Deinitialize()` 管理订阅与 `AssemblyReloadEvents` 钩子
- 所有主要子系统均以 VividSubsystem 单例形式运行：
  - `DecalSystem` — DBuffer Decal 系统
  - `VirtualTextureSystem` — Virtual Texture 系统（地址空间、页表更新、反馈读取、驻留管理）
  - `LTCAreaLightSystem` — LTC 面光源系统（Charlie / CookTorrance / Disney / GGX / KajiyaKaySpecular / OrenNayar / Ward BRDF）
  - `SkyManager` — 天空管理器（按相机类型条件更新全局天空环境，缓存天空数据）
  - `VividAutoExposureSystem` — 自动曝光子系统（预渲染订阅、帧上下文清理时的资源释放）
  - `VividGPUDrivenSystem` — GPU Driven 子系统
  - `VividSceneLightSystem` — 场景灯光系统（PlayerLoop PreLateUpdate 阶段构建灯光快照，调度 `VividLightRenderDatabase` 数据准备）
  - `VividReflectionProbeAtlasSystem` — 反射探针图集系统（探针注册/淘汰、图集打包、GGX 卷积调度）

### LTC 面光源

- `LTCAreaLightSystem` — 基于 Linearly Transformed Cosines 的面光源系统
- 支持 Charlie、CookTorrance、Disney、GGX、KajiyaKaySpecular、OrenNayar、Ward 等 BRDF 模型
- 面光源数据与 `VividLightRenderDatabase` 集成（Barn Door、Shape Radius 等参数）

### Virtual Texture

- `VirtualTextureSystem` — 完整的 Virtual Texture 运行时系统
- 地址空间管理（`VTAddressSpace`）、页表更新（`VTPageTableUpdater`）、反馈读取（`VirtualTextureFeedback`）
- Producer 与 Request 管线、驻留管理（`VTResidencyManager`）
- `VTPhysicalPool` — 共享物理纹理页池，支持跨地址空间共享、页分配/淘汰与统计追踪
- Streamed Virtual Texture（SVT）— 流式虚拟纹理 Producer，已集成到 `StandardLayeredLit` 材质中
- `VirtualTextureDemoPass` / `VirtualTextureVisualizationPass` 调试可视化

### 工具

- `ColorChecker Tool` — 颜色校准参考图表组件（`Rendering/VividRP/Color Checker Tool`），支持 Color Palette / Cross Polarized Grayscale / Middle Gray / Reflection / Stepped Luminance / Material Palette / External Texture 七种模式，含网格叠加、梯度可视化、球面模式与 Unlit 对比选项

### 原生插件绑定

- `BindlessPluginBindings` — UnityBindless.dll 原生绑定
- `METISBindings` — METIS 库原生绑定
- `MeshOptimizerBindings` — Mesh Optimizer 原生绑定
- `NVAPI` 与 `NeuralRadianceCache` 目录 — NVIDIA API 与神经辐射缓存集成

### Ray Tracing

- `RayTracingSettingsVolume` — Ray Tracing 配置（Build Mode / Culling Mode / Ray Bias / Build Flags 等）
- 已包含可序列化的 `RenderGraphAccelerationStructureDesc`
- 编辑器中可直接 author Acceleration Structure resource node
- `RTASBuildPass` — 构建场景 Ray Tracing Acceleration Structure，支持 Shader Tag Fallback、双面/AlphaTest/透明关键词、RTAS Instance Batching、Frustum/Sphere/SolidAngle Culling
- 已实现 directional ray traced shadow 与 SIGMA shadow denoise pass
- 包内已包含 SIGMA shadow denoise 相关 shader 资源

### 材质、Shader 与后处理

- 当前包内提供 `StandardLit`、`SimpleLit`、`SimpleForward`、`StandardLayeredLit` 等材质 shader
- 已包含 URP Lit 材质转换（`URPLitMaterialConverter`）与 HDRP 材质转换（`HDRPMaterialConverter`，支持 HDRP/Lit、HDRP/LitTessellation、HDRP/Unlit → VividRP StandardLit）
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
  - Editor-only：Export Final Frame Screenshot 按钮（GameView 分辨率读回并保存为 PNG）
- `VividAdditionalLightData` — 灯光附加数据
  - Light Render Database (`VividLightRenderDatabase`) 全局灯光追踪
  - Shadow Settings（CSM 质量、Ray Traced Shadow、PCSS 参数、Bend SSS）
  - HDRP 风格 Spot Shape 切换（Cone / Box / Pyramid），Box Spot 可编辑 Gizmo
  - Punctual Light Radius 暴露到 Inspector
  - Light category flags、cast shadows、rendering layer masks
  - Time of Day 控制（`m_EnableTimeOfDay` / `m_TimeOfDay` 0–24 小时制）
    - `EvaluateTimeOfDaySun()` — 基于正弦仰角、大气消光与地平线渐隐的太阳位置计算
    - `EvaluateTimeOfDayLux()` — 大气质量衰减与平滑地平线过渡
    - `ApplyTimeOfDayToLight()` — 自动设置方向光旋转与 Lux 强度
    - 支持 `m_TimeOfDayMaximumLux` 配置最大照度

**Volume 组件（运行时）：**

| 类别 | Volume 组件 |
|---|---|
| **阴影** | `CascadedShadowSettingsVolume`（CSM 级联数/距离/分割比/偏移）、`RayTracingSettingsVolume`（DXR 构建/剔除/偏置）、`GPUDrivenSettingsVolume`（LOD 深度/误差阈值） |
| **天空** | `HDRISky`、`PhysicallyBasedSky`、`SkySettings` |
| **体积雾** | `VividVolumetricFogVolume`（雾密度/各向异性/光照参数） |
| **曝光** | `AutoExposure`（测光模式/HDRP 与 Unreal 实现/直方图/适应速度）、`LocalExposure`（局部曝光） |
| **后处理** | `Bloom`、`ColorAdjustments`、`ColorCurves`、`ChannelMixer`、`LiftGammaGain`、`ShadowsMidtonesHighlights`、`SplitToning`、`Tonemapping`、`WhiteBalance`、`DepthOfField`、`Diffusion`、`GTAO`、`FilmGrain`、`ScreenSpaceLensFlare`、`ScreenSpaceReflection`（含 HDRP 对比路径）、`Vignette` |

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
- `VividAntialiasingData` — 抗锯齿状态（模式选择、渲染/输出分辨率、时域 Jitter 与历史重置）
- `VividVolumetricData` — 体积雾帧状态（VBuffer 纹理、局部雾 Buffer、Shader 变量）
- `VividColorPyramidData` — 颜色金字塔 Mip Chain 与历史追踪
- `VividScreenSpaceReflectionData` — SSR 反射纹理追踪与有效性标记
- `FrameContextSystem` — CameraRelative 系统，驱动时序状态推进 / Shader Globals 设置 / Subsystem 调度

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
- `StandardLitShaderGUI`（LWGUI 框架）、`URPLitMaterialConverter` 与 `HDRPMaterialConverter` — 材质编辑器、URP Lit 材质转换与 HDRP 材质转换
- Volume 组件自定义 Inspector（CascadedShadow / RayTracing / GPUDriven / Sky / AutoExposure / ColorGrading / Diffusion / ScreenSpaceLensFlare）
- `BoundProxy` 编辑器工具 — 代理体形编辑与管理
- `Tests/Editor` 已覆盖 RenderGraph、Pass、Drawer、GPU Driven、Ray Tracing Volume、材质导入、组件数据、FrameContext、Shadow Data、SubSystem 等大量 EditMode 场景

## 快速开始

1. 使用 Unity `6000.5.0a7` 或兼容的 `6000.5` 打开包含该包的 Unity 项目根目录
2. 通过 `Assets/Create/VividRP/Vivid Render Pipeline` 创建 `VividRenderPipelineAsset`
3. 在 `Project Settings > Graphics` 中把该资产设为当前 SRP
4. 通过 `Assets/Create/VividRP/Standard Render Graph` 创建基于内置模板的 `.vrdg`；如果需要从零开始，也可以使用 `Assets/Create/VividRP/Render Graph`
5. 双击 `.vrdg` 打开 RenderGraph 编辑器，按项目需要调整资源节点和 Pass 节点
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

- legacy README 里的大规模 GI / Path Tracing 系列能力
- `Terrain`、`Foliage` 等子系统的完整实现
- 生产可用的 samples / demo scene 工作流

> 以下能力已在当前包中实质落地，不再列入"未恢复"清单：SSR（含 ReBlur 降噪器与时域累积）、Volumetric Fog（密度/光照/MaxZ 三 Pass + Local Volumetric Fog 组件）、DLSS SR/RR、LTC 面光源、Virtual Texture 完整系统（含 SVT 与物理页池）、Reflection Probe Atlas（含 HDRP 风格 Proxy/Fade 字段与图集调试）、ReGIR（Grid Build Pass + 调试可视化）。

如果你是在对照旧版 README 寻找某个特性，建议先检查当前源码、`Documentation/` 和 `Tests/Editor/` 是否真的存在对应实现。

## 包结构速览

- `Runtime/RenderPipeline` — 管线资产、全局设置、Volume 组件
- `Runtime/RenderGraph` — 运行时图数据、资源描述符、History/Preview、Pass Recorder、帧上下文系统
- `Runtime/RenderGraph/FrameContext` — 逐帧上下文数据（Camera / Light / Shadow / Sky / Temporal / ClusteredLighting / RayTracing / VirtualTexture）
- `Runtime/RenderPass/Core` — 核心渲染 Pass（PreDepth / GBuffer / DeferredLighting / MotionVector / Shadow / Sky / PostProcessing / GPUDriven / Volumetric 等）
- `Runtime/RenderPass/Debug` — 调试 Pass（Cluster / Tile / Exposure / RTAS / VirtualTexture / VisibilityBuffer）
- `Runtime/RenderPass/Example` — 示例 Pass（FullScreen / ImportTexture）
- `Runtime/SubSystem/GPUDriven` — GPU Driven、Meshlet、Bindless、Native 集成
- `Runtime/SubSystem/DLSS` — NVIDIA DLSS Super Resolution 与 Ray Reconstruction
- `Runtime/SubSystem/Decal` — DBuffer Decal 系统（DecalProjector / DecalData / DecalSystem）
- `Runtime/SubSystem/AreaLight` — LTC 面光源系统（多 BRDF 支持）
- `Runtime/SubSystem/Volumetric` — 体积雾系统（全局/局部雾、VBuffer、体素化）
- `Runtime/SubSystem/VirtualTexture` — Virtual Texture 系统（地址空间/页表/驻留管理）
- `Runtime/SubSystem/Sky` — 天空系统（HDRI / PhysicallyBased / SkyManager）
- `Runtime/SubSystem/Reflection` — 反射探针图集系统（Atlas / Probe Cache / Settings）
- `Runtime/SubSystem/Plugin` — 原生插件绑定（Bindless / METIS / MeshOptimizer / NVAPI / NeuralRadianceCache）
- `Runtime/SubSystem/Lighting` — 场景灯光系统（VividSceneLightSystem）
- `Runtime/ComponentData` — 自定义组件数据（VividAdditionalCameraData / VividAdditionalLightData / VividAdditionalReflectionData）
- `Runtime/Utility` — 运行时工具集（PipelineResource / BoundProxy / Camera Utility / BlueNoise / HaltonJitter）
- `Runtime/Tools` — 运行时工具（ColorChecker）
- `Runtime/Extension/CoreRP` — Core RP 扩展（UnsafeGraphContext 扩展、ObjectDispatcherService）
- `Editor/RenderGraph` — Graph Toolkit 编辑器、验证、导入、编译、节点注册生成
- `Editor/PipelineResource` — 资源回收与同步工具
- `Editor/ComponentEditor` — 相机/灯光自定义 Inspector
- `Editor/Material` / `Editor/Shader` — 材质 Shader GUI、URP Lit 材质转换、编辑器 Shader
- `Editor/VolumeEditor` — Volume 组件自定义 Inspector
- `Editor/Tools` — 编辑器工具（ColorChecker Editor）
- `Shaders` — 包内 shader（StandardLit / SimpleLit / SimpleForward / StandardLayeredLit / DeferredLit / 编辑器 Shader）
- `Documentation` — 当前工作流和子系统文档
- `Tests/Editor` — EditMode 测试

## 文档

- [RenderGraph Editor](Documentation~/RenderGraphEditor.md)
- [RenderGraph SubSystem](Documentation~/RenderGraphSubSystem.md)
- [RenderGraph Resource Descriptors](Documentation~/RenderGraphResourceDescriptors.md)
- [Acceleration Structure Support](Documentation~/AccelerationStructureSupport.md)
- [Bindless Setup](Documentation~/Bindless.md)
- [LWGUI Notes](Documentation~/LWGUI.md)
- [Physically Based Sky HDRP Gap Inventory](Documentation~/PhysicallyBasedSkyHdrpGapInventory.md)
- [Sky System Roadmap](Documentation~/SkySystemRoadmap.md)
- [Local Exposure](Documentation~/LocalExposure.md)

## 路线图

- [Shadow](Roadmap~/Shadow.md)
- [Sky](Roadmap~/Sky.md)
- [Virtual Texture System](Roadmap~/VirtualTextureSystem.md)
- [Streamed Virtual Texture (SVT)](Roadmap~/SVTRoadmap.md)

