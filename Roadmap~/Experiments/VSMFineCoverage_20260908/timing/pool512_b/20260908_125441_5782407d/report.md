# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## pool512_b — completed_with_warnings
Attempt: 0 | Observations: 870 | Active seconds: 5.0013001984127072 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 425/419/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 870/870 | 5.7667482178658247 | 6.3131945207715034 | available |
| cpu_frame_ms | 870/870 | 5.7531 | 6.4709 | available |
| gpu_frame_ms | 868/870 | 5.724672 | 6.244096 | available |
| cpu_render_thread_ms | 870/870 | 1.9712 | 2.4705 | available |
| gc_allocated_in_editor_frame_bytes | 870/870 | 14551 | 14753 | available |
| VSM.LayoutRemap_gpu_ms | 870/870 | 0.001024 | 0.0012799999999999999 | available |
| VSM.Allocate_gpu_ms | 870/870 | 0.380928 | 0.471808 | available |
| VSM.InvalidateStatic_gpu_ms | 0/870 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 870/870 | 0.068352 | 0.26700799999999997 | available |
| VSM.StaticCasterCull_gpu_ms | 870/870 | 0.247808 | 0.63769599999999993 | available |
| VSM.DynamicCasterCull_gpu_ms | 870/870 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 870/870 | 0.22860799999999998 | 0.434944 | available |
| VSM.StaticRaster_gpu_ms | 870/870 | 0.0076799999999999993 | 0.010239999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 870/870 | 0.039424 | 0.042752 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/870 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 870/870 | 0.003328 | 0.004352 | available |
| VSM.ResetFeedback_gpu_ms | 0/870 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 870/870 | 1.71328 | 2.2878719999999997 | available |

