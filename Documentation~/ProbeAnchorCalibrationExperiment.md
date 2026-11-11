# Cornell Box 探针 anchor / SH 校准方差实验

2026-11-11；Unity 7000.0.0a16，Direct3D12，RTX 5070 Ti。

## 结论

本次固定机位平面测试**没有观察到方向滤波降低的方差在 SH 校准时重新出现**。开启重要性引导时，天花板的时间残差方差依次为 `251.947 → 144.964 → 66.616`：方向空间滤波降低 42.46%，校准再降低 54.05%。关闭引导时结论相同。

校准后 SH 在探针自身法线方向的求值与 temporal anchor 一致；两者天花板亮度差的 RMS 为 `0.01120`，相对于 temporal anchor 均值约 `0.0200%`。差异包含两份独立 FP16 存储与 SH 求值的量化误差。因此应保留当前能量校准。残余噪声需要继续沿 temporal anchor 的输入、覆盖率和历史有效性定位，不能据此削弱校准或直接使用未校准 SH。

这里测量的是探针阶段的线性间接光，**不是经过全分辨率插值、像素时域和 À-trous 后的最终画面噪声**。结论只覆盖这些固定平面在自身法线方向的重建，不外推到运动遮挡边缘或任意方向的 SH 求值。

## 实验条件与导出点

使用 `SurfaceCacheCornellBoxBright` 原有 Cornell Comparison Camera：位置 `(1.62, 1.66, -1.7)`，旋转四元数 `(0.03591185, -0.16398631, 0.005973937, 0.98579067)`。

- 1920×1080；太阳方向光 3600 lx；EV8；临时关闭相机 AA 和 Local Exposure。
- 原生 Screen Probe Gather，Hybrid，16 px 网格，每探针 8 条射线；未启用 ReSTIR。
- Adaptive 5×5 探针滤波、两轮 À-trous、像素历史上限 16；Surface Cache B、37 张卡片、8 阶反弹，小场景全量更新阈值 128。
- 每组独立重建 pass，预热 128 帧，记录连续 256 帧；固定射线帧号序列。三组记录的 768 个 VP 矩阵完全相同。
- 天花板 168 个、后墙 230 个、地面 65 个固定基础探针。逐帧验证几何有效、位置不动、法线属于预期平面。
- 未修改场景、Profile、材质或默认质量参数。实验使用临时 Profile；结束后已恢复现场并关闭诊断钩子。

GPU readback 的 Y 方向与截图不同。最终中央区域使用探针坐标：天花板 `(48,59,24,7)`、后墙 `(76,18,10,23)`、地面 `(27,3,13,5)`；`geometry.f32` 验证对应天花板世界 Y≈3、地面世界 Y≈0。第一批定位采样保存在 `capture/`，最终表格和分析只使用 `central/`，避免按截图坐标误标平面。

在 `ScreenProbeConvertToIrradianceCS` 的同一次执行中输出：

| 输出 | 数据来源 |
|---|---|
| raw anchor | 当前 Composite 的有效追踪辐亮度加权均值，尚未经过标量 anchor 时域 |
| temporal anchor | `AccumulateDirectionalProbeAnchor` 后实际写入 `m_Probes` 的 RGB / coverage |
| 方向滤波前 SH | 用相同 directional temporal 输入、相同 16 个方向投影，作为未执行方向空间滤波的对照 |
| 校准前 SH | 当前方向空间滤波和 borrowed-shape 能量归一化后、常数项校准前的实际 SH |
| 校准后 SH | 原管线实际使用的 SH |

诊断数据只在 Editor 的 `EditorRecordProbeAnchorDiagnostics` 与现有捕获钩子同时启用时分配。`m_FilteredProbes` 从 7 个平面扩展为 22 个：`[0..6]` 校准后 SH、`[7..13]` 校准前 SH、`[14..20]` 方向滤波前 SH、`[21]` raw anchor。7 平面 SH 依次为 `r0/g0/b0/r1/g1/b1/(c.rgb,coverage)`，保持现有 Cartesian SH3 打包。

## 统计口径

从完整 SH 系数按各探针实际解码法线求值，使用 `Y = 0.2126R + 0.7152G + 0.0722B`。五个阶段统一乘以同一帧的 temporal anchor coverage，以隔离校准与方向滤波的影响；分析文件同时保留不乘 coverage 的 conditional RGB 指标和原始 coverage。

令 `e[t,p] = Y[t,p] - mean_t(Y[t,p])`，去除每个探针的静态光照梯度。时间残差方差为 `mean(e²)`，RMS 为其平方根。平面残差方差另去掉每帧平面平均残差；低频 RMS 对残差图做 σ=1 个探针的 Gaussian 低通后统计。它们都不把真实空间明暗梯度直接算作噪声。

## 中央天花板结果

默认重要性引导开启：

| 阶段 | 平均 Y | 时间残差方差 | 时间 RMS | 低频 RMS |
|---|---:|---:|---:|---:|
| raw anchor | 56.4255 | 683.6776 | 26.1472 | 8.5789 |
| temporal anchor | 55.9730 | 66.6055 | 8.1612 | 2.7441 |
| 方向滤波前 SH | 60.0681 | 251.9465 | 15.8728 | 5.1999 |
| 校准前 SH | 60.1106 | 144.9642 | 12.0401 | 4.5458 |
| 校准后 SH | 55.9733 | 66.6157 | 8.1618 | 2.7443 |

方向滤波前后平均能量几乎不变。校准前 SH 比 temporal anchor 亮约 7.4%，不能把它作为无偏能量参考。去掉 coverage 加权后，相对 RMS 仍从 `25.77% → 19.06% → 13.75%` 下降，结论并非仅由整体亮度下降产生。

去掉每帧平面平均残差后，方差为 `250.4644 → 143.5977 → 66.2025`；低频 RMS 为 `5.1999 → 4.5458 → 2.7443`。没有发现“总方差下降，但低频沸腾重新变强”的反例。

关闭重要性引导时：

| 阶段 | 平均 Y | 时间残差方差 | 时间 RMS | 低频 RMS |
|---|---:|---:|---:|---:|
| raw anchor | 55.8075 | 643.8669 | 25.3745 | 8.3011 |
| temporal anchor | 55.6887 | 64.6559 | 8.0409 | 2.7134 |
| 方向滤波前 SH | 58.8861 | 252.0759 | 15.8769 | 5.2686 |
| 校准前 SH | 58.9043 | 142.5465 | 11.9393 | 4.6049 |
| 校准后 SH | 55.6891 | 64.6654 | 8.0415 | 2.7137 |

配对的 32 帧 block bootstrap（4096 次重采样）给出方差比的 95% 区间：

| 引导 | 方向滤波后 / 前 | 校准后 / 前 |
|---|---:|---:|
| 开启 | 0.5754 [0.5684, 0.5837] | 0.4595 [0.4482, 0.4700] |
| 关闭 | 0.5655 [0.5596, 0.5709] | 0.4536 [0.4445, 0.4611] |

这是单机位有限样本的相关帧统计，不是高采样真值或全场景误差界限。

## 其他平面与校准恒等式

| 区域 / 引导 | 方向滤波前方差 | 校准前方差 | 校准后方差 |
|---|---:|---:|---:|
| 后墙 / 开启 | 61.4740 | 46.5682 | 12.3300 |
| 后墙 / 关闭 | 60.0916 | 47.0752 | 13.3220 |
| 地面 / 开启 | 256.5213 | 215.2328 | 61.5508 |
| 地面 / 关闭 | 250.9333 | 214.8959 | 62.2430 |

实际校准等价于：

```text
C = temporalAnchor.rgb - Evaluate(SH_pre, N_probe)
SH_post.constant.rgb += C
Evaluate(SH_post, N_probe) = temporalAnchor.rgb   // FP16 存储前
```

对全部记录，校准前后只有 RGB 常数项改变，其他系数与 coverage 逐位相同。开启引导的天花板上，校正项自身方差为 `98.0437`，与校准前残差的两倍协方差为 `-176.3922`，满足：

```text
V_post = 144.9642 + 98.0437 - 176.3922 = 66.6157
```

因此不能单看“校正项有噪声”就断定它会增加最终噪声；这里负协方差使总方差明显下降。

## 诊断不干扰正式输出的验证

独立冷启动的 diagnostic off / on 组没有逐位一致，尽管接收几何和 VP 完全一致；本实验没有定位独立复跑的其他状态差异，也没有将独立组间差当作校准效果。

为排除这一干扰，另做 128 帧预热、32 帧记录：在同帧先捕获正式输出，再保持所有 shader 输入不变，将诊断开关设为 0 重放 Convert。同输入下 32 帧的正式 7 平面 SH 和 temporal anchor **全部逐位相同，差异元素为 0**。主要方差结论来自同一 dispatch 内的阶段对比，无需依赖独立运行达到确定性。

## 文件与复现

本机输出根目录：`E:/VividRP_Reborn/Logs/ProbeAnchorCalibration-20261111/`。

- `central/`：最终逐帧 GPU 原始数据、几何、参数记录、最终截图；所有运行错误记录为空。
- `same-input/`：32 帧诊断开关重放结果。
- `analysis/guide-on-probes.npz`、`guide-off-probes.npz`：实际 raw/temporal anchor、三份完整 SH、几何、法线及各阶段法线求值 RGB。
- `analysis/probe_samples.csv`：逐帧逐探针五阶段亮度与 coverage；`metrics.csv`、`summary.json` 包含完整指标。
- `analysis/ceiling-stages.png`：五阶段的时间标准差图与最后一帧残差图，每行统一色标。
- `central.cs`、`same-input.cs`：实际执行的 Unity CLI fixture；`editor-verification.json`：现场恢复与 shader 消息核验。

NPZ 中 anchor 为 `[frame, point, RGBA]`；SH 为 `[frame, 7, point, RGBA]`；`stage_normal_rgb` 为 `[5, frame, point, RGB]`，阶段顺序同上。点顺序由 `central/regions.csv` 的 offset 定义。数据保留 GPU FP16 系数，不是由 RGB 图重新拟合出的 SH。

离线重算（依赖 numpy、scipy、matplotlib）：

```powershell
python Tools~/Diagnostics/ProbeAnchorCalibration.py `
  E:/VividRP_Reborn/Logs/ProbeAnchorCalibration-20261111/central `
  E:/VividRP_Reborn/Logs/ProbeAnchorCalibration-20261111/analysis `
  --same-input E:/VividRP_Reborn/Logs/ProbeAnchorCalibration-20261111/same-input
```

12 个相关 compute kernel 的 DXIL / SPIR-V 编译全部通过；Unity C# 导入和实际 D3D12 捕获通过。Unity 当前 shader 消息只有已有的 D3D11 `plane` 循环变量遮蔽警告，没有错误。未运行 Unity Test Framework（Editor 正在交互运行），也未测量开启诊断时的 GPU 性能。

## 下一步定位

保留能量校准与当前默认参数。先记录 anchor 的有效命中类别、条件辐亮度、原始 / 时域 coverage、重投影接受权重及有效历史长度，区分高方差射线贡献、覆盖波动与历史拒绝。中央天花板 temporal coverage 平均约 0.774；这是有效权重，不能直接解释成约 22.6% 的几何没有覆盖。

在有数据支持后再选择对 anchor 做保边时空滤波或调整采样 / 历史。运动时的边缘漏光、拖影和能量偏差仍需另外验证，本次不修改降噪行为。
