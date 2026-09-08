# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## smrt4x8_b — completed_with_warnings
Attempt: 0 | Observations: 441 | Active seconds: 5.0057351048752849 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: False | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 454/447/0/0
- Fresh whole-frame GPU coverage is below 95%; Frame Timing Stats enabled is not evidence of valid data.
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 441/441 | 8.6274938657879829 | 24.766396731138229 | available |
| cpu_frame_ms | 441/441 | 8.512 | 25.6028 | available |
| gpu_frame_ms | 416/441 | 5.398272 | 5.982464 | available |
| cpu_render_thread_ms | 441/441 | 3.3921 | 4.5453 | available |
| gc_allocated_in_editor_frame_bytes | 441/441 | 14551 | 32122 | available |
| VSM.LayoutRemap_gpu_ms | 441/441 | 0.001024 | 0.0012799999999999999 | available |
| VSM.Allocate_gpu_ms | 441/441 | 0.379648 | 0.472064 | available |
| VSM.InvalidateStatic_gpu_ms | 0/441 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 441/441 | 0.063232 | 0.064 | available |
| VSM.StaticCasterCull_gpu_ms | 441/441 | 0.24345599999999998 | 0.252672 | available |
| VSM.DynamicCasterCull_gpu_ms | 441/441 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 441/441 | 0.22527999999999998 | 0.231424 | available |
| VSM.StaticRaster_gpu_ms | 441/441 | 0.007936 | 0.010496 | available |
| VSM.DynamicRaster_gpu_ms | 441/441 | 0.038911999999999995 | 0.040959999999999996 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/441 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 441/441 | 0.002048 | 0.0025599999999999998 | available |
| VSM.ResetFeedback_gpu_ms | 0/441 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 441/441 | 1.387008 | 1.8611199999999999 | available |

