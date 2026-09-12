# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length50_b — completed_with_warnings
Attempt: 0 | Observations: 404 | Active seconds: 4.0018192035147422 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/663/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 404/404 | 9.8883006721735 | 10.574801824986935 | available |
| cpu_frame_ms | 404/404 | 9.85075 | 11.0448 | available |
| gpu_frame_ms | 404/404 | 9.872128 | 10.443264 | available |
| cpu_render_thread_ms | 404/404 | 2.36545 | 2.9778 | available |
| gc_allocated_in_editor_frame_bytes | 404/404 | 18307 | 33022 | available |
| VSM.LayoutRemap_gpu_ms | 404/404 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 404/404 | 0.991232 | 1.376512 | available |
| VSM.Allocate_gpu_ms | 404/404 | 0.534272 | 0.54399999999999993 | available |
| VSM.InvalidateStatic_gpu_ms | 0/404 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 404/404 | 0.13055999999999998 | 0.32255999999999996 | available |
| VSM.StaticCasterCull_gpu_ms | 404/404 | 0.19712 | 0.39808 | available |
| VSM.DynamicCasterCull_gpu_ms | 404/404 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 404/404 | 0.175616 | 0.183296 | available |
| VSM.StaticRaster_gpu_ms | 404/404 | 0.007424 | 0.011776 | available |
| VSM.DynamicRaster_gpu_ms | 404/404 | 0.087551999999999991 | 0.09344 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/404 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 404/404 | 0.003328 | 0.004352 | available |
| VSM.ResetFeedback_gpu_ms | 404/404 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 404/404 | 2.958464 | 3.419648 | available |

