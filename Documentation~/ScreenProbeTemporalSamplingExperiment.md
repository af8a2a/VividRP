# Cornell Box：连续 Sobol 时间采样候选未采用

2026-11-13；Unity 7000.0.0a16 / D3D12。纯自发光 Cornell Box，1920×1080、原机位、**16 px 网格 / 每探针 8 条射线**。原生 Screen Probe Gather；ReSTIR 路径不参与本次实验。

## 结论与默认行为

连续低差异时间采样降低了时间噪声，但改变了部分区域的长期平均照明。候选**未作为默认行为保留**，没有通过全局增益或曝光补偿掩盖差异。默认继续使用原有逐帧独立哈希；Editor 诊断 bit `262144` 才启用连续 Sobol 候选。

本轮保留可复现的 GPU 样本检查、分级捕获、长序列能量门槛和失败数据。它们不是已经完成 Lumen 画质对齐的证明。

## 采样与检查

在每个探针 / 射线的二维采样域中使用独立 digital shift 的 Sobol 序列，继续沿用原有余弦分层和重要性采样 PDF。Screen Hit 和 SDF 共用生成函数，不增加射线，也不改变滤波强度、历史上限、光强、反射率、曝光或默认 4 cm bias。

CPU 检查覆盖 65,536 个索引及所有对齐的 2～1024 帧二维 elementary interval 分层。GPU 检查逐值核对 4,096 个二维样本，与独立的逐方向数 CPU 算法一致；每条射线的前 256 个点恰好覆盖 16×16 单元各一次。默认与实验开关确实产生不同样本。此实现不是 Unreal 蓝噪声采样的逐行移植。

默认白噪声控制与修改前的实际 GPU 数据逐位比较：Hybrid 和 SDF Only 各 **3,332 个文件完全相同**，包含源缓存、探针和最终像素。恢复默认开关后又运行了 GPU 样本检查。

## 256 帧同机位及移动结果

每组预热 512 帧，再采集 256 个连续帧。Radiosity 为 8 阶、64 samples / batch、32 batches；六组的发布缓存首尾及组间逐字节相同。比较线性 consumed E/π，不包括接收面的反射率、曝光和显示变换。区域使用既有匹配机位的解析 object ID 排除其他物体。

| 路径 / 区域 | 最终时间方差下降 | 256 帧平均 Y 变化 |
|---|---:|---:|
| Hybrid / 天花板 | 73.95% | +0.35% |
| Hybrid / 地面 | 40.72% | +0.63% |
| Hybrid / 右墙 | 48.48% | −1.68% |
| SDF Only / 天花板 | 47.11% | +0.81% |
| SDF Only / 地面 | 36.70% | +2.91% |
| SDF Only / 右墙 | 57.97% | −0.34% |

不能只凭天花板的 RMS 宣称通过。Hybrid 的 raw scalar 方差反而增加约 3.42%；时间相关性改变后，收益主要体现在后续积分与像素历史。

两条 256 帧轨迹包含两次相同的 128 帧正弦往返：水平 0.28 m、前后 0.06 m、yaw −7°；重复周期使用相同浮点输入。A/B 的 VP、几何、源缓存逐位一致。28,104 个明确新露出样本，两组均无 age>1 的旧像素历史继承，Temporal 相对当前 Resolve 的 P99 / max 都为 0。

在避开第一圈开始阶段的同姿态重复帧中，最终 Y 差值 RMS 从 0.683666 降至 0.537440，绝对 P99 从 2.343187 降至 1.719406。这是同姿态重复性和像素历史继承检查，不是动态 GI 真值，也不证明所有探针层拖影消失。

## 独立长序列能量验证：未通过

另用独立帧相位，对白噪声和 Sobol 的 Hybrid / SDF Only 分别采集 **2,048 帧**。源缓存仍与短序列逐字节相同。预先采用区域平均能量差及其 block-128 条件 bootstrap 区间均在 ±1% 内的诊断门槛。

| 路径 / 区域 | Sobol 相对独立白噪声长序列的均值变化 | 条件 95% 区间 |
|---|---:|---:|
| Hybrid / 天花板 | −0.20% | −0.39～−0.01% |
| Hybrid / 地面 | +4.70% | +3.92～+5.49% |
| Hybrid / 右墙 | −1.59% | −1.92～−1.26% |
| SDF Only / 天花板 | +1.29% | +0.95～+1.64% |
| SDF Only / 地面 | +1.43% | +0.59～+2.28% |
| SDF Only / 右墙 | −0.89% | −1.20～−0.58% |

只有 Hybrid 天花板完整通过门槛，不能推广为能量不漂移。这些区间描述本批冻结源 / 帧序列的离散程度，不是跨场景保证。白噪声参考是当前渲染器原有的**滤波后长期均值**，不是独立物理真值；不能仅据此断言哪条路径更接近真实照明。需要继续分离自适应 PDF 与时间相关采样的耦合，以及后续非线性重建 / 历史融合。

## 关闭引导后的定位

额外采集两组 SDF Only 的 1,024 帧，关闭重要性引导，继续保留相同冻结源、16 px / 8 射线和所有重建设置。额外捕获地面 / 右墙原始命中探针，按真实世界平面与法线筛选，A/B 几何逐位相同。

| 区域 | 原始命中均值变化 | Resolve 均值变化 | Pixel Temporal 均值变化 | 最终均值变化 |
|---|---:|---:|---:|---:|
| 天花板 | −0.068% | −0.388% | −1.906% | −1.908% |
| 地面 | −0.613% | +0.499% | +0.508% | +0.503% |
| 右墙 | −0.006% | +0.008% | +0.953% | +0.952% |

原始命中的三组均值差区间均包含 0；地面区间仍约 −1.52～+0.30%，不将它包装为已证明的 ±1% 等价。天花板和右墙的额外均值变化出现在 Pixel Temporal，而非 À-trous；因此**仅冻结采样引导不足以解决本轮发现的能量问题**。下一个受控实验应先消融 `RectifyPixelHistory` 的颜色裁剪、亮度相关 fast-update 与覆盖率相关历史权重，固定相同输入，分别测 Resolve → Temporal 的均值偏差和噪声。需要同时检查新露出区域及真实灯光变化，不能直接关闭全部抗鬼影保护。

关闭引导后候选的最终时间方差仍下降约 87.55% / 83.47% / 87.48%，说明降噪潜力存在；这不撤销因能量未验证而保持原默认的决定。

## 工具、证据与复现

证据根目录：`E:/VividRP_Reborn/Logs/TemporalSampling-20261113/`。

- `capture-imported/`、`analysis.json`：六组同机位 / 轨迹与各阶段统计。
- `reference/`、`reference-sdf/`、`reference-sobol/`、`energy-reference.json`：四条独立长序列及能量拒绝结果。
- `gpu-validation.json`、`default-baseline-equivalence.json`：GPU 采样和默认结果检查。
- `source-continuous/`：首次候选的准确源码；当时 bit 262144 为白噪声、0 为 Sobol，**与最终安全默认开关极性相反**。数据标签不变。
- `capture/`：首次未确认 Shader 刷新的一批数据，两组输出逐位相同，排除出候选收益统计。
- `source-uniform/`、`uniform-isolation/`：后续关闭引导、额外捕获地面 / 右墙原始探针能量的消融。

`TemporalSamplingCapture.cs` 顶部的 `experiment` 选择 `compare`、`reference`、`reference-sdf`、`reference-sobol` 或 `uniform-isolation`；重新采集前使用新的输出根目录，脚本拒绝覆盖已有捕获。它使用临时 Profile，不保存场景；通过 finally 恢复相机、Profile、活动场景、诊断开关和回调。可写入输出目录的 `cancel.txt` 请求取消并恢复。

```powershell
unity command script eval --project-path E:/VividRP_Reborn --file E:/VividRP_Reborn/Packages/VividRP/Tools~/Diagnostics/TemporalSamplingValidation.cs --timeout 180 --format json
unity command script eval --project-path E:/VividRP_Reborn --file E:/VividRP_Reborn/Packages/VividRP/Tools~/Diagnostics/TemporalSamplingCapture.cs --timeout 4500 --format json
python -B Tools~/Diagnostics/TemporalSamplingExperiment.py E:/VividRP_Reborn/Logs/TemporalSampling-20261113/capture-imported
python -B Tools~/Diagnostics/TemporalSamplingExperiment.py E:/VividRP_Reborn/Logs/TemporalSampling-20261113 --reference-only
python -B Tools~/Diagnostics/TemporalSamplingExperiment.py E:/VividRP_Reborn/Logs/TemporalSampling-20261113 --uniform-only
```

实际 D3D12 捕获的运行错误记录为空；检查过程中没有启动 Unity Test Framework。GPU 耗时、其他图形后端、Sponza、动态几何和灯光变化尚未验证。默认未启用失败候选，因此没有宣称交付画质或性能提升。
