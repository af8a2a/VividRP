# 原生 Screen Probe 的平面方差重建

在已提交的原生历史编码上，À-trous 现在只在历史成熟、几何和照明连续的区域预滤波噪声方差，并保留相关像素的观测方差下限。Cornell 同帧对照中，地面时域方差下降约 14%–15%，背墙约 4%；天花板收益较小。**这是局部静态降噪改进，尚未证明全场景稳定无噪点或整体移动质量不回退。**

证据：`E:/VividRP_GI_Evidence/ScreenProbeMomentGuidance-20261115-144400`。Unity 7000.0.0a16 / D3D12 / RTX 5070 Ti，原生 Gather、16 px / 每探针 8 条射线、age512 / FP32 anchor、4-pass À-trous、bias=0.04。冻结 Radiosity 为 8 阶 / 64 样本 / 32 批，源 SHA-256 为 `40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8`。场景、Profile、光照增益、时域累积和历史编码均未修改。

## 改动与依据

基线时域噪声的水平相邻像素相关系数：地面约 0.86–0.90，背墙约 0.75，天花板约 0.29–0.31。相隔 8 px 后，地面和背墙仍有较强相关性。连续将方差乘以四分之一会高估稀疏探针像素的独立信息量，令后续尺度过早收紧亮度权重。

`SdfDiffuseAtrous.hlsl` 在已有四个轴向几何检查中，按深度、法线、材质和成熟历史对时域方差做五点预滤波。每轮还检查实际 5×5 核中的全部 24 个邻域 tap，以历史均值差、观测方差和有效年龄判断采样范围是否仍处于同一照明区域。只有连续区域才启用空间方差重建及相关性下限；几何、材质、照明边界和未成熟历史保留原有不确定度。

原有亮度范围权重、实际照明输入、覆盖率和粗尺度几何保护保持原样。均值只用于判断是否允许扩大平滑，不作为最终颜色的历史输入。ReSTIR 保留其现有路径。运行时仅修改该 include 和根 compute 的说明，没有新增 C# 绑定、纹理、历史、序列化参数或托管分配。

## 同帧重放验证

新增 `BuildScreenProbeSameFrameAtrous.py` 在第一轮 dispatch 后、原输入尚未覆盖前，复制像素时域输出。最后一轮完成后，使用同一份复制输入、实际接收面纹理和 moments，分别执行内存中的 A/A shader 与参考滤波器。临时纹理和 shader 不写入 Unity 资产，读回完成后在 `finally` 释放；不改写渲染器的输出、历史或 Surface Cache。

验收使用 `legacy-enhanced-replay`，每次预热 1,024 帧、测量 128 帧，共两次重复。四个 ROI 的 1,024 项复制输入 hash、1,024 项 A/A 输出 hash 全部精确一致；覆盖率相同。两个内存参考源码的实际文件字节 hash、四个 DX12 程序及整个捕获期间的源码均记录在目录中。**A/A 覆盖这四个 ROI，不代表所有未保存像素。** 重放引入额外 dispatch 和读回，不用于 GPU 性能比较。

| ROI | 方差下降，两次重复 | 平均消耗 Y 变化 |
|---|---:|---:|
| 天花板 | 1.27% / 5.39% | −0.096% / −0.107% |
| 地面 | 15.46% / 14.13% | −0.035% / −0.057% |
| 背墙 | 3.64% / 3.80% | −0.060% / −0.062% |
| 光片边界大 ROI | 0.007% / −1.16% | 约 −0.102% |

天花板第一次重复的条件 block bootstrap 区间跨过 0，不能把小幅点估计当成稳定收益。地面与背墙两次重复的区间均为正。光片边界没有明显改善，第二次重复方差略增；28 个实际探针中心的均值、时域 RMS 和参考误差基本保持一致，新旧滤波结果的这些统计差异约为 `1e-8`，没有重新出现先前的错误提亮。相对高采样参考的 RMS 仍约 0.01373 / 0.01475；参考共享冻结 Surface Cache，不是绝对 Unreal 真值。

## 移动和正常状态

`legacy-motion` / `legacy-enhanced-motion` 只捕获两次固定移动轨迹，避免重复已验证的静态数据。派生几何、法线、有效性和逆 VP 完全一致，但有 8 条全屏 GBuffer hash 差异，跨运行比较仍为 `qualified=false`。检查没有增加位置容差或舍弃失败帧；未观测的静态分组明确返回 `staticQualified=null`。

每次轨迹各 3,894 个新露出像素，旧像素历史接受数均为 0，像素时域减 Resolve 的 P99 / 最大差均为 0，moments 年龄全部为 1、方差为 0。最终减 Resolve 的 P99：原路径为 2.420 / 2.253，新路径为 2.356 / 1.875；最大差第一组略增。该量包含空间滤波，且跨运行输入未完全匹配，不能据此宣称整体移动质量不回退。

正常加载状态下，一次 GPU 观测得到 194 个有效、count=1 的样本：GI 总均值 3.135 ms、P95 3.332 ms，四轮 À-trous 均值合计约 1.703 ms。这不是合格的 GPU ABBA 对照，不发布新增开销或性能不回退结论。绑定与 `BindProbeFrame` 预热 128 次、测量 128 次，当前线程托管分配为 0；历史在渲染回调内有效。不代表整帧零分配。

实际旧默认和最终两个 wavelet DX12 变体、真实 GPU 重放、文件 hash、源码快照、场景/Profile 恢复检查通过。同帧生成器 2 项、源码快照 5 项、证据保留工具 10 项 Python 回归检查通过。未在活动 Editor 中运行 Unity Test Framework。

## 未保留的实验与存储

直接以半精度历史均值引导亮度权重、Gaussian 范围权重、无边界保护的方差下限，均出现平面或边缘噪声回退，未保留。均值拆成两份半精度后，数值更新停滞显著减少，但最终噪声没有一致改善，因此该编码和新 kernel/history ID 也未合入默认。运行时仍使用已提交的 `ScreenProbeTemporalReprojectionWithMomentsCS` 和原 RGBA16F 编码。

首个同帧工具原型在 Windows 换行转换前计算参考 hash，协议校验失败；原记录保留为不合格诊断。生成器改为对实际写入字节取 hash，后续捕获严格通过。增加了真实命令行生成的 hash 回归检查与已有证据不可覆盖检查，未放宽验证门禁。

按明确的 `archive-policy.json` 清理 11 个已完成、不保留候选的 53,172 个中间 `.bin` 帧，共约 4.623 GiB。报告、实际脚本、源码快照、程序、首末帧和有效对照完整保留；这些已归档候选的全部中间帧不再可重分析，逐文件删除 hash、边界及时间戳检查记在各自 `raw-retention.json` / `raw-retention-files.jsonl`。没有删除高采样参考、旧默认移动对照或最终受测数据。

清理后本轮目录约 1.851 GiB，磁盘剩余约 115.80 GiB。结合上一轮盘点估算全部 GI 证据约 120.95 GiB，低于 125 GiB 软上限；其他目录占用沿用上一轮基数，不是本轮重新扫描所得。精确工具归档于 `analysis-tools`，最终保留的四份运行时源码在 `retained-source`，均带 SHA-256 清单；`candidate-source` 则是未保留的历史编码实验。

## 复现与后续

```powershell
python -B Tools~/Diagnostics/BuildScreenProbeSameFrameAtrous.py NEW_OUTPUT --control-body BEFORE_SdfDiffuseAtrous.hlsl
python -B Tools~/Diagnostics/AnalyzeScreenProbeAtrousExperiment.py NEW_OUTPUT --same-frame --reference-root E:/VividRP_GI_Evidence/ScreenProbeResidualMap-20261115-104052-278383
python -B Tools~/Diagnostics/BuildScreenProbeAtrousExperiment.py NEW_MOTION --motion-history --motion-only
python -B Tools~/Diagnostics/AnalyzeScreenProbeAtrousExperiment.py NEW_MOTION --baseline MOTION_CONTROL --motion-only --compact-motion
```

执行前 pin 实际 C# 和两个内存 shader 源码，结束立即核验。每次仍受 512 MiB 上限约束。最终有效数据与精确分析工具独立保留，正常设置恢复、回调和覆盖参数清空，播放暂停。

后续需要定位移动 GBuffer 的不确定性，补全同帧运动滤波对照和受控 GPU A/B；同时核对去噪统计、条件辐亮度、覆盖能量与 Deferred 实际消费的域是否一致。动态光照变化及 Sponza 整体质量尚未重新验收，Lumen 质量目标仍未完成。

后续受控实验已补上移动边缘 ROI 的同帧对照，并审计覆盖率与统计引导的波动，见 [移动验证与滤波噪声定位](ScreenProbeWaveletMotionAndCausalAudit.md)。该轮没有采用新的渲染器候选，也没有将这里未通过的跨运行比较改判为通过。
