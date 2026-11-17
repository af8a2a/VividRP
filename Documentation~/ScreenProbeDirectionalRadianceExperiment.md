方向 radiance 驱动平面能量：实现与验收
====================================

2026-11-17。承接 [实现对齐分析](ScreenProbeImplementationAlignment.md)。本轮把独立标量 anchor 从候选画面的能量、coverage 和插值 footprint 中移除，并完成实际 D3D12 同帧配对验证。以下保留的是推广前的实验记录；随后按用户要求保留结构对齐，已移入生产路径。不能把结构对齐直接等同于固定斑块修复。

证据根目录：`E:/VividRP_GI_Evidence/DirectionalIrradiance-20261117`。本轮没有恢复上一轮撤销的 `precise` 修改，也没有改变每探针 8 条射线、16 px 间隔、192 SDF steps、源缓存 8 orders / 64 samples / 32 batches 或像素历史 / 小波预算。

**当前生产状态（2026-11-17）**

用户明确要求“先不管实验效果，保留和 Lumen 的对齐结果”。因此保留 `radiance-dither512-v3` 的方向场数据流，不再把本轮均值图结果作为保留实现的门槛；历史失败数据原样保留，未宣称达到 Lumen 画质。

- `SdfScreenProbeFiltering.compute` 现在执行固定 cosine mass 的加权散射、方向 radiance 时间历史、同方向锥空间过滤和方向场积分。SH 与标量摘要均来自同一过滤结果；独立 scalar anchor 不再回填最终 SH。
- `SdfDiffuseGatherPass.cs` 运行三轮四邻域过滤，复用一个 RenderGraph transient 纹理；显式绑定 Filter 的真实帧号及追踪最大距离。空间输出供下一帧 guide，时间历史保留空间过滤前的结果。新历史 ID 隔离旧的条件方向估计及年龄编码。
- `SdfScreenProbeGather.compute` 不再由 scalar anchor 的亮度差收缩插值范围。几何拒绝仍保留。
- 原 `screenProbeAnchorMaxAge` 序列化字段改作方向 radiance 历史上限，面板名称与说明同步更新；原配置及默认数值保留，当前 Cornell 为 512。冷启动按实际有效年龄增加历史权重。Lumen 默认 0.5 的时间权重与此本地窗口仍有区别。
- 原 scalar anchor 两项开关保留序列化但隐藏，生产路径不再调度标量准备 / 预过滤，也不分配它的历史。遗留 shader kernel 与小型资源接口保留给历史诊断工具；不参与最终能量。
- 每探针 8 rays、16 px 间隔、16 个方向格、源缓存预算和像素后处理预算未修改。没有恢复 `precise` 改动；方向场与临时纹理仍是 FP16，采用按帧变化的随机舍入。

只完成一次有限的实现检查，没有增加画质实验组：C# 重载成功；Filter 的 11 个 kernel 与 Gather 的 6 个 kernel 全部通过 D3D12 编译；现有 Cornell 相机在 EditMode 连续运行 16 帧，一次读回最终 GI、几何、方向场、SH 和摘要，未见 GPU 错误或非有限值。6,953 个有效探针全部具有方向场 coverage=1；摘要与方向场均值的最大差为 0.0004302，SH 在探针法线处与方向场积分的最大差为 0.0006830，二者都按 `length(error) / max(1, length(integral))` 统计，与 FP16 存储量化相符。该检查验证能量来源契约，不验证最终均值图的噪点、动态响应或性能。

检查脚本及结果：`C:/Users/11252/.codex/visualizations/2026/11/16/01a1e462-e00e-7582-854a-55ec4a721049/directional-promotion/{compile,smoke}.{cs,json}`。结束后 Editor 仍在 EditMode，诊断钩子已移除，原有场景未保存或修改。交互 Editor 开着，因此未运行 Unity Test Framework。没有进行新的 A/A、A/B 或耗时测量。

诊断 atlas 仍为 22 平面：0–6 是当前方向场修正后的 SH，7–13 是修正前 SH，14–20 是空间过滤前 SH，21 是原始 trace 摘要。之前实验中将“旧标量基线 / 候选”塞入两组 SH 的临时布局没有进入生产。历史 `BuildScreenProbeDirectionalIrradiance.py` 面向 `31f34144` 基线，重跑旧实验须使用该源码快照；已有捕获中的完整 shader / 脚本保留，不应直接套用到当前生产布局。

**推广前的实现内容**

- `BuildScreenProbeDirectionalIrradiance.py` 构建内存 Filter / Gather。原始标量 anchor 的输出保存在对照 SH，候选从另外七个 SH 平面读取。两者使用同一次追踪、几何和方向缓存，分别维护像素历史。生产资源和静态钩子在 finally 恢复。
- `ScreenProbeRadianceTemporal.hlsl.txt` 将 Composite 的 `conditional RGB × covered mass` 还原为采样权重散射量，再除以方向格固定 cosine mass；未抽到的格在当前帧贡献零，也参与时间滤波。这样不再对每格的少量样本单独求条件均值后，假装它们拥有相同采样概率。
- 同文件按几何重投影过滤方向 radiance。空间置信权重不再乘到光能上；alpha 表示方向图接收点有效性。真实黑色和未解析射线都贡献零能量，但原始 trace / Composite 的覆盖及命中状态保留，未把缺失射线改成有光样本。
- `ScreenProbeRadianceSpatial.hlsl.txt` 对照 Lumen 的四邻域、命中距离钳制、同方向锥角度和深度权重，运行三轮空间过滤。另测了保留旧空间核、以及固定归一化的对称通量变体。后者是本地对照，不是 Lumen 原样移植。
- SH 的平面目标来自**同一份已滤波方向场的 cosine 积分**。不再使用 raw / temporal / spatial scalar anchor 回填。因为 Vivid 仍采用 cosine 半球图，而 Lumen 使用全球 equal-area 图，候选保留局部投影修正，使离散 cosine 积分与探针法线处 SH 一致；这不是独立能量历史，也不是额外乘法增益。
- Gather 的 coverage 来自方向图，候选 footprint 不读取成熟 scalar anchor 的亮度差。边缘 repair 的 coverage 也必须读取候选 SH；第一轮发现诊断副本漏改此寻址，立即因 A/A 失败停止，修正后才进行质量分析。

**Lumen 对照范围**

本机 UE 5.7.4 的 `LumenScreenProbeFiltering.usf` 先按采样权重 scatter radiance，再做 probe temporal、spatial 和 SH 投影；没有本项目的独立长标量 anchor 强制回填。默认 probe history weight 为 0.5，空间过滤为三轮；tracing resolution 8 表示 8×8，并非 Vivid 的 8 rays。

本轮保留 Vivid 的射线预算与 cosine 半球参数化，因此是数据流、估计量和过滤语义的对齐，不是完整 Lumen 或同预算 Lumen 画质证明。0.875、512 帧强度和通量过滤均作为本地变体明确记录。

**验证与主要结果**

每组从冷 Gather history 预热 1024 帧，记录 128 帧；使用 2,336 个像素的最终 GI、精确对应的 GPU 位置 / 法线，以及既有独立 Lambert 参考。各完整捕获均通过 132 组原生 / 副本 Resolve 和 3,300 组像素历史 / 最终输出逐字节比较；2,783 项受检源文件在各自捕获期间未变。

这是同帧重建对照。方向场及其引导由该组候选维护，基线共享这些输入；它不等于旧生产全流程与新全流程的独立 A/B，也不宣称跨运行 A/A 已解决。动态、GPU 耗时和全图质量尚未验收。

| 证据目录 | 变体 | 最终均值图 MSE 变化：ceiling / floor / back / edge |
| --- | --- | --- |
| `paired-v2` | 仅取消独立 anchor，沿用条件方向缓存 | +2313% / +1033% / +485% / +1021% |
| `radiance-v1` | 加权 scatter、radiance temporal 0.5，旧空间核一轮 | +291% / +0.35% / −7.72% / +125% |
| `radiance-v2b` | radiance temporal 0.875，旧空间核三轮 | +262% / −16.39% / −1.65% / +29.33% |
| `radiance-v3` | temporal 0.875，同方向锥四邻域三轮 | +58.06% / +42.27% / +2.66% / +20.02% |
| `radiance-history512-ramp` | 长方向历史、正确冷启动年龄、同方向锥三轮 | −26.10% / +61.38% / −13.04% / +4.72% |
| `radiance-conservative512-v2` | 长方向历史、对称空间通量 | −53.65% / +65.29% / −18.29% / +4.70% |
| `radiance-dither512-v3` | 长方向历史、同方向锥三轮、随帧随机量化 | −22.93% / −7.67% / +8.33% / −10.52% |

这些百分比只比较**同一行同帧配对的两条重建路径**。不同组的引导与上游 radiance 历史不同，不能把跨行数值之差当成单一因素的严格收益。

`radiance-history512-ramp` 的平均能量误差为：

| ROI | 标量对照 | 方向候选 | 去均值空间误差 RMS，对照→候选 |
| --- | ---: | ---: | ---: |
| ceiling | −0.460% | −0.298% | 1.601% → 1.400% |
| floor | +2.291% | +3.575% | 3.638% → 4.129% |
| back | −1.497% | −1.412% | 6.143% → 5.725% |
| edge | +0.897% | +1.963% | 5.459% → 5.310% |

同组 floor 和 edge 时间方差分别下降 52.8%、95.0%，但最终空间 MSE / 能量仍退化。这组结果不能通过既定画质门槛。数据仅覆盖有限相关帧及所选像素，不是无限收敛偏差。

`radiance-history512` 使用固定高历史权重，没有正确的冷启动年龄递增，首帧稀疏采样会在预热后残留；该变体已被 `-ramp` 替代。`radiance-conservative512` 的几何 / 角度权重组合不完全对称，已由 `-v2` 修正；旧证据保留。

**历史量化对照**

Lumen 在 probe temporal 写入时调用 `LumenFloatQuantization.ush` 的随机量化。候选使用相邻 FP16 数的概率舍入，保持 FP16 格式和期望值，不使用 `precise` 或扩大纹理格式。

初版 `radiance-dither512` 漏掉 Filter 的帧号绑定：生产 C# 原来只在 Trace / Gather / Guide 上传 `_SdfDiffuseFrame`。它实际使用固定空间随机阈值，因此不能作为随帧随机量化的有效实验。`radiance-dither512-v2` 补了帧号，但异步回读乱序使原统计器严格中止，未作为验收结果。`radiance-dither512-v3` 在 BeginFrame 把 Filter 帧号写入 GPU 命令，并按帧号缓存回读结果后交给原严格统计器；重复、缺帧及 A/A 检查均保留。生产移植若进行，必须在 C# 命令录制处增加匹配的 Filter 帧号绑定，不能只改 HLSL。

最后一组完整通过 132 组 Resolve、3,300 组历史 / 最终输出重放检查及源文件核验。最终平均能量误差（对照→候选）为：ceiling −0.444%→−0.462%，floor +2.572%→+2.496%，back −1.505%→−2.520%，edge +0.947%→+1.000%。四区去均值空间残差 RMS 均略降，但后墙增加约 1.015 个百分点欠亮，导致均值图 MSE 增加 8.33%，故仍未通过全区域验收。不能只依据去均值残差下降忽略能量偏移。

**当前判断**

“校准后平面能量由标量 anchor 决定”是已核对的代数事实；“移除它即可改善全部云纹”没有得到实验支持。仅删校准会暴露条件方向估计量的能量损失；改成方向 radiance 后，还必须处理方向重建、空间传输和有限历史的误差，不能再用一个标量增益掩盖差异。

候选及生成器可供继续定位首次能量偏离的阶段，但当前没有通过全部 ROI / 物体 / RGB 通道的最终图门槛。在当时的画质门槛下未推广生产默认，也未提交画质改善 commit；随后按上述用户要求保留结构对齐。没有把时间方差下降作为通过依据。

按用户要求收束实验，不再扩展候选组合。Editor 已恢复 EditMode；四个诊断回调为空，frame override 为 −1、diagnostics 为 0、anchor diagnostics 关闭，两个原有场景均未脏。当时收束实验时生产 Runtime / Shader 没有遗留修改，保留的是诊断实现、报告和失败证据；后续推广状态以上文为准。恢复记录为证据根目录的 `restoration-before-stop.json` 和 `restoration-final.json`。
