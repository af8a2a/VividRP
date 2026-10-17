# SMRT 生产自适应射线退出 (Adaptive Rays)

返回索引：[Documentation~ 索引](README.md) · 相关文档：[VSM 代码布局](VirtualShadowMapCodeLayout.md) | [VSM Shader 参考关系](VirtualShadowMapShaderReference.md) | [SMRT 成本诊断](SMRTCostDiagnostics.md)

## 启用条件

在 `Cascaded Shadow Maps` Volume 中开启 **Virtual Shadow Map SMRT Adaptive Rays**：

- **生效前提**：仅当 **VSM**、**SMRT** 与**非零灯光角直径**同时有效时生效；关闭时使用完整配置的固定射线数。
- **无历史依赖**：判定仅基于当帧 Wave 内的射线求交结果，不依赖时域降噪或有效阴影历史。

## 自适应退出策略

| 阶段 / 情形 | Wave 投票与退出行为 |
| --- | --- |
| **第 1 条射线（零基索引 `0`）** | 若 Wave 内所有参与 Lane 均有效且**全部未遮挡（All Miss）**，直接输出全亮（`1.0`）并提前结束 |
| **第 2 条射线起（零基索引 `>= 1`，对标 UE `AdaptiveRayCount = 1`）** | 若所有参与 Lane 的累计结果**全部遮挡（All Occluded）**，直接输出全暗（`0.0`）并提前结束 |
| **混合半影 Wave** | 继续追踪后续射线；后续某一轮出现单轮全未遮挡**不会**触发全亮退出 |
| **Footprint 缺页或追踪失败** | 失效 Lane 必须先参与有效性投票（禁止其它 Lane 将失效 Lane 的离开误判为一致结果），再退出 |
| **结果归一化** | 按实际执行的射线数归一化 |

> [!NOTE]
> - 静态/动态深度池仍各保留完整 16 层，每条实际发射的射线沿用多层 DDA 求交。
> - 减少射线数会改变统计估计和半影噪声，不保证与固定预算逐像素完全一致；需要精确固定预算对照时请关闭该开关。

## 实现与诊断集成

- **独立 Kernel 编译**：开启时调度追加在末尾的 `VSMShadowResolveAdaptive` Kernel，关闭时调度原 `CSMShadowResolve` Kernel（旧 Kernel 索引不变，且关闭模式编译产物不携带任何 Wave 操作）。
- **历史自动失效**：`CSMShadowResolvePass` 在准备历史前计算 Adaptive 状态，切换开关会自动使旧阴影历史失效。
- **调试与成本统计联动**：`VSMReceiverDebugPass` 的 `SMRTWork` 视图及 [`smrt-cost` 诊断工具](SMRTCostDiagnostics.md)均按当前激活模式统计实际执行的射线数，正常渲染帧不产生额外常驻 GPU 缓冲或诊断 Dispatch。
