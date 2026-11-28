# Solid Optical & Volumetric Glass Architecture and Roadmap

本文档记录 VividRP **实心光学 / 体积吸收玻璃（Solid Optical & Volumetric Glass）** 的架构实现现状（Phase 1 + Phase 2 已交付）以及 **Phase 3（DXR 扩展，当前留档暂不推进）** 的设计备忘。

---

## 1. 已完成交付（Phase 1 & Phase 2）

- **Phase 1（Commit `a660f9e8`）**：物理双面折射、OpenPBR Beer-Lambert 体积吸收、Cauchy/Fraunhofer RGB Abbe 色散、同帧 `OpaqueColorPyramid` 粗糙磨砂模糊与 `GlassLookDev` 标定场景。
- **Phase 2 & SRP Batcher（Commit `461438bc`）**：彩色透射阴影 + 雅可比聚焦焦散（`TransmissiveCausticsPass`）、背面二次直接高光、OpenPBR Henyey-Greenstein 内部体积单次散射，以及全 Pass 统一 `UnityPerMaterial` CBuffer 的 **SRP Batcher 完整兼容**。

### 1.1 标准 RenderGraph 链路（零 Hook、稳态零 GC）

```
DeferredLightingPass -> SubsurfaceScatteringPass
  -> TransmissiveCausticsPass       (Light-space backface/frontface MRT -> Screen-space 9-tap PCF colored shadow + caustics into SceneColor)
  -> OpaqueColorPyramidPass         (Same-frame FFX SPD OpaqueColorPyramid + R32_SFloat OpaqueDepth)
  -> TransparentBackfacePass        (LightMode = VividTransparentBackface -> RGBA16F octNormalWS + linearViewZ)
  -> ForwardRefractionPass          (LightMode = VividRefraction -> Double-sided Snell + Beer-Lambert + Abbe Dispersion + HG Scatter + Backface Spec)
```

- RenderGraph 自动拼接工具：`Editor/RenderGraph/RefractionRenderGraphUtility.cs`
- 已接入模板与资产：`Editor/RenderGraph/Templates/StandardRenderGraph.vrdg.txt`、`Assets/Standard Vivid Render Graph.vrdg`、`Assets/ClassicSponza/Profiles/Sponza Noon Realtime GI/Sponza Noon Realtime GI.vrdg`

### 1.2 着色器与核心文件清单

| 模块 | 路径 | 说明 |
| --- | --- | --- |
| 玻璃主着色器 | `Shaders/Material/Glass/Glass.shader` | `VividRP/Material/Glass/OpticalGlass`，含 6 个 Pass（`VividTransparentBackface`、`VividRefraction`、`ShadowCaster`、`ReferencedPathtracingDXR`、`VividCausticsBackface`、`VividCausticsFrontface`），通过 `SubShader` `HLSLINCLUDE` 复用 `VIVIDRP_STANDARD_LIT_UNITY_PER_MATERIAL_EXTENSION` 实现 SRP Batcher 100% 兼容 |
| 彩色阴影与焦散 Pass | `Runtime/RenderPass/Core/Refraction/TransmissiveCausticsPass.cs` | 支持 Spot（透视）与 Directional（正交）最多 2 盏主光源，每光源独立 `RenderGraphRenderList` + `1024×1024` 瞬态贴图 |
| 焦散屏幕空间投影 | `Shaders/Core/Private/TransmissiveCaustics.compute` | 9-tap 平滑 PCF、高色散晶体径向 RGB 焦散色散、掠射角柔和衰减、Spot/Directional 阴影路径区分 |
| 同帧颜色金字塔与深度复制 | `Runtime/RenderPass/Core/Refraction/OpaqueColorPyramidPass.cs` | 复用 `ColorPyramid.compute`（FFX SPD）与 `HDRPHZB.compute` |
| 背面厚度与法线 Pass | `Runtime/RenderPass/Core/Refraction/TransparentBackfacePass.cs` | 输出出射面八面体法线与线性深度 |
| 前向物理折射 Pass | `Runtime/RenderPass/Core/Refraction/ForwardRefractionPass.cs` | 绑定金字塔、深度、背面数据、Clustered LightGrid、Sky IBL、CSM 阴影与运行时开关/调试模式 |
| 标定示例场景 | `Samples/GlassLookDev/GlassLookDev.unity` | Khronos 琥珀玻璃龙 + 4 联标定球（清透 / 磨砂欧泊 HG 散射 / 祖母绿体积吸收 / 高色散水晶） |
| 自动化测试 | `Tests/Editor/RenderPass/RefractionPassesTests.cs` | 覆盖 Pass 开关与释放、Shader 契约、SRP Batcher 兼容性与模板节点校验 |

---

## 2. Phase 3 留档设计（Deferred — 当前暂无 DXR 推进计划）

> **状态：留档（Archived / Deferred）**。当前聚焦光栅化与计算着色器实时管线，暂无推进 DXR 硬件光追实时玻璃的排期；以下方案作为未来扩展的设计备忘保留。

### 2.1 现有光栅化方案的物理边界

Phase 1 + Phase 2 的纯光栅化/计算方案已能高质量覆盖绝大多数实时场景，但在以下极端光学场景存在固有限制：

1. **屏幕外折射与遮挡缺失**：`OpaqueColorPyramid` 仅包含当前帧可见不透明像素，折射射线指向屏幕边缘外或被前景遮挡区域时只能回退到环境 IBL / 原 UV。
2. **多重内反射与复杂刻面全内反射（TIR）**：双面单次出射折射（Front + Projected Backface）无法精确模拟钻石/切割水晶内部的 3–6 次连续全内反射，以及“玻璃杯 + 内部液体”多层嵌套介质界面（Dielectric Priority Stack）。
3. **复杂折射焦散精度**：光源空间单层厚度 + 雅可比通量聚焦对单一凸体/雕塑效果良好，但在多层玻璃叠放、远距离长焦距聚焦或强遮挡场景下，无法替代真实折射光线追踪 / 光子映射。

### 2.2 未来 Phase 3 DXR 扩展方向

若后续启动 DXR 实时折射/焦散能力，建议沿用以下解耦架构（完全作为可选增量节点，不破坏既有光栅化路径与 SRP Batcher 兼容性）：

1. **Hybrid DXR Multi-Bounce Refraction / Reflections（RayQuery / Inline Ray Tracing 或 DXR HitGroup）**
   - 优先利用 VividRP 既有的 `RTASBuildPass` 与 `VividRTAS` 场景加速结构。
   - 对全内反射（`hitTir == true`）、屏幕外折射（`outOfBounds`）或高 IOR 宝石材质按需发射 2–4 次折射/内反射射线；普通清晰/磨砂玻璃仍可保留屏幕空间 `OpaqueColorPyramid` 快速路径，按材质标志或质量档位分流。
   - 引入 OpenPBR 介质优先级栈（Nested Dielectrics）支持玻璃容器 + 内部液体界面的正确相对折射率 $\eta_1 / \eta_2$ 计算。
2. **Ray-Traced / Photon-Guided Caustics**
   - 在具备 DXR 与时域降噪（复用现有 NRD / Reservoir 基础设施）时，可为 `TransmissiveCausticsPass` 增加可选的 DXR 焦散光子溅射或双向引导采样后端，与现有光源空间雅可比焦散共享同一屏幕合成接口。
3. **Ground-Truth 对照闭环**
   - `Glass.shader` 已内置与 `StandardLit` 共享参数集的 `ReferencedPathtracingDXR` Pass，可直接作为未来 Phase 3 实时 DXR 路径的离线参考真值基线，确保能量守恒、色散波长偏移与体积吸收系数严格一致。
4. **工程约束**
   - 新增 DXR Pass 必须通过独立 RenderGraph 节点与显式开关控制，在无硬件光追或关闭开关时保持零开销回退至 Phase 1 + Phase 2 实时链路。
   - 不修改 `Glass.shader` 现有的 `VIVIDRP_STANDARD_LIT_UNITY_PER_MATERIAL_EXTENSION` CBuffer 布局契约，确保 `GlassShader_IsSrpBatcherCompatible` 持续通过。
