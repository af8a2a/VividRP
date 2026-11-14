# Cornell 的连通平面 anchor 预滤波

2026-11-14，Unity 7000.0.0a16 / D3D12。通过静止、完整轨迹及统一发光响应检查后，仅在 Emissive Ceiling Profile 与对应场景构建器启用 `screenProbeAnchorPreFilter`。设置类默认仍为 false，没有推广到其他 Cornell、Sponza 或 ReSTIR 配置。仍有可见低频斑块，不是稳定无噪点或 Lumen 全面对齐的完成证明。

Cornell 保持原机位、1920×1080、16 px / 8 rays，原生 Gather、anchor age 128、四轮 À-Trous。Radiosity 固定 8 阶、64 样本、32 批。没有修改曝光、材质、BRDF、π 或反弹增益。专用 Profile 通过 Unity API 保存，修改前备份；没有手改资产 YAML。

## 候选与静止结果

`screenProbeAnchorPreFilter` 在 SH 校准前增加半强度 5×5 连通平面重建。历史仍保存原始时域 anchor；遇到几何断裂、有效自适应探针或边界时保留原重建。关闭时通过显式动态分支跳过保护范围的扫描，已检查实际 DXIL。

新增一个 dispatch 和一张 RGBA16F 临时纹理：1080p 时 120×136，130,560 B（127.5 KiB），关闭时为 1×1。缓存 shader ID、kernel、sampler 与 CPU 历史开关；开关变化使对应历史失效。未改变 GPU 共享结构。没有 GPU 耗时或整帧分配实测，不将这些源码观察宣称为零开销。

无几何保护的先期版本使短板接触地面均值增加 10.21%，已拒绝。保护版本的数据：

`E:/VividRP_GI_Evidence/ScalarAnchorPreFilterGuarded-20261114-090305-272/invalid-input-analysis.json`

- 静止 A/A 的 7,428 份数据相同；保护范围内 6,154 个探针的完整 SH 在 256 帧内不变。
- 最终静止方差：左墙 −17.53%、右墙 −20.36%、背墙 −2.38%、天花板 −2.42%，地面 +0.012%。没有声称所有区域都改善。
- 五个平面均值变化绝对值不超过 0.137%；短板地面接触区域不变。独立高样本同渲染器网格参考下，各平面候选均值误差绝对值最大约 0.457%；它不是物理真值参考。
- 静止原始光照、anchor、几何和统计相同；当前 Resolve 覆盖率相同。
- **运动输入不合格**：原始命中光照已经不同，不能把最终差值归因于候选。`pose-reference-analysis-unqualified.json` 保留原始未合格结果，不能用于通过或拒绝运动质量。

## 生产者 A/A 定位

回正相机并完整清理该相机历史后，两个都关闭候选的原运动捕帧仍不一致：

`E:/VividRP_GI_Evidence/ScalarAnchorPreFilterMotion-20261114-092821-631/`

随后短冷启动 A/A 前 96 帧的有效数据相同；实际掩码检查确认其射线纹理差异只发生于未使用的无效探针槽位：

`E:/VividRP_GI_Evidence/ScreenProbeProducerCold-20261114-094512-788/`

新诊断 `ScreenProbeProducerHistoryCapture.cs` 在准备和预热期间也读回逐阶段数据，而非只在测量期捕获。两组各 1,351 帧：264 帧时缓存准备完毕，随后 1,024 帧静止预热及 64 帧运动。记录、结果与捕帧时的源码校验：

`E:/VividRP_GI_Evidence/ScreenProbeProducerHistory-20261114-095739-898/producer-analysis.json`

- 13,683 份配对读回中 13,681 份相同。几何、proposal、raw、anchor、统计、composite、逐方向时域及空间结果、SH、采集到的 Resolve / 像素时域片段和 CPU 状态相同。该生产者诊断未采集 À-Trous 后的最终像素，不能用于最终画质验收。
- 两份射线纹理有字节差异；本次工具没有保存对应的 A 侧载荷，无法做有效几何掩码。因此报告明确 `rayPayloadQualified=false`，不能借用前一个短冷启动实验的结论将本次差异解释为无效槽位。工具随后补上两侧射线及对应几何的保存，修改后的保存策略尚未运行。
- 捕帧前后 2,780 个受检文件相同。该校验先于上述诊断保存策略的后续修改；证据目录内保留原快照。
- 此运行扩大了可复现的检查范围，但与原捕帧的读回安排不同，且仅含 64 帧运动。它没有解释原长轨迹 A/A 的差异，也不构成候选的运动验收。

本轮 `EmissionTraceConformance-20261114-095404-329` 同时复验生产 Radiosity、Far Probe 和远场缓存重连：Off / Front / Back 均符合材质朝向，Back 正面约 200、背面 0，黑命中有效性和正距离保留。`EmissionSidednessState.cs` 确认全部回调、诊断 flags、frame override 已恢复，默认 bias 0.04，正常 8 阶 / 32 批，两场景未脏。

## 验证范围

以下补测完成后才启用专用配置。当前已验证同输入静止、完整固定轨迹、新露出、接触区、能量和统一发光响应；尚未完成 Sponza、Unreal、任意局部光照变化或稳定无噪点验收。未测 GPU 耗时；未在正在使用的 Editor 中运行 Unity Test Framework。

## 完整轨迹与合格 A/B 补测

`ScreenProbeReadbackSchedule-20261114-100647-433` 对比预热逐帧读回与不读回，两组各 1,543 帧，含完整 256 帧往返轨迹。3,142 份公共检查点的生产者阶段一致；六份射线纹理差异经两侧载荷与相同几何掩码确认，仅位于无效探针槽位。1,858 份跨工具生产者检查点也一致。不支持“预热读回本身导致输入差异”的假设；未解释旧运行的偶发差异。

原画质捕帧随后通过 2,658 份有效数据的 A/A。资格检查仍严格比较所有非射线载荷，并比较全部有效探针的三个射线数据平面；仅忽略消费者明确不使用的无效接收点槽位。静止与新运动捕帧的 2,779 个生产/夹具文件、实际 DX12 kernel 指纹一致，合并器保留旧失败与新 payload 来源，不改写原数据：

`E:/VividRP_GI_Evidence/ScalarAnchorPreFilterCombined-20261114-102230-265945/`

完整 A/B 的几何、raw、未过滤 anchor、统计和源缓存一致。静止结果保持上表；运动同姿态差值 RMS 从 0.564606 降为 0.554566（−1.778%，条件 95% 区间 [−2.146%, −1.450%]），绝对 P99 均为 1.93408。28,104 个新露出像素和 73 个新露出探针仍拒绝旧历史。独立姿态参考的 1,459 个近期露出像素，RMSE / P99 / max 均逐项相同（1.208741 / 2.626096 / 2.921705）。这不是跨场景或物理真值验收。

像素 Resolve 参考下，后墙小 ROI 的既有平均能量差仍约 −1.2%（基线 −1.184%、候选 −1.203%）；没有以全平面网格均值通过代替这项局部限制。对照图已修正 GPU 读回的上下显示方向，统计坐标及原始数据不变。

光照响应首轮 `ScalarAnchorPreFilterRelight-20261114-102610-369` 捕帧完整、2,781 个受检文件不变，但 **降光组在阶跃前的 raw 已不匹配**，严格分析拒绝该组，保存 `invalid-input-analysis.json`。升光组的阶跃前输入相同。不能把降光差值解释为滤波响应或通过推广。

补测工具 `ScalarAnchorPreFilterRelightSettledCapture.cs` 在每组冷启动前要求 GPU 直接发光峰值为 200、完整直接源纹理连续八次相同，再清理 GI 历史。这个条件仅用于建立相同初始状态；测量中的升光/降光/关灯仍即时发生，不插入等待。

## 合格光照响应与保存

`E:/VividRP_GI_Evidence/ScalarAnchorPreFilterRelightSettled-20261114-103852-758/`

恒光/升光/降光/关灯四组全部完成，实际发光为 200 → 800 / 50 / 0。每组固定源完成后预热 512 帧，再采集 384 帧，于测量第 64 帧阶跃。所有组实际启用预滤波，变化组的阶跃前 raw 与恒光组一致，几何始终一致，2,781 个受检文件在捕帧前后相同。

初始准备阶段实际观察到直接源峰值 0、800、50 等旧帧值随后恢复为 200；四组最后的直接源 hash 相同。这个控制条件不等于修复了所有旧运行的历史差异，也没有延迟测量中的实际灯光阶跃。

- 升光/降光五区最终输出首帧均值误差绝对值最大 0.01548%。
- 关灯后，五区 Resolve、像素时域及最终输出从第一帧起均为零，没有旧发光残留。
- 最终 Radiosity atlas 与恒光缓存乘物理倍率一致，归一化 RMS 差小于 0.000001%。
- 有限末段差异仍在：升光地面 −1.8284%，降光后墙 +1.0172%。源 atlas 比例正确，不将这些差异宣称为已消除或直接归因于随机误差。

`AuditScalarAnchorPreFilter.py` 核对完整输入、静止噪声与能量、独立网格参考、接触区、完整运动、近期露出和实际光照阶跃，并将地面微小方差增加等限制写入组合目录的 `quality-audit.json`。`PromoteCornellAnchorPreFilter.cs` 进一步核对证据 hash 和当前 Runtime/Shader 源码，备份后通过 Unity API 保存专用 Profile；异常时恢复原设置。备份及 `profile-promotion.json` 位于组合目录。

最终正常渲染确认预滤波实际开启，scratch 为 120×136 RGBA16F，GPU 最大 anchor age 为 128，6,055 个探针至少达到 96 帧；16 px / 8 rays、四轮、正常 8 阶 / 32 批及 bias=0.04 均正确。场景和共享 Profile 未脏、捕帧回调为空、诊断 flags=0、override=-1。构建器已实际重编译，C# 无编译失败，结果在 `post-promotion-verification.json`。

随后把新增的 `RWTexture2D.Load(int3(..., 0))` 改为其实际要求的 `int2` 坐标，去掉无用的零 mip 分量及相关截断警告。修正前后的 **11 个实际 D3D12 kernel 编译数据逐字节相同**，并核对原捕帧的两个活动 kernel 指纹；`warning-cleanup-equivalence.json` 单独记录这次源码演变，不改写捕帧快照。最终无 shader 错误，53 条警告为既有代码诊断及新入口继承的旧循环变量警告；详见 `post-warning-cleanup-verification.json`。

生产 Radiosity、Far Probe 与远场重连的 Off / Front / Back 矩阵再次通过：`EmissionTraceConformance-20261114-105516-958`。Back 正面约 200、背面为零，黑命中保留有效性与正距离，实际材质值未改，64 B 材质 / 48 B 节点布局仍正确。未测 GPU 时间或整帧分配，未在正在使用的 Editor 中启动 Unity Test Framework。
