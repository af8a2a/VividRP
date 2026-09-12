# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length50_a — completed_with_warnings
Attempt: 0 | Observations: 372 | Active seconds: 4.00199379960317 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/667/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 372/372 | 10.495999827980995 | 12.085799127817154 | available |
| cpu_frame_ms | 372/372 | 10.5611 | 12.1685 | available |
| gpu_frame_ms | 372/372 | 10.473472000000001 | 12.064768 | available |
| cpu_render_thread_ms | 372/372 | 1.93055 | 2.3064 | available |
| gc_allocated_in_editor_frame_bytes | 372/372 | 18307 | 18728 | available |
| VSM.LayoutRemap_gpu_ms | 372/372 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 372/372 | 0.9653759999999999 | 1.3672959999999998 | available |
| VSM.Allocate_gpu_ms | 372/372 | 0.531712 | 0.542464 | available |
| VSM.InvalidateStatic_gpu_ms | 0/372 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 372/372 | 0.132096 | 0.135936 | available |
| VSM.StaticCasterCull_gpu_ms | 372/372 | 0.205312 | 0.35456 | available |
| VSM.DynamicCasterCull_gpu_ms | 372/372 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 372/372 | 0.185088 | 0.19072 | available |
| VSM.StaticRaster_gpu_ms | 372/372 | 0.007424 | 0.011007999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 372/372 | 0.088832 | 0.092928 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/372 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 372/372 | 0.004608 | 0.005376 | available |
| VSM.ResetFeedback_gpu_ms | 372/372 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 372/372 | 2.8255999999999997 | 3.2453119999999998 | available |

