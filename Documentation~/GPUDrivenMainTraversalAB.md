# Main-view traversal: moving roof camera A/B

Date: 2026-10-15. Source: `63c85764ceff5fc366c776c3d7aea0ba029838ba`.

## Comparison and controls

- A: the retained flat `GPUInstanceCulling.CS` entry point, without main-view
  hierarchy traversal. B: `CSMainHierarchyEnqueue`, indirect
  `CSMainHierarchyTraverse`, then exact cluster selection.
- Both modes use the same current meshoptimizer assets, 16B shared group table,
  final selection, meshlet culling, shadow/VSM path, materials and render graph.
  This isolates the main traversal change; it is not an old-asset/new-asset test.
- Unity 7000.0.0a9, D3D12, NVIDIA GeForce RTX 5070 Ti, Sponza / Camera_1.
  Output 1920x1080; TSR Balanced internal depth 1130x636. FOV 59.999985 degrees,
  near/far 0.3/1000, LOD pixel budget 1, forced depth -1. These are the current
  session's rendering settings, not the original no-AA screenshot settings.
- The previous 399-pose roof route is reused: distance 6 to 100 and back,
  oscillations around 42.295 and 98.353, and stationary holds on both sides.
  Rotation is Euler (43.272297, 304.76556, 1.6177362). Position is
  (6.181255, 18.638472, -10.503865) + forward * (12 - distance).
- Four image runs use A/B/B/A order. Each has 192 stationary warmup renders and
  399 captured poses. Captures include final PNG, R32 depth, GPU-evaluated exact
  main/shadow LOD predicates, and actual visible/recovered draw requests/arguments.
- TSR's 24-phase jitter is replayed identically, including warmup, by a temporary
  jitter-index override. TSR remains enabled and jitter evolves normally. Global
  pipeline frame tokens are not changed. The first unaligned attempt is retained
  under `unaligned/` but excluded: A1/B1 had different jitter phases. Its A1/B2
  and A1/A2 pairs, which happened to share a phase, had identical depth and main
  LOD probe words at all 399 poses. The formal run controls this input explicitly.

## Geometry and temporal results

All four runs completed: 1,596 captured frames, 4,548 instance/node cases per
pose, one main view and 17 shadow views. Each view has 5,351 reconstructed
finest-to-root paths. Across 153,723,528 path/view/pose checks:

- Zero missing cuts, overlapping ancestor/descendant selections or inconsistent
  shared-group decisions.
- Zero duplicate actual draws across visible + recovered lists, and zero draws
  from nodes rejected by the exact LOD predicate.
- A1/B1 and A2/B2 have byte-identical depth, identical main/shadow probe words,
  and identical actual draw sets at all 399 poses. Lost/gained coverage and
  changed depth pixels are zero. A1/A2 and B1/B2 repeat controls also match.
- Every run has the same 8,664 main node-selection changes, including 870 roof
  node changes. No additional geometry flicker, false rejection, LOD hole or
  overlap was introduced by staged traversal on this route.

Final RGB is not bit-identical. Mean absolute differences, in 8-bit channel units:

| Comparison | RGB MAE | Temporal residual MAE |
| --- | ---: | ---: |
| A1 / B1 | 0.025105 | 0.042325 |
| A2 / B2 | 0.023745 | 0.039861 |
| A1 / A2 repeat | 0.022781 | 0.038194 |
| B1 / B2 repeat | 0.024963 | 0.042145 |

Temporal residual is the frame-to-frame change of the matched A/B image
difference, excluding the first frame. Its scale is comparable to same-mode
repeats. Contact sheets and the synchronized 30 fps video retain the existing
upper-right debug rectangle. Exact matching of depth and draw sets is the stronger
geometry result; these color statistics are not a proof of zero shading noise.

There is a **shared baseline threshold instability** under current TSR settings:
at stationary poses 254-265, `Arch_H001` cases 831/832/840/841/842 switch selection
nine times each (45 changed node flags), identically in A and B, as temporal
jitter changes. The other three holds have zero selected-node changes. Depth
naturally varies within holds with jitter; identical-depth acceptance therefore
compares the two modes at the same pose/phase, not adjacent jittered frames.
The near/far return also crosses different jitter phases and has the same 20
node-flag differences in every run. This validation establishes no *new*
instability; it does not claim the baseline scene is free of LOD oscillation.

The captured scene uses complete resident refinements. This rendered A/B does
not extend the prior synthetic absent-refinement checks to asynchronous streaming
or partially published groups.

## GPU timing

Timing runs are separate from image capture: no screenshots, diagnostic compute
dispatches or GPU readbacks. Six paired windows alternate BA/AB order. Each
window has 192 stationary warmup renders, one 399-pose moving warmup cycle,
and two measured moving cycles (798 observations). All 9,576 observations are
valid: all six GPU markers have positive values and sample count one. No timing
window or outlier is removed. Each mode contributes 4,788 observations.

The 95% intervals below use six paired window-mean differences with Student's t
(5 degrees of freedom), rather than treating correlated frames as independent.

| GPU scope | Flat A | Staged B | B - A | Paired 95% interval |
| --- | ---: | ---: | ---: | ---: |
| Camera total | 2.290421 ms | 2.325852 ms | +35.430 us | -43.083 to +113.944 us |
| PreGraph | 1.126678 ms | 1.140144 ms | +13.466 us | +4.051 to +22.881 us |
| RenderGraph | 1.162684 ms | 1.184674 ms | +21.990 us | -48.806 to +92.786 us |
| Main culling total | 21.352 us | 33.262 us | +11.911 us | +10.492 to +13.330 us |
| Instance + hierarchy traversal | 4.262 us | 16.416 us | +12.153 us | +11.701 to +12.606 us |
| Cluster final selection | 5.211 us | 5.237 us | +0.026 us | -0.161 to +0.213 us |

The staged path has **no demonstrated performance benefit on this workload**.
Main culling costs 55.8% more, or about 11.9 us in absolute time. The added cost
is concentrated in instance/hierarchy processing; reduced candidate work does
not produce a measurable reduction in final selection time. Whole-camera time
is 1.55% higher on average, but its interval crosses zero, so neither a stable
whole-camera regression nor a speedup is established by these runs.

These are GPU timestamps inside the camera, covering pregraph, render graph and
camera completion; Editor UI is outside the scope. ProfilerRecorder observations
are delayed: the stored CPU pose is not asserted to identify the same GPU frame.
GPU clocks were not locked. Before/after window telemetry spans 57-63 C and
2782-2910 MHz; these endpoint samples are not continuous clock traces. Per-window
means, p50/p95/p99/max values, paired differences and telemetry are retained.
Results apply to this Sponza route and settings, not larger scenes or streamers.

## Evidence and restoration

Local artifacts are under `Temp~/main-traversal-ab/` (Git-ignored):

- `report/report.html`, `roof-route-ab.mp4`, contact sheet and timing plots.
- `report/geometry-summary.json`, `timing-summary.json`, `timing-windows.csv`.
- `A1/`, `B1/`, `B2/`, `A2/`: raw per-frame evidence and configuration.
- `TA1/` through `TA6/`, `TB1/` through `TB6/`: raw timing and GPU telemetry.
- `route.json`, `route-info.json`, analysis scripts and archived instrumentation
  in `harness/`; source hashes in `source-manifest.json` and `source-restored.json`.

The three temporarily instrumented production files were restored byte-for-byte
against the saved pre-validation hashes. The temporary Editor assets were removed
through AssetDatabase. Play Mode, camera transform, timing settings and screenshot
directory were restored; the scene's pre-existing dirty state was preserved and
the scene was not saved. Final Unity inspection reports compilation successful,
not compiling and zero current console errors. Unity Test Framework was not run
in the open interactive Editor; these results come from live rendered GPU probes.
