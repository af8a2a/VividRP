# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## baseline256_d — completed_with_warnings
Attempt: 0 | Observations: 823 | Active seconds: 5.0021812996031727 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 9 | Median interval (s, 0=unavailable): 0.0066368976757367193
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: over_budget_at_last_frame | Last-frame resident/requested/new/overflow: 256/419/2/163
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Positive GPU timings below 1 ms retained as diagnostics; no numeric filtering applied.
- Physical page requests exceed the budget at the last frame; quality is not validated.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 823/823 | 5.910700187087059 | 7.0261973887681961 | available |
| cpu_frame_ms | 823/823 | 5.8876 | 7.3687 | available |
| gpu_frame_ms | 821/823 | 5.78688 | 6.473216 | available |
| cpu_render_thread_ms | 823/823 | 2.2617 | 3.033 | available |
| gc_allocated_in_editor_frame_bytes | 823/823 | 14551 | 14898 | available |
| VSM.LayoutRemap_gpu_ms | 823/823 | 0.001024 | 0.0012799999999999999 | available |
| VSM.Allocate_gpu_ms | 823/823 | 0.493056 | 0.56166399999999994 | available |
| VSM.InvalidateStatic_gpu_ms | 0/823 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 823/823 | 0.033535999999999996 | 0.041215999999999996 | available |
| VSM.StaticCasterCull_gpu_ms | 823/823 | 0.17152 | 0.551168 | available |
| VSM.DynamicCasterCull_gpu_ms | 823/823 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 823/823 | 0.152064 | 0.162304 | available |
| VSM.StaticRaster_gpu_ms | 823/823 | 0.024576 | 0.030976 | available |
| VSM.DynamicRaster_gpu_ms | 823/823 | 0.013824 | 0.01664 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/823 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 823/823 | 0.0025599999999999998 | 0.004096 | available |
| VSM.ResetFeedback_gpu_ms | 0/823 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 823/823 | 1.5999999999999999 | 2.273536 | available |

