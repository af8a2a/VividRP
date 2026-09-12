# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length50_b — completed_with_warnings
Attempt: 0 | Observations: 404 | Active seconds: 4.0026671981292452 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/661/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 404/404 | 9.83930379152298 | 11.194799095392227 | available |
| cpu_frame_ms | 404/404 | 9.85785 | 11.204 | available |
| gpu_frame_ms | 404/404 | 9.81824 | 10.99264 | available |
| cpu_render_thread_ms | 404/404 | 2.1699 | 2.7998 | available |
| gc_allocated_in_editor_frame_bytes | 404/404 | 18307 | 24125 | available |
| VSM.LayoutRemap_gpu_ms | 404/404 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 404/404 | 0.995328 | 1.31456 | available |
| VSM.Allocate_gpu_ms | 404/404 | 0.53376 | 0.556288 | available |
| VSM.InvalidateStatic_gpu_ms | 0/404 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 404/404 | 0.130304 | 0.1344 | available |
| VSM.StaticCasterCull_gpu_ms | 404/404 | 0.360448 | 0.664064 | available |
| VSM.DynamicCasterCull_gpu_ms | 404/404 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 404/404 | 0.339584 | 0.630528 | available |
| VSM.StaticRaster_gpu_ms | 404/404 | 0.007424 | 0.010752 | available |
| VSM.DynamicRaster_gpu_ms | 404/404 | 0.089343999999999993 | 0.099072 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/404 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 404/404 | 0.0038399999999999997 | 0.0048639999999999994 | available |
| VSM.ResetFeedback_gpu_ms | 404/404 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 404/404 | 2.85952 | 3.233024 | available |

