# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## view768005_b — completed_with_warnings
Attempt: 0 | Observations: 771 | Active seconds: 5.0065196995464873 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 635/624/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 771/771 | 6.370004266500473 | 7.3785004206001759 | available |
| cpu_frame_ms | 771/771 | 6.3553 | 7.9943 | available |
| gpu_frame_ms | 770/771 | 6.193408 | 6.958592 | available |
| cpu_render_thread_ms | 771/771 | 2.5071 | 3.4567 | available |
| gc_allocated_in_editor_frame_bytes | 771/771 | 18203 | 23987 | available |
| VSM.LayoutRemap_gpu_ms | 771/771 | 0.001024 | 0.0012799999999999999 | available |
| VSM.Allocate_gpu_ms | 771/771 | 0.482048 | 0.594432 | available |
| VSM.InvalidateStatic_gpu_ms | 0/771 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 771/771 | 0.096 | 0.255488 | available |
| VSM.StaticCasterCull_gpu_ms | 771/771 | 0.32179199999999997 | 0.69964799999999994 | available |
| VSM.DynamicCasterCull_gpu_ms | 771/771 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 771/771 | 0.302336 | 0.56166399999999994 | available |
| VSM.StaticRaster_gpu_ms | 771/771 | 0.0063999999999999994 | 0.011007999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 771/771 | 0.064512 | 0.06912 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/771 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 771/771 | 0.002304 | 0.002816 | available |
| VSM.ResetFeedback_gpu_ms | 0/771 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 771/771 | 1.727744 | 2.242048 | available |

