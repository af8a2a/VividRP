# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## pcf512_a — completed_with_warnings
Attempt: 0 | Observations: 816 | Active seconds: 5.0026955002834477 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 425/419/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 816/816 | 6.0625495389103889 | 6.9046979770064354 | available |
| cpu_frame_ms | 816/816 | 6.0447000000000006 | 7.1947 | available |
| gpu_frame_ms | 814/816 | 6.0184320000000007 | 6.715648 | available |
| cpu_render_thread_ms | 816/816 | 2.0614 | 2.7482 | available |
| gc_allocated_in_editor_frame_bytes | 816/816 | 14551 | 20961 | available |
| VSM.LayoutRemap_gpu_ms | 816/816 | 0.001024 | 0.001024 | available |
| VSM.Allocate_gpu_ms | 816/816 | 0.378112 | 0.463616 | available |
| VSM.InvalidateStatic_gpu_ms | 0/816 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 816/816 | 0.062464 | 0.07168 | available |
| VSM.StaticCasterCull_gpu_ms | 816/816 | 0.243968 | 0.508416 | available |
| VSM.DynamicCasterCull_gpu_ms | 816/816 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 816/816 | 0.224768 | 0.397312 | available |
| VSM.StaticRaster_gpu_ms | 816/816 | 0.0076799999999999993 | 0.009984 | available |
| VSM.DynamicRaster_gpu_ms | 816/816 | 0.03968 | 0.04224 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/816 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 816/816 | 0.002304 | 0.002816 | available |
| VSM.ResetFeedback_gpu_ms | 0/816 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 816/816 | 1.9521279999999999 | 2.445824 | available |

