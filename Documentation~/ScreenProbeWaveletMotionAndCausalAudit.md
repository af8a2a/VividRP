# 原生 Screen Probe 移动验证与滤波噪声定位

本轮补齐 `7b9d8836` 方差重建的移动边缘同帧对照，并否决四个未通过质量门槛的候选。**没有改动默认 shader、场景、Profile 或采样参数。** 冻结统计引导的实验提供了下一步定位线索，但不是可用于移动或光照变化的实现。

证据目录：`E:/VividRP_GI_Evidence/ScreenProbeCoveredWavelet-20261115-164056`。Unity 7000.0.0a16 / DX12 / RTX 5070 Ti，Cornell 原生 Gather、16 px / 8 rays、FP32 anchor age512、像素历史上限32、4-pass À-trous、bias=0.04。仍使用同一份 8 阶 / 64 samples / 32 batches 的冻结 Radiosity，SHA-256 为 `40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8`。

## 移动边缘同帧验证

`BuildScreenProbeSameFrameAtrous.py --motion-history` 复用已存在的固定轨迹：周期128帧、横移0.28、前移0.06、偏航−7°。两次重复均预热1,024帧、测量128帧。实际渲染器、内存 A/A 和旧参考滤波器共享这一帧复制的像素时域照明、接收面及 moments，只修改参考的空间滤波代码；不分别演化另一套历史。

`motion-baseline/same-frame-comparison-with-exposed-difference.json` 合格。边缘 ROI `[880,414,160,128]` 的256项输入复制 hash、256项 A/A 输出 hash 精确一致，覆盖率相同。此结论覆盖该 ROI，不能替代完整画面、Sponza、物体运动或动态光照验收。

| 新露出像素检查 | 重复1 | 重复2 |
|---|---:|---:|
| 样本 / 涉及帧 | 3,894 / 73 | 3,894 / 73 |
| 接受旧像素历史 | 0 | 0 |
| 像素时域减 Resolve，P99 / 最大值 | 0 / 0 | 0 / 0 |
| moments 年龄 / 方差 | 1 / 0 | 1 / 0 |
| 新旧最终空间输出差，P99 | 0 | 0 |
| 新旧最终空间输出差，最大值 | 0.005197 | 0.005336 |
| RGBA 值不同的样本 | 7 | 9 |

“最终减 Resolve”的P99及最大值在两条路径相同，不足以证明每个像素都相同；新增逐值 hash 和差值检查后明确保留上表中的少量差异。最终空间输出差是线性消耗 GI 的亮度差，不是对物理真值的误差。整个移动 ROI 的平均消耗 Y 变化为 −0.0136% / +0.00433%。没有扩大几何容差、删帧，原来的跨运行 GBuffer 失败记录继续保留。

## 覆盖率与历史审计

Deferred 的 `LoadVividRealtimeDiffuseGI` 在 realtime-only 模式消费 `RGB × coverage`；原生时域历史也按覆盖能量混合，而 moments 和 wavelet 的亮度权重统计条件辐亮度。这是已确认的接口约定，不能仅凭域不同断言其有错误。

对上一轮合格同帧数据测量，固定平均辐亮度时，覆盖率单独波动产生的方差约为天花板总方差的2.4%–2.9%，地面0.3%–0.6%，背墙0.7%–0.8%。这些不是可相加的方差分解，省略了协方差和乘积项；仅支持优先检查辐亮度与统计引导，尚不足以支持更改能量域。

双端方差同帧捕获中，三个平面 ROI 的所有测量 moments 年龄均为32。当前剩余静态噪声不能归因于这批像素历史频繁归零。

## 未保留的候选

所有候选仅作为临时内存中的 control shader 或额外 dispatch 执行，实际渲染器仍为 `7b9d8836`。每个静态对照都验证四个 ROI 的1,024项复制输入和1,024项 A/A 输出，固定源码、实际执行脚本和两个参考源码的字节 hash。表中正数表示时域方差下降，负数表示增加。

事前门槛：两次天花板方差下降均至少5%，其条件 block bootstrap 下界大于0；所有 ROI 平均能量变化小于0.3%，光片边界方差增加不超过2%，28个参考探针中心的参考 RMS 不增加超过1%；其余平面方差也不回退超过2%。没有按结果降低门槛。

| 候选 | 天花板，两次重复 | 光片边界，两次重复 | 结论 |
|---|---:|---:|---|
| 沿平滑照明梯度放宽方差重建 | −10.33% / −9.82% | −18.81% / −18.84% | 否决 |
| 双端方差归一化亮度权重 | +9.47% / +9.48% | −18.35% / −18.84% | 边界能量约 +0.48%，参考误差也增加，否决 |
| 仅连续照明区域使用双端方差 | +3.33% / +2.41% | −0.23% / +0.37% | 未达到天花板门槛，地面/背墙仍有回退，否决 |
| 增加 step16 的第5轮 wavelet | −5.88% / −27.12% | 0 / 0 | 否决 |

四个对照的输入可比性合格，不等于候选质量合格。没有采用第五轮滤波或更改亮度权重，也没有发布 GPU 开销结论。

## 冻结 moments 的因果定位

`--freeze-control-moments` 仅供静态诊断：每次重复的首个测量帧复制 moments，后续仅给参考滤波器使用该固定纹理。照明输入和实际渲染器仍逐帧更新。生成器禁止将此模式用于移动历史验证；所有临时纹理和 shader 在 `finally` 释放。

首个原型没有逐帧记录冻结纹理 hash，保留为不合格记录。复测 `frozen-moments-verified` 中，四个 ROI 的1,024项冻结输入 hash 都与各自首帧实际 moments 精确一致，复制照明和 A/A 输出也均精确一致。

但这次复测有7帧参考探针的世界位置 X 末位不同，最大差 `5.960464477539063e-8`；整体静态参考门禁仍失败。`reference-geometry-failure.json` 保留具体帧和值，`frozen-input-diagnostic.json` 明确为 `qualified=false`，单独记录冻结输入证明通过，没有扩展容差或排除这7帧。

在全部测量帧中，冻结引导的天花板方差下降8.67% / 22.31%，光片边界下降24.47% / 22.89%，平均能量变化很小；地面方差增加6.58% / 7.04%。它支持继续测试统计引导的时间稳定性，也表明冻结首帧不适合作为默认行为。该诊断不证明生产质量改进或 Unreal 能量对齐。

下一项实现应分开处理统计均值和方差：先测试更稳定的方差估计，保留均值更新、重投影拒绝与光照变化响应，再按相同平面、参考能量、边缘和移动门槛验收。只增加空间平滑强度的四个候选已被本轮证据否决。

## 状态、存储与复现

正常渲染恢复检查通过：两个已加载场景及 Profile 干净，回调全部清空，诊断为0，覆盖参数为−1，仍为16 px / 8 rays / native / 4-pass，实际三个 DX12 程序 hash 与 `7b9d8836` 的验证记录一致。Editor 保持播放暂停，Frame Debugger 关闭，PIX 空闲。没有启动 Unity Test Framework，没有新增链接或 Unity 资产。

Python 同帧生成器及校验5项、源码快照5项、证据保留10项回归检查通过。DX12 内存参考编译与真实 GPU 重放已经执行；重放的计时包含额外工作，本轮不发布新增 GPU 开销或性能不回退结论。

按两个明确的归档策略清理5个否决或证据不足实验的中间 `.bin`，共2,268,241,920字节，约2.113 GiB；保留实际脚本、源码、协议、报告、逐文件删除 hash 和首末帧。旧默认移动对照、最新冻结输入诊断与高样本参考未删除。完整中间帧已归档的候选不能重做全序列分析。

清理后本轮目录约0.946 GiB，磁盘剩余约114.83 GiB；结合上一轮基数估算全部 GI 证据约121.89 GiB，低于125 GiB软上限。其他目录未重新全量盘点，实际脚本、源码及工具清单归档于证据目录。容量记录不包含最后少量报告元数据。

```powershell
python -B Tools~/Diagnostics/BuildScreenProbeSameFrameAtrous.py NEW_MOTION --control-body OLD_BODY.hlsl --motion-history
python -B Tools~/Diagnostics/AnalyzeScreenProbeAtrousExperiment.py NEW_MOTION --same-frame --motion-only
python -B Tools~/Diagnostics/BuildScreenProbeSameFrameAtrous.py NEW_SCALE --control-body CURRENT_BODY.hlsl --control-iterations 5
python -B Tools~/Diagnostics/BuildScreenProbeSameFrameAtrous.py NEW_FROZEN --control-body CURRENT_BODY.hlsl --freeze-control-moments
```

生成后、执行前 pin 实际脚本和两个参考源码，结束后验证。每次保存上限仍为512 MiB，本轮目录上限2 GiB。默认 A/A 始终运行实际四轮滤波；冻结引导不参与移动验收。静态标准分析仍要求完全一致的参考几何，已失败的复测必须继续报告失败。
