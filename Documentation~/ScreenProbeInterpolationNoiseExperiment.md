# Screen Probe 插值与最终噪声分离

2026-11-14；Cornell 纯顶部发光，1920×1080、16 px / 每探针 8 rays、固定原机位，anchor 预滤波开启、最大年龄 128、四轮 À-Trous。没有改变光强、曝光或材质。

## 冻结 SH 的 Resolve 重放

`ScreenProbeResolveReplayCapture.cs` 用真实 SH 的单帧与 256 帧平均场，重放生产 `ScreenProbeIntegrateCS`，分别开启和关闭插值抖动。原始 Resolve 在 `EditorGatherStageCapture` 的 stage 0 读取：`m_RawResolved` 在后续 À-Trous 中会作为 ping-pong 临时纹理使用，不能在帧末把它当作原始 Resolve。

有效重放：`ScreenProbeResolveReplay-20261114-111531-719223`。五个 ROI、64 帧的动态输入重放与真正的 Resolve 全部逐字节相同；原几何和源缓存一致。冻结平均 SH 后，天花板 Resolve 的时间方差仍为动态输入方差的约 **95.18%**；关闭抖动后冻结输入的输出逐帧相同。这是固定输入干预的方差比值，不是最终去噪图像的噪声归因百分比。

本机 Unreal 的 `LumenScreenProbeGather.usf` 使用 `BlueNoiseVec2` 生成插值偏移；`BlueNoise.ush` 的序列布局是 128×128×64。候选复用 Vivid 已有的 STBN Vec2 纹理，保持原偏移幅度、深度衰减、几何检查和所有追踪预算。

`ScreenProbeResolveReplay-20261114-112421-783829` 的冻结输入重放中，STBN 配合**诊断用 32 帧 box average** 的方差明显降低（天花板约 86%，其他平面约 90%）。这个 box average 不包含生产时域的拒绝、裁剪、响应及后续空间滤波，不能用它推广实际画面收益。

## STBN 真实全链路对照：候选已撤回

`ScreenProbeStbn-20261114-113230-540`：每组预热 1,024 帧后测量 256 帧，正常原生时域与四轮滤波全部执行。静止 A/A 的 **7,780** 份有效数据一致；2,780 个受检源码/夹具文件在捕帧前后不变。静止 A/B 的几何、raw、anchor、统计、SH 和重建输入逐帧一致。

| 区域 | 最终像素时间方差下降 | 线性均值变化 |
|---|---:|---:|
| 左墙 | +0.037% | −0.0018% |
| 右墙 | −0.138% | −0.0040% |
| 背墙 | −0.094% | +0.0028% |
| 地面 | −0.116% | −0.0017% |
| 天花板 | +1.373% | −0.0152% |

负的“下降”表示方差微增。天花板条件 block-bootstrap 95% 区间约为 [0.613%, 1.875%]；左墙区间跨零。这个收益不支持把 STBN 作为解决当前广泛低频噪声的默认改动。

移动组输入不合格：首个测量帧 1,024 的几何相同，但 raw 有 286 个不同 texel，anchor 有 679 个，SH 有 4,192 个。保留原始 `invalid-input-analysis.json`；不能把移动后的差值解释为 STBN 的质量变化。128 帧轨迹又与 64 帧 STBN 序列同步，不能以同姿态差值直接推广移动稳定性。没有进行后续 127 帧周期验收，也没有测 GPU 耗时。

生产候选已撤回；补丁在 `Tools~/Diagnostics/Patches/ScreenProbeStbnInterpolation.patch`。`ScreenProbeStbnCapture.cs` 和包含 STBN 的重放工具需要先应用该补丁并通过 Unity 编译才能重捕帧。补丁默认开关为 false，从未写入 Cornell Profile。回归分析保留 `--static-only` 模式，输出单独的 `static-qualified-analysis.json`，明确 `overallAcceptance=false`；默认完整分析仍拒绝未合格的移动输入。

重放前用 `PrepareScreenProbeResolveReplay.py` 创建全新的输入目录，并将 C# 工具的 `root` 指向它。工具拒绝覆盖已有编号帧文件。

```text
E:/VividRP_GI_Evidence/ScreenProbeStbn-20261114-113230-540/static-qualified-analysis.json
E:/VividRP_GI_Evidence/ScreenProbeStbn-20261114-113230-540/invalid-input-analysis.json
E:/VividRP_GI_Evidence/ScreenProbeStbn-20261114-113230-540/rejection.json
E:/VividRP_GI_Evidence/ScreenProbeStbn-20261114-113230-540/source-verification.json
```

统计在清理前生成并核对。按 [证据保留规则](GiEvidenceRetention.md) 归档该候选的中间帧，保留首尾帧、报告、源码快照和补丁，另释放 6,802,068,480 B（约 6.335 GiB）。原长序列已无法在旧目录完整复算，复算需重捕帧。

## anchor 方向性空间滤波的离线筛选

真实几何中，天花板 1,044 个平面探针有 882 个被现有 9×9 几何保护挡住，只有 162 个可用额外预滤波。`AnchorAnisotropicPrediction.py` 预测在同平面连续的 9×5 / 5×9 足迹内增加轴向半步扩散；也比较只扩大受保护预滤波的世界半径。

使用真实 GPU anchor、统计、SH 和独立的 16,384 rays/probe 同渲染器参考。CPU 基线与 GPU 校准后 SH 的平面 RMS 差约为均值的 0.04%，包含半精度存储和运算顺序差异。该预测不生成 GI，也不是最终像素或动态验收。

| 预测方案 | 天花板探针能量时间方差下降 |
|---|---:|
| 原世界半径，增加轴向保护 | 1.014% |
| 仅扩大预滤波世界半径 2 倍 | 0.158% |
| 2 倍半径与轴向保护 | 1.281% |

后者在左右墙的预测收益约 12%，但没有明显解决当前重点的天花板噪声；没有据此增加运行时纹理或 dispatch。预测文件位于 `AnchorAnisotropicPrediction-20261114-v1`、`AnchorWidePrediction-20261114-v1`、`AnchorWideAxisPrediction-20261114-v1`。后续应在真正的像素时域链路中冻结输入，确认残余噪声所在阶段后再实施下一项运行时改动。

另用新基线预测只跳过屏幕外的 guard tap，保留实际几何边缘检查：`AnchorViewportPrediction-20261114-v1`。它额外放行 270 个天花板探针，但全平面探针方差预测仅下降 0.468%，地面约 5.477%；没有把它实现为默认运行时修复。该新诊断未采集接触像素，报告中的接触区只表示固定 ROI 内的探针投影估计。

## 冻结输入经过真实像素时域与四轮滤波

`ScreenProbeFrozenChain-20261114-123903-882` 使用同一机位、源缓存及 16 px / 8 rays。四组分别为两次动态 SH 基线、冻结真实 256 帧平均 SH、冻结该 SH 并关闭插值抖动。每组等待正常 8 阶 / 64 samples / 32 batches 完成后，预热 512 帧再测量 256 帧。其余像素时域、历史拒绝、裁剪和四轮 À-Trous 全部走生产实现。

只增加 Editor 的 `EditorBindResolveProbe` 诊断回调，位于正常 probe 绑定之后、真实 dispatch 之前。回调使用当前相机上下文，只给目标 Cornell 相机绑定独立只读 Texture2D；默认回调为空且仅在已有 trace capture 激活时调用。正式渲染和 Player 没有冻结输入。没有改动 shader、GPU 结构、场景资产或默认滤波设置。

两次基线 **6,402** 份读回逐字节相同。四组每帧的生产者几何、raw、anchor、统计、SH 均相同；只有 Resolve 消费的 SH 输入不同。冻结纹理首尾 GPU 读回与原始上传字节完全相同，源缓存与几何也匹配冻结输入的原始夹具。2,780 个受检文件与 4 份外部输入在捕帧前后不变，实际 D3D12 Integrate kernel 已归档。最初脚本预检的原生纹理绑定类型错误已在正式运行前修正，正式四组无运行错误。

| 区域 | 动态 SH 最终时间方差 | 冻结 SH 最终时间方差 | 方差下降 | 冻结且无抖动 |
|---|---:|---:|---:|---|
| 左墙 | 0.00698784 | 0.000007649 | 99.891% | 逐帧字节相同 |
| 右墙 | 0.01209087 | 0.000006218 | 99.949% | 逐帧字节相同 |
| 背墙 | 0.00720045 | 0.000000876 | 99.988% | 逐帧字节相同 |
| 地面 | 0.05427309 | 0.000013878 | 99.974% | 逐帧字节相同 |
| 天花板 | 0.00034985 | 0.000001848 | 99.472% | 逐帧字节相同 |

数值是 scene-linear consumed E/pi 的 Y 时间方差；只统计同材质平面 ROI。ROI 在捕帧前核对有效像素，非事后挑选。天花板 Resolve 阶段只下降 18.89%，经过真实像素时域和空间滤波后才达到 99.47%；不能从原始 Resolve 的方差比值直接推断最终噪声。固定输入无抖动的极小约 1e−30 统计量来自 CPU 求均值的浮点舍入，实际 GPU 结果逐帧相同。

三个受检模式的所有有效 ROI 像素 age 都为 **32**、状态都是正常历史接受。因而本组结果不支持“过早丢弃像素历史”作为首要原因；更有价值的方向是 SH/anchor 输入的慢变化、有效独立样本数及累积精度。冻结平均场取自另一个时间窗口，背墙与地面最终均值分别变化约 +1.548% / +1.064%；该干预不是物理参考，也不是可推广的冻结修复。

```powershell
unity command script eval --project-path E:/VividRP_Reborn --file E:/VividRP_Reborn/Packages/VividRP/Tools~/Diagnostics/ScreenProbeFrozenChainCapture.cs --timeout 4500 --format json
python -B Tools~/Diagnostics/ScreenProbeFrozenChainExperiment.py E:/VividRP_GI_Evidence/ScreenProbeFrozenChain-20261114-123903-882 --linear E:/VividRP_GI_Evidence/TemporalSamplingCovered-20261113/linear
```

每次捕帧创建唯一输出目录。输入来自保留的真实 SH 文件，不覆盖旧数据。关键证据为该目录的 `analysis.json`、`qualification.json`、`source-verification.json` 和 `final-verification.json`。恢复检查确认新输入绑定回调与原有回调均为空，flags=0，frame/Atrous override=-1，正常 8 阶 / 32 批、bias=0.04、两场景未脏。未进行 GPU 计时，未在活动 Editor 中启动 Unity Test Framework。

本轮完成噪声来源分离并撤回低收益候选，没有宣称已取得新的运行时降噪收益。下一步优先验证 anchor 的累积精度与更有效的稳定样本收敛，同时保持移动时短窗口、新露出拒绝和真实光照变化响应。

后续实验应分离“128 帧 FP16 / 128 帧 FP32”和更长稳定窗口的效果，并在 GPU 源缓存稳定后单独建立 Gather 历史起点，避免准备期旧能量混入长窗口。需要同时检查独立高样本均值、完整轨迹、新露出以及升光/降光/关灯响应，不能只以静止方差下降推广。当前默认仍是已验证的 128 帧配置。

收尾磁盘检查为证据约 **119.149 GiB**、E 盘可用约 **116.252 GiB**，低于 125 GiB 软上限且高于 64 GiB 预留；本次完整分离数据已加入保护清单。另见 `post-experiment-storage.json` 和 `post-restoration-state.json`。未测量整帧托管分配。

## 长窗口实验的基线资格与有限读回

随后尝试 128 / 512 帧、FP16 / FP32 的因子实验。先等待源缓存 8 / 64 / 32 连续八帧就绪，再只使 Gather 的历史签名失效，保留已收敛的 Radiosity，并从独立采样帧 1733 开始。两次起点的几何、源、raw、anchor、统计与 SH 均逐字节一致，GPU 统计中的起点 age 只有 0 / 1。但预热 2,048 帧后的 A/A 有 6,146 / 6,410 份读回不同，实验按门槛自动停止，**没有运行 FP32 或 512 帧候选，也没有质量结论**。

为减少磁盘占用，改用 `ScreenProbeEpochControlCapture.cs`：CPU 记录历史签名，GPU 数据主要保存 SHA-256，只有首两帧、末帧和每类首次分歧写入完整 payload。源就绪后独立建立历史起点；Profile 克隆、相机开关、回调与帧覆盖均在 `finally` 恢复，不保存场景。分析器为 `ScreenProbeEpochControlExperiment.py`。

| 对照 | epoch 帧数 / arm | GPU 检查方式 | 配对检查点 | 差异 |
|---|---:|---|---:|---:|
| `134959-695` | 256 | 每帧 | 3,839 | 0 |
| `135607-453` | 2,056 | 前缀、2 的幂边界及末段 | 374 | 120 |
| `140653-569` | 640 | 前缀、第 240～520 帧及末帧 | 4,289 | 0 |
| `141146-608` | 640 | 前缀、2 的幂边界及末帧 | 179 | 0 |
| `142048-816`，源码文件恢复 | 640 | 前缀、2 的幂边界及末帧 | 179 | 0 |
| `143435-247`，实际程序集重载 | 64 | 前缀、2 的幂边界及末帧 | 119 | 0 |

表中六个目录均以 `E:/VividRP_GI_Evidence/ScreenProbeEpochControl-20261114-` 为前缀。前四次受检的 2,781 份源码 / 捕帧文件与两份外部输入均未变化；文件恢复后的 2,782 份文件及相同输入也未变化。对应实际捕帧脚本按原始 hash 归档到各目录。三组 640 帧控制的 A/A CPU 签名完全一致，没有检测到中途 Gather 拒绝、源重新准备或采样帧偏离。文件恢复后的全部 358 份 GPU 数据也与 `141146-608` 的同设置控制检查点逐字节相同。

最终反射检查发现 Editor 当时关闭了自动刷新，源码恢复后仍加载旧程序集；不能把文件 hash 当作运行版本证明。保持该用户设置不变，通过 `script recompile` 显式编译，状态返回 completed、无编译错误，随后验证原型字段已从实际程序集移除。重载后的 64 帧前缀对照在执行前强制检查该字段不存在：238 份 GPU 数据与 `142048-816` 的相同前缀逐字节一致，2,782 份源码 / 工具、两份比较输入和实际执行脚本在捕帧前后均不变。最终 callbacks=null、flags=0、frame/Atrous override=-1，场景未脏、正常 8 阶 / 32 批；详见 `reloaded-comparison.json`、`source-verification.json`、`post-restoration-state.json`。这仍是有限前缀的恢复验证，不替代长序列失效原因定位。

长对照首次**检测到**差异的检查点为 511，此前检查点 255 一致，故实际分歧起点只定位到 `(255, 511]`。不同项涉及 previous directional filtered、proposal、ray、raw、composite、directional temporal / filter、anchor、统计和 SH；几何、Radiosity、屏幕源辐亮度 / 几何 / motion 在该长对照的所有已采检查点中相同。长对照没有全帧 CPU 日志，不能排除两个检查点之间的临时重置。两次较短对照通过，也不能证明长对照无问题，或据此认定是 GPU 同步错误。原因仍未确定，后续应优先补齐长序列的全帧历史状态与首次分歧输入，而不是推广新累积窗口。

未验证的 Editor 512 帧 override、对应 shader 分支及状态检查已从生产代码撤回，保存为 `Tools~/Diagnostics/Patches/ScreenProbeAnchorPrecision.patch`；`ScreenProbeAnchorPrecisionCapture.cs` 仅是要求该补丁的实验工具。默认仍为当前夹具已验证的 **128 帧、FP16、16 px / 8 rays、原生 Gather、四轮 À-Trous**。没有声称新的运行时降噪收益、零分配或 GPU 耗时改善，没有在活动 Editor 中启动 Unity Test Framework。

失败因子实验的中间读回已按 [保留规则](GiEvidenceRetention.md) 清理，首尾帧、原因、编译二进制与源码指纹保留；完整旧序列不能再复算。后续有限读回对照每组只保留约数百 MiB，避免反复生成整段截图与原始纹理。

分析器的七项独立 Python 检查通过，覆盖缺失 GPU 检查点、两组同时变动的源、缺失 CPU 帧、两组共同发生历史重置、重复 CPU 帧与缺失源数据。它们只验证资格门槛，不替代真实 GPU 对照。运行方式为 `python -B Tools~/Diagnostics/test_screen_probe_epoch_control.py`。
