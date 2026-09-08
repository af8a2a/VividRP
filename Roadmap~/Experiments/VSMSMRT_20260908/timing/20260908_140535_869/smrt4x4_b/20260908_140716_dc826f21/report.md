# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## smrt4x4_b — completed_with_warnings
Attempt: 0 | Observations: 763 | Active seconds: 5.002287400793648 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 439/435/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 763/763 | 6.551296915858984 | 7.0791030302643776 | available |
| cpu_frame_ms | 763/763 | 6.5617 | 7.3836 | available |
| gpu_frame_ms | 762/763 | 6.505472 | 7.0464 | available |
| cpu_render_thread_ms | 763/763 | 2.0377 | 2.504 | available |
| gc_allocated_in_editor_frame_bytes | 763/763 | 14539 | 28924 | available |
| VSM.LayoutRemap_gpu_ms | 763/763 | 0.001024 | 0.0012799999999999999 | available |
| VSM.Allocate_gpu_ms | 763/763 | 0.388096 | 0.48230399999999995 | available |
| VSM.InvalidateStatic_gpu_ms | 0/763 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 763/763 | 0.068096 | 0.074495999999999993 | available |
| VSM.StaticCasterCull_gpu_ms | 763/763 | 0.246272 | 0.62847999999999993 | available |
| VSM.DynamicCasterCull_gpu_ms | 763/763 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 763/763 | 0.227328 | 0.514304 | available |
| VSM.StaticRaster_gpu_ms | 763/763 | 0.0076799999999999993 | 0.009984 | available |
| VSM.DynamicRaster_gpu_ms | 763/763 | 0.0384 | 0.040959999999999996 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/763 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 763/763 | 0.004608 | 0.005632 | available |
| VSM.ResetFeedback_gpu_ms | 0/763 |  |  | no_samples_or_not_executed |
| VSM.ResolveAndFeedback_gpu_ms | 763/763 | 2.358016 | 2.821888 | available |

