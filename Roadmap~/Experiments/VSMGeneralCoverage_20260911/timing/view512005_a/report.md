# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## view512005_a — completed_with_warnings
Attempt: 0 | Observations: 744 | Active seconds: 5.0011348001700711 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: over_budget_at_last_frame | Last-frame resident/requested/new/overflow: 512/626/4/114
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Physical page requests exceed the budget at the last frame; quality is not validated.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 744/744 | 6.7012542858719826 | 7.2855018079280853 | available |
| cpu_frame_ms | 744/744 | 6.7060999999999993 | 7.6152 | available |
| gpu_frame_ms | 743/744 | 6.655744 | 7.228928 | available |
| cpu_render_thread_ms | 744/744 | 2.1382 | 2.7445 | available |
| gc_allocated_in_editor_frame_bytes | 744/744 | 18203 | 21692 | available |
| VSM.LayoutRemap_gpu_ms | 744/744 | 0.001024 | 0.001024 | available |
| VSM.Allocate_gpu_ms | 744/744 | 0.92108799999999991 | 1.194752 | available |
| VSM.InvalidateStatic_gpu_ms | 0/744 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 744/744 | 0.062336 | 0.079615999999999992 | available |
| VSM.StaticCasterCull_gpu_ms | 744/744 | 0.25561599999999995 | 0.636672 | available |
| VSM.DynamicCasterCull_gpu_ms | 744/744 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 744/744 | 0.23551999999999998 | 0.514304 | available |
| VSM.StaticRaster_gpu_ms | 744/744 | 0.042623999999999995 | 0.067583999999999991 | available |
| VSM.DynamicRaster_gpu_ms | 744/744 | 0.038911999999999995 | 0.042496 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/744 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 744/744 | 0.002304 | 0.0038399999999999997 | available |
| VSM.ResetFeedback_gpu_ms | 0/744 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 744/744 | 2.0241919999999998 | 2.3808 | available |

