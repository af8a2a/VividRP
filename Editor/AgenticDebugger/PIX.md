# Agentic PIX — M0

M0 adds explicit capture and a hard content gate. `success=true` for a capture
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
3. For M0, set the VividRP asset's **Enable Async Compute** to false. The command
   rejects enabled async compute and never silently changes the asset. M1 will
   implement multi-queue joins. Open the target camera's Game/Scene view so it renders.
4. Use an actual, exact GPU pass marker containing nonzero draw/dispatch work.
   VividRP generates markers such as `VividRP.RenderPass.Record/<displayName>`;
   supply the display name from the active graph, not a guessed global pass name.

```powershell
$project = 'E:/VividRPSample'
$pix = 'C:/Program Files/Microsoft PIX Preview/2606.18-preview'
$analyzer = 'E:/VividRPSample/Packages/Custom_URP/Tools~/PIX/bin/vivid-pix-analyzer.exe'

unity command agentic_pix --project-path $project --action status --format json

# Replace EXACT_GPU_PASS_MARKER with a marker from this camera's active graph.
unity command agentic_pix --project-path $project --action capture --camera MainCamera --expected_pass 'EXACT_GPU_PASS_MARKER' --pix_install $pix --analyzer_path $analyzer --timeout_seconds 120 --format json

# Save data.result.sessionId from capture; poll until ready or failed.
unity command agentic_pix --project-path $project --action status --format json
unity command agentic_pix --project-path $project --action release --session_id SESSION --format json
```

The optional `PixCli` adapter supports `com.unity.pipeline >= 0.4.0-exp.1`.
It is separate from the existing Frame Debugger CLI assembly's version gate.
Editor C# on the main thread can call `PixCaptureBridge.Execute(...)` directly.
`pix_install`/`analyzer_path` can alternatively come from `VIVID_PIX_INSTALL` and
`VIVID_PIX_ANALYZER`. Missing tools/runtime, ambiguous cameras and existing output
files fail before capture. Default output is `Temp/AgenticDebugger/PIX/<GUID>.wpix`
under the project; explicit paths must name a new `.wpix` file.

## Scope and lifecycle

This M0 captures the **next normal render of the selected camera**. It requests
existing views to repaint but does not call `Camera.Render`, select a window,
change Play Mode, enable Frame Debugger, or depend on Present/NumFrames.
If the target view never renders, the request times out instead of accepting an
unrelated Editor frame. No callbacks remain subscribed when idle/terminal.
No per-frame code or serialized pipeline/resource assets are modified.

Sequence:

```text
Preparing -> [native submission fence passed] -> Armed
  -> target beginCameraRendering: PIXBeginCapture + unique FrameBegin marker
  -> normal VividRP camera work
  -> target endCameraRendering: FrameEnd marker + native finish event + Submit
  -> WaitingForGpu -> Finalizing (background PIXEndCapture)
  -> Validating (external PIX API process) -> Ready | Failed
```

Begin occurs on the main thread before VividRP records this camera's commands.
Native callbacks synchronize worker recording/submission and signal a real fence;
no `Graphics.WaitOnAsyncGraphicsFence` CPU-wait assumption or fixed serialization
sleep is used. Errors logged during the camera render, exceptions, missing target
work, missing/repeated/reversed session markers, stale results and timeouts fail.
Validation requires work **under the expected pass**, within the session's markers
on the same queue, not merely another Editor draw elsewhere in the file.

`release` requires the owner token. During cancellation, `cleanupPending=true`
holds native ownership until EndCapture completes; poll and release again.
Assembly reload, quit and Play Mode changes cancel capture. Native tokens survive
managed reload and stale plugin events cannot act on a subsequent session.
Partial/failed captures and `<capture>.validation.json` evidence are retained.
An analyzer crash/timeout or failed API call never becomes Ready. The captured
GPU work count is an acceptance count under the chosen pass, not a whole-frame
performance metric. This does not yet guarantee replay or analysis correctness.

## Verification boundary (2026-09-27)

- MSVC builds the capture DLL and analyzer against installed PIX Preview
  **2606.18-preview**; 21 C++ validation checks and 30 .NET session checks pass.
- The real D3D12 smoke uses production native capture code: two consecutive valid
  captures pass PIX API validation; marker-only, missing-end, stale-session,
  wrong-pass and nonexistent-file cases fail (7 checks). Native busy ownership,
  stale callbacks and cancelled-domain recovery are also checked.
  Unity plugin interface delivery is simulated in that smoke.
- C# core and CLI compile offline against Unity **6000.7.0b1** and the project's
  actual Pipeline package. The initial project version was 6000.7.0a3; it was
  changed externally while this work was running, and is not changed by this backend.
- Actual VividRP camera capture, Unity's worker/submission integration, Scene/Game
  view behavior and domain reload during capture still need a PIX-launched Editor
  run. Unity Test Framework was not run in the interactive Editor.
- The separate PIX API LaunchProcess experiment hung with a suspended test child;
  that experimental launcher was removed. M0 uses PIX's supported UI launch
  prerequisite; an unattended bootstrap is still follow-up work.

The original old fork isn't a dependency of this package; its Present/Delay code
remains in that external repository. This backend supersedes that capture path
without patching the unrelated legacy UnityBindless exports.
