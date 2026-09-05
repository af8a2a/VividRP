# Vivid Simple Slab V1 — frozen contract

Phase 8.8 freezes the first default real-time BSDF and its Deferred integration.
This document describes the final implementation, superseding the intermediate
8.0–8.7 migration notes. Contract freeze and runtime acceptance are separate:
the GPU, resource-sync and image checks below must pass before declaring a
release validated.

## Version and change policy

| Contract/component | Frozen value | Source |
| --- | --- | --- |
| Slab inputs / fingerprint schema | 1 / 1 | `SimpleSlabContract` |
| Input convention fingerprint | `0x26E2E47BB6B790D8` | `SimpleSlabContract.Fingerprint` |
| Analytic BSDF kernel | 1 | `SimpleSlabBSDFKernelVersion` |
| Directional/punctual adapter | 2 | `SimpleSlabDirectLightingVersion` |
| Energy model | 1 | `SimpleSlabEnergyVersion` |
| Deferred evaluator | 4 | `SimpleSlabDeferredLightingVersion` |
| Native Slab LUT | 1, 64² RGBA16F, 4096 samples | `VividSlabLut` |
| Surface Summary / Dual Sidecar ABI | 1 / 1 | `SurfaceSummaryGBuffer.hlsl` |

The input fingerprint identifies the four inputs and evaluation conventions;
it is not a compiled-shader hash or a seal over the whole light loop. The
version set is pinned by `SimpleSlabContractTests`; C#/HLSL agreement is also
checked by the kernel, direct, energy and Deferred tests.

Changes to inputs, roughness/Fresnel conventions or the energy model require
reviewing the Slab contract version and fingerprint. Changes to lighting
semantics require the corresponding evaluator version and new regression
baselines. LUT channel/coordinate/format/integration changes require reviewing
its version and rebuilding it. A source-only refactor preserving results does
not require a new material program ID.

Evaluator version 4 makes reserved GeneralSlab/Subsurface/CatchAll material
classes diagnostic. It keeps Fast/Dual lighting formulas from version 3.
MaterialProgram compiler versions, Catalog hashes and serialized payloads are
unchanged: generated material programs export Slab inputs, while the pipeline
owns BSDF evaluation. A future export/layout change must follow the existing
Deferred Export and Catalog invalidation contracts.

## Supported material contract

V1 implements an opaque, isotropic Slab with one normal basis. The default
StandardLit authoring path and general MaterialProgram exports supply:

| Order | Semantic | Type | Range |
| --- | --- | --- | --- |
| 0 | `DiffuseAlbedo` | `float3` | finite, linear, [0, 1] |
| 1 | `SpecularF0` | `float3` | finite, linear, [0, 1] |
| 2 | `PerceptualRoughness` | `float` | [0, 1] |
| 3 | `NormalWS` | `float3` | finite, normalized |

Coverage, ambient occlusion, emission, diffuse irradiance, SSR and decal flags
are material outputs outside the BSDF. Alpha testing happens in Coverage.
No transmission, anisotropy, fuzz, clear coat or subsurface lobe is part of
this default kernel. Dual Slab supports exactly two Slabs using horizontal mix
or the approximate vertical layer described below.

| Export / condition | Default Deferred behavior |
| --- | --- |
| Empty / Unlit | Preserve emission; no BSDF or LUT required |
| FastSlab | One native Slab |
| DualSlab | Two native Slabs; valid Sidecar required |
| GeneralSlab / Subsurface / CatchAll material class | Pre-exposed magenta diagnostic |
| Error / unknown class / invalid Surface Summary tag | Pre-exposed magenta diagnostic |
| Missing LUT on lit pixels / missing Dual Sidecar | Pre-exposed magenta diagnostic |
| Sky | Compute clear samples sky; raster surface entrypoints return black/alpha one |

The CatchAll *tile variant* still shades mixed Fast/Dual/Unlit/Error tiles. It
does not imply support for the reserved CatchAll *material class*. Classification
selects one most-capable variant per tile; shading does not silently substitute
another BSDF for an unsupported class.

## Analytic and energy conventions

Directions are normalized caller inputs. Direct responses include `NdotL`;
backfacing view/light directions return zero. All colors use the active linear
working space.

- Lambert diffuse.
- Isotropic GGX with height-correlated Smith visibility.
- Schlick Fresnel with stored F0 and derived achromatic F90.
- `alpha = max(saturate(PerceptualRoughness)^2, 0.002)`.
- `F90 = saturate(50 * average(saturate(F0)))`.

`VividSimpleSlabBSDF.hlsl` owns the uncompensated analytic kernel.
`VividSimpleSlabEnergy.hlsl` adds a reciprocal Kulla-Conty-style MS lobe and
transmitted Lambert medium. For unit-Fresnel single-scatter directional albedo
`E`, loss `L=1-E`, Schlick-weighted albedo `B`, and cosine-weighted averages:

```text
Favg        = F0 + (F90 - F0) / 21
C           = Favg² * (1 - Lavg) / (1 - Favg * Lavg)
Sss(v)      = F0 * (1 - L(v) - B(v)) + F90 * B(v)
Sms(v)      = C * L(v)
S(v)        = Sss(v) + Sms(v)
f_ms(v,l)   = C * L(v) * L(l) / (pi * Lavg)
f_diff(v,l) = DiffuseAlbedo * (1-S(v)) * (1-S(l)) / (pi * (1-Savg))
```

Both evaluated lobes include `NdotL`. Zero-loss/transmission denominators are
guarded; no output luminance clamp hides energy gain. The model's white-
environment integral is `S(v) + DiffuseAlbedo * (1-S(v))`, bounded by one per
channel. White diffuse media and unit conductors preserve unit energy, subject
to LUT quadrature, interpolation and quantization error. Reciprocity applies
to individual Slabs; it is not claimed for the approximate vertical operator.

| Calibration case | Diffuse albedo | F0 | Roughness | F90 | Alpha |
| --- | --- | --- | ---: | ---: | ---: |
| Black absorber | (0, 0, 0) | (0, 0, 0) | 0.5 | 0 | 0.25 |
| Low-F0 dielectric | (0.18, 0.18, 0.18) | (0.01, 0.01, 0.01) | 0.5 | 0.5 | 0.25 |
| Default plastic | (0.18, 0.18, 0.18) | (0.04, 0.04, 0.04) | 0.5 | 1 | 0.25 |
| Smooth plastic | (0.18, 0.18, 0.18) | (0.04, 0.04, 0.04) | 0 | 1 | 0.002 |
| Copper-like conductor | (0, 0, 0) | (0.95, 0.64, 0.54) | 0.25 | 1 | 0.0625 |

## LUT and resource lifetime

The LUT uses linear RGBA16F, bilinear/clamp, no mips: 32 KiB persistent storage.
Coordinates are `x=sqrt(saturate(NdotV))`, `y=PerceptualRoughness`, on an
endpoint grid with half-texel remapping at sampling. Channels are
`(B, L, Bavg, Lavg)`. Loss is stored directly to preserve near-mirror precision.

The first compute dispatch uses 4096 deterministic Heitz visible-normal
samples per texel with the same alpha clamp and correlated Smith model as
direct lighting. It accumulates loss directly. The second dispatch integrates
the quantized rows using the exact cosine-weighted integral of their piecewise-
linear interpolation. Temporary bake storage is released after both dispatches.

`VividPreIntegratedFGDSystem` owns/prepares the native LUT and publishes it
through `VividPreIntegratedFGDData.slabLutTexture`; `DeferredLightingPass`
imports and binds it independently of the legacy FGD validity flag. Stable
frames reuse the texture and record no bake commands or managed allocations.
Device texture loss, shader replacement, source reimport/deletion/movement and
subsystem disposal invalidate it. A missing shader releases the cached LUT;
restoring it requires a fresh bake. `VividSlabLutPostprocessor` tracks all
direct bake includes across imported/deleted/moved/moved-from asset arrays.

The default subsystem no longer creates the legacy GGX/Disney and Charlie/
Fabric FGD textures. Their utility classes, resource fields and shader assets
remain available for explicit compatibility use. LTC is still a shared resource.

`VividRPCoreResources.SlabLutCompute` must be populated by the normal
`PipelineResourceUpdater` sync. In the target Unity project, import the package
and use the PipelineResources Inspector **Recollect** button if needed. Commit
Unity-generated metadata and the synchronized resource asset together. Never
hand-edit their GUIDs. The serialized-container regression checks the actual
runtime resource entry; merely finding the shader with AssetDatabase is
insufficient for a player build.

## Direct, indirect and final composition

Directional/punctual lights use the full energy-aware analytic kernel. Color,
distance/range/spot attenuation, main directional shadow and layer weights are
outside the BSDF. Only the main directional light consumes the resolved
directional shadow. Material AO times GTAO affects indirect lighting only.

Diffuse GI priority is baked irradiance, then APV, then ambient SH.
Indirect diffuse uses `DiffuseAlbedo * (1-S(v))`.
Environment single scattering uses the existing dominant reflection direction,
GGX roughness-to-mip convention and `Sss(v)`. Nonzero MS samples around N at
the roughest GGX mip, with amplitude `Sms(v)`. Each lobe traverses the
reflection-probe hierarchy independently; face fades depend on direction.
Probe radiance is already weighted and the uncovered weight belongs to sky.

The MS sample is a broad-lobe real-time approximation, not an exact Lambert
or directional-loss convolution. Constant-environment energy is preserved;
small bright environment features can differ from the direct kernel integral.

SSR supplies uncolored linear incident radiance plus confidence:

```text
specular = directSpecular + environmentMS
         + (1-confidence) * environmentSS
         + confidence * SSRradiance * Sss(v)
```

Environment SS/MS include indirect AO. SSR receives no additional AO factor.
Receive flags and the global enable gate confidence. Direct light, MS and
emission are preserved; the debug target shows pre-SSR indirect diffuse+SS+MS.

Rectangle/tube lights use the shared geometry, range, sidedness and barn-door
logic. GGX LTC weights single scattering; identity Lambert LTC weights MS and
transmitted diffuse. These broad shapes, the GGX fit and rectangle clipped-
sphere horizon approximation are retained approximations, not exact integration
of the full angular BSDF.

`VividDeferredLighting.hlsl` owns material decoding, Fast/Dual light loops
and final pixel composition for compute and both raster entrypoints. It returns
complete pre-exposed surface lighting, adding emission exactly once. Compute
clear initializes sky/emission for unscheduled pixels; scheduled pixels
overwrite with that complete result.

## Dual Slab composition

Sidecar V1 stores top diffuse albedo/F0/roughness/weight and shares the base
normal, AO and irradiance. Each Slab prepares its own LUT energy and LTC once;
light lists and resolved shadow samples are shared. With top weight `w`,
recovered top opacity `O`, top energy `S_top(v)` and Schlick `F_top`:

| Contribution | Horizontal base weight | Vertical base weight | Top weight |
| --- | --- | --- | --- |
| Directional / punctual | 1-w | 1-w + w*(1-F_top(v))*(1-F_top(l))*(1-O) | w |
| GI / environment SS+MS / area | 1-w | 1-w + w*(1-S_top(v))*(1-O) | w |
| SSR single-scatter response | 1-w | Same as environment | w |

SSR removes the weighted sum of both environment SS lobes and adds its radiance
times `baseWeight*Sss_base + w*Sss_top`. Both MS lobes remain. There is one
SSR trace/confidence signal for both layers, even when their roughness differs.

Opacity recovery retains the StandardLit metallic-workflow quadratic, including
its dielectric-biased dark-color ambiguity. The Sidecar has no independent
top normal or opacity. Exact physical vertical transport is outside V1.
Zero-weight materials export FastSlab upstream; quantized Sidecar alpha zero
is the missing/invalid sentinel and must not silently downgrade a Dual pixel.

## Integration boundary and cost

Production uses `DeferredLit.compute`; `DeferredDirectionalLightingPass`
inherits that path. The retained `SimpleDeferredLitPass.hlsl` and
`DeferredDirectionalLightingIndirectPass.hlsl` share its surface evaluator,
but have no production ShaderLab wrapper/caller in this repository. Their
test wrapper exercises fullscreen triangles and indexed pixel points.

PostSurfaceSummary consumers retain their existing ABI. Independent
Experimental Closure shading, ray-hit indirect lighting, legacy raster material
producers and the OpenPBR calibration renderer are outside this default path.
No production shader entrypoint includes `HdrpLitLighting.hlsl`.

FastSlab uses one view LUT sample plus one per analytic light, one LTC lookup,
and up to two environment/probe evaluations when MS is nonzero. Dual prepares
two Slabs; a valid Sidecar adds two texture loads. MS can add one sky lookup
and a second probe traversal per Slab. These are structural costs, not measured
GPU timings. Scene GPU time, full-render-loop allocation on all threads, and
quality/performance comparisons remain acceptance work.

## Validation and release acceptance

`Tools~/Validate-SimpleSlab.ps1` compiles against the real package/Core RP
includes. From the package root:

```powershell
./Tools~/Validate-SimpleSlab.ps1 -DxcPath C:/VulkanSDK/1.4.350.0/Bin/dxc.exe -CorePackageRoot ../CoreRP
```

It checks 27 Deferred compute/raster variants (APV off/L1/L2), three production
classification kernels, two bake kernels and seven focused test kernels.
It uses temporary include junctions and removes only those junctions and empty
directories. It does not import assets or run Unity tests.

| Gate | Coverage / required evidence |
| --- | --- |
| Contract and versions | `SimpleSlabContractTests`, C#/HLSL constants and fixed version set |
| Kernel | `SimpleSlabBSDFKernelTests`, frozen analytic baselines, reciprocity, backfaces |
| Direct lights | `SimpleSlabDirectLightingTests`, light scaling and native integration |
| Energy/LUT | `SimpleSlabEnergyTests`, actual bake, 90 furnace cases, 25 reciprocity cases, independent double quadrature, invalidation and stable/missing-source zero allocation |
| Deferred pixels | `SimpleSlabDeferredLightingTests`: 41 original compute cases, 8 raster scenarios, 5 reserved/invalid-class scenarios; raster scenarios exercise both entries and repeat draws |
| Render integration | `DeferredDirectionalLightingPassTests`, `PreIntegratedFGDFrameContextTests`, `MaterialProgramAotGpuTests` production RenderGraph and Resolve/Classify/Deferred routes |
| Serialized resources | `PipelineResourcesContainerEditorTests.FrozenSimpleSlabLut_IsPublishedByTheSerializedRuntimeContainer` |
| Visual acceptance | Fixed-exposure linear HDR captures: dielectric/metal, roughness sweep, grazing view, white environment, direct, probe/SSR and both Dual operators |
| Performance | Warm-frame Unity Profiler GC on all relevant threads; representative Fast/Dual GPU timings |

Furnace tolerance is 0.015 per channel; independent rough-lobe LUT quadrature
tolerance is 0.004; reciprocity error is below 2e-5. Deferred pixel tolerance
is 0.003 including emission/exposure. The Deferred oracle shares the separately
tested direct kernel but independently combines layer, environment and SSR
weights. Rectangle checks numerically integrate its vector form factor before
the retained horizon approximation. The older AOT routing test's synthetic
F90 LUT is not a physical furnace reference.

Use identical camera/normal fields, fixed exposure, no temporal accumulation,
and separate direct-white-light and white-environment captures. Preserve any
available pre-migration HDRP images as regression baselines. OpenPBR is the
physical reference for the common opaque/isotropic, coat/fuzz/SSS/transmission-
disabled subset; image equality with Unreal or OpenPBR is not a V1 requirement.

At the 2026-09-05 freeze handoff, 39 DXC entrypoints/variants and the managed
Runtime/Editor/test build passed (temporary installed a6 references). Thirty
pure managed numerical, version and source-invalidation checks also passed,
without invoking Unity Test Runner or native graphics APIs.

Target-project asset import/resource sync,
Unity GPU/GC tests, images and timings have not been verified. The checked-in
resource container still lacks the Slab LUT entry and several Phase 8 assets
await Unity-generated metadata. Unity MCP is connected to a different project;
its console state cannot certify this workspace. The target project/csproj
still reference 6000.7.0a5 while installed Unity references are a6. Managed
compilation uses temporary a6 references, without changing project versions.
A successful DXC/managed build does not close these acceptance gates.

## References

- [Unreal Substrate overview](https://dev.epicgames.com/documentation/en-us/unreal-engine/overview-of-substrate-materials-in-unreal-engine)
- In-repository OpenPBR 1.1: `Shaders/Material/ShaderPass/OpenPBR`.
- [Filament energy compensation derivation](https://google.github.io/filament/main/filament.html#materialsystem/improvingthebrdfs/energylossinspecularreflectance)
- [Heitz visible-normal sampling](https://jcgt.org/published/0007/04/01/paper.pdf)
