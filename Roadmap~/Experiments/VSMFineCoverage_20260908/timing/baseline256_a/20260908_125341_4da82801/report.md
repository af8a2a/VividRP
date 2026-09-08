# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## baseline256_a — completed_with_warnings
Attempt: 0 | Observations: 932 | Active seconds: 5.0033071995464873 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: over_budget_at_last_frame | Last-frame resident/requested/new/overflow: 256/421/2/165
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Physical page requests exceed the budget at the last frame; quality is not validated.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 932/932 | 5.3459538612514734 | 5.9223002754151821 | available |
| cpu_frame_ms | 932/932 | 5.3587000000000007 | 6.0177 | available |
| gpu_frame_ms | 930/932 | 5.30432 | 5.891328 | available |
| cpu_render_thread_ms | 932/932 | 1.92815 | 2.3506 | available |
| gc_allocated_in_editor_frame_bytes | 932/932 | 14551 | 14753 | available |
| VSM.LayoutRemap_gpu_ms | 932/932 | 0.001024 | 0.001024 | available |
| VSM.Allocate_gpu_ms | 932/932 | 0.492288 | 0.59264 | available |
| VSM.InvalidateStatic_gpu_ms | 0/932 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 932/932 | 0.033535999999999996 | 0.040959999999999996 | available |
| VSM.StaticCasterCull_gpu_ms | 932/932 | 0.17024 | 0.365056 | available |
| VSM.DynamicCasterCull_gpu_ms | 932/932 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 932/932 | 0.151808 | 0.161024 | available |
| VSM.StaticRaster_gpu_ms | 932/932 | 0.024832 | 0.031487999999999995 | available |
| VSM.DynamicRaster_gpu_ms | 932/932 | 0.013824 | 0.016128 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/932 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 932/932 | 0.003328 | 0.004096 | available |
| VSM.ResetFeedback_gpu_ms | 0/932 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 932/932 | 1.4949119999999998 | 1.943552 | available |

