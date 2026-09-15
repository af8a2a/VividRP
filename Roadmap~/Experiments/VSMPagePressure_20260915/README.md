# VSM：补页压力驱动质量与分配成本

日期：2026-09-15。基线：`221b4d6f`（上一轮按 clipmap 分组大型页面请求）。本轮最终源码及 SHA-256 随报告归档。

## 结果

新增 GPU 页面压力反馈，在预算不足时降低接收者的空间密度，余量持续充足后逐步恢复。分配预处理改为多组并行，物理槽按原有淘汰顺序一次排序，消除每次缺页重新遍历物理池的成本。**最终提交仍按角色及从粗到细的顺序串行执行。**

双池各 16 层深度、原子层插入、隐藏深度查询和 SMRT 射线路径保留。质量调节会改变空间分辨率，不能把“所选页面全部驻留”理解成原细节目标在小预算下也完全达标。

Sponza 固定机位的预算升降轨迹中，128、256、512、1024 页预算均收敛到零必要缺页，最后 60 次相机渲染的质量偏移范围均为 0；预算降回原值后，偏移及驻留分布回到相同结果。

| 预算 | 固定密度的必要需求 / 缺页 | 自适应偏移 | 自适应必要需求 / 缺页 | 最细驻留级别 | 首次升预算窗口的稳定起点* |
|---:|---:|---:|---:|---:|---:|
| 128 | 1431 / 1303 | 5.000000 | 74 / 0 | 5 | 100 |
| 256 | 1431 / 1175 | 4.000000 | 160 / 0 | 4 | 124 |
| 512 | 1431 / 919 | 3.000000 | 350 / 0 | 3 | 124 |
| 1024 | 1431 / 407 | 1.984375 | 932 / 0 | 1 | 123 |

*起点为窗口内从 0 开始的相机渲染观测序号，不是毫秒。完整下降路径 1024→512→256→128 的稳定起点分别为 76、68、68。级别是当前投影数组索引。

![预算、需求、质量偏移与缺页轨迹](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMPagePressure_20260915/budget-trajectory.png)

小预算没有填满，是因为更细一级的需求存在离散跳变：128、256、512 页窗口中，失败的更细探测分别请求 141、303、610 页。保留余量使所选 footprint 可以完整驻留，也避免周期性尝试更细层造成振荡。控制目标不是保证所有场景都占用预算的 85%–95%。

原始证据：[逐帧记录](budget-sweep/frames.json)、[窗口汇总](budget-sweep/summary.json)、[各级驻留页数](budget-sweep/stages.json)、[绘图脚本](scripts/plot-pressure.py)。

## 1. 压力反馈与质量选择

入口：[Volume 设置](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPipeline/CascadedShadowSettingsVolume.cs:33)、[GPU 控制器](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute:504)、[接收者密度选择](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/VSMReceiverQuality.hlsl:27)。

- `virtualShadowMapPagePressure` 默认开启，仅在 `virtualShadowMapScreenDensity` 开启时生效；可独立关闭，恢复固定密度策略。
- 输入是上次分配的必要需求及未满足数量，分母是物理页预算；必要需求包含主层、父级/过渡角色，排除更低优先级的推测回退。SMRT 续接如果携带主层角色，也计入实际必要需求。本次场景中必要需求与主层需求相同。
- 使用需求数量，不使用残留缓存占用；新分配后无需继续重绘的缓存页不会仅因占着槽位而驱动降质。这是容量与缺页闭环，尚未以 GPU 重绘毫秒数作为控制目标。
- 必要缺页非零，或需求/预算大于 0.95：每次提高 `1/32`–`1/8` LOD 偏移，步长为 `clamp(0.5*log2(max(pressure/0.85,1)), 1/32, 1/8)`。
- 比例小于 0.85 且持续 60 次更新：开始每次减小 `1/64` LOD；中间区间保持；无接收者需求不作为恢复细节的依据。偏移限制在现有投影范围内。
- 更细探测一旦越过预算边界，回退到上一个可行偏移，记住探测前后需求比。处于该边界时，只有按当前需求推算的更细需求不超过当前预算的 90%，才允许再次探测。预算增大或需求下降可解除约束。
- 在标页之前冻结当次偏移，标页、Resolve、Debug 共用。偏移变化时拒用旧阴影历史，避免将不同密度的历史混合；该帧也不利用旧历史减少射线数。
- 对退化接收平面及算出负 LOD 的表面，将原始 LOD 先限制到可用最细级，再添加偏移，避免这些表面绕过压力反馈。

48 字节 GPU 状态包含偏移、恢复计龄、需求/驻留计数、上次偏移、目标及失败探测信息；生产路径无需 CPU 回读。只有预算变化时保留控制状态和生产相机身份；布局、分辨率、生产相机切换、帧序重置或基础密度目标变化时重置。资源生命周期见 [EnsureAllocationResources](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/CSMShadowPass.cs:2926) 和[预算重建](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/CSMShadowPass.cs:3139)。

## 2. 分配改动及顺序保证

原实现由单组 64 线程预处理整个虚拟表，随后线程 0 在缺页时扫描物理池寻找空槽或淘汰项。

现在分为两步：

1. `VSMPrototypePrepareAllocation` 按 32 个页面一字分派多个线程组，整理本帧请求并清理过期反馈。每个字、每个页面只有一个写者。
2. `VSMPrototypeAllocatePages` 的 64 线程对最多 1024 个物理槽进行 bitonic 排序，线程 0 依原角色及粗到细顺序提交，单向消费排序后的槽列表。

排序依次为：空槽、低价值角色、较老的请求帧、较小的物理槽索引。原来对更高优先级、同角色更粗/同级驻留页的保护继续生效。被跳过的受保护候选在后续遍历中不会重新变得可淘汰；新分配页也由该遍历次序保护。

额外资源为最多 32 KiB 的请求位图及 16 KiB 的组共享排序空间。预处理工作分布到多个组；淘汰候选查询从每次缺页扫描整个池，变为一次排序加单向消费。四轮请求遍历和串行提交仍在，尚未实现 Unreal 的并行 page-list 分配模式。

新增 `VSM.AllocationPrepare`、`VSM.AllocationCommit`，均包含于既有 `VSM.Allocate`。上一轮清页、提交、覆盖、层插入成本的结果见[持续重绘报告](../VSMRebuildCost_20260915/README.md)；本轮没有重测这些阶段，不能把分配压测收益等同于整体阴影或重绘收益。

代码：[准备内核](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute:624)、[分配内核](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute:665)、[派发与阶段标记](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/CSMShadowPass.cs:1369)。

## 3. 合成压力场景完成耗时

同一 Editor、同一 GPU、相同 ShaderUtil 导入路径，8192 虚拟分辨率、10 级、40960 虚拟页、1024 物理槽，每次提交 2048 个强制请求。每种情形预热 8 轮、采样 16 轮，交替旧/新提交顺序；新实现包含准备与分配两个 dispatch。

| 场景 | 旧实现完成耗时中位数 | 新实现完成耗时中位数 |
|---|---:|---:|
| 空池集中补页 | 30.350 ms | 3.201 ms |
| 满池淘汰补页 | 267.851 ms | 3.694 ms |

这里计量的是 CPU Stopwatch 包围 dispatch 与阻塞 `GetData` 的完成耗时，包含提交、等待和读回。输入 `SetData` 调用位于计时区外，但排队传输仍可能影响等待。**这是集中补页的合成压力场景，不是生产帧的纯 GPU 时间或帧率提升。** 每轮都验证旧/新四项计数一致且工作实际完成：请求 2048 页、最终占用 1024 页。

两种计时着色器的分配段与最终实现对照一致。原始记录及范围见 [completion-timing](completion-timing/summary.json)，脚本见 [allocator-completion.cs.txt](scripts/allocator-completion.cs.txt)。该同步诊断超过 CLI 服务端 5000 ms 等待上限，CLI 报超时后 Editor 继续完成并写出全部 32 条结果，finally 释放资源；后续状态查询确认 Editor ready。

先前 GPU Profiler 压测出现不可信样本：同导入路径的冷启动/淘汰窗口，旧实现各有 21/64、22/64 次仅 256–512 ns，而另一些样本为数十至数百毫秒。保留于 [excluded-profiler-timing](excluded-profiler-timing/results.json)，**不纳入性能结论、不筛掉异常后计算提升比例**。上表采用独立的完成耗时交叉测量。

## 4. 验证与复现范围

| 检查 | 结果 |
|---|---|
| 最终 GPU 分配与旧实现对照 | 108 组，页表、全部 uint4 元数据、物理归属及四项计数逐值一致；请求位图尾部哨兵未破坏 |
| 控制器 GPU 检查 | 延迟恢复、快速降质、饱和、空需求、重置、关闭、失败探测后连续 180 次稳定、增预算解锁通过 |
| 退化 / 负 LOD 接收面 | 固定策略选 0/0 级，偏移 2 的自适应策略选 2/2 级 |
| 运行时资源稳定调用 | 预热 32 次，测量 256 次 `EnsureResources` + `EnsurePhysicalPageForBinding`，当前线程托管分配 0 字节 |
| DXC | 52/52 个生产及采样测试 compute 入口通过 |
| Roslyn | Runtime、Editor、Editor.Tests 三个程序集通过，存在原有警告 |
| 编辑器预算轨迹 | 3456 次相机渲染观测；固定基线 4×24，自适应 7×480；最后窗口无偏移抖动 |
| 场景恢复 | before/after JSON 完全相同；8192/1024、16 深度层、Edit Mode 停止状态、相机/太阳姿态、后台标志恢复；两个场景均未变脏 |

分配对照覆盖级数 1/2/4/16、预算 1/2/7/31/64/65/127/256/1024、空/混合/满池、小于 32 页的布局、帧 0/1/1000、随机角色、过期请求、孔洞、相同年龄等；每组独立初始化，**不是 108 条多帧运动轨迹**。旧有多帧分配回归已适配新的两步派发。

预算轨迹环境：Unity 6000.7.0a6、RTX 5070 Ti、Direct3D12、1920×1080、Sponza + SponzaLightingDay、固定相机。临时 Volume 固定 4 射线、关闭自适应减射线；太阳按 ±0.0001° 微扰以持续触发 Editor 渲染，诊断结束恢复。保留了原有 16 层深度写入。详情见[最终编辑器快照](validation/final-editor-snapshot.json)和[轨迹脚本](scripts/live-v5.cs.txt)。

没有运行 Unity Test Framework：Editor 为用户正在使用的交互会话。新增及改动的 `CascadedShadowSettingsVolumeTests`、`VirtualShadowMapReceiverQualityTests` 已编译；完整 Unity 回归需由用户手动运行。上述独立 GPU 诊断不调用 NUnit 测试方法。

当前线程 0 GC 仅覆盖所测资源复用路径，不等于已用 Profiler 证明全部渲染线程 0 GC。复杂相机运动、多相机交替、动态遮挡轨迹、收敛期间的画面质量尚未穷尽验证。压力状态仍由全局 VSM 生产者持有，相机切换重置；未添加每相机的长期控制状态。达到最粗级仍超预算时可能继续缺页，本轮不能保证任意场景零缺页。

最后导入完成后的控制台增量未出现新的 shader 绑定/编译错误；记录到一次上述同步诊断的服务端超时。历史导入期和其他管线 UAV 错误不据此宣称已修复，见[控制台记录](validation/final-console.json)。

## 5. 归档

- [最终源码哈希](validation/source-final-sha256.json)、`source-final/`、`source-before/`。
- [分配对照结果](validation/allocator-equivalence.json)、[控制器及分配检查](validation/pressure-checks.json)。
- [DXC 结果](validation/dxc/validation.json)、[C# 结果](validation/roslyn/validation.json)，目录内保留编译日志，不包含生成二进制。
- `scripts/` 保留当次实际运行脚本；其中诊断和编译脚本使用当时的本地 Temp/Unity Bee 路径，跨机器复现需要调整路径及导入环境。
- `budget-sweep/` 仅归档最终 v5 轨迹；早期调参轨迹不作为最终结果。`timing-shaders/` 是合成计时使用的冻结输入，最终完整源码以 `source-final/` 为准。
