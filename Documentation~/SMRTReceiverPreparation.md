# SMRT 接收点与投影准备复用

本次优化将与 ray 无关的数据准备移到接收面/投影层级，保留原有选层、完整 footprint 检查、多层 DDA 和缺页回退。

| 数据 | 准备与复用范围 |
| --- | --- |
| 归一化接收面法线 | 每次 `ResolveVSMReceiverMode` 一次，供密度选层及候选投影使用 |
| 投影、normal bias、接收点坐标 | `PrepareVSMReceiverProjection`；密度选层通过 `SelectVSMDensityLevelPrepared` 返回已接受的数据，主层求值直接消费 |
| 随机相位 | `PrepareVSMSMRTReceiverSamples` 在第一个完整 footprint 后按需获取，供同一接收点的候选层和过渡层共享 |
| 起始投影的纹素尺度、深度尺度、平移 | `PrepareVSMSMRTProjection` 一次准备，供 footprint 和所有 ray 使用 |
| 接收点原始纹素坐标 | ray 循环外计算；每条 ray 的抖动和斜率补偿仍分别执行 |

粗层继续根据自己的投影与平移计算，不能复用细层偏移后的坐标替代粗层接收点。跨 clipmap 的射线保持同一条世界空间路径；起始层也保留原来的减平移、缩放、加平移运算顺序。没有创建每像素 clipmap 数组或跨帧缓存。

静态和动态深度池仍各保留 16 层。此次不改变 ray 数、自适应规则、步数、射线长度、层间混合或无效样本语义。成本诊断中的 ray、DDA、隐藏层读取计数保持原义，GPU 收益来自减少重复准备，不能根据这些计数推导 ALU 或访存节省量。

与 UE 的对应是 `TraceDirectional` 在 ray 循环前准备接收点、投影和斜率的职责组织；当前仍采用自己的多层 DDA，不等同于 UE 的固定步点 `SMRTFindSample`。

[编译、GPU 等价对照及配对计时报告](../Temp~/VSM/Roadmap~/Experiments/SMRTReceiverPreparation_20260918/README.md)。
