# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## pcf512_b — completed_with_warnings
Attempt: 0 | Observations: 350 | Active seconds: 5.10488759920635 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: False | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 425/419/0/0
- Frame intervals over 100 ms retained; investigate before comparing.
- Fresh whole-frame GPU coverage is below 95%; Frame Timing Stats enabled is not evidence of valid data.
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 350/350 | 8.6112990975379944 | 48.243701457977295 | available |
| cpu_frame_ms | 350/350 | 8.64155 | 49.4597 | available |
| gpu_frame_ms | 329/350 | 5.383936 | 5.848064 | available |
| cpu_render_thread_ms | 350/350 | 3.3608000000000002 | 4.3974 | available |
| gc_allocated_in_editor_frame_bytes | 350/350 | 14551 | 42065 | available |
| VSM.LayoutRemap_gpu_ms | 350/350 | 0.001024 | 0.0012799999999999999 | available |
| VSM.Allocate_gpu_ms | 350/350 | 0.354304 | 0.41625599999999996 | available |
| VSM.InvalidateStatic_gpu_ms | 0/350 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 350/350 | 0.063232 | 0.064256 | available |
| VSM.StaticCasterCull_gpu_ms | 350/350 | 0.23910399999999998 | 0.245248 | available |
| VSM.DynamicCasterCull_gpu_ms | 350/350 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 350/350 | 0.2208 | 0.226816 | available |
| VSM.StaticRaster_gpu_ms | 350/350 | 0.0076799999999999993 | 0.009984 | available |
| VSM.DynamicRaster_gpu_ms | 350/350 | 0.039424 | 0.041984 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/350 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 350/350 | 0.002048 | 0.0025599999999999998 | available |
| VSM.ResetFeedback_gpu_ms | 0/350 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 350/350 | 1.630464 | 2.111744 | available |

