# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length50_b — completed_with_warnings
Attempt: 0 | Observations: 367 | Active seconds: 4.00933550170069 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/668/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 367/367 | 10.701204650104046 | 12.09589745849371 | available |
| cpu_frame_ms | 367/367 | 10.7752 | 12.2021 | available |
| gpu_frame_ms | 367/367 | 10.697216 | 12.072448 | available |
| cpu_render_thread_ms | 367/367 | 1.9234 | 2.2383 | available |
| gc_allocated_in_editor_frame_bytes | 367/367 | 18307 | 20525 | available |
| VSM.LayoutRemap_gpu_ms | 367/367 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 367/367 | 0.966144 | 1.344256 | available |
| VSM.Allocate_gpu_ms | 367/367 | 0.531968 | 0.542208 | available |
| VSM.InvalidateStatic_gpu_ms | 0/367 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 367/367 | 0.132096 | 0.133888 | available |
| VSM.StaticCasterCull_gpu_ms | 367/367 | 0.205568 | 0.40627199999999997 | available |
| VSM.DynamicCasterCull_gpu_ms | 367/367 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 367/367 | 0.18534399999999998 | 0.19097599999999998 | available |
| VSM.StaticRaster_gpu_ms | 367/367 | 0.0076799999999999993 | 0.010752 | available |
| VSM.DynamicRaster_gpu_ms | 367/367 | 0.088832 | 0.092928 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/367 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 367/367 | 0.004608 | 0.005376 | available |
| VSM.ResetFeedback_gpu_ms | 367/367 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 367/367 | 2.836992 | 3.31648 | available |

