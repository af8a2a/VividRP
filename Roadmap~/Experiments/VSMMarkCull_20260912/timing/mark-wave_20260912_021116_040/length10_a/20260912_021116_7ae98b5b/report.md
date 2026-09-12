# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length10_a — completed_with_warnings
Attempt: 0 | Observations: 388 | Active seconds: 4.0043017006802728 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: False | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 670/665/0/0
- Fresh CPU frame timing coverage is below 95%.
- Fresh whole-frame GPU coverage is below 95%; Frame Timing Stats enabled is not evidence of valid data.
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 388/388 | 10.303702671080828 | 10.992495343089104 | available |
| cpu_frame_ms | 0/388 |  |  | no_samples_or_not_executed |
| gpu_frame_ms | 0/388 |  |  | no_samples_or_not_executed |
| cpu_render_thread_ms | 0/388 |  |  | no_samples_or_not_executed |
| gc_allocated_in_editor_frame_bytes | 388/388 | 18307 | 24125 | available |
| VSM.LayoutRemap_gpu_ms | 388/388 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 388/388 | 0.82841599999999993 | 1.073664 | available |
| VSM.Allocate_gpu_ms | 388/388 | 0.528384 | 0.539648 | available |
| VSM.InvalidateStatic_gpu_ms | 0/388 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 388/388 | 0.135424 | 0.144128 | available |
| VSM.StaticCasterCull_gpu_ms | 388/388 | 0.379264 | 0.602368 | available |
| VSM.DynamicCasterCull_gpu_ms | 388/388 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 388/388 | 0.357888 | 0.557568 | available |
| VSM.StaticRaster_gpu_ms | 388/388 | 0.007424 | 0.010496 | available |
| VSM.DynamicRaster_gpu_ms | 388/388 | 0.08576 | 0.092159999999999992 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/388 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 388/388 | 0.004608 | 0.005632 | available |
| VSM.ResetFeedback_gpu_ms | 388/388 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 388/388 | 2.5909759999999995 | 2.917888 | available |

