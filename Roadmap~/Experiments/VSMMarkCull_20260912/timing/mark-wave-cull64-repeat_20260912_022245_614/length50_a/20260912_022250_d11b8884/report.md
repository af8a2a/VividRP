# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length50_a — completed_with_warnings
Attempt: 0 | Observations: 330 | Active seconds: 4.000241397392287 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 4 | Median interval (s, 0=unavailable): 0.0476335034013573
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/667/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Positive GPU timings below 1 ms retained as diagnostics; no numeric filtering applied.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 330/330 | 11.489799711853266 | 14.220195822417736 | available |
| cpu_frame_ms | 330/330 | 11.52375 | 14.2727 | available |
| gpu_frame_ms | 327/330 | 11.458304 | 13.962752 | available |
| cpu_render_thread_ms | 330/330 | 2.2748999999999997 | 3.7477 | available |
| gc_allocated_in_editor_frame_bytes | 330/330 | 18307 | 36353 | available |
| VSM.LayoutRemap_gpu_ms | 330/330 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 330/330 | 0.99327999999999994 | 1.42208 | available |
| VSM.Allocate_gpu_ms | 330/330 | 0.532992 | 0.63744 | available |
| VSM.InvalidateStatic_gpu_ms | 0/330 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 330/330 | 0.130304 | 0.32716799999999996 | available |
| VSM.StaticCasterCull_gpu_ms | 330/330 | 0.198784 | 0.47948799999999997 | available |
| VSM.DynamicCasterCull_gpu_ms | 330/330 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 330/330 | 0.176896 | 0.184576 | available |
| VSM.StaticRaster_gpu_ms | 330/330 | 0.007424 | 0.012032 | available |
| VSM.DynamicRaster_gpu_ms | 330/330 | 0.088832 | 0.09548799999999999 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/330 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 330/330 | 0.003328 | 0.004352 | available |
| VSM.ResetFeedback_gpu_ms | 330/330 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 330/330 | 2.93952 | 3.552768 | available |

