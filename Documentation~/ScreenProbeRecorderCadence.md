# 原生 Gather 的 recorder / 读回节奏控制

2026-11-15，Unity 7000.0.0a16 / D3D12 / RTX 5070 Ti。纯自发光 Cornell Box，同机位1920×1080，原 shader、FP16/128、16 px / 8 rays、四轮 À-trous，源8阶 / 64样本 / 32批。本轮没有改变生产渲染行为，也没有验证新的画质收益。

## 实验与门槛

[上轮长 A/A](ScreenProbeContinuousPixControl.md)在第1727帧发现 raw 分歧，但密集读回结束与 recorder 开启之间没有观测。本轮将两个因素分开，全部使用自然 PlayerLoop，不启用 PIX 捕帧、额外 join、GPU 见证或 shader 原型。

- 所有组保持 Unity GPU profiler 开启，Editor profiling关闭；仅 marker recorder 是因素之一。
- 第1152帧按条件启动7个 GPU marker recorder。
- 另一因素是在第1024～1726帧每帧读取 geometry 的1×1区域，16 B / 次，共703次；它会影响资源转换和调度，不是生产修复。
- 每组独立冷启动，源准备264帧，随后固定 Gather epoch0～1727。前1024帧及末帧读取阶段 hash，末帧读取源与几何。不同条件按 `00,10,11,01,01,11,10,00` 交错，每种两次。
- 因果解释要求共同前缀、CPU状态、冻结的源与几何一致；不丢弃失败组，不把诊断运行的 GPU 样本用于候选性能比较。

生成脚本为 [BuildScreenProbeRecorderCadenceExperiment.py](../Tools~/Diagnostics/BuildScreenProbeRecorderCadenceExperiment.py)，分析为 [AnalyzeScreenProbeRecorderCadence.py](../Tools~/Diagnostics/AnalyzeScreenProbeRecorderCadence.py)。生成器校验原模板的每个替换片段且拒绝覆盖已有目录。实际执行脚本、协议、模板 hash 及源码指纹均保留。

```powershell
python -B Tools~/Diagnostics/BuildScreenProbeRecorderCadenceExperiment.py E:/VividRP_GI_Evidence/<fresh-directory>
# 先固定源码与实际脚本，使用已授权的 unpaused PlayMode 运行生成脚本，结束后验证指纹并恢复原暂停状态。
python -B Tools~/Diagnostics/AnalyzeScreenProbeRecorderCadence.py E:/VividRP_GI_Evidence/<completed-directory>
```

## 结果：共同前缀失败，不能归因

证据：`E:/VividRP_GI_Evidence/ScreenProbeRecorderCadence-20261115-054646-719003`。

8组均完成1728帧，其他相机零帧、运行错误为空；四个 pulse 组各703次全部完成，无待处理请求。四个 recorder 组各7个有效 recorder，每个575个样本，无环绕；关闭组没有启动 recorder。此处样本数只验证因素生效，不代表有效的性能比较。

2,783份源码及一份外部协议、一份实际脚本核验未变。三个检查点的源与几何完全一致；全部受检 CPU epoch、history/guide状态、jitter等一致。148,232份纹理观测中，共同前缀有129,507对比较，仍不一致：

- 相对首组，六组在第1帧的 `directional-temporal` / `directional-filtered` 首次出现功能结果分歧，第2帧起传播至 proposal / raw / composite。
- `05-r1-t1` 的共同前缀保持到第30帧，第31帧先在 temporal / filtered 分歧，第32帧起传播。第31帧的 geometry / raw / composite 仍与首组相同。
- 所有分歧均早于第1024帧的 pulse 和第1152帧的 recorder。故本运行既不能证明二者导致异常，也不能证明二者没有影响。

完整 reset 帧 ray atlas 的 hash 在部分组已不同，可能包含未使用存储；本轮未保存对应完整 payload，不能作有效 lane 的掩码比对。原始观测保留，严格门槛不忽略它们；上面的功能阶段结论单列 geometry / raw / composite / temporal / filtered，不能扩大成所有输入都已相同。

同条件重复组末帧 raw 的差异如下，均为全体16,320个探针 texel，**不是最终像素或天花板 ROI 的噪声指标**：

| recorder | 1×1 pulse | 两次组号 | 不同 raw texel | 最大分量差 |
|---|---|---|---:|---:|
| 关 | 关 | 00 / 07 | 3,240 | 80.078125 |
| 开 | 关 | 01 / 06 | 3,296 | 75.765625 |
| 开 | 开 | 02 / 05 | 3,206 | 67.5 |
| 关 | 开 | 03 / 04 | 3,229 | 82.921875 |

`analysis.json` 明确 `observationGatePassed=false`、`performanceComparisonValid=false`、`renderingChange=false`。可选的 first-hit / ray diagnostics 在实际脚本中按开关读取，本次两类均未观察；分析器要求一旦出现就完整覆盖全部组，否则明确报告缺失观察。初版错误地把它们列为必选的分析保存在 `analysis-initial-required-optional.json`，不将其缺项误报成运行渲染错误；修正后共同前缀仍失败。

## 验证与收尾

生成的 C# 在 live Editor dry-run 编译通过；原脚本实测完整运行，Python语法与真实数据分析通过。没有运行 Unity Test Framework，没有新建 PIX 捕帧，没有声称 GI 画质或耗时改善。

恢复检查确认正常 temporal 为1920×408，原FP16/128、16/8、四轮、bias0.04、源8/32；两个场景干净，回调为空、override=-1、flags0，PIX idle，Playing=true / Paused=true，autoRefresh0。Console 游标174→202无新错误、无drop/reset。

只保存声明的96份原始载荷，**149,492,736 B（142.57 MiB）**，逐份SHA通过，低于192 MiB上限。完整阶段 hash、CPU记录、实际脚本、恢复记录与报告保留，目录加入保护；容量复核见 [GI 数据保留](GiEvidenceRetention.md)。

下一步应围绕 temporal 首次分歧的真实输入绑定与历史读取建立更短、可复现的控制；尤其保留第30→31帧这种前缀一致的样本。当前没有足够证据添加生产栅栏、量化几何或推广FP32/512，也不能将上一轮单个1727检查点直接视为 recorder 导致。

后续[64帧窗口与冻结原kernel重放](ScreenProbeDirectionalBinding.md)已保存另一例第1→2帧分歧；1,512对CPU history内容链接一致，克隆与直接原对象的34对重放一致，原dispatch的实际常量与读取时序仍需取证。
