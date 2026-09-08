# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## smrt256_a — completed_with_warnings
Attempt: 0 | Observations: 697 | Active seconds: 5.0034903982426187 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: over_budget_at_last_frame | Last-frame resident/requested/new/overflow: 256/449/3/193
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Physical page requests exceed the budget at the last frame; quality is not validated.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 697/697 | 7.1957977488636971 | 7.8366994857788086 | available |
| cpu_frame_ms | 697/697 | 7.1705 | 8.0744 | available |
| gpu_frame_ms | 696/697 | 7.153024 | 7.736064 | available |
| cpu_render_thread_ms | 697/697 | 2.0229 | 2.4751 | available |
| gc_allocated_in_editor_frame_bytes | 697/697 | 14539 | 28924 | available |
| VSM.LayoutRemap_gpu_ms | 697/697 | 0.001024 | 0.0012799999999999999 | available |
| VSM.Allocate_gpu_ms | 697/697 | 0.457472 | 0.535552 | available |
| VSM.InvalidateStatic_gpu_ms | 0/697 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 697/697 | 0.036095999999999996 | 0.042752 | available |
| VSM.StaticCasterCull_gpu_ms | 697/697 | 0.173312 | 0.551936 | available |
| VSM.DynamicCasterCull_gpu_ms | 697/697 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 697/697 | 0.154112 | 0.16127999999999998 | available |
| VSM.StaticRaster_gpu_ms | 697/697 | 0.022528 | 0.032512 | available |
| VSM.DynamicRaster_gpu_ms | 697/697 | 0.012799999999999999 | 0.014848 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/697 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 697/697 | 0.004608 | 0.005632 | available |
| VSM.ResetFeedback_gpu_ms | 0/697 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 697/697 | 2.692352 | 3.2194559999999997 | available |

