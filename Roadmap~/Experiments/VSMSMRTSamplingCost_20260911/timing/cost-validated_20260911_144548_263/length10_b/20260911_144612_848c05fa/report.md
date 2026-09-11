# VSM baseline report

Status: completed_with_warnings | Reason: completed
Cases processed: 1/1 | Sampling goals met: 1 | Timing windows comparable: 0
Frame timing source: editor_frames_unattributed | Quality: not_validated

Whole-frame CPU/GPU, GC and GPU stage totals include Editor and all cameras. FrameTiming/GPU results are delayed and not synchronized to the observation frame. Missing/skipped data is unavailable, not zero. PageCull is nested in caster culling; do not sum stage quantiles.

Comparable is a timing check, not a P5 quality certification. Missing metrics remain blank.

## length10_b — completed_with_warnings
Attempt: 0 | Observations: 336 | Active seconds: 4.0063207979024966 | Segments: 1
Reason: 
Recorder repaint: Manual | Observed repaints: 0 | GPU <1 ms (retained): 0 | Median interval (s, 0=unavailable): 0
Timing coverage passed: True | Source: editor_frames_unattributed | Quality: not_validated
Residency: no_overflow_at_last_frame | Last-frame resident/requested/new/overflow: 672/661/0/0
- Whole-frame timing source is not attributed to the selected camera; coverage alone cannot certify comparability.

| Metric | Valid / observations | Median | P95 | Availability |
| --- | --- | --- | --- | --- |
| frame_interval_ms | 336/336 | 11.981253512203693 | 12.824900448322296 | available |
| cpu_frame_ms | 336/336 | 11.94975 | 13.4181 | available |
| gpu_frame_ms | 336/336 | 11.954304 | 12.794112 | available |
| cpu_render_thread_ms | 336/336 | 2.5071000000000003 | 3.4185 | available |
| gc_allocated_in_editor_frame_bytes | 336/336 | 14419 | 91187 | available |
| VSM.LayoutRemap_gpu_ms | 336/336 | 0 | 0 | available |
| VSM.MarkReceiverPages_gpu_ms | 336/336 | 3.978624 | 4.398848 | available |
| VSM.Allocate_gpu_ms | 336/336 | 0.53888 | 0.55808 | available |
| VSM.InvalidateStatic_gpu_ms | 0/336 |  |  | no_samples_or_not_executed |
| VSM.ClearPhysicalPages_gpu_ms | 336/336 | 0.13055999999999998 | 0.133376 | available |
| VSM.StaticCasterCull_gpu_ms | 336/336 | 0.355456 | 0.660992 | available |
| VSM.DynamicCasterCull_gpu_ms | 336/336 | 0 | 0.000256 | available |
| VSM.PageCull_gpu_ms | 336/336 | 0.33433599999999997 | 0.609792 | available |
| VSM.StaticRaster_gpu_ms | 336/336 | 0.007424 | 0.011007999999999999 | available |
| VSM.DynamicRaster_gpu_ms | 336/336 | 0.087935999999999986 | 0.093952 | available |
| VSM.UnityCompatibilityRaster_gpu_ms | 0/336 |  |  | no_samples_or_not_executed |
| VSM.FinalizePages_gpu_ms | 336/336 | 0.003328 | 0.004352 | available |
| VSM.ResetFeedback_gpu_ms | 336/336 | 0 | 0 | available |
| VSM.Resolve_gpu_ms | 336/336 | 2.411264 | 2.8638719999999998 | available |

