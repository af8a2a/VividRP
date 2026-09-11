# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length50_a — completed_with_warnings
Attempt: 0 | Observations: 451 | Active seconds: 4.0093348993764195 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 2 | Median interval (s, 0=unavailable): 0.072057001133792653
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 668/654/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Positive GPU timings below 1 ms retained as diagnostics; no numeric filtering applied.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 451/451 | 8.9304987341165543 | 9.5955990254879 | available |
| cpu_frame_ms | 451/451 | 8.9118 | 9.7765 | available |
| gpu_frame_ms | 451/451 | 8.902144 | 9.583872 | available |
| cpu_render_thread_ms | 451/451 | 2.1757 | 2.734 | available |
| gc_allocated_in_editor_frame_bytes | 451/451 | 14419 | 49561 | available |
| VSM.LayoutRemap_gpu_ms | 451/451 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 451/451 | 3.827968 | 4.1832959999999995 | available |
| VSM.Allocate_gpu_ms | 451/451 | 0.526336 | 0.541184 | available |
| VSM.InvalidateStatic_gpu_ms | 0/451 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 451/451 | 0.121344 | 0.122624 | available |
| VSM.StaticCasterCull_gpu_ms | 451/451 | 0.351744 | 0.56243199999999993 | available |
| VSM.DynamicCasterCull_gpu_ms | 451/451 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 451/451 | 0.32972799999999997 | 0.50176 | available |
| VSM.StaticRaster_gpu_ms | 451/451 | 0.007168 | 0.011519999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 451/451 | 0.087296 | 0.093696 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/451 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 451/451 | 0.003328 | 0.004096 | available |
| VSM.ResetFeedback_gpu_ms | 451/451 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 451/451 | 0.388096 | 1.084416 | available |

