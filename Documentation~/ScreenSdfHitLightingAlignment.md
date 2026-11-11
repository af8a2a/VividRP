# Cornell Box：Screen Hit 与 SDF Hit 光照对齐

2026-11-11；Unity 7000.0.0a16，D3D12，RTX 5070 Ti；本机 Unreal 源码版本 5.7.4。

## 已修正的三处差异

### 太阳光的大气透射

`VividLightData` 中的原始太阳光为 `(3600, 3600, 3600)`，但 `LightGridPass.UpdateDirectionalLightUploadData` 会在上传前应用物理天空透射。Cornell 当前机位的透射为 `(0.82201594, 0.66098744, 0.42148829)`。

此前 `SdfDiffuseGatherPass.ReadLight` 使用了未衰减的光色，导致直接命中、Surface Cache 多次反弹及屏幕来源不一致。现在按同一相机、天空上下文和 `interactsWithSky` 开关使用相同透射；点光源保持原路径。原始灯光数据相同不能证明 GPU 实际光色相同。

### 屏幕反弹来源的 BRDF

此前原生 OpenPBR 导出相机视角下的直接漫反射，SDF 命中使用 Lambert。现在原生 OpenPBR 额外导出供屏幕追踪使用的、未曝光的 Lambert 反弹辐亮度：

```text
L_bounce = saturate(baseWeight * baseColor * (1 - metalness))
           * incidentLight * max(N.L, 0) / pi
```

方向光和点光沿用现有光色、阴影与衰减；面积光复用现有 Lambert LTC 形状因子。发光在外层加入一次，间接光由卡片命中单独加入，避免屏幕历史形成重复反馈。表面可见光照继续使用完整 OpenPBR BRDF。

屏幕辐亮度历史改用 `SdfDiffuseScreenLambertRadianceHistory`，防止沿用旧契约的数据。

### 彩板线性贴图被再次解码

`LayerColors.asset` 实测格式为 `R8G8B8A8_UNorm`、`isDataSRGB=false`。它包含线性颜色字节：红色 `(230, 41, 9)`，青色 `(6, 173, 230)`。

Surface Cache 捕获正确保留这些线性值。GPUDriven VT 的 BaseColor 层固定声明为 sRGB，但构建器此前直接保存原始线性字节，运行时再次 sRGB 解码，造成彩板反射率明显偏暗。现在在构建 GPUDrivenSurface BaseColor 页时，将线性源编码到 sRGB 存储；sRGB 源保持原字节，alpha、Normal、Mask 和 Generic 构建契约保持原行为。

该处理同时覆盖 RGBA32 与 Desktop BCn 构建路径，并加入内容编码版本标记以区分旧缓存。通过 Unity 导入流程重建了 Cornell 彩板的 VT stream；没有修改源贴图、材质基色或 SDF 卡片。修改前文件已备份到项目 `Logs/HitLightingAlignment-20261111/VT-before/`。

| 彩板样本 | 线性源反射率 | 原 GPU GBuffer | 修正后的 GPU GBuffer |
|---|---|---|---|
| 红色 | 0.901961, 0.160784, 0.035294 | 0.775822, 0.023153, 0.003035 | 0.896269, 0.165132, 0.043735 |
| 青色 | 0.023529, 0.678431, 0.901961 | 0.001821, 0.417885, 0.791298 | 0.023153, 0.679543, 0.904661 |

剩余的小幅差异包含 BC7 压缩及 GBuffer 量化误差。Ivory 等无贴图材质的 GBuffer 基色没有变化。其他已有的线性 BaseColor GPUDriven VT 需通过同一导入流程重建后才会使用新的编码。

## 同机位验证

固定 1920×1080、3600 lx、EV8、16 px 探针网格、每探针 8 条射线、Adaptive 5×5、两轮 À-trous、8 阶卡片反弹。各组独立冷启动，运行 72 帧，统计第 64–71 帧。基线和最终版本的所有采集 VP 矩阵一致。

关闭方向引导以保持两条路径的逐射线方向一致；另外复测了默认开启方向引导的实际配置。光照诊断使用原生 Screen Probe Gather，没有启用 ReSTIR。

| 区域 | 修正前 SDF / Hybrid 亮度 | 修正前相对差 | 修正后 SDF / Hybrid 亮度 | 修正后相对差 |
|---|---:|---:|---:|---:|
| 天花板 | 59.0135 / 51.2025 | -13.24% | 42.3204 / 44.2860 | +4.64% |
| 地面靠红墙 | 88.6265 / 88.7594 | +0.15% | 61.8787 / 62.0878 | +0.34% |
| 地面靠绿墙 | 41.9374 / 42.0200 | +0.20% | 28.7979 / 28.8867 | +0.31% |
| 后墙 | 42.6302 / 45.2936 | +6.25% | 29.1204 / 29.1818 | +0.21% |

这是覆盖率加权线性 GI 的取样区域均值，量纲沿用 `E/pi`。整体亮度下降包含补齐太阳大气衰减的结果，不能将修正前更高的 SDF 能量当作参考真值。

默认开启方向引导后，天花板相对差为 **+4.16%**，红墙侧地面 **+0.60%**、绿墙侧地面 **+0.26%**、后墙 **+0.57%**。红墙侧地面两条路径的入射 GI RGB 分别为 `(106.746, 51.652, 26.863)` 与 `(107.407, 51.958, 27.027)`。

## 天花板剩余差异的定位

最终 86665 条实体、法线和距离相符的配对射线，Screen/SDF 总辐亮度比为 **0.999512**；天花板接收探针中的 11644 条对应射线比为 **0.998874**。回退到同一个 SDF 命中的样本，辐亮度逐值一致。

天花板局部探针邻域中，1344 条两边均确认命中的射线，亮度总和为 `209419.136 / 209404.273`。另有 **106 条** SDF 结果为 `RayMissingSurface`，而 Screen Hit 恢复了有效命中，新增亮度总和为 `30232.844`。它们全部命中 `Overlapping Panels`，卡片拒绝原因是：

- 73 条无覆盖。
- 17 条深度拒绝。
- 16 条法线拒绝。

临时关闭太阳阴影的对照仍保留彩板差异；修正线性贴图编码后，同实体的彩板命中能量已基本一致。当前剩余的全分辨率天花板差异应继续从卡片覆盖、薄壳命中位置与重构的覆盖率处理推进，而不是再加减反弹增益或拒绝有效的 Screen Hit。

## Unreal 的线性反射率对照

本机 5.7.4 的 `MaterialExpressions.cpp:5126` 将 Constant3Vector 的三个分量直接编译为材质常量；`MaterialTemplate.ush:3720` 对 Base Color 做范围限制，不再将这个线性常量当成 sRGB 解码。

因此若使用 Unreal 的线性 Constant3Vector 来匹配这里的 Unity 材质，应输入转换后的线性值：

| 材质 | Unity 序列化的颜色数值 | 对应 Unreal 线性常量 |
|---|---|---|
| 红墙 | 0.72, 0.12, 0.08 | 0.477000, 0.013412, 0.007194 |
| 绿墙 | 0.09, 0.64, 0.20 | 0.008540, 0.367246, 0.033105 |
| Ivory | 0.78, 0.76, 0.70 | 0.570482, 0.538236, 0.447988 |

彩板贴图自身已经是线性数据，在 Unreal 应关闭该源贴图的 sRGB 标记；不能对它再应用上表的颜色转换。

还需区分 **Base Color** 与 **Lumen 卡片有效反射率**。本机非 Substrate 路径 `LumenCardBasePass.ush:114` 先计算 `BaseColor * (1 - Metallic)`，随后调用 `BRDF.ush:675` 的完全粗糙近似，将 `0.45 * SpecularColor` 并入卡片漫反射。默认非金属 Specular=0.5、F0=0.04 时，该项约为每通道 0.018。它是 Lumen 的粗糙镜面折合近似，不是原始基色，也不是 gamma 修正；本次未将这项近似直接加入 VividRP 的纯漫反射源。

参考：[Epic 物理材质说明](https://dev.epicgames.com/documentation/en-us/unreal-engine/physically-based-materials-in-unreal-engine)、[Lumen 技术细节](https://dev.epicgames.com/documentation/en-us/unreal-engine/lumen-technical-details-in-unreal-engine)。本次核对了本机引擎代码与材质输入契约，没有运行独立 Unreal 场景的视觉 A/B，不能宣称完整画面已与 UE5 一致。

## 验证与证据

- 最终 6 组共 432 帧 D3D12 渲染通过，错误记录为空；包括 0/8 阶、SDF Only/Hybrid 和默认方向引导配置。
- 21 个受影响的 GI/Deferred kernel 通过 DXIL 与 SPIR-V 编译；Unity 实际导入 Deferred shader 无诊断。
- 新增线性 BaseColor 编码与既有 sRGB 字节保持检查，真实运行时方法验证通过。NUnit 程序集独立编译通过；Editor 正在运行，未启动 Unity Test Framework。关闭 Editor 后可运行 `VividVirtualTextureAssetBuilderTests` 中新增的两个编码测试。
- `ReadLight(Camera)` 预热后 4096 次稳定调用为 **0 B** 托管分配；这不是全帧分配测量。
- 本轮没有测 GPU 时间，也没有高采样 GI 真值。

完整缓冲、脚本、统计与截图位于项目 `Logs/HitLightingAlignment-20261111/`。最终验收数据为 `final/`、`final-rays.txt` 和 `summary.txt`；`after/` 是仅改屏幕 Lambert 源的中间组，`attenuated/` 是进一步补齐太阳透射后的中间组，均没有用于冒充最终对齐结果。
