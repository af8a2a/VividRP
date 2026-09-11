当前 VSM 与 Unreal 的实现差距（2026-09-10）

本次核对生产实现 `caa227a5`，纳入局部历史裁剪和公开蓝噪声图集实验。UE 一侧依据 Epic 当前在线文档（页面标记 5.8）、5.5 发布说明及官方控制台参数说明；没有获得指定 UE 版本源码的逐行核验，也没有在 UE 中渲染同场景。因此下文区分已确认机制、本地源码事实与待验证推断。本轮只做分析，没有修改运行时、shader 或场景。

当前已经实现 128×128 分页、相机周围的倍增 clipmap、屏幕密度选层、覆盖过渡、页面优先级与 LRU 淘汰、静态/动态深度分离、局部静态失效、meshlet 页面绘制和 SMRT。主要差距集中在实际采样密度、软阴影估计器与时域重建之间的配合。不能把目前的边缘噪点归结为“缺一张 Unreal 的噪声图”。

| 环节 | 当前实现 | UE 可确认机制 | 对当前瑕疵的意义 |
|---|---|---|---|
| 虚拟分辨率与覆盖 | 实验 4096；代码允许至 16384。最多 16 层，半径逐层翻倍；按接收面投影的 texel 屏幕尺寸选层，覆盖与 guard 是硬约束。 | 每层 16K，方向光用 clipmap，并按相机投影选择密度。[VSM 文档](https://dev.epicgames.com/documentation/en-us/unreal-engine/virtual-shadow-maps-in-unreal-engine) | 差距是能在预算内兑现的密度。已有密度策略，不是仍停留在固定距离 CSM。 |
| 单帧软阴影采样 | 每层 4 rays × 8 cells；每条 ray 为二值命中。光盘与接收面覆盖共同由这 4 条 ray 积分。 | 文档正文给出 Epic 阴影档 8 rays；沿线样本数与射线数职责不同。[VSM 文档](https://dev.epicgames.com/documentation/en-us/unreal-engine/virtual-shadow-maps-in-unreal-engine) | 4×8 只有 4 个方向估计，不等于 32 个独立可见度样本。 |
| 随机采样 | BND 1SPP 的 256 帧时序提供四维相位，随后经过径向分层、角旋转、Hammersley 平移和 texel 中心量化。 | UE 5.5 明确把 STBN 用于 SMRT step offset 和方向光 ray；另记录通用 BlueNoiseScalar/Vec2 的 FAST 改善。[5.5 发布说明](https://dev.epicgames.com/documentation/es-es/unreal-engine/unreal-engine-5-5-release-notes) | 本地替换相位资源尚不等于对齐 UE 的实际射线/步进序列。 |
| 深度场求交 | 逐 texel DDA、有限 slab、单格 gap fill、到达发散上限后沿中心光方向延续；不可用时逐层兜底，最终重试 PCF。 | 单层深度也有重叠遮挡局限；通过深度外推弥补，且暴露方向光外推斜率限制。[VSM 文档](https://dev.epicgames.com/documentation/en-us/unreal-engine/virtual-shadow-maps-in-unreal-engine)、[参数说明](https://dev.epicgames.com/documentation/unreal-engine/unreal-engine-console-variables-reference) | 半影形状和接触处残差可能来自估计器偏差，增加历史不能修复错误均值。 |
| 时域重建 | 3×3 方差裁剪；稳定连续接收面最多恢复一半裁剪位移；三帧同向变化确认；最终混合沿用原公式。 | TSR 分别控制裁剪与最终混合，并追踪亮度闪烁。[TSR FAQ](https://dev.epicgames.com/documentation/en-us/unreal-engine/temporal-super-resolution-frequently-asked-questions-for-unreal-engine)、[TSR 文档](https://dev.epicgames.com/documentation/en-us/unreal-engine/temporal-super-resolution-in-unreal-engine) | 已有保护和局部放宽，但噪声分类与重建仍较简化。 |
| 页面与绘制预算 | 常规 256 个页面；质量诊断 512 页。静态仅失效页重画，动态层逐帧清除；主采样、过渡和兜底已有优先级。 | 页面缓存、静动分离及 Nanite 批量绘制；另提供池负载驱动的动态 LOD。[VSM 文档](https://dev.epicgames.com/documentation/en-us/unreal-engine/virtual-shadow-maps-in-unreal-engine)、[参数说明](https://dev.epicgames.com/documentation/unreal-engine/unreal-engine-console-variables-reference) | 影响在相同预算下能否给边缘更多样本/密度。无缺页的实验仍有噪声，页池并非全部原因。 |

分辨率需要结合世界覆盖比较。[ClipmapLayout](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/VirtualShadowMapClipmapLayout.cs:36) 与 [密度选择](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/VSMReceiverQuality.hlsl:29) 已有相关机制；底层上限为 [16384](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/VirtualShadowMapProjection.cs:25)。在同一世界覆盖下，16384 相对 4096 的 texel 边长为四分之一，但虚拟地址空间不能换算为完整纹理的常驻成本。UE 与本地 clipmap 的单位、覆盖和 LOD 策略不同，不宜直接照搬 FirstLevel 或 LOD bias 数值。

当前 [页面优先级](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute:149) 是 coarse/parent 优先，然后 primary，再 transition；同一页面有多个角色时取较高优先级。它优先保证粗层覆盖和兜底可靠性，不是屏幕误差最小化的预算分配。当前 [页池上限](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/CSMShadowPass.cs:2566) 为 256；UE 参数表的 MaxPhysicalPages 初始值为 4096，服务于其多光源架构，且受配置覆盖，不能据此宣称存在固定的 16 倍质量或性能差距。[官方参数表](https://dev.epicgames.com/documentation/unreal-engine/unreal-engine-console-variables-reference)

软阴影估计器还有一个关键耦合。[VSMSMRTRayLength](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute:1088) 的发散长度为：

`min(maxWorldLength, (steps - 2.001) / sqrt(2) × worldUnitsPerTexel / tan(sourceAngularDiameter / 2))`

8 步对应的最大横向范围约为 4.24 texels；4 步约为 1.41 texels。当该上限起作用时，8×4 相比 4×8 会把世界发散长度缩至约三分之一。固定层覆盖、步数和光源角度时，把分辨率翻倍也会让这个世界长度上限减半；实际选层变化可能抵消或改变它。因此后续对照须记录实际长度和半影宽度，不能只比较 `rays × steps`。这里的长度是偏离中心光线的范围，达到后仍有平行延续，并非简单把之后的遮挡全丢弃。

[TryTraceVSMSMRTRay](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/VSMSMRT.hlsl:49) 每格只保留首层深度；厚度取半个世界 texel，gap fill 只利用前一个格子的深度，不跨有效空格递推。UE 的参数说明明确存在带斜率限制的深度外推，并说明更宽松外推可能再次漏光。当前不是缺少所有 gap fill，而是近似规则不同。未核验 UE shader 前，不能宣称单格规则是 UE 的简化等价物，也不能把沿线随机偏移直接加进 DDA 后跳过格子。[官方外推参数](https://dev.epicgames.com/documentation/unreal-engine/unreal-engine-console-variables-reference)

对当前随机噪声，最直接的差距仍是实际估计样本。单层 4-ray 在混合前只有五档可见度；射线之间共享光盘和接收相位，并非独立方向采样。[采样实现](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/VSMSMRT.hlsl:25)。此前 BND 4×8→8×8 的片段中，最终时间 RMS 降低约 29%–32%，是已有实验中较明确的收益；该结果来自较早基线与有限片段，不保证当前版本有相同幅度。[BND 对照](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMBNDComparison_20260909/README.md)

公开图集结果也必须限定到本地接法：STBN/FAST 在当前相位映射下使最终时间 RMS 降低约 6%–18%，但原始阴影固定相位均值误差约为 BND 的 2.1–3.6 倍。它们尚未成为逐射线直接样本；这不能证明 UE 的采样效果更差或 STBN 本身不适用。8 个 TSR jitter 相位下，BND256/STBN64/FAST32 的每相位不同切片数分别为 32/8/4；这支持检查两种周期的关系，但没有证明周期是唯一原因。[最新公开图集实验](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/PublicBlueNoise4096_20260910/README.md)

当前 TSR 不应再描述为无条件强裁剪。新增 [局部放宽](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/TSR/TSRRejectShading.compute:139) 已生效，但噪声信号仍主要来自空间对比和当前颜色相对重投影历史的差值；三帧同方向变化用于光照响应，不能替代完整闪烁分类。[亮度波动估计](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/TSR/TSRDilateVelocity.compute:130)。UE 在不透明光照后单独记录亮度并跟踪闪烁周期，用于放宽拒绝。这与本地从最终着色中同时判断砖墙细节、阴影噪声和光照变化有区别。[TSR 亮度分析](https://dev.epicgames.com/documentation/en-us/unreal-engine/temporal-super-resolution-in-unreal-engine)

另一个可明确定位的差异是历史权重语义。[UpdateHistory](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/TSR/TSRUpdateHistory.compute:70) 在计数达到上限后，`previousSampleCount / sampleCount` 可到 1，再受 0.96 等约束；因此稳定连续区域的 history 16 并不等于当前帧最少贡献 1/16，调到 32 也未必改变最终权重。UE 的 History.SampleCount 明确约束该最小贡献。这个语义差异值得修正或明确，但直接照抄 UE 的 1/16 可能增加当前帧占比，不能当成必然降噪。[TSR FAQ](https://dev.epicgames.com/documentation/en-us/unreal-engine/temporal-super-resolution-frequently-asked-questions-for-unreal-engine)

本地历史分辨率等于输出分辨率；所谓 resurrection 是另一组被重投影并随已接受结果更新的缓存，不是按间隔保存的多个原始历史快照。UE 的高档支持 200% 历史分辨率和持久历史切片。[本地历史资源](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/PostProcessing/TSR/TSRUpscalerPass.cs:1014)、[TSR FAQ](https://dev.epicgames.com/documentation/en-us/unreal-engine/temporal-super-resolution-frequently-asked-questions-for-unreal-engine)。这些更关系运动中的细节与历史恢复，存在额外成本，不是当前 4-ray 半影方差的首选补救。

VSM 激活时本地会明确关闭旧的双边滤波和 Bend composite，最终主要靠 TSR 重建。[启用分支](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/CSMShadowResolvePass.cs:469)。公开资料没有充分依据把硬件光追阴影 denoiser、Lumen denoiser 或 NFOR 当作 UE 默认 VSM SMRT 的降噪链。若采用独立 shadow visibility 时间积累，那是本项目可评估的设计选择，需要单独处理静止接收面上的移动阴影。

架构范围也不同：当前这条 VSM 管线服务主方向光，UE 同时覆盖方向光与局部光。尤其需要更正自适应采样的适用范围：官方 `SMRT.AdaptiveRayCount` 说明仅支持 OnePassProjection，而 OnePassProjection 用于局部光投影。当前方向光不能直接照搬“关闭自适应即可消除噪声”的经验。本地没有像素级射线数自适应，但单条 DDA 射线在命中时已经可以早退。[主光源选择](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderGraph/FrameContext/VividShadowData.cs:187)、[官方参数说明](https://dev.epicgames.com/documentation/unreal-engine/unreal-engine-console-variables-reference)

建议按下列顺序缩小差距；逐射线映射、统计分类和自适应策略都是本项目候选，不声称为 UE 私有实现的复刻。

1. 固定 4096、光源外观、4×8、当前 TSR 与公开图集，验证逐射线采样映射和 TSR jitter 的联合序列；同时检查光盘与接收面采样的相关性。保持概率分布和既定积分范围，不重新生成噪声。记录 raw visibility 的固定残差和最终时间噪声。
2. 单独检查 DDA 发散长度、texel 中心起点及 gap fill 的形状偏差。用接触处、薄杆、重叠遮挡和大半影场景核验；既有高采样参考只是真实几何之外的同估计器积分，无法证明这一层正确。
3. 用固定 8×8 重新建立当前基线的质量/耗时参照，之后才评估本项目的边缘预算分配。不要以四条恰巧同值就认定无半影，也不要用变短的 8×4 伪装等价计算预算。
4. 输入误差厘清后，再改 TSR 的多帧分类和实际混合权重。已有局部裁剪仅降低约 1%–2% 时间 RMS，简单翻转状态实验也未显示更好的取舍，需优先获得更可靠的阴影或不透明亮度统计。[局部裁剪实验](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/TSRLocalClipping4096_20260909/README.md)
5. 页池、动态缓存与绘制优化作为取得更多质量预算的并行方向；独立记录 Shadow Depth 与 Resolve 成本。512 页质量对照和带读回的采集不能证明常规配置仍满足此前约 7.3 ms 的 GPU 边界。

验收继续区分硬阴影的投影锯齿、随机半影颗粒、固定估计残差、缺页兜底和历史拖尾。每组至少保留原始 visibility、实际选层/发散长度、最终颜色、灯光/相机状态和页面统计；动态验证需要新增移动遮挡物与相机运动，不能仅依赖光照强度和角直径阶跃。
