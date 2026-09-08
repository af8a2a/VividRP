# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## bounded512020_a — completed_with_warnings
Attempt: 0 | Observations: 885 | Active seconds: 5.00266750283447 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 511/501/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 885/885 | 5.6429989635944366 | 6.2379040755331516 | available |
| cpu_frame_ms | 885/885 | 5.6329 | 6.3857 | available |
| gpu_frame_ms | 884/885 | 5.61024 | 6.159616 | available |
| cpu_render_thread_ms | 885/885 | 1.9381 | 2.3594 | available |
| gc_allocated_in_editor_frame_bytes | 885/885 | 14551 | 14753 | available |
| VSM.LayoutRemap_gpu_ms | 885/885 | 0.001024 | 0.001024 | available |
| VSM.Allocate_gpu_ms | 885/885 | 0.423168 | 0.542208 | available |
| VSM.InvalidateStatic_gpu_ms | 0/885 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 885/885 | 0.062208 | 0.072191999999999992 | available |
| VSM.StaticCasterCull_gpu_ms | 885/885 | 0.25907199999999997 | 0.628992 | available |
| VSM.DynamicCasterCull_gpu_ms | 885/885 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 885/885 | 0.240896 | 0.46643199999999996 | available |
| VSM.StaticRaster_gpu_ms | 885/885 | 0.0076799999999999993 | 0.009984 | available |
| VSM.DynamicRaster_gpu_ms | 885/885 | 0.038911999999999995 | 0.041984 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/885 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 885/885 | 0.003328 | 0.004096 | available |
| VSM.ResetFeedback_gpu_ms | 0/885 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 885/885 | 1.6250879999999999 | 2.0339199999999997 | available |

