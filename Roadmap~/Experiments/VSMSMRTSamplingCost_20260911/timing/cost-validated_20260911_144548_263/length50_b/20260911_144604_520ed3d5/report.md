# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length50_b — completed_with_warnings
Attempt: 0 | Observations: 307 | Active seconds: 4.0063068027210846 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/667/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 307/307 | 13.047902844846249 | 13.933000154793262 | available |
| cpu_frame_ms | 307/307 | 13.0363 | 14.3287 | available |
| gpu_frame_ms | 307/307 | 13.033472 | 13.902592 | available |
| cpu_render_thread_ms | 307/307 | 2.5187 | 3.1572 | available |
| gc_allocated_in_editor_frame_bytes | 307/307 | 14419 | 84755 | available |
| VSM.LayoutRemap_gpu_ms | 307/307 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 307/307 | 4.123648 | 4.556032 | available |
| VSM.Allocate_gpu_ms | 307/307 | 0.550144 | 0.566784 | available |
| VSM.InvalidateStatic_gpu_ms | 0/307 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 307/307 | 0.13055999999999998 | 0.32051199999999996 | available |
| VSM.StaticCasterCull_gpu_ms | 307/307 | 0.355584 | 0.66457599999999994 | available |
| VSM.DynamicCasterCull_gpu_ms | 307/307 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 307/307 | 0.33433599999999997 | 0.6016 | available |
| VSM.StaticRaster_gpu_ms | 307/307 | 0.007424 | 0.011519999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 307/307 | 0.08806399999999999 | 0.09548799999999999 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/307 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 307/307 | 0.003328 | 0.004352 | available |
| VSM.ResetFeedback_gpu_ms | 307/307 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 307/307 | 2.7630079999999997 | 3.395072 | available |

