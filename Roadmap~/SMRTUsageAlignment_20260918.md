# 定向光 SMRT：对齐 UE 的调用方式

依据：用户提供的 `Shaders/VirtualShadowMap/ReferenceVSM` 快照，而非某个未经确认的 UE 发布版本。约束继续沿用：保留静态/动态独立 16 层的隐藏遮挡能力。第 1 项自适应调用已接入生产，见[实现与验证](../Temp~/VSM/Roadmap~/Experiments/SMRTAdaptiveProduction_20260918/README.md)；第 2 项准备复用已实现，见[等价与计时报告](../Temp~/VSM/Roadmap~/Experiments/SMRTReceiverPreparation_20260918/README.md)；第 3 项固定步点/视距长度已做[独立验证](../Temp~/VSM/Roadmap~/Experiments/SMRTFixedStepsViewLength_20260918/README.md)，存在明显遮挡质量代价，未切换生产。

## 核心差距

**当前实现是多层深度场 DDA；UE 参考是固定步数的阴影贴图射线采样。** 二分展开只是当前内部循环的优化，尚未对齐 UE 的整体成本模型。

| 项目 | UE 本地参考 | 当前实现 | 对齐时的边界 |
|---|---|---|---|
| 接收面入口 | `Projection.usf::ProjectLight` 判断有效像素/背光，处理 normal bias、可选 screen ray offset 后调用 `TraceDirectional` | `ResolveVSMReceiverMode` 选层、重试，并可能完整计算过渡层 | 先明确材质和双面语义，不能直接用法线背光判断删除所有接收面 |
| 起始 clipmap | `ProjectionDirectional.ush::GetMappedClipmap` 按接收点找到已映射层，`TraceDirectional` 准备一次投影和斜率 | 每个候选仍检查整条射线的保守 footprint；密度选层复用已接受投影，候选/过渡共享相位，起始投影跨 ray 复用 | 已复用 receiver/projection 准备结果；不能把“起点有页”误当成“整条射线完整可用” |
| 采样预算 | `SMRTTemplate.ush::SMRTRayCast` 对整条 ray 使用 `NumSteps`；循环 `i <= NumSteps`，未提前命中时还有 `t=0` 端点 | `SamplesPerRay` 实际限制每个 clipmap 段的 DDA 格步数，末段还会扩大预算 | UE 配置 8 steps 最多 9 次采样位置查询；当前 8 不是整条 ray 的总上限，更不是 8 次深度 Load |
| 步点和方向 | 平方分布的 `SampleTime` 从远端向起点推进；保存深度历史/可选受限斜率外推 | 从接收点向光方向逐格 DDA，完整区间求交与单格 gap fill | 换成稀疏点采样会改变薄遮挡和隐藏几何的覆盖，需要独立图像验收 |
| 射线长度 | `SMRTRayLengthScale * length(ViewPosition)`，影响屏幕中的半影大小 | 固定最大世界长度，各 clipmap 分段保持同一条 ray | 对齐是质量语义变化；必须同步修改标页 halo、footprint 和终段处理 |
| 抖动/偏移 | 随视距/LOD 调整 texel dither，并用 receiver slope 计算偏移补偿 | 两纹素接收面覆盖、渐进 ray 前缀、一次 phase 获取 | 先稳定跨 clipmap 尺度和偏移语义；替换随机数本身不能保证提速 |
| 自适应 ray 数 | 第一条 ray 全 wave miss 可结束；`i >= AdaptiveRayCount` 后累计 miss 为零可结束；按实际 ray 数归一 | 已接通生产 `VSMShadowResolveAdaptive`；固定预算独立编译，自适应由现有 Volume 开关控制 | 当前 bool 已生效；采用零基暗区门槛 1，失效 lane 禁止提前退出，并在模式切换时失效历史 |
| 深度表示 | `SMRTFindSample` 返回一个 `SampleDepth`，跨层采样转回同一深度空间 | 静态/动态池独立排序，最多 16+16 层区间查询 | UE 单层外推不能恢复任意隐藏层；不能把该替换称为画质等价优化 |

## 建议实施顺序

### 1. 先对齐自适应调用及参数语义

用户已确认先前 Hang 为 GPU 超时，旧根因排查不再是推进前置。本轮已将 adaptive 控制接到实际定向光入口，规则及验收要求如下：

- 明确启用状态、最大 ray 数和暗区提前退出门槛，严格对应参考代码的 **零基 ray 索引 `i >= AdaptiveRayCount`**；这份 Shader 快照不足以证明 UE 的 CPU 默认门槛值。
- 保留当前完整多层求交，归一使用实际执行 ray 数。未获得有效映射/追踪失败的 lane 不能被当作全亮，也不能通过退出当前活动集合伪造一致投票。
- 分别统计首 ray 全亮、累计全暗、混合 wave、缺页拒绝提前退出的占比，并比较 raw/时域后的阴影及 GPU 时间。
- 用固定 ray 版本作为 A/B 质量基线。提前退出是统计估计变化，不能要求或宣称与固定 ray 逐像素等价；重点验收隐藏遮挡、接触阴影、运动与半影噪声。

### 2. 对齐 `TraceDirectional` 的一次准备与 `SMRTFindSample` 的职责

已将接收点法线、已接受投影的坐标/偏移、随机相位及起始投影参数复用，实景两种模式各 16 帧 raw/空间滤波零差异，合成完整接收面和标页对照一致。实现见[准备复用说明](../Documentation~/SMRTReceiverPreparation.md)。下一步再实验按采样位置选择有效 clipmap。保留当前 `unavailable` 和粗层/PCF 回退语义；UE 参考入口自身对缺页行为留有 TODO，不能直接将无效样本视为亮。

当前约三分之一像素还计算第二个过渡投影，不能直接删掉以获取 ray 数收益。须同时比较驻留、层边界接缝与相同接收点的输出。

### 3. 单独验证整条 ray 的固定步数与视距长度

**2026-09-18 验证完成，暂不接入默认。** 独立四路重放对比分离了固定步点、视距长度与叠加效果；8 步实验约 14.0% 的实景 raw 像素改变，一纹素隐藏遮挡压力夹具漏检 71.55%。仅视距长度也改变约 6.83% 像素。完整数据、源码、复现和边界见[报告](../Temp~/VSM/Roadmap~/Experiments/SMRTFixedStepsViewLength_20260918/README.md)。实验保留多层查询，但不保留逐格覆盖，不能当作等价优化。

这是更接近 UE 成本模型的一步，也会改变画质。应作为明确的实验路径，与当前多层 DDA 在薄墙、栅栏、多层叠放、移动遮挡、掠射光和大半影场景对照。即使每个步点仍查询全部隐藏层，跳过中间纹素也可能遗漏遮挡；不能以“还存着 16 层”证明质量没有变化。

若保留严格逐格覆盖要求，则可以借鉴 UE 的参数/状态组织，但不能同时承诺 UE 式固定少量采样位置的成本。也不建议直接将“UE 首层结果为亮”作为跳过隐藏层查询的证明。

### 4. 保守空区间跳跃

**2026-09-18 独立研究完成，原型暂不接入生产。** 前层 4×4、完整层 4×4/8×8 摘要在 61,440 对合成射线及固定/自适应各 16 个实景噪声相位中零差异。但自适应查询由 4.608 ms 上升至 5.437–5.631 ms，另有构建成本；完整层摘要只减少约 7% 隐藏层读取，平均每段只有约 3 格。原型保留逐格浮点推进，仅省略深度查询。证明条件、生命周期接入点和数据见[报告](../Temp~/VSM/Roadmap~/Experiments/SMRTConservativeIntervals_20260918/README.md)。后续先验证长段触发与保守深度区间掩码，再决定是否建设脏页摘要缓存。

### 5. 长段触发和层间空隙掩码

**2026-09-18 验证完成，当前场景仍不接入。** 8×8/64 位局部深度掩码通过 98,304 对合成射线、32,768 项桶边界检查及固定/自适应/门槛扫描各 16 相位实景零差异。受控长段微基准有收益，但实景所有段都短于 8 格；2/4/8 门槛均未获益，全段掩码虽减少约 24% 隐藏层读取，查询仍变慢。完整实现、计数和数值注意事项见[报告](../Temp~/VSM/Roadmap~/Experiments/SMRTDepthMaskLongSegments_20260918/README.md)。只有真实负载出现足够长的端段才值得继续评估；当前不增加持久摘要资源。

## 本轮已完成的基础

[四步二分展开报告](../Temp~/VSM/Roadmap~/Experiments/SMRTBinaryUnroll_20260918/README.md)：正常 Trace 中位数 8.794 → 8.403 ms，三帧 1080p raw 阴影零差异。该实现保留，生产自适应已接通；准备复用也已完成；固定步点/视距长度独立验收显示明显偏差。保守块摘要也已独立验证：输出一致但查询变慢，尚不接入；长段触发和深度区间掩码也已验证，受控长段有收益，但当前实景仍回归，保持独立实验；视距长度仍属于另行验收的可选质量参数。

## 源码入口

- [UE 定向光入口、选层、自适应](../Shaders/VirtualShadowMap/ReferenceVSM/VirtualShadowMapProjectionDirectional.ush)
- [UE 步进与深度历史](../Shaders/VirtualShadowMap/ReferenceVSM/VirtualShadowMapSMRTTemplate.ush)
- [UE 参数和随机采样](../Shaders/VirtualShadowMap/ReferenceVSM/VirtualShadowMapSMRTCommon.ush)
- [UE 接收面与 screen-ray offset](../Shaders/VirtualShadowMap/ReferenceVSM/VirtualShadowMapProjection.usf)
- [当前 SMRT](../Shaders/VirtualShadowMap/Private/VSMSMRT.hlsl)、[接收面求值](../Shaders/VirtualShadowMap/Private/VSMReceiverResolve.hlsl)、[CPU 绑定](../Runtime/RenderPass/Core/CSMShadowResolvePass.cs)
