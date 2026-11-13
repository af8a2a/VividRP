# Fine directional importance sampling experiments

2026-11-14，Unity 7000.0.0a16 / DX12。目标是在 Cornell Box 的 **16 px / 每探针 8 rays** 预算内降低噪声；不改变曝光、反弹能量或接收材质。

## 对照方法

两路均使用已验证的 32 帧固定 proposal / 独立数字移位 Sobol 序列。控制组为原 4×4 方向引导，候选使用 8×8 等余弦质量方向格，结合上一帧滤波辐亮度与原始射线端点的密度估计，生成八个互不重叠的矩形分区。每个分区抽一条射线，逆相对 PDF 为 `area / 8`，八条权重之和为 8。

每组预热 512 帧、测量连续 256 帧；静态 Hybrid、静态 SDF Only、Hybrid 两周期固定轨迹均成对采集。Radiosity 固定 8 阶、每批 64 样本、32 批；分析检查每组首尾及组间 atlas 字节完全一致，并核对相机矩阵和几何。每轮独立目录，生产源码在第一份采样前记录 SHA-256、结束后比对。

运动指标为相同 80 个机位的两周期亮度差，按 8 个相邻机位成块 bootstrap。该指标衡量固定轨迹的周期噪声，**不是动态真值误差**。附带的 nearest-depth 新露出诊断不等于生产四 tap 历史重放；后者见 `ScreenProbeMotionHistoryReplay.md`。

`TemporalSamplingExperiment.py` 支持 `fine` 候选，检查 8×8 编码、分区完整覆盖、权重总和和 32 帧 proposal 冻结，并输出包含负收益及条件置信区间的 `sampling-quality.png`。

## 已否决：无象限约束的全域分区

完整六组数据：

```text
E:/VividRP_GI_Evidence/FineDirectionalSampling-20261113-191006-928/
```

| 模式 | 天花板最终方差变化 | 地面最终方差变化 | 右墙最终方差变化 |
|---|---:|---:|---:|
| Hybrid | +80.21% | −21.45% | +41.06% |
| SDF Only | +37.03% | −32.01% | +186.66% |

运动周期 RMS 变化 −2.58%，配对 block-8 条件 95% 区间为 [−7.84%, +3.25%]，不能证明运动改善。所有几何、源缓存、PDF 与冻结检查通过；**采样数学契约通过不能证明最终重建质量改善**。

该分区允许一条射线覆盖很大的余弦质量：天花板 proposal 的最大逆相对 PDF 从控制组 2 增至 4.5，约 1.35% 的描述符权重大于 2。稀疏端点核也会在原本较平缓的光照上形成尖峰。以上是观测到的结构差异，不将其单独当作已证实的因果分解。

## 已否决：仅增加滤波辐亮度峰值门槛

数据及精确源码：

```text
E:/VividRP_GI_Evidence/FineDirectionalSampling-20261113-191558-902/
```

要求原有滤波辐亮度的峰值超过均值三倍后，才允许端点核参与细分。完成的 Hybrid 静态物体 ID 掩码统计：天花板方差仍增加 82.62%，右墙增加 67.39%，地面下降 42.18%。静态门槛失败后，通过工具自带的 owner cancel 结束剩余组，清单 `complete=false`；不把该批当作完整六组验证，也不继续运行更长能量或性能测试。

两个失败版本的 `source-prototype/` 均保留实际源码及补丁，未覆盖原始数据。

## 四象限约束原型

恢复原路径的四个上半球面分区，每个象限至少保留一条射线，其余四条在象限内部细分；保留滤波辐亮度峰值门槛。因此每个叶子的余弦质量不超过 1/4，逆相对 PDF 不超过 2。细格能表示 1/64 的质量；原 4×4 格的最小质量为 1/16。没有增加射线或历史纹理。

`FineDirectionalProposalValidation.cs` 实际检查 131,072 个 GPU 样本，CPU 独立实现 Sobol 数字方向和余弦映射：最大方向误差 8.90×10⁻⁶，FP16 整数 payload 精确，均匀、点峰、条带和双峰四类输入均完整覆盖半球，常量积分质量正确，解析余弦矩检查通过。该检查不替代场景质量或性能验证。

```text
E:/VividRP_GI_Evidence/FineDirectionalValidation-20261113-191940-237/gpu-validation.json
E:/VividRP_GI_Evidence/FineDirectionalSampling-20261113-192001-744/
```

该版本也被否决。已完整结束的 Hybrid 静态配对，正式统计使用相同的物体 ID 掩码：

| 区域 | 最终均值变化 | 最终方差变化 |
|---|---:|---:|
| 天花板 | −0.138% | +50.89% |
| 地面 | −0.036% | −14.73% |
| 右墙 | −0.126% | +4.69% |

天花板方差增加的 block-32 条件 95% 区间约为 [+11.07%, +111.65%]，不满足质量门槛。其余组由 owner cancel 安全结束，`complete=false`，只分析已完整结束的静态配对。实际 proposal 检查确认全部半球质量和冻结约束通过、最大权重为 2、无跨象限矩形。增加约束缩小了部分退化，但不能以地面收益替代天花板验收。

## 逐阶段与频谱证据

同一静态天花板区域，四象限候选相对 4×4 控制组的方差增加：

- Resolve：+10.96%。
- 像素时域后：+45.88%。
- 最终 À-Trous 后：+50.89%。

对每个像素减去其 256 帧时间均值后做 FFT，按 Parseval 等价核对总谱功率与直接计算的方差一致。最终控制组／候选中，周期不短于 32 帧的功率分别占总方差 **96.62% / 96.96%**；候选该频段功率增加 **51.41%**。Resolve 的同一频段也已增加 36.13%，故不能把最终退化归因于一个像素时域 bug。

这支持下一步优先测试 **4×4 原有分区的更新稳定性**：在不更改方向缓存分辨率、射线数和精确 PDF 的条件下，只在有足够预测收益时替换旧 proposal，并保持新光照下的响应。分区抖动是待检验的机制；上述频谱本身不能证明因果。下一轮需要比较分区变化次数、低频功率、最终噪声及平均能量，继续使用相同固定轨迹检查新露出和响应，不能仅延长历史或继续加大空间滤波。

三种 8×8 版本的生产 C# / HLSL / compute 改动均已撤回至 `c01b2d1c` 的实现。仅保留诊断工具、报告和 `Tools~/Diagnostics/Prototypes/` 下的重放补丁；这些补丁不是推荐默认。所有补丁均通过在已恢复源码上的 `git apply --check --unidiff-zero`。没有添加或手改 `.meta`、场景或同步资产。

复现被否决的四象限版本时，在隔离 checkout 使用 `git apply --unidiff-zero Tools~/Diagnostics/Prototypes/FineDirectional-FourFaces.patch`，再运行 GPU conformance，并把 `TemporalSamplingCapture.cs` 的实验名改为 `fine`。没有补丁时，工具明确报告该原型已经撤回。完整分析会拒绝 `complete=false`；有意提前否决的批次用以下命令，只统计已完成的配对：

```powershell
python -B Tools~/Diagnostics/TemporalSamplingExperiment.py E:/VividRP_GI_Evidence/FineDirectionalSampling-20261113-192001-744/capture --candidate fine --static-only --linear E:/VividRP_GI_Evidence/TemporalSamplingCovered-20261113/linear
```

最终图表 `sampling-quality.png` 保留负收益和条件置信区间；`static-analysis.json` 保留逐阶段与频段功率。未将短序列均值当作长期物理收敛证明，未测 GPU 耗时；本轮未推广到 Sponza，也未运行 Unity Test Framework（Editor 正在使用）。
