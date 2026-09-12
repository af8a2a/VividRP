# 定向光 VSM：16 层容量、阶段成本与动态质量基线

已完成实景测量、独立 GPU 夹具、同相位动态对照、高射线参考复核与原始数据归档。本次只增加四个缓存 GPU 计时 sampler 及作用域；阴影算法保持前序实现。

## 测量结论与下一步选择

1. **16 层仍有容量压力。** 静态池 15.77% 的已分配纹素填满 16 层；独立几何抽样有 14.00% 超过 16 个深度桶，最大 37。满层率、几何复杂度、生产丢弃率和漏光率必须分别看待。
2. **常见窗口优先优化深度访问。** 静态自适应 Resolve 为 9.55–9.59 ms，Trace 占约 97.7%；滤波约 0.22 ms。动态薄遮挡/接收面剔除约 0.1 ms，当前证据不支持先做大规模通用剔除改造。
3. **动态太阳另有重绘瓶颈。** 持续转动太阳时，发生失效的观测中静态重绘约 8.2 ms、分配约 1.94 ms；失效标记本身约 0.002 ms。要优化实际重建/写入工作，而非只优化标记核。
4. **短历史与自适应需要保留响应尾部约束。** 1024 射线参考下，自适应薄遮挡条件 P95/P99 为 1/2 帧；光源阶跃条件 P99 为 3 帧，31/1902 个完整观测事件在 0–8 帧窗口内未检出。这里仍混合采样与几何偏差，不能直接全部归因为拖影。

建议下一项实现选择**按页/池占用跳过确定为空的深度访问**，先保持当前 16 层列表、求交规则和采样集合等价，复跑本报告的静态/动态质量对照与无回读成本窗口，再决定是否继续更改存储。动态太阳的多层写入/插入竞争作为独立后续测量项。基于最前层深度删除隐藏 caster 会破坏现有表示的可能命中，当前数据不支持这样做。

## 环境与配置

- 日期 2026-09-12；基于 `e9dd0a39` 的当前工作树，包含前序 16 层、分层 SMRT、短历史及自适应射线改动。关键生产文件 hash 见 [production-source-hashes.json](production-source-hashes.json)。
- Unity 6000.7.0a6、DX12、RTX 5070 Ti、驱动 616.64；Edit Mode，Sponza / SponzaLightingDay，1920×1080，AA=None。
- 2048² VSM、128² 页、256 物理页、16 深度层、10 个 clipmap；方向光角直径 7.1°。4 射线 × 8 步，长度参数 10；另设固定 8 射线质量对照。
- 相机 position=(0.205129, 5.076887, 4.140471)，Euler=(30.164865, 1.881778, 0.538555)；光方向=(0.043504, 0.861320, −0.506197)。实际矩阵、Volume 和资源描述均归档。
- 容量扫描、RTAS 参考、回读与图像采集在显式诊断中运行。静态及动态计时窗口均排除这些工作、逐帧文件输出与后台压缩。
- 两套多层 R32 深度池为 512 MiB；物理光栅 DSV、Unity 兼容 DSV 另各 16 MiB。历史纹理、页表和场景数据也另计。

## 16 层容量与存储基线

当前静态 Sponza 机位的静态深度较密，动态深度池为空。256 个逻辑物理页各有独立的静态/动态 16 层 R32 存储，共预分配 **512 MiB**；实际非零深度 payload 为 **142.56 MiB（27.84%）**。这是按非零 slot 计算的存储占用，不代表这些 slot 都贡献了阴影，也不是已经能兑现的压缩收益。

| 全部已分配页纹素 | 静态池 | 动态池 |
|---|---:|---:|
| 预分配深度存储 | 256 MiB | 256 MiB |
| 非空纹素比例 | 98.35% | 0% |
| 平均非零层数 | 8.910 | 0 |
| 非零层数 P50 / P90 / P99 | 9 / 16 / 16 | 0 / 0 / 0 |
| 第16层非空纹素比例 | 15.77% | 0% |
| 含第16层数据的页 | 132 / 256 | 0 / 256 |
| 非零 slot / 预分配 slot | 55.69% | 0% |

512 个池/页记录、22 个池/clipmap 汇总和 2,560 个虚拟页表项通过独立 CPU 复核：直方图、层占用、空/非空守恒及页表双向所有权一致；排序、重复、中间空洞和非法深度异常均为 0。分配器同帧为 **266 请求、256 分配、10 页预算溢出**；这是页面预算指标，不能与深度层容量混为一谈。该帧所有页都由静态 caster 填充，不能据此断言动态场景永远不需要动态池。

正式动态采集同时保留逐帧页计数：自适应相机平移的预算溢出为5–19页，变形序列为21–26页，太阳转动为3–11页；太阳转动/阶跃均出现单帧256个新页，而静止为0。它们描述需求与重建压力，不能代替实际接收者缺页率。见 [动态页预算记录](dynamic-page-pressure.json)。

![静态层分布及逐 clipmap 容量压力](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/capacity-baseline.png)

另一次同机位完成帧使用独立 RTAS（217 个静态实例、264,754 个三角形，动态实例为 0）沿每页 8×8 个纹素中心枚举几何深度。静态池 16,384 个有效抽样中，**2,294 个（14.00%）的参考深度桶数超过 16**；最大为 37，未遇到 65 桶下界截断。level 1/2 的相应比例为 31.55%/19.97%。投影深度跨度约 1024 m，实际分桶 epsilon 为 0.0001 m。每页只抽样其 0.390625% 纹素；这些是强制 opaque、双面源三角形的几何桶统计，**不是生产插入丢弃率，更不是漏光像素比例**。

按 1 cm 最近距离容差，参考捕获中的生产深度有 **145,956 / 145,961（99.9966%）** 匹配已枚举几何；近 16 个参考表面共有 1,258 个未匹配，分布于 1,085 个抽样。匹配不是一对一，且 alpha、背面剔除、LOD、光栅覆盖及量化都会影响集合，因此不能直接把未匹配项归因为层溢出。满 16 层本身也无法证明存在第 17 层；额外表面是否造成可见性错误还需要具体接收者斜射线反例。

容量诊断的独立 GPU 夹具通过完整字段、所有权异常和 **36 个直接调用生产插入函数的光栅用例**；独立几何参考的 **18 个实际 RTAS/GPU 用例**也已通过。前者明确覆盖 16 个不同深度恰好填满但无遗漏，以及 17/32/64 个不同深度发生已知有限容量截取，未修改生产插入代码来伪造实景丢弃计数。

原始数据共 27 文件、753,736 字节已归档到 [raw/capacity](raw/capacity) 和 [raw/capacity-reference](raw/capacity-reference)，全部源/归档 SHA256 一致，见 [归档清单](capacity-archive.json)。分析现已完全从归档重跑，不依赖 Temp 目录：[实际占用](capacity-analysis.json)、[逐页分布](capacity-pages.csv)、[独立几何容量](capacity-reference-analysis.json)、[容量 GPU 结果](capacity-gpu-result.txt)、[参考 GPU 结果](reference-gpu-result.txt)。

从包根目录复现分析与图：

```powershell
python Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/analyze-capacity.py Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/raw/capacity
python Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/analyze-capacity-reference.py Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/raw/capacity-reference/frame_000 --validation-status pass --validation-evidence Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/reference-gpu-result.txt
python Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/plot-capacity.py
```

上述两个容量捕获是独立完成帧，未作为逐纹素严格配对差分；GPU 计时应使用另行运行的无容量扫描、无 RTAS 参考和无读回窗口。

## 无回读静态阶段成本

6 个正反顺序窗口，各预热 64 帧、观测 128 次；768/768 次通过校验。下表是独立的窗口中位数，单位 ms，不将子阶段中位数与父阶段相加。

| 模式 | Resolve 正 / 反 | Trace 正 / 反 |
|---|---:|---:|
| 仅空间滤波，固定4 | 10.759 / 10.201 | 10.580 / 10.063 |
| 短历史，固定4 | 11.104 / 10.693 | 10.867 / 10.439 |
| 短历史，自适应4 | 9.550 / 9.587 | 9.328 / 9.350 |

H 约 0.064–0.065 ms；空间 V 约 0.065 ms，时域 V 约 0.157 ms。自适应相对固定4短历史的 Resolve 在同向窗口下降约 10.3%–14.0%。方向窗口存在漂移，不能将本轮绝对值与旧报告直接相减后称为版本收益。Trace 包含选层、跨层重试、求交与历史查询，尚未由硬件计数证明具体的带宽或原子瓶颈。

详见 [静态阶段结果](stage-timing-results.md)、[原始窗口](timing)、[计时层级与归属限制](timing-instrumentation.md)。

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

![动态阶段成本](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/dynamic-stage-timing.png)

## 动态质量：同相位对照与高射线复核

正式采集为 **9 情景 × 3 模式 × 32 帧 = 864 帧**。每组预热至少 64 帧，随后从生产相位 0 开始连续采集；27 组帧序/轨迹校验与 18 组模式配对通过。ROI 起点 (482,162)、范围 960×720、stride=4，共 240×180 个采样点；误差主要报告有效水平地板，另单独报告抬升面的精确顶部。

六个代表情景另采 1024 射线参考，共 192 帧。其参考复用于三模式**原有生产输出**：18 组完整 world/normal/depth 逐位一致，姿态、随机相位、几何 hash 和后端一致。高射线运行自己的阴影输出不混入比较。0.001/0.01 m 法线 bias 均保存，用于检查参考敏感性。

下表使用 1024 射线、0.001 m bias。MAE 是归一化可见度误差，非最终颜色误差。

| 情景 | 固定4 MAE | 固定8 MAE | 自适应 MAE |
|---|---:|---:|---:|
| 静止 | 0.015244 | 0.014788 | 0.015248 |
| 细杆扫过 | 0.015553 | 0.015006 | 0.015580 |
| 烘焙变形序列 | 0.010036 | 0.009788 | 0.010043 |
| 接收面抬升：地板 | 0.009560 | 0.009263 | 0.009563 |
| 太阳连续转动 | 0.015154 | 0.014336 | 0.015154 |
| 太阳角度阶跃 | 0.012775 | 0.012339 | 0.012777 |

首检事件采用共同阈值：相邻参考变化 ≥0.1、接收点世界位置连续且 bias 不敏感；输出跨过旧/新参考中点并持续 2 帧，观测延迟 0–8。**P95/P99 仅对已检出事件统计**，完整窗口未检出另列；参考回转、换表面、尾部截断和事先已在新侧都不能当作零延迟。

| 情景 / 模式 | 已检出 | 完整窗口未检出 | 条件 P95 / P99（帧） |
|---|---:|---:|---:|
| 细杆 / 固定4 | 1551 | 0 | 0 / 2 |
| 细杆 / 固定8 | 1621 | 1 | 0 / 1 |
| 细杆 / 自适应 | 1535 | 0 | 1 / 2 |
| 光源阶跃 / 固定4 | 1871 | 31 | 0 / 3 |
| 光源阶跃 / 固定8 | 1874 | 30 | 0 / 1 |
| 光源阶跃 / 自适应 | 1871 | 31 | 0 / 3 |

细杆三模式各有 2850 个合格几何事件；约 1100 个在等待期间参考回转或失去表面条件，完整分母见 [1024 结果表](selected-1024-results.md)。光源阶跃各 2165 个合格事件，自适应其中 263 个事先已在新侧，31/(1871+31)=1.63% 在完整窗口未检出。固定8降低了这两个情景的条件 P99，但未检出数并非全部改善。

256→1024 的细杆事件集合 Jaccard 为 97.57%；自适应在256参考中出现的2个未检出在1024中消失，P95从0变为1。因此最终尾部结论采用高射线复核。共同的 ≥0.5 强跃迁阈值也已对全部模式统计，保留 [原始完整矩阵](final-dynamic-quality-jump05.json) 与 [高射线子集](selected-1024-quality-jump05.json)，不按有利结果选择阈值。

抬升面有 **137240** 个有效顶部样点，法线契约覆盖约 **99.983%**。顶部 MAE 为固定4/固定8/自适应 **0.025574 / 0.025031 / 0.025575**；历史年龄中位数在运动期降至1，静止后恢复。它验证了深度变化下的历史拒绝，尚未提供带物体运动对应的材质点延迟。

抽样支持包络之外，自适应细杆有4/3307次旧侧残差观测、光源阶跃4/801次，最大误差约0.113/0.117。stride=4不能证明未采样像素的完整支持域，几何及积分偏差也可能产生残差；这些数字不能全部解释为时域拖尾。

### 覆盖边界

- 刚体夹具使用真实动态 MeshletRenderer；源普通 Renderer 被关闭。参考使用相同 mesh/transform 和已验证的显式几何 flags。
- deform 是32帧预烘焙 meshlet 几何序列，包含 renderer 注册切换，未验证实时 Skinned 顶点变形。其参考确实变化，但本轨迹与平滑 sun_rotate 的单帧变化不足0.1，只有沿轨迹误差证据，首检延迟标为不足。
- camera_slide / camera_turn / receiver_slide 只有256参考；相机运动没有伪造运动矢量对应，不能从事件不足推断无拖影。
- RTAS 参考强制 opaque、双面且有限采样；alpha/LOD/特殊几何与光栅覆盖仍可能不一致。全有效屏幕 ROI 只作诊断，不能当作全场景精确真值。
- 本轮没有生产场景唯一丢失表面计数、primary/transition逐角色接收者满足率或完整展开实例分布。当前容量、页预算和阶段时间足以选择下一项候选，尚不能替代这些归因指标。
- 原普通 Renderer 夹具缺 GBuffer 法线、RTAS null flags 缺实际命中的数据已排除。原因与替代数据见 [排除记录](excluded-captures.json)、[独立审查](independent-dynamic-review.md)。


![1024参考响应事件](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/figures-selected-1024-jump01/dynamic-response-events.png)

## 验证、恢复与复现

- 实际 GPU：容量诊断全字段与异常状态校验；36 个直接调用生产插入函数的光栅组合；18 个独立容量/接收者参考夹具；18 个 Renderer/MeshConfig 动态更新对照均通过。
- 最终 Runtime、Editor、Editor.Tests 独立 Roslyn 编译全部通过，源树与引用响应文件稳定；见 [最终编译结果](validation/csharp-final.json)。新增诊断 shader 已做 DXC 编译并在实际 GPU 运行：[容量入口6/6](capacity-dxc.json)、[参考入口3/3](validation/reference-dxc.json)。
- 新增四个 sampler 的7条记录路径，各预热128次、测4096次，当前线程托管分配均为0；见 [GC结果](profiling-gc-result.txt)。这不代表整个渲染管线或所有线程均已做零分配认证。
- 9个临时诊断源与Unity生成的9个meta已按归档内容/哈希验证后清理。相机、光源、Volume及两场景记录状态已恢复，场景仍未保存且均clean，临时对象数0；见 [恢复记录](state-restoration.json)。
- 编辑器保持打开，未启动 Unity Test Framework；需要执行的 Editor 测试由用户在合适时机手动运行。未进行 Nsight 实际 GPU 捕获，不宣称已有硬件带宽/原子竞争结论。

容量原始数据、静态/动态成本CSV及元数据均在报告内。动态原始 float4 reference/signal/world、页计数和逐帧元数据采用无损 tar.xz + SHA256 去重归档：256组27case共79.21 MiB，1024组6case共12.29 MiB。GPU字节按原始解压内容保存；截图、progress和另行归档的容量附加buffer不属于该动态包。恢复与分析无需Unity，示例从包根目录执行：

```powershell
python Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/archive-dynamic.py restore Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/raw/dynamic-256 Temp~/vsm-baseline/restored-dynamic-256
python Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/archive-dynamic.py restore Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/raw/dynamic-1024 Temp~/vsm-baseline/restored-dynamic-1024
python Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/analyze-dynamic.py Temp~/vsm-baseline/restored-dynamic-256 --jump-threshold .1 --output Temp~/vsm-baseline/restored-quality.json
python Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/analyze-reference-convergence.py Temp~/vsm-baseline/restored-dynamic-256 Temp~/vsm-baseline/restored-dynamic-1024 --all-base-modes --output Temp~/vsm-baseline/restored-1024.json
```

两组归档均已实际恢复：5,356个文件、2,640个去重引用逐字节/哈希核验通过；四份最终分析从恢复数据重跑后，仅忽略来源路径的严格语义比较全部通过。解析器元数据已统一，质量统计未变；见 [归档与重算复验](dynamic-archive-validation.json)。

在线重采使用归档的 `.cs.txt` / `.compute.txt` / `.shader.txt` 临时导入，导入规则和菜单见 [诊断说明](dynamic-meshlet-baseline.md)、[参考绑定](reference-interface.md) 及 [计时说明](timing-instrumentation.md)。不要同时导入多个 `VSMDynamicBaselineProbe` 类副本，元数据由Unity生成。每次重采先确认实际帧布局、有效法线、参考命中与状态恢复，再进入正式对照。
