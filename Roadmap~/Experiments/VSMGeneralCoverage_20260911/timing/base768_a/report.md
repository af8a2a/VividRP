# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## base768_a — completed_with_warnings
Attempt: 0 | Observations: 784 | Active seconds: 5.0010382015306121 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 425/419/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 784/784 | 6.3479486852884293 | 6.9488026201725006 | available |
| cpu_frame_ms | 784/784 | 6.32535 | 7.3714 | available |
| gpu_frame_ms | 783/784 | 6.30144 | 6.864896 | available |
| cpu_render_thread_ms | 784/784 | 2.19295 | 2.8943 | available |
| gc_allocated_in_editor_frame_bytes | 784/784 | 18203 | 21691 | available |
| VSM.LayoutRemap_gpu_ms | 784/784 | 0.001024 | 0.001024 | available |
| VSM.Allocate_gpu_ms | 784/784 | 0.40959999999999996 | 0.497664 | available |
| VSM.InvalidateStatic_gpu_ms | 0/784 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 784/784 | 0.091904 | 0.25344 | available |
| VSM.StaticCasterCull_gpu_ms | 784/784 | 0.28159999999999996 | 0.66022399999999992 | available |
| VSM.DynamicCasterCull_gpu_ms | 784/784 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 784/784 | 0.26239999999999997 | 0.457472 | available |
| VSM.StaticRaster_gpu_ms | 784/784 | 0.0076799999999999993 | 0.011007999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 784/784 | 0.064767999999999992 | 0.069887999999999992 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/784 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 784/784 | 0.002304 | 0.002816 | available |
| VSM.ResetFeedback_gpu_ms | 0/784 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 784/784 | 1.9310079999999998 | 2.458112 | available |

