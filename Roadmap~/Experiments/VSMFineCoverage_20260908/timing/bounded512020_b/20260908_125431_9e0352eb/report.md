# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## bounded512020_b — completed_with_warnings
Attempt: 0 | Observations: 843 | Active seconds: 5.00024119897958 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 511/503/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 843/843 | 5.9380033053457737 | 6.4602959901094437 | available |
| cpu_frame_ms | 843/843 | 5.943 | 6.6611 | available |
| gpu_frame_ms | 842/843 | 5.90528 | 6.405632 | available |
| cpu_render_thread_ms | 843/843 | 1.9111 | 2.1683 | available |
| gc_allocated_in_editor_frame_bytes | 843/843 | 14551 | 14753 | available |
| VSM.LayoutRemap_gpu_ms | 843/843 | 0.001024 | 0.001024 | available |
| VSM.Allocate_gpu_ms | 843/843 | 0.40243199999999996 | 0.48768 | available |
| VSM.InvalidateStatic_gpu_ms | 0/843 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 843/843 | 0.062464 | 0.081152 | available |
| VSM.StaticCasterCull_gpu_ms | 843/843 | 0.259328 | 0.687616 | available |
| VSM.DynamicCasterCull_gpu_ms | 843/843 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 843/843 | 0.24064 | 0.616448 | available |
| VSM.StaticRaster_gpu_ms | 843/843 | 0.0076799999999999993 | 0.010496 | available |
| VSM.DynamicRaster_gpu_ms | 843/843 | 0.039168 | 0.041728 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/843 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 843/843 | 0.003328 | 0.004352 | available |
| VSM.ResetFeedback_gpu_ms | 0/843 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 843/843 | 2.0019199999999997 | 2.4819199999999997 | available |

