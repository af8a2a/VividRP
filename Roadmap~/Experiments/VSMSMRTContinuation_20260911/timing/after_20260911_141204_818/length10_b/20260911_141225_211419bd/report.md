# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length10_b — completed_with_warnings
Attempt: 0 | Observations: 228 | Active seconds: 4.0055911989795945 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/665/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 228/228 | 17.641648650169373 | 18.518997356295586 | available |
| cpu_frame_ms | 228/228 | 17.624049999999997 | 18.9556 | available |
| gpu_frame_ms | 227/228 | 17.607936 | 18.505472 | available |
| cpu_render_thread_ms | 228/228 | 2.72275 | 4.0125 | available |
| gc_allocated_in_editor_frame_bytes | 228/228 | 14471 | 92195 | available |
| VSM.LayoutRemap_gpu_ms | 228/228 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 228/228 | 4.7801599999999995 | 5.150976 | available |
| VSM.Allocate_gpu_ms | 228/228 | 0.551808 | 0.567808 | available |
| VSM.InvalidateStatic_gpu_ms | 0/228 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 228/228 | 0.135424 | 0.342784 | available |
| VSM.StaticCasterCull_gpu_ms | 228/228 | 0.379136 | 0.76544 | available |
| VSM.DynamicCasterCull_gpu_ms | 228/228 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 228/228 | 0.35852799999999996 | 0.662272 | available |
| VSM.StaticRaster_gpu_ms | 228/228 | 0.007424 | 0.010752 | available |
| VSM.DynamicRaster_gpu_ms | 228/228 | 0.08806399999999999 | 0.094975999999999991 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/228 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 228/228 | 0.004608 | 0.005632 | available |
| VSM.ResetFeedback_gpu_ms | 228/228 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 228/228 | 4.236928 | 4.733696 | available |

