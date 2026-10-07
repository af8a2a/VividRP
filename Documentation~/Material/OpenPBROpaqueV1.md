# OpenPBROpaqueV1 默认材质合约

本合约冻结 VividRP 下一代默认 PBR 的原生输入和 BSDF 调用语义。默认家族选择包内的 OpenPBR 1.1 实现，首个 profile 为 `OpenPBROpaqueV1`：不透明、各向同性、单个 OpenPBR 叶 Closure。OpenPBR 内部的漫反射、介电反射和金属反射仍由 Vendor 实现组合；“单 Closure”不表示只保留一个反射 lobe。

合约冻结与生产路径迁移、GPU 验收分别推进。本次不切换现有 Material Graph、IR、AOT、Frozen Catalog 或 Deferred 默认 evaluator，也不修改现有 StandardLit/PT adapter。后续实现以本合约作为新的原生材质入口。

## 版本与权威来源

| 项目 | 冻结值 / 来源 |
| --- | --- |
| Profile | `OpenPBROpaqueV1` |
| 材质家族 | 包内 OpenPBR 1.1 Vendor 实现 |
| 输入合约版本 | `OpenPBROpaqueContract.Version = 1` |
| 指纹模式版本 | `OpenPBROpaqueContract.FingerprintVersion = 1` |
| 输入约定指纹 | `0x1602062CE683E774`，C#/HLSL 一致 |
| CPU 合约 | `Runtime/SubSystem/GPUDriven/Material/OpenPBROpaqueContract.cs` |
| GPU 合约 | `Shaders/Core/Public/VividOpenPBROpaqueContract.hlsl` |
| OpenPBR 调用桥接 | `Shaders/Core/Public/VividOpenPBROpaque.hlsl` |
| Vendor 参数与默认值 | `Shaders/Material/ShaderPass/OpenPBR/Vendor/openpbr_resolved_inputs.h` |
| Vendor API | `Shaders/Material/ShaderPass/OpenPBR/Vendor/openpbr_api.h` |

版本和指纹独立于 `SimpleSlabContract`。指纹标识字段语义、默认值、范围和调用约定，不是编译 shader 的内容哈希，也不替代 MaterialProgram 的 ABI/hash。字段语义、默认值、有效范围、profile 能力或积分约定发生变化时，必须审查合约版本和指纹，并增加对应回归基线；保持结果不变的源码重构不需要更换材质 program ID。

这里冻结的是语义字段顺序，不冻结 C# 结构体的原始内存、GPU payload 二进制布局、GBuffer 格式、字节偏移或压缩精度。新 IR/export ABI、CPU/GPU 存储布局与 Catalog invalidation 必须在后续接入中单独版本化。不得仅替换指纹便把已有 SimpleSlab Catalog 解释为 OpenPBR Catalog。

## 原生输入

输入是完成纹理采样后的数据。颜色使用 VividRP 当前线性工作 RGB 空间，纹理解码、sRGB 转换和法线纹理解码由调用方负责。所有标量和向量分量必须有限；非法输入应在合约边界被拒绝，不能通过静默饱和或替换材质家族掩盖。

| 顺序 | 字段 | 类型 | 原生默认值 | 有效范围 / 语义 |
| --- | --- | --- | --- | --- |
| 0 | `BaseWeight` | `float` | `1` | `[0, 1]`；OpenPBR base weight |
| 1 | `BaseColor` | `float3` | `(0.8, 0.8, 0.8)` | 各分量 `[0, 1]`；线性 base color |
| 2 | `BaseDiffuseRoughness` | `float` | `0` | `[0, 1]`；漫反射粗糙度，与 specular roughness 独立 |
| 3 | `BaseMetalness` | `float` | `0` | `[0, 1]`；OpenPBR metalness |
| 4 | `SpecularWeight` | `float` | `1` | `[0, 1]`；OpenPBR specular weight |
| 5 | `SpecularColor` | `float3` | `(1, 1, 1)` | 各分量 `[0, 1]`；线性 specular color |
| 6 | `SpecularRoughness` | `float` | `0.3` | `[0, 1]`；OpenPBR specular roughness |
| 7 | `SpecularIor` | `float` | `1.5` | `[1, 3]`；绝对折射率，外部介质 IOR 固定为 `1` |
| 8 | `NormalWS` | `float3` | `(0, 0, 1)` | 世界空间、有限、已归一化 |
| 9 | `EmissionLuminance` | `float` | `0` | 有限、`>= 0`；沿用 Vendor 的 nits（cd/m²）单位 |
| 10 | `EmissionColor` | `float3` | `(1, 1, 1)` | 各分量有限、`>= 0`；线性 emission color |

Emission 不使用 `[0, 1]` 上限，但 `EmissionLuminance * EmissionColor` 的每个分量也必须保持有限。有限的输入乘法溢出同样是非法输入。

`SpecularIor` 的 `[1, 3]` 是 VividRP 此 profile 的输入限制，不是额外的 Vendor 物理保证。当前多重散射数据的 `OpenPBR_IorMax` 为 `2.5`，超过该值由 Vendor 外推；`(2.5, 3]` 仍需纳入数值和图像验收。`IOR = 1` 是合法端点，不应在原生输入层强行抬高。

`SpecularRoughness = 0` 也是合法原生值。当前 Vendor 不支持理想 delta specular：GGX 有效轴 roughness 下限为 `0.001`，对应 alpha 下限 `1e-6`。这是 Vendor 内部的数值处理，不应把原始输入或其平方粗暴改成 SimpleSlab 的 alpha 下限。输入、有效微表面参数和存储量化应分别处理。

调用方负责归一化 `NormalWS`，合约校验为 `abs(dot(NormalWS, NormalWS) - 1) <= 1e-4`。Bridge 对已通过容差的法线再做数值归一化后生成各向同性正交 basis，满足 Vendor 的单位向量前置条件；这不改变非法法线的拒绝规则。`geometry_coat_basis` 与 `geometry_basis` 相同。V1 不接受独立切线、各向异性旋转或 coat normal。零法线和非有限法线不可通过。已有 Resolved record 的三个 basis 轴还必须满足 Vendor 的 `abs(length(axis) - 1) < 1e-6` 单位长度条件。

## 能力边界与完整初始化

V1 只允许上述原生字段。以下能力未启用，不能作为 V1 中的有效材质特性：

- Coat、fuzz、transmission、subsurface scattering 和 thin film。
- Specular anisotropy、coat anisotropy 和 transmission dispersion。
- Thin-walled geometry。

Feature mask 必须拒绝未知位。V1 的有效 mask 为零；任何已知但不受支持的能力位也必须拒绝。错误输入应保留明确诊断，不能落回 StandardLit 或 SimpleSlab 着色。

`OpenPBROpaqueUnsupportedFeatures` 与对应 HLSL mask 的诊断位固定如下；这些位描述拒绝原因，并不启用能力：

| 能力 | 位值 |
| --- | --- |
| `Coat` / `Fuzz` / `Transmission` | `1` / `2` / `4` |
| `Subsurface` / `ThinFilm` | `8` / `16` |
| `SpecularAnisotropy` / `CoatAnisotropy` | `32` / `64` |
| `Dispersion` / `ThinWalled` | `128` / `256` |

`OpenPBROpaqueValidationErrors` 可组合：`UnsupportedFeatures = 1`、`NonFinite = 2`、`OutOfRange = 4`、`InvalidNormal = 8`；零表示通过。CPU 的 `Validate` 与 HLSL 的 `VividValidateOpenPBROpaqueInputs` 使用相同规则。`VividTryResolveOpenPBROpaqueInputs` 只有通过校验才返回成功；其失败时初始化的输出不得被当成合法替代材质继续着色。

Bridge 必须先使用 `openpbr_make_default_resolved_inputs()` 完整初始化 Vendor struct，再覆盖本合约字段和 geometry basis。未公开的 inactive 参数必须保留 Vendor 默认值；例如零 coat weight 下的 coat IOR 仍为 `1.6`。它们不是 V1 的额外输入自由度。禁用的权重和效果量必须保持 inactive：

| 参数 | V1 固定值 |
| --- | --- |
| `coat_weight`、`fuzz_weight`、`transmission_weight`、`subsurface_weight`、`thin_film_weight` | `0` |
| `specular_roughness_anisotropy`、`coat_roughness_anisotropy`、`transmission_dispersion_scale` | `0` |
| `specular_anisotropy_rotation_cos_sin`、`coat_anisotropy_rotation_cos_sin` | `(1, 0)` |
| `geometry_thin_walled` | `false` |
| `geometry_opacity` | `1` |
| `geometry_coat_basis` | 与 `geometry_basis` 相同 |

`VividValidateOpenPBROpaqueResolvedInputs` 检查完整 Vendor record 的全部浮点分量，包括 inactive 参数；非有限值报告 `NonFinite`，未公开参数偏离默认值报告 `OutOfRange`，激活禁用能力仍报告 `UnsupportedFeatures`。不能假设零权重会屏蔽非法值：Vendor prepare 中的 `0 * NaN` 仍可能污染 base lobes。

不要通过全局更改现有 OpenPBR specialization constants 来限制此 profile：现有 PT adapter 的能力保留；V1 边界由其输入合约和 Bridge 单独保证。

## Coverage 与宿主职责

Coverage、AlphaClip、几何可见性和阴影可见性属于宿主和材质 Coverage 阶段。AlphaClip 判断保留下来的像素/交点进入 BSDF 时，`geometry_opacity` 恒为 `1`。`BaseWeight`、`SpecularWeight` 与 alpha 均不能代替几何 Coverage。

材质 AO、GTAO、光源颜色、距离/范围/spot attenuation、阴影、曝光和 pre-exposure 不属于这 11 个 BSDF 输入。AO 只作用于间接光照；直接光源 attenuation 和 shadow 由宿主积分器施加。是否接收某种光照、SSR confidence、decal 和其他后处理状态同样由宿主负责，不从 OpenPBR 反推。

## Prepare、Eval、Sample 与最终合成

V1 的 Bridge 固定使用 `path_throughput = (1, 1, 1)`、`exterior_ior = 1` 和 Vendor 的 `OpenPBR_BaseRgbWavelengths_nm = (620, 540, 450)`。路径积分器自身的累计 throughput 仍由宿主维护。当前 profile 不表达内部/嵌套介质或光谱输运；未来需要这些能力时必须定义新调用合约。

`view_direction` 和 `light_direction` 是从着色点指向观察方向/光源方向的归一化世界空间向量。它们是 prepare/eval 的调用输入，不能持久化为材质参数。随机采样输入遵循 Vendor API。`OpenPBR_PreparedBsdf` 依赖 view direction，只能作为当前着色事件的临时准备结果；不得把它作为材质资产或跨相机 GBuffer payload。

| 调用 / 输出 | 冻结约定 |
| --- | --- |
| `openpbr_prepare` | 从完整 resolved inputs 准备 BSDF、volume 和 emission |
| `openpbr_eval` | 返回 `f * abs(cos(theta_i))`；包含 cosine，不再乘一次 `NdotL` |
| `openpbr_sample` | 返回的 weight 已含 `f * abs(cos(theta_i)) / pdf`；不能再次乘 cosine 或除 pdf |
| Sample `pdf > 0` | direction、weight、sampled type 是有效输出 |
| Sample `pdf == 0` | 没有有效样本，其余输出未定义，不得读取或参与计算 |
| `openpbr_pdf` | 相对于立体角的 sampling PDF |
| `prepared.emission` | 宿主在最终表面合成中只加入一次，再按 VividRP 规则处理 pre-exposure |

Vendor 自带其 OpenPBR 多重散射和能量补偿。接入后不得再叠加 SimpleSlab 的 MS lobe、F0/F90 修正或 LUT 能量补偿。直接光的辐亮度与 attenuation、shadow 在 eval 之外相乘；采样路径的 MIS 和路径 throughput 由宿主负责。

本合约没有冻结实时 IBL 预积分、SSR 替换响应或面积光拟合。它们应以 OpenPBR 的 eval/sample 数值积分作为参考，并定义各自版本和误差验收，不能直接把现有 SimpleSlab 响应系数当成新的 OpenPBR 响应。后处理所需 Surface Summary 可以继续存在，但不是本合约权威输入，不能从 DiffuseAlbedo/F0 Summary 反推完整 OpenPBR 参数。

## StandardLit 兼容与后续迁移

现有 `StandardLitOpenPBRAdapter.hlsl` 和 PT 行为本轮保留。其 base diffuse roughness 与 specular roughness 都来自 `1 - smoothness`，再分别应用原有 saturation/floor；这是 legacy 映射策略。原生 V1 的漫反射粗糙度默认值是 `0`，与 specular roughness 独立。该 adapter 还承载现有纹理、Coverage、coat 和 transport 选择，因此它不等价于本 profile。

未来需要一个独立版本的 StandardLit → OpenPBR compatibility mapping，明确旧资产 smoothness、颜色、metalness、emission、Coverage 以及超出 V1 能力的处理，并让 Raster/PT 共享同一映射。迁移映射的版本与原生输入合约分别维护。不能在本轮静默改变旧材质的 PT 外观，也不能把旧图或 Catalog 的 SimpleSlab export 自动重解释为 OpenPBR。

下一阶段是 Native OpenPBROpaqueV1 authoring、typed output、版本化 payload 以及方向/点光生产闭环。之后补 IBL/面积光/SSR 验收，再切换默认创建、导入、预览和生产 evaluator。General Closure 的 Mix/Layer 组合另行定义，不能继承 DualSlab 当前的颜色反推 opacity 公式。

## 验收状态与剩余检查

冻结前已对照包内 Vendor 的 OpenPBR 1.1 参数默认值、eval/sample 约定、零粗糙度数值下限和 IOR LUT 外推代码。2026-10-07 的 focused checks 已通过：

- 21 个纯托管合约用例：默认值、字段顺序、边界、非法数值、未知能力位、法线、emission 乘积溢出、指纹及 C#/HLSL/Vendor 源码约定一致性；其中一个源码检查覆盖完整 Resolved record 的有限性和 dormant 默认值约束，不代表这些 shader 条件已在 GPU 执行。
- 热身后重复 4096 次默认输入创建与校验，当前线程 managed allocation 为 `0` 字节；这不是整帧分配测量。
- DXC `cs_6_0` 编译 3 个入口：输入/Resolved 校验、prepare/eval/pdf、sample。保留 Vendor `openpbr_aggregate_lobe.h` 的两类未初始化输出警告（`pdf`、`sampled_type`，3 次编译共 6 条），未改 Vendor 源码。编译通过不等于 GPU 数值或图像验收。

可在包目录复现：

```powershell
& '.\Tools~\Validate-OpenPBROpaque.ps1'
```

该工具需要 .NET 10 SDK、DXC，以及目标项目已导入的 Unity.Mathematics/NUnit 程序集；新版 Unity 的转发类型引用从目标项目生成的 `VividRP.Runtime.csproj` 解析。它运行实际合约和 NUnit fixture，并提取实际共享 hash utility 编译，不启动 Unity，不执行 GPU shader。生成的探针、DXIL 和日志保存在输出目录。

本合约冻结阶段尚不具备以下运行时验收证据：

- 原生图经过 Graph → IR/LIR → AOT → Catalog → VisibilityBuffer Resolve 的生产链路。
- Unity/GPU 的 direct lighting、白炉、roughness/metalness/IOR 扫描及掠射角对照。
- IBL、面积光、SSR 与 OpenPBR 数值参考的误差，以及固定曝光 HDR 对照。
- 原生/兼容材质的 Raster/PT 参数一致性与稳定帧 managed allocation 验收。

这些检查完成前，只能声明输入合约已冻结，不能声明 GPU 视觉一致、性能改善或新默认生产路径通过。
