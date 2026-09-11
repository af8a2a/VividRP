# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length50_b — completed_with_warnings
Attempt: 0 | Observations: 446 | Active seconds: 4.0059391014739205 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 1 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 668/658/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Positive GPU timings below 1 ms retained as diagnostics; no numeric filtering applied.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 446/446 | 8.9664007537066936 | 9.5402002334594727 | available |
| cpu_frame_ms | 446/446 | 8.9822999999999986 | 10.0344 | available |
| gpu_frame_ms | 446/446 | 8.9506559999999986 | 9.471488 | available |
| cpu_render_thread_ms | 446/446 | 2.3213 | 3.1318 | available |
| gc_allocated_in_editor_frame_bytes | 446/446 | 14419 | 49561 | available |
| VSM.LayoutRemap_gpu_ms | 446/446 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 446/446 | 3.9868159999999997 | 4.286976 | available |
| VSM.Allocate_gpu_ms | 446/446 | 0.52889599999999992 | 0.555008 | available |
| VSM.InvalidateStatic_gpu_ms | 0/446 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 446/446 | 0.121344 | 0.12287999999999999 | available |
| VSM.StaticCasterCull_gpu_ms | 446/446 | 0.35072 | 0.607488 | available |
| VSM.DynamicCasterCull_gpu_ms | 446/446 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 446/446 | 0.32844799999999996 | 0.502784 | available |
| VSM.StaticRaster_gpu_ms | 446/446 | 0.007168 | 0.010496 | available |
| VSM.DynamicRaster_gpu_ms | 446/446 | 0.087551999999999991 | 0.094463999999999992 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/446 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 446/446 | 0.003328 | 0.004096 | available |
| VSM.ResetFeedback_gpu_ms | 446/446 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 446/446 | 0.38527999999999996 | 0.71039999999999992 | available |

