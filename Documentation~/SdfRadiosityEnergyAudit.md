# Surface Cache 漫反射能量审计

2026-11-11；Unity 7000.0.0a16，D3D12，RTX 5070 Ti。

后续对照发现并修正了太阳大气透射与彩板线性贴图编码差异。本文数值保留为该修正前的 Radiosity 预算验证记录；当前命中光照与 Unreal 反射率对照见 [Screen/SDF 命中光照对齐](ScreenSdfHitLightingAlignment.md)。

## 能量与颜色约定

当前反弹链使用未曝光的线性 RGB。设漫反射反射率为 `rho`，入射辐照度为 `E`，出射辐亮度为 `L`：

```text
直接命中：L_direct = emission + rho * E_direct / pi
卡片采样：p(w) = cos(theta) / pi
卡片存储：C = mean(L_in) = E_indirect / pi
卡片读取：L_indirect = rho * C
最终接收：L_receiver = rho_receiver * mean(L_hit)
```

上式描述 Lambertian 能量契约；最终 OpenPBR 像素仍使用其材质反射率与能量分配。

`SdfRadiosity.compute::GatherCardIndirect` 的余弦 PDF 已消去积分中的 `cos / pi`；对平均值额外乘除 pi 都会改变能量。`TraceCardRadiance` 在命中处组合直接光、发光和上一完整反弹阶的间接光；每阶始终从完整的旧 atlas 读取。

本次审计没有发现重复除 pi，保留现有归一化。移除了 `TryLoadCardIndirect` 对反射率逐通道 `min(rho, 0.95)` 的额外衰减，使用 `saturate(rho)`。反弹阶数有限，不需要通过压暗理想白色来强制无限反馈收缩。

Screen Hit 的历史来源是 `VividEvaluateDeferredSurfacePixel` 导出的直接漫反射加发光，未乘曝光，也不包含最终间接光。材质颜色已经乘入，`ReadScreenRadiance` 直接保留 RGB。匹配卡片的间接光在命中后单独加入；不能在读取 Screen Hit 时再乘一次 Albedo。

## 材质回传实测

Cornell Box 墙面无金属、无基色贴图。浮点卡片读取值与材质转换后的线性反射率一致，GBuffer 的 sRGB 存储解码后也保留了颜色：

| 材质 | 源线性 RGB | 卡片 RGB | GBuffer 解码 RGB |
|---|---|---|---|
| 红墙 | 0.477000, 0.013412, 0.007194 | 0.476807, 0.013405, 0.007191 | 0.479320, 0.013702, 0.006995 |
| 绿墙 | 0.008540, 0.367246, 0.033105 | 0.008537, 0.367188, 0.033081 | 0.008568, 0.366253, 0.033105 |
| Ivory | 0.570482, 0.538236, 0.447988 | 0.570312, 0.538086, 0.447754 | 0.571125, 0.539480, 0.445201 |

卡片格式为 `R16G16B16A16_SFloat`，没有额外的 sRGB 编码。GBuffer 的差异与 8-bit 存储精度相符。带贴图的 Layered Panel 会组合基色贴图，不能将其平均值直接与白色材质乘数比较。

Unity 材质中显示/序列化的基色数值经过 `ConvertMaterialColorForGPU` 转为线性。与 Unreal 比较时应对齐实际线性反射率；例如这里序列化的绿色通道 `0.64` 对应约 `0.367` 的线性反射率，不能直接当成 Unreal 的线性材质常量 `0.64`。

## 更新预算

- `Surface Cache Bounce Orders`：支持 0–8 阶，0 保留原有直接命中行为。
- `Surface Cache Cards Per Frame`：大场景每帧卡片预算，默认 16。
- `Surface Cache Full Update Card Limit`：卡片总数不超过此值时，每帧完成一阶完整扫描，默认 128；0 关闭快速填充。

预算变化继续当前扫描的 cursor，不清空已发布结果。未完成的扫描保持不可读；改变光照、几何、材质卡片版本或反弹目标时，沿用现有失效规则重新计算。大场景保留固定预算，收敛后停止更新。新设置没有引入稳定路径的托管分配。

Cornell Box Profile 已保存为 Screen Probe Gather、8 阶、预算 16、快速填充阈值 128。修改前的完整 Profile 备份在项目 `Logs/RadiosityEnergy-20261111/CornellBox Profile.before.asset`。

## Cornell Box 验证

固定机位、1920×1080、3600 lx 定向光、EV8、16 px 探针网格、每探针 8 条射线、Adaptive 5×5、两轮 À-trous。使用 B 卡片资产，共 37 张实例卡片。每组从独立的 Gather 状态和未发布反弹开始，运行 72 帧；统计第 64–71 帧的线性、覆盖率加权 GI。

| SDF Only 配置 | 首次完整发布的帧索引（从 0 开始） | 首帧更新卡片 |
|---|---:|---:|
| 3 阶，固定 16（原实现） | 9 | 16 |
| 3 阶，小场景快速填充 | 3 | 37 |
| 8 阶，固定 16 | 24 | 16 |
| 8 阶，小场景快速填充 | 8 | 37 |

独立冷启动的两种 8 阶预算在收敛后的卡片 atlas 逐值一致，最大差值为 0。快速填充只改变完成时间。预算切换保持已完成结果的行为也通过单独的连续配置运行检查。

下表是固定取样区域的 `E/pi` 亮度均值，包含追踪覆盖率；0 阶仍包含从直接照亮表面返回的一次漫反射，并不代表关闭 GI：

| 取样区域 | 0 阶 | 3 阶 | 8 阶 | 8 阶相对 3 阶 |
|---|---:|---:|---:|---:|
| 地面靠红墙 | 70.3288 | 88.5603 | 89.1961 | +0.72% |
| 地面靠绿墙阴影 | 27.2453 | 41.4805 | 42.4400 | +2.31% |
| 后墙 | 21.8470 | 41.5175 | 42.4829 | +2.32% |
| 天花板 | 37.8612 | 58.1778 | 58.8928 | +1.23% |

8 阶时靠红墙地面的入射 GI 为 RGB `(129.896, 79.507, 65.326)`，保留明显红色溢出。3 阶已包含这套材质和光照下的大部分能量；不能依据画面期望人为补一个 pi 或色溢增益。

## 检查与边界

- 5 组独立冷缓存配置共 360 帧 D3D12 渲染通过，包含 0/3/8 阶、两种预算、Hybrid；捕获期间错误记录为空。
- 11 个受共享光照代码影响的 shader kernel 通过 DXIL 与 SPIR-V 编译。
- 10 个预算边界用例通过真实运行时方法检查；4096 次稳定调用为 0 B 托管分配。新增 NUnit 测试独立编译通过，包含中途修改预算的扫描延续检查。
- 当前 Editor 正在运行，未启动 Unity Test Framework。关闭 Editor 后可运行 `VividRP.Editor.Tests.SdfRadiosityBudgetTests`。
- 本次未测 GPU 时间。快速填充增加前几帧更新工作量，发布更早不等于总 GPU 耗时降低。
- Hybrid 与 SDF Only 的亮度仍有差异，例如天花板区域 Hybrid 为 51.2961，SDF Only 为 58.8928；不能将这项差异归因于基色饱和度丢失。命中可见性与直接光 BRDF 的差异仍需进一步分离。
- 没有同场景 Unreal 高采样参考，本次不能证明整体明暗已经与 UE5 一致，也没有验证对无限反弹解的误差。

完整缓冲、收敛曲线与复现脚本位于项目 `Logs/RadiosityEnergy-20261111/`。用于验收的是 `cold/` 和 `cold-comparison.txt`。早期 `baseline/` 仅使用真实的 `bounce3` 对照；其余命名的早期反弹组因诊断脚本替换了已缓存的 VolumeParameter 对象而无效，未用于阶数对比。后续通过原参数的 `Override` 修正，并逐帧断言实际反弹目标。

参考：[PBRT 漫反射与余弦采样](https://pbr-book.org/4ed/Reflection_Models/Diffuse_Reflection)、[Epic Lumen 技术细节](https://dev.epicgames.com/documentation/en-us/unreal-engine/lumen-technical-details-in-unreal-engine)。
