# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## view768005_a — completed_with_warnings
Attempt: 0 | Observations: 791 | Active seconds: 5.0019009991496546 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 635/623/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 791/791 | 6.3139949925243855 | 6.8830996751785278 | available |
| cpu_frame_ms | 791/791 | 6.2979 | 7.4114 | available |
| gpu_frame_ms | 790/791 | 6.271104 | 6.786048 | available |
| cpu_render_thread_ms | 791/791 | 2.2209 | 2.8472 | available |
| gc_allocated_in_editor_frame_bytes | 791/791 | 18203 | 21295 | available |
| VSM.LayoutRemap_gpu_ms | 791/791 | 0.001024 | 0.001024 | available |
| VSM.Allocate_gpu_ms | 791/791 | 0.515072 | 0.672256 | available |
| VSM.InvalidateStatic_gpu_ms | 0/791 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 791/791 | 0.091392 | 0.10521599999999999 | available |
| VSM.StaticCasterCull_gpu_ms | 791/791 | 0.320256 | 0.703488 | available |
| VSM.DynamicCasterCull_gpu_ms | 791/791 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 791/791 | 0.30131199999999997 | 0.585728 | available |
| VSM.StaticRaster_gpu_ms | 791/791 | 0.0076799999999999993 | 0.011007999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 791/791 | 0.065024 | 0.069632 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/791 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 791/791 | 0.002304 | 0.004096 | available |
| VSM.ResetFeedback_gpu_ms | 0/791 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 791/791 | 1.808384 | 2.237184 | available |

