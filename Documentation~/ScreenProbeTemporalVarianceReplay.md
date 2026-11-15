# 原生 Screen Probe 的独立方差历史重放

本轮增加独立时域 moments 的同帧 GPU 重放，测试降低统计引导波动能否缓解天花板噪声。**最终候选未通过独立采样序列复验，未修改默认 Runtime、shader、场景或 Profile。** 首组通过不作为发布依据，也未在复验失败后继续调整系数。

证据目录：`E:/VividRP_GI_Evidence/ScreenProbeVarianceHistory-20261115-172500`。实际渲染器为 `7b9d8836`，工具起点为 `98af3b81`；Unity 7000.0.0a16 / D3D12 / RTX 5070 Ti。Cornell 原生 Gather 保持 16 px / 每探针 8 条射线，FP32 anchor age512、像素历史上限32、4-pass À-trous、bias=0.04。冻结 Radiosity 为8阶 / 64样本 / 32批，SHA-256 仍为 `40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8`。

## 实验边界

`BuildScreenProbeVarianceHistory.py` 在真实像素时域 dispatch 后、RawResolved 被空间滤波复用前，运行两条临时内存参考：

- A/A 使用真实的上一帧 moments，复现原时域 kernel 的照明、像素历史、几何和 moments 四份输出。
- control 从第0帧开始在自有 ping-pong 纹理中演化统计历史，逐帧使用实际深度、GBuffer、运动、矩阵、Resolve 和像素颜色历史。独立的是统计历史，不是完整的另一套 GI 历史。

静态各两次重复，每次预热1,024帧、测量128帧。每组四个 ROI 的4,160项时域 A/A hash、3,120项 control 颜色/历史/几何 hash 均逐值检查；另核对初始历史转换和未改变的统计通道。输入源码、实际 C# 脚本、四份参考源码及实际 DX12 程序均固定身份。所有临时 shader 和纹理在 `finally` 释放，不创建 Unity 资产。

单方差的1.5/2/4倍历史窗口实验未发布：2/4倍有地面方差回退；1.5倍与旧高采样参考的几何检查仍失败。直接在原 kernel 中增加第二方差也改变了部分原快速方差值，未通过不变性检查。失败记录未覆盖。

## 分离快速与慢速方差

后续实验保留原快速 kernel，再用独立 dispatch 更新慢方差。临时 RGBA16F moments 的前三通道仍为原均值、快速方差、年龄；第四通道改为有效标记 `2^-14 + slowVariance`，无效时为0。该标记保留原 kernel 对历史有效性的判断，避免额外生产纹理。初始化显式执行 FP16 最近偶数舍入；隐式写回原型的舍入差异单独保留为失败。

慢方差沿用原重投影、矩合并、光照 Jacobian、覆盖率检查和历史拒绝。只有成熟历史使用较慢更新；新露出区域仍为年龄1、方差0。空间滤波 step1/2 继续读取原快速方差，step4/8 才读慢方差，几何与照明边界保护保持原样。该描述仅对应临时实验，不是当前默认布局。

`dual-variance-plan.json` 在捕获前另行规定：两次天花板时域方差下降均至少5%，条件 block bootstrap 下界大于0；所有 ROI 平均消耗能量变化小于0.3%，地面、背墙和光片边界的方差增加不超过2%；均值/快速方差/年龄及原颜色历史精确相同。此外，全部28个参考中心、256帧的最终 RGBA 必须逐 half-word 相同。

原外部参考的严格几何门槛继续失败，没有放宽容差或剔帧。上述7,168个最终 RGBA 样本完全相同，能够证明这些点相对同一参考的能量和误差没有变化；它不能证明绝对正确、完整画面准确，或把原几何失败改成通过。

## 静态发现组与独立序列复验

表中正数表示时域方差下降，负数表示增加。候选始终位于 control，真实生产渲染器没有切换。每个数对应一次128帧重复，不是独立场景的统计保证。

| 固定参数与序列 | 天花板，两次重复 | 地面，两次重复 | 结论 |
|---|---:|---:|---|
| 2倍慢方差，显式舍入，序列1733 | +9.83% / +6.02% | −2.17% / −0.70% | 超出地面2%回退门槛 |
| 1.75倍，发现组，序列1733 | +5.67% / +7.40% | −1.88% / −0.23% | 发现组通过，尚未采用 |
| 同一1.75倍，复验序列4361 | +2.40% / +0.54% | +1.03% / +1.07% | 天花板未达到5%，否决 |

`holdout-plan.json` 在复验前冻结1.75倍系数、新序列及禁止继续调系数的约束。两组的快速统计不变性、时域重放及全部参考点最终输出身份检查通过；发现组天花板区间约为[4.80%,7.62%] / [7.25%,7.87%]，复验为[1.41%,3.57%] / [0.27%,1.17%]。这些是单次序列内按32帧块重采样的条件区间，不能取代独立序列复验。

复验天花板慢方差自身的时域 RMS 比快速方差降低约34.6%–34.7%，最终像素方差收益仍很小。因此，稳定方差估计没有可靠转化为最终输出收益；不能据此认定均值波动已是唯一剩余原因。下一项诊断应把统计均值、方差与照明输入的影响继续分离，先验证因果，再增加历史或模糊强度。

## 固定轨迹诊断

候选在复验失败后仅用于验证工具和历史拒绝，不重新获得采用资格。沿用周期128帧、横移0.28、前移0.06、偏航−7°的轨迹，序列保持4361。两个移动 ROI 的时域 A/A、原颜色/几何及快速统计精确相同，覆盖率不变。

两次各3,894个新露出样本、涉及73帧：旧像素历史接受数为0，像素时域减 Resolve 的 P99/最大值为0；原快速矩和 control 慢方差均为年龄1、方差0，有效标记均正确。新露出样本的最终 RGBA 在两条空间路径逐值相同。这覆盖记录的边缘 ROI，不代表物体运动、动态光照或 Sponza 验收。

整个移动 ROI 的路径差 P99 为0.00876 / 0.00835，最大值0.05494 / 0.04920；这些是同帧路径差，不是物理真值误差。多次重放与读回改变 GPU 工作量，本轮未执行性能 A/B，也没有新增开销或性能不回退结论。

## 验证、恢复和存储

26项 Python 回归检查通过：独立时域生成器4项、全参考输出身份2项、原同帧生成器5项、源码快照5项、证据保留10项。覆盖输出不可覆盖、源码 hash、固定复验序列、互斥布局及单个 half-word 变化必须拒绝等检查。内存 DX12 shader 编译和实际 GPU 执行通过；开放的 Editor 内未运行 Unity Test Framework。

恢复检查确认两个场景与 Profile 干净、回调全部清空、覆盖参数为−1、诊断为0，实际三个生产 DX12 程序 hash 与本轮前一致。正常模式仍为16 px / 8 rays / native / 4-pass / age512。Editor 保持播放暂停，Frame Debugger 关闭，PIX 空闲；本轮没有重新启动 Editor 或新增 PIX 捕获。

已按明确归档策略清理否决或不完整实验的中间 `.bin`，保留源码、实际脚本、协议、报告、逐文件删除 hash、首末帧及指定诊断帧。完整复验和移动序列受保护。归档后的发现组不能再执行全序列重分析，现有报告和逐文件身份仍保留。精确字节和存储巡检见证据根目录 `archive-*-apply.json`、`post-experiment-storage.json`。

本轮共清理3,984,943,104字节，约3.711 GiB；当前目录约1.004 GiB。沿用上一份其他目录清单估算证据总量122.895 GiB，E盘剩余113.805 GiB，未触及125 / 64 GiB审查线。29份工具及依赖已独立归档，当前目录未发现重解析链接。

## 复现

临时候选源码保存在上述证据根目录；需使用原生产源码及相同场景状态。命令不会把临时 kernel 安装进默认渲染器。

```powershell
python -B Tools~/Diagnostics/BuildScreenProbeVarianceHistory.py NEW_STATIC --control-temporal E:/VividRP_GI_Evidence/ScreenProbeVarianceHistory-20261115-172500/temporal-packed-dual1p75.compute --control-wavelet E:/VividRP_GI_Evidence/ScreenProbeVarianceHistory-20261115-172500/wavelet-packed-dual2.hlsl --packed-dual-variance --sample-frame-base 4361
python -B Tools~/Diagnostics/BuildScreenProbeVarianceHistory.py NEW_MOTION --control-temporal E:/VividRP_GI_Evidence/ScreenProbeVarianceHistory-20261115-172500/temporal-packed-dual1p75.compute --control-wavelet E:/VividRP_GI_Evidence/ScreenProbeVarianceHistory-20261115-172500/wavelet-packed-dual2.hlsl --packed-dual-variance --sample-frame-base 4361 --motion-history
python -B Tools~/Diagnostics/AnalyzeScreenProbeVarianceHistory.py NEW_STATIC --reference-root E:/VividRP_GI_Evidence/ScreenProbeResidualMap-20261115-104052-278383
python -B Tools~/Diagnostics/AnalyzeScreenProbeAtrousExperiment.py NEW_MOTION --same-frame --motion-only
```

生成后先用 `VerifyGiSourceSnapshot.py --create` 固定实际脚本和四份参考源码，通过活动 Unity CLI 执行，再立即核验源码快照并分析。每次捕获载荷上限512 MiB，沿用现有高采样参考，不复制参考序列。
