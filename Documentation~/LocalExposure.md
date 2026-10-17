# 双边网格局部曝光 (Local Exposure)

返回索引：[Documentation~ 索引](README.md)

`LocalExposurePass` 将 Unreal 直方图与局部曝光后处理流程中的**双边网格（Bilateral Grid）局部曝光**算法移植到 VividRP 的显式 RenderGraph 模型中。

## 渲染图位置与连接

该 Pass 不会自动注入现有 `.vrdg` 资产，需在后处理链路中显式放置。典型后处理顺序如下：

```text
AutoExposurePass -> LocalExposurePass -> BloomPass -> ColorGradingPass -> FinalBlitPass
```

- **输入/输出**：读取 `source`，写入 `LocalExposureOutput`。
- **管线串联**：实现了后处理 Source Override 契约，下游基于 `source` 的 Pass 可直接按常规后处理链消费局部曝光调整后的场景颜色。

## 实现规格与范围

当前仅包含**双边网格局部曝光路径**（不包含 Unreal 实验性的 Fusion 路径或局部曝光可视化调试视图）：

- **网格划分**：`64 × 64` 双边网格瓦片（Bilateral-Grid Tiles）× `32` 层亮度切片（Luminance Slices）。
- **存储格式**：使用 `R32G32_SFloat` 3D 纹理存储加权对数亮度累加值与权重。
- **曝光联动**：直接消费当前 VividRP 自动曝光缓冲区中的 Exposure Scale 与 Pre-Exposure。
- **模糊近似**：对模糊亮度采用有界可分离高斯近似。

## Volume 控制参数

在 Volume Profile 中添加并启用 `Local Exposure` 组件，可调节以下参数：
- 高光/阴影对比度（Highlight / Shadow Contrast）及可选的对比度曲线；
- 细节保留强度（Detail Strength）；
- 模糊亮度混合比例与核半径百分比（Blurred Luminance Blend / Kernel Percent）；
- 阈值控制与中灰偏移（Middle Grey Bias）。
