# Sponza：全分辨率历史重投影复验

2026-11-14，Unity 7000.0.0a16 / DX12，原生 Gather，1920×1080、16 px / 8 rays。目的为区分实际历史误用与 CPU 近似诊断的误分类，不修改历史拒绝阈值或默认采样路径。

## 诊断实现

`InspectScreenProbeTemporalHistoryCS` 仅由 Editor 实验显式 dispatch，直接调用生产 `ReadScreenProbeTemporalHistory`。它记录实际四 tap reader 的有效性、footprint、前景权重、物体运动误差、covered lighting、coverage、有效 age，以及原始接收点/法线/深度、实际 previous pixel 和期望深度。

Capture 通过生产 C# `BindReceiver` / `BindProbeFrame` 绑定独立的 compute shader clone，再绑定生产使用的上一帧 VP、jitter、motion、pixel geometry 和 pixel lighting。记录的输入包含 32 px halo；没有从当前 VP 推测 motion，也没有把最近邻测试当作四 tap 的结果。诊断输出为每个采样点六个连续 float4，GraphicsBuffer stride 16；不改变生产 GPU 数据结构或正常 dispatch。

两组分别预热 512 帧，随后沿已记录的 128 帧往返轨迹运动 256 帧；每四帧捕获一次，每个捕获帧在 920×480 区域每四像素检查一个点，共 230×120。每组得到 **1,766,400** 个实际 GPU 样本。2 阶、64 samples / batch、32 batches 的源缓存首尾和两组之间相同。

## 结果

| 指标 | 白噪声 | 分块采样 |
|---|---:|---:|
| CPU replay 与 GPU 有效性判定不一致 | 0 | 0 |
| 读取超出捕获 halo | 0 | 0 |
| 生产 reader 拒绝后仍有 age>1 的样本 | 0 | 0 |
| 有当前 GI 的生产拒绝样本数 | 51,639 | 52,281 |
| 拒绝样本 Temporal−Resolve 最大绝对 Y | 0 | 0 |
| 当前有效像素平均 age | 25.750 | 25.613 |
| object-motion error P99，像素 | 0.004467 | 0.004469 |

CPU 使用记录的精确 GPU 接收点、法线、previous pixel、容差，以及原始前帧 geometry/lighting 独立执行四 tap 计算。footprint 最大差约 8.76e−8、foreground 约 5.97e−8、coverage 小于 2.71e−7、age 小于 9.29e−6。covered RGB 的最大绝对差约 0.001053，P99 约 0.000201，为本批 HDR 输入上的 float32 运算差异；没有将其称为逐位一致。

原来的“新露出诊断带”只看最近邻、采用 0.075 / 0.10 m 阈值；生产路径是四 tap、距离相关容差，并会拒绝 footprint<0.35 或 foreground≥0.25 的组合。因此此前诊断带里约 1.4% 的 age>1，**不能直接作为生产路径接受了已拒绝历史的证据**。本批未发现这类违规继承；它也不证明探针历史、所有几何遮挡或动态物体完全正确。

像素历史在移动中平均仍约 25.6 帧，物体运动残差很小，当前证据不支持为了降噪而放宽这一层拒绝。后续应分离 scalar anchor 的几何重投影、方向场和最终重建。

## 控制边界与复现

与上一批 Sponza 捕获比较时，相机相同，但重新加载场景后的源缓存 hash 不同。跨批 GI 输出对比已判为不合格控制，不用它证明诊断 dispatch 的输出等价或画质变化。本轮 reader 复验只使用本批相同冻结源的两组，以及各自的实际输入，不跨批比较绝对照明能量。

```text
E:/VividRP_GI_Evidence/SponzaReprojection-20261113-173802-195/capture/capture.json
E:/VividRP_GI_Evidence/SponzaReprojection-20261113-173802-195/history-replay.json
```

将 `SponzaBlockedSamplingCapture.cs` 的 experiment 设为 `sponza-reprojection`，通过 Unity CLI 执行；随后运行：

```powershell
python -B Tools~/Diagnostics/ScreenProbeHistoryReplay.py <capture 返回的目录>
```

实际 D3D12 dispatch、回读及 CPU 重放成功，错误清单为空；未运行 Unity Test Framework。未测量 GPU 耗时。本次加入诊断入口，生产拒绝规则、正常内存预算与默认采样不变。

## Scalar anchor 消融：两个原型均撤回

随后固定相同的 16 px / 8 rays、2 阶 / 64 samples / 32 batches，分别以白噪声原路径对照两个独立原型。每批仍有静止及移动四组，各预热 512 帧、测量 256 帧；**只在各自同一冻结源批次内配对**，不跨批比较绝对能量。

1. 窗口原型：通过严格共面、当前帧三段连接路径和原有全分辨率可见性检查后，将移动 anchor 上限逐渐由 8 提到最多 16，保留静止上限 32。没有改变 donor 几何权重。
2. 置信度原型：在上述验证范围内，以原 `ProbeGeometryWeight` 乘连接路径的有界距离权重，补充原线性欧氏距离衰减；同时保留窗口原型。其余区域使用原权重，新露出拒绝不变。

原型使用独立 compute 入口，由 Editor 捕帧临时选择；额外的当前 depth / GBuffer / VP 绑定也仅用于该实验。首个窗口尝试在补齐绑定前由 owner 取消，`complete=false`，未纳入统计。两次完整捕获均成功，GPU 错误记录为空。

| 指标 | 窗口原型：对照 → 原型 | 置信度原型：对照 → 原型 |
|---|---:|---:|
| 移动 anchor 平均有效 age | 5.886 → 6.140 | 5.885 → 7.437 |
| 最终重复机位差值 RMS | 239.017 → 238.987 | 238.673 → 238.449 |
| RMS 相对变化 | −0.0126% | −0.0940% |
| 绝对差值 P99 | 1056.051 → 1055.960 | 1052.556 → 1052.217 |
| 新露出 anchor 数（每组） | 43,059 | 43,029 |
| 新露出 age=1、blend=0 且保留当前样本 | 全部通过 | 全部通过 |

新露出 anchor 的当前样本比较允许一个 FP16 ULP（另含 1e−6 绝对计算容差），覆盖原始诊断纹理写入与显式 nearest-even 的差别，不把量化误差当作历史继承。两批各约 3,533 万个精确匹配几何的重复机位样本参与 RMS。

窗口原型的三个静止 ROI 最终均值与时间方差均不变；实际最终改善太小，没有保留理由。置信度原型虽使移动 age 增加约 26%，但静止左 / 右 ROI 短序列均值分别变化 **+1.306% / +1.518%**，中部 −0.261%；左侧方差增加约 **5.96%**。这些是本批 256 帧结果，不冒充长期能量漂移证明。

置信度原型 RMS 变化的 paired block-8 条件 95% 区间为 −0.116～−0.073%，仅描述本次 80 个重复机位的块离散程度。很小的统计差异不足以抵消上述画质问题。两个原型的 shader 入口和 C# 实验绑定均已撤回，生产源码恢复原实现；不通过增益、曝光或更长历史补偿。

精确原型、patch、SHA-256、完整输入和结果保留在项目外：

```text
E:/VividRP_GI_Evidence/SponzaPlanarAnchor-20261113-175547-778/analysis.json
E:/VividRP_GI_Evidence/SponzaPlanarAnchor-20261113-175547-778/source-window-only/
E:/VividRP_GI_Evidence/SponzaPlanarAnchor-20261113-180918-964/analysis.json
E:/VividRP_GI_Evidence/SponzaPlanarAnchor-20261113-180918-964/source-confidence/
```

原型基于 `0886fc72`，复现应在独立 checkout 应用对应 snapshot 的 patch，并使用对应 capture 源码，不能只在当前主路径打开一个已删除的实验入口。分析器保留 `candidate=planar` 的历史数据读取能力。

恢复检查确认 prototype kernel 不存在、原入口索引恢复 5、hooks 为空、flags=0、frame override=−1，原两个 Cornell 场景均未脏。过滤 shader 无 Error；保留现有 `plane` 同名和潜在未初始化 Warning，详见第二批的 `restoration.json`。生产 C# 和过滤 shader 与原实现的 diff 为空。

后续 `sponza-anchor` 模式只记录正常路径的 raw / temporal anchor、GPU age / separation / rejection、探针几何，不修改 kernel 选择。重放与消融共同说明：当前证据支持继续分离命中辐亮度、方向采样和重建方差，不支持继续扩大 scalar 历史作为主要解决途径。

本机 UE 5.7.4 的 `LumenScreenProbeImportanceSampling.usf` 使用带 SourceLevel 的 RayInfo、多级细分和 BRDF×Lighting PDF。VividRP 目前的 `BuildDirectionalOctGuide` 仍在固定 4×4 格内用八个矩形叶子分区。下一次重要性采样实验应明确区分更细方向提案和现有重建的影响，继续验证完整支持、逆 PDF、长期能量及运动稳定性；上述原型结果不构成这项功能已完成的证据。
