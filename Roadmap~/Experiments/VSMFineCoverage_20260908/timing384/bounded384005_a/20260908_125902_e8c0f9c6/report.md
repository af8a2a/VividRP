# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## bounded384005_a — completed_with_warnings
Attempt: 0 | Observations: 750 | Active seconds: 5.0045683035714248 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: over_budget_at_last_frame | Last-frame resident/requested/new/overflow: 384/503/4/119
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Physical page requests exceed the budget at the last frame; quality is not validated.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 750/750 | 6.6717509180307388 | 7.2092050686478615 | available |
| cpu_frame_ms | 750/750 | 6.6645 | 7.4635 | available |
| gpu_frame_ms | 749/750 | 6.617344 | 7.17568 | available |
| cpu_render_thread_ms | 750/750 | 1.92935 | 2.3092 | available |
| gc_allocated_in_editor_frame_bytes | 750/750 | 14551 | 15309 | available |
| VSM.LayoutRemap_gpu_ms | 750/750 | 0.001024 | 0.001024 | available |
| VSM.Allocate_gpu_ms | 750/750 | 0.736 | 0.91904 | available |
| VSM.InvalidateStatic_gpu_ms | 0/750 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 750/750 | 0.047616 | 0.059648 | available |
| VSM.StaticCasterCull_gpu_ms | 750/750 | 0.20915199999999998 | 0.589568 | available |
| VSM.DynamicCasterCull_gpu_ms | 750/750 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 750/750 | 0.189696 | 0.387584 | available |
| VSM.StaticRaster_gpu_ms | 750/750 | 0.041728 | 0.064 | available |
| VSM.DynamicRaster_gpu_ms | 750/750 | 0.026624 | 0.029696 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/750 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 750/750 | 0.003072 | 0.004352 | available |
| VSM.ResetFeedback_gpu_ms | 0/750 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 750/750 | 2.2821119999999997 | 2.7217919999999998 | available |

