# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## bounded384005_b — completed_with_warnings
Attempt: 0 | Observations: 273 | Active seconds: 5.0019998015873028 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: False | Source: editor_frames_unattributed | Quality: not_validated
Residency: over_budget_at_last_frame | Last-frame resident/requested/new/overflow: 384/504/4/120
- Frame intervals over 100 ms retained; investigate before comparing.
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Physical page requests exceed the budget at the last frame; quality is not validated.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 273/273 | 6.0560018755495548 | 7.8753046691417694 | available |
| cpu_frame_ms | 273/273 | 6.0831 | 8.1606 | available |
| gpu_frame_ms | 271/273 | 5.90336 | 6.65856 | available |
| cpu_render_thread_ms | 273/273 | 2.4214 | 3.5862 | available |
| gc_allocated_in_editor_frame_bytes | 273/273 | 14551 | 14753 | available |
| VSM.LayoutRemap_gpu_ms | 273/273 | 0.001024 | 0.001024 | available |
| VSM.Allocate_gpu_ms | 273/273 | 0.727808 | 0.89523199999999992 | available |
| VSM.InvalidateStatic_gpu_ms | 0/273 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 273/273 | 0.048639999999999996 | 0.060927999999999996 | available |
| VSM.StaticCasterCull_gpu_ms | 273/273 | 0.207616 | 0.632832 | available |
| VSM.DynamicCasterCull_gpu_ms | 273/273 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 273/273 | 0.188416 | 0.37068799999999996 | available |
| VSM.StaticRaster_gpu_ms | 273/273 | 0.041984 | 0.062464 | available |
| VSM.DynamicRaster_gpu_ms | 273/273 | 0.025856 | 0.029696 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/273 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 273/273 | 0.0025599999999999998 | 0.004096 | available |
| VSM.ResetFeedback_gpu_ms | 0/273 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 273/273 | 1.39776 | 2.075392 | available |

