# VividRP

[English](README_EN.md) | 简体中文

VividRP 是一个面向 Unity 6 的实验性 Scriptable Render Pipeline（SRP）包。它以可视化、数据驱动的 RenderGraph 工作流为核心，并在此基础上探索延迟渲染、GPU Driven、Bindless、Ray Tracing、参考路径追踪、体积雾、虚拟纹理和时域超分辨率等高端渲染方向。

> [!IMPORTANT]
> - 当前开发基线为 Unity `6000.7.0a3`（提交 `b08e599c`）。`package.json` 的最低兼容声明仍为 `6000.6.0a3`；使用新功能时请以 `6000.7.0a3` 为准。
> - 当前最可靠的运行环境是 Windows + DirectX 12；Bindless 与 Ray Tracing 流程依赖 DX12 / DXR 能力。
> - VividRP 正在积极开发中。部分子系统是实验性实现，不承诺跨平台、生产就绪或向后兼容。
> - 本仓库不再与旧版 [VividRP](https://github.com/af8a2a/VividRP) 的功能清单一一对应；请以当前源码、本文档和 `Documentation~` 为准。

## 核心工作流

VividRP 的主要内容由 `.vrdg` 图资产描述：

1. 在 Project 窗口中选择 `Assets/Create/VividRP/Standard Render Graph` 创建可运行的模板图，或选择 `Assets/Create/VividRP/Render Graph` 创建空图。
2. 双击 `.vrdg`，在基于 `com.unity.graphtoolkit` 的编辑器中配置 Pass 与资源连接。
3. 导入器会将图编译为运行时的 `RenderGraphData`。
4. 将生成的 `RenderGraphData` 指定给 `VividRenderPipelineAsset` 的 **Render Graph Asset** 字段。

Pass 通过 `[RenderGraphResource]` 标记的字段自动生成端口。运行时使用 `PassRecorder` 调度三类标准 Pass：

- `RasterPass`：光栅化渲染；
- `ComputePass`：Compute Shader 调度；
- `UnsafePass`：需要原生 `CommandBuffer` 访问的场景。

图编辑器支持 Texture、Buffer、Render List、History、Preview 和 Acceleration Structure 资源，以及 Local Subgraph 形式的 SubSystem。资源端口、依赖关系、Pass Culling 和图校验均由编译流程处理。

## 已包含的能力

下列模块已在当前包中提供实现；可用性仍取决于项目配置、平台和硬件能力。

| 领域 | 主要内容 |
| --- | --- |
| 基础渲染 | 预深度、GBuffer、延迟光照、Motion Vector、HZB、通用物体绘制、Color Pyramid |
| 阴影与光照 | CSM / PCSS、簇状光照、方向光 DXR 阴影与 SIGMA 降噪、天空与大气散射 |
| 后处理 | 自动曝光（Unity、HDRP、Unreal 预设 Inspector）、Bloom（Mip Scattering 与 FFT 卷积核模式）、色彩分级、景深、环境光遮蔽（XeGTAO 或 FidelityFX CACAO）、镜头光晕、SSR（含 REBLUR：Checkerboard 交织、时域稳定、Hit-Distance 重建、可配置降噪与分离直接光照）、局部曝光、最终合成 |
| 抗锯齿与超分 | CMAA2、TAA、TSR、FSR3；DLSS Super Resolution / Ray Reconstruction 需额外插件集成 |
| NVIDIA 集成 | NVAPI Shader Execution Reordering（SER） |
| 体积效果 | 全局及局部体积雾、VBuffer、体积光照和 Max-Z 生成 |
| GPU Driven | Meshlet 导入与渲染、Visibility Buffer、对象调度、调试 Overlay、Bindless 描述符支持 |
| Ray Tracing | 可序列化 RTAS 描述符、RTAS 构建、方向光光追阴影与相关调试 Pass |
| 参考路径追踪 | 基于 OpenPBR 的多反弹 DXR 原型，支持随机 RGB 几何透明度、薄壁透射以及带四层介质栈和 Beer–Lambert 吸收的半透明实体折射；另含解析光源与 HDRI 的 MIS / Next-Event Estimation、rectangle / disc 的 BSDF 段命中评估、着色点感知的混合光源选择与空间索引、Rendering Debugger 传输视图、确定性像素捕获、REBLUR 信号路由（有限太阳光照）、NRD REBLUR 预览降噪与 Unity Open Image Denoise 后端 |
| 资源与子系统 | 虚拟纹理（含 SVT）、DBuffer Decal、LTC 面光源、反射探针图集、ReGIR、天空管理 |
| Per-Object Buffer | 不依赖 `MaterialPropertyBlock` 的逐 Renderer Shader 数据、子系统生命周期集成、集中生成的 HLSL 布局、颜色示例与 MPB CPU 对比基准 |
| 实验性粒子 | 基于 ECS 页式存储的粒子模拟、裁剪、排序、Billboard / Mesh / Stretch 渲染、Trail、碰撞和子发射器 |
| 编译与工具 | DXC Shader 性能编译后端、RenderGraph 编辑/导入/编译/校验、节点注册生成、资源描述符 Drawer、EditMode 测试套件 |
| 材质与 PBR | `StandardLit`（含 Metallic / Smoothness / AO 的 PBR Remap Range 支持）、`SimpleLit`、`SimpleForward` 和 `StandardLayeredLit`，并包含 URP Lit 与 HDRP Lit 的材质转换工具 |

相机、灯光、反射探针及多数 Volume 设置均配有 VividRP 的附加组件或自定义 Inspector。

## 快速开始

1. 使用 Unity `6000.7.0a3` 或兼容的 `6000.7` 版本打开项目。
2. 通过 Package Manager 将本包作为嵌入式包或本地包加入项目。
3. 选择 `Assets/Create/VividRP/Vivid Render Pipeline` 创建 `VividRenderPipelineAsset`。
4. 在 **Project Settings > Graphics** 中将该资产指定为当前 Render Pipeline。
5. 创建并编辑一个 `Standard Render Graph`，将其生成的 `RenderGraphData` 赋给管线资产。
6. 如需后处理或全局渲染设置，在管线资产 Inspector 中初始化 **Default Volume**，或在场景中创建 Volume Profile。
7. 如需时域抗锯齿，在相机上添加 `VividAdditionalCameraData` 并选择对应模式。

### 可选：GPU Driven、Bindless 与 Ray Tracing

- 在管线资产 Inspector 中启用 **GPU Driven**。
- Bindless 需要在项目根目录执行下列脚本，然后重启 Unity：

```powershell
powershell -ExecutionPolicy Bypass -File .\Packages\VividRP\Setup-Bindless.ps1
```

- Ray Tracing 需要 DXR 兼容硬件及 DX12。请在 Volume Profile 中添加 `VividRP/Ray Tracing/Settings` 并按项目需求配置。
- DLSS 需要安装 NVIDIA 相关依赖，并定义 `DLSS_PLUGIN_INTEGRATE` 脚本符号。
- NVIDIA Shader Execution Reordering（SER）在参考路径追踪中为 Windows x86_64 + DX12 提供可选加速，需 NVAPI 支持。
- 参考路径追踪使用独立的 `.vrdg` 和管线资产，面向受控场景的验证，不以实时性能或完整 ground truth 为目标。`StandardLit` 将几何覆盖与材质透射分开：随机覆盖由 `Base Color.a × Base Map.a × Opacity Map.r` 决定，`Transmission Weight × Transmission Map.r` 则控制 OpenPBR 透射，两个附加贴图都复用 Base Map UV。透射颜色由 `Transmission Color` 控制；薄片应启用 `Thin-Walled Transmission`，封闭实体应关闭该选项，并可用 `Transmission Depth` 指定该颜色对应的 Beer–Lambert 吸收距离；实体介质最多正确嵌套四层。OIDN 后端仅在 Editor 或 64 位 Standalone 且 `com.unity.rendering.denoising` 可用时启用。

## 目录导览

- `Runtime/RenderPipeline`：管线、管线资产、全局设置与 Volume 集成。
- `Runtime/RenderGraph`：运行时图数据、资源描述符、History / Preview、Pass Recorder 和 Frame Context。
- `Runtime/RenderPass`：Core、Debug 与 Example Pass。
- `Runtime/SubSystem`：GPU Driven、Virtual Texture、Volumetric、Sky、Decal、Reflection、DLSS 等子系统。
- `Runtime/ComponentData`：相机、灯光和反射探针附加数据。
- `Runtime/PerObjectBuffer`：逐 Renderer Shader 数据系统及示例。
- `Runtime/Experiment/Particle`：实验性 ECS 粒子系统。
- `Editor/RenderGraph`：图编辑器、导入器、编译器、校验与节点注册生成。
- `Editor/PipelineResource`：`[PipelineResource]` 的资源回收与同步工具。
- `Shaders`：包内 Shader 及其程序集。
- `Documentation~`：具体工作流、限制和设计说明。
- `Tests/Editor`：EditMode 测试。

## 资源同步与开发提示

- `.vrdg` 是 RenderGraph 的源文件；不要手动维护导入生成的 `RenderGraphData` 内容。
- 不要手动编辑 `Editor/RenderGraph/GeneratedRenderPassNodes.g.cs` 或 `Runtime/Resources/PipelineResources.asset`。前者由节点注册生成器维护，后者应通过资源回收流程更新。
- 修改 `[PipelineResource]` 或 `[ResourcePath]` 声明后，选择 `Runtime/Resources/PipelineResources.asset`，在 Inspector 中执行 **Recollect Engine Resources**。
- RenderGraph Pass 的资源字段名是端口、预览和已编译绑定的契约；变更前请一并检查图资产与测试。

## 文档与路线图

- [RenderGraph 编辑器](Documentation~/RenderGraphEditor.md)
- [RenderGraph SubSystem](Documentation~/RenderGraphSubSystem.md)
- [RenderGraph 资源描述符](Documentation~/RenderGraphResourceDescriptors.md)
- [Acceleration Structure 支持](Documentation~/AccelerationStructureSupport.md)
- [Bindless 设置](Documentation~/Bindless.md)
- [Local Exposure](Documentation~/LocalExposure.md)
- [环境光遮蔽（XeGTAO / FidelityFX CACAO）](Documentation~/AmbientOcclusion.md)
- [FFT 卷积 Bloom](Documentation~/FFTBloom.md)
- [NVIDIA Shader Execution Reordering](Documentation~/NVAPIShaderExecutionReordering.md)
- [Virtual Texture 架构](Documentation~/VirtualTextureCoreArchitecture.md)
- [Per-Object Buffer](Documentation~/PerObjectBuffer.md)
- [路线图：Shadow](Roadmap~/Shadow.md)、[Sky](Roadmap~/Sky.md)、[Virtual Texture](Roadmap~/VirtualTextureSystem.md)、[SVT](Roadmap~/SVTRoadmap.md)、[参考路径追踪](Roadmap~/ReferencePathTracingRoadmap.md)
