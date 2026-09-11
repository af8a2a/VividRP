# BND 联合索引接入与 4096 / 4×8 对照

已保留原有共享射线集合，并接入 **SMRT Joint Sampling** 开关，默认关闭。BND 使用 `f + floor(f/8)` 后，完整周期的固定残差降低约 **71%–75%**，TSR 输出的时间噪声增加约 **0.7%–0.8%**。它改善长期固定颗粒的覆盖，尚未成为“更安静”的默认方案。

光照阶跃中，屋檐区域降至恢复首帧 10% 误差的时点由 12 帧变为 16 帧；其余区域和相机回位阈值一致。综合这个取舍，原索引继续作为默认，联合索引作为已接入的可切换选项。

## 正式实现

在阴影 Volume 的 Cascaded Shadow Maps / SMRT 设置中新增 `virtualShadowMapSMRTJointSampling`。启用 SMRT、该选项及实际 TSR jitter 后生效；关闭选项或离开 TSR 时偏移为 0。沿用现有 BND 纹理资源。

CPU 从当前相机的实际 TSR 相位数 P 计算一个独立的 `_VSMSMRTSampleIndexOffset`：

```text
P > 1 且 P 为偶数：offset = floor(frame / P) & 255
否则：offset = 0
SMRT 的 BND index = (frame + offset) & 255
```

固定 jitter 相位下，偶数 P 的索引步长从 P 变为 P+1，后者与 256 互质。奇数 P 原本已经能遍历全部 256 个索引，保持原序列；直接对奇数 P 也多走一步会丢失覆盖。正式实现读取实际相位数，没有把 8 写死。

圆盘径向分层、golden-angle 角向关系、共享 receiver Hammersley 相位保留。Shader 仅在共享相位函数增加一个整数偏移；主 resolve 与 receiver debug 均显式绑定该偏移。原 `_CSMFrameIndex` 继续供其他阴影路径及页面标记使用。DDA、样本数量、偏移处理和 TSR 裁剪/混合使用原实现。

实际变更为 7 个文件：`VirtualShadowMapReceiverQuality.cs`、`CSMShadowResolvePass.cs`、`VSMReceiverDebugPass.cs`、`VSMSMRT.hlsl`、Volume 定义及其编辑器、已有 ReceiverQuality 测试。`production.patch` 是最终变更；`diagnostic.patch` 是已撤回的受控实验钩子，两者用途不同。

## 公平对照

固定 4096、4 rays × 8 steps、7.1° 光源角直径、10 世界单位最大射线长度、0.2 过渡、screen density 开启、target 1、LOD bias 0。NativeAA 1920×1080，8 jitter 相位，history 16，sharpen 0.483；固定曝光。ROI 和掩码沿用前轮的屋檐、横檐、拱门。

三组各记录 2080 帧，排除开头 32 帧，使用其后完整 2048 帧。每组预热至少 512 帧。BND 原索引完整周期为 256 帧；BND 联合为 2048 帧；STBN 联合为 512 帧。本窗口使每个 jitter 相位分别访问 BND 原索引 32 个、BND 联合 256 个、STBN 联合 64 个不同采样索引。

受控画质对照使用 **512 页池**，各组均 Active，overflow=0、missing=0。共 6240 个静态帧，其中 6144 帧进入完整周期统计；另有 2176 个动态帧。资源仅使用已有 BND 与公开 NVIDIA STBN vec2 RG8 图集；没有生成或替换图集，没有运行 FastNoise。来源与许可证随报告归档。

下面的三项依次为屋檐 / 横檐 / 拱门。负数表示较 BND 原索引下降。固定残差是各 jitter 相位的 raw visibility 均值对独立参考的 RMS；时间噪声是 TSR 输出亮度减去同 jitter 相位均值后的 RMS。

| 变体 | 固定残差变化 | TSR 输出时间噪声变化 |
|---|---|---|
| BND 原索引 | +0.00% / +0.00% / +0.00% | +0.00% / +0.00% / +0.00% |
| BND 联合索引 | -71.31% / -74.66% / -71.98% | +0.66% / +0.82% / +0.77% |
| STBN 联合索引 | -24.54% / -29.21% / -14.45% | -2.74% / -0.79% / -0.64% |

![静态对照与横檐收敛曲线](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/BNDJointIndex4096_20260911/static/comparison.png)

BND 联合索引的 raw 单帧总误差仅变化约 +0.03%–+0.07%，固定误差降低的同时，时变分量稍增。短窗口也不保证获益：256 帧时 BND 联合在部分区域尚不优于原索引；到 512 帧后开始明显降低固定残差。不能把 2048 帧平均的优势直接当作当前 16 帧 TSR 历史的即时降噪效果。

STBN 联合在本轮的固定残差下降约 14%–29%、TSR 时间噪声下降约 0.6%–2.7%，与前轮保守复测一致。BND 联合更擅长降低完整周期的固定误差，STBN 联合的最终时变噪声较小。本轮正式接入的是复用已有资源的 BND 索引开关。

参考沿用每 jitter 1024 个独立四维相位估计，针对同一 4×8 SMRT 估计器，不是几何真值。参考不确定性 RMS 约 0.00297 / 0.00253 / 0.00266。分别使用两半参考时，BND 联合的固定残差仍下降约 65%–69%。当前基线帧 256、287、511 的 raw shadow 与上一轮相同帧逐值一致，参数和 GPU VP 也匹配；详见 `reference-reuse-check.json`。

## 光照与运动恢复

两变体分别测量：静止接收面先使用 35% 光强，到 step 64 恢复；以及相机前 64 帧沿 `(1.5,0,1.5) * sin(pi * step / 63)` 往返，到 step 64 回原位。各组记录 544 帧，恢复后的输出每 4 帧采集一次，并与同变体静态帧配对。

| 场景 | 区域 | 原索引 / 联合：首次低于初始恢复误差的 10% | 联合前 32 帧平均误差变化 |
|---|---|---:|---:|
| 光照恢复 | 屋檐 | 12 / 16 帧 | -0.67% |
| 光照恢复 | 横檐 | 12 / 12 帧 | -0.04% |
| 光照恢复 | 拱门 | 12 / 12 帧 | +1.60% |
| 相机回位 | 屋檐 | 20 / 20 帧 | -0.68% |
| 相机回位 | 横檐 | 20 / 20 帧 | -0.51% |
| 相机回位 | 拱门 | 20 / 20 帧 | +0.24% |

![与配对静态结果相比的恢复误差](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/BNDJointIndex4096_20260911/dynamic/dynamic-response.png)

屋檐阈值推迟了一个观测间隔，不能报告为所有区域完全无回归。前 32 帧平均误差总体接近，也不能把单一阈值视为普遍的 4 帧延迟。动态与静态的 source 仍有少量差异，绝对相机帧号也不同；配对匹配的是实际 jitter、GPU VP、SMRT 采样帧与光源/相机条件。本轮没有改动 TSR 历史响应算法，也没有从尾部差异推导额外的降噪收益。

## 生产路径验证与页面压力

正式源码安装后，另跑“原索引 → 联合索引 → 原索引”的短验证，各 64 帧、预热至少 64 帧，使用真实相机帧号和生产 **256 页池**，没有采样帧覆盖或额外公开图集。联合阶段的实际索引偏移为 86–94；前后原索引阶段均为 0。记录的开关、实际帧号与索引公式全部匹配。

三组 GPU 诊断重算相对实际阴影的最大误差均小于 0.0005。每组首尾抽查 480×270 网格，约 124,500 个接收点全部找到可用层。关闭后的偏移清零和真实渲染绑定均验证通过。

**256 页池存在明显回退压力。** 三组分配计数均到达 256，请求计数约 446–452，overflow 计数为 190–196。诊断中 61,161–61,409 个网格点曾遇到缺页，包含成功回退的点；最终 unresolved 层计数为 0，层分布在三组间一致。这不是开关新增的页面压力，也不代表这些像素最终没有阴影。

最初的自动核验把“遇到缺页为 0”当作生产路径条件，因而触发断言；进一步检查最终层与 GPU 重算后，确认这是不适用于 256 页池的验证假设，完整证据保留在 `production/verification.json`。受控对照的 71%–75% 不能直接当作当前生产页池下的全屏改善，页面回退仍可能主导可见瑕疵。短生产验证不是画质统计或性能基准。

## 测试与交付状态

- Unity EditMode 实际执行 12 个定向测试，12/12 通过。覆盖每 jitter 遍历 256 索引、奇数周期、无效相位数、SMRT/TSR 开关及接近 `int.MaxValue` 的周期性质；原始 NUnit XML 已归档。
- 受控实验 46/46 DXC kernel/keyword 组合通过；正式路径相关 23/23 kernel 通过。
- Runtime、Editor、Editor.Tests 的正式版本和清理后版本均通过 Roslyn 编译，源码及 Bee response 稳定。编译工具自身的 `unityTestFrameworkRun=false` 仅指该工具不运行测试；实际测试证据为独立的 `joint-index-tests.xml`。
- 临时捕获源码、图集覆盖、采样帧覆盖与 512 页实验钩子已移除。最终保留七份正式源码变更、定向测试和报告；未提交 Git commit。
- 场景恢复到实验前有效 VSM 2048、原相机/光源/AA，联合开关默认 false；timeScale=1、captureDelta=0、background=false，无临时对象。实验中的 4096 为临时验收设置。

抓帧与 readback 有额外开销，本轮没有有效 GPU cost 测量。新增正式路径没有额外噪声纹理或射线样本；不能据此宣称已验证零性能开销。

## 复现入口

工作目录 `E:/VividRP_Reborn/Packages/VividRP`，基线提交 `caa227a5`。受控原始捕获保留在 `Temp~/bnd-joint-captures/20260910_154413_370`、`20260910_160504_958`；生产短验证在 `Temp~/bnd-joint-production-captures/20260910_161322_644`。本目录归档压缩帧记录和统计，没有重复复制数 GB 的 readback。

`diagnostic.patch` 基于原基线，不能叠加到最终实现上当作普通功能补丁。复现时使用隔离的原基线，放入对应的 `BNDJointProbe.cs.txt` 与 `VSMBNDCompareAudit.compute.txt`，按 `run-plan-static.json` 或 `run-plan-dynamic.json` 配置捕获。公开图集文件及哈希沿用前轮资源；在正常图形 Editor 的 Play Mode 下执行诊断菜单。

分析脚本从 `Temp~/bnd-joint` 使用：`analyze-static.py <static-root>`、`plot-static.py`；`analyze-dynamic.py <dynamic-root> <output> <static-root>`、`summarize-dynamic.py`。脚本固定了本机参考与输出目录，移机需要调整路径。正式实现的往返验证脚本独立归档，使用真实相机帧和 256 页池。最终开关无需这些脚本即可在 Volume 中使用。
