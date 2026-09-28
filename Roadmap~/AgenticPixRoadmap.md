# Agentic PIX roadmap

## M0 — explicit capture and reject empty/wrong captures

Implemented: optional native capture backend, begin/end HRESULT checks,
submission/worker synchronization, fence polling, bounded session lifecycle,
unique target-camera markers, external PIX API validation, JSON evidence,
native/.NET regression checks and a real D3D12 smoke harness.

Validated: native compilation, C# compilation with actual Unity references,
standalone real GPU positive/negative capture acceptance. The D3D12 smoke uses
the production capture implementation and simulated Unity interface delivery.

**Real scene progress (M3):** consecutive SampleScene camera captures and wrong-pass
rejection passed in a PIX-launched Editor, with fresh Console error checks.
**Remaining M0 acceptance:** live timeout, stale-owner/cancellation and domain reload
during capture. Do not mark the full matrix complete from positive scene runs. See
[usage and exact boundaries](../Editor/AgenticDebugger/PIX.md).

## M1 — full VividRP frame integration

Implemented:

- Pipeline-owned camera entry/exit, with submitted, skipped and failed outcomes;
  a `finally` covers camera callbacks, culling, recording and submission failures.
- Capture-only fences join Default, Background and Urgent compute queues before
  native completion. Async compute remains enabled; validation accepts target work
  on compute queues and requires all logical scope markers, including aliased queues.
- `agentic_gpu_debugger --backend pix|frame_debugger`, common readiness/ownership
  fields, cross-backend exclusion and legacy-command compatibility. Frame Debugger's
  raster restrictions remain in place.

Validated: full Runtime compilation with Unity 6000.7.0b1's actual response file;
Editor/CLI compilation; standalone hook/queue/router checks (idle hooks 0 B after
warmup); 13 real D3D12/PIX acceptance cases including consecutive async-only work.
These checks simulate Unity interface delivery; they do not prove Editor execution.

**Real scene progress (M3):** repeated camera captures validate four scopes. Real
execution exposed and fixed command-buffer async flags being reset by Clear.
**Remaining M1 acceptance:** async-only VividRP work, disabled async, failed/skipped
camera, cancellation and active-capture domain reload. Enabling HZB async in a
temporary graph still produced a graphics-queue Dispatch; settings are not queue
evidence. No whole-scene allocation/performance claim is made.

## M2 — external analysis

Implemented: independent `vivid-pix-analyzer` queries for events, pipeline/root
signature, shader identity, resources/views, timing, counters, occupancy and
Dr. PIX; unified Editor/CLI asynchronous job start/poll/cancel. Capture gate is
rechecked on every query. Queue-local event identity, SHA-256, pagination, unique
result files and bounded process deadlines prevent stale/unbounded results.
Analysis can finish after Editor exit/reload; saved captures can also be analyzed
directly without Unity. Capture/replay remain separate processes.

Validated: full Editor/CLI compilation; 31 process/request checks (33 with the
production analyzer); 76 real analysis checks on an async D3D12 capture, including
replayed timing, accessed resources, one counter and one Dr. PIX experiment.
Prior 13 real capture gate checks still pass. Occupancy returns E_NOTIMPL on the
installed SDK/GPU; counter format 7 is preserved as raw evidence, not decoded.

**Real scene progress (M3):** capture -> events -> selected event pipeline/resources
-> timing/accessed resources/counter succeeded on VividRP SampleScene.
**Remaining coverage:** raster/RT GPU analysis, full PSO
fixed-function decoding and shader-table bindings are not validated/implemented
by this bounded first adapter. See [precise field coverage and SDK limits](../Editor/AgenticDebugger/PIX.md#m2-external-analysis).
M0/M1 failure/async matrices remain pending; indirect maximum counts still do not
qualify as executed GPU work. No performance improvement is claimed.

## M3 — agent workflow

Implemented: [Python workflow](../Tools~/PIX/README.md), official pixtool early
injection bootstrap, live preflight, capture/poll/release, bounded marker/work
selection, pipeline/resources inspection, question-directed optional counters and
replay queries, and structured diagnosis. SHA-256/session/queue identity, Console
error evidence, deadlines, explicit retry policy and retained failure artifacts
are enforced. The stalled Preview API launcher is superseded by the tested
official CLI path; existing user Editors are never silently restarted.

**End-to-end passed:** Unity 6000.7.0b1 + VividRP SampleScene + PIX 2606.18-preview;
two HZB work events, four scope pairs, selected compute Dispatch, two resource
views, pipeline/root, accessed resources, timing and one requested counter. Wrong
pass is rejected without retry. Original assets/settings were restored after the
temporary async-option experiment. [Evidence and exact limits](../Documentation~/AgenticPixM3Acceptance.md).

No automatic GPU bottleneck attribution or unattended Editor crash/restart recovery
is claimed. Occupancy/counter-format limitations and actual graphics queue placement
are carried in evidence rather than converted into fabricated metrics.
