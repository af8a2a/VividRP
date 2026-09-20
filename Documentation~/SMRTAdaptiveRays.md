# SMRT 生产自适应退出

在 Cascaded Shadow Maps Volume 中开启 **Virtual Shadow Map SMRT Adaptive Rays**。它只在 VSM、SMRT 和非零灯光角直径有效时生效；关闭时使用完整配置 ray 数。默认值沿用已有设置，不依赖时域降噪或有效历史。

## 策略

- 第一条 ray 的所有参与 lane 都有效且未遮挡：输出全亮并结束。
- 至少执行两条 ray 后，所有参与 lane 的累计结果均遮挡：输出全暗并结束。
- 混合 wave 继续追踪；后续某轮全 miss 不会触发全亮退出。
- footprint 缺页或中途追踪失败先参与有效性投票，再退出。其他 lane 不能把失效 lane 的离开当成一致结果。
- 按实际执行 ray 数归一。对应 UE 参考 `AdaptiveRayCount = 1` 的零基索引门槛，不是根据历史阴影判定。

静态/动态深度池仍分别保留完整 16 层；每条实际发射的 ray 沿用多层 DDA 求交。减少 ray 数会改变统计估计和半影噪声，不保证与固定预算逐像素一致。需要精确固定预算对照时关闭该开关。

## 实现和诊断

生产开启使用新增 `VSMShadowResolveAdaptive` kernel，关闭使用原 `CSMShadowResolve`。新入口追加在 kernel 列表末尾，旧索引不变。独立编译保证关闭模式不携带波操作；其 DXIL 与接线前固定预算版本一致。

`CSMShadowResolvePass` 在准备历史前计算 adaptive 状态，因此切换开关会使不兼容的旧阴影历史失效。`VSMReceiverDebugPass` 同步绑定设置，SMRTWork 视图和 `smrt-cost` 按当前模式统计实际 ray 数。没有额外常驻 GPU 缓冲或正常帧诊断 dispatch。

验证、计时及质量差异见 [生产接线报告](../Temp~/VSM/Roadmap~/Experiments/SMRTAdaptiveProduction_20260918/README.md)。
