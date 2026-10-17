# VividRP 文档索引 (`Documentation~`)

返回主文档：[中文 README](../README.md) | [English README](../README_EN.md)

本目录汇集了 VividRP 的核心架构设计、子系统规范、材质合约、算法验证报告以及工具使用说明。可按以下主题快速定位所需文档。

---

## 推荐阅读路径

1. **初次接触 VividRP**：先阅读 [VividRP Wizard](VividRPWizard.md) 与 [Bindless 插件设置](Bindless.md) 完成环境配置，再通过 [RenderGraph 编辑器](RenderGraphEditor.md) 了解 `.vrdg` 渲染图的搭建方式。
2. **编写自定义 RenderPass**：阅读 [RenderGraph 编辑器](RenderGraphEditor.md)、[RenderGraph 可序列化资源描述符](RenderGraphResourceDescriptors.md) 与 [RenderGraph SubSystem](RenderGraphSubSystem.md)。
3. **深入 GPU Driven 与场景管线**：依次阅读 [PrimitiveScene V1](PrimitiveSceneV1.md) → [保守实例视锥裁剪](GPUDrivenInstanceCulling.md) → [Cluster LOD 生成与选择](GPUDrivenClusterLOD.md) → [保守 Meshlet 裁剪](GPUDrivenMeshletCulling.md)。
4. **材质与着色开发**：参阅 [SimpleSlab V1 冻结合约](Material/SimpleSlabV1.md) 与 [OpenPBROpaque V1 默认材质合约](Material/OpenPBROpaqueV1.md)。
5. **虚拟阴影图（VSM）开发與调优**：阅读 [VirtualShadowMap 代码布局](VirtualShadowMapCodeLayout.md) 与 [VSM Shader 与 Unreal 参考关系](VirtualShadowMapShaderReference.md)，并结合 `SMRT*` 系列文档分析成本与质量。

---

## 分类文档目录

### 1. RenderGraph 与核心框架

| 文档 | 内容概要 |
| --- | --- |
| [RenderGraphEditor.md](RenderGraphEditor.md) | 基于 Graph Toolkit 的 `.vrdg` 图资产编辑、节点类型、Pass 自持资源（`PassOwnedOverrideable`）、临时资源（`[TransientResource]`）、History 导入与运行时执行流程 |
| [RenderGraphSubSystem.md](RenderGraphSubSystem.md) | 基于 `Local Subgraph` 的 `SubSystem` 折叠机制、显式边界资源类型、导入期扁平化展开规则与校验约束 |
| [RenderGraphResourceDescriptors.md](RenderGraphResourceDescriptors.md) | `RenderGraphTextureDesc`、`RenderGraphBufferDesc`、`RenderGraphAccelerationStructureDesc` 可序列化描述符体系，以及零 GPU 开销的 `[PassBypass]` 旁路机制 |
| [AccelerationStructureSupport.md](AccelerationStructureSupport.md) | 光线追踪加速结构（RTAS）在 RenderGraph 中的声明式配置、`RTASBuildPass` 及下游 Shader 绑定示例 |
| [PerObjectBuffer.md](PerObjectBuffer.md) | 不依赖 `MaterialPropertyBlock` 的逐 Renderer SSBO 数据系统、代码布局声明、集中生成 HLSL 接口、内置颜色示例与 CPU 基准测试 |

### 2. 材质与着色合约 (`Material/`)

| 文档 | 内容概要 |
| --- | --- |
| [Material/SimpleSlabV1.md](Material/SimpleSlabV1.md) | 第一代默认实时 BSDF（`SimpleSlabV1`）冻结合约：版本指纹、单层/双层 Slab 输入、Kulla-Conty 能量补偿、LUT 规范与延迟光照集成 |
| [Material/OpenPBROpaqueV1.md](Material/OpenPBROpaqueV1.md) | 下一代默认 PBR（`OpenPBROpaqueV1`）冻结合约：OpenPBR 1.1 原生 11 项输入、能力边界、校验规则、Prepare/Eval/Sample 语义与原生延迟光照闭环 |
| [LWGUI.md](LWGUI.md) | 内嵌 LWGUI 版本说明、相对路径图标适配，以及面向 D3D12 Shader Model 6 DXIL 的 DXC 静态性能分析后端 |

### 3. GPU Driven、地形与场景表示

| 文档 | 内容概要 |
| --- | --- |
| [GPUDrivenClusterLOD.md](GPUDrivenClusterLOD.md) | 基于 meshoptimizer v1.1 的 Cluster LOD 构建、边界锁与属性权重、16B 共享组表、保守运行时 LOD 选择、主视图层级遍历与八面体/16-bit 顶点量化 |
| [GPUDrivenInstanceCulling.md](GPUDrivenInstanceCulling.md) | `GPUInstanceCulling.compute` 仿射包围盒（Affine Box）平面支撑测试、严格原生 FP16 算术误差界与 41,024 用例 D3D12 验证 |
| [GPUDrivenMeshletCulling.md](GPUDrivenMeshletCulling.md) | `GPUMeshletCulling.compute` 视锥支撑测试、局部空间法线锥裁剪、Reversed-Z 双阶段 HZB 遮挡剔除与 24,640 用例 D3D12 验证 |
| [PrimitiveSceneV1.md](PrimitiveSceneV1.md) | `PrimitiveScene` V1：带 `SceneToken` 的 Author Handle、稀疏/密集双表维护、每相机 Burst 粗剔除与 8 桶 `VividPrimitiveDrawSet` 生产接入 |
| [PrimitiveScenePrototype.md](PrimitiveScenePrototype.md) | `VividPrimitiveScene` V0 原型：Renderer 粒度稳定身份、逻辑句柄生命周期、DrawSection 连续区间分配与 7 张 GPU 缓冲区增量上传设计 |
| [VividTerrainBaking.md](VividTerrainBaking.md) | `VividTerrain` 转换工作流：Unity Terrain 无损复制、高度图/洞采样、8 层材质与 Control Map VT 绑定、锁边界多级 Meshlet LOD 与 LZ4 压缩存储 |
| [GPUDrivenMainTraversalAB.md](GPUDrivenMainTraversalAB.md) | 实测 A/B 报告：Sponza 移动屋顶路线下扁平遍历 vs. 两阶段层级遍历的几何等价性、时域稳定性与 GPU 计时分析 |
| [GPUDrivenGenerationQualityAB.md](GPUDrivenGenerationQualityAB.md) | 实测 A/B 报告：旧版构建器 vs. 紧凑球界 + 法线/UV 权重构建器在渲染法线、Base Color、UV RMSE 与 GPU 耗时上的对比 |

### 4. 虚拟阴影图（VSM）与 SMRT

| 文档 | 内容概要 |
| --- | --- |
| [VirtualShadowMapCodeLayout.md](VirtualShadowMapCodeLayout.md) | VSM Runtime 子系统与 `Shaders/Core` Public/Private 目录职责、独立 `VSMShadowPass`、布局重映射、当帧请求与常驻状态分离、动态缓存与工作列表补绘顺序 |
| [VirtualShadowMapShaderReference.md](VirtualShadowMapShaderReference.md) | VividRP VSM Shader 与 Unreal Engine 5.7.4 `ReferenceVSM`（37 个文件）的主链路对照、函数级映射表与差异说明 |
| [SMRTAdaptiveRays.md](SMRTAdaptiveRays.md) | `VSMShadowResolveAdaptive` 生产自适应射线退出策略：首条射线全亮退出、双射线全暗退出、缺页投票保护与历史失效规则 |
| [SMRTReceiverPreparation.md](SMRTReceiverPreparation.md) | SMRT 接收面法线、投影参数、随机相位与起始纹素坐标在射线循环外的准备与跨层复用 |
| [SMRTCostDiagnostics.md](SMRTCostDiagnostics.md) | `smrt-cost` 诊断工具用法、32 项逐像素源码级工作量计数器、R16_SFloat 重放一致性校验与性能测量注意事项 |

### 5. 光线追踪、路径追踪与毛发

| 文档 | 内容概要 |
| --- | --- |
| [HairPathTracing.md](HairPathTracing.md) | 基于 RTXCR Chiang 近场 BCSDF 的 `VividRP/Material/Hair` 光追管线、DOTS 12 顶点线段网格契约、`HairDotsGpuStream` 持久 GPU 更新与 DLSS-RR 运动向量引导 |
| [NVAPIShaderExecutionReordering.md](NVAPIShaderExecutionReordering.md) | 参考路径追踪中的 NVIDIA SER 硬件线程重排集成、`g_NvidiaExt` UAV 绑定与 `PluginSource~/NVAPINative~` 源码说明 |
| [RTXTextureFiltering.md](RTXTextureFiltering.md) | 参考路径追踪中的 NVIDIA RTXTF 随机纹理过滤（Linear / Cubic / Gaussian）作用范围与收敛重置规则 |

### 6. 虚拟纹理、天空与后处理

| 文档 | 内容概要 |
| --- | --- |
| [VirtualTextureCoreArchitecture.md](VirtualTextureCoreArchitecture.md) | 虚拟纹理核心（VT Core）架构：`VirtualTextureSystem`、Producer 注册、Allocation、Page-Table Space、Stack/Layer、Physical Pool、Feedback 与每帧绑定 |
| [AmbientOcclusion.md](AmbientOcclusion.md) | `Ambient Occlusion` Volume 组件：XeGTAO 与 FidelityFX CACAO 双后端切换、质量档位（0–4 自适应重要性采样）与瞬态资源说明 |
| [FFTBloom.md](FFTBloom.md) | `BloomPass` 的 `Convolution FFT` 模式：2 的幂频域构造、Wave32/Wave64 与 LDS 兼容路径、频谱核缓存与性能边界 |
| [LocalExposure.md](LocalExposure.md) | 基于双边网格（64×64 tile × 32 亮度切片）的 `LocalExposurePass` 后处理管线顺序、作用范围与 Volume 参数 |
| [SkySystemRoadmap.md](SkySystemRoadmap.md) | 天空系统现状、GPU-only 漫反射/镜面反射 IBL 卷积、对照 HDRP 的关键差距及分阶段演进路线 |
| [PhysicallyBasedSkyHdrpGapInventory.md](PhysicallyBasedSkyHdrpGapInventory.md) | Physically Based Sky 相对 HDRP 17.5 的现存兼容层、预计算缓存差异、天体数据源与待收口风险信号清单 |

### 7. 环境配置、调试工具与升级记录

| 文档 | 内容概要 |
| --- | --- |
| [VividRPWizard.md](VividRPWizard.md) | **Window > Rendering > VividRP Wizard** 向导：Windows Standalone Direct3D 12 首选 API 与 Build Profile DXC 配置 |
| [Bindless.md](Bindless.md) | `UnityBindless.dll` 早加载机制说明（`UUM-134389`）、`BindlessNative~` 载荷与 `Setup-Bindless.ps1` 安装流程 |
| [Unity7000a9Upgrade.md](Unity7000a9Upgrade.md) | Unity `7000.0.0a9`（`69e891f60f9a`）包基线升级、内嵌 CoreRP 同步、DXC 独立编译与 18 个 GPU 结构体偏移反射验证记录 |
| [AgenticPixM3Acceptance.md](AgenticPixM3Acceptance.md) | Agentic PIX M3 端到端捕获、多队列校验、DOS 8.3 路径修复、异步 CommandBuffer 标志恢复与验收边界记录 |
| [../Editor/AgenticDebugger/README.md](../Editor/AgenticDebugger/README.md) | AgenticDebugger（Frame Debugger / [PIX](../Editor/AgenticDebugger/PIX.md) / [GC Profiler](../Editor/AgenticDebugger/GCProfiler.md)）CLI 与 C# 自动化诊断接口说明 |
| [../SourceGenerators~/README.md](../SourceGenerators~/README.md) | `VividRP.RenderPassNodeGenerator` Roslyn 增量生成器测试与构建部署说明 |
