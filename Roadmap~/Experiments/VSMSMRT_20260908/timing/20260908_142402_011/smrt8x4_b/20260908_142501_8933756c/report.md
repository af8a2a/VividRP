# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## smrt8x4_b — completed_with_warnings
Attempt: 0 | Observations: 395 | Active seconds: 5.0007160997732427 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: False | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 439/433/0/0
- Fresh whole-frame GPU coverage is below 95%; Frame Timing Stats enabled is not evidence of valid data.
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 395/395 | 9.002302773296833 | 31.819704920053482 | available |
| cpu_frame_ms | 395/395 | 9.0796 | 35.9788 | available |
| gpu_frame_ms | 373/395 | 5.693696 | 6.270464 | available |
| cpu_render_thread_ms | 395/395 | 3.4092 | 4.4406 | available |
| gc_allocated_in_editor_frame_bytes | 395/395 | 14551 | 28936 | available |
| VSM.LayoutRemap_gpu_ms | 395/395 | 0.001024 | 0.0012799999999999999 | available |
| VSM.Allocate_gpu_ms | 395/395 | 0.38246399999999997 | 0.473856 | available |
| VSM.InvalidateStatic_gpu_ms | 0/395 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 395/395 | 0.063232 | 0.064 | available |
| VSM.StaticCasterCull_gpu_ms | 395/395 | 0.241664 | 0.24806399999999998 | available |
| VSM.DynamicCasterCull_gpu_ms | 395/395 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 395/395 | 0.22425599999999998 | 0.229632 | available |
| VSM.StaticRaster_gpu_ms | 395/395 | 0.0076799999999999993 | 0.00896 | available |
| VSM.DynamicRaster_gpu_ms | 395/395 | 0.039424 | 0.041728 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/395 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 395/395 | 0.002048 | 0.0025599999999999998 | available |
| VSM.ResetFeedback_gpu_ms | 0/395 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 395/395 | 1.6163839999999998 | 2.067456 | available |

