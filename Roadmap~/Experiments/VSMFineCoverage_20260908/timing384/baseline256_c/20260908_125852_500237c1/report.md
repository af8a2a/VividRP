# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## baseline256_c — completed_with_warnings
Attempt: 0 | Observations: 879 | Active seconds: 5.0045015022675727 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: over_budget_at_last_frame | Last-frame resident/requested/new/overflow: 256/419/2/163
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Physical page requests exceed the budget at the last frame; quality is not validated.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 879/879 | 5.6964000687003136 | 6.3744969666004181 | available |
| cpu_frame_ms | 879/879 | 5.6701 | 6.6055 | available |
| gpu_frame_ms | 878/879 | 5.648384 | 6.287104 | available |
| cpu_render_thread_ms | 879/879 | 2.1197 | 2.683 | available |
| gc_allocated_in_editor_frame_bytes | 879/879 | 14551 | 14948 | available |
| VSM.LayoutRemap_gpu_ms | 879/879 | 0.001024 | 0.001024 | available |
| VSM.Allocate_gpu_ms | 879/879 | 0.491776 | 0.560384 | available |
| VSM.InvalidateStatic_gpu_ms | 0/879 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 879/879 | 0.033535999999999996 | 0.041984 | available |
| VSM.StaticCasterCull_gpu_ms | 879/879 | 0.17049599999999998 | 0.49203199999999997 | available |
| VSM.DynamicCasterCull_gpu_ms | 879/879 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 879/879 | 0.15129599999999999 | 0.161024 | available |
| VSM.StaticRaster_gpu_ms | 879/879 | 0.024576 | 0.032256 | available |
| VSM.DynamicRaster_gpu_ms | 879/879 | 0.014591999999999999 | 0.016895999999999998 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/879 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 879/879 | 0.002048 | 0.002816 | available |
| VSM.ResetFeedback_gpu_ms | 0/879 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 879/879 | 1.6299519999999998 | 2.193152 | available |

