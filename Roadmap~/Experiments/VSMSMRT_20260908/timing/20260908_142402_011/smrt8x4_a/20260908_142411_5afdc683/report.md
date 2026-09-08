# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## smrt8x4_a — completed_with_warnings
Attempt: 0 | Observations: 704 | Active seconds: 5.0054082979024912 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 439/434/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 704/704 | 7.090848172083497 | 7.72470235824585 | available |
| cpu_frame_ms | 704/704 | 7.05765 | 8.1781 | available |
| gpu_frame_ms | 702/704 | 7.0348799999999994 | 7.638016 | available |
| cpu_render_thread_ms | 704/704 | 2.0635000000000003 | 2.747 | available |
| gc_allocated_in_editor_frame_bytes | 704/704 | 14551 | 21163 | available |
| VSM.LayoutRemap_gpu_ms | 704/704 | 0.001024 | 0.0012799999999999999 | available |
| VSM.Allocate_gpu_ms | 704/704 | 0.386816 | 0.480768 | available |
| VSM.InvalidateStatic_gpu_ms | 0/704 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 704/704 | 0.068352 | 0.07551999999999999 | available |
| VSM.StaticCasterCull_gpu_ms | 704/704 | 0.247552 | 0.58163199999999993 | available |
| VSM.DynamicCasterCull_gpu_ms | 704/704 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 704/704 | 0.22784 | 0.419328 | available |
| VSM.StaticRaster_gpu_ms | 704/704 | 0.0076799999999999993 | 0.010239999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 704/704 | 0.039424 | 0.042496 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/704 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 704/704 | 0.002304 | 0.002816 | available |
| VSM.ResetFeedback_gpu_ms | 0/704 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 704/704 | 2.4152319999999996 | 2.913792 | available |

