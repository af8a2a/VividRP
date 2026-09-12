# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length10_b — completed_with_warnings
Attempt: 0 | Observations: 376 | Active seconds: 4.0065342970521556 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/661/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 376/376 | 10.638948995620012 | 11.303195729851723 | available |
| cpu_frame_ms | 376/376 | 10.63125 | 11.6079 | available |
| gpu_frame_ms | 376/376 | 10.617472 | 11.236608 | available |
| cpu_render_thread_ms | 376/376 | 2.09955 | 2.6209 | available |
| gc_allocated_in_editor_frame_bytes | 376/376 | 18307 | 24091 | available |
| VSM.LayoutRemap_gpu_ms | 376/376 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 376/376 | 0.82995199999999991 | 1.199872 | available |
| VSM.Allocate_gpu_ms | 376/376 | 0.530944 | 0.546048 | available |
| VSM.InvalidateStatic_gpu_ms | 0/376 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 376/376 | 0.13568 | 0.33228799999999997 | available |
| VSM.StaticCasterCull_gpu_ms | 376/376 | 0.38118399999999997 | 0.69888 | available |
| VSM.DynamicCasterCull_gpu_ms | 376/376 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 376/376 | 0.35968 | 0.56319999999999992 | available |
| VSM.StaticRaster_gpu_ms | 376/376 | 0.007424 | 0.011264 | available |
| VSM.DynamicRaster_gpu_ms | 376/376 | 0.086016 | 0.09267199999999999 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/376 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 376/376 | 0.004608 | 0.005888 | available |
| VSM.ResetFeedback_gpu_ms | 376/376 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 376/376 | 2.622464 | 3.031552 | available |

