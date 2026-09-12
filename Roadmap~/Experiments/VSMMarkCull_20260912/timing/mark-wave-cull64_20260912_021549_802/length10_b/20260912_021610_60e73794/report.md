# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length10_b — completed_with_warnings
Attempt: 0 | Observations: 388 | Active seconds: 4.0014564980158696 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/663/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 388/388 | 9.9787521176040173 | 11.695096269249916 | available |
| cpu_frame_ms | 388/388 | 10.05595 | 11.8129 | available |
| gpu_frame_ms | 388/388 | 9.9450879999999984 | 11.661568 | available |
| cpu_render_thread_ms | 388/388 | 1.9322499999999998 | 2.2836 | available |
| gc_allocated_in_editor_frame_bytes | 388/388 | 18307 | 18711 | available |
| VSM.LayoutRemap_gpu_ms | 388/388 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 388/388 | 0.96396799999999994 | 1.332992 | available |
| VSM.Allocate_gpu_ms | 388/388 | 0.53145599999999993 | 0.54579199999999994 | available |
| VSM.InvalidateStatic_gpu_ms | 0/388 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 388/388 | 0.132096 | 0.134912 | available |
| VSM.StaticCasterCull_gpu_ms | 388/388 | 0.205312 | 0.406016 | available |
| VSM.DynamicCasterCull_gpu_ms | 388/388 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 388/388 | 0.185088 | 0.19225599999999998 | available |
| VSM.StaticRaster_gpu_ms | 388/388 | 0.007424 | 0.011264 | available |
| VSM.DynamicRaster_gpu_ms | 388/388 | 0.088832 | 0.09267199999999999 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/388 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 388/388 | 0.004352 | 0.005632 | available |
| VSM.ResetFeedback_gpu_ms | 388/388 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 388/388 | 2.583808 | 3.0796799999999998 | available |

