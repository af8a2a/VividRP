# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## pcf256_b — completed_with_warnings
Attempt: 0 | Observations: 815 | Active seconds: 5.0060020975056716 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: over_budget_at_last_frame | Last-frame resident/requested/new/overflow: 256/419/1/163
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Physical page requests exceed the budget at the last frame; quality is not validated.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 815/815 | 6.13959738984704 | 6.735402625054121 | available |
| cpu_frame_ms | 815/815 | 6.1149 | 6.9822 | available |
| gpu_frame_ms | 814/815 | 6.097792 | 6.689792 | available |
| cpu_render_thread_ms | 815/815 | 2.0002 | 2.3945 | available |
| gc_allocated_in_editor_frame_bytes | 815/815 | 14539 | 28924 | available |
| VSM.LayoutRemap_gpu_ms | 815/815 | 0.001024 | 0.0012799999999999999 | available |
| VSM.Allocate_gpu_ms | 815/815 | 0.491008 | 0.557312 | available |
| VSM.InvalidateStatic_gpu_ms | 0/815 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 815/815 | 0.03584 | 0.04224 | available |
| VSM.StaticCasterCull_gpu_ms | 815/815 | 0.171008 | 0.369152 | available |
| VSM.DynamicCasterCull_gpu_ms | 815/815 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 815/815 | 0.152576 | 0.159488 | available |
| VSM.StaticRaster_gpu_ms | 815/815 | 0.024832 | 0.030463999999999998 | available |
| VSM.DynamicRaster_gpu_ms | 815/815 | 0.012544 | 0.014591999999999999 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/815 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 815/815 | 0.004608 | 0.005632 | available |
| VSM.ResetFeedback_gpu_ms | 0/815 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 815/815 | 2.088192 | 2.590208 | available |

