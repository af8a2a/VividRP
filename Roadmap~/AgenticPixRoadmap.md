# Agentic PIX roadmap

## M0 — explicit capture and reject empty/wrong captures

Implemented: optional native capture backend, begin/end HRESULT checks,
submission/worker synchronization, fence polling, bounded session lifecycle,
unique target-camera markers, external PIX API validation, JSON evidence,
native/.NET regression checks and a real D3D12 smoke harness.

Validated: native compilation, C# compilation with actual Unity references,
standalone real GPU positive/negative capture acceptance. The D3D12 smoke uses
the production capture implementation and simulated Unity interface delivery.

**Pending M0 acceptance:** run consecutive captures of a real VividRP camera in
a PIX-launched Editor, verify the configured expected pass, then exercise timeout,
wrong session, cancellation and domain reload. Do not mark the full M0 milestone
complete based only on standalone smoke. See
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

**Pending M1 acceptance:** in a PIX-launched VividRP Editor, capture the same camera
twice with async compute enabled and select a known nonzero async pass. Confirm four
validated queue scopes, then check disabled async, failed/skipped camera, cancellation
and domain reload. M0's scene acceptance remains pending too. No whole-scene or
whole-frame allocation/performance claim is made from standalone tests.

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

**Remaining acceptance/coverage:** exercise capture -> events -> selected event
analysis through a PIX-launched VividRP Editor. Raster/RT GPU analysis, full PSO
fixed-function decoding and shader-table bindings are not validated/implemented
by this bounded first adapter. See [precise field coverage and SDK limits](../Editor/AgenticDebugger/PIX.md#m2-external-analysis).
M0/M1 scene acceptance remains pending; indirect maximum counts still do not
qualify as executed GPU work. No performance improvement is claimed.

## M3 — unattended workflow

Reliable early-injection bootstrap, capture/retry policy, marker-based diagnosis
and bounded event/resource results. The tested Preview API launcher stalled;
resolve that protocol before adopting it for unattended Editor startup.
