# Screen Probe 滤波引导的单通道与尺度隔离

本轮因果实验把天花板剩余波动进一步定位到方差引导：只冻结均值没有改善；只冻结方差时，两组方向序列的天花板方差分别下降约10.47%和13.41%。只在粗尺度使用固定方差仍下降约8.20%。**冻结首帧不是可用于移动与动态光照的实现，默认渲染器没有切换。** 持续更新的5×5粗尺度方差池候选只降低约2.4%，未达到事前门槛，未采用。

证据目录：`E:/VividRP_GI_Evidence/ScreenProbeMomentChannelIsolation-20261115-185000`。工具起点为 `97cd4e6e`，生产渲染器仍为 `7b9d8836`。Unity 7000.0.0a16 / D3D12 / RTX 5070 Ti，Cornell 原生 Gather、16 px / 每探针8条射线、FP32 anchor age512、像素历史上限32、4-pass À-trous、bias=0.04。沿用冻结的8阶 / 64样本 / 32批 Radiosity，SHA-256 为 `40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8`。

## 输入隔离与证明范围

`BuildScreenProbeMomentChannelIsolation.py` 复用已有同帧 wavelet 重放。每次重复预热1,024帧、测量128帧，在首个测量帧保存 moments，再由临时 `ComposeSdfMomentGuideCS` 生成参考输入：只将 `.x` 均值或 `.y` 中心方差替换成首帧值，其余通道逐值保留当前帧值。两条空间路径使用同一份复制的像素时域照明、接收面及当前历史；实际渲染器、颜色历史和 Surface Cache 不受修改。

每个完整单通道捕获在四个 ROI、全部256个测量帧读回36,962,304个混合 moments half-word，逐值核对所选通道来自首帧、其余来自当前帧，并核对冻结源的每帧 hash。首个测量帧的全部参考空间输出必须与实际输出精确相同。五个完整隔离捕获均通过；不是只检查声明、均值或百分位数。

每组还检查1,024项复制照明输入 hash、1,024项实际渲染器与 A/A wavelet 输出 hash，以及四个空间程序和混合 kernel 的 DX12 编译身份。源码、实际 C# 脚本、两个内存 shader 的文件字节 hash 均保留。临时纹理由重放工具拥有并在 `finally` 释放，没有创建 Unity 资产。

新工具只支持静态隔离，不提供移动验证开关。`--frozen-scales fine` 只在 step1/2 绑定混合输入，`coarse` 只在 step4/8 绑定，默认 `all` 为四轮。未选尺度和 A/A 使用实际 moments；每组实际执行脚本及尺度协议均固定身份。

## 同帧结果

正数表示最终消耗 GI 的时域方差下降，负数表示增加。表中的重复使用同一方向序列，不是两个独立随机序列；两个方差全尺度捕获及尺度隔离捕获的实际最终输出在各自两次重复中逐值相同。单通道影响存在耦合，不能作为可相加的方差分解。

| 静态隔离输入 | 序列 | 天花板，两次重复 | 地面，两次重复 | 光片边界，两次重复 |
|---|---:|---:|---:|---:|
| 仅均值固定，四轮 | 1733 | −0.73% / −0.82% | −0.07% / −0.14% | +1.65% / +1.45% |
| 仅方差固定，四轮 | 1733 | +10.47% / +10.47% | −1.08% / −1.08% | +19.09% / +19.09% |
| 仅方差固定，四轮 | 4361 | +13.41% / +13.41% | −6.54% / −6.54% | +19.54% / +19.54% |
| 仅细尺度方差固定 | 1733 | +3.16% / +3.16% | +0.11% / +0.11% | +18.46% / +18.46% |
| 仅粗尺度方差固定 | 1733 | +8.20% / +8.20% | −0.53% / −0.53% | +1.34% / +1.34% |

这些捕获中平均消耗能量的变化都很小，不证明绝对能量正确。序列4361的地面噪声回退也说明，不能把冻结方差简单作为默认优化。未重复运行已否决的双 half 均值编码；其先前实验虽然减少均值更新停滞，最终噪声未一致改善，见 [平面方差重建](ScreenProbeWaveletVariance.md)。

原外部高采样参考的严格几何检查继续失败。分析显式保留原失败和相关帧，没有放宽位置容差、删掉失败帧或把部分参考通过改成全组通过。本轮结论是同帧输入隔离下的滤波敏感性，未宣称绝对 Unreal 真值、动态质量或完整画面无噪点。

## 未采用的空间方差池

在得到方差敏感性后，`spatial-pool-plan.json` 事前固定另一个持续更新候选：只在 step4/8 的既有5×5 moments 采样范围，按 binomial 核和原照明连续性权重重建方差幅度；step1/2、时域历史和几何保护保持原样。没有调整前轮已冻结的1.75倍历史系数。

事前门槛仍要求两次天花板方差下降均至少5%、条件 block bootstrap 下界大于0；所有 ROI 平均能量变化小于0.3%，其他 ROI 方差回退不超过2%；全部28个参考中心的256帧最终 RGBA 必须逐 half-word 相同。后者只证明这些点相对同一参考的误差没有变化，不修复原参考几何失败。

同帧输入、A/A、覆盖率和参考中心输出身份检查通过；天花板方差仅下降2.47% / 2.36%，地面下降0.76% / 0.70%，背墙约−0.34% / +0.05%。天花板平均能量变化约−0.0367% / −0.0361%。候选未达到5%门槛，停止该候选的复验、调参和性能评估，未写入默认 shader。

当前结果支持下一步把方差的两种用途分开：其一是 `nativePlaneTrust` 的照明连续性阈值，其二是实际亮度滤波与传播方差的幅度。只重建后者的候选收益不足；还不能据此认定连续性阈值就是唯一原因。下一轮应分别隔离两种用途，再验证可持续更新的实现，保留颜色边界、地面噪声及动态拒绝门槛。

## 验证、恢复与存储

首次单通道原型增加读回后触及512 MiB上限，捕获不完整。原始失败、实际脚本和源码身份均保留，未用于质量结论。修订仅将重复像素历史的载荷改为只保存 hash，仍保留完整像素时域照明、最终输出和混合统计，未提高预算。完整重测另存 `*-v2`，每次仍受512 MiB上限约束。

单通道工具5项回归检查覆盖：所有混合通道精确、任一 half-word 泄漏必须失败、首帧输出或 ROI 不同必须失败，以及实际 CLI 生成的源码 hash、固定序列/尺度和已有结果不可覆盖。合并既有同帧、方差重放、快照与清理检查共31项通过。DX12 内存 shader 编译及实际 GPU 执行通过，活动 Editor 内未运行 Unity Test Framework。

正常模式恢复与实际程序检查见本轮 `normal-restored.json`、`active-programs-restored.json`、`final-editor-status.json`。实验没有改动生产 Runtime、shader、场景、Profile 或采样预算。额外重放与读回不构成 GPU 性能 A/B，本轮没有新增开销或性能不回退结论。

已归档不完整捕获、完成的发现组及未通过质量门槛的候选中间 `.bin`。保留协议、源码、实际脚本、报告、逐文件删除 hash、首末帧和指定诊断帧；完整的独立序列和最终尺度隔离受保护。已归档发现组不能再做完整序列重分析。精确存储与清理字节见本轮 `post-experiment-storage.json` 和 `archive-*-apply.json`。

本轮清理1,955,487,744字节，约1.821 GiB；当前目录约1.507 GiB。沿用上一份其他目录清单估算证据总量124.402 GiB，E盘剩余112.274 GiB，尚未触及125 / 64 GiB审查线，后续大捕获前需再次检查。31份工具及依赖已独立归档，当前目录没有重解析链接。正常模式保持播放暂停、Frame Debugger关闭、PIX空闲，两个场景及 Profile干净。

## 复现

```powershell
python -B Tools~/Diagnostics/BuildScreenProbeMomentChannelIsolation.py NEW_MEAN --frozen-channel mean
python -B Tools~/Diagnostics/BuildScreenProbeMomentChannelIsolation.py NEW_VARIANCE --frozen-channel variance --sample-frame-base 4361
python -B Tools~/Diagnostics/BuildScreenProbeMomentChannelIsolation.py NEW_COARSE --frozen-channel variance --frozen-scales coarse
python -B Tools~/Diagnostics/AnalyzeScreenProbeAtrousExperiment.py NEW_COARSE --same-frame --matched-reference-subset --reference-root E:/VividRP_GI_Evidence/ScreenProbeResidualMap-20261115-104052-278383
```

执行前用 `VerifyGiSourceSnapshot.py --create` 固定实际脚本、参考源码和协议，经活动 Unity CLI 捕获，再核验快照并分析。`--matched-reference-subset` 只允许保留参考失败后的诊断报告，不会把原失败状态改成通过。
