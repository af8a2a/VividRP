# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length50_b — completed_with_warnings
Attempt: 0 | Observations: 286 | Active seconds: 4.012250602324265 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/667/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 286/286 | 14.075899962335825 | 14.584898948669434 | available |
| cpu_frame_ms | 286/286 | 14.04485 | 14.7451 | available |
| gpu_frame_ms | 286/286 | 14.064384 | 14.569728 | available |
| cpu_render_thread_ms | 286/286 | 2.0307000000000004 | 2.4228 | available |
| gc_allocated_in_editor_frame_bytes | 286/286 | 18307 | 20629 | available |
| VSM.LayoutRemap_gpu_ms | 286/286 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 286/286 | 3.536512 | 3.942144 | available |
| VSM.Allocate_gpu_ms | 286/286 | 0.539648 | 0.550912 | available |
| VSM.InvalidateStatic_gpu_ms | 0/286 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 286/286 | 0.132096 | 0.324864 | available |
| VSM.StaticCasterCull_gpu_ms | 286/286 | 0.367488 | 0.6976 | available |
| VSM.DynamicCasterCull_gpu_ms | 286/286 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 286/286 | 0.346112 | 0.55577599999999994 | available |
| VSM.StaticRaster_gpu_ms | 286/286 | 0.007168 | 0.010752 | available |
| VSM.DynamicRaster_gpu_ms | 286/286 | 0.08832 | 0.09344 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/286 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 286/286 | 0.0025599999999999998 | 0.002816 | available |
| VSM.ResetFeedback_gpu_ms | 286/286 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 286/286 | 2.864512 | 3.25888 | available |

