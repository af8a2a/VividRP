# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## smrt8x4_a — completed_with_warnings
Attempt: 0 | Observations: 743 | Active seconds: 5.0041790036848042 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 439/434/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 743/743 | 6.3370037823915482 | 9.1836024075746536 | available |
| cpu_frame_ms | 743/743 | 6.3439 | 9.4234 | available |
| gpu_frame_ms | 735/743 | 6.17216 | 6.87872 | available |
| cpu_render_thread_ms | 743/743 | 2.2008 | 3.4995 | available |
| gc_allocated_in_editor_frame_bytes | 743/743 | 14539 | 20949 | available |
| VSM.LayoutRemap_gpu_ms | 743/743 | 0.001024 | 0.0012799999999999999 | available |
| VSM.Allocate_gpu_ms | 743/743 | 0.36735999999999996 | 0.43596799999999997 | available |
| VSM.InvalidateStatic_gpu_ms | 0/743 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 743/743 | 0.068608 | 0.076288 | available |
| VSM.StaticCasterCull_gpu_ms | 743/743 | 0.245504 | 0.645888 | available |
| VSM.DynamicCasterCull_gpu_ms | 743/743 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 743/743 | 0.226816 | 0.420608 | available |
| VSM.StaticRaster_gpu_ms | 743/743 | 0.0076799999999999993 | 0.0097279999999999988 | available |
| VSM.DynamicRaster_gpu_ms | 743/743 | 0.038655999999999996 | 0.041728 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/743 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 743/743 | 0.004096 | 0.005632 | available |
| VSM.ResetFeedback_gpu_ms | 0/743 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 743/743 | 1.6350719999999999 | 2.132736 | available |

