## 动态 Meshlet 阶段成本

实际捕获 `20260912_080211_102` 已完成：九情景按正序、反序分别预热64帧并观测128次，共18窗口、2304次观测，全部通过布局、设置、帧序和scope调用计数检查。每窗128个scope向量均不同，没有整向量重复读到同一计时值的明显情形；这仍不能代替GPU frame归属认证。场景为2048/256页/16层、adaptive4、短历史，运动采用原速32帧循环并包含复位。窗口内无参考RTAS、GPU回读、截图或逐帧JSON。96个原始文件共2,397,616字节已按SHA256归档到 `dynamic-timing/`，分析从归档重跑通过。

以下各项是先在每次观测内合计**非嵌套、已记录的父scope**，再求分位数；不是把阶段中位数相加，也不是完整VSM墙钟耗时。正序/反序分别列出，单位ms。

| 情景 | 外层scope合计 P50 正 / 反 | P95 正 / 反 | 主要观测 |
|---|---:|---:|---|
| static | 11.574 / 11.430 | 12.527 / 12.699 | 本轮静态控制窗口，独立于早先静态成本实验 |
| camera_slide | 14.264 / 13.528 | 16.164 / 16.326 | Trace P50 12.614 / 12.084 |
| camera_turn | 15.572 / 12.992 | 24.047 / 15.255 | 记录到8 / 4次静态失效与约8ms重绘 |
| thin_sweep | 13.017 / 11.935 | 13.886 / 13.159 | Cull父scope合计P50约0.107 / 0.106，Raster约0.038 |
| deform | 12.989 / 12.503 | 13.873 / 13.695 | 预烘焙meshlet序列切换，Raster父scope合计P50约0.070 / 0.069 |
| receiver_slide | 12.935 / 12.263 | 13.699 / 13.046 | 真动态meshlet，未记录静态失效执行 |
| receiver_lift | 12.809 / 12.129 | 13.850 / 12.884 | 真动态meshlet，未记录静态失效执行 |
| sun_rotate | 22.951 / 23.290 | 27.855 / 27.590 | 每窗96/128次静态失效；StaticRaster P50约8.13 / 8.16 |
| sun_step | 11.573 / 11.911 | 19.240 / 21.873 | 每窗8/128次静态失效，少数重绘使P95显著增加 |

跨窗口 Trace P50为9.970–14.091ms，占其父Resolve的逐观测比值中位数97.75%–98.78%；H核P50约0.0648–0.0673ms。Trace包含选层、跨层重试、求交和自适应历史查询，不能把全部时间都称为16层纹理读取。运动薄遮挡/receiver的剔除父scope约0.1ms，Raster约0.04ms；本轮证据不支持先投入大规模通用剔除改造。

新的主要成本证据是**光照变化时的页面重建与静态重绘**。sun_rotate中，同一次观测记录到InvalidateStatic时，StaticRaster P50为8.183 / 8.255ms、Allocate约1.936ms；没有InvalidateStatic样本的观测分别约0.0072ms与0.2235–0.2236ms。失效标记本身仅约0.002ms。所有StaticRaster超过1ms的观测均同时记录到InvalidateStatic，camera_turn也相同；这是scope共现证据，不应把峰值硬绑定到某一精确的运动帧。下一步应优先分解重建/重绘中的16层写入、插入竞争与实际需更新页数，并保留几何正确性参考；不能仅压低Invalidate核或直接剔除隐藏caster来宣称解决成本。

camera_turn正反序的重绘样本数为8与4：后者占比仅3.125%，故P95可能漏掉约8ms重绘（其StaticRaster P99仍约8.159ms）。这类尾部需要同时看事件计数、P99与条件分组，不能用一次P95或合并正反序掩盖差异。其他方向差异也保留为本次观测的不确定性，不用来声称版本收益。

UnityCompatibilityRaster全部零次，PageCull在有fixture的四情景每次两次、其余情景每次一次，符合本次meshlet后端。deform含预烘焙renderer注册切换，不能外推为Skinned流式顶点变形成本。普通Renderer缺失GBuffer的旧数据是原测试夹具不匹配既有渲染图，不属于这次新增的VSM缺陷。零计数scope没有执行成本样本，不能当作测得零耗时。

完整阶段P50/P95/P99、计数、Invalidate条件分组和各窗最高五次观测分解见 `dynamic-stage-timing.json`；表见 `dynamic-stage-timing-results.md`，图见 `dynamic-stage-timing.png`。可复现命令：

```powershell
python Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/check-dynamic-stage-parser.py
python Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/analyze-dynamic-stage-timing.py Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/dynamic-timing
```
