# VSM 深度区间查找：降低跨层读取与访存依赖

2026-09-13。已将 SMRT 深度层的线性扫描替换为有序区间查找，默认开启。保留 16 层容量、静态／动态独立池和现有求交规则。同一场景的未插桩正反顺序 A/B、Nsight 硬件指标、GPU 区间对照与完整 Resolve 同帧对照共同支持保留本次优化。

## 测量结果

RTX 5070 Ti / GB203，Unity 6000.7.0a6，DX12，1920×1080，Sponza + SponzaLightingDay，AA=None，定向光角直径 7.1°。物理页预算 1024，16 深度层，固定四射线、自适应关闭，8 samples/segment，其他有效设置与前次基线相同。

未插桩 GPU ProfilerRecorder：每个窗口预热 64 帧、记录 128 个主相机观察值，目标 marker 每帧样本数均为 1。执行顺序为 2048 旧→新、4096 旧→新、4096 新→旧、2048 新→旧。下表使用收益较保守的反向配对中位数。

| 分辨率 | ResolveTrace 旧→新 | 降幅 | 完整 VSM.Resolve 旧→新 | 降幅 |
|---|---:|---:|---:|---:|
| 2048 | 7.435 → 4.690 ms | 36.9% | 7.657 → 4.954 ms | 35.3% |
| 4096 | 18.526 → 5.540 ms | 70.1% | 18.790 → 5.772 ms | 69.3% |

两轮窗口的 Trace 中位数范围：2048 旧 7.435–8.693 ms、新 4.471–4.690 ms；4096 旧 18.526–20.385 ms、新 5.540–5.589 ms。旧路径存在时段波动，所以保留逐窗口数据，并不把某一次最大降幅视为稳定保证。此 A/B 使用同一编译着色器中的诊断分支，旧分支保留原线性算法；它用于隔离算法差异，不能替代所有平台的最终发行构建测量。

![性能与硬件指标](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMDepthSearch_20260913/comparison.png)

Nsight Graphics 2026.3.1：新路径每种分辨率 4 帧，与[前次复采基线](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMNsight_20260913/README.md)比较。GPU 锁定基频，GPC 约 2293 MHz；正式采集前均已预热至少 256 帧。

| Nsight 指标 | 2048 旧→新 | 4096 旧→新 |
|---|---:|---:|
| ResolveTrace | 8.054 → 4.889 ms | 19.393 → 6.410 ms |
| L1 命中率 | 71.88 → 85.00% | 54.60 → 80.36% |
| Long Scoreboard 采样状态占比 | 45.60 → 37.41% | 73.50 → 42.68% |
| Trace 区间 DRAM 吞吐 / sustained peak | 4.27 → 5.22% | 55.43 → 19.01% |
| Compute occupancy | 48.27 → 44.93% | 44.91 → 43.61% |
| predicate-on 有效线程 / 指令 | 18.36 → 19.75 | 18.11 → 19.46 |
| Allocate | 0.337 → 0.367 ms | 0.720 → 0.725 ms |
| 请求／驻留页 | 266／266 → 266／266 | 641／641 → 641／641 |

两组均无新增页、无页溢出，相机、光源和有效 Volume 参数逐项相等。缓存命中和等待占比的变化支持“减少深层探测缩短访存依赖链，并改善工作集的实际缓存表现”这一解释。物理 Texture2DArray 布局及其显存容量沿用原实现，收益来自访问方式变化。

Long Scoreboard 在这里是等待 L1TEX 相关载入结果的采样状态占比，分母为全部导出 warp 状态，包含 Selected / Not Selected，**不是帧时间百分比**。[NVIDIA Shader Profiler](https://docs.nvidia.com/nsight-graphics/UserGuide/shader-profiler.html)

硬件计数属于 Trace 区间，不能直接当作某一深度纹理的精确读流量。2048 的 DRAM 利用率小幅上升、两组 texture-source L2 命中率下降，与总体 L1／时间变化并不等价；不要据此宣称所有缓存层都得到改善。硬件区间还可能包含共享 GPU 上其他进程的活动。[NVIDIA GPU Trace UI Reference](https://docs.nvidia.com/nsight-graphics/UserGuide/gpu-trace-ui.html)

## 实现与正确性约束

[VSMSMRT.hlsl](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/VSMSMRT.hlsl:88) 利用 caster 原子插入形成的降序 reverse-Z 前缀和尾部零值。首层仍按原路径读取；需要查找隐藏层时，在剩余 15 层中二分定位是否存在与当前光线 cell 区间相交的表面。每个参与查询的池最多增加 4 次读取，原来最多增加 15 次。

- 静态池和动态池分别搜索，保留各自的层排序；池间层号不代表同一几何深度。
- 查询某池前使用已载入的首层值；已知空池继续直接跳过。页映射验证与页内映射复用沿用原路径。
- 保留原浮点运算及比较顺序，包含 `surface > enter`、`surface - thickness <= exitTime`，避免重排阈值计算改变边界结果。
- DDA cell 遍历、粗细层接续、光程、单 cell gap fill、平行尾段、厚度与 bias 均沿用原逻辑。
- `_VSMDepthSearchLinear=0` 为默认有序查询，`1` 仅用于诊断 A/B。没有新增 Volume 选项或 C# 渲染热路径分配。
- 诊断 `g_VSMDebugWork.y` 计数包括原来的双池查询及新路径的单池探测，因此不是统一的标量 Load 数，不能用新旧计数比例代替实际访存节省比例。

正确性证据：

| 检查 | 覆盖与结果 |
|---|---|
| GPU 区间 oracle | 131,072 个案例；289 种静态／动态有效层数配对；全部 16 层、两组 depth scale、表面／厚度边界；22,581 命中、108,491 未命中，零差异 |
| 完整生产 Resolve 同帧对照 | 2048／4096 × 固定／自适应射线 × 静止／相机平移／光源旋转，共 12 组，每组 16 帧；398,131,200 个像素逐位一致，最大误差 0 |
| 着色器编译 | DXC：28 个生产入口 + 22 个 GPU 验证入口，共 50／50 通过 |
| C# 编译 | 包含新增回归测试的 Editor.Tests 程序集，通过独立 Roslyn 编译 |
| 状态恢复 | 未插桩测量前后相机、设置、场景状态一致；同帧探针结束时恢复相机／太阳变换并释放临时资源；诊断进程已退出，未保存临时 Volume 或场景 |

同帧探针对同一套当前深度、页面、蓝噪声相位和历史输入运行两次实际 `CSMShadowResolve`，只切换查询算法；比较的是原始 Resolve 输出。它证明这些输入下未引入额外漏光／遮挡差异，并不证明历史上所有几何误差已消除，也没有单独验证两条独立滤波历史序列。动态池覆盖来自 GPU 合成数据；完整场景对照本次包括相机和光源运动，没有额外安排真实移动 caster 场景。

新增 [SMRT_OrderedDepthIntervalsMatchLinearScanAcrossPoolsAndBoundaries](E:/VividRP_Reborn/Packages/VividRP/Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.cs) 回归测试。Unity Test Framework 未执行；交互 Editor 打开期间使用直接 GPU 验证与独立编译。完整 Editor 测试套件仍可由用户手动运行。

## 数据与复现

- [timing-results.json](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMDepthSearch_20260913/timing-results.json)、`timing/*.csv`：8 个未插桩窗口、有效设置、页计数与状态恢复快照。
- [correctness.json](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMDepthSearch_20260913/correctness.json)、`same-frame/results.json`、`gpu-fixture.json`：正确性聚合与逐帧记录。
- [nsight-comparison.csv](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMDepthSearch_20260913/nsight-comparison.csv)、`nsight-results.json`：Nsight 配对指标及原始四帧向量。
- [nsight-exports.zip](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMDepthSearch_20260913/nsight-exports.zip)：官方 TSV 表、配置、相机／光源快照与采集日志；不收录无关的 Editor 许可日志。`nsight-manifest.json` 保存两个原始 `.ngfx-gputrace` 的路径和 SHA-256；大文件在忽略目录 `Temp~/vsm-depth-search/nsight/`。
- `timing.cs.txt`、`gpu-fixture.cs.txt`、`same-frame.cs.txt`、`nsight-prepare.cs.txt`：Unity CLI `eval_file` 诊断脚本，复制到忽略目录中的 `.cs` 后运行，不作为项目脚本导入。
- `analyze.py`、`archive.py`、`plot.py` 可重算结果及归档；复用前次 Nsight 多帧 TSV 解析器。只保留归档时，先将新旧 zip 分别解压至对应 Temp 目录。`compile-shaders.py`、`dxc-final.json`、`csharp.json`、`source-sha256.json` 保存验证与源码指纹。

Nsight 使用 `--start-after-ms 120000 --max-duration-ms 200 --limit-to-frames 4 --auto-export --architecture "Blackwell GB20x" --metric-set-id 0 --real-time-shader-profiler`。实际导出配置为 event buffer 64000 kB、HES 5000 kB、warp sampling interval 512，与旧基线一致。4096 前两次启动未在默认连接期限内完成；失败日志保留，未进入数据集。最后一次加 `--no-timeout` 成功，两组正式采集均无丢样或缓冲区耗尽。原有 DX12 interface／Debug Layer 警告仍存在。结束时 Nsight 在导出成功后终止诊断 Editor 并输出连接断开消息，退出码 0。

## 后续优先项

保留本次有序查找。下一轮先统计真实层占用和“首层后即空”的比例，判断浅层快路径是否能避免二分搜索的无效探测；同时检查新瓶颈下的 DDA cell 数与页表访问。需要进一步改变布局时，再单独对照局部层分组的读取收益与首层命中时的额外过取，沿用此次同帧 oracle，并增加真实移动 caster 场景。继续按完整 Resolve 成本和几何差异决定是否采用。
