# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length10_a — completed_with_warnings
Attempt: 0 | Observations: 253 | Active seconds: 4.0045145975056684 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: False | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 670/663/0/0
- Fresh CPU frame timing coverage is below 95%.
- Fresh whole-frame GPU coverage is below 95%; Frame Timing Stats enabled is not evidence of valid data.
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 253/253 | 14.769501052796841 | 16.341099515557289 | available |
| cpu_frame_ms | 0/253 |  |  | no_samples_or_not_executed |
| gpu_frame_ms | 0/253 |  |  | no_samples_or_not_executed |
| cpu_render_thread_ms | 0/253 |  |  | no_samples_or_not_executed |
| gc_allocated_in_editor_frame_bytes | 253/253 | 20689 | 103378 | available |
| VSM.LayoutRemap_gpu_ms | 253/253 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 253/253 | 7.085312 | 7.476736 | available |
| VSM.Allocate_gpu_ms | 253/253 | 0.527872 | 0.544512 | available |
| VSM.InvalidateStatic_gpu_ms | 8/253 | 0.13158399999999998 | 0.147456 | available |
| VSM.ClearPhysicalPages_gpu_ms | 253/253 | 0.12185599999999999 | 0.283648 | available |
| VSM.StaticCasterCull_gpu_ms | 253/253 | 0.361984 | 0.60288 | available |
| VSM.DynamicCasterCull_gpu_ms | 253/253 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 253/253 | 0.342784 | 0.526848 | available |
| VSM.StaticRaster_gpu_ms | 253/253 | 0.007168 | 0.011264 | available |
| VSM.DynamicRaster_gpu_ms | 253/253 | 0.086272 | 0.09344 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/253 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 253/253 | 0.004352 | 0.005632 | available |
| VSM.ResetFeedback_gpu_ms | 253/253 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 253/253 | 1.843456 | 2.5712639999999998 | available |

