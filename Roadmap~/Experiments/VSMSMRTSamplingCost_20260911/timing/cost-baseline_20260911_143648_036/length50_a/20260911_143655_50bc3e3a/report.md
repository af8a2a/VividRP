# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length50_a — completed_with_warnings
Attempt: 0 | Observations: 226 | Active seconds: 4.0033584963151938 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 1 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/668/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Positive GPU timings below 1 ms retained as diagnostics; no numeric filtering applied.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 226/226 | 17.60339830070734 | 18.666602671146393 | available |
| cpu_frame_ms | 226/226 | 17.596449999999997 | 19.0016 | available |
| gpu_frame_ms | 226/226 | 17.58976 | 18.541312 | available |
| cpu_render_thread_ms | 226/226 | 2.3987499999999997 | 3.0989 | available |
| gc_allocated_in_editor_frame_bytes | 226/226 | 14539 | 133205 | available |
| VSM.LayoutRemap_gpu_ms | 226/226 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 226/226 | 4.127616 | 4.50688 | available |
| VSM.Allocate_gpu_ms | 226/226 | 0.66790399999999994 | 0.68787199999999993 | available |
| VSM.InvalidateStatic_gpu_ms | 0/226 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 226/226 | 0.133376 | 0.13516799999999998 | available |
| VSM.StaticCasterCull_gpu_ms | 226/226 | 0.366336 | 0.62105599999999994 | available |
| VSM.DynamicCasterCull_gpu_ms | 226/226 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 226/226 | 0.346496 | 0.50892799999999994 | available |
| VSM.StaticRaster_gpu_ms | 226/226 | 0.007424 | 0.010496 | available |
| VSM.DynamicRaster_gpu_ms | 226/226 | 0.08832 | 0.094975999999999991 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/226 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 226/226 | 0.004352 | 0.005376 | available |
| VSM.ResetFeedback_gpu_ms | 226/226 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 226/226 | 4.60736 | 5.095168 | available |

