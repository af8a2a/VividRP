# VSM stage timing instrumentation

Production changes add four cached samplers. No shader work, resource layout or sampling rules change.

- `VSM.Resolve` is the existing outer inclusive total.
  - `VSM.ResolveTrace`: full receiver resolve, including selection, tracing, retries and adaptive-history lookup. It is not SMRT intersection alone.
  - `VSM.FilterHorizontal`: horizontal spatial filter.
  - Exactly one of `VSM.FilterVertical` (vertical spatial filter) or `VSM.FilterTemporalVertical` (merged vertical spatial filter and short history) follows.
- Those three executed children are non-overlapping siblings. The outer total also covers command setup between them. Do not add their medians to one another or to the parent median.
- `VSM.PageCull` is nested in both `VSM.StaticCasterCull` and `VSM.DynamicCasterCull`. Its recorder aggregates all executions in the completed profiling frame; do not add it to either inclusive parent. It is not a new stage added by this change.
- Other marker names in the helper describe the existing layout, request, allocation, invalidation, clear, raster and finalize scopes. Stages that do not execute must remain absent/zero-count, not be reported as measured zero cost.
- Bend contact-shadow composite is outside `VSM.Resolve`; these markers do not represent total lighting cost or total frame GPU time.

`VSMStageTimingCapture.cs.txt` is optional temporary Editor source, not a production dependency. Construct the helper before warm-up (for example capacity 128), call Observe once per selected-camera callback after warm-up, write CSV after capture, and dispose it. Reset permits the same storage/recorders to be reused after a configuration change and another warm-up. The helper owns independent ProfilerRecorder handles and does not change the cached samplers' recording flags. Observe uses preallocated arrays and struct samples; string formatting and file IO occur only in WriteCsv.

GPU profiler values are the last completed profiling frame, not necessarily the current camera callback frame. The CSV therefore labels its frame/time fields as observations. Preserve gpu_sample_count: Resolve count other than one cannot establish selected-camera attribution. PageCull may validly have two executions. Captures must keep scene/camera/settings stable across the GPU latency window, exclude transitions, and separate timings from screenshots, AsyncGPUReadback, compression and shader compilation. Compare per-stage distributions; use a directly scoped parent for totals. No hardware bottleneck conclusion is implied by elapsed times alone.

A focused non-Unity C# validation command already exists:

`powershell -NoProfile -ExecutionPolicy Bypass -File Roadmap~/Experiments/VSMSpatialAA_20260907/validate-csharp.ps1 -OutputDirectory Temp~/vsm-capacity-cost/csharp`

Run once temporary Editor sources and Unity Bee responses are stable. It independently rebuilds Runtime, Editor and Editor.Tests with the current Unity references, checks source/response hashes, and does not invoke Unity Test Framework.
`analyze-stage-timing.py <capture.csv> --output <summary.json>` parses those CSVs. It excludes inactive observations, repeated/rewound observed frame IDs, non-singleton resolve/trace counts, and inconsistent filter branch counts. Zero-count markers are reported as absent, and two PageCull executions remain one aggregated diagnostic value. It emits per-marker distributions and a paired parent-minus-children consistency diagnostic without constructing totals from stage medians. `--self-test` passes 9 focused parser checks; production C# compilation and GPU/GC sampling are delegated to the main capture workflow.

## Optional static scene runner

`VSMBaselineCostProbe.cs.txt` uses the stage helper and capacity audit in a separate explicit run. Import both C# texts and `VSMCapacityAudit.compute.txt` under `Editor/Tools` with Unity-generated metadata. Menus:

- `Tools/VividRP/Diagnostics/Run Temporary VSM Baseline Capacity`
- `Tools/VividRP/Diagnostics/Run Temporary VSM Baseline Timing`
- `Tools/VividRP/Diagnostics/Cancel Temporary VSM Baseline Cost`

Outputs are under `Packages/VividRP/Temp~/vsm-baseline/cost/yyyyMMdd_HHmmss_fff`, with `latest.txt` in the parent and progress/status in each run. Timing executes spatial, fixed temporal and adaptive forward, then adaptive, fixed temporal and spatial reverse, each 64 warm-up frames and 128 observations. Settings and frame metadata are written at the start of warm-up, then the timing callback performs no file IO or GPU readback. Stage CSV is written after collecting samples. Capacity captures one post-warm-up frame, dispatches the two audit kernels in order, and records pages/summary/owners/table/metadata/counters/projections as raw `.bin` files. It does not build RTAS or infer unique overflow from full slots.

The runner requires the actual 2048 / 256 / 16 production layout and a unique active Game camera. It uses a temporary hidden highest-priority Volume for VSM/SMRT activation and timing denoise variants, leaving resolution, budget, geometry, light and camera unchanged. Camera/light movement or a changed output size aborts. Cleanup handles completion, error, cancellation, reload and quit, drains its outstanding GPU requests, disposes its recorders/buffers and temporary assets, unsubscribes callbacks, and restores runInBackground. It never saves scenes or switches Play Mode. The two capture modes must be invoked separately from the dynamic-quality harness.
