# Screen Probe 方差用途隔离与当前帧预滤波

本轮确认方差通过两条途径影响最终波动：照明连续性判断，以及亮度滤波和传播方差的幅度。冻结任一用途都有收益，但这些影响存在耦合。新的密集5×5当前帧预滤波把天花板方差引导的时域 RMS 降低约64%–65%，最终像素方差却只下降1.88%–2.71%，未达到事前门槛。**默认 Runtime、shader、场景和 Profile 均未修改。**

证据目录：`E:/VividRP_GI_Evidence/ScreenProbeVarianceRoleIsolation-20261115-192000`。工具起点为 `377f4063`，生产渲染仍为 `7b9d8836`。Unity 7000.0.0a16 / D3D12 / RTX 5070 Ti；Cornell 原生 Gather、16 px / 每探针8条射线、FP32 anchor age512、像素历史上限32、4-pass À-trous、bias=0.04。冻结 Radiosity 仍为8阶 / 64样本 / 32批，SHA-256 为 `40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8`。

## 两种用途的同帧隔离

`BuildScreenProbeMomentChannelIsolation.py --variance-role guard|magnitude` 在原有单通道隔离上增加一份实际 moments 的只读绑定。每组两次同序列重复，每次预热1,024帧、测量128帧；只有 step4/8 的指定用途读取首个测量帧的方差。均值、年龄、覆盖率、未选用途的方差输入以及 step1/2 均继续读取当前帧。

- `guard`：只固定两个连续性尺度表达式使用的方差，其余原始方差输入保持当前值。连续性权重同时影响 `nativePlaneTrust` 和方差预滤波的 tap 权重，因此派生幅度仍可随连续性权重改变。
- `magnitude`：两个连续性尺度表达式继续读取当前方差，其余幅度输入读取固定方差。

两组均通过混合输入36,962,304个 half-word 的逐值核对、冻结源每帧 hash、首测帧输出身份，以及1,024项复制照明和1,024项实际渲染器/A/A输出 hash 检查。五个 DX12 程序、实际 C# 脚本和两个内存 shader 源码固定身份。该证据覆盖记录的四个 ROI，不代表未保存的完整画面。

正数表示最终消耗 GI 的时域方差下降，负数表示增加。重复仍使用方向序列1733；`guard` 的两个实际最终输出重复逐值相同，不是独立随机证据。

| 固定用途 | 天花板，两次重复 | 地面，两次重复 | 背墙，两次重复 |
|---|---:|---:|---:|
| 连续性判断 | +4.46% / +4.46% | −0.05% / −0.05% | +1.53% / +1.53% |
| 方差幅度 | +7.08% / +9.32% | −0.52% / −2.57% | −0.22% / +0.02% |

这些是相对各自同帧实际输出的敏感性，不是可相加的噪声贡献比例。冻结输入未用于生产，也没有将幅度组天花板的收益掩盖地面的回退。

## 持续更新的密集预滤波候选

`dense-prefilter-plan.json` 在捕获前固定新算法和门槛。临时 `PrefilterSdfMomentVarianceCS` 在当前帧完整 moments 上执行一像素步长的密集5×5 binomial 预滤波，结合深度平面、法线、材质和历史成熟度；不以亮度差再次筛选方差。输出只修改方差，均值、年龄和覆盖率逐值保留。粗尺度两种用途读取预滤波结果，细尺度继续读取原 moments。输入不冻结、不增加射线，也不写回原统计或颜色历史。

`BuildScreenProbeVariancePrefilter.py` 复用同帧重放和资源清理。实际预滤波结果完整读回，验证均值/年龄/覆盖率的27,721,728个 half-word 不变、方差有限非负，以及 ROI 内部的输出不越过原密集5×5邻域最小/最大方差。GPU 检查通过。静态 ROI 中新历史样本数为0；新历史不变检查有单元测试，但本轮没有移动 GPU 验收，不能把零差异计数当成已经覆盖新露出区域。

事前采用门槛：两次天花板方差均下降至少5%，条件 block bootstrap 下界大于0；所有 ROI 平均消耗能量变化小于0.3%，其他 ROI 方差回退不超过2%；全部28个参考中心、256帧的最终 RGBA 必须逐 half-word 相同。后者是参考点不回退的输出界限，不能修复原外部参考的严格几何失败，也不代表绝对 Unreal 真值。

| 候选结果 | 第一次 | 第二次 |
|---|---:|---:|
| 天花板方差下降 | 2.71% | 1.88% |
| 地面方差下降 | 0.91% | 0.82% |
| 背墙方差下降 | 1.87% | 2.54% |
| 天花板平均能量变化 | −0.00869% | −0.00881% |
| 天花板方差引导时域 RMS | 0.0001494 → 0.0000516 | 0.0001507 → 0.0000542 |

输入、A/A、统计不变性、覆盖率和7,168个参考 RGBA 样本的输出身份检查通过；天花板仍未达到5%门槛。没有继续调参、运行独立序列复验、移动验收或性能 A/B，也没有安装进默认渲染器。原参考几何失败继续保留，没有放宽位置容差或删帧。

这个结果收窄了下一步：单纯稳定方差引导不能充分消除最终波动，应检查实际颜色历史与空间滤波结果如何复用，以提高有效光照样本量。尝试颜色历史空间反馈前，需要可比的同帧高采样能量参考和边界偏差检查；不能把更长历史或反复模糊直接当成质量改善。现有参考入口是 `ScalarAnchorGridReference.cs` 与 `AnalyzeScreenProbeResidualReference.py`，未来参考必须明确固定实际接收面几何。

## 验证、状态与存储

39项 Python 回归检查通过，包括用途协议/输入组合校验、实际写入源码 hash、既有结果不可覆盖、任一不应改变的统计通道、初生方差和有符号零的逐字身份，以及预滤波局部方差界限。DX12 内存 shader 编译、实际 GPU 重放和源码快照核验通过。活动 Editor 内未运行 Unity Test Framework；重放与读回改变了工作量，本轮无 GPU 性能结论。

恢复检查确认两个场景与 Profile 干净，回调清空、诊断为0、覆盖参数为−1。实际三个生产 DX12 程序 hash 与本轮前一致，保持16 px / 8 rays / native / 4-pass / age512。Editor 播放暂停，Frame Debugger关闭，PIX空闲。没有新 Unity 资产、手工 `.meta`、目录链接或 Editor重启。

本轮先归档了此前未采用的 `packed-dual1p75-holdout` 和已完成的 `frozen-variance-fine` 中间读回，释放后才继续捕获。其历史报告中的完整序列保护状态由本轮明确的归档日志取代；所有源码、协议、报告、逐文件删除 hash、首末帧和首测帧仍保留。本轮两个冻结用途组也已归档中间读回，最新密集预滤波完整数据受保护。归档组不再可做全序列重分析。

精确清理和存储量见本轮 `older-archive-apply.json`、`archive-*-apply.json` 与 `post-experiment-storage.json`。原高采样参考、已经接受的生产证据和既有有效移动对照未删除。

本轮清理1,951,997,952字节，约1.818 GiB；当前目录约0.511 GiB。沿用上一份清单并扣除明确归档字节、补计新增归档日志后，估算证据总量124.005 GiB，E盘剩余112.667 GiB，未触及125 / 64 GiB审查线。34份工具及依赖已独立归档；没有遍历链接目标。

## 复现

```powershell
python -B Tools~/Diagnostics/BuildScreenProbeMomentChannelIsolation.py NEW_GUARD --frozen-channel variance --variance-role guard --frozen-scales coarse
python -B Tools~/Diagnostics/BuildScreenProbeMomentChannelIsolation.py NEW_MAGNITUDE --frozen-channel variance --variance-role magnitude --frozen-scales coarse
python -B Tools~/Diagnostics/BuildScreenProbeVariancePrefilter.py NEW_PREFILTER
python -B Tools~/Diagnostics/AnalyzeScreenProbeAtrousExperiment.py NEW_PREFILTER --same-frame --matched-reference-subset --reference-root E:/VividRP_GI_Evidence/ScreenProbeResidualMap-20261115-104052-278383
```

每次先固定实际脚本、shader、协议与计划，再经活动 Unity CLI 捕获，结束立即核验源码。每组读回仍受512 MiB上限约束；`--matched-reference-subset` 只保留原参考失败后的诊断，不改判其失败状态。
