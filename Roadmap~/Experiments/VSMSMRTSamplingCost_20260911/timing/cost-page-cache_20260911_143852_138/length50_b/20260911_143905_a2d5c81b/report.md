# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length50_b — completed_with_warnings
Attempt: 0 | Observations: 220 | Active seconds: 4.0038801941609989 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 220 | Median interval (s, 0=unavailable): 0.018183297902496065
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/665/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Positive GPU timings below 1 ms retained as diagnostics; no numeric filtering applied.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 220/220 | 18.178249709308147 | 19.248200580477715 | available |
| cpu_frame_ms | 220/220 | 18.189300000000003 | 19.7829 | available |
| gpu_frame_ms | 220/220 | 0.016896 | 0.018688 | available |
| cpu_render_thread_ms | 220/220 | 0.18805 | 0.2965 | available |
| gc_allocated_in_editor_frame_bytes | 220/220 | 20719 | 103378 | available |
| VSM.LayoutRemap_gpu_ms | 220/220 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 220/220 | 8.037119999999998 | 8.64768 | available |
| VSM.Allocate_gpu_ms | 220/220 | 0.529408 | 0.54784 | available |
| VSM.InvalidateStatic_gpu_ms | 0/220 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 220/220 | 0.12236799999999999 | 0.27827199999999996 | available |
| VSM.StaticCasterCull_gpu_ms | 220/220 | 0.363136 | 0.55552 | available |
| VSM.DynamicCasterCull_gpu_ms | 220/220 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 220/220 | 0.343808 | 0.495872 | available |
| VSM.StaticRaster_gpu_ms | 220/220 | 0.007168 | 0.0097279999999999988 | available |
| VSM.DynamicRaster_gpu_ms | 220/220 | 0.087039999999999992 | 0.093183999999999989 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/220 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 220/220 | 0.004352 | 0.005376 | available |
| VSM.ResetFeedback_gpu_ms | 220/220 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 220/220 | 2.871168 | 3.2883199999999997 | available |

