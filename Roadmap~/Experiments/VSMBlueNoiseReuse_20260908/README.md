# SMRT 复用现有 BlueNoise 的可行性

2026-09-08，源码 `584636b5`。本轮只读源码、纹理和 Unity 绑定状态，并做离线采样分布分析；没有修改渲染代码、场景或 `.meta`。

**结论：现有资源、生命周期、RenderGraph 依赖登记、Compute 绑定和 BND 核心均可复用。先做保留 SMRT 分层的 BND 相位对照，不必立即创建专门 STBN 系统。** 但“可接入”不等于“确定能降噪”：本轮离线结果没有支持在 8 rays 下直接默认替换当前采样。如果实际 TSR 对照仍没有改善，再考虑增加真正的 STBN 数据，继续使用现有资源框架。

## 已有内容

| 内容 | 现场/源码核对 | 复用判断 |
|---|---|---|
| `Texture/BlueNoise` | 1/8/256 SPP 各一对 ranking / scrambling，外加 256×256 Owen-Sobol；tile 为 128×1024，编码 128×128 像素的八个维度 | 七张纹理均存在，已经绑定，支持直接复用 |
| `BlueNoiseResources` / `BlueNoise` | 初始化分配 RTHandle，跨帧导入 RenderGraph，集中释放；已有 `SupportsBnd256` | 不需要另一套生命周期，不需要重复加载纹理 |
| `IBlueNoiseConsumerPass` | `PassRecorder.SetupImportedHandles` 自动登记纹理/缓冲读取依赖 | SMRT 主 Resolve 和 Debug 可沿用这套机制 |
| `Bind(ComputeCommandBuffer, ComputeShader, kernel)` | 已有显式逐 kernel 绑定接口 | 不需要依赖别的 pass 的全局状态，也不必新增同功能接口 |
| 方向光光追 / SSR / 参考 PT | 已使用同一资源集合的不同索引方式 | 可参考资源接入，采样索引不能不加区别照搬 |

Unity 只读核对：七张纹理均为 `R8G8B8_UNorm`、mipmapCount=1、Point、sRGB=false、Uncompressed，当前全局绑定与相应资产一致。现有位域/XOR 采样所需的无损线性数据条件满足。[现场记录](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMBlueNoiseReuse_20260908/resource-state.json)

主要代码：[资源声明](E:/VividRP_Reborn/Packages/VividRP/Runtime/Core/Utility/BlueNoise/BlueNoiseResources.cs)、[资源管理与绑定](E:/VividRP_Reborn/Packages/VividRP/Runtime/Core/Utility/BlueNoise/BlueNoise.cs:278)、[依赖登记](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderGraph/PassRecorder.cs:308)、[采样函数](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Public/BlueNoise.hlsl)。

## 现有 BND 与 STBN 的边界

`BlueNoise.hlsl` 对应 Heitz 等人的 Owen-Sobol + ranking/scrambling 屏幕空间蓝噪声方案；目标是低差异样本序列和屏幕空间的蓝噪声误差。原作者区分 progressive 与 target-SPP 的优化方式，不能仅按函数名选目标样本数。[2019 原始论文页面](https://belcour.github.io/blog/research/publication/2019/06/17/sampling-bluenoise.html)、[作者补充说明](https://belcour.github.io/blog/supp/2019-sampling-bluenoise/index.html)

STBN 另对空间和时间维度的误差结构进行设计。现有资源没有提供这种联合优化的证明；`1SPPTemporal` 名字表示索引可以跨帧推进，不表示已经实现 STBN。反过来，也不能因为它不属于 STBN 就认定不可复用或必然更差。[STBN 原论文](https://research.nvidia.com/publication/2022-07_spatiotemporal-blue-noise-masks)

## 索引合同：最容易错误复用的地方

| 接口 | 实际行为 | 对 SMRT 的含义 |
|---|---|---|
| `GetBNDSequenceSample` / `256SPP` | `sampleIndex & 255`，使用 256-SPP ranking/scrambling | 可以跨帧使用；直接索引 `frame*R+ray` 时，R=4/8 分别每 64/32 帧循环 |
| `GetBNDSequenceSample1SPPTemporal` | 使用 1-SPP 资源，但样本索引保留到 255；本地 ranking1SPP 全零 | 可以做时间变化候选，但不是 8 rays 自动最优的选择 |
| `GetBNDSequenceSample1SPP` | 样本索引掩码为 0 | 固定像素/维度时没有时间变化 |
| `GetBNDSequenceSample8SPP` | 样本索引与 ranking 都限制在 0–7 | 直接传 `frame*8+ray` 会完全抹掉帧号，每帧重复八个样本；4 rays 时只交替两组 |

有限周期本身不是“非 STBN”的判定标准，STBN 纹理也可以循环；这里的风险在于错误索引让预期的时间积分退化成固定噪声。参考 PT 的跨 256 样本块置换也只是在避免完全重复，不能由此推出时空蓝噪声特性。

## 离线采样检查

使用真实 PNG 资源、按左下原点重排的 UNorm 读取和 HLSL 等价索引，在 128×128 像素、256 帧上检查 16 个具有解析答案的圆盘半平面积分。比较当前 SMRT 光盘采样、逐射线 BND、固定 8SPP，以及用 BND 驱动当前分层相位的候选。数学计算为 CPU float32 近似，未验证 GPU 逐位一致性。

这里仅隔离光盘二维积分：**没有深度场、接收原点随机化、页表、TSR 裁剪、曝光或运动**。不代表真实 Sponza 效果或 GPU 性能。表中是 16 个积分各自误差的中位数，数值越小越好。

| 8 rays | 单帧 RMSE | 16 帧均值 RMSE | 理想 EMA RMSE | EMA 后 3×3 tent RMSE |
|---|---:|---:|---:|---:|
| 当前分层采样 | 0.06540 | 0.01046 | 0.00501 | 0.00188 |
| 逐射线 BND256 | 0.08425 | 0.00978 | 0.00706 | 0.00243 |
| BND256 驱动分层相位 | 0.06543 | 0.01098 | 0.00565 | 0.00203 |
| 直接使用 8SPP 掩码 | 0.08396 | 0.08396 | 0.08396 | 0.02546 |

EMA 权重为 0.96，运行 256 帧只统计末 16 帧；3×3 tent 是用于判断可过滤性的线性模型，不是本地 TSR，也不是拟新增的默认模糊核。16 帧均值与 EMA 是不同的重建方式，不能只选其中有利的一列。

4 rays 时，当前/逐射线 BND256/相位 BND256 的“EMA 后 tent”误差为 0.00360 / 0.00340 / 0.00335，存在小幅改善；8 rays 下则没有胜出。相位复用保留了原有单帧分层质量，直接逐射线替换的单帧误差更高。BND 的低频能量占比可以下降，但不能把占比下降直接解释为全部绝对误差减少；完整 JSON 同时记录了低频段的绝对 RMSE。

因此现有 BND 有明确的接入与实验价值，却没有“接上就能解决 GIF 噪声”的证据。也不应仅以这个二维积分否定其在接收覆盖、空间过滤和真实 TSR 下的潜在收益。

可复现脚本：[analyze_sampling.py](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMBlueNoiseReuse_20260908/analyze_sampling.py)；数据、资源校验和与全部候选：[sampling-analysis.json](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMBlueNoiseReuse_20260908/sampling-analysis.json)。

## 最小复用方案

首个候选使用现有 `GetBNDSequenceSample(pixel, frameIndex, dimension)` 生成每像素随机相位，保留现有 `sqrt((ray+phase.x)/rayCount)` 径向分层及角向间隔。建议光盘维度 0/1，接收原点维度 2/3；原点仍保留 Hammersley 覆盖结构。每个相位按像素构建一次，不必按每条射线重复读取相同的 ranking/scrambling。四维联合行为尚需验证，不能从本轮二维结果外推。

该映射使用完整 256 帧索引，不需要移动像素地址或逐帧重新哈希破坏 tile 的邻域关系。相位方式有两点优势：保留当前射线分层，且不同 ray count 都可沿用同一帧相位。它是较小的复用改动，不保证优于直接 BND 或当前序列，需保留 A/B 开关验证。

接入需要的改动范围：

1. CSM Resolve / VSM Debug 声明 `IBlueNoiseConsumerPass`，对实际采样 kernel 调用现有显式 Compute 绑定；资源就绪可沿用 `SupportsBnd256`。
2. `VSMSMRT.hlsl` 引入现有头文件，仅调整相位生成。保留 DDA 薄遮挡遍历、bias、最大支持范围、父层与 PCF 回退。不能因为换了噪声跳过深度格点。
3. 复现工具和 GPU fixture 显式绑定同一组纹理；当前主 Resolve 尚未声明这些读取依赖，不能仅插入 HLSL 调用就依赖全局纹理恰好存在。
4. 无需手改 `PipelineResources.asset`：本轮七张资源已经通过现有同步链可用。无需新增运行时纹理、每帧数组或托管分配；真正接入后仍需预热并验证零分配。

按此前约定，以 4096 做主要画质验收，2048 做症状复现，同时覆盖 0.5° 与用户 7.1°。固定射线数、步数、页面覆盖和 TSR 配置，分开测光盘随机化与原点随机化。观测原始可见度、TSR 的裁剪损失/实际权重和动态光照响应，不能只用静止截图或 GIF 噪点判断。

## 何时再增加专门 STBN

若现有 BND 相位与直接采样在真实 TSR 对照下都不能降低噪声或改善收敛，再加入专门 STBN 作为第三个候选。届时仍复用 `BlueNoiseResources`、初始化/导入/释放及 pass 依赖登记，只扩展所需纹理、绑定和维度映射；不另建重复的噪声子系统。STBN 也必须保留薄杆、侧墙自阴影、缺页与真实光照变化的验收，它不能修复这些系统性误差。
