# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length50_b — completed_with_warnings
Attempt: 0 | Observations: 351 | Active seconds: 4.0047425028344676 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/665/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 351/351 | 11.371300555765629 | 12.124099768698215 | available |
| cpu_frame_ms | 351/351 | 11.3982 | 12.4258 | available |
| gpu_frame_ms | 351/351 | 11.357952 | 12.082688 | available |
| cpu_render_thread_ms | 351/351 | 2.1212 | 2.571 | available |
| gc_allocated_in_editor_frame_bytes | 351/351 | 18307 | 24125 | available |
| VSM.LayoutRemap_gpu_ms | 351/351 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 351/351 | 0.840448 | 1.2369919999999999 | available |
| VSM.Allocate_gpu_ms | 351/351 | 0.530176 | 0.544768 | available |
| VSM.InvalidateStatic_gpu_ms | 0/351 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 351/351 | 0.135424 | 0.359168 | available |
| VSM.StaticCasterCull_gpu_ms | 351/351 | 0.38118399999999997 | 0.73804799999999993 | available |
| VSM.DynamicCasterCull_gpu_ms | 351/351 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 351/351 | 0.359936 | 0.580352 | available |
| VSM.StaticRaster_gpu_ms | 351/351 | 0.007424 | 0.011519999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 351/351 | 0.086528 | 0.092416 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/351 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 351/351 | 0.004608 | 0.005632 | available |
| VSM.ResetFeedback_gpu_ms | 351/351 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 351/351 | 2.96832 | 3.387648 | available |

