# NRD REBLUR shader provenance

This directory contains the shader-side subset of NVIDIA Real-time Denoisers
(NRD) v4.16 required by the `REBLUR_DIFFUSE_SPECULAR` integration.

- Reference checkout: `E:\NRD-Sample_simplex`
- Reference revision: `a805a0d2f9464f41790f4ad6ea952cc8fbf47917`
- Upstream subtree: `External/NRD/Shaders`
- Included dispatches: ClassifyTiles, HitDistReconstruction (3x3/5x5), PrePass,
  TemporalAccumulation, HistoryFix, Blur and PostBlur
- Deliberately deferred: temporal stabilization, checkerboard permutations,
  SH/occlusion variants and validation dispatches

The Unity `.compute` files in the parent directory are VividRP wrappers that
select `NRD_SIGNAL=BOTH`, `NRD_MODE=RADIANCE` and the no-temporal-stabilization
PostBlur permutation. Resource declarations remap NRD's `gNearestClamp` name to
Unity's equivalent recognized inline sampler name, `gPointClamp`; this is the
only local change inside the copied shader subset.

This software contains source code provided by NVIDIA Corporation. See
[`../LICENSE.txt`](../LICENSE.txt) for the NVIDIA RTX SDK license that
accompanied the reference checkout.
