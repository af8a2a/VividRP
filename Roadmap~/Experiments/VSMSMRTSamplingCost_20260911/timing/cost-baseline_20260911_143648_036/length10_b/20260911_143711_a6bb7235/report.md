# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length10_b — completed_with_warnings
Attempt: 0 | Observations: 261 | Active seconds: 4.0028438988095232 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/666/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 261/261 | 15.323100611567497 | 16.345104202628136 | available |
| cpu_frame_ms | 261/261 | 15.3023 | 16.8258 | available |
| gpu_frame_ms | 261/261 | 15.298816 | 16.292864 | available |
| cpu_render_thread_ms | 261/261 | 2.3861 | 3.3222 | available |
| gc_allocated_in_editor_frame_bytes | 261/261 | 14539 | 102274 | available |
| VSM.LayoutRemap_gpu_ms | 261/261 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 261/261 | 3.4050559999999996 | 3.7790719999999998 | available |
| VSM.Allocate_gpu_ms | 261/261 | 0.668416 | 0.68736 | available |
| VSM.InvalidateStatic_gpu_ms | 0/261 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 261/261 | 0.133632 | 0.30131199999999997 | available |
| VSM.StaticCasterCull_gpu_ms | 261/261 | 0.365568 | 0.57753599999999994 | available |
| VSM.DynamicCasterCull_gpu_ms | 261/261 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 261/261 | 0.34559999999999996 | 0.53888 | available |
| VSM.StaticRaster_gpu_ms | 261/261 | 0.007168 | 0.011007999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 261/261 | 0.086272 | 0.093952 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/261 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 261/261 | 0.004352 | 0.005376 | available |
| VSM.ResetFeedback_gpu_ms | 261/261 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 261/261 | 3.7990399999999998 | 4.319488 | available |

