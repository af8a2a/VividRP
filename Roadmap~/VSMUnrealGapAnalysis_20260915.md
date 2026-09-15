# VividRP 与 Unreal VSM 源码差距分析

日期：2026-09-15。

后续实施：[多层存储与持续重绘成本分解](Experiments/VSMRebuildCost_20260915/README.md)。已按 clipmap 分组大型请求页面，保留双池 16 层深度；该报告包含同构建 A/B、深度等价及验证限制。下文保留优化前的差距分析口径。

## 结论与分析范围

当前 VividRP 已有完整的定向光虚拟阴影主链路。主要差距集中在 **多层深度的存储/重建成本、页面调度的扩展性、动态缓存复用、几何提交效率，以及多灯光/多接收者集成**。仅增加虚拟分辨率或 SMRT 射线数无法补齐这些差距。

这也是两种表示路线的比较：VividRP 用 16 层隐藏深度和逐格求交改善重叠遮挡；UE 用首层深度、有限采样和深度外推控制成本。因此，不能把移植 UE 单层采样或最前层 HZB 剔除描述为保质量的等价优化。

- VividRP：当前工作区运行时代码，HEAD `70f4eb9b`。工作区另有未跟踪的 `VSMDepthVariants_20260913` 实验归档，已纳入分析；其中候选已恢复，不属于默认实现。
- Unreal：本地 `E:/UnrealEngine` 源码，[Build.version](E:/UnrealEngine/Engine/Build/Build.version:2) 为 **5.7.4**。
- 本次只进行源码和归档证据审计，没有启动 Unity/Unreal，没有新跑性能或画质对照。下列测量均标明来源，不能当作两引擎同场景对跑。
- Epic 在线 [VSM 文档](https://dev.epicgames.com/documentation/en-us/unreal-engine/virtual-shadow-maps-in-unreal-engine) 当前页面标注 5.8，仅辅助核对概念；本地 5.7.4 实际调用链优先于文档和 CVar 注释。

## 1. 当前已具备的能力

| 项目 | VividRP 当前状态 | 对比意义 |
|---|---|---|
| 虚拟寻址 | 128×128 页，单投影支持到 16384，最多 16 个方向光 clipmap | 页尺寸、16K 寻址能力均已有，不能列为缺失 |
| 需求产生 | 当帧 depth/GBuffer → 标页 → 分配/绘制 → Resolve；wave 合并请求 | 已消除依赖上一帧反馈的基本结构缺口 |
| 驻留与缓存 | 角色优先级、LRU、滚动 remap、静动态分池、静态 meshlet 旧/新 bounds 失效 | 已具备缓存骨架；复用范围仍受后端限制 |
| 接收者质量 | 屏幕密度选层、LOD bias、覆盖/层间过渡、完整 footprint 回退 | 已有质量选择，缺页为零仍不代表细节目标达标 |
| 软阴影 | PCF、跨 clipmap SMRT、固定世界发散长度、16 层深度 | 已超出简单首层 SMRT，但承担更多存储和求交工作 |
| 近期优化 | 页/池判空、页内地址复用、有序深度区间查找、短历史、自适应 2/4–8 射线 | 不能再建议“先增加空池跳过/二分查找/时域降噪” |
| 诊断 | 页和 receiver 可视化、阶段 sampler、GPU oracle、几何参考、动态轨迹、Nsight 归档 | 调试并非空白；下一步应复用这些门槛 |

入口：[配置](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPipeline/CascadedShadowSettingsVolume.cs:21)、[当帧顺序](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/CSMShadowPass.cs:410)、[密度选层](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/VSMReceiverQuality.hlsl:28)、[有序查询](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/VSMSMRT.hlsl:88)。

## 2. 最根本的差异：深度表示和采样成本

### VividRP

每个驻留页的静态、动态池各保存 16 层 R32_UInt 深度，通过原子插入保留最近的不同深度。SMRT 用 DDA 逐纹素格推进，首层未解决时分别查询两池的有序深度区间。现在每池隐藏层查询最多额外 4 次探测，已经替代最多 15 次线性探测。

每个 clipmap 段有独立格数预算，末级可扩大预算以走完设定世界长度；主层/过渡层、footprint 重试也会增加工作。因此 Volume 的“4 射线 × 8 样本”不等于每像素仅 32 次深度读取。

### Unreal 5.7.4

SMRT 基于首层深度，用之前的深度和斜率向遮挡后方外推。模板循环为 `i <= NumSteps`，含接收者端点，采样时间采用平方分布；不是遍历每一个经过的纹素格。方向光射线长度按距视点距离缩放，另有纹素抖动、接收面斜率和屏幕空间起点处理。

源码：[UE 采样模板](E:/UnrealEngine/Engine/Shaders/Private/VirtualShadowMaps/VirtualShadowMapSMRTTemplate.ush:25)、[UE 方向光长度与射线循环](E:/UnrealEngine/Engine/Shaders/Private/VirtualShadowMaps/VirtualShadowMapProjectionDirectional.ush:221)、[Vivid 原子插入](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Public/Shadow/VividVirtualShadowMapCaster.hlsl:66)、[Vivid 跨层续接](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/VSMSMRT.hlsl:227)。

**影响：**Vivid 的隐藏遮挡信息更丰富，但不是严格几何光追，仍有有限层容量、粗纹素覆盖、slab/gap 和平行尾段近似。UE 的单层外推同样存在重叠遮挡和半影局限；不能由表示层数推导哪个引擎在所有画面上更准确。

### 显存差异可直接计算

两池深度存储：`页数 × 128 × 128 × 4 bytes × 16 层 × 2 池`。

| 页预算 | Vivid 当前双池 16 层 | 同页数双池单层布局 |
|---|---:|---:|
| 256 | 512 MiB | 32 MiB |
| 1024 | 2 GiB | 128 MiB |

此表只比较深度 payload，不含 DSV、页表、历史、HZB 和内存布局开销，**不是 UE 总显存对比**。Vivid 的硬阴影/PCF 也共用该布局。[资源常量](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/CSMShadowPass.cs:2678)

容量也未被 16 层彻底解决：9 月 12 日静态机位有 15.77% 已分配纹素填满第 16 层，独立几何抽样有 14.00% 超过 16 个深度桶。它们分别是满层率和几何复杂度统计，不能写成漏光率或生产插入丢弃率。[容量报告](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/README.md:24)

## 3. 页面管理与质量反馈：明确的扩展性差距

| 维度 | VividRP | Unreal 5.7.4 |
|---|---|---|
| 新页分配 | 单个 64 线程组预处理全虚拟表，线程 0 按角色串行处理请求并寻找空槽/淘汰页 | GPU physical-page lists，wave 原子 push/pop、并行 worker 分配、列表压缩和重用 |
| 页压力应对 | 固定 target/bias，预算不足后按角色保留退路，采样时退层 | 池占用驱动 GlobalResolutionLodBias，带升降速率、恢复等待和上限 |
| 绘制负载控制 | 未见 VSM 专用时间/工作量反馈控制 | 存在 Nanite ShadowDepths 时间/估计负载控制；相关时间预算功能标记 experimental，默认关闭 |
| 细粒度需求 | 页级请求角色；空池标记是在光栅后用于减少读取 | 8×8 receiver mask 表示页内需要覆盖的区域，接入剔除；方向光默认启用，局部光默认关闭 |

证据：[Vivid 分配器](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute:525)、[UE 列表](E:/UnrealEngine/Engine/Shaders/Private/VirtualShadowMaps/VirtualShadowMapPhysicalPageManagement.usf:71)、[UE 分配 worker](E:/UnrealEngine/Engine/Shaders/Private/VirtualShadowMaps/VirtualShadowMapPhysicalPageManagement.usf:597)、[UE 池压力反馈](E:/UnrealEngine/Engine/Source/Runtime/Renderer/Private/VirtualShadowMaps/VirtualShadowMapCacheManager.cpp:750)、[UE 负载预算](E:/UnrealEngine/Engine/Source/Runtime/Renderer/Private/VirtualShadowMaps/VirtualShadowMapArray.cpp:695)、[UE receiver mask](E:/UnrealEngine/Engine/Shaders/Shared/VirtualShadowMapDefinitions.h:23)。

这不是“UE 完全不扫描虚拟页表”；UE 也有按映射范围遍历等工作。关键差别是请求、物理列表、分配和后续任务已分阶段并行组织。

对 Vivid，虚拟分辨率翻倍会使完整页表条目数变为四倍。既有 10 级、固定 256 页实验中，2048→16384 的 Allocate 从约 0.224 ms 增至 3.386 ms。这些数值采于有序深度优化之前，但该优化不修改分配器；仍需在同版本完整预算中复测。[分辨率实验](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMResolutionScaling_20260913/README.md:73)

**质量陷阱：**同一实验中 8192/256 页的接收者全部退到末级，其快速 Resolve 来自粗层回退。不能用“提高虚拟分辨率后更快”证明优化成功。应同时追踪实际采样层、primary/continuation 请求满足率和屏幕纹素误差。

## 4. 缓存、剔除与光栅化

### 已有缓存，但复用粒度不及 UE

Vivid 静态 meshlet 已局部失效和缓存；动态池仍每帧清空全部驻留页的 16 层，再绘制动态内容。Unity Renderer 兼容路径按投影/粗 tile 提交，写入动态池，没有享受相同的静态 meshlet 复用。这不是“CPU 每个物理页 draw 一次”。

UE 分别跟踪静态/动态缓存状态，按实例变化失效，并有静止对象转静态缓存机制（此源码阈值默认 100 帧）、变形/WPO 相关策略和阴影 HZB。普通非 Nanite 几何也有批量提交与 HZB 路径。

证据：[Vivid 动态清页](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute:804)、[Unity 兼容提交](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/CSMShadowPass.cs:1598)、[UE 静止阈值](E:/UnrealEngine/Engine/Source/Runtime/Renderer/Private/VirtualShadowMaps/VirtualShadowMapCacheManager.cpp:85)、[UE HZB/批处理开关](E:/UnrealEngine/Engine/Source/Runtime/Renderer/Private/VirtualShadowMaps/VirtualShadowMapArray.cpp:532)。

### 多层表示限制了直接借用 UE 剔除

Vivid shadow dispatcher 明确 `supportsOcclusion: false`。这确实减少了一类性能手段，但简单打开首层 shadow HZB 会丢弃当前 16 层算法可能需要的隐藏 caster。SMRT 斜向射线可以命中从主光轴看被遮挡的表面。

UE receiver mask 也不能只按接收者中心点照搬：Vivid 必须覆盖 PCF/软阴影射线及跨层续接所需的区域。UE 自身还对静态缓存几何限制 receiver-mask 剔除，避免缓存内容依赖瞬时屏幕覆盖。[Vivid 遮挡配置](E:/VividRP_Reborn/Packages/VividRP/Runtime/SubSystem/GPUDriven/VividGPUDrivenSystem.cs:865)、[UE 静态缓存 mask 限制](E:/UnrealEngine/Engine/Shaders/Private/Nanite/NaniteClusterCulling.usf:514)

### 动态太阳应单独预算

光源旋转使方向光缓存失效，两者都受此约束；差距在失效后的重建成本和可用质量控制。未跟踪的最新实验归档中，4096/1024、持续太阳旋转的开关版基线 StaticRaster 约 27.55–27.63 ms，而稳态主要成本在 Resolve。这是单独实验窗口，不能与其他报告的 Resolve 中位数相加，也不能据此直接归因为原子争用；需进一步拆解提交、覆盖和层插入成本。[布局实验](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMDepthVariants_20260913/README.md:38)

## 5. 自适应与时域稳定：需纠正旧的 UE 判断

UE CVar 注释写着自适应“仅支持 OnePassProjection”，但 5.7.4 调用链为：uniform 的 `SMRTAdaptiveRayCount` → `GetSMRTTraceSettingsDirectional` → `TraceDirectional`；compute 分支在首射线全 wave miss、或随后全 wave hit 时提前退出。方向光入口实际调用了这条路径。因此不能继续断言 UE 方向光没有接入自适应。

Vivid 则在当前采样前，用深度/法线验证的历史判断稳定区，将预算降为 2，并每 8 帧错峰完整刷新。这是不同的噪声/响应权衡：UE 看当前 wave 的命中结果；Vivid 看上一帧历史稳定性。不能直接用一个布尔选项对齐两者质量。

证据：[UE 设置传递](E:/UnrealEngine/Engine/Shaders/Private/VirtualShadowMaps/VirtualShadowMapSMRTCommon.ush:77)、[UE 方向光提前退出](E:/UnrealEngine/Engine/Shaders/Private/VirtualShadowMaps/VirtualShadowMapProjectionDirectional.ush:280)、[Vivid 预算选择](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute:2177)。

当前 Vivid 短历史按相机 VP 重投影，没有在该阴影历史核中使用物体运动矢量；已有 TSR 联合采样选项，不能写成“完全没有 TSR 配合”。9 月 13 日邻域裁剪优化将归档半影时间标准差降低约 7.7%，但仍非完整的动态内容稳定性闭环。后续需覆盖真骨骼/植被变形、共面移动接收者、薄遮挡首帧检出与残留。运动矢量也不能单独解决静止地面上的移动投影阴影。[历史读取](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute:2138)、[噪声实验](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMNoiseStability_20260913/README.md:1)

## 6. 通用引擎能力的差距

| 范围 | 当前 VividRP VSM | UE 对照 |
|---|---|---|
| 灯光 | 主方向光链路 | 方向光 clipmap、点光六面/聚光 mip、远处小灯单页、多局部灯 projection |
| 接收者 | 当帧 opaque depth/GBuffer 驱动；体积光仍采 CSM | 有 coarse page marking、HairStrands 输入、高质量透明投影等专用集成 |
| 多视图 | 物理池/页表为 static 单 owner，历史按相机隔离 | 按 light/view 管理 cache entries 和投影视图；同样不能假定所有视图都无成本共享 |
| 大世界 | float Matrix4x4/Vector3，clipmap 共享带迟滞的深度区间 | translated-world、double-float 平移和逐 clipmap 深度缓存管理 |
| 内容接入 | Renderer 需显式 VSM caster 合约，不兼容时回 CSM；VT 变化保守失效 | Nanite/非 Nanite、变形、材质位移等更多渲染系统集成 |

这些是适用范围差异，大世界精度或多相机抖动在本次并未被实测为故障。

证据：[Vivid 单池 owner](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/CSMShadowPass.cs:2678)、[体积光 CSM](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/Volumetric/VolumetricLighting.compute:355)、[UE 多类投影视图](E:/UnrealEngine/Engine/Source/Runtime/Renderer/Private/VirtualShadowMaps/VirtualShadowMapArray.cpp:4673)、[UE double-float 投影](E:/UnrealEngine/Engine/Source/Runtime/Renderer/Private/VirtualShadowMaps/VirtualShadowMapClipmap.cpp:518)、[UE Hair/透明入口](E:/UnrealEngine/Engine/Source/Runtime/Renderer/Private/VirtualShadowMaps/VirtualShadowMapProjection.cpp:42)。

## 7. 最新性能证据应如何使用

有序查找已解决旧报告中最突出的线性深层查询成本。在 1080p、RTX 5070 Ti、Sponza、1024 页、固定 4 射线、16 层、角径 7.1° 的归档反向配对窗口：

| 分辨率 | ResolveTrace 旧→有序查找 | 完整 Resolve 旧→有序查找 |
|---|---:|---:|
| 2048 | 7.435→4.690 ms | 7.657→4.954 ms |
| 4096 | 18.526→5.540 ms | 18.790→5.772 ms |

这是一次有诊断分支的 A/B；后续独立原始源码窗口 4096 Resolve 为 5.173–5.216 ms。窗口、编译和时钟不同，不将两轮之差当作额外优化收益。它们都表明不能把旧的 18–20 ms 当作当前稳态成本。[有序查询 A/B](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMDepthSearch_20260913/README.md:7)、[后续独立窗口](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMDepthVariants_20260913/README.md:50)

后续四层交错布局、页级最大层数、最近首层复用三个具体实现均未达到采用标准，生产代码已恢复。其失败不证明所有压缩/复用方案无效，但不能把它们再次当作未验证的首选。

没有 UE 同资产、同硬件、同光源角度、同输出分辨率、同驻留/阴影质量目标的对跑，故无法给出“慢几倍”或“达到 UE 百分之多少”。

## 8. 建议优先级与验收

### P0：为两类实际负载分别控制成本

1. **稳态：**保留有序查找，重新统计 cell 数、真实命中层、续接/重试和实际重复地址分布，再选择一种采样或存储候选。若研究按需隐藏层，必须把容量、重建、metadata 和读取成本一起计算；不先承诺收益。
2. **持续重绘：**按光源旋转、相机暴露和动态 caster 分开测。优先找出不必要提交/重复覆盖、多层写入和清页的占比，验证动态脏页与静止分类。首层 HZB 和降回单层只能作为明确改变画质目标的实验分支。
3. **质量门槛：**保留当前 16 层隐藏命中/空隙 oracle、静动态独立池、拱门反例，并增加真实动态 caster。记录完整 VSM 非嵌套成本、P95/P99、显存和几何 FP/FN。

### P1：让质量和页面预算闭环

4. 引入带迟滞的页压力 LOD/target 调节；标页与 Resolve 消费同一生效值。验收同时限制 overflow、细层满足率和实际阴影细节损失。
5. 将全表串行分配尾部改为请求压缩/物理空闲列表等可扩展组织；不能破坏当前角色优先级、同帧 ownership 与粗层退路。按 2K/4K/8K/16K、稳定/滚动/满池测试。
6. 将虚拟页表配置与物理池资源描述拆开。当前分辨率变化会走整体 `ReleaseAllocatedResources`，即使物理容量不变也重建池；这是配置切换成本，不是稳态每帧问题。[资源匹配](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/CSMShadowPass.cs:3029)

### P2：按项目需求扩展

7. Unity Renderer 静态复用、保持 SMRT footprint 的页内需求剔除、体积/透明 VSM 采样。
8. 局部灯光、多相机缓存和大世界坐标；完成前先定义目标场景，避免一次复制整个 UE 系统。

最终判断：**VividRP 的方向光 VSM 功能骨架已经齐全，也有比首层方案更丰富的隐藏深度；与 UE 的主要距离在以可控成本覆盖广泛场景的工程能力。** 当前最有价值的是降低多层路线的存储和重建成本、稳定细层驻留，并扩大动态内容验证，而非继续堆高分辨率与射线数。
