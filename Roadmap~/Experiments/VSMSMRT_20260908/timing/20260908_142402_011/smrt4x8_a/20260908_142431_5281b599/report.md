# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## smrt4x8_a — completed_with_warnings
Attempt: 0 | Observations: 543 | Active seconds: 5.0004157029478478 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 454/446/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 543/543 | 7.7894981950521469 | 16.569005325436592 | available |
| cpu_frame_ms | 543/543 | 7.7786 | 18.3494 | available |
| gpu_frame_ms | 526/543 | 5.801216 | 6.321408 | available |
| cpu_render_thread_ms | 543/543 | 3.2755 | 4.2961 | available |
| gc_allocated_in_editor_frame_bytes | 543/543 | 14551 | 28953 | available |
| VSM.LayoutRemap_gpu_ms | 543/543 | 0.001024 | 0.0012799999999999999 | available |
| VSM.Allocate_gpu_ms | 543/543 | 0.39039999999999997 | 0.483072 | available |
| VSM.InvalidateStatic_gpu_ms | 0/543 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 543/543 | 0.063744 | 0.069632 | available |
| VSM.StaticCasterCull_gpu_ms | 543/543 | 0.2432 | 0.251392 | available |
| VSM.DynamicCasterCull_gpu_ms | 543/543 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 543/543 | 0.22527999999999998 | 0.231936 | available |
| VSM.StaticRaster_gpu_ms | 543/543 | 0.0076799999999999993 | 0.010239999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 543/543 | 0.038655999999999996 | 0.040704 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/543 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 543/543 | 0.002048 | 0.0025599999999999998 | available |
| VSM.ResetFeedback_gpu_ms | 0/543 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 543/543 | 1.755136 | 2.16064 | available |

