# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length50_a — completed_with_warnings
Attempt: 0 | Observations: 355 | Active seconds: 4.0093731009070268 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/667/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 355/355 | 11.361599899828434 | 12.011599726974964 | available |
| cpu_frame_ms | 355/355 | 11.3546 | 12.4973 | available |
| gpu_frame_ms | 355/355 | 11.337472 | 11.954688 | available |
| cpu_render_thread_ms | 355/355 | 2.2911 | 2.929 | available |
| gc_allocated_in_editor_frame_bytes | 355/355 | 18307 | 24125 | available |
| VSM.LayoutRemap_gpu_ms | 355/355 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 355/355 | 0.995328 | 1.3273599999999999 | available |
| VSM.Allocate_gpu_ms | 355/355 | 0.53376 | 0.551936 | available |
| VSM.InvalidateStatic_gpu_ms | 0/355 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 355/355 | 0.130304 | 0.33638399999999996 | available |
| VSM.StaticCasterCull_gpu_ms | 355/355 | 0.358656 | 0.62208 | available |
| VSM.DynamicCasterCull_gpu_ms | 355/355 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 355/355 | 0.33792 | 0.530432 | available |
| VSM.StaticRaster_gpu_ms | 355/355 | 0.007424 | 0.010239999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 355/355 | 0.08832 | 0.095232 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/355 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 355/355 | 0.0038399999999999997 | 0.0048639999999999994 | available |
| VSM.ResetFeedback_gpu_ms | 355/355 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 355/355 | 2.902784 | 3.4042879999999998 | available |

