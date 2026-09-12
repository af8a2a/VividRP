# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length50_a — completed_with_warnings
Attempt: 0 | Observations: 291 | Active seconds: 4.0004326034580515 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/665/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 291/291 | 13.735700398683548 | 14.433000236749649 | available |
| cpu_frame_ms | 291/291 | 13.7376 | 14.6388 | available |
| gpu_frame_ms | 291/291 | 13.712896 | 14.411776 | available |
| cpu_render_thread_ms | 291/291 | 2.0838 | 2.5422 | available |
| gc_allocated_in_editor_frame_bytes | 291/291 | 18307 | 24125 | available |
| VSM.LayoutRemap_gpu_ms | 291/291 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 291/291 | 3.44832 | 3.838208 | available |
| VSM.Allocate_gpu_ms | 291/291 | 0.53990399999999994 | 0.553728 | available |
| VSM.InvalidateStatic_gpu_ms | 0/291 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 291/291 | 0.132096 | 0.331264 | available |
| VSM.StaticCasterCull_gpu_ms | 291/291 | 0.366336 | 0.63846399999999992 | available |
| VSM.DynamicCasterCull_gpu_ms | 291/291 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 291/291 | 0.34559999999999996 | 0.542976 | available |
| VSM.StaticRaster_gpu_ms | 291/291 | 0.007168 | 0.011007999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 291/291 | 0.087808 | 0.094463999999999992 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/291 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 291/291 | 0.0025599999999999998 | 0.002816 | available |
| VSM.ResetFeedback_gpu_ms | 291/291 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 291/291 | 2.843648 | 3.1795199999999997 | available |

