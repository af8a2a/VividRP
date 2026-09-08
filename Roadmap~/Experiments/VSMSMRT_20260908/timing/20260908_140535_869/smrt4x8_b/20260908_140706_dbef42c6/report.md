# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## smrt4x8_b — completed_with_warnings
Attempt: 0 | Observations: 698 | Active seconds: 5.0042300028344613 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 6 | Median interval (s, 0=unavailable): 0.071415299036289071
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 454/451/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Positive GPU timings below 1 ms retained as diagnostics; no numeric filtering applied.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 698/698 | 7.1389032527804375 | 7.7955001033842564 | available |
| cpu_frame_ms | 698/698 | 7.1438500000000005 | 8.1438 | available |
| gpu_frame_ms | 697/698 | 7.085824 | 7.694592 | available |
| cpu_render_thread_ms | 698/698 | 2.08135 | 2.7115 | available |
| gc_allocated_in_editor_frame_bytes | 698/698 | 14539 | 28924 | available |
| VSM.LayoutRemap_gpu_ms | 698/698 | 0.001024 | 0.0012799999999999999 | available |
| VSM.Allocate_gpu_ms | 698/698 | 0.37529599999999996 | 0.44595199999999996 | available |
| VSM.InvalidateStatic_gpu_ms | 0/698 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 698/698 | 0.068352 | 0.080639999999999989 | available |
| VSM.StaticCasterCull_gpu_ms | 698/698 | 0.247296 | 0.632832 | available |
| VSM.DynamicCasterCull_gpu_ms | 698/698 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 698/698 | 0.228352 | 0.556288 | available |
| VSM.StaticRaster_gpu_ms | 698/698 | 0.0076799999999999993 | 0.010752 | available |
| VSM.DynamicRaster_gpu_ms | 698/698 | 0.0384 | 0.040959999999999996 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/698 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 698/698 | 0.004608 | 0.005632 | available |
| VSM.ResetFeedback_gpu_ms | 0/698 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 698/698 | 2.501376 | 3.0126079999999997 | available |

