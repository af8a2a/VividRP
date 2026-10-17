# 环境光遮蔽 (Ambient Occlusion: XeGTAO / FidelityFX CACAO)

返回索引：[Documentation~ 索引](README.md)

VividRP 的 `Ambient Occlusion` Volume 组件支持在 **XeGTAO** 与 **FidelityFX CACAO** 两种算法实现之间无缝切换。两者共用同一个 `GTAOPass` RenderGraph 节点并统一输出 `GTAOTexture`，因此切换实现时**无需修改 `.vrdg` 渲染图连线**。

> [!NOTE]
> 运行时 Volume 组件类型为 `AmbientOcclusion`；旧版使用 `GTAO` 类型创建的 Volume Profile 会自动迁移并默认保持 `GTAO` 模式。

## 启用与切换实现

1. 在 Volume Profile 中添加或选中 `Post-processing > Ambient Occlusion`。
2. 勾选启用该组件。
3. 将 `Implementation` 设为 `GTAO` 或 `FidelityFX CACAO`（自定义 Inspector 会自动过滤并仅显示当前实现所需的参数）。

| 实现模式 | `Quality Level` 范围 | 特性说明 |
| --- | --- | --- |
| `GTAO`（默认） | `0` – `3` | 基于 XeGTAO 的屏幕空间环境光遮蔽路径 |
| `FidelityFX CACAO` | `0` – `4` | 等级 `4` 启用自适应重要性图（Adaptive Importance-Map）路径 |

## FidelityFX CACAO 参数与管线细节

- **`CACAO Downsampled`**：降低内部 AO 分辨率，并启用 CACAO 的深度感知双边上采样器（Depth-Aware Bilateral Upsampler）。
- **`CACAO Blur Passes`**：选择 `0` – `8` 次边缘敏感模糊迭代。
- **`CACAO Adaptive Quality Limit`**：控制仅在 `Quality Level = 4` 下生效的额外采样预算。
- **核心外观参数**：Shadow Multiplier、Power、Clamp、Horizon Threshold、Detail Strength、Fade Distances、Sharpness 与 Bilateral Sigma 直接映射到对应的 FidelityFX CACAO 参数。
- **输入与瞬态资源**：CACAO 读取 HZB Mip 0 作为设备深度，并从 `GBuffer1` 解码世界空间法线；其内部的去交织（Deinterleaved）深度、法线、SSAO、重要性图与负载计数器纹理均为 RenderGraph 瞬态资源（Transient Resources）。
- **开源许可**：移植基于 AMD FidelityFX CACAO 及其源自 Intel ASSAO 的着色器代码，许可声明见 `Shaders/Core/Private/CACAO/LICENSE.txt` 与包根目录的 `Third Party Notices.md`。
