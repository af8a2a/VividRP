# VirtualShadowMap 代码布局

当前 Shader 与本地 Unreal `ReferenceVSM` 的函数对应见 [Shader 参考关系](VirtualShadowMapShaderReference.md)；下一步性能工作见 [定向光 VSM 优化路线](../Roadmap~/DirectionalVSMOptimization_20260917.md)。

本次整理于 2026-09-16，保持现有渲染行为、资源 GUID、Pass 类型身份、shader kernel 名称及顺序。

## Runtime 子系统

根目录：`Runtime/SubSystem/VirtualShadowMap/`。内部算法和状态使用命名空间 `VividRP.Runtime.VirtualShadowMap`。

| 文件 | 职责 |
| --- | --- |
| `VirtualShadowMapPrototypeRuntime.cs` | 物理页池、页表、元数据、补页压力、静态/动态缓存与失效资源的生命周期 |
| `VirtualShadowMapPrototypeCacheKey.cs` | 缓存复用条件和输入有效性 |
| `VirtualShadowMapFrameState.cs` | 帧状态、回退原因 |
| `VirtualShadowMapUnityCasterCompatibility.cs` | Unity caster 能力验证、保守 bounds 契约及对象变化跟踪 |
| `VirtualShadowMapProjection.cs` | GPU 投影数据布局与绑定 |
| `VirtualShadowMapClipmapLayout.cs` | Clipmap 布局、移动和页重映射 |
| `VirtualShadowMapReceiverQuality.cs` | 接收面质量参数、SMRT 和补页压力控制参数 |
| `VSMProfiling.cs` | VSM 分阶段计时标记 |
| `RenderPass/VSMDebugPass.cs`、`RenderPass/VSMReceiverDebugPass.cs` | 物理页与接收面调试 Pass |
| `RenderPass/VSMShadowPass.cs` | 独立 VSM 阴影生成：接收面请求、页管理、缓存失效、caster 光栅与状态提交 |

调试 Pass 保留 `VividRP.Runtime.RenderPass.Core` 命名空间，避免改变已有 RenderGraph 的序列化类型身份及生成节点注册。

## Shader

根目录：`Shaders/VirtualShadowMap/`。

| 目录 / 文件 | 职责 |
| --- | --- |
| `Public/VividVirtualShadowMapAddressing.hlsl` | 共享虚拟页寻址 |
| `Public/VividVirtualShadowMapProjection.hlsl` | 投影 ABI 和坐标变换 |
| `Public/VividVirtualShadowMapCaster.hlsl` | Caster 多层深度插入 |
| `Private/VSMPageDefinitions.hlsl` | 页标志、请求优先级及调试统计定义 |
| `Private/VSMPageManagement.hlsl` | Meshlet 页请求、分配、重映射、失效、清页、占用归约 |
| `Private/VSMPhysicalSampling.hlsl` | 页解析、深度层读取、虚拟采样 |
| `Private/VSMReceiverNormal.hlsl` | 接收面法线重建 |
| `Private/VSMReceiverResolve.hlsl` | 接收面偏移、PCF、层间过渡和阴影求值 |
| `Private/VSMReceiverQuality.hlsl` | 接收面覆盖、密度和层级选择 |
| `Private/VSMSMRT.hlsl` | 多层深度 SMRT 遮挡查询和渐进采样；生产自适应使用独立 kernel，现有 Volume 开关控制波级提前退出 |
| `Private/VSMFiltering.hlsl` | 双边滤波、历史重投影和时间积累 |
| `Private/VSMReceiverDebug.hlsl`、`Debug/VSMDebug.shader` | 接收面与页池可视化 |

`Private` 模块依赖共享 compute 入口声明的资源和辅助函数，按入口中的 include 顺序编译。添加 shader 功能时，在这些模块中实现；当前 kernel 声明仍由共享入口统一维护。

## 共用入口与集成点

- `Runtime/SubSystem/VirtualShadowMap/RenderPass/VSMShadowPass.cs` 独立准备和执行 VSM；其生命周期管理 VSM 资源释放。公开 Pass 类型沿用调试 Pass 所在的 `VividRP.Runtime.RenderPass.Core` 命名空间，算法与状态仍属于 VirtualShadowMap 子系统。
- `Runtime/RenderPass/Core/CSMShadowPass.cs` 只绘制传统级联阴影，VSM 当帧成功后跳过绘制。`ShadowCasterPass.cs` 仅共享 Meshlet、材质和虚拟纹理绑定等 caster 基础代码，不持有 VSM 页管理或 CSM 投影算法。
- `Runtime/RenderPass/Core/CSMShadowResolvePass.cs` 保留共用 resolve 调度与屏幕空间历史资源绑定。
- `Shaders/Core/Private/CSMShadowResolve.compute` 保留 CSM 算法、共享资源声明和 31 个 kernel 入口；VSM 算法通过新目录的模块引入。新增 kernel 继续追加，避免热重载期间改变已缓存的索引。
- `CascadedShadowSettingsVolume` 继续承载现有 CSM/VSM 序列化设置。PrimitiveScene、GPUDriven 的 caster 变化记录及失效通知仍属于各自子系统。
- VSM 录制和质量复现工具集中于 `Editor/Tools/VirtualShadowMap/`；共用诊断入口仍在 `Editor/Tools/Diagnostics/`。
- `VividResources.cs` 的调试 shader 路径已更新；`PipelineResources.asset` 由 `PipelineResourceUpdater` 同步，GUID 不变。现有包路径别名使用同一组新相对路径。

## 独立阴影生成 Pass（2026-09-17）

渲染图顺序为 `Depth/GBuffer → VSMShadowPass → CSMShadowPass → CSMShadowResolvePass`。VSM 接收原 Depth/GBuffer1 输入，输出实际导入的 `m_PageTable`；CSM 的只读 `m_VSMPageTable` 连接显式约束执行顺序。页表实际读写依赖由 RenderGraph 声明，不另造 GPU 状态缓冲。

`VividShadowData.virtualShadowMapRendered` 仅在 VSM Record 完成后置为 true，每相机 Update/Reset 清空。准备失败或录制失败时保持 false，由 CSM 绘制图集；不以“VSM 已准备”代替“VSM 已完成”。现有图的迁移从 Resolve 分享接收面输入，重复迁移不增加节点，也不覆盖已连接的 VSM 输入。当前使用图和标准模板已通过 GraphToolkit 保存。

本轮拆分阴影生成与资源生命周期；屏幕空间 Resolve 和 Compute 入口仍共用，Shader、kernel 顺序及 SMRT 隔离不变。验证见 [独立 Pass 报告](../Temp~/VSM/Roadmap~/Experiments/VSMShadowPassSplit_20260917/README.md)。

## 布局重映射

`VSMShadowPass.RecordVirtualShadowMapLayout` 在布局变化时依次派发：

1. `VSMUpdatePhysicalPageAddresses`：每个物理槽读取旧 owner 对应的虚拟页元数据，按 clipmap 原点差更新地址；失效基准或越界的槽解除所有权。
2. `VSMClearVirtualPageMappings`：清空密集虚拟页表和虚拟页元数据。
3. `VSMRemapPages`：按保留物理槽的 owner 写回映射及完整元数据。

第一步全部完成后才能清空，清空完成后才能写回，防止平移重叠覆盖尚未读取的来源。临时 `RemapPageMetadata` 为每个物理槽 16 字节，由 runtime 随物理池容量创建、复用和释放。物理深度不搬移；已驻留页的 dirty、Deferred、请求年龄和调试快照保留，未驻留的旧请求由随后当帧标记重新生成。布局无变化时不派发这三个 kernel。

该阶段采用 UE 按物理页更新虚拟地址的组织方式；当前密集虚拟页表仍需一次全表清零。相机/光源/布局基准的兼容判定、全组重置条件及后续分配策略仍由 VividRP 管理。

## 动态缓存扩展（2026-09-17）

带 `VividVSMConservativeBounds=1` ShadowCaster Pass 的普通 MeshRenderer 使用 Unity culling 的聚合投影物 bounds。Runtime 合并上次成功提交和当前 bounds，只失效覆盖页面；当前覆盖仍逐帧重绘，覆盖之外可复用。四个无顶点形变的内置材质已声明该契约；Skinned、Terrain、粒子及未声明契约的自定义材质保留全量刷新。

动态失效 kernel 每个 bounds/projection 使用 64 线程处理页面矩形，C# dispatch 维度不变；静态失效入口保持单线程。Unity 阴影 RendererList 每个粗瓦片创建一次，避免同帧重复执行同一列表。该轮保留完整 16 层静态/动态深度池；后续 SMRT 波投票已接入[生产自适应](SMRTAdaptiveRays.md)。实现边界、质量对照及计时见 [扩展报告](../Temp~/VSM/Roadmap~/Experiments/VSMUnityDynamicCache_20260917/README.md)。

## 分配提交并行化（2026-09-17）

`VSMPrototypeAllocatePages` 保留原候选排序，按同角色、同 clipmap 压缩请求，每批 64 个请求并行读取/提交元数据，最后并行清请求位和归约统计。lane 0 仅保留共享内存中的顺序槽配对。请求 bitset、两个分配 dispatch、31 个 kernel 顺序、CPU 绑定与持久资源不变；组共享声明增至约 26.75 KiB。完整状态等价和隔离 GPU 计时见 [分配提交报告](../Temp~/VSM/Roadmap~/Experiments/VSMAllocationCommit_20260917/README.md)。

## 目录整理时的验证

- 新 shader 模块展开后，与迁移前的 compute 算法文本一致（路径替换前比较）。
- Roslyn：Runtime、Editor、Editor.Tests 三个程序集编译通过。
- DXC：54 个 compute kernel（包含采样检查入口）、32 个 caster VS/PS 变体通过。
- Unity：17 个迁移资源 GUID 保留；31 个入口索引正常；两个调试 Pass 注册正常；compute、caster、调试 shader 导入无错误；VSM 活跃，物理预算 1024 页。
- 导入期间发生一次 CLI 等待超时，及 GPUMeshletCulling、HZBGenerate、ColorPyramid 的 UAV 报错；重载和资源同步完成后未再次出现。已恢复原有 Play Mode，后续控制台检查无新增错误。
- Editor 在运行，因此按仓库约定未执行 Unity Test Framework。需在 Editor 空闲关闭后运行测试，或手动运行 Shadows、VSM Debug 及 GPUDriven 源码契约相关测试。

历史跑测报告和源码快照继续使用测量时的原路径，保留证据的可追溯性。现行源码关系以本文为准；性能结论参见 `Temp~/VSM/Roadmap~/Experiments/VSMDynamicCache_20260915/timing/README.md`，本次目录整理不构成新的性能测量。

## 脏页工作列表（2026-09-17）

`VSMShadowPass` 在全部失效标记后构建 GPU 页列表，以间接派发执行清页和占用归约。`VirtualShadowMapPrototypeRuntime` 管理按预算复用的 `PageWorkList` 与 `PageWorkDispatchArgs`，`VSMProfiling.BuildPageWorkLists` 单独记录构建成本。旧直接入口保留，新三个 kernel 追加；完整双池 16 层不变。实现、实测和未结案挂起记录见 [报告](../Temp~/VSM/Roadmap~/Experiments/VSMPageWorkLists_20260917/README.md)。

## SMRT 成本诊断（2026-09-17）

`VIVID_VSM_SMRT_COST` 仅为新增 `VSMReceiverCost` 入口启用计数，源码仍位于 `Shaders/VirtualShadowMap/Private`。Editor 的 `SMRTCostCapture` 负责一次性读回的统计归约，`VividDiagnostics` 暴露 `smrt-cost` 操作。钩子位于 raw resolve 与降噪之间，默认无订阅/派发。见 [使用说明](SMRTCostDiagnostics.md) 与 [实测报告](../Temp~/VSM/Roadmap~/Experiments/SMRTCost_20260917/README.md)。
