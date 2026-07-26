# VividRP Unity_NVAPI native source

This directory vendors the native source needed to rebuild
`Runtime/SubSystem/Plugin/NVAPI/Plugins/x86_64/Unity_NVAPI.dll`.
Unity ignores directories whose names end in `~`, so the C++ SDK files do not
become Unity assets.

Pinned upstream revisions:

- Unity plugin: `af8a2a/Unity_NVAPI@45ed387cac3eed9de313a4864c6ccf00947292b4`
- NVIDIA NVAPI: `NVIDIA/nvapi@9b181ea572f680327fe01a14a0f1f41c78034104`

The vendored NVAPI snapshot contains the public headers and the x64 import
library required by this plugin. The three shader-extension headers used by
Unity are also copied to `Shaders/Core/Private/NVAPI/`.

## Release build

From this directory, use Visual Studio 2022:

```powershell
cmake -S . -B build -G "Visual Studio 17 2022" -A x64
cmake --build build --config Release
```

The multi-configuration output is
`build/bin/Release/Unity_NVAPI.dll`.

Clang-cl with Ninja is also supported:

```powershell
cmake -S . -B build-clang -G Ninja `
  -DCMAKE_C_COMPILER=clang-cl `
  -DCMAKE_CXX_COMPILER=clang-cl `
  -DCMAKE_LINKER=lld-link `
  -DCMAKE_BUILD_TYPE=Release
cmake --build build-clang --config Release
```

The single-configuration output is `build-clang/bin/Unity_NVAPI.dll`.

After rebuilding, replace the prebuilt DLL in the runtime plugin directory.
The managed binding is maintained in
`Runtime/SubSystem/Plugin/NVAPI/NvApiSer.cs`; it intentionally uses the actual
CMake output name `Unity_NVAPI`, fixing the stale `UnityPlugin` name in the
upstream managed example.
