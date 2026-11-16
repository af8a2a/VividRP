# Screen Probe 发射起点与追踪预算实验

2026-11-16，Unity 7000.0.0a16 / D3D12 / RTX 5070 Ti。承接[小波因子分离](ScreenProbeWaveletFactorial.md)中的 4 cm 查询偏移敏感性。结果是**保留诊断工具，撤回默认推广**：自适应 1 cm 消除了直接缩短偏移导致的自命中，也改善了部分面的静态误差，但天花板、部分盒面和时间稳定性仍有退化。

证据目录：`E:/VividRP_GI_Evidence/ScreenProbeOriginReplay-20261116-174500`。本轮没有修改生产 Runtime、shader、Profile 或场景；没有新增 PIX 或 GPU 耗时结论。

## 实验约束与工具

继续使用纯顶部单面自发光 Cornell、固定相机、16 px 网格、每探针 8 条射线。Radiosity 固定 8 阶、64 samples、32 batches，测量期间不更新。每次只重置 Gather，预热 1024 帧，统计后续 128 帧。两个采样序号起点为 1733、8779。

实验沿用此前的 consumed-energy moments 和覆盖率小波原型，保留原方向性双边权重。因此结果不能推广为保存的默认链的质量测量。独立参考仍为已有的 2336 个真实像素目标：天花板及背墙 ROI 每点 131072 paths，其余每点 16384 paths，9 次 Lambert 漫反射。未改参考几何、材质、光强、源码或拟合增益。

`ScreenProbeOriginReplay.py` 生成内存实验 shader，替换 Screen / SDF 两处发射起点，保留配置中的 .04 m 命中阈值和其他使用位置。每个测量帧在原绑定下重放到独立输出：

- 完整 directional rays、geometry、hit lighting、trace sources、ray diagnostics、first hit，以及有效时的 screen rays，均做 native / A/A 逐字节比较。
- 方向输出中的 state0 原本不写入；只保留这些未定义槽位，每个实际应写入的槽位先填毒值，再比较完整纹理。
- 用 structured buffer 导出 receiver entity / SDF instance，检查同对象自命中、未完成射线和有效黑背面；比较逐射线的未完成掩码，不能用总数相抵。
- 每个完整运行有 1031 对追踪输出/计数 A/A，另有 3300 对原生/私有历史验证。跨起点比较还核验 640 对几何输入、矩阵、相同的编译程序及 frozen source。

原生 4 cm / 1 cm 各自持有完整的方向引导与时间历史。它们是分别收敛的链，**不声称跨运行的历史逐位相同**。统计保留参考 IID 不确定度；不能将相关的 renderer 帧当作独立样本。

## 先解决自命中，再检查步数

直接把两处起点改为 `min(.04, .01)` 时，129 个诊断帧、7175496 条有效 receiver 射线中出现 **43450 次有覆盖率的同对象命中**。端点检查包括 Signed Solid 及 ThinShell；典型 origin 已在几何外侧，却仍落在 SDF 的 .075 voxel 命中带内。既有 ThinShell 退出修正要求内部起点和梯度一致，不能靠放松该条件跳过几何。

自适应版本依据 receiver 的实际 SDF 实例和非均匀缩放选择法向偏移：至少保留局部 shell 半厚及 .125 voxel 安全距离，最大不超过原 .04 m。还要求该起点已离开场，或实际 SDF 值大于既有 .075 voxel 命中阈值，否则退回原 .04 m。这里的“1 cm”是请求下限，并非所有面恒为 1 cm。

自适应 1 cm 在原 192 步下消除了新增自命中，但未完成射线从 2100 增至 2410。提高到 384 步后总数下降，却仍有 **4 条新增未完成射线**，逐射线门槛拒绝该版本。随后采用 field 对角线推导、最多 2048 步的有限预算，才通过这些帧的检查；一组救回 2081 条旧未完成射线且没有新增未完成射线。

不能将该预算称为任意场的完成保证。实测 Cornell 已有三个 field：

| 尺寸 | 未封顶的对角线预算 | 实际预算 |
| --- | ---: | ---: |
| 32×32×32 | 1024 | 1024 |
| 139×11×107 | 3321 | 2048 |
| 71×71×79 | 2402 | 2048 |

后两个 field 已触及封顶。离线回归明确保留 64³ 的反例。通过的是本次射线集合的实际未完成检查，不是所有未来方向的理论证明。5 mm 在序号1733的完整运行中仍出现一次同对象命中，未采用。

## 分离起点与预算

早期 `native-origins-physical.json` 与 `sequence8779-native-origins-physical.json` 同时改变了起点和小起点分支的预算，不能单独归因于起点。随后增加 `--origin-bound-legacy`，让所有起点共用同一预算，另跑四个完整 native capture。

`shared1733-native-origins-physical.json`、`shared8779-native-origins-physical.json` 的 4 cm / 1 cm 两侧使用相同源码、编译程序和预算；四次运行全部通过 1031 对追踪 A/A，均没有新增自命中或未完成射线。有效黑背面仍保持覆盖且 RGB=0。

以下为**时间均值图相对独立参考的亮度 MSE 变化**，负数表示改善；不是时间噪声减少率。

| 实际目标组 | 序号1733 | 序号8779 |
| --- | ---: | ---: |
| 天花板 288 点 | +121.21% | +43.39% |
| floor ROI 384 点 | −43.64% | −44.92% |
| 其中实际 Floor 290 点 | −60.15% | −56.92% |
| 其中 Short White Block 80 点 | +14.13% | +16.10% |
| 其中 Tall White Block 14 点 | +16.30% | +2.96% |
| back ROI 384 点 | −79.43% | −64.12% |
| edge ROI 1280 点 | −42.39% | −40.89% |

实际 Floor 的平均误差分别从 +3.703%→+1.565%、+3.826%→+1.673%，与独立模型中缩短查询偏移降低地面直接项的方向一致。天花板则从 −0.492%→−1.320%、−0.853%→−1.273%。不能用整体 ROI 的均值抵消不同对象的误差，也不能用地面收益宣布全场景通过。

全 ROI 的**时间方差变化**另列如下，正数为退化：

| ROI | 序号1733 | 序号8779 |
| --- | ---: | ---: |
| ceiling | +152.98% | +3.41% |
| floor | +22.96% | −6.36% |
| back | +8.99% | +148.97% |
| edge | +31.30% | +12.33% |

时间方差的基数和完整绝对 RMS 均保留在 JSON；这些比率不能解释为同幅度的可见亮度变化。第二序列 back 的帧差 RMS 还增加 7.11%，不满足移动稳定性推广的前置要求。尚未执行运动轨迹或 GPU 性能验收。

另用 `budget-factor-physical.json` 比较固定 4 cm、仅修改两个 marcher 预算表达式的独立历史。floor ROI 的均值图 MSE 分别增加 32.22% / 6.66%，ceiling 增加 .36% / 12.22%。救回未完成射线不自动等于最终图更接近参考；不能单独推广更高预算。

## 保留的失败与结论边界

首次 native 1 cm 在 frame1055 因 geometry A/A 不同停止：3478 个 texel、4094 个 uint word，最大位置差 7.152557e−7 m。其余追踪输出一致，CPU 矩阵及已读深度不变。原生 geometry 只在 tracing 中写入，未找到后续 Pass 改写证据。

`native-aa-failure-witness.json` 与两份原始失败纹理保留。相同代码另建目录重跑通过，后续四次等预算运行也通过，但**没有证明首次失配的根因或修复**，没有放宽几何容差、跳过该输出或抹去失败。

其他失败包括早期 persistent trace 所有权检查、UInt 纹理直接 async readback 不支持，以及实验 screen kernel 缺少新增的 SDF buffer 绑定。分别保留失败目录，随后修正诊断实现：使用 structured readback、验证已写槽位、只给实验 shader 补齐 screen 所需 buffer 并逐帧检查引用未变。

早期生成协议的说明文字误写为“只改起点”；实际执行 shader、`originTraceSteps`、`originStepPolicy` 已记录预算变化。未重写旧协议。当前 builder 已修正文案，判读以固定源码及显式 policy 为准。

## 恢复、保留与下一步

190 项 Python 检查通过；所有实验 C# / compute 由 live eval 实际编译执行。活动 Editor 未运行 Unity Test Framework。完成后用原链预热1024帧，并核验 persistent Hybrid trace、原 temporal / 两个小波 DXIL、空诊断回调、override −1、flags0、16/8、anchor512、pixel32、四轮、bias .04、源8阶/32批。两个场景干净，PlayMode 暂停，Frame Debugger 关闭、PIX idle。

完成分析的原始 frame 读回在逐项 ZIP 解压、SHA 校验后移除重复的未压缩副本；失败捕获保持完整。协议、shader、脚本、对象分组、统计和可完整恢复的 ZIP 保留，容量审计见 `post-experiment-storage.json`。完整重分析须恢复原路径并核验 SHA，再移开 `raw-retention.json`。

下一步应保持原默认，先固定同一批方向，分开核对精确几何首命中、SDF 首命中及命中辐亮度，重点定位 ceiling 与 Short 面的退化，再决定起点应如何投影或校正。不要叠加更大滤波核或全局能量补偿，也不要将减少偏移与增加步数打包为已验证的质量改进。

后续[同方向首命中分解](ScreenProbeFirstHitOracle.md)已复用这四个等预算捕获，确认1 cm的对象命中更接近精确几何，同时定位到新增光片卡片缺失；尚未撤销本页的最终质量失败结论。
