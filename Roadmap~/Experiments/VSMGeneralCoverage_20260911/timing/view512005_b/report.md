# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## view512005_b — completed_with_warnings
Attempt: 0 | Observations: 698 | Active seconds: 5.002496903344678 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: over_budget_at_last_frame | Last-frame resident/requested/new/overflow: 512/625/4/113
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Physical page requests exceed the budget at the last frame; quality is not validated.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 698/698 | 6.91935233771801 | 8.6066965013742447 | available |
| cpu_frame_ms | 698/698 | 6.95155 | 9.2284 | available |
| gpu_frame_ms | 696/698 | 6.764544 | 7.3792 | available |
| cpu_render_thread_ms | 698/698 | 2.74195 | 3.7222 | available |
| gc_allocated_in_editor_frame_bytes | 698/698 | 18203 | 24004 | available |
| VSM.LayoutRemap_gpu_ms | 698/698 | 0.001024 | 0.001024 | available |
| VSM.Allocate_gpu_ms | 698/698 | 0.9235199999999999 | 1.201408 | available |
| VSM.InvalidateStatic_gpu_ms | 0/698 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 698/698 | 0.063488 | 0.078592 | available |
| VSM.StaticCasterCull_gpu_ms | 698/698 | 0.25216 | 0.622848 | available |
| VSM.DynamicCasterCull_gpu_ms | 698/698 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 698/698 | 0.23296 | 0.54399999999999993 | available |
| VSM.StaticRaster_gpu_ms | 698/698 | 0.041215999999999996 | 0.061183999999999995 | available |
| VSM.DynamicRaster_gpu_ms | 698/698 | 0.038655999999999996 | 0.041984 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/698 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 698/698 | 0.002304 | 0.003072 | available |
| VSM.ResetFeedback_gpu_ms | 0/698 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 698/698 | 1.9740159999999998 | 2.4038399999999998 | available |

