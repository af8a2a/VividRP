# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## base768_b — completed_with_warnings
Attempt: 0 | Observations: 804 | Active seconds: 5.0004916028911452 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 425/419/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 804/804 | 6.1665531247854233 | 6.8849064409732819 | available |
| cpu_frame_ms | 804/804 | 6.1569 | 7.4511 | available |
| gpu_frame_ms | 803/804 | 6.102784 | 6.734592 | available |
| cpu_render_thread_ms | 804/804 | 2.3902 | 3.1759 | available |
| gc_allocated_in_editor_frame_bytes | 804/804 | 18203 | 21692 | available |
| VSM.LayoutRemap_gpu_ms | 804/804 | 0.001024 | 0.0012799999999999999 | available |
| VSM.Allocate_gpu_ms | 804/804 | 0.411776 | 0.50380799999999992 | available |
| VSM.InvalidateStatic_gpu_ms | 0/804 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 804/804 | 0.100864 | 0.289024 | available |
| VSM.StaticCasterCull_gpu_ms | 804/804 | 0.285696 | 0.674048 | available |
| VSM.DynamicCasterCull_gpu_ms | 804/804 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 804/804 | 0.265984 | 0.46975999999999996 | available |
| VSM.StaticRaster_gpu_ms | 804/804 | 0.0076799999999999993 | 0.011264 | available |
| VSM.DynamicRaster_gpu_ms | 804/804 | 0.065536 | 0.069632 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/804 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 804/804 | 0.002304 | 0.002816 | available |
| VSM.ResetFeedback_gpu_ms | 0/804 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 804/804 | 1.66848 | 2.125056 | available |

