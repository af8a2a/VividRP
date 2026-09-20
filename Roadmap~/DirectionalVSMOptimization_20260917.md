# 定向光 VSM 下一步优化方向

日期：2026-09-17。基于 VividRP `038853de` 与用户提供的 UE 本地参考快照。源码对应、参考文件覆盖和版本边界见 [Shader 参考关系](../Documentation~/VirtualShadowMapShaderReference.md)。

实施更新：已完成 [P1-A 分配提交并行化](../Temp~/VSM/Roadmap~/Experiments/VSMAllocationCommit_20260917/README.md)：组内压缩请求、并行读取/写回和归约，保留顺序槽配对。下文保留实施前的排序与基线依据；最新实测和验证边界以该报告为准，P1-B 已建立工作列表，见 [实现与计时报告](../Temp~/VSM/Roadmap~/Experiments/VSMPageWorkLists_20260917/README.md)。跨帧重测通过。2026-09-18 用户确认先前 GPU Hang 为 GPU 超时；后续按有界、分批 GPU 检查继续推进。SMRT 已完成[固定四步二分展开](../Temp~/VSM/Roadmap~/Experiments/SMRTBinaryUnroll_20260918/README.md)，保留完整隐藏遮挡质量。

## 1. 结论与边界

**优先把请求、脏页和绘制工作压缩到真正需要处理的范围，同时保留静态/动态双池各 16 层。** 下一项实现建议是分配提交的分阶段并行化；随后建立脏页工作列表，并让 caster 提交消费这些列表。SMRT 是当前最大的已测阶段，已完成成本细分、二分展开及生产自适应接线；已进一步完成接收点和起始投影准备复用，见[等价检查与计时](../Temp~/VSM/Roadmap~/Experiments/SMRTReceiverPreparation_20260918/README.md)。

当前已经完成密度/补页压力反馈、并行请求预处理、一次候选排序、Meshlet 旧/新 bounds 缓存、Unity 聚合 bounds 局部缓存、动态失效组内并行，以及独立 `VSMShadowPass`。本路线不把它们重新列为缺失能力。

本次是方案与证据整理，**没有新增性能测量或实现变更**。UE 参考主要提供工作组织方式；单层深度外推、首层 HZB 遮挡剔除与当前隐藏遮挡表示不等价。

## 2. 用已有计时确定投入位置

来源：[Unity 动态缓存报告](../Temp~/VSM/Roadmap~/Experiments/VSMUnityDynamicCache_20260917/README.md)、[timing-summary.json](../Temp~/VSM/Roadmap~/Experiments/VSMUnityDynamicCache_20260917/timing-summary.json) 的 `timing-parallel.gpu_ms`。Unity 6000.7.0a6 / RTX 5070 Ti / D3D12 / Sponza / 1920×1080 / VSM 8192 / 1024 物理页 / 4 条固定 SMRT rays，含临时 Unity Cube 与动态 Meshlet。同构建 A/B/B/A，每模式 128 个观察值。

**这是独立 VSMShadowPass 拆分之前的计时，不是当前拆分后的新基线，也不是与 UE 的同场景对跑。** [Pass 拆分报告](../Temp~/VSM/Roadmap~/Experiments/VSMShadowPassSplit_20260917/README.md) 只支持该轮功能/数据验证，不能补作性能证明。

| GPU 阶段 | 动态全刷均值 ms | 局部复用均值 ms | 对下一步的意义 |
|---|---:|---:|---|
| ResolveTrace | 5.9629 | 5.8634 | 最大已测阶段，但包含接收面求值/回退等，不能全部算作隐藏层读取 |
| AllocationCommit | 1.0746 | 1.1162 | 稳态仍较高，排序后串行扫描/写回值得优先拆分 |
| MarkReceiverPages | 0.7257 | 0.7189 | footprint 与请求生成也有成本，需和 Trace 一起检查重复工作 |
| UnityCompatibilityRaster | 0.5209 | 0.5291 | 缓存减少了写页，未减少粗瓦片提交；没有实测收益 |
| PageCull | 0.5189 | 0.5152 | 已有按页绘制仍可能处理大量候选 |
| StaticCasterCull | 0.4442 | 0.4538 | 静态大面积缓存命中时也需考虑调度开销 |
| ClearPhysicalPages | 0.3573 | 0.1944 | 已下降 45.6%，继续优化应看总 dispatch 与实际脏页的差额 |
| PageOccupancy | 0.1821 | 0.1264 | 可与脏/未知占用页列表协同 |
| InvalidateDynamic | 0.0025 | 0.0154 | 局部标记的额外成本；组内并行已经完成 |
| DynamicRasterDraw | 0.0347 | 0.0129 | 此场景很小；不能据此认定其他全重绘场景的层插入成本也很小 |

局部复用时驻留 1024 页、动态脏页 446。`Resolve` 父标记为 6.1257 ms、`Allocate` 父标记为 1.3259 ms，均不能和子标记再次相加。清页/动态失效/占用/动态绘制合计约 0.577 → 0.349 ms，是四个阶段的收益，不是整帧提升。

**目前没有独立的层插入耗时。** `DynamicRasterDraw` 与 Unity 粗瓦片 marker 都混合了光栅覆盖、地址验证、原子插入等工作；不应把其时间直接命名为 insertion time。

## 3. 实施顺序

### P0：固定当前基线与重载边界

- 在独立 `VSMShadowPass` 上重跑相同计时窗口，同时记录相机数、驻留/脏页、fallback、有效细层比例、ray 数以及材质/投影物配置，确认拆分没有改变工作负载。
- 保存代码/Shader 重载是用户报告的 Hang 触发条件。用户于 2026-09-18 确认为 GPU 超时，不再以根因排查作为优化前置；生产波退出现使用独立 `VSMShadowResolveAdaptive` kernel。新 kernel/资源调整需检查重载后的 kernel 索引、资源绑定、容量与释放次序，并分开记录冷启动和重载结果。
- 沿用 [GPU Hang 记录](../Temp~/VSM/Roadmap~/Experiments/SMRTWave_20260916/gpu-hang.md)，不把短时未复现或 RendererList 漏绘修复当成 Hang 根因已解决。

### P1-A：并行化分配提交，保住质量优先级

**当前入口：** `VSMPageManagement.hlsl::VSMPrototypeAllocatePages`。64 线程完成候选排序后，lane 0 扫描四个角色阶段、提交/淘汰映射、更新统计，并再次遍历所有 owner 清请求位。`AllocationCommit` 计时覆盖整个 kernel，尚未单独测出排序与串行部分的占比。

**UE 参考：** `VirtualShadowMapPhysicalPageManagement.usf::{UpdatePhysicalPages, AllocateNewPageMappingsCS, PackAvailablePages, AppendPhysicalPageLists}` 和 `VirtualShadowMapPerPageDispatch.ush::FPerPageDispatchSetup`。

建议拆成可独立验证的小步骤：

1. 将当前 bitset 压缩成按角色、粗到细和稳定页序排列的请求列表；并行归约驻留命中/需求，避免 lane 0 四遍扫描相同 word 范围。
2. 将命中页写回、统计归约及 owner 清请求位移到有明确唯一写者的并行阶段；保留现有 miss 分配逻辑作为中间基线。
3. 再为 miss 建立确定的请求—候选 slot 配对，分阶段提交旧映射解除与新映射写入；每个物理 slot 只能有一个新 owner。存在同帧低优先级 owner 被更高优先级替换的依赖，不能用全局 atomic pop 随机抢页代替当前策略。

**验收：** 复用 [压力/分配报告](../Temp~/VSM/Roadmap~/Experiments/VSMPagePressure_20260915/README.md) 的分配 oracle，覆盖小布局、非满预算、满预算、滚动孔洞、同优先级跨层淘汰、保底页与过量请求。要求页表/owner 双向一致、脏页正确、统计与压力输入一致；若保留策略，应与原分配结果等价。新方案确实改变选择策略时，必须单独比较细层驻留/回退质量，不以更少的工作冒充算法加速。

性能只比较同一请求、预算与 ray 配置下的 Prepare/Commit 总成本和 p95；当前 1.1162 ms 是参考成本，不是承诺可全部消除的收益。

### P1-B：脏物理页工作列表，先减少清页与归约的空发射

**实施更新：** 已接入 GPU 构建和 indirect 清页/归约；未知占用与禁用占用优化均覆盖，finalize/caster 尚未改为列表消费。下述为实施前设计，当前结果与历史稳定性记录见 [报告](../Temp~/VSM/Roadmap~/Experiments/VSMPageWorkLists_20260917/README.md)。

**当前入口：** `VSMPrototypeClearPhysicalPages`、`VSMPrototypeReducePageOccupancy`、`VSMPrototypeFinalizeDirtyPages` 与 `VSMShadowPass` 的 dispatch。当前脏页判断能跳过写入，但不能消除针对非脏物理页发射的大量清页线程。

**UE 参考：** `SelectPagesToInitializeCS` → `EmitPageToProcess` → `InitializePhysicalPagesIndirectCS`；`SelectPagesForHZBAndUpdateDirtyFlagsCS` 只借鉴选页模式，不借鉴首层遮挡结论。

- 在分配、remap 和全部失效标记完成后，生成静态/动态待清页列表和待判空列表，使用 indirect dispatch。判空列表还必须包含 occupancy unknown 页，不能只看本帧 dirty。
- 列表一项可携带两池 mask，或分别保留两池列表；先选额外资源/UAV 最少的实现。保存完整 16 层写入，复用工作缓冲容量，空列表正确生成零工作参数。
- 明确 pass 内顺序：列表构建 → 清页 → 所有 caster 写入 → 判空 → finalize。不能在 caster 完成前清 dirty 位或把旧 owner 的列表用于 remap 后新 owner。
- 后续可让 finalize 和 caster 候选阶段消费同一批页，避免另一次全页扫描；不要一次重写全部阶段而丢失收益归因。

**验收：** 同 owner/同投影下双池全部 16 层逐值一致；覆盖零脏页、单池脏、两池脏、未知占用、换页、全失效、禁用后重开和首帧。记录列表构建、清页、判空、finalize 总时间与发射组数；1024 页全脏时额外列表成本也必须可接受。

### P1-C：压缩有效投影/瓦片与 caster 候选

**UE 参考：** `CompactViewsVSM_CS`、`CullPerPageDrawCommandsCs`、`OverlapsAnyValidPage`、`GenerateHierarchicalPageFlags`。注意 UE 的 view 压缩本身也有小范围标量循环，并非所有环节都应强行并行。

- Meshlet 侧从已知脏页范围构建活跃 clipmap/页面区域，减少无工作投影的 caster cull；对宽覆盖实例借鉴页层级 flags/mask，先判断是否有相关页再展开细页请求。
- Unity 侧当前按粗瓦片创建独立 Shadow RendererList。GPU 压缩页列表**不会自动减少 CPU RendererList 提交**：先用 CPU 已知的保守 bounds/投影范围跳过确定无关的粗瓦片；要让 GPU 精确脏页控制提交，需要可行的 GPU 绘制接入，不能同步回读每帧列表来抵消收益。
- 更细 receiver mask 必须包含 SMRT 射线锥、抖动与层过渡的保守扩张。不能只保留中心接收点覆盖，也不能用最前层 HZB 删掉隐藏 caster。

**验收：** 分开记录 CPU 列表创建/提交时间、GPU caster cull/page cull、draw 数、粗瓦片数和实际覆盖。所有保留下来的 caster 覆盖及双池深度与全量基线一致，特别检查跨瓦片 Unity RendererList 只执行一次的约束。

### P2-A：SMRT 降低每条射线工作量，保持求交语义

**成本细分已落地：** 新增 `smrt-cost` 单帧诊断，统计 footprint、DDA、两池分层 Load、重试和过渡的分布，并与 raw 阴影逐像素对照。[三帧实测报告](../Temp~/VSM/Roadmap~/Experiments/SMRTCost_20260917/README.md) 显示当前静态 Sponza 的隐藏层 Load 占标量池 Load 调用约 73.56%，约 33.3% 像素计算过渡投影，PCF fallback 为 0；这是工作量证据，不能据此按比例分配 GPU 毫秒。

**生产自适应已接通（2026-09-18）：** 现有开关选择独立 adaptive/fixed kernel，保留固定预算二进制和全部隐藏层；按实际 rays 归一，失效 lane 禁止一致性退出。实现、单相机配对计时与画质差异见[接线报告](../Temp~/VSM/Roadmap~/Experiments/SMRTAdaptiveProduction_20260918/README.md)。

**后续方向更新：** 按用户要求优先考虑 [UE SMRT 调用方式对齐](SMRTUsageAlignment_20260918.md)：先接通并验证实际自适应调用及参数语义，再整理一次投影准备/逐样本寻址，最后独立评估整条 ray 固定步数和视距长度。保留多层隐藏遮挡，不能直接以 UE 的单层外推替换。

**2026-09-18 实施：** 隐藏层二分保留原探测顺序，展开为最多四次查询。正常 Trace 中位数 8.794 → 8.403 ms；同帧交替重放 8.590 → 7.651 ms（缓存已热，不能当作整帧收益）。三帧 1080p raw 阴影零差异，26 万区间查询命中/读取数一致，详见[实现、候选取舍与计时报告](../Temp~/VSM/Roadmap~/Experiments/SMRTBinaryUnroll_20260918/README.md)。先探测首个隐藏层/末层的候选均未保留；下一步优先查 receiver/projection 范围内重复 footprint 和坐标变换。

**当前入口：** `HasVSMSMRTFootprint`、`TryTraceVSMSMRTRay`、`TryTraceVSMSMRTClipmaps`、`TryFilterVSMSMRT`；UE 对照为 `GetMappedClipmap`、`SMRTFindSample`、`SMRTRayCast`。

已有页内地址复用、空池跳过、有序区间二分查询、一次 phase 获取和跨 clipmap 连续射线，不应重新实现这些优化。下一步从现有 `g_VSMDebugWork` / `g_VSMDebugSMRT` 扩展诊断：

- 分清 footprint 页检查、实际 DDA 格步数、页切换、两池隐藏层读取、层过渡双算、不可用重试和 PCF 回退；采集分布/p95，而不只看平均 rays。
- 验证完整 footprint 检查是否在主层/过渡/回退中重复，并寻找可在 receiver/projection 范围内复用的保守结果。不要直接移除检查，否则可能增加追踪失败与重复计算，或改变回退结果。
- 对已证明两池均空的整页，可研究推进到页出口。必须正确更新 DDA 边界、段末长度和 gap history；没有占用证明的缺页不能当作空页。
- 若首层/隐藏层带宽确为主因，再评估保守块级范围辅助数据。数据构建、额外缓存失效和访问成本一起计入，不能以“层级结构总会更快”为前提。

**验收：** 固定采样序列下复用几何/深度 oracle，保持 `unavailable` 与 clear ray 的区别、双池独立排序、实际世界长度和终段行为。优化后还需比较接触阴影、叠层遮挡、薄物体和 clipmap 边界。5.8634 ms 只是 Trace 总成本，内部瓶颈判断仍是待验证假设。

### P2-B：Unity 动态缓存由聚合范围推进到变化对象

**UE 参考：** `VSMUpdateViewInstanceStateCS`、`ProcessInvalidationQueueGPUCS`、`InvalidateInstancePagesLoadBalancerCS`、`ShouldMaterialInvalidateShadowCache`。当前 Unity 聚合范围外可复用，范围内仍重画；对象分散时聚合范围会放大成本。

- 对具备保守 bounds 契约的普通 Renderer 建立持久身份与变化 journal，只向失效队列提交上次成功绘制状态和本帧变化。禁止仅凭 Transform/bounds 不变判静止：Mesh、材质参数、Alpha Clip、MaterialPropertyBlock、Shader 重载、可见/投影状态变化都可能改变深度。
- 若无法可靠观察某类变化，继续保守刷新该类对象覆盖；运行时修改材质和纹理也需要契约/版本通知，不能完全依赖 Editor 对象通知。
- 静态/动态池迁移必须失效旧池旧覆盖与目标覆盖；状态只在成功 Record 后提交。UE 状态转换策略存在自身的近似取舍，不能直接当作本实现的深度等价保证。
- 改为细粒度判断时复用持久数组/字典和 NativeList，预热后零托管分配；不要每帧枚举全部 Renderer 并新建对象快照。

**验收：** 静止、连续移动、停止、移除、禁用、换材质/Alpha Clip、变 Mesh、MPB 更新、录制失败、热重载与多相机。逐池 16 层对照，保证测试对象在实际驻留页中有非零深度贡献；只比较全零池不算有效证据。

### P2-C：分离覆盖与层插入成本，再决定存储结构实验

**当前入口：** `VividInsertVSMDepth`。UE 参考包没有对应的多层插入器。

1. 计时保持清页、分配提交、Meshlet/Unity 绘制、判空分开。另记录 CPU 提交和 GPU 覆盖指标，不能把 CPU 时间加入 GPU marker。
2. 仅在诊断变体中统计进入 PS 数、有效页写入数、层探测/原子次数、重复深度提前结束及 16 层溢出分布。统计本身会扰动性能，生产计时使用无统计变体。
3. 可用相同几何/驻留/提交的 coverage-only、单层、完整 16 层诊断对照估计边际工作，但关掉 UAV/原子会改变编译、early-Z、带宽与占用率，差值只能称“变体增量”，不能精确拆成原子耗时。
4. 只有数据显示隐藏层普遍稀疏且存储/清理仍主导，才重开可变隐藏层存储实验。需设计溢出容量、清理、跨帧回收和最坏层数保证；不得悄悄裁层。此前存储实验结论见 [重绘成本报告](../Temp~/VSM/Roadmap~/Experiments/VSMRebuildCost_20260915/README.md)，避免重复无收益方案。

### P3：质量控制扩展与其余定向光差距

| 方向 | 参考与实施边界 | 验收重点 |
|---|---|---|
| 细层驻留与成本预算 | 继续使用现有 `UpdateVSMPagePressure` 的需求/缺页信号和恢复迟滞；参考 `Throttle.usf` 补充实际 caster 工作量控制。页面容量压力与光栅成本压力分开记录，若组合 bias 必须明确责任、上限与恢复速率，避免两个反馈环互相追逐。 | 预算升降、静止/移动/太阳旋转；记录 primary demand/resident、父层/终层回退、bias 轨迹及 p95。池满不等于需要继续降质，缺页为零也不等于细节达标。 |
| 波级当帧提前退出 | 参考 `TraceDirectional`；先解决重载验证与投票执行范围，再在明确参与 lane 集合的 compute 路径评估。无效 lane 不能在退出后让余下 lane 误判一致。 | 固定全 ray 基线、全亮/全暗/半影、不同 wave 宽度、缺页/越界/层切换、重复保存与热重载。提前退出属质量/性能取舍，单独报告偏差与噪声。 |
| 大世界精度、多个相机 | 参考 `ProjectionStructs` 的 `PreViewTranslation`、Directional 中 DoubleFloat 相减及 instance/view 身份；需要 CPU 投影/缓存归属配套。 | 远离原点、相机切换与多个相机交替。每相机历史并不等于每相机物理页驻留隔离。 |
| froxel/coarse 接收者 | 参考 `GeneratePageFlagsFromFroxelsCS`、`MarkCoarsePages`；仅在体积/透明/毛发确需 VSM 时接入，避免无接收者也强制铺粗页。 | 不依赖不透明 depth 的接收者覆盖，以及新增保底需求对细层预算的影响。 |

局部灯、one-pass 多灯 mask 与透射材质属于功能扩展，当前不插入定向光性能优化的关键路径。

## 4. 下一轮具体交付建议

范围控制为 **“分配提交分阶段 + 原分配策略等价检查 + 新 Pass 同构建计时”**。先完成 P0 与 P1-A，暂不同时改层数、SMRT rays、驻留质量策略和 Renderer 提交后端。随后 P1-B 才能独立回答“工作列表省了多少空清页/归约”。

每轮均交付：

- 有明确配置与场景的同构建 A/B/B/A，预热后记录均值/中位数/p95、页/列表计数与其他相机回调。
- 算法变更的针对性编译/数据 oracle；渲染调度改动的全双池 16 层对照。物理 owner 不同的样本应按虚拟页重新匹配，否则拒绝作为逐值等价证据。
- 缓存命中、局部运动、滚动 remap、预算压力、太阳旋转/全失效分别测量，不能用稳定缓存成绩代表持续全重绘。
- 热路径预热后的 `GC.GetAllocatedBytesForCurrentThread()` 回归及 Profiler 相关线程检查；不要把单线程零分配推成全管线零 GC。
- Editor 运行时按仓库要求采用针对性编译/诊断，不主动启动 Unity Test Framework；未执行项与重载稳定性边界如实列出。
