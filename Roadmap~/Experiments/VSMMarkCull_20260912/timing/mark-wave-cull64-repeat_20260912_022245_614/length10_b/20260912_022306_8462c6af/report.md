# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length10_b — completed_with_warnings
Attempt: 0 | Observations: 244 | Active seconds: 4.0191512967687046 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/666/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 244/244 | 17.138549126684666 | 19.908100366592407 | available |
| cpu_frame_ms | 244/244 | 16.9635 | 20.5591 | available |
| gpu_frame_ms | 244/244 | 17.032704000000003 | 19.625472 | available |
| cpu_render_thread_ms | 244/244 | 3.0136000000000003 | 4.6313 | available |
| gc_allocated_in_editor_frame_bytes | 244/244 | 18307 | 36208 | available |
| VSM.LayoutRemap_gpu_ms | 244/244 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 244/244 | 0.937472 | 5.70496 | available |
| VSM.Allocate_gpu_ms | 244/244 | 0.533504 | 0.557312 | available |
| VSM.InvalidateStatic_gpu_ms | 0/244 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 244/244 | 0.132224 | 2.292736 | available |
| VSM.StaticCasterCull_gpu_ms | 244/244 | 0.20416 | 2.364672 | available |
| VSM.DynamicCasterCull_gpu_ms | 244/244 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 244/244 | 0.178176 | 0.203008 | available |
| VSM.StaticRaster_gpu_ms | 244/244 | 0.007424 | 0.01792 | available |
| VSM.DynamicRaster_gpu_ms | 244/244 | 0.088832 | 0.097024 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/244 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 244/244 | 0.003584 | 0.004352 | available |
| VSM.ResetFeedback_gpu_ms | 244/244 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 244/244 | 2.747776 | 7.145728 | available |

