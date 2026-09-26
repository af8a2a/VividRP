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
| `Private/VSMPageMarking.hlsl` | 专用接收点请求生成；逐层合并 SMRT 和 PCF footprint，保留独立角色与完整回退链 |
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

## 当帧请求与常驻状态

`PageRequestFlags` 为每个虚拟页保存一个 uint，由 `VirtualShadowMapPrototypeRuntime` 按布局创建、稳定复用和释放。`VSMShadowPass` 先完成布局更新，再清空请求缓冲并从当前接收点标记。请求不参与布局重映射；换相机或帧回退时额外重置请求年龄。

标记只对 `_VSMPageRequestFlags` 合并角色，不写常驻元数据或年龄。`VSMPrototypePrepareAllocation` 在逐页生成分配 bitset 时更新已请求页的年龄。`PageMetadata` 的 x/y/z/w 分别保存常驻状态、物理槽编码、最后请求帧及状态调试快照，不再保存请求角色。分配器与限额补绘列表读取同一份请求缓冲；请求保留到下一次标记前清空，供 Requested/Request Role 调试读取。调试导出在读取时合并请求和状态，保持原输出格式。

请求角色、优先级、跨层覆盖及 SMRT/PCF footprint 不变。此分离不包含 receiver mask、像素步长或 UE 层级选择策略的迁移。

`VSMMarkReceiverPages` 通过 `MarkVSMReceiver` 独立生成需求；阴影求值不再负责标记。SMRT 与 PCF 各自选择起始层和过渡权重，在共同层级复用投影准备，按页面矩形的精确并集合并请求：交集只提交一次并合并角色，独有页面保留原角色。SMRT continuation 仍为 Primary，完整父链及末层 Coarse 请求保留；选择不读取常驻状态、物理深度或随机射线相位。

## 动态缓存扩展（2026-09-17）

带 `VividVSMConservativeBounds=1` ShadowCaster Pass 的普通 MeshRenderer 使用 Unity culling 的聚合投影物 bounds。Runtime 合并上次成功提交和当前 bounds，只失效覆盖页面；当前覆盖仍逐帧重绘，覆盖之外可复用。四个无顶点形变的内置材质已声明该契约；Skinned、Terrain、粒子及未声明契约的自定义材质保留全量刷新。

动态失效 kernel 每个 bounds/projection 使用 64 线程处理页面矩形，C# dispatch 维度不变；静态失效入口保持单线程。Unity 阴影 RendererList 每个粗瓦片创建一次，避免同帧重复执行同一列表。该轮保留完整 16 层静态/动态深度池；后续 SMRT 波投票已接入[生产自适应](SMRTAdaptiveRays.md)。实现边界、质量对照及计时见 [扩展报告](../Temp~/VSM/Roadmap~/Experiments/VSMUnityDynamicCache_20260917/README.md)。

## 分配提交并行化（2026-09-17）

`VSMPrototypeAllocatePages` 保留原候选排序，按同角色、同 clipmap 压缩请求，每批 64 个请求并行读取/提交元数据，最后归约统计。lane 0 仅保留共享内存中的顺序槽配对。请求分离后仍保留 bitset 和两个分配 dispatch，移除了提交末尾清元数据请求位的循环；当帧请求由下一次标记前统一清空。组共享声明约 26.75 KiB。该轮完整状态等价和隔离 GPU 计时见 [分配提交报告](../Temp~/VSM/Roadmap~/Experiments/VSMAllocationCommit_20260917/README.md)。

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

## 完整请求下的补绘顺序（2026-09-26）

`VSMBuildPageWorkLists` 只改变有限预算内的补绘选择，不裁剪请求、分配需求或父链，不改变静态/动态 16 层深度池。末层当前 Coarse 请求必须全部可读（页表、元数据和物理 owner 一致，已分配且无 dirty/deferred）才进入细节恢复；检查包含尚未分配的虚拟页。否则保持从粗到细补齐兜底。已完成的空页也算可读。

兜底就绪后，Primary（含 SMRT continuation）、Transition 和最近一级 Parent 优先，同组仍从粗到细。其余父链使用剩余预算；每 8 帧从原预算中保留 `max(budget / 8, 1)` 页给这些请求，按其实际积压数量截断，未用份额回给优先组。该维护轮将远端父层视作同组，并按维护轮次旋转物理槽，避免持续失效使完整父链永久排队。无限预算保持原有行为。

工作列表仍逐帧依据当前 owner 重建，未选脏页保持 Deferred，接收端继续拒绝读取；无需持久 CPU 队列或新增 GPU buffer。`VSMShadowPass` 为工作列表入口补充页表只读绑定。提前恢复细节可能增加随后失效时的重绘总量，也可能短暂使用更粗的中间回退；此调度不保证逐帧阴影误差单调下降。

实现对照、故障注入、完整请求等价与质量回放记录位于忽略目录 `Temp~/VSM/RecoveryOrder_20260926/`。

## Receiver mask 与动态缓存覆盖（2026-09-26）

每个虚拟页以两个 `uint` 保存 8×8 receiver 单元。`VSMPageMarking.hlsl` 保留 SMRT continuation、PCF halo、过渡层和完整父链请求，在精确 footprint 并集上生成 mask；末层 Coarse 请求始终为完整 mask。普通清请求和切换接收相机的 reset 均同步清除当帧 mask。

`VSMPrototypeCullMeshletsToPages` 对动态 meshlet 的投影范围与 mask 做交集测试；大记录展开后在 vertex 阶段再次按具体页面裁剪，Unity 普通 caster 和分页 caster 均在深度层插入前拒绝未请求的 texel。静态缓存始终整页绘制，静态/动态双池各 16 层不变。

`PageReceiverMasks` 为当帧虚拟页需求，`PhysicalReceiverMasks` 为动态物理页已完成的覆盖。分配准备发现需求超出旧覆盖时只标记 DynamicDirty，之后服从已有补绘预算；Finalize 只在动态页实际完成后替换覆盖，延期页不提交。动态页整页清除，所以覆盖不能与旧值 OR。页平移保留物理槽及其覆盖；槽重新分配时覆盖清零。需求收缩可以复用旧覆盖，未变化的页面继续复用动态缓存。

PCF、SMRT 逐格采样和 SMRT 整段 footprint 预检都检查完成覆盖；空页标记和页内地址复用不能绕过检查。缺覆盖仍走较粗层回退，Availability 调试原因增加 bit 16（uncovered）。这避免把部分绘制页当成完整、全亮页面。

UE 参考关系：`VirtualShadowMapPageMarking.usf` 的 8×8 mask 标记、`VirtualShadowMapBuildPerPageDrawCommands.usf` 的静态缓存禁用 mask 裁剪，以及 `VirtualShadowMapPhysicalPageManagement.usf` 对部分动态页的有效性限制。本实现用完成覆盖检查保留动态复用；尚未采用 UE 的 mask 纹理 mip 层级或 froxel 标记。

资源由 `VirtualShadowMapPrototypeRuntime` 按有效尺寸复用和释放，并纳入 RenderGraph 访问声明。独立 GPU 检查、合成质量回放和实际场景单帧记录位于忽略目录 `Temp~/VSM/ReceiverMask_20260926/`。

## SMRT 成本诊断（2026-09-17）

`VIVID_VSM_SMRT_COST` 仅为新增 `VSMReceiverCost` 入口启用计数，源码仍位于 `Shaders/VirtualShadowMap/Private`。Editor 的 `SMRTCostCapture` 负责一次性读回的统计归约，`VividDiagnostics` 暴露 `smrt-cost` 操作。钩子位于 raw resolve 与降噪之间，默认无订阅/派发。见 [使用说明](SMRTCostDiagnostics.md) 与 [实测报告](../Temp~/VSM/Roadmap~/Experiments/SMRTCost_20260917/README.md)。
