# Chiang Hair Path Tracing Roadmap

## Context

VividRP 已具备一条可运行的 Reference Path Tracing 路径，以及对应的 Raytracing GBuffer、ReBLUR 和 DLSS Ray Reconstruction 集成。下一步计划以 RTXCR 的 Chiang near-field hair BCSDF 为参考，实现独立的 Hair shader，让真实 strand geometry 能参与 Reference PT、直接光 NEE、多次反弹和现有 DLSS-RR preview。

RTXCR 参考实现位于 `E:\RTXCR`。最相关的代码是：

- `libraries/rtxcr/material/shaders/include/rtxcr/HairMaterial.hlsli`
    - hair material、吸收模型、melanin 映射和 interaction 构建。
- `libraries/rtxcr/material/shaders/include/rtxcr/HairChiangBSDF.hlsli`
    - Chiang BCSDF evaluation、PDF 和 importance sampling。
- `libraries/rtxcr/geometry/include/CurveTessellation.h`
    - line segment 到 LSS、DOTS 和 PolyTube 的转换。
- `samples/pathtracer/shaders/geometry.hlsli`
    - DOTS 命中后的中心线、径向法线、切线、半径和上一帧位置重建。
- `samples/pathtracer/shaders/PathtracingPass.rgs.hlsl`
    - Hair BCSDF 在 NEE 和间接反弹中的使用方式。
- `samples/pathtracer/shaders/GBufferPass.rgs.hlsl`
    - hair roughness、albedo、normal 和 strand motion vector 输出。

本 roadmap 的首要目标不是一次性移植 RTXCR 的全部 hair system，而是先建立一个可验证、可扩展的 Chiang Hair shader 闭环。

## Decision Summary

Hair V1 固定采用以下方案：

- 新增独立的 `VividRP/Hair` ShaderLab shader，不把 Chiang 分支塞进 `StandardLit.shader`。
- 首个几何后端使用 DOTS（Disjoint Orthogonal Triangle Strips）。它能以普通 Unity `MeshRenderer` 进入现有 RTAS，不要求修改 Unity 原生 DXR 接口。
- 首个材质模型只实现 Chiang near-field BCSDF，不同时加入 far-field、dual scattering 或经验漫反射层。
- 新 shader 提供 `ReferencedPathtracingDXR` 和 `RaytracingGBufferDXR` 两个 material pass，直接接入现有 Reference PT 与 DLSS-RR。
- Hair transport 在 V1 中全部归入 specular signal；TT/TRT 不触发 VividRP 的实体介质栈切换。
- 静态 strand 是 V1 的完成边界。strand deformation、上一帧中心线和 RTAS update 属于下一阶段。
- Procedural AABB 和原生 LSS 是可替换几何后端，不进入 Hair V1 的关键路径。

## Current Baseline

现有实现已经提供以下接入条件：

- `ReferencedPathTracingPass` 使用 material shader pass `ReferencedPathtracingDXR`，无需为 Hair shader 新增一套 raygen pass。
- `ReferencedPathtracingSurfaceResult` 已能返回 NEE diffuse/specular radiance、BSDF PDF、下一跳方向、throughput weight、roughness、hit distance 和 denoising AOV。
- `ReferencedPathtracingNEECandidate.hlsl` 已统一处理方向光、punctual light、area light 和 environment proposal。
- `RaytracingGBufferPass` 使用 material shader pass `RaytracingGBufferDXR`，其输出已经被 ReBLUR 和 DLSS-RR 消费。
- `ReferencedPathTracingDLSSRayReconstructionPass` 已绑定 radiance、depth、motion vector、normal/roughness、diffuse/specular albedo、emissive 和 hit-distance guide。
- `RTASBuildPass` 已收集普通 `MeshRenderer` 及其 material，因此 V1 的 DOTS mesh 不需要新增专用 RTAS instance 路径。
- ReBLUR 常量已有 `gStrandMaterialID` 和 `gStrandThickness`，但默认 strand material ID 为 `999`，当前等价于未启用 strand 特化。

当前还缺少：

- Hair 专用 ShaderLab properties 和材质 adapter。
- Chiang BCSDF 的 VividRP-owned wrapper。
- DOTS mesh 数据约定和 closest-hit 几何重建。
- 第四个 Chiang BSDF 随机数维度。
- DOTS 穿越另一组正交 strip 时的 hair-aware ray origin offset。
- Hair 专用 Raytracing GBuffer closest-hit 和 DLSS-RR albedo guide 映射。
- 动态 strand 的 previous centerline position。

## Scope

### Hair V1

V1 支持：

- 静态 line-segment hair asset 的小规模测试数据。
- 每段独立的 DOTS 三角形表示。
- 每端点 position、radius 和 UV。
- 均匀或逐顶点 strand tangent。
- Chiang R、TT、TRT 和剩余高阶散射项。
- Color、Physics melanin 和 Normalized melanin 三种吸收模式。
- 纵向粗糙度、方位粗糙度、IOR、cuticle angle 和 Fresnel approximation。
- Analytic light 与 environment NEE。
- 多次反弹、MIS、Russian roulette 和现有 accumulation。
- Raytracing GBuffer、ReBLUR 和 DLSS-RR preview。
- Hair geometry、lobe、normal、roughness 和 AOV debug 验证。

V1 不承诺：

- 原生 LSS。
- Procedural AABB intersection shader。
- strand skinning、morph target、simulation 或 per-frame topology change。
- 完整 glTF `NV_materials_hair` importer。
- full-resolution Claire hair asset 的实时性能。
- far-field BCSDF、multiple-scattering compensation 或 Marschner dual scattering。
- 栅格透明毛发、深度预通道、常规实时 lighting 和 shadow-map parity。
- hair texture atlas、root/tip variation、随机染色和复杂 groom authoring。
- DLSS-RR 输出作为 ground truth；raw Reference PT accumulation 始终是正确性基准。

## Design Principles

- 几何表示与 Hair BCSDF 分离。BSDF 只依赖稳定的 `HairSurfaceGeometry`，不依赖 DOTS、AABB 或 LSS 的具体实现。
- Vendor source 与 VividRP adapter 分离。若许可证允许直接引入 RTXCR material library，vendor 文件保持原样；坐标系、参数和 integrator glue 放在 VividRP-owned 文件中。
- 新 Hair shader 复用现有 path-tracing payload 和 NEE contract，不复制 raygen integrator。
- Hair BCSDF 不使用普通 surface BRDF 的额外 `NdotL`。所有 Eval/Sample/PDF 的 measure 必须由同一 adapter 明确定义。
- Hair scattering 允许出射方向位于径向 normal 的任一侧。不能复用 StandardLit 的 opaque reflection hemisphere rejection。
- Hair TT/TRT 是 fiber scattering lobe，不是一个闭合 solid volume boundary。V1 必须保持 `mediumTransition = 0`。
- Sampling dimension layout 只追加，不移动既有 StandardLit、NEE、volume、atmosphere 和 RTXTF 的维度。
- DLSS-RR 只消费物理意义稳定的 guide；不为了短期观感向 normal、roughness 或 motion vector 写入 screen-space hack。
- 首个提交资产必须小而可审查。全量 groom 的吞吐和内存优化在正确性闭环之后进行。

## Architecture

### Hair Material Contract

建议 V1 ShaderLab properties：

- `_HairBaseColor`
- `_HairAbsorptionModel`
- `_HairMelanin`
- `_HairMelaninRedness`
- `_HairLongitudinalRoughness`
- `_HairAzimuthalRoughness`
- `_HairIor`
- `_HairCuticleAngleDegrees`
- `_HairFresnelApproximation`
- `_HairRadiusScale`
- `_HairEmissionColor`，仅作为可选的 surface emission，不参与 Hair BCSDF 参数推导。

VividRP adapter 构造统一的 `VividHairMaterialData`，再映射到 Chiang interaction：

- `eta = rcp(ior)`，不作为独立材质参数暴露。
- roughness 和 melanin 输入进入 adapter 前执行有限范围 clamp。
- color absorption 使用 scene-linear base color。
- material property 默认值与 RTXCR sample 默认值对齐，并在 roadmap 实施时记录精确数值。
- V1 不使用 StandardLit metalness、clear coat、normal map、thin transmission 或 volume 参数。

### DOTS Vertex Contract

V1 以普通 Unity `Mesh` 表达 DOTS。每个 line segment 生成两组正交 strip，共 4 个三角形和 12 个独立顶点。顶点不跨 segment 共享，保证 closest-hit 可以由当前 triangle 的第一个和第三个顶点稳定找回端点。

建议 attribute 布局：

- `POSITION`: DOTS 展开后的 strip surface position。
- `NORMAL`: 该顶点相对中心线的有符号 offset axis。
- `TANGENT.xyz`: segment centerline tangent。
- `TEXCOORD0.xy`: strand UV。
- `TEXCOORD1.x`: 应用 DOTS volume compensation 后的 radius。
- `TEXCOORD1.y`: endpoint coordinate，起点为 0，终点为 1，供 contract test 和后续动画使用。

构建规则：

- 使用与 RTXCR 相同的两个正交 frame axis 和 triangle winding。
- 应用 `1 / (sin(pi / 4) / (pi / 4))` 的 DOTS radius compensation。
- radius epsilon 使用 hair asset 单位和显式配置，不在 importer 中无条件写死世界空间 `0.001`。
- mesh bounds 必须包含 radius expansion。
- 大于 65535 顶点时显式使用 `IndexFormat.UInt32`。
- V1 validation asset 必须限制 segment 数量，避免以全量 Claire 数据作为首个 Unity import 测试。

### DOTS Hit Reconstruction

`HairGeometry.hlsl` 提供独立的 `VividHairSurfaceGeometry`：

- `positionWS`
- `centerlinePositionWS`
- `faceNormalWS`
- `radialNormalWS`
- `tangentWS`
- `radius`
- `strandUv`
- `segmentU`
- `hitDistance`
- `isFrontFace`

对每个命中 triangle：

1. 获取 triangle indices 和三个顶点的 position、normal、tangent、UV、radius。
2. 根据约定的第一个和第三个顶点恢复 segment 两端：
   `p0 = surfaceP0 - normal0 * radius0`，
   `p1 = surfaceP1 - normal1 * radius1`。
3. 使用 RTXCR 的 tapered DOTS normal reconstruction 计算真实径向 normal，而不是使用 strip triangle face normal。
4. 由 `normalize(p1 - p0)` 得到 strand tangent。
5. 求解命中点沿 segment 的参数 `u`，并重建 centerline position 和插值 radius。
6. 将 payload position 修正到近似圆柱/圆锥表面，使 depth、motion 和下一跳 origin 使用同一几何语义。
7. 对退化 segment、零半径、近平行视线和非有限结果提供稳定 fallback。

DOTS geometry helper 必须独立于 Chiang adapter。未来 AABB 或 LSS 后端只需要生成相同的 `VividHairSurfaceGeometry`。

### Hair Local Frame

Hair adapter 使用正交 TBN：

- `T = normalize(tangentWS)`。
- `N = normalize(radialNormalWS)`。
- `B = normalize(cross(N, T))`。
- 再以 `cross(T, B)` 修正 `N`，避免插值误差破坏正交性。

必须用 contract test 固定 HLSL matrix 的行列约定，以及 world-to-local/local-to-world 的乘法方向。Chiang 的 azimuthal offset `h` 依赖入射方向、径向 normal 和 tangent 的相对朝向；TBN 转置错误不会产生编译错误，但会交换或镜像 R/TT/TRT 分布。

### Chiang Evaluation and Sampling

VividRP wrapper 暴露以下最小接口：

```hlsl
VividHairPreparedChiang VividHairPrepareChiang(
    VividHairMaterialData material,
    VividHairSurfaceGeometry geometry,
    float3 viewDirectionWS);

float3 VividHairEvaluateChiang(
    VividHairPreparedChiang prepared,
    float3 lightDirectionWS,
    out float pdf);

bool VividHairSampleChiang(
    VividHairPreparedChiang prepared,
    float4 random,
    out float3 directionWS,
    out float3 bsdfValue,
    out float pdf,
    out uint lobe);
```

接口语义必须固定：

- `Evaluate` 返回与 RTXCR Chiang 相同 measure 下的 BCSDF，不额外乘 `abs(dot(N, L))`。
- `Sample` 返回未除 PDF 的 BCSDF value；closest-hit 显式写入 `nextThroughputWeight = bsdfValue / pdf`。
- `Evaluate` 和 `Sample` 使用完全相同的 absorption、roughness、Fresnel 和 cuticle 参数。
- 所有返回值进入 payload 前执行 finite、non-negative 和 PDF guard。
- lobe ID 至少区分 R、TT、TRT 和 residual，供 debug 使用；V1 对 integrator 仍统一上报 specular lobe class。

### Sampling Dimension Contract

当前 Reference PT 每次 bounce 为 surface BSDF 保留 3 个随机维度，而 RTXCR Chiang sampler 需要两个 `float2`，即 4 个随机数。

V1 不移动现有 NEE 或后续 feature 的维度。建议：

- 在当前 bounce stride 的保留区分配一个 `kReferencedPathtracingHairBsdfExtraDimensionOffset`。
- 保留现有 `bsdfRandom.xyz`，新增一个独立的 `hairBsdfExtraRandom` payload input。
- Chiang 组装为 `{ bsdfRandom.xy, float2(bsdfRandom.z, hairBsdfExtraRandom) }`。
- 增加 `REFERENCED_PATH_SAMPLING_CONTRACT_VERSION`。
- StandardLit 继续只消费原来的 3 个 BSDF 维度，其序列不变。
- sampling contract test 覆盖维度不重叠、Indexed BND/Hash 两种模式和历史重置行为。

输入 payload 当前只使用 40 DWORD storage 的前一部分，追加一个 input scalar 不要求扩大 payload；但所有 offset、pack、unpack 和 shader contract tests 必须同步更新。

### Reference Path Tracing Material Pass

`HairReferencedPathtracingDXR` closest-hit 复用现有 `ReferencedPathtracingSurfaceResult`：

- 从 DOTS attributes 构造 `VividHairSurfaceGeometry`。
- 准备 Chiang interaction。
- 复用 `ReferencedPathtracingSampleUnifiedNEECandidate(...)`。
- 对有效 NEE candidate 调用 Chiang Eval，并写入：
    - `neeDiffuseRadiance = 0`
    - `neeSpecularRadiance = hairBcsdf * incidentRadianceOverPdf`
    - `neeBsdfPdf = hairPdf`
- 调用 Chiang Sample，并写入：
    - `nextDirectionWS`
    - `nextThroughputWeight = hairBcsdf / hairPdf`
    - `nextPdf = hairPdf`
    - `nextLobeClass = 2`
    - `nextLobeIsDelta = 0`
    - `nextLobeIsTransmission = 0`
    - `mediumTransition = 0`
- `linearRoughness` 初期使用 longitudinal roughness。
- `denoisingNormalWS` 使用 view-consistent radial normal。
- `denoisingAlbedo` 初期使用 hair base color，以保持与 RTXCR GBuffer 的 reference mapping 一致。

Hair direction validation 不能调用 StandardLit 的 opaque reflection hemisphere helper。R、TT 和 TRT 都允许绕 fiber 穿过径向 normal 的另一侧，只需检查方向、PDF 和 throughput 是否有限且非零。

### DOTS Self-intersection and Ray Origin

DOTS 每段包含两组互相正交的 strip。普通的微小 normal bias 在 ray 穿越 fiber 时可能立即命中另一组 strip。RTXCR 对 transition direction 增加约 `2 * radius` 的偏移。

V1 必须把这个行为纳入公共 surface result，而不是在 Hair shader 中悄悄移动世界坐标：

- 为 path-tracing surface result 增加 strand surface 标记与 geometry offset 数据，或建立明确的 payload union。
- NEE visibility ray 和下一跳 radiance ray 使用同一 hair-aware origin helper。
- 当 `dot(direction, radialNormal) < 0` 时，在正常浮点 bias 外增加约 `2 * radius` 的跨 fiber 偏移。
- 非 strand surface 保持现有 offset 完全不变。
- 若扩展 payload 尺寸，必须记录 DXC payload size、shader stack/register 变化；若复用现有字段，必须以显式 surface-kind union 表达，不能隐式挪用 unrelated StandardLit 字段。

单 segment 双 strip 场景必须验证 primary、shadow 和 multi-bounce ray 都不会产生稳定的零距离自交亮点或黑边。

### Raytracing GBuffer and DLSS-RR

Hair shader 提供独立的 `RaytracingGBufferDXR` closest-hit，输出：

- corrected hair surface position。
- radial shading normal。
- longitudinal roughness。
- base color / absorption tint。
- hair IOR 推导的 specular F0。
- emission。
- hit distance。

现有 GBuffer raygen 会从 `baseColor + metalness` 推导 diffuse/specular albedo，这对 Hair 不够通用。V1 应将 GBuffer payload 改为 material closest-hit 直接提供：

- `diffuseAlbedo`
- `specularAlbedo`
- 可选的 `materialID` 或 `surfaceKind`

StandardLit closest-hit 按现有公式填写这两个字段，输出保持不变；Hair closest-hit 初期对齐 RTXCR：

- `diffuseAlbedo = hairBaseColor`
- `specularAlbedo = dielectricF0(ior)`

由于 path tracer 将 Hair radiance 归入 specular signal，后续应通过 DLSS-RR debug/quality comparison 判断是否需要使用方向相关的 hair energy guide；V1 不在没有测量的情况下引入经验补偿。

静态 Hair 的 motion vector 继续使用当前 position 与 previous camera matrix，足以覆盖相机运动。动态 Hair 阶段必须增加 previous centerline/surface position，不能继续复用当前 `positionWS`。

DLSS-RR 验收必须同时比较：

- raw current-frame radiance。
- progressive raw accumulation。
- DLSS-RR enabled result。
- DLSS-RR disabled/fallback result。
- normal、roughness、depth、motion、diffuse/specular albedo 和 hit-distance guide debug view。

### ReBLUR Strand Metadata

ReBLUR 已支持 strand material ID 和 thickness，但当前 GBuffer 将 NRD packed material ID 写为 0。该能力不阻塞 DLSS-RR Hair V1，但建议在 V1 收尾或动态阶段完成：

- GBuffer payload 添加 material ID。
- Hair 使用稳定、可编码的 material ID。
- `gStrandMaterialID` 与 packed value 使用同一归一化约定。
- `gStrandThickness` 来自可解释的 groom/world-space diameter，而不是固定 magic number。
- StandardLit material ID 和已有 ReBLUR 行为保持不变。

## Proposed Files

### Hair shader and VividRP adapters

- `Shaders/Material/Hair/Hair.shader`
- `Shaders/Material/Hair/HairInput.hlsl`
- `Shaders/Material/Hair/HairGeometry.hlsl`
- `Shaders/Material/Hair/HairChiangAdapter.hlsl`
- `Shaders/Material/ShaderPass/HairReferencedPathtracing.hlsl`
- `Shaders/Material/ShaderPass/HairRaytracingGBuffer.hlsl`

### RTXCR material source, subject to license gate

- `Shaders/Material/Hair/Vendor/RTXCR/HairMaterial.hlsli`
- `Shaders/Material/Hair/Vendor/RTXCR/HairChiangBSDF.hlsli`
- `Shaders/Material/Hair/Vendor/RTXCR/utils/*`
- `Shaders/Material/Hair/Vendor/RTXCR/NOTICE.md`

如果选择基于论文重新实现而不是分发 RTXCR source，则改用 VividRP-owned 文件路径，并在文档中记录算法来源和差异，不创建伪装成 vendor copy 的目录。

### Geometry and authoring

- `Runtime/Utility/Hair/HairStrandData.cs`
- `Runtime/Utility/Hair/HairDotsMeshBuilder.cs`
- `Editor/Hair/HairValidationAssetBuilder.cs`

V1 可以先只提交小型 synthetic validation asset builder。正式 glTF/groom importer 在 shader 闭环后另立里程碑。

### Existing files expected to change

- `Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingSampling.hlsl`
- `Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingCommon.hlsl`
- `Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracing.rgen.hlsl`
- `Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/RaytracingGBufferCommon.hlsl`
- `Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/RaytracingGBuffer.rgen.hlsl`
- `Shaders/Material/ShaderPass/RaytracingGBuffer.hlsl`
- optional: `Runtime/RenderPass/Core/GlobalIllumination/ReferencedPathtracing/ReblurSharedConstants.cs`

### Tests

- `Tests/Editor/HairDotsMeshBuilderTests.cs`
- `Tests/Editor/HairShaderContractTests.cs`
- `Tests/Editor/HairSamplingContractTests.cs`
- `Tests/Editor/HairMaterialParameterTests.cs`
- `Tests/Editor/HairRaytracingGBufferContractTests.cs`

如果新增 runtime-only buffer update 或 dynamic geometry，再增加 `Tests/Runtime/` assembly；不要为了静态 V1 提前创建空的 Runtime test assembly。

## Milestone H0: Source, License and Shader Compile Gate

### Goal

在修改 path tracer contract 前，证明 Chiang library 能在 Unity/DXC 环境中合法、稳定地编译。

### Scope

- 确认 `HairMaterial.hlsli`、`HairChiangBSDF.hlsli` 和其 utils 的精确许可证及 source redistribution 条件。
- 在以下两种方式中做出明确选择：
    - 保留版权和许可证的 vendor source import。
    - 根据公开论文/API contract 进行 VividRP-owned implementation。
- 建立最小 DXC compile harness，调用 prepare、eval、pdf 和 sample，而不是只测试 include 成功。
- 固定 HLSL matrix convention、local tangent axis、normal axis 和 incident/outgoing direction convention。
- 记录 RTXCR commit ID 和移植差异。

### Acceptance

- 许可证选择被写入 `NOTICE.md` 或 implementation note。
- DXC SM 6.6 编译通过，无 warning-as-error、NaN constant folding 或 unsupported intrinsic。
- Eval、PDF 和 Sample 的入口都进入编译后的 DXIL。
- Vendor 文件如果被引入，保持无 VividRP-specific 修改。

## Milestone H1: Static DOTS Geometry Contract

### Goal

让一个静态 line-segment validation asset 以普通 `MeshRenderer` 进入现有 RTAS，并在 closest-hit 中恢复真实 fiber geometry。

### Scope

- 定义 `HairStrandData` 的 position、radius、UV 和 segment index contract。
- 实现每段 4 triangle / 12 vertex 的 DOTS builder。
- 实现 orthogonal frame、radius compensation、bounds 和 UInt32 index handling。
- 实现 `VividHairBuildDotsSurfaceGeometry(...)`。
- 增加 debug 输出：triangle face normal、reconstructed radial normal、tangent、segment U 和 radius。
- 创建 single segment、crossed segments、tapered segment 和 small hair bundle validation assets。

### Acceptance

- 每个 segment 精确生成 4 triangles 和 12 vertices。
- 4 个 triangle 命中重建出的 centerline endpoints、radius 和 tangent 一致。
- tapered segment 的径向 normal 连续且无明显 strip seam。
- DOTS mesh 可被当前 `RTASBuildPass` 收集，无 Hair-specific RTAS C# 修改。
- 退化输入被拒绝或稳定修复，不生成 NaN bounds/vertex。

## Milestone H2: Chiang Adapter and Sampling Contract

### Goal

把 Chiang Eval/PDF/Sample 封装为 VividRP integrator 可直接使用的稳定接口。

### Scope

- 实现 material property mapping 和 absorption model。
- 实现 world/local Hair frame conversion。
- 实现 Chiang prepare、eval、pdf 和 sample wrapper。
- 为 Chiang 增加第 4 个 BSDF 随机维度，但不改变已有 StandardLit 序列。
- 增加 lobe debug mode：R、TT、TRT、residual 和 combined。
- 对极端 roughness、cuticle angle、IOR、melanin 和 grazing direction 加 finite guards。

### Acceptance

- 固定 random seed 下 Sample 可重复。
- Sample 返回方向都为有限的单位向量，PDF 为有限非负值。
- 对 Sample 产生的方向重新 Eval/PDF，PDF contract 一致。
- Monte Carlo estimator 与数值积分在预设统计容差内一致。
- 切换 absorption model 和 melanin 参数产生可解释、单调的颜色变化。
- StandardLit sampling sequence golden test 不变化。

## Milestone H3: Hair Reference Path Tracing Pass

### Goal

让新 Hair shader 在现有 Reference PT 中完成 direct NEE、environment 和 multi-bounce Chiang transport。

### Scope

- 新增 `Hair.shader` 的 `ReferencedPathtracingDXR` pass。
- 复用统一 NEE candidate 和 visibility trace。
- Hair Eval 只写 specular NEE signal，不额外乘 surface cosine。
- Hair Sample 显式写入 `BCSDF / PDF` throughput。
- 所有 Hair lobe 上报 specular、non-delta、non-medium-transition。
- 放宽 Hair 的出射 hemisphere 验证。
- 实现 DOTS hair-aware primary/visibility/continuation ray origin offset。
- 保持 raw accumulation 和 capture contract 不变。

### Acceptance

- 单束 Hair 可接收 analytic light 和 HDRI environment lighting。
- R、TT、TRT debug 模式的高光位置与 RTXCR reference 一致。
- 关闭 NEE 后结果仍通过 BSDF path 收敛；启用 NEE 后均值一致而噪声下降。
- 多次反弹没有错误 medium stack push/pop。
- 无稳定 self-hit、黑色 strip seam、firefly ring 或反面完全丢失。
- raw output 中不存在新增的 NaN/Inf/negative-radiance invalid sample。

## Milestone H4: Hair GBuffer and DLSS-RR

### Goal

让 Hair 使用正确的几何和材质 guide 进入现有 DLSS Ray Reconstruction 路径。

### Scope

- 新增 `Hair.shader` 的 `RaytracingGBufferDXR` pass。
- 将 GBuffer payload 从 StandardLit 推导模式扩展为 material-supplied diffuse/specular albedo。
- Hair 输出 corrected position、radial normal、longitudinal roughness、base color、F0 和 emission。
- 验证 camera motion vector、depth 和 hit distance 与 corrected position 一致。
- 增加 Hair guide debug view 或 capture。
- 对 DLSS-RR reset、camera cut、resize、quality mode change 和 material change 进行历史验证。

### Acceptance

- DLSS-RR 开启后 Hair 不出现由平面 strip normal 导致的十字形高光或法线断层。
- 静态 Hair 在相机移动时 motion vector 方向和幅度正确，无持续拖影。
- depth、normal/roughness、albedo 和 hit-distance guide 在 Hair 边界对齐。
- 关闭 DLSS-RR 后 raw/fallback 输出仍正常，RR 不成为 Reference PT 的硬依赖。
- StandardLit GBuffer 与 DLSS-RR 回归图不发生非预期变化。

## Milestone H5: Validation Corpus and V1 Freeze

### Goal

把 Chiang Hair shader 从“能出图”推进到可作为后续几何与 denoising 优化的 reference baseline。

### Required Scenes

- `HairSingleSegment`
    - 检查四个 DOTS triangle、径向 normal、tangent、h 和 ray offset。
- `HairTaperedBundle`
    - 检查变半径、UV、近距离轮廓和 strip seam。
- `HairLobeReference`
    - 固定相机与灯光，分别输出 R、TT、TRT 和 combined。
- `HairMelaninSweep`
    - 扫描 melanin、redness 和 absorption model。
- `HairRoughnessSweep`
    - 扫描 longitudinal/azimuthal roughness。
- `HairDenoisingMotion`
    - 静态 Hair、移动相机，比较 raw accumulation、ReBLUR 和 DLSS-RR。
- `HairReducedGroom`
    - 小规模真实 groom，用于内存、BLAS、trace 和 RR 时间记录。

### Measurements

- DOTS vertex/index memory。
- RTAS build time 和 BLAS memory。
- Reference trace GPU time。
- Raytracing GBuffer GPU time。
- DLSS-RR evaluation time。
- invalid sample rate。
- raw 1 spp、8 spp、64 spp 和 converged reference 的误差。
- DLSS-RR temporal stability 和边缘 ghosting。

### Acceptance

- validation scene 和固定 capture settings 被记录。
- RTXCR reference 差异可以由坐标、参数、几何后端或 integrator 差异解释。
- Hair shader 在所有 required scene 中无 NaN、明显 self-intersection 或稳定漏光。
- StandardLit Reference PT、GBuffer、ReBLUR 和 DLSS-RR 回归测试通过。
- V1 限制、参数范围和资产单位写入 `Documentation/`。

## Milestone H6: Dynamic Strand and Motion

### Goal

在 V1 冻结后支持 strand deformation，并向 DLSS-RR/ReBLUR 提供正确的 previous geometry。

### Scope

- 保存 current/previous centerline endpoint 和 radius buffer。
- 根据同一 segment U 重建 previous centerline position。
- 使用 previous object transform 和 previous centerline 生成 previous surface position。
- 将 GBuffer payload 从 camera-only motion 扩展为 material-supplied previous position。
- 更新或重建 DOTS vertex buffer，并验证 Unity RTAS dynamic geometry 行为。
- 根据实际 hair diameter 启用 NRD strand material ID/thickness。
- 定义 topology change、simulation reset、teleport 和 groom swap 的 history reset。

### Acceptance

- 只有 strand deformation、相机静止时仍能得到非零且方向正确的 motion vector。
- 相机与 strand 同时运动时 previous clip position 组合正确。
- DLSS-RR 和 ReBLUR 在动画 Hair 上没有持续拖影、爆闪或 previous/current buffer 反转。
- animation reset 后 history 被保守清空。

## Milestone H7: Alternative Geometry Backends

### Goal

保持 Chiang Hair shader 不变，评估比 DOTS 更紧凑的几何路径。

### Procedural AABB

- 每个 line segment 一个 conservative AABB。
- intersection shader 实现有限 tapered swept-sphere/capsule 求交。
- intersection attributes 返回 segment U 和构造 radial normal 所需数据。
- 与 DOTS 对比 memory、AS build、trace time、自交和轮廓精度。
- 只有在目标硬件上稳定胜过 DOTS 时才替换默认后端。

### Native LSS

- 当前 Unity 6000.7 公开 C# RTAS API 没有 Linear Swept Spheres 配置和 capability query。
- 不在 V1 中通过 native plugin 接管整套 DXR pipeline。
- 保留 `VividHairSurfaceGeometry` backend boundary，等待 Unity 暴露 LSS 后再实现。
- 若未来必须走 NVIDIA-only native plugin，先单独评估 RenderGraph resource ownership、BLAS/TLAS ownership、shader table、NVAPI shader extension 和 DLSS interop，不与 Hair shader V1 混合交付。

## Test Strategy

### CPU and EditMode Tests

- DOTS vertex/index count、winding 和 attribute layout。
- orthogonal frame 与 radius compensation。
- bounds expansion、UInt32 indices 和 degenerate segment handling。
- material property defaults、range clamp 和 absorption mode mapping。
- sampling dimension offsets 不重叠，StandardLit 既有维度不移动。
- shader source contract 包含两个 material pass 和预期 entry points。
- GBuffer payload 由 material 提供 albedo 后，StandardLit mapping 保持原结果。

### Shader Compile Tests

- 独立 DXC SM 6.6 编译 Chiang prepare/eval/pdf/sample。
- 编译 Hair `ReferencedPathtracingDXR` closest-hit。
- 编译 Hair `RaytracingGBufferDXR` closest-hit。
- 编译 Indexed BND 和 Indexed Hash sampling variants。
- 编译所有 absorption/Fresnel feature variants，同时控制 variant 数量。

### GPU and Image Tests

- primary hit geometry reconstruction。
- direct NEE、environment NEE 和 BSDF-only reference。
- R/TT/TRT lobe reference captures。
- tangent/normal orientation rotation test。
- grazing angle 和 back side test。
- DOTS self-intersection stress test。
- DLSS-RR guide alignment、camera motion 和 history reset。
- StandardLit non-regression captures。

按照仓库规则，Unity Editor 正在运行时不主动启动 batchmode EditMode tests；此时使用 DXC、代码 contract test 和当前 Unity console 做针对性验证，并在交付说明中列出需要用户手动运行的 Unity tests。

## Main Risks

### Licensing

RTXCR 根仓库、geometry 和 sample glue 的许可证并不完全相同。不能因为部分 Hair HLSL 带有 MIT-style header，就把整个 RTXCR 当作可直接复制的 MIT package。H0 必须先确定实际引入文件和分发方式。

### Coordinate Convention Drift

Chiang 对 tangent axis、radial normal 和入射方向符号非常敏感。错误通常表现为“看起来仍像头发”，但 R/TT/TRT 峰值位置已经错误，因此必须使用固定 reference scene 和 lobe isolation 验证。

### Sampling Bias

缺少第四个独立随机维度、错误的 PDF measure、额外 surface cosine 或把 Sample return value 重复除 PDF，都会引入系统性偏差。不能只依赖降噪后的视觉结果判断。

### DOTS Self-intersection

两组正交 strip 会让 transition ray 命中同一 segment 的另一面。普通 surface bias 不足以解决，需要显式的 strand radius-aware offset。

### Payload and Shader Cost

Reference PT surface payload 当前已经较大。新增字段前应优先检查 input/result storage 是否可安全复用，并记录 payload size、stack 和 DXIL 变化。不能为了避免结构调整而将 hair radius 隐式塞进 unrelated field。

### DLSS-RR Guide Semantics

StandardLit 的 metalness/F0 albedo 推导不适用于 Hair。错误的 guide 可能让 RR 画面短期更平滑，但产生颜色漂移和历史不稳定。Hair GBuffer 必须由 material 明确给出 guide，并保留 raw reference 对照。

### Geometry Scale

DOTS 每段需要 12 个顶点。全量真实 groom 会快速放大 Unity Mesh、upload、BLAS 和内存成本。V1 只证明正确性；生产资产必须有 LOD、segment reduction、groom partition 和替代后端计划。

## Recommended Delivery Order

1. H0：许可证、DXC 和坐标契约。
2. H1：小型静态 DOTS validation mesh 与几何 debug。
3. H2：Chiang adapter、第四随机维度和数值测试。
4. H3：Reference PT NEE、多跳、MIS 和 hair-aware ray offset。
5. H4：Raytracing GBuffer 与 DLSS-RR guide。
6. H5：validation corpus、性能基线和 Hair V1 freeze。
7. H6：动态 strand、previous position 和 NRD strand metadata。
8. H7：Procedural AABB；原生 LSS 等待 Unity API。

H1 与 H2 可以并行开发，但 H3 必须在两者的 contract 都冻结后合入。H4 可以复用 H1 的几何 helper，但应在 H3 已证明 radial normal 和 corrected position 正确后进行。

## Definition of Done for Hair V1

Hair V1 只有在以下条件全部满足时才能标记完成：

- 独立 `VividRP/Hair` shader 同时提供 `ReferencedPathtracingDXR` 和 `RaytracingGBufferDXR`。
- 小型静态 DOTS groom 能由普通 `MeshRenderer` 进入现有 RTAS。
- closest-hit 使用 reconstructed radial normal、centerline tangent 和 radius，而不是 strip face normal。
- Chiang Eval/PDF/Sample 通过 DXC、finite、repeatability 和 Monte Carlo consistency tests。
- 第四个 Hair BSDF 随机维度不改变既有 StandardLit sampling sequence。
- Direct light、environment 和 indirect path 都使用同一 Chiang material interaction。
- Hair transport 不额外乘 surface cosine，不进入实体介质栈，并统一写入 specular signal。
- primary、shadow 和 continuation ray 均通过 hair-aware offset 避免 DOTS 自交。
- Hair GBuffer 为 DLSS-RR 输出一致的 position、normal、roughness、albedo、depth、motion 和 hit-distance guide。
- raw Reference PT accumulation 与 DLSS-RR preview 可以独立启用和比较。
- validation corpus、性能数据、已知限制和许可证说明已记录。
- StandardLit Reference PT、ReBLUR 和 DLSS-RR 没有非预期回归。

