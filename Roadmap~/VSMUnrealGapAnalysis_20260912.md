# 定向光 VSM 差距复核（2026-09-12）

后续状态：本文提出的“16层容量、阶段成本与动态质量基线”已完成，最新实测见 [基线报告](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/README.md)。16层满层率15.77%，独立几何抽样14.00%超过16桶；常见窗口Trace占Resolve约97.7%，动态太阳失效观测的静态重绘约8.2ms。九情景三模式动态对照与六情景1024射线复核已归档，下一项优先验证按页/池占用跳过确定为空的深度访问。下文保留基线采集前的分析记录；有关“尚未测量”和当时成本的描述，以新报告的实测及覆盖边界为准。

当前主线应转向：**在保住隐藏遮挡和细层质量的前提下，降低表示、绘制与求交成本，并完成动态场景验证。** 当帧标页、跨层续接、静动分离、短历史及自适应射线已经存在，不能再作为待补功能。

本报告基于 `e9dd0a39` 加当前工作区的 16 层残余漏光修复、短历史和自适应修改。仅分析，未改变运行时代码。Unreal 对照使用本次查阅的 Epic 官方文档（页面标注 UE 5.8）及 Fortnite 技术文章，没有 UE 源码审计或同场景同硬件对跑；不估计“达到 UE 百分之多少”或速度倍数。旧的 [2026-09-10 分析](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/VSMUnrealGapAnalysis_20260910.md) 保留作历史记录，涉及缺失功能和性能的结论以本次复核为准。

## 已经跨过的阶段

| 能力 | 当前实现与验证 | 仍需区分的边界 |
| --- | --- | --- |
| 虚拟页与定向 clipmap | 128×128 页、指数覆盖、屏幕密度选层、视域覆盖；虚拟分辨率支持到 16384 | 最新质量/耗时场景实际为 2048，不能写成“实现仅支持 2048/4096” |
| 当前帧需求 | 本帧深度/法线 → 标页 → 分配/绘制 → Resolve；已有 wave 请求合并 | 已验证轨迹上的缺页归零，不等于任意页预算下细层质量都足够 |
| 缓存与失效 | 角色优先级、LRU、滚动 remap、静动池、静态 meshlet 旧/新 AABB 局部失效 | Unity Renderer 兼容路径和动态池尚无同等静态复用能力 |
| SMRT 几何 | 跨层保持配置世界光程，缓存连续格页地址；16 层分别处理静/动态隐藏深度 | 有限层数、粗纹素覆盖和最大长度后的平行尾段仍是近似 |
| 降噪与射线 | 空间双边滤波 + 深度/法线拒绝的短历史；稳定区 2 条，其他区域配置 4–8 条 | 当前是第一版二档预算；动态实景尚未完成全面对照 |

代码入口：[分辨率上限](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/VirtualShadowMapProjection.cs:25)、[当帧执行顺序](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/CSMShadowPass.cs:410)、[分配器](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute:488)、[SMRT](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/VSMSMRT.hlsl:129)、[历史及预算](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute:2069)。

## 与 Unreal 对照时必须保留的区别

Epic 公开的 SMRT 使用第一深度层并以 gap filling 近似隐藏遮挡，也承认单投影的半影局限。当前 16 层是不同的表示选择，不能把回到单层当成无损对齐 UE。[Epic VSM 文档](https://dev.epicgames.com/documentation/en-us/unreal-engine/virtual-shadow-maps-in-unreal-engine)

公开参数提供页池负载驱动的动态 LOD、静止对象转入静态缓存，以及阴影视图 HZB。`SMRT.AdaptiveRayCount` 仅注明支持 OnePassProjection，而后者用于局部光源；不能据此宣称我们的定向光自适应已复现 UE 同一路径。[Epic Console Variables Reference](https://dev.epicgames.com/documentation/en-us/unreal-engine/unreal-engine-console-variables-reference)

Fortnite 的案例还说明：持续移动太阳与变形植被可能迫使定向光放弃页面缓存，直接把全量更新成本做进预算。它是特定游戏的取舍，并非全部 UE 项目的默认策略。[Epic Fortnite VSM 技术文章](https://www.unrealengine.com/tech-blog/virtual-shadow-maps-in-fortnite-battle-royale-chapter-4?lang=en-US)

## 当前成本基线

最新测试是 RTX 5070 Ti、DX12、1920×1080、AA None、2048 虚拟分辨率、256 页、16 深度层、7.1° 角径、4 条最大射线、每段 8 格、10 m 发散光程。

| 指标 | 空间滤波 | 短历史 + 自适应 | 解读 |
| --- | ---: | ---: | --- |
| 拱门帧间噪声 σ | 0.0017022 | 0.0008345 | 再下降约 51% |
| 拱门对几何参考 MAE | 0.0055781 | 0.0054378 | 整体几何误差仍非零 |
| 射线尝试/有效接收者 | 5.485 | 3.594 | 含跨层混合、重试，下降约 34.5% |
| 静动态深度对读取/接收者 | 381.24 | 234.82 | 下降约 38.4%，不是带宽瓶颈证明 |
| `VSM.Resolve` 中位数 | 13.143 ms | 12.232 ms | 包含 resolve 与滤波，下降约 6.9%；不是整帧提升 |

固定射线加短历史为 14.345 ms，说明历史自身存在可见成本。短历史额外占约 47.46 MiB/1080p/相机。当前静动态深度池合计 512 MiB/256 页；1024 页为 2 GiB，均未计 DSV、页表等资源。每个逻辑物理页对应两池各 16 层 R32，合计 2 MiB；其深度存储量是同页数、同静动分离单层布局的 16 倍，不是 UE 总显存的 16 倍。

用户箭头点在最新三组各 32 帧均为零可见度；这只证明已修复该固定机位问题。完整数据与采样边界见 [短历史报告](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMSMRTTemporal_20260912/README.md:19) 和 [16 层报告](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMResidualLeak_20260912/README.md:24)。

历史数据不可拼接：当帧标记和 PageCull 优化使用 4096/1024 页及更早深度表示，最终约 0.94–0.99 ms、0.175–0.178 ms。不能将它们加到最新 12.232 ms 上当作总成本，也不能沿用旧单层显存表。[标页/剔除报告](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMMarkCull_20260912/README.md:15)

## 剩余差距及优先级

### 1. 最高优先：16 层的容量与成本尚未形成通用闭环

所有物理页固定分配 16 层；插入循环到达上限后，没有输出仍被丢弃深度的标志。16 层修复了当前拱门，不能保证密集场景永不溢出。多层恢复轴向隐藏表面，却不能恢复粗纹素内部的覆盖和实体几何。

下一步先统计：按页/clipmap/静动态池的层占用分布、末层非空、插入丢弃事件、实际命中层与深度读取数量。**末层非空不是溢出；并发插入的丢弃事件也不能直接当成唯一丢失表面数。** 保留少量独立几何枚举核对真实容量遗漏，再决定哪些页需要附加层，哪些页可压缩。

阶段计时需要覆盖清页、caster 写入、trace、横向滤波、纵向历史。按需附加层、占用元数据或读取剪枝都是候选，尚无本轮实测收益；不能直接将全场景降回 8 层。现有硬阴影/PCF 模式也使用同一固定层池，若为其缩减布局，切换 SMRT 时必须重建相应缓存内容。

证据：[有界原子插入](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Public/Shadow/VividVirtualShadowMapCaster.hlsl:66)、[固定层池](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/CSMShadowPass.cs:3393)、[清页](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute:820)。

### 2. 最高优先的质量门槛：动态场景与粗层几何

历史按前帧相机 VP、深度和法线重投影，没有物体运动矢量。当前裁剪和可见度差异超过 0.2 时重置年龄，已通过合成移动边缘测试；共面移动接收者、旗帜/骨骼变形、快速细遮挡、太阳旋转仍缺实景配对验证。上帧被判断为稳定后降至两条射线，是否延迟细阴影首帧检出，需要测量，不能断言已发生问题。

先配对捕获固定 4/8 条与自适应的相机平移/转向、薄杆扫过、共面物体和变形 caster；记录首帧检出、P95/P99 响应延迟、滤波范围外残留、漏遮挡峰值。物体运动矢量可以改善移动接收者重投影，但静止地面上的移动投影阴影仍需要变化门控，不能只加 motion vector 就宣布解决。

粗层仍采用有限 slab 和单格 gap，最大配置光程后仍有沿主光轴的尾段。八层阶段的 698,880 射线模型中，FN 5640→3642、FP 2298→2496，新增 198 FP 来自粗纹素覆盖；这是历史模型结果，不是当前 16 层生产 GPU 全量结果。应将生产 16 层接回完整矩阵，分开统计容量遗漏、粗纹素覆盖、slab/gap 与尾段误差，再评估细层确认等候选。单独收紧粗层厚度的候选也未通过既有逐场景误差门槛，因此不能只靠调厚度解决。

证据：[动态验证边界](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMSMRTTemporal_20260912/README.md:51)、[粗层模型](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMLayeredDepth_20260912/README.md:25)、[厚度候选被否决](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMSMRTSamplingCost_20260911/README.md:43)。

### 3. 随后：页压力驱动的质量调节

现在分配器会保护 terminal/parent，再考虑 primary/transition 等角色，角色内结合帧龄。它保证退路，但不会根据实际压力调整屏幕纹素目标。Volume 的页预算、屏幕密度目标和 LOD bias 仍是固定输入；缺页为零也可能只是成功退到更粗层。

应把 primary/transition 请求满足率、驻留回退、世界/屏幕纹素误差、页面换入换出与池压力关联起来，评估有上下限和迟滞的质量反馈。标页与 Resolve 必须消费同一帧生效的质量参数。验收需要同时限制页溢出和细节损失，不能仅看分配失败归零。降低请求分辨率可减轻需求和绘制，但不会自动缩小预分配的 16 层池显存。

分配器还有可测性能候选：64 线程预处理后，实际分配/淘汰由单线程扫描，缺页可能遍历整个池；目前没有最新配置下的瓶颈证据，不应立即并行重写。[分配扫描](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute:547)、[质量输入](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/VirtualShadowMapReceiverQuality.cs:46)

### 4. 按测量选择：提交、清页与缓存优化

| 已确认的实现限制 | 应先测的量 | 满足条件后评估 |
| --- | --- | --- |
| 动态池每帧清理所有驻留页的全部层 | 清页时间、动态实际占用页、重绘页数 | 动态脏页跟踪/静止分类；必须清除移动后遗留深度 |
| 大 meshlet 超过 4 页时按相关页集合展开，vertex 再过滤 | 展开实例数、有效页实例数、拒绝比例 | 大请求压缩与按实际数量间接派发 |
| Unity Renderer 兼容路径每层/粗 tile 重绘到动态池 | 后端 caster 数、`UnityCompatibilityRaster` 时间 | 普通 Renderer 静态缓存或更精确提交 |
| VT 状态变化保守失效静态 alpha caster | alpha 失效页占比与 streaming 尖峰 | 更细纹理页到 caster 的依赖 |
| 自适应读取下降没有等比例转为 GPU 收益 | trace/两次滤波耗时，预算分布、历史拒绝原因、硬件计数 | 历史访问或采样预算优化，保持完整分层采样集合 |

代码：[动态清页](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute:820)、[大请求](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute:402)、[Unity 兼容绘制](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/CSMShadowPass.cs:1603)、[VT 失效](E:/VividRP_Reborn/Packages/VividRP/Runtime/SubSystem/GPUDriven/VividGPUDrivenSystem.cs:304)。

**不能直接套最前深度 HZB。** 当前 shadow dispatcher 明确关闭遮挡剔除，而 SMRT 会读取主光轴后方的隐藏层。从主轴看不见的 caster，仍可能被斜向 SMRT 射线命中；在光栅前删除它可能重现拱门漏光。这是代码推论，尚未实现该候选。[遮挡开关](E:/VividRP_Reborn/Packages/VividRP/Runtime/SubSystem/GPUDriven/VividGPUDrivenSystem.cs:865)

若以后评估深度剔除，必须证明保留当前多层表示：例如研究同一池第 16 有效层的保守阈值，而非第一层；空纹素、alpha hole、滚动和静动态版本均需保守。即使完整 16 层列表等价，也只证明未恶化现有表示，不能保证容量足够。先做请求/提交的等价优化通常更容易验证。

### 5. 扩展阶段：接收者范围、动态太阳、多相机与大世界

当前当帧需求来自可见不透明接收者；体积光仍调用 `EvaluateDirectionalCSMShadow`，没有共用这套 VSM 任意位置采样。阴影短历史逐相机隔离，但物理页池/页表仍是单 owner；相机切换的安全保护不等于多相机独立缓存。各 clipmap 共用拟合深度区间，大世界精度还没有实测闭环。

应补低角径太阳、长光程薄几何、alpha 植被、持续移动太阳、快速相机及多相机的小型基准组。静态场景缓存收益不能代表移动太阳场景。RTAS 参考目前强制 opaque，适合实体拱门；用于植被/变形前必须扩展参考能力。

证据：[体积阴影调用](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/Volumetric/VolumetricLighting.compute:399)、[共享池](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/CSMShadowPass.cs:2675)、[深度区间](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/VirtualShadowMapClipmapLayout.cs:63)、[参考几何范围](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMCorrectness_20260912/README.md:13)。

## 下一轮具体交付

建议下一轮题目为：**“16 层 VSM 的容量、阶段成本与动态质量基线”**。

1. 使用当前生产配置，在同一版本、同一机位/轨迹完成整个 VSM 阶段计时。复用 [现有 profiling sampler](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/VSMProfiling.cs:8)，细分 Resolve 的 trace/H/temporal V；计时与图像回读、额外审计分开，避免相加嵌套 marker 或各自的分位数。
2. 输出层占用/容量丢弃、请求满足率、实际绘制页与展开实例的分布。诊断默认关闭，稳定热路径保持零托管分配。
3. 先完成固定基准的动态质量配对，再按主耗时选择一项实现：深度读写/存储、页质量反馈、提交压缩或兼容后端缓存。不给未测候选预报收益。
4. 所有候选以 16 层为质量基线：保持隐藏命中/空隙夹具、拱门箭头点及条带指标；几何矩阵逐场景限制 FP/FN，动态测试限制首帧检出和残留。记录总 VSM 中位数/P95、显存及细层满足率，避免只优化单个计数。

本次分析未重新运行 Unity 或性能捕获。沿用的最新验证为 DXC 46/46、三套 Roslyn 编译及 2/2 定向 Unity 测试通过；完整阴影套件和全线程 GC 审计仍未完成。所有“尚需测量”的项目均未被表述为已观察到的故障。
