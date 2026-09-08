# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## smrt8x8_b — completed_with_warnings
Attempt: 0 | Observations: 358 | Active seconds: 5.0063853954081594 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: False | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 454/451/0/0
- Frame intervals over 100 ms retained; investigate before comparing.
- Fresh whole-frame GPU coverage is below 95%; Frame Timing Stats enabled is not evidence of valid data.
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 358/358 | 10.459353681653738 | 34.816693514585495 | available |
| cpu_frame_ms | 358/358 | 9.98965 | 35.6733 | available |
| gpu_frame_ms | 326/358 | 6.3719680000000007 | 6.997248 | available |
| cpu_render_thread_ms | 358/358 | 3.5206 | 4.3065 | available |
| gc_allocated_in_editor_frame_bytes | 358/358 | 14551 | 44699 | available |
| VSM.LayoutRemap_gpu_ms | 358/358 | 0.001024 | 0.0012799999999999999 | available |
| VSM.Allocate_gpu_ms | 358/358 | 0.368384 | 0.434944 | available |
| VSM.InvalidateStatic_gpu_ms | 0/358 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 358/358 | 0.063232 | 0.064512 | available |
| VSM.StaticCasterCull_gpu_ms | 358/358 | 0.2432 | 0.249856 | available |
| VSM.DynamicCasterCull_gpu_ms | 358/358 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 358/358 | 0.22527999999999998 | 0.231424 | available |
| VSM.StaticRaster_gpu_ms | 358/358 | 0.0076799999999999993 | 0.010239999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 358/358 | 0.039424 | 0.041984 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/358 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 358/358 | 0.002048 | 0.0025599999999999998 | available |
| VSM.ResetFeedback_gpu_ms | 0/358 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 358/358 | 1.8266879999999999 | 2.2630399999999997 | available |

