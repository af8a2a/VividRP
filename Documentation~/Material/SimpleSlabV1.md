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
