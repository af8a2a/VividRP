# Cornell 原生 Screen Probe：三轮 À-Trous 与接触边界

2026-11-14，Unity 7000.0.0a16 / D3D12。原生 Screen Probe Gather，独立逐帧采样，16 px 网格、每探针 8 条射线。没有引入 ReSTIR、增加射线数或调整曝光、反弹增益。

## 可比较的起点

此前三轮滤波试验在 QMC 冻结 proposal 下得到正向结果，但默认独立采样的部分批次改变了 proposal 和命中光照，未作为默认值依据：

- `Atrous3WhiteSampling-20261114-002334-757`：Hybrid 输入一致，SDF Only 不一致。
- `Atrous3WhiteSampling-20261114-004046-641`：两轮 A/A 的 5,890 份文件一致，但后续两轮 / 三轮 A/B 输入不一致。

使用 `AtrousHistoryIsolation.cs` 记录资源身份。48 帧诊断未发现 Pass、相机状态或 GPU Scene 重建；受检临时纹理与历史纹理没有物理别名。该诊断同步查询 native texture pointer，会改变执行时序，不将它单独当作无干扰生产执行的证明。

随后预热源缓存 512 帧，按 2 / 2 / 3 / 2 轮各捕获冷启动后的连续 64 帧。1,158 份受检输入文件逐字节一致，包括 proposal、命中光照、方向滤波、Resolve、像素 Temporal 和源 atlas；数据有限、owner 不变。检查范围是脚本定义的局部区域，不是全屏每个中间值。

```text
E:/VividRP_GI_Evidence/AtrousHistoryIsolation-20261114-011615-246/identity.json
E:/VividRP_GI_Evidence/AtrousHistoryIsolation-20261114-012159-171/input-equivalence.json
```

正式比较改为每组先在相同追踪模式下预热 64 帧，再复位 Gather 历史。保留两次 SDF Only 两轮滤波的 256 帧 A/A，并要求 5,890 份采样文件逐字节一致。Hybrid、SDF Only 静态组和固定轨迹都继续要求受检滤波前输入一致，没有放宽原门槛。初始状态变化的具体生产原因尚未定位；不能据此声称修复了资源别名或历史所有权问题。

## 未保留的直接三轮模式

完整对照：`E:/VividRP_GI_Evidence/Atrous3WhiteSampling-20261114-012546-010/`。

每组预热 512 帧、测量连续 256 帧；相同机位与固定往返轨迹。固定 8 阶、每批 64 样本、32 批 Surface Cache；各组源 atlas 首尾及配对一致。2,275 个受检运行时、shader 和 Cornell 夹具文件在测量前后校验一致。输入一致性检查覆盖静态平面、三处接触 / 墙角区域以及 320×256 运动区域。

| 路径 | 天花板时间方差下降 | 地面 | 右墙 |
|---|---:|---:|---:|
| Hybrid | 9.04% | 12.60% | 13.16% |
| SDF Only | 11.94% | 10.68% | 12.06% |

六个区域的 block-32 条件 95% 方差收益区间下界均大于零。主区域线性均值变化最大 0.123%。固定轨迹同姿态差值 RMS 从 0.656511 降至 0.630554（−3.95%），block-8 条件区间为 [−4.80%, −3.27%]。28,104 个明确新露出样本均未继承 age>1 的旧像素历史，Temporal 相对当前 Resolve 的 P99 / max 为零。这是有限序列的重投影诊断，不是完整动态场景误差为零。

额外检查发现短盒子底部的暗地面均值增加 2.82% / 2.89%，局部 RGB 最大增加约 3.73%，边缘均值差的绝对 P99 约 0.0543。覆盖率不变，接触 / 墙角输入也一致，但不能只用主平面平均能量来忽略这项局部变化。两轮滤波的均值不是物理真值；本轮将这处接触区变化作为需要保护的细尺度信号，因此没有直接采用原三轮模式。

## 最粗尺度的几何边界保护

现有 5×5 核使用步长 1、2、4，两轮与三轮的最大支撑宽度分别为 13 px、29 px。最粗一轮如果只平均剩余的同平面 tap，可能在接触位置改变暗部轮廓。

`SdfDiffuseAtrous.hlsl` 复用已经加载的 tap 深度平面与法线 / 材质类别判定：最粗 footprint 跨越屏幕内的几何边界时保留前两轮结果；连续表面继续使用第三轮。没有增加 tap、纹理、追踪射线或历史反馈。前两轮的行为保持原有表达式。

仅改 include 的候选批次 `Atrous3WhiteSampling-20261114-014521-306` 的接触区结果未变化，已由 owner cancel 停止，未计入有效质量验证。当前 Unity 会话中，强制导入 include 和 compute 后，`ShaderUtil.CompileComputeShaderVariant` 导出的最终 kernel 仍不含该边界分支；不能把源码校验和等同于实际 GPU 代码已经更新。

最终 kernel 入口显式声明 `VIVID_SDF_ATROUS_GEOMETRY_GUARD` 后，实际 DXIL 从 8,204 B / SHA-256 `12d5cd0b…` 变为 8,308 B / `31fbe530…`。反汇编确认步长至少 4 的判断和最终 RGB 条件选择已存在。保留两份字节码与反汇编，没有清理或重建 Unity 缓存；不据此推断其他 Unity 会话或后端均存在同一问题。

零阶、连续 64 帧的算子隔离中，449 份两轮输出及上游输入文件逐字节不变；第三轮输入也与两轮最终结果一致。短盒底部 615 个地面像素的 covered 线性均值变化从 +4.302% 降至 +0.148%，RGB 最大差从 0.15381 降至 0.02100；86.88% 的地面像素样本保留细尺度 RGB。此隔离不代表完整 8 阶 GI 的质量已通过。

```text
E:/VividRP_GI_Evidence/Atrous3WhiteSampling-20261114-021041-162/shader-import-verification/verification.json
E:/VividRP_GI_Evidence/AtrousHistoryIsolation-20261114-020039-361/identity.json
E:/VividRP_GI_Evidence/AtrousHistoryIsolation-20261114-020732-959/identity.json
```

捕帧工具现在先归档实际 normal / final kernel 字节码，并拒绝与已检查最终 kernel 指纹不符的实验。修改 kernel 或升级编译环境后须重新检查产物，不能直接跳过此项。

## 已保留的受保护三轮模式

正确字节码的完整批次 `Atrous3WhiteSampling-20261114-021041-162` 中，SDF Only 和固定轨迹的输入检查通过，但 Hybrid 静态输入仍不一致，即使源 atlas、VP 和探针几何相同。该 Hybrid 配对被排除，不依据其均值或方差作质量判断。随后只重测 Hybrid：每组在相机所属的 CPU Gather state 和 GPU CameraHistory 上执行清理，再预热 512 帧；保留其他相机状态。Hybrid A/A 的 5,890 份文件一致，A/B 的 4,096 份受检滤波前输入一致。

`MergeAtrousComparison.py` 只引用两次**已完整完成**捕帧中的有效配对，不复制或重写原始 payload。它验证两份运行的参数、实际 kernel 字节码指纹、2,275 个源码 / 夹具文件快照及 A/A 结果相同；合成清单记录每项数据来源、排除配对和不同的复位策略。组合后重新执行全部输入、噪声、能量、边缘和运动检查，未放宽门槛。完整清理使该 Hybrid 复测可重复，但尚未定位旧复位方式的具体生产原因，不将诊断隔离操作描述成运行时修复。

```text
E:/VividRP_GI_Evidence/Atrous3WhiteSampling-20261114-023458-746/capture/capture.json
E:/VividRP_GI_Evidence/AtrousGuardedCombined-20261114-024357-560568/capture/capture.json
E:/VividRP_GI_Evidence/AtrousGuardedCombined-20261114-024357-560568/analysis.json
E:/VividRP_GI_Evidence/AtrousGuardedCombined-20261114-024357-560568/denoise-edges.json
```

| 路径 | 天花板时间方差下降 | 地面 | 右墙 |
|---|---:|---:|---:|
| Hybrid | 9.98% | 8.89% | 12.95% |
| SDF Only | 11.94% | 8.36% | 12.06% |

六个区域的条件 95% 收益区间下界均大于零；主区域平均线性 Y / RGB 变化均低于 0.10%。短盒底部暗地面的 Y 变化为 +0.063% / +0.070%，三处接触 / 墙角各物体的 Y 变化最大 0.138%、RGB 最大 0.165%；均通过 ±1% 检查。受检几何边缘带的像素均值差 P99 为 0，覆盖率逐字节不变。保护区域保留两轮噪声水平，不将其描述为边缘噪声也同步下降。

固定轨迹同姿态差值 RMS **0.656511 → 0.647549（−1.36%）**，block-8 条件区间 **[−1.52%, −1.20%]**；绝对 P99 **2.22659 → 2.21582**。两组 28,104 个明确新露出样本均未继承 age>1 或 accepted 旧像素历史，Temporal 相对当前 Resolve 的 P99 / max 为零。这个结果不替代逐运动帧的物理真值或所有动态物体验证。

已通过 Unity API 将 **Emissive Ceiling Profile** 的 `Atrous2` 改为 `Atrous3`，并同步场景构建器。运行中的 Cornell 相机已确认实际使用原生路径三轮滤波；场景保持不脏。更新前的 Profile 原始备份和 `profile-promotion.json` 保存在组合证据目录。其他场景的配置及全局默认未改动。

在本配置开启像素时域的前提下，第三轮复用现有 Raw Resolve 颜色暂存，新增一张全分辨率 R16F 方差暂存，1920×1080 的描述符预算约 **3.96 MiB**，并多一次全分辨率 dispatch。边界保护复用原 tap 的 guide，不另增资源；这些是尺寸及调用数，不是驱动显存或 GPU 耗时实测。

实际 DX12 捕帧、kernel 编译及有效输入检查通过。Editor 构建器改动编译通过；重载后再次验证 Cornell 实际为原生三轮、16 px / 8 rays、8 阶 / 32 批、场景不脏、诊断回调为空。无 shader 错误，保留重要性采样 shader 的两条已有静态警告（可能未初始化值及循环变量遮蔽），详见 `post-promotion-verification.json`。恢复检查另确认 flags=0、frame override=-1、单面模式、bias=0.04。Editor 正在使用，未运行 Unity Test Framework；未测 GPU 耗时。

复现入口：`TemporalSamplingCapture.cs` 的 `atrous3-white-cold` 运行完整矩阵，`atrous3-white-fresh-hybrid` 仅运行完整相机复位的 Hybrid A/A 与 A/B。运行前后保存相同 tracked `Runtime/`、`Shaders/` 和发光 Cornell 夹具文件的 SHA-256 快照；组合器要求各源目录的 `source-before-capture.json`、`source-verification.json` 及 A/A 清单齐全。先验证有效配对，再执行：

```powershell
python -B Tools~/Diagnostics/MergeAtrousComparison.py BASE_EVIDENCE HYBRID_EVIDENCE
python -B Tools~/Diagnostics/TemporalSamplingExperiment.py COMBINED_EVIDENCE/capture --candidate atrous3 --linear LINEAR_REFERENCE
python -B Tools~/Diagnostics/TemporalSamplingExperiment.py COMBINED_EVIDENCE/capture --edges-only --linear LINEAR_REFERENCE
```

`LINEAR_REFERENCE` 是同机位线性导出的目录，含 `vivid-ev4/object_id.exr`。`PromoteCornellAtrous3.cs` 仅在编译指纹、完整输入、噪声 / 能量、接触区、运动等门槛通过后保存 Profile，并备份原文件、观察目标相机是否实际启用。

## 受保护四轮模式（2026-11-14）

三轮模式之后增加步长 8，累计支撑宽度从 29 px 扩至 61 px。四轮的颜色暂存和方差暂存按奇偶迭代交替使用，避免第三次中间写入读写同一纹理。步长至少 4 的所有迭代都保留几何边界保护；中间轮拒绝粗尺度 footprint 时，同时保留输入颜色和输入方差，避免下一轮把未平滑的信号当作已降低方差。

新增 `Atrous4 = 4`，保留原枚举值与其他场景配置。Cornell 的 Profile 及场景构建器采用四轮；全局设置类的初始默认仍保留原值。正常运行不启用 Editor 实验回调或迭代 override。

完整对照证据：

```text
E:/VividRP_GI_Evidence/Atrous4Sampling-20261114-052735-802/
```

原生 Screen Probe Gather，1920×1080、16 px / 8 rays，同一机位与固定往返轨迹。固定 8 阶 Radiosity，每批 64 样本、32 批；独立采样、diagnostics flags=0。每组独立清理目标相机的 Gather 与 GPU 历史，再预热 512 帧并连续采集 256 帧。A/A 的 **7,426 份**受检 payload 逐字节一致。捕帧前后 **2,837 个**受检源码、诊断及夹具文件校验一致；各组源 atlas 首尾及组间一致。

静态平面、三处接触 / 墙角区域共 **11,264 份**受检上游和前三轮结果完全一致。运动区域的 Resolve、Temporal、像素历史与前三轮结果也完全一致，最终覆盖率不变。实际纹理身份检查覆盖首尾帧的 **14 组、48 次 dispatch**：颜色及方差输入输出不同，链路连续，第三次中间写入复用第一张自有暂存。该检查不冒充驱动物理堆别名分析。

实际 normal kernel 为 8,600 B / SHA-256 `cd991be0…`；final kernel 仍为 8,308 B / `31fbe530…`。原始字节码与 DXIL 已归档，normal 的粗尺度边界保护在第四轮之前生效。

| 路径 | 天花板时间方差下降，条件 95% 区间 | 地面 | 右墙 |
|---|---:|---:|---:|
| Hybrid | 23.08% [19.03, 27.40] | 16.01% [11.99, 19.41] | 29.81% [28.52, 31.72] |
| SDF Only | 22.42% [19.46, 28.64] | 15.64% [13.24, 18.37] | 30.92% [28.30, 34.47] |

主区域平均线性 Y 变化最大 **0.210%**、RGB 最大 **0.224%**。三处接触 / 墙角按物体统计的 Y 变化最大 **0.274%**、RGB 最大 **0.294%**；短盒底部暗地面与三轮完全一致。受检几何边缘带像素均值差的绝对 P99 为 0，覆盖率不变。所有组通过本次 ±1% 能量门槛；三轮结果不是物理真值，不能把这些差值解释为物理能量误差。

固定轨迹同姿态差值 RMS **0.669342 → 0.652785（−2.47%）**，block-8 条件区间 **[−2.74%, −2.19%]**；绝对 P99 **2.43768 → 2.41534**。两组各 **28,104** 个明确新露出样本均未继承 age>1 或 accepted 旧像素历史，Temporal 相对当前 Resolve 的 P99 / max 为零。这是既定轨迹和 ROI 的诊断，不表示全场景动态误差为零。

通过 `PromoteCornellAtrous4.cs` 核对完整捕帧、实际 kernel、源冻结、A/A、六个静态区域、接触区、运动、覆盖率及纹理链路后，使用 Unity API 保存 Cornell Profile。原 Profile 备份与 `profile-promotion.json` 位于证据目录；实际相机确认使用四轮，场景不脏。

三轮到四轮继续复用两张颜色与两张方差暂存，描述符预算不增加，多一次全分辨率 dispatch。没有测量驱动实际显存或 GPU 耗时，不能据此声称新增开销可忽略。本轮质量验收限于原生 Screen Probe Gather，ReSTIR 四轮模式未单独验收。Editor 正在使用，未启动 Unity Test Framework；实际 DX12 捕帧和 C# 编译通过。

复现：`TemporalSamplingCapture.cs` 的 `atrous4` 模式，随后执行完整分析与 `--edges-only`，两次命令与前节一致，但候选改为 `--candidate atrous4`。`source-before-capture.json` 必须在捕帧前保存，`source-verification.json` 在修改或推广候选前保存。分析器另外检查完整静态与运动覆盖率，并记录 `first_three_wavelet_exact` 和实际 `atrous_bindings`。

## 质量目标的剩余范围

`atrous4-overview.png` 的同尺度整帧与天花板检查仍可见墙面低频云斑，四轮空间平滑尚未达到稳定无噪声目标。后续应区分固定空间误差与时间方差，改善输入估计的收敛，并在动态光照、Sponza、真实 GPU 计时及更强参考下验证；本轮不把方差收益等同于物理误差的同等下降或 Lumen 质量已经对齐。
