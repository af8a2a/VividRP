# 16 层当前配置的阶段计时结果

来源：`Temp~/vsm-baseline/cost/20260912_072119_372`，status 为 complete。实际配置沿用 2048 / 256 页 / 16 层；各阶段的实际设置与首个预热帧描述已归档至 `timing/<case>/`。按 spatial → fixed temporal → adaptive → adaptive → fixed temporal → spatial 的正反顺序，各预热 64 帧、观测 128 次，总计 768 次；每个观测窗口约 2.21–2.49 秒。没有在窗口内做 GPU 回读、截图或文件输出。

## 样本有效性

- 6 组各 128/128 观测通过解析：VSM 状态为 active；观测 camera frame 严格递增；Resolve、Trace、H 及对应 V/TemporalV 各恰好一次。没有无效或跨分支计数。
- PageCull 本场景每观测一次；它位于 StaticCasterCull 内，不能另加进父阶段。UnityCompatibilityRaster 和 InvalidateStatic 均零次，本场景无法用这些窗口评价它们的执行成本。
- LayoutRemap、ResetFeedback、DynamicCasterCull 有调用而 GPU 计时中位数为零，表示空工作/低于计时分辨率的观测，不应外推为该功能始终没有成本。
- 已记录的阶段具有多种不同 GPU 值，Resolve 各窗口有 125–128 个不同时间值；不是把一个 stale 值重复 128 次的明显情形。但 recorder 不暴露 GPU frame identity，仍不能仅凭一次 sample count 认证目标相机归属。
- 外层 Resolve 减去同一观测的 Trace + H + 实际 V/TemporalV，6 窗口中位差均为 0.00128 ms；所有逐观测差非负（最小约 0.000512 ms）。这支持 scope 嵌套一致性，不构成整帧认证。

## Resolve 分解

单位为每窗口中位数 ms；以下各列是独立分布，不能按行直接相加中位数。

| 模式/顺序 | Resolve 总 scope | Trace | H | V空间 / V时域 |
|---|---:|---:|---:|---:|
| Spatial 正向 | 10.759168 | 10.579840 | 0.064256 | 0.064768 |
| Spatial 反向 | 10.201088 | 10.062976 | 0.063488 | 0.064512 |
| Fixed temporal 正向 | 11.104000 | 10.866688 | 0.065280 | 0.157440 |
| Fixed temporal 反向 | 10.692736 | 10.438912 | 0.065024 | 0.157440 |
| Adaptive 正向 | 9.550336 | 9.327744 | 0.064512 | 0.157184 |
| Adaptive 反向 | 9.587328 | 9.349888 | 0.064768 | 0.157184 |

Trace/Resolve 的逐观测比值中位数为 97.66%–98.77%。此处 Trace 包含接收者选层、重试、求交和自适应历史查询，不是单独的16层深度加载成本。时域纵向核较空间纵向核约增加 0.092–0.093 ms；不能把不同模式的 Trace 变化归于纵向核。

反向相对正向：Spatial Resolve -5.19%，Fixed temporal -3.70%，Adaptive +0.39%。因此应保留方向窗口范围，不能忽略时间漂移给出一个高精度统一收益。按相同方向比较，自适应相对空间滤波 Resolve 下降约 6.0%–11.2%，相对固定射线时域下降约 10.3%–14.0%。这是本轮窗口内的比较；不能与旧报告 12.232 ms 直接作版本性能结论。

## Resolve 之前的阶段

六窗口中位数范围：MarkReceiverPages 0.6403–0.6413 ms；Allocate 0.2245 ms；ClearPhysicalPages 0.0899–0.0901 ms；StaticCasterCull 0.0677–0.0685 ms（其中 PageCull 0.0481–0.0489 ms）；StaticRaster 0.0069–0.0072 ms；DynamicRaster 0.0128–0.0133 ms。静态缓存暖态不足以评价运动后的 invalidation/raster 峰值，也没有 Unity 兼容路径样本。

## 可以计算什么样的联合时间

`stage-timing-joint.json` 明确列出 13 个不相互嵌套的外层 marker，对每次观测先求这些已执行区间的时间之和，再计算分布。排除了 PageCull 和 Resolve 的四个子 marker；没有把中位数相加。

| 模式 | 正向逐观测scope合计中位数 ms | 反向逐观测scope合计中位数 ms |
|---|---:|---:|
| Spatial | 11.937664 | 11.372800 |
| Fixed temporal | 12.297344 | 11.876992 |
| Adaptive | 10.693504 | 10.745088 |

这个数是“非嵌套已记录 GPU 区间的合计诊断”，不能称为完整 VSM 的墙钟耗时或关键路径。它缺少 scope 间未标记工作、调度/同步间隔和明确 GPU-frame/camera 身份；全帧其他工作也不在范围内。要认证实际整 VSM/相机预算，应在可归属的 GPU capture 中检查整条时间线或增加直接总区间，不能用这里的合计代替。

解析：`python analyze-stage-timing.py timing/*/stage-timing.csv --output stage-timing.json`（PowerShell 先枚举输入路径）。原始CSV、settings和frame已保留。`stage-timing.json` 给每marker分布、计数直方图与层级；`stage-timing-joint.json` 给逐观测合计与方向比较。