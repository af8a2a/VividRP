# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length10_a — completed_with_warnings
Attempt: 0 | Observations: 321 | Active seconds: 4.0010294997165552 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 670/659/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 321/321 | 12.483297847211361 | 13.466496951878071 | available |
| cpu_frame_ms | 321/321 | 12.4868 | 13.787 | available |
| gpu_frame_ms | 321/321 | 12.471552 | 13.4464 | available |
| cpu_render_thread_ms | 321/321 | 2.6526 | 3.2778 | available |
| gc_allocated_in_editor_frame_bytes | 321/321 | 14419 | 97242 | available |
| VSM.LayoutRemap_gpu_ms | 321/321 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 321/321 | 3.867904 | 4.3448319999999994 | available |
| VSM.Allocate_gpu_ms | 321/321 | 0.54016 | 0.563456 | available |
| VSM.InvalidateStatic_gpu_ms | 0/321 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 321/321 | 0.130304 | 0.29209599999999997 | available |
| VSM.StaticCasterCull_gpu_ms | 321/321 | 0.352512 | 0.630016 | available |
| VSM.DynamicCasterCull_gpu_ms | 321/321 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 321/321 | 0.33152 | 0.53504 | available |
| VSM.StaticRaster_gpu_ms | 321/321 | 0.007424 | 0.011264 | available |
| VSM.DynamicRaster_gpu_ms | 321/321 | 0.087039999999999992 | 0.10751999999999999 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/321 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 321/321 | 0.003328 | 0.004352 | available |
| VSM.ResetFeedback_gpu_ms | 321/321 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 321/321 | 2.411264 | 2.865408 | available |

