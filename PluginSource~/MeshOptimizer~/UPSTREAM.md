# MeshOptimizer source

VividRP vendors the following unmodified upstream source snapshot. Git
metadata is intentionally omitted.

- source: https://github.com/zeux/meshoptimizer
- tag: `v1.1`
- commit: `dc9d09ed83e1004aef47a1c3c597e0ec64848a37`
- license: MIT (`Source/LICENSE.md`)

The shared library exports both stable and experimental APIs. Experimental
bindings are included for the meshlet codec, higher-level meshlet optimization,
meshlet index extraction, and opacity micromap processing described by the v1.1
README; their ABI may change in a future meshoptimizer release.

`MeshOptimizerBindings.cs` covers all 81 C functions exported by this build,
including indexing, cache/overdraw/fetch optimization, buffer codecs and
filters, simplification, analysis, meshlet and cluster processing, spatial
sorting, quantization, and the experimental APIs listed above.

Run `PluginSource~/Build-GeometryPlugins.ps1` from the package root to build
the Release DLL and copy it to the Unity plugin directory.
