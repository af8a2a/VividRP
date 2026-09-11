# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length50_b — completed_with_warnings
Attempt: 0 | Observations: 230 | Active seconds: 4.0079640943877521 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/667/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 230/230 | 17.47825276106596 | 18.521202728152275 | available |
| cpu_frame_ms | 230/230 | 17.4281 | 18.6039 | available |
| gpu_frame_ms | 230/230 | 17.457408 | 18.494976 | available |
| cpu_render_thread_ms | 230/230 | 2.30165 | 3.1154 | available |
| gc_allocated_in_editor_frame_bytes | 230/230 | 14621 | 119845 | available |
| VSM.LayoutRemap_gpu_ms | 230/230 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 230/230 | 4.121472 | 4.460544 | available |
| VSM.Allocate_gpu_ms | 230/230 | 0.668928 | 0.685568 | available |
| VSM.InvalidateStatic_gpu_ms | 0/230 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 230/230 | 0.133376 | 0.297472 | available |
| VSM.StaticCasterCull_gpu_ms | 230/230 | 0.366848 | 0.628224 | available |
| VSM.DynamicCasterCull_gpu_ms | 230/230 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 230/230 | 0.346624 | 0.531712 | available |
| VSM.StaticRaster_gpu_ms | 230/230 | 0.007424 | 0.010752 | available |
| VSM.DynamicRaster_gpu_ms | 230/230 | 0.087935999999999986 | 0.094463999999999992 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/230 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 230/230 | 0.004352 | 0.005376 | available |
| VSM.ResetFeedback_gpu_ms | 230/230 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 230/230 | 4.556032 | 4.9674239999999994 | available |

