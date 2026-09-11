# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## view1024005_a — completed_with_warnings
Attempt: 0 | Observations: 700 | Active seconds: 5.0020966978458006 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 635/623/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 700/700 | 6.7125000059604645 | 9.5031959936022758 | available |
| cpu_frame_ms | 700/700 | 6.7984500000000008 | 9.8083 | available |
| gpu_frame_ms | 699/700 | 6.419456 | 6.954496 | available |
| cpu_render_thread_ms | 700/700 | 3.0080999999999998 | 3.7655 | available |
| gc_allocated_in_editor_frame_bytes | 700/700 | 18203 | 21812 | available |
| VSM.LayoutRemap_gpu_ms | 700/700 | 0.001024 | 0.001024 | available |
| VSM.Allocate_gpu_ms | 700/700 | 0.507136 | 0.62105599999999994 | available |
| VSM.InvalidateStatic_gpu_ms | 0/700 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 700/700 | 0.122112 | 0.13516799999999998 | available |
| VSM.StaticCasterCull_gpu_ms | 700/700 | 0.357632 | 0.707584 | available |
| VSM.DynamicCasterCull_gpu_ms | 700/700 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 700/700 | 0.338432 | 0.535552 | available |
| VSM.StaticRaster_gpu_ms | 700/700 | 0.0076799999999999993 | 0.011007999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 700/700 | 0.09036799999999999 | 0.09548799999999999 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/700 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 700/700 | 0.002304 | 0.002816 | available |
| VSM.ResetFeedback_gpu_ms | 0/700 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 700/700 | 1.906432 | 2.275584 | available |

