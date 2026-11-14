# 二维紧凑重要性分层实验

2026-11-14，Unity 7000.0.0a16 / DX12，顶部单面发光 Cornell Box。保持原机位、1920×1080、16 px / 8 rays、原生 Gather、护边 Atrous3；Surface Cache 为固定 8 阶、64 samples / batch、32 batches。没有修改光强、曝光、Albedo 或积分 π。

## 分区与精度契约

上一轮 row-major CDF 可将一个射线区间映射到相距较远的方向单元。本原型在 4×4 余弦图内递归执行三层重要性中位线划分：每次沿父矩形的长轴分为两个连续矩形，最终八个矩形完整覆盖原图。重要性仍为 50% 历史辐亮度与 50% 均匀探索，保留原峰值门槛和无历史余弦兜底。

共享边界先统一量化到图坐标的 1/16 网格，再写入原 RGBA16F guide，描述符为 `(xmin,ymin,xmax,-8-ymax)`。标记与旧 coarse、fine 及 row-major 格式互不混淆。逆相对 PDF 为 `0.5 * width * height`，直接由实际量化后的面积计算，不使用理论分位点的面积。八个权重之和保持 8，单个权重也检查半精度往返精确，避免 scatter metadata 二次量化失去质量。

没有增加射线或 guide dispatch。沿用现有八个 guide 描述符；冻结模式沿用已有 proposal history，相对旧冻结路径无新增纹理。中位线的 GPU 成本尚未计时，不能宣称性能不变。

## 基础验证

128 组分布覆盖零、常量、单峰、HDR、低能量、条带与随机情形。生产 GPU producer 输出的 1,024 个矩形与独立双精度 CPU 中位线公式一致，最大边界误差 0；64×64 离散网格上的每个单元恰好覆盖一次，质量和误差 0，所有 PDF 半精度往返精确。实际最大逆 PDF 为 2.1328125。

65,536 个单位局部基底下的生产采样方向与 CPU 对照，最大方向误差 1.07×10⁻⁵。该独立检查不代表任意世界朝向均已覆盖。分析器使用真实 GPU 矩形做回放，并通过注入重叠、缺口确认二者均被拒绝。首次原型中 HLSL 保留字冲突已修复，失败尝试没有计入有效 GPU 结果。

数据：`E:/VividRP_GI_Evidence/CompactProposalValidation-20261114-042510-302/`，包含 `gpu-validation.json` 与 `analysis-checks.json`。

## 逐帧白噪声分区：拒绝采用

第一轮只改变分区，使用原独立白噪声。默认 A/A 的 5,890 个受检文件逐字节一致；每组预热 512 帧后采集连续 256 帧，静态及两次 128 帧正弦轨迹均已完成。所有组的 Radiosity 首尾与组间字节一致，SHA-256 为 `8d1b4e596e3ef067de430082d0d25763b9df83c25b57422acbdd0e33c73d6e62`；前后 2,839 个受检文件未变。实际 DX12 Screen/SDF 字节码已确认 `w < -8` 的新采样分支。

| 路径 / 区域 | 最终时间方差下降 | block-32 条件 95% 区间 | 最终 Y 均值变化 |
|---|---:|---:|---:|
| Hybrid / 天花板 | 6.57% | −6.62～+18.37% | +0.781% |
| Hybrid / 地面 | 43.62% | +23.87～+59.22% | +1.576% |
| Hybrid / 右墙 | −4.02% | −54.37～+25.05% | −0.005% |
| SDF Only / 天花板 | −23.89% | −133.22～+27.70% | +1.095% |
| SDF Only / 地面 | 47.30% | +33.00～+59.15% | +1.630% |
| SDF Only / 右墙 | −55.66% | −93.49～−28.58% | +0.321% |

SDF 右墙的方差增加已在本次条件区间中明确，因此停止采用逐帧白噪声版本，没有继续用长序列挑选有利均值或调整探索比例。RMS 移动指标有改善仍不足以覆盖静态退化：同姿态差值 RMS 0.669342→0.533088（−20.36%，block-8 条件区间 −27.45～−12.46%），绝对 P99 2.43768→1.64686。两组相同 28,104 个明确新露出样本均未继承 age>1 的旧像素历史，Temporal 相对 Resolve 的 P99/max 为 0。

真实分区拓扑变化次数：Hybrid 23,546→35,542，SDF 24,079→36,597，运动 32,163→40,182。更多变化与噪声退化同时存在，但不能据此宣称已经证明唯一因果。

```text
E:/VividRP_GI_Evidence/CompactDirectionalSampling-20261114-042648-235/analysis.json
E:/VividRP_GI_Evidence/CompactDirectionalSampling-20261114-042648-235/compiled-tracing/
E:/VividRP_GI_Evidence/CompactDirectionalSampling-20261114-042648-235/source-verification.json
```

`Prototypes/CompactDirectional-White.patch` 保留该轮生产与 Editor shader 原型；不是当前默认行为。

## 冻结分布的独立对照

第二轮保持同一中位线公式、探索比例与量化精度，只允许在 32 帧 QMC 块边界重建分区，块内复制已发布描述符。基线也使用原有冻结 QMC，避免把已有 QMC 的收益归给新分区。诊断 flags：基线 `8388608`，候选 `276824064`；渲染程序集需明确加载 `CompactProposalVersion=2`。当前原型只允许冻结模式激活，没有启用失败的逐帧模式。

扩展随机序列 GPU 检查到 8,192 个样本：旧冻结路径与紧凑路径的坐标逐位相同，均符合独立 CPU Sobol 方向数公式；16×16 / 块内 8×4 联合分层及跨块 digital shift 检查通过。数据：`E:/VividRP_GI_Evidence/BlockedTemporalValidation-20261114-044124-866/gpu-validation.json`。

冻结对照全部完成，两次基线 A/A 的 5,890 个受检文件逐字节相同。每组分别检查 41,500 个块内探针帧，描述符均保持不变，未出现固定余弦重置；半球支持、精确半精度 PDF 与质量和通过。前后 2,840 个受检文件未变，源缓存仍与第一轮相同。

| 路径 / 区域 | 相对旧冻结路径的方差下降 | block-32 条件 95% 区间 | Y 均值变化 |
|---|---:|---:|---:|
| Hybrid / 天花板 | −0.25% | −24.65～+21.46% | +0.031% |
| Hybrid / 地面 | 67.94% | +60.27～+74.48% | −1.767% |
| Hybrid / 右墙 | 18.65% | −16.79～+41.97% | −0.739% |
| SDF Only / 天花板 | −17.95% | −37.86～+1.15% | +0.048% |
| SDF Only / 地面 | 56.98% | +38.34～+67.36% | −0.574% |
| SDF Only / 右墙 | −9.27% | −35.46～+16.06% | −0.141% |

Hybrid 地面均值区间 −2.334～−1.166%，未通过 ±1% 门槛；不能据此单独宣布长期系统偏差。天花板和右墙没有证实全局降噪增益，所以不继续做本候选的长序列采用确认，也不调参寻找通过结果。

固定轨迹 RMS 0.463104→0.421471（−8.99%，block-8 条件区间 −17.82～−0.60%），绝对 P99 1.83626→1.47102；相同的 28,104 个新露出样本均未继承旧像素历史，Temporal 相对 Resolve 的 P99/max 均为 0。这些局部收益不足以替代全部静态与能量门槛。

```text
E:/VividRP_GI_Evidence/CompactDirectionalSampling-20261114-044330-778/analysis.json
E:/VividRP_GI_Evidence/CompactDirectionalSampling-20261114-044330-778/source-verification.json
```

没有降低已有 ±1% Y/RGB 能量门槛，也没有启用新默认。仅得到更紧凑的分区或更少更新不构成画质验收。

## 撤回与重放

两种采样原型均已撤回，三份运行时源码及独立 Editor shader 与实验前 HEAD 一致。保留 `Prototypes/CompactDirectional-White.patch`、`Prototypes/CompactDirectional-Frozen.patch`；两份均通过撤回后的 `git apply --check --unidiff-zero`。在隔离 checkout 应用其中一份后，先运行 `CompactDirectionalProposalValidation.cs`；质量捕帧将 `TemporalSamplingCapture.cs` 的 `experiment` 分别设为 `compact` 或 `compact-blocked`。未应用对应补丁时，工具会明确拒绝重放。

已实际确认程序集卸载紧凑原型，C# 编译完成；目标 Cornell 相机为原生 Gather、Atrous3、16 px / 8 rays、8 阶 / 32 批。诊断回调为空、flags=0、frame override=-1、强制双面关闭、bias=0.04，两个场景均未脏。默认 shader 没有 Error，保留原实现的编译 Warning。状态及消息保存在冻结实验目录的 `final-verification.json`、`default-denoiser-verification.json`。

本轮未测 GPU 耗时，Editor 运行期间未启动 Unity Test Framework。目标“稳定无噪点并对齐 Lumen”仍未达到。下一项转回全分辨率空间降噪：当前三轮支撑宽度为 29 px，测试额外的 step 8，同时保护所有粗尺度几何边缘，先验证颜色与方差的 ping-pong 不读写同一资源，再检查输入等价、平面噪声、接触区域能量和固定轨迹。
