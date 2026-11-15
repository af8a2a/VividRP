# 稳定辐亮度边界的 anchor 与像素重建

在上一轮 ThinShell 黑覆盖修复上，保留由稳定时域 anchor 引导的对称空间权重，以及成熟辐亮度边界处的插值范围控制。Cornell 顶部光片上方 28 个实际探针中心的最终参考 RMS 误差从约 0.641 降至 0.118–0.119，时域方差下降约 81%–88%。**这是严格匹配输入的局部静态收益；移动质量、全场景稳定无噪声和新增 GPU 开销尚未完成验收。**

证据：`E:/VividRP_GI_Evidence/ScreenProbeBlackAnchorStages-20261115-1300`。D3D12 / RTX 5070 Ti / Unity 7000.0.0a16，原生 Screen Probe Gather，16 px / 每探针 8 条射线，age512 / FP32 anchor，4-pass À-trous，bias=0.04。冻结 Radiosity 为 8 阶 / 64 样本 / 32 批，源 SHA-256 仍为 `40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8`。没有改变射线预算、反弹增益、历史拒绝、已保存 Profile 或场景。

## 同帧阶段证据

`production`、`footprint` 各两次静态重复，每次预热 1,024 帧、测量 128 帧。捕获原始结果、时域 anchor、空间半步、SH、Resolve、像素时域与最终 GI；固定源参考复用保护的 `ScreenProbeResidualMap-20261115-104052-278383/inside-shell-reference`，每探针 16,384 条射线。

`footprint/stage-comparison.json` 通过逐帧实际深度、GBuffer0/1、自适应布点、逆 VP、派生探针几何和 ROI 像素几何检查。28 个参考点两组各 256 帧均精确匹配参考几何；没有位置容差、丢帧或增益拟合。

| 阶段平均消耗 Y，第一次重复 | 原路径 | 新路径 | 当前帧参考 |
|---|---:|---:|---:|
| 时域 anchor | 0.25293 | 0.25211 | 0.25265 |
| 空间半步 | 0.42938 | 0.25211 | 0.25265 |
| SH 在接收法线处 | 0.60560 | 0.25202 | 0.25265 |
| Resolve | 0.85521 | 0.25253 | 0.25265 |
| 像素时域 | 0.85478 | 0.25213 | 0.25265 |
| 最终 À-trous 输出 | 0.87722 | 0.36758 | 0.25265 |

偏差先出现在平面内的 scalar 空间混合，随后被抖动插值放大；时域 anchor 本身已经接近参考。第二次重复得到相同结论。阶段图为 `anchor-stage-summary.png`；`gi-static-before-after.png` 使用同一显示变换展示单个末帧的间接光 × 接收反射率，不是完整 SceneColor 或时域平均图。

## 实现

`SdfScreenProbeFiltering.compute` 的两次空间重建都用未做空间滤波的时域 anchor 作为引导。对称 RGB 相对差决定连接权重；有效年龄在 8–64 间逐渐建立信任。固定 5×5 归一化和成对的正负能量通量保持原样，覆盖率不变。新光照没有额外历史缓冲。拒绝的连接将权重留在中心，稳定的阴影与颜色边界不会因几何共面被抹平。

单独启用空间引导的 `guided` 候选没有保留：虽然半步偏差下降，最终像素 RMS 却由约 0.0049 升至 0.007。它另有 8 个探针帧不匹配参考位置，严格参考检查失败。可选的 `--matched-reference-subset` 只对各自精确匹配的 124 帧提供诊断，仍输出 `qualified=false`；默认检查保留原断言。

联合候选在 `ScreenProbeIntegrateCS` 中，只在四角几何连续、没有自适应几何断裂、覆盖率充足且历史已经成熟时，根据时域 RGB 反差连续缩小查询抖动与一像素扩张。新露出、低覆盖、几何边缘及未成熟历史保留原重建方式。单维探针网格的分母有下限，避免收缩扩张后出现除零。

本机 UE 5.7.4 的 `E:/UnrealEngine/Engine/Shaders/Private/Lumen/LumenScreenProbeGather.usf:142` 使用抖动坐标、`BilinearExpand=1` 和深度平面双边权重。本实现沿用该重建结构，为 VividRP 固定探针中心与较长 scalar 历史增加成熟辐亮度边界控制，不能称为逐行移植或已达到 Unreal 最终画面质量。

`SdfDiffuseGatherPass.cs` 复用现有 PreparedAnchor / AnchorStatistics 资源和属性 ID，向相应 kernel 绑定只读输入；不新增历史纹理。稳定帧新增绑定与 `BindProbeFrame` 的实际调用已共同测量：预热 128 次后再调用 128 次，当前线程托管分配为 0。

## 质量、能量与限制

28 点最终方差下降为 **80.66% / 87.88%**，参考 RMS 误差由 **0.6411 / 0.6415** 降至 **0.1182 / 0.1190**。两次重复中，各平面 SH 法线处的平均消耗 Y 变化在约 **0.11%** 内；空间半步的平面均值与自身时域输入在浮点误差范围内一致。参考共享冻结 Surface Cache，不能评估缓存来源误差，也不是绝对 Unreal 真值。

最终像素仍比参考高约 **0.115**：候选 Resolve 和像素时域约为 0.252，而 À-trous 后约为 0.368。画面仍有低频斑块，不能宣称无噪声。下一步应分别记录各轮 wavelet 与方差输入，检查普通探针路径的噪声估计和亮度权重，不将真实照明边界当作待平滑的方差。

移动验收使用 `quality-head` / `quality-footprint`，四组同机位 / 固定轨迹，每组预热 1,024、测量 128 帧。基线包含已提交的 ThinShell 黑覆盖修复；两者使用相同程序集，原 shader 算法通过精确备份恢复，额外未使用的资源绑定不改变原算法。

`quality-head-comparison.json` 中，两次移动各 3,894 个新露出像素均没有旧历史年龄或接受状态，时域减当前 Resolve 的 P99 / 最大差均为 0。但全组仍有 10 帧派生几何差异和 5 条移动 GBuffer 哈希差异；最大有效位置差为 `7.15e-7 m`，法线和有效性相同，**严格整组检查未通过**。最终减 Resolve 的移动 P99，原路径约 2.508 / 2.416，新路径约 2.145 / 2.547；该量包含空间滤波且输入未完全匹配，不能据此宣称移动质量不回退。更早对照上轮黑覆盖修复前基线的报告仅作累积诊断，不作为本轮增量验收。

## 验证与复现

实际 C# 程序集编译、5 个 filter 及 1 个原生积分 DX12 变体、GPU 阶段读回、源缓存 hash、捕获期间源码快照与零分配绑定检查通过。未在活动 Editor 中运行 Unity Test Framework。捕获时的分析器按固定 SHA 精确归档在 `analysis-tools`；后来增加的独立比较入口另有版本记录。

正常加载 Cornell 场景的一次 GPU 观测有 194 个有效单次样本：GI 总均值约 **3.02 ms**、P95 约 **3.65 ms**，Resolve 均值约 **0.197 ms**。这是当前场景单次绝对观测；`gpu-control` 也保留原算法独立观测，但没有组成合格 ABBA、相同原始输入和独立重复，因此**不发布新增 GPU 开销或性能不回退结论**。

```powershell
python -B Tools~/Diagnostics/BuildScreenProbeAnchorStageExperiment.py NEW_OUTPUT
python -B Tools~/Diagnostics/AnalyzeScreenProbeAnchorStages.py E:/VividRP_GI_Evidence/ScreenProbeBlackAnchorStages-20261115-1300/footprint --baseline E:/VividRP_GI_Evidence/ScreenProbeBlackAnchorStages-20261115-1300/production --reference-root E:/VividRP_GI_Evidence/ScreenProbeResidualMap-20261115-104052-278383
```

实际 C# 捕获脚本、前后源码快照、参数、字节数和 hash 均在各目录。每份两组阶段数据为 527,849,472 字节，写入前受 512 MiB 上限约束；移动质量组也受 512 MiB 上限约束。全屏 GBuffer / 深度只记 hash，未复制既有参考。证据加入保留清单，存储巡检单独保存。

最终核对实际 pass 采用的三个关键 DX12 程序与受测候选 SHA 完全相同。正常非诊断帧恢复 8 阶 / 32 批、16 px / 8 rays、age512 / FP32、4-pass、bias=0.04，回调和覆盖参数清空，两场景及 Profile 干净，播放已暂停。本轮新目录约 2.396 GiB，结合既有清单估算证据总量约 115.897 GiB，E 盘剩余约 120.586 GiB；未触及 125 / 64 GiB 审查线，无链接或删除操作。
