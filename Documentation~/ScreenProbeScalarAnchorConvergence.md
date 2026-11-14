# Cornell scalar anchor 的收敛窗口

2026-11-14，Unity 7000.0.0a16 / D3D12。原生 Screen Probe Gather，1920×1080、16 px 网格、每探针 8 条射线，Adaptive 5×5、空间 anchor、受保护的四轮 À-Trous。纯自发光顶灯，太阳和天空为零，机位 `(0, 1.5, -3.8)`、VFOV 42°。没有修改曝光、材质、BRDF 或反弹增益。

## 先区分源缓存误差与 Gather 噪声

静态探针快照中，左、右、后墙和地面的有效 scalar 历史已经达到 32 帧，静止接收点没有持续拒绝历史。天花板约 97.95% 的有效点处于静态状态。后墙分析排除开放盒子外侧的端盖，不能将这些无效点误判成室内历史失效。

随后只改变 Radiosity 累积批数：32 / 128 / 512 批，每批 64 样本、8 阶反弹。512 批使用独立 seed；Gather 使用相同的均匀余弦序列，关闭引导。每组等待源缓存完成，预热 512 帧，测量 256 帧；A/A 的 2,308 份受检 payload 完全相同。所有测量期间源 atlas 首尾不变，2,836 个受检源码及夹具文件校验一致。

相对 512 批参考，32 批源造成的标量空间形状 RMS 约为 0.00685～0.01575；同期 scalar anchor 的时间 RMS 约为 0.1465～1.2125。更高源样本确实减少固定误差，但其幅度不足以解释当前 Gather 噪声，因此保留生产的 **8 阶 / 64 样本 / 32 批**。这里的形状诊断分离整体比例，同时报告真实未拟合均值；没有把拟合比例写回图像或生产代码。高批数参考仍是同一渲染器，不是物理真值。

```text
E:/VividRP_GI_Evidence/ProbePlaneSnapshot-20261114-060209-436/
E:/VividRP_GI_Evidence/EmissiveRadiosityConvergence-20261114-061338-532/analysis.json
E:/VividRP_GI_Evidence/EmissiveRadiosityConvergence-20261114-061338-532/whole-plane-temporal.json
```

## 实现与适用范围

新增 `screenProbeAnchorMaxAge`，可配置 32～128。设置类初始值仍为 32；只将已经验证的 **Emissive Ceiling Profile** 和对应场景构建器设置为 128。其他场景、Sponza 和 ReSTIR 配置没有推广。

`AccumulateDirectionalProbeAnchor` 只将原来固定的静态上限 32 替换成该参数。保留原有世界位移衰减：位移增大时趋向 8 帧；保留全分辨率前景 / 深度平面 / 法线可见性拒绝。没有全分辨率几何历史时仍限制为 8 帧。光照变化沿用原有线性重标定或失效逻辑，没有跨几何边界强行保留历史。

改变窗口会使对应相机历史失效，避免混用两种配置。只增加缓存的 shader property ID、标量参数和 CPU 历史字段，复用原统计 / 颜色纹理；没有增加追踪射线、纹理或 dispatch。没有改动 GPU 共享结构布局。没有测量 GPU 耗时或整帧分配，不能将这些静态观察当作实测零开销。

## 同输入的 32 / 128 帧对照

每组独立清理目标相机 Gather 和 GPU CameraHistory，源缓存完成后预热 512 帧，随后测量 256 帧。使用默认独立采样及方向引导，diagnostics=0。A/A 的 **4,868 份** payload 逐字节一致；32 / 128 的全部受检原始命中照明、源 atlas、VP 与探针几何相同。捕帧前后 **2,836 个**源码 / 夹具文件不变。

| 平面 | 最终像素时间方差下降 | 条件 95% 区间 | 线性 Y 均值变化 |
|---|---:|---:|---:|
| 左墙 | 75.23% | [70.90, 79.79]% | +0.048% |
| 右墙 | 73.74% | [67.34, 79.85]% | −0.225% |
| 后墙 | 83.40% | [80.05, 86.48]% | +0.899% |
| 地面 | 83.70% | [76.10, 89.25]% | +0.436% |
| 天花板 | 64.11% | [57.75, 70.80]% | +0.458% |

各区域平均 RGB 变化最大 0.945%。GPU 统计中静态 anchor 的中位年龄实际达到 128。区间来自有限序列的 block bootstrap，不表示跨场景的普遍收益。

```text
E:/VividRP_GI_Evidence/ScalarAnchorAge-20261114-063528-201/analysis.json
E:/VividRP_GI_Evidence/ScalarAnchorAge-20261114-063528-201/scalar-anchor-overview.png
```

对照图使用同一个 `sRGB(RGB / 20)` 显示变换和相同色标。时间噪声明显下降，但墙面、天花板仍有低频空间斑块，尚未达到稳定无噪声目标。

## 独立参考与固定轨迹

五个固定姿态分别采集 1,024 帧均匀余弦、每探针 8 条射线的 current-scalar Resolve，使用独立帧序列并关闭 scalar 历史。实际 GPU 统计确认 age=1；参考与候选的源 atlas、VP 和接收点几何一致。这相当于每探针累计 8,192 条射线，但仍是同渲染器、像素降噪前的蒙特卡洛参考，不能称为独立物理真值或 Unreal 对齐结果。

| 平面 | 32 帧最终均值相对参考 | 128 帧最终均值相对参考 |
|---|---:|---:|
| 左墙 | −0.115% | −0.067% |
| 右墙 | +0.685% | +0.458% |
| 后墙 | −1.364% | −0.478% |
| 地面 | −0.756% | −0.323% |
| 天花板 | −0.633% | −0.177% |

五个区域都更接近参考，128 帧的平均 RGB 误差最大 0.616%。没有拟合能量增益。

固定往返轨迹采用 128 帧一周期，平移 `(0.28 sin(t), 0, 0.06 sin(t))`、yaw `−7 sin(t)`，测量两个周期。同姿态差值 RMS **0.638865 → 0.638003（−0.135%）**，条件 95% 区间 **[−0.527%, +0.165%]**，未证明运动一致性有显著改善；绝对 P99 **2.18794 → 2.21292（+1.14%）**。

两组各有 **28,104 个**明确新露出像素，均未接受旧像素历史，Temporal 与当前 Resolve 的差值 P99 / max 为零；另有 **73 个**新露出探针，均拒绝旧 scalar 历史，age 不超过 1。四个运动姿态、当前及前三帧新露出区域的重投影并集共 1,459 像素，相对姿态参考的 RMSE **1.535804 → 1.536385（+0.038%）**；P99 **5.398677**、max **6.533621** 相同。只考虑参考分块抽样不确定性的 RMSE 变化区间为 [−0.073%, +0.149%]，不是动态误差普遍不变的证明。各姿态完整 ROI 的误差有增有减，原始报告保留这些差异。

原参考运行在 pose104 首个测量帧发现源缓存改变，明确失败，不能整体算作通过。其已完成且当时无错误的 pose0/24/48/80 前缀，与独立完成的 pose104 补测组合；组合器核对 **2,835 个生产 / 夹具文件**相同，记录原失败、被排除尝试及 payload 来源，不改写原文件。补测要求连续八帧源就绪，预热中断则重新预热；测量期间失效仍直接报错。生产源缓存偶发失效的原因尚未定位，不能将诊断预热改进描述成运行时修复。

```text
E:/VividRP_GI_Evidence/ScalarAnchorPoseReference-20261114-064947-630/capture.json
E:/VividRP_GI_Evidence/ScalarAnchorPoseReference-20261114-071412-091/capture.json
E:/VividRP_GI_Evidence/ScalarAnchorPoseReferenceCombined-20261114-072002-644163/capture.json
E:/VividRP_GI_Evidence/ScalarAnchorPoseReferenceCombined-20261114-072002-644163/reference-analysis.json
```

## 升光、降光与关灯

通过临时 Meshlet 材质代理，将顶部发光从线性 200 改为 800、50、0；不修改源材质或保存场景。分别对 32 / 128 帧运行 constant / up / down / off 八组。源缓存就绪后预热 512 帧，测量 384 帧，在第 64 帧改变灯光；临时代理、Profile、相机和诊断钩子均在 finally 恢复。

升光 / 降光的实际发光值与请求一致，变化前 64 帧的 raw 输入与各自恒光对照完全相同。末帧 Radiosity atlas 等于恒光缓存乘对应倍率，归一化 RMS 差低于 **0.000001%**。八组捕帧完整、无错误；2,836 个受检源码 / 夹具文件测量前后相同。

128 帧方案在五个受检平面的最终照明首帧均值误差绝对值均低于 **0.02%**；升光前 32 帧的地面均值绝对误差为 0.052%，32 帧基线为 0.208%。关灯后，五区的 Resolve、像素 Temporal、最终输出从第一帧起均为零，没有旧自发光残留。

末段 64 帧仍存在 Gather / 重建与恒光倍率目标的有限序列差异：升光地面最终均值为 **−1.828%**，基线为 **−2.207%**；降光地面为 **+0.467%**，基线为 **+2.092%**。128 帧降光后墙仍为 +1.003%。源 atlas 的线性比例检查通过，这些差异并非该 atlas 的能量比例漂移；尚未进一步区分采样、时域状态和重建非线性，不把它们直接归因于随机误差，也不声称已消除最终光照偏差。当前推广限于静态收敛及本次统一发光倍率响应。

```text
E:/VividRP_GI_Evidence/ScalarAnchorRelight-20261114-073213-475/capture.json
E:/VividRP_GI_Evidence/ScalarAnchorRelight-20261114-073213-475/analysis.json
E:/VividRP_GI_Evidence/ScalarAnchorRelight-20261114-073213-475/source-verification.json
```

## 推广与复现

`PromoteCornellAnchor128.cs` 核对完整捕帧、A/A、原始输入、静态方差 / RGB 均值、独立参考、露出区历史拒绝、统一发光响应及源比例，并检查待推广 Runtime / Shader 文件与实测快照一致。使用 Unity API 保存 Cornell 专用 Profile，原始备份与 `profile-promotion.json` 位于 ScalarAnchorAge 目录。它不把上述有限运动和末段误差差异隐藏成“全动态质量通过”。

捕帧工具均为实时 Editor 诊断，不是 Unity Test Framework 测试。Editor 使用期间未启动 UTF；实际 D3D12 捕帧和对应分析通过。`VerifyCornellAnchor128.cs` 在正常源缓存完成后再渲染 512 帧，确认实际 GPU 最大年龄为 128，6,059 个探针年龄至少为 96；相机、16 px / 8 rays、四轮滤波、8 阶 / 32 批、seed=0、bias=0.04 均与保存配置一致。C# 编译通过，51 条 shader 消息与先前四轮基线完全相同，没有错误或新增警告。诊断回调为空、override 均为 −1、flags=0、强制双面关闭，所有已加载场景与共享 Profile 均未脏。结果保存在 `ScalarAnchorAge-20261114-063528-201/post-promotion-verification.json`。

新配置还重新通过 [四路单面发光回归](SdfEmissionSidedness.md)：52,411 条背面黑命中保留有效覆盖，正面约 200 的发光不变，地面最终均值变化最大 −0.097%。Radiosity / Far Probe / 远场重连的正反面 GPU 矩阵同时通过。默认偏移的零阶天花板最终重建仍约有 0.000122 的残留，原始背面命中照明为零，不声称最终插值结果处处为零。

主要入口：

```powershell
# 每轮捕帧前先生成独立快照，捕帧完成后、修改生产代码或资产前再核对。
python -B Tools~/Diagnostics/VerifyGiSourceSnapshot.py SNAPSHOT --create --include Tools~/Diagnostics/ScalarAnchorAgeCapture.cs
unity command script eval --project-path E:/VividRP_Reborn --file E:/VividRP_Reborn/Packages/VividRP/Tools~/Diagnostics/ScalarAnchorAgeCapture.cs --timeout 4500 --format json
python -B Tools~/Diagnostics/VerifyGiSourceSnapshot.py SNAPSHOT CAPTURE
python -B Tools~/Diagnostics/ScalarAnchorAgeExperiment.py CAPTURE --linear LINEAR_REFERENCE
python -B Tools~/Diagnostics/ScalarAnchorAgeOverview.py CAPTURE
```

源误差、独立姿态参考与光照变化分别用 `EmissiveRadiosityConvergenceCapture.cs`、`ScalarAnchorPoseReferenceCapture.cs`、`ScalarAnchorRelightCapture.cs`，随后运行各自的 Python 分析器。参考合并仅用于已显式记录的失败后补测，不可绕过有效性检查。`LINEAR_REFERENCE` 需要匹配夹具和机位的 `vivid-ev4/object_id.exr`。证据目录在当前工作区写入权限之外时须使用获授权的执行方式，写入失败的分析不计作完整保存结果。

后续仍需处理静态低频斑块、刚露出与连续移动时的八射线噪声，定位源缓存偶发失效，补齐局部光照 / 几何变化、Sponza、GPU 计时和与 Unreal 的独立质量对照。
