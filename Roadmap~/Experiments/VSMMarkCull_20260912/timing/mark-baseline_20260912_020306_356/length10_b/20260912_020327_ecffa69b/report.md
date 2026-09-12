# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length10_b — completed_with_warnings
Attempt: 0 | Observations: 313 | Active seconds: 4.0026811011904755 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/665/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 313/313 | 12.761401943862438 | 13.442105613648891 | available |
| cpu_frame_ms | 313/313 | 12.7551 | 13.7522 | available |
| gpu_frame_ms | 313/313 | 12.743168 | 13.377536 | available |
| cpu_render_thread_ms | 313/313 | 2.0291 | 2.5972 | available |
| gc_allocated_in_editor_frame_bytes | 313/313 | 18307 | 18728 | available |
| VSM.LayoutRemap_gpu_ms | 313/313 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 313/313 | 2.921728 | 3.249152 | available |
| VSM.Allocate_gpu_ms | 313/313 | 0.54067199999999993 | 0.56243199999999993 | available |
| VSM.InvalidateStatic_gpu_ms | 0/313 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 313/313 | 0.132352 | 0.31513599999999997 | available |
| VSM.StaticCasterCull_gpu_ms | 313/313 | 0.367104 | 0.695552 | available |
| VSM.DynamicCasterCull_gpu_ms | 313/313 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 313/313 | 0.346112 | 0.556288 | available |
| VSM.StaticRaster_gpu_ms | 313/313 | 0.007168 | 0.010239999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 313/313 | 0.087808 | 0.094208 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/313 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 313/313 | 0.0025599999999999998 | 0.003072 | available |
| VSM.ResetFeedback_gpu_ms | 313/313 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 313/313 | 2.587392 | 3.001344 | available |

