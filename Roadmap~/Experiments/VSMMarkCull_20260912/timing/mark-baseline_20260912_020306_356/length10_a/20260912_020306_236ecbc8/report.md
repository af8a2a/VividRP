# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length10_a — completed_with_warnings
Attempt: 0 | Observations: 156 | Active seconds: 4.0008314980158737 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: False | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 670/665/0/0
- Fresh CPU frame timing coverage is below 95%.
- Fresh whole-frame GPU coverage is below 95%; Frame Timing Stats enabled is not evidence of valid data.
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 156/156 | 13.357146177440882 | 50.581101328134537 | available |
| cpu_frame_ms | 0/156 |  |  | no_samples_or_not_executed |
| gpu_frame_ms | 0/156 |  |  | no_samples_or_not_executed |
| cpu_render_thread_ms | 0/156 |  |  | no_samples_or_not_executed |
| gc_allocated_in_editor_frame_bytes | 156/156 | 18359 | 1887240 | available |
| VSM.LayoutRemap_gpu_ms | 156/156 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 156/156 | 2.803072 | 3.30496 | available |
| VSM.Allocate_gpu_ms | 156/156 | 0.642304 | 0.667648 | available |
| VSM.InvalidateStatic_gpu_ms | 55/156 | 0.126208 | 0.12723199999999998 | available |
| VSM.ClearPhysicalPages_gpu_ms | 156/156 | 0.132096 | 0.133888 | available |
| VSM.StaticCasterCull_gpu_ms | 156/156 | 0.365568 | 0.779264 | available |
| VSM.DynamicCasterCull_gpu_ms | 156/156 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 156/156 | 0.34265599999999996 | 0.72729599999999994 | available |
| VSM.StaticRaster_gpu_ms | 156/156 | 0.0076799999999999993 | 36.958976 | available |
| VSM.DynamicRaster_gpu_ms | 156/156 | 0.087039999999999992 | 0.094975999999999991 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/156 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 156/156 | 0.0025599999999999998 | 0.009472 | available |
| VSM.ResetFeedback_gpu_ms | 156/156 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 156/156 | 2.826112 | 3.163904 | available |

