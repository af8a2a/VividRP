# Vivid Simple Slab V1

Phase 8 uses this contract to replace the HDRP-derived deferred BSDF without
changing the Surface Summary GBuffer ABI. The contract describes an opaque,
isotropic, single-normal-basis Slab. It is intentionally smaller than Unreal
Substrate's full Slab and OpenPBR Surface.

## Frozen inputs

All color values are finite, non-negative values in the active linear working
color space. Values written through the Surface Summary ABI are representable
in `[0, 1]`.

| Order | Semantic | Type | Range |
| --- | --- | --- | --- |
| 0 | `DiffuseAlbedo` | `float3` | `[0, 1]` |
| 1 | `SpecularF0` | `float3` | `[0, 1]` |
| 2 | `PerceptualRoughness` | `float` | `[0, 1]` |
| 3 | `NormalWS` | `float3` | finite, normalized before evaluation |

Ambient occlusion, emissive radiance, baked diffuse irradiance, coverage, SSR,
and decal policy are material outputs outside the BSDF. AO affects indirect
lighting only. Emission is added after direct and indirect lighting.

## Frozen evaluation conventions

- Diffuse model: Lambert.
- Specular model: isotropic GGX with height-correlated Smith visibility.
- Fresnel model: Schlick with stored `F0` and derived achromatic `F90`.
- Energy model: directional-albedo multiple-scattering compensation plus
  interface transmission applied to the diffuse medium.
- The directional BSDF response includes the `NdotL` cosine.
- `alpha = max(saturate(PerceptualRoughness)^2, 0.002)`.
- `F90 = saturate(50 * average(saturate(F0)))`. A gray `F0` of `0.02`
  therefore reaches an `F90` of one without adding a GBuffer field.

The directional-albedo LUT representation is specified below by Phase 8.3.
Changing any convention above requires a new contract version and fingerprint.

## Frozen calibration cases

| Case | Diffuse Albedo | F0 | Perceptual Roughness | Derived F90 | Alpha |
| --- | --- | --- | ---: | ---: | ---: |
| Black absorber | `(0, 0, 0)` | `(0, 0, 0)` | 0.5 | 0 | 0.25 |
| Low-F0 dielectric | `(0.18, 0.18, 0.18)` | `(0.01, 0.01, 0.01)` | 0.5 | 0.5 | 0.25 |
| Default plastic | `(0.18, 0.18, 0.18)` | `(0.04, 0.04, 0.04)` | 0.5 | 1 | 0.25 |
| Smooth plastic | `(0.18, 0.18, 0.18)` | `(0.04, 0.04, 0.04)` | 0 | 1 | 0.002 |
| Copper-like conductor | `(0, 0, 0)` | `(0.95, 0.64, 0.54)` | 0.25 | 1 | 0.0625 |

## Phase 8.1 analytic kernel

`VividSimpleSlabBSDF.hlsl` owns the first Vivid-native BSDF implementation. It
has no dependency on HDRP's `BSDF.hlsl` or the legacy
`HdrpLitLighting.hlsl`. Version 1 provides:

- Lambert diffuse;
- isotropic GGX normal distribution;
- height-correlated Smith visibility;
- the V1 derived-F90 Schlick Fresnel function;
- colored diffuse and specular directional responses that include `NdotL`.

The normal, view direction, and light direction are normalized caller inputs.
Backfacing view or light directions return zero. Directional-albedo
multiple-scattering compensation, IBL integration, LTC area lights, and the
production Deferred switch remain outside the 8.1 kernel boundary.

## Phase 8.2 direct lighting

The production deferred Fast Slab and Dual Slab paths evaluate directional and
punctual lights through `VividSimpleSlabDirectLighting.hlsl`. Light color,
direction normalization, resolved directional shadows, punctual distance/range
attenuation, spot attenuation, and dual-slab weights are applied outside the
BSDF kernel.

HDRP-derived code remains responsible for indirect lighting, reflection probes,
SSR integration, and LTC area lights. The two paths accumulate separately so
the HDRP post-evaluation stage cannot recolor Lambert output or apply its
specular energy compensation to Vivid's direct response.

## Phase 8.3 energy and Vivid Slab LUT

`VividSimpleSlabEnergy.hlsl` version 1 adds a reciprocal Kulla-Conty-style
multiple-scattering lobe and a transmitted Lambert medium. This is Vivid's
chosen real-time approximation, not a claim of binary or image equivalence
with Unreal or an exact microfacet random walk. The analytic 8.1 kernel remains
available as the uncompensated single-scattering baseline.

For unit-Fresnel single-scatter directional albedo `E`, loss `L = 1 - E`,
Schlick-weighted albedo `B`, and cosine-weighted hemispherical averages:

```
Favg       = F0 + (F90 - F0) / 21
C          = Favg^2 * (1 - Lavg) / (1 - Favg * Lavg)
S(v)       = F0 * (1 - L(v) - B(v)) + F90 * B(v) + C * L(v)
f_ms(v,l)  = C * L(v) * L(l) / (pi * Lavg)
f_diff(v,l)= DiffuseAlbedo * (1-S(v)) * (1-S(l)) / (pi * (1-Savg))
```

`f_ms` is added to the GGX single-scattering lobe; both final lobes include
`NdotL` on output. The normalized diffuse transmission is Vivid's opaque-medium
approximation: its directional integral is `DiffuseAlbedo * (1-S(v))`.
Therefore the combined white-environment response is
`S(v) + DiffuseAlbedo * (1-S(v))`, bounded by one per channel. A unit conductor
or a white diffuse medium preserves unit energy. This statement is exact for
the model's integrals, subject to numerical integration/interpolation error in
the implementation. Zero-loss denominators are guarded; no output luminance
clamp is used to hide energy gain. Reciprocity applies to each individual Slab,
not to the existing approximate vertical layer operator.

### LUT representation and lifetime

- `64 x 64`, linear `RGBA16F`, bilinear/clamp, no mips: 32 KiB persistent storage.
- `x = sqrt(saturate(NdotV))`, `y = saturate(PerceptualRoughness)`, endpoint grid
  with half-texel remapping at sampling. Stored channels: `R=B`, `G=L`,
  `B=Bavg`, `A=Lavg`. Storing loss avoids cancellation near a perfect mirror.
- First compute dispatch uses 4096 deterministic visible-normal samples per
  texel with the same alpha clamp and height-correlated Smith model as direct
  lighting. Loss is accumulated directly, not subtracted from a rounded sum.
- Second dispatch integrates each quantized directional row with the exact
  cosine-weighted integral of its piecewise-linear interpolation. The average
  channels therefore describe the same texture used by the light loop.
- `VividSlabLut` owns the texture; the existing frame FGD subsystem prepares it,
  and Deferred imports/binds it through frame context. Build scratch storage is
  released after the two dispatches. Stable frames allocate/dispatch nothing
  for LUT preparation. Device texture loss, source reimport, shader replacement,
  and subsystem disposal invalidate it. Relevant shader includes are tracked
  by `VividSlabLutPostprocessor`.
- `VividRPCoreResources.SlabLutCompute` is collected by the normal resource sync
  pipeline. Generated `PipelineResources.asset` must not be hand-edited. A
  missing LUT produces magenta on lit Slab pixels instead of silently using an
  incompatible legacy LUT. Unlit/emission clearing is unaffected.

### Production bridge and remaining work

Fast and Dual Slab directional/punctual lights now use the energy-aware kernel.
View-dependent energy is prepared once per Slab, and light-dependent LUT data
is read per light. AO and exposure remain outside the kernel.

The existing environment/probe/SSR/LTC infrastructure consumes `S(v)` for
specular and `1-S(v)` for diffuse, with the old HDRP specular compensation
disabled. LTC diffuse uses the identity Lambert transform. These paths use a
directional-albedo approximation: the broad MS energy still shares the
specular prefilter/LTC shape. Separating its environment convolution and
fitting dedicated area-light shapes remain later work; Phase 8.3 does not
claim exact arbitrary-environment or area-light agreement. Existing Dual Slab
composition weights are unchanged. No Surface Summary, material ABI, or
Frozen Catalog payload changes are required.

`SimpleSlabEnergyTests` covers the real GPU bake, 90 white-furnace cases
(including black absorber, low F0, copper, and minimum roughness), reciprocity,
independent double-precision hemisphere quadrature, source/format contracts,
device-loss/source invalidation, and warmed zero-allocation preparation.
Furnace tolerance is 1.5% for GPU integration and half-precision LUT error.
The existing direct-light test uses a synthetic zero-loss LUT to isolate light
scaling; the earlier AOT/SSR routing test uses an explicitly synthetic F90 LUT,
not a physical white-furnace reference.

## Phase 8.4 main FastSlab Deferred switch

`EvaluateDeferredFastSlabLighting` is now the production FastSlab entrypoint.
It consumes the Surface Summary directly and uses the Vivid-native preparation,
indirect lighting, LTC adapter, and composition in
`VividSimpleSlabDeferredLighting.hlsl` (version 1). It no longer constructs
`VividGBufferSurfaceData` / `VividLitBSDFData` or calls the legacy
`GetVividPreLightData`, `EvaluateBSDF_Env`, `EvaluateBSDF_Area`, or
`PostEvaluateBSDF` functions. There is no FastSlab legacy fallback switch.

The new entrypoint applies albedo, interface transmission, and specular energy
only at their lobe evaluations. Its result contains colored direct/indirect
radiance plus the SSR factor, with no emission or pre-exposure. The main pass
keeps the existing rules:

- Material AO times GTAO affects indirect lighting only.
- Baked diffuse irradiance takes precedence over APV, then ambient SH fallback.
- Reflection-probe hierarchy radiance is already weighted and is not weighted
  a second time. Remaining probe weight is filled by the sky.
- SSR replaces indirect specular only; it does not erase direct highlights or
  reapply AO. Receive-SSR flags still gate this operation.
- `ClearDeferredLit` adds emission once, including Unlit pixels. Exposure is
  applied once at the final render-target boundary. Missing LUT and invalid
  exports retain diagnostic output.

Neutral sky sampling and area-light geometry were extracted into
`VividSkyLighting.hlsl` and `VividAreaLightCommon.hlsl`, shared with legacy
consumers without changing their formulas. Dual Slab remains on the explicit
legacy adapter for now, using the 8.3 Slab energy weights. The legacy headers
and FGD resources are therefore still present for that path; this phase does
not claim to remove HDRP-derived shading repository-wide. The IBL dominant
direction, prefilter convention, and LTC GGX fit are unchanged. Separately
convolving multiple-scatter energy or replacing LTC fits remains later work.

No changes to MaterialClassification's tile policy, the GBuffer ABI, material
program IDs, Frozen Catalog assets, or render-loop managed allocations are
required. Mixed tiles still select the most capable deferred variant; FastSlab
pixels within a CatchAll tile use the same new FastSlab entrypoint.

`SimpleSlabDeferredLightingTests` records packed GBuffer inputs, the production
tile classifier and indirect-argument builder, then production Clear/Deferred
dispatches. It covers arbitrary colored F0, rough conductor, low F0, AO-zero,
Unlit, sky, mixed Error/FastSlab tiles, missing LUT, directional shadow,
punctual/rectangle lighting, partial/full SSR and receive flags, emission,
pre-exposure, and independence from legacy FGD contents. The lighting oracle
uses the 8.3 direct kernel, explicit uniform-environment arithmetic, and the
8.3 legacy area adapter rather than the new FastSlab composition. These are
focused shader tests, not a replacement for the existing full-SRP pixel tests.

## Phase 8.5 IBL, reflection probes, SSR and area lights

The FastSlab deferred evaluator is now version 2. It separates the LUT's
directional specular energy into single scattering `Sss(v)` and multiple
scattering `Sms(v) = C * L(v)`, retaining `S(v) = Sss(v) + Sms(v)` for direct
lighting and diffuse transmission. This does not change the Slab contract,
LUT format/bake, Surface Summary, or material/Catalog fingerprints.

### Environment and probe integration

- Single scattering uses the existing GGX dominant direction and perceptual
  roughness-to-mip convention, multiplied by `Sss(v)`.
- Multiple scattering independently samples the normal direction at perceptual
  roughness one (the widest available sky/probe prefilter), multiplied by
  `Sms(v)`. It no longer follows the narrow GGX reflection. Zero MS energy
  bypasses this extra evaluation.
- Each lobe evaluates probe hierarchy coverage independently: probe face fades
  depend on direction. The existing sorted probe order, influence volume,
  box projection, atlas mapping/padding, multiplier, and invalid-entry fallback
  are retained. Already-weighted probe radiance is added once; only uncovered
  weight samples the sky contribution.
- Baked irradiance / APV / ambient SH priority and diffuse transmission are
  unchanged. Material AO times GTAO multiplies the two environment lobes once.

This is an explicit **real-time broad-lobe approximation**, not a new
directional-loss convolution. The roughest GGX mip is not an exact Lambert
convolution or the exact `L(l)` convolution of the 8.3 direct model. Constant
environment energy is preserved, but agreement for small bright environment
features is not claimed. There are no new textures, cache invalidation rules,
or per-frame managed allocations. Nonzero MS adds one sky lookup and one
additional probe hierarchy traversal (up to one atlas lookup per contributing
probe); exact GPU cost still needs scene profiling.

### SSR replacement contract

SSR supplies linear, uncolored incident reflection radiance plus confidence;
the receiving material's response is applied in Deferred, not the SSR producer.
FastSlab now exports an explicit `screenSpaceReplaceableSpecularLighting`:

```
environmentSS = SS radiance * Sss(v) * AO
environmentMS = broad radiance * Sms(v) * AO
specular      = directSpecular + environmentMS
              + (1-confidence) * environmentSS
              + confidence * SSRradiance * Sss(v)
```

Receive-SSR flags and the global enable still gate confidence. SSR does not
erase MS, direct lighting, or emission, and receives no additional AO factor.
Pre-exposure is applied once at the output boundary. The indirect debug target
continues to show pre-SSR diffuse plus both environment specular lobes.

### Area-light adapter

Directional/punctual lighting retains the full reciprocal 8.3 kernel. Rectangle
and tube lights now use the existing GGX LTC shape only for `Sss(v)`. MS uses
the identity Lambert LTC shape with amplitude `Sms(v)`; transmitted diffuse
uses that same broad shape with amplitude `DiffuseAlbedo * (1-S(v))`.
Thus all three lobe amplitudes preserve the same white-environment budget,
without assigning the MS energy to the narrow highlight. Range attenuation,
rectangle sidedness, barn doors and the existing tube geometry are retained.

Both the Lambert MS shape and the diffuse transmission shape remain
directional-albedo approximations, not exact integrations of the 8.3 angular
kernel. The existing GGX fit and rectangle clipped-sphere horizon approximation
are retained; a dedicated loss-lobe fit, exact convolution and angular-error
calibration remain future work. Dual Slab deliberately remains on its explicit
8.3/8.4 legacy environment/area/SSR adapter; this phase changes FastSlab only.

### Regression coverage

`SimpleSlabDeferredLightingTests` now contains 20 production pixel cases plus
the source-boundary test. In addition to the 8.4 cases, it uses mip-colored sky
and two probe atlas slices to distinguish narrow/broad samples, partial probe
coverage and sky fill, saturated overlapping coverage, multipliers, invalid
entries, and differing N/R face fades. Partial/full/disabled SSR checks preserve
MS, AO policy and receive flags. Rectangle, tube, backface, range and barn-door
cases use a synthetic non-identity GGX transform distinct from Lambert.
The rectangle oracle numerically integrates the vector form factor at 64 x 64
samples and then applies the retained horizon approximation; it does not call
the new area-light adapter or claim an exact physical-rectangle reference.
The tube test retains the existing line primitive and independently combines
its lobe weights. These are production Classify + indirect Deferred shader
tests, not full-SRP captures or proof of the physical accuracy of the fits.

## Image validation

For image baselines, use a linear HDR target, fixed exposure, no temporal
accumulation, the same camera and normal field, and separate direct-white-light
and white-environment captures. The current HDRP-derived deferred output is the
regression baseline; the existing OpenPBR path tracer is the physical reference
for the opaque, isotropic, coat/fuzz/SSS/transmission-disabled common subset.

Image equality with Unreal or OpenPBR is not part of V1. Required invariants are
finite non-negative output, reciprocity of the BSDF kernel, monotonic roughness
broadening, normal-incidence F0, derived grazing response, and white-furnace
energy not exceeding one.

## References

- [Unreal Engine: Overview of Substrate Materials](https://dev.epicgames.com/documentation/en-us/unreal-engine/overview-of-substrate-materials-in-unreal-engine)
- The in-repository OpenPBR 1.1 implementation under
  `Shaders/Material/ShaderPass/OpenPBR` is the calibration oracle.
- [Filament: energy compensation derivation](https://google.github.io/filament/main/filament.html#materialsystem/improvingthebrdfs/energylossinspecularreflectance)
  documents the Kulla-Conty additional lobe used here, rather than Filament's
  later scaled-GGX approximation.
- [Heitz: Sampling the GGX Distribution of Visible Normals](https://jcgt.org/published/0007/04/01/paper.pdf)
  supplies the visible-normal sampling construction used for baking.
