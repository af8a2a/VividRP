# UE5 Lumen Specular Reflections 移植进度与 HWRT 迁移文档

返回索引：[Documentation~ 索引](README.md) · 相关文档：[RTAS 支持](AccelerationStructureSupport.md) | [SimpleSlab V1](Material/SimpleSlabV1.md) | [OpenPBROpaque V1](Material/OpenPBROpaqueV1.md) | [SER 硬件重排](NVAPIShaderExecutionReordering.md)

---

## 1. 当前进度快照

| 维度 | 当前状态 |
| --- | --- |
| **基线提交** | `05174ee`（分支 `datadriven`） |
| **参考基线（Ground Truth）** | 本地 `E:\UnrealEngine`（UE 5.7.4 Lumen Reflections） |
| **一期追踪后端** | **已完成**：Software SDF（HZB Screen Trace → Mesh SDF + Surface Cache → Global SDF + ObjectGrid + World Radiance Cache + Occluded Sky） |
| **降噪后端** | **已完成**：UE5 原生 Lumen Reflection Denoiser（BRDF Spatial Reconstruction Resolve + Virtual Hit-Point Temporal Accumulation + Variance-Guided Bilateral Spatial Filter） |
| **延迟光照合成** | **已完成**：FastSlab、DualSlab（双层）、OpenPBR 与 `Rough Specular` 平滑过渡及静态 Reflection Probe 衰减 |
| **调试视图** | **已完成**：`Overview`（中间面板）、`ReflectionView`（Mode 4）、`DedicatedReflectionRays`（Mode 7） |
| **二期 HWRT 路线** | **暂缓待推进**：等待材质系统重构稳定后启动（详见第 5 节） |

---

## 2. 一期架构与 UE5 源码对照

`LumenReflectionPass` 目前作为 `SdfDiffuseGatherPass` 内部子系统执行，直接复用 `ClosestHZB`、`MeshSdfCardGrid`、`SurfaceCache FinalLighting`、`GlobalSdfClipmap`、`ObjectGrid`、`FarRadiance` 与 `SurfaceCacheFeedback`，全部持久与瞬态资源均经由 `CameraHistory` + `CameraHistoryRenderGraphBridge` 复用（热路径零托管内存分配，不修改 `.vrdg` 资产）。

### 2.1 文件级映射表

| VividRP 文件 | UE5 参考源码 (`E:\UnrealEngine`) | 职责 |
| --- | --- | --- |
| `Shaders/Core/Public/VividLumenReflectionsCombine.hlsl` | `Engine/Shaders/Private/Lumen/LumenReflectionsCombine.ush` | `LumenCombineReflectionsAlpha`、`NeedsLumenReflections`、`ApplyLumenReflectionSmoothBias` |
| `Shaders/Core/Private/GlobalIllumination/LumenReflectionCommon.hlsl` | `LumenReflectionCommon.ush`、`LumenReflectionDenoiserCommon.ush`、`MonteCarlo.ush`、`BRDF.ush` | 公共数学/采样（VNDF GGX、STBN 蓝噪、ZOrder2D、YCoCg、Denoiser Tonemap、帧数编码）+ VividRP 材质薄适配 `ReadLumenReflectionMaterial` |
| `Shaders/Core/Private/GlobalIllumination/LumenScreenTraceHZB.hlsl` | `LumenScreenTracing.ush` | `TraceHZB` 屏幕层次深度追踪与屏幕边界渐隐 `LumenComputeHitVignetteFromScreenPos` |
| `Shaders/Core/Private/GlobalIllumination/LumenReflectionsTracing.compute` | `LumenReflections.usf`、`LumenReflectionTracing.usf` | 15 个 Kernel：Tile 分类、光线生成、HZB Screen Trace、Wave/ZOrder2D 压缩、Mesh SDF、Global SDF + Feedback |
| `Shaders/Core/Private/GlobalIllumination/LumenReflectionsDenoiser.compute` | `LumenReflectionDenoiserClear.usf`、`LumenReflectionResolve.usf`、`LumenReflectionDenoiserTemporal.usf`、`LumenReflectionDenoiserSpatial.usf`、`LumenVisualize.ush` | 10 个 Kernel：非反射 Tile 清理、BRDF 空间重建、虚像重投影时间滤波、方差引导双边空间滤波、调试视图 |
| `Runtime/RenderPass/Core/GlobalIllumination/LumenReflectionPass.cs` | `Engine/Source/Runtime/Renderer/Private/Lumen/LumenReflections.h/.cpp`、`LumenReflectionTracing.cpp` | C# 5 阶段调度、`CameraHistory` 生命周期管理、全/半分辨率 (`DownsampleFactor = 1/2`) 控制 |
| `Runtime/RenderPass/Core/GlobalIllumination/SdfDiffuseGISettings.cs` | `LumenReflections.cpp` CVars | 22 个 Volume 参数，默认值与 UE5 一致 |
| `Runtime/RenderPass/Core/DeferredLightingPass.cs` + `Shaders/Core/Public/Vivid*DeferredLighting.hlsl` | `DiffuseIndirectComposite.usf`、`ReflectionEnvironmentPixelShader.usf` | 延迟光照端 `LumenReflections` 与 `RoughSpecular` 混合及静态 Probe 衰减 |
| `Tests/Editor/RenderPass/Lighting/LumenReflectionTests.cs` | — | 默认参数、混合曲线、SmoothBias、Tile/降采样维度与零分配回归、Shader Kernel 完整性测试 |

### 2.2 管线执行流（5 阶段）

```text
Stage 1: Tile Classification
  InitReflectionIndirectArgsCS
  -> ReflectionTileClassifyCS (8x8 tile -> _RWLumenTileBitmask)
  -> ReflectionTileClassificationBuildListsCS (Resolve / Clear tile lists)
  -> [DownsampleFactor == 2] ReflectionTileClassificationBuildListsDownsampledCS

Stage 2: Ray Generation
  ReflectionClearUnusedTraceTileDataCS
  -> ReflectionGenerateRaysCS (VNDF GGX + STBN BlueNoise -> _RWRayBuffer, _RWDownsampledDepth)
  -> ReflectionClearTracesCS

Stage 3: Multi-Tier Tracing (Software SDF)
  [Screen Traces] ReflectionTraceScreenTexturesCS (HZB -> _SdfScreenPreviousRadiance)
  -> Compaction #1 (SetupCompactionIndirectArgsCS -> ReflectionCompactTracesCS -> SetupCompactedTracesIndirectArgsCS)
  -> [Mesh SDF] ReflectionTraceMeshSDFsCS / FeedbackCS (MeshSDF CardGrid + Surface Cache)
  -> Compaction #2 (仅剩余未命中光线)
  -> [Global SDF] ReflectionTraceVoxelsCS / FeedbackCS (GlobalSDF Clipmap + ObjectGrid + FarRadiance + Occluded Sky)

Stage 4: Resolve & Denoise (UE5 Native)
  LumenReflectionDenoiserClearCS (清理非反射 Tile 并写入 _RWSceneDepthHistory / _RWPreExposureHistory)
  -> LumenReflectionResolve[Downsampled][Spatial]CS (BRDF 空间重建 + 写入历史深度与预曝光)
  -> LumenReflectionDenoiserTemporal[ValidHistory]CS (DominantDir 虚像重投影 + 表面重投影回退 + YCoCg 邻域裁剪)
  -> LumenReflectionDenoiserSpatial[Bypass]CS (方差/Disocclusion 引导双边空间滤波 -> _RWSpecularIndirectAccumulated)

Stage 5: Deferred Compositing & Debug
  DeferredLightingPass 读取 _RealtimeLumenReflections (PreExposed -> * VividGetOneOverPreExposure())
  按 LumenCombineReflectionsAlpha 与 RoughSpecular 混合，并按 (1 - alpha) 衰减静态 Reflection Probe
```

---

## 3. 材质系统重构耦合边界（重构时必读）

本次实现严格遵循“核心算法与材质解耦、仅留薄适配层”的原则。后续重构材质系统时，只需关注以下 **2 处 HLSL 适配点** 和 **2 处 C# 绑定点**，其余追踪与降噪代码（Tile 分类、光线生成、Compaction、HZB、Mesh/Global SDF、Resolve/Temporal/Spatial Denoiser）**均无需改动**。

### 3.1 反射消费端材质输入（唯一读取入口）

- **文件**：`Shaders/Core/Private/GlobalIllumination/LumenReflectionCommon.hlsl`
- **入口函数**：`FLumenReflectionMaterial ReadLumenReflectionMaterial(uint2 ScreenCoord, bool bNeedDiffuseAlbedo)`
- **当前依赖的纹理与编码**：
  - `_SdfDiffuseDepth`（Reversed-Z 深度）
  - `_SdfDiffuseGBuffer0` / `_SdfDiffuseGBuffer1` / `_SdfDiffuseGBuffer4`（通过 `VividSurfaceSummary` 解码有效标记、法线、粗糙度、DiffuseAlbedo、BackfaceDiffuse/Foliage 标志）
  - `_LumenReflectionLayerAux0` / `_LumenReflectionLayerAux1`（仅在 `_LumenReflectionHasDualSlabSidecar != 0` 且处于 DualSlab 模式时用于读取顶层 Coat 粗糙度与法线）
- **C# 绑定点**：
  - `LumenReflectionPass.cs::BindMaterialInputs`（调用 `owner.BindReceiverForReflections` 并绑定 `LayerAux0/1`）
  - `SdfDiffuseGatherPass.cs::Prepare` / `Record`（透传 `m_LayerAux0`、`m_LayerAux1`、`hasDualSlabSidecar`）

**材质重构后的最小替换契约**：新材质系统只需重写 `ReadLumenReflectionMaterial` 以填充 `FLumenReflectionMaterial` 的 7 个字段即可保持整个反射与降噪管线零改动运行：

```hlsl
struct FLumenReflectionMaterial
{
    bool   bIsValid;            // 有效表面像素（非天空/背景）
    bool   bHasBackfaceDiffuse; // 植被/双面透射类材质（使用 MaxRoughnessToTraceForFoliage）
    float  DeviceZ;             // Reversed-Z 设备深度
    float  SceneDepth;          // 线性视图深度（米）
    float3 WorldNormal;         // 顶层高光着色法线（世界空间，单位向量）
    float  TopLayerRoughness;   // 顶层高光粗糙度（用于分类、GGX 采样、Resolve/Temporal/Spatial 核宽）
    float3 DiffuseAlbedo;       // 仅 DedicatedReflectionRays 调试视图灰阶底色使用
};
```

> 若新材质系统引入多高光层（如 Substrate / Coat + Base 双层独立追踪需求），可在后续扩展 `ClosureIndex` 与多层 Tile 分类，但单顶层路径可先保持此结构零侵入过渡。

### 3.2 延迟光照合成端（反射与材质 BRDF 合并）

当前耦合在 3 个延迟光照着色器与 1 个 C# Pass：
- `Runtime/RenderPass/Core/DeferredLightingPass.cs`：导入 `sdfDiffuse.SpecularReflectionHistory`，绑定 `_RealtimeLumenReflections`、`_RealtimeLumenReflectionsEnabled` 及 4 个 Combine 参数。
- `Shaders/Core/Public/VividDeferredLighting.hlsl`：`LoadVividRealtimeLumenReflections`（读取 PreExposed 反射并乘以 `VividGetOneOverPreExposure()`）与 `ApplyVividLumenReflectionsToIndirectSpecular`（计算 `LumenCombineReflectionsAlpha`、与 `RoughSpecular` 混合并衰减静态 Reflection Probe）。
- `Shaders/Core/Public/VividSimpleSlabDeferredLighting.hlsl` 与 `Shaders/Core/Public/VividOpenPBROpaqueDeferredLighting.hlsl`：在间接镜面项中调用上述辅助函数。

**材质重构时的对齐要求**：
1. 延迟光照合成端传入 `LumenCombineReflectionsAlpha` 的粗糙度必须与 `ReadLumenReflectionMaterial` 输出的 `TopLayerRoughness` 完全一致（否则在 `[MaxRoughness - FadeLength, MaxRoughness]` 过渡区间会出现分类边界跳变）。
2. `RoughSpecular` 与 `LumenReflections` 必须在同一曝光空间（线性未预曝光 Radiance）内按 `alpha` 混合，再乘以材质自身的 `PreIntegratedGF` / Specular Fresnel / BentNormal AO，同时静态 Reflection Probe 需乘以 `1 - alpha` 避免双重能量叠加。
3. 历史屏幕辐射源 `_SdfScreenPreviousRadiance`（在 `DeferredLightingPass` 的 `SdfScreenSource` 变体中写出）须继续写入当前帧 **PreExposed** 完整着色结果，以供下一帧 HZB Screen Trace 采样。

---

## 4. 一期相对 UE5 的偏差记录（Deviation Log）

便于后续材质重构与 HWRT 对齐时快速核对：

1. **单位换算（cm → m）**：Screen Trace 未命中偏置 `0.02 m`（UE5 `2 cm`）；SDF 起点偏置沿 `WorldNormal` 偏移 `_SdfDiffuseRayBias`（默认 `0.04 m` = `4 cm`）以防薄壳掠射自交；Global SDF 命中回退 `4.0 * clipmapVoxelSize`；`CardTraceEndDistanceFromCamera = max(meshDetailDistance * 2, 40 m)`。
2. **材质与 Closure 简化**：采用单顶层 `FLumenReflectionMaterial`（Surface Summary + DualSlab TopLayer），未移植 UE5 Substrate 多 Closure 打包、Hair / SingleLayerWater / Translucent 特化分支。
3. **追踪后端范围**：仅实现首期 Software SDF（复用 VividRP `VividTraceMeshSdfSceneNodes`、`VividTraceGlobalSdf`、`EvaluateMeshSdfCardHit`、`EvaluateGlobalDistanceFieldHit`），不含 Heightfield SDF、Voxel Lighting Grid、Distant Screen Traces、Volumetric Fog 采样及 HWRT。
4. **缓冲格式与历史自洽性**：`TraceHit`、`DownsampledDepth`、`SceneDepthHistory` 采用 `R32_SFloat`；由 `LumenReflectionResolveCS` 与 `LumenReflectionDenoiserClearCS` 互补写入 `_RWSceneDepthHistory` 与 `_RWPreExposureHistory[0]`，不依赖 Diffuse GI 时间滤波是否开启。
5. **虚像重投影空间**：`ComputeReflectionVirtualHitPosition` 在世界空间沿视线延伸 `ReflectionHitDistance` 后使用 `_SdfDiffusePreviousVP` 投影（替代 UE5 `TranslatedWorld` + `PrevTranslatedWorldToClip`）。

---

## 5. 待材质系统稳定后的 HWRT 推进路线图

### 5.1 为什么等待材质系统重构后再推进 HWRT

UE5 的 Lumen Reflections HWRT（`LumenReflectionHardwareRayTracing.cpp` / `LumenReflectionHardwareRayTracing.usf`）包含两条核心着色模式：
1. **Surface Cache 模式（`RAY_TRACING_PASS_DEFAULT`）**：硬件光追仅替换精确三角面求交与几何法线，命中后仍查询 Lumen Surface Cache。
2. **HitLighting 模式（`RAY_TRACING_PASS_HIT_LIGHTING`）**：在光追命中点直接评估材质 BSDF（Albedo / Roughness / Metallic / Emissive / Normal Map）、直接光 + 阴影（含透明阴影/太阳阴影）与间接光照（Surface Cache / Radiance Cache），并支持多跳镜面反射（`MaxReflectionBounces`）。

HitLighting 模式要求 RT 命中着色（ClosestHit 或 Inline Bindless Material Eval）与新材质系统的**统一材质参数布局、Bindless 纹理/缓冲布局及 BSDF 求值契约**完全一致；在材质系统重构前编写 HitLighting 材质求值会产生一次性返工。因此先以 Software SDF 冻结上游（分类/光线生成/压缩）与下游（原生 Denoiser/延迟合成/调试视图），待材质系统定型后再切入 HWRT。

### 5.2 分阶段实施规划

#### Phase H1：HWRT Surface Cache 模式（Inline `RayQuery` 优先）
- **目标**：在不依赖最终 HitLighting 材质求值的前提下，优先用硬件光追替代 Mesh SDF 追踪阶段，消除薄几何/复杂拓扑上的 SDF 漏光与法线近似误差。
- **UE5 参考**：`LumenReflectionHardwareRayTracing.usf` 中 `LUMEN_HARDWARE_INLINE_RAYTRACING` + `RAY_TRACING_PASS_DEFAULT`，以及 `FLumenReflectionHardwareRayTracingIndirectArgsCS`。
- **VividRP 集成要点**：
  - 复用现有 `RTASBuildPass` 产出的 `SceneRTAS`（参考 [AccelerationStructureSupport.md](AccelerationStructureSupport.md)），在 `SdfDiffuseGatherPass` / `LumenReflectionPass` 接入只读 RTAS 资源。
  - **零改动复用上游压缩缓冲区**：直接消费 Screen Trace 后的 `CompactedTraceTexelData` 与 `CompactedTraceTexelAllocator`，通过新增 `SetupHardwareRayTracingIndirectArgsCS` 生成 1D Indirect Dispatch。
  - 使用 Inline `RayQuery<RAY_FLAG_NONE>`（可配合 `HitGroup` / Bindless 索引查询实例化属性与 Mesh SDF Card 映射）获取精确命中距离 `HitT` 与几何法线，再调用现有 `EvaluateMeshSdfCardHit` / Surface Cache 查询着色。
  - 射线起点偏置对照 UE5 `r.Lumen.Reflections.HardwareRayTracing.Bias`（`0.1 cm -> 0.001 m`）与 `NormalBias`（`0.1 cm -> 0.001 m`）。
  - 未命中光线继续经由 Compaction 回落到 Global SDF / FarRadiance / Occluded Sky（或 Phase H3 的 FarField TLAS）。

#### Phase H2：HWRT HitLighting 模式（待新材质系统定型）
- **目标**：在反射命中点提供精确高光/纹理细节、动态自发光、镜中镜多跳反射与高精度直接光阴影。
- **UE5 参考**：`LumenReflectionHardwareRayTracing.usf`（`RAY_TRACING_PASS_HIT_LIGHTING`）与 `LumenReflectionHardwareRayTracing.cpp`（`r.Lumen.Reflections.HardwareRayTracing.HitLighting`、`BucketMaterials`）。
- **VividRP 集成要点**：
  - 基于重构后的统一材质数据契约，在 RayGen / ClosestHit（或 Inline + Bindless 材质求值）中解码命中点材质，避免为 HWRT 单独维护一套平行材质解码器。
  - 在命中点执行直接光着色（复用 Clustered Light Grid / 主方向光 + RT Inline Shadow Ray）+ 间接漫反射（Surface Cache / FarRadiance）。
  - 结合 VividRP 已有 [NVAPIShaderExecutionReordering.md](NVAPIShaderExecutionReordering.md) 或 UE5 风格 `BucketMaterials`（按材质/Shader 桶排序二次光线）缓解非相干反射射线的发散开销。
  - 扩展 `MaxReflectionBounces`（`> 1` 跳）以支持镜面互相反射。

#### Phase H3：远场、透明体与高级降噪选项
- **FarField TLAS / Distant Screen Traces**：对接远景代理几何或远场 HZB，补齐超出近场 RTAS 半径的大尺度场景反射。
- **透明体与水体反射**：基于稳定后的材质管线扩展 Front-Layer Translucency / SingleLayerWater 专用反射追踪与合成。
- **可选降噪器扩展**：保持 UE5 原生 Denoiser 为默认基线，同时为高粗糙度/动态低采样 HWRT 保留接入 NRD / DLSS-RR 的接口（复用 `LumenReflectionPass` 现有 `RayBuffer`、`TraceRadiance`、`TraceHit` 与 `Motion` 输出）。

---

## 6. 恢复工作时的验证清单

当材质重构完成并重新启动 HWRT 推进时，按以下清单逐项确认基线未回归：

1. **单元测试**：在 Unity Test Runner（EditMode）运行 `VividRP.Editor.Tests.LumenReflectionTests`，确认默认参数、混合曲线、`ApplySmoothBias`、Tile 维度与零 GC 分配回归以及所有 Compute Kernel 均通过。
2. **数值健康检查**：在 `SponzaNoon` 等基准场景检查 `LumenReflectionSpecularOutput` 全屏零 `NaN` / `Inf` / 负值，且 `Default (MaxRoughness = 0.4)` 与 `FullScene` 在 `DownsampleFactor = 1 / 2` 下数值一致。
3. **视图与一致性检查**：
   - `DedicatedReflectionRays` 视图正确高亮低粗糙度像素，且与延迟光照中 `LumenCombineReflectionsAlpha > 0` 的区域边界完全吻合。
   - `ReflectionView` 与 `Overview` 三分屏无黑边、无曝光倍增错误、室内无天空漏光。
   - 对比 `LumenReflections = ON` 与 `OFF`，静态 Reflection Probe 在低粗糙度区域正确衰减且高粗糙度区域与 `RoughSpecular` 无缝衔接。
4. **性能门禁**：遵循 `AGENTS.md` 第 4、6 节规范——确认热路径零托管内存分配，HWRT 与 Software SDF 的性能对比采用同场景、同预热条件的交错 A-B-A-B 实验协议，并使用 PIX 采集 Pass 级 GPU 耗时。
