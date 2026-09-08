# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## pool512_a — completed_with_warnings
Attempt: 0 | Observations: 900 | Active seconds: 5.0018434027777765 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 425/419/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 900/900 | 5.580799886956811 | 6.0331989079713821 | available |
| cpu_frame_ms | 900/900 | 5.5446500000000007 | 6.318 | available |
| gpu_frame_ms | 899/900 | 5.53088 | 5.952768 | available |
| cpu_render_thread_ms | 900/900 | 1.8929 | 2.2002 | available |
| gc_allocated_in_editor_frame_bytes | 900/900 | 14551 | 14753 | available |
| VSM.LayoutRemap_gpu_ms | 900/900 | 0.001024 | 0.001024 | available |
| VSM.Allocate_gpu_ms | 900/900 | 0.35712 | 0.41958399999999996 | available |
| VSM.InvalidateStatic_gpu_ms | 0/900 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 900/900 | 0.062208 | 0.070912 | available |
| VSM.StaticCasterCull_gpu_ms | 900/900 | 0.241664 | 0.606208 | available |
| VSM.DynamicCasterCull_gpu_ms | 900/900 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 900/900 | 0.22323199999999999 | 0.423936 | available |
| VSM.StaticRaster_gpu_ms | 900/900 | 0.0076799999999999993 | 0.010496 | available |
| VSM.DynamicRaster_gpu_ms | 900/900 | 0.039168 | 0.041984 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/900 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 900/900 | 0.003328 | 0.004096 | available |
| VSM.ResetFeedback_gpu_ms | 0/900 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 900/900 | 1.6650239999999998 | 2.142464 | available |

