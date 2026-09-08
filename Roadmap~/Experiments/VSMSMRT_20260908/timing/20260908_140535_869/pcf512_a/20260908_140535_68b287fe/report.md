# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## pcf512_a — completed_with_warnings
Attempt: 0 | Observations: 846 | Active seconds: 5.0047130952380954 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 425/419/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 846/846 | 5.9057504404336214 | 6.53129955753684 | available |
| cpu_frame_ms | 846/846 | 5.9169 | 6.799 | available |
| gpu_frame_ms | 844/846 | 5.871744 | 6.483456 | available |
| cpu_render_thread_ms | 846/846 | 1.9492500000000001 | 2.3726 | available |
| gc_allocated_in_editor_frame_bytes | 846/846 | 14539 | 20611 | available |
| VSM.LayoutRemap_gpu_ms | 846/846 | 0.001024 | 0.001024 | available |
| VSM.Allocate_gpu_ms | 846/846 | 0.347008 | 0.408576 | available |
| VSM.InvalidateStatic_gpu_ms | 0/846 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 846/846 | 0.062208 | 0.248832 | available |
| VSM.StaticCasterCull_gpu_ms | 846/846 | 0.24345599999999998 | 0.72064 | available |
| VSM.DynamicCasterCull_gpu_ms | 846/846 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 846/846 | 0.224768 | 0.565504 | available |
| VSM.StaticRaster_gpu_ms | 846/846 | 0.0076799999999999993 | 0.0097279999999999988 | available |
| VSM.DynamicRaster_gpu_ms | 846/846 | 0.038144 | 0.040959999999999996 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/846 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 846/846 | 0.004352 | 0.005376 | available |
| VSM.ResetFeedback_gpu_ms | 0/846 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 846/846 | 1.8727679999999998 | 2.518272 | available |

