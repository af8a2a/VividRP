# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length10_a — completed_with_warnings
Attempt: 0 | Observations: 240 | Active seconds: 4.0052527990362812 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: False | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 670/659/0/0
- Fresh CPU frame timing coverage is below 95%.
- Fresh whole-frame GPU coverage is below 95%; Frame Timing Stats enabled is not evidence of valid data.
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 240/240 | 16.733797267079353 | 17.791999503970146 | available |
| cpu_frame_ms | 0/240 |  |  | no_samples_or_not_executed |
| gpu_frame_ms | 0/240 |  |  | no_samples_or_not_executed |
| cpu_render_thread_ms | 0/240 |  |  | no_samples_or_not_executed |
| gc_allocated_in_editor_frame_bytes | 240/240 | 20569 | 99796 | available |
| VSM.LayoutRemap_gpu_ms | 240/240 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 240/240 | 4.444672 | 4.874496 | available |
| VSM.Allocate_gpu_ms | 240/240 | 0.54323199999999994 | 0.56064 | available |
| VSM.InvalidateStatic_gpu_ms | 0/240 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 240/240 | 0.129024 | 0.327424 | available |
| VSM.StaticCasterCull_gpu_ms | 240/240 | 0.372992 | 0.62771199999999994 | available |
| VSM.DynamicCasterCull_gpu_ms | 240/240 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 240/240 | 0.353024 | 0.556288 | available |
| VSM.StaticRaster_gpu_ms | 240/240 | 0.007424 | 0.011519999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 240/240 | 0.086272 | 0.091648 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/240 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 240/240 | 0.004352 | 0.005376 | available |
| VSM.ResetFeedback_gpu_ms | 240/240 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 240/240 | 4.009472 | 4.456192 | available |

