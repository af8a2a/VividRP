# Agentic PIX M3 acceptance — 2026-09-28

Environment: `E:/VividRPSample`, Unity 6000.7.0b1, PIX 2606.18-preview,
Direct3D12, `Assets/Scenes/SampleScene.unity`, Main Camera, original
`Assets/New Vivid Render Pipeline Asset.asset` and
`Assets/_Recovery/Standard Vivid Render Graph.vrdg`.

## Verified in the real Editor

- Official pixtool launch injected PIX before the Unity D3D12 device; preflight
  confirmed the runtime's installation and native availability. Editor PID 32208
  and launcher PID 7816 identify this run only; do not reuse PIDs as ownership.
- The final workflow captured a normal camera render and validated all four
  logical queue scopes on three physical queues, with two nonzero work events
  under `VividRP.RenderPass.Record/5:HZBGeneratePass`.
- It selected Dispatch `(queueId=1, eventIndex=825, queueType=0/graphics)`, read
  one compute shader and a root signature with two parameters, and found the
  1920x1080 PreDepth SRV and HZB UAV. Accessed-resource replay also succeeded.
- Timing and the requested CS Invocations counter were collected. This single
  replay/event is not a whole-frame performance measurement or a bottleneck claim.
- The final Console interval contained no new errors, with no dropped/reset cursor.
- A nonexistent marker failed with `expected_pass_missing`, exactly one capture
  attempt, no analysis, and successful session release.
- Repeated captures succeeded. An in-memory clone with HZB Async Compute enabled
  was also captured twice without new Console errors, then the original pipeline
  was restored. **PIX still placed its target Dispatch on the graphics queue**;
  this does not satisfy real async-only VividRP workload acceptance.

## Fixes discovered through this run

1. PIX injects using a DOS 8.3 DLL path. `Path.GetFullPath` did not expand that
   alias and incorrectly rejected the same PIX installation. Runtime paths now
   expand through `GetLongPathName` before comparison.
2. Unity `CommandBuffer.Clear()` resets the async execution flag. Reused capture
   boundary recordings now restore it after every Clear. The .NET fake was
   corrected to reproduce the real behavior, so the queue regression check
   catches a recurrence.
3. Error monitoring now covers the preparation render and final boundary Submit;
   it previously detached before boundary errors could be observed. Workflow
   Console evidence adds a second acceptance check. The first exploratory capture
   is retained, but is not counted as clean acceptance because it logged the
   async flag error.

## Retained local evidence

These ignored artifacts are local, not shipped with the repository:

- [Final structured diagnosis](../Temp~/PIX-M3/final-e2e/diagnosis.json)
- [Ready capture and validation](../Temp~/PIX-M3/final-e2e/capture-ready.json)
- [Console interval](../Temp~/PIX-M3/final-e2e/console-after-1.json)
- [Capture](../Temp~/PIX-M3/final-e2e/capture-1.wpix)
- [Wrong-pass rejection](../Temp~/PIX-M3/negative-missing-pass/diagnosis.json)
- [Early injection launch](../Temp~/PIX-M3/launch-m3-2/diagnosis.json)
- [Async-option queue proof](../Temp~/PIX-M3/async-queue-proof.json)
- [Original pipeline restored](../Temp~/PIX-M3/restored-pipeline.json)

Final capture SHA-256:
`9c40aa391de262fbed85609485c84048d27e96b348d7b1219c490e0cbeaec2ef`.

## Remaining boundaries

Occupancy is explicitly unsupported (`E_NOTIMPL`); counter format 7 is retained
as undecoded raw bits. DXBC shader hashes/entry metadata were unavailable in this
SDK query. Full PSO/raster/RT coverage, async-only scene work, cancellation/domain
reload during an active capture and failure/skip camera matrices are not claimed
from this run. Startup had existing shader UAV-limit errors before the measured
Console interval; this task does not resolve those renderer/shader issues.

Validation includes native build/regression, focused C# compile, 35 session,
24 hook/queue/router, 31 M2 process checks, and the offline Python workflow suite.
The Editor imported/compiled and executed the changed production code. No Unity
Test Framework test run was started in the interactive Editor.
