# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## base256_a — completed_with_warnings
Attempt: 0 | Observations: 811 | Active seconds: 5.0009888038548809 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: False | Source: editor_frames_unattributed | Quality: not_validated
Residency: over_budget_at_last_frame | Last-frame resident/requested/new/overflow: 256/419/1/163
- Fresh CPU frame timing coverage is below 95%.
- Fresh whole-frame GPU coverage is below 95%; Frame Timing Stats enabled is not evidence of valid data.
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Physical page requests exceed the budget at the last frame; quality is not validated.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 811/811 | 6.1141937039792538 | 6.7646969109773636 | available |
| cpu_frame_ms | 0/811 |  |  | no_samples_or_not_executed |
| gpu_frame_ms | 0/811 |  |  | no_samples_or_not_executed |
| cpu_render_thread_ms | 0/811 |  |  | no_samples_or_not_executed |
| gc_allocated_in_editor_frame_bytes | 811/811 | 18203 | 21276 | available |
| VSM.LayoutRemap_gpu_ms | 811/811 | 0.001024 | 0.001024 | available |
| VSM.Allocate_gpu_ms | 811/811 | 0.48870399999999997 | 0.55910399999999993 | available |
| VSM.InvalidateStatic_gpu_ms | 0/811 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 811/811 | 0.032768 | 0.041471999999999995 | available |
| VSM.StaticCasterCull_gpu_ms | 811/811 | 0.170752 | 0.422656 | available |
| VSM.DynamicCasterCull_gpu_ms | 811/811 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 811/811 | 0.152576 | 0.162304 | available |
| VSM.StaticRaster_gpu_ms | 811/811 | 0.022015999999999997 | 0.028672 | available |
| VSM.DynamicRaster_gpu_ms | 811/811 | 0.014079999999999999 | 0.016384 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/811 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 811/811 | 0.002048 | 0.0025599999999999998 | available |
| VSM.ResetFeedback_gpu_ms | 0/811 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 811/811 | 2.014976 | 2.4491519999999998 | available |

