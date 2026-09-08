# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## smrt4x8_a — completed_with_warnings
Attempt: 0 | Observations: 759 | Active seconds: 5.0024669997165532 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 454/449/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 759/759 | 6.5381024032831192 | 7.3120961897075176 | available |
| cpu_frame_ms | 759/759 | 6.5617 | 7.6069 | available |
| gpu_frame_ms | 758/759 | 6.488704 | 7.165696 | available |
| cpu_render_thread_ms | 759/759 | 2.0826 | 2.8371 | available |
| gc_allocated_in_editor_frame_bytes | 759/759 | 14539 | 20949 | available |
| VSM.LayoutRemap_gpu_ms | 759/759 | 0.001024 | 0.0012799999999999999 | available |
| VSM.Allocate_gpu_ms | 759/759 | 0.36607999999999996 | 0.43468799999999996 | available |
| VSM.InvalidateStatic_gpu_ms | 0/759 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 759/759 | 0.068352 | 0.07782399999999999 | available |
| VSM.StaticCasterCull_gpu_ms | 759/759 | 0.247808 | 0.673536 | available |
| VSM.DynamicCasterCull_gpu_ms | 759/759 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 759/759 | 0.22886399999999998 | 0.429568 | available |
| VSM.StaticRaster_gpu_ms | 759/759 | 0.0076799999999999993 | 0.009984 | available |
| VSM.DynamicRaster_gpu_ms | 759/759 | 0.0384 | 0.041215999999999996 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/759 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 759/759 | 0.004352 | 0.005888 | available |
| VSM.ResetFeedback_gpu_ms | 0/759 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 759/759 | 1.835008 | 2.4757759999999998 | available |

