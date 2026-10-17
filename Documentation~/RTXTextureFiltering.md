# 参考路径追踪中的 RTX Texture Filtering (RTXTF)

返回索引：[Documentation~ 索引](README.md) · 相关文档：[NVIDIA SER](NVAPIShaderExecutionReordering.md)

`StandardLit` 参考路径追踪器集成了位于 `Shaders/ThirdParty/RTXTF` 的 **NVIDIA RTX Texture Filtering (RTXTF)** 随机纹理过滤库（保留原始 NVIDIA 许可声明，参见 [`Shaders/ThirdParty/RTXTF/README.md`](../Shaders/ThirdParty/RTXTF/README.md)）。

## 作用范围与设计约束

- **默认适用范围**：在 `ReferencedPathtracingDXR` 中，RTXTF 默认仅对**不透明（Opaque）`StandardLit` 表面**的纹理读取生效。
- **安全回退材质**：Alpha Test、半透明（Transparent）以及使用虚拟纹理（Virtual Texture）的材质保留原有的可见性安全采样路径（RTXTF 上游文档明确指出，对 Alpha 贴图与离屏可见性缓冲进行随机纹素采样会引入伪影）。
- **Ray-Cone LOD 职责分离**：显式 Mip LOD 选择仍由参考路径追踪器的 Ray Cone 计算负责；RTXTF 在该显式 LOD 下随机采样目标纹素与分数级 Mip（Fractional Mip）。
- **禁用跨线程协同放大**：由于光线追踪 Wave 内的线程不保证屏幕空间相邻一致性，Closest-Hit 着色器中禁用了 Collaborative Magnification。

## Volume 参数配置

在 **VividRP/Path Tracing/Reference Path Tracing** Volume 组件中配置：

| 参数 | 说明 |
| --- | --- |
| **Enable RTXTF** | 开启或关闭随机纹理过滤 |
| **RTXTF Filter** | 选择重建滤波器：`Linear`（默认，收敛结果与硬件双线性过滤一致）、`Cubic` 或 `Gaussian` |
| **RTXTF Gaussian Sigma** | 控制 `Gaussian` 模式下的高斯核半径（单位：纹素） |

> [!IMPORTANT]
> `Cubic` 与 `Gaussian` 滤波器单帧噪声更高，需配合多帧时域累积或 DLSS Ray Reconstruction 收敛。任何 RTXTF 参数均属于积分器签名（Integrator Signature）的一部分，修改后会自动使旧的累积历史失效并重新收敛。
