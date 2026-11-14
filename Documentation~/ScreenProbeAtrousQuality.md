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

## 质量目标的剩余范围

整帧截图仍有可见低频斑块，三轮空间平滑本身不足以证明画面已稳定无噪声。后续仍需改善输入估计的收敛，并在动态光照、Sponza、真实 GPU 计时及更强参考下验证；本轮不把方差收益等同于物理误差的同等下降或 Lumen 质量已经对齐。
