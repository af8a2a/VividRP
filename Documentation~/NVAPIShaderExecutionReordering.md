# NVIDIA Shader Execution Reordering

The Reference Path Tracing pass has an optional NVIDIA Shader Execution
Reordering (SER) variant for Windows x86_64 and Direct3D 12.

## Enabling SER

1. Use Direct3D 12 on Windows with an NVIDIA GPU and driver that report SER
   support.
2. Add or select `VividRP/Path Tracing/Reference Path Tracing` in the active
   Volume profile.
3. Enable **Shader Execution Reordering**.

The pass queries both the driver opcode and GPU thread-reordering capability.
If the native plugin, graphics API, driver, GPU, or shader variant is
unavailable, VividRP logs one warning and dispatches the standard `TraceRay`
variant.

SER wraps material-heavy surface rays with `NvTraceRayHitObject`,
`NvReorderThread`, and `NvInvokeHitObject`. Visibility rays keep the standard
path because they skip closest-hit shading and do not benefit from the same
material-coherence reordering.

The SER variant declares NVAPI's `g_NvidiaExt` instruction UAV at `u31`.
VividRP binds a one-element, 256-byte counter buffer before dispatch so Unity's
ray-tracing resource validation sees the property as initialized; NVAPI
consumes the extension operations on supported NVIDIA drivers.

Switching SER does not reset reference accumulation: it changes scheduling,
not the estimator or sample sequence.

## Vendored source and binary

- Managed binding and prebuilt plugin:
  `Runtime/SubSystem/Plugin/NVAPI/`
- NVAPI shader headers:
  `Shaders/Core/Private/NVAPI/`
- Rebuildable C++ plugin and NVAPI SDK snapshot:
  `NVAPINative~/`

Pinned revisions and Release build commands are recorded in
`NVAPINative~/README.md`.
