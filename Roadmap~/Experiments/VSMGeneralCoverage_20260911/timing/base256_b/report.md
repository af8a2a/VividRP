# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## base256_b — completed_with_warnings
Attempt: 0 | Observations: 773 | Active seconds: 5.0010310941042917 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: over_budget_at_last_frame | Last-frame resident/requested/new/overflow: 256/419/1/163
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Physical page requests exceed the budget at the last frame; quality is not validated.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 773/773 | 6.3312002457678318 | 7.1351048536598682 | available |
| cpu_frame_ms | 773/773 | 6.3334 | 7.6232 | available |
| gpu_frame_ms | 772/773 | 6.27648 | 6.900224 | available |
| cpu_render_thread_ms | 773/773 | 2.2873 | 3.1288 | available |
| gc_allocated_in_editor_frame_bytes | 773/773 | 18203 | 23987 | available |
| VSM.LayoutRemap_gpu_ms | 773/773 | 0.001024 | 0.001024 | available |
| VSM.Allocate_gpu_ms | 773/773 | 0.469504 | 0.508672 | available |
| VSM.InvalidateStatic_gpu_ms | 0/773 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 773/773 | 0.03456 | 0.042496 | available |
| VSM.StaticCasterCull_gpu_ms | 773/773 | 0.174592 | 0.50969599999999993 | available |
| VSM.DynamicCasterCull_gpu_ms | 773/773 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 773/773 | 0.155136 | 0.163072 | available |
| VSM.StaticRaster_gpu_ms | 773/773 | 0.024319999999999998 | 0.032 | available |
| VSM.DynamicRaster_gpu_ms | 773/773 | 0.014079999999999999 | 0.016384 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/773 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 773/773 | 0.002304 | 0.002816 | available |
| VSM.ResetFeedback_gpu_ms | 0/773 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 773/773 | 2.088448 | 2.613504 | available |

