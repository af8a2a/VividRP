# Agentic PIX — M0/M1/M2/M3

For capture-to-diagnosis orchestration and official pixtool early injection, use
the [M3 agent workflow](../../Tools~/PIX/README.md). Real VividRP scene acceptance
and its remaining boundaries are recorded in the
[M3 report](../../Documentation~/AgenticPixM3Acceptance.md).

The backend uses explicit capture and a hard content gate. `success=true` for a capture
requires `state=ready`; an existing `.wpix`, a successful Begin/End HRESULT, or
GPU-fence completion alone is insufficient. The existing Frame Debugger backend
and its raster restrictions are unchanged.

## Setup and capture

1. Build/deploy the optional native tools using
   [the build guide](../../PluginSource~/PixCaptureNative~/README.md).
2. Launch the project's matching Unity Editor through **PIX Launch Win32**, with
   arguments `-projectPath E:/VividRPSample -force-d3d12`. The capturer must be injected
   before D3D12 device creation. Starting from Unity Hub and loading the DLL later
   does not satisfy this requirement. The bridge checks an already loaded capturer
   and requires its installation to match the selected analyzer PIX installation.
3. Open the target camera's Game/Scene view so it renders. **Enable Async Compute
   may remain enabled.** M1 joins the graphics and all three Unity compute queues;
   it does not change the pipeline asset. GPU fence support is required.
4. Use an actual, exact GPU pass marker containing nonzero draw/dispatch work.
   VividRP generates markers such as `VividRP.RenderPass.Record/<displayName>`;
   supply the display name from the active graph, not a guessed global pass name.

```powershell
$project = 'E:/VividRPSample'
$pix = 'C:/Program Files/Microsoft PIX Preview/2606.18-preview'
$analyzer = 'E:/VividRPSample/Packages/Custom_URP/Tools~/PIX/bin/vivid-pix-analyzer.exe'

unity command agentic_gpu_debugger --project-path $project --backend pix --action status --format json

# Replace EXACT_GPU_PASS_MARKER with a marker from this camera's active graph.
unity command agentic_gpu_debugger --project-path $project --backend pix --action capture --camera MainCamera --expected_pass 'EXACT_GPU_PASS_MARKER' --pix_install $pix --analyzer_path $analyzer --timeout_seconds 120 --format json

# Save data.result.sessionId from capture; poll until ready or failed.
unity command agentic_gpu_debugger --project-path $project --backend pix --action status --session_id SESSION --format json
unity command agentic_gpu_debugger --project-path $project --backend pix --action release --session_id SESSION --format json
```

The optional `PixCli` adapter supports `com.unity.pipeline >= 0.4.0-exp.1`.
It is separate from the existing Frame Debugger CLI assembly's version gate.
Editor C# on the main thread can call `GpuDebuggerBridge.Execute(...)` directly.
The existing `agentic_pix` and `agentic_frame_debugger` commands remain available.
`pix_install`/`analyzer_path` can alternatively come from `VIVID_PIX_INSTALL` and
`VIVID_PIX_ANALYZER`. Missing tools/runtime, ambiguous cameras and existing output
files fail before capture. Default output is `Temp/AgenticDebugger/PIX/<GUID>.wpix`
under the project; explicit paths must name a new `.wpix` file.

## Scope and lifecycle

M1 drains one normal target-camera render, then captures the **next normal target
render after the preparation fence passes**. It requests
existing views to repaint but does not call `Camera.Render`, select a window,
change Play Mode, enable Frame Debugger, or depend on Present/NumFrames.
If the target view never renders, the request times out instead of accepting an
unrelated Editor frame. No capture observer remains registered when idle/terminal.
The runtime's Editor-only hook reads an exclusive observer reference when idle;
player builds contain no observer calls. No serialized assets are modified.

Sequence:

```text
Preparing -> target camera submitted -> join compute tails -> native prepare event
  -> [native submission fence passed] -> Armed
  -> pipeline camera entry: PIXBeginCapture + graphics/compute FrameBegin markers
  -> normal VividRP camera work
  -> pipeline camera exit: require Submitted, compute end markers/fences
  -> graphics waits for all compute tails, FrameEnd + native finish event + Submit
  -> WaitingForGpu -> Finalizing (background PIXEndCapture)
  -> Validating (external PIX API process) -> Ready | Failed
```

Begin occurs on the main thread before public camera callbacks, culling jobs and
RenderGraph recording. The pipeline wrapper reports completion after Submit and
end-camera callbacks, and reports failure from its `finally` even if any of them
throws. Resource initialization skips, failed culling and RenderGraph failures
produce specific `target_camera_*` errors and cannot become Ready.

The current CoreRP compiler submits async passes to `ComputeQueueType.Background`.
Capture boundaries also cover Default and Urgent for subsystem/camera callbacks.
Start fences gate compute queues after the graphics begin marker; queue-tail
fences join all three back to graphics before the native finish fence. Boundaries
are outside RenderGraph and cannot be culled. Queue names may alias a physical
queue; each logical scope has its own marker pair. These are capture-only joins,
so captures are not evidence of unperturbed frame timing. Unity-managed upload
queues retain Unity's resource-dependency synchronization.

Native callbacks synchronize worker recording/submission and signal a real fence;
no `Graphics.WaitOnAsyncGraphicsFence` CPU-wait assumption or fixed serialization
sleep is used. Errors logged during the camera render, exceptions, missing target
work, missing/repeated/reversed session markers, stale results and timeouts fail.
Validation requires work **under the expected pass**, within the session's markers
on the same physical queue, which may be graphics or compute. `boundaryMode=all`
requires all four logical scope pairs and `validatedQueueScopes=4`. If the device
has no async compute, `boundaryMode=graphics` requires one pair. Missing scopes,
split-queue pairs and old analyzer evidence fail. Indices from different queues
are never compared as a global timeline; aliased scopes do not duplicate counts.

`release` requires the owner token. During cancellation, `cleanupPending=true`
holds native ownership until EndCapture completes; poll and release again.
Assembly reload, quit and Play Mode changes cancel capture. Native tokens survive
managed reload and stale plugin events cannot act on a subsequent session.
Partial/failed captures and `<capture>.validation.json` evidence are retained.
An analyzer crash/timeout or failed API call never becomes Ready. The captured
GPU work count is an acceptance count under the chosen pass, not a whole-frame
performance metric. This does not yet guarantee replay or analysis correctness.

## Shared agent contract

`agentic_gpu_debugger --backend pix|frame_debugger` returns `schemaVersion`,
`backend`, `sessionId` when owned, `state`, `code`, `ready` and `pending`, alongside
the backend's evidence. A pending capture has `success=false`, `pending=true`;
poll until `ready=true` or failure. Idle status is a successful query, not a capture.
Native PIX cleanup can remain pending after failure: inspect `cleanupPending` too.
Frame Debugger's historical commands retain their original response semantics.

Every unified session operation except preflight/status/capture requires its backend's
matching `session_id`; an explicitly supplied token on status is checked too.
PIX and Frame Debugger cannot acquire captures while the other owns a session,
including through legacy commands. User-enabled Frame Debugger also blocks PIX.
Frame Debugger still supports `events`, `select` and `event` for Compute/RayTracing
only; raster/native render-pass inspection and texture export remain disabled.

## M2 external analysis

After capture reaches `ready`, the unified command starts a separate
`vivid-pix-analyzer.exe` for each query. The Editor returns immediately with
`state=analyzing`, `analysisId` and `resultPath`. Poll **analysis_status** with both
owner tokens; capture `status` remains the capture's state. Only a matching JSON
result and exit code 0 make the analysis ready. One job runs per capture session.

```powershell
# Use the ready capture's sessionId. Marker filtering includes descendants.
unity command agentic_gpu_debugger --project-path $project --backend pix --action events --session_id SESSION --marker 'EXACT_GPU_PASS_MARKER' --count 100 --format json
unity command agentic_gpu_debugger --project-path $project --backend pix --action analysis_status --session_id SESSION --analysis_id JOB --format json

# Select queueId AND eventIndex from result.data.items; these are not global indices.
unity command agentic_gpu_debugger --project-path $project --backend pix --action pipeline --session_id SESSION --queue_id QUEUE --event_index EVENT --format json
# Poll the new analysisId, then repeat for resources, timing, etc.
unity command agentic_gpu_debugger --project-path $project --backend pix --action analysis_cancel --session_id SESSION --analysis_id JOB --format json
```

| Action | Selection and output |
| --- | --- |
| `events` | Captured scope only; optional exact `marker` and `queue_id`; `offset`, `count` (1..256), `nextOffset`. |
| `event` | `queue_id` + `event_index`; API call XML (16 KiB limit, truncation flag). |
| `pipeline` | Selected event: program type, root parameters/static samplers, shader IDs/stages/DXIL and PDB hashes when exposed, PSO subobject types and selected typed fields. |
| `resources` | Without event: capture resource inventory. With event: static views, bindings, resource descriptions and supported view fields; these are not proof of access. |
| `accessed_resources` | Selected event's views filtered using PIX replay's accessed-resource analysis. |
| `timing` | Selected event's top/EOP timings in **nanoseconds**, with unavailable values as null. |
| `counters` | Omit `counter_id` for paged catalog; specify ID plus event to collect one counter. |
| `occupancy` | Catalog of type/stage indices; both `occupancy_type` and `occupancy_stage` select paged points, optionally for one event. |
| `drpix` | Omit `experiment` for catalog; specify returned GUID plus event to run one experiment and return metrics/messages. |

All analysis results use schema 3 inside the common schema 1 job envelope and
include action, request ID, session, capture path, expected pass, boundary mode,
queue/event identity, HRESULT and SHA-256 `captureHash`. Each query reopens and
revalidates the M0/M1 content gate. The digest is checked before and after analysis;
changed captures fail. PIX 2606 requires releasing the helper's short read lock
before opening its document. Do not edit captures while querying them. The Editor
pins the first successful digest; `events_hash` can also explicitly require a
digest returned by a previous analysis.

`path` may name a **new .json** result file; otherwise the result is stored beside
the capture as `<capture>.analysis.<analysisId>.json`. Evidence is never overwritten.
Native timeout is 1..300 seconds (default 120), including opening, hashing and
replay; an independent watchdog writes timeout evidence and ends its process.
The Editor has a five-second watchdog grace interval. Cancel/release kills only
the owned analyzer process; cancellation may leave an incomplete output, which
must not be accepted. Domain reload/Editor exit detaches from an analysis rather
than killing it: the bounded helper can finish its JSON independently. To inspect
retained captures after Unity exits, use the standalone commands in the
[native guide](../../PluginSource~/PixCaptureNative~/README.md#external-analysis-m2).
There is no automatic reattachment after reload.

The adapter deliberately exposes a bounded subset of the Preview SDK. PSO
subobjects without serialized fields report `detailsAvailable=false`; root/shader/
binding collections report total counts when capped. Resource view descriptions
include common buffer/2D fields, not every union member. Raytracing shader tables,
full fixed-function PSO decoding, shader source/disassembly and texture export are
not implemented. Missing DXBC hashes/size remain null. A failed/unbound view
descriptor retains its HRESULT and `available=false` alongside its binding.

Observed with **2606.18-preview** on the local GPU: `GetOccupancy` returns
`E_NOTIMPL` (`unsupported=true`); this is not a zero-occupancy measurement.
The `CS Invocations` counter reports format `7`, outside the installed header's
format encoding; its `rawBits` and format are retained with `decoded=false` and
`valueText=null`. Known formats preserve raw bits plus a lossless textual value;
64-bit IDs/times are decimal strings. Dr. PIX metric labels are kept exactly as
returned by the SDK, without inferred unit conversion. Timing/counter/Dr. PIX
queries replay on the local GPU using the captured adapter's ordinary defaults.

## Verification boundary (2026-09-28)

- MSVC builds the capture DLL and analyzer against installed PIX Preview
  **2606.18-preview**; 30 C++ validation checks and 35 .NET session checks pass.
- The real D3D12 smoke uses production native capture code: two consecutive valid
  graphics captures and two consecutive captures with work only on Background
  pass PIX API validation. Marker-only, missing-end (including async), stale-session,
  wrong-pass, mode mismatch and nonexistent-file cases fail (13 checks). Native busy ownership,
  stale callbacks and cancelled-domain recovery are also checked.
  Unity plugin interface delivery is simulated in that smoke.
- C# core and CLI compile offline against Unity **6000.7.0b1** and the project's
  actual Pipeline package. The full Runtime assembly also compiles using Unity's
  own response file, references and generators, with outputs isolated from Library.
- 24 standalone M1 checks cover exclusive/stale hook ownership, skip/failure
  notification, queue scheduling, cross-backend ownership and routing. Idle hooks
  allocate 0 B over 100,000 warmed calls in .NET. Unity calls are simulated in these
  checks; this is not an Editor whole-frame allocation measurement.
- The existing 10 Frame Debugger policy/serialization checks pass against the
  newly compiled Editor assembly; raster inspection remains excluded.
- M2: 31 standalone request/process checks cover identity/schema, queue-local
  indices, hashes, Windows argument quoting, timeout, cancellation, detach and
  evidence preservation; 33 checks with the optional production analyzer run.
  Full Editor/CLI assemblies compile with Unity's real response files, separately
  from the focused C# compile (zero warnings/errors).
- 76 real PIX analysis checks pass on the retained async D3D12 capture: event
  pagination/filtering, pipeline/root/shader identity, static/accessed resources,
  timing, counter catalog/value, Dr. PIX catalog/experiment and negative content/
  identity checks. Occupancy is checked as explicitly unsupported on this SDK;
  no occupancy points are claimed. Raster/RT analysis is not GPU-validated here.
- Actual VividRP camera capture, Unity's worker/submission integration, Scene/Game
  view behavior and domain reload during capture were not covered by the M1/M2
  standalone checks. M3 has since verified a real SampleScene normal-camera
  capture/analysis path; see the report above for the remaining async/failure matrix.
  Unity Test Framework was not run in the active Editor.
- The separate PIX API LaunchProcess experiment hung with a suspended test child;
  that experimental launcher was removed. This integration uses PIX's UI launch
  prerequisite in M0/M1. M3 now uses the verified official `pixtool launch` path.

The original old fork isn't a dependency of this package; its Present/Delay code
remains in that external repository. This backend supersedes that capture path
without patching the unrelated legacy UnityBindless exports.

Queue API references: [Unity GraphicsFence](https://docs.unity.com/en-us/engine/6000.7/script-reference/unityengine/rendering/graphicsfence),
[ScriptableRenderContext.ExecuteCommandBufferAsync](https://docs.unity.cn/2023.2/Documentation/ScriptReference/Rendering.ScriptableRenderContext.ExecuteCommandBufferAsync.html).
