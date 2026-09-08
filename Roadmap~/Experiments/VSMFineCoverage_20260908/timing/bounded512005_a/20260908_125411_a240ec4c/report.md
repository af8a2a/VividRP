# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## bounded512005_a — completed_with_warnings
Attempt: 0 | Observations: 856 | Active seconds: 5.005293799603173 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 511/504/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 856/856 | 5.8670989237725735 | 6.4172972925007343 | available |
| cpu_frame_ms | 856/856 | 5.8495 | 6.5216 | available |
| gpu_frame_ms | 856/856 | 5.835904 | 6.32448 | available |
| cpu_render_thread_ms | 856/856 | 1.8881999999999999 | 2.182 | available |
| gc_allocated_in_editor_frame_bytes | 856/856 | 14551 | 14753 | available |
| VSM.LayoutRemap_gpu_ms | 856/856 | 0.001024 | 0.0012799999999999999 | available |
| VSM.Allocate_gpu_ms | 856/856 | 0.42496 | 0.54579199999999994 | available |
| VSM.InvalidateStatic_gpu_ms | 0/856 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 856/856 | 0.068352 | 0.26624 | available |
| VSM.StaticCasterCull_gpu_ms | 856/856 | 0.26111999999999996 | 0.67532799999999993 | available |
| VSM.DynamicCasterCull_gpu_ms | 856/856 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 856/856 | 0.24140799999999998 | 0.61926399999999993 | available |
| VSM.StaticRaster_gpu_ms | 856/856 | 0.0076799999999999993 | 0.010752 | available |
| VSM.DynamicRaster_gpu_ms | 856/856 | 0.039424 | 0.042496 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/856 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 856/856 | 0.003328 | 0.004352 | available |
| VSM.ResetFeedback_gpu_ms | 0/856 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 856/856 | 1.643776 | 2.044416 | available |

