# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length10_b — completed_with_warnings
Attempt: 0 | Observations: 450 | Active seconds: 4.0084855017006831 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 668/655/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 450/450 | 8.9022992178797722 | 9.5044998452067375 | available |
| cpu_frame_ms | 450/450 | 8.89275 | 9.8599 | available |
| gpu_frame_ms | 450/450 | 8.8787200000000013 | 9.488128 | available |
| cpu_render_thread_ms | 450/450 | 2.30775 | 3.0791 | available |
| gc_allocated_in_editor_frame_bytes | 450/450 | 14419 | 49681 | available |
| VSM.LayoutRemap_gpu_ms | 450/450 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 450/450 | 4.1034239999999995 | 4.4451839999999994 | available |
| VSM.Allocate_gpu_ms | 450/450 | 0.539392 | 0.54732799999999993 | available |
| VSM.InvalidateStatic_gpu_ms | 0/450 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 450/450 | 0.121344 | 0.305664 | available |
| VSM.StaticCasterCull_gpu_ms | 450/450 | 0.350976 | 0.634624 | available |
| VSM.DynamicCasterCull_gpu_ms | 450/450 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 450/450 | 0.32844799999999996 | 0.532224 | available |
| VSM.StaticRaster_gpu_ms | 450/450 | 0.007168 | 0.010496 | available |
| VSM.DynamicRaster_gpu_ms | 450/450 | 0.087808 | 0.094463999999999992 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/450 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 450/450 | 0.003328 | 0.004096 | available |
| VSM.ResetFeedback_gpu_ms | 450/450 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 450/450 | 0.384768 | 0.674048 | available |

