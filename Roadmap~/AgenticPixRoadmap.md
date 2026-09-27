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

- Pipeline-owned frame boundaries including failure/skip reporting.
- Join all relevant async compute work; remove M0's explicit async-compute rejection.
- Merge the backend selector/session contract with existing agent debugger APIs.

## M2 — external analysis

Extend the current validate-only helper with events, pipeline/root signature,
shader identity, resources/views, timing, counters and Dr. PIX. Preserve the
acceptance gate and do not treat indirect maximum counts as executed GPU work.

## M3 — unattended workflow

Reliable early-injection bootstrap, capture/retry policy, marker-based diagnosis
and bounded event/resource results. The tested Preview API launcher stalled;
resolve that protocol before adopting it for unattended Editor startup.
