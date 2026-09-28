# Vivid PIX capture, queue validation and external analysis

Optional Windows x64 tools. `VividPixCapture.dll` only captures; the external
`vivid-pix-analyzer.exe` validates and analyzes the resulting document through PIX API.
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
The harness includes three real compute queues with work only on Background,
graphics-to-compute start fences and compute-to-graphics tail joins. The native
completion must observe every tail before reporting captured. `build.ps1` also
runs M1 hook/queue/router and M2 request/process checks using the containing project's resolved
Newtonsoft.Json assembly. No Unity Test Framework or Editor launch is involved.

## Native contract

`Prepare` creates a generation token; pass that integer through
`IssuePluginEventAndData`, never a pointer to freed managed memory. Both plugin
events request `FlushCommandBuffers | SyncWorkerThreads` and submission-thread
queue access. Prepare signals a D3D12 fence after submission; `Poll` becomes Armed
only when the actual completion value is reached. Main-thread `Begin` is then
called before target-camera recording. Finish signals another fence; only its
completion starts background `PIXEndCapture(FALSE)`. Captured means End returned;
it is not Ready until external validation passes.
The plugin fence is on the direct queue. M1's managed pipeline boundary enqueues
all compute-tail waits before that event; the native fence alone is not a join.

Every HRESULT uses signed `FAILED(hr)` semantics, including device/fence errors.
Cancel closes only an owned capture. Stale callback tokens are ignored, busy
requests cannot replace ownership, and cancelled background completion is reaped
even after a managed domain reload. Device initialization/reset/shutdown callbacks
are paired with registration/unregistration. The plugin never late-loads PIX.

## Analyzer contract

```powershell
./Tools~/PIX/bin/vivid-pix-analyzer.exe validate PIX_INSTALL CAPTURE.wpix SESSION_ID EXPECTED_PASS NEW_RESULT.json all
```

Exit 0 means validated; 2 means a rejected capture; other exits indicate a tool
failure. JSON must also report `success=true`/`code=ok`. The output file is created
with `CREATE_NEW` and flushed; existing evidence is never overwritten.
All paths are resolved before selecting the PIX installation as the helper's
working directory (required by 2606.18-preview's factory dependency resolution).
The selected `pixapi.dll` is loaded by absolute path. Rebuild/retest for other PIX
Preview API versions; these COM interfaces are still a preview contract.

The last argument is `all` (M1 with compute queues) or `graphics` (no async hardware;
also the default for the standalone M0 smoke). Schema 2 reports `boundaryMode` and
`validatedQueueScopes`; M1 rejects old evidence that omits either field.
Acceptance requires exactly one ordered pair per logical scope: the original
graphics markers plus `/Default`, `/Background`, `/Urgent` suffixes in `all` mode.
Each pair must belong to one physical queue; compute scopes may alias each other
or graphics. Nonzero draw/dispatch work must appear below the exact expected pass
inside at least one scope. API calls are distinguished from markers using PIX's
`ApiCallData` XML; zero-dimensional calls, clears, copies, and marker-only frames
do not count. `ExecuteIndirect.MaxCommandCount` alone is not proof of GPU work;
the capture must expose a nonzero supported draw/dispatch. For indirect-only
passes without such records, validation conservatively fails.

Sources: [programmatic capture contract](https://devblogs.microsoft.com/pix/programmatic-capture/),
[Microsoft GPU Capture sample](https://github.com/microsoft/pix-samples/tree/main/api/preview/GpuCapture).
The installed SDK headers are authoritative for signatures.

## External analysis (M2)

```powershell
./Tools~/PIX/bin/vivid-pix-analyzer.exe events PIX_INSTALL CAPTURE.wpix SESSION_ID EXPECTED_PASS NEW_RESULT.json all --marker EXACT_MARKER --count 100 --request_id UNIQUE_ID
./Tools~/PIX/bin/vivid-pix-analyzer.exe pipeline PIX_INSTALL CAPTURE.wpix SESSION_ID EXPECTED_PASS NEW_RESULT.json all --queue QUEUE_ID --event EVENT_INDEX --capture_hash SHA256 --timeout_seconds 120 --request_id UNIQUE_ID
```

Actions: `events`, `event`, `pipeline`, `resources`, `accessed_resources`, `timing`,
`counters`, `occupancy`, `drpix`. Schema 3 reports result identity, capture digest,
HRESULT, explicit unsupported status and bounded data. All actions revalidate the
capture gate before inspection/replay. `--offset`/`--count` paginate catalogs;
count is 1..256. `--counter ID` collects one event counter; `--experiment GUID`
runs one event experiment. `--type INDEX --stage INDEX` collects occupancy points.
Use catalog output for IDs; queue-local event IDs are not collection offsets.

Exit 0 plus matching successful JSON means accepted; exit 2 means analysis failed,
4 is watchdog timeout, other nonzero exits indicate invocation/process failure.
Parsing errors and forced cancellation can leave no JSON; never interpret an
output file alone as success. The independent watchdog permits 1..300 seconds.
Analysis works without a running Unity Editor or injected capture process; replay
still needs compatible PIX/runtime/GPU. See [field coverage and current SDK
limitations](../../Editor/AgenticDebugger/PIX.md#m2-external-analysis).

Run against a directory printed by `gpu-smoke.ps1` (real GPU replay):

```powershell
./PluginSource~/PixCaptureNative~/tests/analysis-smoke.ps1 -PixInstall 'C:/Program Files/Microsoft PIX Preview/2606.18-preview' -CaptureDirectory 'ABSOLUTE_SMOKE_DIRECTORY'
```

The test retains unique JSON/log evidence and a summary. It expects a real async
dispatch, probes the analysis actions, and rejects stale session/pass/hash,
missing scope, marker-only capture and wrong event identity. Occupancy may pass
only as a successful query or an explicit unsupported HRESULT; other errors fail.
Current local results: 76 checks, occupancy E_NOTIMPL, counter format 7 retained
undecoded. Unity execution and raster/RT replay remain outside this smoke.

The .NET `Tests~/M2Checks` accepts optional arguments `ANALYZER PIX_INSTALL
ASYNC_VALID_CAPTURE` to exercise the production process/JSON adapter as well as
controlled child-process failure cases (33 checks versus 31 without arguments).

API references: [Microsoft GPU Counters sample](https://github.com/microsoft/pix-samples/tree/main/api/retail/GpuCounters),
[Dr. PIX sample](https://github.com/microsoft/pix-samples/tree/main/api/retail/DrPix).
The preview shader identity interface is taken from the same installed SDK's
`experimental/PixApiCommonExperimentalInterfaces.h`; no PIX DLL enters Unity's
managed analysis path.
