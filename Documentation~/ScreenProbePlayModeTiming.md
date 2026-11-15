# Screen Probe 播放模式计时与输入重复性

重启并注入 PIX 后的原 shader A/A 仍在第1帧方向时域结果失配；精确捕获的两份同帧 temporal 则输出相同。实际 DXIL、常量寄存器与资源顺序的对照及其观测限制见[成对 PIX 检查](ScreenProbeTemporalPixPair.md)。候选计时仍未合格。

后续的原 shader 整数 visibility / SDF buffer 对照、精度候选撤回与原生绑定检查见 [追踪重复性定位](ScreenProbeTraceRepeatability.md)。下文保留首次播放模式诊断当时的检查范围。

2026-11-15，Unity 7000.0.0a16 / D3D12 / RTX 5070 Ti。播放模式的 GPU-only recorder 已可用，但 FP32/512 的受控 A/B 尚未合格；保存的默认配置继续使用 FP16/128。本轮没有修改 Runtime、Shader 或保存的 Profile，也没有候选 GPU 成本、性能改善或新增 GI 质量改善结论。

## 可用计时与实验条件

`ScreenProbeAnchorGpuTimingProbe-20261114-223553-035` 的七个 marker 各返回 192 个正值 GPU 样本，sample count 均为 1；目标相机 258 次渲染、其他相机零次。`Diffuse GI Total` 的中位数为 2,600,832 ns。这只是 recorder 可用性数据，不是 FP16/128 与 FP32/512 的性能比较。

此前两次 Edit Mode 探针计时为空，不能记作 0 ms。播放模式关闭 Editor profiling，使用自然 Player Loop；后续工具不在播放模式额外请求 Editor repaint。Profiler、GPU area、Editor profiling 与相机状态在 `finally` 恢复。没有 Nsight/PIX GPU trace；Nsight 的已注入目标列表为空也不等于普通 Editor 可直接附加。

完整计时协议先运行两个 FP16/128 冷启动 A/A，再运行两组 ABBA：128/512/512/128。每组保留 16 px 网格、每探针 8 rays、原生 Gather、guarded anchor 预滤波、四轮 À-trous 和 8 阶/64 样本/32 批 Radiosity。相机为 `(0, 1.5, -3.8)`、单位旋转、FOV42、1920×1080。太阳、天空和额外近场遮挡保持原纯自发光实验条件。

每组清理相机 GI/CameraHistory，固定源 seed0，在源完成后等待八个稳定帧，再启动相同采样序号的 Gather epoch。计时前预热 1,024 帧、留出 128 帧读回排空区间，再观察 512 帧并排空 64 帧。预定统计丢弃 recorder 返回样本的首尾各 64 项，至少保留 320 项，不能把异步 GPU 样本冒充精确的 CPU 帧对应关系。任何输入不一致、空值/零计时、多相机、计数异常或环形覆盖都会拒绝比较。

## 输入差异的定位

所有长 A/B 都在原始输入检查失败后被拒绝。最初八组没有充分重置源状态；随后增加完整冷启动、源屏障、自然 Player Loop、实际帧序号和逐阶段读回，仍未通过 A/A。没有使用这些失败运行发布候选耗时百分比。

Bright 与 Emissive Ceiling 两场景均加载，几何启用，但分别位于 30/29 层；追踪代码按相机 mask 筛选。临时关闭非目标场景根节点后仍有分歧，因此没有将双场景直接认定为原因。根节点 active 标志已恢复，不保存场景。

| 诊断目录（均在 `E:/VividRP_GI_Evidence`） | 已观察事实 |
|---|---|
| `ScreenProbeDirectionalHistoryInputs-20261114-232229-529731` | 前三帧的历史纹理和当前输出对应正确，两组矩阵相同；后期 raw 不一致。不能认定历史交换别名或缺栅栏。 |
| `ScreenProbeDenseInputHashes-20261114-233923-261783` | 第 2 帧一条射线的距离由 1.193359375 变为 1.19140625，命中法线元数据相差一个半精度量级；raw RGB、几何、GBuffer、HZB mip0 一致。随后方向滤波开始出现差异。 |
| `ScreenProbeTraceInputHashes-20261114-234243-546604` | 同一条屏幕射线由零变为有效黑命中的像素编码；该编码不是距离。HZB 0～4 层、GBuffer、几何和前帧辐射度一致。首次分歧在屏幕追踪，不能归给下游历史权重。 |
| `ScreenProbeDirectDepthHashes-20261114-234735-781902` | 复用已有 `KCopyDepth` 读取 GI 当前深度；直接深度、motion、前帧屏幕几何和 HZB 0～4 一致。第 2 帧接收点位置有 3,478 个 texel 不同，最大 7.152557e-7 m；方向结果随后不同。 |
| `ScreenProbeInverseVpInputs-20261114-235131-623461` | 仅用于输入定位的短 A/A，64 帧预热、两组128；再次出现位置差异。前 0～2 帧 CPU 当前/上一帧 VP、实际 inverse VP、camera forward 和 light scale 均相同。不是完整 GPU A/B。 |

读回会改变观测时序，不同诊断的首次已观察分歧不完全相同。上述“相同”指对应纹理读回 SHA 或记录的 CPU 数值；没有读取 dispatch 当时的 GPU 常量缓冲，不能据此证明 GPU 绑定或同步正确，也不能宣布存在 GPU race。

原生 depth/整数 visibility 的直接 AsyncGPUReadback 不受当前 Editor 支持，第一轮在计时前终止。后续使用已有深度复制 kernel 和 owned R32F 纹理，等待读回结束后释放。整数 visibility 没有读回。下一步应捕获实际 dispatch 的常量/资源绑定及顺序，解释“相同已观测输入、不同重建/屏幕结果”，通过重复性检查后再量化候选成本。

## 工具、验证与恢复

- `ScreenProbeAnchorGpuTimingProbe.cs`：先确认 GPU marker 可用，支持 Edit/Play Mode，播放模式使用自然帧循环。
- `ScreenProbeAnchorGpuTimingCapture.cs`：完整 A/A→ABBA 模板，逐阶段哈希、实际格式验证、失败即停和状态恢复。短定位运行的实际脚本与缩短协议另存于证据目录，没有将它当完整计时。
- `ScreenProbeTimingQualification.py`：核对完成状态、源码/输入指纹、A/A、输入端点、完整密集哈希、四张 anchor 实际格式及 GPU 样本；失败报告不提供性能结论。无效的 reset 帧 previous storage 不参与历史内容比较。

密集诊断的三个阶段均核验 2,782 份源码/场景文件、独立协议和实际执行脚本不变。最新脚本主体实际经 live eval 编译并运行，因明确的 A/A 差异退出；完整 ABBA 未通过。本轮新增的三项 Python 回归通过，覆盖空/零 GPU 计时、raw 不变但射线元数据变化、密集序列缺帧不能只凭端点通过。运行中的 Editor 未执行 Unity Test Framework。

`normal-state.json` / `normal-camera.json` 确认回调清空、override=-1、flags=0、实际 age128/16 px/8 rays/四轮/bias0.04/源8阶32批；两场景干净、Edit Mode、自动刷新0，相机恢复原姿态/FOV42。

## 存储控制

旧隔离诊断预计保存 512 MiB，实际约 914 MiB；此前只是预算，没有硬性限制。新模板只常规保存前三帧和差异载荷，其余保留 SHA，并在每次写入前检查 **1 GiB 二进制载荷硬预算**；超出即停止，保留已有证据，不在活动捕帧中删除。短 inverse-VP 运行实际保存约 275.61 MiB，包含延迟返回的差异载荷。该预算不包含小型协议/源码/JSON，也不是 GPU 显存指标。

`ScreenProbePayloadBudgetGuard-20261115-000048-115815` 将实际执行脚本的预算临时设为 1 字节，验证它在首轮写入前停止：二进制文件数和保存字节数均为零，原场景根节点与 Profiler 状态恢复，源码校验通过。该运行是预期触发保护的验证，不是 GI 或性能失败。

被替代的隔离、密集哈希和射线输入三个失败实验，在默认状态恢复后另存首次分歧及前帧已保存载荷，逐份 SHA 校验，并检查保护目录无原始数据依赖，再纳入归档白名单。保留所有哈希、报告、协议、脚本、指纹和完整首尾帧；归档后旧中间序列不能完整复算。详见 [GI 数据保留与清理](GiEvidenceRetention.md)。最新直接深度、inverse-VP 定位与播放模式计时可用性数据保持保护。

```powershell
python -B Tools~/Diagnostics/test_screen_probe_timing_qualification.py
python -B Tools~/Diagnostics/ScreenProbeTimingQualification.py E:/VividRP_GI_Evidence/ScreenProbeInverseVpInputs-20261114-235131-623461
```

资格报告使用排他创建；已存在时直接查看 `timing-qualification.json`，避免覆盖原判定。
