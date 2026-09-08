# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## smrt256_b — completed_with_warnings
Attempt: 0 | Observations: 695 | Active seconds: 5.0062603954081624 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: over_budget_at_last_frame | Last-frame resident/requested/new/overflow: 256/446/0/190
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Physical page requests exceed the budget at the last frame; quality is not validated.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 695/695 | 7.2200042195618153 | 7.7916029840707779 | available |
| cpu_frame_ms | 695/695 | 7.2195 | 8.0844 | available |
| gpu_frame_ms | 694/695 | 7.190144 | 7.749376 | available |
| cpu_render_thread_ms | 695/695 | 2.0559 | 2.6406 | available |
| gc_allocated_in_editor_frame_bytes | 695/695 | 14539 | 28924 | available |
| VSM.LayoutRemap_gpu_ms | 695/695 | 0.001024 | 0.0012799999999999999 | available |
| VSM.Allocate_gpu_ms | 695/695 | 0.476416 | 0.55654399999999993 | available |
| VSM.InvalidateStatic_gpu_ms | 0/695 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 695/695 | 0.036095999999999996 | 0.045824 | available |
| VSM.StaticCasterCull_gpu_ms | 695/695 | 0.17407999999999998 | 0.4672 | available |
| VSM.DynamicCasterCull_gpu_ms | 695/695 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 695/695 | 0.155392 | 0.168448 | available |
| VSM.StaticRaster_gpu_ms | 695/695 | 0.020735999999999997 | 0.030719999999999997 | available |
| VSM.DynamicRaster_gpu_ms | 695/695 | 0.014079999999999999 | 0.016128 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/695 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 695/695 | 0.004608 | 0.005632 | available |
| VSM.ResetFeedback_gpu_ms | 0/695 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 695/695 | 2.670848 | 3.2115199999999997 | available |

