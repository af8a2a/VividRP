# RTX Texture Filtering in Reference Path Tracing

The StandardLit reference path tracer integrates NVIDIA RTX Texture Filtering
(RTXTF) from `Shaders/ThirdParty/RTXTF`. The vendored source is taken from the
RTXTF library supplied in `E:\RTXTF\libraries\RTXTF-Library`; its NVIDIA license
is retained beside the shader headers.

## Scope

- RTXTF is enabled by default for opaque StandardLit surface texture reads in
  `ReferencedPathtracingDXR`.
- Alpha-tested, transparent, and virtual-textured materials retain their
  visibility-safe sampling path. RTXTF's upstream documentation identifies
  alpha maps and off-screen visibility buffers as problematic stochastic uses.
- Ray-cone LOD selection remains owned by the reference path tracer. RTXTF
  stochastically selects the texel and fractional mip sample at that explicit
  LOD.
- Collaborative magnification is disabled in closest-hit shaders because a
  ray wave does not guarantee coherent screen-space lanes.

## Settings

Use the **VividRP/Path Tracing/Reference Path Tracing** volume component:

- **Enable RTXTF** toggles stochastic texture filtering.
- **RTXTF Filter** selects Linear, Cubic, or Gaussian reconstruction.
- **RTXTF Gaussian Sigma** controls the Gaussian footprint in texels.

Linear converges to the existing hardware-filtered result and is the default.
Cubic and Gaussian create a noisier per-frame signal and need temporal
accumulation or ray reconstruction to resolve. Changing any RTXTF setting is
part of the integrator signature and therefore invalidates old accumulation.
