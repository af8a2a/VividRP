# Ambient Occlusion

VividRP's `Ambient Occlusion` volume component can run either the existing
XeGTAO path or FidelityFX CACAO. Both implementations use the existing
`GTAOPass` RenderGraph node and continue to publish the `GTAOTexture` output,
so changing the implementation does not require rewiring a render graph.
The runtime Volume component type is `AmbientOcclusion`; profiles created with
the former `GTAO` type are migrated automatically.

## Selecting an implementation

1. Add or select the `Post-processing > Ambient Occlusion` volume component.
2. Enable the component.
3. Set `Implementation` to `GTAO` or `FidelityFX CACAO`.

Existing profiles remain on `GTAO` by default. `Quality Level` uses values
0-3 for GTAO. CACAO accepts 0-4; level 4 enables its adaptive importance-map
path. The custom inspector displays only the controls used by the selected
implementation.

## CACAO controls

- `CACAO Downsampled` reduces the internal AO resolution and enables CACAO's
  depth-aware bilateral upsampler.
- `CACAO Blur Passes` selects 0-8 edge-sensitive blur iterations.
- `CACAO Adaptive Quality Limit` controls the extra sample budget used only by
  quality level 4.
- Shadow multiplier, power, clamp, horizon threshold, detail strength, fade
  distances, sharpness, and bilateral sigma values map to the corresponding
  FidelityFX CACAO settings.

CACAO consumes HZB mip 0 as device depth and decodes world-space normals from
VividRP's `GBuffer1`. Its deinterleaved depth, normal, SSAO, importance-map,
and load-counter textures are transient RenderGraph resources.

The port is based on AMD FidelityFX CACAO and its Intel ASSAO-derived shader
code. License text and attribution are included in
`Shaders/Core/Private/CACAO/LICENSE.txt` and the package third-party notices.
