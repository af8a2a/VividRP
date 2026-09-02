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

The exact directional-albedo LUT representation is intentionally deferred to
Phase 8.3. Changing any convention above requires a new contract version and
fingerprint.

## Frozen calibration cases

| Case | Diffuse Albedo | F0 | Perceptual Roughness | Derived F90 | Alpha |
| --- | --- | --- | ---: | ---: | ---: |
| Black absorber | `(0, 0, 0)` | `(0, 0, 0)` | 0.5 | 0 | 0.25 |
| Low-F0 dielectric | `(0.18, 0.18, 0.18)` | `(0.01, 0.01, 0.01)` | 0.5 | 0.5 | 0.25 |
| Default plastic | `(0.18, 0.18, 0.18)` | `(0.04, 0.04, 0.04)` | 0.5 | 1 | 0.25 |
| Smooth plastic | `(0.18, 0.18, 0.18)` | `(0.04, 0.04, 0.04)` | 0 | 1 | 0.002 |
| Copper-like conductor | `(0, 0, 0)` | `(0.95, 0.64, 0.54)` | 0.25 | 1 | 0.0625 |

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
