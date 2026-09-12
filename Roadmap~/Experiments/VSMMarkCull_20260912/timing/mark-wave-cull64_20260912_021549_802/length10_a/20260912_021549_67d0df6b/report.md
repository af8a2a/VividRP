# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length10_a — completed_with_warnings
Attempt: 0 | Observations: 403 | Active seconds: 4.00139589710885 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: False | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 670/659/0/0
- Fresh CPU frame timing coverage is below 95%.
- Fresh whole-frame GPU coverage is below 95%; Frame Timing Stats enabled is not evidence of valid data.
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 403/403 | 9.87709779292345 | 10.456901974976063 | available |
| cpu_frame_ms | 0/403 |  |  | no_samples_or_not_executed |
| gpu_frame_ms | 0/403 |  |  | no_samples_or_not_executed |
| cpu_render_thread_ms | 0/403 |  |  | no_samples_or_not_executed |
| gc_allocated_in_editor_frame_bytes | 403/403 | 18307 | 18654 | available |
| VSM.LayoutRemap_gpu_ms | 403/403 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 403/403 | 0.963584 | 1.189888 | available |
| VSM.Allocate_gpu_ms | 403/403 | 0.52889599999999992 | 0.53478399999999993 | available |
| VSM.InvalidateStatic_gpu_ms | 0/403 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 403/403 | 0.13183999999999998 | 0.132352 | available |
| VSM.StaticCasterCull_gpu_ms | 403/403 | 0.205056 | 0.22092799999999999 | available |
| VSM.DynamicCasterCull_gpu_ms | 403/403 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 403/403 | 0.185088 | 0.19097599999999998 | available |
| VSM.StaticRaster_gpu_ms | 403/403 | 0.0076799999999999993 | 0.010239999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 403/403 | 0.088832 | 0.09267199999999999 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/403 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 403/403 | 0.004352 | 0.005376 | available |
| VSM.ResetFeedback_gpu_ms | 403/403 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 403/403 | 2.536192 | 2.803968 | available |

