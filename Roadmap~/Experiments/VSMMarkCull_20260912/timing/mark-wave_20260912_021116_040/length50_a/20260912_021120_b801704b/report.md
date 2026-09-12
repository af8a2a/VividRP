# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length50_a — completed_with_warnings
Attempt: 0 | Observations: 354 | Active seconds: 4.0045680059523789 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/667/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 354/354 | 11.322551406919956 | 11.794203892350197 | available |
| cpu_frame_ms | 354/354 | 11.321 | 12.0051 | available |
| gpu_frame_ms | 354/354 | 11.303296 | 11.7632 | available |
| cpu_render_thread_ms | 354/354 | 2.1028000000000002 | 2.6009 | available |
| gc_allocated_in_editor_frame_bytes | 354/354 | 18307 | 24125 | available |
| VSM.LayoutRemap_gpu_ms | 354/354 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 354/354 | 0.840704 | 1.214976 | available |
| VSM.Allocate_gpu_ms | 354/354 | 0.529792 | 0.54143999999999992 | available |
| VSM.InvalidateStatic_gpu_ms | 0/354 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 354/354 | 0.135424 | 0.34022399999999997 | available |
| VSM.StaticCasterCull_gpu_ms | 354/354 | 0.38028799999999996 | 0.72217599999999993 | available |
| VSM.DynamicCasterCull_gpu_ms | 354/354 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 354/354 | 0.3584 | 0.56319999999999992 | available |
| VSM.StaticRaster_gpu_ms | 354/354 | 0.007424 | 0.011776 | available |
| VSM.DynamicRaster_gpu_ms | 354/354 | 0.085504 | 0.09344 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/354 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 354/354 | 0.004608 | 0.005888 | available |
| VSM.ResetFeedback_gpu_ms | 354/354 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 354/354 | 2.965632 | 3.3233919999999997 | available |

