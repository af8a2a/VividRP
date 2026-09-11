# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length10_b — completed_with_warnings
Attempt: 0 | Observations: 275 | Active seconds: 4.0105062996031791 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 275 | Median interval (s, 0=unavailable): 0.014452249858276645
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/665/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Positive GPU timings below 1 ms retained as diagnostics; no numeric filtering applied.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 275/275 | 14.454293996095657 | 15.827996656298637 | available |
| cpu_frame_ms | 275/275 | 14.5439 | 16.2152 | available |
| gpu_frame_ms | 275/275 | 0.017152 | 0.018432 | available |
| cpu_render_thread_ms | 275/275 | 0.1859 | 0.2642 | available |
| gc_allocated_in_editor_frame_bytes | 275/275 | 20637 | 103378 | available |
| VSM.LayoutRemap_gpu_ms | 275/275 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 275/275 | 6.986496 | 7.5013119999999995 | available |
| VSM.Allocate_gpu_ms | 275/275 | 0.527104 | 0.53632 | available |
| VSM.InvalidateStatic_gpu_ms | 0/275 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 275/275 | 0.122112 | 0.130304 | available |
| VSM.StaticCasterCull_gpu_ms | 275/275 | 0.361984 | 0.61004799999999992 | available |
| VSM.DynamicCasterCull_gpu_ms | 275/275 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 275/275 | 0.34227199999999997 | 0.535552 | available |
| VSM.StaticRaster_gpu_ms | 275/275 | 0.007168 | 0.011007999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 275/275 | 0.086272 | 0.092928 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/275 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 275/275 | 0.004352 | 0.005376 | available |
| VSM.ResetFeedback_gpu_ms | 275/275 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 275/275 | 1.816832 | 2.0544 | available |

