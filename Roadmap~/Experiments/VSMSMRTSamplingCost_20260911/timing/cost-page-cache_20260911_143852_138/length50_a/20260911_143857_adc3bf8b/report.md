# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length50_a — completed_with_warnings
Attempt: 0 | Observations: 222 | Active seconds: 4.0138754039115625 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 222 | Median interval (s, 0=unavailable): 0.01812750141723285
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/668/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Positive GPU timings below 1 ms retained as diagnostics; no numeric filtering applied.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 222/222 | 18.126250244677067 | 18.668396398425102 | available |
| cpu_frame_ms | 222/222 | 18.170749999999998 | 19.1972 | available |
| gpu_frame_ms | 222/222 | 0.016896 | 0.018688 | available |
| cpu_render_thread_ms | 222/222 | 0.18105 | 0.2555 | available |
| gc_allocated_in_editor_frame_bytes | 222/222 | 20719 | 103580 | available |
| VSM.LayoutRemap_gpu_ms | 222/222 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 222/222 | 7.963519999999999 | 8.466432 | available |
| VSM.Allocate_gpu_ms | 222/222 | 0.52864 | 0.541952 | available |
| VSM.InvalidateStatic_gpu_ms | 0/222 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 222/222 | 0.12236799999999999 | 0.28620799999999996 | available |
| VSM.StaticCasterCull_gpu_ms | 222/222 | 0.363008 | 0.567808 | available |
| VSM.DynamicCasterCull_gpu_ms | 222/222 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 222/222 | 0.34342399999999995 | 0.506624 | available |
| VSM.StaticRaster_gpu_ms | 222/222 | 0.007168 | 0.010239999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 222/222 | 0.087296 | 0.094208 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/222 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 222/222 | 0.004352 | 0.005376 | available |
| VSM.ResetFeedback_gpu_ms | 222/222 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 222/222 | 2.8407039999999997 | 3.195392 | available |

