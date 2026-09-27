# Vivid PIX M0 native backend

Optional Windows x64 tools. `VividPixCapture.dll` only captures; the external
`vivid-pix-analyzer.exe validate` opens the resulting document through PIX API.
Neither is part of the default Unity build. No generated `.meta` files are edited.

From the package directory, with Visual Studio C++ tools, CMake and .NET 10:

```powershell
./PluginSource~/PixCaptureNative~/build.ps1 -PixInstall 'C:/Program Files/Microsoft PIX Preview/2606.18-preview' -Deploy -BuildGpuSmoke
```

The script downloads and SHA256-verifies `WinPixEventRuntime 1.0.240308001` and
`Microsoft.Direct3D.D3D12 1.721.1-preview` from NuGet into the ignored
`PluginSource~/.build/pix/deps`. D3D12 preview headers are needed by this PIX API;
the Unity capture plugin continues to use the Windows SDK D3D12 interface.
The existing vendored Unity plugin interfaces in `NVAPINative~/PluginAPI` are
reused. `-Deploy` copies the plugin/runtime to `Editor/AgenticDebugger/Plugins/x86_64`
and the analyzer plus a dependency/hash manifest to `Tools~/PIX/bin`.
Those local build outputs are ignored; run this script on each checkout/machine.
If a loaded DLL is locked, close that Editor normally before deploying an update.

`build.ps1` runs ordinary C++/.NET checks, never Unity Test Framework. The optional
GPU smoke executable is **built** by `-BuildGpuSmoke`; it is only **run** explicitly:

```powershell
./PluginSource~/PixCaptureNative~/tests/gpu-smoke.ps1 -PixInstall 'C:/Program Files/Microsoft PIX Preview/2606.18-preview'
```

This uses the production `Capture.cpp`, a real D3D12 device and real PIX capture.
Unity interface delivery is simulated: it does **not** validate Unity rendering,
worker scheduling, or a VividRP scene. Captures and JSON evidence are retained.

## Native contract

`Prepare` creates a generation token; pass that integer through
`IssuePluginEventAndData`, never a pointer to freed managed memory. Both plugin
events request `FlushCommandBuffers | SyncWorkerThreads` and submission-thread
queue access. Prepare signals a D3D12 fence after submission; `Poll` becomes Armed
only when the actual completion value is reached. Main-thread `Begin` is then
called before target-camera recording. Finish signals another fence; only its
completion starts background `PIXEndCapture(FALSE)`. Captured means End returned;
it is not Ready until external validation passes.

Every HRESULT uses signed `FAILED(hr)` semantics, including device/fence errors.
Cancel closes only an owned capture. Stale callback tokens are ignored, busy
requests cannot replace ownership, and cancelled background completion is reaped
even after a managed domain reload. Device initialization/reset/shutdown callbacks
are paired with registration/unregistration. The plugin never late-loads PIX.

## Analyzer contract

```powershell
./Tools~/PIX/bin/vivid-pix-analyzer.exe validate PIX_INSTALL CAPTURE.wpix SESSION_ID EXPECTED_PASS NEW_RESULT.json
```

Exit 0 means validated; 2 means a rejected capture; other exits indicate a tool
failure. JSON must also report `success=true`/`code=ok`. The output file is created
with `CREATE_NEW` and flushed; existing evidence is never overwritten.
All paths are resolved before selecting the PIX installation as the helper's
working directory (required by 2606.18-preview's factory dependency resolution).
The selected `pixapi.dll` is loaded by absolute path. Rebuild/retest for other PIX
Preview API versions; these COM interfaces are still a preview contract.

Acceptance requires exactly one begin/end session marker, in order on the same
graphics queue, and nonzero draw/dispatch work below the exact expected pass marker
between those boundaries. API calls are distinguished from markers using PIX's
`ApiCallData` XML; zero-dimensional calls, clears, copies, and marker-only frames
do not count. `ExecuteIndirect.MaxCommandCount` alone is not proof of GPU work;
the capture must expose a nonzero supported draw/dispatch. For indirect-only
passes without such records, validation conservatively fails.

Sources: [programmatic capture contract](https://devblogs.microsoft.com/pix/programmatic-capture/),
[Microsoft GPU Capture sample](https://github.com/microsoft/pix-samples/tree/main/api/preview/GpuCapture).
The installed SDK headers are authoritative for signatures.
