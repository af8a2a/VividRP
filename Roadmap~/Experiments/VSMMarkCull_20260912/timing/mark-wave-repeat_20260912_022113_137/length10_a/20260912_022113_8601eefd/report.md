# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length10_a — completed_with_warnings
Attempt: 0 | Observations: 194 | Active seconds: 4.00926050170068 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: False | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 670/663/0/0
- Fresh CPU frame timing coverage is below 95%.
- Fresh whole-frame GPU coverage is below 95%; Frame Timing Stats enabled is not evidence of valid data.
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 194/194 | 10.133698116987944 | 47.032300382852554 | available |
| cpu_frame_ms | 0/194 |  |  | no_samples_or_not_executed |
| gpu_frame_ms | 0/194 |  |  | no_samples_or_not_executed |
| cpu_render_thread_ms | 0/194 |  |  | no_samples_or_not_executed |
| gc_allocated_in_editor_frame_bytes | 194/194 | 18307 | 1566589 | available |
| VSM.LayoutRemap_gpu_ms | 194/194 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 194/194 | 0.98969599999999991 | 1.331968 | available |
| VSM.Allocate_gpu_ms | 194/194 | 0.53312 | 0.63232 | available |
| VSM.InvalidateStatic_gpu_ms | 60/194 | 0.121984 | 0.140544 | available |
| VSM.ClearPhysicalPages_gpu_ms | 194/194 | 0.130048 | 0.32972799999999997 | available |
| VSM.StaticCasterCull_gpu_ms | 194/194 | 0.35775999999999997 | 0.592128 | available |
| VSM.DynamicCasterCull_gpu_ms | 194/194 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 194/194 | 0.336128 | 0.495104 | available |
| VSM.StaticRaster_gpu_ms | 194/194 | 0.007936 | 35.97184 | available |
| VSM.DynamicRaster_gpu_ms | 194/194 | 0.090239999999999987 | 0.388096 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/194 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 194/194 | 0.004352 | 0.011007999999999999 | available |
| VSM.ResetFeedback_gpu_ms | 194/194 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 194/194 | 1.89568 | 2.804224 | available |

