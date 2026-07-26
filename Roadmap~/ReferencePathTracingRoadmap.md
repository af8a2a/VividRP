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

### 2026-07-21 V1 Implementation Checkpoint

当前已从最小 Lambert 闭环推进到 OpenPBR 单主光源路径积分器原型：

- `OpenPBR.hlsl` 已包含完整 `Vendor/openpbr.h`，V1 暂选自包含的 array LUT 路径。
- 增加 Unity HLSL/DXC renderer-owned interop：处理 GLSL-style vector splat、OpenPBR struct factory、32-bit LUT element fallback，以及 legacy HLSL wrapper nominal-type tag；`Vendor/` 的窄幅可移植性补丁包括对应 hook，以及一处 HLSL struct-return 三目表达式修正。
- `StandardLitOpenPBRAdapter.hlsl` 已映射 base color、metalness、smoothness/roughness、normal map、clear coat、opacity/alpha test 和 emission；transmission、subsurface、fuzz、dispersion 与 thin film 保持关闭。
- Closest-hit 已实际调用 `openpbr_prepare`、`openpbr_eval`、`openpbr_sample` 和 `openpbr_pdf`，并在 geometric-normal hemisphere 与 finite-value guard 后返回下一跳状态。
- Raygen 已实现 iterative 4-bounce path loop、每次命中的主方向光 NEE、阴影 visibility ray、throughput 更新，以及从第 3 次反弹开始的 Russian roulette；DXR recursion depth 仍为 1。
- 方向光属于 delta light，当前单灯阶段使用离散选择 PDF 1，不施加与 BSDF PDF 的 MIS 权重。ReGIR 数据不参与 radiance integration。
- 独立 DXC SM 6.6 动态材质编译闸门已覆盖完整 `prepare/eval/sample/pdf` 调用并通过；本机约 3.5 秒、DXIL 约 220 KiB。仍需在 Unity shader importer 与实际 DX12/DXR 画面中完成最终 variant/runtime 验证。

该 checkpoint 仍是单帧、每像素 1 sample 的原型：尚无 progressive accumulation、sample-index 驱动的低差异序列、环境光、其他灯型、AOV 拆分或 EXR capture，因此还不能标记为正式 ground truth V1。

## Scope Definition

### Reference Path Tracing V1

V1 以静态、受控 benchmark 场景为目标，支持：

- Perspective camera。
- Static MeshRenderer 和已被当前 RTAS 正确收集的实例。
- StandardLit opaque 和 alpha-tested material。
- Base color、metalness、roughness、normal map、coat、opacity 和 emission 到 OpenPBR 的映射。
- Directional、point、spot、rectangle/area light 和 HDRI Sky environment lighting。
- Multi-bounce transport、next-event estimation、shadow visibility、MIS 和 Russian roulette。
- FP32 progressive accumulation。
- Beauty、direct、indirect diffuse、indirect specular、emission、albedo、normal 和 depth AOV。
- Linear EXR capture 和 deterministic sampling metadata。

V1 不承诺：

- Real-time performance。
- Denoised result 作为 ground truth。
- 未经 RIS correction 的 ReGIR reservoir、screen-space lighting、probe GI 或 raster lighting
  参与 reference integration。
- Animated scene 的跨帧 accumulation。
- Transparent surface、thick transmission、subsurface random walk、participating media、dispersion 或 thin-film 的完整支持。
- Physically Based Sky、Reference Atmosphere、volumetric cloud 或其他天空参与介质路径；V1 的环境光范围限定为 HDRI Sky。
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
- Direct-light sampling 必须使用原始 light data 和可验证的 PDF。ReGIR 只能作为 proposal：
  必须使用 reservoir correction weight，并在 cell/reservoir 无效时提供具有完整 support 的 fallback；
  不能把 reservoir 内容直接当作 radiance。
- 默认关闭 radiance/firefly clamp；任何有偏稳定化选项都必须显式标记，且不能用于 canonical ground truth。
- Accumulation 使用 FP32，并在无法证明历史有效时保守重置。
- Reference 和 preview/denoised 输出分离；raw accumulation 始终可导出。
- 随机数 dimension layout 必须稳定、文档化，新增 feature 不应静默改变已有 sample sequence。
- V1 环境光遵循 HDRP 的职责分离：相机背景与参与路径积分的环境辐亮度共享同一天空语义，
  但可以使用不同分辨率的表示、可见性和更新频率。
- HDRI Sky 的 scene-linear radiance、sampling distribution 和 accumulation history 不包含 auto exposure
  或 pre-exposure；曝光只作用于 presentation。

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

## Environment Lighting Roadmap

环境光分为两个明确阶段。阶段一参考 HDRP，以 HDRI Sky 完成可验证的无限远环境光积分，并属于
Reference Path Tracing V1 的阻塞项。阶段二参考 Unreal Path Tracer，将大气作为参与介质进行路径追踪，
属于 V1 完成后的长期目标。

阶段二不得与阶段一并行推进。只有 `E0`～`E6` 的 acceptance 全部满足，并且 HDRI canonical corpus
已经冻结版本后，才能启动 `A0`。这样可以先用离散环境贴图验证 miss、NEE、PDF、MIS、曝光和历史失效，
避免把基础积分器错误与大气体积传输错误混在一起。

### Phase 1: HDRP-style HDRI Sky

#### E0: Environment Contract and RenderGraph Wiring

**Goal**

建立独立于具体天空实现的环境光数据契约，并将其接入 reference RenderGraph。

**Scope**

- 从 `VividSkyData`/`SkyManager` 消费当前 HDRI cubemap、rotation、tint、scene-linear intensity 和 sky hash。
- 明确区分 `cameraVisible`、`lightingEnabled` 和 `importanceSamplingEnabled`。
- 为路径追踪绑定 lighting cubemap、camera background、sampling data 和 fallback black environment。
- 在 `ReferencePathTracingSettingsVolume` 中增加环境光开关与 debug sampling mode。
- 环境状态缺失时使用黑色无限远环境，不隐式回退到 raster color buffer。

**Acceptance**

- HDRI 可以只参与照明、只作为相机背景、同时启用或全部关闭。
- Reference graph 不读取 Deferred Lighting、SSR、probe GI 或 raster sky color 作为环境积分结果。
- 缺失 cubemap、无效资源和禁用 Sky 时不会输出 NaN/Inf。
- 环境 texture、rotation、tint、intensity 或 enable state 进入 accumulation signature。

**Implementation checkpoint (2026-07-25)**

- 已增加 `ReferencedPathTracingEnvironmentState`，统一解析 `VividSkyData`、HDRI 有效性、scene-linear
  intensity、rotation、tint、sky hash，以及 lighting/camera/sampling 三类独立开关。
- 已增加 `ReferencedPathTracingSettingsVolume`，支持环境照明、相机可见性，以及
  BSDF-only / importance sampling / uniform-sphere 调试采样模式。
- `ReferencedPathTracingPass` 已通过显式 RenderGraph cube 资源消费 `SkyManager` 的 HDRI
  specular cubemap；缺失、禁用或非 HDRI Sky 时导入 Sky 子系统的黑色 fallback cubemap。
- 环境纹理和契约参数已在 dispatch 前同时绑定为 global 与 ray-tracing 参数，供 material
  closest-hit 和 raygen/miss 路径共享。
- 环境契约 signature 已接入无限累积历史判定；Sky 或环境 Volume 状态改变会重置累积。
- E0 到此只建立资源与状态契约。实际 camera/BSDF miss 采样留在 E1，重要性分布纹理及 PDF
  留在 E2。

#### E1: Camera Background and BSDF Miss Evaluation

**Goal**

完成最小 HDRI Sky 可见性，让 primary ray 和间接 BSDF ray 使用正确的环境辐亮度。

**Scope**

- Primary miss 输出相机可见天空或 camera clear color，并维护正确 alpha。
- 间接 miss 按世界空间方向、HDRI rotation、tint 和物理强度采样 lighting cubemap。
- 允许 camera background 使用单独的全分辨率 raster sky，lighting 使用较低分辨率 cubemap。
- 所有环境值保持未曝光的 scene-linear radiance；presentation 阶段再应用 VividRP pre-exposure。
- 增加 environment-only、primary-background-only 和 indirect-miss debug mode。

**Acceptance**

- 旋转 HDRI 后，背景和反射/间接照明方向一致。
- `cameraVisible = false` 时相机可以输出 clear color，但表面仍能接受环境照明。
- 关闭 lighting environment 时，背景可见但不贡献路径能量。
- Primary background 不施加 BSDF/light MIS 权重。
- 恒定环境下 diffuse white sphere 的高 SPP 均值符合 Lambert/OpenPBR 解析预期。

**Implementation checkpoint (2026-07-25)**

- Primary miss 已按 camera clear flags 在 HDRI Sky 与 scene-linear camera clear color 之间选择；
  Sky background 输出 alpha 1，clear color 保留 camera alpha，表面命中保持 alpha 1。
- Secondary 及后续 BSDF miss 已按世界空间射线方向采样 lighting cubemap，并应用与相机背景共享的
  HDRI rotation、tint 和物理强度。OpenPBR throughput 已包含 `f * cos(theta) / pdf`，因此 miss
  不再重复除以 BSDF PDF；E2 之前也不施加 environment light PDF 或 MIS 权重。
- Environment radiance、clear color 和累积 history 均保持未曝光 scene-linear；VividRP
  pre-exposure 仍只在 presentation/resolve 阶段应用。
- 已增加 Combined、Environment Only、Primary Background Only 和 Indirect Miss Only 调试输出。
  debug mode、camera clear flags、scene-linear clear color 以及 E0 环境契约都进入 accumulation
  signature，相关状态变化会清空历史。
- Primary background 与 indirect miss 按相同方向旋转约定读取 cubemap；`cameraVisible` 与
  `lightingEnabled` 可独立关闭，缺失 HDRI 时相机回退 clear color、间接路径回退黑环境。
- 当前 camera background 与 lighting 共用 `SkyManager` cubemap 的 mip 0。单独的全分辨率
  raster-sky background 仍保留为可选资源扩展，不允许回退读取已经合成场景几何的 raster color。
- C# runtime 与 EditMode test assembly 已通过编译；恒定环境 white-sphere 的高 SPP GPU
  解析验收仍需在 canonical validation scene 中完成。

#### E2: Equiareal Importance Distribution

**Goal**

建立 HDRP 风格、可评估 PDF 的环境重要性采样数据。

**Scope**

- 将 cubemap 按 equiareal spherical mapping 投影为二维重要性域。
- 使用 scene-linear luminance 构建 per-row conditional CDF 和 marginal CDF。
- 保存环境积分归一化因子，并提供 `SampleEnvironment()` 与 `EvaluateEnvironmentPdf()`。
- 采样表只在 sky hash、rotation、tint、intensity 或采样配置变化时重建。
- 提供关闭重要性采样后的 uniform sphere/debug fallback。

**Acceptance**

- Equiareal 参数化下不额外乘 `sin(theta)`。
- `SampleEnvironment()` 返回的 solid-angle PDF 与任意方向的 `EvaluateEnvironmentPdf()` 一致。
- Constant、single-bright-texel、high-contrast HDRI 的采样 histogram 与声明 PDF 在统计容差内一致。
- 全黑环境返回零 radiance/零可采样能量，并安全终止 NEE。
- HDRI intensity 的纯全局缩放不会改变归一化后的方向分布。

**Implementation checkpoint (2026-07-25)**

- 已增加独立 `ReferencedPathTracingEnvironmentSamplingPass`，从 `SkyManager` 导入当前 HDRI
  lighting cubemap，并构建固定 128×64 等面积域的 conditional/marginal CDF。
- metadata、64 项 marginal CDF 和 64×128 项 per-row conditional CDF 打包在一个持久化
  `EnvironmentImportanceDistribution` structured buffer 中；RenderGraph 只需将该输出连接到
  `ReferencedPathTracingPass` 的同名输入即可建立构建到 ray dispatch 的显式依赖。
- 分布权重使用已应用 rotation、tint 和 scene-linear physical intensity 的 HDRI luminance；
  等面积映射的 Jacobian 为常数，因此构建阶段不额外乘 `sin(theta)`。
- buffer metadata 保存 `meanLuminance`、`1 / (4 * PI * meanLuminance)`、valid flag 和 layout
  version。全黑环境生成可安全二分搜索的 uniform CDF，但 valid 和 normalization 为 0，后续
  NEE 会直接判定没有可采样能量，不产生除零或 NaN。
- shader common contract 已提供世界空间 `direction <-> equiareal UV`、CDF 二分采样、
  `ReferencedPathtracingSampleEnvironment()` 和
  `ReferencedPathtracingEvaluateEnvironmentPdf()`。Importance 与 Uniform Sphere 返回
  solid-angle PDF，BSDF Only 返回 0。
- 分布使用独立 `samplingSignature` 缓存，仅追踪 HDRI identity/hash、rotation、tint、
  intensity、lighting state 和 sampling mode；camera visibility 与 resolved debug mode
  不会触发无关重建。
- 未连接分布输入时，path-tracing pass 绑定全零 fallback buffer，importance sampling
  安全失效而不会读取未初始化资源。
- Runtime、Editor Tests 程序集以及 Unity ray-tracing/compute shader import 已通过。
  Constant、single-bright-texel 和 high-contrast HDRI 的 GPU histogram 回归仍需加入
  canonical validation corpus；该 proposal 已在 E3 接入路径能量。

#### E3: Environment NEE and Visibility

**Goal**

把 HDRI Sky 作为 non-delta infinite light 接入 surface next-event estimation。

**Scope**

- 在每次有效 surface interaction 中允许选择 environment light。
- 完整 light PDF 为 `p(select environment) * p_environment(omega)`。
- NEE 使用 OpenPBR `eval/pdf`，并向采样方向投射 `TMax = infinity` 的 alpha-aware visibility ray。
- 环境光参加 multiple-light distribution，但保持与 ReGIR local-light proposal 的 correction contract 分离。
- 输出 environment direct diffuse/specular debug AOV。

**Acceptance**

- 遮挡物能正确阻挡 HDRI direct lighting，alpha-tested 几何遵循当前 visibility policy。
- Environment-only 场景在 NEE 开启后显著降低亮区 HDRI 的收敛方差。
- Light selection histogram、conditional environment PDF 和最终 combined PDF 一致。
- 禁用 ReGIR 时环境结果不变；启用 ReGIR local-light proposal 时环境仍具有完整 support。

**Implementation checkpoint (2026-07-25)**

> Historical E3 checkpoint. Its independent environment/ReGIR proposal layout was superseded by
> Phase 4.2 Unified NEE Candidate on 2026-07-26.

- 每次 surface interaction 为 environment NEE 分配独立二维随机流，不复用或推进
  OpenPBR BSDF、ReGIR light/shape 以及后续 bounce 的既有 RNG 序列。`BSDF Only`
  关闭 NEE；Importance 与 Uniform Sphere 模式启用相同的 visibility/integration 路径。
- shader common contract 新增 `SampleEnvironmentLight()` 与
  `EvaluateEnvironmentLightPdf()`。环境当前作为独立 proposal family，每个 hit 固定生成
  一个 candidate，因此 `p(select environment) = 1`；该离散因子仍显式包含在 combined
  light PDF 中，后续统一 light selector 可以改变选择概率而不改变 estimator 接口。
- StandardLit closest-hit 使用 `openpbr_eval()` 拆分 diffuse/specular，并调用
  `openpbr_pdf()` 保存 competing BSDF PDF。返回给 raygen 的未遮挡贡献为
  `Le * f * abs(NdotL) / (p_select * p_environment)`，所有 PDF 都使用 solid-angle measure。
- Raygen 对有效 candidate 发射 `RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH |
  RAY_FLAG_SKIP_CLOSEST_HIT_SHADER` visibility ray，`TMax` 使用 `FLT_MAX` 表示无限远。
  any-hit 仍然执行，因此 StandardLit alpha-test 会 `IgnoreHit()` 并继续搜索后续遮挡物。
- Environment NEE 在每个 bounce 都参与积分：primary hit 保留 OpenPBR diffuse/specular
  拆分并写入 `EnvironmentDirectDiffuse` 与 `EnvironmentDirectSpecular` 两个 FP32
  RenderGraph direct debug AOV，后续 bounce 按 primary lobe 归类到 beauty/REBLUR signal。
  Environment Only debug resolve 同时包含 background、BSDF miss 和所有 bounce 的
  environment NEE。
- Environment proposal 与 ReGIR local-light proposal 独立生成，ReGIR 开关不会改变
  environment 的随机维度、选择 PDF 或 support。
- Runtime 与 Editor Tests 程序集编译通过；Unity 6000.7 DX12 已重新导入
  `ReferencedPathtracing.raytrace` 和 StandardLit material shader，未出现 E3 Shader error。
- E3 按里程碑边界暂时保留 BSDF miss 权重 1，也尚未对 light-sampled candidate 应用
  power heuristic，因此 Combined 输出会同时包含两个 environment estimator。E4 必须完成
  双向 MIS/no-double-counting gate 后，结果才能重新标记为 canonical ground truth。
- Alpha-tested blocker、bright-texel HDRI variance、light-selection histogram 与
  ReGIR on/off 图像回归仍需加入 GPU validation corpus。

#### E4: Bidirectional MIS and No-double-counting Gate

**Goal**

统一 environment NEE 与 BSDF miss 的估计器，消除 double counting 并保持无偏。

**Scope**

- Light-sampled environment 使用 power heuristic：
  `w_light = PowerHeuristic(p_light, p_bsdf)`。
- BSDF-sampled miss 使用相同的 combined environment light PDF：
  `w_bsdf = PowerHeuristic(p_bsdf, p_light)`。
- Delta BSDF event 命中环境时不错误应用不可达的 competing-technique PDF。
- 关闭 environment importance sampling 时，移除 environment NEE，BSDF miss 权重固定为 1。
- 增加 light-only、BSDF-only、MIS debug modes。

**Acceptance**

- Light-only、BSDF-only 和 MIS 在高 SPP 下均值位于统计误差内。
- Rough diffuse、rough specular、near-mirror 和 metal sphere 均不出现系统性增亮或变暗。
- Environment NEE 开关不会造成约两倍亮度的 double counting。
- Combined light-pick probability 改变后均值保持稳定。
- 通过 HDRI rotation/intensity matrix 和 bright-sun HDRI regression。

**Implementation checkpoint (2026-07-25)**

- shader common contract 已加入数值稳定的 power heuristic。实现先用两侧 PDF 的最大值
  归一化再平方，避免极尖锐 glossy PDF 溢出；light-sampled estimator 使用
  `PowerHeuristic(p_light, p_bsdf)`，其中 `p_light` 仍是包含离散环境选择概率的
  combined solid-angle PDF。
- Raygen 现在跨 bounce 保存 OpenPBR 返回的 combined BSDF PDF 和 sampled-lobe delta
  标志。BSDF miss 重新评估同一方向的 combined environment light PDF，并使用
  `PowerHeuristic(p_bsdf, p_light)`；throughput 已包含 `f * cos(theta) / p_bsdf`，
  因此 miss 只乘 MIS 权重，不重复除 PDF。
- OpenPBR vendor contract 已确认 `OpenPBR_BsdfLobeTypeSpecular` 表示 singular/delta
  event，而 `Glossy` 仍是可与 environment NEE 竞争的连续分布。Delta miss 固定保留
  权重 1，不向其应用不可达的连续 light PDF。
- `ReferencedPathTracingSettingsVolume` 新增 `Environment Estimator Mode`：
  `MIS` 为默认 production 路径；`Light Only` 关闭非 delta BSDF miss；
  `BSDF Only` 从 CPU state gate 关闭 environment NEE，并令 miss 权重为 1。
  Light Only 仍保留 delta miss；当 NEE 被 sampling mode 关闭或目标方向的 light PDF
  为 0/无效时，也回退 BSDF estimator，确保 proposal 无 support 时不丢能量。
- Estimator mode 进入完整 environment signature，因此切换模式会正确重置 progressive
  accumulation；它不进入 `samplingSignature`，所以 Light Only/BSDF Only/MIS 对照不会
  无谓重建同一 HDRI importance distribution。
- C# state/default tests 已覆盖 MIS 默认值、各 estimator 对 NEE gate 的影响，以及
  estimator 切换只失效 accumulation 而不失效 distribution cache。Editor Tests 项目
  编译通过，Unity 6000.7 DX12 已重新编译 ray-tracing shader 与 StandardLit shader，
  未出现 E4 shader error。
- E4 的积分器闭环已完成；high-SPP 三估计器均值、rough/near-mirror/metal corpus、
  bright-sun HDRI 与 combined light-pick probability 图像统计回归仍需在 canonical
  GPU validation corpus 中执行后，才能关闭全部 acceptance。

#### E5: Background/Lighting Resolution, Cache and Exposure Integration

**Goal**

完成可长期使用的 HDRI environment 生命周期与 presentation 契约。

**Scope**

- 按 HDRP 模式允许高分辨率 camera background 与较低分辨率 lighting cubemap 分离。
- 使用 sky content hash 缓存 environment CDF/marginal 数据。
- 明确 cubemap 内容、rotation、tint、intensity、sampling mode 与 exposure 变化的失效边界。
- Raw path radiance、REBLUR input、FP32 accumulation 和 capture 保持未曝光；仅 resolve/final blit 使用 pre-exposure。
- 在 capture metadata 中记录 environment asset identity、hash、rotation、intensity、sampling mode 和 PDF version。

**Acceptance**

- Auto exposure 变化不会重建 environment distribution 或清空 raw accumulation。
- HDRI 内容或物理强度变化会重建必要资源并清空 accumulation。
- 相机分辨率变化只重建 background target，不无条件重建 lighting distribution。
- 重复使用未变化 HDRI 时不发生逐帧 CDF dispatch 或资源分配。
- Raw EXR 数值不随 display exposure 改变。

**Implementation Checkpoint (2026-07-25)**

- `SkySpecularCache` 已拆分 raw source cubemap 与 GGX lighting cubemap 两条句柄。
  `ReferencedPathTracingPass` 的 primary camera miss 从
  `PathTracingEnvironmentBackground` 采样原始 HDRI；BSDF miss、NEE 和
  `ReferencedPathTracingEnvironmentSamplingPass` 继续使用
  `PathTracingEnvironment`。Camera background 由 raygen 逐像素输出，天然跟随相机目标
  分辨率，不需要额外维护 HDRP 式 2D background copy。
- Lighting cubemap 采用独立 reflection resolution：Low/Default/High/Ultra 分别为
  128/256/512/1024，并限制不超过源 HDRI 分辨率。GGX convolution 的 mip 0 会按方向
  重采样 source，因此可以安全地生成较低分辨率 lighting cache；源 HDRI 仍保留完整
  分辨率用于 camera presentation。
- `VividSkyData.skyContentHash` 与 `SkyManager.GetSkyTextureContentHash` 已建立 content
  contract，覆盖 `imageContentsHash`、尺寸、mip、graphics format 与 texture
  dimension。Specular cache key 使用 source identity、content hash 和 lighting
  resolution；HDRI rotation/intensity 不再触发无意义的 GGX reconvolution。
- 完整 environment signature 继续负责 progressive accumulation invalidation，并覆盖
  content、asset identity、rotation、tint、physical intensity、background/lighting
  resolution 和 estimator state。CDF `samplingSignature` 仅覆盖会改变 directional
  radiance/PDF 的 content、asset identity、rotation、tint、physical intensity、
  lighting resolution 与 sampling mode；camera visibility、debug/estimator mode、
  background resolution 和 display exposure 不进入 CDF key。
- `ReferencedPathTracingEnvironmentMetadata` 已预留 runtime-compatible capture contract，
  记录 asset name/identity、sky/content hash、两类分辨率、rotation、physical
  intensity、sampling/estimator mode 与 PDF version。`rawRadianceIsPreExposed=false`
  固化 raw capture 的 scene-linear 语义；未来 capture pass 可在 Editor 侧追加 asset
  GUID，而不改变 runtime contract。
- Exposure 路径维持 E5 要求：PT sample、REBLUR input 与 FP32 history 均为 raw
  scene-linear radiance；accumulation/REBLUR resolve 才输出 pre-exposed presentation，
  final blit 处理 current/pre-exposure ratio。`VividExposureData` 明确不参与 raw
  accumulation signature，因此 auto exposure 变化不会清空 history 或重建 CDF。
- `VividRP.Runtime.csproj` 编译通过（0 error）；Unity 6000.7 当前 Editor 会话的 Tundra
  build 成功生成 `VividRP.Editor.Tests.dll`，并成功重新导入 ray-tracing shader 与
  environment sampling compute，未出现本次相关 shader error。由于 Editor 正在运行，
  按仓库约束未启动 batch EditMode Tests。Canonical raw EXR exposure 对照、camera
  resize 与逐帧 allocation/dispatch GPU 验证仍留给 E6 corpus。

#### E6: HDRI Validation and V1 Freeze Gate


**Goal**

冻结阶段一接口、验证场景和 canonical reference，作为启动 Reference Atmosphere 的前置条件。

**Required Scenes**

- Constant white/gray environment furnace。
- HDRI rotation、tint 和 intensity matrix。
- Bright localized emitter HDRI。
- Diffuse/roughness/metalness/coat sphere grid。
- Interior doorway/environment occlusion scene。
- Alpha-tested foliage environment shadow scene。
- Camera-hidden but lighting-enabled environment scene。

**Acceptance**

- E0～E5 的 CPU/API、shader import、PDF histogram 和 GPU image tests 全部通过。
- HDRI reference capture 包含固定 seed、SPP、bounce、environment hash 和 sampling metadata。
- Canonical raw result 不依赖 denoiser、pre-exposure、ReGIR reservoir 随机状态或 raster GI。
- HDRI environment 的接口、PDF version 和 AOV decomposition 被标记为 V1 frozen。
- 完成 E6 后，Milestone 3 的 environment lighting 条目才视为完成，并允许启动 Phase 2。

**Implementation Checkpoint (2026-07-25)**

- `ReferencedPathTracingSettingsVolume` 已增加 canonical integrator contract：
  deterministic sampling、fixed seed、max bounce、Russian roulette start bounce、
  ReGIR gate 与 target SPP。普通交互模式默认行为保持不变；canonical 模式使用
  accumulation-relative sample index，并在 camera/scene/light/environment/integrator
  signature 变化时从 sample 0 重新开始。
- `ReferencedPathTracingPass` 已将 fixed seed 显式混入 shader RNG。Canonical 模式强制
  关闭 REBLUR checkerboard；`enableReGIR=false` 同时关闭 raygen 的 reservoir 输入消费，
  并将 ReGIR 状态排除出 raw accumulation signature，确保 HDRI reference 不依赖外部
  reservoir 随机序列。
- 已建立 `ReferencedPathTracingV1Corpus` version 1，冻结 7 个 case ID：
  constant furnace、rotation/tint/intensity、bright emitter、OpenPBR sphere grid、
  interior occlusion、alpha foliage shadow、camera-hidden lighting。V1 capture profile 固定为
  512×512、2048 SPP、seed `0x13579B`、4 bounce、RR depth 3、Importance Sampling + MIS。
- `ReferencedPathTracingV1FreezeGate` 将 contract-ready 与 frozen 分离：
  - contract-ready 要求 raw/unexposed、StandardLit-only、0 unsupported material、
    无主光/局部光、无 ReGIR/denoiser/raster GI，并校验 HDRI content/PDF/version、
    camera visibility、分辨率、SPP、seed 和 integrator signature；
  - frozen 还要求 GPU evidence 为 Passed、EXR SHA-256 有效、finite pixel ratio 为 1、
    negative radiance ratio 为 0、relative mean error 不超过 2%；
  - `ValidateCorpus` 只有在 7 个 case 均存在唯一且通过的 capture 时才返回 true。
- 新增 terminal `ReferencedPathTracingCapturePass`。其
  `PathTracingAccumulationRaw` 输入必须连接 accumulation pass 的 FP32 current-history
  输出；它仅在 exact target SPP 发起 async GPU readback，使用 float ZIP EXR，并同步
  写出 JSON metadata 与 EXR SHA-256。Capture API 不覆盖已有文件：

  ```csharp
  ReferencedPathTracingCapture.Request(
      camera,
      "hdri-constant-furnace",
      exrPath,
      out var failure);
  ```

- Capture 生成的 evidence 初始状态刻意为 `NotRun`。GPU comparator 填入
  finite/negative/relative-mean metrics 并标记 `Passed` 前，freeze gate 保持关闭；
  这避免仅凭一次成功 readback 就错误宣布 V1 frozen。
- Runtime 与 Editor Tests 项目已通过定向编译；新增测试覆盖 integrator signature、
  sample sequence reset、raw capture pass 资源契约、7-case corpus completeness 以及
  ReGIR/pre-exposure/camera-visibility 的拒绝路径。当前 Unity Editor 会话运行中，因此
  未启动 batch EditMode/GPU graphics tests。

**Remaining Freeze Evidence**

- 在 package 外的 validation project 中创建并固定 7 个 scene/camera/HDRI asset。
- 将 `ReferencedPathTracingAccumulationCurrent` 显式接入
  `ReferencedPathTracingCapturePass.PathTracingAccumulationRaw`，逐 case 生成 EXR/JSON。
- 对 constant furnace、三 estimator 均值、bright-emitter MIS、OpenPBR grid 和
  visibility corpus 运行 DX12 GPU comparator，写回 metrics/status。
- 只有 `ReferencedPathTracingV1FreezeGate.ValidateCorpus(...)` 返回 true 后，才更新本节为
  V1 frozen 并启动 Reference Atmosphere；当前状态是 **freeze infrastructure complete,
  GPU evidence pending**。

### Phase 2: Unreal-style Reference Atmosphere (Long-term)

Reference Atmosphere 不再把天空当作一张无限远辐亮度贴图，而是把球形大气当作参与介质。天空颜色来自
太阳方向光在 Rayleigh/Mie 介质中的透射与散射，外层空间本身可以保持黑色。该模式应与 HDRI skydome
互斥，避免重复环境能量；混合 HDRI 与大气的艺术模式必须在 reference 模式之外单独定义。

#### A0: Architecture and Mode Isolation

- 增加 `HDRI` 与 `ReferenceAtmosphere` environment mode。
- Reference Atmosphere 激活时禁用 HDRI infinite-light NEE 和 BSDF miss emission。
- 定义 planet center、bottom/top radius、ground albedo、Rayleigh/Mie/ozone 参数和太阳灯引用。
- 为 atmosphere/cloud/ground holdout 与 camera visibility 预留独立标志。

**Gate:** 只有 E6 完成后才能开始；模式切换必须清空 accumulation 并写入 capture metadata。

#### A1: Spherical Atmosphere Intersection and Transmittance

- 使用高精度 camera-relative planet/atmosphere sphere intersection。
- 建立 atmosphere interval 和可选 planet-ground virtual hit。
- 实现 Rayleigh、Mie、ozone extinction/scattering density。
- 构建 optical-depth LUT 加速解析 segment transmittance，并保留 reference ray-marched 对照模式。

**Acceptance:** LUT 与高采样数 reference transmittance 在高度/天顶角测试矩阵中一致。

#### A2: Participating-medium Path Integration

- 在 path loop 中加入 atmosphere interval、free-flight/delta tracking 和 phase-function sampling。
- 支持 Rayleigh phase 与 Mie Henyey-Greenstein phase。
- 体积 scatter event 参加 throughput、Russian roulette、AOV 和 max-depth contract。
- Camera、surface、shadow 和 BSDF miss ray 都应用一致的大气透射。

**Acceptance:** 无太阳时大气无自发光；启用太阳后天空辐亮度随密度、相函数和观察高度合理变化。

#### A3: Sun, Ground and Atmosphere MIS

- 使用 VividRP directional light 的物理 illuminance/角直径契约表示太阳。
- 支持从 atmosphere scatter event 对太阳执行 NEE 和 shadow visibility。
- 明确太阳盘 camera visibility、directional delta/finite-angle classification 与 MIS。
- Planet ground 使用 atmosphere ground albedo，并能参与对大气的多次散射贡献。

**Acceptance:** 不与 HDRI 太阳盘或额外 skydome double count；太阳、天空和地面能量单位一致。

#### A4: Reference Volumetric Clouds

- 在 atmosphere 稳定后再加入 cloud shell intersection、density majorant 和 callable/material sampling 边界。
- 支持 cloud transmittance、single scattering、可配置 multiple-scattering approximation 和 cloud shadow。
- 建立 acceleration map/LUT，且任何近似都必须在 metadata 中显式标记。

**Acceptance:** 关闭 clouds 时与 A3 完全一致；cloud shadow 对 surface 和 atmosphere scatter 使用同一透射契约。

#### A5: Reference Atmosphere Validation and Optimization

- 与高采样数 CPU/offline reference 或已验证的 Unreal reference scene 对照。
- 覆盖海平面、高空、太空视角、日出/正午、ground on/off、cloud on/off。
- 分离无偏 reference mode 与 analytic/LUT/multiple-scattering approximation mode。
- 完成 GPU timeout、max ray-march steps、NaN/Inf 和极端尺度测试。

Reference Atmosphere 在 A0～A5 完成前不进入 GI Baseline V1 的 Definition of Done，也不能替代 HDRI
canonical corpus。它完成后应形成独立的 `Reference Atmosphere V2` baseline/version。

## OpenPBR Integration

### Renderer-owned Bridge

保持 `Vendor/` 的 BSDF 算法与接口语义不变；Unity HLSL 所需的 scalar-to-vector 构造 hook、legacy wrapper type tag 和 struct-return 修正作为可审计的窄幅移植补丁，其余配置与材质映射放在 renderer-owned 层：

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

当前 StandardLit V1 已明确采用 `specular_roughness = 1 - perceptualSmoothness`。该值直接作为
NRD 的 linear roughness 与 DLSS-RR 的 `sqrt(alphaRoughness)`；计算 specular albedo 的环境 BRDF
时再使用 `alphaRoughness = specular_roughness²`，避免在不同接口之间重复平方或开平方。

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
- 按 Environment Lighting Roadmap 完成 `E0`～`E6`：HDRI miss、importance sampling、NEE、
  visibility、双向 MIS、cache/exposure contract 和 validation freeze。
- Multiple-light selection distribution。
- Direct、indirect 和 emission AOV。

### Acceptance

- Cornell Box 能随 SPP 提升稳定收敛。
- Direct-light-only、BSDF-only 和 MIS 三种 debug mode 可对照。
- Delta lights 和 non-delta lights 不发生错误 MIS。
- HDRI environment 通过 E4 no-double-counting gate，并完成 E6 V1 freeze。
- 单灯采样频率与声明 PDF 一致。
- Russian roulette 开启前后的高 SPP 均值在统计误差内一致。

### Phase 4.1 checkpoint: Reference Light List (2026-07-26)

- 新增独立 `ReferencedPathTracingLightListPass`。它直接消费
  `VividLightRenderDatabase.sceneLightData` 的完整 active scene snapshot，不依赖 camera
  visible-light 列表，也不继承 ReGIR 的 collection box，因此 reference inventory 不随相机位置裁剪。
- V1 GPU record 覆盖 directional、point、spot、rectangle、tube 与 disc，并为未来的
  environment/emissive-triangle 类型保留稳定枚举值。位置、正交基、range、shape、spot attenuation、
  barn door、rendering layers、shadow strength 和源灯光的 scene-linear physical color 均显式入表。
- `EntityId` 被拆成 64-bit stable key 并用于确定性排序；缺少或重复 stable ID 的
  external/particle light 暂不进入 canonical list，而是在 parameters 中累计
  `unstableLightCount`。不支持或无效的 active light 通过 `unsupportedLightCount` 暴露，
  避免静默改变 reference scene。
- 每条记录保存 power-proxy selection weight、归一化 selection PDF 与单调 CDF。rectangle/disc 使用
  area measure，tube 使用 line measure；delta/continuous、infinite、one-sided、BSDF-reachable 和
  shadow capability 由 flags 固化。Phase 4.1 当时只有显式有限方向光模型标记为
  BSDF-reachable；解析面积灯的合成 BSDF segment 相交由 Phase 4.3 补齐。
- 参数 buffer 保存 light/active/rejected 数量、总权重、逆总权重、distribution/version 以及对排序后
  完整记录计算的 64-bit deterministic signature。空场景、全黑灯光和未连接 RenderGraph 输入都绑定
  versioned zero fallback，不读取未初始化 GPU 资源。
- `ReferencedPathTracingPass` 已声明并绑定 `ReferenceLightList` 与
  `ReferenceLightListParameters` 输入；Phase 4.1 只建立 inventory/distribution contract。

### Phase 4.2 checkpoint: Unified NEE Candidate (2026-07-26)

- 每个 shading vertex 现在只生成一个 `ReferencedPathtracingNEECandidate`。解析灯总权重与
  `4π × HDRI average luminance` 组成同一个离散选择域；环境、方向光、point、spot、rectangle、
  tube 与 disc 不再分别运行独立 NEE estimator。
- candidate 对齐 RTXPT `PathLightSample` 约定，保存 `incidentRadianceOverPdf`、方向、距离、
  stable light index/type/flags、shadow strength，并把离散 `selectionPdf` 与条件
  `solidAnglePdf` 分开保留。payload 使用同一布局，未来引入 emissive triangle、light tree 或
  proposal mixture 时无需再次迁移 payload。
- closest-hit 只负责采样 candidate 与计算 OpenPBR diffuse/specular response；raygen 统一负责
  shadow visibility、delta/BSDF-reachable gate、power-heuristic MIS 和 AOV 路由。环境 miss 的
  BSDF-side PDF 也乘相同的全局 environment selection PDF，避免 light/BSDF 两侧使用不同 proposal。
- finite directional 的 BSDF 反向策略遍历 Reference Light List，并使用
  `selectionPdf × conditional solid-angle PDF` 参与 MIS。Phase 4.2 的 rectangle/disc
  仍是 light-only，后续由 Phase 4.3 的解析 segment intersection 开启反向策略。
- Reference PT 已移除对 ReGIR light/grid/reservoir RenderGraph 输入的依赖。为兼容已序列化 Volume，
  `enableReGIR` 字段暂时保留，但运行时只作为本地解析灯 NEE 开关；积累失效签名改用完整
  Reference Light List signature，不再受 ReGIR collection volume 或相机位置影响。

### Phase 4.3 checkpoint: BSDF-segment Light Evaluation (2026-07-26)

- 新增 `ReferencedPathtracingSegmentLightHit` 和解析 light tracing contract。raygen 对
  OpenPBR 采样出的 BSDF 方向遍历 `BSDF_REACHABLE` 灯光，计算沿该方向的虚拟 emitter
  intersection、未遮挡 radiance、命中距离以及与 NEE 完全一致的条件 solid-angle PDF。
- finite directional 继续使用圆盘方向域；rectangle 使用与 NEE 相同的 shading-point
  barn-door clipping、ray-plane intersection 和面积到立体角 Jacobian；disc 使用 one-sided
  ray-plane/disc intersection 与 `distance² / (cosTheta × area)` PDF。解析灯无需把 display mesh
  加入 RTAS，visibility 仍由 alpha-aware shadow ray 和逐灯 shadow strength 决定。
- rectangle/disc 现在标记为 `BSDF_REACHABLE`。其 BSDF estimator 使用
  `PowerHeuristic(bsdfPdf, selectionPdf × solidAnglePdf)`；light-sampled candidate 使用相反顺序，
  两侧共享 Phase 4.2 的全局光源选择域。关闭本地灯 NEE 时 selection PDF 为 0，BSDF 命中自动以
  权重 1 保留完整 support。
- point/spot 仍是 delta 事件，tube 仍是 line-measure 零面积事件，因此不会被连续 BSDF 方向命中；
  它们继续只通过 NEE 积分。environment 仍只在实际 BSDF miss 时求值，避免在场景几何之前错误加入
  无限远辐亮度。
- primary BSDF segment 命中的 area light 按采样 lobe 路由到 diffuse/specular REBLUR signal，
  使用解析 emitter hit distance；finite directional 继续保留 FP32 direct AOV 与 finite-sun
  denoiser copy。

### Phase 4.4 checkpoint: Light Transport Conformance (2026-07-26)

- 原 `environmentEstimatorMode` 序列化字段保留以兼容已有 Volume Profile，但语义扩展为全局
  transport estimator。`MIS`、`Light Only` 与 `BSDF Only` 现在统一控制解析灯 NEE、
  BSDF-segment light evaluation 和 environment NEE/miss；integrator contract 升级为 V3，
  estimator mode 进入积累失效签名与 capture metadata。debug mode 后续已迁移到
  Rendering Debugger，不再参与积累签名。
- BSDF-only 模式会从 unified candidate distribution 中剔除 `BSDF_REACHABLE` emitter，
  因而 segment/miss 侧观察到 selection PDF 为 0 并以权重 1 保留完整 support。point、spot、
  tube、delta directional 等 BSDF 不可达或 singular 事件仍保留 NEE，Light-only 模式也保留
  delta BSDF 的唯一可达路径。
- 新增 raw transport debug views：首个 primary NEE 的 selection/solid-angle/BSDF PDF、
  light-side MIS weight、stable light identity，以及首个 primary BSDF-segment emitter 的对应
  PDF 与 BSDF-side MIS weight。Invalid Sample Mask 分离标记 NEE、segment 和最终 path 的
  NaN/Inf/negative-radiance 异常。debug view 由 Rendering Debugger 管理并写入独立
  `DebugTexture`；resolved output 和物理 lighting AOV 始终保持 Combined。
- 新增 CPU estimator-policy mirror 和 `ReferencedPathTracingTransportConformanceGate`。
  conformance evidence 必须同时提供三 estimator 的高 SPP 均值/standard error、light-selection
  histogram 与 sample/evaluate PDF reciprocity；V1 frozen capture contract 升级为 V2 并要求
  conformance evidence 为 Passed，且所有 transport/environment debug mode 为 Combined。
- 代码与 EditMode contract 已建立；实际 DX12 GPU corpus 仍需填充 estimator measurements、
  histogram 和 PDF comparison evidence，未执行前不得将 V1 freeze 标记为完成。

### Phase 4.5 checkpoint: Shading-point-aware Light Selection (2026-07-26)

- unified source selector 现在混合两套离散 proposal：
  `p_select = α × p_global + (1 - α) × p_local`。`p_global` 继续使用稳定 Reference Light
  List 的 power proxy；`p_local` 在每个 shading vertex 根据位置、几何法线、距离/range window、
  spot cone、单面 area emitter 朝向以及 tube radial factor 重排解析灯。HDRI 在 local proposal
  中仍保留，并继续由自身的方向重要性分布负责条件采样。
- 默认 `α = 0.25`，并强制至少 0.05 的 global support floor。关闭
  `Shading Point Light Selection` 时退化为原全局 distribution；局部 proposal 无有效支持时也自动
  fallback 到全局 proposal。该策略只改变方差，不裁剪任何原 proposal 可达的光源。
- 每个 shading vertex 只构建一次 `ReferencedPathtracingLightSelectionContext`，缓存 global/local
  analytic、environment 与 total weight。NEE 从混合 proposal 采样；BSDF segment 遍历复用同一
  context 直接求每个 emitter 的完整 mixture selection PDF；environment miss 使用上一 vertex 的
  同一 context。因此解析 emitter 路径保持 O(N)，不会因逐灯重复归一化退化为 O(N²)。
- light-sampled candidate、BSDF-segment emitter 和 environment miss 都使用完全相同的
  mixture selection PDF，并再乘各自的条件 solid-angle PDF 参与 Phase 4.4 MIS。现有 payload
  已独立保存 selection/conditional PDF，本阶段没有改变 ray payload 布局。
- 新开关和 global proposal probability 已进入 integrator V4 签名、shader binding、capture
  metadata 与 V1 freeze contract V3。transport conformance evidence 新增 global-only 与
  shading-point proposal 的高 SPP 均值、standard error 和 variance；冻结门禁要求两者均值在统计
  容差内一致，但 variance 只记录不作为 correctness hard gate。
- CPU contract 与 shader importer 检查已建立；实际 DX12 light-matrix/interior GPU capture 仍需
  填充 proposal on/off evidence。只有均值一致且局部 proposal 显示预期方差收益后，才把 4.5
  标记为 GPU-validated。

### Phase 4.6 checkpoint: Reference Light Spatial Index (2026-07-26)

- Reference Light List builder 现在基于每个 finite light 的 conservative influence AABB 构建
  camera-independent 三轴投影空间索引。XY、XZ、YZ cell 分别保存稳定 light index；shading
  point 查询三个投影并选择未溢出且候选最少的轴。directional 和其他无法建立有限 influence
  bound 的记录进入独立 unbounded 集合。
- 索引采用固定 `32 × 32 × 3` cell、每 cell 64 个候选。area/tube 的 shape extent 与
  range window 一起扩张 influence bound；自定义 range attenuation 会反解其最远非零距离，
  避免只使用 `Light.range` 造成 emitter 漏检。cell index 按 Reference Light List 的 stable
  order 写入，因此同一 light signature 生成完全一致的 buffer。
- 为保持 `.vrdg` 与 payload ABI 不变，空间 header、cell header 和 packed light indices
  追加在现有 `ReferenceLightListParameters` structured buffer 尾部；首个 48-byte block 仍保留
  light-list parameters ABI。light-list contract 升级为 V2，ray payload 没有变化。
- Phase 4.5 selection context 现在缓存 unbounded range、spatial candidate range、选中的轴和
  fallback flags。local proposal 只在该完整候选域内归一化；global NEE 命中候选域外灯时，
  local PDF 分量精确为 0，最终仍由 global support floor 保持完整支持。sample/evaluate 和
  BSDF-side MIS 通过相同的 stable-index membership query 观察同一个 mixture PDF。
- BSDF-segment analytic emitter traversal 与 NEE 复用同一 context。任一完整投影可用时只遍历
  unbounded + cell candidates；三个投影同时 overflow、索引关闭或 header 无效时回退完整
  Reference Light List。shading point 位于所有 finite influence bounds 之外时只遍历 unbounded
  lights，不会把容量截断作为 correctness authority。
- 新增 `Light Spatial Index` Volume 开关与 transport debug view。debug RGB 分别输出 traversal
  candidate count、selected axis + 1 和 context flags（indexed/fallback/outside/overflow）。
  transport/environment debug 选项现由 Rendering Debugger 的 `Reference Path Tracing`
  foldout 管理，并统一输出到 pass-owned `DebugTexture` RenderGraph port；切换 debug view
  不再修改 Volume、主路径追踪输出或 accumulation signature。debug state 与 integrator/
  environment state 解耦后 integrator contract 升级为 V6，V1 freeze contract 升级为 V5；
  capture metadata 继续固定记录 Combined，明确 raw radiance 未经过 debug 替换。
  capture metadata 仍记录 spatial-index version、resolution 和 cell capacity。
- `VividRP.Runtime.csproj` 与 `VividRP.Editor.Tests.csproj` 的 .NET 编译已通过，light-list HLSL
  也通过 DXC `lib_6_6` 编译；新增 deterministic packing、stable-order 和 overflow behavior
  tests。实际 Unity shader import、DX12 debug view 和 spatial-index OFF/ON 高 SPP 均值对比
  仍是 GPU validation gate。

### Phase 4.8 checkpoint: Reference Sampling Conformance (2026-07-26)

- 路径采样从依赖控制流消费顺序的 PCG stream 改为随机访问接口
  `(pixel, sampleIndex, dimension, seed)`。默认使用 renderer 已有的 256-SPP
  Owen-Sobol blue-noise set；缺少 keyword 或资源时显式回退 Indexed Hash，而不是在同一
  shader variant 中保留未绑定 BND 资源。canonical 与 interactive 路径都由 per-camera
  accumulation-relative sequence 提供 sample index，不再直接用渲染帧号作为 global sample。
- sample dimension ABI 固定为 film 0-1、lens 2-3、camera reserve 4-7，以及每 bounce
  16 个维度：BSDF 0-2、NEE 3-5、Russian roulette 6、stochastic alpha 7、
  volume 8-11、future 12-15。当前最多 8 bounce，最高维度 135，保持在 256 维
  Owen-Sobol 资源容量内；控制流跳过某一 estimator 不会再改变后续 bounce 的随机数。
- 超过 256 SPP 时按 256 样本分块。每个 block 使用确定性的 pixel/dimension permutation
  和 Cranley-Patterson rotation，避免 255→256、511→512 边界复用同一 BND lookup；该策略保留块内
  BND 分层与任意 sample index 随机访问，但不宣称跨 block 的全局 Sobol discrepancy 保证。
- 实际生效的 BND/Hash 模式与 sampling contract version 已进入 integrator/frame/
  accumulation signature 和 capture metadata。BND 资源失效导致的 Hash fallback 会使历史
  自动失效；V1 canonical freeze gate 只接受 contract V1 的 Indexed BND capture。
- Rendering Debugger 新增 `Path Samples` transport view，通过 pass-owned `DebugTexture`
  输出 film dimensions 0/1 与 bounce-zero roulette dimension，主 radiance、payload 和
  MIS PDF 布局保持不变。Phase 4.7A emissive geometry inventory 按当前决策暂缓；
  emissive mesh 仍只在路径命中时计入，不进入 NEE。

## Milestone 4: Progressive Accumulation and Capture

### Goal

把 integrator 变成可重复、可导出的 reference baseline 工具。

### Current implementation status (2026-07-24)

- 已实现独立 `ReferencedPathTracingAccumulationPass`，通过 RenderGraph history pair 保存
  `R32G32B32A32_SFloat` 的逐像素算术均值。
- `PTGraph.vrdg` 的当前交互预览路径已切换为
  `RTAS -> {ReferencedPathTracingPass, RaytracingGBufferPass} ->
  ReferencedPathTracingReblurPass -> FinalBlitPass`。无限累积和 OIDN pass 仍保留为可选的
  reference/capture 支线，但不再与 REBLUR 同时执行，避免重复维护两套时域历史。
- 累积采用 `mean_n = mean_(n-1) + (sample_n - mean_(n-1)) / n`，没有固定 history window、
  重投影、邻域裁剪或亮度 clamp，保持静态 reference accumulation 的无偏性质。
- 样本计数按 camera 隔离；分辨率、view/projection matrix、主方向光方向或颜色变化时自动从 1 spp 重置。
- ray generation 已把帧序号混入 RNG，并加入逐帧 sub-pixel jitter，避免重复累积完全相同的路径样本。
- 已通过 `com.unity.rendering.denoising` 的 `CommandBufferDenoiser` 接入 Intel Open Image Denoise color-only
  preview。OIDN 使用异步 readback/CPU worker，不在 RenderGraph pass 内 flush 或 submit command buffer；结果未就绪、
  package 宏不可用、非 64 位桌面平台或 native backend 不支持时，输出稳定回退到 raw accumulation。
- OIDN 结果按 camera 隔离；分辨率、view/projection matrix 或主方向光变化时废弃旧请求结果。
- package API 被隔离在 `IReferencedPathTracingDenoiserBackend` 适配边界之后；Unity 6.7+ 切换为预编译 package
  时不需要把实现迁入 VividRP，只需维护 adapter 和程序集引用。
- 已参考 `E:\NRD-Sample_simplex`（NRD v4.16，revision
  `a805a0d2f9464f41790f4ad6ea952cc8fbf47917`）接入 shader-side
  `REBLUR_DIFFUSE_SPECULAR`。当前调度序列为 ClassifyTiles、PrePass、TemporalAccumulation、
  HistoryFix、Blur、PostBlur、TemporalStabilization，并可在 ClassifyTiles 与 PrePass 之间选择执行
  3x3/5x5 hit-distance reconstruction；当 `maxStabilizedFrameNum` 为 0 时使用无稳定化的
  PostBlur permutation 并跳过 TemporalStabilization。
- `ReferencedPathTracingPass` 现在输出 NRD front-end 约定的 diffuse/specular
  `RGBA16F radiance + normalized hit distance`（linear-RGB packing），并将 emission 单独保留，最终 resolve
  时再合成，避免 emission 进入时域滤波。
- 2026-07-23 的正确性修正进一步把 primary-bounce 主方向光 NEE 拆为
  `PathTracingDirectLighting`：确定性的硬阴影不再进入 REBLUR 的空间滤波，只在降噪完成后与
  indirect diffuse/specular 和 emission 合成；secondary-bounce NEE 仍属于需要降噪的随机路径信号。
- 2026-07-25 的有限太阳圆盘阶段继续保留 FP32 `PathTracingDirectLighting` 作为 canonical raw direct
  AOV，但会按 OpenPBR lobe 将 finite-angle 主光的 primary diffuse/specular 副本写入 REBLUR signal。
  该分流遵循 RTXPT stable-plane NEE 契约：太阳使用远光源 hitT，并与已有路径信号按辐射亮度合成
  source distance；路径追踪 pass 通过 frame context 把实际分流决策交给 REBLUR resolve，避免独立
  重判造成 raw direct 与 denoised direct 重复叠加。零角直径 delta 主光仍沿用未滤波 direct 路径。
  这样 raw beauty/累积保持无偏且 AOV 语义不变，交互预览的随机软阴影可以参与时域与空间收敛。
- REBLUR signal 使用统一的 linear-RGB 契约。有限精度的中间滤波或 history 一旦裁掉 YCoCg 的负色度，
  高饱和红/蓝局部光会解码成错误色相；path tracer producer、NRD permutation 与 resolve 现在共享同一
  encoding 开关，避免输入、history 和输出对 signal 颜色空间产生分歧。
- 2026-07-26 修正 punctual range 交叠处的单 candidate 黑带：light-list v3 标记局部 proposal 无法
  完整覆盖贡献支持域的 rectangle/disc/tube 灯。仅含 directional/point/spot 的场景会使用支持域精确
  的 shading-point proposal，不再强制混入可能选中范围外零贡献 punctual 灯的 global CDF；存在形状
  光时仍保留 global/local mixture，维持完整支持域与 MIS PDF 一致性。
- 新增专用 `RaytracingGBufferPass`，使用稳定的 primary visibility ray 输出 NRD guide：positive linear
  viewZ、2.5D pixel motion、`R10G10B10A2_UNorm` oct normal + linear roughness；同时预留
  DLSS Ray Reconstruction guide：RG16F screen-space motion、hardware depth、world normal + perceptual
  roughness、diffuse/specular albedo 和 base-color/metalness。
- Raytracing GBuffer 现在使用 NRD 官方 `NRD_MaterialFactors` 生成 primary-surface diffuse/specular
  material factor。REBLUR 前先把 OpenPBR 的材质调制信号解调为 radiance，降噪后再重调制，避免
  base-color、metalness 和高频纹理边界被当作 lighting noise 跨表面扩散。
- REBLUR history 由 RenderGraph history registry 按 camera 隔离，包含 previous viewZ、normal/roughness、
  internal data、diffuse/specular main/fast history、diffuse/specular stabilized luma 和 specular
  hit-distance tracking；当完整 REBLUR dispatch 不可用时，resolve 会回退到未降噪的
  diffuse/specular 输入，而不是输出黑屏。
- 已参考 NRD Sample 增加 `VividRP/Path Tracing/REBLUR Denoiser` VolumeComponent，开放当前调度链实际
  支持的 accumulation、history fix、anti-lag、prepass/spatial radius、normal/roughness rejection、
  anti-firefly、responsive accumulation、hit-distance normalization 和 debug 参数。已开放参数的
  默认值及约束与 NRD v4.16 `ReblurSettings` 对齐。
- hit-distance normalization 参数同时驱动 path-tracing front-end 的 hitT 编码与 REBLUR backend 常量，
  避免只调整 denoiser 一侧造成信号契约失配；任意有效 REBLUR Volume 设置变化都会按 camera 使历史失效。
- Volume 已开放 `maxStabilizedFrameNum`，默认沿用 NRD v4.16 的 63，并在运行时按
  `maxAccumulatedFrameNum` 截断；0 显式关闭稳定化。
- Volume 已开放 `checkerboardMode` 的 Off/Black/White 模式。启用后 path-tracing front-end 按
  `(pixel.x ^ pixel.y ^ frameIndex)` 将 diffuse/specular 以相反相位紧凑写入 signal texture 左半区，
  REBLUR PrePass 负责恢复全分辨率；GBuffer guide、direct lighting、emission 与最终输出仍保持全分辨率。
  Resolve Prepare 会按各自相位读取对应 full-resolution material factor，避免解调坐标错位；raw fallback
  也会按相位解包。遵循 NRD v4.16 约束，checkerboard 启用时自动跳过 hit-distance reconstruction。
- 主方向光已明确采用 VividRP 的 photometric contract：`DirectionalLightData.color` 保存 RGB illuminance
  （lux），OpenPBR `openpbr_eval` 返回 `BSDF * NdotL`，两者直接相乘得到物理尺度的直射光结果，不再施加
  重复的 cosine 或 `1 / PI`。全分辨率 direct-lighting/emission AOV 升级为 RGBA32F，避免日光照度与高光
  超过 FP16 动态范围。
- REBLUR resolve/raw fallback 与无限累积的 presentation 输出已接入 VividRP pre-exposure；REBLUR history、
  raw AOV 和 FP32 accumulation history 仍保持未曝光的 scene-linear 数据。这样 AutoExposurePass 可以先
  去除上一帧 pre-exposure 后计量场景亮度，FinalBlit 再应用 current/pre-exposure 比值，同时曝光适应不会
  污染时域历史或 ground-truth capture。
- Phase 4.2 已用 camera-independent Reference Light List + HDRI 的单一 candidate selector
  取代上述 ReGIR reservoir 积分路径。point/spot、rectangle/disc、tube、方向光与环境光共享离散
  selection PDF；shape/direction PDF 单独保存，visibility、MIS 与 AOV 路由由 raygen 统一处理。
  积累与 OIDN 历史现在追踪完整 Reference Light List signature，而不是 ReGIR cell/reservoir 状态。
- 当前 Raytracing GBuffer 只覆盖 StandardLit，motion vector 只包含 camera motion；skinned/deformed
  object previous position、confidence 与动态分辨率列为后续质量项。
- 当前降噪仅用于交互 preview。raw FP32 accumulation 仍是 canonical ground-truth/capture 来源，不能以 denoised
  output 替代数值基线。
- 当前尚未覆盖 scene/material mutation、手动 reset、target SPP、variance AOV 与 capture；这些仍属于本 milestone 后续范围。

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

完整 uber-BSDF 可能带来较长 shader import、较大 DXIL、较高寄存器和 stack pressure。Mitigation：P0 测试 LUT 模式、feature specialization 和 material variant；Vendor 修改仅限已记录的跨编译器可移植性补丁。

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
- Directional、point、spot、area light 通过验证；HDRI environment 完成 `E0`～`E6` 并冻结 V1 contract。
- FP32 deterministic accumulation 和可靠 history reset。
- Beauty、direct、indirect diffuse/specular、emission 和 primary material AOV。
- Raw linear EXR 和 metadata capture。
- White furnace、Cornell Box、material sphere、light matrix 和 alpha-test regression scenes。
- Unsupported feature/renderer/material 可见且可统计。
- Canonical output 无 denoiser、默认无 radiance clamp；启用 ReGIR proposal 时必须保留 correction weight、
  support floor、uniform fallback 和对应 metadata，并能与禁用 ReGIR 的直接采样结果做统计回归。
- 测试和文档明确区分 V1 ground truth 与尚未支持的 transmission/volume/emissive-mesh feature。

完成 V1 后，实时 GI 功能应以这些 reference scene 的 indirect AOV 为主要质量基准，并将 performance、temporal stability 和 bias 分开评估。
