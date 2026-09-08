# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## smrt8x8_a — completed_with_warnings
Attempt: 0 | Observations: 475 | Active seconds: 5.00121379676871 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 454/448/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 475/475 | 7.4072987772524357 | 25.366801768541336 | available |
| cpu_frame_ms | 475/475 | 7.5129 | 25.2625 | available |
| gpu_frame_ms | 454/475 | 5.930752 | 6.597888 | available |
| cpu_render_thread_ms | 475/475 | 3.166 | 4.3555 | available |
| gc_allocated_in_editor_frame_bytes | 475/475 | 14551 | 32932 | available |
| VSM.LayoutRemap_gpu_ms | 475/475 | 0.001024 | 0.0012799999999999999 | available |
| VSM.Allocate_gpu_ms | 475/475 | 0.37196799999999997 | 0.437504 | available |
| VSM.InvalidateStatic_gpu_ms | 0/475 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 475/475 | 0.063744 | 0.070144 | available |
| VSM.StaticCasterCull_gpu_ms | 475/475 | 0.245504 | 0.254976 | available |
| VSM.DynamicCasterCull_gpu_ms | 475/475 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 475/475 | 0.227072 | 0.235264 | available |
| VSM.StaticRaster_gpu_ms | 475/475 | 0.0076799999999999993 | 0.011007999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 475/475 | 0.038911999999999995 | 0.041728 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/475 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 475/475 | 0.002304 | 0.0025599999999999998 | available |
| VSM.ResetFeedback_gpu_ms | 0/475 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 475/475 | 1.4612479999999999 | 1.843712 | available |

