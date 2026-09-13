# 2048 / 4096：1024 页、固定四射线的 Nsight 对照

2026-09-13 完成四次正式 GPU Trace：2048 → 4096 → 2048 → 4096，每次 4 帧，累计 16 帧。每次采集前至少预热 256 个主相机帧；所有配置均无新增页、无页溢出。结果支持将下一步重点放在**深度采样的局部性和访存依赖链**。

| 指标（每轮四帧中位数的范围） | 2048 | 4096 |
|---|---:|---:|
| VSM.ResolveTrace | 8.05–10.17 ms | 19.39–19.61 ms |
| 实际驻留 / 请求页 | 266 / 266 | 641 / 641 |
| 页溢出 | 0 | 0 |
| L1 命中率 | 69.65–71.88% | 54.60–54.61% |
| L1 texture-load 命中率（首次） | 68.29% | 53.39% |
| Trace 区间 DRAM 吞吐 / sustained peak | 4.27–5.88% | 55.43–56.40% |
| 等待 L1TEX 载入结果的采样状态占比 | 45.60–47.98% | 73.50–74.33% |
| 每条指令 predicate-on 有效线程 / 32 | 18.36–18.39 | 18.09–18.11 |
| Compute occupancy | 39.63–48.27% | 44.91–46.09% |
| SM throughput / sustained peak | 45.21–56.67% | 27.67–27.95% |

![Nsight 对照图](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMNsight_20260913/comparison.png)

4096 下缓存命中下降，访存依赖等待增加，SM 发射吞吐降低。DRAM 利用率显著上升，但区间平均值没有达到峰值饱和，适合描述为**访存延迟与流量压力加重**。Long Scoreboard 表示等待 local/global/texture/surface load 的结果；要进一步定位，需要追溯产生数据的 load，不能将等待所在的消费者指令直接认作根因。[NVIDIA Shader Profiler](https://docs.nvidia.com/nsight-graphics/UserGuide/shader-profiler.html)

有效线程数只下降约 1.4–1.6%，occupancy 区间重叠。分支和提前退出造成的低线程利用率仍值得优化，但本次数据没有支持它是分辨率成本跃升的首要因素。当前导出没有提供可靠的逐 shader 寄存器数量，因此不将原因归为寄存器溢出或寄存器占用突变。

首次采集的 VSM.Allocate 为 0.340 → 0.721 ms；FilterHorizontal 为 0.082 → 0.081 ms，FilterTemporalVertical 为 0.187 → 0.187 ms。新增成本集中在 Trace。分配器值得继续优化，但单独优化它无法消除约 9–11 ms 的 Nsight Trace 差距。

**L2 和流量归因的边界：**整体 L2 命中率为 87.28–92.99% → 42.05–42.61%，但 `srcunit_tex` 子项为 94.50–97.97% → 98.29–98.38%，二者变化方向不同。首次 Trace 区间 `dram__sectors.sum` 的导出量增加约 18.8 倍；不能把这个整体量直接等同为 VSM 深度池读流量，也不能声称“深度纹理 L2 命中率降到 42%”。本次采用 L1、warp 访存等待、吞吐的共同变化确定优化方向，保留 L2 来源拆分和逐 load 归因为后续验证。GPU Trace 的区间硬件指标也可能包含共享 GPU 上其他进程的工作。[NVIDIA GPU Trace UI Reference](https://docs.nvidia.com/nsight-graphics/UserGuide/gpu-trace-ui.html)

**下一步建议的具体实验：**

1. 保持 16 层容量与当前求交规则，先对深度布局做 A/B：对照现有 Texture2DArray 分层布局与局部层分组或近层/溢出层布局，测量 L1 命中、Long Scoreboard、Trace 时间，同时回归拱门漏光与细几何遮挡。是否合并加载必须由测量决定，避免首层命中时的额外过取。
2. 增加细粒度层占用或有效层范围的原型，目标是减少确定为空的深层读取；现有按页/池判空继续保留。先统计各层真实访问分布，再决定元数据粒度，计入元数据带宽与构建成本。
3. 将按接收层/页邻近性组织屏幕 tile、减少跨层重复读列为第二组实验。它可能改善局部性，但会增加调度或额外 pass，需比较完整 Resolve 成本。

对应现有热路径是 [逐 cell / 深度层扫描](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/VSMSMRT.hlsl:121)、[深层迭代](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/VSMSMRT.hlsl:143) 和 [静态 / 动态池加载](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute:1303)。这仍是基于源码与硬件区间数据的定位；本次没有改动渲染实现。

**采集口径与复现：**

- Unity 6000.7.0a6，DX12，RTX 5070 Ti / GB203，驱动 616.64；Nsight Graphics 2026.3.1。
- Sponza + SponzaLightingDay；相机位置、旋转、投影、附加相机参数与前次基线一致；输出 1920×1080，AA=None；定向光角直径 7.1°。
- 1024 物理页，128×128 texel/page，16 深度层；静态/动态池容量固定。固定四射线，自适应关闭，8 samples/segment，最大光程 10，短历史降噪和空间过滤保留。除分辨率外，四次有效 Volume 设置完全相同。
- 主配置等待 120 秒后抓取，复采等待 90 秒；`--max-duration-ms 200 --limit-to-frames 4 --auto-export --architecture "Blackwell GB20x" --metric-set-id 0 --real-time-shader-profiler`。
- CLI 请求 event buffer 64000 kB、HES buffer 10000 kB、warp interval 16384；Nsight 实际导出报告为 event buffer 64000 kB、HES buffer 5000 kB、warp interval 512。四次实际报告一致，GPU Clocks=Locked to Base，导出的 GPC 时钟约 2293 MHz。以实际报告为准。
- 开始先做了工具预检，默认缓冲区出现 HES / periodic sampling 丢样；该预检被排除。四次正式采集均成功导出，日志没有丢样或缓冲区耗尽。仍有不支持的 DX12 device interface 与 Debug Layer 强制关闭警告，已保留在原始日志。
- Nsight 最后输出 `Terminating process... Activity session destroyed, connection error encountered.`，发生在成功写出 trace 和所有表之后，命令退出码为 0；随后核实诊断 Unity/Nsight 进程均退出。此次开始时 Unity 原本关闭，未保存临时 Volume 或场景参数。
- **Nsight 时间用于硬件诊断，不替换未注入性能基线。**2048 两次中位数有约 21% 波动，4096 两次相差约 1.1%；因此报告两轮范围，不将首轮 1.93 倍写成精确的产品性能比例。此前未注入固定四射线的 Trace 为约 6.74–6.77 → 17.75–17.77 ms，仍使用[上一轮报告](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMResolutionScaling_20260913/README.md)。

数据文件：

- [results.json](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMNsight_20260913/results.json)：全部四轮逐帧结果、Trace 原始指标及归一化 warp 状态；[comparison.csv](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMNsight_20260913/comparison.csv) 为首次配对的详细比较。
- [nsight-exports.zip](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMNsight_20260913/nsight-exports.zip)：四轮官方表、有效配置、相机/光源快照和采集日志；不包含无关的编辑器许可日志。
- [manifest.json](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMNsight_20260913/manifest.json)：四个 `.ngfx-gputrace` 的本地路径、大小和 SHA-256。大文件保留在忽略目录 `Temp~/vsm-nsight-20260913/`。
- [verification.json](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMNsight_20260913/verification.json)：预热早于采集、原机位一致、相机与光源参数一致、只有分辨率不同，全部通过。
- [prepare.cs.txt](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMNsight_20260913/prepare.cs.txt)：命令行运行的临时 Volume / 预热诊断；复制为忽略目录内 `.cs` 后执行 Unity `eval_file`，不导入项目脚本。
- [analyze.py](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMNsight_20260913/analyze.py)、[archive.py](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMNsight_20260913/archive.py)、[plot.py](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMNsight_20260913/plot.py) 可重生成结果。若仅保留 zip，先将其解压到上述 Temp 目录。

Nsight 2026.3 的 `.xls` 实际是 TSV：同名指标列分别对应四帧，flattened event 行还会重复。通用 harness 的 summary 没有正确解析该多帧格式，故使用专用解析脚本，只取首个匹配 Trace 行的完整四帧向量，避免把重复导出行当成额外样本。Long Scoreboard 百分比以所有导出的 warp 状态之和归一化，包含 Selected / Not Selected；它不是帧时间百分比。
