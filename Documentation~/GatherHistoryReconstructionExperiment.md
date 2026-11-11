# Cornell Box：固定高样本 Radiosity 后的 Gather 历史与重建

2026-11-11，Unity 7000.0.0a16 / D3D12，本机 RTX 5070 Ti。接续 `RadiosityFixedPatternExperiment.md`，本轮固定源缓存，定位天花板剩余时间噪声。

## 实验约束与结论

- 原生 Screen Probe Gather，**16 px 网格，每探针 8 条射线**，没有增加 Gather 样本。
- Radiosity **8 阶、每卡片 texel 4096 条射线、seed 0**，37 张卡片；收敛后测量阶段每帧更新卡片数为 0。
- 1920×1080，Cornell Bright 原机位，太阳 3600 lux、固定 EV100=8，AA / Local Exposure 关闭。
- 各组均使用现有空间 anchor、Adaptive 5×5、2-pass À-trous；采样引导关闭以统一采样序列。空间 anchor 仍是显式实验选项，未改其默认值。
- 每组静止记录 512 帧；首组预热 424 帧，后续组预热 128 帧，使用同一 pass 保留已收敛 Radiosity，单独清 Gather 历史。
- 天花板选取 168 个探针（x=48,y=59,w=24,h=7），以及 96×48 的 GI 像素区域（x=912,y=970）。像素坐标沿 GPU texture 原始方向。
- 分阶段导出 raw / temporal anchor、校准前后 SH、Resolve、像素 Temporal、第一轮和最终 À-trous、像素历史年龄、方向缓存元数据。

**已保留三项原生路径修正：历史中心坐标校正、半精度回写显式最近偶数舍入、探针尺度的历史裁剪方差。** 默认 anchor blend 仍为 0.85，默认像素历史上限仍为 16，插值抖动保持开启。没有将诊断中的 0.97 长 anchor 历史设为默认。

## 像素历史没有大量丢失，裁剪却削弱了有效历史

旧路径静止 ROI 的历史接受率为 **100%**，年龄均值 15.683，P5 / P50 / P95 为 15 / 16 / 16。几何拒绝不是本区域主要问题。

| 隔离项 | 最终 GI 时间 RMS | 说明 |
|---|---:|---|
| 旧行为，16 帧 | 1.8662 | 基线 |
| 仅改像素上限为 32 帧 | 1.6270 | 实际年龄均值 29.785 |
| 绕过历史裁剪 | 1.5990 | 只用于定位；不取消正式路径裁剪 |
| 关闭插值 jitter | 1.9742 | 反而更差，保留 jitter |

16 px 探针插值后的相关噪声跨越多个像素，3×3 像素邻域的方差偏小，历史即使通过深度 / 法线验证也会被过紧的颜色界限推回当前帧。按静止像素估计的有效当前帧权重，旧行为约 0.09084；修正后约 0.06865，更接近 16 帧历史的响应。此回归估计仅描述这段静止数据，不等同于算法显式混合系数。

新增 8 个距离为半探针间距（8 px）的采样，仅扩展 YCoCg 方差界限，仍以细邻域均值为中心；深度平面、法线、材质类别、Albedo、coverage 和积分模式均参与验证。没有把远邻 RGB 直接混入历史，也没有放松新露出区域的几何拒绝。

## 更长历史暴露了半精度反馈偏移

直接将 anchor blend 提到 0.97，并校正历史中心后，最终 RMS 可降到 0.9688，但校准 SH 平均能量相对参考偏低 **2.2647%**。只看噪声会误判为成功。

相同参数下，旧半精度 anchor 均值 54.1856，FP32 anchor 均值 55.4202。独立 GPU 写入实验进一步定位到本机路径的转换：直接 typed R16F store 和单纯 `f16tof32(f32tof16(x))` 都表现为朝零截断，简单转换往返没有解决偏移。

显式构造相邻 half 值、比较上下距离并处理偶数 tie，随后写入可精确表示的 half 数值。GPU 对照包含普通值、half 中点、次正规值、HDR 值，共 256 组正负样本；显式舍入以及后续 R16F store **全部匹配 CPU 最近偶数结果**。直接 store 的匹配率为 50.39%。这是本机 D3D12 实测，不能据此断言所有设备或后端均有同样表现。

| 0.97 长历史、校正中心 | Anchor 平均 Y | 最终时间 RMS |
|---|---:|---:|
| 旧半精度回写 | 54.1856 | 0.9688 |
| 显式最近偶数半精度回写 | 55.4256 | 1.0093 |
| FP32 anchor 对照 | 55.4202 | 1.0062 |

修正后半精度 anchor 与 FP32 均值仅相差 **0.0098%**。修正应用于原生方向 temporal、anchor feedback 和原生全分辨率像素历史回写；packed 像素历史元数据在量化 coverage 前生成，避免额外损失。保持原有 RGBA16F 常规资源格式，FP32 仅用于 Editor 隔离对照。此修正主要保证反馈能量，没有把舍入修正本身宣称为降噪来源。

## 历史坐标与最终默认行为 A/B

探针接收像素采用整数 `tile * 16 + 8`，投影 UV 对应这个像素的中心，即 `+0.5`。旧历史反求公式遗漏了半像素，静止时产生 0.03125 个探针格的偏移。修正从 UV 减去半像素后反求，并处理最后一个不足 16 px、接收点被 clamp 的网格。实际 GPU 几何投影回像素中心的误差约为 0.00018 px。

最终 A/B 使用正式默认行为（诊断 flags=0），与恢复旧 store / 旧中心 / 细邻域裁剪的 flags=896 比较。其余参数完全相同，两组各 512 帧：

| 指标 | 旧行为 | 修正后 |
|---|---:|---:|
| Raw anchor 时间 RMS | 26.3728 | 26.3736 |
| Temporal anchor 时间 RMS | 8.0434 | 7.4373 |
| 校准 SH 时间 RMS | 5.0039 | 4.5825 |
| 全分辨率 Resolve 时间 RMS | 3.6644 | 3.3019 |
| 像素 Temporal 时间 RMS | 1.9166 | 1.6804 |
| 第一轮 À-trous 时间 RMS | 1.9016 | 1.6658 |
| 最终 GI 时间 RMS | **1.8663** | **1.6357** |
| 最终 GI 时间方差 | 3.4831 | 2.6755 |
| 最终 GI 平均 Y | 52.4968 | 52.9543 |
| 校准 SH 能量相对参考 | −0.5020% | **−0.0547%** |

最终 RMS 下降 **12.36%**，方差下降 **23.19%**。平均像素能量上升 0.87%，对应反馈偏移修正，没有用降低能量换噪声。能量参考来自上一轮同源缓存的 1024 帧原始 Gather，共 8192 条射线 / 探针，168 个探针平均 Y=55.4919354；各组有效卡片数据同时与这份参考源逐位校验。它是当前 SDF / 卡片实现的高样本参考，不是无误差的路径追踪真值。

该 ROI 仍有相关低频变化：修正后最终输出 lag-1 相关性 0.9914，低通时间 RMS 1.2925。不能声称天花板已经完全收敛。

## 固定移动轨迹：新露出区域的像素历史

两组各 128 帧，沿相机局部方向平移 `(0.28t,0,0.06t)`（世界高度保持不变）、yaw `−7t` 度，`t=sin(pi * frame/127)`，最后回到原机位。逐帧 VP 完全一致。

首次移动尝试在第 38 个测量帧被中止：太阳大气透射颜色从 `(2959.2573,2379.5547,1517.3578)` 变成 `(2959.26,2379.5605,1517.3579)`，使 Radiosity 的精确颜色比较失效，阶数从 8 归零。失败记录保留在 `motion/`，不混入统计。

为隔离 Gather，后续 `motion-stable/` 两组均临时关闭太阳 `interactsWithSky`，使用相同恒定 `(3600,3600,3600)` 输入，重新收敛后冻结。该移动实验的源与静止实验颜色不同；仅比较移动 A/B。结束后太阳设置已恢复，正式 `ReadLight` 未改动。

在板边 320×256 区域导出逐帧 packed depth / normal、Resolve、Temporal、history。用实际 VP 独立重投影，选取上一帧有更近前景、平面差 >7.5 cm、位置差 >10 cm 的明确新露出样本，排除边界半深度量化歧义。

| 指标 | 旧行为 | 修正后 |
|---|---:|---:|
| 明确新露出像素样本 / 涉及帧 | 12,570 / 114 | 12,570 / 114 |
| 继承 age>1 的比例 | 0% | 0% |
| 被标记 accepted 的比例 | 0% | 0% |
| Temporal 相对当前 Resolve 的 Y 误差 P99 / max | 0 / 0 | 0 / 0 |

两组源缓存首尾和有效卡片逐位一致，所有测量帧都是 8 阶、更新 0 卡片。该结果说明本轮修改没有让选定明确新露出像素继承旧像素历史；它不代表完整移动 GI 误差为零，也没有排除探针层历史残留或混合边界问题。

## 剩余定位方向

1. **逐方向历史的有效复用率**。本轮正常路径有效方向样本的年龄均值仅 1.544，P5 / P50 / P95 为 1 / 1 / 3；像素历史则接近 16。方向年龄计数的是有效抽样次数，不能直接等同于帧数。下一步应输出命中类别、endpoint 距离 / 方向验证的拒绝原因，区分真实遮挡变化和同一方向 bin 内的采样抖动，再调整一致性判断，避免串过板边。
2. **自适应 anchor 历史**。0.97 长历史在舍入修正后能显著降低静止噪声，但尚未验证冷启动、光照变化和探针移动残留。后续使用置信度 / 有效年龄控制 buildup，而非全局直接加大固定历史。
3. **太阳源缓存失效条件**。移动时大气衰减的微小浮点变化会重置高阶源缓存，应单独区分物理灯光变化和相机采样误差，带变化幅度证据制定失效策略。本轮保留该行为，避免隔离实验同时改变源缓存语义。

## 产物与验证

本机输出：`E:/VividRP_Reborn/Logs/GatherHistory-20261111/`。

- `final/`：最终默认行为 A/B、512 帧所有阶段、截图及灯光 / work / VP 记录。
- `ablations/`：像素历史长度、裁剪、jitter、anchor 历史与历史中心隔离。
- `rounding/`：简单 intrinsic 往返的负对照；其中历史文件名的 `rne` 不表示真正最近偶数。`roundtrip-source/` 保留当时源码。
- `nearest/`：真正最近偶数、FP32 anchor、32 帧等隔离对照。
- `quantization-nearest/`：256 组 GPU / CPU 舍入对照；`quantization/` 保留最初 64 组截断证据。
- `analysis/summary.json`、`metrics.csv`、`curves.npz`、`ceiling-history-stages.png`：静止完整结果。
- `motion-stable/motion-summary.json`：移动历史拒绝结果。`scripts/`、`final-source/`：实际脚本和本轮源码存档。
- `final-compile/shader-verification.txt`：18 个入口的 DXIL / SPIR-V 编译；`editor-verification.json`：现场恢复和 Unity shader 消息。

离线重算需要 numpy / scipy / matplotlib：

```powershell
python Tools~/Diagnostics/GatherHistoryExperiment.py `
  E:/VividRP_Reborn/Logs/GatherHistory-20261111 `
  E:/VividRP_Reborn/Logs/GatherHistory-20261111/analysis `
  --reference E:/VividRP_Reborn/Logs/RadiosityPattern-20261111/capture
python Tools~/Diagnostics/GatherHistoryMotion.py `
  E:/VividRP_Reborn/Logs/GatherHistory-20261111/motion-stable
```

18 个相关 kernel 的 DXIL / SPIR-V 编译通过，Unity C# 实际调用及 D3D12 静止 / 移动捕获通过，成功组错误记录为空。Unity shader 消息仅有现存 loop variable shadowing warning，没有 shader error。诊断钩子与 flags 已清理，原相机、Profile、太阳 `interactsWithSky` 已恢复，没有修改场景或 Profile 资产。未测 GPU 耗时或全帧托管分配；新增正式 C# 路径仅绑定缓存 ID / 值类型参数。未运行 Unity Test Framework，Editor 正在交互运行。
