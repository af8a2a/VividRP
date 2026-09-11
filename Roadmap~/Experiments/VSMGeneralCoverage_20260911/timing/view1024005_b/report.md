# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## view1024005_b — completed_with_warnings
Attempt: 0 | Observations: 746 | Active seconds: 5.00541439909297 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 635/623/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 746/746 | 6.6236997954547405 | 7.464604452252388 | available |
| cpu_frame_ms | 746/746 | 6.5810499999999994 | 8.5014 | available |
| gpu_frame_ms | 740/746 | 6.543488 | 7.163904 | available |
| cpu_render_thread_ms | 746/746 | 2.62585 | 3.3132 | available |
| gc_allocated_in_editor_frame_bytes | 746/746 | 18203 | 21295 | available |
| VSM.LayoutRemap_gpu_ms | 746/746 | 0.001024 | 0.001024 | available |
| VSM.Allocate_gpu_ms | 746/746 | 0.507776 | 0.619776 | available |
| VSM.InvalidateStatic_gpu_ms | 0/746 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 746/746 | 0.12185599999999999 | 0.302848 | available |
| VSM.StaticCasterCull_gpu_ms | 746/746 | 0.359168 | 0.750336 | available |
| VSM.DynamicCasterCull_gpu_ms | 746/746 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 746/746 | 0.33971199999999996 | 0.667648 | available |
| VSM.StaticRaster_gpu_ms | 746/746 | 0.0076799999999999993 | 0.011776 | available |
| VSM.DynamicRaster_gpu_ms | 746/746 | 0.09088 | 0.096256 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/746 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 746/746 | 0.002304 | 0.002816 | available |
| VSM.ResetFeedback_gpu_ms | 0/746 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 746/746 | 2.0121599999999997 | 2.447616 | available |

