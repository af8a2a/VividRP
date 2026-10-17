# SMRT 接收点与投影准备复用

返回索引：[Documentation~ 索引](README.md) · 相关文档：[VSM Shader 参考关系](VirtualShadowMapShaderReference.md) | [SMRT 自适应射线退出](SMRTAdaptiveRays.md) | [SMRT 成本诊断](SMRTCostDiagnostics.md)

## 概述

本优化参照 Unreal Engine `TraceDirectional` 在进入射线循环前统一准备接收点、投影和斜率的职责组织方式，将与单条射线无关的数据准备提升至**接收面/投影层级**，同时完整保留原有的密度选层、Footprint 检查、多层 DDA 与缺页回退语义。

## 准备与复用范围

| 准备数据 | 负责函数 / 位置 | 复用范围 |
| --- | --- | --- |
| **归一化接收面法线** | `ResolveVSMReceiverMode` 入口计算一次 | 供密度选层及所有候选投影共享 |
| **投影、Normal Bias、接收点坐标** | `PrepareVSMReceiverProjection` / `SelectVSMDensityLevelPrepared` | 密度选层直接返回已接受的投影准备数据，主层求值直接消费，避免重复矩阵乘法 |
| **随机相位（Random Phase）** | `PrepareVSMSMRTReceiverSamples` | 在通过第一个完整 Footprint 检查后按需获取一次，供同一接收点的候选层与过渡层共享 |
| **起始投影纹素尺度、深度尺度与平移** | `PrepareVSMSMRTProjection` | 每个投影准备一次，供 Footprint 检查与所有 SMRT 射线共用 |
| **接收点原始纹素坐标** | 提升至 Ray 循环外计算 | 循环内每条射线仅分别计算各自的抖动（Jitter）与斜率补偿（Slope Bias） |

## 精度与行为不变量

- **粗层坐标独立计算**：粗层继续根据自身的投影与滚动平移计算坐标，**不复用**细层法线偏移后的坐标替代粗层接收点；跨 Clipmap 的射线保持同一条世界空间路径，起始层严格保留原有的“减平移 → 缩放 → 加平移”运算顺序，保证数值等价。
- **零额外存储**：不创建每像素 Clipmap 数组或跨帧缓存，静态与动态深度池仍各保留 16 层。
- **诊断计数不变性**：由于本优化不改变射线数、自适应规则、步数、射线长度或层间混合，[`smrt-cost`](SMRTCostDiagnostics.md) 中的射线、步点与深度读取计数保持不变；减少重复准备带来的 ALU 收益需通过普通生产 Kernel 的配对 GPU 计时测量。
