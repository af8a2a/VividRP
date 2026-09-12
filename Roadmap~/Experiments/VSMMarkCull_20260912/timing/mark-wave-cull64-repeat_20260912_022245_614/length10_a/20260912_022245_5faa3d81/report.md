# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length10_a — completed_with_warnings
Attempt: 0 | Observations: 199 | Active seconds: 4.0022778982426317 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: False | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 670/659/0/0
- Fresh CPU frame timing coverage is below 95%.
- Fresh whole-frame GPU coverage is below 95%; Frame Timing Stats enabled is not evidence of valid data.
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 199/199 | 10.074100457131863 | 47.2945012152195 | available |
| cpu_frame_ms | 50/199 | 9.6308 | 11.4342 | available |
| gpu_frame_ms | 50/199 | 9.6704 | 10.76992 | available |
| cpu_render_thread_ms | 50/199 | 2.2607999999999997 | 2.8595 | available |
| gc_allocated_in_editor_frame_bytes | 199/199 | 18307 | 1534632 | available |
| VSM.LayoutRemap_gpu_ms | 199/199 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 199/199 | 0.95206399999999991 | 1.3457919999999999 | available |
| VSM.Allocate_gpu_ms | 199/199 | 0.530944 | 0.635648 | available |
| VSM.InvalidateStatic_gpu_ms | 59/199 | 0.121344 | 0.139264 | available |
| VSM.ClearPhysicalPages_gpu_ms | 199/199 | 0.130048 | 0.135936 | available |
| VSM.StaticCasterCull_gpu_ms | 199/199 | 0.196352 | 0.495616 | available |
| VSM.DynamicCasterCull_gpu_ms | 199/199 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 199/199 | 0.174592 | 0.1856 | available |
| VSM.StaticRaster_gpu_ms | 199/199 | 0.0076799999999999993 | 36.46208 | available |
| VSM.DynamicRaster_gpu_ms | 199/199 | 0.09088 | 0.395264 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/199 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 199/199 | 0.0038399999999999997 | 0.010752 | available |
| VSM.ResetFeedback_gpu_ms | 199/199 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 199/199 | 1.9563519999999999 | 2.695168 | available |

