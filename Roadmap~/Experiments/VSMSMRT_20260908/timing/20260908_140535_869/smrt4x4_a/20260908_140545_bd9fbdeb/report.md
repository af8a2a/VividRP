# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## smrt4x4_a — completed_with_warnings
Attempt: 0 | Observations: 824 | Active seconds: 5.0022106009070342 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 1 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 439/433/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.
- Positive GPU timings below 1 ms retained as diagnostics; no numeric filtering applied.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 824/824 | 6.0096017550677061 | 6.7862030118703842 | available |
| cpu_frame_ms | 824/824 | 5.9996 | 7.2263 | available |
| gpu_frame_ms | 821/824 | 5.885952 | 6.583296 | available |
| cpu_render_thread_ms | 824/824 | 2.2518000000000002 | 3.0383 | available |
| gc_allocated_in_editor_frame_bytes | 824/824 | 14539 | 20949 | available |
| VSM.LayoutRemap_gpu_ms | 824/824 | 0.001024 | 0.0012799999999999999 | available |
| VSM.Allocate_gpu_ms | 824/824 | 0.388608 | 0.484352 | available |
| VSM.InvalidateStatic_gpu_ms | 0/824 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 824/824 | 0.068352 | 0.15923199999999998 | available |
| VSM.StaticCasterCull_gpu_ms | 824/824 | 0.246528 | 0.65612799999999993 | available |
| VSM.DynamicCasterCull_gpu_ms | 824/824 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 824/824 | 0.227072 | 0.434432 | available |
| VSM.StaticRaster_gpu_ms | 824/824 | 0.0076799999999999993 | 0.010239999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 824/824 | 0.038911999999999995 | 0.041728 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/824 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 824/824 | 0.002816 | 0.005376 | available |
| VSM.ResetFeedback_gpu_ms | 0/824 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 824/824 | 1.614976 | 2.157312 | available |

