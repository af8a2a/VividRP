# 等重要性分层的方向引导实验

2026-11-14，Unity 7000.0.0a16 / DX12，顶部单面发光 Cornell Box。固定原机位、1920×1080、16 px 网格、每探针 8 rays、原生 Screen Probe Gather 和护边 Atrous3。没有修改曝光、接收材质、光强、积分 π 或 Surface Cache。

## 算法与契约

现有 4×4 余弦图用八个矩形分区分配射线，亮区最多细分到一个单元。原型使用 50% 均匀探索与 50% 历史辐亮度构建 CDF，按八个等重要性分位点划分 row-major 图坐标 `[0,16]`。保留原先的方向峰值门槛及无历史余弦兜底。

每条射线仍在自己的区间内按**基线余弦测度均匀采样**，逆相对 PDF 为 `0.5 * (end-start)`。八个区间完整覆盖半球、互不重叠，权重和保持 8；不是按未知随机总权重再归一化。共享端点以相同 RGBA16F 值写入，量化后仍保持分区闭合。实际最大逆 PDF 为 2.00390625，略高于理论上限 2 来自端点量化。

描述符为 `(start,end,source,-7)`；`-5/-6` 保留给已归档的 8×8 原型。复用原八条 RGBA16F guide 描述符，没有增加纹理或追踪预算。Screen Trace 与 SDF 使用同一采样 helper，覆盖率与有效黑命中的契约不变。仅 Editor 诊断 bit `134217728` 启用；独立白噪声采样，禁止与冻结 QMC 分区或 ReSTIR 混用。没有添加序列化的质量默认。

## GPU 与重复性验证

- 128 组分布、1,024 个描述符、65,536 个 GPU 方向样本与独立 CPU 公式比对。包含零分布、常量、单亮单元、HDR、低能量、条带及随机分布；方向比对使用单位局部基底，不能代表任意世界朝向的独立覆盖。
- 最大区间边界误差 0.00780955，逆 PDF 质量和误差 0，最大方向误差 3.33×10⁻⁵；实际半精度纹理无空区间、缺口或重叠。
- 测量前清理目标相机的 CPU/GPU 历史；两次默认 A/A 的 5,890 个受检文件逐字节相同。
- 实际 DX12 字节码反汇编确认 Screen Trace、两种 Mesh SDF 追踪和 CDF producer 包含 `-7` 分支。三轮末级滤波字节码仍为 `31fbe530…b3d3148`。
- 短序列前后 2,838 个受检源码、夹具和诊断文件校验和一致。所有静止、运动与两种追踪模式的 Radiosity atlas 首尾一致，SHA-256 为 `8d1b4e596e3ef067de430082d0d25763b9df83c25b57422acbdd0e33c73d6e62`。

## 短序列结果

固定 8 阶 Radiosity、64 samples / batch、32 batches；每组预热 512 帧，连续测量 256 帧。均值口径为消费 coverage 后、接收材质之前的线性 E/π，不能当作独立物理真值。

| 路径 / 区域 | 最终时间方差下降 | block-32 条件 95% 区间 | 最终均值变化 |
|---|---:|---:|---:|
| Hybrid / 天花板 | 47.86% | +22.87～+64.89% | +1.264% |
| Hybrid / 地面 | 68.46% | +62.81～+73.92% | −0.092% |
| Hybrid / 右墙 | 30.06% | +13.22～+42.86% | +0.083% |
| SDF Only / 天花板 | 8.70% | −78.08～+52.75% | +1.350% |
| SDF Only / 地面 | 61.29% | +46.68～+73.12% | +1.740% |
| SDF Only / 右墙 | 45.58% | +26.76～+62.09% | −0.648% |

固定两次 128 帧正弦往返、80 个匹配姿态：最终差值 RMS 从 0.669342 降至 0.542008（−19.02%，block-8 条件区间 −24.30～−12.77%），绝对 P99 从 2.43768 降至 2.07871。两组相同的 28,104 个明确新露出样本均未继承 age>1 的旧像素历史，Temporal 相对当前 Resolve 的 P99/max 均为零。该结果不等于所有探针层历史或动态物体均已验证。

两条路径天花板的大窗口原始探针均值仅变化 +0.083% / +0.262%，不能直接与较小像素 ROI 的约 +1.3% 相减并归因于重建。缩小到对应均匀网格范围的 24 个探针后，原始命中均值变化为 +1.253% / +0.980%，anchor 为 +1.475% / +1.304%。这是相同屏幕范围的近似，不是精确双边插值权重回放。`aligned-probe-audit.json` 保留这项口径检查。

## 独立长序列与采用门槛

短序列未通过原定的六区域均值 ±1% 与方差置信区间均为正的门槛，因此没有启用默认。追加独立 frame phase 7919、每组 2,048 帧的四组静止确认；仍清理相机历史、预热 512 帧并固定相同源缓存。能量门槛同时检查 Y 和 RGB 各通道的点估计及 block-128 条件 95% 区间，全部必须在 ±1% 内；没有通过曝光或增益拟合匹配。

四组已全部完成。两条路径的天花板能量均通过 ±1% 的 Y/RGB 门槛，短序列的约 +1.3% 没有稳定复现；但其他必要门槛没有通过：

| 路径 / 区域 | 最终方差下降 | block-128 条件 95% 区间 | Y 均值变化 | Y/RGB 能量检查 |
|---|---:|---:|---:|---|
| Hybrid / 天花板 | 5.16% | −18.63～+22.35% | −0.026% | 通过 |
| Hybrid / 地面 | 54.77% | +39.96～+64.95% | +1.039% | 未证实 |
| Hybrid / 右墙 | 13.25% | −7.71～+28.64% | −0.228% | 通过 |
| SDF Only / 天花板 | 20.63% | −6.68～+41.77% | −0.379% | 通过 |
| SDF Only / 地面 | 54.90% | +42.33～+63.81% | +0.560% | 未证实 |
| SDF Only / 右墙 | 9.04% | −8.56～+23.22% | +0.039% | 通过 |

Hybrid 地面 Y 的区间为 +0.161～+1.973%，RGB 上界最高约 +2.125%；SDF 地面 Y 的区间为 −0.332～+1.441%，RGB 上界最高约 +1.546%。因此既不能宣布能量等价，也不能把它当作长期系统偏差已经证实。两条路径的天花板和右墙方差区间均跨零，短序列收益不足以支持默认采用。没有继续调整探索比例、峰值门槛或挑选有利相位以绕过检查。

长序列首尾及四组之间的源缓存仍与短序列完全一致，探针几何、配对 VP、完整半球支持和质量和通过；前后 2,838 个受检文件未变。最终报告 `all_energy_passed=false`、`all_noise_reductions_confirmed=false`。

分析器以分块一／二阶充分统计量计算逐像素时间方差，并与直接 `numpy.var` 独立核对；已检查相同输入通过能量一致性、2% 能量变化被拒绝。长序列同时记录大探针窗口和与天花板 ROI 对齐的网格范围。

## 数据与限制

```text
E:/VividRP_GI_Evidence/CdfProposalValidation-20261114-033914-332/gpu-validation.json
E:/VividRP_GI_Evidence/CdfDirectionalSampling-20261114-034031-433/analysis.json
E:/VividRP_GI_Evidence/CdfDirectionalSampling-20261114-034031-433/aligned-probe-audit.json
E:/VividRP_GI_Evidence/CdfDirectionalSampling-20261114-034031-433/compiled-tracing/
E:/VividRP_GI_Evidence/CdfDirectionalSampling-20261114-034946-575/
E:/VividRP_GI_Evidence/CdfDirectionalSampling-20261114-034946-575/energy-reference.json
```

没有有效 GPU 耗时数据；没有运行 Unity Test Framework（Editor 正在使用）。没有验证 Sponza 或光照切换，不能据此宣称全场景收益。实际 PNG 中背墙和盒子仍有可见低频斑块，目标“稳定无噪点并对齐 Lumen”尚未达到。

## 撤回、重放与下一步

原型未通过完整门槛，已从三份运行时 C#/HLSL/compute 和独立 Editor 验证 shader 中撤回，这四份文件与实验前 HEAD 完全一致。当前 Cornell 仍使用护边 Atrous3 和原独立采样默认。保留分析工具、检查口径及重放补丁；没有把本轮称为已发布的画质提升。

实际 Editor 复验确认原型字段已卸载、C# 编译完成，目标相机为原生 Gather、三轮滤波、16 px / 8 rays、正常 8 阶 / 32 批；诊断回调为空、flags=0、frame override=-1、强制双面关闭、bias=0.04，两个原场景均未脏。默认 shader 检查没有 Error，保留原实现导入后报告的 51 条静态分析／寄存器压力等 Warning；未因此增加后端兼容改动。状态与消息保存于长序列目录的 `final-verification.json`、`default-denoiser-verification.json`。

`Tools~/Diagnostics/Prototypes/CdfDirectional-RowMajor.patch` 保存生产与 Editor shader 的零上下文补丁。先在隔离 checkout 执行：

```powershell
git apply --check --unidiff-zero Tools~/Diagnostics/Prototypes/CdfDirectional-RowMajor.patch
git apply --unidiff-zero Tools~/Diagnostics/Prototypes/CdfDirectional-RowMajor.patch
unity command script eval --project-path E:/VividRP_Reborn --file E:/VividRP_Reborn/Packages/VividRP/Tools~/Diagnostics/CdfDirectionalProposalValidation.cs --timeout 240 --format json
```

重放质量捕帧时，将 `TemporalSamplingCapture.cs` 的 `experiment` 改为 `cdf`（256 帧和固定轨迹）或 `reference-cdf`（独立 2,048 帧），再运行该工具。未应用补丁时会明确拒绝 CDF 捕帧。长序列分析使用 `CdfDirectionalEnergyExperiment.py` 的 `--linear` 与 `--short` 参数，分别传入线性夹具目录和同一缓存短序列的 `analysis.json`。补丁已完成撤回后 `git apply --check` 验证。

撤回后的 GPU 验证工具已实际触发保护检查：CLI 按预期返回明确的原型撤回错误，记录于 `replay-guard.json`。这是有意验证的拒绝路径，不属于上述捕帧的渲染错误，也没有分配临时 GPU 资源或修改场景。

下一项应比较**二维空间紧凑的等重要性分层**：在余弦图上按重要性中位线递归划分八个矩形，允许亮单元内部继续细分，避免 row-major 区间跨行时拼接相距较远的方向。仍保持完整支持、权重和 8、相同射线预算，并在半精度量化后验证共享边界闭合；先做独立 GPU/CPU 一致性，再沿用静态、轨迹和长序列能量门槛。它是下一项待验证方案，不能从当前实验直接推导出画质收益。
