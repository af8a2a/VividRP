# Upstream provenance

This directory is a vendored source snapshot for the native mesh-shader plugins
shipped by VividRP.

- Repository: <https://github.com/af8a2a/Unity-MeshShader.git>
- Revision: `43be16b4b678b65d985f7ed0bcd0dbe22601cdb1`
- Snapshot date: 2026-08-28

The snapshot includes the build configuration, project license, documentation,
runtime and compiler sources, Unity native plugin headers, and the empty
external-dependency placeholder. It deliberately excludes the upstream Git
metadata, build outputs, and DXC SDK/runtime binaries.

Unity ignores this directory because its name ends in `~`. The prebuilt binaries
used by the package remain in:

```text
Runtime/SubSystem/Plugin/MeshShader/Plugins/x86_64/VividMeshShader.dll
Editor/SubSystem/Plugin/MeshShader/Plugins/x86_64/VividMeshShaderCompiler.dll
Editor/SubSystem/Plugin/MeshShader/Plugins/x86_64/dxcompiler.dll
Editor/SubSystem/Plugin/MeshShader/Plugins/x86_64/dxil.dll
```

When updating the native implementation, update the upstream repository first,
resynchronize this snapshot, rebuild the Release DLLs, and verify the packaged
binaries against the build outputs.
