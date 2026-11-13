# 固定分布的分块 Screen Probe 采样实验

Unity 7000.0.0a16 / DX12，Cornell 顶部单面自发光场景，1920×1080、16 px 探针网格、每探针 8 条射线。默认仍为独立白噪声；本实验由 Editor 诊断 bit `8388608` 启用，没有提高每帧射线预算。

## 原因与实现

修复 covered pixel history 后，连续 Sobol 的独立 2,048 帧验证仍出现 Hybrid 地面 +2.466%、SDF Only 地面 +1.811% 的最终能量差。单纯降低时间方差不能作为启用依据。自适应采样分布依赖先前同序列的结果，是需要隔离的相关性来源；该实验不假定它是所有能量差的唯一原因。

- 每个探针使用 32 帧的 Sobol net。块内固定重要性分区，块间重新生成 digital shift；探针按自身 hash 错开块边界。
- 分区在使用该块随机偏移之前构造，继续完整覆盖余弦半球并使用原逆 PDF。每帧重新追踪当前几何和光照；不会把旧辐亮度当作新命中，也不重投影分区。
- 新增独立的 camera-owned proposal 双缓冲。失去历史且尚未到块边界时，使用固定余弦分区，避免在同一块中重新自适应。
- Screen Trace 与 SDF Gather 使用同一份分区纹理和采样函数。方向缓存、材质积分、覆盖率、历史拒绝及 À-trous 参数保持一致。
- 沿用现有 Guide dispatch；块内仅复制分区，没有新增 GPU pass。1080p 自适应探针下，RGBA16F 双缓冲描述符预算为 2,088,960 B（1.992 MiB / camera）；活跃时替代原 0.996 MiB transient guide，净增约 0.996 MiB。关闭实验后，已有 history 由 camera history owner 保留并负责释放。

## 已完成的验证

生产采样 helper 的 6,144 个 GPU 样本与独立 CPU 方向数实现逐项一致。连续 Sobol 的 16×16 分层与分块 Sobol 的 8×4 分层均通过，块间没有重复短循环，诊断 shader 无编译消息。

默认 flags=0 的 8,970 份 GPU 数据与修改前逐字节一致，包含静止与运动、命中、SH、像素历史和最终输出。新分支未改变默认结果。

冻结相同的 8 阶 Surface Cache（每批 64 样本、32 批），每组预热 512 帧后测量连续 256 帧：

| 路径 | 天花板最终时间方差下降 | 地面 | 右墙 |
|---|---:|---:|---:|
| Hybrid | 85.94% | 75.14% | 80.21% |
| SDF Only | 80.37% | 66.86% | 82.00% |

这里是时间方差下降，不能等同于 RMS 的同等百分比下降，也没有把白噪声参考作为物理真值。

静止 Hybrid、静止 SDF Only、固定往返轨迹分别检查了 41,500 个块内探针帧，实际分区纹理保持逐字节不变；边界更新次数为 924 / 936 / 1,202。每帧分区均无重叠或缺口，逆 PDF 质量和保持 8。

固定轨迹下 A/B 几何与 VP 逐位一致，28,104 个明确新露出样本均未继承 age>1 的旧像素历史；Temporal 相对当前 Resolve 的 P99/max 均为 0。同姿态往返差值 RMS 从 0.656511 降为 0.455109，绝对 P99 从 2.22659 降为 1.67166。这个检查不代表所有探针层历史或动态物体已验证。

## 能量门槛：仍待确认

独立帧相位的 2,048 帧长序列结果：

| 路径 / 区域 | 最终均值变化 | block-128 条件 95% 区间 | ±1% 检查 |
|---|---:|---:|---|
| Hybrid / 天花板 | −0.211% | −0.452～+0.003% | 通过 |
| Hybrid / 地面 | +0.636% | −0.329～+1.556% | 未证实 |
| Hybrid / 右墙 | −0.528% | −0.865～−0.184% | 通过 |
| SDF Only / 天花板 | −0.332% | −0.576～−0.098% | 通过 |
| SDF Only / 地面 | +0.277% | −0.724～+1.259% | 未证实 |
| SDF Only / 右墙 | −0.368% | −0.655～−0.060% | 通过 |

六组点估计都在 ±1% 内，但两个地面区间还不够窄，**不能宣布能量等价或改成默认**。另外预先设定独立相位 15,731、每组 8,192 帧的四组确认实验，保留相同 ±1% 点估计及区间门槛。

8,192 帧确认实验的 Hybrid 两组现已完成，VP 逐位相同，源缓存首尾和两组之间的 SHA-256 相同；三个区域均通过原门槛：

| Hybrid 区域 | 最终均值变化 | block-128 条件 95% 区间 |
|---|---:|---:|
| 天花板 | −0.075% | −0.203～+0.049% |
| 地面 | +0.344% | +0.017～+0.672% |
| 右墙 | −0.071% | −0.309～+0.163% |

这是已完成 Hybrid 对的结果，不代表尚在运行的 SDF Only 两组已通过；默认行为继续保持不变。

## 计时与剩余检查

首次无纹理回读的 GPU ABBA 计时不可用：原始数据包含约 `1.794e12 ms` 的未校准时间戳及跨大量脚本帧不更新的旧值。分析器拒绝没有有效 profiler publication index、计数不为 1、数据不新鲜的记录；本次没有可报告的 GPU 性能变化。后续计时工具显式开启并恢复 GPU profiler publication，尚需实际复验。

相机独立 proposal history 的稳定分配/轮转检查、8,192 帧能量确认、有效 GPU 计时、灯光变化响应以及 Sponza 帘边/侧廊验证仍未完成。Editor 运行期间未启动 Unity Test Framework。目标“稳定无噪点并对齐 Lumen”尚未达到。

## 数据与复现

```text
E:/VividRP_GI_Evidence/BlockedTemporalValidation-20261113-151050-565/gpu-validation.json
E:/VividRP_GI_Evidence/BlockedTemporalSampling-20261113/default-baseline-equivalence.json
E:/VividRP_GI_Evidence/BlockedTemporalSampling-20261113/blocked/capture.json
E:/VividRP_GI_Evidence/BlockedTemporalSampling-20261113/analysis.json
E:/VividRP_GI_Evidence/BlockedTemporalSampling-20261113/reference-blocked/capture.json
E:/VividRP_GI_Evidence/BlockedTemporalSampling-20261113/energy-reference.json
E:/VividRP_GI_Evidence/BlockedTemporalSampling-20261113/blocked-sampling-quality.png
E:/VividRP_GI_Evidence/BlockedTemporalGpu-20261113-152436-253/analysis.json
E:/VividRP_GI_Evidence/BlockedTemporalConfirmation-20261113/reference-confirm-blocked/
E:/VividRP_GI_Evidence/BlockedTemporalConfirmation-20261113/hybrid-confirmation-partial.json
```

`TemporalSamplingCapture.cs` 的 `blocked` 模式采集六组短序列，`reference-blocked` 采集两组 2,048 帧候选，`reference-confirm-blocked` 采集四组 8,192 帧确认数据。使用新目录重跑，脚本拒绝覆盖；`cancel.txt` 可请求结束并恢复现场。参考阶段只读回最终区域及首尾源缓存，减少诊断 I/O，渲染算法与逐帧采样预算相同。

```powershell
unity command script eval --project-path E:/VividRP_Reborn --file E:/VividRP_Reborn/Packages/VividRP/Tools~/Diagnostics/TemporalSamplingValidation.cs --timeout 180 --format json
python -B Tools~/Diagnostics/TemporalSamplingExperiment.py E:/VividRP_GI_Evidence/BlockedTemporalSampling-20261113/blocked --candidate blocked --linear E:/VividRP_GI_Evidence/TemporalSamplingCovered-20261113/linear
python -B Tools~/Diagnostics/TemporalSamplingExperiment.py E:/VividRP_GI_Evidence/BlockedTemporalSampling-20261113 --reference-only --candidate blocked --control-root E:/VividRP_GI_Evidence/TemporalSamplingCovered-20261113 --linear E:/VividRP_GI_Evidence/TemporalSamplingCovered-20261113/linear
python -B Tools~/Diagnostics/TemporalSamplingConfirmation.py E:/VividRP_GI_Evidence/BlockedTemporalConfirmation-20261113/reference-confirm-blocked --linear E:/VividRP_GI_Evidence/TemporalSamplingCovered-20261113/linear
```
