# FFT 卷积泛光 (FFT Convolution Bloom)

返回索引：[Documentation~ 索引](README.md)

`BloomPass` 在 Volume 中支持两种模式：

- **`Scattering`（默认快速路径）**：沿用多级 Mip 金字塔散射 Bloom。
- **`Convolution FFT`**：使用用户提供的卷积核纹理在频域对阈值化后的 HDR 图像执行二维卷积。

## 配置步骤

1. 在 Volume Profile 中添加 **Post-processing > Bloom**。
2. 将 **Mode** 设为 `Convolution FFT`。
3. 为 **Convolution Kernel** 指定一张线性 HDR 或灰度卷积核纹理（建议使用黑色背景与非负 RGB 值，支持彩色卷积核）。
4. 将 **Convolution Center** 设为卷积核最亮中心点（居中卷积核使用 `(0.5, 0.5)`）。
5. 调节 **Convolution Size** 与 **Intensity**。

> [!NOTE]
> 实现会在卷积前对中心高亮区域进行钳制（Clamp），使源高光保留在场景中，而周围的散射瓣转化为泛光。若未指定卷积核纹理或 FFT Kernel 不可用，`BloomPass` 会自动回退到 `Scattering` 模式。

## 算法流程（对标 Unreal FFT Bloom）

1. **降采样与阈值化**：对场景颜色输入进行降采样并提取高亮部分。
2. **边缘零填充（Zero Padding）**：根据卷积核半径预留零填充区，抑制周期性圆周卷积带来的跨屏幕边缘回绕。
3. **2 的幂次频域对齐**：将宽高向上取整至 2 的幂次（上限 `4096`）。
4. **卷积核预处理与频谱缓存**：将居中的空间卷积核环绕对齐至 FFT 原点，归约 RGB 能量并缓存其频域表示（仅在纹理内容、尺寸、中心、钳制参数或 FFT 域尺寸变化时失效重建）。
5. **正/逆 FFT 与频域复数乘法**：执行酉基 2（Unitary Radix-2）正向变换、频域复数乘法与逆向变换。每个水平或垂直轴仅需**单次 Dispatch**（不再按基 2 级数多次 Dispatch）。
6. **能量归一化与合成**：按卷积核总能量归一化后，接入现有的 Bloom Tint、Intensity、Lens Dirt、Lens Flare 及最终合成绑定。

## 性能特征与硬件路径

| 项目 | 行为与约束 |
| --- | --- |
| **Convolution Resolution Scale** | 默认 `0.25`。由于 FFT 内存和计算量按下一个 2 的幂次跳变，微小的分辨率调整可能使某条轴的计算域翻倍 |
| **Convolution Buffer Scale** | 限制零填充比例。调低可节省显存，但过低会导致强高光在屏幕对侧边缘出现泛光回绕 |
| **频域缓冲区精度** | 使用 `R16G16B16A16_SFloat`；极端明亮且大面积分布的预曝光值可能超出半精度浮点上限 |
| **Wave32 / Wave64 主路径** | 当变换轴长度 ≥ 1 个 Subgroup 且 ≤ `2048` 时启用：扫描线前几级使用 Wave Shuffle，后续级使用 Group-Shared Memory (LDS) |
| **LDS 兼容路径** | 当变换轴达到 `4096`、小于 Subgroup、缺少 Wave 变体或 Subgroup 尺寸不受支持时启用：通过复用的 16 KiB 复数共享内存按 R/G/B 通道顺序处理 |
