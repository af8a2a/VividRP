# VividRP PIX agent workflow (M3)

`vivid_pix_agent.py` coordinates Unity capture and the external analyzer using
Python 3.10+ standard library, Unity CLI and the optional native tools. It produces
`diagnosis.json` with selected evidence, limits and links to raw JSON; agents do
not need to ingest the `.wpix` binary. Install/build instructions and SDK field
coverage are in [PIX.md](../../Editor/AgenticDebugger/PIX.md).

## Start a correctly instrumented Editor

Close the project normally first. Launch refuses an existing `UnityLockfile` and
never restarts or kills a user's Editor. Use the Editor version in the containing
project's `ProjectSettings/ProjectVersion.txt`. From the package directory:

```powershell
python -X utf8 ./Tools~/PIX/vivid_pix_agent.py launch --project E:/VividRPSample --pix 'C:/Program Files/Microsoft PIX Preview/2606.18-preview' --editor E:/Unity/6000.7.0b1/Editor/Unity.exe --output ./Temp~/PIX-M3/launch-new
```

This uses the official `pixtool launch` early injection path and leaves
`programmatic-capture --until-exit` supervising the Editor. It checks a live
`preflight` response for D3D12, VividRP, native availability and the exact PIX
installation. DOS 8.3 injection paths are normalized before comparison. A startup
timeout retains launcher PID/logs; inspect these before retrying, since Unity may
still be starting. The old experimental PIX API `LaunchProcess` helper is not used.

Open the desired scene in Unity (or through its discovered `open_scene` CLI
command) and keep the camera rendering. The workflow does not change the scene,
camera, Play Mode or serialized pipeline settings. Discover exact markers:

```powershell
unity command agentic_gpu_debugger --project-path E:/VividRPSample --backend pix --action preflight --format json
```

`preflight` exposes enabled cameras, active graph path, configured pass markers
and native runtime state. The `asyncCompute` field describes a graph option; it
is not proof of physical queue placement. Analysis events report SDK `queueType`
(0 graphics, 1 compute, 2 copy) and `gpuWork`, alongside `queueId`/`eventIndex`.

## Capture, inspect and diagnose

The tested SampleScene/graph has this exact HZB marker. For another graph, choose
its marker from preflight. Output directories must be new:

```powershell
python -X utf8 ./Tools~/PIX/vivid_pix_agent.py diagnose --project E:/VividRPSample --pix 'C:/Program Files/Microsoft PIX Preview/2606.18-preview' --output ./Temp~/PIX-M3/diagnose-new --camera 'Main Camera' --marker 'VividRP.RenderPass.Record/5:HZBGeneratePass' --question 'Inspect HZB dispatch and its resources' --timing --counter 'CS Invocations' --accessed
```

The workflow:

1. Checks preflight and takes a fresh capture using the existing session contract.
2. Polls to validated ready; checks a Console cursor for new errors or lost/reset
   evidence. Never accepts an empty/wrong capture or an incomplete Console interval.
3. Releases the capture owner, retaining disk evidence for independent analysis.
4. Pages events under the exact marker. Selects the first **actual nonzero GPU
   work** returned by PIX, or an explicit `--queue ID --event INDEX` from a prior
   event query. This selection is not a claim about the slowest event.
5. Reads event, pipeline and resource bindings. Adds replay only when requested:
   `--timing`, `--accessed`, `--occupancy`, or up to four `--counter 'Exact Name'`
   options. Counter lookup stops once requested names are found; it does not
   dump the whole catalog into the diagnosis.
6. Writes structured facts, a bounded shader/resource summary, raw evidence
   paths, truncation flags and limitations to `diagnosis.json` and stdout.

Choose counters for the question being investigated. No counter unit/encoding,
occupancy value or bottleneck is guessed. Current PIX returns `E_NOTIMPL` for
occupancy and format 7 for the tested counter; these remain explicit limitations.
Shader hashes can be unavailable for Unity's DXBC programs. `success=true` means
the required capture/inspection succeeded, with optional gaps listed separately.

Each analysis checks schema, request/session/path/pass/mode, queue-local identity,
exit status and the pinned SHA-256. Output is never overwritten. Defaults:
128 rows/page, 512 inspected marker events, 120 seconds per capture/query,
600 seconds overall plus bounded cleanup/grace. Configurable maxima: 256 rows,
4096 marker events, 300 seconds/query, 1800 seconds overall, four counters.
The diagnosis summarizes at most 16 shaders and eight resource views; raw bounded
responses remain alongside it. Resources are static bindings unless the separate
accessed-resource query succeeds.

`--attempts` is 1..3 (default 2). Only the transient `editor_busy` capture
precondition is retried; missing passes, empty captures, render errors and timeouts
are terminal and their evidence is preserved. Preflight errors fail immediately.
On cancellation/failure the workflow releases only its matching session. A lost
Editor or pending native cleanup is reported with the token for recovery.
No automatic relaunch, asset mutation or retry loop hides a semantic failure.

## Validation

```powershell
python -X utf8 ./Tools~/PIX/test_workflow.py
```

These are offline workflow tests, not Unity Test Framework tests. The real
2026-09-28 VividRP run is documented in
[the M3 acceptance report](../../Documentation~/AgenticPixM3Acceptance.md).
Native analyzer smoke commands remain in
[the native guide](../../PluginSource~/PixCaptureNative~/README.md).
