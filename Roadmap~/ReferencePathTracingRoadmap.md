# Reference Path Tracing Roadmap

## Context

VividRP 计划在推进实时 GI 功能前，先建立一条参考路径追踪渲染路径，用于生成受控测试场景的高质量 ground-truth 图像。运行时 pass 位于：

`Runtime/RenderPass/Core/GlobalIllumination/ReferencedPathtracing/`

参考路径追踪使用已移植的 OpenPBR 作为 BSDF，并通过独立的 RenderGraph authoring asset 运行，避免与常规 raster、SSR、ReGIR、实时 GI 和 denoising 路径产生隐式依赖。

该功能的首要目标不是实时性能，而是：

- 在明确定义的材质、灯光和几何支持范围内提供无偏或可解释的参考结果。
- 输出可供 GI 验证使用的 beauty、direct、indirect 和材质 AOV。
- 提供确定性采样、可靠历史重置和线性 HDR 导出。
- 让后续 GI 实现可以通过固定场景和固定参数与 reference result 对比。

## Current Baseline

仓库已经具备以下基础设施：

- `VividRenderPipelineAsset.RenderGraphAsset` 提供单一 authored RenderGraph 入口。
- `VividRenderPipeline` 在完成 camera、volume、culling 和 FrameContext 准备后，通过 `PassRecorder` 编译和执行当前 `RenderGraphData`。
- `RenderGraphAccelerationStructure` 已作为正式 RenderGraph 资源接入。
- `RTASBuildPass` 已支持 scene renderer、ray-tracing settings、GPU-driven meshlet instance 和 RTAS build。
- `ScreenSpaceReflectionPass` 已验证 `RayTracingShader`、RTAS、material shader pass 和 `DispatchRays` 的运行路径。
- `StandardLit.shader` 已包含 `IndirectDXR` material pass。
- `IndirectDiffuse.hlsl` 已包含 hit geometry 重建、UV/normal/tangent 插值、normal map、alpha test 和 StandardLit 纹理采样逻辑。
- `BlueNoise` 已能绑定到 `RayTracingShader`。
- RenderGraph history registry 已支持按 camera 和 graph 管理历史纹理与 buffer。
- `VividLightData` 已准备 directional、punctual 和 area light 数据。

当前 OpenPBR 状态仍属于“vendor 源码已移植，renderer integration 未完成”：

- `Shaders/Material/ShaderPass/OpenPBR/OpenPBR.hlsl` 当前只包含 interop bridge，没有包含完整 `Vendor/openpbr.h`。
- `OpenPBRMaterial.cs` 尚未定义材质数据或 authoring contract。
- 尚无 Unity shader import、DXR material pass 或 GPU 数值验证覆盖。
- 默认 array LUT 模式在 Unity DXC 下的编译时间、DXIL 大小、16-bit 常量支持和运行时寄存器压力尚未验证。

因此，完整 OpenPBR 在 Unity DXR 中的可编译性是本 roadmap 的第一道技术闸门。

## Scope Definition

### Reference Path Tracing V1

V1 以静态、受控 benchmark 场景为目标，支持：

- Perspective camera。
- Static MeshRenderer 和已被当前 RTAS 正确收集的实例。
- StandardLit opaque 和 alpha-tested material。
- Base color、metalness、roughness、normal map、coat、opacity 和 emission 到 OpenPBR 的映射。
- Directional、point、spot、rectangle/area light 和 environment lighting。
- Multi-bounce transport、next-event estimation、shadow visibility、MIS 和 Russian roulette。
- FP32 progressive accumulation。
- Beauty、direct、indirect diffuse、indirect specular、emission、albedo、normal 和 depth AOV。
- Linear EXR capture 和 deterministic sampling metadata。

V1 不承诺：

- Real-time performance。
- Denoised result 作为 ground truth。
- ReGIR、screen-space lighting、probe GI 或 raster lighting 参与 reference integration。
- Animated scene 的跨帧 accumulation。
- Transparent surface、thick transmission、subsurface random walk、participating media、dispersion 或 thin-film 的完整支持。
- Terrain、particle、decal 和所有自定义 shader 自动具备 path-tracing hit shader。
- Emissive mesh importance sampling；未加入 light sampling distribution 前，emissive surface 只能通过 BSDF path 命中。

任何超出支持矩阵的场景都不能标记为完整 ground truth。输出和 capture metadata 必须记录启用的 feature、unsupported material count 和 integrator settings。

## Design Principles

- Reference graph 与常规 raster graph 分离，避免共享临时资源、pass ordering 或后处理假设。
- 第一阶段使用独立的 `VividRenderPipelineAsset` 手动指向 reference `.vrdg`，不进行 per-camera graph 热切换。
- OpenPBR `Vendor/` 目录保持原样；renderer 配置和材质映射位于 VividRP-owned bridge 中。
- 使用独立的 `ReferencePathTracingDXR` material pass，不复用 `IndirectDXR` 的 payload 或 lighting contract。
- Ray generation shader 负责 path loop，material closest-hit 负责几何/材质解析和 OpenPBR evaluation/sampling。
- 保持 DXR recursion depth 为 1；radiance ray 和 visibility ray 通过统一 payload 的 trace kind 区分。
- Direct-light sampling 必须使用原始 light data 和可验证的 PDF，不使用 ReGIR reservoir 结果。
- 默认关闭 radiance/firefly clamp；任何有偏稳定化选项都必须显式标记，且不能用于 canonical ground truth。
- Accumulation 使用 FP32，并在无法证明历史有效时保守重置。
- Reference 和 preview/denoised 输出分离；raw accumulation 始终可导出。
- 随机数 dimension layout 必须稳定、文档化，新增 feature 不应静默改变已有 sample sequence。

## RenderGraph Architecture

推荐的最小 reference graph：

```text
FrameContext camera/light preparation
    -> optional sky/environment preparation
    -> RTASBuildPass
    -> ReferencePathTracingPass
    -> ReferencePathTracingAccumulationPass
    -> optional ReferencePathTracingAOVResolvePass
    -> FinalBlitPass
```

核心资源：

- `SceneRTAS`: `RTASBuildPass` 写，`ReferencePathTracingPass` 读。
- `PathTracingSampleRadiance`: 当前 frame 的 sample mean，建议 `R32G32B32A32_SFloat`。
- `PathTracingSampleAOV*`: 可选的 per-frame direct/indirect/material AOV。
- `PathTracingAccumulationPrevious/Current`: RenderGraph history pair，使用 FP32。
- `PathTracingResolvedColor`: 供 `FinalBlitPass` 或 capture 使用的线性 HDR 输出。
- `PathTracingSampleCount`: per-camera accumulation state，可由 history buffer 或 camera-relative runtime state 管理。

`ReferencePathTracingPass` 建议派生 `UnsafePass`，原因是 DXR dispatch 需要 native command buffer ray-tracing API。该 pass 同时实现 `IBlueNoiseConsumerPass`。如果 pass 写入 RenderGraph 之外的 persistent capture/readback 状态，则还应实现 `IRenderGraphSideEffectPass`。

追踪和 accumulation 保持为两个 pass：

- Trace pass 只负责生成本帧 1～N spp 的 sample mean 和 AOV。
- Accumulation pass 负责 history、sample count、reset 和可选 variance/moment statistics。

这样可以独立验证 integrator 与 accumulation，且后续加入 denoised preview 时不会污染 raw reference output。

## Graph Selection Strategy

V1 推荐：

1. 创建独立的 `ReferencePathTracing.vrdg`。
2. 创建一份专用 `VividRenderPipelineAsset`，其现有 `RenderGraphAsset` 指向该图。
3. benchmark scene 或 quality tier 显式使用这份 pipeline asset。
4. 常规 raster pipeline asset 保持不变。

暂不推荐在 `VividAdditionalCameraData` 上增加 graph override。当前 `PassRecorder` 只维护一份 `s_CurrentGraphAsset`；graph asset 变化时会 dispose 全部 runtime pass 并重新 compile。SceneView 与 Game Camera 选择不同 graph 会导致重复 compile、pass state 重建和 history churn。

后续如果确实需要 camera-level raster/reference 并存，应先把 `PassRecorder` 的 compiled graph state 改为按 graph asset 隔离，而不是直接增加 camera dropdown。

## Integrator Architecture

### Iterative Raygen Path Loop

每个 pixel/sample 的 ray-generation 流程：

1. 使用 non-jittered camera matrices 和 sample sequence 生成 sub-pixel camera ray。
2. 初始化 throughput、radiance、previous BSDF PDF、medium state 和 RNG state。
3. 对每个 bounce：
    - 发射 radiance `TraceRay`。
    - Miss 返回 environment radiance 和 environment light PDF。
    - Closest-hit 重建 hit geometry 和 resolved material inputs。
    - Closest-hit 调用 `openpbr_prepare(...)`。
    - 累加 surface emission。
    - 生成一个 NEE light candidate，调用 `openpbr_eval(...)` 和 `openpbr_pdf(...)`。
    - 调用 `openpbr_sample(...)` 生成下一跳方向和 throughput weight。
    - Raygen 发射 visibility ray，应用 MIS 后累加 direct contribution。
    - 更新 throughput、previous PDF、sampled lobe 和 ray origin。
    - 从配置的 bounce 开始执行 Russian roulette。
4. 将 sample radiance 和 AOV 写入 per-frame output。

保持 `max_recursion_depth = 1`，避免在 closest-hit 中递归 `TraceRay`。Closest-hit 不把庞大的 `OpenPBR_PreparedBsdf` 放入 payload，只返回 path loop 所需的紧凑结果。

### Payload Contract

统一 payload 至少需要表达：

- Trace kind：radiance 或 visibility。
- Hit/miss state 和 hit distance。
- RNG state 或该 bounce 所需的随机数。
- Emission contribution。
- NEE candidate：shadow origin、direction、distance、unoccluded contribution、light PDF、BSDF PDF。
- Next path state：direction、BSDF weight、PDF、sampled lobe type、inside/outside state。
- Environment radiance 和 environment PDF。

Payload 必须控制尺寸，并通过 shader import 和 GPU capture 检查 stack/register cost。现有 `IndirectDiffuse.hlsl` 的 radiance/visibility trace-kind 设计可以参考，但不能直接复用其 payload。

### Sampling

- 使用现有 blue-noise/Sobol 基础设施，或增加独立的 Owen-scrambled Sobol helper。
- Sample index 必须由 per-camera accumulation sample count 驱动，不能直接等同于 `Time.frameCount`。
- 为 camera、light selection、light surface、BSDF lobe、BSDF direction、Russian roulette 和 wavelength sampling 分配固定 dimension range。
- `samplesPerFrame > 1` 时，每个 sample 使用连续的 global sample index。
- Canonical capture 固定 seed；interactive preview 可允许随机 seed，但需显式标记。

### Direct Lighting and MIS

V1 light sampling按以下顺序扩展：

1. Single directional light。
2. Point 和 spot light。
3. Rectangle/area light。
4. Environment importance sampling。
5. Multiple-light distribution，优先使用 power-weighted alias table。
6. Emissive mesh light distribution。

所有 light 类型必须定义：

- Sample procedure。
- Solid-angle PDF。
- Delta/non-delta classification。
- Emitted radiance units。
- Visibility ray maximum distance。
- 与 BSDF PDF 的 MIS 规则。

环境光同时通过 NEE 和 BSDF miss 可达时必须使用 MIS，避免 double counting。Emissive mesh 在尚无 light PDF 时不得同时通过 NEE 和 BSDF hit 重复计数。

## OpenPBR Integration

### Renderer-owned Bridge

保持 `Vendor/` 不变，并增加 renderer-owned 层：

```text
Shaders/Material/ShaderPass/OpenPBR/OpenPBR.hlsl
    - OpenPBR target/configuration macros
    - LUT mode selection and bindings
    - include Vendor/openpbr.h

Shaders/Material/ShaderPass/StandardLitOpenPBRAdapter.hlsl
    - hit geometry -> OpenPBR basis
    - StandardLit textures/properties -> OpenPBR_ResolvedInputs
    - alpha/opacity policy
    - feature support flags
```

第一阶段同时测试两种 LUT 模式：

- Array mode：接入简单、自包含，但可能显著增加 compile time、DXIL size 和 shader constant pressure。
- Texture mode：需要八个 LUT 的 pipeline resource、导入和 sampling macros，但更适合长期 GPU shader 集成。

最终选择以 Unity 6000.5 DXR shader 的实际编译和运行数据为准，不提前假设 array mode 一定可接受。

### StandardLit Mapping V1

V1 从现有 StandardLit 解析：

- `_BaseColor`、`_BaseMap` -> `base_color`、`geometry_opacity`。
- `_Metallic`、`_MetallicGlossMap` -> `base_metalness`。
- `_Smoothness`、roughness map/channel policy -> `specular_roughness`。
- `_BumpMap`、`_BumpScale` -> `geometry_basis.n/t/b`。
- `_ClearCoatMask`、`_ClearCoatSmoothness` -> coat weight/roughness。
- `_EmissionColor`、`_EmissionMap` -> emission color/luminance contract。
- `_ALPHATEST_ON`、`_Cutoff` -> any-hit rejection。

需要明确验证 smoothness-to-roughness 的转换。OpenPBR 期望的 `specular_roughness` 与当前 GBuffer `linearRoughness` 不能凭字段名直接等同。

OpenPBR 要求 view/light direction 和 geometry basis 位于同一坐标空间且方向均指向表面外侧。Normal mapping 后的 shading normal 必须和 geometric normal 保持一致半球；ray offset 使用 oriented geometric normal，而不是直接使用 shading normal。

### Material Shader Pass

StandardLit 增加独立 pass：

```text
Name      "ReferencePathTracingDXR"
LightMode "ReferencePathTracingDXR"
```

该 pass 包含：

- Reference path-tracing payload contract。
- Closest-hit：material resolve、OpenPBR prepare/eval/sample。
- Any-hit：alpha-test，visibility trace 可 early accept。
- 与现有 StandardLit local shader keywords 一致的 variant set。

Terrain、SimpleLit、Unlit、particle 和自定义 shader 在增加对应 DXR pass 前视为 unsupported，并在 capture metadata 中报告。

## Accumulation and History Invalidation

Accumulation建议使用 running mean：

```text
mean(n + k) = mean(n) + (frameMean - mean(n)) * k / (n + k)
```

其中 `n` 是已有 sample count，`k` 是本帧 samples per pixel。Accumulation texture 使用 FP32；display/resolve texture 可以使用 FP16。

历史必须在以下条件变化时重置：

- Camera view、projection、FOV、orthographic mode 或 viewport。
- Render resolution、dynamic-resolution scale 或 target texture。
- Integrator seed、max bounce、RR depth、SPP、light sampling mode 或 feature flags。
- OpenPBR mapping 或 LUT mode。
- Light transform、shape、color、intensity 或 enable state。
- Sky/environment texture、rotation、intensity 或 exposure。
- Geometry transform、mesh、renderer enable/layer 或 RTAS instance set。
- Material property、keyword、texture 或 MaterialPropertyBlock。
- RenderGraph import version。
- Explicit Reset Accumulation request。

V1 可以要求 scene frozen，并提供手动 Reset，但仍必须自动覆盖 camera、resolution、integrator settings 和 graph changes。无法可靠检测的动态场景变化应显示 warning，而不是继续声称历史有效。

Edit Mode 下，只要 reference path tracing active 且 sample count 未达到 target，pipeline 必须继续请求 editor repaint。达到 target 后停止自动 repaint，避免持续占用 GPU。

## AOV and Capture Contract

Canonical output：

- `BeautyLinearHDR`
- `DirectDiffuse`
- `DirectSpecular`
- `IndirectDiffuse`
- `IndirectSpecular`
- `Emission`
- `PrimaryAlbedo`
- `PrimaryNormalWS`
- `PrimaryDepth`
- `SampleCount`

OpenPBR 的 `OpenPBR_DiffuseSpecular` 可以帮助生成 diffuse/specular split，但复杂 lobe path 的分类并非总是唯一。AOV decomposition 规则必须写入文档并保持版本化。

Ground-truth capture 使用线性 EXR，不经过 display tonemapping、auto exposure、film grain、vignette、bloom 或 denoising。可额外导出 display PNG 供人工查看，但 PNG 不是数值比较源。

每次 capture 同时记录 metadata：

- Scene、camera 和 graph asset identity。
- Resolution、SPP、seed、bounce count 和 RR depth。
- OpenPBR version、LUT mode 和 enabled features。
- Light sampling mode 和 environment sampling mode。
- Supported/unsupported renderer/material counts。
- GPU、graphics API、Unity/VividRP version。
- Clamp、denoiser 或其他 biased option 状态。

## Proposed Files

### Runtime passes

```text
Runtime/RenderPass/Core/GlobalIllumination/ReferencedPathtracing/
    ReferencePathTracingPass.cs
    ReferencePathTracingAccumulationPass.cs
    ReferencePathTracingAOVResolvePass.cs          # optional
    ReferencePathTracingCapturePass.cs             # optional/readback stage
```

### Runtime settings and state

```text
Runtime/RenderPipeline/
    ReferencePathTracingSettingsVolume.cs

Runtime/RenderGraph/FrameContext/
    VividReferencePathTracingData.cs
```

### Shaders

```text
Shaders/Core/Private/GlobalIllumination/ReferencedPathTracing/
    ReferencePathTracing.raytrace
    ReferencePathTracingCommon.hlsl
    ReferencePathTracingSampling.hlsl
    ReferencePathTracingLighting.hlsl
    ReferencePathTracingAccumulation.compute

Shaders/Material/ShaderPass/
    ReferencePathTracing.hlsl
    StandardLitOpenPBRAdapter.hlsl
```

### Pipeline resources

Add the ray-tracing shader, accumulation compute shader and optional OpenPBR LUT textures through the existing `VividRPCoreResources`/pipeline resource recollection workflow. Do not manually edit `Runtime/Resources/PipelineResources.asset` entries.

### Tests

```text
Tests/Editor/RenderPass/GlobalIllumination/ReferencedPathTracing/
    ReferencePathTracingPassTests.cs
    ReferencePathTracingAccumulationTests.cs
    ReferencePathTracingSettingsVolumeTests.cs
    StandardLitOpenPBRAdapterTests.cs

Tests/Runtime/RenderPass/GlobalIllumination/ReferencedPathTracing/
    ReferencePathTracingGraphicsTests.cs
```

Runtime GPU correctness无法用 EditMode API 可靠覆盖时，应建立 `Tests/Runtime/`/PlayMode assembly，而不是只增加 shader source text assertions。

## Milestone 0: OpenPBR DXR Feasibility Gate

### Goal

证明完整 OpenPBR API 能在 Unity 6000.5 的 DXR material/ray-generation shader 环境中编译和运行，并确定 LUT 策略。

### Scope

- 完成 renderer-owned OpenPBR bridge。
- 创建最小 `.raytrace` 和 StandardLit test hit pass。
- 实际调用 `openpbr_prepare`、`openpbr_eval`、`openpbr_sample` 和 `openpbr_pdf`，不能只验证 include。
- 测试 array LUT 和 texture LUT。
- 收集 shader import time、错误/警告、DXIL size、payload size、GPU stack/register 行为。
- 使用默认 diffuse 和 rough dielectric sphere 验证输出不含 NaN/Inf。

### Acceptance

- Unity shader importer 无错误。
- DX12/DXR 能完成 primary hit 和一次 OpenPBR sample。
- `eval/pdf/sample` 返回有限值，invalid sample 通过 `pdf == 0` 正确终止。
- 明确选定 V1 LUT mode，并记录选择原因。
- 若 OpenPBR 无法在当前 DXC 路径稳定编译，暂停后续 milestone，优先修复 interop/feature specialization，而不是临时替换 BSDF。

## Milestone 1: Minimal Reference RenderGraph

### Goal

建立完全独立、可见的 primary-ray reference rendering path。

### Scope

- 创建独立 `ReferencePathTracing.vrdg` 和专用 pipeline asset。
- Graph 包含 RTAS build、reference trace、accumulation/resolve 和 final blit。
- `ReferencePathTracingPass` 读取 `SceneRTAS` 并 dispatch full-resolution rays。
- 实现 camera ray、opaque closest-hit、alpha any-hit 和 environment/constant miss。
- 使用独立 `ReferencePathTracingDXR` material pass。
- 暂时允许只输出 albedo/normal/emission，确保 geometry/material routing 正确。

### Acceptance

- StandardLit opaque 和 alpha-tested mesh 可见。
- Camera orientation、Y flip、FOV、aspect 和 render texture path 正确。
- Front/back-face、double-sided flag 和 geometric normal orientation 可调试。
- RTAS unavailable、shader missing 或 unsupported graphics API 时显示清晰 fallback/warning。
- 常规 raster graph 不受影响。

## Milestone 2: OpenPBR Material V1

### Goal

让 StandardLit 的核心参数通过 OpenPBR 进行 BSDF evaluation 和 sampling。

### Scope

- 提炼 reusable hit geometry 和 StandardLit texture sampling helper。
- 实现 StandardLit-to-OpenPBR adapter。
- 支持 base、metal、roughness、normal、coat、opacity 和 emission。
- 加入 shading-normal correction 和 geometric-normal ray offset。
- 固定 material feature specialization 策略，避免无控制的 variant/code explosion。

### Acceptance

- Diffuse、rough dielectric、smooth dielectric、metal 和 coat sphere 输出合理。
- White furnace scene 不出现明显能量增长或系统性变暗。
- Normal map 不产生背面采样、self-intersection 或大面积黑斑。
- Alpha cutout 与 raster silhouette 一致。
- StandardLit property/keyword 变化会使 accumulation 失效。

## Milestone 3: Unbiased Multi-bounce Lighting

### Goal

完成可用于静态 GI ground-truth 的 path integrator。

### Scope

- Iterative multi-bounce path loop。
- Throughput update 和 finite-value guard。
- Configurable max bounce 和 Russian roulette start depth。
- Directional、point、spot、area light sampling。
- Shadow visibility rays。
- BSDF/light MIS。
- Environment evaluation 和 importance sampling。
- Multiple-light selection distribution。
- Direct、indirect 和 emission AOV。

### Acceptance

- Cornell Box 能随 SPP 提升稳定收敛。
- Direct-light-only、BSDF-only 和 MIS 三种 debug mode 可对照。
- Delta lights 和 non-delta lights 不发生错误 MIS。
- Environment 不 double count。
- 单灯采样频率与声明 PDF 一致。
- Russian roulette 开启前后的高 SPP 均值在统计误差内一致。

## Milestone 4: Progressive Accumulation and Capture

### Goal

把 integrator 变成可重复、可导出的 reference baseline 工具。

### Scope

- FP32 RenderGraph history accumulation。
- Per-camera sample count 和 target SPP。
- Automatic/editor repaint until convergence。
- Camera、resolution、settings、light、scene 和 material invalidation。
- Manual Reset Accumulation。
- Raw linear EXR 和 display PNG capture。
- Capture metadata sidecar。
- Optional first/second moment or variance AOV。

### Acceptance

- 固定 scene/seed/settings 时输出可复现。
- Camera 或材质轻微变化不会混合旧历史。
- 达到 target SPP 后停止自动 repaint。
- EXR 不包含 tonemapping、auto exposure 或 post-processing。
- Capture metadata 足以离线重现 integrator configuration。

## Milestone 5: Validation Corpus and GI Baseline

### Goal

建立后续 GI 开发可重复使用的场景、图像和验收规则。

### Required Scenes

- White furnace material sphere matrix。
- Cornell Box diffuse multi-bounce。
- Roughness/metalness/coat sphere grid。
- Directional、point、spot 和 area-light matrix。
- HDRI environment rotation/intensity scene。
- Alpha-tested foliage card scene。
- Normal-map grazing-angle scene。
- Mixed direct/indirect interior scene。
- High-dynamic-range emissive stress scene。

### Validation Rules

- Canonical references 使用固定 seed、resolution、SPP 和 integrator version。
- Monte Carlo output 使用统计容差，不要求跨 GPU bit-identical。
- 比较 linear HDR/AOV，不以 tonemapped PNG 作为唯一依据。
- GI implementation 优先比较 indirect diffuse/specular AOV，再比较 beauty。
- 每次 BSDF、light PDF、sampling dimension 或 accumulation contract 变化都更新 reference version。

### Acceptance

- 每个场景都有支持矩阵、参数和 expected behavior 文档。
- 可批量生成 reference EXR 和 metadata。
- 至少有一套自动 image metric 和一套人工 debug AOV workflow。
- 后续 GI feature PR 可以明确引用对应 reference scene 和误差结果。

## Milestone 6: Extended Material and Scene Coverage

### Goal

在 V1 baseline 稳定后扩展完整 OpenPBR 和复杂场景支持。

### Candidate Scope

- Skinned and animated geometry 的单帧 capture/reset policy。
- GPU-driven instance material parity。
- Terrain、SimpleLit、Unlit 和 custom material hit shaders。
- Transparent/thin-walled transmission。
- Thick transmission 和 medium boundary tracking。
- Homogeneous volume、subsurface random walk。
- Dispersion 和 stochastic wavelength sampling。
- Thin-film 和 fuzz。
- Emissive mesh extraction、triangle alias table 和 MIS。
- Depth of field、physical aperture 和 motion blur。
- Optional unbiased spectral/RGB wavelength workflow。

这些能力不应阻塞 V1 GI baseline，但每一项启用前必须增加独立 validation scene 和 capture metadata flag。

## Test Strategy

### CPU/EditMode behavior tests

- Settings sanitization 和 defaults。
- Graph/pass resource fields、access flags 和 node ports。
- Accumulation formula 和 sample-count progression。
- History signature/hash 对 camera、resolution、settings 和 scene version 的响应。
- Light selection distribution 和 PDF helper。
- StandardLit property 到 OpenPBR resolved input 的 CPU mirror rules（适用时）。
- Unsupported feature reporting。

### Shader import and GPU tests

- OpenPBR full API shader import。
- Material keyword variants。
- Ray payload compatibility。
- NaN/Inf detection buffer。
- White furnace energy test。
- BSDF sample/eval/pdf consistency。
- Light sampler histogram/PDF consistency。
- Fixed-seed image capture。

避免新增只读取 shader 源码并断言字符串存在的测试。能通过 importer、runtime API、GPU buffer 或 image result 验证的行为，应使用对应行为测试。

## Main Risks

### OpenPBR shader complexity

完整 uber-BSDF 可能带来较长 shader import、较大 DXIL、较高寄存器和 stack pressure。Mitigation：P0 测试 LUT 模式、feature specialization 和 material variant，保持 Vendor 源码不变。

### Material parity

Raster StandardLit 与 OpenPBR 并非同一 BRDF。最终 beauty 差异可能同时包含 GI 和 material-model 差异。Mitigation：固定 mapping contract，输出 indirect AOV，并对 benchmark material 使用明确的 OpenPBR-supported subset。

### Incomplete hit-shader coverage

目前只有 StandardLit 有 DXR material pass。Mitigation：V1 限定支持范围，收集 unsupported renderer/material count，禁止静默使用错误 fallback material。

### History contamination

场景变化检测不完整会产生看似收敛但错误的图像。Mitigation：V1 强制 frozen-scene workflow、保守 reset、manual reset 和 metadata；之后接入可靠 scene/material versioning。

### Incorrect light PDF or units

错误 PDF、delta classification 或 light units 会造成系统性 bias。Mitigation：每种 light 单独建立 histogram、analytic 和 image test，不直接复用实时 clustered/ReGIR sampling 结果。

### Graph switching cost

当前 `PassRecorder` 切 graph 会 dispose/compile runtime passes。Mitigation：V1 使用独立 pipeline asset，不做 per-camera override。

### Misleading ground-truth label

有限 SPP、有限 bounce 和 unsupported feature 都会限制参考质量。Mitigation：所有 capture 包含完整 metadata，并将 canonical baseline 限定在明确的 V1 support matrix 内。

## Definition of Done for GI Baseline V1

只有满足以下条件，Reference Path Tracing V1 才可作为推进 GI 的正式基线：

- 独立 `.vrdg` 和独立 pipeline asset，不依赖 raster GI/SSR/ReGIR output。
- StandardLit opaque/alpha-tested OpenPBR mapping 稳定。
- Multi-bounce、NEE、visibility、MIS 和 Russian roulette 完成。
- Directional、point、spot、area 和 environment lighting 通过验证。
- FP32 deterministic accumulation 和可靠 history reset。
- Beauty、direct、indirect diffuse/specular、emission 和 primary material AOV。
- Raw linear EXR 和 metadata capture。
- White furnace、Cornell Box、material sphere、light matrix 和 alpha-test regression scenes。
- Unsupported feature/renderer/material 可见且可统计。
- Canonical output 无 denoiser、无 ReGIR、默认无 radiance clamp。
- 测试和文档明确区分 V1 ground truth 与尚未支持的 transmission/volume/emissive-mesh feature。

完成 V1 后，实时 GI 功能应以这些 reference scene 的 indirect AOV 为主要质量基准，并将 performance、temporal stability 和 bias 分开评估。
