# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## smrt8x4_b — completed_with_warnings
Attempt: 0 | Observations: 716 | Active seconds: 5.0023096017573749 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 439/434/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 716/716 | 6.9711485411971807 | 7.6546980999410152 | available |
| cpu_frame_ms | 716/716 | 6.94025 | 8.1029 | available |
| gpu_frame_ms | 714/716 | 6.932736 | 7.534592 | available |
| cpu_render_thread_ms | 716/716 | 2.26615 | 3.1279 | available |
| gc_allocated_in_editor_frame_bytes | 716/716 | 14539 | 28924 | available |
| VSM.LayoutRemap_gpu_ms | 716/716 | 0.001024 | 0.0012799999999999999 | available |
| VSM.Allocate_gpu_ms | 716/716 | 0.358912 | 0.42675199999999996 | available |
| VSM.InvalidateStatic_gpu_ms | 0/716 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 716/716 | 0.068352 | 0.075776 | available |
| VSM.StaticCasterCull_gpu_ms | 716/716 | 0.247296 | 0.563456 | available |
| VSM.DynamicCasterCull_gpu_ms | 716/716 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 716/716 | 0.22860799999999998 | 0.42496 | available |
| VSM.StaticRaster_gpu_ms | 716/716 | 0.0076799999999999993 | 0.010496 | available |
| VSM.DynamicRaster_gpu_ms | 716/716 | 0.038655999999999996 | 0.041471999999999995 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/716 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 716/716 | 0.004352 | 0.005632 | available |
| VSM.ResetFeedback_gpu_ms | 0/716 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 716/716 | 2.4020479999999997 | 2.914816 | available |

