# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length10_a — completed_with_warnings
Attempt: 0 | Observations: 259 | Active seconds: 4.0046294005102041 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 670/659/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 259/259 | 15.51380380988121 | 16.196202486753464 | available |
| cpu_frame_ms | 259/259 | 15.5248 | 16.3718 | available |
| gpu_frame_ms | 259/259 | 15.486208 | 16.179456 | available |
| cpu_render_thread_ms | 259/259 | 2.2117 | 2.7842 | available |
| gc_allocated_in_editor_frame_bytes | 259/259 | 14539 | 107924 | available |
| VSM.LayoutRemap_gpu_ms | 259/259 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 259/259 | 3.3891839999999998 | 3.8625279999999997 | available |
| VSM.Allocate_gpu_ms | 259/259 | 0.666624 | 0.685568 | available |
| VSM.InvalidateStatic_gpu_ms | 0/259 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 259/259 | 0.133632 | 0.305152 | available |
| VSM.StaticCasterCull_gpu_ms | 259/259 | 0.365056 | 0.627456 | available |
| VSM.DynamicCasterCull_gpu_ms | 259/259 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 259/259 | 0.345088 | 0.53990399999999994 | available |
| VSM.StaticRaster_gpu_ms | 259/259 | 0.007168 | 0.011007999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 259/259 | 0.08832 | 0.094975999999999991 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/259 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 259/259 | 0.004352 | 0.005376 | available |
| VSM.ResetFeedback_gpu_ms | 259/259 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 259/259 | 3.8592 | 4.299264 | available |

